namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 资材采购申请（请购单）—— 模型层
//
// 它要解决的是排产链路上最关键的一段断点：
//   齐套校验能告诉你"缺铝壳 2842 只"，但**然后呢**？
//   系统原先只到"报缺料"为止，没有"找谁补、补多少、批不批、什么时候到"的出口。
//   本单据就是那个出口——把缺料转成可流转、可追溯的申请单，与工单绑定。
//
// 与既有单据的分工：
//   · 采购申请（本单据）：**请购**方向，对外产生采购需求，尚未收货
//   · 采购入库（WmsOrder，IsInbound）：**收货**方向，货已到、过账入账
//   两者是上下游，不能复用同一张单——状态语义完全不同。
// ============================================================================

/// <summary>采购申请状态。</summary>
public static class PurchaseRequestStatus
{
    /// <summary>草稿（可编辑、可删除）</summary>
    public const string Draft = "草稿";

    /// <summary>待审批（已提交，等待审批）</summary>
    public const string Submitted = "待审批";

    /// <summary>已批准（等待向供应商下单）</summary>
    public const string Approved = "已批准";

    /// <summary>已驳回（退回申请人修改，可改后重新提交）</summary>
    public const string Rejected = "已驳回";

    /// <summary>已下单（已向供应商下达订单）</summary>
    public const string Ordered = "已下单";

    /// <summary>已关闭（入库完成或已取消）</summary>
    public const string Closed = "已关闭";

    public static readonly string[] All = { Draft, Submitted, Approved, Rejected, Ordered, Closed };

    /// <summary>终态（不可再流转）</summary>
    public static bool IsFinal(string? status) =>
        string.Equals(status, Closed, StringComparison.Ordinal);

    /// <summary>是否处于可编辑状态（只有草稿与已驳回能改明细）</summary>
    public static bool IsEditable(string? status) =>
        string.Equals(status, Draft, StringComparison.Ordinal)
        || string.Equals(status, Rejected, StringComparison.Ordinal);

    /// <summary>
    /// 是否"在途"：待审批 / 已批准 / 已下单——已过请购流程、货还没到。
    /// <b>已关闭不算在途</b>：关闭表示这次请购事务结束（取消 / 减产 / 另寻渠道），
    /// 但货并没有到，物料缺口依然存在。如果把已关闭也算成在途，缺口会被永久抹平——
    /// 工单永远显示不出还缺料、也再也没法生成新的申请单，这在采购环节是致命 bug。
    /// </summary>
    public static bool IsEffective(string? status) =>
        string.Equals(status, Submitted, StringComparison.Ordinal)
        || string.Equals(status, Approved, StringComparison.Ordinal)
        || string.Equals(status, Ordered, StringComparison.Ordinal);
}

/// <summary>允许的状态流转动作（页面上就是这几个按钮）。</summary>
public static class PurchaseRequestActions
{
    /// <summary>提交：草稿 / 已驳回 → 待审批</summary>
    public const string Submit = "提交";

    /// <summary>批准：待审批 → 已批准</summary>
    public const string Approve = "批准";

    /// <summary>驳回：待审批 → 已驳回</summary>
    public const string Reject = "驳回";

    /// <summary>下单：已批准 → 已下单</summary>
    public const string Order = "下单";

    /// <summary>关闭：已批准 / 已下单 → 已关闭</summary>
    public const string Close = "关闭";

    public static readonly string[] All = { Submit, Approve, Reject, Order, Close };

    /// <summary>该状态下允许执行的动作；不允许返回空集。</summary>
    public static string[] AllowedIn(string? status) => status switch
    {
        PurchaseRequestStatus.Draft => new[] { Submit },
        PurchaseRequestStatus.Rejected => new[] { Submit },
        PurchaseRequestStatus.Submitted => new[] { Approve, Reject },
        PurchaseRequestStatus.Approved => new[] { Order, Close },
        PurchaseRequestStatus.Ordered => new[] { Close },
        PurchaseRequestStatus.Closed => Array.Empty<string>(),
        _ => Array.Empty<string>()
    };

    /// <summary>该状态是否允许执行指定动作。</summary>
    public static bool CanDo(string? status, string action) =>
        AllowedIn(status).Contains(action, StringComparer.Ordinal);
}

/// <summary>
/// 采购申请明细行。
/// 需求量 / 可用量 / 缺口三项都保留下来，而不是只留一个"申请量"——
/// 审批人要看的就是"凭什么是这个数"，没有这三列他没法判断。
/// </summary>
public class PurchaseRequestLine
{
    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>规格</summary>
    public string Spec { get; set; } = "";

    /// <summary>计量单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>物料类型：主材 / 辅材 / 包材</summary>
    public string MaterialType { get; set; } = "";

    /// <summary>需求公式原文（如 "0.0125 × (1 + 1.5%) × 投产数"），让人能核对</summary>
    public string FormulaText { get; set; } = "";

    /// <summary>需求量（含损耗，按工单投产数展开 BOM）</summary>
    public double Requirement { get; set; }

    /// <summary>可用库存（不含待检区 / 退货待处理区）</summary>
    public double Available { get; set; }

    /// <summary>待检/退货等不可用量（提示"还有一批没判"，不计入可用）</summary>
    public double FrozenQty { get; set; }

    /// <summary>缺口 = max(0, 需求 − 可用)</summary>
    public double Shortage => Math.Max(0, Requirement - Available);

    /// <summary>实际申请量（默认等于缺口，审批人可调整）</summary>
    public double RequestQty { get; set; }

    /// <summary>建议供应商（取自该物料最近一次入库批次的供应商，可改）</summary>
    public string Supplier { get; set; } = "";

    /// <summary>行备注</summary>
    public string Remark { get; set; } = "";

    public string RequirementText => $"{Requirement:0.##}";
    public string AvailableText => $"{Available:0.##}";
    public string ShortageText => $"{Shortage:0.##}";
    public string RequestQtyText => $"{RequestQty:0.##}";
    public string SupplierText => string.IsNullOrWhiteSpace(Supplier) ? "（待定）" : Supplier;

    /// <summary>是否仍有缺口（审批人最关心：批了还缺不缺）</summary>
    public bool StillShort => Shortage > 1e-9;
}

/// <summary>
/// 资材采购申请单（请购单）。单号形如 PR-20261008-001（前缀-日期-序号）。
/// 一张申请单对应**一张工单**（<see cref="WoNo"/>），不跨工单合并——
/// 跨单合并会让"这张工单还缺什么"追不回来，而那正是排产要回答的问题。
/// </summary>
public class PurchaseRequest
{
    /// <summary>申请单号，形如 PR-20261008-001</summary>
    public string ReqNo { get; set; } = "";

    /// <summary>来源工单号（缺料的那张工单）</summary>
    public string WoNo { get; set; } = "";

    /// <summary>来源 ERP 订单号（工单关联时有）</summary>
    public string ErpOrderNo { get; set; } = "";

    /// <summary>产品型号（便于按型号筛选）</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>状态（见 <see cref="PurchaseRequestStatus"/>）</summary>
    public string Status { get; set; } = PurchaseRequestStatus.Draft;

    /// <summary>申请人</summary>
    public string Applicant { get; set; } = "";

    /// <summary>创建时间</summary>
    public DateTime CreateTime { get; set; }

    /// <summary>提交时间（未提交为 null）</summary>
    public DateTime? SubmitTime { get; set; }

    /// <summary>审批人（未审批为���）</summary>
    public string? Approver { get; set; }

    /// <summary>审批时间</summary>
    public DateTime? ApproveTime { get; set; }

    /// <summary>审批意见（驳回时必填，说明要改什么）</summary>
    public string ApprovalRemark { get; set; } = "";

    /// <summary>备注</summary>
    public string Remark { get; set; } = "";

    public List<PurchaseRequestLine> Lines { get; set; } = new();

    /// <summary>是否工单缺料自动生成（手工建的可为 false）</summary>
    public bool AutoCreated { get; set; }

    // ---- 派生显示 ----

    public int LineCount => Lines.Count;
    public int ShortageLineCount => Lines.Count(l => l.StillShort);

    /// <summary>合计申请量（同单位物料混加仅作规模感参考，明细以行为准）</summary>
    public string TotalQtyText => Lines.Count == 0
        ? "—"
        : $"{Lines.Sum(l => l.RequestQty):0.##}（{Lines.Count} 项）";

    /// <summary>需求缺口的简短描述（列表页用）</summary>
    public string ShortageBrief
    {
        get
        {
            var shortLines = Lines.Where(l => l.StillShort).ToList();
            return shortLines.Count == 0
                ? "无缺口"
                : string.Join("、", shortLines.Take(3).Select(l => $"{l.MaterialName} 缺 {l.ShortageText}{l.Unit}"))
                  + (shortLines.Count > 3 ? $" 等 {shortLines.Count} 项" : "");
        }
    }

    public string CreateTimeText => CreateTime == default ? "" : CreateTime.ToString("yyyy-MM-dd HH:mm");
    public string SubmitTimeText => SubmitTime is null ? "" : SubmitTime.Value.ToString("yyyy-MM-dd HH:mm");
    public string ApproveTimeText => ApproveTime is null ? "" : ApproveTime.Value.ToString("yyyy-MM-dd HH:mm");

    /// <summary>状态徽标文案（列表着色用）</summary>
    public string StatusText => Status switch
    {
        PurchaseRequestStatus.Draft => "草稿",
        PurchaseRequestStatus.Submitted => "待审批",
        PurchaseRequestStatus.Approved => "已批准",
        PurchaseRequestStatus.Rejected => "已驳回",
        PurchaseRequestStatus.Ordered => "已下单",
        PurchaseRequestStatus.Closed => "已关闭",
        _ => Status
    };
}