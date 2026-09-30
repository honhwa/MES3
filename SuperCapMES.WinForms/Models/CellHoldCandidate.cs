namespace SuperCapMES.WinForms.Models;

/// <summary>资材追溯候选行：按资材关键字命中的电芯及其关联资材/保留信息</summary>
public class CellHoldCandidate
{
    /// <summary>电芯 SN</summary>
    public string Sn { get; set; } = "";

    /// <summary>产品型号</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>产线</summary>
    public string Line { get; set; } = "";

    /// <summary>当前状态（在制 / 化成中 / 检测中 / 合格 / 不良等）</summary>
    public string Status { get; set; } = "";

    /// <summary>分档结果（A1 / B2 / NG 等）</summary>
    public string Result { get; set; } = "";

    /// <summary>极片批号</summary>
    public string ElectrodeLot { get; set; } = "";

    /// <summary>电解液批号</summary>
    public string ElectrolyteLot { get; set; } = "";

    /// <summary>命中的资材说明，如"活性炭电极材料 PC-260812-L1-001"（多批用" / "分隔）</summary>
    public string MaterialDesc { get; set; } = "";

    /// <summary>保留状态：空表示未保留；非空形如"保留中-化成外观"</summary>
    public string HoldStatus { get; set; } = "";

    /// <summary>命中的材料批次快照（不参与网格展示，供执行保留时聚合资材编码/名称/批号）</summary>
    public List<MaterialLot> HitMaterials { get; } = new();
}
