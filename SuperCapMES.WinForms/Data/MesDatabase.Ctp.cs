using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 交期承诺（CTP）+ 换型矩阵 —— 数据层
//
// 一条铁律：**承诺必须有出处**。所以 EstimateCtp 的每一步都写进 Evidence，
// 界面上能逐条点开：换型几分钟、排队到几号、生产几天、哪个料几号到。
// 出问题能立刻指出是哪一段算错，而不是"系统说能交"。
//
// 换型时间的推导（可追问）：
//   基础清线 15 分钟（任何换型都要做）
//   + 封装尺寸差异 25（换模具）
//   + 电解液体系差异 40（注液管路清洗，最费时）
//   + 极片宽度差异 25（换辊/切刀）
//   + 容量段差异 20（换化成工装；容量段直接从型号解析，100F/300F）
// 除容量段外，其余三个维度是型号的**演示特征**（由编码稳定哈希推导），
// 真实环境应从工艺路线 / 工装台账 / 换型标准作业指导书取数。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>换型基础清线时间（分钟）</summary>
    public const int ChangeoverCleanMinutes = 15;

    // ================= 型号特征（换型时间推导用） =================

    /// <summary>型号的工艺特征签名：封装尺寸 / 电解液体系 / 极片宽度 / 容量段</summary>
    private static (int Size, int Electrolyte, int Width, int CapacityF) ModelSignature(string model)
    {
        var m = (model ?? "").Trim();
        var h = Math.Abs(StableHash(m));

        // 容量段能从型号直接解析（SC-CX2026-100F → 100F），解析不到就用哈希兜底
        var cap = 0;
        var digits = new string(m.Where(char.IsDigit).ToArray());
        if (m.EndsWith("F", StringComparison.OrdinalIgnoreCase))
        {
            var idx = m.LastIndexOf('-');
            var tail = idx >= 0 ? new string(m[(idx + 1)..].Where(char.IsDigit).ToArray()) : digits;
            if (int.TryParse(tail, out var f)) cap = f;
        }
        if (cap == 0) cap = 50 + h % 4 * 50;

        return (h % 3, (h / 7) % 3, (h / 31) % 3, cap);
    }

    /// <summary>
    /// 两个型号之间的换型信息（分钟 + 要动哪些要素）。
    /// 同型号返回 0 分钟；任一为空按"首次上线"处理（只清线，不换模具）。
    /// </summary>
    public static ChangeoverInfo GetChangeoverInfo(string? fromModel, string? toModel)
    {
        var from = (fromModel ?? "").Trim();
        var to = (toModel ?? "").Trim();

        var info = new ChangeoverInfo
        {
            FromModel = from,
            ToModel = to,
            SameModel = from.Length > 0 && string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
        };
        if (info.SameModel) return info;

        info.CleanMinutes = ChangeoverCleanMinutes;
        if (from.Length == 0 || to.Length == 0) return info;   // 首次上线：只清线

        var a = ModelSignature(from);
        var b = ModelSignature(to);
        if (a.Size != b.Size) info.ToolingMinutes = 25;
        if (a.Electrolyte != b.Electrolyte) info.FlushMinutes = 40;
        if (a.Width != b.Width) info.RollerMinutes = 25;
        if (a.CapacityF != b.CapacityF) info.FormMinutes = 20;
        return info;
    }

    /// <summary>换型矩阵：全部产品型号两两之间的换型时间。纯查询。</summary>
    public static ChangeoverMatrix GetChangeoverMatrix()
    {
        var models = GetProductModels();
        var matrix = new ChangeoverMatrix { Models = models };
        foreach (var from in models)
            foreach (var to in models)
                if (!matrix.Cells.ContainsKey(ChangeoverMatrix.Key(from, to)))
                    matrix.Cells[ChangeoverMatrix.Key(from, to)] = GetChangeoverInfo(from, to);
        return matrix;
    }

    /// <summary>
    /// 最小换型序列：给定一天/一段时间要生产的型号集合，用**最近邻贪心**排出换型最短的顺序。
    /// 这不是最优解（旅行商问题是 NP-hard），但比"按接单顺序做"稳定更好，且规则可解释。
    /// </summary>
    public static ChangeoverSequence BuildChangeoverSequence(IEnumerable<string> models)
    {
        var input = (models ?? Enumerable.Empty<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seq = new ChangeoverSequence();
        if (input.Count == 0) return seq;

        // 原顺序的换型时间（对比基线）
        var naive = 0;
        var naiveSteps = new List<string>();
        for (var i = 0; i < input.Count; i++)
        {
            var prev = i == 0 ? "" : input[i - 1];
            var co = GetChangeoverInfo(prev, input[i]);
            naive += co.Minutes;
            naiveSteps.Add(i == 0
                ? $"{input[i]}（首次上线：{co.Minutes} 分钟）"
                : $"{prev} → {input[i]}：{co.Minutes} 分钟");
        }
        seq.NaiveMinutes = naive;

        // 最近邻贪心：从换型时间最短的一对开始，逐次接最省换型的下一个型号
        var best = new List<string>();
        var bestTotal = int.MaxValue;
        foreach (var start in input)
        {
            var route = new List<string> { start };
            var rest = input.Where(m => !string.Equals(m, start, StringComparison.OrdinalIgnoreCase)).ToList();
            while (rest.Count > 0)
            {
                var last = route[^1];
                var next = rest.OrderBy(m => GetChangeoverInfo(last, m).Minutes)
                               .ThenBy(m => m, StringComparer.Ordinal)
                               .First();
                route.Add(next);
                rest.Remove(next);
            }
            var total = 0;
            for (var i = 0; i < route.Count; i++)
                total += GetChangeoverInfo(i == 0 ? "" : route[i - 1], route[i]).Minutes;
            if (total < bestTotal)
            {
                bestTotal = total;
                best = route;
            }
        }

        seq.Ordered = best;
        seq.TotalMinutes = bestTotal;
        for (var i = 0; i < best.Count; i++)
        {
            var prev = i == 0 ? "" : best[i - 1];
            var co = GetChangeoverInfo(prev, best[i]);
            seq.Steps.Add(i == 0
                ? $"{best[i]}（首次上线）"
                : $"{prev} → {best[i]}：{co.MinutesText}｜{co.DetailText}");
        }
        return seq;
    }

    // ================= CTP 交期承诺 =================

    /// <summary>
    /// 交期承诺评估：询单型号 + 数量 + 期望交期 → 各线可交日 → 承诺结论（三档）。
    /// 纯查询，不改任何单据、不占产能。
    /// </summary>
    /// <param name="model">产品型号</param>
    /// <param name="qty">询单数量（只）</param>
    /// <param name="expected">客户期望交期</param>
    /// <param name="now">"现在"（自测注入固定值，保证结果可复现）</param>
    public static CtpResult EvaluateCtp(string model, int qty, DateTime expected, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var today = at.Date;
        var result = new CtpResult
        {
            Query = new CtpQuery
            {
                ProductModel = (model ?? "").Trim(),
                Qty = Math.Max(0, qty),
                ExpectedDate = expected.Date
            }
        };

        var q = result.Query;
        if (q.ProductModel.Length == 0 || q.Qty <= 0)
        {
            result.Verdict = CtpVerdict.Reject;
            result.SummaryText = "请先选择产品型号并填写询单数量（> 0）。";
            return result;
        }

        // ---- ① 良率与工时（历史样本；不足则用默认值并标注） ----
        var history = GetProductionHistory(q.ProductModel);
        var yield = history.YieldFromHistory ? history.AvgYield : SchedulingRules.DefaultYield;
        var unitHours = history.HoursFromHistory ? history.UnitHours : SchedulingRules.DefaultUnitHours;
        var inputQty = yield <= 0 ? q.Qty : (int)Math.Ceiling(q.Qty / yield);
        var requiredHours = inputQty * unitHours;

        result.Yield = yield;
        result.UnitHours = unitHours;
        result.InputQty = inputQty;
        result.RequiredHours = requiredHours;
        result.Reasons.Add($"投产数量 {inputQty} 只（按良率 {yield:P1} 补偿，历史样本 {history.SampleCount} 张）");
        result.Reasons.Add($"单位工时 {unitHours:0.00000} h/只 → 需产能 {requiredHours:0.##} h"
                          + $"（每日有效产能 {SchedulingRules.DailyCapacityHours:0.#} h）");

        // ---- ② 物料齐套：短缺物料的最长采购前置期决定"料几号到" ----
        var materialReadyDate = today;
        var shortageCount = 0;
        var materialText = "物料齐套，立即可投产";
        var maxLeadDays = 0;
        var materialBrief = new List<string>();

        var stock = CheckBomAvailability(q.ProductModel, inputQty, q.Qty);
        if (stock is not null && !stock.NoBom)
        {
            var shortRows = stock.Rows.Where(r => r.Status == ShortageStatus.Short).ToList();
            shortageCount = shortRows.Count;
            if (shortRows.Count > 0)
            {
                foreach (var r in shortRows)
                {
                    var pp = GetProcureParam(r.MaterialCode);
                    if (pp.TotalLeadDays > maxLeadDays) maxLeadDays = pp.TotalLeadDays;
                    materialBrief.Add($"{r.MaterialName} 缺 {r.Shortage:0.##}{r.Unit}（L/T {pp.TotalLeadDays} 天）");
                }
                materialReadyDate = today.AddDays(maxLeadDays);
                materialText = $"{shortageCount} 项物料短缺，最长前置期 {maxLeadDays} 天 → 料约 {materialReadyDate:MM-dd} 到";
                result.Reasons.Add($"物料：{string.Join("；", materialBrief.Take(3))}"
                                  + (materialBrief.Count > 3 ? $" 等 {materialBrief.Count} 项" : ""));
            }
            else
            {
                result.Reasons.Add($"物料：{stock.Rows.Count} 项 BOM 物料全部满足（含紧张项）");
            }
        }
        else
        {
            materialText = "该型号无 BOM 数据，物料就绪无法判定";
            result.Reasons.Add("物料：⚠ 该型号没有 BOM 明细，齐套无法校验（交期结论按「无物料约束」给出，实际排产前必须补 BOM）");
        }

        // ---- ③ 逐线评估：排队 + 换型 + 生产，取可交日 ----
        var lines = KnownLines();
        foreach (var line in lines)
        {
            var opt = new CtpLineOption
            {
                Line = line,
                ExpectedDate = q.ExpectedDate,
                MaterialReadyDate = materialReadyDate,
                ShortageCount = shortageCount,
                MaterialText = materialText
            };

            // 线体时间线（与智能排产/插单同一内核）：队尾型号是换型起点，队尾完工是排队结束
            var timeline = BuildLineTimeline(line, at);
            opt.FromModel = timeline.TailModel;

            var co = GetChangeoverInfo(timeline.TailModel, q.ProductModel);
            opt.Changeover = co;

            var load = GetLineLoad(line, at);          // 仅用于"在制张数/剩余工时"的展示
            opt.LineFreeTime = timeline.TailFinish > at ? timeline.TailFinish : at;
            opt.PlannedStart = opt.LineFreeTime.AddMinutes(co.Minutes);
            opt.PlannedFinish = opt.PlannedStart.AddDays(requiredHours / SchedulingRules.DailyCapacityHours);

            // 料到了还要生产 → 最早可交日不是 max(完工, 到料)，而是 max(完工, 到料 + 生产周期)
            var materialDriven = materialReadyDate.AddDays(requiredHours / SchedulingRules.DailyCapacityHours);
            opt.EarliestDeliver = opt.PlannedFinish > materialDriven ? opt.PlannedFinish : materialDriven;

            // 硬约束（设备/工装/人员）：同型号在制优先时也可能被维保卡住
            var hard = CheckHardConstraints(line, q.ProductModel);
            opt.Feasible = hard.All(h => h.Ok);
            if (!opt.Feasible)
                opt.BlockReason = string.Join("；", hard.Where(h => !h.Ok).Select(h => $"{h.Name}（{h.Note}）"));

            // 瓶颈：物料 vs 换型 vs 产能
            if (materialDriven > opt.PlannedFinish)
            {
                opt.Bottleneck = CtpBottleneck.Material;
                opt.BottleneckText = $"物料 {materialReadyDate:MM-dd} 才到，比线体产能更晚（{materialText}）";
            }
            else if (co.Minutes >= 60)
            {
                opt.Bottleneck = CtpBottleneck.Changeover;
                opt.BottleneckText = $"换型 {co.Minutes} 分钟（{co.DetailText}）";
            }
            else if (load.RemainingHours > SchedulingRules.DailyCapacityHours * 5)
            {
                opt.Bottleneck = CtpBottleneck.Capacity;
                opt.BottleneckText = $"线体在制 {load.OpenWoCount} 张 / 剩余 {load.RemainingHours:0.#} h，排队到 {opt.LineFreeTime:MM-dd}";
            }

            // 部分承诺：期望交期前还能产多少（换型后开始算）
            var usableDays = (q.ExpectedDate.Date - opt.PlannedStart.Date).TotalDays;
            var promiseByCapacity = usableDays <= 0
                ? 0
                : (int)Math.Floor(usableDays * SchedulingRules.DailyCapacityHours / unitHours * yield);
            if (materialReadyDate.Date > q.ExpectedDate.Date) promiseByCapacity = 0;   // 料都到不了
            opt.PromiseQty = Math.Max(0, Math.Min(q.Qty, promiseByCapacity));

            // 评分：可交日越早越高；不可分配直接 0
            opt.Score = !opt.Feasible ? 0
                : Math.Round(Math.Max(0, 100 - (opt.EarliestDeliver.Date - today).TotalDays * 2 - shortageCount * 5), 1);

            opt.Evidence.Add($"换型：{(opt.FromModel.Length == 0 ? "线体空闲" : $"在制 {opt.FromModel}")} → {q.ProductModel}，"
                           + $"{co.MinutesText}（{co.DetailText}）");
            opt.Evidence.Add($"排队：线体在制 {load.OpenWoCount} 张 / 剩余 {load.RemainingHours:0.#} h，"
                           + $"含段间换型 {timeline.ChangeoverMinutes} 分 → 队尾 {opt.LineFreeTime:MM-dd HH:mm} 腾出");
            opt.Evidence.Add($"生产：{inputQty} 只 × {unitHours:0.00000} h ÷ {SchedulingRules.DailyCapacityHours:0.#} h/天 = "
                           + $"{requiredHours / SchedulingRules.DailyCapacityHours:0.##} 天 → {opt.PlannedFinish:MM-dd} 完工");
            opt.Evidence.Add($"物料：{(shortageCount == 0 ? "齐套" : $"{shortageCount} 项短缺，{materialReadyDate:MM-dd} 到料")}");
            opt.Evidence.Add($"最早可交：{opt.EarliestDeliver:yyyy-MM-dd}（{opt.MeetText}）");

            result.Options.Add(opt);
        }

        // 排序：可分配优先 → 最早可交 → 评分
        result.Options = result.Options
            .OrderByDescending(o => o.Feasible)
            .ThenBy(o => o.EarliestDeliver)
            .ThenByDescending(o => o.Score)
            .ThenBy(o => o.Line, StringComparer.Ordinal)
            .ToList();

        var best = result.Options.FirstOrDefault(o => o.Feasible);
        result.BestOption = best;
        result.BestLine = best?.Line ?? "";

        // ---- ④ 承诺三档 ----
        if (best is null)
        {
            result.Verdict = CtpVerdict.Reject;
            result.CommitDate = default;
            result.CommitQty = 0;
            result.SummaryText = "三条线体均不可分配（设备/工装/人员硬约束不通过），当前无法承诺交期。";
            result.Reasons.Add("线体：" + string.Join("；", result.Options.Select(o => $"{o.Line} {o.BlockReason}")));
            return result;
        }

        if (best.MeetExpected)
        {
            result.Verdict = CtpVerdict.Commit;
            result.CommitDate = best.EarliestDeliver;
            result.CommitQty = q.Qty;
            result.SummaryText = $"可承诺：{best.Line} 线 {best.EarliestDeliver:yyyy-MM-dd} 可交 {q.Qty} 只"
                               + $"（期望 {q.ExpectedDate:yyyy-MM-dd}，提前 {Math.Abs((best.EarliestDeliver.Date - q.ExpectedDate.Date).TotalDays):0} 天）。";
        }
        else if (best.PromiseQty >= (int)Math.Ceiling(q.Qty * 0.3))
        {
            result.Verdict = CtpVerdict.Conditional;
            result.CommitDate = q.ExpectedDate;
            result.CommitQty = best.PromiseQty;
            result.SummaryText = $"有条件承诺：{best.Line} 线 {q.ExpectedDate:yyyy-MM-dd} 前最多交 {best.PromiseQty} 只（占 {best.PromiseQty * 100.0 / q.Qty:0.#}%），"
                               + $"余量 {q.Qty - best.PromiseQty} 只最早 {best.EarliestDeliver:yyyy-MM-dd} 补齐。"
                               + $"卡点：{best.BottleneckText}";
        }
        else
        {
            result.Verdict = CtpVerdict.Reject;
            result.CommitDate = best.EarliestDeliver;
            result.CommitQty = 0;
            result.SummaryText = $"不可承诺：{best.Line} 线最早 {best.EarliestDeliver:yyyy-MM-dd} 才能交（比期望晚 {best.DelayDays} 天），"
                               + $"期望交期前只能交 {best.PromiseQty} 只（不足 30%）。建议与客户改期，或走插单/加班评估。";
        }

        result.Reasons.Add($"候选线体：{string.Join("｜", result.Options.Select(o => $"{o.Line} {o.EarliestText}{(o.Feasible ? "" : "（不可分配）")}"))}");
        return result;
    }
}
