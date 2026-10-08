using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SuperCapMES.WinForms.Models;

/// <summary>
/// AI 助手的意图常量。每个意图对应「一类问题 + 一组确定性数据来源」。
/// 当前由规则引擎识别（<see cref="RuleBasedAssistant"/>），将来换大模型只替换后端实现，意图口径不变。
/// </summary>
public static class AssistantIntent
{
    public const string Overview = "生产概览";
    public const string Alert = "预警待办";
    public const string Abnormal = "异常管理";
    public const string Hold = "保留管理";
    public const string Rework = "返工管理";
    public const string Complaint = "客户投诉";
    public const string Personnel = "人员资质";
    public const string Inventory = "库存物料";

    /// <summary>齐套 / 缺料：投产前的物料够不够（与「库存有多少」是两回事）</summary>
    public const string Material = "齐套缺料";
    public const string Equipment = "设备状态";
    public const string Spc = "SPC 参数";
    public const string Trace = "电芯追溯";
    public const string Guide = "操作导航";
    public const string Unknown = "未识别";

    /// <summary>全部意图（用于自测遍历与统计）</summary>
    public static readonly string[] All =
    {
        Overview, Alert, Abnormal, Hold, Rework, Complaint,
        Personnel, Inventory, Material, Equipment, Spc, Trace, Guide
    };

    /// <summary>意图的中文说明（可解释面板展示"系统是怎么理解这句话的"）</summary>
    public static string Describe(string intent) => intent switch
    {
        Overview => "问的是全局产量 / 良率 / 在制 / 设备运行情况",
        Alert => "问的是「现在有什么要处理的」——聚合待办",
        Abnormal => "问的是异常单的登记与闭环情况",
        Hold => "问的是不合格品隔离（保留）的执行与解保",
        Rework => "问的是返工单的流转与积压",
        Complaint => "问的是客户投诉 / 8D 的进度",
        Personnel => "问的是人员资质、能否上岗",
        Inventory => "问的是库存数量、效期、账实差异",
        Material => "问的是投产前物料够不够（齐套、缺料、能不能开工）",
        Equipment => "问的是设备状态、点检保养、备件",
        Spc => "问的是工艺参数是否受控、是否触发预警",
        Trace => "问的是某只电芯 / 批次 / 工单的全链追溯",
        Guide => "问的是「这个操作去哪做、点哪个按钮」",
        _ => "未能匹配到已有数据域"
    };
}

/// <summary>
/// 助手给出的「建议动作」：一句话按钮 + 目标页面（+ 可选子页/按钮名）。
/// 点击后由界面层用 <c>ActivatePage</c> 跳转，保证与用户手册的跳转口径一致。
/// </summary>
public sealed class AssistantAction
{
    /// <summary>按钮文字（用户看到的动作）</summary>
    public string Label { get; init; } = "";

    /// <summary>目标二级页面标题（须能在导航中定位到）</summary>
    public string TargetPage { get; init; } = "";

    /// <summary>目标子页签（该页面内还有子页签时用，可为空）</summary>
    public string TargetSubPage { get; init; } = "";

    /// <summary>目标按钮名（进入页面后该点哪个按钮，可为空）</summary>
    public string TargetButton { get; init; } = "";

    /// <summary>动作说明（工具提示）</summary>
    public string Hint { get; init; } = "";

    /// <summary>按钮上展示的完整说明：页面 › 子页 › 按钮</summary>
    public string WhereText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(TargetPage)) parts.Add(TargetPage);
            if (!string.IsNullOrWhiteSpace(TargetSubPage)) parts.Add(TargetSubPage);
            if (!string.IsNullOrWhiteSpace(TargetButton)) parts.Add(TargetButton);
            return parts.Count == 0 ? "—" : string.Join(" › ", parts);
        }
    }
}

/// <summary>回答里的一个关键数据点（可追溯：值从哪来）</summary>
public sealed class AssistantFact
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Note { get; init; } = "";

    /// <summary>状态级别：正常 / 关注 / 预警 / 信息（决定界面配色）</summary>
    public string Level { get; init; } = FactLevel.Info;

    public string Display => string.IsNullOrWhiteSpace(Note) ? $"{Label}：{Value}" : $"{Label}：{Value}（{Note}）";
}

/// <summary>关键数据的状态级别</summary>
public static class FactLevel
{
    public const string Normal = "正常";
    public const string Watch = "关注";
    public const string Warn = "预警";
    public const string Info = "信息";
}

/// <summary>
/// 一次问答的完整回答。刻意把「结论 / 正文 / 关键数据 / 数据依据 / 建议动作」分开，
/// 目的是让界面能把"这句话是基于哪几个数、哪些表算出来的"直接摆给用户看——
/// 生产过程问答必须可追溯，不允许只给一个没法追责的结论。
/// </summary>
/// <summary>
/// 一轮对话（供多轮追问用）。
/// 只带<b>问题 + 结论 + 意图</b>，不带全文——历史的作用是让模型知道"上一轮在聊什么"，
/// 带上全文既费 token 又会让模型被旧细节干扰。
/// </summary>
public class ChatTurn
{
    public string Question { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Intent { get; set; } = "";

    public ChatTurn() { }

    public ChatTurn(string question, string headline, string intent)
    {
        Question = question;
        Headline = headline;
        Intent = intent;
    }

    public ChatTurn(AssistantReply r) => FromReply(r);

    public void FromReply(AssistantReply r)
    {
        Question = r.Question;
        Headline = r.Headline;
        Intent = r.Intent;
    }

    /// <summary>一句话摘要，用于拼进提示词</summary>
    public string Compact => $"问：{Question}　→ [{Intent}] {Headline}";
}

/// <summary>
/// 对话上下文（环形，最多保留 <see cref="MaxRounds"/> 轮）。
/// 为什么限轮数：历史全量进提示词会让 token 成本随轮次线性上升，
/// 而且超过 3~5 轮后模型基本已经够用了，再多只是噪声。
/// </summary>
public class ConversationHistory
{
    /// <summary>最多保留的轮数</summary>
    public const int MaxRounds = 4;

    private readonly List<ChatTurn> _turns = new();

    public IReadOnlyList<ChatTurn> Turns => _turns;

    public int Count => _turns.Count;

    /// <summary>最近一轮（没有则 null）</summary>
    public ChatTurn? Last => _turns.Count > 0 ? _turns[^1] : null;

    /// <summary>记录一轮对话（超上限自动丢最旧的）</summary>
    public void Add(AssistantReply reply)
    {
        if (string.IsNullOrWhiteSpace(reply?.Question)) return;
        _turns.Add(new ChatTurn(reply));
        while (_turns.Count > MaxRounds) _turns.RemoveAt(0);
    }

    public void Clear() => _turns.Clear();

    /// <summary>拼成提示词里的【上文】段落（不含当前轮）</summary>
    public string ToPromptText()
        => _turns.Count == 0
            ? "（无，这是第一次提问）"
            : string.Join("\n", _turns.Select((t, i) => $"  第{i + 1}轮 {t.Compact}"));
}

public sealed class AssistantReply
{
    /// <summary>用户原话</summary>
    public string Question { get; init; } = "";

    /// <summary>识别到的意图（见 <see cref="AssistantIntent"/>）</summary>
    public string Intent { get; init; } = AssistantIntent.Unknown;

    /// <summary>意图置信度 0-100（规则引擎给出的是"匹配强度"，不是模型概率，界面上已标注）</summary>
    public int Confidence { get; init; }

    /// <summary>结论：一句话先给答案</summary>
    public string Headline { get; init; } = "";

    /// <summary>正文：分条说明</summary>
    public List<string> Body { get; init; } = new();

    /// <summary>关键数据</summary>
    public List<AssistantFact> Facts { get; init; } = new();

    /// <summary>数据依据：数据取自哪个仓储集合 / 哪个汇总方法</summary>
    public List<string> Evidence { get; init; } = new();

    /// <summary>建议动作（可一键跳转）</summary>
    public List<AssistantAction> Actions { get; init; } = new();

    /// <summary>后端标识（用于界面上标明"当前回答由谁产生"）</summary>
    public string BackendName { get; init; } = "";

    /// <summary>是否未命中明确意图（走了兜底/导航分支）</summary>
    public bool IsFallback { get; init; }

    /// <summary>置信度分档文案。刻意不写"准确率"，避免把规则匹配强度说成模型概率。</summary>
    public string ConfidenceText => Confidence >= 80 ? "高" : Confidence >= 55 ? "中" : Confidence > 0 ? "低" : "—";

    /// <summary>置信度说明（界面可解释面板用）</summary>
    public string ConfidenceExplain =>
        Confidence >= 80 ? "关键词与数据域强匹配"
        : Confidence >= 55 ? "关键词命中，可能同时属于其他数据域"
        : Confidence > 0 ? "弱匹配，建议补充关键词（如 SN / 单号 / 工号）"
        : "未识别到有效意图";

    /// <summary>空回答（用于输入为空等场景）</summary>
    public static AssistantReply Empty(string question, string backendName) => new()
    {
        Question = question,
        BackendName = backendName
    };
}

/// <summary>
/// 从提问里抽取的实体。全部为纯文本抽取（正则），**是否真实存在由引擎去仓储核对**，
/// 抽取命中不等于业务存在——这样界面能区分"你没说"和"你说了但查无此物"。
/// </summary>
public sealed class AssistantEntities
{
    public string? CellSn { get; set; }
    public string? ModuleSn { get; set; }
    public string? WoNo { get; set; }
    public string? HoldNo { get; set; }
    public string? LotNo { get; set; }
    public string? EmpNo { get; set; }
    public string? EquipmentCode { get; set; }
    public string? DocNo { get; set; }      // 异常单 / 客诉单 / 返工单 / 首件单 等通用单号
    public string? ProcessName { get; set; } // 工序名（用于上岗资质校验）

    /// <summary>是否抽到了任意实体</summary>
    public bool Any => CellSn is not null || ModuleSn is not null || WoNo is not null || HoldNo is not null
        || LotNo is not null || EmpNo is not null || EquipmentCode is not null || DocNo is not null;

    /// <summary>实体的中文清单（供回答里回显"我从你的话里认出了这些"）</summary>
    public List<string> Describe()
    {
        var list = new List<string>();
        if (CellSn is not null) list.Add($"电芯 SN = {CellSn}");
        if (ModuleSn is not null) list.Add($"模组 SN = {ModuleSn}");
        if (WoNo is not null) list.Add($"工单号 = {WoNo}");
        if (HoldNo is not null) list.Add($"保留单 = {HoldNo}");
        if (LotNo is not null) list.Add($"批次 = {LotNo}");
        if (EmpNo is not null) list.Add($"工号 = {EmpNo}");
        if (EquipmentCode is not null) list.Add($"设备 = {EquipmentCode}");
        if (DocNo is not null) list.Add($"单号 = {DocNo}");
        if (ProcessName is not null) list.Add($"工序 = {ProcessName}");
        return list;
    }
}

/// <summary>
/// 实体抽取器：把用户口语里夹带的编号认出来。
/// 编码规则来自种子数据的实际编号格式（SC-/SM- 电芯模组、WO- 工单、HLD- 保留、CMP- 客诉、RW- 返工、E 工号…）。
/// </summary>
public static class AssistantEntityParser
{
    // 顺序即优先级：先长后短，避免 SM- 被 SC- 的规则误吃。
    // 边界一律用 (?<![A-Za-z0-9]) / (?![A-Za-z0-9]) 而不是 \b ——
    // 提问里编号后面通常紧跟中文（如「SC-...00001 这只电芯」），而 .NET 的 \w 包含中日韩字符，
    // 中文与数字之间**不存在**单词边界，用 \b 会整体匹配失败（自测第一轮就栽在这）。
    private static readonly (Regex Rx, string Kind)[] Rules =
    {
        (new Regex(@"(?<![A-Za-z0-9])SM-[A-Za-z0-9]{4,10}-[A-Za-z0-9]{1,4}-\d{2,6}(?![A-Za-z0-9])", RegexOptions.Compiled), "MODULE"),
        (new Regex(@"(?<![A-Za-z0-9])SC-[A-Za-z0-9]{4,10}-[A-Za-z0-9]{1,4}-\d{2,6}(?![A-Za-z0-9])", RegexOptions.Compiled), "CELL"),
        (new Regex(@"(?<![A-Za-z0-9])WO-\d{6,8}-\d{2,4}(?![A-Za-z0-9])", RegexOptions.Compiled), "WO"),
        (new Regex(@"(?<![A-Za-z0-9])HLD-\d{6,8}-\d{2,4}(?![A-Za-z0-9])", RegexOptions.Compiled), "HOLD"),
        (new Regex(@"(?<![A-Za-z0-9])CMP-\d{6,8}-\d{2,4}(?![A-Za-z0-9])", RegexOptions.Compiled), "DOC"),
        (new Regex(@"(?<![A-Za-z0-9])RW-\d{6,8}-\d{2,4}(?![A-Za-z0-9])", RegexOptions.Compiled), "DOC"),
        (new Regex(@"(?<![A-Za-z0-9])FAI-\d{6,8}-\d{2,4}(?![A-Za-z0-9])", RegexOptions.Compiled), "DOC"),
        (new Regex(@"(?<![A-Za-z0-9])AB-\d{6,8}-\d{2,4}(?![A-Za-z0-9])", RegexOptions.Compiled), "DOC"),
        (new Regex(@"(?<![A-Za-z0-9])SPC-\d{6,8}-\d{2,4}(?![A-Za-z0-9])", RegexOptions.Compiled), "DOC"),
        (new Regex(@"(?<![A-Za-z0-9])TPM-\d{1,6}(?![A-Za-z0-9])", RegexOptions.Compiled), "DOC"),
        (new Regex(@"(?<![A-Za-z0-9])(?:PC|EL|SP|CC)-\d{4,8}-[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*(?![A-Za-z0-9-])", RegexOptions.Compiled), "LOT"),
        (new Regex(@"(?<![A-Za-z0-9])E\d{4}(?![0-9])", RegexOptions.Compiled), "EMP"),
        (new Regex(@"(?<![A-Za-z0-9])[A-Z]{3,5}-[A-Z0-9]{1,3}-\d{2}(?![0-9])", RegexOptions.Compiled), "EQUIP"),
    };

    /// <summary>抽取提问中的业务编号。纯函数，不访问仓储。</summary>
    public static AssistantEntities Parse(string? question)
    {
        var e = new AssistantEntities();
        if (string.IsNullOrWhiteSpace(question)) return e;

        var text = Normalize(question);

        foreach (var (rx, kind) in Rules)
        {
            foreach (Match m in rx.Matches(text))
            {
                var v = m.Value.ToUpperInvariant();
                switch (kind)
                {
                    case "MODULE": e.ModuleSn ??= v; break;
                    case "CELL": e.CellSn ??= v; break;
                    case "WO": e.WoNo ??= v; break;
                    case "HOLD": e.HoldNo ??= v; break;
                    case "DOC": e.DocNo ??= v; break;
                    case "LOT": e.LotNo ??= v; break;
                    case "EMP": e.EmpNo ??= v; break;
                    case "EQUIP": e.EquipmentCode ??= v; break;
                }
            }
        }

        return e;
    }

    /// <summary>归一化：全角转半角 + 去空白 + 大写（编号一律按大写比对）。与手册搜索共用同一套归一化口径。</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var buf = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c >= '\uFF01' && c <= '\uFF5E') c = (char)(c - 0xFEE0);
            buf[i] = c;
        }
        var s = new string(buf);
        s = s.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
        return s.ToUpperInvariant();
    }
}

/// <summary>
/// 助手后端接口。**这是"换模型"的唯一接缝**：
/// 现在挂 <see cref="RuleBasedAssistant"/>（纯规则 + 现成仓储汇总，不调外部模型）；
/// 接大模型时新写一个实现（如 <see cref="LlmAssistant"/>），
/// 在启动处 <c>AssistantEngine.Backend = ...</c> 一行替换即可，界面与页面零改动。
/// </summary>
public interface IAssistantBackend
{
    /// <summary>后端标识（界面右下角展示，便于分辨回答来源）</summary>
    string Name { get; }

    /// <summary>后端说明（可解释面板展示实现方式与数据边界）</summary>
    string Describe { get; }

    /// <summary>
    /// 回答一次提问。实现须保证：只读仓储，不产生任何副作用。
    /// </summary>
    /// <param name="history">
    /// 上文对话（可为空）。2026-10-08 起新增——之前界面显示"第 N 轮"却从不把历史传给后端，
    /// 追问接不住，属"暗示了不存在的能力"。加的是<b>可选参数</b>，既有实现与调用都不受影响。
    /// </param>
    AssistantReply Ask(string question, ConversationHistory? history = null);
}

/// <summary>
/// 可选扩展接口：标记「回答要出网（调用云端大模型）」的后端。
///
/// 单独抽一个接口、而不是往 <see cref="IAssistantBackend"/> 里加属性，是为了**不动已冻结的接口口径**：
/// 规则引擎不实现它，界面据此决定要不要把提问放到后台线程——出网调用是秒级阻塞，
/// 直接跑在 UI 线程上会把界面冻住。
/// </summary>
public interface IRemoteAssistantBackend : IAssistantBackend
{
    /// <summary>远端当前是否可用（未启用 / 未填 Key 时为 false，此时应自动退回本地取数）</summary>
    bool IsAvailable { get; }

    /// <summary>远端目标（如 api.deepseek.com ｜ deepseek-chat），供界面标注"数据发往哪里"</summary>
    string Endpoint { get; }
}

/// <summary>
/// 助手门面：界面只与门面交互，不直接依赖具体后端。
/// 后端可替换（<see cref="Backend"/> 可写），因此"模型可替换"是有代码依据的，不是口头承诺。
/// </summary>
public static class AssistantEngine
{
    private static IAssistantBackend _backend = new RuleBasedAssistant();

    /// <summary>当前后端。替换后端只改这一处，页面与模型不动。</summary>
    public static IAssistantBackend Backend
    {
        get => _backend;
        set => _backend = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>当前后端标识</summary>
    public static string BackendName => _backend.Name;

    /// <summary>当前后端说明</summary>
    public static string BackendDescribe => _backend.Describe;

    /// <summary>当前后端是否走网络（界面据此决定同步提问还是异步提问）</summary>
    public static bool IsRemote => _backend is IRemoteAssistantBackend { IsAvailable: true };

    /// <summary>当前远端后端（非远端或不可用时返回 null）</summary>
    public static IRemoteAssistantBackend? Remote => _backend as IRemoteAssistantBackend;

    /// <summary>问一次。空提问直接返回空回答，不进入后端。</summary>
    public static AssistantReply Ask(string? question, ConversationHistory? history = null)
    {
        var q = (question ?? "").Trim();
        if (q.Length == 0) return AssistantReply.Empty("", _backend.Name);
        try
        {
            return _backend.Ask(q, history);
        }
        catch (Exception ex)
        {
            // 问答失败不能让整页崩掉：返回一条带错误说明的回答，界面照常渲染
            return new AssistantReply
            {
                Question = q,
                Intent = AssistantIntent.Unknown,
                Confidence = 0,
                Headline = "这次没能得出答案（内部异常）。",
                Body = new List<string> { "可换一种说法再问一次；若反复出现，请记录该提问内容反馈开发。" },
                Evidence = new List<string> { "异常信息：" + ex.Message },
                BackendName = _backend.Name,
                IsFallback = true
            };
        }
    }

    /// <summary>推荐提问（点一下即问）：按业务覆盖面挑选，覆盖全部 12 类意图。</summary>
    public static readonly string[] SampleQuestions =
    {
        "现在产线情况怎么样",
        "今天有什么要处理的",
        "有哪些异常还没闭环",
        "现在有多少不合格品被保留了",
        "返工还剩多少没做完",
        "客户投诉处理得怎么样了",
        "张伟能不能开卷绕机",
        "库存有没有快过期的料",
        "现在缺料吗，能开工吗",
        "设备现在都什么状态",
        "工艺参数有没有失控",
        "SC-A260812-L1-00001 这只电芯怎么回事",
        "我要登记不合格品，去哪操作"
    };
}
