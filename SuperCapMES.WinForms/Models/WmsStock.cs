namespace SuperCapMES.WinForms.Models;

/// <summary>
/// WMS 库存台账行：某物料在指定批次 + 库位下的在库数量。
/// 唯一键 = MaterialCode + LotNo + Location。
/// </summary>
public class WmsStock
{
    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>物料名称（入库时从物料主数据/单据行补齐）</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>规格型号</summary>
    public string Spec { get; set; } = "";

    /// <summary>计量单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>批次号（无批次用空串）</summary>
    public string LotNo { get; set; } = "";

    /// <summary>库位：原料库-A区 / 成品库-B区 / 退货待处理区 / 待检区</summary>
    public string Location { get; set; } = WmsLocations.RawA;

    /// <summary>在库数量</summary>
    public double Quantity { get; set; }

    /// <summary>最后更新时间（入库/出库过账时刷新）</summary>
    public DateTime LastUpdate { get; set; }

    /// <summary>有效期到期日（null = 不进行效期管理）</summary>
    public DateTime? ExpiryDate { get; set; }
}
