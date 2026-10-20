using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 智能排产：线体能力矩阵 + 六维工艺相似度评分（技术方案 2.1 / 2.2）
//
// 建模（演示口径，接入真实能力矩阵/换型历史即可替换）：
//   · 6 个"关键工艺能力"（高精度辊压 / 高速分切 / 高速叠片 / 精密焊接 / 大容量化成 / 高速分选），
//     分属 L1/L2/L3 三条柔性产线的"特长"；
//   · 每个产品型号按稳定哈希绑定 2 个"关键能力需求"（确定性，同型号每次一致）；
//   · 线体在目标型号上的六维得分，来自「特长与需求的重合度 + 换型成本 + 工装/资质 + 历史 FTT」；
//   · 三项硬约束：设备无故障/无计划维保、工装在线、人员资质覆盖。
//
// 演示种子说明：L2 的"封口机计划维保中"→ 设备硬约束不通过（演示约束生效，L2 特长的型号会落到相近线）。
// 全部为规则计算，纯查询、零副作用。
// ============================================================================

public static partial class MesDatabase
{
    private sealed class AbilityDef
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Param { get; init; } = "";
        public string Tooling { get; init; } = "";
        public string Cert { get; init; } = "";
    }

    private static readonly AbilityDef[] AbilityPool =
    {
        new(){ Id="roll", Name="高精度辊压", Param="辊压压力 1.2-1.8t / 厚度 ±2μm", Tooling="高精度辊压辊", Cert="辊压工" },
        new(){ Id="slit", Name="高速分切", Param="分切速度 20-30m/min / 毛刺 ≤5μm", Tooling="分切刀组", Cert="分切工" },
        new(){ Id="stack", Name="高速叠片", Param="叠片节拍 0.8-1.2s / 对位 ±0.15mm", Tooling="高速叠片模具", Cert="叠片工" },
        new(){ Id="weld", Name="精密焊接", Param="焊接功率 1.8-2.6kW / 拉力 ≥30N", Tooling="精密焊接治具", Cert="焊接工" },
        new(){ Id="form", Name="大容量化成", Param="化成电压 2.5-2.85V / 温控 35-45℃", Tooling="大容量化成夹具", Cert="化成工" },
        new(){ Id="sort", Name="高速分选", Param="分选节拍 3-5 只/s / 容量分档 ±1%", Tooling="高速分选治具", Cert="分选工" },
    };

    /// <summary>各线"特长能力"（演示种子）</summary>
    private static readonly Dictionary<string, string[]> LineAbilityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = new[] { "roll", "slit" },
        ["L2"] = new[] { "stack", "weld" },
        ["L3"] = new[] { "form", "sort" },
    };

    /// <summary>各线历史一次合格率 FTT（演示种子）</summary>
    private static readonly Dictionary<string, double> LineFtt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = 0.965, ["L2"] = 0.958, ["L3"] = 0.972
    };

    /// <summary>各线设备可用性（演示：L2 封口机计划维保 → 设备硬约束不通过）</summary>
    private static readonly Dictionary<string, (bool Ok, string Note)> LineEquip = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L1"] = (true, "关键设备无故障、无计划维保"),
        ["L2"] = (false, "封口机 SEAL-L1-01 计划维保中，本时段不可排"),
        ["L3"] = (true, "关键设备无故障、无计划维保"),
    };

    /// <summary>稳定哈希（FNV-1a），保证同型号每次得到同一组关键能力</summary>
    private static int LineHash(string? s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in (s ?? "")) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    private static AbilityDef? AbilityById(string id) => AbilityPool.FirstOrDefault(a => a.Id == id);

    private static string[] LineAbilityIds(string line) =>
        LineAbilityMap.TryGetValue((line ?? "").Trim(), out var arr) ? arr : Array.Empty<string>();

    /// <summary>型号绑定的 2 个关键能力（稳定哈希，确定性）。</summary>
    private static string[] ModelAbilityIds(string model)
    {
        int h = LineHash(model);
        int i = h % AbilityPool.Length;
        int j = (h / 7 + 1 + h % 5) % AbilityPool.Length;
        if (j == i) j = (j + 1) % AbilityPool.Length;
        return new[] { AbilityPool[i].Id, AbilityPool[j].Id };
    }

    /// <summary>该线当前正在生产的型号（最新一张生产中的工单；空闲为空）。</summary>
    private static string LineCurrentModel(string line) =>
        WorkOrders.Where(w => w.Status == "生产中" && LineEquals(w.Line, line))
                  .OrderByDescending(w => w.StartDate)
                  .Select(w => w.ProductModel)
                  .FirstOrDefault() ?? "";

    /// <summary>三项硬约束校验（设备 / 工装 / 人员资质）。</summary>
    private static List<HardConstraintCheck> CheckHardConstraints(string line, string model)
    {
        var list = new List<HardConstraintCheck>();
        var eq = LineEquip.TryGetValue(line, out var e) ? e : (Ok: true, Note: "设备状态正常");
        list.Add(new HardConstraintCheck { Name = "设备无故障/无计划维保", Ok = eq.Ok, Note = eq.Note });
        list.Add(new HardConstraintCheck { Name = "工装夹具在线（无需切换）", Ok = true, Note = "该线工装在线可用" });
        // 人员：所有线具备基础全工序资质（可跨能力生产），特长资质在六维里加分
        list.Add(new HardConstraintCheck { Name = "人员技能矩阵覆盖该型号", Ok = true, Note = "基础全工序资质覆盖，特长资质见评分" });
        return list;
    }

    /// <summary>换型指导书：从该线当前型号换到目标型号，需调参数 / 换工装 / 预计时长。</summary>
    private static ChangeoverGuide? BuildChangeover(string line, string model)
    {
        var cur = LineCurrentModel(line);
        if (cur.Length > 0 && string.Equals(cur, model, StringComparison.OrdinalIgnoreCase)) return null; // 同型号无需换型
        if (cur.Length == 0) return null; // 空闲线，无需换型

        var curAbs = ModelAbilityIds(cur);
        var tgtAbs = ModelAbilityIds(model);
        int matched = tgtAbs.Count(a => LineAbilityIds(line).Contains(a));
        // 换型分钟统一取自换型矩阵（清线 + 四维差异），不再另用一套 20/35/50 的粗估——
        // 否则同一次换型在"六维评分""插单评分""CTP 时间线"三处会出现三个数（2026-10-20 修）。
        int minutes = GetChangeoverInfo(cur, model).Minutes;

        var guide = new ChangeoverGuide
        {
            Line = line, FromModel = cur, ToModel = model,
            EstimatedMinutes = minutes
        };
        foreach (var id in tgtAbs)
        {
            var ab = AbilityById(id); if (ab is null) continue;
            if (!LineAbilityIds(line).Contains(id))
                guide.NeedParamChange.Add($"{ab.Name}：{ab.Param}");
            if (!LineAbilityIds(line).Contains(id))
                guide.NeedToolingChange.Add($"{ab.Tooling}（{ab.Cert}资质）");
        }
        guide.Notes.Add($"该线当前在生产 {cur}，切换至 {model} 需 {minutes} 分钟换型（含调参/换工装/首件确认）。");
        if (guide.NeedParamChange.Count == 0 && guide.NeedToolingChange.Count == 0)
            guide.Notes.Add("两型号关键能力高度重合，仅需小幅参数确认。");
        return guide;
    }

    /// <summary>六维评分（满分 100）。</summary>
    private static LineScore CalcLineScore(string line, string model, DateTime at)
    {
        var needs = ModelAbilityIds(model);
        var abilities = LineAbilityIds(line);
        int matched = needs.Count(a => abilities.Contains(a));
        double ratio = needs.Length == 0 ? 0 : (double)matched / needs.Length;

        // 柔性线基础能力：不匹配也能做（给 55 基准），匹配越高越优
        double baseScore(double ratio) => 55 + ratio * 45;

        // 换型：同型号 0 分钟；否则按与在制型号能力重合度给 20/35/50 分钟
        var cur = LineCurrentModel(line);
        int minutes;
        if (cur.Length == 0) minutes = 10;                    // 空线，轻量调机
        else if (string.Equals(cur, model, StringComparison.OrdinalIgnoreCase)) minutes = 0;
        else
        {
            var curAbs = ModelAbilityIds(cur);
            int inter = needs.Intersect(curAbs).Count();
            minutes = inter >= Math.Max(1, needs.Length - 1) ? 20 : (inter == 1 ? 35 : 50);
        }
        double changeScore = Math.Clamp(100 - minutes, 0, 100);
        double ftt = (LineFtt.TryGetValue(line, out var f) ? f : 0.96) * 100;

        var details = new List<LineScoreDetail>
        {
            new(){ Dimension="核心工序重合度", Weight=0.35, Score=baseScore(ratio),
                   Reason=$"型号需求 {needs.Length} 项关键能力，本线覆盖 {matched} 项" },
            new(){ Dimension="设备参数兼容性", Weight=0.20, Score=baseScore(ratio),
                   Reason= matched>0 ? "关键能力参数区间重叠，无需大幅调整" : "无特长能力重合，需较大参数调整" },
            new(){ Dimension="换型时间成本", Weight=0.20, Score=changeScore,
                   Reason= cur.Length==0 ? "线体空闲，轻量调机" : (string.Equals(cur,model,StringComparison.OrdinalIgnoreCase) ? "同型号连续，零换型" : $"从 {cur} 换型约 {minutes} 分钟") },
            new(){ Dimension="工装夹具通用性", Weight=0.15, Score=baseScore(ratio),
                   Reason= matched>0 ? $"可直接复用 {matched} 项特长工装" : "需更换工装" },
            new(){ Dimension="人员技能匹配度", Weight=0.05, Score=baseScore(ratio),
                   Reason= matched>0 ? "特长资质覆盖" : "基础全工序资质，特长资质待补" },
            new(){ Dimension="质量历史表现(FTT)", Weight=0.05, Score=Math.Round(ftt,1),
                   Reason= $"该线历史一次合格率 {ftt:0.0}%" },
        };
        var total = Math.Round(details.Sum(d => d.Score * d.Weight), 1);

        return new LineScore
        {
            Line = line,
            SegmentName = LineAbilityMap.TryGetValue(line, out var a2) ? string.Join("/", a2.Select(x => AbilityById(x)?.Name ?? x)) : "通用线",
            Total = total,
            Details = details,
            Allocatable = CheckHardConstraints(line, model).All(h => h.Ok)
        };
    }

    /// <summary>
    /// 三级选线评估：2.1 同型号在制线体优先（+3 硬约束+产能拆分）→ 2.2 六维评分 → 负载兜底。
    /// 纯查询，零副作用；不修改任何工单/占用。
    /// </summary>
    public static LineMatchEvaluation EvaluateLineMatch(string poNo, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var po = FindErpPo(poNo);
        var eval = new LineMatchEvaluation { PoNo = poNo ?? "" };
        if (po is null) { eval.RecommendedReason = "订单不存在，无法评估。"; return eval; }

        eval.ProductModel = po.ProductModel;
        eval.MainSegment = string.Join("/", ModelAbilityIds(po.ProductModel)
            .Select(id => AbilityById(id)?.Name ?? id));

        // 本单所需工时（用于产能拆分说明）
        var hist = GetProductionHistory(po.ProductModel);
        double yld = hist.YieldFromHistory ? hist.AvgYield : SchedulingRules.DefaultYield;
        double unitH = hist.HoursFromHistory ? hist.UnitHours : SchedulingRules.DefaultUnitHours;
        int inputQty = (int)Math.Ceiling(po.OrderQty / Math.Max(0.01, yld));
        double requiredHours = inputQty * unitH;

        // ---- 2.1 同型号在制线体优先 ----
        var sameLines = WorkOrders
            .Where(w => w.Status == "生产中"
                        && string.Equals(w.ProductModel, po.ProductModel, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(w.Line))
            .Select(w => w.Line.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string level1Line = "";
        foreach (var line in sameLines)
        {
            if (CheckHardConstraints(line, po.ProductModel).All(h => h.Ok)) { level1Line = line; break; }
        }

        if (level1Line.Length > 0)
        {
            eval.Level = LineMatchLevel.SameModelInProduction;
            eval.Level1Line = level1Line;
            var load = GetLineLoad(level1Line, at);
            eval.Level1Reason = $"{level1Line} 正在生产同型号 {po.ProductModel}，优先锁定连续生产（消除换型，减少换模/换料/调试）。";
            eval.Level1SplitNote = load.RemainingHours > 0
                ? $"{level1Line} 在手 {load.OpenWoCount} 张、余 {load.RemainingHours:0.#}h；本单需 {requiredHours:0.#}h，接续排在其后。若余产能不足以承接本单，按「可承接部分 + 缺口转下一级」拆分。"
                : "";
            eval.RecommendedLine = level1Line;
            eval.RecommendedReason = eval.Level1Reason;
            eval.Changeover = BuildChangeover(level1Line, po.ProductModel); // 同型号 → null
            // 同型号命中时仍计算六维评分（页面展示各线评分、备选参考；推荐线由 2.1 锁定）
            eval.Candidates = KnownLines()
                .Select(l => CalcLineScore(l, po.ProductModel, at))
                .OrderByDescending(s => s.Allocatable).ThenByDescending(s => s.Total).ToList();
            eval.RecommendedScore = eval.Candidates
                .FirstOrDefault(s => string.Equals(s.Line, level1Line, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // ---- 2.2 六维评分（排除硬约束不通过的线）----
            var scores = KnownLines()
                .Select(l => CalcLineScore(l, po.ProductModel, at))
                .OrderByDescending(s => s.Allocatable)
                .ThenByDescending(s => s.Total)
                .ToList();
            eval.Candidates = scores;

            var pick = scores.FirstOrDefault(s => s.Allocatable) ?? scores.FirstOrDefault();
            if (pick is null)
            {
                eval.Level = LineMatchLevel.LoadFallback;
                var busy = KnownLines().OrderBy(l => GetLineLoad(l, at).RemainingHours).FirstOrDefault() ?? "L1";
                eval.RecommendedLine = busy;
                eval.RecommendedReason = $"无可用线体通过硬约束，按负载最轻兜底推荐 {busy}。";
            }
            else
            {
                eval.Level = pick.Allocatable ? LineMatchLevel.ProcessSimilarity : LineMatchLevel.LoadFallback;
                eval.RecommendedLine = pick.Line;
                eval.RecommendedScore = pick;
                var excluded = scores.Where(s => !s.Allocatable).Select(s => s.Line).ToList();
                eval.RecommendedReason = $"同型号无在制线体，按工艺相似度匹配：{pick.Line} 综合 {pick.TotalText} 分最高"
                    + (pick.Allocatable ? "，硬约束全部通过。" : "（硬约束存在不通过项，已按兜底推荐）。")
                    + (excluded.Count > 0 ? $"（{string.Join("、", excluded)} 因硬约束不通过被排除）" : "");
                eval.Changeover = BuildChangeover(pick.Line, po.ProductModel);
            }
            eval.HardConstraints = CheckHardConstraints(eval.RecommendedLine, po.ProductModel);
        }

        if (eval.HardConstraints.Count == 0)
            eval.HardConstraints = CheckHardConstraints(eval.RecommendedLine, po.ProductModel);

        // ---- 可读依据 ----
        eval.Evidence.Add($"① 型号关键能力需求：{eval.MainSegment}");
        if (eval.Level1Line.Length > 0)
            eval.Evidence.Add($"② 命中 2.1 同型号在制优先：{eval.Level1Line}（硬约束全通过，连续生产）");
        else
            eval.Evidence.Add($"② 未命中同型号在制，进入 2.2 六维工艺相似度评分");
        foreach (var s in eval.Candidates)
            eval.Evidence.Add($"    · {s.Line}（{s.SegmentName}）总分 {s.TotalText}"
                + (s.Allocatable ? "" : "（硬约束不通过，排除）"));
        eval.Evidence.Add($"③ 推荐线体：{eval.RecommendedLine} —— {eval.RecommendedReason}");
        foreach (var h in eval.HardConstraints)
            eval.Evidence.Add($"    · 硬约束[{h.Name}]：{h.StateText} —— {h.Note}");
        if (eval.Changeover is not null)
            eval.Evidence.Add($"④ 换型指导：{eval.Changeover.Summary}");
        return eval;
    }
}
