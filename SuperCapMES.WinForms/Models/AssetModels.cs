namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 生产资产与占用 —— 模型层
//
// 要解决的问题：现在工单只校验"物料够不够"，**不校验"设备/工装够不够、撞不撞"**。
// 两张工单都要用注液机，时间窗重叠时，后建的那张会把前一张的工序挤掉——
// 这种冲突在纸面上看不出来，等到现场撞车才发现，代价是停线。
//
// 三条设计取舍：
//   ① **占用是"时间窗 × 数量"的二维问题**，不是"有没有"的一维问题。
//      同一台设备在不同时段可以反复占用，只看是否被占用会误判成冲突。
//   ② **占用记录独立成表（预留表），不塞进工单**——一个工单占多台资产、
//      一台资产被多张工单占用，是 1:N 双向关系，挤在一张表里必然有一头失真。
//   ③ **判定全部由规则算**（可审计），大模型只做"文本理解 + 归因措辞"，
//      绝不参与"够不够、撞不撞"的结论——这是整个项目一贯的铁律。
// ============================================================================

/// <summary>生产资产类型</summary>
public static class AssetTypes
{
    public const string Equipment = "设备";
    public const string Tooling = "工装";
    public const string Fixture = "夹具";
    public const string Inspector = "检测仪";

    public static readonly string[] All = { Equipment, Tooling, Fixture, Inspector };
}

/// <summary>资产占用状态</summary>
public static class OccupancyStatus
{
    /// <summary>已释放（不再占用）</summary>
    public const string Released = "已释放";

    /// <summary>已预留（工单草稿阶段占位，随时可退）</summary>
    public const string Reserved = "已预留";

    /// <summary>已确认（工单已开工，锁死不再被抢占）</summary>
    public const string Confirmed = "已确认";

    public static readonly string[] All = { Reserved, Confirmed, Released };

    /// <summary>是否仍在占用（未释放）</summary>
    public static bool IsActive(string? status) =>
        string.Equals(status, Reserved, StringComparison.Ordinal)
        || string.Equals(status, Confirmed, StringComparison.Ordinal);
}

/// <summary>占用来源（用于追溯是谁占的、为什么占）</summary>
public static class OccupancySource
{
    /// <summary>手工新建/编辑工单</summary>
    public const string Manual = "手工建单";

    /// <summary>智能排产</summary>
    public const string Scheduling = "智能排产";

    /// <summary>采购申请（备料期占用）</summary>
    public const string Purchase = "采购申请";

    public static readonly string[] All = { Manual, Scheduling, Purchase };
}

/// <summary>资产占用校验的行状态</summary>
public static class AssetCheckStatus
{
    /// <summary>满足：需求占用数 ≤ 可用余量，且时间窗无冲突</summary>
    public const string Ok = "满足";

    /// <summary>冲突：时间窗与其它工单重叠（数量够也排不下）</summary>
    public const string Conflict = "时间冲突";

    /// <summary>不足：可用余量 < 需求占用数</summary>
    public const string Insufficient = "余量不足";

    public static readonly string[] All = { Ok, Conflict, Insufficient };
}

/// <summary>可选方案类型</summary>
public static class OccupancyOptionKind
{
    /// <summary>调整数量：把需求降到可用余量</summary>
    public const string AdjustQty = "调整数量";

    /// <summary>错排时间：挪到最近的无冲突时间窗</summary>
    public const string Reschedule = "错排生产时间";

    /// <summary>替代资产：换一台同工序同型号的资产</summary>
    public const string Substitute = "替代资产";

    public static readonly string[] All = { AdjustQty, Reschedule, Substitute };
}

/// <summary>
/// 生产资产：可被工单在某个时间窗内占用的资源（设备 / 工装 / 夹具 / 检测仪）。
/// 与 <see cref="Equipment"/> 的区别：Equipment 是**设备台账**（状态 / OEE / 产量），
/// 本模型是**排产资源**（工序 / 线体 / 并发容量 / 适用型号）——关注点不同，分开建模。
/// </summary>
public class ProductionAsset
{
    /// <summary>资产编码（如 WIND-L1-01）</summary>
    public string Code { get; set; } = "";

    /// <summary>资产名称（如 卷绕机）</summary>
    public string Name { get; set; } = "";

    /// <summary>资产类型（见 <see cref="AssetTypes"/>）</summary>
    public string Type { get; set; } = AssetTypes.Equipment;

    /// <summary>所属工序段（与设备台账 Section 对应）</summary>
    public string Section { get; set; } = "";

    /// <summary>对应工序名（对应 <c>ProcessSequence.Order</c>，用于自动分配）</summary>
    public string Process { get; set; } = "";

    /// <summary>所属线体（L1 / L2 / L3；空 = 通用，不限线体）</summary>
    public string Line { get; set; } = "";

    /// <summary>并发容量：同时最多能被几张工单占用。1 = 独占</summary>
    public int Capacity { get; set; } = 1;

    /// <summary>可用状态：在用 / 维护 / 停机 / 备用。维护与停机不参与分配</summary>
    public string Status { get; set; } = "在用";

    /// <summary>适用产品型号（逗号分隔；空 = 通用）</summary>
    public string Models { get; set; } = "";

    /// <summary>显示文本</summary>
    public string DisplayDesc => $"{Name}（{Code}）";

    /// <summary>是否可供分配（状态决定）</summary>
    public bool IsAllocatable =>
        Status == "在用" || Status == "备用";

    /// <summary>是否适用于该产品型号（空 = 通用）</summary>
    public bool SupportsModel(string? productModel)
    {
        if (string.IsNullOrWhiteSpace(Models) || string.IsNullOrWhiteSpace(productModel)) return true;
        return Models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(m => string.Equals(m, productModel.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 资产占用记录（**预留表**）：一张工单在某时间窗内占用某资产若干数量。
/// 这是防超占用的落点——所有"占用了多少"的判断都只读这张表，不做二次推算。
/// </summary>
public class AssetOccupancy
{
    /// <summary>占用记录号</summary>
    public string Id { get; set; } = "";

    /// <summary>资产编码</summary>
    public string AssetCode { get; set; } = "";

    /// <summary>占用工单号（空 = 非工单占用，如计划维护）</summary>
    public string WoNo { get; set; } = "";

    /// <summary>占用来源（见 <see cref="OccupancySource"/>）</summary>
    public string Source { get; set; } = OccupancySource.Manual;

    /// <summary>占用数量（占用几台 / 几套）</summary>
    public int Qty { get; set; } = 1;

    /// <summary>占用起始时间</summary>
    public DateTime StartTime { get; set; }

    /// <summary>占用结束时间（含）</summary>
    public DateTime EndTime { get; set; }

    /// <summary>占用状态（见 <see cref="OccupancyStatus"/>）</summary>
    public string Status { get; set; } = OccupancyStatus.Reserved;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 乐观锁版本号。并发写占用表时用：读出版本 → 计算 → 写回时比对，
    /// 版本不一致说明期间被别人改过 → 拒绝并让调用方重试。
    /// 内存仓储是单线程，本身不会真并发；但**接口先立住**，
    /// 将来换数据库时这段防超占用逻辑可以原样搬过去，不用重写业务。
    /// </summary>
    public int Version { get; set; } = 1;

    public string Remark { get; set; } = "";

    /// <summary>时间窗是否与另一段重叠（含端点相接不算冲突）</summary>
    public bool Overlaps(DateTime start, DateTime end) =>
        StartTime < end && start < EndTime;

    public string WindowText => $"{StartTime:yyyy-MM-dd HH:mm} ~ {EndTime:yyyy-MM-dd HH:mm}";
}

/// <summary>冲突占用明细：谁占了、占多少、占了哪段时间。</summary>
public class OccupancyConflict
{
    /// <summary>冲突的工单号</summary>
    public string WoNo { get; set; } = "";

    /// <summary>被占用的资产编码</summary>
    public string AssetCode { get; set; } = "";

    /// <summary>资产名称</summary>
    public string AssetName { get; set; } = "";

    /// <summary>对方占用数量</summary>
    public int OccupiedQty { get; set; }

    /// <summary>对方占用起始</summary>
    public DateTime Start { get; set; }

    /// <summary>对方占用结束</summary>
    public DateTime End { get; set; }

    /// <summary>重叠时段描述（便于界面直接显示）</summary>
    public string OverlapText { get; set; } = "";

    public string WindowText => $"{Start:yyyy-MM-dd HH:mm} ~ {End:yyyy-MM-dd HH:mm}";
}

/// <summary>可选方案（冲突/不足时给出，人可直接照做）</summary>
public class OccupancyOption
{
    /// <summary>方案类型（见 <see cref="OccupancyOptionKind"/>）</summary>
    public string Kind { get; set; } = "";

    /// <summary>方案说明</summary>
    public string Text { get; set; } = "";

    /// <summary>替代资产编码（仅替代资产方案有值）</summary>
    public string TargetAssetCode { get; set; } = "";

    /// <summary>是否可行（如"错排到 X 时段"算出来会超交期，则标不可行）</summary>
    public bool Feasible { get; set; } = true;
}

/// <summary>资产占用校验的一行（逐资产逐工序）</summary>
public class AssetCheckLine
{
    /// <summary>对应工序</summary>
    public string Process { get; set; } = "";

    /// <summary>资产编码</summary>
    public string AssetCode { get; set; } = "";

    /// <summary>资产名称</summary>
    public string AssetName { get; set; } = "";

    /// <summary>资产类型</summary>
    public string AssetType { get; set; } = "";

    /// <summary>本工单需求占用数</summary>
    public int NeedQty { get; set; }

    /// <summary>资产并发容量</summary>
    public int Capacity { get; set; }

    /// <summary>该时间窗内被其它工单占用数</summary>
    public int OccupiedByOthers { get; set; }

    /// <summary>可用余量 = 容量 − 其它工单占用</summary>
    public int AvailableQty { get; set; }

    /// <summary>余量缺口 = max(0, 需求 − 可用)</summary>
    public int ShortageQty { get; set; }

    /// <summary>状态：满足 / 时间冲突 / 余量不足</summary>
    public string Status { get; set; } = AssetCheckStatus.Ok;

    /// <summary>冲突明细（工单号 / 占用数量 / 起止时间）</summary>
    public List<OccupancyConflict> Conflicts { get; set; } = new();

    /// <summary>可选方案</summary>
    public List<OccupancyOption> Options { get; set; } = new();

    /// <summary>处置建议（一句话说明这条为什么不过，界面上直接显示）</summary>
    public string Advice { get; set; } = "";

    /// <summary>单位说明（台 / 套）</summary>
    public string Unit { get; set; } = "台";

    public string NeedText => $"{NeedQty} {Unit}";
    public string AvailableText => $"{AvailableQty} {Unit}";
    public string ShortageText => ShortageQty > 0 ? $"{ShortageQty} {Unit}" : "—";
    public string WindowText => string.IsNullOrEmpty(Process) ? "" : Process;

    /// <summary>状态显示文本</summary>
    public string StatusText => Status switch
    {
        AssetCheckStatus.Conflict => "时间冲突",
        AssetCheckStatus.Insufficient => "余量不足",
        _ => "满足"
    };

    /// <summary>冲突占用明细（工单号 / 占用数量 / 起止时间），无冲突时给余量说明</summary>
    public string ConflictText => Conflicts.Count == 0
        ? (Status == AssetCheckStatus.Ok ? "—" : $"可用 {AvailableQty} {Unit}，本单需 {NeedQty} {Unit}")
        : string.Join("；", Conflicts.Select(c =>
            $"工单 {c.WoNo} 占 {c.OccupiedQty} 台 {c.WindowText}"
            + (c.OverlapText.Length > 0 ? $"（重叠 {c.OverlapText}）" : "")));

    /// <summary>可选方案文本（可行方案在前）</summary>
    public string OptionText => Options.Count == 0
        ? ""
        : string.Join("；", Options.OrderByDescending(o => o.Feasible).Select(o =>
            $"[{o.Kind}]{(o.Feasible ? "" : "（需协商）")}{o.Text}"));
}

/// <summary>物料 + 资产 的统一校验结果</summary>
public class ResourceCheckResult
{
    /// <summary>被校验的工单号（新建时可能为空）</summary>
    public string WoNo { get; set; } = "";

    /// <summary>产品型号</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>计划数量</summary>
    public int PlanQty { get; set; }

    /// <summary>校验时间窗</summary>
    public DateTime WindowStart { get; set; }

    public DateTime WindowEnd { get; set; }

    /// <summary>资产占用校验结果</summary>
    public List<AssetCheckLine> AssetLines { get; set; } = new();

    /// <summary>物料齐套结果（复用上一轮的 CheckBomAvailability 口径）</summary>
    public MaterialCheckResult? Material { get; set; }

    /// <summary>结果来源：规则 / 模型辅助（模型只做措辞，判定仍是规则）</summary>
    public string Source { get; set; } = "规则";

    /// <summary>归因说明（模型可用；未接模型时为空）</summary>
    public string Remark { get; set; } = "";

    public int ConflictCount => AssetLines.Count(l => l.Status == AssetCheckStatus.Conflict);
    public int InsufficientCount => AssetLines.Count(l => l.Status == AssetCheckStatus.Insufficient);

    /// <summary>资产是否全部可满足</summary>
    public bool AssetOk => ConflictCount == 0 && InsufficientCount == 0;

    /// <summary>物料是否齐套（无 BOM 时视为通过，另行提示）</summary>
    public bool MaterialOk => Material is null || Material.NoBom || Material.AllSatisfied;

    /// <summary>整体是否通过</summary>
    public bool AllOk => AssetOk && MaterialOk;

    /// <summary>摘要（界面顶部状态条用）</summary>
    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            parts.Add(AssetLines.Count == 0
                ? "无资产占用需求"
                : AssetOk
                    ? $"资产满足（{AssetLines.Count} 项）"
                    : $"资产 {ConflictCount} 项时间冲突 / {InsufficientCount} 项余量不足");
            if (Material is null) parts.Add("未做物料校验");
            else if (Material.NoBom) parts.Add("无 BOM，未做物料校验");
            else parts.Add(Material.AllSatisfied
                ? $"物料齐套（{Material.Rows.Count} 项）"
                : $"物料 {Material.ShortCount} 项缺料");
            return string.Join("；", parts) + "。";
        }
    }

    /// <summary>全部冲突明细（跨资产汇总）</summary>
    public List<OccupancyConflict> AllConflicts =>
        AssetLines.SelectMany(l => l.Conflicts).ToList();

    /// <summary>全部可选方案（跨资产汇总）</summary>
    public List<OccupancyOption> AllOptions =>
        AssetLines.SelectMany(l => l.Options).ToList();
}
