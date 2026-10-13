namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 碳排放核算 —— 模型层
//
// 设计铁律（碳核查的硬要求，不是 nice-to-have）：
//   ① **因子版本化**：同一种能源可有多条因子（不同年份/版本），历史核算结果锁定当时用的那条
//      → 换因子后重算旧账期，结果必须不变，否则追溯链断
//   ② **范围分类**：范围一（直接）/ 范围二（外购电力热力）/ 范围三（其他间接）
//   ③ **逐项留痕**：排放源清单每项都要能展开"用了哪个计量点、哪个读数、哪个因子"
//   ④ **纯函数核算**：排放量 = 活动数据 × 因子，可手算复算
// ============================================================================

/// <summary>排放范围</summary>
public static class EmissionScopes
{
    /// <summary>范围一：直接排放（自有设施燃烧等）</summary>
    public const string Scope1 = "范围一";

    /// <summary>范围二：间接排放（外购电力、蒸汽等）</summary>
    public const string Scope2 = "范围二";

    /// <summary>范围三：其他间接排放（外购水、运输、供应链等）</summary>
    public const string Scope3 = "范围三";

    public static readonly string[] All = { Scope1, Scope2, Scope3 };

    /// <summary>范围的中文全称（界面展示用）</summary>
    public static string FullName(string scope) => scope switch
    {
        Scope1 => "范围一 · 直接排放",
        Scope2 => "范围二 · 外购电力与热力",
        Scope3 => "范围三 · 其他间接",
        _ => scope
    };
}

/// <summary>
/// 排放因子：某种能源在某个生效年份的排放系数。
/// 唯一键 = FactorId；同一 TypeId 可有多条（版本化）。
/// </summary>
public class EmissionFactor
{
    public string FactorId { get; set; } = "";

    /// <summary>能源种类 Id（ELEC / WATER / GAS / STEAM）</summary>
    public string TypeId { get; set; } = "";

    /// <summary>能源名称（冗余）</summary>
    public string TypeName { get; set; } = "";

    /// <summary>排放范围</summary>
    public string Scope { get; set; } = EmissionScopes.Scope2;

    /// <summary>因子值</summary>
    public double Value { get; set; }

    /// <summary>因子单位，如 kgCO2e/kWh</summary>
    public string Unit { get; set; } = "";

    /// <summary>因子来源（必须标注，正式使用需替换为当地发布值）</summary>
    public string Source { get; set; } = "";

    /// <summary>生效年份（该年及之后、下一条因子生效前，均用本因子）</summary>
    public int EffectiveYear { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string ValueText => $"{Value:0.####} {Unit}";
    public string ScopeText => EmissionScopes.FullName(Scope);
    public string YearText => $"{EffectiveYear} 年起";
    public string IsActiveText => IsActive ? "启用" : "停用";
    public string CreatedAtText => CreatedAt.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>排放源清单的一项（核查时要逐项展开）</summary>
public class CarbonSourceItem
{
    /// <summary>计量点编号</summary>
    public string MeterId { get; set; } = "";

    public string MeterName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string MeterScope { get; set; } = "";
    public string Section { get; set; } = "";
    public string Line { get; set; } = "";
    public string EquipmentCode { get; set; } = "";
    public string EquipmentName { get; set; } = "";

    /// <summary>排放范围</summary>
    public string Scope { get; set; } = EmissionScopes.Scope2;

    /// <summary>活动数据（消耗量合计）</summary>
    public double ActivityData { get; set; }

    /// <summary>本次核算实际使用的因子值</summary>
    public double FactorValue { get; set; }

    public string FactorUnit { get; set; } = "";

    /// <summary>使用的因子 Id（留痕：换因子后能查回当时用哪条）</summary>
    public string FactorId { get; set; } = "";

    public string FactorSource { get; set; } = "";
    public int FactorYear { get; set; }

    /// <summary>排放量（kgCO2e）</summary>
    public double EmissionKg { get; set; }

    /// <summary>覆盖的账期数（用于说明样本量）</summary>
    public int PeriodCount { get; set; }

    public string ActivityText => $"{ActivityData:0.##} {Unit}";
    public string FactorText => $"{FactorValue:0.####} {FactorUnit}";
    public string EmissionKgText => $"{EmissionKg:0.##} kgCO2e";
    public string EmissionTText => $"{EmissionKg / 1000:0.####} tCO2e";
    public string ScopeText => EmissionScopes.FullName(Scope);
    public string PeriodCountText => $"{PeriodCount} 个";
}

/// <summary>按范围汇总的一行</summary>
public class CarbonScopeSummary
{
    public string Scope { get; set; } = "";
    public double EmissionKg { get; set; }
    public int ItemCount { get; set; }

    public string ScopeText => EmissionScopes.FullName(Scope);
    public string EmissionTText => $"{EmissionKg / 1000:0.####} tCO2e";
    public double Ratio { get; set; }
    public string RatioText => $"{Ratio:P1}";
}

/// <summary>碳核算结果（一次核算的快照）</summary>
public class CarbonInventory
{
    /// <summary>核算单号</summary>
    public string InventoryId { get; set; } = "";

    /// <summary>核算边界：全厂 / 线体 / 设备（用户自选）</summary>
    public string Boundary { get; set; } = EnergyScopes.Plant;

    /// <summary>边界取值（全厂=空；线体=L1；设备=设备编码）</summary>
    public string BoundaryValue { get; set; } = "";

    public string PeriodFrom { get; set; } = "";
    public string PeriodTo { get; set; } = "";

    /// <summary>总排放量（kgCO2e）</summary>
    public double TotalKg { get; set; }

    /// <summary>总活动数据（按能源分行，不跨单位合计）</summary>
    public List<EnergySummaryRow> Activity { get; set; } = new();

    /// <summary>分范围汇总</summary>
    public List<CarbonScopeSummary> Scopes { get; set; } = new();

    /// <summary>排放源清单（逐项）</summary>
    public List<CarbonSourceItem> Items { get; set; } = new();

    /// <summary>本次核算使用的因子版本清单（留痕）</summary>
    public List<EmissionFactor> FactorVersions { get; set; } = new();

    public DateTime CalculatedAt { get; set; } = DateTime.Now;

    /// <summary>免责说明（核算口径必带）</summary>
    public string Disclaimer { get; set; } = "";

    public string TotalTText => $"{TotalKg / 1000:0.####} tCO2e";
    public string PeriodText => $"{PeriodFrom} ~ {PeriodTo}";
    public string BoundaryText => Boundary == EnergyScopes.Plant
        ? "全厂"
        : Boundary == EnergyScopes.Line ? $"线体 {BoundaryValue}" : $"设备 {BoundaryValue}";
    public int ItemCount => Items.Count;
}

/// <summary>碳核算的免责与口径说明（正式使用前必须替换因子）</summary>
public static class CarbonDisclaimers
{
    public const string FactorNote =
        "当前排放因子为**模拟园区口径**（按常见量级拟定），仅用于演示。"
        + "正式核算必须替换为当地主管部门最新发布的电网/燃气/热力排放因子，并留存来源文件。";

    public const string ScopeNote =
        "本次核算覆盖范围一（自有设施燃烧）与范围二（外购电力、热力），"
        + "范围三（外购水、运输、供应链等）在后续阶段接入。";

    public const string BoundaryNote =
        "核算边界为用户选定的组织/项目边界；边界内计量点的能耗数据取自台账读数。";
}