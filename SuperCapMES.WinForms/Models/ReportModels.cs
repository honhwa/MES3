namespace SuperCapMES.WinForms.Models;

/// <summary>报表中心——产量日报 / 月报行（周期维度聚合）</summary>
public class OutputReportRow
{
    /// <summary>周期：日 = yyyy-MM-dd，月 = yyyy-MM</summary>
    public string Period { get; set; } = "";

    /// <summary>周期内有产量的设备数</summary>
    public int DeviceCount { get; set; }

    /// <summary>产量（设备日产量合计）</summary>
    public int Output { get; set; }

    /// <summary>过站次数（工序实绩记录数）</summary>
    public int StationCount { get; set; }

    /// <summary>测试数</summary>
    public int TestCount { get; set; }

    /// <summary>良品数</summary>
    public int PassCount { get; set; }

    /// <summary>不良数</summary>
    public int NgCount { get; set; }

    /// <summary>良率 %（无测试为 0）</summary>
    public double YieldRate { get; set; }

    /// <summary>良率显示文本</summary>
    public string YieldText => TestCount == 0 ? "—" : $"{YieldRate:0.##}%";

    /// <summary>单台设备日均产量</summary>
    public double AvgPerDevice => DeviceCount == 0 ? 0 : Math.Round((double)Output / DeviceCount, 1);
}

/// <summary>报表中心——良率趋势行（按日）</summary>
public class YieldTrendRow
{
    public string Period { get; set; } = "";
    public int TestCount { get; set; }
    public int PassCount { get; set; }
    public int NgCount { get; set; }
    public double YieldRate { get; set; }

    /// <summary>目标良率 %</summary>
    public double TargetRate { get; set; } = 98;

    /// <summary>较上一期变化（百分点）；首期为 null</summary>
    public double? DeltaPoints { get; set; }

    public string DeltaText => DeltaPoints is null
        ? "—"
        : (DeltaPoints.Value >= 0 ? $"↑ +{DeltaPoints.Value:0.##}" : $"↓ {DeltaPoints.Value:0.##}");

    public string Judge => TestCount == 0 ? "无数据" : (YieldRate >= TargetRate ? "达标" : "低于目标");
}

/// <summary>报表中心——异常 TOP 行（大类 / 细分 / 设备 / 线体 四个维度合并展示）</summary>
public class AbnormalTopRow
{
    /// <summary>统计维度：大类 / 细分 / 设备 / 线体</summary>
    public string Dimension { get; set; } = "";

    public string Name { get; set; } = "";
    public int Count { get; set; }

    /// <summary>占比 %（同维度内）</summary>
    public double Share { get; set; }

    public string ShareText => $"{Share:0.#}%";
}

/// <summary>报表中心——OEE 汇总行（按设备）</summary>
public class OeeSummaryRow
{
    public string DeviceCode { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Section { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>今日 OEE %（取当日小时记录均值；无记录回退设备台账值）</summary>
    public double TodayOee { get; set; }

    public double TargetOee { get; set; } = 85;

    /// <summary>达成率 % = 今日 OEE / 目标</summary>
    public double Achievement { get; set; }

    public string AchievementText => $"{Achievement:0.#}%";

    public string Judge => Achievement >= 100 ? "达标" : (Achievement >= 90 ? "接近" : "偏低");
}

/// <summary>报表中心——库存汇总行（类别维度 + TOP 物料）</summary>
public class StockSummaryRow
{
    /// <summary>维度：类别 / 物料</summary>
    public string Dimension { get; set; } = "";

    /// <summary>类别名（原料/辅料/成品）或物料名</summary>
    public string Name { get; set; } = "";

    /// <summary>条目数（批次/库位组合数）</summary>
    public int ItemCount { get; set; }

    public double TotalQty { get; set; }

    /// <summary>占比 %（同维度内）</summary>
    public double Share { get; set; }

    public string ShareText => $"{Share:0.#}%";
}

/// <summary>报表中心——顶部汇总卡数据</summary>
public class ReportCenterSummary
{
    public int TodayOutput { get; set; }
    public int MonthOutput { get; set; }
    public double AvgYield { get; set; }
    public int OpenAbnormal { get; set; }
    public double AvgOee { get; set; }

    /// <summary>产量环比（今日 vs 昨日，%）；无昨日数据为 null</summary>
    public double? OutputWow { get; set; }

    public string OutputWowText => OutputWow is null
        ? "—"
        : (OutputWow.Value >= 0 ? $"↑ +{OutputWow.Value:0.#}%" : $"↓ {OutputWow.Value:0.#}%");
}
