namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 供应链碳管理 —— 模型层
//
// 管理范围：供应商的碳排放数据（单位物料碳强度），用于评估与对比。
//
// 三条设计原则：
//   ① **无数据 ≠ 0 排放**：没提供数据的供应商必须明确标"未提供"，
//      当成 0 会让它排到第一名，直接导致错误的采购决策
//   ② 数据来源必须标注（供应商提供 / 第三方数据库 / 估算）——
//      不同来源的可信度差很多，混在一起排序没有意义
//   ③ 范围三分类要贴 GHG Protocol 的 15 个类别口径，便于后续接入标准清单
// ============================================================================

/// <summary>范围三类别（GHG Protocol Corporate Value Chain Standard，节选与采购相关的）</summary>
public static class Scope3Categories
{
    /// <summary>类别 1：采购的商品与服务</summary>
    public const string PurchasedGoods = "类别1 采购的商品与服务";

    /// <summary>类别 2：资本货物</summary>
    public const string CapitalGoods = "类别2 资本货物";

    /// <summary>类别 4：上游运输与配送</summary>
    public const string UpstreamTransport = "类别4 上游运输与配送";

    /// <summary>类别 9：下游运输与配送</summary>
    public const string DownstreamTransport = "类别9 下游运输与配送";

    public static readonly string[] All =
    {
        PurchasedGoods, CapitalGoods, UpstreamTransport, DownstreamTransport
    };
}

/// <summary>碳数据来源（可信度不同，界面必须分开看）</summary>
public static class SupplierCarbonSources
{
    /// <summary>供应商自行提供（最可信）</summary>
    public const string SupplierProvided = "供应商提供";

    /// <summary>来自第三方数据库（如 Ecoinvent / 中国生命周期基础数据库）</summary>
    public const string ThirdPartyDb = "第三方数据库";

    /// <summary>按行业均值估算（可信度最低）</summary>
    public const string Estimated = "行业均值估算";

    public static readonly string[] All = { SupplierProvided, ThirdPartyDb, Estimated };
}

/// <summary>
/// 供应商碳数据：某供应商供应的某物料的碳强度。
/// 唯一键 = Supplier + MaterialCode（同一供应商同一物料一条，可更新）。
/// </summary>
public class SupplierCarbon
{
    public string Supplier { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>单位碳强度（kgCO2e/单位）。**null = 未提供**，与 0 是两回事。</summary>
    public double? KgPerUnit { get; set; }

    /// <summary>范围三类别</summary>
    public string Scope3Category { get; set; } = Scope3Categories.PurchasedGoods;

    /// <summary>数据来源</summary>
    public string Source { get; set; } = SupplierCarbonSources.SupplierProvided;

    /// <summary>数据年份（供应商数据的核算年份）</summary>
    public int DataYear { get; set; } = 2026;

    public string Remark { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string UpdatedBy { get; set; } = "";

    /// <summary>是否已提供数据</summary>
    public bool HasValue => KgPerUnit is not null && KgPerUnit.Value > 0;

    public string ValueText => HasValue ? $"{KgPerUnit:0.####} kgCO2e/{Unit}" : "未提供";
    public string SourceText => HasValue ? Source : "—";
    public string Scope3Text => Scope3Category;
    public string YearText => HasValue ? $"{DataYear} 年" : "—";
    public string UpdatedAtText => UpdatedAt.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>同物料多供应商对比行</summary>
public class SupplierCompareRow
{
    public string Supplier { get; set; } = "";
    public double? KgPerUnit { get; set; }
    public string Unit { get; set; } = "";
    public string Source { get; set; } = "";
    public int DataYear { get; set; }

    /// <summary>排名（未提供数据的不参与排名，标"—"）</summary>
    public int? Rank { get; set; }

    /// <summary>与最优供应商的差距（%，正数=比最优差多少）</summary>
    public double? GapPct { get; set; }

    /// <summary>供货批次记录数（来自 MaterialLot，说明合作深度）</summary>
    public int LotCount { get; set; }

    /// <summary>最近一次到货日期</summary>
    public DateTime? LastReceived { get; set; }

    public bool IsBest { get; set; }

    public string ValueText => KgPerUnit is null || KgPerUnit.Value <= 0 ? "未提供" : $"{KgPerUnit:0.####} kgCO2e/{Unit}";
    public string RankText => Rank is null ? "—" : $"第 {Rank} 名";
    public string GapText => IsBest ? "基准（最优）" : GapPct is null ? "—" : $"+{GapPct:0.#}%";
    public string LotCountText => $"{LotCount} 批";
    public string LastReceivedText => LastReceived?.ToString("yyyy-MM-dd") ?? "—";
    public string SourceText => KgPerUnit is null || KgPerUnit.Value <= 0 ? "—" : Source;
    public string YearText => KgPerUnit is null || KgPerUnit.Value <= 0 ? "—" : $"{DataYear} 年";
}

/// <summary>同物料供应商对比结果</summary>
public class SupplierCompareResult
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>参与对比的供应商行（已提供的按碳强度升序，未提供的排在最后）</summary>
    public List<SupplierCompareRow> Rows { get; set; } = new();

    /// <summary>供应商总数</summary>
    public int SupplierCount => Rows.Count;

    /// <summary>已提供数据的供应商数</summary>
    public int ProvidedCount => Rows.Count(r => r.KgPerUnit is not null && r.KgPerUnit.Value > 0);

    /// <summary>数据覆盖率（已提供 / 总数）</summary>
    public double Coverage => SupplierCount == 0 ? 0 : (double)ProvidedCount / SupplierCount;

    /// <summary>最优（最低）碳强度</summary>
    public double? BestKg => Rows.Where(r => r.KgPerUnit is not null && r.KgPerUnit.Value > 0)
        .MinBy(r => r.KgPerUnit)?.KgPerUnit;

    public string CoverageText => $"{Coverage:P0}（{ProvidedCount}/{SupplierCount}）";
    public string BestText => BestKg is null ? "—" : $"{BestKg:0.####} kgCO2e/{Unit}";
    public string BestSupplier => Rows.FirstOrDefault(r => r.IsBest)?.Supplier ?? "—";
}

/// <summary>供应链碳管理的免责说明</summary>
public static class SupplyChainDisclaimers
{
    public const string Note =
        "供应商碳数据**无外部数据源，全靠录入**；当前未提供的供应商按物料类型给的是行业均值估算值，"
        + "界面会明确标注来源（供应商提供 / 第三方数据库 / 行业均值估算）。"
        + "正式核算应要求主要供应商提供经核查的数据，或采购第三方数据库（如 Ecoinvent、中国生命周期基础数据库）。";

    public const string ZeroNote =
        "**未提供数据不等于 0 排放**——未提供的供应商不参与排名，避免因数据缺失反而排到第一名。";
}