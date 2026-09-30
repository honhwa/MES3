namespace SuperCapMES.WinForms.Models;

/// <summary>设备上传的工序实绩：每个电芯在指定设备/工序上的生产信息</summary>
public class ProcessActualRecord
{
    public string CellSn { get; set; } = "";
    public string WoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Equipment { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Shift { get; set; } = "";
    public string ParamName { get; set; } = "";
    public string ParamValue { get; set; } = "";
    public DateTime Time { get; set; }
}
