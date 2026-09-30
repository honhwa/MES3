namespace SuperCapMES.WinForms.Models;

/// <summary>WMS 单据明细行：一单多行物料（物料 + 批次 + 库位 + 数量）。</summary>
public class WmsOrderLine
{
    /// <summary>所属单据号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>物料名称（创建时从物料主数据补齐）</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>规格型号</summary>
    public string Spec { get; set; } = "";

    /// <summary>计量单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>批次号（无批次用空串）</summary>
    public string LotNo { get; set; } = "";

    /// <summary>数量（入库/出库均按正数填写，方向由单据类型决定）</summary>
    public double Quantity { get; set; }

    /// <summary>库位：原料库-A区 / 成品库-B区 / 退货待处理区 / 待检区</summary>
    public string Location { get; set; } = WmsLocations.RawA;

    /// <summary>行备注</summary>
    public string Remark { get; set; } = "";
}
