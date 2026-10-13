using System.Text;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 碳核查支撑 —— 模型层
//
// 核查机构要看三样东西：**排放源清单、数据台账、可追溯性**。
// 所以核算结果不能只在屏幕上算一遍就丢，必须落成台账快照：
//   · 快照只追加、不改写（历史核算结果不能被后续覆盖）
//   · 快照必须带全上下文：边界、账期、总量、逐项明细、因子版本、免责说明
//   · 报告里的每个数字都要能追回"哪条读数 × 哪条因子"
// ============================================================================

/// <summary>
/// 一次碳核算的台账快照（只追加）。
/// 与 CarbonInventory 的区别：Inventory 是"算出来的一次结果"，
/// AuditTrail 是"留档备查的历史记录"，且带快照号与存档说明。
/// </summary>
public class CarbonAuditTrail
{
    /// <summary>台账号（快照号），形如 AUD-20261013-001</summary>
    public string AuditNo { get; set; } = "";

    /// <summary>核算单号（对应 CarbonInventory.InventoryId）</summary>
    public string InventoryId { get; set; } = "";

    /// <summary>核算边界：全厂 / 线体 / 设备</summary>
    public string Boundary { get; set; } = "";

    public string BoundaryValue { get; set; } = "";
    public string BoundaryText => Boundary == EnergyScopes.Plant
        ? "全厂（组织边界）"
        : Boundary == EnergyScopes.Line ? $"线体 {BoundaryValue}（项目边界）" : $"设备 {BoundaryValue}";

    public string PeriodFrom { get; set; } = "";
    public string PeriodTo { get; set; } = "";
    public string PeriodText => $"{PeriodFrom} ~ {PeriodTo}";

    /// <summary>总排放量（kgCO2e）</summary>
    public double TotalKg { get; set; }

    /// <summary>存档时的说明（谁存的、为什么存）</summary>
    public string Note { get; set; } = "";

    public string ArchivedBy { get; set; } = "";
    public DateTime ArchivedAt { get; set; } = DateTime.Now;

    /// <summary>存档时冻结的核算结果（含逐项明细与因子版本）</summary>
    public CarbonInventory Snapshot { get; set; } = new();

    /// <summary>免责说明（存档时一并冻结）</summary>
    public string Disclaimer { get; set; } = "";

    public string TotalTText => $"{TotalKg / 1000:0.####} tCO2e";
    public string ArchivedAtText => ArchivedAt.ToString("yyyy-MM-dd HH:mm");
    public int ItemCount => Snapshot.Items.Count;
    public string ItemCountText => $"{ItemCount} 项";
}

/// <summary>碳核查报告的元信息（生成报告时填写）</summary>
public class CarbonReportMeta
{
    /// <summary>报告标题（默认"温室气体排放核算报告"）</summary>
    public string Title { get; set; } = "温室气体排放核算报告";

    /// <summary>组织名称</summary>
    public string Organization { get; set; } = "中绿泰州超级电容工厂";

    /// <summary>核算依据（如 GB/T 32150 / ISO 14064-1）</summary>
    public string Standard { get; set; } = "GB/T 32150-2015《工业企业温室气体排放核算和报告通则》";

    /// <summary>编制人</summary>
    public string PreparedBy { get; set; } = "";

    /// <summary>备注</summary>
    public string Remark { get; set; } = "";
}

/// <summary>碳核查报告的免责与口径说明</summary>
public static class CarbonReportDisclaimers
{
    public const string Header =
        "本报告由 MES 系统根据台账读数自动生成，用于核查支撑。"
        + "报告中的排放因子为**模拟园区口径**，正式提交前必须替换为当地主管部门最新发布值，并留存来源文件。";

    public const string TraceNote =
        "报告中每一项排放源均可追溯：计量点编号 → 台账读数（活动数据）→ 排放因子编号与来源 → 排放量。";
}