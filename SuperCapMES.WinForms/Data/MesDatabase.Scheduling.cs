using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 智能排产与齐套校验 —— 仓储/推理层
//
// 一句话说清这里在干什么：
//   给定一张 PO（产品型号 + 订单量 + 交期），从**历史工单**里统计出良率与单位工时，
//   据此推出「投产数量 / 推荐线体 / 开工时间 / 预计完工」；同时按 BOM 展开物料需求，
//   与现有库存逐项比对，缺料就把缺口报出来。
//
// 为什么用"统计 + 规则"而不是直接问大模型：
//   排产结论是要拿去开工的，必须**可审计**——"为什么建议 6086 只而不是 6000 只"
//   得能一路追到"哪几张历史工单、什么公式"。大模型给不出这种追溯，
//   所以它在这条链路上的角色是"把结论组织成人话"（见 AI 助手云端后端），
//   而不是产生结论。
//
// 口径（写在函数注释里，避免各处说法不一）：
//   · 可用库存 = 所有库位中**除待检区、退货待处理区之外**的在库合计。
//     待检/退货物料尚未判定合格，不能直接投产，单独统计为"不可用量"提示出来。
//   · 需求 = 单位用量 × (1 + 损耗率/100) × 投产数量（投产数量已含良率补偿）。
//   · 1 个"生产日" = 每日有效产能工时 16h（两班 × 8h），见 SchedulingRules.DailyCapacityHours。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>全部有 BOM 的产品型号（去重、排序）。供助手按型号回答齐套问题。</summary>
    public static List<string> GetProductModels()
        => ErpBomItems.Select(b => b.ProductModel)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

    // ================= 库存口径 =================

    /// <summary>
    /// 该库位的物料是否可投入生产。
    /// 待检区（未判定）与退货待处理区（待处置）一律不算可用——这是齐套校验的第一道口径，
    /// 否则会把"来料还没判的料"算成可用库存，做出来的齐套结论是假的。
    /// 空/未知库位同样按**不可用**处理：来路不明的库存宁可漏算进"不可用量"提示，
    /// 也不能虚报成可用——虚报会让齐套通过、生产中断。
    /// </summary>
    public static bool IsUsableLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return false;
        return !string.Equals(location.Trim(), WmsLocations.PendingInspection, StringComparison.Ordinal)
            && !string.Equals(location.Trim(), WmsLocations.ReturnPending, StringComparison.Ordinal);
    }

    /// <summary>某物料的可用量（可投产库位在库合计）。纯查询，不修改任何集合。</summary>
    public static double GetMaterialAvailableQty(string materialCode)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return 0;
        return WmsStocks
            .Where(s => CodeEquals(s.MaterialCode, materialCode) && IsUsableLocation(s.Location))
            .Sum(s => s.Quantity);
    }

    /// <summary>某物料的不可用量（待检区 + 退货待处理区在库合计）。</summary>
    public static double GetMaterialFrozenQty(string materialCode)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return 0;
        return WmsStocks
            .Where(s => CodeEquals(s.MaterialCode, materialCode) && !IsUsableLocation(s.Location))
            .Sum(s => s.Quantity);
    }

    /// <summary>某物料的可用批次概要：批次数 + 最早到期日（用于提示效期风险）。</summary>
    public static string GetMaterialLotHint(string materialCode)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return "";
        var lots = WmsStocks
            .Where(s => CodeEquals(s.MaterialCode, materialCode)
                        && IsUsableLocation(s.Location)
                        && s.Quantity > 1e-9)
            .ToList();
        if (lots.Count == 0) return "";

        var expiries = lots.Where(l => l.ExpiryDate.HasValue)
                           .Select(l => l.ExpiryDate!.Value)
                           .OrderBy(d => d)
                           .ToList();
        return expiries.Count == 0
            ? $"{lots.Count} 批在库"
            : $"{lots.Count} 批在库；最早到期 {expiries[0]:yyyy-MM-dd}";
    }

    // ================= 齐套校验（BOM 需求 vs 库存） =================

    /// <summary>
    /// 齐套校验：按产品型号展开 BOM，逐项算需求量并与可用库存比对。
    /// </summary>
    /// <param name="productModel">产品型号（区分大小写不敏感）</param>
    /// <param name="planQty">投产数量（未含良率补偿时由调用方自行换算）</param>
    /// <param name="orderQty">订单数量（仅用于展示"订单→投产"的差异来源；≤0 时取 planQty）</param>
    public static MaterialCheckResult CheckBomAvailability(string productModel, int planQty, int orderQty = 0)
    {
        var result = new MaterialCheckResult
        {
            ProductModel = (productModel ?? "").Trim(),
            PlanQty = Math.Max(0, planQty),
            OrderQty = orderQty > 0 ? orderQty : Math.Max(0, planQty)
        };

        if (result.ProductModel.Length == 0 || result.PlanQty <= 0)
        {
            result.NoBom = FetchErpBom(result.ProductModel).Count == 0;
            return result;
        }

        var bom = FetchErpBom(result.ProductModel);
        if (bom.Count == 0)
        {
            result.NoBom = true;
            return result;
        }

        foreach (var b in bom)
        {
            var requirement = GetBomRequirement(b, result.PlanQty);
            var available = GetMaterialAvailableQty(b.MaterialCode);
            var frozen = GetMaterialFrozenQty(b.MaterialCode);

            var row = new MaterialShortageRow
            {
                MaterialCode = b.MaterialCode,
                MaterialName = b.MaterialName,
                Spec = b.Spec,
                Unit = b.Unit,
                MaterialType = b.MaterialType,
                QtyPerUnit = b.QtyPerUnit,
                LossRate = b.LossRate,
                Requirement = requirement,
                Available = available,
                FrozenQty = frozen,
                LotHint = GetMaterialLotHint(b.MaterialCode)
            };

            if (row.Shortage > 1e-9)
            {
                row.Status = ShortageStatus.Short;
                row.Advice = $"缺 {row.ShortageText} {b.Unit}：需补料采购，或按现有库存削减投产数。";
            }
            else if (available < requirement * SchedulingRules.TightBufferRatio)
            {
                // 刚好够用：投产过程中一旦出现来料不良或额外损耗，立刻变成缺料
                row.Status = ShortageStatus.Tight;
                row.Advice = $"仅富余 {row.Surplus:0.##} {b.Unit}，无缓冲；注意批次效期与来料波动。";
            }
            else
            {
                row.Status = ShortageStatus.Ok;
            }

            if (frozen > 1e-9)
            {
                row.Advice = string.IsNullOrEmpty(row.Advice)
                    ? $"另有 {frozen:0.##} {b.Unit} 在待检/退货区，判定合格后可用。"
                    : row.Advice + $"（另有 {frozen:0.##} {b.Unit} 在待检/退货区未判定）";
            }

            result.Rows.Add(row);
        }

        // 主材优先展示（缺料时先看主材），同类内按缺口从大到小
        result.Rows = result.Rows
            .OrderBy(r => TypeOrder(r.MaterialType))
            .ThenByDescending(r => r.Shortage)
            .ThenBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ToList();
        return result;
    }

    private static int TypeOrder(string? materialType) => materialType switch
    {
        ErpMaterialTypes.Main => 0,
        ErpMaterialTypes.Aux => 1,
        ErpMaterialTypes.Pack => 2,
        _ => 3
    };

    // ================= 历史样本统计（推理依据） =================

    /// <summary>
    /// 统计某型号的历史生产表现：良率、单位工时、常用线体。
    /// 样本不足时**不硬算**，改用兜底值并把 <c>YieldFromHistory/HoursFromHistory</c> 置 false，
    /// 让界面能如实标注"样本不足，采用默认值"——排产结论拿着去做决策，不能假装有依据。
    /// </summary>
    public static ProductionHistory GetProductionHistory(string productModel)
    {
        var model = (productModel ?? "").Trim();
        var h = new ProductionHistory { ProductModel = model };
        if (model.Length == 0) return WithDefaults(h, "未指定产品型号");

        // 只统计有产出的工单：未开工的工单没有产出，进样本会把良率与工时拉偏
        var samples = WorkOrders
            .Where(w => string.Equals(w.ProductModel, model, StringComparison.OrdinalIgnoreCase)
                        && (w.FinishedQty > 0 || w.NgQty > 0))
            .OrderBy(w => w.StartDate)
            .ToList();

        h.SampleWoNos = samples.Select(w => w.WoNo).ToList();
        h.TotalPlanQty = samples.Sum(w => w.PlanQty);
        h.TotalFinishedQty = samples.Sum(w => w.FinishedQty);
        h.TotalNgQty = samples.Sum(w => w.NgQty);

        if (h.SampleWoNos.Count == 0)
            return WithDefaults(h, $"型号 {model} 无历史生产记录");

        var denom = h.TotalFinishedQty + h.TotalNgQty;
        if (denom > 0)
        {
            h.AvgYield = (double)h.TotalFinishedQty / denom;
            h.YieldFromHistory = true;
        }
        else
        {
            h.AvgYield = SchedulingRules.DefaultYield;
        }

        var withHours = samples.Where(w => w.WorkHours > 0 && w.FinishedQty > 0).ToList();
        if (withHours.Count > 0)
        {
            h.UnitHours = withHours.Sum(w => w.WorkHours) / withHours.Sum(w => w.FinishedQty);
            h.HoursFromHistory = true;
        }
        else
        {
            h.UnitHours = SchedulingRules.DefaultUnitHours;
        }

        h.LineUsage = samples
            .Where(w => !string.IsNullOrWhiteSpace(w.Line))
            .GroupBy(w => w.Line)
            .Select(g => (Line: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Line, StringComparer.Ordinal)
            .ToList();
        h.PreferredLine = h.LineUsage.Count > 0 ? h.LineUsage[0].Line : "";

        // ---- 口径说明（逐条展示，用户能看懂这些数是怎么来的） ----
        h.Notes.Add($"样本 {h.SampleCount} 张同类工单：{string.Join("、", h.SampleWoNos)}");
        h.Notes.Add(h.YieldFromHistory
            ? $"良率 = Σ完工 {h.TotalFinishedQty} ÷ (Σ完工 {h.TotalFinishedQty} + Σ不良 {h.TotalNgQty}) = {h.AvgYield * 100:0.0}%"
            : $"样本无产出数据，良率采用默认值 {SchedulingRules.DefaultYield * 100:0.0}%");
        h.Notes.Add(h.HoursFromHistory
            ? $"单位工时 = Σ报工工时 {withHours.Sum(w => w.WorkHours):0.#}h ÷ Σ完工 {withHours.Sum(w => w.FinishedQty)} 只 = {h.UnitHours * 3600:0.0} 秒/只"
            : $"样本无报工工时，单位工时采用默认值 {SchedulingRules.DefaultUnitHours * 3600:0.0} 秒/只");
        if (h.PreferredLine.Length > 0)
            h.Notes.Add($"常用线体：{string.Join("、", h.LineUsage.Select(x => $"{x.Line} {x.Count} 次"))}");

        if (!h.EnoughSamples)
            h.Notes.Add($"⚠ 样本量 {h.SampleCount} 张 < 门槛 {SchedulingRules.MinSampleCount} 张，结论仅供参考。");

        return h;
    }

    private static ProductionHistory WithDefaults(ProductionHistory h, string reason)
    {
        h.AvgYield = SchedulingRules.DefaultYield;
        h.UnitHours = SchedulingRules.DefaultUnitHours;
        h.YieldFromHistory = false;
        h.HoursFromHistory = false;
        h.Notes.Add(reason + "，良率与工时均采用默认值，结论仅供参考。");
        return h;
    }

    // ================= 线体负载 =================

    /// <summary>工单是否还没做完（未开始 / 生产中）。</summary>
    public static bool IsOpenWorkOrder(WorkOrder w) =>
        w is not null && (w.Status == "未开始" || w.Status == "生产中");

    /// <summary>系统已知的线体（取自工单实际用过的线体，兜底 L1/L2/L3），按名称排序。</summary>
    public static List<string> KnownLines()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "L1", "L2", "L3" };
        foreach (var w in WorkOrders)
            if (!string.IsNullOrWhiteSpace(w.Line)) set.Add(w.Line.Trim());
        return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 某条线的当前负载：手上还有几张未完工工单、剩余多少工时、什么时候能腾出手。
    /// 新工单最早只能从 <see cref="LineLoadInfo.EarliestFreeTime"/> 开始排。
    /// </summary>
    public static LineLoadInfo GetLineLoad(string line, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var info = new LineLoadInfo { Line = (line ?? "").Trim() };
        if (info.Line.Length == 0) return info;

        var open = WorkOrders
            .Where(w => IsOpenWorkOrder(w) && LineEquals(w.Line, info.Line))
            .ToList();

        info.OpenWoCount = open.Count;
        info.OpenPlanQty = open.Sum(w => w.PlanQty);
        info.RemainingQty = open.Sum(w => Math.Max(0, w.PlanQty - w.FinishedQty));

        double hours = 0;
        foreach (var w in open)
        {
            var remaining = Math.Max(0, w.PlanQty - w.FinishedQty);
            if (remaining <= 0) continue;

            // 该工单自己报过工时就用它自己的；没报过就借用同型号的历史单位工时
            var unit = w.WorkHours > 0 && w.FinishedQty > 0
                ? w.WorkHours / w.FinishedQty
                : GetProductionHistory(w.ProductModel).UnitHours;
            hours += remaining * unit;
        }

        info.RemainingHours = hours;
        info.EarliestDueDate = open.Count > 0 ? open.Min(w => w.DueDate) : null;
        info.EarliestFreeTime = at.AddDays(hours / SchedulingRules.DailyCapacityHours);
        return info;
    }

    // ================= 排产建议 =================

    /// <summary>
    /// 指定 PO → 生成排产建议。
    /// 推理链：历史良率（决定投产数量）→ 历史单位工时（决定需要多少产能）
    ///         → 推荐线体的在手负载（决定最早何时能开工）→ 算出完工与交期余量
    ///         → BOM 展开做齐套校验。
    /// 每一步都产出可读的 <see cref="ScheduleRecommendation.Evidence"/>，界面直接摊开给人看。
    /// </summary>
    /// <param name="poNo">ERP 订单号</param>
    /// <param name="now">"当前时间"（自测注入固定值，保证断言确定）</param>
    public static ScheduleRecommendation? RecommendSchedule(string poNo, DateTime? now = null)
    {
        var po = FindErpPo(poNo);
        if (po is null) return null;

        var at = now ?? DateTime.Now;
        var rec = new ScheduleRecommendation
        {
            PoNo = po.PoNo,
            Customer = po.Customer,
            ProductModel = po.ProductModel,
            ProductName = po.ProductName,
            OrderQty = po.OrderQty,
            DueDate = po.DueDate
        };

        // ---- ① 历史证据 ----
        var history = GetProductionHistory(po.ProductModel);
        rec.History = history;
        rec.Yield = history.YieldFromHistory ? history.AvgYield : SchedulingRules.DefaultYield;
        rec.UnitHours = history.HoursFromHistory ? history.UnitHours : SchedulingRules.DefaultUnitHours;

        // ---- ② 投产数量（良率补偿） ----
        rec.InputQty = rec.Yield <= 0 ? po.OrderQty : (int)Math.Ceiling(po.OrderQty / rec.Yield);
        rec.RequiredHours = rec.InputQty * rec.UnitHours;

        // ---- ③ 推荐线体：历史首选；无历史则挑当前最闲的线 ----
        var line = history.PreferredLine;
        string lineReason;
        if (string.IsNullOrWhiteSpace(line))
        {
            line = KnownLines().OrderBy(l => GetLineLoad(l, at).RemainingHours).First();
            lineReason = $"该型号无历史排线记录，取当前负载最轻的线体 {line}";
        }
        else
        {
            var used = history.LineUsage.FirstOrDefault(x => x.Line == line);
            lineReason = $"历史 {history.SampleCount} 张同类工单中，{line} 用过 {used.Count} 次（占比最高）";
        }
        rec.RecommendedLine = line;

        // ---- ④ 线体负载 → 最早开工时间 ----
        var load = GetLineLoad(line, at);
        rec.LineLoad = load;
        rec.LineOpenWoNos = WorkOrders
            .Where(w => IsOpenWorkOrder(w) && LineEquals(w.Line, line))
            .OrderBy(w => w.DueDate)
            .Select(w => w.WoNo)
            .ToList();

        rec.PlannedStart = load.EarliestFreeTime > at ? load.EarliestFreeTime : at;
        rec.PlannedFinish = rec.PlannedStart.AddDays(rec.RequiredHours / SchedulingRules.DailyCapacityHours);
        rec.SlackDays = (po.DueDate - rec.PlannedFinish).TotalDays;

        // ---- ⑤ 风险判定与处置建议 ----
        if (rec.SlackDays < 0)
        {
            rec.Risk = ScheduleRisk.Late;
            var lateDays = Math.Abs(rec.SlackDays);
            rec.RiskText = $"按 {line} 现有负载推算，最早 {rec.PlannedStart:MM-dd} 才能开工，"
                           + $"预计 {rec.PlannedFinish:MM-dd} 完工，比交期晚 {lateDays:0.#} 天。";

            // 备选：换到更闲的线体
            var alt = KnownLines()
                .Where(l => !LineEquals(l, line))
                .Select(l => new { Line = l, Load = GetLineLoad(l, at) })
                .OrderBy(x => x.Load.RemainingHours)
                .FirstOrDefault();
            if (alt is not null)
            {
                var altStart = alt.Load.EarliestFreeTime > at ? alt.Load.EarliestFreeTime : at;
                var altFinish = altStart.AddDays(rec.RequiredHours / SchedulingRules.DailyCapacityHours);
                var altSlack = (po.DueDate - altFinish).TotalDays;
                rec.AlternateLine = alt.Line;
                rec.AlternateReason = altSlack >= 0
                    ? $"改排 {alt.Line}：预计 {altFinish:MM-dd} 完工，可在交期内完成（余量 {altSlack:0.#} 天）"
                    : $"改排 {alt.Line}：预计 {altFinish:MM-dd} 完工，仍需延期 {Math.Abs(altSlack):0.#} 天";
                rec.RiskText += " " + rec.AlternateReason + "。";
            }

            // 加班：把落后的小时数补回来
            var needHours = rec.RequiredHours + load.RemainingHours;
            var availHours = (po.DueDate - at).TotalDays * SchedulingRules.DailyCapacityHours;
            var gap = needHours - availHours;
            if (gap > 0)
                rec.RiskText += $" 若不加线，需在可排窗口内额外投入约 {gap:0.#} 小时产能（加班或增开班次）。";
        }
        else if (rec.SlackDays < SchedulingRules.TightSlackDays)
        {
            rec.Risk = ScheduleRisk.Tight;
            rec.RiskText = $"预计 {rec.PlannedFinish:MM-dd} 完工，交期 {po.DueDate:MM-dd}，"
                           + $"余量仅 {rec.SlackDays:0.#} 天，工序衔接无缓冲；建议提前备料并锁定 {line} 产能。";
        }
        else
        {
            rec.Risk = ScheduleRisk.Normal;
            rec.RiskText = $"预计 {rec.PlannedFinish:MM-dd} 完工，交期 {po.DueDate:MM-dd}，"
                           + $"余量 {rec.SlackDays:0.#} 天，可按建议排产。";
        }

        // ---- ⑥ 齐套校验（按投产数量展开 BOM） ----
        rec.StockCheck = CheckBomAvailability(po.ProductModel, rec.InputQty, po.OrderQty);

        // ---- ⑦ 推理依据（可解释，逐条可追问） ----
        rec.Evidence = BuildScheduleEvidence(rec, lineReason, at);
        return rec;
    }

    /// <summary>把推理链摊成可读条目——这是"可解释"的落地处，不要为了省事只输出结论。</summary>
    private static List<string> BuildScheduleEvidence(ScheduleRecommendation rec, string lineReason, DateTime at)
    {
        var h = rec.History;
        var ev = new List<string>
        {
            $"① 历史样本：{h.SampleCount} 张同类工单"
                + (h.SampleWoNos.Count > 0 ? $"（{string.Join("、", h.SampleWoNos)}）" : "")
                + (h.EnoughSamples ? "" : $"；不足门槛 {SchedulingRules.MinSampleCount} 张，结论仅供参考"),
            $"② 采用良率 {rec.Yield * 100:0.0}%"
                + (h.YieldFromHistory ? "（由历史完工/不良算出）" : "（样本不足，默认值）"),
            $"③ 采用单位工时 {rec.UnitHours * 3600:0.0} 秒/只"
                + (h.HoursFromHistory ? "（由历史报工工时算出）" : "（样本不足，默认值）"),
            $"④ 投产数量 = ⌈订单 {rec.OrderQty} ÷ 良率 {rec.Yield * 100:0.0}%⌉ = {rec.InputQty} 只"
                + $"（其中 {rec.InputQty - rec.OrderQty} 只是给不良留的补偿）",
            $"⑤ 推荐线体：{rec.RecommendedLine} —— {lineReason}",
            rec.LineLoad.OpenWoCount == 0
                ? $"⑥ 线体负载：{rec.RecommendedLine} 当前无在手工单，可立即开工"
                : $"⑥ 线体负载：{rec.RecommendedLine} 有 {rec.LineLoad.OpenWoCount} 张在手"
                  + $"（{string.Join("、", rec.LineOpenWoNos)}），剩余 {rec.LineLoad.RemainingHours:0.#}h"
                  + $" ≈ {rec.LineLoad.DaysToClear:0.#} 个生产日",
            $"⑦ 建议开工 {rec.PlannedStart:yyyy-MM-dd HH:mm}（= 该线手上活干完的时间与当前时间取晚者）",
            $"⑧ 本单工时 = 投产 {rec.InputQty} × 单位工时 {rec.UnitHours * 3600:0.0} 秒 = {rec.RequiredHours:0.#}h"
              + $" ≈ {rec.RequiredHours / SchedulingRules.DailyCapacityHours:0.#} 个生产日"
              + $"（每日有效产能 {SchedulingRules.DailyCapacityHours:0.#}h）",
            $"⑨ 预计完工 {rec.PlannedFinish:yyyy-MM-dd HH:mm}，交期 {rec.DueDate:yyyy-MM-dd HH:mm}，"
              + $"余量 {rec.SlackDays:0.#} 天 → 风险 {rec.Risk}",
            rec.StockCheck.NoBom
                ? "⑩ 齐套校验：该型号无 BOM 数据，未能校验物料"
                : $"⑩ 齐套校验：{rec.StockCheck.Rows.Count} 项物料，满足 {rec.StockCheck.OkCount}、"
                  + $"紧张 {rec.StockCheck.TightCount}、短缺 {rec.StockCheck.ShortCount}"
                  + (rec.StockCheck.AllSatisfied ? " → 齐套通过" : $" → 缺料：{rec.StockCheck.ShortageBrief}")
        };

        if (rec.AlternateLine.Length > 0)
            ev.Add($"⑪ 备选方案：{rec.AlternateReason}");

        if (rec.StockCheck.ShortCount > 0)
            ev.Add($"⑫ 处置建议：先按缺口补料（{rec.StockCheck.ShortageBrief}），"
                   + "或与计划协商把投产数降到现有库存可覆盖的水平。");

        return ev;
    }

    // ================= 按建议生成工单 =================

    /// <summary>
    /// 按排产建议生成工单（工单号 = WO-{PoNo}，与 ERP 下发口径一致）。
    /// 校验：PO 存在、尚未下发、同号工单不存在。
    /// <paramref name="forceIgnoreShortage"/> 为 false 且存在缺料时**拒绝创建**——
    /// 缺料还开工是排产里最典型的错误，默认必须拦住；要越过得显式传 true（界面会二次确认）。
    /// </summary>
    public static (bool Ok, string Message, string WoNo) CreateWorkOrderFromRecommendation(
        ScheduleRecommendation rec, string line, string @operator, bool forceIgnoreShortage = false)
    {
        if (rec is null) return (false, "排产建议为空，无法生成工单。", "");
        var po = FindErpPo(rec.PoNo);
        if (po is null) return (false, $"ERP 中不存在订单 {rec.PoNo}。", "");
        if (po.Status == ErpPoStatus.Pushed) return (false, $"订单 {rec.PoNo} 已下发，请勿重复生成工单。", "");

        var woNo = "WO-" + po.PoNo;
        if (WorkOrders.Any(w => string.Equals(w.WoNo, woNo, StringComparison.OrdinalIgnoreCase)))
            return (false, $"工单 {woNo} 已存在。", "");

        // 缺料拦截：默认不放行
        var check = CheckBomAvailability(po.ProductModel, rec.InputQty, po.OrderQty);
        if (!check.AllSatisfied && !check.NoBom && !forceIgnoreShortage)
            return (false, $"物料不齐套，已阻止创建：{check.ShortageBrief}。如需强制开工请确认后重试。", "");

        var lineValue = string.IsNullOrWhiteSpace(line) ? rec.RecommendedLine : line.Trim();
        if (string.IsNullOrWhiteSpace(lineValue)) lineValue = "L1";

        var now = DateTime.Now;
        WorkOrders.Add(new WorkOrder
        {
            WoNo = woNo,
            ErpOrderNo = po.PoNo,
            ProductModel = po.ProductModel,
            Line = lineValue,
            PlanQty = rec.InputQty,
            FinishedQty = 0,
            NgQty = 0,
            Status = "未开始",
            StartDate = rec.PlannedStart,
            DueDate = po.DueDate,
            ErpReportStatus = ErpReportStatus.NotReported
        });
        po.Status = ErpPoStatus.Pushed;
        po.PushedTime = now;

        var warn = string.IsNullOrEmpty(@operator) ? "" : $"（操作人：{@operator}）";

        // 缺料时**同步生成采购申请**：只报"缺"却没人去补，是排产链路上最典型的断点。
        // 这里把缺口直接转成一张草稿申请单，与本工单绑定，让缺料有出口。
        var prNote = "";
        if (!check.AllSatisfied && !check.NoBom)
        {
            var (prOk, prMsg, prNo) = BuildRequestFromWorkOrder(woNo, @operator, "工单缺料自动生成，请及时补料");
            if (prOk) prNote = $"；已同步生成采购申请 {prNo}";
        }

        return (true,
            $"已生成工单 {woNo}：{po.ProductModel}，投产 {rec.InputQty} 只，线体 {lineValue}，"
            + $"计划 {rec.PlannedStart:MM-dd} 开工、{rec.PlannedFinish:MM-dd} 完工{warn}{prNote}。",
            woNo);
    }

    // ================= 全厂缺料预警（供预警中心 / AI 助手复用） =================

    /// <summary>
    /// 按"已下发但未完工的工单"汇总物料总需求，与可用库存比对，列出会缺料的物料。
    /// 用途：不只看单张工单，而是看**在手计划整体**会不会断料——这是计划员每天要问的问题。
    /// </summary>
    public static List<MaterialShortageRow> GetOpenWorkOrderShortage()
    {
        var open = WorkOrders.Where(IsOpenWorkOrder).ToList();
        if (open.Count == 0) return new List<MaterialShortageRow>();

        // 物料 → 累计需求 / 单位 / 名称 / 类型
        var demand = new Dictionary<string, (double Qty, string Unit, string Name, string Type, string Spec)>(
            StringComparer.Ordinal);

        foreach (var w in open)
        {
            var remaining = Math.Max(0, w.PlanQty - w.FinishedQty);
            if (remaining <= 0) continue;
            foreach (var b in FetchErpBom(w.ProductModel))
            {
                var need = GetBomRequirement(b, remaining);
                if (demand.TryGetValue(b.MaterialCode, out var cur))
                    demand[b.MaterialCode] = (cur.Qty + need, b.Unit, b.MaterialName, b.MaterialType, b.Spec);
                else
                    demand[b.MaterialCode] = (need, b.Unit, b.MaterialName, b.MaterialType, b.Spec);
            }
        }

        var rows = new List<MaterialShortageRow>();
        foreach (var kv in demand)
        {
            var available = GetMaterialAvailableQty(kv.Key);
            var row = new MaterialShortageRow
            {
                MaterialCode = kv.Key,
                MaterialName = kv.Value.Name,
                Spec = kv.Value.Spec,
                Unit = kv.Value.Unit,
                MaterialType = kv.Value.Type,
                Requirement = kv.Value.Qty,
                Available = available,
                FrozenQty = GetMaterialFrozenQty(kv.Key),
                LotHint = GetMaterialLotHint(kv.Key)
            };
            row.Status = row.Shortage > 1e-9 ? ShortageStatus.Short
                : available < row.Requirement * SchedulingRules.TightBufferRatio ? ShortageStatus.Tight
                : ShortageStatus.Ok;
            if (row.Status == ShortageStatus.Short)
                row.Advice = $"在手计划共需 {row.RequirementText} {row.Unit}，可用仅 {row.AvailableText}，"
                             + $"缺 {row.ShortageText} {row.Unit}；不补料将影响投产。";
            rows.Add(row);
        }

        return rows
            .OrderBy(r => r.Status == ShortageStatus.Short ? 0 : r.Status == ShortageStatus.Tight ? 1 : 2)
            .ThenByDescending(r => r.Shortage)
            .ThenBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ToList();
    }

    // ================= 小工具 =================

    private static bool CodeEquals(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool LineEquals(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
