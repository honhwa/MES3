using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 紧急插单：局部重排三方案 What-if + 自动评分（技术方案 2.3）
//   · 冲突域：只算推荐线（及其在制老单），不做全局重排；
//   · 三方案：老单延后 / 拆分插空 / 转次优线；
//   · 评分权重：紧急单准时 40% > 老单影响 30% > 换型成本 20% > 负载均衡 10%。
// 纯规则计算、纯查询零副作用（"应用建议"在页面层为演示，不改真实排程）。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>模拟紧急插单的三方案（What-if）。纯查询，零副作用。</summary>
    public static UrgentInsertSimulation? SimulateUrgentInsert(string poNo, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var po = FindErpPo(poNo);
        if (po is null) return null;

        var eval = EvaluateLineMatch(po.PoNo, at);
        var mainLine = string.IsNullOrWhiteSpace(eval.RecommendedLine)
            ? (KnownLines().FirstOrDefault() ?? "L1") : eval.RecommendedLine;
        var altLine = eval.Candidates
            .Where(s => s.Allocatable && !LineEquals(s.Line, mainLine))
            .Select(s => s.Line).FirstOrDefault() ?? mainLine;

        var hist = GetProductionHistory(po.ProductModel);
        double yld = hist.YieldFromHistory ? hist.AvgYield : SchedulingRules.DefaultYield;
        double unit = hist.HoursFromHistory ? hist.UnitHours : SchedulingRules.DefaultUnitHours;
        int inputQty = (int)Math.Ceiling(po.OrderQty / Math.Max(0.01, yld));
        double urgentHours = inputQty * unit;
        double urgentDays = urgentHours / SchedulingRules.DailyCapacityHours;

        var co = BuildChangeover(mainLine, po.ProductModel);
        int mainChange = co?.EstimatedMinutes ?? 10;

        var p1 = BuildUrgentPlan("P1", "方案一：老单延后",
            $"紧急单优先占用 {mainLine}，冲突时段低优先工单顺延至紧急单完工后（局部重排）。",
            mainLine, po, at, urgentDays, urgentHours, mainChange, split: false, eval);

        var p2 = BuildUrgentPlan("P2", "方案二：拆分插空",
            $"把紧急单拆两段，插入 {mainLine} 在制工单空隙，减少老单顺延（换型按两次计）。",
            mainLine, po, at, urgentDays, urgentHours, mainChange * 2, split: true, eval);

        var coAlt = BuildChangeover(altLine, po.ProductModel);
        var p3 = BuildUrgentPlan("P3", "方案三：转次优线",
            $"紧急单转排次优匹配线体 {altLine}，对 {mainLine} 老单零影响，但换型成本更高。",
            altLine, po, at, urgentDays, urgentHours, coAlt?.EstimatedMinutes ?? 35, split: false, eval);

        var sim = new UrgentInsertSimulation
        {
            PoNo = po.PoNo, ProductModel = po.ProductModel, OrderQty = po.OrderQty,
            DueDate = po.DueDate, UrgentHours = urgentHours, MainLine = mainLine, AltLine = altLine,
            Plans = new List<UrgentPlan> { p1, p2, p3 }
        };
        sim.RecommendedPlanId = sim.Plans.OrderByDescending(p => p.Total).First().Id;
        sim.Notes.Add($"冲突域：紧急单 {po.ProductModel} 需 {urgentHours:0.#}h（≈{urgentDays:0.#} 个生产日），优先线 {mainLine}；受影响范围 = 该线在制低优先工单（局部重排，不全局打乱）。");
        sim.Notes.Add("评分权重：紧急单准时交付 40% > 老单交期影响 30% > 换型成本 20% > 负载均衡 10%。");
        return sim;
    }

    private static UrgentPlan BuildUrgentPlan(string id, string name, string desc, string line,
        ErpPo po, DateTime at, double urgentDays, double urgentHours,
        int changeover, bool split, LineMatchEvaluation eval)
    {
        var start = at;
        var finish = at.AddDays(urgentDays);

        // 受影响老单：按交期顺序顺延
        var busy = finish;
        var affected = new List<UrgentAffectedWo>();
        foreach (var w in WorkOrders.Where(x => IsOpenWorkOrder(x) && LineEquals(x.Line, line)).OrderBy(x => x.DueDate))
        {
            var rem = Math.Max(0, w.PlanQty - w.FinishedQty);
            if (rem <= 0) continue;
            var unit = (w.WorkHours > 0 && w.FinishedQty > 0)
                ? w.WorkHours / w.FinishedQty
                : GetProductionHistory(w.ProductModel).UnitHours;
            var days = rem * unit / SchedulingRules.DailyCapacityHours;
            var rawFinish = busy.AddDays(days);
            var newFinish = split
                ? w.DueDate.AddDays(Math.Max(0, (rawFinish - w.DueDate).TotalDays) * 0.4)  // 拆分插空：顺延减少 60%
                : rawFinish;
            var delay = Math.Max(0, (newFinish - w.DueDate).TotalDays);
            affected.Add(new UrgentAffectedWo
            {
                WoNo = w.WoNo, ProductModel = w.ProductModel, DueDate = w.DueDate,
                OldFinish = w.DueDate, NewFinish = newFinish, DelayDays = delay
            });
            busy = rawFinish;
        }
        var affectedCount = affected.Count(a => a.DelayDays > 0.01);
        var maxDelay = affected.Count > 0 ? affected.Max(a => a.DelayDays) : 0;

        bool onTime = finish <= po.DueDate;
        double lateDays = Math.Max(0, (finish - po.DueDate).TotalDays);
        double onTimeScore = onTime ? 100 : Math.Clamp(100 - lateDays * 25, 0, 100);
        double affectScore = Math.Clamp(100 - maxDelay * 20 - affectedCount * 5, 0, 100);
        double changeScore = Math.Clamp(100 - changeover * 1.5, 0, 100);
        var lineHours = GetLineLoad(line, at).RemainingHours + urgentHours;
        double loadScore = Math.Clamp(100 - lineHours / SchedulingRules.DailyCapacityHours * 2, 0, 100);

        var scores = new List<UrgentScoreDim>
        {
            new(){ Dimension="紧急单准时交付率", Weight=0.40, Score=onTimeScore,
                   Reason= onTime ? $"预计 {finish:MM-dd} 完工，交期 {po.DueDate:MM-dd} 内" : $"预计延期 {lateDays:0.#} 天" },
            new(){ Dimension="老单交期影响最小", Weight=0.30, Score=affectScore,
                   Reason= affectedCount==0 ? "无老单受影响" : $"{affectedCount} 单受影响，最大延期 {maxDelay:0.#} 天" },
            new(){ Dimension="换型成本最低", Weight=0.20, Score=changeScore,
                   Reason= $"换型约 {changeover} 分钟" + (split ? "（拆两段×2）" : "") },
            new(){ Dimension="设备负载均衡", Weight=0.10, Score=loadScore,
                   Reason= $"{line} 负载 {lineHours:0.#}h" },
        };
        var total = Math.Round(scores.Sum(s => s.Score * s.Weight), 1);

        var plan = new UrgentPlan
        {
            Id = id, Name = name, Desc = desc, Line = line,
            UrgentStart = start, UrgentFinish = finish, UrgentOnTime = onTime,
            AffectedWoCount = affectedCount, MaxDelayDays = maxDelay, Affected = affected,
            ChangeoverMinutes = changeover,
            CapacityLossHours = urgentHours + (affectedCount > 0 ? urgentHours * 0.2 : 0),
            Total = total, Scores = scores
        };

        if (onTime) plan.Pros.Add($"紧急单 {finish:MM-dd} 可准时交付");
        else plan.Cons.Add($"紧急单将延期 {lateDays:0.#} 天");
        if (affectedCount == 0) plan.Pros.Add("对在制老单零影响");
        else plan.Cons.Add($"{affectedCount} 单老单顺延，最大 {maxDelay:0.#} 天");
        if (split) plan.Pros.Add("拆分插空，降低老单顺延");
        if (!LineEquals(line, eval.RecommendedLine))
            plan.Pros.Add($"转排 {line}，规避 {eval.RecommendedLine} 冲突");
        return plan;
    }
}
