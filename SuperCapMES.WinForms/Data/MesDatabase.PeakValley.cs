using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 峰谷电费「移峰填谷」节约测算 —— 仓储层
//
// 数据底座与口径（务必如实，演示纪律）：
//   · 月用电量：取自能耗台账全厂电力计量点（M-PLANT-ELEC 当月账期，kWh）——真实演示数据；
//   · 日负荷曲线：**演示假设**（典型工厂白天高/夜间低），把月用电摊到 24 小时——非实测；
//   · 分时电价：**演示参数**（尖峰/高峰/平段/低谷四时段），以当地电价政策为准——非财务决算；
//   · 节约额 = 峰段(尖峰+高峰)电量 × 转移比例 × (峰段加权均价 − 低谷价)，纯规则算术、可核对。
//
// 判定归规则：本测算只反映"电价差的理论节约空间"，不判定移峰是否可行
// （能否移要结合生产工艺、设备特性、订单节奏，由人决策）。断网/欠费一概无关——不调任何外部服务。
// ============================================================================

/// <summary>分时电价与时段划分（演示参数）。</summary>
public static class PeakValleyTariff
{
    public const double Sharp = 1.15;   // 尖峰 元/kWh
    public const double Peak = 0.92;    // 高峰
    public const double Flat = 0.60;    // 平段
    public const double Valley = 0.32;  // 低谷

    /// <summary>小时 → 时段名（0-23，无重叠、覆盖全天）。</summary>
    public static string PeriodOfHour(int h)
    {
        h = ((h % 24) + 24) % 24;
        if (h >= 23 || h < 7) return "低谷";
        if (h == 7 || (h >= 14 && h < 18) || (h >= 21 && h < 23)) return "平段";
        if ((h >= 8 && h < 10) || (h >= 12 && h < 14) || h == 20) return "高峰";
        return "尖峰";   // 10-12、18-20
    }

    /// <summary>小时 → 电价（元/kWh）。</summary>
    public static double PriceOfHour(int h) => PeriodOfHour(h) switch
    {
        "尖峰" => Sharp,
        "高峰" => Peak,
        "平段" => Flat,
        _ => Valley
    };

    /// <summary>四时段展示顺序 + 电价 + 主题色（页面复用）。</summary>
    public static readonly (string Name, double Price, string Color)[] Periods =
    {
        ("尖峰", Sharp, "#f4544c"),
        ("高峰", Peak, "#f5a524"),
        ("平段", Flat, "#2ecc8f"),
        ("低谷", Valley, "#35c6d4")
    };
}

/// <summary>峰谷电费测算结果。</summary>
public sealed class PeakValleyAnalysis
{
    public string Period { get; set; } = "";          // 账期 yyyy-MM
    public double MonthKwh { get; set; }              // 当月全厂用电（台账）
    public int DaysInMonth { get; set; }
    public int TransferPct { get; set; }              // 转移比例 %

    public double SharpKwh { get; set; }
    public double PeakKwh { get; set; }
    public double FlatKwh { get; set; }
    public double ValleyKwh { get; set; }
    public double SharpShare { get; set; }            // %
    public double PeakShare { get; set; }
    public double FlatShare { get; set; }
    public double ValleyShare { get; set; }

    public double CurrentCost { get; set; }           // 当前月电费（元）
    public double TransferKwh { get; set; }           // 拟转移电量（峰→谷，kWh）
    public double Saving { get; set; }                // 月度节约（元）
    public double OptimizedCost { get; set; }         // 优化后月电费（元）
    public double AnnualSaving { get; set; }          // 年化节约（元，×12）

    /// <summary>24 小时负荷占比（演示曲线，sum=1），供页面展示。</summary>
    public double[] LoadCurve { get; set; } = new double[24];
}

public static partial class MesDatabase
{
    /// <summary>演示典型日负荷曲线（相对权重，白天高/夜间低；代码内归一化到 sum=1）。非实测。</summary>
    private static readonly double[] PeakValleyLoad =
    {
        1.0, 0.9, 0.9, 0.9, 1.0, 1.1, 1.4,   // 0-6 夜间低谷
        2.0,                                 // 7 开班爬升
        3.0, 3.2, 3.3, 3.1,                  // 8-11 早班高峰
        2.7, 2.9,                            // 12-13 午班
        3.1, 3.2, 3.1, 3.0,                  // 14-17 下午
        2.8, 2.9,                            // 18-19
        2.5, 2.2,                            // 20-21
        1.7, 1.2                             // 22-23 收尾渐低
    };

    /// <summary>
    /// 峰谷电费「移峰填谷」节约测算。纯查询/计算，零副作用。
    /// </summary>
    /// <param name="transferPct">转移比例（峰段电量拟移到谷段的百分比），钳制 0~80。</param>
    public static PeakValleyAnalysis GetPeakValleyAnalysis(int transferPct)
    {
        transferPct = Math.Clamp(transferPct, 0, 80);
        var today = DateTime.Today;
        var period = ToPeriod(today);
        var reading = FindEnergyReading("M-PLANT-ELEC", period);
        double monthKwh = reading?.Value ?? 0;
        int days = DateTime.DaysInMonth(today.Year, today.Month);

        double wsum = PeakValleyLoad.Sum();
        var share = PeakValleyLoad.Select(x => x / wsum).ToArray();   // sum=1

        double sharp = 0, peak = 0, flat = 0, valley = 0;
        for (int h = 0; h < 24; h++)
        {
            double kwhH = monthKwh * share[h];
            switch (PeakValleyTariff.PeriodOfHour(h))
            {
                case "尖峰": sharp += kwhH; break;
                case "高峰": peak += kwhH; break;
                case "平段": flat += kwhH; break;
                default: valley += kwhH; break;
            }
        }

        double total = sharp + peak + flat + valley;
        double pct(double v) => total > 0 ? v / total * 100 : 0;

        double currentCost = sharp * PeakValleyTariff.Sharp
                           + peak * PeakValleyTariff.Peak
                           + flat * PeakValleyTariff.Flat
                           + valley * PeakValleyTariff.Valley;

        // 移峰填谷：把 (尖峰+高峰) 电量的 transferPct 移到低谷
        double peakKwh = sharp + peak;
        double transferKwh = peakKwh * transferPct / 100.0;
        double peakAvgPrice = peakKwh > 0
            ? (sharp * PeakValleyTariff.Sharp + peak * PeakValleyTariff.Peak) / peakKwh
            : 0;
        double saving = transferKwh * (peakAvgPrice - PeakValleyTariff.Valley);
        if (saving < 0) saving = 0;   // 谷价低于峰段均价，理论必正；防御性钳制

        return new PeakValleyAnalysis
        {
            Period = period,
            MonthKwh = monthKwh,
            DaysInMonth = days,
            TransferPct = transferPct,
            SharpKwh = sharp, PeakKwh = peak, FlatKwh = flat, ValleyKwh = valley,
            SharpShare = pct(sharp), PeakShare = pct(peak), FlatShare = pct(flat), ValleyShare = pct(valley),
            CurrentCost = currentCost,
            TransferKwh = transferKwh,
            Saving = saving,
            OptimizedCost = currentCost - saving,
            AnnualSaving = saving * 12,
            LoadCurve = share
        };
    }
}
