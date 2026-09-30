namespace SuperCapMES.WinForms.Models;

/// <summary>检验记录：针对某个电芯在指定设备/工序上的检验项目结果</summary>
public class InspectionRecord
{
    public string CellSn { get; set; } = "";
    public string Equipment { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string StandardValue { get; set; } = "";
    public string MeasuredValue { get; set; } = "";
    public string Result { get; set; } = "合格";
    public string Inspector { get; set; } = "";
    public DateTime InspectTime { get; set; }
}

/// <summary>检验项目填写行（界面可编辑）</summary>
public class InspectionRow
{
    public string ItemName { get; set; } = "";
    public string StandardValue { get; set; } = "";
    public string MeasuredValue { get; set; } = "";
    public string Result { get; set; } = "合格";
    public string Inspector { get; set; } = "";
}
