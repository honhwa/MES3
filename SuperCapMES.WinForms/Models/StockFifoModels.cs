namespace SuperCapMES.WinForms.Models;

/// <summary>库存效期状态常量</summary>
public static class StockExpiryStatus
{
    public const string Expired = "已过期";
    public const string NearExpiry = "临期";
    public const string Normal = "正常";
    public const string NoExpiry = "无效期";

    /// <summary>按剩余天数判定：<0 已过期；≤ alertDays 临期；否则正常；null 无效期</summary>
    public static string Judge(double? daysLeft, int alertDays = 30) =>
        daysLeft is null ? NoExpiry
        : daysLeft < 0 ? Expired
        : daysLeft <= alertDays ? NearExpiry
        : Normal;
}

/// <summary>
/// 库存 FIFO / 效期行（按「物料 + 批次 + 库位」展开）：
/// FIFO 依据 = 入库/最后更新时间升序；效期依据 = 有效期到期日。
/// </summary>
public class StockFifoRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";
    public double Quantity { get; set; }

    /// <summary>入库 / 最后更新时间（FIFO 排序依据）</summary>
    public DateTime ReceivedDate { get; set; }

    /// <summary>有效期到期日（null = 不进行效期管理）</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>距到期剩余天数（负值 = 已过期）</summary>
    public double? DaysLeft { get; set; }

    /// <summary>效期状态：已过期 / 临期 / 正常 / 无效期</summary>
    public string ExpiryStatus => StockExpiryStatus.Judge(DaysLeft);

    /// <summary>是否该物料下 FIFO 推荐优先出库的批次</summary>
    public bool IsFifoRecommended { get; set; }

    /// <summary>FIFO 推荐标记文本</summary>
    public string FifoText => IsFifoRecommended ? "★ 优先出库" : "";

    /// <summary>剩余天数显示文本</summary>
    public string DaysLeftText => DaysLeft is null
        ? "—"
        : (DaysLeft < 0 ? $"超期 {-DaysLeft:0} 天" : $"剩 {DaysLeft:0} 天");

    /// <summary>效期显示文本</summary>
    public string ExpiryText => ExpiryDate?.ToString("yyyy-MM-dd") ?? "—";
}
