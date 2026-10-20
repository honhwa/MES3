using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 物料全链管控（一期 3.1）—— 缺料三级校验 + 采购前置期 + 自动建单 —— 数据层
//
// 这一层不重复造轮子：需求展开直接复用既有的 BuildWorkOrderMaterialOccupancy
// （单位用量 ×(1+损耗率)× 剩余待产，与资材占用台账同一公式），
// 可用 / 冻结 / 在途也复用既有的 GetMaterialAvailableQty / GetMaterialFrozenQty / GetMaterialInTransitQty，
// 只在其上补两块当前缺的东西：
//   ① **采购前置期**（L/T + 检验缓冲 + MPQ），算出"最晚什么时候必须下单"；
//   ② **建议采购量**（净缺口 + 安全库存缺口，×1.1 损耗，MPQ 向上取整，再按超量线/限额钳制）。
//
// 为什么不直接用 AggregateMaterialOccupancy（资材占用台账）：
//   台账要"登记过"才有数据，工单刚建、还没人点过占用登记时台账是空的——
//   那时缺料校验会一份数据都算不出来（静默返回"没有缺口"，比报错更危险）。
//   所以这里按当前未完工工单 + BOM **实时展开**，口径与台账完全一致（同公式），
//   但不依赖台账是否已登记，也不会写任何数据。
//
// 自动建单仍走既有 BuildRequestFromWorkOrder（一张申请单只对应一张工单、跨工单汇总、
// 已在途不重复申请），本层只在其后做"损耗与 MPQ 调整 + 数量校验复核"，
// 不另写一套建单逻辑——两套口径必然打架，这是请购环节最贵的错。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>物料采购前置期参数台账（唯一键 = MaterialCode）</summary>
    public static List<MaterialProcureParam> MaterialProcureParams { get; } = new();

    private static bool _procureParamReady;

    /// <summary>建议采购量的损耗系数（演示口径：按 10% 加放）</summary>
    public const double ChainLossFactor = 0.10;

    /// <summary>下单时点"临近"阈值（天）：距最晚下单日 ≤ 该值判临近</summary>
    public const int ChainNearDays = 7;

    /// <summary>
    /// 采购前置期参数种子。
    /// 按**物料类别 + 编码稳定哈希**推导，保证同一天反复打开结果一致（不用 Random）。
    /// 真实环境这三个数来自 ERP 供应商主数据 / 采购框架协议，这里只是让演示有可信的量级差：
    /// 原料 14~25 天（进口/定制件慢）、辅料 7~14 天、包材 5~10 天。
    /// </summary>
    public static void EnsureProcureParamSeed()
    {
        if (_procureParamReady) return;

        foreach (var m in WmsMaterials)
        {
            var h = Math.Abs(StableHash(m.Code));
            int lead, insp, buffer;
            double mpq;

            switch (m.Category)
            {
                case WmsCategories.Raw:
                    lead = 14 + h % 12;                  // 14 ~ 25 天
                    insp = 2 + h % 2;                    // 2 ~ 3 天
                    buffer = 3;
                    mpq = (h % 3) switch { 0 => 500, 1 => 1000, _ => 2000 };
                    break;
                case WmsCategories.Aux:
                    lead = 7 + h % 8;                    // 7 ~ 14 天
                    insp = 1 + h % 2;                    // 1 ~ 2 天
                    buffer = 2;
                    mpq = (h % 3) switch { 0 => 100, 1 => 200, _ => 500 };
                    break;
                default:
                    lead = 5 + h % 6;                    // 5 ~ 10 天
                    insp = 1;
                    buffer = 2;
                    mpq = h % 2 == 0 ? 1000 : 2000;
                    break;
            }

            MaterialProcureParams.Add(new MaterialProcureParam
            {
                MaterialCode = m.Code,
                MaterialName = m.MaterialName,
                Unit = m.Unit,
                Category = m.Category,
                LeadTimeDays = lead,
                InspectionDays = insp,
                ArriveBufferDays = buffer,
                MinOrderQty = mpq,
                Supplier = GetPreferredSupplier(m.Code)
            });
        }

        _procureParamReady = true;
    }

    /// <summary>
    /// 取某物料的前置期参数。物料主数据里没有的编码也返回一份保守默认值
    /// （L/T 15 + 检验 2 天、MPQ 不限），避免界面出现空白行。
    /// </summary>
    public static MaterialProcureParam GetProcureParam(string materialCode)
    {
        EnsureProcureParamSeed();
        var code = (materialCode ?? "").Trim();
        var p = MaterialProcureParams.FirstOrDefault(x =>
            string.Equals(x.MaterialCode, code, StringComparison.OrdinalIgnoreCase));
        if (p is not null) return p;

        var mat = FindWmsMaterial(code);
        return new MaterialProcureParam
        {
            MaterialCode = code,
            MaterialName = mat?.MaterialName ?? code,
            Unit = mat?.Unit ?? "",
            Category = mat?.Category ?? "",
            LeadTimeDays = 15,
            InspectionDays = 2,
            ArriveBufferDays = 3,
            MinOrderQty = 0,
            Supplier = GetPreferredSupplier(code)
        };
    }

    /// <summary>全部前置期参数（按物料类别 + L/T 倒序，界面/导出用）。纯查询。</summary>
    public static List<MaterialProcureParam> GetProcureParams()
    {
        EnsureProcureParamSeed();
        return MaterialProcureParams
            .OrderByDescending(p => p.TotalLeadDays)
            .ThenBy(p => p.MaterialCode, StringComparer.Ordinal)
            .ToList();
    }

    // ================= 缺料三级校验 =================

    /// <summary>
    /// 缺料三级校验：一级 需求合并 → 二级 扣减 → 三级 净缺口 + 建议采购量 + 采购前置期。
    /// 纯查询，不写任何数据。
    /// </summary>
    /// <param name="woNos">参与校验的工单；为空 = 全部未完工工单（跨工单合并才有意义）</param>
    /// <param name="filter">显示范围（仅短缺 / 短缺+紧张 / 全部物料）</param>
    public static ShortageChainResult BuildShortageChain(
        IEnumerable<string>? woNos = null, string filter = ChainFilters.ShortAndTight)
    {
        EnsureProcureParamSeed();

        var scope = woNos?.Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scopeSet = scope is { Count: > 0 } ? scope.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

        // 一级：按当前未完工工单 × BOM 实时展开（纯计算，不读台账、不写数据）
        var wos = WorkOrders
            .Where(IsOpenWorkOrder)
            .Where(w => scopeSet is null || scopeSet.Contains(w.WoNo))
            .ToList();

        var groups = new Dictionary<string, List<MaterialOccupancy>>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in wos)
        {
            foreach (var line in BuildWorkOrderMaterialOccupancy(w))
            {
                if (!groups.TryGetValue(line.MaterialCode, out var list))
                    groups[line.MaterialCode] = list = new List<MaterialOccupancy>();
                list.Add(line);
            }
        }

        var today = DateTime.Today;
        var rows = new List<ShortageChainRow>();

        foreach (var kv in groups)
        {
            var details = kv.Value.OrderBy(d => d.StartTime).ThenBy(d => d.WoNo, StringComparer.Ordinal).ToList();
            var first = details[0];
            var mat = FindWmsMaterial(kv.Key);
            var pp = GetProcureParam(kv.Key);

            var available = GetMaterialAvailableQty(kv.Key);
            var inTransit = GetMaterialInTransitQty(kv.Key);
            var frozen = GetMaterialFrozenQty(kv.Key);
            var totalOccupied = details.Sum(d => d.OccupiedQty);

            var row = new ShortageChainRow
            {
                MaterialCode = first.MaterialCode,
                MaterialName = first.MaterialName,
                Spec = mat?.Spec ?? "",
                Unit = first.Unit,
                MaterialType = first.MaterialType,

                // 一级：需求合并（跨工单累加）
                WorkOrderCount = details.Select(d => d.WoNo).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Requirement = totalOccupied,
                Details = details,

                // 二级：扣减（四个口径分列）
                Available = available,
                InTransit = inTransit,
                DraftQty = Math.Max(0, GetMaterialPendingQty(kv.Key) - inTransit),
                FrozenQty = frozen,
                SafetyStock = mat?.SafetyStock ?? 0,

                // 三级：净缺口
                NetShortage = Math.Max(0, totalOccupied - available - inTransit),

                // 采购前置期
                LeadTimeDays = pp.LeadTimeDays,
                InspectionDays = pp.InspectionDays,
                ArriveBufferDays = pp.ArriveBufferDays,
                MinOrderQty = pp.MinOrderQty,
                Supplier = string.IsNullOrWhiteSpace(pp.Supplier) ? GetPreferredSupplier(kv.Key) : pp.Supplier
            };

            // 校验等级：短缺 / 紧张（够用但覆盖倍数 < 1）/ 满足
            var covered = available + inTransit;
            row.VerifyLevel = row.NetShortage > 1e-9 ? ShortageStatus.Short
                : totalOccupied > 1e-9 && covered < totalOccupied - 1e-9 ? ShortageStatus.Tight
                : ShortageStatus.Ok;

            row.SafetyGap = Math.Max(0, row.SafetyStock - row.Available);

            // 需求日：取范围内**最早开工**的那张工单，前置期从它往前倒推（最紧的那条线决定下单时点）
            var earliestStart = details.Min(d => d.StartTime);
            row.DemandDate = earliestStart.Date.AddDays(-pp.ArriveBufferDays);
            row.LatestOrderDate = row.DemandDate.AddDays(-pp.TotalLeadDays);
            row.DaysLeft = (row.LatestOrderDate - today).Days;

            row.TimingStatus = !row.NeedsPurchase ? OrderTiming.None
                : row.DaysLeft < 0 ? OrderTiming.Late
                : row.DaysLeft <= ChainNearDays ? OrderTiming.Near
                : OrderTiming.Ample;

            CalcChainSuggestion(row, ChainLossFactor);
            row.Advice = BuildChainAdvice(row);

            rows.Add(row);
        }

        // 排序：已逾期下单 → 短缺 → 净缺口大 → 主材优先 → 编码
        var all = rows
            .OrderBy(r => r.TimingStatus == OrderTiming.Late ? 0 : r.TimingStatus == OrderTiming.Near ? 1 : 2)
            .ThenByDescending(r => r.NeedsPurchase)
            .ThenByDescending(r => r.NetShortage)
            .ThenBy(r => r.MaterialType == ErpMaterialTypes.Main ? 0 : r.MaterialType == ErpMaterialTypes.Aux ? 1 : 2)
            .ThenBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ToList();

        var result = new ShortageChainResult
        {
            All = all,
            WorkOrderCount = wos.Count,
            ScopeText = scopeSet is null ? "全部未完工工单" : $"指定 {wos.Count} 张工单",
            Filter = string.IsNullOrWhiteSpace(filter) ? ChainFilters.ShortAndTight : filter
        };

        result.Rows = result.Filter switch
        {
            ChainFilters.ShortOnly => all.Where(r => r.NeedsPurchase).ToList(),
            ChainFilters.All => all,
            _ => all.Where(r => r.NeedsPurchase || r.VerifyLevel == ShortageStatus.Tight).ToList()
        };

        return result;
    }


    /// <summary>
    /// 建议采购量 = 向上取整((净缺口 + 安全库存缺口) × (1 + 损耗), MPQ)，
    /// 再钳制在 [净缺口, min(需求 × 1.2, 采购限额)]。
    /// 为什么要钳制：MPQ 取整会把量抬高，抬过超量线就会在提交时被数量校验拦下——
    /// 与其事后被拦，不如生成时就把量压回可提交区间，并在行备注里写明被钳制。
    /// </summary>
    private static void CalcChainSuggestion(ShortageChainRow row, double lossFactor)
    {
        if (!row.NeedsPurchase)
        {
            row.SuggestedQty = 0;
            return;
        }

        // 已有草稿申请覆盖的部分不再重复建议——重复建单是采购环节最贵的错
        var need = row.UncoveredShortage;
        if (need <= 1e-9)
        {
            row.SuggestedQty = 0;
            return;
        }

        var baseQty = need + row.SafetyGap;
        var target = baseQty * (1 + lossFactor);

        // MPQ 向上取整
        if (row.MinOrderQty > 1e-9)
        {
            var rounded = Math.Ceiling(target / row.MinOrderQty - 1e-9) * row.MinOrderQty;
            row.MpqRounded = rounded > target + 1e-9;
            target = rounded;
        }

        // 上限：不超需求量 × 1.2，不超单次采购限额
        var upper = row.Requirement > 1e-9 ? row.Requirement * 1.2 : double.MaxValue;
        var limit = GetMaterialPurchaseLimit(row.MaterialCode);
        if (limit > 1e-9) upper = Math.Min(upper, limit);

        var floor = need;
        if (upper < floor) upper = floor;          // 限额比净缺口还小：以缺口为准（另有超限提示）

        if (target > upper + 1e-9)
        {
            target = upper;
            row.Clamped = true;
        }
        if (target < floor) target = floor;

        row.SuggestedQty = Math.Round(target, 2);
    }

    /// <summary>单行结论：缺口 + 采购量 + 下单时点建议（一句话说清该怎么办）</summary>
    private static string BuildChainAdvice(ShortageChainRow row)
    {
        if (!row.NeedsPurchase)
            return row.VerifyLevel == ShortageStatus.Tight
                ? $"够用但无缓冲（覆盖 {(row.Requirement > 1e-9 ? (row.Available + row.InTransit) / row.Requirement * 100 : 100):0.#}%），建议顺手备料。"
                : "已满足需求，无需采购。";

        // 草稿已覆盖：缺口还在（草稿未提交、货不会来），但不必再建新单
        if (row.UncoveredShortage <= 1e-9)
            return $"净缺口 {row.NetShortage:0.##} {row.Unit} 已由草稿申请单覆盖 {row.DraftQty:0.##} {row.Unit}，"
                 + "无需新建；请到「资材采购申请」提交并推进审批（草稿不算在途，故缺口仍显示）。";

        var head = $"净缺口 {row.NetShortage:0.##} {row.Unit}，建议采购 {row.SuggestedQty:0.##} {row.Unit}";
        if (row.DraftQty > 1e-9) head += $"（其中草稿已覆盖 {row.DraftQty:0.##}）";
        if (row.SafetyGap > 1e-9) head += $"（含补安全库存 {row.SafetyGap:0.##}）";
        if (row.MpqRounded) head += $"（MPQ {row.MinOrderQty:0.#} 取整）";
        if (row.Clamped) head += "（已按超量线/限额钳制）";

        var timing = row.TimingStatus switch
        {
            OrderTiming.Late => $"⚠ 最晚下单日 {row.LatestOrderDate:MM-dd} 已过 {-row.DaysLeft} 天，"
                              + $"按标准前置期 {row.LeadTimeDays + row.InspectionDays} 天已赶不上 {row.DemandDate:MM-dd} 需求日，"
                              + "需插单加急 / 让步接收 / 换料或调整排产。",
            OrderTiming.Near => $"⏰ 最晚 {row.LatestOrderDate:MM-dd} 前必须下单（剩 {row.DaysLeft} 天）。",
            _ => $"最晚下单日 {row.LatestOrderDate:MM-dd}（还有 {row.DaysLeft} 天）。"
        };

        return $"{head}；{timing}";
    }

    // ================= 自动建单 =================

    /// <summary>
    /// 按当前缺料清单**自动生成采购申请**：按工单开工先后依次建单（同工单只建一张，
    /// 跨工单需求合并、在途不重复申请，全部由既有建单逻辑保证），
    /// 建完后对每行做"损耗 + MPQ"调整，并**按已有未提交单据扣减重复量**
    /// （既有建单只把「待审批/已批准/已下单」当在途，草稿不算；
    ///   若不管，第二张单会把第一张草稿已申请的同一批料再申请一遍——这是采购最贵的错），
    /// 最后用数量校验复核，被拦下的行自动回退到净缺口。
    /// </summary>
    /// <param name="applicant">申请人</param>
    /// <param name="maxRequests">本次最多生成几张（去重后剩空单会被丢弃）</param>
    /// <returns>生成结果：单号列表 + 面向用户的一句话回执</returns>
    public static (bool Ok, string Message, List<string> ReqNos) AutoCreateRequestsFromShortage(
        string applicant, int maxRequests = 3)
    {
        var cands = GetWorkOrdersWithShortage()
            .OrderBy(w => w.StartDate)
            .ThenBy(w => w.DueDate)
            .ThenBy(w => w.WoNo, StringComparer.Ordinal)
            .Take(Math.Max(1, maxRequests))
            .ToList();

        if (cands.Count == 0)
            return (false, "当前没有需要请购的缺料工单：缺口可能已在在途申请单中，或全部物料已齐套。", new List<string>());

        var created = new List<string>();
        var skipped = new List<string>();
        var droppedEmpty = 0;
        var adjusted = 0;
        var lines = 0;

        foreach (var wo in cands)
        {
            var (ok, msg, reqNo) = BuildRequestFromWorkOrder(
                wo.WoNo, applicant,
                remark: $"按缺料三级校验自动建单（建议量含 {ChainLossFactor:P0} 损耗与 MPQ 取整）");

            if (!ok || string.IsNullOrWhiteSpace(reqNo))
            {
                skipped.Add($"{wo.WoNo}：{msg}");
                continue;
            }

            var req = FindPurchaseRequest(reqNo);
            if (req is null)
            {
                skipped.Add($"{wo.WoNo}：建单后未找到申请单");
                continue;
            }

            // 去重：扣掉其它未提交单据（草稿 / 已驳回）已覆盖的量，避免多张单重复申请同一批料
            DeductDraftCoverage(req);

            // 全部行都被扣光的单没有存在意义，删掉（不占用单号语义，也不留空单在列表里）
            if (req.Lines.Count == 0)
            {
                PurchaseRequests.Remove(req);
                droppedEmpty++;
                skipped.Add($"{wo.WoNo}：缺口已被既有未提交单据覆盖，未重复建单");
                continue;
            }

            adjusted += ApplyChainUplift(req);
            lines += req.Lines.Count;

            created.Add(reqNo);
        }

        if (created.Count == 0)
            return (false,
                "本次未新建申请单：" + string.Join("；", skipped.Take(3))
                + "。\n若缺口已由未提交单据（草稿 / 已驳回）覆盖，请到「资材采购申请」提交并推进审批，不要重复建单。",
                new List<string>());

        var message = $"已生成 {created.Count} 张采购申请（{string.Join("、", created)}），共 {lines} 个采购项，"
                    + $"其中 {adjusted} 项按损耗/MPQ 上调了申请量。"
                    + (droppedEmpty > 0 ? $"已按「未提交单据覆盖」去重，丢弃 {droppedEmpty} 张无剩余缺口的空单。" : "")
                    + (skipped.Count > 0 ? $"\n跳过 {skipped.Count} 张：{string.Join("；", skipped.Take(2))}" : "")
                    + "\n提示：申请单为草稿状态，请核对后再提交。";
        return (true, message, created);
    }

    /// <summary>
    /// 从新生成的申请单里扣掉"其它未提交单据已覆盖"的量（草稿 / 已驳回）。
    /// 返回被扣减的行数。扣到 0 的行直接移除——同一个缺口被两张单各买一遍，
    /// 是请购环节最贵的错，宁可少买也不能重复买。
    /// </summary>
    private static int DeductDraftCoverage(PurchaseRequest req)
    {
        var touched = 0;
        for (var i = req.Lines.Count - 1; i >= 0; i--)
        {
            var line = req.Lines[i];
            var coveredByOthers = Math.Max(0,
                GetMaterialPendingQty(line.MaterialCode, req.ReqNo) - GetMaterialInTransitQty(line.MaterialCode));

            if (coveredByOthers <= 1e-9) continue;

            var remain = line.RequestQty - coveredByOthers;
            touched++;

            if (remain <= 1e-9)
            {
                req.Lines.RemoveAt(i);
                continue;
            }

            line.RequestQty = Math.Round(remain, 2);
            line.Remark = AppendNote(line.Remark,
                $"已扣除其它未提交单据覆盖 {coveredByOthers:0.##} {line.Unit}，本单仅申请未覆盖部分。");
        }
        return touched;
    }

    /// <summary>
    /// 对一张草稿申请单的每行施加"损耗 + MPQ"调整，返回被调整的行数。
    /// 每行调整后立刻用既有数量校验复核；若被判定阻断（超量/超限额/重复请购），
    /// **回退到净缺口**并在行备注写明原因——宁可少买，不可让单据卡在提交不了的状态。
    /// </summary>
    private static int ApplyChainUplift(PurchaseRequest req)
    {
        var changed = 0;

        foreach (var line in req.Lines)
        {
            var raw = line.RequestQty;
            if (raw <= 1e-9) continue;

            var pp = GetProcureParam(line.MaterialCode);
            var safetyGap = Math.Max(0, (FindWmsMaterial(line.MaterialCode)?.SafetyStock ?? 0) - line.Available);

            var target = (raw + safetyGap) * (1 + ChainLossFactor);
            var mpqApplied = false;
            if (pp.MinOrderQty > 1e-9)
            {
                var rounded = Math.Ceiling(target / pp.MinOrderQty - 1e-9) * pp.MinOrderQty;
                mpqApplied = rounded > target + 1e-9;
                target = rounded;
            }

            var upper = line.Requirement > 1e-9 ? line.Requirement * 1.2 : double.MaxValue;
            var limit = GetMaterialPurchaseLimit(line.MaterialCode);
            if (limit > 1e-9) upper = Math.Min(upper, limit);
            if (upper < raw) upper = raw;

            var clamped = target > upper + 1e-9;
            if (clamped) target = upper;
            if (target < raw) target = raw;
            target = Math.Round(target, 2);

            if (Math.Abs(target - raw) < 1e-9) continue;   // 无需调整

            line.RequestQty = target;

            // 写前复核：调整不能把单据变成"提交不了"
            var chk = CheckLineQuantity(line, req.ReqNo);
            if (chk.Blocking)
            {
                line.RequestQty = raw;
                line.Remark = AppendNote(line.Remark,
                    $"原建议量已上调，但触发数量校验（{chk.Verdict}），保留净缺口 {raw:0.##}。");
                continue;
            }

            var note = $"建议量 {raw:0.##} → {target:0.##}（含 {ChainLossFactor:P0} 损耗";
            if (safetyGap > 1e-9) note += $"，补安全库存 {safetyGap:0.##}";
            if (mpqApplied) note += $"，MPQ {pp.MinOrderQty:0.#} 取整";
            if (clamped) note += "，按超量线/限额钳制";
            line.Remark = AppendNote(line.Remark, note + "）");

            changed++;
        }

        return changed;
    }
}
