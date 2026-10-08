using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 大模型结构化调用服务
//
// 解决的问题：现有 LlmAssistant 只能做"问答"（拿一串文字回来），
// 但工单解析、资产匹配这类任务需要**结构化 JSON**，而且必须能安全降级。
//
// 这个服务统一提供四件事，缺一不可：
//   ① 调用（复用 LlmHttp 与既有配置，不新增配置项）
//   ② 容错解析（模型爱返回 ```json 包裹、前后带废话、尾逗号——都要能救回来）
//   ③ **降级**（超时 / 异常 / 解析失败 / 置信度不足 → 明确标注来源为「规则」）
//   ④ **数字核对**（解释类任务里出现素材外的数字 → 整段丢弃，防止模型编数据）
//
// 铁律（与 AI 助手、智能排产完全一致）：
//   **判定永远由规则做，模型只负责"理解文本"和"组织语言"。**
//   模型不得参与"够不够、撞不撞、能不能做"的结论——那些要可审计。
// ============================================================================

/// <summary>模型调用结果（带来源标注 —— 这是降级链路的对外契约）</summary>
public class LlmCallResult<T> where T : class
{
    /// <summary>是否拿到可用结果</summary>
    public bool Ok { get; init; }

    /// <summary>结构化结果（Ok=false 时为 null）</summary>
    public T? Value { get; init; }

    /// <summary>来源：<b>模型</b> / <b>规则</b>。界面上必须显示这个，来源不透明等于不可信。</summary>
    public string Source { get; init; } = LlmSources.Rule;

    /// <summary>降级或失败原因（成功时为空）</summary>
    public string Reason { get; init; } = "";

    /// <summary>模型自评置信度 0~1</summary>
    public double Confidence { get; init; }

    /// <summary>耗时（毫秒）</summary>
    public int LatencyMs { get; init; }

    /// <summary>原始返回（排查用，界面上不展示）</summary>
    public string RawJson { get; init; } = "";

    /// <summary>给界面显示的一行说明：谁答的、置信多少、为什么降级</summary>
    public string TraceText => Ok && Source == LlmSources.Model
        ? $"来源：模型（置信度 {Confidence:P0}，耗时 {LatencyMs}ms）"
        : $"来源：规则" + (string.IsNullOrEmpty(Reason) ? "（规则计算）" : $"（模型未生效：{Reason}）");
}

/// <summary>结果来源常量</summary>
public static class LlmSources
{
    /// <summary>由云端大模型产出</summary>
    public const string Model = "模型";

    /// <summary>由本地确定性规则产出（降级后的默认来源）</summary>
    public const string Rule = "规则";
}

/// <summary>大模型结构化调用服务</summary>
public static class LlmService
{
    /// <summary>默认置信度门槛：低于此值一律降级为规则</summary>
    public const double DefaultMinConfidence = 0.60;

    /// <summary>当前是否可调用（已配置 + 已启用 + 有 Key）</summary>
    public static bool Enabled
    {
        get
        {
            try { return AssistantLlmSettingsStore.Load().CanCall; }
            catch { return false; }
        }
    }

    /// <summary>当前配置（界面展示用）</summary>
    public static AssistantLlmSettings CurrentSettings
    {
        get
        {
            try { return AssistantLlmSettingsStore.Load(); }
            catch { return new AssistantLlmSettings(); }
        }
    }

    /// <summary>
    /// 结构化调用：给系统提示 + 业务载荷，拿回 JSON 对象。
    /// 任何一步出问题都返回 Ok=false / Source=规则，**绝不抛异常到业务层**。
    /// </summary>
    /// <param name="systemPrompt">系统提示（含输出格式约束）</param>
    /// <param name="payload">业务载荷（会被序列化成 JSON 放进 user 消息）</param>
    /// <param name="parse">从 JSON 文本反序列化并做业务校验；返回 null 表示不可用</param>
    /// <param name="allowedNumbers">授权数字集合；非空时启用数字核对（解释类任务用）</param>
    public static LlmCallResult<T> CallJson<T>(
        string systemPrompt, object payload,
        Func<JsonElement, (T? Value, double Confidence, string Reason)> parse,
        double minConfidence = DefaultMinConfidence,
        HashSet<string>? allowedNumbers = null,
        HttpClient? http = null) where T : class
    {
        var cfg = CurrentSettings;
        if (!cfg.CanCall)
            return new LlmCallResult<T> { Ok = false, Source = LlmSources.Rule, Reason = "云端大模型未启用或未配置 API Key" };

        var userPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        });

        var sw = Stopwatch.StartNew();
        string raw;
        try
        {
            raw = LlmHttp.Send(cfg, systemPrompt, userPayload, http);
        }
        catch (Exception ex)
        {
            return new LlmCallResult<T>
            {
                Ok = false,
                Source = LlmSources.Rule,
                Reason = LlmHttp.ShortError(ex),
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
        sw.Stop();

        // ① 容错提取 JSON（模型常带 ```json 包裹或前后废话）
        if (!TryExtractJson(raw, out var json))
            return Bad<T>("模型返回内容中没有可解析的 JSON", raw, sw);

        // ② 业务解析 + 校验
        JsonElement root;
        try { using var doc = JsonDocument.Parse(json); root = doc.RootElement.Clone(); }
        catch (Exception ex) { return Bad<T>("JSON 解析失败：" + ex.Message, raw, sw); }

        var (value, confidence, reason) = parse(root);
        if (value is null)
            return Bad<T>(string.IsNullOrWhiteSpace(reason) ? "模型结果未通过业务校验" : reason, raw, sw);

        // ③ 置信度门槛：模型自己都不确定，就不该拿它的输出当结论
        if (confidence < minConfidence)
            return Bad<T>($"模型置信度 {confidence:P0} 低于门槛 {minConfidence:P0}", raw, sw, confidence);

        // ④ 数字核对：解释类任务里出现素材外的数字 → 整段丢弃（模型编数是最高风险）
        if (allowedNumbers is { Count: > 0 })
        {
            // 序列化时必须**不转义中文**（与请求体同一口径）：默认编码器会把中文变成 \u5377，
            // 于是"卷绕机"里的数字被当成编造数字——自测实测踩过这个坑。
            var text = JsonSerializer.Serialize(root, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var offenders = FindUnauthorizedNumbers(text, allowedNumbers);
            if (offenders.Count > 0)
                return Bad<T>($"模型输出出现未授权数字（{string.Join("、", offenders.Take(3))}），已整段丢弃", raw, sw, confidence);
        }

        return new LlmCallResult<T>
        {
            Ok = true,
            Value = value,
            Source = LlmSources.Model,
            Confidence = confidence,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            RawJson = LlmHttp.Shorten(raw, 4000)
        };
    }

    private static LlmCallResult<T> Bad<T>(string reason, string raw, Stopwatch sw, double confidence = 0)
        where T : class =>
        new()
        {
            Ok = false,
            Source = LlmSources.Rule,
            Reason = reason,
            Confidence = confidence,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            RawJson = LlmHttp.Shorten(raw, 4000)
        };

    /// <summary>
    /// 从模型返回里抠出 JSON 对象：兼容 ```json 包裹、前后废话、尾逗号。
    /// 找不到就返回 false（调用方负责降级）。
    /// </summary>
    public static bool TryExtractJson(string? raw, out string json)
    {
        json = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var t = raw.Trim();

        // 去掉 ```json ... ``` 包裹
        var fence = Regex.Match(t, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (fence.Success) t = fence.Groups[1].Value.Trim();

        // 找第一个 { 与最后一个 } 之间的内容（按深度配对，避免被文字里的花括号带偏）
        var start = t.IndexOf('{');
        if (start < 0) return false;

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < t.Length; i++)
        {
            var c = t[i];
            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        json = t[start..(i + 1)];
                        return true;
                    }
                    break;
            }
        }
        return false;
    }

    /// <summary>
    /// 找出文本中不在授权集合内的数字（复用 AI 助手那一套口径：
    /// 先剥行首有序编号、千分位去逗号）。解释类任务专用。
    /// </summary>
    public static List<string> FindUnauthorizedNumbers(string text, HashSet<string> allowed)
    {
        // 先剥掉行首有序编号（"1. " / "2、"），避免把列表序号误判成编造的数字
        var cleaned = Regex.Replace(text, @"(?m)^\s*\d+\s*[.、)）]\s*", "");
        // 再剥掉 confidence 等**模型自评元字段**——它们是模型对自身把握的评分，
        // 不是业务数字。自测实测：不清掉的话任何模型返回都会因 "confidence":0.85 被误判为编造。
        cleaned = Regex.Replace(cleaned, @"""(confidence|score|probability)""\s*:\s*\d+(\.\d+)?", "\"$1\":0");

        var t = cleaned.Replace(",", "").Replace("，", "");
        var bad = new List<string>();
        foreach (Match m in Regex.Matches(t, @"\d+(?:\.\d+)?"))
        {
            var v = m.Value;
            if (v.Contains('.')) v = v.TrimEnd('0').TrimEnd('.');
            if (v.Length == 0 || v == "0") continue;
            if (!allowed.Contains(v) && !bad.Contains(v)) bad.Add(v);
        }
        return bad;
    }

    // ================= JSON 取值小工具（容错） =================

    public static string Str(JsonElement e, string name, string fallback = "")
    {
        if (e.ValueKind != JsonValueKind.Object) return fallback;
        if (!e.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? fallback)
             : v.ValueKind == JsonValueKind.Number ? v.ToString() : fallback;
    }

    public static int Int(JsonElement e, string name, int fallback = 0)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return fallback;
    }

    public static double Dbl(JsonElement e, string name, double fallback = 0)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
        return fallback;
    }

    public static bool Bool(JsonElement e, string name, bool fallback = false)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => fallback
        };
    }

    public static List<string> StrList(JsonElement e, string name)
    {
        var result = new List<string>();
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return result;
        if (v.ValueKind == JsonValueKind.Array)
            foreach (var item in v.EnumerateArray())
            {
                var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
            }
        else if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) result.AddRange(s.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }
        return result;
    }

    public static List<JsonElement> Arr(JsonElement e, string name)
    {
        var result = new List<JsonElement>();
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return result;
        if (v.ValueKind == JsonValueKind.Array) result.AddRange(v.EnumerateArray());
        return result;
    }
}
