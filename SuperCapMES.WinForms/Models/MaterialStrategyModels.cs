namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 替代料 + 安全库存补货策略 —— 模型层（技术方案第 4 章追加项）
//
// 替代料要回答的是："主料不够，能不能用另一种料顶上？顶多少？剩余还缺多少？"
//   三个前置条件缺一不可：① 替代关系成立（用量系数）；② **客户/工艺已认可**；③ 替代料自己有货。
//   很多厂栽在 ②上——仓库里有替代料、系统"能顶"，但客户没签字，做出去是批量退货。
//   所以本模块把"认可状态"做成硬门槛：待客户批准的替代关系只展示、不参与顶缺口。
//
// 安全库存补货要回答的是："这个料什么时候该下单、下多少？"
//   补货点 ROP = 日均消耗 × 采购提前期 + 安全库存
//     含义：在等料的这段时间里会消耗掉多少，再加上安全线——低于它就必须下单，否则会断料。
//   建议补货量 = ROP − 净库存（可用 + 在途 + 草稿），按 MPQ 向上取整、不超单次限额。
//   净库存把**草稿申请**也算进来：草稿虽不算在途（货不会来），但重复下单同样是浪费，
//   这里选择"算进来但标注"，与缺料校验页的口径差异在界面上写明。
// ============================================================================

/// <summary>替代关系认可状态（硬门槛：只有「已认可」才允许参与顶缺口）</summary>
public static class SubstituteApproval
{
    /// <summary>已认可：客户/工艺已签字，可直接投产使用</summary>
    public const string Approved = "已认可";

    /// <summary>待客户批准：关系成立但不能用，只展示不顶缺口</summary>
    public const string Pending = "待客户批准";

    /// <summary>客户否决：不可用</summary>
    public const string Rejected = "客户否决";

    public static readonly string[] All = { Approved, Pending, Rejected };
}

/// <summary>物料替代关系（一条 = 主料可用某替代料）。唯一键 = MainCode + SubCode。</summary>
public class MaterialSubstitute
{
    public string Id { get; set; } = "";
    public string MainCode { get; set; } = "";
    public string MainName { get; set; } = "";
    public string SubCode { get; set; } = "";
    public string SubName { get; set; } = "";

    /// <summary>用量系数：用 1 单位主料的位置需要多少替代料（1.05 = 多耗 5%）</summary>
    public double Ratio { get; set; } = 1.0;

    /// <summary>优先级（1 = 优先使用）</summary>
    public int Priority { get; set; } = 1;

    /// <summary>认可状态（见 <see cref="SubstituteApproval"/>）</summary>
    public string Approval { get; set; } = SubstituteApproval.Approved;

    /// <summary>适用条件（如"客户书面同意后方可使用""仅用于非安全件"）</summary>
    public string Condition { get; set; } = "";

    public string Remark { get; set; } = "";

    /// <summary>是否可用于顶缺口（只有「已认可」才算）</summary>
    public bool Usable => string.Equals(Approval, SubstituteApproval.Approved, StringComparison.Ordinal);

    public string RatioText => Ratio <= 0 ? "1:1" : $"1 : {Ratio:0.###}";
    public string PriorityText => $"P{Priority}";
    public string ApprovalText => Usable ? Approval : $"{Approval}（不可用）";
}

/// <summary>替代齐套试算的单行（一条 = 一个 BOM 物料）</summary>
public class SubstituteRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>需求量（含损耗，按投产数展开）</summary>
    public double Requirement { get; set; }

    /// <summary>主料可用量</summary>
    public double Available { get; set; }

    /// <summary>主料缺口 = max(0, 需求 − 可用)</summary>
    public double Shortage { get; set; }

    /// <summary>命中的替代料编码（无替代为空）</summary>
    public string SubCode { get; set; } = "";
    public string SubName { get; set; } = "";

    /// <summary>替代料的库存可用量</summary>
    public double SubAvailable { get; set; }

    /// <summary>替代料的用量系数</summary>
    public double Ratio { get; set; }

    /// <summary>实际替代量（按主料口径）= min(缺口, 替代可用 ÷ 系数)</summary>
    public double AppliedQty { get; set; }

    /// <summary>替代后剩余缺口</summary>
    public double RemainShortage { get; set; }

    /// <summary>替代关系认可状态</summary>
    public string Approval { get; set; } = "";

    /// <summary>说明（为什么没用 / 用了多少）</summary>
    public string Note { get; set; } = "";

    /// <summary>是否真的应用了替代</summary>
    public bool Applied => AppliedQty > 1e-9;

    public string RequirementText => $"{Requirement:0.##} {Unit}";
    public string AvailableText => $"{Available:0.##} {Unit}";
    public string ShortageText => Shortage > 1e-9 ? $"{Shortage:0.##} {Unit}" : "—";
    public string SubAvailableText => SubCode.Length == 0 ? "—" : $"{SubAvailable:0.##} {Unit}";
    public string AppliedText => Applied ? $"{AppliedQty:0.##} {Unit}" : "—";
    public string RemainText => RemainShortage > 1e-9 ? $"{RemainShortage:0.##} {Unit}" : "满足";

    public string StatusText => Shortage <= 1e-9 ? "主料满足"
        : Applied ? (RemainShortage > 1e-9 ? "部分替代" : "替代后满足")
        : "无可用替代";
}

/// <summary>替代齐套试算结果</summary>
public class SubstituteCheckResult
{
    public string ProductModel { get; set; } = "";
    public int Qty { get; set; }
    public int InputQty { get; set; }
    public List<SubstituteRow> Rows { get; set; } = new();

    /// <summary>替代前短缺项数 / 缺口合计</summary>
    public int ShortBefore => Rows.Count(r => r.Shortage > 1e-9);
    public double QtyBefore => Rows.Where(r => r.Shortage > 1e-9).Sum(r => r.Shortage);

    /// <summary>替代后仍短缺项数 / 缺口合计</summary>
    public int ShortAfter => Rows.Count(r => r.RemainShortage > 1e-9);
    public double QtyAfter => Rows.Sum(r => r.RemainShortage);

    /// <summary>实际被替代的项数</summary>
    public int AppliedCount => Rows.Count(r => r.Applied);

    /// <summary>因认可状态被挡下的项数</summary>
    public int BlockedByApproval => Rows.Count(r => r.Shortage > 1e-9 && !r.Applied && r.SubCode.Length > 0);

    public string SummaryText =>
        $"{ProductModel} × {Qty} 只（投产 {InputQty} 只）：替代前短缺 {ShortBefore} 项 / {QtyBefore:0.##}，"
        + $"替代后 {ShortAfter} 项 / {QtyAfter:0.##}"
        + (AppliedCount > 0 ? $"；{AppliedCount} 项已用替代料顶替" : "")
        + (BlockedByApproval > 0 ? $"；{BlockedByApproval} 项因「认可状态 / 无库存」无法替代" : "");
}

/// <summary>补货状态</summary>
public static class ReplenishStatus
{
    /// <summary>紧急：净库存已低于安全库存</summary>
    public const string Urgent = "紧急补货";

    /// <summary>需补货：低于补货点（ROP）</summary>
    public const string Need = "需补货";

    /// <summary>正常：高于补货点</summary>
    public const string Ok = "正常";

    /// <summary>呆滞/无消耗：没有消耗依据，不做补货建议</summary>
    public const string Idle = "无消耗依据";

    public static readonly string[] All = { Urgent, Need, Ok, Idle };
}

/// <summary>安全库存补货行（一个物料一行）</summary>
public class ReplenishmentRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "";

    /// <summary>日均消耗（双口径：领用实测 / 工单需求）</summary>
    public double DailyConsumption { get; set; }

    /// <summary>消耗依据来源</summary>
    public string ConsumptionSource { get; set; } = AgeSources.None;

    /// <summary>采购提前期（L/T + 检验）</summary>
    public int LeadDays { get; set; }

    /// <summary>安全库存</summary>
    public double SafetyStock { get; set; }

    /// <summary>可用库存</summary>
    public double Available { get; set; }

    /// <summary>在途（待审批/已批准/已下单）</summary>
    public double InTransit { get; set; }

    /// <summary>草稿申请量（未提交，也算"已有人管"，避免重复下单）</summary>
    public double DraftQty { get; set; }

    /// <summary>最小起订量</summary>
    public double Mpq { get; set; }

    /// <summary>单次采购限额（0 = 不限）</summary>
    public double MaxPurchaseQty { get; set; }

    /// <summary>补货点 ROP = 日均 × 提前期 + 安全库存</summary>
    public double Rop { get; set; }

    /// <summary>建议补货量（按 MPQ 取整、不超限额）</summary>
    public double SuggestQty { get; set; }

    /// <summary>补货状态</summary>
    public string Status { get; set; } = ReplenishStatus.Ok;

    /// <summary>建议下单日（正常时为"预计跌破补货点"的日期）</summary>
    public DateTime? SuggestOrderDate { get; set; }

    /// <summary>净库存 = 可用 + 在途 + 草稿</summary>
    public double NetStock => Available + InTransit + DraftQty;

    /// <summary>距补货点的差额（负数 = 已低于补货点）</summary>
    public double GapToRop => NetStock - Rop;

    /// <summary>是否需下单</summary>
    public bool NeedsOrder => Status is ReplenishStatus.Urgent or ReplenishStatus.Need;

    /// <summary>是否由 MPQ 抬高过建议量</summary>
    public bool MpqRounded { get; set; }

    public string QtyText => $"{Available:0.##} {Unit}";
    public string NetText => $"{NetStock:0.##} {Unit}";
    public string RopText => $"{Rop:0.##} {Unit}";
    public string SafetyText => $"{SafetyStock:0.##} {Unit}";
    public string SuggestText => SuggestQty > 1e-9
        ? $"{SuggestQty:0.##} {Unit}" + (MpqRounded ? $"（MPQ {Mpq:0.#} 取整）" : "")
        : "—";

    public string DailyText => ConsumptionSource == AgeSources.None
        ? "无依据"
        : $"{DailyConsumption:0.##} {Unit}/天（{ConsumptionSource}）";

    public string LeadText => $"{LeadDays} 天";
    public string DateText => SuggestOrderDate?.ToString("yyyy-MM-dd") ?? "—";

    /// <summary>结论说明</summary>
    public string VerdictText
    {
        get
        {
            if (Status == ReplenishStatus.Idle)
                return SafetyStock > 1e-9 && Available < SafetyStock
                    ? $"无消耗依据，但可用 {Available:0.##} 已低于安全库存 {SafetyStock:0.##}，按安全库存补齐。"
                    : "既无领用记录也无在手需求，不做补货建议（呆滞风险另行评估）。";
            if (Status == ReplenishStatus.Urgent)
                return $"净库存 {NetStock:0.##} 已低于安全库存 {SafetyStock:0.##}，立即下单 {SuggestQty:0.##} {Unit}"
                     + $"（提前期 {LeadDays} 天，等料期间会断料）。";
            if (Status == ReplenishStatus.Need)
                return $"净库存 {NetStock:0.##} 低于补货点 {Rop:0.##}（日均 {DailyConsumption:0.##} × 提前期 {LeadDays} 天 + 安全 {SafetyStock:0.##}），"
                     + $"建议下单 {SuggestQty:0.##} {Unit}。";
            return $"净库存 {NetStock:0.##} 高于补货点 {Rop:0.##}，预计 {SuggestOrderDate:yyyy-MM-dd} 跌破补货点，可暂缓。";
        }
    }
}

/// <summary>补货看板</summary>
public class ReplenishmentBoard
{
    public List<ReplenishmentRow> Rows { get; set; } = new();

    public int Total => Rows.Count;
    public int UrgentCount => Rows.Count(r => r.Status == ReplenishStatus.Urgent);
    public int NeedCount => Rows.Count(r => r.Status == ReplenishStatus.Need);
    public int IdleCount => Rows.Count(r => r.Status == ReplenishStatus.Idle);
    public int OrderCount => Rows.Count(r => r.NeedsOrder);

    public string SummaryText =>
        $"{Total} 种物料：紧急补货 {UrgentCount} / 需补货 {NeedCount} / 正常 {Total - UrgentCount - NeedCount - IdleCount}"
        + (IdleCount > 0 ? $" / 无消耗依据 {IdleCount}" : "");
}
