namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 碳资产管理 —— 模型层（纯演示：内存台账，不接数据库）
//
// 两类资产台账：
//   ① 碳配额（Carbon Allowance）：政府发放 + 市场交易 + 履约清缴的持有量变动
//   ② CCER（国家核证自愿减排量）：项目签发 + 市场交易 + 履约抵消
//
// 核心口径（界面必须可见）：
//   · 台账是"持有量变动"流水，每笔记方向（流入+ / 流出-），并算运行余额
//   · CCER 履约抵消有比例上限（示范 5%），超出部分不能抵、只能买配额
//   · 履约 = 实际排放（阶段二核算） vs 配额持有 + 可用 CCER 抵消 → 缺口/盈余/成本
// ============================================================================

/// <summary>碳资产记录类型</summary>
public static class CarbonAssetTypes
{
    // 配额
    public const string AllowFree = "免费发放";
    public const string AllowPaid = "有偿发放";
    public const string AllowGovRecall = "政府收回";
    public const string AllowBuy = "市场买入";
    public const string AllowSell = "市场卖出";
    public const string AllowSurrender = "履约清缴";

    // CCER
    public const string CcerIssue = "项目签发";
    public const string CcerBuy = "市场买入";
    public const string CcerSell = "市场卖出";
    public const string CcerOffset = "履约抵消";

    public static readonly string[] AllowanceTypes =
        { AllowFree, AllowPaid, AllowGovRecall, AllowBuy, AllowSell, AllowSurrender };
    public static readonly string[] CcerTypes =
        { CcerIssue, CcerBuy, CcerSell, CcerOffset };
}

/// <summary>碳资产规则（计算口径唯一来源，别处不要另立）</summary>
public static class CarbonAssetRules
{
    /// <summary>该类型是否增加持有量（true=流入，false=流出）。</summary>
    public static bool IsInflow(string kind, string txType)
    {
        if (kind == "A")
            return txType is CarbonAssetTypes.AllowFree or CarbonAssetTypes.AllowPaid or CarbonAssetTypes.AllowBuy;
        if (kind == "C")
            return txType is CarbonAssetTypes.CcerIssue or CarbonAssetTypes.CcerBuy;
        return false;
    }

    /// <summary>CCER 履约抵消比例上限（示范 5%，正式以主管部门规定为准）。</summary>
    public const double CcerOffsetRatio = 0.05;

    /// <summary>演示用市场价格（元/tCO2e）。实际应对接碳市场行情接口。</summary>
    public const double AllowanceMarketPrice = 80.0;

    public const double CcerMarketPrice = 60.0;
}

/// <summary>碳配额台账流水（一条 = 一次持有量变动）</summary>
public class CarbonAllowance
{
    public int Seq { get; set; }
    public string Year { get; set; } = "";
    public string TxType { get; set; } = "";
    public double Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double Amount { get; set; }
    public DateTime TxDate { get; set; } = DateTime.Now;
    public string Note { get; set; } = "";
    public double BalanceAfter { get; set; }

    public bool IsInflow => CarbonAssetRules.IsInflow("A", TxType);
    public string TypeText => TxType;
    public string QtyText => (IsInflow ? "+" : "-") + $"{Quantity:0.###}";
    public string PriceText => $"{UnitPrice:0.##} 元/t";
    public string AmountText => $"{(Amount >= 0 ? "+" : "")}{Amount:0.##} 元";
    public string DateText => TxDate.ToString("yyyy-MM-dd");
    public string BalanceText => $"{BalanceAfter:0.###} t";
}

/// <summary>CCER 台账流水</summary>
public class CcerRecord
{
    public int Seq { get; set; }
    public string Year { get; set; } = "";
    public string Project { get; set; } = "";
    public string TxType { get; set; } = "";
    public double Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double Amount { get; set; }
    public DateTime TxDate { get; set; } = DateTime.Now;
    public string Note { get; set; } = "";
    public double BalanceAfter { get; set; }

    public bool IsInflow => CarbonAssetRules.IsInflow("C", TxType);
    public string TypeText => TxType;
    public string QtyText => (IsInflow ? "+" : "-") + $"{Quantity:0.###}";
    public string PriceText => $"{UnitPrice:0.##} 元/t";
    public string AmountText => $"{(Amount >= 0 ? "+" : "")}{Amount:0.##} 元";
    public string DateText => TxDate.ToString("yyyy-MM-dd");
    public string BalanceText => $"{BalanceAfter:0.###} t";
}

/// <summary>履约测算视图（年度）</summary>
public class CarbonComplianceView
{
    public string Year { get; set; } = "";
    public double CcerOffsetRatioUsed { get; set; }    // 本次试算实际用的CCER抵消比例
    public double ActualEmissionT { get; set; }      // 实际排放（来自阶段二核算，tCO2e）
    public double AllowanceBalanceT { get; set; }    // 配额年底结余
    public double CcerBalanceT { get; set; }         // CCER 年底结余
    public double CcerOffsetCapT { get; set; }       // CCER 抵消上限（=实际排放×5%）
    public double CcerUsableT { get; set; }          // 实际可用于抵消的 CCER
    public double AllowanceUsedT { get; set; }       // 用配额清缴的量
    public double CcerUsedT { get; set; }            // 用 CCER 抵消的量
    public double ShortfallT { get; set; }           // 配额+CCER 抵完后仍缺口
    public double SurplusT { get; set; }             // 盈余（>0 表示富余）
    public double HistoricalCost { get; set; }       // 已发生的购买支出
    public double ProjectedCost { get; set; }        // 预估还需购买支出
    public double TotalCost { get; set; }            // 总履约成本

    public bool Compliant => ShortfallT <= 1e-9;

    /// <summary>本次试算用的抵消比例文本（如 5% / 8%）。</summary>
    public string RatioText => $"{CcerOffsetRatioUsed * 100:0.##}%";

    /// <summary>非默认比例时给出提示，提醒当前是 what-if 试算而非政策口径。</summary>
    public string ScenarioText =>
        Math.Abs(CcerOffsetRatioUsed - CarbonAssetRules.CcerOffsetRatio) < 1e-9
            ? $"CCER 抵消上限 {RatioText}（默认口径）"
            : $"CCER 抵消上限 {RatioText}（what-if 试算，非默认 5% 口径）";

    public string ActualText => $"{ActualEmissionT:0.###} t";
    public string AllowanceText => $"{AllowanceBalanceT:0.###} t";
    public string CcerText => $"{CcerBalanceT:0.###} t";
    public string CapText => $"{CcerOffsetCapT:0.###} t";
    public string ShortfallText =>
        $"{(ShortfallT > 0 ? "缺口 " : "盈余 ")}{Math.Abs(ShortfallT):0.###} t";
    public string StatusText => Compliant ? "履约达标" : "配额不足，需购买";
    public string CostText => $"{TotalCost:0.##} 元";
}
