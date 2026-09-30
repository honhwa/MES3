namespace SuperCapMES.WinForms.Models;

/// <summary>SPC 预警类型常量</summary>
public static class SpcAlertType
{
    /// <summary>判异（Nelson 简化版判异规则命中）</summary>
    public const string RuleViolation = "判异";

    /// <summary>能力不足（Cpk 偏低）</summary>
    public const string Incapable = "能力不足";

    /// <summary>超规格（超出 USL / LSL）</summary>
    public const string OutOfSpec = "超规格";

    /// <summary>全部类型（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { RuleViolation, Incapable, OutOfSpec };
}

/// <summary>SPC 预警严重度常量</summary>
public static class SpcSeverity
{
    public const string High = "高";
    public const string Medium = "中";
    public const string Low = "低";

    /// <summary>全部严重度（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { High, Medium, Low };
}

/// <summary>SPC 预警状态常量：待处理 → 已确认 → 已关闭</summary>
public static class SpcAlertStatus
{
    public const string Pending = "待处理";
    public const string Acked = "已确认";
    public const string Closed = "已关闭";

    /// <summary>全部状态（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { Pending, Acked, Closed };
}

/// <summary>
/// SPC 预警记录：由 <c>MesDatabase.RefreshSpcAlerts</c> 依据控制图重算生成（去重后仅新增未见预警）。
/// 单号形如 SPC-{yyyyMMdd}-{seq:000}，序号全局递增、跨天自然延续。
/// </summary>
public class SpcAlert
{
    /// <summary>预警单号，形如 SPC-20260914-001</summary>
    public string AlertNo { get; set; } = "";

    /// <summary>监控项编号</summary>
    public string ItemId { get; set; } = "";

    /// <summary>监控项名称快照</summary>
    public string ItemName { get; set; } = "";

    /// <summary>预警类型：判异 / 能力不足 / 超规格</summary>
    public string AlertType { get; set; } = SpcAlertType.RuleViolation;

    /// <summary>规则名（如「单点超出控制限」「连续 6 点递增（或递减）」「Cpk&lt;1.0」）</summary>
    public string RuleName { get; set; } = "";

    /// <summary>严重度：高 / 中 / 低</summary>
    public string Severity { get; set; } = SpcSeverity.Medium;

    /// <summary>触发点数值</summary>
    public double Value { get; set; }

    /// <summary>中心线 CL</summary>
    public double CenterLine { get; set; }

    /// <summary>控制上限 UCL</summary>
    public double Ucl { get; set; }

    /// <summary>控制下限 LCL</summary>
    public double Lcl { get; set; }

    /// <summary>规格上限（无则 NaN）</summary>
    public double Usl { get; set; } = double.NaN;

    /// <summary>规格下限（无则 NaN）</summary>
    public double Lsl { get; set; } = double.NaN;

    /// <summary>判定所用规格版本号（0 = 无版本信息）</summary>
    public int Version { get; set; }

    /// <summary>判定所用规格版本文本，如 "V2"（无版本信息时为空串）</summary>
    public string VersionText { get; set; } = "";

    /// <summary>触发样本时间</summary>
    public DateTime SampleTime { get; set; }

    /// <summary>触发样本设备编码</summary>
    public string Equipment { get; set; } = "";

    /// <summary>触发样本电芯 SN（可空）</summary>
    public string CellSn { get; set; } = "";

    /// <summary>状态：待处理 / 已确认 / 已关闭</summary>
    public string Status { get; set; } = SpcAlertStatus.Pending;

    /// <summary>处理人（确认/关闭时写入）</summary>
    public string Handler { get; set; } = "";

    /// <summary>处理时间（确认/关闭时写入）</summary>
    public DateTime? HandleTime { get; set; }

    /// <summary>处理措施（关闭时必填）</summary>
    public string Action { get; set; } = "";

    /// <summary>备注</summary>
    public string Remark { get; set; } = "";

    /// <summary>已联动生成的异常单号（空表示尚未转单；转单后写回，防止重复转）</summary>
    public string LinkedAbnormalNo { get; set; } = "";
}
