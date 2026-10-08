using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 主动预警与辅助决策 —— 计算层（**纯查询，不写任何数据**）
//
// 与既有「预警中心」的分工：
//   预警中心：把各模块"待处理事项"投影成被动列表，等人去看
//   本文件：给出**结论 + 建议动作**，回答"这批料会不会废""这台设备要不要提前修"
//
// 三条预警：
//   ① 物料临期 × 未来需求匹配 → 能不能消耗完？要不要预防性采购？
//   ② 设备异常集中度 → 异常集中在哪个部件？要不要预防性维护？
//   ③ 呆滞料占压 → 哪些料占着钱不用？
//
// 所有阈值见 <see cref="MesDatabaseInsightRules"/>，全部可配。
// 所有数字本地算、可复算；模型（若有）只参与措辞，不参与任何判定。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>三类预警汇总。纯查询。</summary>
    public static InsightReport BuildInsightReport(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        return new InsightReport
        {
            GeneratedAt = at,
            Rules = MesDatabaseInsightRules.Snapshot(),
            Expiry = AnalyzeExpiryConsumption(at),
            Equipment = AnalyzeEquipmentRisk(at),
            SlowMoving = AnalyzeSlowMoving(at)
        };
    }

    // ========================================================================
    // ① 物料临期 × 未来需求匹配
    //
    // 核心判断（这是本预警的价值所在，单纯的"还剩多少天"没有决策价值）：
    //   对每个临期批次，**按 FIFO 模拟**它在效期内能被消耗掉多少，
    //   再看**效期过后还有没有需求**：
    //     · 效期内能消耗完            → 无需处置，但必须优先出库（否则到期就废）
    //     · 效期内消耗不完 + 效期后仍有需求 → 建议**预防性采购**（新批次效期长，顶得上）
    //     · 效期内消耗不完 + 效期后无需求   → 到期**报废**（报废物料处置流程）
    // ========================================================================
    public static List<ExpiryInsightRow> AnalyzeExpiryConsumption(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;

        var result = new List<ExpiryInsightRow>();

        // 临期 + 已过期的库存批次（无有效期的料不存在效期问题，直接排除）
        var fifo = GetStockFifo()
            .Where(r => r.ExpiryDate is not null && r.Quantity > 1e-9)
            .Where(r => (r.DaysLeft ?? int.MaxValue) <= MesDatabaseInsightRules.NearExpiryDays)
            .ToList();
        if (fifo.Count == 0) return result;

        // 未来工单的资材需求（跨工单累加）——这是判断"消耗得完吗"的唯一依据。
        // ⚠ 占用预留表可能还没登记（比如工单刚建、或本进程还没跑过登记），
        //   此时汇总为 0，会把所有临期料都判成"将过期报废"——那是**危险的方向性错误**。
        //   所以这里必须回退到"按未完工工单 × BOM 现算"。
        var demand = AggregateMaterialOccupancy()
            .GroupBy(r => r.MaterialCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalOccupied), StringComparer.OrdinalIgnoreCase);
        foreach (var code in fifo.Select(x => x.MaterialCode).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!demand.TryGetValue(code, out var v) || v <= 1e-9)
                demand[code] = GetMaterialDemandByOpenWorkOrders(code);
        }

        foreach (var g in fifo.GroupBy(r => r.MaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            var mat = FindWmsMaterial(g.Key);
            var unit = mat?.Unit ?? g.First().Unit;
            var totalDemand = demand.TryGetValue(g.Key, out var d) ? d : 0;
            var daily = totalDemand / Math.Max(1, MesDatabaseInsightRules.DemandHorizonDays);   // 日均消耗

            // FIFO 模拟：先到期先消耗，逐批算出效期内能吃掉多少
            var batches = g.OrderBy(r => r.ExpiryDate).ToList();
            var stockAll = batches.Sum(b => b.Quantity);

            foreach (var b in batches)
            {
                var daysLeft = b.DaysLeft ?? int.MaxValue;
                // 效期内可消耗量 = min(批次量, 日均消耗 × 剩余天数)；已过期则不可消耗
                var consumable = daysLeft <= 0
                    ? 0
                    : Math.Min(b.Quantity, daily * daysLeft);
                var leftover = Math.Max(0, b.Quantity - consumable);

                // 该物料效期过后还需要多少 = 总需求 − 效期内可消耗总量
                var consumableAll = batches.Sum(x => x.DaysLeft > 0
                    ? Math.Min(x.Quantity, daily * (x.DaysLeft!.Value))
                    : 0);
                var demandAfterExpiry = Math.Max(0, totalDemand - consumableAll);

                var row = new ExpiryInsightRow
                {
                    MaterialCode = g.Key,
                    MaterialName = mat?.MaterialName ?? batches[0].MaterialName,
                    Unit = unit,
                    LotNo = b.LotNo,
                    Location = b.Location,
                    StockQty = b.Quantity,
                    ExpiryDate = b.ExpiryDate,
                    DaysLeft = b.DaysLeft.HasValue ? (int)Math.Floor(b.DaysLeft.Value) : null,
                    ConsumableQty = consumable,
                    LeftoverQty = leftover,
                    DailyDemand = Math.Round(daily, 4)
                };

                if (daysLeft <= 0)
                {
                    // 已过期：直接报废
                    row.Action = ExpiryActions.WillWaste;
                    row.WasteQty = b.Quantity;
                    row.Level = InsightLevels.Urgent;
                    row.ReasonText = $"已于 {b.ExpiryDate:yyyy-MM-dd} 过期，剩余 {b.Quantity:0.##} {unit} 已不可用";
                    row.ActionText = "立即按报废物料流程处置，并核查同批次其它库存与在库物料的效期台账";
                }
                else if (leftover <= 1e-9)
                {
                    // 效期内能消耗完
                    row.Action = ExpiryActions.CanConsume;
                    row.Level = daily <= 1e-9 ? InsightLevels.Low : InsightLevels.Medium;
                    row.ReasonText = daily <= 1e-9
                        ? $"该批次已无工单需求（无需求覆盖），{daysLeft} 天后到期存在报废风险"
                        : $"按日均需求 {daily:0.###} {unit} 测算，效期内可消耗完（剩余 {daysLeft} 天，需日耗 ≥ {(b.Quantity / daysLeft):0.###}）";
                    row.ActionText = "按 FIFO 优先出库该批次；出库单下达时勾选『最早到期优先』";
                }
                else if (demandAfterExpiry > 1e-9)
                {
                    // 消耗不完但效期后仍要用 → 提前补货
                    row.Action = ExpiryActions.PreBuy;
                    row.SuggestedBuyQty = Math.Round(demandAfterExpiry, 2);
                    var coverAfter = daily > 1e-9 ? leftover / daily : 999;
                    row.Level = coverAfter < MesDatabaseInsightRules.PreBuyCoverDays ? InsightLevels.Urgent : InsightLevels.High;
                    row.ReasonText =
                        $"效期内仅能消耗 {consumable:0.##} {unit}，将剩余 {leftover:0.##} {unit}；"
                        + $"而效期后仍有需求 {demandAfterExpiry:0.##} {unit}（剩余可撑 {coverAfter:0.#} 天）";
                    row.ActionText =
                        $"建议预防性采购 {row.SuggestedBuyQty:0.##} {unit}，并在工单中把该批次排在其它批次之前消耗";
                }
                else
                {
                    // 消耗不完且效期后无需求 → 报废
                    row.Action = ExpiryActions.WillWaste;
                    row.WasteQty = Math.Round(leftover, 2);
                    var rate = stockAll > 1e-9 ? leftover / stockAll : 1;
                    row.Level = rate >= MesDatabaseInsightRules.WasteRateThreshold ? InsightLevels.High : InsightLevels.Medium;
                    row.ReasonText =
                        $"效期内最多消耗 {consumable:0.##} {unit}，将剩余 {leftover:0.##} {unit}（占该物料库存 {rate:P0}）；"
                        + "且效期后无工单需求，此批只能报废";
                    row.ActionText = "提前发起报废申请；若近期可能有零星需求，可先按 FIFO 消化再报";
                }

                result.Add(row);
            }
        }

        return result
            .OrderByDescending(r => InsightLevels.Weight(r.Level))
            .ThenBy(r => r.DaysLeft ?? 999)
            .ToList();
    }

    // ========================================================================
    // ② 设备异常集中度与预防性维护
    //
    // 判断逻辑：频次高 ≠ 要修。真正指向"该修某个部件"的是**集中度**——
    //   次数多且集中在同一个 SubType（如全是"精度漂移"）→ 明确的部件问题，安排针对性维护
    //   次数多但分散（什么都坏一点）→ 更可能是整体老化，考虑整机预防性维护/更新
    //   再叠加"距上次保养多久"，保养超期 + 异常集中 → 高风险
    // ========================================================================
    public static List<EquipmentInsightRow> AnalyzeEquipmentRisk(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;

        var from = at.AddDays(-MesDatabaseInsightRules.DeviceWindowDays);

        var abns = AbnormalRecords
            .Where(a => !string.IsNullOrWhiteSpace(a.EquipmentCode))
            .Where(a => a.OccurTime >= from)
            .ToList();

        var alarms = DeviceAlarms.Where(a => a.Time >= from).ToList();
        var lastMaint = TpmRecords
            .GroupBy(r => r.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(r => r.ExecuteTime), StringComparer.OrdinalIgnoreCase);
        var nextTpm = TpmPlans
            .Where(p => p.NextDueTime is not null)
            .GroupBy(p => p.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(p => p.NextDueTime!.Value), StringComparer.OrdinalIgnoreCase);

        var codes = abns.Select(a => a.EquipmentCode)
            .Concat(alarms.Select(a => a.DeviceCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<EquipmentInsightRow>();
        foreach (var code in codes)
        {
            var eq = Equipments.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            var mine = abns.Where(a => string.Equals(a.EquipmentCode, code, StringComparison.OrdinalIgnoreCase)).ToList();
            var myAlarms = alarms.Count(a => string.Equals(a.DeviceCode, code, StringComparison.OrdinalIgnoreCase));
            var subCounts = mine
                .GroupBy(a => string.IsNullOrWhiteSpace(a.SubType) ? "未分类" : a.SubType)
                .ToDictionary(g => g.Key, g => g.Count());

            // ⚠ 集中度只能看**设备类**子类。异常单上挂了设备编码不等于异常属于设备——
            //   "操作不规范""来料不良""漏工序"这些是人的/来料/流程问题，算进集中度会把
            //   结论带偏（实测踩过：注液机异常里"操作不规范"占 50%，被判成"操作不规范部件劣化"）。
            var deviceTypes = new HashSet<string>(
                AbnormalSubTypes.Map[AbnormalCategories.Equipment], StringComparer.OrdinalIgnoreCase);
            var deviceSubCounts = subCounts
                .Where(kv => deviceTypes.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            var deviceSubTotal = deviceSubCounts.Values.Sum();

            var top = deviceSubCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).FirstOrDefault();
            var concentration = deviceSubTotal > 0 ? (double)(top.Value) / deviceSubTotal : 0;

            var daysSinceMaint = lastMaint.TryGetValue(code, out var lm)
                ? (int)(at.Date - lm.Date).TotalDays
                : (int?)null;
            var tpmDueDays = nextTpm.TryGetValue(code, out var nd) ? (int)(nd - at).TotalDays : (int?)null;

            var noDeviceSubEarly = deviceSubTotal == 0;
            var row = new EquipmentInsightRow
            {
                EquipmentCode = code,
                EquipmentName = eq?.Name ?? code,
                Section = eq?.Section ?? "",
                Line = LineOfEquipmentCode(code),
                AbnormalCount = mine.Count,
                OpenCount = mine.Count(a => a.Status != AbnormalStatus.Closed),
                AlarmCount = myAlarms,
                SubTypeCounts = subCounts,
                Concentration = Math.Round(concentration, 3),
                TopSubType = noDeviceSubEarly ? "（无设备类子类）" : (top.Key ?? "—"),
                DaysSinceMaintenance = daysSinceMaint,
                TpmDueDays = tpmDueDays
            };

            var concentrated = concentration >= MesDatabaseInsightRules.DeviceConcentrationThreshold;
            var frequent = mine.Count >= MesDatabaseInsightRules.DeviceAbnormalThreshold;
            var maintStale = (daysSinceMaint is null || daysSinceMaint > MesDatabaseInsightRules.MaintenanceStaleDays)
                              || (tpmDueDays is not null && tpmDueDays < 0);

            if (!frequent && !maintStale)
                continue;   // 既不频繁、保养也正常 → 不列，避免噪声

            var noDeviceSub = deviceSubTotal == 0;
            if (frequent && concentrated && !noDeviceSub)
            {
                row.Level = InsightLevels.High;
                row.ReasonText =
                    $"近 {MesDatabaseInsightRules.DeviceWindowDays} 天异常 {mine.Count} 次，其中「{row.TopSubType}」占 {concentration:P0}"
                    + $"（{top.Value}/{mine.Count} 次）；未闭环 {row.OpenCount} 次"
                    + (daysSinceMaint is not null ? $"；距上次保养已 {daysSinceMaint} 天" : "；无保养记录");
                row.MaintenanceItems.AddRange(MaintenanceForSubType(row.TopSubType));
                row.ActionText =
                    $"疑似「{row.TopSubType}」部件劣化，建议在下次计划停机窗口安排：{string.Join("、", row.MaintenanceItems.Take(3))}"
                    + (row.OpenCount > 0 ? $"；并先闭环 {row.OpenCount} 条未处理异常" : "");
            }
            else if (frequent)
            {
                row.Level = InsightLevels.Medium;
                row.ReasonText = noDeviceSub
                    ? $"近 {MesDatabaseInsightRules.DeviceWindowDays} 天异常 {mine.Count} 次"
                      + $"（{string.Join("、", subCounts.Keys)}），**均不属于设备类故障**（设备故障停机/精度漂移/报警频繁/夹具异常/传动异常）"
                      + $"；更可能是人员操作、来料或流程问题，不该按设备劣化处理"
                    : $"近 {MesDatabaseInsightRules.DeviceWindowDays} 天设备类异常 {deviceSubTotal} 次，"
                      + $"分布在 {deviceSubCounts.Count} 个不同类型（集中度仅 {concentration:P0}）；未闭环 {row.OpenCount} 次";
                row.MaintenanceItems.Add(noDeviceSub ? "复核人员操作与来料质量" : "整机预防性维护");
                row.ActionText = noDeviceSub
                    ? "建议先查操作 SOP 与来料批次，而不是安排设备维修——把非设备问题当设备问题修，成本最高"
                    : "异常类型分散、更像整体状态劣化而非单点故障；建议安排整机预防性维护并建立该设备异常台账";
            }
            else
            {
                row.Level = tpmDueDays is not null && tpmDueDays < 0 ? InsightLevels.High : InsightLevels.Medium;
                row.ReasonText = tpmDueDays is not null && tpmDueDays < 0
                    ? $"异常频次尚未超阈值，但点检保养已超期 {Math.Abs(tpmDueDays!.Value)} 天未做"
                    : $"异常频次尚未超阈值，但{(daysSinceMaint is not null ? $"距上次保养已 {daysSinceMaint} 天" : "无保养记录")}"
                      + $"（阈值 {MesDatabaseInsightRules.MaintenanceStaleDays} 天）";
                row.MaintenanceItems.Add("按点检计划补做保养");
                row.ActionText = "异常尚未集中，但保养已到期限，建议本周内补做"
                              + $"{(tpmDueDays is not null && tpmDueDays < 0 ? "超期的点检保养" : "例行保养")}，避免转入高频异常";
            }

            result.Add(row);
        }

        return result
            .OrderByDescending(r => InsightLevels.Weight(r.Level))
            .ThenByDescending(r => r.AbnormalCount)
            .ToList();
    }

    /// <summary>设备编码里带线体（L1/L2/L3）时提取出来，用于归类与展示</summary>
    private static string LineOfEquipmentCode(string code)
    {
        var parts = code.Split('-');
        return parts.Length >= 2 && parts[0].Length <= 2 && char.IsLetter(parts[0][0])
            ? parts[0].ToUpperInvariant()
            : "";
    }

    /// <summary>按异常子类映射建议的预防性维护项目</summary>
    private static List<string> MaintenanceForSubType(string subType) => subType switch
    {
        "精度漂移" => new List<string> { "精度校准", "导轨与丝杠间隙检查", "传感器标定" },
        "夹具异常" => new List<string> { "夹具定位销检查与更换", "夹紧力校验" },
        "传动异常" => new List<string> { "传动机构润滑与检查", "皮带/链条张紧度调整" },
        "报警频繁" => new List<string> { "线路与接插件检查", "报警阈值复核", "电源与信号滤波检查" },
        "设备故障停机" => new List<string> { "整机故障诊断", "易损件更换" },
        _ => new List<string> { "按点检计划做全面检查" }
    };

    // ========================================================================
    // ③ 呆滞料与资金占压
    //
    // 判据：库存 − 未来需求 − 安全库存 > 0 的部分，就是**占着钱但用不掉**的部分。
    // 未来需求来自跨工单资材占用汇总；安全库存保留是必要的，不算呆滞。
    // ========================================================================
    public static List<SlowMovingInsightRow> AnalyzeSlowMoving(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;

        var result = new List<SlowMovingInsightRow>();

        // 按物料汇总当前可用库存（排除待检 / 退货区，与齐套口径一致）
        var stockByMat = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in WmsStocks)
        {
            if (!IsUsableLocation(s.Location)) continue;
            stockByMat[s.MaterialCode] = stockByMat.GetValueOrDefault(s.MaterialCode) + s.Quantity;
        }

        foreach (var agg in AggregateMaterialOccupancy())
        {
            var stock = stockByMat.GetValueOrDefault(agg.MaterialCode);
            if (stock <= MesDatabaseInsightRules.SlowMovingIdleThreshold) continue;

            var row = new SlowMovingInsightRow
            {
                MaterialCode = agg.MaterialCode,
                MaterialName = agg.MaterialName,
                Unit = agg.Unit,
                Category = agg.MaterialType,
                StockQty = Math.Round(stock, 2),
                FutureDemand = Math.Round(agg.TotalOccupied, 2),
                SafetyStock = FindWmsMaterial(agg.MaterialCode)?.SafetyStock ?? 0
            };

            var idle = stock - agg.TotalOccupied - row.SafetyStock;
            if (idle <= MesDatabaseInsightRules.SlowMovingIdleThreshold) continue;

            row.Grade = agg.TotalOccupied <= 1e-9 ? SlowMovingGrades.Idle : SlowMovingGrades.Slow;
            row.Level = agg.TotalOccupied <= 1e-9
                ? InsightLevels.High                     // 一点需求都没有，是纯占压
                : idle / stock >= 0.5 ? InsightLevels.Medium : InsightLevels.Low;
            row.ReasonText =
                $"当前库存 {stock:0.##} {row.Unit}，未来工单需求仅 {agg.TotalOccupied:0.##} {row.Unit}"
                + $"（{agg.WorkOrderCount} 张在手工单），安全库存 {row.SafetyStock:0.##} {row.Unit}"
                + $"；超出可用部分 {idle:0.##} {row.Unit} 属资金占压";
            row.ActionText = agg.TotalOccupied <= 1e-9
                ? $"近 {MesDatabaseInsightRules.SlowMovingWindowDays} 天无任何工单需求，建议核查是否有替代料或改用其它牌号；确认无用后走呆滞料处置"
                : "库存远超未来需求，建议下调后续补货量、暂停该物料的常规补货，必要时与供应商协商退换";

            result.Add(row);
        }

        return result
            .OrderByDescending(r => InsightLevels.Weight(r.Level))
            .ThenByDescending(r => r.IdleQty)
            .ToList();
    }
}