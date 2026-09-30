namespace SuperCapMES.WinForms.Models;

/// <summary>模组状态常量</summary>
public static class ModuleStatus
{
    /// <summary>已装配（在库/在制）</summary>
    public const string Assembled = "已装配";

    /// <summary>已出货（随出货单发出）</summary>
    public const string Shipped = "已出货";

    /// <summary>已拆解（电芯已解绑）</summary>
    public const string Dismantled = "已拆解";

    public static readonly string[] All = { Assembled, Shipped, Dismantled };

    /// <summary>是否为在装状态（可拆解）</summary>
    public static bool IsActive(string? status) => string.Equals(status, Assembled, StringComparison.Ordinal);
}

/// <summary>
/// 模组装配记录：模组级追溯的根节点（模组 SN → 电芯 → 材料/工序/测试）。
/// 超容产品多以模组出货，此记录补齐"电芯以上"的追溯层级。
/// </summary>
public class ModuleRecord
{
    /// <summary>模组 SN（如 SM-A260913-L1-00031）</summary>
    public string ModuleSn { get; set; } = "";

    /// <summary>模组型号（如「模组 SC-MOD-100F-24V」）</summary>
    public string ModuleModel { get; set; } = "";

    /// <summary>电芯型号</summary>
    public string CellModel { get; set; } = "";

    /// <summary>实际装配电芯数</summary>
    public int CellCount { get; set; }

    public string Line { get; set; } = "";

    /// <summary>关联工单</summary>
    public string WoNo { get; set; } = "";

    public string Status { get; set; } = ModuleStatus.Assembled;

    public string Operator { get; set; } = "";
    public DateTime AssembledTime { get; set; }
    public string Remark { get; set; } = "";

    public string StatusText => Status;
}

/// <summary>模组内电芯行（模组级追溯明细：含材料批次与测试结论）</summary>
public class ModuleCellRow
{
    public string ModuleSn { get; set; } = "";
    public string CellSn { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string ElectrodeLot { get; set; } = "";
    public string ElectrolyteLot { get; set; } = "";

    /// <summary>分档结果（A1/A2/NG/—）</summary>
    public string Result { get; set; } = "";

    /// <summary>电芯当前状态</summary>
    public string CellStatus { get; set; } = "";

    public DateTime? TestTime { get; set; }
    public bool? Passed { get; set; }

    /// <summary>测试结论文本</summary>
    public string TestText => Passed is null ? "未测" : (Passed.Value ? "合格" : "不合格");
}

/// <summary>模组装配明细（供窗体勾选与记录）</summary>
public class ModuleCellItem
{
    public bool Chk { get; set; }
    public string CellSn { get; init; } = "";
    public string ProductModel { get; init; } = "";
    public string CellStatus { get; init; } = "";
    public string ElectrodeLot { get; init; } = "";
    public DateTime CreateTime { get; init; }
}
