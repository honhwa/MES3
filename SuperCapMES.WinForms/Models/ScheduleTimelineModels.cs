namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 线体时间线 —— 排产口径的**唯一事实来源**
//
// 为什么要单独抽这一层（2026-10-20 统一"模拟插单"与"智能排产"）：
//   原先两个页面各自算一遍时间：
//     · 智能排产：开工 = 线体腾出时间（at + Σ工时/16h），完工 = 开工 + 本单工时；
//     · 插单模拟：开工 = 现在（at），完工 = at + 本单工时，老单从紧急单完工后往后顺延。
//   于是同一个 PO 在两页差出 0.7~0.9 天，问「到底哪天能交」得到两个答案。
//   更麻烦的是两页都**没把换型算进时间线**（换型只用于评分），而 CTP 又算了——
//   同一件事三套口径，评审现场一问就穿。
//
// 现在的口径（本文件的规则，全部页面共用）：
//   线体上的任务按「交期优先」排队，逐个占用时间：
//       第 i 个任务开始 = 第 i−1 个任务完工 + 换型(第 i−1 型号 → 第 i 型号)
//       第 i 个任务完工 = 开始 + 工时 ÷ 每日有效产能(16h)
//   第一个任务从"现在"开始（线体此刻就在干活）。换型时间来自换型矩阵，
//   同一型号连续生产为 0，首次上线只清线。
//
// 于是三种排法只是在同一条时间线上的不同插入位置，可对比、可验证：
//   队尾追加（= 智能排产的推荐结论）     ← 不打扰任何在制工单
//   队首插入（= 插单方案一 老单延后）    ← 紧急单最早完工，代价是在制工单整体顺延
//   分段插空（= 插单方案二 拆分插空）    ← 找一个插入位置，让老单总延期最小
//

/// <summary>时间线上的一个任务（在制工单，或模拟插入的紧急单）</summary>
public sealed class LineTimelineItem
{
    /// <summary>任务号（工单号；模拟插入的紧急单用「(紧急单)」占位）</summary>
    public string TaskNo { get; set; } = "";

    /// <summary>是否真实工单（false = 模拟插入的任务）</summary>
    public bool IsWorkOrder { get; set; } = true;

    public string ProductModel { get; set; } = "";

    /// <summary>本任务数量（只）</summary>
    public int Qty { get; set; }

    /// <summary>本任务工时（h）</summary>
    public double Hours { get; set; }

    /// <summary>进入本任务前的换型时间（分钟；首任务为首次上线清线）</summary>
    public int ChangeoverMinutes { get; set; }

    public DateTime Start { get; set; }
    public DateTime Finish { get; set; }

    /// <summary>交期（老单用于算延期；紧急单可为空）</summary>
    public DateTime? DueDate { get; set; }

    public double Days => Hours / SchedulingRules.DailyCapacityHours;

    public string ModelText => string.IsNullOrWhiteSpace(ProductModel) ? "—" : ProductModel;
    public string QtyText => $"{Qty} 只";
    public string HoursText => $"{Hours:0.#}h";
    public string ChangeoverText => ChangeoverMinutes <= 0 ? "—" : $"{ChangeoverMinutes} 分";
    public string StartText => Start.ToString("MM-dd HH:mm");
    public string FinishText => Finish.ToString("MM-dd HH:mm");
    public string DueText => DueDate?.ToString("MM-dd") ?? "—";
}

/// <summary>
/// 一条线体的时间线：在制工单按交期排队 + 每段的换型与起止时间。
/// <see cref="TailFinish"/> 就是"新单排在队尾时最早能开工的时间"。
/// </summary>
public sealed class LineTimeline
{
    public string Line { get; set; } = "";

    /// <summary>时间线起点（= 现在）</summary>
    public DateTime At { get; set; }

    public List<LineTimelineItem> Items { get; set; } = new();

    /// <summary>队尾完工时间（无在制工单 = 起点 now）</summary>
    public DateTime TailFinish => Items.Count == 0 ? At : Items[^1].Finish;

    /// <summary>队尾型号（新单接在它后面，换型起点）</summary>
    public string TailModel => Items.Count == 0 ? "" : Items[^1].ProductModel;

    /// <summary>队首型号（插队时线体正在跑的型号，换型起点）</summary>
    public string HeadModel => Items.Count == 0 ? "" : Items[0].ProductModel;

    public int WorkOrderCount => Items.Count(i => i.IsWorkOrder);
    public double WorkHours => Items.Sum(i => i.Hours);
    public int ChangeoverMinutes => Items.Sum(i => i.ChangeoverMinutes);

    public string QueueText => Items.Count == 0
        ? "线体空闲，可立即开工"
        : $"{WorkOrderCount} 张在制 / {WorkHours:0.#}h / 换型 {ChangeoverMinutes} 分 → 队尾 {TailFinish:MM-dd HH:mm}";

    public string SummaryText => $"{Line}：{QueueText}";
}
