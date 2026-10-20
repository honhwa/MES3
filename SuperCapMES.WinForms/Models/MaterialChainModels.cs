namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 物料全链管控（一期 3.1）—— 缺料三级校验 + 采购前置期 —— 模型层
//
// 为什么要把"缺料"拆成三级，而不是给一个缺口数字：
//   一级 需求合并：几工单共用一批料，各自算缺口会导致同一批料被重复申请（采购最贵的错）；
//   二级 扣减    ：可用库存 / 在途采购 / 待检冻结 / 安全库存，四个口径必须分开列，
//                  否则审批人看不出"为什么缺口是这个数"；
//   三级 净缺口  ：净缺口 → 建议采购量（含损耗 + MPQ 取整 + 限额钳制）→ 落地成请购量。
//
// 采购前置期（Lead Time）解决的是另一半问题：**什么时候必须下单**。
//   需求日 = 最早开工日 − 到货缓冲（默认开工前 3 天）
//   最晚下单日 = 需求日 − (供应商 L/T + 来料检验缓冲)
//   距下单天数 < 0 → 已逾期（再怎么催也赶不上开工，只能插单/让步接收/换料）
//   这条时间线是"缺料 → 请购"之间唯一能提前预警的抓手，所以单列成三色状态。
//
// 演示口径：L/T、检验缓冲、MPQ 为**演示参数**（按物料类别 + 编码稳定哈希推导，
// 同一天反复打开结果一致）。真实环境这三个数来自 ERP 供应商主数据与采购框架协议，
// 界面上必须写明来源为演示参数，不能让人以为是真实合同周期。
// ============================================================================

/// <summary>下单时点状态（三色：逾期 / 临近 / 充裕）</summary>
public static class OrderTiming
{
    /// <summary>已逾期：最晚下单日已过，按标准 L/T 已赶不上开工</summary>
    public const string Late = "逾期";

    /// <summary>临近：距最晚下单日 ≤ 7 天</summary>
    public const string Near = "临近";

    /// <summary>充裕：距最晚下单日 &gt; 7 天</summary>
    public const string Ample = "充裕";

    /// <summary>无需下单（无缺口）</summary>
    public const string None = "无需采购";

    public static readonly string[] All = { Late, Near, Ample, None };
}

/// <summary>缺料校验的显示范围</summary>
public static class ChainFilters
{
    public const string ShortOnly = "仅短缺";
    public const string ShortAndTight = "短缺 + 紧张";
    public const string All = "全部物料";

    public static readonly string[] AllItems = { ShortOnly, ShortAndTight, All };
}

/// <summary>
/// 物料采购前置期参数（演示参数，按物料编码稳定推导；真实环境取自 ERP 供应商主数据）。
/// 唯一键 = MaterialCode。
/// </summary>
public class MaterialProcureParam
{
    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>计量单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>物料类别：原料 / 辅料 / 成品</summary>
    public string Category { get; set; } = "";

    /// <summary>供应商交货周期（天）：下单 → 到货</summary>
    public int LeadTimeDays { get; set; }

    /// <summary>来料检验缓冲（天）：到货后 IQC 判定所需时间</summary>
    public int InspectionDays { get; set; }

    /// <summary>到货缓冲（天）：要求物料在开工前多少天到货（默认 3 天）</summary>
    public int ArriveBufferDays { get; set; } = 3;

    /// <summary>最小起订量 MPQ（0 = 不限制）</summary>
    public double MinOrderQty { get; set; }

    /// <summary>主供应商（取自最近一次入库批次）</summary>
    public string Supplier { get; set; } = "";

    /// <summary>采购总前置期 = L/T + 检验缓冲</summary>
    public int TotalLeadDays => LeadTimeDays + InspectionDays;

    public string LeadText => $"L/T {LeadTimeDays} + 检验 {InspectionDays} = {TotalLeadDays} 天";
    public string MpqText => MinOrderQty > 1e-9 ? $"MPQ {MinOrderQty:0.#} {Unit}" : "MPQ 不限";
}

/// <summary>
/// 缺料三级校验行（一种物料一行）。
/// 一级需求合并 → 二级扣减 → 三级净缺口 → 采购前置期与最晚下单日。
/// </summary>
public class ShortageChainRow
{
    // ---- 物料标识 ----
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string MaterialType { get; set; } = "";

    // ---- 一级：需求合并 ----
    /// <summary>占着它的工单数</summary>
    public int WorkOrderCount { get; set; }

    /// <summary>跨工单累加的需求量（= 资材占用总量）</summary>
    public double Requirement { get; set; }

    /// <summary>占用来源明细（哪几张工单、各占多少、时间窗）</summary>
    public List<MaterialOccupancy> Details { get; set; } = new();

    // ---- 二级：扣减 ----
    /// <summary>可用库存（不含待检区 / 退货待处理区）</summary>
    public double Available { get; set; }

    /// <summary>在途采购量（待审批 / 已批准 / 已下单）</summary>
    public double InTransit { get; set; }

    /// <summary>
    /// 草稿采购量（草稿 / 已驳回：人在流程里但**还没提交出去**）。
    /// 它不算在途（口径与系统一致：草稿未提交，货一定不会来），
    /// 但必须显示出来——否则"刚建完单回头看缺口还在"会让人以为系统没记住。
    /// </summary>
    public double DraftQty { get; set; }

    /// <summary>待检 / 退货等冻结量（不计入可用，仅提示）</summary>
    public double FrozenQty { get; set; }

    /// <summary>安全库存（补到安全线才算够）</summary>
    public double SafetyStock { get; set; }

    // ---- 三级：净缺口 ----
    /// <summary>净缺口 = max(0, 需求 − 可用 − 在途)</summary>
    public double NetShortage { get; set; }

    /// <summary>
    /// 未被覆盖的缺口 = max(0, 净缺口 − 草稿采购量)。
    /// 这才是"还需要新买多少"——已有草稿覆盖的部分不该再建一张单。
    /// </summary>
    public double UncoveredShortage => Math.Max(0, NetShortage - DraftQty);

    /// <summary>安全库存缺口 = max(0, 安全库存 − 可用)</summary>
    public double SafetyGap { get; set; }

    /// <summary>建议采购量 = 向上取整((净缺口 + 安全库存缺口) × (1 + 损耗系数), MPQ)，并钳制在 [净缺口, 需求×1.2 与限额取小]</summary>
    public double SuggestedQty { get; set; }

    /// <summary>校验等级：满足 / 紧张 / 短缺（复用 ShortageStatus 口径）</summary>
    public string VerifyLevel { get; set; } = ShortageStatus.Ok;

    // ---- 采购前置期 ----
    public int LeadTimeDays { get; set; }
    public int InspectionDays { get; set; }
    public int ArriveBufferDays { get; set; }

    /// <summary>需求日 = 最早开工日 − 到货缓冲</summary>
    public DateTime DemandDate { get; set; }

    /// <summary>最晚下单日 = 需求日 − (L/T + 检验缓冲)</summary>
    public DateTime LatestOrderDate { get; set; }

    /// <summary>距最晚下单日还剩几天（负数 = 已逾期）</summary>
    public int DaysLeft { get; set; }

    /// <summary>下单时点状态（逾期 / 临近 / 充裕 / 无需采购）</summary>
    public string TimingStatus { get; set; } = OrderTiming.None;

    /// <summary>最小起订量</summary>
    public double MinOrderQty { get; set; }

    /// <summary>建议供应商</summary>
    public string Supplier { get; set; } = "";

    /// <summary>建议采购量是否被 MPQ 取整抬高</summary>
    public bool MpqRounded { get; set; }

    /// <summary>建议采购量是否被超量线 / 采购限额钳制</summary>
    public bool Clamped { get; set; }

    /// <summary>单行结论（一句话，直接可读）</summary>
    public string Advice { get; set; } = "";

    // ---- 派生显示 ----

    /// <summary>是否需要采购（净缺口 &gt; 0）</summary>
    public bool NeedsPurchase => NetShortage > 1e-9;

    /// <summary>一级：需求合并</summary>
    public string MergeText => WorkOrderCount <= 0
        ? "—"
        : $"{WorkOrderCount} 张工单合并 {Requirement:0.##} {Unit}";

    /// <summary>二级：扣减明细</summary>
    public string DeductText
    {
        get
        {
            var parts = new List<string> { $"可用 {Available:0.##}" };
            if (InTransit > 1e-9) parts.Add($"在途 {InTransit:0.##}");
            if (DraftQty > 1e-9) parts.Add($"草稿 {DraftQty:0.##}");
            if (FrozenQty > 1e-9) parts.Add($"冻结 {FrozenQty:0.##}");
            if (SafetyStock > 1e-9) parts.Add($"安全 {SafetyStock:0.##}");
            return string.Join("｜", parts) + $" {Unit}";
        }
    }

    /// <summary>三级：净缺口</summary>
    public string NetShortageText => NetShortage > 1e-9 ? $"{NetShortage:0.##} {Unit}" : "—";

    /// <summary>建议采购量（紧凑文案：量 + 损耗 / MPQ / 钳制短标记，完整说明见行结论）</summary>
    public string SuggestedText
    {
        get
        {
            if (SuggestedQty <= 1e-9)
                return NeedsPurchase && UncoveredShortage <= 1e-9 ? "—（草稿已覆盖）" : "—";
            var s = $"{SuggestedQty:0.##} {Unit} · 含损耗";
            if (MpqRounded) s += "·MPQ";
            if (Clamped) s += "·已钳制";
            return s;
        }
    }

    /// <summary>前置期</summary>
    public string LeadText => $"{LeadTimeDays}+{InspectionDays} = {LeadTimeDays + InspectionDays} 天";

    public string DemandDateText => NeedsPurchase ? DemandDate.ToString("MM-dd") : "—";
    public string LatestOrderText => NeedsPurchase ? LatestOrderDate.ToString("MM-dd") : "—";

    /// <summary>距下单（人话）</summary>
    public string DaysLeftText
    {
        get
        {
            if (!NeedsPurchase) return "—";
            if (DaysLeft < 0) return $"已逾期 {-DaysLeft} 天";
            return DaysLeft == 0 ? "今天必须下单" : $"剩 {DaysLeft} 天";
        }
    }

    /// <summary>等级显示（避免空串）</summary>
    public string VerifyLevelText => VerifyLevel switch
    {
        ShortageStatus.Short => "短缺",
        ShortageStatus.Tight => "紧张",
        _ => "满足"
    };
}

/// <summary>缺料三级校验结果（一行 = 一种物料；Summary 按**未过滤**的全量统计）</summary>
public class ShortageChainResult
{
    /// <summary>范围内全部物料（未按显示过滤）</summary>
    public List<ShortageChainRow> All { get; set; } = new();

    /// <summary>按显示范围过滤后的行（界面绑定这一份）</summary>
    public List<ShortageChainRow> Rows { get; set; } = new();

    /// <summary>参与合并的工单数</summary>
    public int WorkOrderCount { get; set; }

    /// <summary>显示范围文案</summary>
    public string ScopeText { get; set; } = "";

    /// <summary>当前显示过滤</summary>
    public string Filter { get; set; } = ChainFilters.ShortAndTight;

    public int MaterialCount => All.Count;
    public int ShortCount => All.Count(r => r.NeedsPurchase);
    public int TightCount => All.Count(r => !r.NeedsPurchase && r.VerifyLevel == ShortageStatus.Tight);

    /// <summary>需要采购且已过最晚下单日的物料数</summary>
    public int LateCount => All.Count(r => r.NeedsPurchase && r.TimingStatus == OrderTiming.Late);

    /// <summary>需要采购且 7 天内必须下单的物料数</summary>
    public int NearCount => All.Count(r => r.NeedsPurchase && r.TimingStatus == OrderTiming.Near);

    /// <summary>有建议采购量的物料数</summary>
    public int SuggestItemCount => All.Count(r => r.SuggestedQty > 1e-9);

    public string SummaryText =>
        $"{ScopeText}：{MaterialCount} 种物料，短缺 {ShortCount} 项 / 紧张 {TightCount} 项"
        + (LateCount > 0 ? $"；{LateCount} 项已过最晚下单日" : "")
        + (NearCount > 0 ? $"；{NearCount} 项 7 天内须下单" : "");
}
