namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 能源管理（EMS）—— 采集层模型
//
// 三层计量点（对应"全厂核算 / 分线体核算 / 设备级"三种组织边界，由用户自选）：
//   Scope=Plant  厂级总表（全厂核算用）
//   Scope=Line   线体级（分线体核算用）
//   Scope=Device 设备级（精确到单台设备）
//
// 为什么读数按"账期"而不是"时间戳"：能源抄表天然是周期性的（月/日），
// 用账期做主键才能做 upsert —— 每次启动补生成新账期，已存在的不重复，
// 否则数据会随启动次数线性堆积。
// ============================================================================

/// <summary>组织边界 / 计量点层级</summary>
public static class EnergyScopes
{
    /// <summary>全厂</summary>
    public const string Plant = "全厂";

    /// <summary>线体</summary>
    public const string Line = "线体";

    /// <summary>设备</summary>
    public const string Device = "设备";

    public static readonly string[] All = { Plant, Line, Device };
}

/// <summary>能源种类</summary>
public static class EnergyTypes
{
    public const string Electricity = "电";
    public const string Water = "水";
    public const string Gas = "燃气";
    public const string Steam = "蒸汽";

    public static readonly string[] All = { Electricity, Water, Gas, Steam };

    /// <summary>各能源的计量单位</summary>
    public static string UnitOf(string type) => type switch
    {
        Electricity => "kWh",
        Water => "t",
        Gas => "Nm³",
        Steam => "t",
        _ => ""
    };

    /// <summary>演示用的单位单价（仅用于估算成本，不是真实报价）</summary>
    public static double DemoPriceOf(string type) => type switch
    {
        Electricity => 0.68,
        Water => 4.20,
        Gas => 3.60,
        Steam => 28.0,
        _ => 0
    };
}

/// <summary>能源种类定义（含默认排放因子引用，因子在阶段二细化）</summary>
public class EnergyTypeDef
{
    public string TypeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Remark { get; set; } = "";

    public string UnitText => Unit.Length > 0 ? Unit : "—";
}

/// <summary>
/// 计量点：某个位置对某种能源的计量。
/// 唯一键 = MeterId；定位某次读数用 (MeterId, Period)。
/// </summary>
public class EnergyMeter
{
    public string MeterId { get; set; } = "";

    /// <summary>能源种类</summary>
    public string TypeId { get; set; } = "";

    /// <summary>能源名称（冗余，便于展示）</summary>
    public string TypeName { get; set; } = "";

    public string Unit { get; set; } = "";

    /// <summary>组织边界：全厂 / 线体 / 设备</summary>
    public string Scope { get; set; } = EnergyScopes.Line;

    /// <summary>区域（工序段），与 Equipment.Section 同口径</summary>
    public string Section { get; set; } = "";

    /// <summary>所属线体（L1/L2/L3）</summary>
    public string Line { get; set; } = "";

    /// <summary>设备编码（Scope=Device 时必填，与 Equipment.Code 对应）</summary>
    public string EquipmentCode { get; set; } = "";

    /// <summary>设备名称（冗余，便于展示）</summary>
    public string EquipmentName { get; set; } = "";

    /// <summary>安装位置</summary>
    public string Location { get; set; } = "";

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>展示名：全厂/线体用位置，设备级用设备名</summary>
    public string DisplayName => Scope == EnergyScopes.Device && EquipmentName.Length > 0
        ? EquipmentName
        : Scope == EnergyScopes.Plant ? "全厂总表"
        : Line.Length > 0 ? $"{Line} {Section}".Trim() : Section;

    public string ScopeText => Scope;

    /// <summary>线体（全厂总表没有线体，显式说"—"而不是留空白让人以为出错）</summary>
    public string LineText => Line.Length > 0 ? Line : "—";

    public string EquipmentCodeText => EquipmentCode.Length > 0 ? EquipmentCode : "—";
    public string EquipmentNameText => EquipmentName.Length > 0 ? EquipmentName : "—";
    public string SectionText => Section.Length > 0 ? Section : "—";
    public string EnabledText => Enabled ? "启用" : "停用";
}

/// <summary>
/// 能耗读数：某计量点在某账期的消耗量。
/// 唯一键 = (MeterId, Period)；Period 形如 2026-09（月账期）。
/// </summary>
public class EnergyReading
{
    public string MeterId { get; set; } = "";

    /// <summary>账期，形如 2026-09</summary>
    public string Period { get; set; } = "";

    /// <summary>消耗量</summary>
    public double Value { get; set; }

    /// <summary>数据来源：模拟 / 人工录入 / 导入</summary>
    public string Source { get; set; } = "模拟";

    public string EnteredBy { get; set; } = "系统";

    public DateTime RecordedAt { get; set; } = DateTime.Now;

    public string Remark { get; set; } = "";

    // ---- 冗余展示字段（由查询层填充，避免界面反复 join）----
    public string TypeId { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Section { get; set; } = "";
    public string Line { get; set; } = "";
    public string EquipmentCode { get; set; } = "";
    public string EquipmentName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PeriodText => Period.Length == 7 ? $"{Period[..4]} 年 {Period[5..7]} 月" : Period;
    public string RecordedAtText => RecordedAt.ToString("yyyy-MM-dd HH:mm");
    public string ScopeText => Scope.Length > 0 ? Scope : "—";
    public string LineText => Line.Length > 0 ? Line : "—";
    public string EquipmentCodeText => EquipmentCode.Length > 0 ? EquipmentCode : "—";
    public string EquipmentNameText => EquipmentName.Length > 0 ? EquipmentName : "—";
    public string SectionText => Section.Length > 0 ? Section : "—";
    public string DisplayNameText => DisplayName.Length > 0 ? DisplayName : "—";
    public string ValueText => $"{Value:0.##} {Unit}";
}

/// <summary>能耗汇总行（按维度分组后的结果）</summary>
public class EnergySummaryRow
{
    public string Dimension { get; set; } = "";      // 分组维度值（月份/线体/能源/设备）
    public string SubDimension { get; set; } = "";   // 次级分组（如 月份 + 能源）
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public string Source { get; set; } = "模拟";

    public string ValueText => $"{Value:0.##} {Unit}";

    /// <summary>估算成本（演示用，单价取 EnergyTypes.DemoPriceOf）</summary>
    public double EstCost => Value * EnergyTypes.DemoPriceOf(Unit switch
    {
        "kWh" => EnergyTypes.Electricity,
        "t" => Unit == "t" ? EnergyTypes.Water : EnergyTypes.Steam,
        "Nm³" => EnergyTypes.Gas,
        _ => ""
    });

    public string EstCostText => EstCost <= 0 ? "—" : $"¥ {EstCost:0.##}";
}