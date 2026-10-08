using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 主动预警与辅助决策 —— 模型层
//
// 与既有「预警中心」的区别（这两者不是一回事，别混）：
//   预警中心：**被动列表**，把各模块"待处理事项"投影出来，等人去看（10 类 AlertItem）
//   主动预警：**给结论 + 给建议动作**，回答"这批料会不会废""这台设备要不要提前修""哪个建议立即处理"
//
// 三条设计红线：
//   ① **纯查询**：预警只算不改，绝不代人做处置决策（不下单、不改库存、不建保养单）
//   ② **阈值全部可配**：MesDatabaseInsightRules 里改，不用改代码逻辑
//   ③ **数字本地算**：预警里的每个数字都来自确定性计算，可复算、可审计
// ============================================================================

/// <summary>预警/决策的输出形式</summary>
public static class InsightOutputKind
{
    /// <summary>预警清单：按优先级排序的条目</summary>
    public const string List = "预警清单";

    /// <summary>归因分析：现象 → 可能原因 → 建议动作</summary>
    public const string Diagnosis = "归因分析";

    /// <summary>建议动作清单：可直接照做的操作项</summary>
    public const string Actions = "建议动作";

    public static readonly string[] All = { List, Diagnosis, Actions };
}

/// <summary>一条预警（统一结构，三类预警共用，便于统一排序与统一展示）</summary>
public class InsightAlert
{
    /// <summary>预警分类（见 <see cref="InsightCategories"/>）</summary>
    public string Category { get; set; } = "";

    /// <summary>预警等级：紧急 / 高 / 中 / 低</summary>
    public string Level { get; set; } = InsightLevels.Low;

    /// <summary>对象标识（物料编码 / 设备编码 / 其它）</summary>
    public string SubjectCode { get; set; } = "";

    /// <summary>对象名称</summary>
    public string SubjectName { get; set; } = "";

    /// <summary>一句话结论（界面上单行显示）</summary>
    public string Headline { get; set; } = "";

    /// <summary>归因说明（为什么会有这个预警）</summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>建议动作（人可直接照做）</summary>
    public List<string> Actions { get; set; } = new();

    /// <summary>关键数字（供界面展示与复算）</summary>
    public Dictionary<string, double> KeyFigures { get; set; } = new();

    /// <summary>优先级分值（越大越优先，用于统一排序）</summary>
    public double Priority { get; set; }

    /// <summary>输出形式</summary>
    public string OutputKind { get; set; } = InsightOutputKind.List;

    /// <summary>数据来源（可追溯到具体数据层方法）</summary>
    public string Evidence { get; set; } = "";

    public string ReasonText => string.Join("；", Reasons);
    public string ActionText => string.Join("；", Actions);
    public string FigureText => string.Join("｜", KeyFigures.Select(k => $"{k.Key} {k.Value:0.##}"));
}

/// <summary>预警分类</summary>
public static class InsightCategories
{
    /// <summary>物料临期 × 未来需求匹配</summary>
    public const string ExpiryConsumption = "物料临期与消耗";

    /// <summary>设备异常集中度与预防性维护</summary>
    public const string EquipmentRisk = "设备异常集中";

    /// <summary>呆滞料与资金占压</summary>
    public const string SlowMoving = "呆滞料占压";

    public static readonly string[] All = { ExpiryConsumption, EquipmentRisk, SlowMoving };
}

/// <summary>预警等级</summary>
public static class InsightLevels
{
    public const string Urgent = "紧急";
    public const string High = "高";
    public const string Medium = "中";
    public const string Low = "低";

    public static readonly string[] All = { Urgent, High, Medium, Low };

    /// <summary>等级 → 优先级权重（用于统一排序）</summary>
    public static double Weight(string level) => level switch
    {
        Urgent => 4, High => 3, Medium => 2, _ => 1
    };
}

// ===========================================================================
// ① 物料临期 × 未来需求匹配
// ===========================================================================

/// <summary>临期物料的处置判定</summary>
public static class ExpiryActions
{
    /// <summary>能在效期内被消耗完：无需处置，但必须优先 FIFO 出库</summary>
    public const string CanConsume = "可消耗完";

    /// <summary>消耗不完且需求量还很大：提前备货，等效期过后新批次顶上</summary>
    public const string PreBuy = "建议预防性采购";

    /// <summary>消耗不完且需求很小：到期只能报废</summary>
    public const string WillWaste = "将过期报废";

    public static readonly string[] All = { CanConsume, PreBuy, WillWaste };
}

/// <summary>一条临期批次的分析结果</summary>
public class ExpiryInsightRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>该批次库存量</summary>
    public double StockQty { get; set; }

    /// <summary>到期日（无有效期为 null）</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>剩余天数（负数=已过期）</summary>
    public int? DaysLeft { get; set; }

    /// <summary>效期内预计被消耗量（FIFO 模拟结果）</summary>
    public double ConsumableQty { get; set; }

    /// <summary>效期结束时仍会剩余的量</summary>
    public double LeftoverQty { get; set; }

    /// <summary>日均消耗（未来工单需求 ÷ 未来天数）</summary>
    public double DailyDemand { get; set; }

    /// <summary>当前可消耗天数（库存 ÷ 日均消耗）</summary>
    public double CoverDays => DailyDemand > 1e-9 ? StockQty / DailyDemand : 999;

    /// <summary>处置判定</summary>
    public string Action { get; set; } = ExpiryActions.CanConsume;

    /// <summary>建议的预防性采购量（仅 PreBuy 时 > 0）</summary>
    public double SuggestedBuyQty { get; set; }

    /// <summary>预计报废量（仅 WillWaste 时 > 0）</summary>
    public double WasteQty { get; set; }

    /// <summary>预警等级</summary>
    public string Level { get; set; } = InsightLevels.Low;

    /// <summary>说明</summary>
    public string ReasonText { get; set; } = "";

    /// <summary>建议动作（人可照做）</summary>
    public string ActionText { get; set; } = "";

    public string StockText => $"{StockQty:0.##} {Unit}";
    public string ConsumableText => $"{ConsumableQty:0.##} {Unit}";
    public string LeftoverText => $"{LeftoverQty:0.##} {Unit}";
    public string ExpiryText => ExpiryDate is null ? "无有效期" : ExpiryDate.Value.ToString("yyyy-MM-dd");
    public string DaysText => DaysLeft is null ? "—" : $"{DaysLeft} 天";

    /// <summary>转成统一预警条目</summary>
    public InsightAlert ToAlert() => new()
    {
        Category = InsightCategories.ExpiryConsumption,
        Level = Level,
        SubjectCode = MaterialCode,
        SubjectName = MaterialName,
        Headline = $"{MaterialName}（{LotNo}）{DaysText}到期，{Action}",
        Reasons =
        {
            ReasonText
        },
        Actions =
        {
            ActionText
        },
        KeyFigures = new Dictionary<string, double>
        {
            ["库存"] = StockQty,
            ["效期内可消耗"] = ConsumableQty,
            ["到期剩余"] = LeftoverQty,
            ["日均需求"] = Math.Round(DailyDemand, 4)
        },
        Priority = InsightLevels.Weight(Level) * 10 + ((DaysLeft ?? 999) < 0 ? 50 : 0),
        OutputKind = InsightOutputKind.Actions,
        Evidence = "数据来源：MesDatabase.WmsStocks（ExpiryDate）+ AggregateMaterialOccupancy（未来工单资材占用汇总）"
    };
}

// ===========================================================================
// ② 设备异常集中度与预防性维护
// ===========================================================================

/// <summary>一条设备风险分析结果</summary>
public class EquipmentInsightRow
{
    public string EquipmentCode { get; set; } = "";
    public string EquipmentName { get; set; } = "";
    public string Section { get; set; } = "";
    public string Line { get; set; } = "";

    /// <summary>统计窗口内的异常次数</summary>
    public int AbnormalCount { get; set; }

    /// <summary>未闭环的异常数</summary>
    public int OpenCount { get; set; }

    /// <summary>设备报警次数（来自 DeviceAlarms）</summary>
    public int AlarmCount { get; set; }

    /// <summary>各异常子类次数：部件 → 次数（如"精度漂移"→ 4）</summary>
    public Dictionary<string, int> SubTypeCounts { get; set; } = new();

    /// <summary>集中度 = 最高部件次数 ÷ 总次数（0~1）</summary>
    public double Concentration { get; set; }

    /// <summary>集中度最高的部件</summary>
    public string TopSubType { get; set; } = "";

    /// <summary>上次保养距今天数</summary>
    public int? DaysSinceMaintenance { get; set; }

    /// <summary>距下次保养到期天数（负数=已超期）</summary>
    public int? TpmDueDays { get; set; }

    /// <summary>预警等级</summary>
    public string Level { get; set; } = InsightLevels.Low;

    /// <summary>建议的预防性维护项目</summary>
    public List<string> MaintenanceItems { get; set; } = new();

    /// <summary>判断依据</summary>
    public string ReasonText { get; set; } = "";

    /// <summary>建议动作</summary>
    public string ActionText { get; set; } = "";

    public string AbnormalText => $"{AbnormalCount} 次";
    public string ConcentrationText => $"{Concentration:P0}（{TopSubType}）";

    /// <summary>异常类型分布（按次数降序，最多 4 个）</summary>
    public string SubTypeText => SubTypeCounts.Count == 0
        ? "—"
        : string.Join("、", SubTypeCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Take(4).Select(kv => $"{kv.Key}×{kv.Value}"))
          + (SubTypeCounts.Count > 4 ? $" 等 {SubTypeCounts.Count} 类" : "");

    /// <summary>距上次保养（无记录要显式说"无记录"，不能显示成空白让人以为是 0）</summary>
    public string MaintText => DaysSinceMaintenance is null
        ? "无保养记录"
        : $"{DaysSinceMaintenance} 天前";

    /// <summary>点检保养到期（负数=已超期）</summary>
    public string TpmText => TpmDueDays is null
        ? "无点检计划"
        : TpmDueDays < 0 ? $"已超期 {Math.Abs(TpmDueDays.Value)} 天" : $"剩 {TpmDueDays} 天";

    /// <summary>建议维护项目</summary>
    public string MaintItemsText => MaintenanceItems.Count == 0 ? "—" : string.Join("、", MaintenanceItems.Take(3));

    public InsightAlert ToAlert() => new()
    {
        Category = InsightCategories.EquipmentRisk,
        Level = Level,
        SubjectCode = EquipmentCode,
        SubjectName = EquipmentName,
        Headline = $"{EquipmentName}（{EquipmentCode}）{AbnormalText}异常，集中于{TopSubType}",
        Reasons = { ReasonText },
        Actions = { ActionText },
        KeyFigures = new Dictionary<string, double>
        {
            ["异常次数"] = AbnormalCount,
            ["未闭环"] = OpenCount,
            ["报警次数"] = AlarmCount,
            ["部件集中度"] = Math.Round(Concentration, 3),
            ["距上次保养"] = DaysSinceMaintenance ?? -1
        },
        Priority = InsightLevels.Weight(Level) * 10 + AbnormalCount + Concentration * 5,
        OutputKind = InsightOutputKind.Diagnosis,
        Evidence = "数据来源：MesDatabase.AbnormalRecords（EquipmentCode + SubType + OccurTime）+ AbnormalHistories"
                    + " + DeviceAlarms + TpmRecords/TpmPlans"
    };
}

// ===========================================================================
// ③ 呆滞料与资金占压
// ===========================================================================

/// <summary>呆滞料分级</summary>
public static class SlowMovingGrades
{
    /// <summary>无需求且有库存</summary>
    public const string Idle = "呆滞";

    /// <summary>需求很少，长期用不完</summary>
    public const string Slow = "慢动";

    public static readonly string[] All = { Slow, Idle };
}

public class SlowMovingInsightRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "";

    /// <summary>当前库存</summary>
    public double StockQty { get; set; }

    /// <summary>未来工单对该物料的总需求</summary>
    public double FutureDemand { get; set; }

    /// <summary>安全库存</summary>
    public double SafetyStock { get; set; }

    /// <summary>呆滞分级</summary>
    public string Grade { get; set; } = SlowMovingGrades.Idle;

    /// <summary>超出需求的占用量</summary>
    public double IdleQty => Math.Max(0, StockQty - FutureDemand - SafetyStock);

    /// <summary>预警等级</summary>
    public string Level { get; set; } = InsightLevels.Low;

    public string ReasonText { get; set; } = "";
    public string ActionText { get; set; } = "";

    public string StockQtyText => $"{StockQty:0.##} {Unit}";
    public string FutureDemandText => $"{FutureDemand:0.##} {Unit}";
    public string IdleQtyText => $"{IdleQty:0.##} {Unit}";

    public InsightAlert ToAlert() => new()
    {
        Category = InsightCategories.SlowMoving,
        Level = Level,
        SubjectCode = MaterialCode,
        SubjectName = MaterialName,
        Headline = $"{MaterialName} {Grade}，库存 {StockQty:0.##} {Unit} 超出未来需求 {IdleQty:0.##} {Unit}",
        Reasons = { ReasonText },
        Actions = { ActionText },
        KeyFigures = new Dictionary<string, double>
        {
            ["库存"] = StockQty,
            ["未来需求"] = FutureDemand,
            ["超出部分"] = IdleQty
        },
        Priority = InsightLevels.Weight(Level) * 10 + IdleQty / Math.Max(1, Math.Abs(FutureDemand)),
        OutputKind = InsightOutputKind.Actions,
        Evidence = "数据来源：MesDatabase.WmsStocks + AggregateMaterialOccupancy（未来工单占用）+ WmsMaterial.SafetyStock"
    };
}

// ===========================================================================
// 汇总容器
// ===========================================================================

/// <summary>三类预警的汇总结果</summary>
public class InsightReport
{
    /// <summary>物料临期 × 消耗匹配</summary>
    public List<ExpiryInsightRow> Expiry { get; set; } = new();

    /// <summary>设备风险</summary>
    public List<EquipmentInsightRow> Equipment { get; set; } = new();

    /// <summary>呆滞料</summary>
    public List<SlowMovingInsightRow> SlowMoving { get; set; } = new();

    /// <summary>本次评估使用的规则快照（界面上要显示，便于复算与审计）</summary>
    public InsightRuleSnapshot Rules { get; set; } = new();

    /// <summary>数据截至时间</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    /// <summary>全部预警条目（统一结构，按优先级降序）</summary>
    public List<InsightAlert> AllAlerts()
        => Expiry.Select(r => r.ToAlert())
            .Concat(Equipment.Select(r => r.ToAlert()))
            .Concat(SlowMoving.Select(r => r.ToAlert()))
            .OrderByDescending(a => a.Priority)
            .ThenBy(a => a.Category)
            .ToList();

    /// <summary>概览文本</summary>
    public string SummaryText =>
        $"物料临期 {Expiry.Count} 项（其中需处置 {Expiry.Count(e => e.Action != ExpiryActions.CanConsume)} 项）"
        + $"｜设备风险 {Equipment.Count} 台"
        + $"｜呆滞料 {SlowMoving.Count} 种"
        + $"；截至 {GeneratedAt:yyyy-MM-dd HH:mm}，阈值：临期{MesDatabaseInsightRules.NearExpiryDays}天"
        + $"／异常频次{MesDatabaseInsightRules.DeviceAbnormalThreshold}次"
        + $"／集中度{MesDatabaseInsightRules.DeviceConcentrationThreshold:P0}"
        + $"／呆滞{MesDatabaseInsightRules.SlowMovingWindowDays}天";
}

/// <summary>规则快照：评估时实际用的阈值（结果要能被复算，就必须记下当时用的阈值）</summary>
public class InsightRuleSnapshot
{
    public int NearExpiryDays { get; set; }
    public int DeviceWindowDays { get; set; }
    public int DeviceAbnormalThreshold { get; set; }
    public double DeviceConcentrationThreshold { get; set; }
    public int SlowMovingWindowDays { get; set; }
    public int DemandHorizonDays { get; set; }

    public string Text =>
        $"临期 {NearExpiryDays} 天｜异常窗口 {DeviceWindowDays} 天/≥{DeviceAbnormalThreshold} 次"
        + $"｜部件集中度 ≥{DeviceConcentrationThreshold:P0}"
        + $"｜呆滞窗口 {SlowMovingWindowDays} 天｜需求预测 {DemandHorizonDays} 天";
}