using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 资材占用 —— 仓储层（本项目"占用"的主体）
//
// 与资产占用（MesDatabase.Asset.cs）的分工，务必分清：
//   · 本文件：物资（BOM 资材）→ **跨工单无条件累加**，直接决定采购量
//   · Asset： 设备/工装（可复用）→ 按时间窗判冲突，是辅助维度
//
// 计算链路（推演工单与采购申请共用这一套，口径只能有一份）：
//   工单 + BOM  →  剩余待产量 →  单位用量×(1+损耗率)×剩余量  →  该工单占用量
//                                        ↓
//                     同一物料跨工单【累加】  →  总占用
//                                        ↓
//              总占用 − 可用库存 − 在途采购量  →  净缺口（= 需采购量）
//
// 三条容易算错、已用断言锁住的地方：
//   ① 只算**剩余待产量**，不算已完成部分（做完的那部分料已经领走了）。
//   ② 跨工单**无条件累加**，不按时间窗过滤（物料是消耗品，错开时段也要备足）。
//   ③ **在途要抵扣**，否则同一批料会被多张申请单重复采购。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>资材占用记录（物料预留表）</summary>
    public static List<MaterialOccupancy> MaterialOccupancies { get; } = new();

    private static int _moSeq;

    private static string NextMaterialOccupancyId() => $"MO-{DateTime.Now:yyyyMMdd}-{++_moSeq:000}";

    // ================= 占用量计算（核心口径） =================

    /// <summary>
    /// 某物料被<b>全部工单</b>占用的合计量（跨工单累加，不按时间窗过滤）。
    /// 这是采购量计算的唯一入口，别处不要另写一份。
    /// </summary>
    public static double GetMaterialOccupiedQty(string materialCode, string? excludeWoNo = null)
        => MaterialOccupancies
            .Where(o => MaterialOccupancyStatus.IsActive(o.Status))
            .Where(o => string.Equals(o.MaterialCode, (materialCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => excludeWoNo is null
                        || !string.Equals(o.WoNo, excludeWoNo.Trim(), StringComparison.OrdinalIgnoreCase))
            .Sum(o => o.OccupiedQty);

    /// <summary>某工单当前有效的资材占用记录。纯查询。</summary>
    public static List<MaterialOccupancy> GetWorkOrderMaterialOccupancies(string woNo)
    {
        if (string.IsNullOrWhiteSpace(woNo)) return new List<MaterialOccupancy>();
        return MaterialOccupancies
            .Where(o => string.Equals(o.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase)
                        && MaterialOccupancyStatus.IsActive(o.Status))
            .ToList();
    }

    /// <summary>某工单有效占用记录的乐观锁版本合计。</summary>
    public static int GetMaterialOccupancyVersionSum(string woNo)
        => GetWorkOrderMaterialOccupancies(woNo).Sum(o => o.Version);

    /// <summary>
    /// 在途采购量：同一物料在"待审批 / 已批准 / 已下单"申请单里的申请量合计。
    /// <b>已关闭不算在途</b>——关闭只是这次请购事务结束，货没到、缺口还在。
    /// </summary>
    public static double GetMaterialInTransitQty(string materialCode)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return 0;
        return PurchaseRequests
            .Where(r => PurchaseRequestStatus.IsEffective(r.Status))
            .SelectMany(r => r.Lines)
            .Where(l => string.Equals(l.MaterialCode, materialCode.Trim(), StringComparison.OrdinalIgnoreCase))
            .Sum(l => l.RequestQty);
    }

    // ================= 按工单 + BOM 层级展开 =================

    /// <summary>
    /// 按「工单 + BOM 层级」计算该工单对每种资材的占用量。
    /// 纯计算，不写任何数据。工单不存在或无 BOM 时返回空列表。
    /// </summary>
    public static List<MaterialOccupancy> BuildWorkOrderMaterialOccupancy(WorkOrder wo)
    {
        var lines = new List<MaterialOccupancy>();
        if (wo is null) return lines;

        // 口径①：只算剩余待产量。已完工部分料已领走，不该继续占库存。
        var remaining = Math.Max(0, wo.PlanQty - wo.FinishedQty);
        if (remaining <= 0) return lines;

        foreach (var b in FetchErpBom(wo.ProductModel))
        {
            lines.Add(new MaterialOccupancy
            {
                WoNo = wo.WoNo,
                ProductModel = wo.ProductModel,
                MaterialCode = b.MaterialCode,
                MaterialName = b.MaterialName,
                Unit = b.Unit,
                MaterialType = b.MaterialType,
                QtyPerUnit = b.QtyPerUnit,
                LossRate = b.LossRate,
                PlanQty = wo.PlanQty,
                FinishedQty = wo.FinishedQty,
                RemainingQty = remaining,
                OccupiedQty = GetBomRequirement(b, remaining),
                StartTime = wo.StartDate,
                EndTime = wo.DueDate,
                DueDate = wo.DueDate,
                Status = MaterialOccupancyStatus.Reserved
            });
        }
        return lines;
    }

    // ================= 跨工单按物料汇总 =================

    /// <summary>
    /// 把多张工单的资材占用按<b>物料维度累加</b>，并算出可用、在途、净缺口（=采购量）。
    /// 这是采购申请生成采购数量的唯一依据。
    /// </summary>
    /// <param name="workOrderNos">参与的工单；为空则取全部未完工工单</param>
    /// <param name="materialFilter">只汇总这些物料（为空则汇总全部）</param>
    public static List<MaterialOccupancyRow> AggregateMaterialOccupancy(
        IEnumerable<string>? workOrderNos = null, IEnumerable<string>? materialFilter = null)
    {
        var woNos = workOrderNos?.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var filter = materialFilter?.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 口径②：跨工单**无条件累加**，不按时间窗过滤——物料是消耗品。
        var source = MaterialOccupancies
            .Where(o => MaterialOccupancyStatus.IsActive(o.Status))
            .Where(o => woNos.Count == 0 || woNos.Contains(o.WoNo))
            .Where(o => filter is null || filter.Contains(o.MaterialCode))
            .ToList();

        var rows = new List<MaterialOccupancyRow>();
        foreach (var g in source.GroupBy(o => o.MaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            var details = g.OrderBy(o => o.DueDate).ThenBy(o => o.WoNo, StringComparer.Ordinal).ToList();
            var first = details[0];
            var available = GetMaterialAvailableQty(g.Key);
            var inTransit = GetMaterialInTransitQty(g.Key);

            var row = new MaterialOccupancyRow
            {
                MaterialCode = first.MaterialCode,
                MaterialName = first.MaterialName,
                Unit = first.Unit,
                MaterialType = first.MaterialType,
                WorkOrderCount = details.Select(d => d.WoNo).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                TotalOccupied = details.Sum(d => d.OccupiedQty),
                Available = available,
                FrozenQty = GetMaterialFrozenQty(g.Key),
                InTransit = inTransit,
                Details = details,
                Spec = WmsMaterials.FirstOrDefault(m =>
                           string.Equals(m.Code, g.Key, StringComparison.OrdinalIgnoreCase))?.Spec ?? ""
            };

            row.Status = NetShortageOf(row) > 1e-9 ? ShortageStatus.Short
                : CoveredRatioOf(row) < 1.0 ? ShortageStatus.Tight
                : ShortageStatus.Ok;

            // 口径：时间窗挤兑只做**提醒**，不影响判定——物料会累加，缺不缺看的是总量。
            var crowding = details.Where(d => details.Any(o =>
                !ReferenceEquals(o, d)
                && string.Equals(o.WoNo, d.WoNo, StringComparison.Ordinal) == false
                && d.StartTime < o.EndTime && o.StartTime < d.EndTime)).ToList();
            if (crowding.Count > 0)
            {
                row.HasTimeCrowding = true;
                row.CrowdingText = string.Join("；", crowding.Take(3).Select(d =>
                    $"工单 {d.WoNo} 占用 {d.OccupiedQtyText}（{d.WindowText}）"))
                    + (crowding.Count > 3 ? $" 等 {crowding.Count} 条" : "");
            }

            rows.Add(row);
        }

        // 排序：主材优先 → 净缺口大优先 → 编码
        return rows
            .OrderByDescending(r => NetShortageOf(r) > 1e-9)
            .ThenBy(r => r.MaterialType == ErpMaterialTypes.Main ? 0 : r.MaterialType == ErpMaterialTypes.Aux ? 1 : 2)
            .ThenByDescending(r => NetShortageOf(r))
            .ThenBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ToList();
    }

    private static double NetShortageOf(MaterialOccupancyRow r) => Math.Max(0, r.TotalOccupied - r.Available - r.InTransit);

    /// <summary>覆盖倍数（可用+在途 ÷ 占用），用于判"紧张"</summary>
    private static double CoveredRatioOf(MaterialOccupancyRow r) =>
        r.TotalOccupied <= 1e-9 ? 1 : (r.Available + r.InTransit) / r.TotalOccupied;

    // ================= 占用校验（推演工单 / 建单 / 采购共用） =================

    /// <summary>
    /// 单张工单的资材占用校验：按工单 + BOM 展开 → 跨工单按物料累加 → 算出净缺口与采购量。
    /// 纯查询，不写数据。
    /// </summary>
    public static MaterialOccupancyCheck CheckMaterialOccupancy(
        string? woNo, string? productModel = null, int? planQty = null, int? finishedQty = null,
        DateTime? start = null, DateTime? end = null, DateTime? due = null, string source = LlmSources.Rule)
    {
        var result = new MaterialOccupancyCheck { Source = source };

        WorkOrder? wo = null;
        if (!string.IsNullOrWhiteSpace(woNo))
            wo = WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase));

        // 工单尚未落库时（新推演单 / 手工填表），用传入的参数构造一份**虚拟工单**参与计算。
        // 注意不能放进上面的 if 里——推演单没有工单号，那样会直接返回空结果。
        if (wo is null && !string.IsNullOrWhiteSpace(productModel) && (planQty ?? 0) > 0)
            wo = new WorkOrder
            {
                WoNo = string.IsNullOrWhiteSpace(woNo) ? "(未建单)" : woNo!.Trim(),
                ProductModel = productModel!.Trim(),
                PlanQty = planQty!.Value,
                FinishedQty = finishedQty ?? 0,
                StartDate = start ?? DateTime.Today,
                DueDate = due ?? (end ?? DateTime.Today.AddDays(7))
            };
        if (wo is null) return result;

        result.WoNo = wo.WoNo;
        result.ProductModel = wo.ProductModel;
        result.PlanQty = wo.PlanQty;
        result.FinishedQty = wo.FinishedQty;
        result.RemainingQty = Math.Max(0, wo.PlanQty - wo.FinishedQty);
        result.WindowStart = wo.StartDate;
        result.WindowEnd = wo.DueDate;
        result.Lines = BuildWorkOrderMaterialOccupancy(wo);

        // 本单已登记过的占用不重复计入（用"已登记"版本），未登记的按计算值作虚拟占用参与汇总
        var virtualLines = result.Lines
            .Where(l => !GetWorkOrderMaterialOccupancies(wo.WoNo).Any(x =>
                string.Equals(x.MaterialCode, l.MaterialCode, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // 跨工单汇总：把「全部在手工单的已登记占用」与「本次计算出的本单虚拟占用」合并后按物料汇总。
        // ⚠ 必须取**全部**在手工单的占用，不能只取本工单的——否则推演单会把别的工单已占的料漏掉，
        //   采购量算小，等真缺料时才发现，已经晚了。
        var registered = MaterialOccupancies
            .Where(o => MaterialOccupancyStatus.IsActive(o.Status))
            .Where(o => !string.Equals(o.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase))
            .ToList();

        result.Aggregated = AggregateWithVirtual(registered, virtualLines, null);
        return result;
    }

    /// <summary>把"已登记的占用"与"本次计算出的虚拟占用"合并后按物料汇总。</summary>
    private static List<MaterialOccupancyRow> AggregateWithVirtual(
        List<MaterialOccupancy> registered, List<MaterialOccupancy> virtualLines, HashSet<string>? filter)
    {
        var all = new List<MaterialOccupancy>(registered);
        foreach (var v in virtualLines)
        {
            all.Add(new MaterialOccupancy
            {
                Id = "(虚拟)",
                WoNo = v.WoNo + "*",   // 加星号区分"已登记"与"待登记"
                ProductModel = v.ProductModel,
                MaterialCode = v.MaterialCode,
                MaterialName = v.MaterialName,
                Unit = v.Unit,
                MaterialType = v.MaterialType,
                QtyPerUnit = v.QtyPerUnit,
                LossRate = v.LossRate,
                PlanQty = v.PlanQty,
                FinishedQty = v.FinishedQty,
                RemainingQty = v.RemainingQty,
                OccupiedQty = v.OccupiedQty,
                StartTime = v.StartTime,
                EndTime = v.EndTime,
                DueDate = v.DueDate,
                Status = MaterialOccupancyStatus.Reserved
            });
        }

        var rows = new List<MaterialOccupancyRow>();
        foreach (var g in all.Where(o => filter is null || filter.Contains(o.MaterialCode))
                              .GroupBy(o => o.MaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            var details = g.OrderBy(o => o.DueDate).ThenBy(o => o.WoNo, StringComparer.Ordinal).ToList();
            var first = details[0];
            var available = GetMaterialAvailableQty(g.Key);
            var inTransit = GetMaterialInTransitQty(g.Key);
            var mat = WmsMaterials.FirstOrDefault(m => string.Equals(m.Code, g.Key, StringComparison.OrdinalIgnoreCase));

            var row = new MaterialOccupancyRow
            {
                MaterialCode = first.MaterialCode,
                MaterialName = first.MaterialName,
                Unit = first.Unit,
                MaterialType = first.MaterialType,
                WorkOrderCount = details.Select(d => d.WoNo.TrimEnd('*')).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                TotalOccupied = details.Sum(d => d.OccupiedQty),
                Available = available,
                FrozenQty = GetMaterialFrozenQty(g.Key),
                InTransit = inTransit,
                Details = details,
                Spec = mat?.Spec ?? ""
            };
            row.Status = NetShortageOf(row) > 1e-9 ? ShortageStatus.Short
                : CoveredRatioOf(row) < 1.0 ? ShortageStatus.Tight
                : ShortageStatus.Ok;
            rows.Add(row);
        }

        return rows
            .OrderByDescending(r => NetShortageOf(r) > 1e-9)
            .ThenBy(r => r.MaterialType == ErpMaterialTypes.Main ? 0 : r.MaterialType == ErpMaterialTypes.Aux ? 1 : 2)
            .ThenByDescending(r => NetShortageOf(r))
            .ToList();
    }

    // ================= 占用登记（预留 / 释放） =================

    /// <summary>
    /// 为工单登记资材占用（**重建式**：先释放该工单旧记录，再按当前 BOM 重算写入）。
    /// 幂等：同一工单重复保存不会叠出两份占用。
    /// </summary>
    /// <param name="expectVersion">乐观锁；传 -1 表示首次占用不校验</param>
    public static (bool Ok, string Message, int NewVersion) ReserveMaterialsForWorkOrder(
        WorkOrder wo, string source, int expectVersion = -1, string remark = "")
    {
        if (wo is null) return (false, "工单为空，无法登记资材占用。", 0);

        // ① 乐观锁：期间被改过就拒绝，让调用方重新校验
        if (expectVersion >= 0 && GetMaterialOccupancyVersionSum(wo.WoNo) != expectVersion)
            return (false, "资材占用已被他人修改，请重新校验后再保存。", GetMaterialOccupancyVersionSum(wo.WoNo));

        var lines = BuildWorkOrderMaterialOccupancy(wo);
        if (lines.Count == 0)
        {
            // 无剩余待产 / 无 BOM：清掉旧占用即可
            ReleaseMaterialsForWorkOrder(wo.WoNo, "工单已完工或无 BOM，释放占用");
            return (true, "该工单无剩余资材占用。", 0);
        }

        // ② 重建式幂等：先释放本工单旧记录
        ReleaseMaterialsForWorkOrder(wo.WoNo, "重建占用");

        var now = DateTime.Now;
        foreach (var l in lines)
        {
            MaterialOccupancies.Add(new MaterialOccupancy
            {
                Id = NextMaterialOccupancyId(),
                WoNo = wo.WoNo,
                ProductModel = wo.ProductModel,
                MaterialCode = l.MaterialCode,
                MaterialName = l.MaterialName,
                Unit = l.Unit,
                MaterialType = l.MaterialType,
                QtyPerUnit = l.QtyPerUnit,
                LossRate = l.LossRate,
                PlanQty = l.PlanQty,
                FinishedQty = l.FinishedQty,
                RemainingQty = l.RemainingQty,
                OccupiedQty = l.OccupiedQty,
                StartTime = l.StartTime,
                EndTime = l.EndTime,
                DueDate = l.DueDate,
                Status = MaterialOccupancyStatus.Reserved,
                Source = source,
                CreatedAt = now,
                Version = 1,
                Remark = remark
            });
        }

        return (true, $"已登记 {lines.Count} 项资材占用（剩余待产 {l(lines)} 只）。".Replace("()", ""),
            GetMaterialOccupancyVersionSum(wo.WoNo));
    }

    private static int l(List<MaterialOccupancy> lines) => lines.Count > 0 ? lines[0].RemainingQty : 0;

    /// <summary>释放工单的全部资材占用（终结/撤销时调用）。</summary>
    public static (bool Ok, string Message) ReleaseMaterialsForWorkOrder(string woNo, string reason = "")
    {
        var list = GetWorkOrderMaterialOccupancies(woNo);
        if (list.Count == 0) return (false, "该工单没有需要释放的资材占用。");
        foreach (var o in list)
        {
            o.Status = MaterialOccupancyStatus.Released;
            o.Version++;
            if (!string.IsNullOrWhiteSpace(reason)) o.Remark = string.IsNullOrWhiteSpace(o.Remark) ? reason : o.Remark + "；" + reason;
        }
        return (true, $"已释放 {list.Count} 项资材占用。");
    }

    /// <summary>资材占用汇总（概览卡片用）。纯查询。</summary>
    public static (int Records, int WorkOrders, int Materials, double TotalQty) GetMaterialOccupancySummary()
    {
        var active = MaterialOccupancies.Where(o => MaterialOccupancyStatus.IsActive(o.Status)).ToList();
        return (active.Count,
                active.Select(o => o.WoNo).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                active.Select(o => o.MaterialCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                active.Sum(o => o.OccupiedQty));
    }

    // ================= 采购数量（按物料维度汇总） =================

    /// <summary>
    /// **按物料维度汇总占用 → 算出每种物料该采购多少**。
    /// 这是采购申请生成采购数量的唯一依据，替代原先"按单张工单算缺口"的做法——
    /// 后者会让同一批料被多张工单重复申请。
    /// </summary>
    /// <param name="workOrderNos">参与汇总的工单；为空取全部未完工工单</param>
    /// <param name="skipInTransit">是否扣减在途（生成新申请时必须 true，查询展示时通常 false 以看真实缺口）</param>
    public static List<MaterialOccupancyRow> AggregatePurchaseDemand(
        IEnumerable<string>? workOrderNos = null, bool skipInTransit = true)
    {
        var rows = AggregateMaterialOccupancy(workOrderNos);
        if (skipInTransit) return rows.Where(r => r.NetShortage > 1e-9).ToList();
        return rows;
    }

    /// <summary>把资材占用汇总行转成采购申请明细行（申请量 = 净缺口 = 占用 − 可用 − 在途）</summary>
    public static List<PurchaseRequestLine> ToPurchaseLines(List<MaterialOccupancyRow> rows)
    {
        var lines = new List<PurchaseRequestLine>();
        foreach (var r in rows)
        {
            lines.Add(new PurchaseRequestLine
            {
                MaterialCode = r.MaterialCode,
                MaterialName = r.MaterialName,
                Spec = r.Spec,
                Unit = r.Unit,
                MaterialType = r.MaterialType,
                // 需求公式沿用首条明细的 BOM 公式，让审批人看得出这批量是怎么算出来的
                FormulaText = r.Details.FirstOrDefault()?.FormulaText ?? "按跨工单汇总",
                // 需求量 = 跨工单累加的占用总量（保留，让审批人知道为什么采购这个量）
                Requirement = r.TotalOccupied,
                Available = r.Available,
                FrozenQty = r.FrozenQty,
                RequestQty = r.NetShortage > 1e-9 ? r.NetShortage : r.TotalOccupied,
                Supplier = GetPreferredSupplier(r.MaterialCode),
                Remark = $"占用 {r.TotalText}（{r.WorkOrderCount} 张工单）｜可用 {r.AvailableText}"
                         + (r.InTransit > 1e-9 ? $"｜在途 {r.InTransitText}" : "")
                         + (r.HasTimeCrowding ? $"｜{r.CrowdingText}" : "")
            });
        }
        return lines;
    }
}
