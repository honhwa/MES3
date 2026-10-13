namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 产品碳足迹 —— 模型层
//
// 环节划分（对应产品全生命周期）：
//   原材料   BOM 用量(含损耗) × 物料碳强度
//   生产     该型号工单所在线体的排放，按产量分摊到该型号
//   运输     ⚠ 当前无运输距离/方式数据 → 标"待补"，**不做假数**
//   报废     ⚠ 当前无报废回收数据 → 标"待补"
//
// 两条铁律：
//   ① 每个环节都必须写清「取数口径 + 数据来源」，否则足迹不可信也没法复算
//   ② 单只碳足迹 = 总碳足迹 ÷ 该型号总良品数（分母为 0 时不给结论）
// ============================================================================

/// <summary>足迹环节</summary>
public static class FootprintStages2
{
    /// <summary>原材料（上游）</summary>
    public const string Raw = "原材料";

    /// <summary>生产制造</summary>
    public const string Production = "生产";

    /// <summary>运输配送</summary>
    public const string Transport = "运输";

    /// <summary>报废与回收</summary>
    public const string EndOfLife = "报废回收";

    public static readonly string[] All = { Raw, Production, Transport, EndOfLife };

    /// <summary>该环节是否已实现核算（未实现的标"待补"，不当 0）</summary>
    public static bool IsCalculable(string stage) => stage is Raw or Production;
}

/// <summary>
/// 物料碳强度：生产 1 单位物料产生的碳排放。
/// 唯一键 = MaterialCode；与排放因子一样，**来源必须标注**。
/// </summary>
public class MaterialCarbonIntensity
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>碳强度值（kgCO2e/单位）</summary>
    public double Value { get; set; }

    /// <summary>数据来源（必须标注；模拟值需说明）</summary>
    public string Source { get; set; } = "";

    /// <summary>对应排放范围（原材料通常属范围三）</summary>
    public string Scope { get; set; } = EmissionScopes.Scope3;

    public string ValueText => $"{Value:0.####} kgCO2e/{Unit}";
    public string ScopeText => EmissionScopes.FullName(Scope);
}

/// <summary>足迹的一个环节</summary>
public class FootprintStage
{
    /// <summary>环节名（原材料/生产/运输/报废回收）</summary>
    public string Stage { get; set; } = "";

    /// <summary>排放量（kgCO2e）</summary>
    public double EmissionKg { get; set; }

    /// <summary>取数口径（怎么算的，必须写清）</summary>
    public string Basis { get; set; } = "";

    /// <summary>数据来源（取自哪个集合/哪次核算）</summary>
    public string Evidence { get; set; } = "";

    /// <summary>是否已核算（未实现的标"待补"，不当 0）</summary>
    public bool Calculated { get; set; } = true;

    /// <summary>待补说明（未实现环节填写）</summary>
    public string TodoNote { get; set; } = "";

    public double Ratio { get; set; }
    public string EmissionTText => $"{EmissionKg / 1000:0.####} tCO2e";
    public string RatioText => $"{Ratio:P1}";
    public string StatusText => Calculated ? "已核算" : "待补数据";
}

/// <summary>一个型号的碳足迹结果</summary>
public class ProductFootprint
{
    public string FootprintId { get; set; } = "";
    public string ProductModel { get; set; } = "";

    /// <summary>统计的工单数</summary>
    public int WoCount { get; set; }

    /// <summary>该型号总良品数（单只足迹的分母）</summary>
    public int GoodQty { get; set; }

    /// <summary>总碳足迹（kgCO2e）</summary>
    public double TotalKg { get; set; }

    /// <summary>单只碳足迹（kgCO2e/只）</summary>
    public double PerUnitKg { get; set; }

    /// <summary>环节分解</summary>
    public List<FootprintStage> Stages { get; set; } = new();

    /// <summary>原材料环节的物料明细</summary>
    public List<MaterialCarbonRow> Materials { get; set; } = new();

    /// <summary>涉及的生产工单（用于追溯）</summary>
    public List<string> WoNos { get; set; } = new();

    /// <summary>涉及的线体</summary>
    public List<string> Lines { get; set; } = new();

    public DateTime CalculatedAt { get; set; } = DateTime.Now;

    /// <summary>免责与口径说明</summary>
    public string Disclaimer { get; set; } = "";

    public string TotalTText => $"{TotalKg / 1000:0.####} tCO2e";
    public string PerUnitText => GoodQty > 0 ? $"{PerUnitKg:0.####} kgCO2e/只" : "—";
    public string GoodQtyText => $"{GoodQty} 只";
}

/// <summary>原材料环节的物料明细行</summary>
public class MaterialCarbonRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>单位用量（含损耗，每只成品）</summary>
    public double QtyPerUnit { get; set; }

    /// <summary>碳强度（kgCO2e/单位）</summary>
    public double Intensity { get; set; }

    /// <summary>单只该物料的碳排放（kgCO2e/只）</summary>
    public double PerUnitKg { get; set; }

    /// <summary>该型号总排放（kgCO2e）</summary>
    public double TotalKg { get; set; }

    public string IntensitySource { get; set; } = "";
    public string LossRateText { get; set; } = "";

    public string QtyText => $"{QtyPerUnit:0.####} {Unit}";
    public string IntensityText => $"{Intensity:0.####} kgCO2e/{Unit}";
    public string PerUnitText => $"{PerUnitKg:0.####} kgCO2e";
    public string TotalTText => $"{TotalKg / 1000:0.####} tCO2e";
}

/// <summary>产品碳足迹的免责说明</summary>
public static class FootprintDisclaimers
{
    public const string Note =
        "本足迹覆盖「原材料 + 生产」两个环节；**运输、报废回收暂无数据，标为待补、不计入总量**。"
        + "物料碳强度为模拟值（按物料类型拟定），正式核算须替换为供应商提供值或第三方数据库（如 Ecoinvent/中国生命周期基础数据库）。";

    public const string ProductionBasis =
        "生产环节按「该型号工单所在线体的期间排放 ÷ 该线体同期总良品数 × 该型号良品数」分摊；"
        + "即假定线体内各型号单位能耗相同——这是简化口径，实际应按各型号实测能耗分摊。";
}