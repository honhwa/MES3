namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 工序流转顺序与设备映射（原型口径）：
/// **卷绕 → 入壳 → 注液 → 封口 → 化成 → 检测**。
/// "下一道工序"按此顺序推进，供 WIP 流转看板与模拟推进使用。
/// </summary>
public static class ProcessSequence
{
    /// <summary>工序顺序（下标 0 = 最早工序）</summary>
    public static readonly string[] Order = { "卷绕", "入壳", "注液", "封口", "化成", "检测" };

    /// <summary>工序 → 默认设备（按设备台账工段近似映射）</summary>
    public static readonly Dictionary<string, string> DefaultEquipment = new()
    {
        ["卷绕"] = "WIND-L1-01",   // 卷绕机（电芯成型）
        ["入壳"] = "WIND-L1-01",   // 原型近似：入壳归电芯成型段
        ["注液"] = "INJ-L1-02",    // 注液机
        ["封口"] = "SEAL-L1-01",   // 封口机
        ["化成"] = "FORM-A01",     // 化成柜组
        ["检测"] = "TST-L1-03"     // 电性能测试仪
    };

    /// <summary>工序在流转顺序中的序号（未知工序返回 -1）</summary>
    public static int IndexOf(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return -1;
        for (var i = 0; i < Order.Length; i++)
            if (string.Equals(Order[i], processName.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>下一道工序（末道工序返回 null，表示已下线）</summary>
    public static string? Next(string? processName)
    {
        var idx = IndexOf(processName);
        if (idx < 0 || idx >= Order.Length - 1) return null;
        return Order[idx + 1];
    }

    /// <summary>电芯处于某工序时的状态（推进时同步更新 CellSn.Status）</summary>
    public static string StatusAt(string processName) => processName switch
    {
        "化成" => "化成中",
        "检测" => "检测中",
        _ => "在制"
    };
}

/// <summary>工序流转状态常量</summary>
public static class WipFlowStatus
{
    /// <summary>在制数 ≥ 均衡水位 × 1.5</summary>
    public const string Bottleneck = "瓶颈";

    /// <summary>在制数 ≥ 均衡水位</summary>
    public const string Busy = "偏忙";

    public const string Normal = "正常";

    public static readonly string[] All = { Normal, Busy, Bottleneck };
}

/// <summary>WIP 看板——工序流转行（每道工序一行）</summary>
public class WipFlowRow
{
    /// <summary>流转顺序（1 起）</summary>
    public int Seq { get; set; }

    public string ProcessName { get; set; } = "";

    /// <summary>该工序出现过的过站设备数（演示口径）</summary>
    public int EquipmentCount { get; set; }

    /// <summary>当前在制数：最近一次过站落在该工序、且状态为 在制/化成中/检测中 的电芯数</summary>
    public int WipCount { get; set; }

    /// <summary>今日过站数（过站时间 ≥ 今天 00:00 的记录数）</summary>
    public int TodayCount { get; set; }

    /// <summary>昨日过站数</summary>
    public int YesterdayCount { get; set; }

    /// <summary>
    /// 平均节拍（分钟/只）：该工序最近 10 条过站记录按时间升序的相邻间隔均值；
    /// 记录不足 2 条为 null（显示"—"）。
    /// </summary>
    public double? CycleMinutes { get; set; }

    /// <summary>最长滞留（小时）：该工序在制电芯距最近一次过站的最长时间；无在制为 null</summary>
    public double? MaxStayHours { get; set; }

    /// <summary>状态：正常 / 偏忙 / 瓶颈（阈值规则见 MesDatabase.GetWipFlowRows）</summary>
    public string Status { get; set; } = WipFlowStatus.Normal;

    /// <summary>预警说明（瓶颈/偏忙原因；正常为空）</summary>
    public string Alert { get; set; } = "";

    /// <summary>平均节拍显示文本（无数据显示「—」）</summary>
    public string CycleText => CycleMinutes is null ? "—" : $"{CycleMinutes:0.#}";

    /// <summary>最长滞留显示文本（无在制显示「—」）</summary>
    public string MaxStayText => MaxStayHours is null ? "—" : $"{MaxStayHours:0.#}";
}

/// <summary>WIP 看板——在制电芯明细行（选中工序后展示）</summary>
public class WipCellRow
{
    public string CellSn { get; init; } = "";
    public string ProductModel { get; init; } = "";
    public string WoNo { get; init; } = "";
    public string Status { get; init; } = "";

    /// <summary>最近过站时间</summary>
    public DateTime LastStationTime { get; init; }

    /// <summary>滞留时长（小时，1 位小数）</summary>
    public double StayHours { get; init; }

    /// <summary>当前工序（最近过站工序）</summary>
    public string ProcessName { get; init; } = "";

    /// <summary>最近过站设备</summary>
    public string Equipment { get; init; } = "";

    /// <summary>上一道工序（首道工序显示「—」）</summary>
    public string PrevProcess { get; init; } = "—";
}
