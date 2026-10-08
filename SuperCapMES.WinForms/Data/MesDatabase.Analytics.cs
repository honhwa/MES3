using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 六项 MES 分析 —— 计算层（**全部纯查询，不写任何数据**）
//
// 复用既有资产，不另立一套：
//   产能口径 → SchedulingRules（DailyCapacityHours / DefaultUnitHours / DefaultYield）
//              单位工时与良率用 ErpWorkReports 实绩，与 RecommendSchedule 同一算法
//   过程能力 → SpcCalculator.ComputeWithVersions（**直接拿已有 Cpk，不重算**）
//
// 六项共用约定：结果必带「判断依据」+「建议动作」+「数据来源」；
//             预测类标免责；归因类不说"根因"（需人工验证）。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>一次跑完六项（页面按子页展示）</summary>
    public static AnalyticsReport BuildAnalyticsReport(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var r = new AnalyticsReport { GeneratedAt = at };
        r.Delivery = AnalyzeDeliveryRisk(at);
        r.RootCause = AnalyzeQualityRootCause();
        r.SpcTuning = AnalyzeSpcTuningAdvice(at);
        r.SkillSchedule = AnalyzeSkillVsSchedule(at);
        r.DuplicateFai = AnalyzeDuplicateFai();
        r.AbnormalCycle = AnalyzeAbnormalCycleTime(at);

        // 覆盖度说明：质量根因那一项必须说清"只覆盖有实绩的部分"
        var linked = ProcessActuals.Select(p => p.CellSn).Distinct().Count();
        r.CoverageNotes.Add(
            $"质量交叉仅覆盖有工序实绩的 {linked} 只电芯（共 {Cells.Count} 只），未覆盖部分不参与统计；材料批次维度样本极薄，只展示不排序。");
        r.CoverageNotes.Add("交期风险为基于当前负荷的推算：" + AnalyticsRules.DeliveryDisclaimer);
        return r;
    }

    // ========================================================================
    // ① 工单交期风险预警
    //   剩余待产量 × 单位工时 ÷ 良率 = 剩余所需产能 → 按 16h/天折算日历天数，
    //   再与「可开工日 → 交期日」的可用天数比较。
    // ========================================================================
    public static List<DeliveryRiskRow> AnalyzeDeliveryRisk(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var rows = new List<DeliveryRiskRow>();

        // 单位工时 / 良率：与产能引擎同算法（ErpWorkReports 实绩优先，无实绩用默认值）
        var (unitHours, yieldRate, fromHistory) = ResolveUnitHoursAndYield();

        foreach (var wo in WorkOrders.Where(IsOpenWorkOrder))
        {
            var remain = Math.Max(0, wo.PlanQty - wo.FinishedQty);
            if (remain <= 0) continue;   // 干完了就没有交期风险

            var y = yieldRate <= 0 ? SchedulingRules.DefaultYield : yieldRate;
            var requiredHours = remain * unitHours / y;
            var requiredDays = requiredHours / SchedulingRules.DailyCapacityHours;

            var start = wo.StartDate > at ? wo.StartDate.Date : at.Date;
            var availableDays = (wo.DueDate.Date - start).TotalDays;
            var gapHours = Math.Max(0, requiredHours - availableDays * SchedulingRules.DailyCapacityHours);

            var row = new DeliveryRiskRow
            {
                WoNo = wo.WoNo,
                ProductModel = wo.ProductModel,
                Line = wo.Line ?? "",
                PlanQty = wo.PlanQty,
                FinishedQty = wo.FinishedQty,
                RemainingQty = remain,
                UnitHours = unitHours,
                RequiredHours = requiredHours,
                RequiredDays = requiredDays,
                AvailableDays = availableDays,
                GapHours = gapHours,
                AdviseDays = Math.Max(0, requiredDays - availableDays),
                Evidence = "数据来源：MesDatabase.WorkOrders（剩余量/交期）"
                           + $" + SchedulingRules.DailyCapacityHours({SchedulingRules.DailyCapacityHours}h) + 单位工时"
                           + (fromHistory ? "（ErpWorkReports 实绩）" : "（SchedulingRules.DefaultUnitHours 默认值）")
                           + $" + 良率 {(yieldRate * 100):0.#}%"
            };

            if (availableDays < 0)
            {
                row.Risk = DeliveryRisk.Overdue;
                row.ReasonText = $"交期 {wo.DueDate:yyyy-MM-dd} 已过 {-availableDays:0} 天，仍有 {remain} 只未完成"
                              + $"（需 {requiredHours:0.#} h 产能，缺口 {gapHours:0.#} h）";
                row.ActionText = "立即上报：要么重新协商交期，要么安排加班/外协补足产能";
            }
            else if (availableDays < requiredDays * AnalyticsRules.DeliveryWarnRatio)
            {
                row.Risk = DeliveryRisk.Warn;
                row.ReasonText = $"余量不足：需 {requiredDays:0.#} 天、只剩 {availableDays:0.#} 天"
                              + $"（安全系数 {AnalyticsRules.DeliveryWarnRatio:0.##}）；产能缺口 {gapHours:0.#} h";
                row.ActionText = gapHours > 1e-6
                    ? $"建议提前 {Math.Ceiling(row.AdviseDays)} 天开工，或补 {gapHours:0.#} h 产能；并评估是否拆单/分流到其它线体"
                    : "产能够但余量薄，建议提前开工并加密跟进";
            }
            else
            {
                row.Risk = DeliveryRisk.Normal;
                row.ReasonText = $"按期可完成：需 {requiredDays:0.#} 天、可用 {availableDays:0.#} 天，余量 {availableDays - requiredDays:0.#} 天";
                row.ActionText = "按当前节奏推进即可";
            }

            row.ReasonText += "　※ " + AnalyticsRules.DeliveryDisclaimer;
            rows.Add(row);
        }

        return rows
            .OrderBy(r => DeliveryRisk.All.ToList().IndexOf(r.Risk))
            .ThenByDescending(r => r.GapHours)
            .ToList();
    }

    /// <summary>
    /// 单位工时与良率：<b>与产能引擎同一算法</b>（ErpWorkReports 有工时用实绩，否则用默认值）。
    /// 不另立口径，否则排产页和本页会算出两个数。
    /// </summary>
    private static (double UnitHours, double Yield, bool FromHistory) ResolveUnitHoursAndYield()
    {
        var reports = ErpWorkReports.Where(r => r.GoodQty > 0).ToList();
        if (reports.Count == 0)
            return (SchedulingRules.DefaultUnitHours, SchedulingRules.DefaultYield, false);

        var totalFinish = reports.Sum(r => r.GoodQty);
        var totalPlan = reports.Sum(r => r.OutputQty);
        var y = totalPlan > 0 ? (double)totalFinish / totalPlan : SchedulingRules.DefaultYield;
        y = Math.Clamp(y, 0.5, 1.0);   // 防种子数据异常把产能算成负数或爆炸

        var withHours = reports.Where(r => r.WorkHours > 0).ToList();
        var uh = withHours.Count > 0 && totalFinish > 0
            ? withHours.Sum(r => r.WorkHours) / totalFinish
            : SchedulingRules.DefaultUnitHours;
        return (uh > 0 ? uh : SchedulingRules.DefaultUnitHours, y, withHours.Count > 0);
    }

    // ========================================================================
    // ② 质量根因交叉（工序 × 设备 × 材料批次）
    //
    // ⚠ TestRecord 本身没有工序/设备/批次字段，全部靠 CellSn 反查 ProcessActuals 关联。
    // ⚠ MaterialLots 仅 10 条且 UsedFor 只指向个别 SN → **材料批次维度只展示不排序**。
    // ⚠ 只说"集中项"，不说"根因"——根因需人工验证。
    // ========================================================================
    public static List<QualityCrossRow> AnalyzeQualityRootCause()
    {
        var rows = new List<QualityCrossRow>();

        // SN → 该电芯的最后一道工序实绩（出问题通常发生在最后几道）
        var byCell = ProcessActuals
            .GroupBy(p => p.CellSn, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Time).ToList(), StringComparer.OrdinalIgnoreCase);

        // 材料批次 → 用了哪些 SN（样本极薄，只作展示）
        var lotByCell = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lot in Materials)
        {
            if (string.IsNullOrWhiteSpace(lot.UsedFor)) continue;
            foreach (var sn in lot.UsedFor.Split(',', StringSplitOptions.RemoveEmptyEntries))
                lotByCell[sn.Trim()] = lot.LotNo;
        }

        // 设备停机时长（告警累计）
        var downtimeByDevice = DeviceAlarms
            .GroupBy(a => a.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (double)g.Sum(a => a.DurationMin), StringComparer.OrdinalIgnoreCase);

        var pdKey = new Dictionary<string, (string Process, string Equipment)>();
        var pdsKey = new Dictionary<string, (string Process, string Equipment, string Shift)>();
        var lotKey = new Dictionary<string, int>();

        foreach (var t in TestRecords)
        {
            if (!byCell.TryGetValue(t.CellSn, out var steps) || steps.Count == 0)
            {
                // 无实绩的电芯不参与交叉（口径：仅覆盖有实绩的部分）
                if (lotByCell.ContainsKey(t.CellSn)) lotKey[lotByCell[t.CellSn]] = lotKey.GetValueOrDefault(lotByCell[t.CellSn]) + 1;
                continue;
            }

            var last = steps[^1];
            var pd = (last.ProcessName, last.Equipment);
            var pds = (last.ProcessName, last.Equipment, last.Shift);

            pdKey[last.ProcessName + "|" + last.Equipment] = pd;
            pdsKey[last.ProcessName + "|" + last.Equipment + "|" + last.Shift] = pds;
            if (lotByCell.TryGetValue(t.CellSn, out var lotNo))
                lotKey[lotNo] = lotKey.GetValueOrDefault(lotNo) + 1;

            var isBad = !t.Passed;
            if (isBad)
            {
                AddCross(rows, RootCauseGroupKind.ProcessDevice, $"{last.ProcessName} / {last.Equipment}",
                    last.ProcessName, last.Equipment, "", isBad, downtimeByDevice);
                AddCross(rows, RootCauseGroupKind.ProcessDeviceShift,
                    $"{last.ProcessName} / {last.Equipment} / {last.Shift}",
                    last.ProcessName, last.Equipment, last.Shift, isBad, downtimeByDevice);
            }
            else
            {
                // 合格样本只累加分母（要算不良率就必须有全量样本）
                BumpTotal(rows, RootCauseGroupKind.ProcessDevice, $"{last.ProcessName} / {last.Equipment}");
                BumpTotal(rows, RootCauseGroupKind.ProcessDeviceShift,
                    $"{last.ProcessName} / {last.Equipment} / {last.Shift}");
            }
        }

        // 材料批次维度：只展示，样本量必须显式标出
        foreach (var (lotNo, total) in lotKey)
        {
            var bad = CountBadByLot(lotNo, byCell, lotByCell);
            rows.Add(new QualityCrossRow
            {
                GroupKind = RootCauseGroupKind.MaterialLot,
                Dimension = lotNo,
                LotNo = lotNo,
                BadCount = bad,
                TotalCount = total,
                BadRate = total == 0 ? 0 : bad / (double)total,
                EnoughSamples = total >= AnalyticsRules.RootCauseMinSamples,
                Confident = total >= AnalyticsRules.RootCauseConfidentSamples,
                Note = "材料批次维度样本极薄（材料批次台账仅覆盖个别电芯），只作展示、不参与主排序"
            });
        }

        // 样本门槛：不够的不进主排序（避免 2/2=100% 的假象）
        foreach (var r in rows)
        {
            // ⚠ BadRate 是普通属性，增量累加后必须重算，否则不良率列会永远显示 0
            r.BadRate = r.TotalCount == 0 ? 0 : r.BadCount / (double)r.TotalCount;
            r.EnoughSamples = r.TotalCount >= AnalyticsRules.RootCauseMinSamples;
            r.Confident = r.TotalCount >= AnalyticsRules.RootCauseConfidentSamples;
            r.Note = r.GroupKind == RootCauseGroupKind.MaterialLot
                ? $"材料批次维度，样本仅 {r.TotalCount} 条，只作展示不排序"
                : !r.EnoughSamples
                    ? $"样本仅 {r.TotalCount} 条，低于 {AnalyticsRules.RootCauseMinSamples} 条门槛，不进主排序"
                    : r.Confident
                        ? $"样本 {r.TotalCount} 条，可下结论"
                        : $"样本 {r.TotalCount} 条，偏少仅供参考";
            r.ActionText = BuildRootCauseAction(r);
        }

        // 材料批次维度样本极薄，只展示不参与主排序 → 必须排在最后（OrderBy 升序，0 在前）
        return rows
            .OrderBy(r => r.GroupKind == RootCauseGroupKind.MaterialLot ? 1 : 0)
            .ThenBy(r => r.EnoughSamples ? 0 : 1)
            .ThenByDescending(r => r.BadRate)
            .ThenByDescending(r => r.TotalCount)
            .ToList();
    }

    private static void AddCross(List<QualityCrossRow> rows, string kind, string dim, string process,
        string equipment, string shift, bool isBad, Dictionary<string, double> downtime)
    {
        var r = rows.FirstOrDefault(x => x.GroupKind == kind && x.Dimension == dim);
        if (r is null)
        {
            r = new QualityCrossRow
            {
                GroupKind = kind, Dimension = dim, ProcessName = process,
                Equipment = equipment, Shift = shift,
                DowntimeMin = downtime.GetValueOrDefault(equipment)
            };
            rows.Add(r);
        }
        r.TotalCount++;
        if (isBad) r.BadCount++;
    }

    private static void BumpTotal(List<QualityCrossRow> rows, string kind, string dim)
    {
        var r = rows.FirstOrDefault(x => x.GroupKind == kind && x.Dimension == dim);
        if (r is not null) r.TotalCount++;
    }

    private static int CountBadByLot(string lotNo, Dictionary<string, List<ProcessActualRecord>> byCell,
        Dictionary<string, string> lotByCell)
    {
        var n = 0;
        foreach (var (sn, lot) in lotByCell)
        {
            if (lot != lotNo) continue;
            if (TestRecords.Any(t => t.CellSn == sn && !t.Passed)) n++;
        }
        return n;
    }

    private static string BuildRootCauseAction(QualityCrossRow r) => r.GroupKind switch
    {
        RootCauseGroupKind.MaterialLot => "如需按批次追责，先补齐材料批次与电芯的对应台账（当前样本不足）",
        _ when !r.EnoughSamples => "样本不足，先补充测试与实绩记录再判",
        _ => r.BadRate >= 0.5
            ? $"「{r.ProcessName} / {r.Equipment}」不良率偏高，建议现场核查该设备该工序，并查是否同一批来料"
            : $"「{r.ProcessName} / {r.Equipment}」为高频组合，建议列入巡检重点"
    };

    // ========================================================================
    // ③ SPC 调机建议（**复用已有 Cpk，不重算**）
    // ========================================================================
    public static List<SpcTuningRow> AnalyzeSpcTuningAdvice(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var rows = new List<SpcTuningRow>();

        foreach (var item in SpcItems)
        {
            var samples = SpcSamples.Where(s => s.ItemId == item.ItemId).ToList();
            var versions = SpcItemVersions.Where(v => v.ItemId == item.ItemId).ToList();
            if (samples.Count == 0) continue;

            // 复用统一计算器：它已经处理了"按生效版本取规格"这件事
            var res = SpcCalculator.ComputeWithVersions(samples, versions, at);
            if (double.IsNaN(res.Cpk)) continue;
            if (res.Cpk >= AnalyticsRules.SpcAdviceCpkLimit) continue;   // 能力良好不管它

            var mean = Math.Abs(res.Mean) < 1e-12 ? 0 : res.Mean;
            var cv = Math.Abs(mean) < 1e-12 ? 0 : res.Sigma / Math.Abs(mean);

            var row = new SpcTuningRow
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                Version = res.CurrentVersion,
                Cpk = res.Cpk,
                Mean = res.Mean,
                Sigma = res.Sigma,
                Usl = versions.FirstOrDefault(v => v.Version == res.CurrentVersion)?.Usl ?? double.NaN,
                Lsl = versions.FirstOrDefault(v => v.Version == res.CurrentVersion)?.Lsl ?? double.NaN,
                Cv = cv,
                CapabilityGrade = res.Cpk < 1.0 ? $"能力不足（Cpk {res.Cpk:0.00} < 1.00）"
                    : $"需关注（Cpk {res.Cpk:0.00}，介于 1.00 与 {AnalyticsRules.SpcAdviceCpkLimit:0.00} 之间）"
            };

            // 区分「波动大」与「均值偏」——两者的排查方向完全不同
            row.Diagnose = cv > AnalyticsRules.SpcCvThreshold
                ? $"波动大（CV {cv:P1} > {AnalyticsRules.SpcCvThreshold:P0}）→ 指向机械/来料一致性"
                : "波动可控（CV 低）→ 指向设定值或补偿参数";

            row.Directions.AddRange(DirectionsFor(item.ItemName, row.Diagnose.StartsWith("波动大")));

            // 预约生效的下一版本：提示即将改善，别让人重复调
            var pending = versions
                .Where(v => v.EffectiveFrom > at)
                .OrderBy(v => v.EffectiveFrom)
                .FirstOrDefault();
            row.PendingVersionText = pending is null ? ""
                : $"注意：参数 V{pending.Version} 已预约 {pending.EffectiveFrom:yyyy-MM-dd} 生效，届时 Cpk 可望回升，勿重复调机";

            row.Priority = res.Cpk < 1.0
                ? (row.Diagnose.StartsWith("波动大") ? SpcTuningPriority.P1 : SpcTuningPriority.P2)
                : SpcTuningPriority.P3;

            // 验证方式：不给验证标准的建议等于没给
            row.VerifyMethod = $"调整后连续采集 {AnalyticsRules.SpcVerifySamples} 个样本重算 Cpk"
                             + $" → Cpk ≥ {AnalyticsRules.SpcAdviceCpkLimit:0.00} 且无超规格点视为有效；"
                             + "未达标则说明判断有误，需工艺工程师重新定位";

            rows.Add(row);
        }

        return rows
            .OrderBy(r => SpcTuningPriority.All.ToList().IndexOf(r.Priority))
            .ThenBy(r => r.Cpk)
            .ToList();
    }

    /// <summary>按参数名给出具体检查项（不做泛泛的"检查设备"）</summary>
    private static List<string> DirectionsFor(string itemName, bool fluctuating)
    {
        var byName = itemName switch
        {
            "卷绕张力" => fluctuating
                ? new List<string> { "极片来料厚度一致性（占张力波动主因）", "张力传感器标定与零点漂移", "卷绕线速度匹配度" }
                : new List<string> { "张力闭环的补偿量是否偏置", "目标值设定是否考虑来料实际厚度" },
            "注液量" => fluctuating
                ? new List<string> { "泵管路压力波动与滤芯堵塞", "注液阀响应时序", "极壳毛刺/异物引起的阻滞" }
                : new List<string> { "定量泵标定量是否偏移", "补偿参数是否按实测重新标定" },
            _ => fluctuating
                ? new List<string> { "测量系统重复性（GR&R）", "设备运行一致性", "来料批次一致性" }
                : new List<string> { "设定值与规格中心的偏离量" }
        };
        byName.Add("以上为建议方向，须由工艺工程师现场验证后执行");
        return byName;
    }

    // ========================================================================
    // ④ 资质 × 排班匹配
    // ========================================================================
    public static List<SkillScheduleRow> AnalyzeSkillVsSchedule(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var rows = new List<SkillScheduleRow>();
        var certs = SkillCertificates.ToList();

        foreach (var u in Users.Where(x => x.Status == EmploymentStatus.Active))
        {
            var need = RequiredSkillsFor(u.Position, u.Dept);
            if (need.Length == 0) continue;

            var mine = certs.Where(c => c.EmpNo == u.EmpNo).ToList();
            foreach (var skill in need)
            {
                var cert = mine.FirstOrDefault(c => c.SkillItem == skill);
                if (cert is null)
                {
                    rows.Add(new SkillScheduleRow
                    {
                        EmpNo = u.EmpNo, EmpName = u.Name, Dept = u.Dept,
                        Position = u.Position, Crew = u.Crew, SkillItem = skill,
                        Kind = SkillConflictKind.Missing,
                        ActionText = $"在岗但无「{skill}」资质，建议调整排班或先完成培训取证"
                    });
                    continue;
                }

                if (cert.ExpiryDate is null) continue;   // 长期有效
                var days = (int)(cert.ExpiryDate.Value.Date - at.Date).TotalDays;
                if (days < 0)
                {
                    rows.Add(new SkillScheduleRow
                    {
                        EmpNo = u.EmpNo, EmpName = u.Name, Dept = u.Dept,
                        Position = u.Position, Crew = u.Crew, SkillItem = skill,
                        Kind = SkillConflictKind.Expired, ExpiryDate = cert.ExpiryDate, DaysToExpiry = days,
                        ActionText = $"「{skill}」已于 {cert.ExpiryDate:yyyy-MM-dd} 过期，不得独立顶岗，建议立即复训换证"
                    });
                }
                else if (days <= AnalyticsRules.SkillExpiringDays)
                {
                    rows.Add(new SkillScheduleRow
                    {
                        EmpNo = u.EmpNo, EmpName = u.Name, Dept = u.Dept,
                        Position = u.Position, Crew = u.Crew, SkillItem = skill,
                        Kind = SkillConflictKind.Expiring, ExpiryDate = cert.ExpiryDate, DaysToExpiry = days,
                        ActionText = $"「{skill}」{days} 天后到期，请提前安排复训换证，避免断档"
                    });
                }
            }
        }

        return rows
            .OrderBy(r => SkillConflictKind.All.ToList().IndexOf(r.Kind))
            .ThenBy(r => r.DaysToExpiry)
            .ToList();
    }

    /// <summary>岗位 → 所需资质（可配：换厂改这里即可）</summary>
    public static string[] RequiredSkillsFor(string? position, string? dept)
    {
        var p = (position ?? "").Trim();
        if (p.Contains("卷绕")) return new[] { SkillCatalogItems.Winding };
        if (p.Contains("注液")) return new[] { SkillCatalogItems.Filling };
        if (p.Contains("点检") || p.Contains("保养")) return new[] { SkillCatalogItems.Inspection };
        if (p.Contains("检验") || p.Contains("QC")) return new[] { SkillCatalogItems.Inspection };
        if (p.Contains("班长") || p.Contains("组长")) return new[] { SkillCatalogItems.Winding, SkillCatalogItems.Inspection };
        if ((dept ?? "").Contains("设备")) return new[] { SkillCatalogItems.Inspection };
        return Array.Empty<string>();
    }

    /// <summary>资质项名称（对齐 SkillCatalog 已有项，避免另起名字）</summary>
    public static class SkillCatalogItems
    {
        public const string Winding = "卷绕机操作资格";
        public const string Filling = "注液机操作资格";
        public const string Inspection = "设备点检资格";
    }

    // ========================================================================
    // ⑤ 首检重复送检提醒
    // ========================================================================
    public static List<DuplicateFaiRow> AnalyzeDuplicateFai()
    {
        var rows = new List<DuplicateFaiRow>();
        var done = FaiOrders
            .Where(f => f.Status == FaiStatus.Passed)
            .ToList();

        foreach (var g in done.GroupBy(f => f.ProductModel + "|" + f.WoNo, StringComparer.OrdinalIgnoreCase))
        {
            if (g.Count() < AnalyticsRules.DuplicateFaiTimes) continue;

            var ordered = g.OrderBy(f => f.InspectTime ?? f.CreatedTime).ToList();
            var first = ordered[0];
            var firstTime = first.InspectTime ?? first.CreatedTime;

            // 首检之后有没有参数变更？有变更则重新送检合理，不报警
            var changed = ProcessActuals.Any(p =>
                p.Time > firstTime &&
                !string.IsNullOrWhiteSpace(p.ParamName) &&
                !string.IsNullOrWhiteSpace(p.ParamValue));

            rows.Add(new DuplicateFaiRow
            {
                ProductModel = ordered[0].ProductModel,
                WoNo = ordered[0].WoNo,
                Times = ordered.Count,
                FaiNos = ordered.Select(f => f.FaiNo).ToList(),
                LastTime = ordered[^1].InspectTime ?? ordered[^1].CreatedTime,
                ParamChanged = changed,
                Reason = changed
                    ? $"同型号同工单已送检 {ordered.Count} 次，但首检后有工艺参数变更，重新送检属正常"
                    : $"同型号同工单已送检 {ordered.Count} 次，且首检后无参数变更，可能属重复送检",
                ActionText = changed
                    ? "参数有变更，重新送检属正常，无需处理；如变更未记录参数请补录"
                    : "参数未变却再次送检，建议先确认首检结论是否已被采纳；若首检无效应走首检作废流程而非重送"
            });
            _ = first;
        }

        return rows.OrderByDescending(r => r.Times).ToList();
    }

    // ========================================================================
    // ⑥ 异常闭环时长分析
    // ========================================================================
    public static List<AbnormalCycleRow> AnalyzeAbnormalCycleTime(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var rows = new List<AbnormalCycleRow>();

        foreach (var g in AbnormalRecords.GroupBy(a => a.Category + "|" + a.SubType))
        {
            var list = g.ToList();
            var cat = list[0].Category;
            var limit = AnalyticsRules.AbnormalCycleLimitHours.TryGetValue(cat, out var l) ? l : 72;

            // 闭环时长：取该异常最后一条流转时间；未闭环的不混进平均
            var durations = new List<double>();
            var open = 0; var overdue = 0;
            foreach (var a in list)
            {
                var hist = AbnormalHistories.Where(h => h.AbNo == a.AbNo).ToList();
                var close = hist.Where(h => h.Action == AbnormalAction.Close)
                    .OrderBy(h => h.Time).LastOrDefault();
                if (close is null) { open++; continue; }
                var hours = (close.Time - a.OccurTime).TotalHours;
                if (hours < 0) hours = 0;
                durations.Add(hours);
                if (hours > limit) overdue++;
            }

            var row = new AbnormalCycleRow
            {
                Category = cat,
                SubType = list[0].SubType,
                Count = list.Count,
                ClosedCount = durations.Count,
                StillOpenCount = open,
                MaxHours = durations.Count > 0 ? durations.Max() : 0,
                LimitHours = limit,
                OverdueCount = overdue,
                AvgHours = durations.Count > 0 ? durations.Average() : 0,
                // 异常场景下中位数更能代表"典型"耗时，均值容易被个别长尾拉偏
                MedianHours = durations.Count > 0
                    ? durations.OrderBy(x => x).ElementAt(durations.Count / 2) : 0
            };
            rows.Add(row);
        }

        // 标出最慢的环节——这是可执行结论
        var slowest = rows.Where(r => r.ClosedCount > 0).OrderByDescending(r => r.AvgHours).FirstOrDefault();
        if (slowest is not null) slowest.Slowest = true;

        foreach (var r in rows)
        {
            r.ActionText = r switch
            {
                { ClosedCount: 0 } => $"该类 {r.Count} 条异常**均未闭环**，需立即确认为何停滞",
                { OverdueCount: > 0, Slowest: true } =>
                    $"闭环最慢的环节（均值 {r.AvgText} > 阈值 {r.LimitText}），且 {r.OverdueCount} 条超时，建议专项梳理责任人与卡点",
                { OverdueCount: > 0 } => $"有 {r.OverdueCount} 条超过 {r.LimitText} 阈值，建议提醒责任人跟进",
                { StillOpenCount: > 0 } => $"尚有 {r.StillOpenCount} 条未闭环，均值 {r.AvgText}，请跟踪",
                _ => $"闭环效率正常（中位数 {r.MedianText}，阈值 {r.LimitText}）"
            };
        }

        return rows.OrderByDescending(r => r.AvgHours).ThenByDescending(r => r.Count).ToList();
    }
}
