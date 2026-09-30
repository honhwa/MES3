namespace SuperCapMES.WinForms.Models;

/// <summary>现场异常状态常量：待确认 → 已确认 → 已闭环</summary>
public static class AbnormalStatus
{
    public const string Pending = "待确认";
    public const string Confirmed = "已确认";
    public const string Closed = "已闭环";

    /// <summary>全部状态（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { Pending, Confirmed, Closed };
}

/// <summary>异常动作类型常量（操作履历 Action）</summary>
public static class AbnormalAction
{
    public const string Register = "登记";
    public const string Confirm = "确认";
    public const string Close = "闭环";
}

/// <summary>异常大类常量与全部可选项</summary>
public static class AbnormalCategories
{
    public const string Production = "生产异常";
    public const string Equipment = "设备异常";
    public const string Process = "工艺异常";
    public const string Other = "其他异常";

    /// <summary>全部大类（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { Production, Equipment, Process, Other };
}

/// <summary>异常大类 → 细分项（随大类联动）</summary>
public static class AbnormalSubTypes
{
    /// <summary>大类→细分项映射；若某大类无细分，对应值为空数组，界面可放宽必填</summary>
    public static readonly Dictionary<string, string[]> Map = new()
    {
        [AbnormalCategories.Production] = new[] { "来料不良", "混料错料", "漏工序", "参数误设", "操作不规范", "标识异常" },
        [AbnormalCategories.Equipment] = new[] { "设备故障停机", "精度漂移", "报警频繁", "夹具异常", "传动异常" },
        [AbnormalCategories.Process] = new[] { "工艺参数超限", "标准执行偏差", "制程波动", "环境超标" },
        [AbnormalCategories.Other] = new[] { "物料供应异常", "工装治具异常", "其它" }
    };

    /// <summary>取某大类的细分项；无映射时返回空数组</summary>
    public static string[] Get(string category) =>
        Map.TryGetValue(category ?? "", out var list) ? list : Array.Empty<string>();
}

/// <summary>横展结论常量：是否需要其他线体横展</summary>
public static class AbnormalHorizon
{
    public const string NotNeeded = "不需要";
    public const string Needed = "需要";

    /// <summary>全部选项（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { NotNeeded, Needed };
}

/// <summary>产线/线体下拉选项（L1/L2 既有，L3 预留）</summary>
public static class LineOptions
{
    /// <summary>全部线体选项</summary>
    public static readonly string[] All = { "L1", "L2", "L3" };
}

/// <summary>
/// 现场异常登记单：编号 AB-yyyyMMdd-序号（序号全局递增、跨天自然延续）。
/// 状态流：待确认（登记完成） → 已确认（后续负责人核对） → 已闭环（根因/根本措施/横展结论齐全）。
/// </summary>
public class AbnormalRecord
{
    /// <summary>异常编号，形如 AB-20260907-001</summary>
    public string AbNo { get; set; } = "";

    /// <summary>发生时间</summary>
    public DateTime OccurTime { get; set; }

    /// <summary>发现人</summary>
    public string FoundBy { get; set; } = "";

    /// <summary>发生线体：L1 / L2 / L3</summary>
    public string Line { get; set; } = "";

    /// <summary>发生设备快照：设备编码</summary>
    public string EquipmentCode { get; set; } = "";

    /// <summary>发生设备快照：设备名称</summary>
    public string EquipmentName { get; set; } = "";

    /// <summary>异常大类：生产异常 / 设备异常 / 工艺异常 / 其他异常</summary>
    public string Category { get; set; } = AbnormalCategories.Production;

    /// <summary>异常类型细分（随大类联动）</summary>
    public string SubType { get; set; } = "";

    /// <summary>异常描述</summary>
    public string Description { get; set; } = "";

    /// <summary>临时措施</summary>
    public string TemporaryMeasure { get; set; } = "";

    /// <summary>后续负责人</summary>
    public string Owner { get; set; } = "";

    /// <summary>状态：待确认 / 已确认 / 已闭环</summary>
    public string Status { get; set; } = AbnormalStatus.Pending;

    /// <summary>登记人</summary>
    public string RegisterBy { get; set; } = "";

    /// <summary>来源类型：人工登记 / SPC预警 / 设备报警（默认「人工登记」，既有手工登记不变）</summary>
    public string SourceType { get; set; } = "人工登记";

    /// <summary>来源单号/描述（如预警号 SPC-... / 报警定位「设备 时间」），人工登记为空</summary>
    public string SourceRef { get; set; } = "";

    /// <summary>登记时间</summary>
    public DateTime RegisterTime { get; set; }

    /// <summary>确认人（未确认为空）</summary>
    public string ConfirmBy { get; set; } = "";

    /// <summary>确认时间（未确认为 null）</summary>
    public DateTime? ConfirmTime { get; set; }

    /// <summary>根本原因（闭环时填写）</summary>
    public string RootCause { get; set; } = "";

    /// <summary>根本措施（闭环时填写）</summary>
    public string RootMeasure { get; set; } = "";

    /// <summary>根因分析人（闭环操作人）</summary>
    public string AnalysisBy { get; set; } = "";

    /// <summary>根因分析/闭环提交时间（已闭环非 null）</summary>
    public DateTime? AnalysisTime { get; set; }

    /// <summary>是否需要其他线体横展：不需要 / 需要</summary>
    public string HorizonNeeded { get; set; } = AbnormalHorizon.NotNeeded;

    /// <summary>横展范围/计划（需要横展时填写）</summary>
    public string HorizonScope { get; set; } = "";

    /// <summary>闭环时间（已闭环非 null）</summary>
    public DateTime? ClosedTime { get; set; }

    /// <summary>设备展示文本（如"卷绕机(WIND-L1-01)"），供列表/概要直接展示</summary>
    public string EquipmentDesc =>
        string.IsNullOrWhiteSpace(EquipmentName)
            ? EquipmentCode
            : string.IsNullOrWhiteSpace(EquipmentCode)
                ? EquipmentName
                : $"{EquipmentName}({EquipmentCode})";
}
