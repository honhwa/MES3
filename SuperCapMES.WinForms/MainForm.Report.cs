using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分，第 12 部分）：报表中心页（产量日报月报 / 良率趋势 / 异常 TOP / OEE 与库存 + 一键导出）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Report」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 报表中心（顶层第 17 个页签） =================

    /// <summary>报表中心：产量日报/月报、良率趋势、异常 TOP、OEE 与库存汇总，支持一键导出 xlsx。</summary>
    private TabPage BuildReportTab()
    {
        var page = new TabPage("报表中心");

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("今日产量 (只)", Color.FromArgb(230, 126, 34), out _lblReportTodayOutput));
        cards.Controls.Add(MakeCard("本月产量 (只)", Color.FromArgb(15, 76, 129), out _lblReportMonthOutput));
        cards.Controls.Add(MakeCard("本月良率 (%)", Color.FromArgb(39, 174, 96), out _lblReportYield));
        cards.Controls.Add(MakeCard("未闭环异常 (单)", Color.FromArgb(192, 0, 0), out _lblReportOpenAbnormal));
        cards.Controls.Add(MakeCard("设备 OEE 均值 (%)", Color.FromArgb(142, 68, 173), out _lblReportOee));

        _reportSubTabs = new TabControl { Dock = DockStyle.Fill };
        _reportSubTabs.TabPages.Add(MakeReportGridTab("产量日报", out _dailyOutputGrid));
        _reportSubTabs.TabPages.Add(MakeReportGridTab("产量月报", out _monthlyOutputGrid));
        _reportSubTabs.TabPages.Add(MakeReportGridTab("良率趋势", out _yieldTrendGrid));
        _reportSubTabs.TabPages.Add(MakeReportGridTab("异常 TOP", out _abnormalTopGrid));
        _reportSubTabs.TabPages.Add(BuildOeeStockSubTab());

        page.Controls.Add(_reportSubTabs);
        page.Controls.Add(BuildReportQueryBar());
        page.Controls.Add(cards);

        SetupReportColumns();
        DoQueryReport();

        page.Enter -= ReportTab_Enter;
        page.Enter += ReportTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>创建「ReportGridTab」控件/样式（统一样式的工厂方法）。</summary>
    private static TabPage MakeReportGridTab(string title, out DataGridView grid)
    {
        var page = new TabPage(title);
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(title, out grid));
        return page;
    }

    /// <summary>OEE 与库存子页（上下两个表）。</summary>
    private TabPage BuildOeeStockSubTab()
    {
        var page = new TabPage("OEE 与库存");
        page.Padding = new Padding(4);
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 210,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("设备 OEE 汇总（今日小时均值 vs 目标）", out _oeeGrid));
        split.Panel2.Controls.Add(MakeGridGroup("库存汇总（按类别 + 物料 TOP10，含占比）", out _stockGrid));
        page.Controls.Add(split);
        return page;
    }

    /// <summary>构建「Report」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildReportQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "统计范围：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboReportRange = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(4, 4, 0, 0) };
        _cboReportRange.Items.AddRange(new object[] { "近 7 天", "近 14 天", "近 30 天" });
        _cboReportRange.SelectedIndex = 1;   // 默认近 14 天
        bar.Controls.Add(_cboReportRange);

        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => DoQueryReport()));
        bar.Controls.Add(MakeToolbarButton("导出报表", BtnReportExport));

        _lblReportHint = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 10, 0, 0) };
        bar.Controls.Add(_lblReportHint);
        return bar;
    }

    /// <summary>设置「ReportColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupReportColumns()
    {
        _dailyOutputGrid.AutoGenerateColumns = false;
        _dailyOutputGrid.Columns.Clear();
        _dailyOutputGrid.Columns.Add(MakeTextCol("Period", "日期", true, 12));
        _dailyOutputGrid.Columns.Add(MakeTextCol("DeviceCount", "设备数", true, 8));
        _dailyOutputGrid.Columns.Add(MakeTextCol("Output", "产量", true, 10));
        _dailyOutputGrid.Columns.Add(MakeTextCol("AvgPerDevice", "单台日均", true, 10));
        _dailyOutputGrid.Columns.Add(MakeTextCol("StationCount", "过站次数", true, 9));
        _dailyOutputGrid.Columns.Add(MakeTextCol("TestCount", "测试数", true, 9));
        _dailyOutputGrid.Columns.Add(MakeTextCol("PassCount", "良品数", true, 9));
        _dailyOutputGrid.Columns.Add(MakeTextCol("NgCount", "不良数", true, 9));
        _dailyOutputGrid.Columns.Add(MakeTextCol("YieldText", "良率", true, 9));

        _monthlyOutputGrid.AutoGenerateColumns = false;
        _monthlyOutputGrid.Columns.Clear();
        _monthlyOutputGrid.Columns.Add(MakeTextCol("Period", "月份", true, 12));
        _monthlyOutputGrid.Columns.Add(MakeTextCol("Output", "产量", true, 12));
        _monthlyOutputGrid.Columns.Add(MakeTextCol("AvgPerDevice", "单台日均", true, 12));
        _monthlyOutputGrid.Columns.Add(MakeTextCol("StationCount", "过站次数", true, 12));
        _monthlyOutputGrid.Columns.Add(MakeTextCol("TestCount", "测试数", true, 12));
        _monthlyOutputGrid.Columns.Add(MakeTextCol("NgCount", "不良数", true, 10));
        _monthlyOutputGrid.Columns.Add(MakeTextCol("YieldText", "良率", true, 12));
        _monthlyOutputGrid.Columns.Add(MakeTextCol("DeviceCount", "设备数", true, 10));

        _yieldTrendGrid.AutoGenerateColumns = false;
        _yieldTrendGrid.Columns.Clear();
        _yieldTrendGrid.Columns.Add(MakeTextCol("Period", "日期", true, 14));
        _yieldTrendGrid.Columns.Add(MakeTextCol("TestCount", "测试数", true, 10));
        _yieldTrendGrid.Columns.Add(MakeTextCol("PassCount", "良品数", true, 10));
        _yieldTrendGrid.Columns.Add(MakeTextCol("NgCount", "不良数", true, 10));
        _yieldTrendGrid.Columns.Add(MakeTextCol("YieldRate", "良率(%)", true, 10));
        _yieldTrendGrid.Columns.Add(MakeTextCol("TargetRate", "目标(%)", true, 10));
        _yieldTrendGrid.Columns.Add(MakeTextCol("DeltaText", "环比", true, 10));
        _yieldTrendGrid.Columns.Add(MakeTextCol("Judge", "判定", true, 10));
        // 先解除再挂接着色事件（同上，防止重复着色）
        _yieldTrendGrid.CellFormatting -= ReportYieldGrid_CellFormatting;
        _yieldTrendGrid.CellFormatting += ReportYieldGrid_CellFormatting;

        _abnormalTopGrid.AutoGenerateColumns = false;
        _abnormalTopGrid.Columns.Clear();
        _abnormalTopGrid.Columns.Add(MakeTextCol("Dimension", "维度", true, 10));
        _abnormalTopGrid.Columns.Add(MakeTextCol("Name", "名称", true, 26));
        _abnormalTopGrid.Columns.Add(MakeTextCol("Count", "数量", true, 10));
        _abnormalTopGrid.Columns.Add(MakeTextCol("ShareText", "占比", true, 10));

        _oeeGrid.AutoGenerateColumns = false;
        _oeeGrid.Columns.Clear();
        _oeeGrid.Columns.Add(MakeTextCol("DeviceName", "设备", true, 14));
        _oeeGrid.Columns.Add(MakeTextCol("Section", "工段", true, 12));
        _oeeGrid.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _oeeGrid.Columns.Add(MakeTextCol("TodayOee", "今日 OEE(%)", true, 10));
        _oeeGrid.Columns.Add(MakeTextCol("TargetOee", "目标(%)", true, 9));
        _oeeGrid.Columns.Add(MakeTextCol("AchievementText", "达成率", true, 9));
        _oeeGrid.Columns.Add(MakeTextCol("Judge", "判定", true, 8));

        _stockGrid.AutoGenerateColumns = false;
        _stockGrid.Columns.Clear();
        _stockGrid.Columns.Add(MakeTextCol("Dimension", "维度", true, 9));
        _stockGrid.Columns.Add(MakeTextCol("Name", "类别 / 物料", true, 34));
        _stockGrid.Columns.Add(MakeTextCol("ItemCount", "条目数", true, 9));
        _stockGrid.Columns.Add(MakeTextCol("TotalQty", "数量合计", true, 11));
        _stockGrid.Columns.Add(MakeTextCol("ShareText", "占比", true, 9));
    }

    /// <summary>良率趋势着色：达标绿 / 低于目标红 / 无数据灰。</summary>
    private void ReportYieldGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_yieldTrendGrid.Rows[e.RowIndex].DataBoundItem is not YieldTrendRow row) return;

        var col = _yieldTrendGrid.Columns[e.ColumnIndex].Name;
        if (col != "Judge" && col != "YieldRate" && col != "DeltaText") return;

        e.CellStyle.ForeColor = row.Judge switch
        {
            "达标" => Color.FromArgb(39, 174, 96),
            "低于目标" => Color.FromArgb(192, 0, 0),
            _ => Color.FromArgb(127, 140, 141)
        };
    }

    /// <summary>进入「ReportTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void ReportTab_Enter(object? sender, EventArgs e) => EnsureReportReady();

    /// <summary>兜底初始化「Report」：页签 Enter/全局刷新时确保数据已加载（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureReportReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_dailyOutputGrid is null) return;
        if (_dailyOutputGrid.Rows.Count == 0) DoQueryReport();
    }

    /// <summary>当前统计范围起点日期（近 7 / 14 / 30 天）。</summary>
    private DateTime ReportRangeStart() => _cboReportRange?.SelectedIndex switch
    {
        0 => DateTime.Today.AddDays(-6),
        2 => DateTime.Today.AddDays(-29),
        _ => DateTime.Today.AddDays(-13)
    };

    /// <summary>加载报表中心全部数据（卡片 + 5 个子页）。</summary>
    private void DoQueryReport()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_dailyOutputGrid is null) return;
        var from = ReportRangeStart();
        var daily = MesDatabase.GetDailyOutputReport(from, DateTime.Today);

        _dailyOutputGrid.DataSource = daily;
        _monthlyOutputGrid.DataSource = MesDatabase.GetMonthlyOutputReport(3);
        _yieldTrendGrid.DataSource = MesDatabase.GetYieldTrendReport(from, DateTime.Today);
        _abnormalTopGrid.DataSource = MesDatabase.GetAbnormalTopReport(from);
        _oeeGrid.DataSource = MesDatabase.GetOeeSummaryReport();
        _stockGrid.DataSource = MesDatabase.GetStockSummaryReport(10);

        var summary = MesDatabase.GetReportCenterSummary();
        if (_lblReportTodayOutput is not null) _lblReportTodayOutput.Text = summary.TodayOutput.ToString("N0");
        if (_lblReportMonthOutput is not null) _lblReportMonthOutput.Text = summary.MonthOutput.ToString("N0");
        if (_lblReportYield is not null) _lblReportYield.Text = summary.AvgYield.ToString("0.##");
        if (_lblReportOpenAbnormal is not null)
        {
            _lblReportOpenAbnormal.Text = summary.OpenAbnormal.ToString("N0");
            _lblReportOpenAbnormal.ForeColor = summary.OpenAbnormal > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }
        if (_lblReportOee is not null) _lblReportOee.Text = summary.AvgOee.ToString("0.#");
        if (_lblReportHint is not null)
            _lblReportHint.Text = $"统计 {daily.Count} 天 ｜ 今日产量环比 {summary.OutputWowText}";
    }

    /// <summary>一键导出报表中心（汇总 + 6 张明细表 = 7 个工作表）。</summary>
    private void BtnReportExport(object? sender, EventArgs e)
    {
        var from = ReportRangeStart();
        var to = DateTime.Today;
        var dlg = new SaveFileDialog
        {
            Title = "导出报表中心",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"MES报表_{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var w = new XlsxWriter();
            BuildReportWorkbook(w, from, to, 3, 10);
            var (sheets, rows) = w.Save(dlg.FileName);
            MessageBox.Show($"报表已导出：\n{dlg.FileName}\n\n工作表 {sheets} 个 ｜ 数据行 {rows} 行",
                "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"导出失败：{ex.Message}");
        }
    }

    /// <summary>构造报表中心工作簿内容（纯数据构造，无 UI / 文件依赖，便于自测）。</summary>
    internal static void BuildReportWorkbook(XlsxWriter w, DateTime from, DateTime to, int months = 3, int topN = 10)
    {
        var summary = MesDatabase.GetReportCenterSummary();

        var s0 = w.AddSheet("汇总");
        s0.SetColumns(("指标", 22), ("数值", 22));
        s0.AddRow("统计区间", $"{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}");
        s0.AddRow("今日产量（只）", summary.TodayOutput);
        s0.AddRow("今日产量环比", summary.OutputWowText);
        s0.AddRow("本月产量（只）", summary.MonthOutput);
        s0.AddRow("本月良率（%）", summary.AvgYield);
        s0.AddRow("未闭环异常（单）", summary.OpenAbnormal);
        s0.AddRow("设备 OEE 均值（%）", summary.AvgOee);
        s0.AddRow("设备数（台）", MesDatabase.Equipments.Count);
        s0.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        var s1 = w.AddSheet("产量日报");
        s1.SetColumns(("日期", 14), ("设备数", 9), ("产量", 11), ("单台日均", 11), ("过站次数", 11),
            ("测试数", 10), ("良品数", 10), ("不良数", 10), ("良率", 10));
        foreach (var r in MesDatabase.GetDailyOutputReport(from, to))
            s1.AddRow(r.Period, r.DeviceCount, r.Output, r.AvgPerDevice, r.StationCount,
                r.TestCount, r.PassCount, r.NgCount, r.TestCount == 0 ? "—" : r.YieldText);

        var s2 = w.AddSheet("产量月报");
        s2.SetColumns(("月份", 12), ("产量", 12), ("单台日均", 12), ("过站次数", 12), ("测试数", 12),
            ("不良数", 11), ("良率", 11), ("设备数", 10));
        foreach (var r in MesDatabase.GetMonthlyOutputReport(months))
            s2.AddRow(r.Period, r.Output, r.AvgPerDevice, r.StationCount, r.TestCount,
                r.NgCount, r.TestCount == 0 ? "—" : r.YieldText, r.DeviceCount);

        var s3 = w.AddSheet("良率趋势");
        s3.SetColumns(("日期", 14), ("测试数", 10), ("良品数", 10), ("不良数", 10),
            ("良率(%)", 10), ("目标(%)", 10), ("环比", 12), ("判定", 12));
        foreach (var r in MesDatabase.GetYieldTrendReport(from, to))
            s3.AddRow(r.Period, r.TestCount, r.PassCount, r.NgCount, r.YieldRate, r.TargetRate, r.DeltaText, r.Judge);

        var s4 = w.AddSheet("异常TOP");
        s4.SetColumns(("维度", 10), ("名称", 26), ("数量", 10), ("占比", 10));
        foreach (var r in MesDatabase.GetAbnormalTopReport(from, 8))
            s4.AddRow(r.Dimension, r.Name, r.Count, r.ShareText);

        var s5 = w.AddSheet("OEE汇总");
        s5.SetColumns(("设备编码", 14), ("设备", 16), ("工段", 12), ("状态", 9),
            ("今日OEE(%)", 12), ("目标(%)", 10), ("达成率", 10), ("判定", 10));
        foreach (var r in MesDatabase.GetOeeSummaryReport())
            s5.AddRow(r.DeviceCode, r.DeviceName, r.Section, r.Status, r.TodayOee, r.TargetOee, r.AchievementText, r.Judge);

        var s6 = w.AddSheet("库存汇总");
        s6.SetColumns(("维度", 10), ("类别/物料", 34), ("条目数", 10), ("数量合计", 12), ("占比", 10));
        foreach (var r in MesDatabase.GetStockSummaryReport(topN))
            s6.AddRow(r.Dimension, r.Name, r.ItemCount, r.TotalQty, r.ShareText);
    }


}
