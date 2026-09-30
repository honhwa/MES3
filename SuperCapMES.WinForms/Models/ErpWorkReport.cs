namespace SuperCapMES.WinForms.Models;

/// <summary>ERP 报工上传状态常量：上传成功 / 失败</summary>
public static class ErpUploadStatus
{
    /// <summary>上传成功</summary>
    public const string Success = "上传成功";

    /// <summary>上传失败</summary>
    public const string Failed = "失败";

    /// <summary>全部状态（顺序即展示顺序）</summary>
    public static readonly string[] All = { Success, Failed };
}

/// <summary>
/// ERP 报工记录：一次「ERP 报工」上传形成一条记录。
/// 单号形如 RPT-20260914-001（序号全局递增、跨天自然延续）；ERP 回执形如 ACK-xxxxxx。
/// 允许同一工单多次报工（每次一条记录、工时累加）。
/// </summary>
public class ErpWorkReport
{
    /// <summary>报工单号，形如 RPT-20260914-001</summary>
    public string ReportNo { get; set; } = "";

    /// <summary>工单号</summary>
    public string WoNo { get; set; } = "";

    /// <summary>ERP 订单号（工单关联的 ErpOrderNo）</summary>
    public string ErpOrderNo { get; set; } = "";

    /// <summary>产出数量</summary>
    public int OutputQty { get; set; }

    /// <summary>良品数</summary>
    public int GoodQty { get; set; }

    /// <summary>不良数</summary>
    public int NgQty { get; set; }

    /// <summary>投入工时（h）</summary>
    public double WorkHours { get; set; }

    /// <summary>操作人</summary>
    public string Operator { get; set; } = "";

    /// <summary>报工时间</summary>
    public DateTime ReportTime { get; set; }

    /// <summary>上传状态：上传成功 / 失败</summary>
    public string ErpStatus { get; set; } = ErpUploadStatus.Success;

    /// <summary>ERP 回执号，形如 ACK-xxxxxx</summary>
    public string ErpReceipt { get; set; } = "";

    /// <summary>备注</summary>
    public string Remark { get; set; } = "";
}
