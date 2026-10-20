using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 排产时间线内核 —— 智能排产 / 模拟插单 / 交期承诺(CTP) 共用的唯一时间算法
//
// 统一前的三套口径（2026-10-20 排查结论）：
//   ① 智能排产：开工 = 线体腾出（at + Σ工时/16h），完工 = 开工 + 工时，**不含换型**；
//   ② 插单模拟：开工 = at（无视在制），完工 = at + 工时，老单从紧急单完工往后顺延，**不含换型**；
//   ③ CTP     ：开工 = 线体腾出 + 单次换型，**含换型**。
//   同一个 PO 在 ①② 之间差 0.7~0.9 天（= 线体在制剩余工时），而 ③ 又比 ① 多算换型。
//
// 统一后的唯一规则（本文件）：
//   线体任务按交期排队 → 每段 = 换型(上一型号→本型号) + 工时÷16h → 依次累加。
//   第一个任务从 now 开始（线体此刻就在干活）。换型时间来自换型矩阵 GetChangeoverInfo，
//   同型号连续为 0，首次上线只清线。
//
// 三种排法 = 同一条时间线上的三种插入位置（见 UrgentInsert 的基线/方案一/方案二/方案三）。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>
    /// 构建某条线的时间线：在制工单按交期排队，逐段累加「换型 + 生产」。
    /// 纯查询，只读工单、不改任何数据。
    /// </summary>
    /// <param name="line">线体</param>
    /// <param name="now">时间线起点（默认现在；自测注入固定值）</param>
    public static LineTimeline BuildLineTimeline(string? line, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var tl = new LineTimeline { Line = (line ?? "").Trim(), At = at };
        if (tl.Line.Length == 0) return tl;

        var cursor = at;
        var prevModel = "";

        foreach (var w in WorkOrders
                     .Where(x => IsOpenWorkOrder(x) && LineEquals(x.Line, tl.Line))
                     .OrderBy(x => x.DueDate)
                     .ThenBy(x => x.WoNo, StringComparer.Ordinal))
        {
            var remaining = Math.Max(0, w.PlanQty - w.FinishedQty);
            if (remaining <= 0) continue;

            // 单位工时口径与既有负载计算保持一致：本单报过工时就用自己的，否则借同型号历史值
            var unit = w.WorkHours > 0 && w.FinishedQty > 0
                ? w.WorkHours / w.FinishedQty
                : GetProductionHistory(w.ProductModel).UnitHours;
            var hours = remaining * unit;

            // 换型：上一型号 → 本型号（首单为首次上线，只清线）
            var co = GetChangeoverInfo(prevModel, w.ProductModel);
            cursor = cursor.AddMinutes(co.Minutes);

            var start = cursor;
            var finish = start.AddDays(hours / SchedulingRules.DailyCapacityHours);

            tl.Items.Add(new LineTimelineItem
            {
                TaskNo = w.WoNo,
                IsWorkOrder = true,
                ProductModel = w.ProductModel,
                Qty = remaining,
                Hours = hours,
                ChangeoverMinutes = co.Minutes,
                Start = start,
                Finish = finish,
                DueDate = w.DueDate
            });

            cursor = finish;
            prevModel = w.ProductModel;
        }

        return tl;
    }

    /// <summary>
    /// 把一个新任务追加到线体队尾（= 智能排产的推荐口径），返回开工/完工/换型。
    /// 队尾追加不打扰任何在制工单，所以老单零顺延。
    /// </summary>
    public static (DateTime Start, DateTime Finish, int ChangeoverMinutes, LineTimeline Timeline)
        AppendToLineTail(string? line, string productModel, double hours, DateTime? now = null)
    {
        var tl = BuildLineTimeline(line, now);
        var co = GetChangeoverInfo(tl.TailModel, productModel);
        var start = tl.TailFinish.AddMinutes(co.Minutes);
        var finish = start.AddDays(hours / SchedulingRules.DailyCapacityHours);
        return (start, finish, co.Minutes, tl);
    }

    /// <summary>
    /// 把一个新任务插到线体队首（= 插单「老单延后」口径），返回紧急单起止与受影响老单。
    /// 老单从紧急单完工之后依次顺延，段间同样计换型。
    /// </summary>
    public static (DateTime Start, DateTime Finish, int ChangeoverMinutes, List<UrgentAffectedWo> Affected)
        InsertAtLineHead(LineTimeline timeline, string productModel, double hours)
    {
        var affected = new List<UrgentAffectedWo>();
        var cursor = timeline.At;

        // 插队时线体正在跑队首那个型号 → 换型起点就是它
        var co = GetChangeoverInfo(timeline.HeadModel, productModel);
        cursor = cursor.AddMinutes(co.Minutes);
        var start = cursor;
        var finish = cursor.AddDays(hours / SchedulingRules.DailyCapacityHours);
        cursor = finish;
        var prev = productModel;

        foreach (var item in timeline.Items)
        {
            var c = GetChangeoverInfo(prev, item.ProductModel);
            cursor = cursor.AddMinutes(c.Minutes);
            var s = cursor;
            var f = s.AddDays(item.Hours / SchedulingRules.DailyCapacityHours);

            affected.Add(new UrgentAffectedWo
            {
                WoNo = item.TaskNo,
                ProductModel = item.ProductModel,
                DueDate = item.DueDate ?? default,
                OldFinish = item.Finish,
                NewFinish = f,
                DelayDays = Math.Max(0, (f - (item.DueDate ?? f)).TotalDays),
                ShiftDays = Math.Max(0, (f - item.Finish).TotalDays)
            });

            cursor = f;
            prev = item.ProductModel;
        }

        return (start, finish, co.Minutes, affected);
    }
}
