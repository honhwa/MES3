using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 云端大模型接入（OpenAI 兼容的 /chat/completions 接口，DeepSeek 直接可用）
//
// 架构选择的理由（这一段比代码重要）：
//   让大模型"自己取数"是行不通的——它不知道 MesDatabase 里有什么，问它"今天产量多少"
//   它只会编一个看起来合理的数。生产现场的问答一旦编数就是事故。
//
//   所以这里走的是「**规则取数 + 模型措辞**」：
//     ① 先让规则引擎（RuleBasedAssistant）把数取准，产出关键数据 + 数据依据；
//     ② 再把这些**已经算好的数**作为唯一素材交给大模型，让它组织成人话、按管理视角排重点；
//     ③ 模型返回后**逐数字核对**：只要出现素材里没有的数字，整段丢弃、退回规则引擎原文。
//
//   好处：数字永远来自仓储（可追溯）、措辞由模型提供（自然）、
//         断网/超时/欠费/报错一律自动降级到本地，页面不崩、演示不翻车。
// ============================================================================

/// <summary>
/// 云端大模型配置。默认整套关闭——不填 Key、不打开开关，系统行为与纯本地完全一致。
/// </summary>
public sealed class AssistantLlmSettings
{
    /// <summary>是否启用云端大模型（默认关）</summary>
    public bool Enabled { get; set; }

    /// <summary>服务地址（OpenAI 兼容的 base url，不带 /chat/completions）</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>模型名</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>API Key（也可用环境变量 <see cref="ApiKeyEnvVar"/> 覆盖，更安全）</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>超时秒数</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>采样温度（工厂问答要稳，默认偏低）</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>单次回答最大 token</summary>
    public int MaxTokens { get; set; } = 700;

    public const string DefaultBaseUrl = "https://api.deepseek.com";
    public const string DefaultModel = "deepseek-chat";

    /// <summary>API Key 的环境变量名（优先级高于配置文件，避免 Key 落盘）</summary>
    public const string ApiKeyEnvVar = "DEEPSEEK_API_KEY";

    /// <summary>可选模型（下拉候选；也可手填其它 OpenAI 兼容模型）</summary>
    public static readonly string[] ModelCandidates = { "deepseek-chat", "deepseek-reasoner" };

    /// <summary>实际生效的 Key：环境变量优先于配置文件</summary>
    public string ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        return string.IsNullOrWhiteSpace(env) ? (ApiKey ?? "").Trim() : env.Trim();
    }

    /// <summary>是否可以真的发起调用</summary>
    public bool CanCall => Enabled && ResolveApiKey().Length > 0;

    /// <summary>克隆一份（界面编辑用，避免直接改到生效对象）</summary>
    public AssistantLlmSettings Clone() => new()
    {
        Enabled = Enabled,
        BaseUrl = BaseUrl,
        Model = Model,
        ApiKey = ApiKey,
        TimeoutSeconds = TimeoutSeconds,
        Temperature = Temperature,
        MaxTokens = MaxTokens
    };
}

/// <summary>
/// 配置存取。落到 <c>%APPDATA%\SuperCapMES\assistant-llm.json</c>——
/// **刻意不放在项目目录**：Key 不能进 git，也不该跟着 exe 走。
/// 任何读写异常都吞掉返回默认值：配置坏了不能连累整个界面起不来。
/// </summary>
public static class AssistantLlmSettingsStore
{
    private const string FileName = "assistant-llm.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>默认配置目录</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperCapMES");

    /// <summary>默认配置文件全路径</summary>
    public static string DefaultFilePath => Path.Combine(DefaultDirectory, FileName);

    /// <summary>读取配置（不存在或读取失败返回默认配置）。<paramref name="filePath"/> 供自测注入临时路径。</summary>
    public static AssistantLlmSettings Load(string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        try
        {
            if (!File.Exists(path)) return new AssistantLlmSettings();
            var json = File.ReadAllText(path, Encoding.UTF8);
            var s = JsonSerializer.Deserialize<AssistantLlmSettings>(json, JsonOpts);
            if (s is null) return new AssistantLlmSettings();
            // 兜底：反序列化出空值/非法值时回落到默认，避免把 HttpClient 配崩
            if (string.IsNullOrWhiteSpace(s.BaseUrl)) s.BaseUrl = AssistantLlmSettings.DefaultBaseUrl;
            if (string.IsNullOrWhiteSpace(s.Model)) s.Model = AssistantLlmSettings.DefaultModel;
            if (s.TimeoutSeconds is < 3 or > 300) s.TimeoutSeconds = 30;
            if (s.MaxTokens is < 64 or > 8000) s.MaxTokens = 700;
            if (s.Temperature is < 0 or > 2) s.Temperature = 0.2;
            return s;
        }
        catch
        {
            return new AssistantLlmSettings();
        }
    }

    /// <summary>保存配置。返回是否写入成功（失败不抛异常，由调用方提示）。</summary>
    public static bool Save(AssistantLlmSettings settings, string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOpts), Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 云端大模型后端：**规则取数 + 模型措辞**，并在返回前做数字核对。
/// 任何一步出问题都退回本地规则引擎的原文，绝不把半截或不可信的内容塞给用户。
/// </summary>
public sealed class LlmAssistant : IRemoteAssistantBackend
{
    /// <summary>本地取数后端：所有数字的唯一来源</summary>
    private readonly IAssistantBackend _facts;

    private readonly AssistantLlmSettings _cfg;
    private readonly HttpClient _http;

    /// <summary>共享 HttpClient（避免每次提问重建连接池）。超时由每次请求的 CancellationToken 控制。</summary>
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    /// <summary>
    /// 请求体序列化选项：不转义中文。
    /// 默认的 JSON 编码器会把「关键数据」写成 \u5173\u952E\u6570\u636E——体积更大、
    /// 抓包/日志里完全没法读，排查线上问题时很要命。HTTP 头已经声明 UTF-8，直接发原文即可。
    /// </summary>
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public LlmAssistant(AssistantLlmSettings cfg, IAssistantBackend? factsBackend = null, HttpClient? http = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _facts = factsBackend ?? new RuleBasedAssistant();
        _http = http ?? SharedHttp;
    }

    /// <summary>是否配置齐全（启用 + 有 Key）</summary>
    public bool IsAvailable => _cfg.CanCall;

    /// <summary>远端目标：数据发往哪个域名 + 用哪个模型（界面上要能看见）</summary>
    public string Endpoint
    {
        get
        {
            var host = _cfg.BaseUrl;
            try { host = new Uri(_cfg.BaseUrl).Host; } catch { /* 非法地址就原样显示 */ }
            return $"{host} ｜ {_cfg.Model}";
        }
    }

    public string Name => $"云端大模型（{_cfg.Model}）+ 本地规则取数";

    public string Describe =>
        $"数字全部由本地仓储汇总算出，只把【关键数据】交给云端模型组织措辞，返回后逐个数字核对，"
        + $"出现未授权数字即整段丢弃并退回本地原文。数据发往：{Endpoint}。"
        + $"发送内容不含原始明细行、客户名与供应商名。";

    // ================= 主流程 =================

    public AssistantReply Ask(string question)
    {
        // ① 先用规则引擎把数取准——这一步不联网
        var baseline = _facts.Ask(question);

        if (!IsAvailable)
            return Degrade(baseline, "云端未启用（未开启开关或未配置 API Key）");

        string raw;
        try
        {
            raw = SendChat(question, baseline);
        }
        catch (Exception ex)
        {
            return Degrade(baseline, ShortError(ex));
        }

        if (string.IsNullOrWhiteSpace(raw))
            return Degrade(baseline, "云端返回内容为空");

        var (headline, notes) = ParseAnswer(raw);
        if (headline.Length == 0)
            return Degrade(baseline, "云端返回内容无法解析");

        // ② 数字核对：模型只被允许复述素材里的数字
        var allowed = CollectNumbers(baseline);
        var offenders = FindUnauthorizedNumbers(headline + "\n" + string.Join("\n", notes), allowed);
        if (offenders.Count > 0)
            return Degrade(baseline, "云端回答出现未授权数字（" + string.Join("、", offenders.Take(3)) + "），已整段丢弃");

        // ③ 通过核对：换措辞，保留全部数字、依据与动作
        var evidence = new List<string>(baseline.Evidence)
        {
            $"措辞由云端大模型（{_cfg.Model}）生成；全部数字取自本地仓储汇总，未由模型生成",
            $"数字核对：云端文本中的 {allowed.Count} 个数字全部来自【关键数据】素材，无编造"
        };

        return new AssistantReply
        {
            Question = baseline.Question,
            Intent = baseline.Intent,
            Confidence = baseline.Confidence,
            Headline = headline,
            Body = notes,
            Facts = baseline.Facts,
            Evidence = evidence,
            Actions = baseline.Actions,
            BackendName = Name,
            IsFallback = baseline.IsFallback
        };
    }

    /// <summary>降级：原样返回本地规则引擎的回答，但把"为什么降级"写清楚。</summary>
    private AssistantReply Degrade(AssistantReply baseline, string reason)
    {
        var headline = baseline.Headline.Length == 0
            ? baseline.Headline
            : "（云端不可用，已用本地规则引擎作答）" + baseline.Headline;

        var evidence = new List<string>(baseline.Evidence)
        {
            $"云端调用未生效：{reason}",
            "本次回答为本地规则引擎原文，数字与依据不受影响"
        };

        return new AssistantReply
        {
            Question = baseline.Question,
            Intent = baseline.Intent,
            Confidence = baseline.Confidence,
            Headline = headline,
            Body = baseline.Body,
            Facts = baseline.Facts,
            Evidence = evidence,
            Actions = baseline.Actions,
            BackendName = IsAvailable ? Name + "（已降级）" : "本地规则引擎（云端未启用）",
            IsFallback = true
        };
    }

    // ================= HTTP =================

    private string SendChat(string question, AssistantReply baseline)
        => SendChatCore(_http, _cfg, BuildSystemPrompt(), BuildUserPrompt(question, baseline));

    /// <summary>
    /// 发一次对话请求，返回模型正文（失败抛异常，由调用方统一降级）。
    /// 实现已抽到 <see cref="LlmHttp"/> 供结构化任务调用共用——此处保留同名薄封装，
    /// 避免出现两份 HTTP 实现导致"一处修了超时识别、另一处没修"。
    /// </summary>
    private static string SendChatCore(HttpClient http, AssistantLlmSettings cfg, string system, string user)
        => LlmHttp.Send(cfg, system, user, http);

    /// <summary>连通性自测：发一条最小请求，返回可用性与说明（供设置对话框调用）。</summary>
    public static (bool Ok, string Message) TestConnection(AssistantLlmSettings cfg, HttpClient? http = null)
        => LlmHttp.TestConnection(cfg, http);

    /// <summary>异常归类（与共用通道一致）。</summary>
    private static string ShortError(Exception ex) => LlmHttp.ShortError(ex);

    /// <summary>截断过长文本。</summary>
    private static string Shorten(string? s, int max) => LlmHttp.Shorten(s, max);

    // ================= 提示词 =================

    private static string BuildSystemPrompt() => string.Join("\n", new[]
    {
        "你是超级电容 MES 系统的问答助手，面向工厂管理者与现场主管。",
        "",
        "【绝对规则】",
        "1. 你只能使用用户消息里【关键数据】给出的数字。严禁编造、推算、四舍五入或引入任何未提供的数字。",
        "2. 如果【关键数据】里没有用户问的这一项，直接说「现有数据里没有这一项」，并指出可以去看哪个页面。",
        "3. 不要给出合格/不合格、达标/不达标之类的质量判定结论——判定归 SPC 规则与人工，你只做汇总与陈述。",
        "4. 不要输出 Markdown 标题、表格、代码块、表情符号，也不要用有序编号。",
        "",
        "【输出格式】严格遵守，共两段：",
        "结论：<一句话先给答案，不超过 50 字>",
        "- <补充要点，1 到 4 条，每条不超过 45 字>",
        "",
        "用工厂里的大白话说，不要客套，不要重复用户的问题。"
    });

    private static string BuildUserPrompt(string question, AssistantReply baseline)
    {
        var sb = new StringBuilder();
        sb.AppendLine("【管理者提问】");
        sb.AppendLine(question);
        sb.AppendLine();

        sb.AppendLine("【关键数据】（这是你唯一可以引用的数字来源）");
        if (baseline.Facts.Count == 0)
            sb.AppendLine("- （本次没有查到任何业务数据）");
        foreach (var f in baseline.Facts)
            sb.AppendLine("- " + f.Display);
        sb.AppendLine();

        sb.AppendLine("【系统识别】");
        sb.AppendLine("- 数据域：" + baseline.Intent);
        sb.AppendLine("- 匹配置信度：" + baseline.Confidence + "%");
        sb.AppendLine();

        sb.AppendLine("【格式提醒】只输出「结论：」一行，以及若干以「- 」开头的要点。不要输出其它内容。");
        return sb.ToString();
    }

    // ================= 返回解析与数字核对 =================

    /// <summary>
    /// 解析模型输出：以「结论」开头的行作为结论，以 - / • / · 开头的行作为要点，
    /// 其余非空行并入要点。解析失败（没有结论行）时把首行当结论兜底。
    /// </summary>
    internal static (string Headline, List<string> Notes) ParseAnswer(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        string headline = "";
        var notes = new List<string>();

        foreach (var line in lines)
        {
            var bare = line.TrimStart('#', '*', ' ').Trim();
            if (bare.StartsWith("结论", StringComparison.Ordinal))
            {
                var v = bare[(bare.IndexOf('：') >= 0 ? bare.IndexOf('：') + 1 : "结论".Length)..].Trim();
                v = v.TrimStart(':', '：', ' ').Trim();
                if (v.Length > 0) { headline = v; continue; }
                // 「结论：」后面是空的：这一行没有信息量，直接丢弃。
                // 不能让它落到下面的兜底分支去当结论——否则界面上会显示一行光秃秃的"结论："。
                continue;
            }

            if (bare.StartsWith("-", StringComparison.Ordinal)
                || bare.StartsWith("•", StringComparison.Ordinal)
                || bare.StartsWith("·", StringComparison.Ordinal)
                || bare.StartsWith("*", StringComparison.Ordinal))
            {
                var v = bare.TrimStart('-', '•', '·', '*', ' ').Trim();
                if (v.Length > 0) notes.Add(v);
                continue;
            }

            if (headline.Length == 0) headline = bare;
            else notes.Add(bare);
        }

        // 模型只给了要点、没给结论时，把第一条要点提上来当结论——
        // 否则这一整段会因为"没有结论行"被判为无法解析，白白丢掉一次可用的回答。
        if (headline.Length == 0 && notes.Count > 0)
        {
            headline = notes[0];
            notes.RemoveAt(0);
        }

        if (headline.Length > 200) headline = headline[..200];
        return (headline, notes.Take(6).ToList());
    }

    /// <summary>收集"被授权"的数字：本地规则回答里出现过的全部数字。</summary>
    internal static HashSet<string> CollectNumbers(AssistantReply baseline)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Feed(string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (var n in ExtractNumbers(s)) set.Add(n);
        }

        Feed(baseline.Headline);
        foreach (var b in baseline.Body) Feed(b);
        foreach (var f in baseline.Facts) { Feed(f.Label); Feed(f.Value); Feed(f.Note); }
        return set;
    }

    /// <summary>
    /// 找出模型文本里"不在授权集合内"的数字。
    /// 先剥掉行首的有序编号（"1. " / "2、"），避免把列表序号误判成编造的数字。
    /// </summary>
    internal static List<string> FindUnauthorizedNumbers(string text, HashSet<string> allowed)
    {
        var cleaned = Regex.Replace(text, @"(?m)^\s*\d+\s*[.、)）]\s*", "");
        var bad = new List<string>();
        foreach (var n in ExtractNumbers(cleaned))
            if (!allowed.Contains(n) && !bad.Contains(n))
                bad.Add(n);
        return bad;
    }

    /// <summary>抽取阿拉伯数字（先去掉千分位分隔符，避免 17,890 被拆成 17 和 890）。</summary>
    private static IEnumerable<string> ExtractNumbers(string text)
    {
        var t = text.Replace(",", "").Replace("，", "");
        foreach (Match m in Regex.Matches(t, @"\d+(?:\.\d+)?"))
        {
            var v = m.Value;
            if (v.Contains('.')) v = v.TrimEnd('0').TrimEnd('.');
            if (v.Length > 0) yield return v;
        }
    }
}

/// <summary>
/// 后端工厂：按配置决定挂"云端大模型"还是"本地规则引擎"。
/// 界面只需要调用 <see cref="CreateFromSettings"/>，不需要知道具体实现。
/// </summary>
public static class AssistantBackendFactory
{
    /// <summary>
    /// 读取配置并创建后端。配置齐全 → <see cref="LlmAssistant"/>；否则 → <see cref="RuleBasedAssistant"/>。
    /// 这就是界面上说的"接大模型只改一行"——那一行就在这里。
    /// </summary>
    public static IAssistantBackend CreateFromSettings(AssistantLlmSettings? settings = null)
    {
        var cfg = settings ?? AssistantLlmSettingsStore.Load();
        return cfg.CanCall ? new LlmAssistant(cfg) : new RuleBasedAssistant();
    }
}
