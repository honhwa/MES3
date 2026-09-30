namespace SuperCapMES.WinForms.Models;

/// <summary>生产工单</summary>
public class WorkOrder
{
    public string WoNo { get; set; } = "";
    public string ErpOrderNo { get; set; } = ""; // ERP 下发的订单号
    public string ProductModel { get; set; } = "";
    public int PlanQty { get; set; }
    public int FinishedQty { get; set; }
    public int NgQty { get; set; }
    public string Status { get; set; } = "未开始"; // 未开始 / 生产中 / 已完成 / 已关闭
    public DateTime StartDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Line { get; set; } = "";

    // ---- ERP 报工（增量字段；均为新增，不影响既有字段语义） ----

    /// <summary>ERP 报工状态：未报工 / 已报工（默认未报工）</summary>
    public string ErpReportStatus { get; set; } = "未报工";

    /// <summary>最近一次 ERP 报工时间（未报工为 null）</summary>
    public DateTime? ErpReportTime { get; set; }

    /// <summary>累计上报工时（h）：由「ERP 报工」逐次累加</summary>
    public double WorkHours { get; set; }
}
