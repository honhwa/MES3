using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 产品碳足迹 —— 计算层（纯查询 + 纯函数）
//
// 环节口径：
//   原材料 = BOM 单位用量(含损耗) × 物料碳强度 → 汇总到型号
//   生产   = 该型号工单所在线体的期间排放 ÷ 线体同期总良品数 × 该型号良品数（按产量分摊）
//   运输 / 报废回收 = 无数据 → 标"待补"，**不计入总量也不当作 0**
//   单只   = 总碳足迹 ÷ 该型号总良品数（分母为 0 则不给结论）
//
// 复用既有资产：ErpBomItems（BOM）、WorkOrders（产量/线体）、ErpWorkReports（报工）、
//              EmissionFactor（阶段二的因子）。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>物料碳强度台账（模拟值，按物料类型拟定）</summary>
    public static List<MaterialCarbonIntensity> MaterialCarbonIntensities { get; } = new();

    private static bool _materialCarbonReady;

    /// <summary>
    /// 物料碳强度种子：按物料类型给默认值（**模拟口径，界面明示须替换**）。
    /// 单位统一为 kgCO2e/该物料单位。
    /// </summary>
    public static void EnsureMaterialCarbonSeed()
    {
        if (_materialCarbonReady) return;

        foreach (var m in WmsMaterials)
        {
            // ⚠ WmsMaterial 的类别字段叫 Category，常量在 WmsCategories（不是 Type / WmsMaterialTypes）
            var (value, note) = m.Category switch
            {
                WmsCategories.Raw => (8.6, "原料类均值（活性炭/铝箔/极耳等，含上游冶炼）"),
                WmsCategories.Aux => (4.3, "辅料类均值（电解液/密封圈/隔膜等）"),
                WmsCategories.Finished => (2.1, "成品/包材类均值（彩盒/纸箱/托盘）"),
                _ => (5.0, "未分类物料默认值")
            };
            MaterialCarbonIntensities.Add(new MaterialCarbonIntensity
            {
                MaterialCode = m.Code, MaterialName = m.MaterialName, Unit = m.Unit,
                Value = value, Scope = EmissionScopes.Scope3,
                Source = $"模拟物料碳强度（{note}）"
            });
        }
        _materialCarbonReady = true;
    }

    /// <summary>按物料编码取碳强度（无则 null，不当 0）。</summary>
    public static MaterialCarbonIntensity? FindMaterialCarbon(string materialCode)
    {
        EnsureMaterialCarbonSeed();
        return MaterialCarbonIntensities.FirstOrDefault(x =>
            string.Equals(x.MaterialCode, materialCode?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>碳强度台账查询（按类型过滤）。</summary>
    public static List<MaterialCarbonIntensity> GetMaterialCarbons(string? type = null)
    {
        EnsureMaterialCarbonSeed();
        var t = (type ?? "").Trim();
        var byCode = WmsMaterials.ToDictionary(m => m.Code, m => m.Category, StringComparer.OrdinalIgnoreCase);
        return MaterialCarbonIntensities
            .Where(x => t.Length == 0 || byCode.GetValueOrDefault(x.MaterialCode) == t)
            .OrderBy(x => x.MaterialCode)
            .ToList();
    }

    // ========================================================================
    // 足迹计算
    // ========================================================================

    /// <summary>
    /// 计算某型号的产品碳足迹。
    /// periodFrom/To 限定工单报工时间范围（生产环节用）；为空则取该型号全部工单。
    /// </summary>
    public static ProductFootprint BuildProductFootprint(
        string productModel, string? periodFrom = null, string? periodTo = null, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        EnsureMaterialCarbonSeed();
        EnsureCarbonSeed();

        var model = (productModel ?? "").Trim();
        var fp = new ProductFootprint
        {
            FootprintId = $"FP-{model}-{at:yyyyMMdd-HHmmss}",
            ProductModel = model,
            CalculatedAt = at,
            Disclaimer = FootprintDisclaimers.Note
        };
        if (model.Length == 0)
        {
            fp.Stages.Add(new FootprintStage
            {
                Stage = FootprintStages2.Raw, Calculated = false,
                TodoNote = "未选择产品型号"
            });
            return fp;
        }

        // ① 该型号的工单（含线体、良品数）
        var from = (periodFrom ?? "").Trim();
        var to = (periodTo ?? "").Trim();
        var wos = WorkOrders.Where(w => string.Equals(w.ProductModel, model, StringComparison.OrdinalIgnoreCase))
            .Where(w => from.Length == 0 || w.DueDate >= DateTime.Parse(from + "-01"))
            .Where(w => to.Length == 0 || w.StartDate < DateTime.Parse(to + "-01").AddMonths(1))
            .ToList();

        fp.WoNos = wos.Select(w => w.WoNo).OrderBy(x => x).ToList();
        fp.WoCount = wos.Count;
        fp.Lines = wos.Select(w => w.Line).Where(l => l.Length > 0).Distinct().OrderBy(x => x).ToList();

        // 良品数优先取报工实绩，无实绩取工单 FinishedQty
        var goodByWo = ErpWorkReports
            .Where(r => fp.WoNos.Contains(r.WoNo))
            .GroupBy(r => r.WoNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.GoodQty), StringComparer.OrdinalIgnoreCase);
        fp.GoodQty = wos.Sum(w => goodByWo.TryGetValue(w.WoNo, out var g) && g > 0 ? g : w.FinishedQty);

        // ---------------- ② 原材料环节 ----------------
        var bom = ErpBomItems.Where(b => string.Equals(b.ProductModel, model, StringComparison.OrdinalIgnoreCase))
            .ToList();
        double rawTotal = 0;
        foreach (var b in bom)
        {
            var loss = 1 + (b.LossRate <= 0 ? 0 : b.LossRate / 100.0);
            var qty = b.QtyPerUnit * loss;                    // 含损耗的单位用量
            var intensity = FindMaterialCarbon(b.MaterialCode);
            var perUnit = intensity is null ? 0 : qty * intensity.Value;
            var total = perUnit * fp.GoodQty;

            fp.Materials.Add(new MaterialCarbonRow
            {
                MaterialCode = b.MaterialCode, MaterialName = b.MaterialName, Unit = b.Unit,
                QtyPerUnit = Math.Round(qty, 6), Intensity = intensity?.Value ?? 0,
                PerUnitKg = Math.Round(perUnit, 6), TotalKg = Math.Round(total, 3),
                IntensitySource = intensity?.Source ?? "无碳强度数据（不计入）",
                LossRateText = b.LossRate > 0 ? $"含损耗 {b.LossRate:0.#}%" : "无损耗"
            });
            rawTotal += perUnit * fp.GoodQty;
        }

        fp.Stages.Add(new FootprintStage
        {
            Stage = FootprintStages2.Raw,
            EmissionKg = Math.Round(rawTotal, 3),
            Calculated = true,
            Basis = bom.Count > 0
                ? $"BOM 共 {bom.Count} 项物料；每只原材料排放 = Σ(单位用量 × (1+损耗率) × 物料碳强度)，再乘该型号总良品数 {fp.GoodQty} 只"
                : "该型号无 BOM 数据，无法核算",
            Evidence = $"数据来源：MesDatabase.ErpBomItems（BOM）+ MaterialCarbonIntensities（物料碳强度，{bom.Count} 项）"
        });

        // ---------------- ③ 生产环节（按产量分摊） ----------------
        double prodTotal = 0;
        var prodEvidence = new List<string>();
        foreach (var line in fp.Lines)
        {
            // 该线体在统计期间的总排放
            var lineInv = BuildCarbonInventory(EnergyScopes.Line, line, from.Length == 0 ? null : from,
                to.Length == 0 ? null : to);
            // 该线体在统计期间的总良品数
            var lineWoNos = WorkOrders
                .Where(w => w.Line == line)
                .Where(w => from.Length == 0 || w.DueDate >= DateTime.Parse(from + "-01"))
                .Where(w => to.Length == 0 || w.StartDate < DateTime.Parse(to + "-01").AddMonths(1))
                .Select(w => w.WoNo).ToList();
            var lineGood = ErpWorkReports.Where(r => lineWoNos.Contains(r.WoNo)).Sum(r => r.GoodQty);
            if (lineGood <= 0)
                lineGood = WorkOrders.Where(w => lineWoNos.Contains(w.WoNo)).Sum(w => w.FinishedQty);

            if (lineInv.TotalKg <= 0 || lineGood <= 0)
            {
                prodEvidence.Add($"{line}：线体排放 {lineInv.TotalKg:0.##} kgCO2e、良品 {lineGood} 只，无法分摊");
                continue;
            }

            // 该型号在该线体的良品数
            var modelGoodInLine = wos.Where(w => w.Line == line)
                .Sum(w => goodByWo.TryGetValue(w.WoNo, out var g) && g > 0 ? g : w.FinishedQty);

            var share = modelGoodInLine * lineInv.TotalKg / lineGood;
            prodTotal += share;
            prodEvidence.Add($"{line}：排放 {lineInv.TotalKg:0.##} ÷ 良品 {lineGood} × 该型号 {modelGoodInLine} 只 = {share:0.##} kgCO2e");
        }

        fp.Stages.Add(new FootprintStage
        {
            Stage = FootprintStages2.Production,
            EmissionKg = Math.Round(prodTotal, 3),
            Calculated = fp.Lines.Count > 0,
            Basis = FootprintDisclaimers.ProductionBasis,
            Evidence = "数据来源：MesDatabase.WorkOrders（型号/线体/良品）+ ErpWorkReports（报工实绩）"
                       + " + BuildCarbonInventory（线体排放，复用阶段二）　明细："
                       + string.Join("；", prodEvidence)
        });

        // ---------------- ④ 运输 / 报废回收：无数据，标待补 ----------------
        fp.Stages.Add(new FootprintStage
        {
            Stage = FootprintStages2.Transport, EmissionKg = 0, Calculated = false,
            Basis = "需运输距离、运输方式、承运方等数据，当前系统未采集",
            TodoNote = "待补：运输距离与方式（建议后续由 ERP 发货单或物流单提供）"
        });
        fp.Stages.Add(new FootprintStage
        {
            Stage = FootprintStages2.EndOfLife, EmissionKg = 0, Calculated = false,
            Basis = "需报废量、回收处理方式等数据，当前系统未采集",
            TodoNote = "待补：报废与回收数据（建议后续由 WMS 报废台账提供）"
        });

        // ---------------- ⑤ 汇总与单只 ----------------
        fp.TotalKg = Math.Round(fp.Stages.Where(s => s.Calculated).Sum(s => s.EmissionKg), 3);
        fp.PerUnitKg = fp.GoodQty > 0 ? Math.Round(fp.TotalKg / fp.GoodQty, 6) : 0;
        foreach (var s in fp.Stages)
            s.Ratio = fp.TotalKg > 1e-9 ? s.EmissionKg / fp.TotalKg : 0;

        fp.Stages = fp.Stages
            .OrderBy(s => FootprintStages2.All.ToList().IndexOf(s.Stage))
            .ToList();
        return fp;
    }

    /// <summary>可选产品型号列表（有 BOM 的型号优先，无 BOM 的也列出但会提示无法核算原材料）。</summary>
    public static List<string> FootprintProductModels()
    {
        var withBom = ErpBomItems.Select(b => b.ProductModel).Distinct().ToList();
        var produced = WorkOrders.Select(w => w.ProductModel).Distinct().ToList();
        return withBom.Union(produced).OrderBy(x => x).ToList();
    }
}