namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 保留单被保留电芯明细（平铺视图）：保留单号 + 电芯台账详情。
/// 由 MesDatabase.GetHoldItemDetails 对 HoldItems 与 Cells 关联生成，
/// 供「保留管理」页双击保留单行后弹出查看。
/// </summary>
public class CellHoldItemDetail
{
    /// <summary>保留单号</summary>
    public string HoldNo { get; set; } = "";

    /// <summary>被保留的电芯 SN</summary>
    public string CellSn { get; set; } = "";

    /// <summary>产品型号（台账缺失时为空串）</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>产线（台账缺失时为空串）</summary>
    public string Line { get; set; } = "";

    /// <summary>当前状态：在制 / 化成中 / 检测中 / 合格 / 不良 等（台账缺失时为空串）</summary>
    public string Status { get; set; } = "";

    /// <summary>分档结果：A1 / B2 / NG 等（台账缺失时为空串）</summary>
    public string Result { get; set; } = "";

    /// <summary>极片批号（台账缺失时为空串）</summary>
    public string ElectrodeLot { get; set; } = "";

    /// <summary>电解液批号（台账缺失时为空串）</summary>
    public string ElectrolyteLot { get; set; } = "";

    /// <summary>生产时间（台账缺失时为 DateTime.MinValue，展示层显示为"—"）</summary>
    public DateTime CreateTime { get; set; }
}
