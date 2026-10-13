using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 碳排放核算 —— 计算层（纯查询 + 纯函数）
//
// 核算公式：排放量(kgCO2e) = 活动数据(消耗量) × 排放因子(kgCO2e/单位)
// 因子匹配：按「能源类型 + 生效年份 ≤ 账期年份」取**最新一条启用的因子**
//           → 这样给新的一年加新因子，历史账期仍用旧因子，**核算结果不变**
//
// 三条硬要求：
//   ① 每次核算记录用了哪些因子（FactorVersions + 每项的 FactorId）
//   ② 不同单位不合计，按能源分行
//   ③ 全过程只读，不写任何数据
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>排放因子台账（版本化，只追加）</summary>
    public static List<EmissionFactor> EmissionFactors { get; } = new();

    private static bool _carbonSeedReady;

    /// <summary>模拟园区排放因子（演示用；正式使用必须替换为当地发布值）。</summary>
    public static void EnsureCarbonSeed()
    {
        if (_carbonSeedReady) return;

        // 按常见园区量级拟定，来源统一标注为"模拟园区口径"
        void Add(string typeId, string typeName, string scope, double value, string unit,
                 int year, string note)
            => EmissionFactors.Add(new EmissionFactor
            {
                FactorId = $"EF-{typeId}-{year}",
                TypeId = typeId, TypeName = typeName, Scope = scope,
                Value = value, Unit = unit, EffectiveYear = year,
                Source = $"模拟园区口径（{note}）", IsActive = true
            });

        // 范围一：自有设施燃烧（天然气）
        Add("GAS", EnergyTypes.Gas, EmissionScopes.Scope1, 2.1622, "kgCO2e/Nm³", 2024,
            "天然气低位热值 35.54 MJ/Nm³、单位热值碳含量 15.3 tC/TJ 折算");
        // 范围二：外购电力
        Add("ELEC", EnergyTypes.Electricity, EmissionScopes.Scope2, 0.5703, "kgCO2e/kWh", 2024,
            "华东区域电网平均排放因子量级");
        Add("ELEC", EnergyTypes.Electricity, EmissionScopes.Scope2, 0.5568, "kgCO2e/kWh", 2026,
            "模拟园区绿电采购比例提升后的修正值");
        // 范围二：外购蒸汽
        Add("STEAM", EnergyTypes.Steam, EmissionScopes.Scope2, 275.0, "kgCO2e/t", 2024,
            "按蒸汽热值约 2.5 GJ/t、燃气锅炉效率折算");
        // 范围三：外购水
        Add("WATER", EnergyTypes.Water, EmissionScopes.Scope3, 0.168, "kgCO2e/t", 2024,
            "市政供水与污水处理的间接排放量级");

        _carbonSeedReady = true;
    }

    /// <summary>
    /// 取某能源在某账期年份生效的因子。
    /// 规则：EffectiveYear ≤ 账期年份的**最新一条**（同年取 IsActive 的最后一条）。
    /// 无匹配因子时返回 null（该能源不参与核算，而不是当 0 排放）。
    /// </summary>
    public static EmissionFactor? ResolveFactor(string typeId, int year)
    {
        return EmissionFactors
            .Where(f => string.Equals(f.TypeId, typeId?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
                        && f.IsActive && f.EffectiveYear <= year)
            .OrderByDescending(f => f.EffectiveYear)
            .ThenByDescending(f => f.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>因子台账查询（按能源过滤，默认按生效年份降序）。</summary>
    public static List<EmissionFactor> GetEmissionFactors(string? typeId = null)
    {
        var t = (typeId ?? "").Trim();
        return EmissionFactors
            .Where(f => t.Length == 0 || string.Equals(f.TypeId, t, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.TypeId)
            .ThenByDescending(f => f.EffectiveYear)
            .ToList();
    }

    // ========================================================================
    // 核算
    // ========================================================================

    /// <summary>
    /// 碳核算：按边界 + 账期范围汇总能耗，逐项乘因子得排放量。
    /// boundary：全厂 / 线体 / 设备（与 EnergyScopes 一致）
    /// boundaryValue：线体=L1…；设备=设备编码；全厂=空
    /// </summary>
    public static CarbonInventory BuildCarbonInventory(
        string boundary = EnergyScopes.Plant, string boundaryValue = "",
        string? periodFrom = null, string? periodTo = null, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        EnsureCarbonSeed();

        var b = (boundary ?? "").Trim();
        if (b.Length == 0) b = EnergyScopes.Plant;
        var bv = (boundaryValue ?? "").Trim();

        // ① 取该边界下的全部读数
        var rows = QueryEnergyReadings(
            scope: b,
            line: b == EnergyScopes.Line ? bv : null,
            equipmentCode: b == EnergyScopes.Device ? bv : null,
            periodFrom: (periodFrom ?? "").Trim().Length == 0 ? null : periodFrom,
            periodTo: (periodTo ?? "").Trim().Length == 0 ? null : periodTo);

        // 账期缺省时取读数里的实际范围（先落成非空变量，避开 CS8601）
        var pf = (periodFrom ?? "").Trim();
        var pt = (periodTo ?? "").Trim();
        var minPeriod = rows.Count > 0 ? rows.Min(r => r.Period) ?? "" : "";
        var maxPeriod = rows.Count > 0 ? rows.Max(r => r.Period) ?? "" : "";

        var inv = new CarbonInventory
        {
            InventoryId = $"CI-{at:yyyyMMdd-HHmmss}",
            Boundary = b, BoundaryValue = bv,
            PeriodFrom = pf.Length > 0 ? pf : minPeriod,
            PeriodTo = pt.Length > 0 ? pt : maxPeriod,
            CalculatedAt = at,
            Disclaimer = CarbonDisclaimers.FactorNote + " " + CarbonDisclaimers.ScopeNote
        };

        // ② 按计量点聚合活动数据（一个计量点一项，逐项留痕）
        var byMeter = rows.GroupBy(r => r.MeterId, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byMeter)
        {
            var first = g.First();
            // 因子按"账期年份"匹配：混合账期时取该组内最新账期年份
            var year = g.Max(r => r.Period.Length >= 4 ? int.Parse(r.Period[..4]) : at.Year);
            var factor = ResolveFactor(first.TypeId, year);
            var activity = g.Sum(r => r.Value);

            if (factor is null)
            {
                // 无因子：记为"未核算"，不当 0 排放（0 排放会被误读成"没有排放"）
                inv.Items.Add(new CarbonSourceItem
                {
                    MeterId = g.Key, MeterName = first.DisplayName, TypeName = first.TypeName,
                    Unit = first.Unit, MeterScope = first.Scope, Section = first.Section,
                    Line = first.Line, EquipmentCode = first.EquipmentCode,
                    EquipmentName = first.EquipmentName,
                    Scope = "未匹配因子", ActivityData = activity,
                    PeriodCount = g.Count(), FactorId = "", FactorSource = "无可用因子",
                    FactorYear = 0, EmissionKg = 0
                });
                continue;
            }

            var kg = activity * factor.Value;
            inv.Items.Add(new CarbonSourceItem
            {
                MeterId = g.Key, MeterName = first.DisplayName, TypeName = first.TypeName,
                Unit = first.Unit, MeterScope = first.Scope, Section = first.Section,
                Line = first.Line, EquipmentCode = first.EquipmentCode,
                EquipmentName = first.EquipmentName,
                Scope = factor.Scope, ActivityData = activity,
                FactorValue = factor.Value, FactorUnit = factor.Unit,
                FactorId = factor.FactorId, FactorSource = factor.Source, FactorYear = factor.EffectiveYear,
                EmissionKg = Math.Round(kg, 3), PeriodCount = g.Count()
            });
        }

        // ③ 总量与分范围汇总
        inv.TotalKg = Math.Round(inv.Items.Where(i => i.Scope != "未匹配因子").Sum(i => i.EmissionKg), 3);

        foreach (var sc in EmissionScopes.All)
        {
            var items = inv.Items.Where(i => i.Scope == sc).ToList();
            inv.Scopes.Add(new CarbonScopeSummary
            {
                Scope = sc,
                EmissionKg = Math.Round(items.Sum(i => i.EmissionKg), 3),
                ItemCount = items.Count,
                Ratio = inv.TotalKg > 1e-9 ? items.Sum(i => i.EmissionKg) / inv.TotalKg : 0
            });
        }

        // ④ 本次用到的因子版本（留痕）
        inv.FactorVersions = inv.Items
            .Where(i => i.FactorId.Length > 0)
            .Select(i => FindFactor(i.FactorId) ?? new EmissionFactor
            {
                FactorId = i.FactorId, TypeName = i.TypeName, Value = i.FactorValue,
                Unit = i.FactorUnit, Source = i.FactorSource, EffectiveYear = i.FactorYear,
                Scope = i.Scope
            })
            .GroupBy(f => f.FactorId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => f.TypeId)
            .ToList();

        // ⑤ 活动数据（按能源分行，不跨单位合计）
        inv.Activity = rows.GroupBy(r => r.TypeId)
            .Select(g => new EnergySummaryRow
            {
                Dimension = g.First().TypeName, SubDimension = g.Key,
                Value = Math.Round(g.Sum(r => r.Value), 2), Unit = g.First().Unit
            })
            .OrderByDescending(r => r.Value)
            .ToList();

        // 排序：排放量降序（最该关注的排前面）
        inv.Items = inv.Items
            .OrderByDescending(i => i.EmissionKg)
            .ThenBy(i => i.MeterId)
            .ToList();

        return inv;
    }

    /// <summary>按 FactorId 取因子（留痕还原用；与按年份匹配的 ResolveFactor 是两个不同入口）。</summary>
    public static EmissionFactor? FindFactor(string factorId)
        => EmissionFactors.FirstOrDefault(f => string.Equals(f.FactorId, factorId?.Trim() ?? "",
            StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 组织层面的可选边界清单（全厂 + 各线体 + 各设备），供界面下拉用。
    /// </summary>
    public static List<(string Boundary, string Value, string Text)> CarbonBoundaryOptions()
    {
        var list = new List<(string, string, string)> { (EnergyScopes.Plant, "", "全厂（组织边界）") };
        foreach (var l in EnergyMeters.Where(m => m.Scope == EnergyScopes.Line)
                     .Select(m => m.Line).Distinct().OrderBy(x => x))
            list.Add((EnergyScopes.Line, l, $"线体 {l}（项目边界）"));
        foreach (var m in EnergyMeters.Where(x => x.Scope == EnergyScopes.Device)
                     .Select(x => (x.EquipmentCode, x.EquipmentName)).Distinct().OrderBy(x => x.EquipmentCode))
            list.Add((EnergyScopes.Device, m.EquipmentCode, $"设备 {m.EquipmentCode} {m.EquipmentName}"));
        return list;
    }
}