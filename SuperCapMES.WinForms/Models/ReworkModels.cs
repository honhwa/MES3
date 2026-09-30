namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 返工单状态常量。质量故事线：**发现（检验不合格）→ 保留 → 返工 → 重检 → 放行**。
/// 流转：待返工 →(开始返工) 返工中 →(返工完成) 待重检 →(重检合格) 已放行
///                                          ↘(重检不合格) 返工中（返工次数 +1）
/// 任意非终态可 → 已报废。
/// </summary>
public static class ReworkStatus
{
    /// <summary>已建单、尚未开始返工</summary>
    public const string Pending = "待返工";

    /// <summary>返工执行中</summary>
    public const string InProgress = "返工中";

    /// <summary>返工完成、等待重检</summary>
    public const string WaitInspect = "待重检";

    /// <summary>重检合格并已解除保留（终态）</summary>
    public const string Released = "已放行";

    /// <summary>判定报废（终态）</summary>
    public const string Scrapped = "已报废";

    public static readonly string[] All = { Pending, InProgress, WaitInspect, Released, Scrapped };

    /// <summary>是否为终态（不可再操作）</summary>
    public static bool IsFinal(string? status) =>
        status is Released or Scrapped;
}

/// <summary>返工流转动作常量（履历动作名）</summary>
public static class ReworkAction
{
    public const string Create = "创建返工单";
    public const string Start = "开始返工";
    public const string Finish = "返工完成";
    public const string InspectPass = "重检合格";
    public const string InspectFail = "重检不合格";
    public const string Release = "放行解除保留";
    public const string Scrap = "报废";
}

/// <summary>
/// 返工单：由「保留中」的电芯发起，记录返工原因 / 责任工序 / 返工工序 / 次数 /
/// 重检结果与放行信息，与保留单双向可查（追溯链上能看到返工记录）。
/// </summary>
public class ReworkOrder
{
    /// <summary>返工单号，形如 RW-yyyyMMdd-001</summary>
    public string ReworkNo { get; set; } = "";

    /// <summary>来源保留单号</summary>
    public string HoldNo { get; set; } = "";

    /// <summary>返工电芯 SN</summary>
    public string CellSn { get; set; } = "";

    /// <summary>产品型号快照</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>返工原因</summary>
    public string Reason { get; set; } = "";

    /// <summary>责任工序（问题发生在哪道工序）</summary>
    public string ResponsibleProcess { get; set; } = "";

    /// <summary>返工工序（实际在哪道工序返工）</summary>
    public string ReworkProcess { get; set; } = "";

    /// <summary>返工次数（重检不合格退回时 +1）</summary>
    public int ReworkTimes { get; set; }

    /// <summary>状态：待返工 / 返工中 / 待重检 / 已放行 / 已报废</summary>
    public string Status { get; set; } = ReworkStatus.Pending;

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedTime { get; set; }

    /// <summary>最近一次返工执行人</summary>
    public string Operator { get; set; } = "";

    public DateTime? StartTime { get; set; }
    public DateTime? FinishTime { get; set; }

    /// <summary>返工参数记录（如 卷绕张力 1.72N）</summary>
    public string ReworkParam { get; set; } = "";

    /// <summary>最近一次重检结果：合格 / 不合格 / 空</summary>
    public string InspectResult { get; set; } = "";

    /// <summary>重检实测值</summary>
    public string InspectMeasured { get; set; } = "";

    /// <summary>重检标准值</summary>
    public string InspectStandard { get; set; } = "";

    public string InspectBy { get; set; } = "";
    public DateTime? InspectTime { get; set; }

    /// <summary>重检次数</summary>
    public int InspectTimes { get; set; }

    /// <summary>放行时间 / 放行人（重检合格后解除保留）</summary>
    public DateTime? ReleaseTime { get; set; }
    public string ReleaseBy { get; set; } = "";

    /// <summary>报废原因（已报废时）</summary>
    public string ScrapReason { get; set; } = "";

    public string Remark { get; set; } = "";

    /// <summary>是否可执行「开始返工」</summary>
    public bool CanStart => Status == ReworkStatus.Pending;

    /// <summary>是否可执行「返工完成」</summary>
    public bool CanFinish => Status == ReworkStatus.InProgress;

    /// <summary>是否可执行「重检登记」</summary>
    public bool CanInspect => Status == ReworkStatus.WaitInspect;

    /// <summary>是否可「报废」</summary>
    public bool CanScrap => !ReworkStatus.IsFinal(Status);

    /// <summary>状态展示文本（含返工次数提示）</summary>
    public string StatusText => ReworkTimes > 1 ? $"{Status}（第 {ReworkTimes} 次）" : Status;
}

/// <summary>返工流转履历</summary>
public class ReworkHistory
{
    public string ReworkNo { get; set; } = "";

    /// <summary>动作：创建返工单 / 开始返工 / 返工完成 / 重检合格 / 重检不合格 / 放行解除保留 / 报废</summary>
    public string Action { get; set; } = "";

    public string Operator { get; set; } = "";
    public DateTime Time { get; set; }
    public string Detail { get; set; } = "";
}
