namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 智能排产与齐套校验 —— 模型层
//
// 设计原则（延续 AI 助手那一套）：
//   **结论必须能被追问**。每一条排产建议都要能回答"你凭什么这么说"——
//   所以这里的每个派生值都保留原始输入（需求 / 可用 / 样本工单号 / 公式口径），
//   界面把它们摊开给人看，而不是只丢一个"建议开工 8 月 20 日"。
// ============================================================================

/// <summary>排产规则常量（集中定义口径，避免公式散落在各处且说法不一）</summary>
public static class SchedulingRules
{
    /// <summary>每日有效产能工时（h）：两班制 × 8h。用于把"工时"折算成"生产日"。</summary>
    public const double DailyCapacityHours = 16.0;

    /// <summary>良率兜底值：历史样本不足时采用，并在界面上标注"默认值"。</summary>
    public const double DefaultYield = 0.98;

    /// <summary>单位工时兜底值（h/只）：历史样本不足时采用。</summary>
    public const double DefaultUnitHours = 0.0021;

    /// <summary>推理所需最少历史样本数；少于该值时结论降级为"参考"，并明示样本不足。</summary>
    public const int MinSampleCount = 3;

    /// <summary>齐套"紧张"判定：可用量相对需求量富余不足该比例时提示批次/效期风险。</summary>
    public const double TightBufferRatio = 1.20;

    /// <summary>交期余量小于该天数视为"偏紧"。</summary>
    public const double TightSlackDays = 2.0;
}

/// <summary>齐套校验状态</summary>
public static class ShortageStatus
{
    /// <summary>可用量满足需求</summary>
    public const string Ok = "满足";

    /// <summary>刚好够用，富余不足（批次/效期风险）</summary>
    public const string Tight = "紧张";

    /// <summary>可用量不足，存在缺口</summary>
    public const string Short = "短缺";

    public static readonly string[] All = { Ok, Tight, Short };
}

/// <summary>交期风险等级</summary>
public static class ScheduleRisk
{
    /// <summary>按历史节拍可从容完成</summary>
    public const string Normal = "正常";

    /// <summary>工序衔接紧，几乎没有缓冲</summary>
    public const string Tight = "偏紧";

    /// <summary>按现有产能推算赶不上交期</summary>
    public const string Late = "来不及";

    public static readonly string[] All = { Normal, Tight, Late };
}

/// <summary>
/// 单个物料的齐套校验行：需求量 vs 可用库存。
/// 需求量与可用量的口径都保留在行上，便于界面直接摊开解释。
/// </summary>
public class MaterialShortageRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>物料类型：主材 / 辅材 / 包材</summary>
    public string MaterialType { get; set; } = "";

    /// <summary>单位用量（1 只成品）</summary>
    public double QtyPerUnit { get; set; }

    /// <summary>损耗率（%）</summary>
    public double LossRate { get; set; }

    /// <summary>需求量 = 单位用量 × (1 + 损耗率/100) × 投产数量</summary>
    public double Requirement { get; set; }

    /// <summary>可用量：可投产库位的在库合计（不含待检区 / 退货待处理区）</summary>
    public double Available { get; set; }

    /// <summary>不可用量：待检区 + 退货待处理区的在库合计（提示"还有一批没判"，不参与齐套）</summary>
    public double FrozenQty { get; set; }

    /// <summary>缺口 = max(0, 需求 − 可用)</summary>
    public double Shortage => Math.Max(0, Requirement - Available);

    /// <summary>富余 = max(0, 可用 − 需求)</summary>
    public double Surplus => Math.Max(0, Available - Requirement);

    /// <summary>覆盖率（%）：可用 / 需求，封顶 100</summary>
    public double Coverage => Requirement <= 1e-9 ? 100 : Math.Min(100, Available / Requirement * 100);

    /// <summary>状态：满足 / 紧张 / 短缺</summary>
    public string Status { get; set; } = ShortageStatus.Ok;

    /// <summary>可用批次概要（如"2 批 / 最早到期 2026-10-15"），无批次管理时为空</summary>
    public string LotHint { get; set; } = "";

    /// <summary>处置建议（仅在紧张/短缺时给）</summary>
    public string Advice { get; set; } = "";

    public string RequirementText => $"{Requirement:0.##}";
    public string AvailableText => $"{Available:0.##}";
    public string ShortageText => Shortage <= 1e-9 ? "—" : $"{Shortage:0.##}";
    public string CoverageText => $"{Coverage:0.#}%";

    /// <summary>需求公式的说明文本（界面/自测直接引用，避免两处口径不一致）</summary>
    public string FormulaText =>
        $"{QtyPerUnit:0.####} × (1 + {LossRate:0.##}%) × 投产数";
}

/// <summary>齐套校验结果（一个产品型号 + 一个投产数量 → 一张物料清单）</summary>
public class MaterialCheckResult
{
    public string ProductModel { get; set; } = "";

    /// <summary>用于计算需求的投产数量（已含良率补偿）</summary>
    public int PlanQty { get; set; }

    /// <summary>订单数量（未含良率补偿；PlanQty 与它的差异来自良率）</summary>
    public int OrderQty { get; set; }

    public List<MaterialShortageRow> Rows { get; set; } = new();

    public int OkCount => Rows.Count(r => r.Status == ShortageStatus.Ok);
    public int TightCount => Rows.Count(r => r.Status == ShortageStatus.Tight);
    public int ShortCount => Rows.Count(r => r.Status == ShortageStatus.Short);

    /// <summary>是否全部齐套（无短缺）</summary>
    public bool AllSatisfied => ShortCount == 0;

    /// <summary>无 BOM 数据时置 true（此时"齐套"无意义，界面需另行提示）</summary>
    public bool NoBom { get; set; }

    /// <summary>短缺物料清单（按缺口从大到小）</summary>
    public List<MaterialShortageRow> ShortageRows =>
        Rows.Where(r => r.Status == ShortageStatus.Short)
            .OrderByDescending(r => r.Shortage)
            .ToList();

    public string SummaryText => NoBom
        ? $"型号 {ProductModel} 暂无 BOM 数据，无法做齐套校验。"
        : $"共 {Rows.Count} 项物料：满足 {OkCount} / 紧张 {TightCount} / 短缺 {ShortCount}"
          + (AllSatisfied ? "，齐套通过。" : $"；{ShortCount} 项缺料，需先补料再投产。");

    /// <summary>缺料摘要（供弹窗、AI 助手、预警文本复用；齐套时为空串）</summary>
    public string ShortageBrief => string.Join("、",
        ShortageRows.Select(r => $"{r.MaterialName}缺 {r.ShortageText}{r.Unit}"));
}

/// <summary>
/// 历史生产样本统计 —— 排产推理的"证据"。
/// 所有结论都挂在这份样本上，界面必须能显示样本量与来源工单号。
/// </summary>
public class ProductionHistory
{
    public string ProductModel { get; set; } = "";

    /// <summary>参与统计的历史工单号</summary>
    public List<string> SampleWoNos { get; set; } = new();

    public int SampleCount => SampleWoNos.Count;

    /// <summary>样本总投产数（计划量合计）</summary>
    public int TotalPlanQty { get; set; }

    /// <summary>样本总产出数（完工数合计）</summary>
    public int TotalFinishedQty { get; set; }

    /// <summary>样本总不良数</summary>
    public int TotalNgQty { get; set; }

    /// <summary>平均良率（0-1）：Σ完工 / Σ(完工+不良)</summary>
    public double AvgYield { get; set; }

    /// <summary>单位工时（h/只）：Σ工时 / Σ完工</summary>
    public double UnitHours { get; set; }

    /// <summary>良率是否有真实样本支撑（false = 用了兜底值）</summary>
    public bool YieldFromHistory { get; set; }

    /// <summary>工时是否有真实样本支撑（false = 用了兜底值）</summary>
    public bool HoursFromHistory { get; set; }

    /// <summary>样本量是否达到推理门槛</summary>
    public bool EnoughSamples => SampleCount >= SchedulingRules.MinSampleCount;

    /// <summary>历史最常用线体</summary>
    public string PreferredLine { get; set; } = "";

    /// <summary>各线体出现次数（线体 → 工单数）</summary>
    public List<(string Line, int Count)> LineUsage { get; set; } = new();

    /// <summary>口径与来源说明（逐条展示给用户看）</summary>
    public List<string> Notes { get; set; } = new();

    public string YieldText => AvgYield <= 0 ? "—" : $"{AvgYield * 100:0.0}%";
    public string UnitHoursText => UnitHours <= 0 ? "—" : $"{UnitHours * 3600:0.0} 秒/只";
}

/// <summary>
/// 线体当前负载：新工单要排到某条线，必须先知道这条线手上还压着多少活。
/// </summary>
public class LineLoadInfo
{
    public string Line { get; set; } = "";

    /// <summary>该线未完工工单数（未开始 + 生产中）</summary>
    public int OpenWoCount { get; set; }

    /// <summary>在手未完工计划量合计（只）</summary>
    public int OpenPlanQty { get; set; }

    /// <summary>在手未完工计划量合计</summary>
    public double RemainingQty { get; set; }

    /// <summary>在手剩余工时（h）</summary>
    public double RemainingHours { get; set; }

    /// <summary>在手工单中最早的交期（无在手工单为 null）</summary>
    public DateTime? EarliestDueDate { get; set; }

    /// <summary>把手上的活干完需要多少个生产日</summary>
    public double DaysToClear => RemainingHours / SchedulingRules.DailyCapacityHours;

    /// <summary>最后一个在手工单的计划完工时间（即新单最早能开工的时间）</summary>
    public DateTime EarliestFreeTime { get; set; }

    public string LoadText => OpenWoCount == 0
        ? "空闲（无在手工单）"
        : $"{OpenWoCount} 张在手 / 剩余 {RemainingHours:0.#}h（约 {DaysToClear:0.#} 个生产日）";
}

/// <summary>
/// 排产建议：指定 PO → 一套可执行参数 + 可追问的推理依据 + 齐套校验。
/// </summary>
public class ScheduleRecommendation
{
    // ---- 输入（PO 原样带上，避免界面再去查一遍） ----
    public string PoNo { get; set; } = "";
    public string Customer { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int OrderQty { get; set; }
    public DateTime DueDate { get; set; }

    // ---- 推理结论（即"建议生成的工单信息"） ----
    /// <summary>推荐线体</summary>
    public string RecommendedLine { get; set; } = "";

    /// <summary>投产数量 = ⌈订单量 ÷ 良率⌉（多投的那部分是给不良的补偿）</summary>
    public int InputQty { get; set; }

    /// <summary>采用的良率（0-1）</summary>
    public double Yield { get; set; }

    /// <summary>采用的单位工时（h/只）</summary>
    public double UnitHours { get; set; }

    /// <summary>本单所需工时（h）</summary>
    public double RequiredHours { get; set; }

    /// <summary>建议开工时间</summary>
    public DateTime PlannedStart { get; set; }

    /// <summary>预计完工时间</summary>
    public DateTime PlannedFinish { get; set; }

    /// <summary>交期余量（天）：＜0 表示来不及</summary>
    public double SlackDays { get; set; }

    /// <summary>风险等级：正常 / 偏紧 / 来不及</summary>
    public string Risk { get; set; } = ScheduleRisk.Normal;

    /// <summary>风险说明与处置建议</summary>
    public string RiskText { get; set; } = "";

    /// <summary>备选线体（推荐线来不及时的第二条路）</summary>
    public string AlternateLine { get; set; } = "";

    /// <summary>备选线体的理由</summary>
    public string AlternateReason { get; set; } = "";

    // ---- 时间线（与"模拟插单"共用的唯一排产口径，2026-10-20 统一） ----

    /// <summary>推荐线体的时间线（在制工单排队 + 换型；队尾即本单开工点）</summary>
    public LineTimeline? Timeline { get; set; }

    /// <summary>本单进入队尾时的换型分钟（来自换型矩阵，与队尾型号相关）</summary>
    public int IncomingChangeoverMinutes { get; set; }

    /// <summary>
    /// 若允许插队（老单顺延）的最早完工时间。
    /// 这是与「模拟插单」对照的基线：插单方案一的紧急单完工必须等于它。
    /// </summary>
    public DateTime? PlannedFinishIfJumpQueue { get; set; }

    /// <summary>插队相对排队可提前的天数（≥0；0 表示线体空闲，插队无收益）</summary>
    public double JumpQueueGainDays => PlannedFinishIfJumpQueue is null
        ? 0 : Math.Max(0, (PlannedFinish - PlannedFinishIfJumpQueue.Value).TotalDays);

    public string IncomingChangeoverText => IncomingChangeoverMinutes <= 0 ? "无需换型" : $"换型 {IncomingChangeoverMinutes} 分钟";
    public string JumpQueueText => PlannedFinishIfJumpQueue is null
        ? "—" : $"插队最早 {PlannedFinishIfJumpQueue:MM-dd HH:mm}（可提前 {JumpQueueGainDays:0.#} 天，代价是老单顺延）";

    // ---- 推理依据（可解释） ----
    /// <summary>三级选线评估（2.1 同型号在制优先 / 2.2 六维工艺相似度评分 / 换型指导书）</summary>
    public LineMatchEvaluation? LineMatch { get; set; }

    public ProductionHistory History { get; set; } = new();

    /// <summary>推荐线体的负载情况</summary>
    public LineLoadInfo LineLoad { get; set; } = new();

    /// <summary>推荐线体上在手的工单号（说明"为什么要等"）</summary>
    public List<string> LineOpenWoNos { get; set; } = new();

    /// <summary>推理过程逐条说明（界面"依据"区直接绑定）</summary>
    public List<string> Evidence { get; set; } = new();

    // ---- 齐套校验 ----
    public MaterialCheckResult StockCheck { get; set; } = new();

    /// <summary>
    /// 资源校验结果（物料齐套 + 资产占用冲突）。
    /// 2026-10-08 起排产也校验资产占用——只查物料不查设备，会排出"纸面上能做、实际撞车"的计划。
    /// </summary>
    public ResourceCheckResult? ResourceCheck { get; set; }

    /// <summary>
    /// **资材占用校验（本项目"占用"的主体）**：按「工单 + BOM 层级」展开，
    /// 再跨工单按物料累加，算出净缺口（= 需采购量）。
    /// 2026-10-08 口径纠偏：占用对象是 BOM 里的资材，不是设备资产。
    /// </summary>
    public MaterialOccupancyCheck MaterialOccupancy { get; set; } = new();

    /// <summary>是否齐备（物料覆盖 + 无资材缺口）</summary>
    public bool MaterialOccupiedReady => MaterialOccupancy.AllSatisfied;

    /// <summary>需采购的物料种类数（跨工单汇总后的净缺口项数）</summary>
    public int PurchaseItemCount => MaterialOccupancy.ShortageCount;

    /// <summary>是否可以做（缺料 = 不建议立刻开工；但允许人工决策后照建）</summary>
    public bool MaterialReady => StockCheck.AllSatisfied;

    /// <summary>一句话结论（页面顶部大字）</summary>
    public string Headline
    {
        get
        {
            if (StockCheck.NoBom)
                return $"{PoNo}｜{ProductModel}：暂无 BOM，无法校验物料，排产参数如下（仅供参考）。";
            if (!MaterialReady)
                return $"{PoNo}｜{ProductModel}：{StockCheck.ShortCount} 项物料缺料，建议先补料；"
                       + $"按现有产能预计 {PlannedFinish:MM-dd} 完工，交期 {DueDate:MM-dd}。";
            return $"{PoNo}｜{ProductModel}：物料齐套，建议排到 {RecommendedLine}，"
                   + $"计划 {PlannedStart:MM-dd} 开工、{PlannedFinish:MM-dd} 完工，"
                   + $"交期 {DueDate:MM-dd}（{SlackText}）。";
        }
    }

    public string SlackText => SlackDays >= 0
        ? $"余量 {SlackDays:0.#} 天"
        : $"已超期 {Math.Abs(SlackDays):0.#} 天";

    public string InputQtyText => $"{InputQty} 只（订单 {OrderQty} 只 ÷ 良率 {Yield * 100:0.0}%）";

    public string RequiredHoursText => $"{RequiredHours:0.#} h（约 {RequiredHours / SchedulingRules.DailyCapacityHours:0.#} 个生产日）";
}
