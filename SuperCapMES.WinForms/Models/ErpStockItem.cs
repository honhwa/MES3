namespace SuperCapMES.WinForms.Models;

/// <summary>
/// ERP 库存（ERP 侧账面库存快照，模拟"从 ERP 同步"的结果）。
/// 匹配口径与 MES 库存台账保持一致：**物料编码 + 批次 + 库位**（唯一键），便于逐键对账。
/// </summary>
public class ErpStockItem
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>ERP 账面数量</summary>
    public double Quantity { get; set; }

    /// <summary>该条库存的最后同步时间</summary>
    public DateTime LastSyncTime { get; set; }

    public string Remark { get; set; } = "";
}

/// <summary>MES ↔ ERP 库存对账状态常量</summary>
public static class StockCompareStatus
{
    /// <summary>数量一致</summary>
    public const string Matched = "一致";

    /// <summary>两侧数量不一致</summary>
    public const string Mismatch = "差异";

    /// <summary>MES 有该键、ERP 无记录</summary>
    public const string MesOnly = "ERP 无此键";

    /// <summary>ERP 有该键、MES 无记录</summary>
    public const string ErpOnly = "MES 无此键";

    public static readonly string[] All = { Matched, Mismatch, MesOnly, ErpOnly };
}

/// <summary>
/// MES ↔ ERP 库存对账行（库存台账每行一条；includeErpOnly 时附加"ERP 独有"行）。
/// 差异 = MES 数量 − ERP 数量：正值表示 MES 账面多于 ERP（实物多/ERP 少记），负值反之。
/// </summary>
public class WmsStockCompareRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>MES 库存数量（ERP 独有行时为 0）</summary>
    public double MesQty { get; set; }

    /// <summary>ERP 库存数量（ERP 无该键时为 null）</summary>
    public double? ErpQty { get; set; }

    /// <summary>差异 = MES − ERP（任一侧缺失时为 null）</summary>
    public double? Diff { get; set; }

    /// <summary>对账状态：一致 / 差异 / ERP 无此键 / MES 无此键</summary>
    public string Status { get; set; } = StockCompareStatus.Matched;

    /// <summary>MES 台账最后更新时间</summary>
    public DateTime? MesLastUpdate { get; set; }

    /// <summary>ERP 侧最后同步时间（无记录时为 null）</summary>
    public DateTime? ErpSyncTime { get; set; }

    /// <summary>MES 数量显示文本</summary>
    public string MesQtyText => MesQty == 0 && Status == StockCompareStatus.ErpOnly ? "—" : $"{MesQty:0.##}";

    /// <summary>ERP 数量显示文本（无记录显示「—」）</summary>
    public string ErpQtyText => ErpQty is null ? "—" : $"{ErpQty:0.##}";

    /// <summary>差异显示文本（带正负号；无法计算时说明原因）</summary>
    public string DiffText
    {
        get
        {
            if (Diff is null) return ErpQty is null ? "ERP 无记录" : "MES 无记录";
            return Math.Abs(Diff.Value) < 1e-9 ? "0" : (Diff.Value > 0 ? $"+{Diff.Value:0.##}" : $"{Diff.Value:0.##}");
        }
    }
}

/// <summary>
/// 库存对账汇总。口径说明（避免歧义）：
/// ・<see cref="MatchedDiffTotal"/>：**账面差异合计**——仅统计双方都有记录的键（MES − ERP），代表"账面对不上"的部分；
/// ・<see cref="MesOnlyQty"/>：MES 有记录、ERP 未记账的数量（待 ERP 补账）；
/// ・<see cref="ErpOnlyQty"/>：ERP 有记录、MES 未记账的数量（待 MES 补账）；
/// ・<see cref="NetDiffTotal"/>：**净差额** = MES 合计 − ERP 合计（大盘口径，含两侧独有项）。
/// </summary>
public class WmsStockCompareSummary
{
    /// <summary>对比行数（含 ERP 独有行，若有）</summary>
    public int RowCount { get; set; }

    public int MatchedCount { get; set; }
    public int MismatchCount { get; set; }
    public int MesOnlyCount { get; set; }
    public int ErpOnlyCount { get; set; }

    /// <summary>MES 库存合计（含 MES 独有行）</summary>
    public double MesTotal { get; set; }

    /// <summary>ERP 库存合计（含 ERP 独有项）</summary>
    public double ErpTotal { get; set; }

    /// <summary>账面差异合计：仅双方都有记录的键（MES − ERP）</summary>
    public double MatchedDiffTotal { get; set; }

    /// <summary>MES 独有数量（ERP 未记账）</summary>
    public double MesOnlyQty { get; set; }

    /// <summary>ERP 独有数量（MES 未记账）</summary>
    public double ErpOnlyQty { get; set; }

    /// <summary>净差额 = MES 合计 − ERP 合计（大盘口径）</summary>
    public double NetDiffTotal => MesTotal - ErpTotal;
}
