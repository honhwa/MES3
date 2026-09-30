namespace SuperCapMES.WinForms.Models;

/// <summary>设备日产量（近30天趋势用）</summary>
public class DeviceDailyProduction
{
    public string DeviceCode { get; set; } = "";
    public DateTime Date { get; set; }
    public int Output { get; set; }
}

/// <summary>设备小时产量与 OEE（当日 0-23 点）</summary>
public class DeviceHourlyRecord
{
    public string DeviceCode { get; set; } = "";
    public DateTime Date { get; set; }
    public int Hour { get; set; }                 // 0~23
    public int Output { get; set; }
    public double OeeRate { get; set; }           // OEE %
    public double TargetOee { get; set; } = 85;   // 目标 OEE %
    /// <summary>OEE 达成率 = OeeRate / TargetOee * 100</summary>
    public double AchievementRate => TargetOee <= 0 ? 0 : Math.Round(OeeRate / TargetOee * 100, 1);
}

/// <summary>设备异常事件</summary>
public class DeviceAlarm
{
    public string DeviceCode { get; set; } = "";
    public DateTime Time { get; set; }
    public string AlarmType { get; set; } = "";     // 过温 / 张力超差 / 注液量异常 / 维护 ...
    public string Message { get; set; } = "";
    public int DurationMin { get; set; }            // 持续时长（分钟）
    public string Status { get; set; } = "未处理";   // 已处理 / 处理中 / 未处理

    /// <summary>已联动生成的异常单号（空表示尚未转单；转单后写回，防止重复转）</summary>
    public string LinkedAbnormalNo { get; set; } = "";
}

/// <summary>设备状态常量（与 Equipment.Status 取值保持一致）</summary>
public static class DeviceStatus
{
    public const string Running = "运行";
    public const string Standby = "待机";
    public const string Stopped = "停机";
    public const string Maintenance = "维护";
}

/// <summary>设备状态履历片段（当天 0 点起按时间升序）</summary>
public class DeviceStatusRecord
{
    public string DeviceCode { get; set; } = "";
    public DateTime StartTime { get; set; }
    /// <summary>结束时间；null 表示进行中（最后一段）</summary>
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = DeviceStatus.Running;   // 运行 / 待机 / 停机 / 维护
    public string Note { get; set; } = "";                        // 简短中文说明

    /// <summary>持续时长（分钟）；EndTime 为 null 时按当前时间计算</summary>
    public int DurationMin => (int)Math.Round(((EndTime ?? DateTime.Now) - StartTime).TotalMinutes);
}
