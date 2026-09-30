namespace SuperCapMES.WinForms.Models;

/// <summary>电芯序列号（追溯唯一主键）</summary>
public class CellSn
{
    public string Sn { get; set; } = "";
    public string WoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string Line { get; set; } = "";
    public string Status { get; set; } = "在制"; // 在制 / 化成中 / 检测中 / 合格 / 不良 / 已入库
    public DateTime CreateTime { get; set; }
    public string ElectrodeLot { get; set; } = ""; // 极片批号
    public string ElectrolyteLot { get; set; } = ""; // 电解液批号
    public string ModuleSn { get; set; } = "";      // 所属模组SN（空表示未组模组）
    public string Result { get; set; } = "";        // 分档结果，如 A1 / B2 / NG
}

/// <summary>
/// 电芯状态常量与「可出货」判定。
/// 业务规则：只有**生产完成且合格**的电芯才允许出货（进入出库单 / 出货绑定）。
/// 「合格」= 检验合格待入库、「已入库」= 已进入成品库，两者均视为可出货；
/// 「在制 / 化成中 / 检测中 / 不良」一律不可出货。
/// </summary>
public static class CellSnStatus
{
    public const string InProcess = "在制";
    public const string Formation = "化成中";
    public const string Testing = "检测中";
    public const string Qualified = "合格";
    public const string Ng = "不良";
    public const string InStock = "已入库";

    /// <summary>允许出货的状态集合（合格 / 已入库）</summary>
    public static readonly string[] Shippable = { Qualified, InStock };

    /// <summary>判断某状态是否允许出货（忽略大小写与首尾空白）</summary>
    public static bool IsShippable(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && Shippable.Any(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>状态说明文案（供 UI 提示为什么不能出货）</summary>
    public static string Describe(string? status)
    {
        if (IsShippable(status)) return "可出货";
        if (string.IsNullOrWhiteSpace(status)) return "状态未知，不可出货";
        return $"状态「{status.Trim()}」不可出货（需为合格或已入库）";
    }
}
