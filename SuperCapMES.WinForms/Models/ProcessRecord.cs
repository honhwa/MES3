namespace SuperCapMES.WinForms.Models;

/// <summary>工序记录（工艺参数挂载到电芯SN）</summary>
public class ProcessRecord
{
    public string CellSn { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Equipment { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Shift { get; set; } = "";      // 班次
    public string ParamName { get; set; } = "";  // 参数名，如 卷绕张力 / 注液量
    public string ParamValue { get; set; } = ""; // 参数值
    public DateTime Time { get; set; }

    /// <summary>生产该工序时设备所处的状态（运行/待机/停机/维护；"—"表示无法判定）。由 MesDatabase.GetTrace 填充。</summary>
    public string DeviceStatusAt { get; set; } = "";
}
