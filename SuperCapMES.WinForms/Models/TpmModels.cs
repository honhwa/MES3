namespace SuperCapMES.WinForms.Models;

/// <summary>点检/保养周期类型</summary>
public static class TpmCycle
{
    public const string Daily = "日点检";
    public const string Weekly = "周点检";
    public const string Monthly = "月度保养";
    public const string Quarterly = "季度保养";

    public static readonly string[] All = { Daily, Weekly, Monthly, Quarterly };

    /// <summary>周期类型对应的天数</summary>
    public static int DaysOf(string cycle) => cycle switch
    {
        Daily => 1,
        Weekly => 7,
        Monthly => 30,
        Quarterly => 90,
        _ => 30
    };
}

/// <summary>点检保养到期状态（由下次到期时间计算）</summary>
public static class TpmDueStatus
{
    public const string Overdue = "已超期";
    public const string DueSoon = "即将到期";
    public const string Normal = "正常";

    /// <summary>按剩余天数判定：<0 超期；≤3 天即将到期；否则正常</summary>
    public static string Judge(double daysLeft) =>
        daysLeft < 0 ? Overdue : (daysLeft <= 3 ? DueSoon : Normal);
}

/// <summary>点检/保养计划</summary>
public class TpmPlan
{
    /// <summary>计划编号，形如 TPM-001</summary>
    public string PlanId { get; set; } = "";

    public string DeviceCode { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Section { get; set; } = "";

    /// <summary>点检/保养项目</summary>
    public string ItemName { get; set; } = "";

    /// <summary>周期类型（日点检 / 周点检 / 月度保养 / 季度保养）</summary>
    public string CycleType { get; set; } = TpmCycle.Daily;

    /// <summary>判定标准</summary>
    public string Standard { get; set; } = "";

    public string Owner { get; set; } = "";
    public DateTime LastDoneTime { get; set; }
    public DateTime? NextDueTime { get; set; }

    /// <summary>距下次到期的剩余天数（负值表示已超期）</summary>
    public double DaysLeft => NextDueTime is null ? 999 : Math.Round((NextDueTime.Value - DateTime.Now).TotalDays, 1);

    /// <summary>到期状态：已超期 / 即将到期 / 正常</summary>
    public string DueStatus => TpmDueStatus.Judge(DaysLeft);

    /// <summary>剩余天数显示文本</summary>
    public string DaysLeftText => NextDueTime is null ? "—" : (DaysLeft < 0 ? $"超期 {-DaysLeft:0.#} 天" : $"剩 {DaysLeft:0.#} 天");

    /// <summary>周期显示文本</summary>
    public string CycleText => $"{CycleType}（{TpmCycle.DaysOf(CycleType)} 天）";
}

/// <summary>点检/保养执行记录</summary>
public class TpmRecord
{
    /// <summary>记录号，形如 TPMR-yyyyMMdd-001</summary>
    public string RecordNo { get; set; } = "";

    public string PlanId { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string ItemName { get; set; } = "";

    public DateTime ExecuteTime { get; set; }

    /// <summary>执行结果：正常 / 异常</summary>
    public string Result { get; set; } = "正常";

    public string Operator { get; set; } = "";
    public string Remark { get; set; } = "";

    public bool IsAbnormal => Result == "异常";
}

/// <summary>备件台账</summary>
public class SparePart
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Stock { get; set; }
    public double SafetyStock { get; set; }
    public string Location { get; set; } = "";
    public string Supplier { get; set; } = "";
    public DateTime? LastIssueTime { get; set; }

    /// <summary>是否低于安全库存</summary>
    public bool IsLow => Stock <= SafetyStock;

    public string StockText => $"{Stock:0.##} {Unit}";

    /// <summary>库存状态：充足 / 低于安全库存</summary>
    public string StockStatus => IsLow ? "低于安全库存" : "充足";
}
