namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 首件检验（FAI）状态：待检验 → 合格（已放行）/ 不合格（需调试后复检）。
/// 业务规则：**换型 / 开班 / 新工单投产前必须完成首件检验并合格，方可批量生产**。
/// </summary>
public static class FaiStatus
{
    public const string Pending = "待检验";
    public const string Passed = "合格（已放行）";
    public const string Failed = "不合格";

    public static readonly string[] All = { Pending, Passed, Failed };

    /// <summary>是否已放行批量生产</summary>
    public static bool IsPassed(string? status) => string.Equals(status, Passed, StringComparison.Ordinal);
}

/// <summary>首件检验触发原因</summary>
public static class FaiReason
{
    public const string Changeover = "换型";
    public const string ShiftStart = "开班";
    public const string NewOrder = "新工单";
    public const string AfterFix = "调整后复检";

    public static readonly string[] All = { Changeover, ShiftStart, NewOrder, AfterFix };
}

/// <summary>
/// 首件检验单：关联工单 + 首件电芯 SN + 检验工序，记录检验项目判定与放行状态。
/// 检验合格后该工单视为「已放行批量」；工序实绩保存时会据此提示。
/// </summary>
public class FaiOrder
{
    /// <summary>首件检验单号，形如 FAI-yyyyMMdd-001</summary>
    public string FaiNo { get; set; } = "";

    public string WoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string Line { get; set; } = "";

    /// <summary>首件电芯 SN</summary>
    public string FirstCellSn { get; set; } = "";

    /// <summary>首检工序（检验项目取自该工序模板）</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>触发原因：换型 / 开班 / 新工单 / 调整后复检</summary>
    public string Reason { get; set; } = FaiReason.Changeover;

    public string Status { get; set; } = FaiStatus.Pending;

    /// <summary>最近一次检验人 / 时间</summary>
    public string Inspector { get; set; } = "";
    public DateTime? InspectTime { get; set; }

    /// <summary>最近一次检验的合格 / 不合格项数</summary>
    public int PassCount { get; set; }
    public int FailCount { get; set; }

    public DateTime CreatedTime { get; set; }
    public string Remark { get; set; } = "";

    /// <summary>是否可再次提交（不合格可调试后复检）</summary>
    public bool CanSubmit => !FaiStatus.IsPassed(Status);

    /// <summary>状态展示文本（不合格时带不合格项数）</summary>
    public string StatusText => Status == FaiStatus.Failed && FailCount > 0 ? $"{Status}（{FailCount} 项）" : Status;
}

/// <summary>首件检验明细项（可编辑实测值）</summary>
public class FaiItem
{
    public string FaiNo { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Standard { get; set; } = "";

    /// <summary>实测值（界面录入）</summary>
    public string Measured { get; set; } = "";

    /// <summary>判定结果：合格 / 不合格 / 未填</summary>
    public string Result { get; set; } = "";

    /// <summary>结果显示文本</summary>
    public string ResultText => string.IsNullOrWhiteSpace(Result) ? "未填" : Result;
}
