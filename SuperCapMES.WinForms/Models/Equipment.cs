namespace SuperCapMES.WinForms.Models;

/// <summary>设备状态</summary>
public class Equipment
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Section { get; set; } = ""; // 工序段
    public string Status { get; set; } = "运行"; // 运行 / 停机 / 维护 / 待机
    public double Oee { get; set; }           // 稼动率 %
    public int TodayOutput { get; set; }
    public string LastEvent { get; set; } = "";
}
