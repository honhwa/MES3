using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 替代料 + 安全库存补货策略 —— 数据层（技术方案第 4 章追加项）
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>物料替代关系台账（唯一键 = MainCode + SubCode）</summary>
    public static List<MaterialSubstitute> MaterialSubstitutes { get; } = new();

    private static bool _substituteReady;

    // ================= 消耗速度（统一口径，临期看板与补货共用） =================

    /// <summary>
    /// 物料日均消耗（统一口径，避免两个页面各算一套）：
    ///   ① 近 90 天已过账现场领用实测（最接近真实消耗）——样本 ≥ 2 条时采用
    ///   ② 未完工工单的 BOM 需求 ÷ 需求视界（30 天）——样本不足时用它
    ///   ③ 两者都没有 → 0，来源标「不可估」
    /// 纯查询。
    /// </summary>
    public static double GetMaterialDailyConsumption(string materialCode, out string source, out int samples)
    {
        var code = (materialCode ?? "").Trim();
        var (consumed, count, days) = GetMaterialConsumption(code, AgeRules.ConsumeWindowDays);
        samples = count;

        if (count >= 2 && days > 0)
        {
            source = AgeSources.IssueActual;
            return consumed / days;
        }

        var horizon = Math.Max(1, MesDatabaseInsightRules.DemandHorizonDays);
        var demand = GetMaterialDemandByOpenWorkOrders(code);
        if (demand > 1e-9)
        {
            source = AgeSources.WorkOrderDemand;
            return demand / horizon;
        }

        source = AgeSources.None;
        return 0;
    }

    // ================= 替代料 =================

    /// <summary>
    /// 替代关系种子：4 组，覆盖三种演示情形——
    ///   · 已认可且有货（铝箔 / 隔膜）→ 能顶一部分缺口
    ///   · 已认可但库存少（只够顶一部分）→ 演示"部分替代 + 剩余仍缺"
    ///   · 待客户批准（电解液 PC 基）→ 有关系但**不能用**，这是最容易被系统放过的坑
    /// </summary>
    public static void EnsureSubstituteSeed()
    {
        if (_substituteReady) return;

        void Add(string mainCode, string subCode, double ratio, int priority,
                 string approval, string condition, string remark)
        {
            var main = FindWmsMaterial(mainCode);
            var sub = FindWmsMaterial(subCode);
            if (main is null || sub is null) return;
            if (MaterialSubstitutes.Any(x =>
                    string.Equals(x.MainCode, mainCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.SubCode, subCode, StringComparison.OrdinalIgnoreCase))) return;

            MaterialSubstitutes.Add(new MaterialSubstitute
            {
                Id = $"SUB-{mainCode}-{subCode}",
                MainCode = main.Code,
                MainName = main.MaterialName,
                SubCode = sub.Code,
                SubName = sub.MaterialName,
                Ratio = ratio,
                Priority = priority,
                Approval = approval,
                Condition = condition,
                Remark = remark
            });
        }

        Add("M-COL-AL-04", "M-COL-AL-04B", 1.00, 1, SubstituteApproval.Approved,
            "厚度公差 ±1 μm 内可用；需在工艺卡标注实际厚度", "同材质薄规格，客户已认可");
        Add("M-SEP-PP-03", "M-SEP-PE-03", 1.05, 1, SubstituteApproval.Approved,
            "PE 隔膜孔隙率更高，单位用量 +5%；首件需加做耐压测试", "材质不同（PP→PE），工艺已确认");
        // 铝壳是最常缺的料（1 只/只），替代关系放这里，默认 6000 只的场景就能演示"部分替代 + 剩余仍缺"
        Add("M-CAN-ALU-05", "M-CAN-ALU-05B", 1.00, 1, SubstituteApproval.Approved,
            "高度公差 ±0.2 mm 可用；外观差异需客户确认", "同规格加高型，客户已认可");
        Add("M-ELE-ACN-02", "M-ELE-ACN-02B", 1.00, 1, SubstituteApproval.Pending,
            "溶剂体系由 ACN 改 PC，需客户书面同意后方可投产", "**未获客户认可，只展示不顶缺口**");
        _substituteReady = true;
    }

    /// <summary>替代关系查询（可按主料过滤）。纯查询。</summary>
    public static List<MaterialSubstitute> GetSubstitutes(string? mainCode = null)
    {
        EnsureSubstituteSeed();
        var code = (mainCode ?? "").Trim();
        return MaterialSubstitutes
            .Where(s => code.Length == 0 || string.Equals(s.MainCode, code, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.MainCode, StringComparer.Ordinal)
            .ThenBy(s => s.Priority)
            .ToList();
    }

    /// <summary>某主料的首选可用替代（已认可 + 有库存）。纯查询。</summary>
    public static MaterialSubstitute? GetUsableSubstitute(string mainCode)
    {
        foreach (var s in GetSubstitutes(mainCode).Where(x => x.Usable).OrderBy(x => x.Priority))
            if (GetMaterialAvailableQty(s.SubCode) > 1e-9) return s;
        return null;
    }

    /// <summary>
    /// 带替代料的齐套校验：先按主料算缺口，再用**已认可且有余量**的替代料按优先级顶缺口。
    /// 待客户批准 / 客户否决的关系只展示、不参与计算（硬门槛）。
    /// 纯查询，不改任何单据。
    /// </summary>
    public static SubstituteCheckResult CheckBomAvailabilityWithSubstitutes(string? productModel, int qty)
    {
        EnsureSubstituteSeed();
        var result = new SubstituteCheckResult
        {
            ProductModel = (productModel ?? "").Trim(),
            Qty = Math.Max(0, qty)
        };
        if (result.ProductModel.Length == 0 || result.Qty <= 0) return result;

        var check = CheckBomAvailability(result.ProductModel, result.Qty, result.Qty);
        if (check is null || check.NoBom) return result;
        result.InputQty = check.PlanQty;

        foreach (var r in check.Rows)
        {
            var row = new SubstituteRow
            {
                MaterialCode = r.MaterialCode,
                MaterialName = r.MaterialName,
                Unit = r.Unit,
                Requirement = r.Requirement,
                Available = r.Available,
                Shortage = Math.Max(0, r.Shortage),
                RemainShortage = Math.Max(0, r.Shortage)
            };

            if (r.Shortage <= 1e-9)
            {
                row.Note = "主料可用量满足需求。";
                result.Rows.Add(row);
                continue;
            }

            // 按优先级找可用替代；不可用的要写明原因（这才是给计划员的真信息）
            var candidates = GetSubstitutes(r.MaterialCode).OrderBy(s => s.Priority).ToList();
            if (candidates.Count == 0)
            {
                row.Note = "无登记替代料，只能采购。";
                result.Rows.Add(row);
                continue;
            }

            MaterialSubstitute? used = null;
            double applied = 0;
            foreach (var s in candidates)
            {
                var subAvail = GetMaterialAvailableQty(s.SubCode);
                if (!s.Usable)
                {
                    row.Note = s.Approval == SubstituteApproval.Pending
                        ? $"{s.SubName} 有关系但{s.Approval}，不可用（需客户书面同意）。"
                        : $"{s.SubName} {s.Approval}，不可用。";
                    continue;
                }
                if (subAvail <= 1e-9)
                {
                    row.Note = $"{s.SubName} 已认可但库存为 0，顶不了缺口。";
                    continue;
                }

                var ratio = s.Ratio <= 0 ? 1 : s.Ratio;
                applied = Math.Min(row.RemainShortage, subAvail / ratio);
                used = s;
                break;
            }

            if (used is not null && applied > 1e-9)
            {
                row.SubCode = used.SubCode;
                row.SubName = used.SubName;
                row.Ratio = used.Ratio;
                row.SubAvailable = GetMaterialAvailableQty(used.SubCode);
                row.Approval = used.Approval;
                row.AppliedQty = Math.Round(applied, 2);
                row.RemainShortage = Math.Max(0, Math.Round(row.Shortage - applied, 2));
                row.Note = $"用 {used.SubName} 顶 {row.AppliedQty:0.##} {row.Unit}"
                         + (used.Ratio != 1 ? $"（用量系数 1:{used.Ratio:0.###}）" : "")
                         + (row.RemainShortage > 1e-9 ? $"，仍缺 {row.RemainShortage:0.##}" : "，缺口补齐");
            }
            else if (row.Note.Length == 0)
            {
                row.Note = "替代料均不可用（无库存或未认可）。";
            }

            result.Rows.Add(row);
        }

        return result;
    }

    // ================= 安全库存补货 =================

    /// <summary>
    /// 安全库存补货看板：逐物料算 补货点 ROP / 净库存 / 建议补货量 / 建议下单日。
    /// 纯查询，不改任何单据。
    /// </summary>
    public static ReplenishmentBoard BuildReplenishmentBoard()
    {
        EnsureProcureParamSeed();
        var board = new ReplenishmentBoard();
        var today = DateTime.Today;

        foreach (var mat in WmsMaterials.Where(m => m.Category != WmsCategories.Finished))
        {
            var daily = GetMaterialDailyConsumption(mat.Code, out var source, out _);
            var pp = GetProcureParam(mat.Code);
            var available = GetMaterialAvailableQty(mat.Code);
            var inTransit = GetMaterialInTransitQty(mat.Code);
            var draft = Math.Max(0, GetMaterialPendingQty(mat.Code) - inTransit);

            var row = new ReplenishmentRow
            {
                MaterialCode = mat.Code,
                MaterialName = mat.MaterialName,
                Spec = mat.Spec,
                Unit = mat.Unit,
                Category = mat.Category,
                DailyConsumption = Math.Round(daily, 4),
                ConsumptionSource = source,
                LeadDays = pp.TotalLeadDays,
                SafetyStock = mat.SafetyStock,
                Available = available,
                InTransit = inTransit,
                DraftQty = draft,
                Mpq = pp.MinOrderQty,
                MaxPurchaseQty = mat.MaxPurchaseQty
            };

            row.Rop = Math.Round(daily * pp.TotalLeadDays + mat.SafetyStock, 2);

            // 状态判定：优先看是否已低于安全库存（最急）
            var netStock = row.NetStock;
            if (netStock < mat.SafetyStock - 1e-9)
                row.Status = ReplenishStatus.Urgent;
            else if (daily > 1e-9 && netStock < row.Rop - 1e-9)
                row.Status = ReplenishStatus.Need;
            else if (daily <= 1e-9)
                row.Status = ReplenishStatus.Idle;
            else
                row.Status = ReplenishStatus.Ok;

            // 建议补货量：补到补货点（ROP），至少补齐安全库存缺口
            var need = Math.Max(0, Math.Max(row.Rop - netStock, mat.SafetyStock - netStock));
            if (need > 1e-9)
            {
                var qty = need;
                if (row.Mpq > 1e-9)
                {
                    var rounded = Math.Ceiling(qty / row.Mpq - 1e-9) * row.Mpq;
                    row.MpqRounded = rounded > qty + 1e-9;
                    qty = rounded;
                }
                if (mat.MaxPurchaseQty > 1e-9 && qty > mat.MaxPurchaseQty)
                {
                    qty = mat.MaxPurchaseQty;   // 不超单次限额（超出部分下次再补）
                }
                row.SuggestQty = Math.Round(qty, 2);
            }

            // 建议下单日：已低于补货点 → 今天；否则按净库存跌破 ROP 的天数推
            if (row.NeedsOrder)
            {
                row.SuggestOrderDate = today;
            }
            else if (daily > 1e-9)
            {
                var daysToRop = (netStock - row.Rop) / daily;
                row.SuggestOrderDate = today.AddDays(Math.Max(0, (int)Math.Floor(daysToRop)));
            }

            board.Rows.Add(row);
        }

        // 排序：紧急 → 需补货 → 正常 → 无依据；同级按缺口大小
        board.Rows = board.Rows
            .OrderBy(r => r.Status == ReplenishStatus.Urgent ? 0
                        : r.Status == ReplenishStatus.Need ? 1
                        : r.Status == ReplenishStatus.Ok ? 2 : 3)
            .ThenByDescending(r => r.SuggestQty)
            .ThenBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ToList();
        return board;
    }

    /// <summary>
    /// 按补货建议生成采购申请（无工单来源的补库存请购）。
    /// 只处理「紧急 / 需补货」且**没有未完成申请单**的物料——已有申请就等它走完，
    /// 否则会重复下单（草稿虽然不算在途，但已经有人在管）。
    /// </summary>
    public static (bool Ok, string Message, string ReqNo) CreateReplenishmentRequest(
        string applicant, string? remark = null)
    {
        var board = BuildReplenishmentBoard();
        var targets = board.Rows
            .Where(r => r.NeedsOrder && r.SuggestQty > 1e-9)
            .Where(r => GetMaterialPendingQty(r.MaterialCode) <= 1e-9)
            .ToList();

        if (targets.Count == 0)
        {
            var pending = board.Rows.Count(r => r.NeedsOrder && GetMaterialPendingQty(r.MaterialCode) > 1e-9);
            return (false,
                pending > 0
                    ? $"需要补货的 {pending} 种物料都已有未完成采购申请，等它走完再评估，避免重复下单。"
                    : "当前没有需要补货的物料（净库存均高于补货点）。",
                "");
        }

        var lines = targets.Select(r => new PurchaseRequestLine
        {
            MaterialCode = r.MaterialCode,
            MaterialName = r.MaterialName,
            Spec = r.Spec,
            Unit = r.Unit,
            MaterialType = r.Category,
            FormulaText = $"安全库存补货：日均 {r.DailyConsumption:0.##} × 提前期 {r.LeadDays} 天 + 安全 {r.SafetyStock:0.##} = 补货点 {r.Rop:0.##}；"
                        + $"净库存 {r.NetStock:0.##}（可用 {r.Available:0.##} + 在途 {r.InTransit:0.##} + 草稿 {r.DraftQty:0.##}）",
            // 需求量基准取 max(ROP, 建议量)：MPQ 取整会把建议量抬高到 ROP 之上，
            // 若仍按 ROP 作需求基准，补货申请会被"超量请购"校验拦住——那是供应商起订量约束，不是买多了
            Requirement = Math.Max(Math.Round(r.Rop, 2), r.SuggestQty),
            Available = r.Available,
            FrozenQty = 0,
            RequestQty = r.SuggestQty,
            Supplier = GetPreferredSupplier(r.MaterialCode),
            Remark = r.VerdictText
        }).ToList();

        var note = (remark ?? "").Trim();
        if (note.Length > 0) note = "；" + note;
        var (ok, msg, reqNo) = CreateRequest(
            woNo: "",
            applicant: string.IsNullOrWhiteSpace(applicant) ? "计划员" : applicant.Trim(),
            lines: lines,
            remark: $"安全库存补货自动生成（{targets.Count} 项物料，口径：补货点 ROP = 日均消耗 × 提前期 + 安全库存）" + note);

        if (!ok) return (false, msg, "");
        return (true,
            $"已生成补货申请 {reqNo}：{targets.Count} 项物料，合计建议补货 "
            + $"{targets.Sum(t => t.SuggestQty):0.##}（各单位混合，明细以行为准）。"
            + "\n提醒：申请单为草稿，请到「资材采购申请」核对后提交审批。",
            reqNo);
    }
}
