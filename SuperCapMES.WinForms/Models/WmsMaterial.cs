namespace SuperCapMES.WinForms.Models;

/// <summary>
/// WMS 物料主数据：WMS 模块独立的物料台账（勿与 MaterialLot 混用）。
/// 唯一键 = Code。
/// </summary>
public class WmsMaterial
{
    /// <summary>物料编码，形如 M-ACT-CAR-01</summary>
    public string Code { get; set; } = "";

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>规格型号</summary>
    public string Spec { get; set; } = "";

    /// <summary>计量单位（kg / L / m² / 只 等）</summary>
    public string Unit { get; set; } = "";

    /// <summary>物料类别：原料 / 辅料 / 成品</summary>
    public string Category { get; set; } = WmsCategories.Raw;

    /// <summary>安全库存（低于该值提示补货，仅作展示/参考）</summary>
    public double SafetyStock { get; set; }

    /// <summary>
    /// 单次采购申请上限（数量校验用）。0 = 不限。
    /// 为什么需要它：<b>可用库存小本身不是超量理由</b>（采购正是为了补缺口），
    /// 真正要拦的是"买多了"——申请量远超需求、或一次吞掉远超额度的量。
    /// 按<b>同一物料</b>的计量单位设定，0 表示不设限。
    /// </summary>
    public double MaxPurchaseQty { get; set; }

    /// <summary>展示文本（如"活性炭电极材料(M-ACT-CAR-01)"），供下拉/列表直接展示</summary>
    public string DisplayDesc =>
        string.IsNullOrWhiteSpace(MaterialName) ? Code : $"{MaterialName}({Code})";
}
