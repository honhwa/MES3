using System.Linq;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 碳资产管理 —— 数据层（纯演示：内存静态台账，不接数据库）
//
// 两类台账：碳配额（CarbonAllowance）+ CCER（CcerRecord）。
// 履约测算复用阶段二 BuildCarbonInventory 取「年度实际排放」，与配额/CCER 持有量比对。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>碳配额台账（流水，唯一序由 Seq 保证）</summary>
    public static List<CarbonAllowance> CarbonAllowances { get; } = new();

    /// <summary>CCER 台账（流水）</summary>
    public static List<CcerRecord> CcerRecords { get; } = new();

    private static bool _carbonAssetReady;
    private static int _assetSeq;

    /// <summary>
    /// 碳资产演示种子：2024 / 2025 / 2026 三个履约年度，**故意构造三种不同结果**便于演示：
    ///   · 2024 配额有盈余        → 履约达标，结余有富余
    ///   · 2025 配额临界不足      → 默认 5% 抵不完留小缺口；把比例调到 8% 即可补平（what-if 演示点）
    ///   · 2026 履约年度进行中    → 排放只统计到当前月，配额充足，达标
    /// 数量按各年模拟排放量标定（2024≈1667t / 2025≈1531t / 2026≈2979t）。
    /// </summary>
    public static void EnsureCarbonAssetSeed()
    {
        if (_carbonAssetReady) return;

        // ---------- 2024：配额有盈余，履约达标 ----------
        AppendAllowance("2024", CarbonAssetTypes.AllowFree, 1800, 0, new DateTime(2024, 1, 15), "2024 年度免费配额分配");
        AppendAllowance("2024", CarbonAssetTypes.AllowSurrender, 1667, 0, new DateTime(2025, 1, 20), "2024 年度履约清缴（排放 1667.12 t，配额有富余）");
        AppendCcer("2024", "林业碳汇方法学", CarbonAssetTypes.CcerIssue, 200, 0, new DateTime(2024, 4, 10), "2024 年 CCER 核证签发");

        // ---------- 2025：配额临界不足，默认 5% 留小缺口 ----------
        AppendAllowance("2025", CarbonAssetTypes.AllowFree, 1200, 0, new DateTime(2025, 1, 15), "2025 年度免费配额分配（收紧）");
        AppendAllowance("2025", CarbonAssetTypes.AllowPaid, 250, 72, new DateTime(2025, 3, 18), "2025 有偿竞价发放");
        AppendAllowance("2025", CarbonAssetTypes.AllowSurrender, 1527, 0, new DateTime(2026, 1, 22), "2025 年度履约清缴（配额 1450 + CCER 76.55，仍差约 4.5 t）");
        AppendCcer("2025", "风光储自备电项目", CarbonAssetTypes.CcerIssue, 150, 0, new DateTime(2025, 3, 5), "2025 年 CCER 核证签发");
        AppendCcer("2025", "林业碳汇采购", CarbonAssetTypes.CcerBuy, 50, 58, new DateTime(2025, 6, 12), "市场购买 CCER 补充抵消量");

// ---------- 2026：履约年度进行中，排放只统计到当前月 ----------
      AppendAllowance("2026", CarbonAssetTypes.AllowFree, 3000, 0, new DateTime(2026, 1, 15), "2026 年度免费配额分配（按上年排放量核定）");
        AppendAllowance("2026", CarbonAssetTypes.AllowPaid, 200, 80, new DateTime(2026, 3, 20), "有偿竞价发放");
   AppendAllowance("2026", CarbonAssetTypes.AllowSurrender, 2980, 0, new DateTime(2026, 12, 20), "年度履约清缴（预登记，排放 2979.1 t）");
        AppendCcer("2026", "风光储自备电项目", CarbonAssetTypes.CcerIssue, 800, 0, new DateTime(2026, 2, 1), "自愿减排项目签发");
 AppendCcer("2026", "林业碳汇采购", CarbonAssetTypes.CcerBuy, 300, 60, new DateTime(2026, 5, 15), "市场购买 CCER");

        _carbonAssetReady = true;
    }

    // ---- 内部追加（不重复校验，种子与公开录入共用）----
    private static void AppendAllowance(string year, string txType, double qty, double price, DateTime date, string note)
    {
        var sign = CarbonAssetRules.IsInflow("A", txType) ? 1 : -1;
        CarbonAllowances.Add(new CarbonAllowance
        {
            Seq = ++_assetSeq,
            Year = year,
            TxType = txType,
            Quantity = qty,
            UnitPrice = price,
            Amount = sign * qty * price,
            TxDate = date,
            Note = note
        });
    }

    private static void AppendCcer(string year, string project, string txType, double qty, double price, DateTime date, string note)
    {
        var sign = CarbonAssetRules.IsInflow("C", txType) ? 1 : -1;
        CcerRecords.Add(new CcerRecord
        {
            Seq = ++_assetSeq,
            Year = year,
            Project = project,
            TxType = txType,
            Quantity = qty,
            UnitPrice = price,
            Amount = sign * qty * price,
            TxDate = date,
            Note = note
        });
    }

    // ---- 公开录入（带校验）----
    public static (bool Ok, string Message) AddAllowance(
        string year, string txType, double qty, double price, DateTime date, string? note)
    {
        var y = (year ?? "").Trim();
        if (y.Length == 0) return (false, "请填写年度。");
        if (!CarbonAssetTypes.AllowanceTypes.Contains(txType)) return (false, "记录类型非法。");
        if (qty <= 0) return (false, "数量必须大于 0。");
        if (price < 0) return (false, "单价不能为负。");
        if (date.Year is < 2000 or > 2100) return (false, "日期不合理。");

        EnsureCarbonAssetSeed();
        AppendAllowance(y, txType, qty, price, date, (note ?? "").Trim());
        var sign = CarbonAssetRules.IsInflow("A", txType) ? "+" : "-";
        return (true, $"已录入配额记录：{txType} {qty:0.###} t（{sign}）");
    }

    public static (bool Ok, string Message) AddCcer(
        string year, string project, string txType, double qty, double price, DateTime date, string? note)
    {
        var y = (year ?? "").Trim();
        if (y.Length == 0) return (false, "请填写年度。");
        if ((project ?? "").Trim().Length == 0) return (false, "请填写来源/项目。");
        if (!CarbonAssetTypes.CcerTypes.Contains(txType)) return (false, "记录类型非法。");
        if (qty <= 0) return (false, "数量必须大于 0。");
        if (price < 0) return (false, "单价不能为负。");
        if (date.Year is < 2000 or > 2100) return (false, "日期不合理。");

        EnsureCarbonAssetSeed();
        AppendCcer(y, (project ?? "").Trim(), txType, qty, price, date, (note ?? "").Trim());
        var sign = CarbonAssetRules.IsInflow("C", txType) ? "+" : "-";
        return (true, $"已录入 CCER 记录：{txType} {qty:0.###} t（{sign}）");
    }

    // ---- 查询（带运行余额）----
    public static List<CarbonAllowance> GetAllowances(string? year = null)
    {
        EnsureCarbonAssetSeed();
        var y = (year ?? "").Trim();
        var list = CarbonAllowances
            .Where(a => y.Length == 0 || string.Equals(a.Year, y, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.TxDate).ThenBy(a => a.Seq)
            .ToList();
        var bal = 0.0;
        foreach (var a in list)
        {
            bal += CarbonAssetRules.IsInflow("A", a.TxType) ? a.Quantity : -a.Quantity;
            a.BalanceAfter = bal;
        }
        return list;
    }

    public static List<CcerRecord> GetCcerRecords(string? year = null)
    {
        EnsureCarbonAssetSeed();
        var y = (year ?? "").Trim();
        var list = CcerRecords
            .Where(c => y.Length == 0 || string.Equals(c.Year, y, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.TxDate).ThenBy(c => c.Seq)
            .ToList();
        var bal = 0.0;
        foreach (var c in list)
        {
            bal += CarbonAssetRules.IsInflow("C", c.TxType) ? c.Quantity : -c.Quantity;
            c.BalanceAfter = bal;
        }
        return list;
    }

    public static double ComputeAllowanceBalance(string year) =>
        GetAllowances(year).Sum(a => CarbonAssetRules.IsInflow("A", a.TxType) ? a.Quantity : -a.Quantity);

    /// <summary>
    /// 年度**可用于履约的配额量** = 流入（免费/有偿/买入）− 非清缴流出（政府收回/市场卖出）。
    /// ⚠ **"履约清缴"必须排除**：清缴是"履约这个动作的结果"，不是可消耗的资源。
    /// 若把清缴也算成流出，则清缴越多、年底结余越少，履约测算反而显示缺口越大——语义反了。
    /// （2026 演示数据就踩过：全年清缴 13500 t，年底结余 1000 t，
    ///   却被判成"缺口 1830 t"，明明配额绰绰有余。）
    /// </summary>
    public static double ComputeAllowanceAvailable(string year)
    {
        var y = (year ?? "").Trim();
        return GetAllowances(y).Sum(a =>
        {
            var inflow = CarbonAssetRules.IsInflow("A", a.TxType);
            var isSurrender = a.TxType == CarbonAssetTypes.AllowSurrender;
            if (inflow) return a.Quantity;
            return isSurrender ? 0 : -a.Quantity;
        });
    }

    public static double ComputeCcerBalance(string year) =>
        GetCcerRecords(year).Sum(c => CarbonAssetRules.IsInflow("C", c.TxType) ? c.Quantity : -c.Quantity);

    /// <summary>可选年度列表（配额 + CCER 的并集，升序）。</summary>
    public static List<string> CarbonAssetYears()
    {
        EnsureCarbonAssetSeed();
        return CarbonAllowances.Select(a => a.Year)
            .Union(CcerRecords.Select(c => c.Year))
            .Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>
    /// 履约测算：实际排放（阶段二全厂核算）× 配额持有 × 可用 CCER 抵消 → 缺口/盈余/成本。
    /// </summary>
    /// <param name="year">履约年度。</param>
    /// <param name="ccerOffsetRatio">
    /// CCER 抵消比例上限（what-if 用）。传 null 取规则默认值 5%；传值则按该比例试算，
    /// 用于演示"若政策放宽到 8%，缺口能否补平"。**不修改任何台账数据**（纯查询）。
    /// </param>
    public static CarbonComplianceView BuildComplianceView(string year, double? ccerOffsetRatio = null)
    {
        EnsureCarbonAssetSeed();
        var y = (year ?? "").Trim();

        var inv = BuildCarbonInventory(EnergyScopes.Plant, "", $"{y}-01-01", $"{y}-12-31");
        var emission = inv.TotalKg / 1000.0;

        var allow = ComputeAllowanceAvailable(y);
        var ccer = ComputeCcerBalance(y);

        var ratio = ccerOffsetRatio ?? CarbonAssetRules.CcerOffsetRatio;
        if (ratio < 0) ratio = 0;
        if (ratio > 1) ratio = 1;

        var cap = Math.Round(emission * ratio, 3);
        var ccerUsable = Math.Min(ccer, cap);

        var usedAllow = Math.Min(allow, emission);
        var remainE = Math.Max(0.0, emission - usedAllow);
        var ccerUsed = Math.Min(ccerUsable, remainE);
        var shortfall = Math.Max(0.0, remainE - ccerUsed);
        var surplus = Math.Max(0.0, allow + ccerUsable - emission);

// 历史支出 = 有偿竞价发放 + 市场买入 + CCER 市场购买（免费配额/项目签发不花钱）
      var histCost = CarbonAllowances
   .Where(a => string.Equals(a.Year, y, StringComparison.OrdinalIgnoreCase)
  && (a.TxType == CarbonAssetTypes.AllowBuy || a.TxType == CarbonAssetTypes.AllowPaid))
   .Sum(a => a.Amount)
+ CcerRecords
      .Where(c => string.Equals(c.Year, y, StringComparison.OrdinalIgnoreCase) && c.TxType == CarbonAssetTypes.CcerBuy)
  .Sum(c => c.Amount);
        var projCost = shortfall * CarbonAssetRules.AllowanceMarketPrice;
        var total = histCost + projCost;

        return new CarbonComplianceView
        {
            Year = y,
            CcerOffsetRatioUsed = ratio,
            ActualEmissionT = emission,
            AllowanceBalanceT = allow,
            CcerBalanceT = ccer,
            CcerOffsetCapT = cap,
            CcerUsableT = ccerUsable,
            AllowanceUsedT = usedAllow,
            CcerUsedT = ccerUsed,
            ShortfallT = shortfall,
            SurplusT = surplus,
            HistoricalCost = histCost,
            ProjectedCost = projCost,
            TotalCost = total
        };
    }
}
