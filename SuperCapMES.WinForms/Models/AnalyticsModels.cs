namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 六项 MES 分析功能 —— 模型与阈值
//
// 六项共用一套约定（与上一轮「主动预警与决策」保持一致，不另立一套）：
//   ① 等级复用 InsightLevels，不新造
//   ② 每条结果必须同时有「判断依据」+「建议动作」
//   ③ 每项结果带 Evidence（数据从哪来、怎么算的）
//   ④ 全部纯查询，不写数据、只给建议
//   ⑤ 预测类必须标免责；归因类不能说"根因"（根因需人工验证）
// ============================================================================

// ---------------------------------------------------------------------------
// 阈值配置（全部可配，换厂要能改）
// ---------------------------------------------------------------------------
public static class AnalyticsRules
{
    // ① 工单交期风险
    /// <summary>余量不足该比例即判「预警」（1.15 = 余量不足 15%）</summary>
    public static double DeliveryWarnRatio { get; set; } = 1.15;

    /// <summary>未考虑加班与人员变动的免责说明（预测类必带）</summary>
    public const string DeliveryDisclaimer = "基于当前负荷推算，未考虑加班、人员变动与设备临时故障，不构成交期承诺。";

    // ② 质量根因交叉
    /// <summary>样本量下限：低于该值不出现在主排序（避免 2/2 = 100% 的假象）</summary>
    public static int RootCauseMinSamples { get; set; } = 3;

    /// <summary>样本量达该值才给占比结论</summary>
    public static int RootCauseConfidentSamples { get; set; } = 5;

    // ③ SPC 调机建议
    /// <summary>Cpk 低于该值才给建议（≥1.33 为良好，1.00~1.33 勉强）</summary>
    public static double SpcAdviceCpkLimit { get; set; } = 1.33;

    /// <summary>变异系数阈值：超过即判"波动大"（指向机械/来料），否则判"均值偏"（指向设定值）</summary>
    public static double SpcCvThreshold { get; set; } = 0.08;

    /// <summary>验证所需样本数（给"调整后怎么验证"用）</summary>
    public static int SpcVerifySamples { get; set; } = 25;

    // ④ 资质 × 排班
    /// <summary>即将过期提醒窗口（天）</summary>
    public static int SkillExpiringDays { get; set; } = 30;

    // ⑤ 重复送检
    /// <summary>同型号+同工单送检次数达到该值才提示（2 = 已经第二次）</summary>
    public static int DuplicateFaiTimes { get; set; } = 2;

    // ⑥ 异常闭环
    /// <summary>按大类给的闭环超时阈值（小时）</summary>
    public static Dictionary<string, double> AbnormalCycleLimitHours { get; set; } = new()
    {
        [AbnormalCategories.Production] = 48,
        [AbnormalCategories.Equipment] = 72,
        [AbnormalCategories.Process] = 120,
        [AbnormalCategories.Other] = 72
    };

    public static void ResetToDefault()
    {
        DeliveryWarnRatio = 1.15;
        RootCauseMinSamples = 3;
        RootCauseConfidentSamples = 5;
        SpcAdviceCpkLimit = 1.33;
        SpcCvThreshold = 0.08;
        SpcVerifySamples = 25;
        SkillExpiringDays = 30;
        DuplicateFaiTimes = 2;
        AbnormalCycleLimitHours = new()
        {
            [AbnormalCategories.Production] = 48,
            [AbnormalCategories.Equipment] = 72,
            [AbnormalCategories.Process] = 120,
            [AbnormalCategories.Other] = 72
        };
    }
}

/// <summary>六项分析统一的结果外壳</summary>
public class AnalyticsReport
{
    public List<DeliveryRiskRow> Delivery { get; set; } = new();
    public List<QualityCrossRow> RootCause { get; set; } = new();
    public List<SpcTuningRow> SpcTuning { get; set; } = new();
    public List<SkillScheduleRow> SkillSchedule { get; set; } = new();
    public List<DuplicateFaiRow> DuplicateFai { get; set; } = new();
    public List<AbnormalCycleRow> AbnormalCycle { get; set; } = new();

    /// <summary>覆盖度说明（质量根因那一项必须带——它只覆盖有实绩的部分）</summary>
    public List<string> CoverageNotes { get; set; } = new();

    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

// ---------------------------------------------------------------------------
// ① 工单交期风险
// ---------------------------------------------------------------------------
public static class DeliveryRisk
{
    public const string Normal = "正常";
    public const string Warn = "预警";
    public const string Overdue = "逾期";
    public static readonly string[] All = { Normal, Warn, Overdue };
}

public class DeliveryRiskRow
{
    public string WoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string Line { get; set; } = "";

    public int PlanQty { get; set; }
    public int FinishedQty { get; set; }
    public int RemainingQty { get; set; }

    /// <summary>单件工时（小时/只，与产能引擎 UnitHours 同口径：0.0021 h = 7.56 秒）</summary>
    public double UnitHours { get; set; }

    /// <summary>剩余所需产能（小时）</summary>
    public double RequiredHours { get; set; }

    /// <summary>按日产能折算的日历所需天数</summary>
    public double RequiredDays { get; set; }

    /// <summary>从可开工日到交期日的可用日历天数（负数=已过期）</summary>
    public double AvailableDays { get; set; }

    /// <summary>产能缺口（小时，正数表示还差多少）</summary>
    public double GapHours { get; set; }

    /// <summary>建议提前开工天数</summary>
    public double AdviseDays { get; set; }

    public string Risk { get; set; } = DeliveryRisk.Normal;
    public string ReasonText { get; set; } = "";
    public string ActionText { get; set; } = "";
    public string Evidence { get; set; } = "";

    public string RemainingText => $"{RemainingQty} 只";
    public string RequiredHoursText => $"{RequiredHours:0.#} h";
    public string DaysText => $"需 {RequiredDays:0.#} 天 / 可用 {AvailableDays:0.#} 天";
    public string GapText => GapHours > 1e-6 ? $"缺 {GapHours:0.#} h" : "产能够";
    public string AdviseDaysText => AdviseDays > 0.5 ? $"提前 {Math.Ceiling(AdviseDays)} 天" : "无需提前";
}

// ---------------------------------------------------------------------------
// ② 质量根因交叉（工序 × 设备 × 材料批次）
// ---------------------------------------------------------------------------
public static class RootCauseGroupKind
{
    /// <summary>工序 × 设备（主排序，样本相对充足）</summary>
    public const string ProcessDevice = "工序×设备";

    /// <summary>工序 × 设备 × 班次（次排序，样本更薄）</summary>
    public const string ProcessDeviceShift = "工序×设备×班次";

    /// <summary>材料批次（样本极薄，只展示不排序）</summary>
    public const string MaterialLot = "材料批次";

    public static readonly string[] All = { ProcessDevice, ProcessDeviceShift, MaterialLot };
}

public class QualityCrossRow
{
    public string GroupKind { get; set; } = "";
    public string Dimension { get; set; } = "";     // 组合展示文本
    public string ProcessName { get; set; } = "";
    public string Equipment { get; set; } = "";
    public string Shift { get; set; } = "";
    public string LotNo { get; set; } = "";

    public int BadCount { get; set; }               // 不良数
    public int TotalCount { get; set; }              // 该组合的样本总数
    public double BadRate { get; set; }

    /// <summary>下游停机时长（分钟，来自 DeviceAlarms）</summary>
    public double DowntimeMin { get; set; }

    /// <summary>样本是否够下结论（≥ RootCauseMinSamples）</summary>
    public bool EnoughSamples { get; set; }

    /// <summary>样本是否够给占比结论（≥ RootCauseConfidentSamples）</summary>
    public bool Confident { get; set; }

    public string Note { get; set; } = "";
    public string ActionText { get; set; } = "";

    public string BadRateText => TotalCount == 0 ? "—" : $"{BadCount / (double)TotalCount:P0}（{BadCount}/{TotalCount}）";
    public string DowntimeText => DowntimeMin <= 0 ? "—" : $"{DowntimeMin / 60:0.#} h";
    public string TotalText => $"{TotalCount} 条";
    public string ConfidentText => Confident ? "可下结论" : EnoughSamples ? "仅供参考" : "样本不足";
}

// ---------------------------------------------------------------------------
// ③ SPC 调机建议
// ---------------------------------------------------------------------------
public static class SpcTuningPriority
{
    public const string P1 = "P1 立即调";
    public const string P2 = "P2 计划调";
    public const string P3 = "P3 关注";
    public static readonly string[] All = { P1, P2, P3 };
}

public class SpcTuningRow
{
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int Version { get; set; }

    public double Cpk { get; set; }                // 复用已有 Cpk，不重算
    public double Mean { get; set; }
    public double Sigma { get; set; }
    public double Usl { get; set; }
    public double Lsl { get; set; }

    /// <summary>变异系数 σ/均值</summary>
    public double Cv { get; set; }

    public string CapabilityGrade { get; set; } = "";   // 能力不足 / 勉强 / 良好
    public string Diagnose { get; set; } = "";          // 波动大 / 均值偏
    public string Priority { get; set; } = SpcTuningPriority.P3;

    /// <summary>调机方向（具体到检查项）</summary>
    public List<string> Directions { get; set; } = new();

    /// <summary>验证方式（必须有）</summary>
    public string VerifyMethod { get; set; } = "";

    /// <summary>预约生效的下一版本提示（对外显示见 PendingVersionText，空白会显示"无预约版本"）</summary>
    private string _pendingVersionText = "";

    public string CpkText => double.IsNaN(Cpk) ? "—" : Cpk.ToString("0.00");
    public string DirectionsText => string.Join("；", Directions);

    /// <summary>预约版本提示（无预约时显式说"无"，不留空白列让人以为出错了）</summary>
    public string PendingVersionText
    {
        get => string.IsNullOrWhiteSpace(_pendingVersionText) ? "无预约版本" : _pendingVersionText;
        set => _pendingVersionText = value;
    }
    public string StatText => $"均值 {Mean:0.###}｜σ {Sigma:0.###}｜CV {Cv:P1}";
}

// ---------------------------------------------------------------------------
// ④ 资质 × 排班
// ---------------------------------------------------------------------------
public static class SkillConflictKind
{
    /// <summary>完全没资质</summary>
    public const string Missing = "缺资质";

    /// <summary>有但已过期</summary>
    public const string Expired = "资质过期";

    /// <summary>30 天内到期（提示级）</summary>
    public const string Expiring = "即将过期";

    public static readonly string[] All = { Missing, Expired, Expiring };
}

public class SkillScheduleRow
{
    public string EmpNo { get; set; } = "";
    public string EmpName { get; set; } = "";
    public string Dept { get; set; } = "";
    public string Position { get; set; } = "";
    public string Crew { get; set; } = "";
    public string SkillItem { get; set; } = "";
    public string Kind { get; set; } = SkillConflictKind.Missing;
    public DateTime? ExpiryDate { get; set; }
    public int DaysToExpiry { get; set; }
    public string ActionText { get; set; } = "";
    public string ExpiryText => ExpiryDate is null ? "长期有效" : $"{ExpiryDate:yyyy-MM-dd}（{(DaysToExpiry >= 0 ? "剩" : "已过")}{Math.Abs(DaysToExpiry)} 天）";
}

// ---------------------------------------------------------------------------
// ⑤ 首检重复送检
// ---------------------------------------------------------------------------
public class DuplicateFaiRow
{
    public string ProductModel { get; set; } = "";
    public string WoNo { get; set; } = "";
    public int Times { get; set; }
    public List<string> FaiNos { get; set; } = new();
    public DateTime? LastTime { get; set; }

    /// <summary>首检后工艺参数是否有变更（有变更则重新送检合理，不报警）</summary>
    public bool ParamChanged { get; set; } = true;

    public string Reason { get; set; } = "";
    public string ActionText { get; set; } = "";

    public string LastTimeText => LastTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string TimesText => $"{Times} 次";
    public string FaiNosText => string.Join("、", FaiNos.Take(4)) + (FaiNos.Count > 4 ? $" 等 {FaiNos.Count} 张" : "");
    public string ParamChangedText => ParamChanged ? "有变更" : "无变更";
}

// ---------------------------------------------------------------------------
// ⑥ 异常闭环时长
// ---------------------------------------------------------------------------
public class AbnormalCycleRow
{
    public string Category { get; set; } = "";
    public string SubType { get; set; } = "";
    public int Count { get; set; }
    public int ClosedCount { get; set; }
    public int StillOpenCount { get; set; }
    public double AvgHours { get; set; }
    public double MedianHours { get; set; }
    public double MaxHours { get; set; }
    public double LimitHours { get; set; }
    public int OverdueCount { get; set; }
    public bool Slowest { get; set; }
    public string ActionText { get; set; } = "";

    public string AvgText => ClosedCount == 0 ? "—" : $"{AvgHours:0.#} h";
    public string MedianText => ClosedCount == 0 ? "—" : $"{MedianHours:0.#} h";
    public string MaxText => MaxHours <= 0 ? "—" : $"{MaxHours:0.#} h";
    public string LimitText => $"{LimitHours:0} h";
    public string CountText => $"{Count} 条";
    public string ClosedText => $"{ClosedCount} 条";
    public string StillOpenText => $"{StillOpenCount} 条";
    public string OverdueText => $"{OverdueCount} 条";
    public string SlowestText => Slowest ? "★ 最慢" : "—";
}
