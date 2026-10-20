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
    public double DelayDays { get; set; }
    public string DelayText => DelayDays <= 0.01 ? "不受影响" : $"延期 {DelayDays:0.#} 天";
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
    public double MaxDelayDays { get; set; }
    public List<UrgentAffectedWo> Affected { get; set; } = new();
    public int ChangeoverMinutes { get; set; }
    public double CapacityLossHours { get; set; }
    public double Total { get; set; }
    public List<UrgentScoreDim> Scores { get; set; } = new();
    public List<string> Pros { get; set; } = new();
    public List<string> Cons { get; set; } = new();

    public string TotalText => $"{Total:0.#}";
    public string OnTimeText => UrgentOnTime ? "紧急单可准时" : "紧急单将延期";
    public string DelayText => MaxDelayDays <= 0.01 ? "老单无延期" : $"老单最大延期 {MaxDelayDays:0.#} 天";
    public string AffectedText => AffectedWoCount == 0 ? "无老单受影响" : $"{AffectedWoCount} 单受影响";
    public string ChangeText => $"换型 {ChangeoverMinutes} 分钟";
    public string FinishText => UrgentFinish.ToString("MM-dd HH:mm");
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

    public UrgentPlan? Recommended => Plans.FirstOrDefault(p => p.Id == RecommendedPlanId);
}
