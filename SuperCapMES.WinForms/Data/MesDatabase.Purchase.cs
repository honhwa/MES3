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

        // ★ 口径核心：按**物料维度跨工单汇总占用** → 再扣可用与在途 → 得采购量。
        //   绝不能"每张工单各算各的缺口"——同一批料会被多张单重复申请，是采购环节最贵的错。
        var wos = WorkOrders.Where(IsOpenWorkOrder).ToList();
        foreach (var w in wos) EnsureMaterialOccupancyRegistered(w);

        var involvedCodes = BuildWorkOrderMaterialOccupancy(wo).Select(b => b.MaterialCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var demand = AggregatePurchaseDemand(wos.Select(w => w.WoNo), skipInTransit: true)
            .Where(r => involvedCodes.Contains(r.MaterialCode))
            .ToList();

        if (demand.Count == 0)
            return (false, $"工单 {wo.WoNo} 涉及的物料已齐套（或缺口已在在途申请中），无需采购申请。", "");

        var lines = ToPurchaseLines(demand);
        var req = BuildRequestCore(wo, lines, applicant,
            AppendNote(remark, OccupancyScopeNote(demand)), autoCreated: true);
        PurchaseRequests.Add(req);

        // 同一事务边界：把设备占用冲突也写进备注（资材是主体，设备是辅助维度）
        AppendOccupancyNoteOnCreate(req, wo);

        // 参与汇总的工单数：取各物料工单数的**最大值**，不能 Sum——
        // Sum 是"每种物料各数一遍工单"，2 种物料 × 3 张单会数成 6 张，纯属重复计数。
        var woCount = demand.Count > 0 ? demand.Max(d => d.WorkOrderCount) : 0;
        return (true,
            $"已生成采购申请 {req.ReqNo}：{lines.Count} 项物料（按 {woCount} 张在手工单的资材占用跨工单汇总），"
            + $"需采购合计 {demand.Sum(d => d.NetShortage):0.##}（已扣可用库存与在途采购量）。来源工单 {wo.WoNo}。",
            req.ReqNo);
    }

    /// <summary>资材占用的汇总口径说明（写进申请单备注，让审批人知道这个量是怎么汇总来的）</summary>
    private static string OccupancyScopeNote(List<MaterialOccupancyRow> demand)
    {
        var woCount = demand.Count > 0 ? demand.Max(d => d.WorkOrderCount) : 0;
        return $"采购量按物料维度跨 {woCount} 张在手工单的资材占用汇总"
               + "（占用 = 单位用量 ×(1+损耗率)× 剩余待产量；跨工单累加），再扣可用库存与在途采购量。";
    }

    /// <summary>确保某工单有资材占用记录（未登记则按当前 BOM 补登记一次，保证汇总口径完整）</summary>
    private static void EnsureMaterialOccupancyRegistered(WorkOrder w)
    {
        if (GetWorkOrderMaterialOccupancies(w.WoNo).Count > 0) return;
        ReserveMaterialsForWorkOrder(w, MaterialOccupancySource.Manual, expectVersion: -1,
            remark: "汇总口径补登记");
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

        // 数量校验（超量 / 超限额 / 重复请购）：创建时拦住，别等到审批才发现买多
        var badLines = lines.Select(l => CheckLineQuantity(l)).Where(c => c.Blocking).ToList();
        if (badLines.Count > 0)
            return (false, $"数量校验不通过：{badLines[0].Message}"
                          + (badLines.Count > 1 ? $"（另有 {badLines.Count - 1} 项同样不通过）" : "")
                          + "。请调整数量，或先处理已有的未完成申请单。", "");

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
        WorkOrder wo, List<PurchaseRequestLine> targets, string applicant, string remark, bool autoCreated)
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

        // 明细已由调用方按"跨工单汇总的净缺口"算好（申请量 = 占用 − 可用 − 在途），这里直接落单
        foreach (var t in targets)
            req.Lines.Add(CopyLine(t));

        // 主材优先、采购量大的在前——审批人先看最要紧的
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

        var oldQty = line.RequestQty;
        line.RequestQty = requestQty;
        if (!string.IsNullOrWhiteSpace(supplier)) line.Supplier = supplier.Trim();
        line.Remark = remark ?? "";

        // 改完立刻复核数量（排除本单自身，避免自己拦自己）
        var chk = CheckLineQuantity(line, req.ReqNo);
        if (chk.Blocking)
        {
            line.RequestQty = oldQty;   // 回滚，不留半改状态
            return (false, chk.Message);
        }
        return (true, $"已更新「{line.MaterialName}」的申请量为 {line.RequestQtyText} {line.Unit}。"
                      + (chk.Verdict == QtyCheckVerdict.High ? " 注意：已超过需求量，请确认是否买多。" : ""));
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

        // 写前重算数量校验：草稿可能放了多天，期间别人可能已提交同一物料
        var (blockQty, qtyMsg, _) = ValidateRequestQuantity(req);
        if (blockQty) return (false, "提交被拦下（提交时重新校验）：" + qtyMsg);

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
    /// <summary>
    /// 删除采购申请草稿单。
    ///
    /// 三条硬规则（2026-10-08）：
    ///   ① **状态**：只认草稿，且判定走状态机 <see cref="PurchaseRequestActions.CanDo"/>，
    ///      不再在方法里另写一套——将来加动作时才不会出现"两个地方口径不一致"。
    ///      已提交/待审批/已批准/已下单/已关闭一律不可删；已驳回也不算（它要留痕、改完重提）。
    ///   ② **权限**：申请人本人，或采购部主管/经理（管理岗）。其他人一律拒绝。
    ///   ③ **超限不影响可删除性**：超量/超限额/重复请购是一次性**校验结论**，不是单据状态，
    ///      所以它永远不会成为删除的障碍；反过来说，如果哪天有人把"超限"做成状态，
    ///      这里也会因为"非草稿"而拒绝——这正是应有的行为（带超限标记的单据不该无痕消失）。
    ///      本方法**刻意不调任何数量校验**，否则超限单会被"校验失败"挡住删不掉。
    ///
    /// 关联记录处理：本单退出在途汇总（<c>GetMaterialPendingQty</c> 自动少掉这部分），
    /// 采购汇总统计随之重算；**资材占用不受影响**——占用属于工单而非申请单，删申请单不释放占用。
    /// </summary>
    public static (bool Ok, string Message) DeleteRequest(string reqNo, string @operator)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在或已被删除。");

        // ① 状态校验（走状态机，与界面按钮同一口径）
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Delete))
            return (false, $"只有草稿可以删除（当前「{req.StatusText}」）。已流转的单据要留痕，请改用「关闭」。");

        // ② 权限校验（无论从界面点还是绕过界面直接调用，这一关都会跑）
        var op = (@operator ?? "").Trim();
        if (op.Length == 0)
            return (false, "未提供操作人，无法判定删除权限。");
        if (!CanDeleteRequest(req, op))
            return (false, $"没有删除权限：{reqNo} 由「{req.Applicant}」创建，只有申请人本人"
                        + $"或采购部主管/经理可以删除（当前操作人：{op}）。");

        // ③ 统计影响：删掉多少行采购量会回到缺口清单里，回执要说清楚，方便用户核对
        var lines = req.Lines.Count;
        var qty = req.Lines.Sum(l => l.RequestQty);
        var codes = req.Lines.Select(l => l.MaterialCode).ToList();

        PurchaseRequests.Remove(req);
        return (true, $"草稿 {reqNo} 已删除（不可恢复）。{lines} 项物料、合计 {qty:0.##} 的采购量"
                    + "已退出在途统计，若原本因此不再计入缺口，现在会重新出现在缺口清单中。"
                    + $"涉及物料：{string.Join("、", codes.Take(5))}{(codes.Count > 5 ? $" 等 {codes.Count} 种" : "")}。");
    }

    /// <summary>
    /// 能否删除某单（前端按钮启用态与后端校验共用同一个判定，避免两处不一致）。
    /// 规则：申请人本人，或采购部主管/经理。操作人查不到人员档案时按普通员工处理（只能删本人）。
    /// </summary>
    public static bool CanDeleteRequest(PurchaseRequest req, string @operator)
    {
        if (req is null) return false;
        var op = (@operator ?? "").Trim();
        if (op.Length == 0) return false;
        if (string.Equals(req.Applicant, op, StringComparison.Ordinal)) return true;   // 本人

        var me = Users.FirstOrDefault(u =>
            string.Equals(u.Name, op, StringComparison.Ordinal)
            || string.Equals(u.EmpNo, op, StringComparison.Ordinal));
        if (me is null) return false;                                                   // 无档案 → 普通员工
        return me.Dept == "采购部"
               && (me.Position.Contains("主管") || me.Position.Contains("经理"));     // 管理岗
    }

    /// <summary>删除按钮的禁用原因（界面上直接显示原因，不让用户对着灰按钮猜）。</summary>
    public static string DeleteBlockedReason(PurchaseRequest? req, string @operator)
    {
        if (req is null) return "请先选择一张申请单。";
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Delete))
            return $"仅草稿可删除（当前「{req.StatusText}」）";
        if (!CanDeleteRequest(req, @operator))
            return $"仅申请人「{req.Applicant}」或采购部主管可删除";
        return "";
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

public static partial class MesDatabase
{
// ============================================================================
// 采购数量校验（超量 / 超限额 / 重复请购）
//
// 统计口径（2026-10-08 明确，写下来是为了别处不再各想一套）：
//   · **参与汇总的申请单**：状态为 草稿 / 待审批 / 已批准 / 已下单（即 `!IsFinal`）。
//     **已关闭不计入** —— 关闭只是这次请购事务结束，货没到、需求还在，不该重复请购。
//   · **按物料汇总**：同一物料在上述所有单据里的申请量合计；校验某单时排除该单自身。
//   · **比较基准**：需求量（跨工单累加的资材占用）���可用库存（只作参考，不作超量依据）、
//     单次采购限额（`WmsMaterial.MaxPurchaseQty`，0 = 不限）。
//
// 超量判定：可用库存小**不是**超量的理由（采购本来就是为了补缺口），
//   真正要拦的是"买多了"：累计申请量 > 需求量 × 1.2，或 > 单次限额。
//
// 并发一致性：① 校验纯查询不锁；② **写前重算**——保存/提交时重新跑一遍本方法，
//   因为草稿可能放了好几天，期间别人可能已提交同一物料；③ 单据自身在汇总里被排除，避免自己拦自己。
// ============================================================================

/// <summary>某物料在**未完成申请单**里的申请量合计（已关闭不计入）。纯查询。</summary>
public static double GetMaterialPendingQty(string materialCode, string? excludeReqNo = null)
{
    if (string.IsNullOrWhiteSpace(materialCode)) return 0;
    return PurchaseRequests
        .Where(r => !PurchaseRequestStatus.IsFinal(r.Status))
        .Where(r => excludeReqNo is null
                    || !string.Equals(r.ReqNo, excludeReqNo.Trim(), StringComparison.OrdinalIgnoreCase))
        .SelectMany(r => r.Lines)
        .Where(l => string.Equals(l.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase))
        .Sum(l => l.RequestQty);
}

/// <summary>某物料"已批准 + 已下单"的量（PendingQty 的子集，单列供界面展示）。纯查询。</summary>
public static double GetMaterialOrderedQty(string materialCode, string? excludeReqNo = null)
{
    if (string.IsNullOrWhiteSpace(materialCode)) return 0;
    return PurchaseRequests
        .Where(r => r.Status == PurchaseRequestStatus.Approved || r.Status == PurchaseRequestStatus.Ordered)
        .Where(r => excludeReqNo is null
                    || !string.Equals(r.ReqNo, excludeReqNo.Trim(), StringComparison.OrdinalIgnoreCase))
        .SelectMany(r => r.Lines)
        .Where(l => string.Equals(l.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase))
        .Sum(l => l.RequestQty);
}

/// <summary>取物料的采购限额（0 = 不限）。</summary>
public static double GetMaterialPurchaseLimit(string materialCode)
    => string.IsNullOrWhiteSpace(materialCode)
        ? 0
        : FindWmsMaterial(materialCode)?.MaxPurchaseQty ?? 0;

/// <summary>
/// 单行数量校验：把"同物料未完成申请量 + 本次申请量"与需求/限额一起比，给出结论与提示文案。
/// 纯查询，不改任何数据。<paramref name="excludeReqNo"/> 传入被校验单自身单号，避免自己拦自己。
/// </summary>
public static PurchaseLineCheck CheckLineQuantity(PurchaseRequestLine line, string? excludeReqNo = null, double overFactor = 1.2)
{
    ArgumentNullException.ThrowIfNull(line);
    var check = new PurchaseLineCheck
    {
        MaterialCode = line.MaterialCode,
        MaterialName = string.IsNullOrWhiteSpace(line.MaterialName)
            ? (FindWmsMaterial(line.MaterialCode)?.MaterialName ?? line.MaterialCode)
            : line.MaterialName,
        Unit = string.IsNullOrWhiteSpace(line.Unit) ? (FindWmsMaterial(line.MaterialCode)?.Unit ?? "") : line.Unit,
        Requirement = line.Requirement,
        Available = line.Available > 0 ? line.Available : GetMaterialAvailableQty(line.MaterialCode),
        RequestQty = line.RequestQty,
        MaxPurchaseQty = GetMaterialPurchaseLimit(line.MaterialCode),
        OverFactor = overFactor <= 0 ? 1.2 : overFactor
    };
    check.PendingQty = GetMaterialPendingQty(line.MaterialCode, excludeReqNo);
    check.OrderedQty = GetMaterialOrderedQty(line.MaterialCode, excludeReqNo);

    // 判定优先级：限额 > 超量/重复 > 偏高 > 通过
    if (check.OverLimitHit)
        check.Verdict = QtyCheckVerdict.OverLimit;
    else if (check.Cumulative > check.OverLine)
        // 已有在途还超额申请 → 归为"重复请购"，提示语更贴近真实原因
        check.Verdict = check.HasPending ? QtyCheckVerdict.Duplicate : QtyCheckVerdict.Over;
    else if (check.Cumulative > check.Requirement)
        check.Verdict = QtyCheckVerdict.High;
    else
        check.Verdict = QtyCheckVerdict.Ok;

    return check;
}

/// <summary>校验一张单据的<b>全部</b>明细行。纯查询。</summary>
public static List<PurchaseLineCheck> CheckRequestQuantity(PurchaseRequest req, double overFactor = 1.2)
{
    if (req is null) return new List<PurchaseLineCheck>();
    return req.Lines.Select(l => CheckLineQuantity(l, req.ReqNo, overFactor)).ToList();
}

/// <summary>整单校验结论：是否存在应阻断的行；并汇总最严重的一条提示。</summary>
public static (bool Blocking, string Message, List<PurchaseLineCheck> Checks) ValidateRequestQuantity(
    PurchaseRequest req, double overFactor = 1.2)
{
    var checks = CheckRequestQuantity(req, overFactor);
    var blockers = checks.Where(c => c.Blocking).ToList();
    if (blockers.Count == 0)
        return (false, "", checks);

    var worst = blockers.OrderByDescending(c => c.Verdict == QtyCheckVerdict.OverLimit ? 2 : 1).First();
    return (true, $"{blockers.Count} 项物料不通过数量校验：{worst.Message}"
                  + (blockers.Count > 1 ? $"（另有 {blockers.Count - 1} 项同样不通过）" : ""), checks);
}
}

// ============================================================================
public static partial class MesDatabase
{
// 采购分析的数据取数（供大模型推理与规则降级共用，保证两边看到的数完全一致）
// ============================================================================

/// <summary>
/// 按「未完工工单 × BOM」现算某物料的需求量（不依赖占用登记）。
/// 用于占用尚未登记时的需求估算。
/// </summary>
public static double GetMaterialDemandByOpenWorkOrders(string materialCode)
{
    if (string.IsNullOrWhiteSpace(materialCode)) return 0;
    var code = materialCode.Trim();
    double total = 0;
    foreach (var w in WorkOrders.Where(IsOpenWorkOrder))
    {
        var remaining = Math.Max(0, w.PlanQty - w.FinishedQty);
        if (remaining <= 0) continue;
        var b = FetchErpBom(w.ProductModel)
            .FirstOrDefault(x => string.Equals(x.MaterialCode, code, StringComparison.OrdinalIgnoreCase));
        if (b is not null) total += GetBomRequirement(b, remaining);
    }
    return total;
}

/// <summary>
/// 构造采购分析的输入快照（放在仓储层实现：Models 不反向依赖 Data，保持分层干净）。
/// 规则与大模型看到的数来自这同一份，杜绝"两边口径不一致"。
/// </summary>
public static PurchaseAnalysisInput BuildPurchaseAnalysisInput(string? materialCode, string? excludeReqNo = null)
{
    var code = (materialCode ?? string.Empty).Trim();
    var mat = FindWmsMaterial(code);
    var agg = AggregateMaterialOccupancy(null, new[] { code }).FirstOrDefault();
    var (consumed, samples, _) = GetMaterialConsumption(code);
    return new PurchaseAnalysisInput
    {
        MaterialCode = agg?.MaterialCode ?? mat?.Code ?? code,
        MaterialName = agg?.MaterialName ?? mat?.MaterialName ?? "",
        Unit = agg?.Unit ?? mat?.Unit ?? "",
        MaterialType = agg?.MaterialType ?? mat?.Category ?? "",
        Available = agg?.Available ?? GetMaterialAvailableQty(code),
        SafetyStock = mat?.SafetyStock ?? 0,
        MaxPurchaseQty = mat?.MaxPurchaseQty ?? 0,
        // 需求：优先用已登记的资材占用；还没登记时回退到"按未完工工单 × BOM 现算"——
        // 否则用户在工单还没建、占用还没登记时问"这料要不要买"，会得到 0 需求的错误结论。
        Requirement = agg is { TotalOccupied: > 0 }
            ? agg.TotalOccupied
            : GetMaterialDemandByOpenWorkOrders(code),
        PendingQty = GetMaterialPendingQty(code, excludeReqNo),
        ConsumedQty = consumed,
        ConsumptionSamples = samples,
        RelatedWoCount = agg?.WorkOrderCount ?? 0
    };
}

/// <summary>
/// 某物料近 N 天的历史领用量（WMS 现场领用单，出库方向）。
/// 这是"历史消耗"口径——它反映**实际领过多少**，比 BOM 推算更接近真实消耗（含损耗、超领）。
/// 种子数据里领用单较少，样本不足时返回 false，界面必须如实标注。
/// </summary>
public static (double Qty, int SampleCount, int Days) GetMaterialConsumption(string materialCode, int days = 90)
{
    if (string.IsNullOrWhiteSpace(materialCode)) return (0, 0, days);
    var from = DateTime.Today.AddDays(-days);

    var issued = WmsOrders
        .Where(o => o.Type == WmsOrderTypes.SiteIssue && o.Status == WmsOrderStatus.Posted)
        .Where(o => o.PostTime is not null && o.PostTime.Value >= from)
        .ToList();
    if (issued.Count == 0) return (0, 0, days);

    var qty = WmsOrderLines
        .Where(l => issued.Any(o => o.OrderNo == l.OrderNo))
        .Where(l => string.Equals(l.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase))
        .Sum(l => l.Quantity);
    var lines = WmsOrderLines.Count(l => issued.Any(o => o.OrderNo == l.OrderNo)
                                      && string.Equals(l.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase));
    return (qty, lines, days);
}

/// <summary>
/// 采购分析的**完整输入快照**（规则先算好，模型只负责解读与预测措辞）。
/// 放在一处定义，保证"模型看到的"与"规则算出的"永远来自同一份数据。
/// </summary>
}
