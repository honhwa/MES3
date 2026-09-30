namespace SuperCapMES.WinForms.Models;

/// <summary>材料批次（电极材料 / 电解液 / 隔膜 / 集流体）</summary>
public class MaterialLot
{
    public string LotNo { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string SupplierLot { get; set; } = "";
    public DateTime ReceivedDate { get; set; }
    public string IqcResult { get; set; } = "合格";
    public string UsedFor { get; set; } = ""; // 关联极片批号或电芯SN
}
