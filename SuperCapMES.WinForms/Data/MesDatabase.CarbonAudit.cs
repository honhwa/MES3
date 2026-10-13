using System.Text;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 碳核查支撑 —— 台账存档 / 报告生成 / 清单导出
//
// 三条硬要求：
//   ① 台账只追加、不改写 —— 历史核算结果不能被后续覆盖（否则追溯链断）
//   ② 报告必须含：边界、账期、总量、分范围汇总、逐项排放源清单、因子版本与来源、免责
//   ③ 报告里每个数字可追溯：计量点 → 读数(活动数据) → 因子编号 → 排放量
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>碳核算台账（只追加）</summary>
    public static List<CarbonAuditTrail> CarbonAudits { get; } = new();

    private static int _auditSeq;

    /// <summary>
    /// 把一次核算结果存档为台账快照。
    /// **只追加**：同一份核算存多少次就产生多少条快照（审计留痕），不会覆盖旧的。
    /// </summary>
    public static (bool Ok, string Message, string AuditNo) SaveCarbonAudit(
        CarbonInventory inv, string note = "", string? archivedBy = null, DateTime? at = null)
    {
        if (inv is null) return (false, "没有可存档的核算结果。", "");
        if (inv.Items.Count == 0) return (false, "核算结果为空（无排放源清单），不予存档。", "");

        var now = at ?? DateTime.Now;
        var no = $"AUD-{now:yyyyMMdd}-{++_auditSeq:000}";

        // 深拷贝快照：避免后续同一 inv 对象被改动影响已存档内容
        var snapshot = new CarbonInventory
        {
            InventoryId = inv.InventoryId,
            Boundary = inv.Boundary, BoundaryValue = inv.BoundaryValue,
            PeriodFrom = inv.PeriodFrom, PeriodTo = inv.PeriodTo,
            TotalKg = inv.TotalKg, CalculatedAt = inv.CalculatedAt,
            Disclaimer = inv.Disclaimer,
            Scopes = inv.Scopes.Select(s => new CarbonScopeSummary
            {
                Scope = s.Scope, EmissionKg = s.EmissionKg, ItemCount = s.ItemCount, Ratio = s.Ratio
            }).ToList(),
            Activity = inv.Activity.Select(a => new EnergySummaryRow
            {
                Dimension = a.Dimension, SubDimension = a.SubDimension,
                Value = a.Value, Unit = a.Unit, Source = a.Source
            }).ToList(),
            FactorVersions = inv.FactorVersions.Select(f => new EmissionFactor
            {
                FactorId = f.FactorId, TypeId = f.TypeId, TypeName = f.TypeName,
                Scope = f.Scope, Value = f.Value, Unit = f.Unit, Source = f.Source,
                EffectiveYear = f.EffectiveYear, IsActive = f.IsActive, CreatedAt = f.CreatedAt
            }).ToList(),
            Items = inv.Items.Select(i => new CarbonSourceItem
            {
                MeterId = i.MeterId, MeterName = i.MeterName, TypeName = i.TypeName, Unit = i.Unit,
                MeterScope = i.MeterScope, Section = i.Section, Line = i.Line,
                EquipmentCode = i.EquipmentCode, EquipmentName = i.EquipmentName,
                Scope = i.Scope, ActivityData = i.ActivityData, FactorValue = i.FactorValue,
                FactorUnit = i.FactorUnit, FactorId = i.FactorId, FactorSource = i.FactorSource,
                FactorYear = i.FactorYear, EmissionKg = i.EmissionKg, PeriodCount = i.PeriodCount
            }).ToList()
        };

        CarbonAudits.Add(new CarbonAuditTrail
        {
            AuditNo = no,
            InventoryId = inv.InventoryId,
            Boundary = inv.Boundary, BoundaryValue = inv.BoundaryValue,
            PeriodFrom = inv.PeriodFrom, PeriodTo = inv.PeriodTo,
            TotalKg = inv.TotalKg,
            Note = (note ?? "").Trim(),
            ArchivedBy = (archoredByHelper(archivedBy)),
            ArchivedAt = now,
            Snapshot = snapshot,
            Disclaimer = inv.Disclaimer
        });

        return (true, $"已存档为台账 {no}（{inv.ItemCount} 项排放源，总排放 {inv.TotalKg / 1000:0.####} tCO2e）。", no);
    }

    private static string archoredByHelper(string? by) => (by ?? "系统").Trim() is { Length: > 0 } v ? v : "系统";

    /// <summary>台账列表（默认按存档时间倒序）。</summary>
    public static List<CarbonAuditTrail> GetCarbonAudits(string? boundary = null)
    {
        var b = (boundary ?? "").Trim();
        return CarbonAudits
            .Where(a => b.Length == 0 || string.Equals(a.Boundary, b, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.ArchivedAt)
            .ThenByDescending(a => a.AuditNo)
            .ToList();
    }

    /// <summary>按台账号取快照。</summary>
    public static CarbonAuditTrail? FindCarbonAudit(string auditNo)
        => CarbonAudits.FirstOrDefault(a => string.Equals(a.AuditNo, auditNo?.Trim() ?? "",
            StringComparison.OrdinalIgnoreCase));

    // ========================================================================
    // 报告生成（Markdown）
    // ========================================================================

    /// <summary>
    /// 生成碳核查报告（Markdown）。内容覆盖核查必需要素：
    /// 核算边界 / 账期 / 总量 / 分范围汇总 / 逐项排放源清单 / 因子版本与来源 / 免责。
    /// </summary>
    public static string BuildCarbonReport(CarbonAuditTrail audit, CarbonReportMeta? meta = null)
    {
        var m = meta ?? new CarbonReportMeta();
        var inv = audit.Snapshot;
        var sb = new StringBuilder();

        sb.AppendLine($"# {m.Title}");
        sb.AppendLine();
        sb.AppendLine($"> 组织：{m.Organization}　|　核算依据：{m.Standard}");
        sb.AppendLine($"> 台账号：**{audit.AuditNo}**　|　核算单号：{audit.InventoryId}");
        sb.AppendLine($"> 存档时间：{audit.ArchivedAtText}　|　存档人：{audit.ArchivedBy}"
                      + (audit.Note.Length > 0 ? $"　|　存档说明：{audit.Note}" : ""));
        if (m.PreparedBy.Length > 0) sb.AppendLine($"> 编制人：{m.PreparedBy}");
        sb.AppendLine();

        // 一、核算概要
        sb.AppendLine("## 一、核算概要");
        sb.AppendLine();
        sb.AppendLine("| 项目 | 内容 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 核算边界 | {audit.BoundaryText} |");
        sb.AppendLine($"| 核算期间 | {audit.PeriodText} |");
        sb.AppendLine($"| 排放源数量 | {inv.Items.Count} 项（其中 {inv.Items.Count(i => i.FactorId.Length > 0)} 项已匹配因子） |");
        sb.AppendLine($"| **总排放量** | **{audit.TotalKg / 1000:0.####} tCO2e** |");
        sb.AppendLine();
        if (inv.Activity.Count > 0)
        {
            sb.AppendLine("**活动数据汇总**（按能源分行，不同单位不合计）：");
            sb.AppendLine();
            sb.AppendLine("| 能源 | 消耗量 |");
            sb.AppendLine("|---|---|");
            foreach (var a in inv.Activity) sb.AppendLine($"| {a.Dimension} | {a.ValueText} |");
            sb.AppendLine();
        }

        // 二、分范围汇总
        sb.AppendLine("## 二、分范围汇总");
        sb.AppendLine();
        sb.AppendLine("| 排放范围 | 排放量（tCO2e） | 占比 | 计量点数 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var s in inv.Scopes) sb.AppendLine($"| {s.ScopeText} | {s.EmissionKg / 1000:0.####} | {s.RatioText} | {s.ItemCount} |");
        sb.AppendLine();

        // 三、排放源清单（核查核心）
        sb.AppendLine("## 三、排放源清单");
        sb.AppendLine();
        sb.AppendLine(CarbonReportDisclaimers.TraceNote);
        sb.AppendLine();
        sb.AppendLine("| # | 排放范围 | 计量点 | 位置/设备 | 能源 | 活动数据 | 排放因子 | 因子来源 | 排放量（kgCO2e） |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var idx = 0;
        foreach (var i in inv.Items)
        {
            idx++;
            var where = i.MeterScope == EnergyScopes.Device
                ? $"{i.EquipmentCode} {i.EquipmentName}".Trim()
                : i.Line.Length > 0 ? $"{i.Line} {i.Section}".Trim() : i.Section;
            sb.AppendLine($"| {idx} | {i.ScopeText} | {i.MeterId} | {where} | {i.TypeName} | {i.ActivityText} | "
                          + $"{(i.FactorId.Length > 0 ? i.FactorText : "无可用因子")} | {i.FactorSource} | {i.EmissionKg:0.###} |");
        }
        sb.AppendLine();

        // 四、排放因子版本
        sb.AppendLine("## 四、本次使用的排放因子");
        sb.AppendLine();
        sb.AppendLine("| 因子编号 | 能源 | 范围 | 因子值 | 生效年份 | 来源 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var f in inv.FactorVersions)
            sb.AppendLine($"| {f.FactorId} | {f.TypeName} | {f.ScopeText} | {f.ValueText} | {f.YearText} | {f.Source} |");
        sb.AppendLine();

        // 五、计算口径与免责
        sb.AppendLine("## 五、计算口径与免责说明");
        sb.AppendLine();
        sb.AppendLine("**计算公式**：排放量（kgCO2e）= 活动数据（台账读数）× 排放因子（kgCO2e/单位）");
        sb.AppendLine();
        sb.AppendLine("**因子匹配规则**：按「排放因子生效年份 ≤ 账期年份」取最新一条启用的因子；"
                      + "因此为新年份新增因子不会改变历史期间的核算结果，保证结果可复现。");
        sb.AppendLine();
        sb.AppendLine($"> ⚠️ {CarbonReportDisclaimers.Header}");
        sb.AppendLine();
        sb.AppendLine($"> ⚠️ {audit.Disclaimer}");
        if (m.Remark.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**备注**：{m.Remark}");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"报告由 MES 系统自动生成（台账号 {audit.AuditNo}）；数据取自本地台账实时计算，可复算。");
        return sb.ToString();
    }

    /// <summary>
    /// 导出排放源清单为 CSV（UTF-8 带 BOM，Excel 直接打开不乱码）。
    /// </summary>
    public static (bool Ok, string Message) ExportCarbonAuditCsv(CarbonAuditTrail audit, string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return (false, "未指定导出路径。");
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var inv = audit.Snapshot;
            var sb = new StringBuilder();
            sb.AppendLine("排放源清单,,,,,,,,");
            sb.AppendLine($"台账号,{audit.AuditNo},核算单号,{audit.InventoryId},,,");
            sb.AppendLine($"核算边界,{audit.BoundaryText},核算期间,{audit.PeriodText},,");
            sb.AppendLine($"总排放量(tCO2e),{audit.TotalKg / 1000:0.####},,,");
            sb.AppendLine();
            sb.AppendLine("序号,排放范围,计量点编号,位置/设备,能源,活动数据,单位,排放因子,因子来源,排放量(kgCO2e)");
            var idx = 0;
            foreach (var i in inv.Items)
            {
                idx++;
                var where = i.MeterScope == EnergyScopes.Device
                    ? $"{i.EquipmentCode} {i.EquipmentName}".Trim()
                    : i.Line.Length > 0 ? $"{i.Line} {i.Section}".Trim() : i.Section;
                sb.AppendLine($"{idx},{i.ScopeText},{i.MeterId},{where},{i.TypeName},"
                              + $"{i.ActivityData:0.##},{i.Unit},"
                              + $"{(i.FactorId.Length > 0 ? i.FactorValue.ToString("0.####") : "无")},"
                              + $"{i.FactorSource},{i.EmissionKg:0.###}");
            }
            sb.AppendLine();
            sb.AppendLine($"免责,{CarbonReportDisclaimers.Header}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            var size = new FileInfo(path).Length;
            return (true, $"已导出到 {path}（{size:N0} 字节，{inv.Items.Count} 项排放源）。");
        }
        catch (Exception ex)
        {
            return (false, "导出失败：" + ex.Message);
        }
    }

    /// <summary>默认报告文件名。</summary>
    public static string DefaultReportFileName(CarbonAuditTrail audit)
        => $"碳核查报告_{audit.AuditNo}.md";
}