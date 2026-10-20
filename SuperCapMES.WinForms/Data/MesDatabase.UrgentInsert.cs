using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 紧急插单：局部重排三方案 What-if + 自动评分（技术方案 2.3）
//
// 2026-10-20 统一口径（与「智能排产」对齐）：
//   · 本页**不再自己算良率/工时/开工时间**，全部取 RecommendSchedule 的结论 ——
//     同一 PO 在两端必须给出同一个"排队完工"数字，否则用户看到两个交期会怀疑系统；
//   · 基线 Baseline = 把紧急单排到队尾（= 智能排产的推荐结论）；三方案都是相对这条基线的改进；
//   · 三方案 = 同一条时间线（LineTimeline）上的三种插入位置，只有插入位置不同：
//       P1 队首插入：紧急单立刻上（at + 换型），在制工单依次顺延 → 最早完工，代价是老单顺延；
//       P2 分段插空：紧急单拆两段（默认 40% / 60%，换型计两次），枚举第二段的插入位置，
//                    取「老单总交期影响最小」的位置 —— 不再用 ×0.4 的拍脑袋系数；
//       P3 队尾追加：直接排到次优线队尾 → 对主线老单零影响，但要比排队等。
//   · 换型时间统一来自换型矩阵（与六维评分、CTP 同一来源）。
// 纯规则计算、纯查询零副作用。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>分段插空时第一段占的比例（剩余为第二段；换型按两次计）</summary>
    public const double UrgentSplitFirstRatio = 0.4;

    /// <summary>模拟紧急插单的三方案（What-if）。纯查询，零副作用。</summary>
    public static UrgentInsertSimulation? SimulateUrgentInsert(string poNo, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var po = FindErpPo(poNo);
        if (po is null) return null;

        // ★ 唯一事实来源：良率、工时、投产数、推荐线、时间线全部来自排产建议
        var rec = RecommendSchedule(po.PoNo, at);
        if (rec is null) return null;

        var mainLine = string.IsNullOrWhiteSpace(rec.RecommendedLine)
            ? (KnownLines().FirstOrDefault() ?? "L1")
            : rec.RecommendedLine;

        var timeline = rec.Timeline ?? BuildLineTimeline(mainLine, at);
        var hours = rec.RequiredHours;

        // 次优线：三级选线里"可分配且非主线"的第一条；没有就用负载最小且非主线的线
        var altLine = rec.LineMatch?.Candidates
                          .Where(s => s.Allocatable && !LineEquals(s.Line, mainLine))
                          .Select(s => s.Line)
                          .FirstOrDefault()
                      ?? KnownLines().Where(l => !LineEquals(l, mainLine))
                          .OrderBy(l => GetLineLoad(l, at).RemainingHours)
                          .FirstOrDefault()
                      ?? mainLine;

        var sim = new UrgentInsertSimulation
        {
            PoNo = po.PoNo,
            ProductModel = po.ProductModel,
            OrderQty = po.OrderQty,
            DueDate = po.DueDate,
            UrgentHours = hours,
            MainLine = mainLine,
            AltLine = altLine,
            Baseline = new UrgentBaseline
            {
                Line = mainLine,
                ChangeoverMinutes = rec.IncomingChangeoverMinutes,
                Start = rec.PlannedStart,
                Finish = rec.PlannedFinish,
                OnTime = rec.PlannedFinish <= po.DueDate,
                QueueWorkOrderCount = timeline.WorkOrderCount
            }
        };

        // ---- P1 队首插入（老单延后） ----
        var head = InsertAtLineHead(timeline, po.ProductModel, hours);
        var p1 = BuildPlan("P1", "方案一：老单延后",
            $"紧急单插到 {mainLine} 队首（{at:MM-dd HH:mm} 起 + 换型 {head.ChangeoverMinutes} 分钟），在制工单依次顺延。",
            "队首插入", mainLine, po, at, head.Start, head.Finish, head.ChangeoverMinutes, head.Affected,
            extraChangeoverMinutes: 0, sim.Baseline, note: $"紧急单最早，但 {timeline.WorkOrderCount} 张在制工单全部顺延");

        // ---- P2 分段插空：枚举第二段插入位置，取老单总交期影响最小 ----
        var best = (insertAfter: 0, totalDelay: double.MaxValue, count: 0, maxDelay: 0.0,
                    start: DateTime.MaxValue, finish: DateTime.MaxValue, co: 0,
                    affected: new List<UrgentAffectedWo>());
        for (var k = 0; k <= timeline.Items.Count; k++)
        {
            var r = SimulateSplit(timeline, po.ProductModel, hours, k);
            if (r.totalDelay < best.totalDelay - 1e-9
                || (Math.Abs(r.totalDelay - best.totalDelay) < 1e-9 && r.finish < best.finish))
            {
                best = (k, r.totalDelay, r.count, r.maxDelay, r.start, r.finish, r.co, r.affected);
            }
        }
        var splitAt = best.insertAfter;
        var splitWhere = splitAt == 0
            ? "紧随第一段（等价于不拆）"
            : $"第 {splitAt} 张在制工单之后";
        var p2 = BuildPlan("P2", "方案二：拆分插空",
            $"紧急单拆两段（{UrgentSplitFirstRatio:P0} / {(1 - UrgentSplitFirstRatio):P0}，换型计两次），第二段插在{splitWhere}，"
            + $"取「老单总顺延量最小」的位置（总顺延 {best.totalDelay:0.#} 天）。",
            "分段插空", mainLine, po, at, best.start, best.finish, best.co, best.affected,
            extraChangeoverMinutes: 0, sim.Baseline, note: $"老单总顺延 {best.totalDelay:0.#} 天（方案一为 {p1.Affected.Sum(a => a.ShiftDays):0.#} 天）");

        // ---- P3 转次优线（队尾追加） ----
        var altTimeline = BuildLineTimeline(altLine, at);
        var altCo = GetChangeoverInfo(altTimeline.TailModel, po.ProductModel);
        var altStart = altTimeline.TailFinish.AddMinutes(altCo.Minutes);
        var altFinish = altStart.AddDays(hours / SchedulingRules.DailyCapacityHours);
        var p3 = BuildPlan("P3", "方案三：转次优线",
            $"紧急单转排次优线 {altLine}，排在队尾（前有 {altTimeline.WorkOrderCount} 张在制）→ 对 {mainLine} 老单零影响，但要等排队。",
            "队尾追加", altLine, po, at, altStart, altFinish, altCo.Minutes,
            new List<UrgentAffectedWo>(), extraChangeoverMinutes: 0, sim.Baseline,
            note: $"对 {mainLine} 老单零影响；相对主线排队 {rec.PlannedFinish:MM-dd HH:mm} 的差异见「vs 基线」");

        sim.Plans = new List<UrgentPlan> { p1, p2, p3 };
        sim.RecommendedPlanId = sim.Plans.OrderByDescending(p => p.Total).First().Id;

        sim.Notes.Add($"统一口径：良率 {rec.Yield:P1}、单位工时 {rec.UnitHours:0.00000} h/只、投产 {rec.InputQty} 只、"
                    + $"需产能 {hours:0.#}h —— 全部取自智能排产，本页不另行计算。");
        sim.Notes.Add(sim.Baseline.BaselineText);
        sim.Notes.Add($"三方案都是同一条线体时间线上的插入位置：队首插入（最早）/ 分段插空（折中，插入点按老单总交期影响搜索）/ 队尾追加（零影响但要等）。"
                    + $"换型时间统一取自换型矩阵；{(sim.BestGainDays > 0.01 ? $"最快方案比排队提前 {sim.BestGainDays:0.#} 天" : "本次插队不带来提前量（线体基本空闲）")}。");
        sim.Notes.Add("评分权重：紧急单准时交付 40% > 老单交期影响 30% > 换型成本 20% > 负载均衡 10%。");
        return sim;
    }

    /// <summary>
    /// 模拟"紧急单拆两段、第二段插在第 insertAfter 张在制工单之后"的时间线结果。
    /// 返回老单总交期影响、最大影响、紧急单起止与受影响明细。
    /// </summary>
    private static (double totalDelay, int count, double maxDelay, DateTime start, DateTime finish, int co,
                    List<UrgentAffectedWo> affected)
        SimulateSplit(LineTimeline timeline, string model, double hours, int insertAfter)
    {
        var cursor = timeline.At;
        var prev = timeline.HeadModel;
        var affected = new List<UrgentAffectedWo>();
        var totalCo = 0;

        // 第一段
        var co1 = GetChangeoverInfo(prev, model);
        totalCo += co1.Minutes;
        cursor = cursor.AddMinutes(co1.Minutes);
        var start = cursor;
        cursor = cursor.AddDays(hours * UrgentSplitFirstRatio / SchedulingRules.DailyCapacityHours);
        prev = model;

        // 前 insertAfter 张在制工单
        for (var i = 0; i < insertAfter && i < timeline.Items.Count; i++)
            PlaceItem(timeline.Items[i], ref cursor, ref prev, affected);

        // 第二段（从当前型号再换回紧急单型号；紧邻则无需换型）
        var co2 = GetChangeoverInfo(prev, model);
        totalCo += co2.Minutes;
        cursor = cursor.AddMinutes(co2.Minutes);
        cursor = cursor.AddDays(hours * (1 - UrgentSplitFirstRatio) / SchedulingRules.DailyCapacityHours);
        var finish = cursor;
        prev = model;

        // 剩余在制工单
        for (var i = insertAfter; i < timeline.Items.Count; i++)
            PlaceItem(timeline.Items[i], ref cursor, ref prev, affected);

        // 优化目标用"总顺延量"：老单自身的超交期与插单无关，不该被拿来当优化目标
        var shifts = affected.Select(a => a.ShiftDays).ToList();
        return (shifts.Sum(), shifts.Count(d => d > 0.01), shifts.Count == 0 ? 0 : shifts.Max(),
                start, finish, totalCo, affected);
    }

    /// <summary>把一张在制工单放到当前游标之后（含换型），记录对交期的影响与顺延量。</summary>
    private static void PlaceItem(LineTimelineItem item, ref DateTime cursor, ref string prev,
                                  List<UrgentAffectedWo> affected)
    {
        var co = GetChangeoverInfo(prev, item.ProductModel);
        cursor = cursor.AddMinutes(co.Minutes);
        var start = cursor;
        var finish = start.AddDays(item.Hours / SchedulingRules.DailyCapacityHours);

        var due = item.DueDate ?? finish;
        affected.Add(new UrgentAffectedWo
        {
            WoNo = item.TaskNo,
            ProductModel = item.ProductModel,
            DueDate = due,
            OldFinish = item.Finish,
            NewFinish = finish,
            DelayDays = Math.Max(0, (finish - due).TotalDays),
            ShiftDays = Math.Max(0, (finish - item.Finish).TotalDays)
        });

        cursor = finish;
        prev = item.ProductModel;
    }

    /// <summary>按统一四维权重给一个方案打分并补齐优缺点说明。</summary>
    private static UrgentPlan BuildPlan(string id, string name, string desc, string basis, string line,
        ErpPo po, DateTime at, DateTime start, DateTime finish, int changeover,
        List<UrgentAffectedWo> affected, int extraChangeoverMinutes, UrgentBaseline baseline, string note)
    {
        // 「受影响」= 被顺延（ShiftDays），不是"超交期"：种子工单的交期多在历史，本身就超期，
        // 那个超期不该算到本次插单头上。超交期照实展示，但不进评分。
        var affectedCount = affected.Count(a => a.ShiftDays > 0.01);
        var maxDelay = affected.Count == 0 ? 0 : affected.Max(a => a.DelayDays);
        var maxShift = affected.Count == 0 ? 0 : affected.Max(a => a.ShiftDays);

        var onTime = finish <= po.DueDate;
        var lateDays = Math.Max(0, (finish - po.DueDate).TotalDays);
        var onTimeScore = onTime ? 100 : Math.Clamp(100 - lateDays * 25, 0, 100);

        // 老单影响 = 顺延量（插单让老单多等多久）+ 受影响张数。超交期只作展示，不进评分。
        var affectScore = Math.Clamp(100 - maxShift * 12 - affectedCount * 5, 0, 100);
        var changeScore = Math.Clamp(100 - changeover * 1.5, 0, 100);

        var lineHours = GetLineLoad(line, at).RemainingHours + (finish - start).TotalDays * SchedulingRules.DailyCapacityHours;
        var loadScore = Math.Clamp(100 - lineHours / SchedulingRules.DailyCapacityHours * 2, 0, 100);

        var scores = new List<UrgentScoreDim>
        {
            new() { Dimension = "紧急单准时交付率", Weight = 0.40, Score = onTimeScore,
                    Reason = onTime ? $"预计 {finish:MM-dd HH:mm} 完工，交期 {po.DueDate:MM-dd} 内" : $"预计延期 {lateDays:0.#} 天" },
            new() { Dimension = "老单交期影响最小", Weight = 0.30, Score = affectScore,
                    Reason = affectedCount == 0 ? "无老单被顺延"
                        : $"{affectedCount} 单被顺延，最大顺延 {maxShift:0.#} 天"
                          + (maxDelay > 0.01 ? $"（其中最大超交期 {maxDelay:0.#} 天，属老单自身历史交期，不计入评分）" : "") },
            new() { Dimension = "换型成本最低", Weight = 0.20, Score = changeScore,
                    Reason = $"换型约 {changeover} 分钟" + (extraChangeoverMinutes > 0 ? $"（另加 {extraChangeoverMinutes} 分钟）" : "") },
            new() { Dimension = "设备负载均衡", Weight = 0.10, Score = loadScore,
                    Reason = $"{line} 负载约 {lineHours:0.#}h" }
        };
        var total = Math.Round(scores.Sum(x => x.Score * x.Weight), 1);

        var plan = new UrgentPlan
        {
            Id = id, Name = name, Desc = desc, Line = line,
            TimelineBasis = basis,
            UrgentStart = start, UrgentFinish = finish, UrgentOnTime = onTime,
            AffectedWoCount = affectedCount, MaxDelayDays = maxDelay, MaxShiftDays = maxShift, Affected = affected,
            ChangeoverMinutes = changeover + extraChangeoverMinutes,
            GainVsBaselineDays = Math.Round((baseline.Finish - finish).TotalDays, 2),
            CapacityLossHours = (finish - start).TotalDays * SchedulingRules.DailyCapacityHours
                                + (affectedCount > 0 ? (finish - start).TotalDays * SchedulingRules.DailyCapacityHours * 0.2 : 0),
            Total = total, Scores = scores
        };

        if (onTime) plan.Pros.Add($"紧急单 {finish:MM-dd HH:mm} 可准时交付");
        else plan.Cons.Add($"紧急单将延期 {lateDays:0.#} 天");
        if (affectedCount == 0) plan.Pros.Add("对在制老单零影响");
        else plan.Cons.Add($"{affectedCount} 单老单被顺延，最大顺延 {maxShift:0.#} 天");
        if (plan.GainVsBaselineDays > 0.01) plan.Pros.Add($"比排队（基线）提前 {plan.GainVsBaselineDays:0.#} 天");
        else if (plan.GainVsBaselineDays < -0.01) plan.Cons.Add($"比排队（基线）晚 {-plan.GainVsBaselineDays:0.#} 天");
        if (note.Length > 0) plan.Pros.Add(note);

        return plan;
    }
}
