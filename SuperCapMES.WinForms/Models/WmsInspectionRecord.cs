namespace SuperCapMES.WinForms.Models;

/// <summary>
/// WMS 入库检记录：针对某张采购入库单的检验项目结果（复用工序实绩的 InspectionRow 交互口径）。
/// </summary>
public class WmsInspectionRecord
{
    /// <summary>所属单据号（通常为采购入库单）</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>检验项目</summary>
    public string ItemName { get; set; } = "";

    /// <summary>标准值（如 3.20±0.10 mm / ≥95 F）</summary>
    public string StandardValue { get; set; } = "";

    /// <summary>实测值</summary>
    public string MeasuredValue { get; set; } = "";

    /// <summary>判定结果：合格 / 不合格</summary>
    public string Result { get; set; } = "合格";

    /// <summary>检验员</summary>
    public string Inspector { get; set; } = "";

    /// <summary>检验时间</summary>
    public DateTime InspectTime { get; set; }
}
