namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 出货绑定状态常量：有效（在绑定台账中）/ 已解绑（一芯一单，解绑后可重新绑定）。
/// </summary>
public static class WmsShipmentStatus
{
    public const string Active = "有效";
    public const string Unbound = "已解绑";

    /// <summary>全部状态（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { Active, Unbound };
}

/// <summary>
/// 出货绑定明细（物流出库单 ↔ 具体电芯 SN 的台账）：
/// 补齐质量追溯链的最后一公里——把"发出去了哪些电芯"记清楚。
/// 绑定不改变库存（库存由过账决定，二者解耦）；仅记录绑定关系与快照。
/// </summary>
public class WmsShipmentItem
{
    /// <summary>所属物流出库单号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>被绑定的电芯 SN</summary>
    public string CellSn { get; set; } = "";

    /// <summary>电芯型号快照（绑定瞬间从 CellSn 取）</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>
    /// 出货时电芯状态快照（绑定瞬间从 CellSn 取）。
    /// 只有 <see cref="CellSnStatus"/> 中「合格 / 已入库」的电芯才允许绑定，因此该值应为可出货状态；
    /// 保留快照用于事后追溯（证明"出货时该芯状态为合格"）。
    /// </summary>
    public string CellStatus { get; set; } = "";

    /// <summary>极片批号快照</summary>
    public string ElectrodeLot { get; set; } = "";

    /// <summary>电解液批号快照</summary>
    public string ElectrolyteLot { get; set; } = "";

    /// <summary>所属模组 SN 快照（空表示未组模组）</summary>
    public string ModuleSn { get; set; } = "";

    /// <summary>客户（往来对象）快照，取自出货单 Partner</summary>
    public string Customer { get; set; } = "";

    /// <summary>成品批次（取自出货单明细中匹配型号的 LotNo，可空）</summary>
    public string WarehouseLot { get; set; } = "";

    /// <summary>绑定时间</summary>
    public DateTime BindTime { get; set; }

    /// <summary>绑定操作人</summary>
    public string BindOperator { get; set; } = "";

    /// <summary>状态：有效 / 已解绑</summary>
    public string Status { get; set; } = WmsShipmentStatus.Active;

    /// <summary>解绑时间（未解绑为 null）</summary>
    public DateTime? UnbindTime { get; set; }

    /// <summary>解绑操作人（未解绑为空）</summary>
    public string UnbindOperator { get; set; } = "";

    /// <summary>解绑备注（未解绑为空）</summary>
    public string UnbindRemark { get; set; } = "";
}
