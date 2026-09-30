namespace SuperCapMES.WinForms.Models;

/// <summary>追溯结果视图模型（单体内外全链路）</summary>
public class TraceResult
{
    public CellSn? Cell { get; set; }
    public List<MaterialLot> Materials { get; set; } = new();
    public List<ProcessRecord> Processes { get; set; } = new();
    public List<TestRecord> Tests { get; set; } = new();

    /// <summary>干燥房环境（模拟）：露点 / 温度 / 湿度 / 采集点</summary>
    public string EnvDewPoint { get; set; } = "";
    public string EnvTemp { get; set; } = "";
    public string EnvHumidity { get; set; } = "";
    public string EnvPoint { get; set; } = "";
}
