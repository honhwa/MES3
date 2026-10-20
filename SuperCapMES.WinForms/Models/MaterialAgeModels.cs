namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 物料全链管控（二期 3.2 + 3.3）—— 临期三色 + 三方案决策 —— 模型层
//
// 3.2 临期三色解决的判断口径问题：
//   "还剩多少天过期"不等于"会不会报废"。真正要问的是两件事：
//     ① 效期还剩几天（ExpiryDate − 今天）        → 决定颜色
//     ② 按当前消耗速度，到期前能不能用完          → 决定要不要动手
//   所以每一行都同时给「剩余效期」和「预计用完日」：
//     · 预计用完日 ≤ 到期日 → 能在效期内消化，绿色
//     · 预计用完日 > 到期日 → 到期会剩 X，黄色临期 / 红色已过期
//   消耗速度取近 90 天领用实测（`GetMaterialConsumption`），样本不足时**如实标注不可估**，
//   不拿"假设值"充当结论——这是可追问的前提。
//
// 3.3 三方案决策解决的落地问题：
//   临期料就三条路：提前排产消耗 / IQC 重检延期 / 报废处置。
//   每条路都要能回答"花多少钱、谁有权批、留什么痕"，所以：
//     · 金额 = 剩余数量 × 单价（单价为演示参数，真实取自 ERP 物料主数据）
//     · 审批分级：≤500 元班组长；500~5000 元仓储主管；>5000 元生产经理——**级别不够后端直接拒**
//     · 执行动作全部落到既有单据：报废走 `ScrapMaterial`、重检走 `ReinspectMaterial`，
//       本模块只多做一层"决策留痕"，不另造第二套处置逻辑
//   兜底：已过期超过 15 天仍未处置的批次，只剩报废一条路（方案名标注「兜底报废」），
//        避免"越拖越没人管、账实长期不符"。
// ============================================================================

/// <summary>效期状态（三色）</summary>
public static class AgeLevels
{
    /// <summary>绿：效期充裕，且能在到期前用完</summary>
    public const string Fresh = "充裕";

    /// <summary>黄：临期（剩余效期 ≤ 阈值），或到期前消耗不完</summary>
    public const string Near = "临期";

    /// <summary>红：已过期</summary>
    public const string Expired = "已过期";

    /// <summary>灰：该物料不做效期管理</summary>
    public const string NoExpiry = "不管理效期";

    public static readonly string[] All = { Expired, Near, Fresh, NoExpiry };
}

/// <summary>临期处置方案</summary>
public static class AgePlans
{
    /// <summary>提前排产消耗：把批次排进近期工单，在到期前用掉</summary>
    public const string Consume = "提前排产消耗";

    /// <summary>IQC 重检延期：走重新进料检，检验合格则延长效期</summary>
    public const string Reinspect = "IQC 重检延期";

    /// <summary>报废处置：不可逆，写废弃台账 + 数量归零</summary>
    public const string Scrap = "报废处置";

    /// <summary>兜底报废：超期未处置，按兜底规则强制报废</summary>
    public const string Fallback = "兜底报废";

    public static readonly string[] All = { Consume, Reinspect, Scrap, Fallback };
}

/// <summary>
/// 审批分级（金额分档 —— 与处置金额挂钩，不能靠人自觉）：
/// 阈值 500 / 5000 元是方案口径，改这里即可，其他地方不写死数字。
/// </summary>
public static class AgeApproval
{
    public const string Team = "班组长";
    public const string Supervisor = "仓储主管";
    public const string Manager = "生产经理";

    /// <summary>班组长可批上限（元）</summary>
    public const double TeamMax = 500;

    /// <summary>仓储主管可批上限（元）</summary>
    public const double SupervisorMax = 5000;

    public static readonly string[] All = { Team, Supervisor, Manager };

    /// <summary>该金额需要的最低审批级别</summary>
    public static string LevelOf(double amount) =>
        amount <= TeamMax ? Team : amount <= SupervisorMax ? Supervisor : Manager;

    /// <summary>审批级别的序（越大权限越高），用于"级别够不够"的比较</summary>
    public static int RankOf(string? level) => level switch
    {
        Team => 1,
        Supervisor => 2,
        Manager => 3,
        _ => 0
    };

    /// <summary>某级别能否批准某金额</summary>
    public static bool CanApprove(string? level, double amount) =>
        RankOf(level) >= RankOf(LevelOf(amount));

    /// <summary>提级说明（给用户看"为什么这批要经理签"）</summary>
    public static string Explain(double amount)
    {
        var need = LevelOf(amount);
        return $"金额 {amount:0.##} 元 → 需「{need}」审批（≤{TeamMax:0} 元班组长；{TeamMax:0}~{SupervisorMax:0} 元仓储主管；>{SupervisorMax:0} 元生产经理）";
    }

    /// <summary>分级规则表（界面直接展示，避免"规则藏在代码里"）</summary>
    public static string RuleText =>
        $"分级审批：≤ {TeamMax:0} 元 → {Team}；{TeamMax:0} ~ {SupervisorMax:0} 元 → {Supervisor}；> {SupervisorMax:0} 元 → {Manager}";
}

/// <summary>临期判定阈值（按物料类别，可调）</summary>
public static class AgeRules
{
    /// <summary>临期阈值（天）：剩余效期 ≤ 该值判临期</summary>
    public static int NearDaysOf(string category) => category switch
    {
        WmsCategories.Raw => 30,
        WmsCategories.Aux => 20,
        _ => 15
    };

    /// <summary>超期未处置兜底天数：过期超过该天数只剩报废一条路</summary>
    public static int FallbackOverdueDays { get; set; } = 15;

    /// <summary>IQC 重检一次可延长的效期（天）</summary>
    public static int ExtendDays { get; set; } = 30;

    /// <summary>消耗估算窗口（天），与 GetMaterialConsumption 一致</summary>
    public static int ConsumeWindowDays { get; set; } = 90;

    /// <summary>阈值说明（界面上直接亮出来）</summary>
    public static string RuleText =>
        $"临期阈值：原料 剩余效期 ≤ {NearDaysOf(WmsCategories.Raw)} 天、辅料 ≤ {NearDaysOf(WmsCategories.Aux)} 天、其他 ≤ {NearDaysOf("")} 天；"
        + $"过期超过 {FallbackOverdueDays} 天未处置转兜底报废；IQC 重检一次延长 {ExtendDays} 天。";
}

/// <summary>临期看板的显示范围</summary>
public static class AgeFilters
{
    public const string All = "全部批次";
    public const string NearExpired = "临期与已过期";
    public const string ExpiredOnly = "仅已过期";

    public static readonly string[] AllItems = { NearExpired, All, ExpiredOnly };
}

/// <summary>
/// 消耗速度的来源（必须标注，不能让"预计用完日"变成没依据的数字）：
///   领用实测 —— 近 90 天已过账的现场领用单（最接近真实消耗，含超领与损耗）
///   工单需求 —— 未来视界内未完工工单的 BOM 需求（领用样本不足时用它，反映"接下来要吃掉多少"）
///   不可估   —— 两者都没有，如实标注、不做预测
/// </summary>
public static class AgeSources
{
    public const string IssueActual = "领用实测";
    public const string WorkOrderDemand = "工单需求";
    public const string None = "不可估";

    public static readonly string[] All = { IssueActual, WorkOrderDemand, None };
}

/// <summary>
/// 临期看板行（一个库存批次一行）。
/// 同时给"效期还剩几天"和"按当前速度什么时候用完"——两句话都缺一不可。
/// </summary>
public class MaterialAgeRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "";

    /// <summary>批次号</summary>
    public string LotNo { get; set; } = "";

    /// <summary>库位</summary>
    public string Location { get; set; } = "";

    /// <summary>在库数量</summary>
    public double Quantity { get; set; }

    /// <summary>有效期（null = 不管理效期）</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>剩余效期（天；负数 = 已过期天数）</summary>
    public int RemainingDays { get; set; }

    /// <summary>日均消耗（近 90 天领用实测 / 90；样本不足为 0）</summary>
    public double DailyConsumption { get; set; }

    /// <summary>消耗样本行数（&lt; 2 视为领用样本不足，改用工单需求口径）</summary>
    public int ConsumptionSamples { get; set; }

    /// <summary>消耗速度来源（领用实测 / 工单需求 / 不可估）</summary>
    public string ConsumptionSource { get; set; } = AgeSources.None;

    /// <summary>按当前速度用完这批需要几天（0 = 不可估）</summary>
    public int ConsumeDays { get; set; }

    /// <summary>预计用完日（不可估时为 null）</summary>
    public DateTime? ExpectedRunOutDate { get; set; }

    /// <summary>到期前消耗不掉的数量（预计报废量）</summary>
    public double WasteQty { get; set; }

    /// <summary>预计用完日晚于到期日的天数（&gt; 0 即"用不完"）</summary>
    public int WasteDays { get; set; }

    /// <summary>三色等级</summary>
    public string AgeLevel { get; set; } = AgeLevels.NoExpiry;

    /// <summary>临期阈值（天，按类别）</summary>
    public int NearDays { get; set; }

    /// <summary>单价（演示参数）</summary>
    public double UnitPrice { get; set; }

    /// <summary>在库金额 = 数量 × 单价</summary>
    public double Amount { get; set; }

    /// <summary>预计报废金额</summary>
    public double WasteAmount => Math.Round(WasteQty * UnitPrice, 2);

    /// <summary>是否已过期</summary>
    public bool IsExpired => ExpiryDate is not null && RemainingDays < 0;

    /// <summary>是否需处置（临期或已过期）</summary>
    public bool NeedsAction => AgeLevel is AgeLevels.Near or AgeLevels.Expired;

    /// <summary>是否有可用的消耗估算依据（领用实测或工单需求）</summary>
    public bool HasEstimate => ConsumptionSource != AgeSources.None && DailyConsumption > 1e-9;

    /// <summary>是否已超期未处置（兜底条件）</summary>
    public bool IsFallback => IsExpired && -RemainingDays > AgeRules.FallbackOverdueDays;

    // ---- 显示 ----

    public string QtyText => $"{Quantity:0.##} {Unit}";
    public string ExpiryText => ExpiryDate?.ToString("yyyy-MM-dd") ?? "—";

    public string RemainingText => ExpiryDate is null ? "—"
        : RemainingDays < 0 ? $"已过期 {-RemainingDays} 天"
        : $"剩 {RemainingDays} 天";

    /// <summary>日均消耗文案（标注来源，样本/需求都没有就如实说不可估）</summary>
    public string DailyText => HasEstimate
        ? $"{DailyConsumption:0.##} {Unit}/天（{ConsumptionSource}）"
        : "不可估";

    /// <summary>预计用完日文案</summary>
    public string RunOutText => ExpectedRunOutDate is null
        ? "不可估"
        : $"{ExpectedRunOutDate:MM-dd}";

    /// <summary>缺口文案（到期前用不完多少）</summary>
    public string WasteText => !HasEstimate && ExpiryDate is not null
        ? "不可估"
        : WasteQty <= 1e-9
            ? "可消化"
            : $"剩 {WasteQty:0.##} {Unit}" + (WasteDays > 0 ? $"（超 {WasteDays} 天）" : "");

    public string AmountText => $"{Amount:0.##} 元";

    /// <summary>表格用精简判定（列宽有限，完整版见选中行结论条 / 悬停）</summary>
    public string VerdictBrief
    {
        get
        {
            if (ExpiryDate is null) return "不做效期管理";
            if (IsExpired) return IsFallback ? "超兜底期限，必须报废" : "已过期，重检或报废";
            if (WasteQty > 1e-9 && HasEstimate) return $"到期前用不完 {WasteQty:0.##} {Unit}";
            if (RemainingDays <= NearDays) return HasEstimate ? "临期，建议优先消耗" : "临期，消耗速度不可估";
            return "充裕";
        }
    }

    /// <summary>一行结论（说清"为什么是这个颜色"）</summary>
    public string VerdictText
    {
        get
        {
            if (ExpiryDate is null) return "不做效期管理，按常规 FIFO 消耗。";
            if (IsExpired)
                return IsFallback
                    ? $"已过期 {-RemainingDays} 天且超过兜底期限（{AgeRules.FallbackOverdueDays} 天），必须报废处置。"
                    : $"已过期 {-RemainingDays} 天，可报废或走 IQC 重检延期（重检合格才可用）。";
            if (WasteQty > 1e-9 && HasEstimate)
                return $"剩余效期 {RemainingDays} 天，但按当前速度需 {ConsumeDays} 天用完，"
                     + $"预计 {ExpectedRunOutDate:MM-dd} 用完 → 到期会剩 {WasteQty:0.##} {Unit}，建议提前排产或重检延期。";
            if (RemainingDays <= NearDays)
                return HasEstimate
                    ? $"剩余效期 {RemainingDays} 天（≤ 阈值 {NearDays} 天），按当前速度可在效期内用完，建议排产时优先消耗。"
                    : $"剩余效期 {RemainingDays} 天（≤ 阈值 {NearDays} 天），但既无领用记录也无在手需求，无法测算消耗速度，需计划员判断。";
            return $"剩余效期 {RemainingDays} 天，充裕。";
        }
    }
}

/// <summary>一个处置方案（三方案之一）</summary>
public class AgePlanOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";

    /// <summary>是否可行（不可行要给原因，不能让用户点了才被拒）</summary>
    public bool Feasible { get; set; }

    /// <summary>不可行原因</summary>
    public string BlockReason { get; set; } = "";

    /// <summary>涉及金额（决策分级审批的依据）</summary>
    public double Amount { get; set; }

    /// <summary>所需审批级别</summary>
    public string RequiredLevel { get; set; } = AgeApproval.Team;

    /// <summary>效果描述（能消化多少 / 延长几天 / 损失多少）</summary>
    public string EffectText { get; set; } = "";

    /// <summary>评分（0~100，规则算出）</summary>
    public double Score { get; set; }

    /// <summary>是否推荐</summary>
    public bool Recommended { get; set; }

    public List<string> Pros { get; set; } = new();
    public List<string> Cons { get; set; } = new();

    public string ScoreText => Feasible ? $"{Score:0.#}" : "—";
    public string AmountText => $"{Amount:0.##} 元";
    public string StatusText => Feasible ? "可执行" : "不可行";
}

/// <summary>某批次的方案模拟结果</summary>
public class AgeSimulation
{
    public MaterialAgeRow? Row { get; set; }
    public List<AgePlanOption> Options { get; set; } = new();
    public string RecommendedId { get; set; } = "";

    public AgePlanOption? Recommended => Options.FirstOrDefault(o => o.Id == RecommendedId);

    /// <summary>提交该方案需要的审批级别（取推荐方案的）</summary>
    public string RequiredLevel => Recommended?.RequiredLevel ?? AgeApproval.Team;
}

/// <summary>临期看板（一次查询的完整结果）</summary>
public class MaterialAgeBoard
{
    public List<MaterialAgeRow> Rows { get; set; } = new();
    public List<MaterialAgeRow> All { get; set; } = new();

    public int BatchCount => All.Count;
    public int NearCount => All.Count(r => r.AgeLevel == AgeLevels.Near);
    public int ExpiredCount => All.Count(r => r.AgeLevel == AgeLevels.Expired);
    public int FallbackCount => All.Count(r => r.IsFallback);

    /// <summary>风险金额 = 临期 + 已过期批次的在库金额</summary>
    public double RiskAmount => Math.Round(All.Where(r => r.NeedsAction).Sum(r => r.Amount), 2);

    /// <summary>预计报废量合计（仅同单位混加作规模感参考，明细以行为准）</summary>
    public double WasteQty => All.Sum(r => r.WasteQty);

    public string ScopeText { get; set; } = "全部批次";

    public string SummaryText =>
        $"{ScopeText}：{BatchCount} 个批次，临期 {NearCount} / 已过期 {ExpiredCount}"
        + (FallbackCount > 0 ? $"（其中 {FallbackCount} 个已超兜底期限）" : "")
        + $"；风险金额 {RiskAmount:0.##} 元";
}

/// <summary>临期处置决策记录（只追加，作为处置留痕）</summary>
public class AgeDecision
{
    public string DecisionNo { get; set; } = "";
    public DateTime DecidedAt { get; set; } = DateTime.Now;
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>采用的方案</summary>
    public string Plan { get; set; } = "";

    /// <summary>审批级别</summary>
    public string ApprovalLevel { get; set; } = "";

    /// <summary>审批人 / 操作人</summary>
    public string Approver { get; set; } = "";

    /// <summary>处置金额（分级审批依据）</summary>
    public double Amount { get; set; }

    /// <summary>执行状态：已登记 / 待 IQC / 已报废 / 已延期</summary>
    public string Status { get; set; } = AgeDecisionStatus.Registered;

    /// <summary>关联单据号（废弃单 / 重检单）</summary>
    public string RefNo { get; set; } = "";

    public string Remark { get; set; } = "";

    public string DecidedAtText => DecidedAt.ToString("yyyy-MM-dd HH:mm");
    public string AmountText => $"{Amount:0.##} 元";
}

/// <summary>决策执行状态</summary>
public static class AgeDecisionStatus
{
    /// <summary>已登记（提前排产消耗：登记优先消耗指令）</summary>
    public const string Registered = "已登记";

    /// <summary>待 IQC（临期未过期，等检验安排）</summary>
    public const string PendingIqc = "待 IQC";

    /// <summary>已报废</summary>
    public const string Scrapped = "已报废";

    /// <summary>已延期（重检合格，效期已延长）</summary>
    public const string Extended = "已延期";

    public static readonly string[] All = { Registered, PendingIqc, Scrapped, Extended };
}

/// <summary>物料单价（演示参数，真实取自 ERP 物料主数据）</summary>
public class MaterialPrice
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";
    public double UnitPrice { get; set; }
}

/// <summary>优先消耗标记（提前排产方案的落地凭据，供排产/领料环节参考）</summary>
public class MaterialPriorityMark
{
    public string MarkNo { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";
    public double Quantity { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>要求在此之前消耗完</summary>
    public DateTime TargetDate { get; set; }

    public string Operator { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Remark { get; set; } = "";

    public string TargetDateText => TargetDate.ToString("yyyy-MM-dd");
    public string CreatedAtText => CreatedAt.ToString("yyyy-MM-dd HH:mm");
}
