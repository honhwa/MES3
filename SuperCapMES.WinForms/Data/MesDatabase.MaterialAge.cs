using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 物料全链管控（二期 3.2 + 3.3）—— 临期三色 + 三方案决策 —— 数据层
//
// 两个核心口径（写在这里，避免别处再各想一套）：
//
//  ① 三色判定的依据是**两件事**，不是一件：
//       · 效期还剩几天（ExpiryDate − 今天）      → 时间维度
//       · 按近 90 天实测消耗速度，到期前能否用完  → 数量维度
//     只用效期分段（"剩 30 天内都叫临期"）会把"剩 25 天但每天用 500、3 天就用完"的料
//     也标成危险，计划员很快就不看这个颜色了。所以消耗不完也判临期，并给出预计报废量。
//     消耗样本 < 2 条时**如实标"不可估"**，不拿假设值硬算。
//
//  ② 处置动作全部落到既有单据，本模块只多做"决策留痕"：
//       报废 → ScrapMaterial（既有：先写废弃台账再改主表、数量归零）
//       重检 → ReinspectMaterial（既有：只允许已过期批次，新效期须晚于今天）
//       提前消耗 → 登记优先消耗标记（本原型不改排程引擎，界面注明）
//     不另造第二套处置逻辑——两套逻辑必然打架，而且报废是**不可逆**动作，更不能有两份实现。
//
//  金额与审批：Amount 统一取"本次决策涉及的在库金额"（数量 × 单价，单价为演示参数）。
//     提前消耗 = 0 元（只是排产指令，不需要资产审批）；
//     重检 / 报废 = 整批在库金额，按 500 / 5000 分档决定审批级别，级别不够**后端直接拒**。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>批次处置决策台账（只追加）</summary>
    public static List<AgeDecision> AgeDecisions { get; } = new();

    /// <summary>优先消耗标记（提前排产方案的落地凭据）</summary>
    public static List<MaterialPriorityMark> MaterialPriorityMarks { get; } = new();

    /// <summary>物料单价台账（演示参数）</summary>
    public static List<MaterialPrice> MaterialPrices { get; } = new();

    private static bool _materialPriceReady;
    private static int _ageDecisionSeq, _priorityMarkSeq;

    // ================= 单价（演示参数） =================

    /// <summary>
    /// 物料单价种子：按**物料类别基数 + 编码稳定哈希**推导，保证任何运行日结果一致。
    /// 真实环境取自 ERP 物料主数据 / 采购价目表，这里只是让"金额分级审批"有可算的量级。
    /// </summary>
    public static void EnsureMaterialPriceSeed()
    {
        if (_materialPriceReady) return;

        foreach (var m in WmsMaterials)
        {
            var h = Math.Abs(StableHash(m.Code));
            var basePrice = m.Category switch
            {
                WmsCategories.Raw => 18.0,
                WmsCategories.Aux => 35.0,
                _ => 260.0
            };
            MaterialPrices.Add(new MaterialPrice
            {
                MaterialCode = m.Code,
                MaterialName = m.MaterialName,
                Unit = m.Unit,
                UnitPrice = Math.Round(basePrice * (0.70 + (h % 7) * 0.12), 2)
            });
        }

        _materialPriceReady = true;
    }

    /// <summary>取物料单价（未知物料按 20 元兜底，避免界面出现 0 元）。纯查询。</summary>
    public static double GetMaterialUnitPrice(string materialCode)
    {
        EnsureMaterialPriceSeed();
        var code = (materialCode ?? "").Trim();
        var p = MaterialPrices.FirstOrDefault(x =>
            string.Equals(x.MaterialCode, code, StringComparison.OrdinalIgnoreCase));
        return p?.UnitPrice ?? 20.0;
    }

    // ================= 3.2 临期看板 =================

    /// <summary>
    /// 临期看板：逐批算「剩余效期 + 预计用完日 → 三色等级 + 预计报废量 + 在库金额」。
    /// 纯查询，不改任何数据。
    /// </summary>
    /// <param name="filter">显示范围（临期与已过期 / 全部批次 / 仅已过期）</param>
    public static MaterialAgeBoard BuildMaterialAgeBoard(string filter = AgeFilters.NearExpired)
    {
        EnsureMaterialPriceSeed();
        var today = DateTime.Today;

        var rows = new List<MaterialAgeRow>();
        foreach (var s in WmsStocks)
        {
            if (s.IsScrapped || s.Quantity <= 1e-9) continue;   // 已报废 / 空批次不参与

            var mat = FindWmsMaterial(s.MaterialCode);

            // 消耗速度：统一走 GetMaterialDailyConsumption（领用实测优先，样本不足用工单需求，都没有标不可估），
            // 与「安全库存补货」页共用同一口径，避免两个页面算出两个日均消耗
            var daily = GetMaterialDailyConsumption(s.MaterialCode, out var source, out var samples);

            var row = new MaterialAgeRow
            {
                MaterialCode = s.MaterialCode,
                MaterialName = string.IsNullOrWhiteSpace(s.MaterialName)
                    ? (mat?.MaterialName ?? s.MaterialCode) : s.MaterialName,
                Spec = string.IsNullOrWhiteSpace(s.Spec) ? (mat?.Spec ?? "") : s.Spec,
                Unit = string.IsNullOrWhiteSpace(s.Unit) ? (mat?.Unit ?? "") : s.Unit,
                Category = mat?.Category ?? "",
                LotNo = s.LotNo,
                Location = s.Location,
                Quantity = s.Quantity,
                ExpiryDate = s.ExpiryDate,
                NearDays = AgeRules.NearDaysOf(mat?.Category ?? ""),
                DailyConsumption = Math.Round(daily, 4),
                ConsumptionSamples = samples,
                ConsumptionSource = source,
                UnitPrice = GetMaterialUnitPrice(s.MaterialCode)
            };

            row.Amount = Math.Round(row.Quantity * row.UnitPrice, 2);
            row.RemainingDays = s.ExpiryDate is null ? 0 : (int)(s.ExpiryDate.Value.Date - today).TotalDays;
            row.ConsumeDays = daily > 1e-9 ? (int)Math.Ceiling(row.Quantity / daily) : 0;
            row.ExpectedRunOutDate = row.ConsumeDays > 0 ? today.AddDays(row.ConsumeDays) : null;

            if (s.ExpiryDate is not null)
            {
                // 到期前可消耗量 = 日均 × 剩余有效天数（已过期则为 0）
                var usableDays = Math.Max(0, row.RemainingDays);
                var canConsume = daily * usableDays;
                row.WasteQty = Math.Max(0, Math.Round(row.Quantity - canConsume, 2));
                row.WasteDays = row.ConsumeDays > 0 ? Math.Max(0, row.ConsumeDays - usableDays) : 0;

                // 没有任何估算依据时不硬算报废量（宁可显示"不可估"）
                if (!row.HasEstimate && row.RemainingDays >= 0) row.WasteQty = 0;
            }

            row.AgeLevel =
                s.ExpiryDate is null ? AgeLevels.NoExpiry
                : row.RemainingDays < 0 ? AgeLevels.Expired
                : row.RemainingDays <= row.NearDays ? AgeLevels.Near
                : row.WasteQty > 1e-9 ? AgeLevels.Near     // 效期还长但到期前用不完，同样要预警
                : AgeLevels.Fresh;

            rows.Add(row);
        }

        // 排序：已过期（超期久的在前）→ 临期（剩效期短的在前）→ 其余按风险金额降序
        var all = rows
            .OrderBy(r => r.AgeLevel == AgeLevels.Expired ? 0 : r.AgeLevel == AgeLevels.Near ? 1 : 2)
            .ThenBy(r => r.RemainingDays)
            .ThenByDescending(r => r.Amount)
            .ThenBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ThenBy(r => r.LotNo, StringComparer.Ordinal)
            .ToList();

        var board = new MaterialAgeBoard
        {
            All = all,
            ScopeText = string.IsNullOrWhiteSpace(filter) ? AgeFilters.NearExpired : filter
        };

        board.Rows = board.ScopeText switch
        {
            AgeFilters.All => all,
            AgeFilters.ExpiredOnly => all.Where(r => r.AgeLevel == AgeLevels.Expired).ToList(),
            _ => all.Where(r => r.NeedsAction).ToList()
        };

        return board;
    }

    /// <summary>按物料 + 批次 + 库位定位看板行（找不到返回 null）。纯查询。</summary>
    public static MaterialAgeRow? FindAgeRow(string materialCode, string lotNo, string location)
    {
        var code = (materialCode ?? "").Trim();
        var lot = (lotNo ?? "").Trim();
        var loc = (location ?? "").Trim();
        return BuildMaterialAgeBoard(AgeFilters.All).All.FirstOrDefault(r =>
            string.Equals(r.MaterialCode, code, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.LotNo, lot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Location, loc, StringComparison.OrdinalIgnoreCase));
    }

    // ================= 3.3 三方案 What-if =================

    /// <summary>
    /// 对某批次模拟三条处置方案（提前排产消耗 / IQC 重检延期 / 报废处置），并推荐一条。
    /// 纯查询，不改任何数据——用户先看代价与审批级别，再决定。
    /// </summary>
    public static AgeSimulation? SimulateAgePlans(string materialCode, string lotNo, string location)
    {
        var row = FindAgeRow(materialCode, lotNo, location);
        if (row is null) return null;

        var sim = new AgeSimulation { Row = row };
        sim.Options.Add(BuildConsumePlan(row));
        sim.Options.Add(BuildReinspectPlan(row));
        sim.Options.Add(BuildScrapPlan(row));

        // 推荐：可行方案里分最高的；同分按方案顺序（消耗 > 重检 > 报废）
        var best = sim.Options.Where(o => o.Feasible).OrderByDescending(o => o.Score).FirstOrDefault();
        if (best is not null)
        {
            best.Recommended = true;
            sim.RecommendedId = best.Id;
        }
        return sim;
    }

    /// <summary>方案一：提前排产消耗（按当前速度，能在效期内用掉多少）</summary>
    private static AgePlanOption BuildConsumePlan(MaterialAgeRow row)
    {
        var usableDays = Math.Max(0, row.RemainingDays);
        var canConsume = Math.Min(row.Quantity, row.DailyConsumption * usableDays);
        var ratio = row.Quantity > 1e-9 ? canConsume / row.Quantity : 0;

        var plan = new AgePlanOption
        {
            Id = AgePlans.Consume,
            Name = AgePlans.Consume,
            Amount = 0,                                  // 只是排产指令，不涉及资产处置
            RequiredLevel = AgeApproval.Team
        };

        if (row.ExpiryDate is null)
        {
            plan.Feasible = false;
            plan.BlockReason = "该批次不管理效期，无需临期处置。";
            return plan;
        }
        if (!row.HasEstimate)
        {
            plan.Feasible = false;
            plan.BlockReason = $"既无已过账领用记录（{row.ConsumptionSamples} 条）、也无在手工单需求，无法测算能消耗多少，"
                             + "请补充领用记录或改走重检/报废。";
            return plan;
        }
        if (row.IsExpired)
        {
            plan.Feasible = false;
            plan.BlockReason = $"已过期 {-row.RemainingDays} 天，不可再投入生产，只能重检或报废。";
            return plan;
        }
        if (ratio < 0.2)
        {
            plan.Feasible = false;
            plan.BlockReason = $"按当前速度到效期只能消耗 {ratio:P0}（{canConsume:0.##} {row.Unit}），"
                             + "剩余部分仍会报废，请改走重检延期或直接报废。";
            return plan;
        }

        plan.Feasible = true;
        plan.EffectText = $"可消化 {canConsume:0.##} {row.Unit}（{ratio:P0}），到期仍剩 {Math.Max(0, row.Quantity - canConsume):0.##} {row.Unit}";
        plan.Score = Math.Round(30 + ratio * 70, 1);
        plan.Pros.Add($"保住 {canConsume:0.##} {row.Unit} 的物料价值（约 {canConsume * row.UnitPrice:0.##} 元）");
        plan.Pros.Add("不需要检验、不需要审批资产处置，动作最快");
        if (ratio < 1)
            plan.Cons.Add($"仍有 {Math.Max(0, row.Quantity - canConsume):0.##} {row.Unit} 到期报废，需同时安排报废或重检");
        plan.Cons.Add("需插单排产（会挤压其它工单产能），本原型只登记优先消耗指令");
        return plan;
    }

    /// <summary>方案二：IQC 重检延期（已过期走既有重检；临期只登记待检）</summary>
    private static AgePlanOption BuildReinspectPlan(MaterialAgeRow row)
    {
        var plan = new AgePlanOption
        {
            Id = AgePlans.Reinspect,
            Name = AgePlans.Reinspect,
            Amount = row.Amount,
            RequiredLevel = AgeApproval.LevelOf(row.Amount)
        };

        if (row.ExpiryDate is null)
        {
            plan.Feasible = false;
            plan.BlockReason = "该批次不管理效期，不需要重检延期。";
            return plan;
        }
        if (row.IsFallback)
        {
            plan.Feasible = false;
            plan.BlockReason = $"已过期 {-row.RemainingDays} 天，超过兜底期限 {AgeRules.FallbackOverdueDays} 天，"
                             + "继续延期风险不可控，只能报废。";
            return plan;
        }

        var times = GetReinspectCount(row.MaterialCode, row.LotNo);
        var newExpiry = DateTime.Today.AddDays(AgeRules.ExtendDays);

        plan.Feasible = true;
        plan.EffectText = row.IsExpired
            ? $"效期 {row.ExpiryText}（已过期 {-row.RemainingDays} 天）→ 重检合格后延长至 {newExpiry:yyyy-MM-dd}（+{AgeRules.ExtendDays} 天）"
            : $"当前效期 {row.ExpiryText}（剩 {row.RemainingDays} 天）→ 提前安排 IQC，合格后延长至 {newExpiry:yyyy-MM-dd}（+{AgeRules.ExtendDays} 天）";
        plan.Score = Math.Round((row.IsExpired ? 70.0 : 85.0) - times * 10.0, 1);
        plan.Pros.Add("保留整批物料，避免报废损失（约 " + row.Amount.ToString("0.##") + " 元）");
        plan.Pros.Add("有 IQC 检验结论背书，质量风险可追溯");
        plan.Cons.Add("重检需时间与费用，且不合格只能报废");
        if (times > 0) plan.Cons.Add($"该批次已重检 {times} 次，不建议反复续期（评分已扣 {times * 10} 分）");
        return plan;
    }

    /// <summary>方案三：报废处置（超出兜底期限时升级为「兜底报废」）</summary>
    private static AgePlanOption BuildScrapPlan(MaterialAgeRow row)
    {
        var isFallback = row.IsFallback;
        var plan = new AgePlanOption
        {
            Id = AgePlans.Scrap,
            Name = isFallback ? AgePlans.Fallback : AgePlans.Scrap,
            Amount = row.Amount,
            RequiredLevel = AgeApproval.LevelOf(row.Amount)
        };

        if (row.ExpiryDate is null)
        {
            plan.Feasible = false;
            plan.BlockReason = "该批次不管理效期，无报废依据。";
            return plan;
        }

        plan.Feasible = true;
        plan.EffectText = isFallback
            ? $"已过期 {-row.RemainingDays} 天（超兜底期限 {AgeRules.FallbackOverdueDays} 天），整批 {row.QtyText} 报废，损失约 {row.Amount:0.##} 元"
            : $"整批 {row.QtyText} 报废，损失约 {row.Amount:0.##} 元（不可逆）";
        plan.Score = isFallback ? 100 : Math.Round(row.IsExpired ? 45 : 20 - Math.Min(15, row.Amount / 10000), 1);
        plan.Pros.Add("一次清账，账实立即一致，不留质量隐患");
        plan.Pros.Add("动作确定、可审计（废弃台账留原因/操作人/时间）");
        if (!isFallback) plan.Cons.Add("物料价值直接损失，不可恢复");
        if (!isFallback && !row.IsExpired) plan.Cons.Add("尚未过期就报废，建议先评估重检延期");
        return plan;
    }

    // ================= 决策执行 =================

    /// <summary>执行处置方案需要的审批级别（界面提示用）。纯查询。</summary>
    public static string RequiredApprovalLevel(string materialCode, string lotNo, string location, string planId)
    {
        var sim = SimulateAgePlans(materialCode, lotNo, location);
        var opt = sim?.Options.FirstOrDefault(o => o.Id == planId);
        return opt?.RequiredLevel ?? AgeApproval.Team;
    }

    /// <summary>
    /// 执行一批次的处置决策。
    /// 校验顺序：批次存在 → 方案可行 → 审批级别够 → 操作人非空 → 动作执行 → 写决策台账。
    /// **失败一律零副作用**（写台账放在动作成功之后；动作本身由既有方法保证先台账后改主表）。
    /// </summary>
    /// <param name="approvalRole">审批角色（班组长 / 仓储主管 / 生产经理）</param>
    /// <param name="operatorName">操作人</param>
    public static (bool Ok, string Message, string DecisionNo) ExecuteAgeDecision(
        string materialCode, string lotNo, string location, string planId,
        string approvalRole, string operatorName, string? remark = null, DateTime? at = null)
    {
        var sim = SimulateAgePlans(materialCode, lotNo, location);
        if (sim?.Row is null) return (false, $"未找到库存批次：{materialCode} / {lotNo} / {location}。", "");
        var row = sim.Row;

        var opt = sim.Options.FirstOrDefault(o => string.Equals(o.Id, planId, StringComparison.Ordinal));
        if (opt is null) return (false, $"未知处置方案：{planId}。", "");
        if (!opt.Feasible) return (false, $"「{opt.Name}」当前不可执行：{opt.BlockReason}", "");

        var op = (operatorName ?? "").Trim();
        if (op.Length == 0) return (false, "请填写操作人。", "");

        var role = (approvalRole ?? "").Trim();
        if (opt.Amount > 1e-9 && !AgeApproval.CanApprove(role, opt.Amount))
            return (false,
                $"审批级别不足：{AgeApproval.Explain(opt.Amount)}。当前选择「{(role.Length == 0 ? "未选" : role)}」，请提级后重试。",
                "");

        var now = at ?? DateTime.Now;
        var note = (remark ?? "").Trim();
        var status = AgeDecisionStatus.Registered;
        var refNo = "";

        switch (opt.Id)
        {
            case AgePlans.Consume:
            {
                // 优先消耗标记：目标日 = 效期日（到期前必须用完）
                var mark = new MaterialPriorityMark
                {
                    MarkNo = $"PC-{now:yyyyMMdd}-{++_priorityMarkSeq:000}",
                    MaterialCode = row.MaterialCode,
                    MaterialName = row.MaterialName,
                    LotNo = row.LotNo,
                    Location = row.Location,
                    Quantity = row.Quantity,
                    ExpiryDate = row.ExpiryDate,
                    TargetDate = row.ExpiryDate ?? now.Date.AddDays(7),
                    Operator = op,
                    CreatedAt = now,
                    Remark = note.Length > 0 ? note : "提前排产消耗：请在目标日前优先领用本批次"
                };
                MaterialPriorityMarks.Add(mark);
                refNo = mark.MarkNo;
                status = AgeDecisionStatus.Registered;
                break;
            }

            case AgePlans.Reinspect:
            {
                if (row.IsExpired)
                {
                    // 已过期：走既有重新进料检（真动作：写重检台账 + 延长效期）
                    var (ok, msg, no) = ReinspectMaterial(
                        row.MaterialCode, row.LotNo, row.Location,
                        DateTime.Today.AddDays(AgeRules.ExtendDays), op,
                        ReinspectResults.Pass, note, now);
                    if (!ok) return (false, msg, "");
                    refNo = no;
                    status = AgeDecisionStatus.Extended;
                }
                else
                {
                    // 临期未过期：只登记待检（不改效期——效期只能由检验结论延长，不能点一下就以为什么都解决了）
                    status = AgeDecisionStatus.PendingIqc;
                }
                break;
            }

            default:   // 报废 / 兜底报废
            {
                var reason = row.IsExpired ? ScrapReasons.Expired : ScrapReasons.Other;
                var scrapRemark = row.IsExpired
                    ? (note.Length > 0 ? note : "临期三方案决策：报废处置")
                    : (note.Length > 0 ? note : "临期主动报废（未过期，按处置决策执行）");
                var (ok, msg, no) = ScrapMaterial(
                    row.MaterialCode, row.LotNo, row.Location,
                    reason, op, scrapRemark, now);
                if (!ok) return (false, msg, "");
                refNo = no;
                status = AgeDecisionStatus.Scrapped;
                break;
            }
        }

        // 动作成功后才写决策台账（对手写错的方案名也一并纠正为实际方案名）
        var dec = new AgeDecision
        {
            DecisionNo = $"AD-{now:yyyyMMdd}-{++_ageDecisionSeq:000}",
            DecidedAt = now,
            MaterialCode = row.MaterialCode,
            MaterialName = row.MaterialName,
            LotNo = row.LotNo,
            Location = row.Location,
            Plan = opt.Name,
            ApprovalLevel = opt.Amount > 1e-9 ? AgeApproval.LevelOf(opt.Amount) : AgeApproval.Team,
            Approver = op,
            Amount = opt.Amount,
            Status = status,
            RefNo = refNo,
            Remark = note
        };
        AgeDecisions.Add(dec);

        var head = opt.Id switch
        {
            AgePlans.Consume => $"已登记优先消耗指令（{refNo}），目标日 {row.ExpiryDate:yyyy-MM-dd} 前用完；请到排产/领料环节优先消耗本批次。",
            AgePlans.Reinspect => row.IsExpired
                ? $"重检已完成（{refNo}），效期延长至 {DateTime.Today.AddDays(AgeRules.ExtendDays):yyyy-MM-dd}。"
                : $"已登记 IQC 重检申请（待检），检验合格后效期可延长 {AgeRules.ExtendDays} 天。",
            _ => $"批次已报废（{refNo}），数量归零并退出可用库存与 FIFO 队列。"
        };

        return (true, $"{row.MaterialName} / {row.LotNo}：{head}\n决策留痕 {dec.DecisionNo}"
                    + (opt.Amount > 1e-9 ? $"（金额 {opt.Amount:0.##} 元，审批级别 {dec.ApprovalLevel}）" : "（无需资产审批）"),
                    dec.DecisionNo);
    }

    // ================= 台账查询 =================

    /// <summary>处置决策留痕（按时间倒序）。纯查询。</summary>
    public static List<AgeDecision> GetAgeDecisions(string? materialCode = null, int limit = 200)
    {
        var code = (materialCode ?? "").Trim();
        return AgeDecisions
            .Where(d => code.Length == 0 || string.Equals(d.MaterialCode, code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.DecidedAt)
            .ThenByDescending(d => d.DecisionNo)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    /// <summary>某物料的优先消耗标记（按时间倒序）。纯查询。</summary>
    public static List<MaterialPriorityMark> GetPriorityMarks(string? materialCode = null)
    {
        var code = (materialCode ?? "").Trim();
        return MaterialPriorityMarks
            .Where(m => code.Length == 0 || string.Equals(m.MaterialCode, code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
    }

    /// <summary>处置看板统计（卡片用）。纯查询。</summary>
    public static (int Total, int Near, int Expired, int Fallback, double RiskAmount, double WasteQty) GetAgeSummary()
    {
        var b = BuildMaterialAgeBoard(AgeFilters.All);
        return (b.BatchCount, b.NearCount, b.ExpiredCount, b.FallbackCount, b.RiskAmount, b.WasteQty);
    }
}
