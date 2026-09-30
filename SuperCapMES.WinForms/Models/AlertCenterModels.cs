namespace SuperCapMES.WinForms.Models;

/// <summary>预警严重度常量</summary>
public static class AlertSeverity
{
    public const string High = "高";
    public const string Medium = "中";
    public const string Low = "低";

    public static readonly string[] All = { High, Medium, Low };
}

/// <summary>预警来源模块常量（预警中心按来源分组）</summary>
public static class AlertSource
{
    public const string Spc = "SPC 预警";
    public const string Abnormal = "异常未闭环";
    public const string Hold = "保留超期";
    public const string Tpm = "点检保养";
    public const string Expiry = "库存效期";
    public const string Feeding = "上料防错";
    public const string Fai = "首件检验";
    public const string Rework = "返工积压";
    public const string Spare = "备件库存";
    public const string Shipment = "出货绑定";

    public static readonly string[] All =
    {
        Spc, Abnormal, Hold, Tpm, Expiry, Feeding, Fai, Rework, Spare, Shipment
    };
}

/// <summary>
/// 预警中心条目：统一承载各模块的「待处理事项」。
/// 各模块把自己的待办事项投影成这个结构，预警中心只负责聚合展示与跳转。
/// </summary>
public class AlertItem
{
    /// <summary>来源模块（见 <see cref="AlertSource"/>）</summary>
    public string Source { get; init; } = "";

    /// <summary>严重度：高 / 中 / 低</summary>
    public string Severity { get; init; } = AlertSeverity.Medium;

    /// <summary>一句话标题</summary>
    public string Title { get; init; } = "";

    /// <summary>详情（值 / 对象 / 时间等）</summary>
    public string Detail { get; init; } = "";

    /// <summary>触发时间（用于排序与展示）</summary>
    public DateTime OccurTime { get; init; }

    /// <summary>关联对象键（单号 / SN / 批次 / 计划号），用于跳转后定位</summary>
    public string TargetKey { get; init; } = "";

    /// <summary>列表展示：关联对象</summary>
    public string TargetText => string.IsNullOrWhiteSpace(TargetKey) ? "—" : TargetKey;

    /// <summary>列表展示：触发时间</summary>
    public string OccurText => OccurTime == default ? "—" : OccurTime.ToString("MM-dd HH:mm");

    /// <summary>排序用：高=0 中=1 低=2</summary>
    public int SeverityOrder => Severity switch
    {
        AlertSeverity.High => 0,
        AlertSeverity.Medium => 1,
        _ => 2
    };
}
