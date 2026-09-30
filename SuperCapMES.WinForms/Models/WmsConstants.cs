namespace SuperCapMES.WinForms.Models;

/// <summary>WMS 物料类别常量</summary>
public static class WmsCategories
{
    public const string Raw = "原料";
    public const string Aux = "辅料";
    public const string Finished = "成品";

    /// <summary>全部类别（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { Raw, Aux, Finished };
}

/// <summary>WMS 单据状态常量：待过账 → 已过账</summary>
public static class WmsOrderStatus
{
    public const string Pending = "待过账";
    public const string Posted = "已过账";

    /// <summary>全部状态（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { Pending, Posted };
}

/// <summary>WMS 操作动作常量（操作履历 Action）</summary>
public static class WmsAction
{
    public const string Create = "创建";
    public const string Inspect = "入库检";
    public const string Post = "过账";
}

/// <summary>WMS 库位常量</summary>
public static class WmsLocations
{
    public const string RawA = "原料库-A区";
    public const string FinishedB = "成品库-B区";
    public const string ReturnPending = "退货待处理区";
    public const string PendingInspection = "待检区";

    /// <summary>全部可选库位（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { RawA, FinishedB, ReturnPending, PendingInspection };
}

/// <summary>
/// WMS 单据类型常量及派生规则：
/// 采购入库(PI,+) / 现场领用(SL,-) / 现场入库(SI,+) / 现场退货(SR,+) /
/// 采购退货(PR,-) / 物流出库(LO,-) / 客户退货(CR,+)。
/// </summary>
public static class WmsOrderTypes
{
    public const string PurchaseInbound = "采购入库";
    public const string SiteIssue = "现场领用";
    public const string SiteInbound = "现场入库";
    public const string SiteReturn = "现场退货";
    public const string PurchaseReturn = "采购退货";
    public const string LogisticsOutbound = "物流出库";
    public const string CustomerReturn = "客户退货";

    /// <summary>全部单据类型（顺序即下拉展示顺序）</summary>
    public static readonly string[] All =
    {
        PurchaseInbound,
        SiteIssue,
        SiteInbound,
        SiteReturn,
        PurchaseReturn,
        LogisticsOutbound,
        CustomerReturn
    };

    /// <summary>库存方向判定：true=入库(+) / false=出库(-)</summary>
    public static bool IsInbound(string type) => type switch
    {
        PurchaseInbound => true,
        SiteInbound => true,
        SiteReturn => true,
        CustomerReturn => true,
        SiteIssue => false,
        PurchaseReturn => false,
        LogisticsOutbound => false,
        _ => false
    };

    /// <summary>单据号前缀</summary>
    public static string Prefix(string type) => type switch
    {
        PurchaseInbound => "PI",
        SiteIssue => "SL",
        SiteInbound => "SI",
        SiteReturn => "SR",
        PurchaseReturn => "PR",
        LogisticsOutbound => "LO",
        CustomerReturn => "CR",
        _ => "WM"
    };

    /// <summary>往来对象标签：随类型变化（供应商 / 客户 / 领用线体 / 来源线体 / 退货线体）</summary>
    public static string PartnerLabel(string type) => type switch
    {
        PurchaseInbound => "供应商",
        PurchaseReturn => "供应商",
        SiteIssue => "领用线体",
        SiteInbound => "来源线体",
        SiteReturn => "退货线体",
        LogisticsOutbound => "客户",
        CustomerReturn => "客户",
        _ => "往来对象"
    };

    /// <summary>是否采购入库（唯一支持入库检的类型）</summary>
    public static bool SupportsInspection(string type) => type == PurchaseInbound;

    /// <summary>是否退货类单据（需填退货原因）：现场退货 / 采购退货 / 客户退货</summary>
    public static bool RequiresReturnReason(string type) =>
        type is SiteReturn or PurchaseReturn or CustomerReturn;

    /// <summary>是否需填物流单号（仅物流出库）</summary>
    public static bool RequiresRefNo(string type) => type == LogisticsOutbound;
}

/// <summary>
/// 入库检默认项目模板：提供标准值示例，便于演示自动判定（InspectionJudge）。
/// 生成的行 MeasuredValue / Result 均为空，等待现场填写。
/// </summary>
public static class WmsInspectionTemplate
{
    /// <summary>默认检验项目（项目名 + 标准值）</summary>
    public static readonly (string Item, string Standard)[] Items =
    {
        ("外观检查", "目视无异常"),
        ("包装标识", "标识齐全清晰"),
        ("尺寸(厚度)", "3.20±0.10 mm"),
        ("容量", "≥95 F"),
        ("ESR", "≤25 mΩ")
    };

    /// <summary>生成默认模板空行副本（每次调用返回全新实例，互不影响）</summary>
    public static List<InspectionRow> CreateRows()
    {
        var rows = new List<InspectionRow>(Items.Length);
        foreach (var (item, standard) in Items)
        {
            rows.Add(new InspectionRow
            {
                ItemName = item,
                StandardValue = standard,
                MeasuredValue = "",
                Result = "",
                Inspector = ""
            });
        }
        return rows;
    }
}
