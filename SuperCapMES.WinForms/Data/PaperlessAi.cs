using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

/// <summary>
/// 点检表「AI 语义复核」：让大模型判断表的语义结构（哪列要填、什么类型），
/// 规则解析器的结果作为基线，两者比对后给人三色确认。
///
/// 三条不可动摇的边界（与方案文档一致）：
/// ① 模型只做"理解与标注"，**不落库**——结果只进确认页，人工点确认才改模板；
/// ② 模型给的文本必须能在原文找到（可溯），否则一律标红不采纳；
/// ③ 完整性不靠模型自证——用规则列做独立第二意见，差集显式呈现，禁止静默合并。
/// </summary>
public static class PaperlessAi
{
    /// <summary>提示词版本：换了提示词就要改，否则缓存与审计对不上</summary>
    public const string PromptVersion = "paperless-sheet-v1";

    /// <summary>
    /// 建议类任务的置信度门槛，比默认 0.60 低。
    /// 理由：这里产出的是"建议"不是"判定"——后面还有四道校验 + 人工逐项确认才落地；
    /// 单列置信度偏低只该标黄让人多看一眼，不该因此把整张表的建议全丢掉。
    /// </summary>
    public const double SuggestMinConfidence = 0.35;

    /// <summary>单列进入"待确认（黄）"的置信度线</summary>
    public const double ItemWarnConfidence = 0.60;

    private static string CacheDir => Path.Combine(PaperlessStore.DataRoot, "ai-cache");

    // ---------------- 系统提示（强约束） ----------------

    public const string SystemPrompt =
@"你是「点检表结构解析器」。用户给出一张从 Excel/Word 提取的表格，每行形如：
R3 | C1=序号 | C2=点检项目 | C3=点检内容及标准
其中 R/C 后是原文件的行号与列号（均从 1 开始）。首行若为 MERGED=... 则是合并单元格区域。

你的唯一任务是判断「表的结构」，不要输出任何数据行内容：

铁律（违反即作废）：
1. header / options / footerNotes 的文本必须逐字取自输入中的单元格原文，
   不得改写、不得翻译、不得补全、不得概括、不得合并多格。
2. 每一项都必须给 sourceRef，格式为 R{行}C{列}（1 起），指向你据以判断的那个单元格。
3. 拿不准就把 confidence 写到 0.5 以下，不要猜。宁可少报让人复核，也不要编。

kind 只能取下列之一：
Context（只读上下文：序号/项目/内容/标准/方法/周期/基准等，不是给人填的）
Text / Multiline / Number / Date / Select / Checkbox / Signature
（Select 必须给 options；Signature 指签字/确认人一类；Multiline 指备注/异常描述一类）

只输出一个 JSON 对象，前后不要任何解释文字：
{""title"":""..."",""headerRow"":3,
""columns"":[{""header"":""..."",""kind"":""Select"",""fillable"":true,""required"":true,
""options"":[""OK"",""NG""],""sourceRef"":""R3C7"",""confidence"":0.95,""evidence"":""列名含结果判定""}],
""headerFields"":[{""header"":""车间/线别：____"",""kind"":""Text"",""fillable"":true,""sourceRef"":""R2C1"",""confidence"":0.9}],
""footerNotes"":[""...""]}";

    // ---------------- 输入侧：带坐标的紧凑文本 ----------------

    /// <summary>
    /// 把原始表压成"带坐标的紧凑文本"：空单元格不输出（省 token），
    /// 每个值都带 R/C 坐标（这是模型给 sourceRef 的唯一依据）。
    /// </summary>
    public static string BuildCompactText(RawSheet raw, int maxRows = 200)
    {
        var sb = new StringBuilder();
        sb.Append("SHEET=").Append(raw.SheetName.Length > 0 ? raw.SheetName : "(未命名)")
          .Append("  ROWS=").Append(raw.RowCount)
          .Append("  COLS=").Append(raw.ColCount).AppendLine();

        if (raw.Merges.Count > 0)
        {
            sb.Append("MERGED=")
              .Append(string.Join(" ", raw.Merges.Select(m => $"R{m.R1 + 1}C{m.C1 + 1}:R{m.R2 + 1}C{m.C2 + 1}")))
              .AppendLine();
        }

        int rows = Math.Min(raw.RowCount, maxRows);
        for (int r = 0; r < rows; r++)
        {
            var cells = new List<string>();
            for (int c = 0; c < raw.Grid[r].Count; c++)
            {
                var v = raw.Grid[r][c];
                if (string.IsNullOrWhiteSpace(v)) continue;
                cells.Add($"C{c + 1}={OneLine(v)}");
            }
            if (cells.Count == 0) continue;
            sb.Append('R').Append(r + 1).Append(" | ").Append(string.Join(" | ", cells)).AppendLine();
        }
        if (raw.RowCount > rows)
            sb.Append("(表格过大，仅输出前 ").Append(rows).Append(" 行，其余未提供)").AppendLine();
        return sb.ToString();
    }

    // ---------------- 主入口 ----------------

    /// <summary>
    /// 跑一次 AI 语义复核。任何失败都返回 Ok=false + Reason，**不抛异常**。
    /// </summary>
    /// <param name="settings">指定配置（自测注入用）；null 则读用户当前配置</param>
    public static AiAnalysisResult Analyze(
        string filePath,
        ChecklistTemplate ruleTpl,
        HttpClient? http = null,
        AssistantLlmSettings? settings = null,
        bool ignoreCache = false,
        int sheetIndex = 0)
    {
        var raw = PaperlessImport.LoadRaw(filePath, sheetIndex);
        if (raw is null)
            return new AiAnalysisResult { Ok = false, Reason = "读不到原始表结构（文件损坏或格式不支持），已保留规则结果" };

        var cfg = settings ?? LlmService.CurrentSettings;

        // ① 缓存优先：同文件 + 同 sheet + 同模型 + 同提示词版本 → 直接命中，零调用
        if (!ignoreCache && TryLoadCache(filePath, cfg.Model, sheetIndex, out var cached) && cached is not null)
        {
            var rc = Compare(ruleTpl, cached, raw);
            rc.Ok = true;
            rc.FromCache = true;
            rc.TraceText = $"来源：本地缓存（{cached.Model} · {cached.PromptVersion} · {cached.At:MM-dd HH:mm}），本次未调用模型";
            return rc;
        }

        if (!cfg.CanCall)
            return new AiAnalysisResult { Ok = false, Reason = "云端大模型未启用或未配置 API Key，已保留规则结果", Raw = raw };

        var payload = new
        {
            sheet = raw.SheetName,
            rows = raw.RowCount,
            cols = raw.ColCount,
            table = BuildCompactText(raw),
        };

        // 走既有护栏链路：JSON 容错提取 → 业务校验 → 置信度门槛 → 失败降级
        var call = settings is null
            ? LlmService.CallJson<AiSheetClaim>(SystemPrompt, payload, root => ParseClaim(root, cfg.Model), SuggestMinConfidence, null, http, 2000)
            : LlmService.CallJson<AiSheetClaim>(cfg, SystemPrompt, payload, root => ParseClaim(root, cfg.Model), SuggestMinConfidence, null, http, 2000);

        if (!call.Ok || call.Value is null)
            return new AiAnalysisResult
            {
                Ok = false,
                Reason = (call.Reason.Length > 0 ? call.Reason : "模型结果不可用") + "，已保留规则结果",
                Raw = raw,
            };

        var claim = call.Value;
        claim.Model = cfg.Model;
        claim.PromptVersion = PromptVersion;
        claim.At = DateTime.Now;
        SaveCache(filePath, claim, sheetIndex);

        var res = Compare(ruleTpl, claim, raw);
        res.Ok = true;
        res.TraceText = call.TraceText;
        return res;
    }

    // ---------------- 模型输出解析 ----------------

    private static (AiSheetClaim?, double, string) ParseClaim(JsonElement root, string model)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, 0, "模型输出不是 JSON 对象");

        var claim = new AiSheetClaim { Model = model };

        if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
            claim.Title = (t.GetString() ?? "").Trim();
        if (root.TryGetProperty("headerRow", out var hr) && hr.ValueKind == JsonValueKind.Number)
            claim.HeaderRow = hr.GetInt32();

        if (!root.TryGetProperty("columns", out var cols) || cols.ValueKind != JsonValueKind.Array)
            return (null, 0, "模型输出缺少 columns 数组");

        foreach (var e in cols.EnumerateArray())
        {
            var f = ReadField(e);
            if (f.Header.Length == 0) continue;     // 空列名直接丢弃
            claim.Columns.Add(f);
        }
        if (claim.Columns.Count == 0) return (null, 0, "模型没有给出任何列的判断");

        if (root.TryGetProperty("headerFields", out var hfs) && hfs.ValueKind == JsonValueKind.Array)
            foreach (var e in hfs.EnumerateArray())
            {
                var f = ReadField(e);
                if (f.Header.Length > 0) claim.HeaderFields.Add(f);
            }

        if (root.TryGetProperty("footerNotes", out var fns) && fns.ValueKind == JsonValueKind.Array)
            foreach (var e in fns.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                {
                    var s = (e.GetString() ?? "").Trim();
                    if (s.Length > 0) claim.FooterNotes.Add(s);
                }

        // 整体置信度取各列最小值（最弱一环）；低于门槛由 LlmService 统一拦掉并降级
        return (claim, claim.Columns.Min(x => x.Confidence), "");
    }

    private static AiFieldClaim ReadField(JsonElement e)
    {
        var f = new AiFieldClaim
        {
            Header = Str(e, "header"),
            Kind = ParseKind(Str(e, "kind")),
            SourceRef = Str(e, "sourceRef").ToUpperInvariant().Replace(" ", ""),
            Evidence = Str(e, "evidence"),
            Confidence = Num(e, "confidence", 0.5),
        };
        f.Fillable = Bool(e, "fillable", f.Kind != FieldKind.Context);
        f.Required = Bool(e, "required", false);
        if (e.TryGetProperty("options", out var ops) && ops.ValueKind == JsonValueKind.Array)
            foreach (var o in ops.EnumerateArray())
                if (o.ValueKind == JsonValueKind.String)
                {
                    var s = (o.GetString() ?? "").Trim();
                    if (s.Length > 0) f.Options.Add(s);
                }
        return f;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static double Num(JsonElement e, string name, double dflt)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : dflt;

    private static bool Bool(JsonElement e, string name, bool dflt)
        => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : dflt;

    private static FieldKind ParseKind(string s)
        => Enum.TryParse<FieldKind>(s, true, out var k) ? k : FieldKind.Text;

    // ---------------- 四道校验 + 三色判定 ----------------

    private static AiAnalysisResult Compare(ChecklistTemplate ruleTpl, AiSheetClaim claim, RawSheet raw)
    {
        var res = new AiAnalysisResult { Raw = raw, Claim = claim };

        // ① 原文可溯：把原文所有非空单元格规范化成集合
        var corpus = raw.Grid.SelectMany(r => r)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(Norm).ToHashSet(StringComparer.Ordinal);

        var used = new HashSet<AiFieldClaim>();
        for (int c = 0; c < ruleTpl.Columns.Count; c++)
        {
            var rc = ruleTpl.Columns[c];
            var ai = claim.Columns.FirstOrDefault(a => !used.Contains(a) && Norm(a.Header) == Norm(rc.Header));
            var row = new AiReviewRow
            {
                RuleCol = c,
                RuleHeader = rc.Header,
                RuleKind = rc.Kind,
                RuleRequired = rc.Required,
                AiHeader = ai?.Header ?? "",
                AiKind = ai?.Kind ?? rc.Kind,
                AiRequired = ai?.Required ?? rc.Required,
                AiOptions = ai?.Options ?? new List<string>(),
                Confidence = ai?.Confidence ?? 0,
                SourceRef = ai?.SourceRef ?? "",
            };

            if (ai is null)
            {
                row.State = AiMatchState.Conflict;
                row.Reason = "模型未给出该列（疑似漏项，按规则保留）";
                row.TakeAi = false;
            }
            else
            {
                used.Add(ai);
                var (rr, cc) = row.ResolveRef();
                if (!Traceable(ai.Header, corpus))
                {
                    row.State = AiMatchState.Hallucination;
                    row.Reason = "模型给的列名在原文中找不到";
                }
                else if (rr < 0 || rr >= raw.RowCount || cc < 0 || cc >= raw.ColCount)
                {
                    row.State = AiMatchState.Hallucination;
                    row.Reason = "模型给的定位坐标越界" + (row.SourceRef.Length == 0 ? "（未给 sourceRef）" : $"（{row.SourceRef}）");
                }
                else if (!Compatible(Norm(raw.Cell(rr, cc)), Norm(rc.Header)))
                {
                    row.State = AiMatchState.Conflict;
                    row.Reason = $"定位格 {row.SourceRef} 是「{OneLine(raw.Cell(rr, cc), 12)}」，与列名不符";
                }
                else if (ai.Kind != rc.Kind)
                {
                    row.State = AiMatchState.Conflict;
                    row.Reason = $"类型不一致：{FieldKindText.Of(rc.Kind)}→{FieldKindText.Of(ai.Kind)}";
                    row.TakeAi = true;      // 类型冲突正是要用模型的地方，默认勾上让人拍一下
                }
                else if (ai.Confidence < ItemWarnConfidence)
                {
                    row.State = AiMatchState.Conflict;
                    row.Reason = $"模型置信度偏低（{ai.Confidence:P0}）";
                }
                else if (ai.Kind == FieldKind.Select && ai.Options.Count == 0)
                {
                    row.State = AiMatchState.Conflict;
                    row.Reason = "下拉列没给候选项";
                }
                else
                {
                    row.State = AiMatchState.Agree;
                    row.TakeAi = true;
                }
            }
            res.Rows.Add(row);
        }

        // 模型多出来的列（规则里没有）——不自动加列，只提示。
        // 这里正是"编造列"最容易出现的地方：原文里根本没有这个名字 → 判红；原文有 → 判黄（可能规则漏了）
        foreach (var ai in claim.Columns.Where(a => !used.Contains(a)))
        {
            if (res.Rows.Any(r => Norm(r.AiHeader) == Norm(ai.Header))) continue;
            var invented = !Traceable(ai.Header, corpus);
            res.Rows.Add(new AiReviewRow
            {
                RuleCol = -1,
                AiHeader = ai.Header,
                AiKind = ai.Kind,
                Confidence = ai.Confidence,
                SourceRef = ai.SourceRef,
                State = invented ? AiMatchState.Hallucination : AiMatchState.Conflict,
                Reason = invented
                    ? "原文中找不到这一列名（模型编造）"
                    : "规则里没有这一列（可能规则漏了，需人工判断；不会自动加列）",
                TakeAi = false,
            });
        }

        // ② 完整性双向核对：差集必须在提示里显式出现
        res.Notes.Add($"完整性核对：规则解析 {ruleTpl.Columns.Count} 列 / 模型识别 {claim.Columns.Count} 列");
        var missing = res.Rows.Where(r => r.RuleCol >= 0 && r.AiHeader.Length == 0).Select(r => r.RuleHeader).ToList();
        if (missing.Count > 0) res.Notes.Add("模型漏项：" + string.Join("、", missing));
        var extra = res.Rows.Where(r => r.RuleCol < 0).Select(r => r.AiHeader).ToList();
        if (extra.Count > 0) res.Notes.Add("模型多出：" + string.Join("、", extra) + "（未自动加列）");
        if (claim.HeaderRow > 0) res.Notes.Add($"模型判断表头在第 {claim.HeaderRow} 行");

        // ③ 抬头字段对照（只报告差异，默认不替换）
        //    —— 模型只认出 1 项、规则认出 4 项时，若自动替换就把 3 项丢了。
        //    替换必须由人在确认页显式勾选（ApplyHeaderFields）。
        var ruleHdr = ruleTpl.HeaderFields.Select(h => Norm(h.Label)).ToList();
        var aiHdr = claim.HeaderFields.Select(f => Norm(HeaderLabel(f.Header))).ToList();
        if (aiHdr.Count > 0 || ruleHdr.Count > 0)
        {
            var diff = aiHdr.Except(ruleHdr).Concat(ruleHdr.Except(aiHdr)).Where(x => x.Length > 0).Distinct().ToList();
            res.Notes.Add(diff.Count == 0
                ? $"抬头字段：规则 {ruleHdr.Count} 项与模型 {aiHdr.Count} 项一致"
                : $"抬头字段差异：{string.Join("、", diff)}（默认不替换，需在下方勾选才会按模型结果覆盖）");
        }
        res.ApplyHeaderFields = false;

        // ④ 表尾说明对照（只提示，不改模板）
        if (claim.FooterNotes.Count > 0)
            res.Notes.Add($"表尾说明：模型识别 {claim.FooterNotes.Count} 条 / 规则解析 {ruleTpl.FooterNotes.Count} 条");

        return res;
    }

    /// <summary>应用人工确认过的 AI 判断，返回新模板（规则模板不被就地修改）。</summary>
    public static ChecklistTemplate ApplyTo(ChecklistTemplate ruleTpl, AiAnalysisResult result)
    {
        var tpl = ruleTpl.Clone();
        foreach (var row in result.Rows)
        {
            if (!row.TakeAi || row.RuleCol < 0 || row.RuleCol >= tpl.Columns.Count) continue;
            var col = tpl.Columns[row.RuleCol];
            col.Kind = row.AiKind;
            col.Required = row.AiRequired;
            if (row.AiOptions.Count > 0) col.Options = row.AiOptions.ToList();
        }

        if (result.ApplyHeaderFields && result.Claim is not null && result.Claim.HeaderFields.Count > 0)
        {
            tpl.HeaderFields = result.Claim.HeaderFields.Select(f =>
            {
                var (label, hint) = SplitLabel(f.Header);
                return new ChecklistHeaderField { Label = label, Hint = hint };
            }).Where(h => h.Label.Length > 0).ToList();
        }

        tpl.Version++;
        return tpl;
    }

    // ---------------- 缓存 ----------------

    private static string CachePath(string filePath, string model, int sheetIndex)
    {
        string hash;
        using (var stream = File.OpenRead(filePath))
            hash = Convert.ToHexString(SHA256.HashData(stream))[..16];
        var m = new string(model.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_').ToArray());
        // sheetIndex 必须进 key：同一个文件的不同 sheet 结构完全不同，混用缓存会张冠李戴
        return Path.Combine(CacheDir, $"{hash}-s{sheetIndex}-{m}-{PromptVersion}.json");
    }

    private static bool TryLoadCache(string filePath, string model, int sheetIndex, out AiSheetClaim? claim)
    {
        claim = null;
        try
        {
            var p = CachePath(filePath, model, sheetIndex);
            if (!File.Exists(p)) return false;
            claim = JsonSerializer.Deserialize<AiSheetClaim>(File.ReadAllText(p, Encoding.UTF8));
            return claim is not null;
        }
        catch { return false; }
    }

    private static void SaveCache(string filePath, AiSheetClaim claim, int sheetIndex)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(
                CachePath(filePath, claim.Model, sheetIndex),
                JsonSerializer.Serialize(claim, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }),
                new UTF8Encoding(false));
        }
        catch { /* 缓存失败不影响主流程 */ }
    }

    /// <summary>清空 AI 解析缓存（换模型/改提示词后重跑用）。</summary>
    public static int ClearCache()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return 0;
            var files = Directory.GetFiles(CacheDir, "*.json");
            foreach (var f in files) File.Delete(f);
            return files.Length;
        }
        catch { return 0; }
    }

    // ---------------- 文本比对工具 ----------------

    /// <summary>
    /// 规范化：去换行/所有空白、全角标点转半角、大小写归一。
    /// 目的是让「点检结果\n(OK/NG)」与「点检结果（OK/NG）」能对上，
    /// 避免因排版差异把模型正确的输出误判成幻觉。
    /// </summary>
    private static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(ch switch
            {
                '（' => '(',
                '）' => ')',
                '：' => ':',
                '，' => ',',
                '、' => ',',
                '；' => ';',
                '．' => '.',
                '　' => ' ',
                _ => ch,
            });
        }
        return sb.ToString().Trim().ToLowerInvariant();
    }

    /// <summary>原文可溯：模型给的文本必须在原文单元格里出现过（等值或互为子串）。</summary>
    private static bool Traceable(string header, HashSet<string> corpus)
    {
        var h = Norm(header);
        if (h.Length == 0) return false;
        if (corpus.Contains(h)) return true;
        foreach (var c in corpus)
        {
            if (c.Length >= 2 && h.Contains(c, StringComparison.Ordinal)) return true;
            if (h.Length >= 2 && c.Contains(h, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>定位格内容与列名是否对得上（互相包含即可，允许表头单元格带单位/换行）。</summary>
    private static bool Compatible(string cellNorm, string headerNorm)
    {
        if (cellNorm.Length == 0 || headerNorm.Length == 0) return false;
        return cellNorm == headerNorm
            || cellNorm.Contains(headerNorm, StringComparison.Ordinal)
            || headerNorm.Contains(cellNorm, StringComparison.Ordinal);
    }

    private static string OneLine(string? s, int max = 200)
    {
        var t = (s ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static (string Label, string Hint) SplitLabel(string raw)
    {
        var s = (raw ?? "").Trim();
        int i = s.IndexOf('：');
        if (i < 0) i = s.IndexOf(':');
        if (i <= 0) return (s, "");
        return (s[..i].Trim(), s[(i + 1)..].Trim());
    }

    /// <summary>抬头字段的"标签部分"（冒号前），用于比对时忽略提示位的下划线差异。</summary>
    private static string HeaderLabel(string raw) => SplitLabel(raw).Label;
}
