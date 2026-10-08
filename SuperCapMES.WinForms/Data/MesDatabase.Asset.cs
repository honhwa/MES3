using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 生产资产与占用 —— 仓储层
//
// 这一层是**防超占用的唯一落点**：所有"某资产在某段时间被占了多少"的判断，
// 都只读 `AssetOccupancies` 这张预留表，绝不临时推算——推算必然出现两处口径不一致。
//
// 计算口径（写在方法注释里，别处不要另立一套）：
//   · **时间窗重叠**：`StartTime < end && start < EndTime`——端点相接（上一张 8 号做完、
//     下一张 8 号接着做）不算冲突，否则排产会被无谓地卡住。
//   · **占用量**：只统计 `Reserved / Confirmed` 且工单号 ≠ 当前工单的记录。
//   · **可用余量** = 资产并发容量 − 上述占用量。
//   · **需求占用数**：默认每道工序 1 台（可由工单显式指定放大）。
//
// 并发防超占用（内存仓储是单线程，本身不会真并发，但**机制先立住**）：
//   1. **乐观锁**：写占用前先读 `Version`，写回时比对；期间被别人改过就拒绝并让调用方重试。
//   2. **写前重算**：不信任"刚才算过"，写入前再算一次占用量——
//      校验与写入之间隔得越久，越不能靠上一次的结论。
//   3. **同一工单幂等**：先把该工单的旧占用标记为已释放再重建，避免重复提交叠出两份。
//   将来换数据库时，第 1 条搬成 `UPDATE ... WHERE version = ?` 即可，业务逻辑不用重写。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>生产资产台账</summary>
    public static List<ProductionAsset> ProductionAssets { get; } = new();

    /// <summary>资产占用记录（预留表）</summary>
    public static List<AssetOccupancy> AssetOccupancies { get; } = new();

    private static int _occSeq;

    private static string NextOccupancyId() => $"OCC-{DateTime.Now:yyyyMMdd}-{++_occSeq:000}";

    // ================= 资产查询 =================

    /// <summary>按编码查资产（无则 null）。纯查询。</summary>
    public static ProductionAsset? FindAsset(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return ProductionAssets.FirstOrDefault(a =>
            string.Equals(a.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>某工序下的资产（可按型号与线体过滤）。纯查询。</summary>
    public static List<ProductionAsset> GetAssetsByProcess(string? process, string? productModel = null, string? line = null)
        => ProductionAssets
            .Where(a => string.Equals(a.Process, (process ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(a => a.SupportsModel(productModel))
            .Where(a => string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(a.Line)
                        || string.Equals(a.Line, line.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

    // ================= 占用量计算（核心口径） =================

    /// <summary>
    /// 某资产在 [start, end] 时间窗内被**其它工单**占用的数量。
    /// 这是整个占用校验的唯一计算入口，别处不要另写一份。
    /// </summary>
    public static int GetOccupiedQty(string assetCode, DateTime start, DateTime end, string? excludeWoNo = null)
        => AssetOccupancies
            .Where(o => string.Equals(o.AssetCode, (assetCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => OccupancyStatus.IsActive(o.Status))
            .Where(o => excludeWoNo is null
                        || !string.Equals(o.WoNo, excludeWoNo.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => o.Overlaps(start, end))
            .Sum(o => o.Qty);

    /// <summary>某资产在时间窗内的冲突占用明细（谁占了、占多少、占哪段）。纯查询。</summary>
    public static List<OccupancyConflict> GetOccupancyConflicts(
        string assetCode, DateTime start, DateTime end, string? excludeWoNo = null)
    {
        var asset = FindAsset(assetCode);
        return AssetOccupancies
            .Where(o => string.Equals(o.AssetCode, (assetCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => OccupancyStatus.IsActive(o.Status))
            .Where(o => excludeWoNo is null
                        || !string.Equals(o.WoNo, excludeWoNo.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => o.Overlaps(start, end))
            .OrderBy(o => o.StartTime)
            .Select(o => new OccupancyConflict
            {
                WoNo = o.WoNo,
                AssetCode = o.AssetCode,
                AssetName = asset?.Name ?? o.AssetCode,
                OccupiedQty = o.Qty,
                Start = o.StartTime,
                End = o.EndTime,
                OverlapText = OverlapText(start, end, o.StartTime, o.EndTime)
            })
            .ToList();
    }

    /// <summary>两段时间窗的重叠区间描述（不重叠返回空串）</summary>
    public static string OverlapText(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var s = aStart > bStart ? aStart : bStart;
        var e = aEnd < bEnd ? aEnd : bEnd;
        return s < e ? $"{s:MM-dd HH:mm} ~ {e:MM-dd HH:mm}" : "";
    }

    /// <summary>某工单当前的有效占用记录（未释放）。纯查询。</summary>
    public static List<AssetOccupancy> GetWorkOrderOccupancies(string woNo)
    {
        if (string.IsNullOrWhiteSpace(woNo)) return new List<AssetOccupancy>();
        return AssetOccupancies
            .Where(o => string.Equals(o.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase)
                        && OccupancyStatus.IsActive(o.Status))
            .ToList();
    }

    /// <summary>某工单有效占用记录的乐观锁版本合计（任一记录被别人改过就说明并发冲突）。</summary>
    public static int GetOccupancyVersionSum(string woNo)
        => GetWorkOrderOccupancies(woNo).Sum(o => o.Version);

    // ================= 资源需求分配（规则，不经大模型） =================

    /// <summary>
    /// 按工艺路线自动分配该工单需要的资产：**每道工序 1 台**，
    /// 优先选同线体的（同线体说明产能是给这条线规划的），同线体没有再选通用资产。
    /// 纯计算，不写任何数据。
    /// </summary>
    public static List<ProductionAsset> AllocateAssetsForModel(string? productModel, string? line)
    {
        var result = new List<ProductionAsset>();
        foreach (var process in ProcessSequence.Order)
        {
            var candidates = GetAssetsByProcess(process, productModel, null)
                .Where(a => a.IsAllocatable)
                .ToList();
            if (candidates.Count == 0) continue;

            var sameLine = candidates.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(line) && string.Equals(a.Line, line.Trim(), StringComparison.OrdinalIgnoreCase));
            result.Add(sameLine ?? candidates[0]);
        }
        return result;
    }

    // ================= 统一校验 =================

    /// <summary>
    /// 工单的**统一资源校验**：物料齐套 + 资产占用，四个入口（手工建单 / 智能排产 /
    /// 采购申请 / 申请提交）都走这一个方法——口径必须只有一份，否则会出现
    /// "排产说够、建单说不够"这种没法解释的情况。
    /// </summary>
    /// <param name="woNo">工单号（新建时可空；用于排除自身占用）</param>
    /// <param name="requiredAssets">显式指定的资产；为空则按工艺路线自动分配</param>
    public static ResourceCheckResult CheckResourceAvailability(
        string? woNo, string? productModel, int planQty, DateTime start, DateTime end,
        IEnumerable<ProductionAsset>? requiredAssets = null, bool includeMaterial = true, string source = "规则")
    {
        var result = new ResourceCheckResult
        {
            WoNo = (woNo ?? "").Trim(),
            ProductModel = (productModel ?? "").Trim(),
            PlanQty = planQty,
            WindowStart = start,
            WindowEnd = end,
            Source = source
        };

        if (end <= start)
        {
            // 时间窗非法：不算冲突，但要明确提示，不能默默当通过
            result.AssetLines.Add(new AssetCheckLine
            {
                Process = "（时间窗）",
                AssetName = "计划交期早于开始日期",
                Status = AssetCheckStatus.Conflict,
                Advice = "结束时间必须晚于开始时间"
            });
            return result;
        }

        var assets = requiredAssets?.Where(a => a is not null).ToList()
                     ?? AllocateAssetsForModel(productModel, null);

        foreach (var asset in assets)
        {
            result.AssetLines.Add(BuildAssetLine(asset, result, planQty));
        }

        if (includeMaterial)
        {
            var qty = planQty > 0 ? planQty : 1;
            result.Material = CheckBomAvailability(productModel, qty, qty);
        }

        return result;
    }

    private static AssetCheckLine BuildAssetLine(ProductionAsset asset, ResourceCheckResult ctx, int planQty)
    {
        var line = new AssetCheckLine
        {
            Process = asset.Process,
            AssetCode = asset.Code,
            AssetName = asset.Name,
            AssetType = asset.Type,
            // 口径：默认每道工序 1 台。工单量大需要并行时可由 requiredAssets 显式给多台。
            NeedQty = 1,
            Capacity = Math.Max(1, asset.Capacity),
            Unit = asset.Type == AssetTypes.Tooling ? "套" : asset.Type == AssetTypes.Inspector ? "台" : "台"
        };

        // 资产本身不可分配（维护 / 停机）——这是"硬冲突"，与时间冲突分开说
        if (!asset.IsAllocatable)
        {
            line.Status = AssetCheckStatus.Conflict;
            line.Conflicts.Add(new OccupancyConflict
            {
                WoNo = "—",
                AssetCode = asset.Code,
                AssetName = asset.Name,
                OccupiedQty = 0,
                Start = ctx.WindowStart,
                End = ctx.WindowEnd,
                OverlapText = $"资产当前状态「{asset.Status}」，不可排产"
            });
            line.Options.AddRange(BuildSubstituteOptions(asset, ctx));
            return line;
        }

        var occupied = GetOccupiedQty(asset.Code, ctx.WindowStart, ctx.WindowEnd, ctx.WoNo);
        line.OccupiedByOthers = occupied;
        line.AvailableQty = Math.Max(0, line.Capacity - occupied);
        line.Conflicts = GetOccupancyConflicts(asset.Code, ctx.WindowStart, ctx.WindowEnd, ctx.WoNo);

        // 判定顺序很关键：**先看余量够不够，再谈时间冲突**。
        // 并发容量 > 1 的资产（化成柜可并行 2 台）即使时间窗有重叠，只要还剩 1 台就能排进去，
        // 反过来判成"冲突"会把完全排得下的单子误杀。
        if (line.AvailableQty <= 0)
            line.Status = AssetCheckStatus.Conflict;          // 一点余量都没有
        else if (line.AvailableQty < line.NeedQty)
            line.Status = AssetCheckStatus.Insufficient;      // 还剩一点，但不够本工单用
        else
        {
            line.Status = AssetCheckStatus.Ok;
            // 够用就不算冲突：把重叠记录清掉，否则界面上会出现"状态=满足却又列着冲突"的矛盾
            line.Conflicts.Clear();
        }

        line.Options = BuildOccupancyOptions(line, asset, ctx);
        return line;
    }

    /// <summary>
    /// 生成可选方案。**全部由规则算**（可复算、可审计），大模型只负责把它讲成人话。
    /// </summary>
    private static List<OccupancyOption> BuildOccupancyOptions(AssetCheckLine line, ProductionAsset asset, ResourceCheckResult ctx)
    {
        var options = new List<OccupancyOption>();
        if (line.Status == AssetCheckStatus.Ok) return options;

        // 方案一：调整数量（余量不够时，把需求降到可用数）
        if (line.AvailableQty < line.NeedQty)
        {
            options.Add(new OccupancyOption
            {
                Kind = OccupancyOptionKind.AdjustQty,
                Text = line.AvailableQty <= 0
                    ? $"该资产在计划时间窗内已被占满，无法再排。可将本工序挪出该时段，或换用替代资产。"
                    : $"可用余量仅 {line.AvailableQty} {line.Unit}，可考虑减少并行台数或与占用方协商让出时段。",
                Feasible = line.AvailableQty > 0
            });
        }

        // 方案二：错排生产时间（找最近的无冲突时间窗）
        if (line.Status == AssetCheckStatus.Conflict)
        {
            var slot = FindNextFreeSlot(asset, ctx);
            if (slot is not null)
            {
                var delayDays = (slot.Value.Start - ctx.WindowStart).TotalDays;
                options.Add(new OccupancyOption
                {
                    Kind = OccupancyOptionKind.Reschedule,
                    Text = $"可错排到 {slot.Value.Start:MM-dd HH:mm} ~ {slot.Value.End:MM-dd HH:mm}"
                           + $"（后移 {delayDays:0.#} 天）"
                           + (slot.Value.WithinDue ? "" : "，但会超出计划交期，需与计划确认"),
                    Feasible = slot.Value.WithinDue
                });
            }
            else
            {
                options.Add(new OccupancyOption
                {
                    Kind = OccupancyOptionKind.Reschedule,
                    Text = $"往后 30 天内均未找到空闲时段（该资产被长期占用），建议改用替代资产或调整工艺安排。",
                    Feasible = false
                });
            }
        }

        // 方案三：替代资产（同工序、同型号、状态可用的其它资产）
        options.AddRange(BuildSubstituteOptions(asset, ctx));
        return options;
    }

    private static List<OccupancyOption> BuildSubstituteOptions(ProductionAsset asset, ResourceCheckResult ctx)
    {
        var options = new List<OccupancyOption>();
        var subs = ProductionAssets
            .Where(a => !string.Equals(a.Code, asset.Code, StringComparison.OrdinalIgnoreCase))
            .Where(a => string.Equals(a.Process, asset.Process, StringComparison.OrdinalIgnoreCase))
            .Where(a => a.IsAllocatable)
            .Where(a => a.SupportsModel(ctx.ProductModel))
            .Select(a => (Asset: a, Free: GetOccupiedQty(a.Code, ctx.WindowStart, ctx.WindowEnd, ctx.WoNo) < a.Capacity))
            .Where(x => x.Free)
            .OrderBy(x => string.Equals(x.Asset.Line, asset.Line, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(3)
            .ToList();

        foreach (var s in subs)
        {
            options.Add(new OccupancyOption
            {
                Kind = OccupancyOptionKind.Substitute,
                Text = $"可改用 {s.Asset.Name}（{s.Asset.Code}）"
                       + (string.IsNullOrWhiteSpace(s.Asset.Line) ? "（通用）" : $"（{s.Asset.Line}）")
                       + "，该时段有余量",
                TargetAssetCode = s.Asset.Code,
                Feasible = true
            });
        }
        return options;
    }

    /// <summary>
    /// 从计划开始时间起按天步进，找该资产第一个不冲突的时间窗。
    /// 返回 (新起始, 新结束, 是否仍不超原交期)；找不到返回 null。
    /// </summary>
    private static (DateTime Start, DateTime End, bool WithinDue)? FindNextFreeSlot(
        ProductionAsset asset, ResourceCheckResult ctx, int maxDays = 30)
    {
        var span = ctx.WindowEnd - ctx.WindowStart;
        for (var day = 1; day <= maxDays; day++)
        {
            var s = ctx.WindowStart.AddDays(day);
            var e = s.Add(span);
            if (GetOccupiedQty(asset.Code, s, e, ctx.WoNo) < Math.Max(1, asset.Capacity))
                return (s, e, e <= ctx.WindowEnd);
        }
        return null;
    }

    // ================= 占用写入（预留 / 确认 / 释放） =================

    /// <summary>
    /// 为工单写入资产占用（重建式：先释放该工单旧记录，再按当前分配重建）。
    /// 三道防超占用：① 乐观锁比对 ② 写前重算占用量 ③ 同工单幂等。
    /// </summary>
    /// <param name="expectVersion">调用方读到的版本合计；传 -1 表示不做乐观锁校验（首次占用）</param>
    public static (bool Ok, string Message, int NewVersion) ReserveAssetsForWorkOrder(
        string woNo, IEnumerable<ProductionAsset> assets, DateTime start, DateTime end,
        string source, int expectVersion = -1, bool confirmed = false, string remark = "")
    {
        if (string.IsNullOrWhiteSpace(woNo)) return (false, "工单号为空，无法登记占用。", 0);
        if (end <= start) return (false, "占用时间窗非法：结束时间必须晚于开始时间。", 0);

        var list = (assets ?? Enumerable.Empty<ProductionAsset>()).Where(a => a is not null).ToList();
        if (list.Count == 0) return (true, "该工单无需占用资产。", 0);

        // ① 乐观锁：期间被改过就拒绝，让调用方重新校验后再试
        if (expectVersion >= 0 && GetOccupancyVersionSum(woNo) != expectVersion)
            return (false, "占用信息已被他人修改，请重新校验后再保存。", GetOccupancyVersionSum(woNo));

        // ② 写前重算：不信任上一次校验的结论（校验与写入之间可能隔着人工操作）
        var blockers = new List<string>();
        foreach (var a in list)
        {
            if (!a.IsAllocatable) { blockers.Add($"{a.Name}（{a.Code}）当前「{a.Status}」"); continue; }
            var occupied = GetOccupiedQty(a.Code, start, end, woNo);
            if (occupied + 1 > Math.Max(1, a.Capacity))
                blockers.Add($"{a.Name}（{a.Code}）已被占用 {occupied}/{a.Capacity}，无余量");
        }
        if (blockers.Count > 0)
            return (false, "资产占用冲突：" + string.Join("；", blockers), GetOccupancyVersionSum(woNo));

        // ③ 同工单幂等：先释放旧记录，避免重复提交叠出两份
        ReleaseAssetsForWorkOrder(woNo, "重建占用");

        var now = DateTime.Now;
        foreach (var a in list)
        {
            AssetOccupancies.Add(new AssetOccupancy
            {
                Id = NextOccupancyId(),
                AssetCode = a.Code,
                WoNo = woNo.Trim(),
                Source = source,
                Qty = 1,
                StartTime = start,
                EndTime = end,
                Status = confirmed ? OccupancyStatus.Confirmed : OccupancyStatus.Reserved,
                CreatedAt = now,
                Version = 1,
                Remark = remark
            });
        }

        return (true, $"已登记 {list.Count} 项资产占用（{start:MM-dd HH:mm} ~ {end:MM-dd HH:mm}）。",
            GetOccupancyVersionSum(woNo));
    }

    /// <summary>工单开工时把预留升级为已确认（锁死，不再被自动重排）。</summary>
    public static (bool Ok, string Message) ConfirmAssetsForWorkOrder(string woNo)
    {
        var list = GetWorkOrderOccupancies(woNo);
        if (list.Count == 0) return (false, "该工单没有资产占用记录。");
        var changed = 0;
        foreach (var o in list)
        {
            if (o.Status == OccupancyStatus.Confirmed) continue;
            o.Status = OccupancyStatus.Confirmed;
            o.Version++;
            changed++;
        }
        return changed == 0
            ? (false, "该工单的资产占用已全部确认。")
            : (true, $"已确认 {changed} 项资产占用。");
    }

    /// <summary>释放工单的全部资产占用（终结工单 / 撤销预留时调用）。</summary>
    public static (bool Ok, string Message) ReleaseAssetsForWorkOrder(string woNo, string reason = "")
    {
        var list = GetWorkOrderOccupancies(woNo);
        if (list.Count == 0) return (false, "该工单没有需要释放的资产占用。");
        foreach (var o in list)
        {
            o.Status = OccupancyStatus.Released;
            o.Version++;
            if (!string.IsNullOrWhiteSpace(reason)) o.Remark = AppendNote(o.Remark, reason);
        }
        return (true, $"已释放 {list.Count} 项资产占用。");
    }

    /// <summary>整站资产占用汇总（概览卡片用）。纯查询。</summary>
    public static (int Total, int Allocatable, int ActiveOccupancy, int ConflictWindows) GetAssetOccupancySummary()
    {
        var total = ProductionAssets.Count;
        var allocatable = ProductionAssets.Count(a => a.IsAllocatable);
        var active = AssetOccupancies.Count(o => OccupancyStatus.IsActive(o.Status));
        var conflicts = AssetOccupancies
            .Where(o => OccupancyStatus.IsActive(o.Status))
            .GroupBy(o => (o.AssetCode, o.StartTime.Date, o.EndTime.Date))
            .Count(g => g.Count() > 1);
        return (total, allocatable, active, conflicts);
    }

    private static string AppendNote(string note, string add)
        => string.IsNullOrWhiteSpace(note) ? add : note + "；" + add;
}
