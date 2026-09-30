namespace SuperCapMES.WinForms.Models;

/// <summary>保留单状态常量</summary>
public static class HoldStatus
{
    public const string Active = "保留中";
    public const string Released = "已解除";
}

/// <summary>保留动作类型常量</summary>
public static class HoldActionType
{
    public const string Hold = "保留";
    public const string Release = "解除";
}

/// <summary>保留位置常量（资材追溯页下拉可选）</summary>
public static class HoldLocations
{
    public const string FormationInspection = "化成外观";
    public const string WmsOutbound = "WMS出库";
    public const string ReworkArea = "返工区";
    public const string PendingInspection = "待检区";

    /// <summary>全部可选位置（顺序即下拉展示顺序）</summary>
    public static readonly string[] All =
    {
        FormationInspection,
        WmsOutbound,
        ReworkArea,
        PendingInspection
    };
}

/// <summary>保留单：记录因哪批资材、在何位置对若干电芯执行了保留</summary>
public class CellHold
{
    /// <summary>保留单号，形如 HLD-20260907-001</summary>
    public string HoldNo { get; set; } = "";

    /// <summary>触发保留的资材快照：物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>触发保留的资材快照：物料名称</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>触发保留的资材快照：批次号</summary>
    public string LotNo { get; set; } = "";

    /// <summary>保留位置：化成外观 / WMS出库 / 返工区 / 待检区</summary>
    public string HoldLocation { get; set; } = HoldLocations.FormationInspection;

    /// <summary>保留原因</summary>
    public string Reason { get; set; } = "";

    /// <summary>保留操作人</summary>
    public string Operator { get; set; } = "";

    /// <summary>保留时间</summary>
    public DateTime HoldTime { get; set; }

    /// <summary>解除操作人（未解除为空）</summary>
    public string ReleaseOperator { get; set; } = "";

    /// <summary>解除时间（未解除为 null）</summary>
    public DateTime? ReleaseTime { get; set; }

    /// <summary>解除原因（未解除为空）</summary>
    public string ReleaseReason { get; set; } = "";

    /// <summary>状态：保留中 / 已解除</summary>
    public string Status { get; set; } = HoldStatus.Active;

    /// <summary>电芯数量：由仓储在查询（QueryHolds）时按保留明细填充，便于网格直接展示</summary>
    public int CellCount { get; set; }

    /// <summary>来源类型：手工 / 检验不合格 / 测试不合格 / 质量扫描（默认「手工」，既有人工保留不变）</summary>
    public string SourceType { get; set; } = "手工";

    /// <summary>来源说明/单号（如「扫描不合格电芯」「检验触发：卷绕 外观」），人工保留为空</summary>
    public string SourceRef { get; set; } = "";
}
