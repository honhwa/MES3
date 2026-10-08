namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 预警阈值 —— **全部可配，改这里不用改任何逻辑**
//
// 为什么要集中配置：这三条预警的"合理阈值"取决于厂里的物料特性与设备状况，
// 照搬到别的厂一定是错的。做成可配项，厂里试跑两周就能按实际情况调到位。
// ============================================================================

/// <summary>主动预警的阈值配置（全局静态，改完即时生效）。</summary>
public static class MesDatabaseInsightRules
{
    // ---------- ① 物料临期 × 未来需求 ----------

    /// <summary>临期预警天数：剩余有效期 ≤ 该值即纳入分析</summary>
    public static int NearExpiryDays { get; set; } = 60;

    /// <summary>
    /// 已过期仍纳入分析的天数（负库存/负天数）。过期比临期更严重，等级更高。
    /// </summary>
    public static int ExpiredGraceDays { get; set; } = 0;

    /// <summary>需求预测视界：按未来多少天的工单需求推算日均消耗</summary>
    public static int DemandHorizonDays { get; set; } = 30;

    /// <summary>
    /// 预防性采购的触发余量：预计剩余量 ÷ 日均消耗 &lt; 该天数时，
    /// 说明效期内消耗不完、且很快会用完，必须提前补货（新批次效期长，能顶上）。
    /// </summary>
    public static double PreBuyCoverDays { get; set; } = 15;

    /// <summary>报废余量：预计剩余量低于该比例时，直接判"将报废"而不建议采购</summary>
    public static double WasteRateThreshold { get; set; } = 0.1;

    // ---------- ② 设备异常集中度 ----------

    /// <summary>异常统计窗口（天）</summary>
    public static int DeviceWindowDays { get; set; } = 30;

    /// <summary>异常频次阈值：窗口内达到该次数即列入风险设备</summary>
    public static int DeviceAbnormalThreshold { get; set; } = 2;

    /// <summary>部件集中度阈值：最高部件占比 ≥ 该值判为"集中故障"</summary>
    public static double DeviceConcentrationThreshold { get; set; } = 0.5;

    /// <summary>保养超期天数：距上次保养超过该天数（或点检已超期）判为高风险</summary>
    public static int MaintenanceStaleDays { get; set; } = 90;

    // ---------- ③ 呆滞料 ----------

    /// <summary>呆滞判定窗口（天）：该窗口内没有任何需求即视为呆滞</summary>
    public static int SlowMovingWindowDays { get; set; } = 60;

    /// <summary>呆滞判定：库存 − 未来需求 − 安全库存 &gt; 0 才算有占压</summary>
    public static double SlowMovingIdleThreshold { get; set; } = 1e-9;

    /// <summary>取当前阈值快照（用于结果复算与审计）</summary>
    public static InsightRuleSnapshot Snapshot() => new()
    {
        NearExpiryDays = NearExpiryDays,
        DeviceWindowDays = DeviceWindowDays,
        DeviceAbnormalThreshold = DeviceAbnormalThreshold,
        DeviceConcentrationThreshold = DeviceConcentrationThreshold,
        SlowMovingWindowDays = SlowMovingWindowDays,
        DemandHorizonDays = DemandHorizonDays
    };

    /// <summary>恢复默认（界面上"恢复默认"按钮用）</summary>
    public static void ResetToDefault()
    {
        NearExpiryDays = 60;
        ExpiredGraceDays = 0;
        DemandHorizonDays = 30;
        PreBuyCoverDays = 15;
        WasteRateThreshold = 0.1;
        DeviceWindowDays = 30;
        DeviceAbnormalThreshold = 2;
        DeviceConcentrationThreshold = 0.5;
        MaintenanceStaleDays = 90;
        SlowMovingWindowDays = 60;
        SlowMovingIdleThreshold = 1e-9;
    }
}
