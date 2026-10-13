using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 供应链碳管理 —— 数据层
//
// 数据从哪来：**没有外部数据源，全靠录入**。所以种子数据的做法是——
//   从 Materials 里取实际发生过的「供应商 × 物料」组合，
//   一部分填"供应商提供"的模拟值，一部分填"第三方数据库"，**故意留几个"未提供"**，
//   让界面能真实体现"哪些还没填"（否则演示时全是满的，看不出这个功能要解决什么问题）。
//
// 对比规则：只对**已提供数据**的供应商排序；未提供的不排名、排最后、差距标"—"。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>供应商碳数据台账（可更新，唯一键 = Supplier + MaterialCode）</summary>
    public static List<SupplierCarbon> SupplierCarbons { get; } = new();

    private static bool _supplierCarbonReady;

    /// <summary>
    /// 供应商碳数据种子：按 Materials 里实际的「供应商 × 物料」生成。
    /// 确定性分配（不用 Random），同一天重复打开结果一致。
    /// </summary>
    public static void EnsureSupplierCarbonSeed()
    {
        if (_supplierCarbonReady) return;

        // 物料类型 → 行业均值估算基线（kgCO2e/单位），仅用于"估算"来源的行
        double BaselineOf(WmsMaterial m) => m.Category switch
        {
            WmsCategories.Raw => 8.6,
            WmsCategories.Aux => 4.3,
            WmsCategories.Finished => 2.1,
            _ => 5.0
        };

        var pairs = Materials
            .Select(l => (l.Supplier, l.MaterialCode, l.MaterialName))
            .Distinct()
            .OrderBy(x => x.MaterialCode)
            .ThenBy(x => x.Supplier)
            .ToList();

        // 基础行：全部"已提供"，来源在三类中轮换（保证 A4 三种来源都出现）。
        // 不在这里制造"未提供"——未提供项改为下方显式标注，避免计数随行数漂移。
        var idx = 0;
        foreach (var (sup, code, name) in pairs)
        {
            idx++;
            var mat = WmsMaterials.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
            var unit = mat?.Unit ?? "";
            var baseline = mat is null ? 5.0 : BaselineOf(mat);
            SupplierCarbons.Add(new SupplierCarbon
            {
                Supplier = sup,
                MaterialCode = code,
                MaterialName = name,
                Unit = unit,
                KgPerUnit = Math.Round(baseline * (0.82 + (idx % 5) * 0.05), 3),
                Scope3Category = Scope3Categories.PurchasedGoods,
                Source = (idx % 3) switch
                {
                    0 => SupplierCarbonSources.SupplierProvided,
                    1 => SupplierCarbonSources.ThirdPartyDb,
                    _ => SupplierCarbonSources.Estimated
                },
                DataYear = 2026,
                Remark = "演示数据（按物料类型量级拟定）",
                UpdatedAt = DateTime.Now,
                UpdatedBy = "系统"
            });
        }

        // 双供应商演示覆盖（对应 MaterialLot 已补录的真实到货，不新增键、不破坏唯一性）：
        //   活性炭——绿源碳材为更优备选（已提供，碳强度低于原供方）；
        //   电解液——新能源电解液尚未提供数据（演示"待跟进"）。
        void Override(string sup, string code, double? kg, string src, string remark)
        {
            var r = SupplierCarbons.FirstOrDefault(x =>
                string.Equals(x.Supplier, sup, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.MaterialCode, code, StringComparison.OrdinalIgnoreCase));
            if (r is null) return;
            r.KgPerUnit = kg;
            r.Source = src;
            r.DataYear = kg is null ? 0 : 2026;
            r.Remark = remark;
            r.UpdatedAt = DateTime.Now;
            r.UpdatedBy = "系统";
        }
        Override("绿源碳材", "M-ACT-CAR-01", 6.90, SupplierCarbonSources.ThirdPartyDb,
            "备选供应商（第三方数据库，碳强度更优）");
        Override("新能源电解液", "M-ELE-ACN-02", null, "",
            "尚未取得该供应商的碳排放数据，待跟进");

        // 再显式标记两个原单供应商为"未提供"（演示部分物料尚未填报，界面给出跟进提示）
        foreach (var r in SupplierCarbons.Where(r =>
                     string.Equals(r.MaterialCode, "M-CAN-ALU-05", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(r.MaterialCode, "M-PKG-BOX-11", StringComparison.OrdinalIgnoreCase)))
        {
            r.KgPerUnit = null;
            r.Source = "";
            r.DataYear = 0;
            r.Remark = "尚未取得该供应商的碳排放数据，待跟进";
        }

        _supplierCarbonReady = true;
    }

    /// <summary>按物料查供应商碳数据（可按供应商过滤）。</summary>
    public static List<SupplierCarbon> GetSupplierCarbons(string? materialCode = null, string? supplier = null)
    {
        EnsureSupplierCarbonSeed();
        var mc = (materialCode ?? "").Trim();
        var sp = (supplier ?? "").Trim();
        return SupplierCarbons
            .Where(x => mc.Length == 0 || string.Equals(x.MaterialCode, mc, StringComparison.OrdinalIgnoreCase))
            .Where(x => sp.Length == 0 || string.Equals(x.Supplier, sp, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.MaterialCode)
            .ThenBy(x => x.Supplier)
            .ToList();
    }

    /// <summary>
    /// 录入 / 更新某供应商某物料的碳数据。
    /// 唯一键 = Supplier + MaterialCode；同键重复录入为**更新**（不新增行）。
    /// KgPerUnit 传 null 表示"该供应商未提供"（保留记录但不参与排名）。
    /// </summary>
    public static (bool Ok, string Message) UpsertSupplierCarbon(
        string supplier, string materialCode, double? kgPerUnit,
        string scope3Category = Scope3Categories.PurchasedGoods,
        string source = SupplierCarbonSources.SupplierProvided,
        int dataYear = 2026, string? remark = null, string? updatedBy = null)
    {
        var sup = (supplier ?? "").Trim();
        var mc = (materialCode ?? "").Trim();
        if (sup.Length == 0) return (false, "请填写供应商名称。");
        if (mc.Length == 0) return (false, "请填写物料编码。");

        var mat = WmsMaterials.FirstOrDefault(m => string.Equals(m.Code, mc, StringComparison.OrdinalIgnoreCase));
        if (mat is null) return (false, $"物料 {mc} 不在 WMS 物料台账中，请先维护物料主数据。");

        if (kgPerUnit is not null && kgPerUnit.Value < 0) return (false, "碳强度不能为负。");
        if (!Scope3Categories.All.Contains(scope3Category))
            return (false, $"范围三类别取值非法：{scope3Category}");
        if (!SupplierCarbonSources.All.Contains(source))
            return (false, $"数据来源取值非法：{source}");
        if (dataYear is < 2000 or > 2100) return (false, $"数据年份不合理：{dataYear}");

        EnsureSupplierCarbonSeed();
        var exist = SupplierCarbons.FirstOrDefault(x =>
            string.Equals(x.Supplier, sup, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.MaterialCode, mc, StringComparison.OrdinalIgnoreCase));

        if (exist is not null)
        {
            var old = exist.KgPerUnit;
            exist.KgPerUnit = kgPerUnit;
            exist.Scope3Category = scope3Category;
            exist.Source = source;
            exist.DataYear = dataYear;
            exist.Remark = (remark ?? "").Trim();
            exist.UpdatedAt = DateTime.Now;
            exist.UpdatedBy = (updatedBy ?? "系统").Trim();
            return (true, kgPerUnit is null
                ? $"已更新 {sup} / {mc}：标记为**未提供**（不参与排名）。"
                : $"已更新 {sup} / {mc}：{old?.ToString("0.####") ?? "未提供"} → {kgPerUnit:0.####} kgCO2e/{exist.Unit}。");
        }

        SupplierCarbons.Add(new SupplierCarbon
        {
            Supplier = sup, MaterialCode = mc, MaterialName = mat.MaterialName, Unit = mat.Unit,
            KgPerUnit = kgPerUnit, Scope3Category = scope3Category, Source = source,
            DataYear = dataYear, Remark = (remark ?? "").Trim(),
            UpdatedAt = DateTime.Now, UpdatedBy = (updatedBy ?? "系统").Trim()
        });
        return (true, $"已录入 {sup} / {mc} 的碳数据（{kgPerUnit?.ToString("0.####") ?? "未提供"} kgCO2e/{mat.Unit}）。");
    }

    /// <summary>
    /// 同物料多供应商对比：按碳强度升序排名，未提供的不排名、排最后。
    /// 同时带上该供应商的供货批次数与最近到货日（来自 MaterialLot，说明合作深度）。
    /// </summary>
    public static SupplierCompareResult CompareSuppliers(string materialCode)
    {
        EnsureSupplierCarbonSeed();
        var mc = (materialCode ?? "").Trim();
        var mat = WmsMaterials.FirstOrDefault(m => string.Equals(m.Code, mc, StringComparison.OrdinalIgnoreCase));

        var res = new SupplierCompareResult
        {
            MaterialCode = mc,
            MaterialName = mat?.MaterialName ?? mc,
            Unit = mat?.Unit ?? ""
        };

        // 供应商来源：碳数据台账 + MaterialLot 实际供货记录（并集）
        var suppliers = SupplierCarbons
            .Where(x => string.Equals(x.MaterialCode, mc, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Supplier)
            .Union(Materials
                .Where(l => string.Equals(l.MaterialCode, mc, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Supplier))
            .Where(s => s.Length > 0)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        foreach (var sup in suppliers)
        {
            var data = SupplierCarbons.FirstOrDefault(x =>
                string.Equals(x.Supplier, sup, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.MaterialCode, mc, StringComparison.OrdinalIgnoreCase));
            var lots = Materials
                .Where(l => string.Equals(l.Supplier, sup, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(l.MaterialCode, mc, StringComparison.OrdinalIgnoreCase))
                .ToList();

            res.Rows.Add(new SupplierCompareRow
            {
                Supplier = sup,
                KgPerUnit = data?.KgPerUnit,
                Unit = data?.Unit ?? mat?.Unit ?? "",
                Source = data?.Source ?? "",
                DataYear = data?.DataYear ?? 0,
                LotCount = lots.Count,
                LastReceived = lots.Count > 0 ? lots.Max(l => l.ReceivedDate) : null
            });
        }

        // 只对已提供数据的排序
        var provided = res.Rows.Where(r => r.KgPerUnit is not null && r.KgPerUnit.Value > 0)
            .OrderBy(r => r.KgPerUnit).ToList();
        var best = provided.Count > 0 ? provided[0].KgPerUnit!.Value : 0;
        for (var i = 0; i < provided.Count; i++)
        {
            provided[i].Rank = i + 1;
            provided[i].IsBest = i == 0;
            provided[i].GapPct = i == 0 ? null : Math.Round((provided[i].KgPerUnit!.Value - best) / best * 100, 1);
        }

        res.Rows = provided
            .Concat(res.Rows.Where(r => r.KgPerUnit is null || r.KgPerUnit.Value <= 0).OrderBy(r => r.Supplier))
            .ToList();
        return res;
    }

    /// <summary>可选物料列表（有供应商供货记录的优先）。</summary>
    public static List<string> SupplyChainMaterials()
    {
        var withSup = Materials.Select(l => l.MaterialCode).Distinct().ToList();
        var all = WmsMaterials.Select(m => m.Code).ToList();
        return withSup.Union(all).OrderBy(x => x).ToList();
    }

    /// <summary>供应链碳数据覆盖率总览（界面卡片用）。</summary>
    public static (int Total, int Provided, double Coverage) SupplierCarbonCoverage()
    {
        EnsureSupplierCarbonSeed();
        var total = SupplierCarbons.Count;
        var provided = SupplierCarbons.Count(x => x.HasValue);
        return (total, provided, total == 0 ? 0 : (double)provided / total);
    }
}