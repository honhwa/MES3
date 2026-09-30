namespace SuperCapMES.WinForms.Models;

/// <summary>上料校验结果常量</summary>
public static class FeedingResult
{
    public const string Passed = "通过";
    public const string OrderNotFound = "工单不存在";
    public const string NotInBom = "不在工单 BOM";
    public const string LotNotFound = "批次不存在";
    public const string NoStock = "批次无可用库存";
    public const string EmptyScan = "扫码内容为空";

    /// <summary>是否为放行结果</summary>
    public static bool IsPass(string? result) => string.Equals(result, Passed, StringComparison.Ordinal);
}

/// <summary>
/// 上料（投料）记录：每次扫码校验一条，拒绝也留痕（防错追溯）。
/// 单号形如 FD-yyyyMMdd-001。
/// </summary>
public class FeedingRecord
{
    public string FeedNo { get; set; } = "";
    public string WoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";

    /// <summary>扫码原文（便于追溯扫码内容）</summary>
    public string ScanCode { get; set; } = "";

    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";
    public double Quantity { get; set; }

    /// <summary>校验结果：通过 / 拒绝原因</summary>
    public string Result { get; set; } = FeedingResult.Passed;

    public string Operator { get; set; } = "";
    public DateTime FeedTime { get; set; }

    /// <summary>是否放行</summary>
    public bool IsPass => FeedingResult.IsPass(Result);
}

/// <summary>一次上料校验的结论（供界面提示）</summary>
public class FeedingCheckOutcome
{
    public bool Ok { get; set; }
    public string Result { get; set; } = "";
    public string Message { get; set; } = "";

    public string WoNo { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>该批次当前可用库存</summary>
    public double AvailableQty { get; set; }

    /// <summary>该工单该物料的需求量（BOM 单位用量 × 计划数，含损耗率）；非 BOM 项为 null</summary>
    public double? RequiredQty { get; set; }

    /// <summary>本次是否该物料首次投料</summary>
    public bool IsFirstFeed { get; set; }
}

/// <summary>工单 BOM 投料进度行</summary>
public class FeedingProgressRow
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Unit { get; set; } = "";
    public string MaterialType { get; set; } = "";

    /// <summary>需求量（含损耗）</summary>
    public double RequiredQty { get; set; }

    /// <summary>已投数量（通过校验的合计）</summary>
    public double FedQty { get; set; }

    /// <summary>投料状态：未投 / 部分投料 / 已投足</summary>
    public string Status => FedQty <= 1e-9 ? "未投" : (FedQty + 1e-9 >= RequiredQty ? "已投足" : "部分投料");

    public string ProgressText => $"{FedQty:0.##} / {RequiredQty:0.##}";
}
