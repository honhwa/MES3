using System;
using System.Collections.Generic;
using System.Linq;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 智能排产甘特图 —— 数据层
//
// 画什么：把「待下发 ERP 订单」经排产引擎 RecommendSchedule 产出的**建议排期**
//         （推荐线体 + 建议开工/完工）画成甘特条。纵轴产线、横轴日期，一眼看出
//         未来各产线被哪些订单占用、哪里赶不及交期。
//
// 口径（务必如实）：
//   · 每张待下发订单**独立**按当前线负载 RecommendSchedule，未累加同批订单相互影响
//     （真实批量排产要串行更新负载，这里做演示近似，工具条已注明）；
//   · 交期余量 SlackDays<0 或 预计完工晚于交期 → 标"来不及"（红）；
//   · 纯查询/计算，零副作用（RecommendSchedule 只读仓储，不建工单）。
// ============================================================================

/// <summary>甘特条：一张待下发订单的排产建议排期。</summary>
public sealed class GanttBar
{
    public string PoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int OrderQty { get; set; }
    public string Line { get; set; } = "";
    public DateTime Start { get; set; }      // 建议开工（按天）
    public DateTime Finish { get; set; }     // 预计完工（按天）
    public DateTime DueDate { get; set; }    // 交期（按天）
    public double SlackDays { get; set; }
    public string Risk { get; set; } = "";
    public string RiskText { get; set; } = "";

    /// <summary>是否赶不及交期（预计完工晚于交期）。</summary>
    public bool Late => Finish.Date > DueDate.Date;
}

/// <summary>排产甘特图数据。</summary>
public sealed class ScheduleGantt
{
    public DateTime From { get; set; }
    public List<DateTime> Dates { get; set; } = new();   // 横轴日期（按天升序）
    public int TodayIndex { get; set; }
    public List<string> Lines { get; set; } = new();     // 纵轴产线
    public List<GanttBar> Bars { get; set; } = new();

    /// <summary>某产线某一天被哪些甘特条占用。</summary>
    public List<GanttBar> BarsOn(string line, DateTime day) =>
        Bars.Where(b => b.Line == line && b.Start.Date <= day.Date && day.Date <= b.Finish.Date).ToList();
}

public static partial class MesDatabase
{
    /// <summary>
    /// 生成排产甘特图数据：对每张待下发 ERP 订单跑排产建议。纯查询，零副作用。
    /// </summary>
    /// <param name="days">窗口天数（从今天起），默认 30。</param>
    public static ScheduleGantt GetScheduleGantt(int days = 30)
    {
        days = Math.Clamp(days, 7, 90);
        var today = DateTime.Today;
        var from = today;
        var dates = Enumerable.Range(0, days).Select(i => from.AddDays(i)).ToList();

        var gantt = new ScheduleGantt
        {
            From = from,
            Dates = dates,
            TodayIndex = 0,                 // 窗口从今天起
            Lines = KnownLines()
        };

        foreach (var po in ErpPos.Where(p => p.Status == ErpPoStatus.Pending).OrderBy(p => p.DueDate))
        {
            ScheduleRecommendation? rec;
            try { rec = RecommendSchedule(po.PoNo, today); }
            catch { continue; }              // 单张订单推理异常不影响其他订单
            if (rec is null) continue;

            gantt.Bars.Add(new GanttBar
            {
                PoNo = rec.PoNo,
                ProductModel = rec.ProductModel,
                ProductName = rec.ProductName,
                OrderQty = rec.OrderQty,
                Line = rec.RecommendedLine,
                Start = rec.PlannedStart.Date,
                Finish = rec.PlannedFinish.Date,
                DueDate = rec.DueDate.Date,
                SlackDays = rec.SlackDays,
                Risk = rec.Risk,
                RiskText = rec.RiskText
            });
        }

        // 同线订单按交期顺序错开排布（批量排产近似）：保持各单推荐线与工期，
        // 仅把同线订单的开工顺序后移（起点接该线在制忙完时间），避免甘特条重叠、更接近真实批量排产。
        foreach (var grp in gantt.Bars.GroupBy(b => b.Line))
        {
            var line = grp.Key;
            DateTime busyUntil = gantt.From;
            try
            {
                var load = GetLineLoad(line, today);
                if (load != null && load.EarliestFreeTime > busyUntil) busyUntil = load.EarliestFreeTime.Date;
            }
            catch { /* 负载查询异常则从今天起排 */ }

            foreach (var b in grp.OrderBy(b => b.DueDate).ThenBy(b => b.Start))
            {
                var durDays = Math.Max(0.0, (b.Finish - b.Start).TotalDays);
                var start = b.Start > busyUntil ? b.Start : busyUntil;
                b.Start = start.Date;
                b.Finish = start.Date.AddDays(durDays);
                b.SlackDays = (b.DueDate.Date - b.Finish).TotalDays;   // 重算余量（Late 由 Finish/DueDate 派生）
                busyUntil = b.Finish.AddDays(1);                       // 下一张同线订单最早次日起
            }
        }
        return gantt;
    }
}
