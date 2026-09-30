namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 工单 ERP 报工状态常量：未报工 → 已报工。
/// 说明：本常量的用途与 WorkOrder.ErpReportStatus 字段同名但语义不同
/// （前者为可选值集合，后者为某工单当前状态值），二者分处「类型」与「实例成员」作用域，互不冲突。
/// </summary>
public static class ErpReportStatus
{
    /// <summary>未报工（默认）</summary>
    public const string NotReported = "未报工";

    /// <summary>已报工</summary>
    public const string Reported = "已报工";

    /// <summary>全部状态（顺序即下拉/展示顺序）</summary>
    public static readonly string[] All = { NotReported, Reported };
}
