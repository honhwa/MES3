namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 资材占用（Material Occupancy）—— 模型层
//
// ⚠ 口径纠偏（2026-10-08）：本项目的"占用"对象是 **BOM 里的资材**，
//   不是设备/工装那类可复用资产。两者根本不同，别混：
//
//   ┌────────────┬──────────────────────────┬───────────────────────────────┐
//   │            │ 设备/工装（资产占用）     │ 资材（物料占用）★ 本项目主体  │
//   ├────────────┼──────────────────────────┼───────────────────────────────┤
//   │ 性质       │ 可复用，跨工单轮流使用     │ **消耗品，用掉就没了**        │
//   │ 跨工单关系 │ 时间窗错开 → 可复用，不累加│ **无条件累加**（各单都要备料）│
//   │ 时间窗作用 │ 决定"撞不撞得上"           │ 只决定**优先级**与**挤兑提醒** │
//   │ 采购关联   │ 无关                     │ **直接决定采购数量**          │
//   └────────────┴──────────────────────────┴───────────────────────────────┘
//
// 举例说清"累加"：A 工单要 100 只铝壳、B 工单要 200 只铝壳，
//   即使两张单的时间窗完全不重叠，也**必须备 300 只**——铝壳不会用完还在。
//   若按设备那套"时间窗错开就不累加"来算，B 单开工会发现没料，这是致命错账。
//
// 三条设计取舍：
//   ① **按「工单 × 物料」记占用**，不按物料单独记——只有挂在工单上，
//      才能回答"这张工单占了多少料"和"这张料被哪几张单占着"两个方向的问题。
//   ② **工单只占用"剩余待产量"对应的量**：做了一半的工单，做完那部分已经领料了，
//      不该继续占着库存。口径 = 单位用量 × (1+损耗率) × max(0, 计划数 − 完工数)。
//   ③ **占用 ≠ 采购量**：采购量还要再扣掉现有可用库存与在途采购量。
//      三者混用就会重复采购——这是请购环节最贵的错误。
// ============================================================================

/// <summary>资材占用状态</summary>
public static class MaterialOccupancyStatus
{
    /// <summary>已预留：工单已下达，物料已被它占住</summary>
    public const string Reserved = "已预留";

    /// <summary>已领料：料已实际发出，需求转为消耗</summary>
    public const string Issued = "已领料";

    /// <summary>已释放：工单终结/撤销，释放占用</summary>
    public const string Released = "已释放";

    public static readonly string[] All = { Reserved, Issued, Released };

    /// <summary>是否仍占用库存</summary>
    public static bool IsActive(string? status) =>
        string.Equals(status, Reserved, StringComparison.Ordinal)
        || string.Equals(status, Issued, StringComparison.Ordinal);
}

/// <summary>资材占用来源</summary>
public static class MaterialOccupancySource
{
    /// <summary>智能排产推演</summary>
    public const string Scheduling = "排产推演";

    /// <summary>手工建单/改单</summary>
    public const string Manual = "手工建单";

    public static readonly string[] All = { Scheduling, Manual };
}

/// <summary>
/// 资材占用记录（**物料预留表**）：一张工单在某个时间窗内，占用某种物料多少量。
/// 与 <see cref="AssetOccupancy"/> 的关键差异：这里**不做时间窗重叠过滤**，
/// 跨工单无条件累加——物料是消耗品。
/// </summary>
public class MaterialOccupancy
{
    /// <summary>占用记录号（MO-日期-序号）</summary>
    public string Id { get; set; } = "";

    /// <summary>占用工单号</summary>
    public string WoNo { get; set; } = "";

    /// <summary>产品型号（冗余存储，便于按型号查）</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>计量单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>BOM 层级：主材 / 辅材 / 包材</summary>
    public string MaterialType { get; set; } = "";

    /// <summary>单位用量（每只成品）</summary>
    public double QtyPerUnit { get; set; }

    /// <summary>损耗率（%）</summary>
    public double LossRate { get; set; }

    /// <summary>本工单计划数</summary>
    public int PlanQty { get; set; }

    /// <summary>本工单完工数</summary>
    public int FinishedQty { get; set; }

    /// <summary>
    /// **剩余待产量** = max(0, 计划数 − 完工数)。只对这部分计算占用。
    /// </summary>
    public int RemainingQty { get; set; }

    /// <summary>
    /// **占用量** = 单位用量 × (1 + 损耗率/100) × 剩余待产量。
    /// 这就是"BOM 层级"落地的地方：需求量由工单的型号 + BOM + 剩余量三者决定。
    /// </summary>
    public double OccupiedQty { get; set; }

    /// <summary>生产时间窗起（仅用于优先级与挤兑提示，不参与累加判断）</summary>
    public DateTime StartTime { get; set; }

    /// <summary>生产时间窗止</summary>
    public DateTime EndTime { get; set; }

    /// <summary>交期（排序用：交期紧的工单优先分配库存）</summary>
    public DateTime DueDate { get; set; }

    /// <summary>状态（见 <see cref="MaterialOccupancyStatus"/>）</summary>
    public string Status { get; set; } = MaterialOccupancyStatus.Reserved;

    /// <summary>来源（见 <see cref="MaterialOccupancySource"/>）</summary>
    public string Source { get; set; } = MaterialOccupancySource.Manual;

    public DateTime CreatedAt { get; set; }

    /// <summary>乐观锁版本号（并发写占用表时比对，防止超占用）</summary>
    public int Version { get; set; } = 1;

    public string Remark { get; set; } = "";

    /// <summary>占用公式说明（界面上直接亮给用户看）</summary>
    public string FormulaText =>
        $"{QtyPerUnit:0.####} × (1 + {LossRate:0.##}%) × 剩余 {RemainingQty} 只";

    public string OccupiedQtyText => $"{OccupiedQty:0.##} {Unit}";
    public string WindowText => $"{StartTime:yyyy-MM-dd HH:mm} ~ {EndTime:yyyy-MM-dd HH:mm}";
}

/// <summary>同一物料被多张工单占用的明细（跨工单汇总时按物料出一行）</summary>
public class MaterialOccupancyRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string MaterialType { get; set; } = "";

    /// <summary>工单总数（几张单占着它）</summary>
    public int WorkOrderCount { get; set; }

    /// <summary><b>跨工单累加的占用总量</b> = 各工单剩余待产量的 BOM 需求之和</summary>
    public double TotalOccupied { get; set; }

    /// <summary>当前可用库存（不含待检区 / 退货待处理区）</summary>
    public double Available { get; set; }

    /// <summary>待检/退货等不可用量（提示，不参与可用）</summary>
    public double FrozenQty { get; set; }

    /// <summary>在途采购量（待审批/已批准/已下单的申请量）</summary>
    public double InTransit { get; set; }

    /// <summary>可用 + 在途（可满足的总量）</summary>
    public double Covered => Available + InTransit;

    /// <summary><b>净缺口</b> = max(0, 占用总量 − 可用 − 在途) = 需采购量</summary>
    public double NetShortage => Math.Max(0, TotalOccupied - Covered);

    /// <summary>占用状态：满足 / 紧张 / 短缺</summary>
    public string Status { get; set; } = ShortageStatus.Ok;

    /// <summary>占着它的工单明细：工单号 + 数量 + 起止时间（界面按此展开）</summary>
    public List<MaterialOccupancy> Details { get; set; } = new();

    /// <summary>时间窗挤兑提示：多张单在重叠时段抢同一批料（消耗品，累加后才看真缺口）</summary>
    public bool HasTimeCrowding { get; set; }

    public string CrowdingText { get; set; } = "";

    public string AvailableText => $"{Available:0.##} {Unit}";
    public string InTransitText => $"{InTransit:0.##} {Unit}";
    public string TotalText => $"{TotalOccupied:0.##} {Unit}";
    public string ShortageText => NetShortage > 1e-9 ? $"{NetShortage:0.##} {Unit}" : "—";
    public string CoveredText => $"{Covered:0.##} {Unit}";

    /// <summary>占用它的工单数（显示用）</summary>
    public string WoCountText => $"{WorkOrderCount} 张";

    /// <summary>状态显示文本</summary>
    public string StatusText => Status switch
    {
        ShortageStatus.Short => "短缺",
        ShortageStatus.Tight => "紧张",
        _ => "满足"
    };

    /// <summary>占用明细：工单号 + 数量 + 起止时间（按交期排序，最紧的在前）</summary>
    public string DetailsText => Details.Count == 0
        ? "—"
        : string.Join("；", Details.Take(4).Select(d =>
            $"{d.WoNo.TrimEnd('*')} 剩余{d.RemainingQty}只 占{d.OccupiedQtyText} [{d.StartTime:MM-dd}~{d.DueDate:MM-dd}]"))
          + (Details.Count > 4 ? $" 等 {Details.Count} 张工单" : "");

    /// <summary>本物料的完整说明（采购申请、弹窗、报表共用一句话）</summary>
    public string BriefText =>
        $"{MaterialName}：占用 {TotalText}（{WorkOrderCount} 张工单）｜可用 {AvailableText}｜在途 {InTransitText}"
        + (NetShortage > 1e-9 ? $"｜<b>需采购 {ShortageText}</b>" : "｜已覆盖");
}

/// <summary>单张工单的资材占用校验结果（按工单 + BOM 层级展开）</summary>
public class MaterialOccupancyCheck
{
    /// <summary>工单号</summary>
    public string WoNo { get; set; } = "";

    public string ProductModel { get; set; } = "";

    public int PlanQty { get; set; }

    public int FinishedQty { get; set; }

    /// <summary>剩余待产量</summary>
    public int RemainingQty { get; set; }

    public DateTime WindowStart { get; set; }

    public DateTime WindowEnd { get; set; }

    /// <summary>本工单逐物料的占用明细</summary>
    public List<MaterialOccupancy> Lines { get; set; } = new();

    /// <summary>跨工单按物料汇总后的占用状况（含可用、在途、净缺口）</summary>
    public List<MaterialOccupancyRow> Aggregated { get; set; } = new();

    /// <summary>结果来源：规则 / 模型辅助</summary>
    public string Source { get; set; } = LlmSources.Rule;

    public int ShortageCount => Aggregated.Count(r => r.NetShortage > 1e-9);
    public int CrowdingCount => Aggregated.Count(r => r.HasTimeCrowding);

    /// <summary>全部齐备（无净缺口）</summary>
    public bool AllSatisfied => ShortageCount == 0;

    public string SummaryText =>
        $"资材占用：{Lines.Count} 项 / {Aggregated.Count} 种物料，跨 {(Aggregated.Count > 0 ? Aggregated.Max(r => r.WorkOrderCount) : 0)} 张工单累加"
        + (AllSatisfied ? "，全部覆盖。" : $"；{ShortageCount} 项需采购。")
        + (CrowdingCount > 0 ? $" {CrowdingCount} 项存在时段挤兑。" : "");
}
