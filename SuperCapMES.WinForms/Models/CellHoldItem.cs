namespace SuperCapMES.WinForms.Models;

/// <summary>保留单明细：保留单号 → 电芯 SN（一单一多只电芯）</summary>
public class CellHoldItem
{
    /// <summary>保留单号</summary>
    public string HoldNo { get; set; } = "";

    /// <summary>被保留的电芯 SN</summary>
    public string CellSn { get; set; } = "";
}
