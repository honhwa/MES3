namespace SuperCapMES.WinForms.Models;

/// <summary>ERP 采购/生产订单状态常量：待下发 → 已下发</summary>
public static class ErpPoStatus
{
    /// <summary>待下发（尚未生成工单）</summary>
    public const string Pending = "待下发";

    /// <summary>已下发（已据此生成工单）</summary>
    public const string Pushed = "已下发";

    /// <summary>全部状态（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { Pending, Pushed };
}

/// <summary>
/// ERP 采购/生产订单（PO）：由「ERP 下发」从 ERP 拉取的模拟结果。
/// 下发后据其生成工单（工单号 = WO-{PoNo}），并将本 PO 置为「已下发」。
/// </summary>
public class ErpPo
{
    /// <summary>ERP 订单号，形如 PO-2026-0914-001</summary>
    public string PoNo { get; set; } = "";

    /// <summary>客户</summary>
    public string Customer { get; set; } = "";

    /// <summary>产品型号</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>产品名称</summary>
    public string ProductName { get; set; } = "";

    /// <summary>订单数量（只）</summary>
    public int OrderQty { get; set; }

    /// <summary>交期</summary>
    public DateTime DueDate { get; set; }

    /// <summary>状态：待下发 / 已下发</summary>
    public string Status { get; set; } = ErpPoStatus.Pending;

    /// <summary>下发时间（未下发的为 null）</summary>
    public DateTime? PushedTime { get; set; }

    /// <summary>备注</summary>
    public string Remark { get; set; } = "";
}
