using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 资材采购申请 —— 仓储层
//
// 这一层要回答的问题是：工单缺料之后，**接下来发生什么**。
// 具体动作：按工单把 BOM 缺口转成申请明细 → 提交 → 审批 → 下单 → 关闭。
//
// 三条设计取舍：
//   ① **一张申请单只对应一张工单**，不跨工单合并——跨单合并后
//      "这张工单还缺什么"就追不回来了，而那正是排产要回答的问题。
//   ② **需求量 / 可用量 / 缺口三项全部保留**，不只留"申请量"——
//      审批人要看的就是"凭什么是这个数"，没有这三列他没法判断。
//   ③ **只带出"短缺"行**：紧张项（够用但无缓冲）不必立刻采购，
//      由计划员判断是否顺手备一点，避免申请单被无谓的备料淹没。
//
// 所有流转方法都是**先校验后写入**，失败路径集合零变化。
// ============================================================================

public static partial class MesDatabase
{
    // ================= 集合与单号 =================

    /// <summary>资材采购申请单（请购单）</summary>
    public static List<PurchaseRequest> PurchaseRequests { get; } = new();

    /// <summary>采购申请单号自增序号（与 WMS/ERP 报工序号同样处理：跨天自然延续）</summary>
    private static int _prSeq;

    /// <summary>生成下一个采购申请单号：PR-{createTime:yyyyMMdd}-{序号:000}</summary>
    private static string NextPurchaseRequestNo(DateTime createTime)
    {
        _prSeq++;
        return $"PR-{createTime:yyyyMMdd}-{_prSeq:000}";
    }

    // ================= 查询 =================

    /// <summary>按单号查申请单（无则 null）。纯查询。</summary>
    public static PurchaseRequest? FindPurchaseRequest(string reqNo)
    {
        if (string.IsNullOrWhiteSpace(reqNo)) return null;
        return PurchaseRequests.FirstOrDefault(r =>
            string.Equals(r.ReqNo, reqNo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>某工单关联的采购申请单（按创建时间倒序）。纯查询。</summary>
    public static List<PurchaseRequest> GetRequestsByWorkOrder(string woNo)
    {
        if (string.IsNullOrWhiteSpace(woNo)) return new List<PurchaseRequest>();
        return PurchaseRequests
            .Where(r => string.Equals(r.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CreateTime)
            .ToList();
    }

    /// <summary>申请单汇总（供卡片展示：各状态张数 + 明细行数）。纯查询。</summary>
    public static PurchaseRequestSummary GetPurchaseRequestSummary()
    {
        var s = new PurchaseRequestSummary();
        foreach (var r in PurchaseRequests)
        {
            s.Total++;
            s.LineCount += r.Lines.Count;
            switch (r.Status)
            {
                case PurchaseRequestStatus.Draft: s.Draft++; break;
                case PurchaseRequestStatus.Submitted: s.Submitted++; break;
                case PurchaseRequestStatus.Approved: s.Approved++; break;
                case PurchaseRequestStatus.Rejected: s.Rejected++; break;
                case PurchaseRequestStatus.Ordered: s.Ordered++; break;
                case PurchaseRequestStatus.Closed: s.Closed++; break;
            }
            if (r.Status == PurchaseRequestStatus.Submitted) s.PendingApprovalLines += r.Lines.Count;
        }
        return s;
    }

    /// <summary>
    /// 某工单目前"还缺多少"：已提交/已批准/已下单的申请视为在途，不重复申请。
    /// 口径：只有**非在途**的缺口才需要新申请——已在采购流程里的料，
    /// 再报一次就是重复采购，这是请购最容易出的错。
    /// </summary>
    public static List<MaterialShortageRow> GetOutstandingShortage(string woNo)
    {
        var check = CheckWorkOrderShortage(woNo, out _);
        if (check is null) return new List<MaterialShortageRow>();

        // 在途数量：同一物料已在"待审批/已批准/已下单"申请单里的申请量合计
        var inTransit = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in GetRequestsByWorkOrder(woNo))
        {
            if (!PurchaseRequestStatus.IsEffective(req.Status)) continue;
            foreach (var l in req.Lines)
                inTransit[l.MaterialCode] = inTransit.GetValueOrDefault(l.MaterialCode) + l.RequestQty;
        }

        var rows = new List<MaterialShortageRow>();
        foreach (var r in check.Rows)
        {
            var shortBy = r.Shortage - inTransit.GetValueOrDefault(r.MaterialCode);
            if (shortBy <= 1e-9) continue;
            rows.Add(new MaterialShortageRow
            {
                MaterialCode = r.MaterialCode,
                MaterialName = r.MaterialName,
                Spec = r.Spec,
                Unit = r.Unit,
                MaterialType = r.MaterialType,
                QtyPerUnit = r.QtyPerUnit,
                LossRate = r.LossRate,
                Requirement = r.Requirement,
                Available = r.Available,
                FrozenQty = r.FrozenQty,
                LotHint = r.LotHint,
                Status = ShortageStatus.Short,
                Advice = $"在手申请已在途 {inTransit.GetValueOrDefault(r.MaterialCode):0.##}，仍需采购 {shortBy:0.##} {r.Unit}"
            });
        }
        return rows;
    }

    /// <summary>
    /// 按工单做齐套校验（把工单的型号与计划数喂给统一的齐套口径）。
    /// <paramref name="woNo"/> 不存在时返回 null。
    /// </summary>
    public static MaterialCheckResult? CheckWorkOrderShortage(string woNo, out WorkOrder? wo)
    {
        wo = string.IsNullOrWhiteSpace(woNo)
            ? null
            : WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase));
        if (wo is null) return null;
        return CheckBomAvailability(wo.ProductModel, wo.PlanQty, wo.PlanQty);
    }

    /// <summary>该物料的推荐供应商（取最近一次入库批次的供应商）。无记录返回空串。</summary>
    public static string GetPreferredSupplier(string materialCode)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return "";
        var lot = Materials
            .Where(m => string.Equals(m.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(m.Supplier))
            .OrderByDescending(m => m.ReceivedDate)
            .FirstOrDefault();
        return lot?.Supplier ?? "";
    }

    /// <summary>
    /// 当前有物料缺口、且尚未走请购流程的工单（供页面下拉选择来源工单）。
    /// "尚未走请购流程"= 没有**未关闭**的采购申请（草稿算——还没送审等于没开始）；
    /// 已关闭的不算已处置（货没到、缺口还在），工单会重新出现在这里等再次请购。
    /// </summary>
    public static List<WorkOrder> GetWorkOrdersWithShortage()
    {
        var result = new List<WorkOrder>();
        foreach (var wo in WorkOrders.Where(IsOpenWorkOrder))
        {
            if (GetRequestsByWorkOrder(wo.WoNo).Any(r => !PurchaseRequestStatus.IsFinal(r.Status)))
                continue;   // 还在请购流程里，不重复列为候选
            if (GetOutstandingShortage(wo.WoNo).Count > 0) result.Add(wo);
        }
        return result;
    }

    // ================= 生成 =================

    /// <summary>
    /// 按工单自动生成采购申请草稿：把 BOM 缺口逐项带出，含需求量 / 可用量 / 缺口。
    /// 校验：工单存在、确实有短缺项、该工单尚无未关闭的申请单。
    /// </summary>
    /// <param name="includeTight">是否一并带出"紧张"项（够用但无缓冲，可选备料）</param>
    public static (bool Ok, string Message, string ReqNo) BuildRequestFromWorkOrder(
        string woNo, string applicant, string remark = "", bool includeTight = false)
    {
        var check = CheckWorkOrderShortage(woNo, out var wo);
        if (wo is null) return (false, $"工单 {woNo} 不存在。", "");
        if (check is null || check.NoBom)
            return (false, $"工单 {wo.ProductModel} 的 BOM 缺失，无法生成采购申请。", "");

        // 已有未关闭的申请单就不重复生成——同一工单重复请购是采购环节最容易出的错
        var existing = GetRequestsByWorkOrder(wo.WoNo).FirstOrDefault(r => !PurchaseRequestStatus.IsFinal(r.Status));
        if (existing is not null)
            return (false, $"工单 {wo.WoNo} 已有未关闭的采购申请 {existing.ReqNo}（{existing.StatusText}），不重复生成。", "");

        var outstanding = GetOutstandingShortage(wo.WoNo);
        var targets = outstanding
            .Concat(includeTight ? check.Rows.Where(r => r.Status == ShortageStatus.Tight) : Enumerable.Empty<MaterialShortageRow>())
            .DistinctBy(r => r.MaterialCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
            return (false, $"工单 {wo.WoNo} 物料已齐套（或缺口已在在途申请中），无需采购申请。", "");

        var req = BuildRequestCore(wo, targets, applicant, remark, autoCreated: true);
        PurchaseRequests.Add(req);
        // 同一事务边界：生成申请单时顺带做一次资源占用校验，冲突与建议写进备注
        AppendOccupancyNoteOnCreate(req, wo);

        return (true, $"已生成采购申请 {req.ReqNo}：{targets.Count} 项物料，合计 {req.TotalQtyText}。来源工单 {wo.WoNo}。", req.ReqNo);
    }

    /// <summary>
    /// 手工新建采购申请：来源可以是一张工单（带出缺口），也可以只填物料（补库存用）。
    /// </summary>
    public static (bool Ok, string Message, string ReqNo) CreateRequest(
        string woNo, string applicant, List<PurchaseRequestLine> lines, string remark = "")
    {
        var applicantName = string.IsNullOrWhiteSpace(applicant) ? "计划员" : applicant.Trim();
        if (lines is null || lines.Count == 0)
            return (false, "请至少添加一行采购明细。", "");

        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l.MaterialCode)) return (false, "明细里有物料编码为空的行，请补齐。", "");
            if (l.RequestQty <= 1e-9) return (false, $"「{l.MaterialName}」的申请量必须大于 0。", "");
        }

        WorkOrder? wo = null;
        if (!string.IsNullOrWhiteSpace(woNo))
        {
            wo = WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase));
            if (wo is null) return (false, $"工单 {woNo} 不存在。", "");
        }

        var req = new PurchaseRequest
        {
            ReqNo = NextPurchaseRequestNo(DateTime.Now),
            WoNo = wo?.WoNo ?? "",
            ErpOrderNo = wo?.ErpOrderNo ?? "",
            ProductModel = wo?.ProductModel ?? "",
            Status = PurchaseRequestStatus.Draft,
            Applicant = applicantName,
            CreateTime = DateTime.Now,
            Remark = remark,
            AutoCreated = false
        };
        foreach (var l in lines) req.Lines.Add(CopyLine(l));

        PurchaseRequests.Add(req);
        return (true, $"已创建采购申请 {req.ReqNo}（草稿），共 {req.Lines.Count} 行。", req.ReqNo);
    }

    private static PurchaseRequest BuildRequestCore(
        WorkOrder wo, List<MaterialShortageRow> targets, string applicant, string remark, bool autoCreated)
    {
        var req = new PurchaseRequest
        {
            ReqNo = NextPurchaseRequestNo(DateTime.Now),
            WoNo = wo.WoNo,
            ErpOrderNo = wo.ErpOrderNo,
            ProductModel = wo.ProductModel,
            Status = PurchaseRequestStatus.Draft,
            Applicant = string.IsNullOrWhiteSpace(applicant) ? "计划员" : applicant.Trim(),
            CreateTime = DateTime.Now,
            Remark = remark,
            AutoCreated = autoCreated
        };

        foreach (var t in targets)
        {
            // 缺口已在 outstanding 里扣减了在途量；紧张项没有在途概念，直接按富余量建议
            var suggest = t.Status == ShortageStatus.Short
                ? Math.Max(0, t.Shortage)
                : Math.Max(0, t.Requirement * 0.1);   // 紧张项：按需求的 10% 建议备一点

            req.Lines.Add(new PurchaseRequestLine
            {
                MaterialCode = t.MaterialCode,
                MaterialName = t.MaterialName,
                Spec = t.Spec,
                Unit = t.Unit,
                MaterialType = t.MaterialType,
                FormulaText = t.FormulaText,
                Requirement = t.Requirement,
                Available = t.Available,
                FrozenQty = t.FrozenQty,
                RequestQty = suggest,
                Supplier = GetPreferredSupplier(t.MaterialCode),
                Remark = t.Status == ShortageStatus.Tight ? "库存刚好够用无缓冲，建议小批量备料" : ""
            });
        }

        // 主材优先、缺口大的在前——审批人先看最要紧的
        req.Lines = req.Lines
            .OrderByDescending(l => l.RequestQty)
            .ThenBy(l => l.MaterialType == ErpMaterialTypes.Main ? 0 : l.MaterialType == ErpMaterialTypes.Aux ? 1 : 2)
            .ThenBy(l => l.MaterialCode, StringComparer.Ordinal)
            .ToList();

        return req;
    }

    private static PurchaseRequestLine CopyLine(PurchaseRequestLine src) => new()
    {
        MaterialCode = src.MaterialCode,
        MaterialName = src.MaterialName,
        Spec = src.Spec,
        Unit = src.Unit,
        MaterialType = src.MaterialType,
        FormulaText = src.FormulaText,
        Requirement = src.Requirement,
        Available = src.Available,
        FrozenQty = src.FrozenQty,
        RequestQty = src.RequestQty,
        Supplier = src.Supplier,
        Remark = src.Remark
    };

    // ================= 明细维护 =================

    /// <summary>在申请单里加一行物料（自动带出需求量 / 可用量 / 缺口 / 建议供应商）。</summary>
    public static (bool Ok, string Message) AddRequestLine(
        string reqNo, string materialCode, double requestQty = 0, string remark = "")
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestStatus.IsEditable(req.Status))
            return (false, $"只有草稿或已驳回的单据可以改明细（当前 {req.StatusText}）。");

        if (string.IsNullOrWhiteSpace(materialCode)) return (false, "请选择物料。");
        if (req.Lines.Any(l => string.Equals(l.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase)))
            return (false, "该物料已在明细里，请直接修改申请量。");

        var shortRow = GetOutstandingShortage(req.WoNo)
            .FirstOrDefault(r => string.Equals(r.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase));

        var mat = WmsMaterials.FirstOrDefault(m =>
            string.Equals(m.Code, materialCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (mat is null && shortRow is null) return (false, $"物料主数据里不存在 {materialCode}。");

        req.Lines.Add(new PurchaseRequestLine
        {
            MaterialCode = shortRow?.MaterialCode ?? mat!.Code,
            MaterialName = shortRow?.MaterialName ?? mat?.MaterialName ?? "",
            Spec = shortRow?.Spec ?? mat?.Spec ?? "",
            Unit = shortRow?.Unit ?? mat?.Unit ?? "",
            MaterialType = shortRow?.MaterialType ?? WmsCategories.Raw,
            FormulaText = shortRow?.FormulaText ?? "手工添加，无 BOM 公式",
            Requirement = shortRow?.Requirement ?? 0,
            Available = shortRow?.Available ?? GetMaterialAvailableQty(materialCode),
            FrozenQty = shortRow?.FrozenQty ?? GetMaterialFrozenQty(materialCode),
            RequestQty = requestQty > 0 ? requestQty : Math.Max(0, (shortRow?.Shortage ?? 0)),
            Supplier = GetPreferredSupplier(materialCode),
            Remark = remark
        });

        return (true, $"已添加「{mat?.MaterialName ?? shortRow!.MaterialName}」，请核对申请量。");
    }

    /// <summary>改某行的申请量 / 供应商 / 备注。</summary>
    public static (bool Ok, string Message) UpdateRequestLine(
        string reqNo, string materialCode, double requestQty, string supplier, string remark)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestStatus.IsEditable(req.Status))
            return (false, $"只有草稿或已驳回的单据可以改明细（当前 {req.StatusText}）。");
        if (requestQty <= 1e-9) return (false, "申请量必须大于 0。");

        var line = req.Lines.FirstOrDefault(l =>
            string.Equals(l.MaterialCode, materialCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (line is null) return (false, $"明细里没有物料 {materialCode}。");

        line.RequestQty = requestQty;
        if (!string.IsNullOrWhiteSpace(supplier)) line.Supplier = supplier.Trim();
        line.Remark = remark ?? "";
        return (true, $"已更新「{line.MaterialName}」的申请量为 {line.RequestQtyText} {line.Unit}。");
    }

    /// <summary>删掉一行明细。</summary>
    public static (bool Ok, string Message) RemoveRequestLine(string reqNo, string materialCode)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestStatus.IsEditable(req.Status))
            return (false, $"只有草稿或已驳回的单据可以改明细（当前 {req.StatusText}）。");
        if (req.Lines.Count <= 1)
            return (false, "明细不能全删，至少保留一行。");

        var line = req.Lines.FirstOrDefault(l =>
            string.Equals(l.MaterialCode, materialCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (line is null) return (false, $"明细里没有物料 {materialCode}。");

        req.Lines.Remove(line);
        return (true, $"已删除「{line.MaterialName}」。");
    }

    // ================= 状态流转 =================

    /// <summary>提交（草稿 / 已驳回 → 待审批）。
    /// 提交前会**再校验一次资产占用**——申请单可能在草稿态放了好几天，
    /// 期间别的工单可能已经占了设备。冲突原因与处理建议写进备注，随单据留痕。</summary>
    public static (bool Ok, string Message) SubmitRequest(string reqNo, string @operator)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Submit))
            return (false, $"当前状态「{req.StatusText}」不能提交。");
        if (req.Lines.Count == 0)
            return (false, "明细为空，无法提交。");
        if (req.Lines.Any(l => l.RequestQty <= 1e-9))
            return (false, "明细里存在申请量为 0 的行，请补齐后再提交。");

        // 提交时重校验资源占用：草稿期间占用状况可能已变
        var occupancyNote = BuildOccupancyNoteForRequest(req);
        if (occupancyNote.Length > 0)
            req.Remark = AppendNote(req.Remark, occupancyNote);

        req.Status = PurchaseRequestStatus.Submitted;
        req.SubmitTime = DateTime.Now;
        req.Applicant = string.IsNullOrWhiteSpace(@operator) ? req.Applicant : @operator.Trim();
        return (true, occupancyNote.Length > 0
            ? $"{req.ReqNo} 已提交，等待审批。\n\n提交时资源占用复校：\n{occupancyNote}"
            : $"{req.ReqNo} 已提交，等待审批。");
    }

    /// <summary>
    /// 生成申请单时做一次资源占用校验，把冲突与建议写进申请单备注。
    /// 这样"为什么这张单要采购"和"这张单能不能按时开工"两张信息落在同一张单据上。
    /// </summary>
    private static void AppendOccupancyNoteOnCreate(PurchaseRequest req, WorkOrder wo)
    {
        var res = CheckResourceAvailability(
            req.WoNo, wo.ProductModel, wo.PlanQty,
            wo.StartDate, wo.DueDate, includeMaterial: false, source: LlmSources.Rule);
        var note = BuildOccupancyNoteCore(res);
        if (note.Length > 0) req.Remark = AppendNote(req.Remark, note);
    }

    private static string BuildOccupancyNoteForRequest(PurchaseRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.WoNo)) return "";
        var check = CheckWorkOrderShortage(req.WoNo, out var wo);
        if (check is null || wo is null) return "";

        var res = CheckResourceAvailability(
            req.WoNo, wo.ProductModel, wo.PlanQty,
            wo.StartDate, wo.DueDate, includeMaterial: false, source: LlmSources.Rule);
        return BuildOccupancyNoteCore(res);
    }

    private static string BuildOccupancyNoteCore(ResourceCheckResult? check)
    {
        if (check is null || check.AssetOk) return "";

        var sb = new System.Text.StringBuilder();
        sb.Append("【资源占用冲突】").Append(check.SummaryText);
        foreach (var l in check.AssetLines.Where(x => x.Status != AssetCheckStatus.Ok))
        {
            sb.Append($"\n· {l.AssetName}（{l.AssetCode}／工序 {l.Process}）：");
            if (l.Conflicts.Count > 0)
                sb.Append(string.Join("；", l.Conflicts.Select(c => $"工单 {c.WoNo} 占 {c.OccupiedQty} 台 {c.WindowText}")));
            else
                sb.Append($"可用余量 {l.AvailableQty} {l.Unit}，本单需 {l.NeedQty} {l.Unit}");
            var opts = l.Options.Where(o => o.Feasible).Select(o => o.Text).ToList();
            if (opts.Count > 0) sb.Append("；处理建议：").Append(string.Join("；", opts.Take(2)));
        }
        return sb.ToString();
    }

    /// <summary>批准（待审批 → 已批准）。</summary>
    public static (bool Ok, string Message) ApproveRequest(string reqNo, string approver, string remark = "")
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Approve))
            return (false, $"当前状态「{req.StatusText}」不能批准。");

        req.Status = PurchaseRequestStatus.Approved;
        req.Approver = string.IsNullOrWhiteSpace(approver) ? "采购主管" : approver.Trim();
        req.ApproveTime = DateTime.Now;
        req.ApprovalRemark = remark ?? "";
        var stillShort = req.ShortageLineCount;
        return (true, $"{req.ReqNo} 已批准（共 {req.Lines.Count} 行）"
                     + (stillShort > 0 ? $"。注意：仍有 {stillShort} 行在批准量下仍不足，下单前请复核。" : "。"));
    }

    /// <summary>驳回（待审批 → 已驳回）。必须填意见——不然申请人不知道要改什么。</summary>
    public static (bool Ok, string Message) RejectRequest(string reqNo, string approver, string remark)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Reject))
            return (false, $"当前状态「{req.StatusText}」不能驳回。");
        if (string.IsNullOrWhiteSpace(remark))
            return (false, "驳回必须填写意见，说明要改什么。");

        req.Status = PurchaseRequestStatus.Rejected;
        req.Approver = string.IsNullOrWhiteSpace(approver) ? "采购主管" : approver.Trim();
        req.ApproveTime = DateTime.Now;
        req.ApprovalRemark = remark.Trim();
        return (true, $"{req.ReqNo} 已驳回：{req.ApprovalRemark}");
    }

    /// <summary>下单（已批准 → 已下单）。</summary>
    public static (bool Ok, string Message) OrderRequest(string reqNo, string @operator)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Order))
            return (false, $"当前状态「{req.StatusText}」不能下单。");

        var noSupplier = req.Lines.Where(l => string.IsNullOrWhiteSpace(l.Supplier)).ToList();
        if (noSupplier.Count > 0)
            return (false, $"还有 {noSupplier.Count} 行未指定供应商（{string.Join("、", noSupplier.Take(3).Select(l => l.MaterialName))}），请先补齐。");

        req.Status = PurchaseRequestStatus.Ordered;
        req.Remark = string.IsNullOrWhiteSpace(req.Remark)
            ? $"下单人：{(@operator ?? "").Trim()}，下单时间：{DateTime.Now:yyyy-MM-dd HH:mm}"
            : req.Remark;
        return (true, $"{req.ReqNo} 已下单，请供应商安排到货。");
    }

    /// <summary>关闭（已批准 / 已下单 → 已关闭）。</summary>
    public static (bool Ok, string Message) CloseRequest(string reqNo, string @operator, string remark = "")
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Close))
            return (false, $"当前状态「{req.StatusText}」不能关闭。");

        req.Status = PurchaseRequestStatus.Closed;
        var who = string.IsNullOrWhiteSpace(@operator) ? "" : @operator.Trim();
        req.Remark = string.IsNullOrWhiteSpace(req.Remark)
            ? $"关闭人：{who}"
            : req.Remark + $"；关闭：{who} {remark}".Trim();
        return (true, $"{req.ReqNo} 已关闭。");
    }

    /// <summary>删除草稿单（仅草稿可删，已流转的单据必须留痕）。</summary>
    public static (bool Ok, string Message) DeleteRequest(string reqNo)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。");
        if (req.Status != PurchaseRequestStatus.Draft)
            return (false, $"只有草稿可以删除（当前 {req.StatusText}）。已流转的单据要留痕，请改用「关闭」。");

        PurchaseRequests.Remove(req);
        return (true, $"草稿 {reqNo} 已删除。");
    }
}

/// <summary>采购申请汇总（卡片展示用）</summary>
public class PurchaseRequestSummary
{
    public int Total { get; set; }
    public int Draft { get; set; }
    public int Submitted { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Ordered { get; set; }
    public int Closed { get; set; }

    /// <summary>全部明细行数</summary>
    public int LineCount { get; set; }

    /// <summary>待审批单里的明细行数（= 现在正等着人处理的采购项）</summary>
    public int PendingApprovalLines { get; set; }

    public string PendingText => $"待审批 {Submitted} 张 / {PendingApprovalLines} 个采购项";
}