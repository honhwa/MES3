namespace SuperCapMES.WinForms.Models;

/// <summary>
/// WMS 仓储单据：单号形如 PI-20260907-001（前缀-日期-序号，序号全局递增、跨天自然延续）。
/// 状态流转：待过账 → 已过账（已过账只读、不可重复过账）。
/// </summary>
public class WmsOrder
{
    /// <summary>单据号，形如 PI-20260907-001</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>单据类型：采购入库 / 现场领用 / 现场入库 / 现场退货 / 采购退货 / 物流出库 / 客户退货</summary>
    public string Type { get; set; } = WmsOrderTypes.PurchaseInbound;

    /// <summary>状态：待过账 / 已过账</summary>
    public string Status { get; set; } = WmsOrderStatus.Pending;

    /// <summary>往来对象：供应商 / 客户 / 领用线体 / 来源线体 / 退货线体（语义随类型变化）</summary>
    public string Partner { get; set; } = "";

    /// <summary>参考号（物流出库时=物流单号，其余可空）</summary>
    public string RefNo { get; set; } = "";

    /// <summary>退货原因（退货类单据填写）</summary>
    public string Reason { get; set; } = "";

    /// <summary>经手人</summary>
    public string Operator { get; set; } = "";

    /// <summary>备注</summary>
    public string Remark { get; set; } = "";

    /// <summary>创建时间</summary>
    public DateTime CreateTime { get; set; }

    /// <summary>过账时间（未过账为 null）</summary>
    public DateTime? PostTime { get; set; }

    /// <summary>过账人（未过账为空）</summary>
    public string PostBy { get; set; } = "";

    /// <summary>物料行数：由仓储在查询时按明细填充，便于网格直接展示</summary>
    public int LineCount { get; set; }

    /// <summary>总数量：由仓储在查询时按明细汇总填充</summary>
    public double TotalQty { get; set; }

    /// <summary>库存方向：入库 / 出库（只读派生，供列表展示）</summary>
    public string DirectionDesc => WmsOrderTypes.IsInbound(Type) ? "入库" : "出库";

    /// <summary>往来对象显示名（供应商 / 客户 / 领用线体 / 来源线体 / 退货线体）</summary>
    public string PartnerLabel => WmsOrderTypes.PartnerLabel(Type);
}
