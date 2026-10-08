using System.Text;
using System.Text.Json;
using SuperCapMES.WinForms.Data;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 四类大模型任务的实现
//
// 每一类都写清楚三件事：**触发时机 / 输入数据结构 / 输出格式 + 业务如何校验消费**。
//
// 分工铁律（贯穿四类）：
//   · 模型负责"读懂文本"和"组织语言"
//   · 规则负责"判定"（够不够、撞不撞、可不可行）
//   · 模型给的任何数字都要过数字核对；判定类结论一律以规则为准
// ============================================================================

/// <summary>① 工单文本解析结果</summary>
public class WorkOrderAiParse
{
    public string ProductModel { get; set; } = "";
    public int PlanQty { get; set; }
    public string Line { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }

    /// <summary>从文本里识别到的设备/工装名称（供后续做资产匹配）</summary>
    public List<string> EquipmentNames { get; set; } = new();

    /// <summary>置信度（模型自评 0~1）</summary>
    public double Confidence { get; set; }

    /// <summary>缺失的必填字段</summary>
    public List<string> MissingFields { get; set; } = new();

    /// <summary>业务校验发现的问题（型号不存在、数量越界、日期颠倒等）</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>② 资产识别与匹配：单条匹配</summary>
public class AssetMatchItem
{
    public string Process { get; set; } = "";
    public string AssetCode { get; set; } = "";
    public string AssetName { get; set; } = "";
    public double Score { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>② 资产识别与匹配：整体结果</summary>
public class AssetMatchResult
{
    public List<AssetMatchItem> Matches { get; set; } = new();
    public double Confidence { get; set; } = 1.0;

    /// <summary>被业务校验丢弃的匹配（资产编码不存在等），界面上要能看到"丢了几条、为什么"</summary>
    public List<string> Rejected { get; set; } = new();
}

/// <summary>③ 库存与占用判断的归因说明（纯措辞，不含新数字）</summary>
public class ResourceExplainResult
{
    public string Conclusion { get; set; } = "";
    public List<string> Reasons { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public double Confidence { get; set; } = 1.0;
}

/// <summary>④ 方案与排产建议（纯措辞，不含新数字）</summary>
public class ScheduleAdviseResult
{
    public string Conclusion { get; set; } = "";
    public List<string> Actions { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public double Confidence { get; set; } = 1.0;
}

/// <summary>四类大模型任务的调用入口</summary>
public static class AiTasks
{
    // ================================================================
    // ① 工单文本解析
    //   触发：工单窗体点「AI 解析文本」或粘贴后失焦
    //   输入：{ "text": 原始文本, "knownModels": [...], "today": "yyyy-MM-dd" }
    //   输出：{ productModel, planQty, line, startDate, dueDate, equipmentNames[],
    //           confidence, missingFields[] }
    //   校验：型号必须在 BOM 主数据内；数量须 >0 且在上限内；startDate ≤ dueDate；
    //         缺必填项 → 降级为规则抽取并标注
    // ================================================================
    public static LlmCallResult<WorkOrderAiParse> ParseWorkOrderText(string text, System.Net.Http.HttpClient? http = null)
    {
        var known = MesDatabase.GetProductModels();
        var today = DateTime.Today;

        if (string.IsNullOrWhiteSpace(text))
            return RuleFallbackParseWorkOrder("", known, today, "输入文本为空");

        var payload = new
        {
            text,
            knownModels = known,
            today = today.ToString("yyyy-MM-dd"),
            lines = new[] { "L1", "L2", "L3" }
        };

        var r = LlmService.CallJson<WorkOrderAiParse>(SysParse(), payload, root =>
        {
            var r = new WorkOrderAiParse
            {
                ProductModel = LlmService.Str(root, "productModel"),
                PlanQty = LlmService.Int(root, "planQty"),
                Line = LlmService.Str(root, "line"),
                EquipmentNames = LlmService.StrList(root, "equipmentNames"),
                Confidence = LlmService.Dbl(root, "confidence", 0.5),
                MissingFields = LlmService.StrList(root, "missingFields")
            };

            if (DateTime.TryParse(LlmService.Str(root, "startDate"), out var sd)) r.StartDate = sd;
            if (DateTime.TryParse(LlmService.Str(root, "dueDate"), out var dd)) r.DueDate = dd;

            // ---- 业务校验（模型的输出在这一步被"体检"，不合格直接降级）----
            if (r.ProductModel.Length > 0
                && !known.Any(m => string.Equals(m, r.ProductModel.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                r.Warnings.Add($"型号「{r.ProductModel}」不在 BOM 主数据内，已忽略该字段");
                r.ProductModel = "";
            }
            if (r.PlanQty is <= 0 or > 1_000_000)
            {
                if (r.PlanQty != 0) r.Warnings.Add($"计划数量 {r.PlanQty} 超出合理区间，已忽略");
                r.PlanQty = 0;
            }
            if (r.StartDate is not null && r.DueDate is not null && r.StartDate > r.DueDate)
            {
                r.Warnings.Add("开始日期晚于交期，已按交期倒推修正");
                r.StartDate = r.DueDate;
            }

            var missing = new List<string>();
            if (r.ProductModel.Length == 0) missing.Add("产品型号");
            if (r.PlanQty <= 0) missing.Add("计划数量");
            r.MissingFields.AddRange(missing);

            if (missing.Count > 0)
                return (null, r.Confidence, "模型未能解析出必填项：" + string.Join("、", missing));

            return (r, r.Confidence, "");
        }, minConfidence: 0.55, http: http);

        // **降级到规则抽取**：未启用 / 超时 / 解析失败 / 置信度不足，一律走正则兜底。
        // 这里绝不能直接返回"失败"——那等于"没配模型就什么都干不了"，
        // 而规则抽取虽然能力弱，但零幻觉、永远可用，正是降级该去的地方。
        return r.Ok ? r : RuleFallbackParseWorkOrder(text, known, today, r.Reason);
    }

    /// <summary>① 的规则降级实现：正则抽取，能力弱但**永远可用、零幻觉**。</summary>
    private static LlmCallResult<WorkOrderAiParse> RuleFallbackParseWorkOrder(
        string text, List<string> knownModels, DateTime today, string reason)
    {
        var r = new WorkOrderAiParse { Confidence = 1.0 };
        var t = (text ?? "").ToUpperInvariant();

        r.ProductModel = knownModels.FirstOrDefault(m => t.Contains(m.ToUpperInvariant())) ?? "";

        var qty = System.Text.RegularExpressions.Regex.Match(text ?? "", @"(\d{3,7})\s*(?:只|pcs|PCS)");
        if (qty.Success) r.PlanQty = int.TryParse(qty.Groups[1].Value, out var q) ? q : 0;

        r.Line = new[] { "L1", "L2", "L3" }
            .FirstOrDefault(l => (text ?? "").Contains(l, StringComparison.OrdinalIgnoreCase)) ?? "";

        var dates = System.Text.RegularExpressions.Regex.Matches(text ?? "", @"\d{4}[-/]\d{1,2}[-/]\d{1,2}")
            .Select(m => DateTime.TryParse(m.Value, out var d) ? d : (DateTime?)null)
            .Where(d => d is not null).Select(d => d!.Value).OrderBy(d => d).ToList();
        if (dates.Count >= 2) { r.StartDate = dates[0]; r.DueDate = dates[^1]; }
        else if (dates.Count == 1) { r.StartDate = dates[0]; r.DueDate = dates[0].AddDays(2); }

        if (r.ProductModel.Length == 0) r.MissingFields.Add("产品型号");
        if (r.PlanQty <= 0) r.MissingFields.Add("计划数量");

        return new LlmCallResult<WorkOrderAiParse>
        {
            Ok = r.MissingFields.Count == 0,
            Value = r.MissingFields.Count == 0 ? r : null,
            Source = LlmSources.Rule,
            Reason = string.IsNullOrEmpty(reason) ? "规则抽取" : reason,
            Confidence = r.MissingFields.Count == 0 ? 0.8 : 0.3
        };
    }

    private static string SysParse() =>
        """
        你是 MES 生产计划员的助手，负责把一段口述/邮件式的中文需求，解析成结构化工单字段。

        【绝对规则】
        1. 只输出 JSON，不要任何解释文字、不要 Markdown 代码块以外的其它内容。
        2. 产品型号必须从【knownModels】里原样选一个；无法确定就留空字符串，不要猜、不要编。
        3. 计划数量只能是原文里明确写出的数字；原文没写就填 0，不要估算。
        4. 日期统一用 yyyy-MM-dd。原文写"下周一""月底"等相对表述时，依据【today】换算成绝对日期。
        5. startDate 与 dueDate 都必须有；缺一个就把另一个也留空，并在 missingFields 里列出缺失项。
        6. 不要输出任何 JSON 之外的数字。

        【输出格式】
        {"productModel":"","planQty":0,"line":"","startDate":"","dueDate":"",
         "equipmentNames":[],"confidence":0.0,"missingFields":[]}
        """;

    // ================================================================
    // ② 资产识别与匹配
    //   触发：文本解析出设备名后，或排产页点「AI 匹配资产」
    //   输入：{ process, productModel, candidates:[{code,name,process,line,capacity,status,availableQty}] }
    //   输出：{ matches:[{process,assetCode,score,reason}], confidence }
    //   校验：**assetCode 必须真实存在于资产台账**，否则该条匹配丢弃并记入 rejected
    //         score 低于 0.5 的匹配丢弃；每道工序只保留得分最高的一条
    // ================================================================
    public static LlmCallResult<AssetMatchResult> MatchAssets(
        string process, string productModel, IEnumerable<ProductionAsset> candidates,
        System.Net.Http.HttpClient? http = null)
    {
        var list = candidates?.Where(c => c is not null).ToList() ?? new List<ProductionAsset>();
        var payload = new
        {
            process,
            productModel,
            candidates = list.Select(c => new
            {
                code = c.Code, name = c.Name, process = c.Process, line = c.Line,
                capacity = c.Capacity, status = c.Status, models = c.Models,
                type = c.Type
            })
        };

        return LlmService.CallJson<AssetMatchResult>(SysMatch(), payload, root =>
        {
            var result = new AssetMatchResult
            {
                Confidence = LlmService.Dbl(root, "confidence", 0.5)
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in LlmService.Arr(root, "matches"))
            {
                var code = LlmService.Str(m, "assetCode");
                var score = LlmService.Dbl(m, "score", 0);

                // —— 业务校验 1：资产必须真实存在（模型可能编造编码）——
                var real = MesDatabase.FindAsset(code);
                if (real is null)
                {
                    result.Rejected.Add($"资产编码「{code}」不存在于资产台账，已丢弃该匹配");
                    continue;
                }
                // —— 业务校验 2：得分太低的不采纳 ——
                if (score < 0.5)
                {
                    result.Rejected.Add($"{real.Name}（{code}）匹配得分 {score:F2} 低于 0.5，已丢弃");
                    continue;
                }
                // —— 业务校验 3：每道工序只保留一条（同分取先出现的）——
                var proc = LlmService.Str(m, "process", real.Process);
                if (!seen.Add(proc + "|" + real.Code)) continue;

                result.Matches.Add(new AssetMatchItem
                {
                    Process = proc,
                    AssetCode = real.Code,
                    AssetName = real.Name,
                    Score = score,
                    Reason = LlmService.Str(m, "reason")
                });
            }

            if (result.Matches.Count == 0)
                return (null, result.Confidence, "模型未给出任何通过校验的资产匹配");

            return (result, result.Confidence, "");
        }, minConfidence: 0.55, http: http);
    }

    private static string SysMatch() =>
        """
        你是 MES 的设备选型助手，负责在候选资产里为指定工序挑出最合适的一台。

        【绝对规则】
        1. 只输出 JSON，不要任何解释文字。
        2. assetCode 必须从【candidates】的 code 里原样选，严禁编造或改写编码。
        3. 每道工序最多给出一条匹配。
        4. score 是 0~1 的匹配把握，没把握就给低分，不要给高分凑数。
        5. 状态为"维护""停机"的资产不要选；型号不匹配的不要选。
        6. reason 用一句大白话说明为什么选它，不超过 30 字。

        【输出格式】
        {"matches":[{"process":"","assetCode":"","score":0.0,"reason":""}],"confidence":0.0}
        """;

    // ================================================================
    // ③ 库存与占用判断的归因说明
    //   触发：资源校验（CheckResourceAvailability）算完之后
    //   输入：**规则的计算结果**（物料缺口、资产冲突明细、可用余量、方案）
    //   输出：{ conclusion, reasons[], suggestions[], confidence }
    //   校验：**纯措辞任务，禁止出现素材外的数字**；数字核对不通过即整段丢弃
    // ================================================================
    public static LlmCallResult<ResourceExplainResult> ExplainResourceCheck(
        ResourceCheckResult check, System.Net.Http.HttpClient? http = null)
    {
        var facts = BuildResourceFacts(check);
        if (facts.ConflictDetail.Count == 0 && facts.ShortageDetail.Count == 0)
        {
            return new LlmCallResult<ResourceExplainResult>
            {
                Ok = true,
                Source = LlmSources.Rule,
                Confidence = 1.0,
                Value = new ResourceExplainResult
                {
                    Conclusion = $"资源校验通过：{check.SummaryText}",
                    Confidence = 1.0
                }
            };
        }

        var payload = new
        {
            summary = check.SummaryText,
            productModel = check.ProductModel,
            planQty = check.PlanQty,
            window = $"{check.WindowStart:yyyy-MM-dd HH:mm} ~ {check.WindowEnd:yyyy-MM-dd HH:mm}",
            assetConflicts = facts.ConflictDetail,
            materialShortage = facts.ShortageDetail,
            options = facts.Options,
            // 授权数字白名单：显式告诉模型哪些数字可以原样引用。
            // 实战经验（2026-10-08 真实接入 DeepSeek 时观察到）：不给白名单，模型会自行
            // 推算出一些"听起来很合理"的数字（实测出现过 5355 / 6309 / 73），
            // 结果被数字核对整段拦截 —— 白名单能让它照抄，而不是自己算。
            allowedNumbers = facts.Allowed.OrderBy(x => x).ToList(),
            rules = new[]
            {
                "可用量不含待检区与退货待处理区",
                "时间窗端点相接不算冲突",
                "可用余量 = 资产并发容量 − 其它工单占用量"
            }
        };

        return LlmService.CallJson<ResourceExplainResult>(SysExplain(), payload, root =>
        {
            var r = new ResourceExplainResult
            {
                Conclusion = LlmService.Str(root, "conclusion"),
                Reasons = LlmService.StrList(root, "reasons"),
                Suggestions = LlmService.StrList(root, "suggestions"),
                Confidence = LlmService.Dbl(root, "confidence", 0.5)
            };
            if (r.Conclusion.Length == 0) return (null, r.Confidence, "模型未给出结论");
            return (r, r.Confidence, "");
        }, allowedNumbers: facts.Allowed, http: http);
    }

    private static string SysExplain() =>
        """
        你是 MES 生产主管的助手，负责把系统算好的资源校验结果讲成人话。

        【绝对规则】
        1. 只输出 JSON，不要任何解释文字。
        2. **只能用【allowedNumbers】白名单里的数字**，原样照抄，一个都不许改。
           严禁做任何加减乘除、单位换算、四舍五入，也不要输出白名单之外的任何数字
           （包括自���的"大概""约""预计"数字）。
        3. 你的任务只是"解释"和"给建议"，不是重新计算——系统已经算过了，你的结论不能与之矛盾。
        4. 不要给出合格/不合格、能不能做之类的最终判定，只说"存在什么冲突、可以怎么办"。
        5. reason 与 suggestion 各不超过 4 条，每条不超过 40 字，用工厂里的口语。
        6. 如果白名单里的数字不够用，说明你编了数字——那就只讲定性结论，不写数字。

        【输出格式】
        {"conclusion":"","reasons":[],"suggestions":[],"confidence":0.0}
        """;

    private static (List<string> ConflictDetail, List<string> ShortageDetail,
        List<string> Options, HashSet<string> Allowed) BuildResourceFacts(ResourceCheckResult check)
    {
        var conflicts = new List<string>();
        foreach (var l in check.AssetLines.Where(x => x.Status != AssetCheckStatus.Ok))
        {
            foreach (var c in l.Conflicts)
                conflicts.Add($"{l.AssetName}（{l.AssetCode}，工序{l.Process}）"
                              + $"：工单 {c.WoNo} 占用 {c.OccupiedQty} 台，时间 {c.WindowText}"
                              + (c.OverlapText.Length > 0 ? $"，重叠 {c.OverlapText}" : "")
                              + $"；本工单需 {l.NeedQty} 台、可用余量 {l.AvailableQty} 台");
            if (l.Conflicts.Count == 0)
                conflicts.Add($"{l.AssetName}（{l.AssetCode}，工序{l.Process}）：可用余量 {l.AvailableQty} 台，本工单需 {l.NeedQty} 台");
        }

        var shortage = new List<string>();
        if (check.Material is { NoBom: false })
        {
            foreach (var r in check.Material.ShortageRows)
                shortage.Add($"{r.MaterialName}：需求 {r.RequirementText} {r.Unit}，"
                              + $"可用 {r.AvailableText} {r.Unit}，缺 {r.ShortageText} {r.Unit}");
        }

        var options = check.AllOptions.Select(o => $"{o.Kind}：{o.Text}").ToList();

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        void Feed(string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(s.Replace(",", ""), @"\d+(?:\.\d+)?"))
            {
                var v = m.Value.Contains('.') ? m.Value.TrimEnd('0').TrimEnd('.') : m.Value;
                if (v.Length > 0) allowed.Add(v);
            }
        }
        Feed(check.SummaryText); Feed(check.ProductModel);
        Feed(check.PlanQty.ToString());
        Feed(check.WindowStart.ToString("yyyy-MM-dd HH:mm"));
        Feed(check.WindowEnd.ToString("yyyy-MM-dd HH:mm"));
        foreach (var s in conflicts) Feed(s);
        foreach (var s in shortage) Feed(s);
        foreach (var s in options) Feed(s);
        foreach (var l in check.AssetLines)
        {
            Feed(l.AssetCode); Feed(l.AssetName); Feed(l.Process);
            Feed(l.NeedQty.ToString()); Feed(l.AvailableQty.ToString());
            Feed(l.OccupiedByOthers.ToString()); Feed(l.Capacity.ToString());
        }

        return (conflicts, shortage, options, allowed);
    }

    // ================================================================
    // ④ 方案与排产建议
    //   触发：排产页点「AI 推演」
    //   输入：规则的排产结论 + 依据 + 资源校验结果
    //   输出：{ conclusion, actions[], risks[], confidence }
    //   校验：同 ③，纯措辞 + 数字核对
    // ================================================================
    public static LlmCallResult<ScheduleAdviseResult> AdviseSchedule(
        ScheduleRecommendation rec, System.Net.Http.HttpClient? http = null)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        void Feed(string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(s.Replace(",", ""), @"\d+(?:\.\d+)?"))
            {
                var v = m.Value.Contains('.') ? m.Value.TrimEnd('0').TrimEnd('.') : m.Value;
                if (v.Length > 0) allowed.Add(v);
            }
        }
        Feed(rec.ProductModel); Feed(rec.OrderQty.ToString()); Feed(rec.InputQty.ToString());
        Feed(rec.RecommendedLine); Feed(rec.PlannedStart.ToString("yyyy-MM-dd"));
        Feed(rec.PlannedFinish.ToString("yyyy-MM-dd"));
        Feed(rec.DueDate.ToString("yyyy-MM-dd"));
        Feed(rec.SlackDays.ToString("0.##"));
        Feed(rec.Yield.ToString("0.##")); Feed(rec.RequiredHours.ToString("0.##"));
        foreach (var e in rec.Evidence) Feed(e);
        if (rec.ResourceCheck is not null)
        {
            Feed(rec.ResourceCheck.SummaryText);
            foreach (var l in rec.ResourceCheck.AssetLines)
            {
                Feed(l.AssetName); Feed(l.AssetCode); Feed(l.Process);
                Feed(l.NeedQty.ToString()); Feed(l.AvailableQty.ToString());
            }
        }

        var payload = new
        {
            poNo = rec.PoNo,
            customer = rec.Customer,
            productModel = rec.ProductModel,
            orderQty = rec.OrderQty,
            inputQty = rec.InputQty,
            recommendedLine = rec.RecommendedLine,
            plannedStart = rec.PlannedStart.ToString("yyyy-MM-dd HH:mm"),
            plannedFinish = rec.PlannedFinish.ToString("yyyy-MM-dd HH:mm"),
            dueDate = rec.DueDate.ToString("yyyy-MM-dd HH:mm"),
            slackDays = rec.SlackDays.ToString("0.##"),
            risk = rec.Risk,
            yield = rec.Yield.ToString("0.##"),
            unitHours = (rec.UnitHours * 3600).ToString("0.0"),
            requiredHours = rec.RequiredHours.ToString("0.##"),
            evidence = rec.Evidence,
            resource = rec.ResourceCheck?.SummaryText ?? "未做资源校验",
            stockCheck = rec.StockCheck?.SummaryText ?? "",
            // 授权数字白名单（同 ③：不给白名单，模型会自己推算出素材外的数字）
            allowedNumbers = allowed.OrderBy(x => x).ToList()
        };

        return LlmService.CallJson<ScheduleAdviseResult>(SysAdvise(), payload, root =>
        {
            var r = new ScheduleAdviseResult
            {
                Conclusion = LlmService.Str(root, "conclusion"),
                Actions = LlmService.StrList(root, "actions"),
                Risks = LlmService.StrList(root, "risks"),
                Confidence = LlmService.Dbl(root, "confidence", 0.5)
            };
            if (r.Conclusion.Length == 0) return (null, r.Confidence, "模型未给出结论");
            return (r, r.Confidence, "");
        }, allowedNumbers: allowed, http: http);
    }

    // ================================================================
    // ⑤ 资材采购分析（防御性预测推理）
    //   触发：采购申请页点「🤖 AI 库存分析」（用户显式触发，不随页面加载自动调）
    //   输入：PurchaseAnalysisInput——库存/需求/在途/历史消耗/限额，全部由规则算好
    //   输出：{ conclusion, riskLevel, suggestedQty, reasons[], actions[], confidence }
    //   校验：① riskLevel 必须是 低/中/高 ② 建议量**必须过规则钳制**才能采用
    //         ③ 风险等级与规则判定**取更保守的一个**（模型说"低"但规则算出缺料 → 升为高）
    //   降级：模型不可用/超时/格式错/置信度不足 → 规则直接给结论与建议量，来源标"规则"
    // ================================================================
    public static PurchaseAiAnalysis AnalyzePurchase(PurchaseAnalysisInput input, System.Net.Http.HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 规则版结果既是降级路径，也是钳制模型结果的基准——任何时候都先算一遍
        var rule = BuildRulePurchaseAnalysis(input, "云端大模型未启用或未配置 API Key");

        if (!LlmService.Enabled) return rule;

        var payload = new
        {
            material = new { input.MaterialCode, input.MaterialName, input.Unit, input.MaterialType },
            available = input.Available,
            safetyStock = input.SafetyStock,
            maxPurchaseQty = input.MaxPurchaseQty,
            requirement = input.Requirement,
            pendingQty = input.PendingQty,
            consumed90d = input.ConsumedQty,
            consumptionSamples = input.ConsumptionSamples,
            relatedWoCount = input.RelatedWoCount,
            derived = new
            {
                netShortage = input.NetShortage,
                safetyGap = input.SafetyGap,
                ruleSuggestedQty = input.RuleSuggestedQty,
                dailyConsumption = input.DailyConsumption,
                coverDays = input.CoverDays,
                enoughSamples = input.EnoughSamples
            },
            bounds = new
            {
                minSuggested = input.RuleSuggestedQty,
                maxSuggested = PurchaseSuggest.UpperBound(input)
            }
        };

        var r = LlmService.CallJson<PurchaseAiAnalysis>(SysPurchaseAnalyze(), payload, root =>
        {
            var a = new PurchaseAiAnalysis
            {
                Conclusion = LlmService.Str(root, "conclusion"),
                RiskLevel = LlmService.Str(root, "riskLevel", PurchaseRisk.Medium),
                RawSuggestedQty = LlmService.Dbl(root, "suggestedQty", 0),
                Reasons = LlmService.StrList(root, "reasons"),
                Actions = LlmService.StrList(root, "actions"),
                Confidence = LlmService.Dbl(root, "confidence", 0.5)
            };

            // 风险等级必须是 低/中/高 之一（模型可能返回"较低""medium"等，一律拒绝）
            if (!PurchaseRisk.All.Contains(a.RiskLevel)) return (null, a.Confidence, "riskLevel 取值非法");
            if (a.Conclusion.Length == 0) return (null, a.Confidence, "模型未给出结论");
            return (a, a.Confidence, "");
        }, minConfidence: 0.55, http: http);

        if (!r.Ok || r.Value is null)
            return BuildRulePurchaseAnalysis(input, r.Reason);

        var v = r.Value;

        // ★ 防御核心：模型给的建议量必须钳制 —— 下限补足缺口+安全线，上限不超超量线/限额
        var (finalQty, clamped) = PurchaseSuggest.Clamp(input, v.RawSuggestedQty);
        v.SuggestedQty = finalQty;
        v.Clamped = clamped;

        // 风险等级与规则判定取更保守者：模型说"低"但规则算出净缺口，必须升为"高"
        var ruleRisk = PurchaseSuggest.RuleRisk(input);
        if (RiskRank(v.RiskLevel) < RiskRank(ruleRisk))
        {
            v.Note = $"模型判断为「{v.RiskLevel}」，但规则口径已算出净缺口，按更保守的「{ruleRisk}」采用。";
            v.RiskLevel = ruleRisk;
        }
        else if (clamped)
        {
            v.Note = $"模型建议量 {v.RawSuggestedQty:0.##} {input.Unit} 已按规则钳制为 {finalQty:0.##} {input.Unit}"
                   + $"（下限补足缺口+安全线，上限不超超量线/限额）。";
        }
        else if (!input.EnoughSamples)
        {
            v.Note = $"历史领用样本仅 {input.ConsumptionSamples} 条，不足以支撑趋势预测，结论仅供参考。";
        }

        v.Source = LlmSources.Model;
        return v;
    }

    private static int RiskRank(string level) => level switch
    {
        PurchaseRisk.High => 3,
        PurchaseRisk.Medium => 2,
        _ => 1
    };

    /// <summary>规则版采购分析（降级路径 + 钳制基准）</summary>
    private static PurchaseAiAnalysis BuildRulePurchaseAnalysis(PurchaseAnalysisInput input, string reason)
    {
        var qty = input.RuleSuggestedQty;
        var clampNote = "";
        if (input.MaxPurchaseQty > 1e-9 && qty > input.MaxPurchaseQty)
        {
            qty = input.MaxPurchaseQty;
            clampNote = $"已按单次采购限额 {input.MaxPurchaseQty:0.##} {input.Unit} 封顶。";
        }

        var conclusion = input.NetShortage > 1e-9
            ? $"当前净缺口 {input.NetShortage:0.##} {input.Unit}，建议采购 {qty:0.##} {input.Unit} 补足。"
            : input.SafetyGap > 1e-9
                ? $"库存 {input.Available:0.##} {input.Unit} 低于安全库存 {input.SafetyStock:0.##} {input.Unit}，建议补 {qty:0.##} {input.Unit}。"
                : $"库存与在途可覆盖需求，暂无采购必要。";

        var a = new PurchaseAiAnalysis
        {
            Conclusion = conclusion,
            RiskLevel = PurchaseSuggest.RuleRisk(input),
            RawSuggestedQty = qty,
            SuggestedQty = qty,
            Source = LlmSources.Rule,
            Confidence = 1.0,
            Note = string.IsNullOrEmpty(reason) ? clampNote : reason + (clampNote.Length > 0 ? "；" + clampNote : "")
        };
        if (input.NetShortage > 1e-9)
            a.Reasons.Add($"净缺口 = 需求 {input.Requirement:0.##} − 可用 {input.Available:0.##} − 在途 {input.PendingQty:0.##} = {input.NetShortage:0.##} {input.Unit}");
        if (input.SafetyGap > 1e-9)
            a.Reasons.Add($"低于安全库存 {input.SafetyStock:0.##} {input.Unit}，缺口 {input.SafetyGap:0.##} {input.Unit}");
        if (input.EnoughSamples && input.CoverDays > 0)
            a.Reasons.Add($"按近 90 天日均消耗 {input.DailyConsumption:0.##} {input.Unit} 估算，现库存可支撑约 {input.CoverDays:0.#} 天");
        else
            a.Reasons.Add("历史领用样本不足，无法估算消耗趋势");
        if (input.MaxPurchaseQty > 1e-9)
            a.Reasons.Add($"单次采购限额 {input.MaxPurchaseQty:0.##} {input.Unit}");

        return a;
    }

    private static string SysPurchaseAnalyze() =>
        """
        你是超级电容工厂的采购分析助手，负责基于库存、需求、在途与历史消耗，判断缺料风险并给出建议采购量。

        【绝对规则】
        1. 只输出 JSON，不要任何解释文字。
        2. **你只能使用【用户消息】里给出的数字**，严禁自行推算、四舍五入或换算单位。
           你要做的判断（该不该补、风险多高、买多少）必须落在系统已经算好的
           【derived】与【bounds】里：建议量不得低于 minSuggested，不得高于 maxSuggested。
        3. riskLevel 只能是三个字符串之一："低"、"中"、"高"。不要写"较低""medium"之类。
        4. 如果历史领用样本不足（enoughSamples 为 false），必须在 reasons 里点明"样本不足"，
           并且**不要假装能预测趋势**。
        5. 不要输出合格/不合格之类的质量判定，也不要替人审批——你只做分析与建议。
        6. reasons 与 actions 各 2 到 4 条，每条不超过 40 字，用工厂里的口语。

        【输出格式】
        {"conclusion":"","riskLevel":"低","suggestedQty":0,
         "reasons":[],"actions":[],"confidence":0.0}
        """;

    private static string SysAdvise() =>
        """
        你是超级电容工厂的生产经理助手，负责把系统算好的排产结果讲成主管能直接决策的话。

        【绝对规则】
        1. 只输出 JSON，不要任何解释文字。
        2. **只能用【allowedNumbers】白名单里的数字**，原样照抄，一个都不许改。
           严禁自行推算、四舍五入、换算单位，也不要输出白名单之外的任何数字
           （包括自己估出来的"大概""约"数字）。
        3. 你的角色是"参谋"：解释现状、指出风险、给可执行的下一步，**不做最终判定**
           （能不能开工、合格与否由系统和人工决定）。
        4. actions 与 risks 各不超过 4 条，每条不超过 40 字。
        5. 不要重复罗列已有的数字清单，直接说结论和怎么办。
        6. 如果白名单里的数字不够用，说明你编了数字——那就只讲定性结论，不写数字。

        【输出格式】
        {"conclusion":"","actions":[],"risks":[],"confidence":0.0}
        """;
}
