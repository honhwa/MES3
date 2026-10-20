namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 紧急插单冲突处理：局部重排三方案 What-if（技术方案 2.3）
//
// 文档口径：
//   · 冲突域识别：只调紧急单占用的线体/设备/工装周边的低优先级工单，不做全局重排；
//   · 三方案：① 低优先工单延后 ② 拆分低优先工单插空隙 ③ 低优先工单转次优线；
//   · 自动评分：紧急单准时交付率 > 老单交期影响最小 > 换型成本最低 > 设备负载均衡（权重排序）。
// 纯规则计算，纯查询零副作用；"应用建议"为演示（不改真实排程）。
// ============================================================================

/// <summary>受影响的老工单（方案下顺延情况）</summary>
public sealed class UrgentAffectedWo
{
    public string WoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public DateTime DueDate { get; set; }
    public DateTime OldFinish { get; set; }
    public DateTime NewFinish { get; set; }

    /// <summary>对交期的影响（天）：新完工晚于交期的部分，0 = 仍能按期</summary>
    public double DelayDays { get; set; }

    /// <summary>顺延量（天）：新完工比原计划完工晚多少（与交期无关，反映"被推后了多久"）</summary>
    public double ShiftDays { get; set; }

    public string DelayText => DelayDays <= 0.01 ? "仍按期" : $"超交期 {DelayDays:0.#} 天";
    public string ShiftText => ShiftDays <= 0.01 ? "无需顺延" : $"顺延 {ShiftDays:0.#} 天";
    public string OldFinishText => OldFinish.ToString("MM-dd HH:mm");
    public string NewFinishText => NewFinish.ToString("MM-dd HH:mm");
    public string DueText => DueDate.ToString("MM-dd");
}

/// <summary>方案评分中的一维</summary>
public sealed class UrgentScoreDim
{
    public string Dimension { get; set; } = "";
    public double Weight { get; set; }
    public double Score { get; set; }
    public double Weighted => Math.Round(Score * Weight, 1);
    public string Reason { get; set; } = "";
    public string DimensionText => $"{Dimension}（{Weight * 100:0}%）";
    public string WeightedText => $"{Weighted:0.#}";
}

/// <summary>一个插单方案（What-if 模拟结果）</summary>
public sealed class UrgentPlan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Line { get; set; } = "";
    public DateTime UrgentStart { get; set; }
    public DateTime UrgentFinish { get; set; }
    public bool UrgentOnTime { get; set; }
    public int AffectedWoCount { get; set; }

    /// <summary>最大超交期天数（老单本身可能早已超期，属于历史问题，不参与评分）</summary>
    public double MaxDelayDays { get; set; }

    /// <summary>最大顺延天数（本次插单让老单多等了多久 —— 这才是插单方案应付的代价）</summary>
    public double MaxShiftDays { get; set; }
    public List<UrgentAffectedWo> Affected { get; set; } = new();
    public int ChangeoverMinutes { get; set; }

    /// <summary>时间线口径：队首插入 / 分段插空 / 队尾追加（三方案与基线只有插入位置不同）</summary>
    public string TimelineBasis { get; set; } = "";

    /// <summary>相对基线（队尾追加 = 智能排产结论）可提前的天数；负数表示比排队更晚</summary>
    public double GainVsBaselineDays { get; set; }

    public double CapacityLossHours { get; set; }
    public double Total { get; set; }
    public List<UrgentScoreDim> Scores { get; set; } = new();
    public List<string> Pros { get; set; } = new();
    public List<string> Cons { get; set; } = new();

    public string TotalText => $"{Total:0.#}";
    public string OnTimeText => UrgentOnTime ? "紧急单可准时" : "紧急单将延期";
    public string DelayText => MaxShiftDays <= 0.01 ? "老单无顺延" : $"老单最大顺延 {MaxShiftDays:0.#} 天";
    public string AffectedText => AffectedWoCount == 0 ? "无老单受影响" : $"{AffectedWoCount} 单受影响";
    public string ChangeText => $"换型 {ChangeoverMinutes} 分钟";
    public string FinishText => UrgentFinish.ToString("MM-dd HH:mm");
    public string GainText => Math.Abs(GainVsBaselineDays) < 0.01
        ? "与排队一致"
        : GainVsBaselineDays > 0 ? $"比排队提前 {GainVsBaselineDays:0.#} 天" : $"比排队晚 {-GainVsBaselineDays:0.#} 天";
}

/// <summary>
/// 插单基线：把紧急单**排到线体队尾**（= 智能排产的推荐结论）。
/// 三方案都与这条基线比，页面才有"插队到底能提前多少、代价是什么"的参照。
/// </summary>
public sealed class UrgentBaseline
{
    public string Line { get; set; } = "";
    public int ChangeoverMinutes { get; set; }
    public DateTime Start { get; set; }
    public DateTime Finish { get; set; }
    public bool OnTime { get; set; }

    /// <summary>排队时前面还有几张在制工单</summary>
    public int QueueWorkOrderCount { get; set; }

    public string StartText => Start.ToString("MM-dd HH:mm");
    public string FinishText => Finish.ToString("MM-dd HH:mm");
    public string OnTimeText => OnTime ? "可准时" : "将延期";
    public string ChangeoverText => ChangeoverMinutes <= 0 ? "无需换型" : $"换型 {ChangeoverMinutes} 分钟";
    public string BaselineText =>
        $"基线（不插队，= 智能排产）：{Line} 队尾追加，前面 {QueueWorkOrderCount} 张在制 → {StartText} 开工 / {FinishText} 完工（{OnTimeText}）";
}

/// <summary>插单模拟整体结果</summary>
public sealed class UrgentInsertSimulation
{
    public string PoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public int OrderQty { get; set; }
    public DateTime DueDate { get; set; }
    public double UrgentHours { get; set; }
    public string MainLine { get; set; } = "";
    public string AltLine { get; set; } = "";
    public List<UrgentPlan> Plans { get; set; } = new();
    public string RecommendedPlanId { get; set; } = "";
    public List<string> Notes { get; set; } = new();

    /// <summary>基线（队尾追加）= 智能排产的结论，数值与 RecommendSchedule 完全一致</summary>
    public UrgentBaseline Baseline { get; set; } = new();

    public UrgentPlan? Recommended => Plans.FirstOrDefault(p => p.Id == RecommendedPlanId);

    /// <summary>最快方案相对基线提前的天数（≤0 表示插队/插空/转线都不比排队更早）</summary>
    public double BestGainDays => Plans.Count == 0 ? 0 : Plans.Max(p => p.GainVsBaselineDays);
}
