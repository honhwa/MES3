using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分，预警中心）：**预警中心页**（顶层第 22 个页签）。
/// 职责：把散落在各模块的「待处理事项」聚合为一页，支持按来源/严重度筛选、双击跳转到对应页面处理、一键导出。
/// 说明：所有数据来自 <see cref="MesDatabase.GetCenterAlerts"/> 的纯查询投影，本文件不修改任何业务集合。
/// </summary>
public partial class MainForm
{
    /// <summary>构建「预警中心」页签：汇总卡 + 筛选条 + 预警列表 + 跳转/导出操作。</summary>
    private TabPage BuildAlertCenterTab()
    {
        var page = new TabPage("预警中心");

        // ---- 顶部汇总卡：高 / 中 / 低 / 合计 ----
        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("高危预警 (条)", Color.FromArgb(192, 0, 0), out _lblAlertHigh));
        cards.Controls.Add(MakeCard("中危预警 (条)", Color.FromArgb(230, 126, 34), out _lblAlertMedium));
        cards.Controls.Add(MakeCard("低危预警 (条)", Color.FromArgb(127, 140, 141), out _lblAlertLow));
        cards.Controls.Add(MakeCard("合计 (条)", Color.FromArgb(15, 76, 129), out _lblAlertTotal));

        page.Controls.Add(MakeGridGroup("待处理事项（按严重度排序；双击一行 → 跳转到对应页面处理）", out _alertGrid));
        page.Controls.Add(BuildAlertActionBar());
        page.Controls.Add(BuildAlertQueryBar());
        page.Controls.Add(cards);

        SetupAlertColumns();
        DoQueryAlerts();

        page.Enter -= AlertTab_Enter;
        page.Enter += AlertTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>构建筛选条：来源 / 严重度 / 刷新（下拉项在构造时填充，避免 Enter 时重建导致选中丢失）。</summary>
    private Control BuildAlertQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "来源：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboAlertSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(4, 4, 0, 0) };
        _cboAlertSource.Items.Add("全部");
        _cboAlertSource.Items.AddRange(AlertSource.All.Cast<object>().ToArray());
        _cboAlertSource.SelectedIndex = 0;

        bar.Controls.Add(new Label { Text = "严重度：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboAlertSeverity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(4, 4, 0, 0) };
        _cboAlertSeverity.Items.Add("全部");
        _cboAlertSeverity.Items.AddRange(AlertSeverity.All.Cast<object>().ToArray());
        _cboAlertSeverity.SelectedIndex = 0;

        bar.Controls.Add(_cboAlertSource);
        bar.Controls.Add(_cboAlertSeverity);
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryAlerts()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _cboAlertSource.SelectedIndex = 0;
            _cboAlertSeverity.SelectedIndex = 0;
            DoQueryAlerts();
        }));

        _lblAlertHint = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 10, 0, 0) };
        bar.Controls.Add(_lblAlertHint);
        return bar;
    }

    /// <summary>构建操作条：跳转处理 / 导出预警清单。</summary>
    private Control BuildAlertActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnAlertNavigate = MakeToolbarButton("跳转处理", BtnAlertNavigate);
        bar.Controls.Add(_btnAlertNavigate);
        bar.Controls.Add(MakeToolbarButton("导出清单", BtnAlertExport));
        bar.Controls.Add(new Label
        {
            Text = "预警来源包括：SPC / 异常 / 保留 / 点检保养 / 库存效期 / 上料防错 / 首件检验 / 返工 / 备件 / 出货绑定",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>设置预警列表列定义（显式列名，供绑定与着色使用）。</summary>
    private void SetupAlertColumns()
    {
        _alertGrid.AutoGenerateColumns = false;
        _alertGrid.Columns.Clear();
        _alertGrid.Columns.Add(MakeTextCol("Severity", "严重度", true, 8));
        _alertGrid.Columns.Add(MakeTextCol("Source", "来源", true, 12));
        _alertGrid.Columns.Add(MakeTextCol("Title", "待处理事项", true, 34));
        _alertGrid.Columns.Add(MakeTextCol("Detail", "详情", true, 46));
        _alertGrid.Columns.Add(MakeTextCol("TargetText", "关联对象", true, 14));
        _alertGrid.Columns.Add(MakeTextCol("OccurText", "触发时间", true, 12));

        _alertGrid.SelectionChanged -= AlertGrid_SelectionChanged;
        _alertGrid.SelectionChanged += AlertGrid_SelectionChanged;
        _alertGrid.CellFormatting -= AlertGrid_CellFormatting;
        _alertGrid.CellFormatting += AlertGrid_CellFormatting;
        _alertGrid.CellDoubleClick -= AlertGrid_CellDoubleClick;
        _alertGrid.CellDoubleClick += AlertGrid_CellDoubleClick;
        _alertGrid.RowPrePaint += (_, e) => { };   // 预留：行级自定义绘制（当前无需）
    }

    /// <summary>严重度着色：高=红、中=橙、低=灰（整行文字随严重度着色，便于扫视）。</summary>
    private void AlertGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_alertGrid.Rows[e.RowIndex].DataBoundItem is not AlertItem item) return;

        // 仅对"严重度/待处理事项"两列着色，保持详情可读性
        var col = _alertGrid.Columns[e.ColumnIndex].Name;
        if (col != "Severity" && col != "Title") return;

        e.CellStyle.ForeColor = item.Severity switch
        {
            AlertSeverity.High => Color.FromArgb(192, 0, 0),
            AlertSeverity.Medium => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(127, 140, 141)
        };
    }

    private void AlertTab_Enter(object? sender, EventArgs e) => EnsureAlertReady();

    /// <summary>兜底初始化：首次进入预警中心时加载数据（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureAlertReady()
    {
        if (_alertGrid is null) return;
        if (_alertGrid.Rows.Count == 0) DoQueryAlerts();
    }

    private void AlertGrid_SelectionChanged(object? sender, EventArgs e) => UpdateAlertButtons();

    private void AlertGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        BtnAlertNavigate(null, EventArgs.Empty);
    }

    /// <summary>按选中项更新按钮可用状态。</summary>
    private void UpdateAlertButtons()
    {
        if (_btnAlertNavigate is not null) _btnAlertNavigate.Enabled = SelectedAlert() is not null;
    }

    private AlertItem? SelectedAlert()
    {
        if (_alertGrid.SelectedRows.Count == 0) return null;
        return _alertGrid.SelectedRows[0].DataBoundItem as AlertItem;
    }

    /// <summary>加载预警列表：按来源/严重度筛选 + 刷新汇总卡与提示。</summary>
    private void DoQueryAlerts()
    {
        if (_alertGrid is null) return;

        var source = _cboAlertSource is not null && _cboAlertSource.SelectedIndex > 0
            ? _cboAlertSource.SelectedItem?.ToString()
            : null;
        var severity = _cboAlertSeverity is not null && _cboAlertSeverity.SelectedIndex > 0
            ? _cboAlertSeverity.SelectedItem?.ToString()
            : null;

        var list = MesDatabase.GetCenterAlerts();
        if (source is not null) list = list.Where(a => a.Source == source).ToList();
        if (severity is not null) list = list.Where(a => a.Severity == severity).ToList();

        _alertGrid.DataSource = null;
        _alertGrid.DataSource = list;

        var (high, medium, low, total) = MesDatabase.GetAlertCenterSummary();
        if (_lblAlertHigh is not null)
        {
            _lblAlertHigh.Text = high.ToString();
            _lblAlertHigh.ForeColor = high > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }
        if (_lblAlertMedium is not null) _lblAlertMedium.Text = medium.ToString();
        if (_lblAlertLow is not null) _lblAlertLow.Text = low.ToString();
        if (_lblAlertTotal is not null) _lblAlertTotal.Text = total.ToString();

        if (_lblAlertHint is not null)
        {
            var bySource = MesDatabase.GetAlertBySource();
            var top = bySource.Count == 0 ? "无" : string.Join("、", bySource.Take(3).Select(x => $"{x.Source} {x.Count}"));
            _lblAlertHint.Text = $"当前显示 {list.Count} / 共 {total} 条 ｜ 高危 {high} 条 ｜ 来源 TOP3：{top}";
        }

        if (_alertGrid.Rows.Count > 0)
        {
            _alertGrid.ClearSelection();
            _alertGrid.Rows[0].Selected = true;
        }
        UpdateAlertButtons();
    }

    /// <summary>把选中预警跳转到来源页面处理；若目标页支持定位，则同时选中对应单据/记录。</summary>
    private void BtnAlertNavigate(object? sender, EventArgs e)
    {
        var item = SelectedAlert();
        if (item is null) { Warn("请先在列表中选择一条预警。"); return; }
        NavigateToAlert(item);
    }

    /// <summary>
    /// 预警跳转路由：来源 →（页面 + 定位）。
    /// 说明：切换页签会触发目标页的 Enter 事件完成数据加载；支持定位的页面会同时选中关联单据。
    /// </summary>
    private void NavigateToAlert(AlertItem item)
    {
        switch (item.Source)
        {
            case AlertSource.Spc:
                GoTab("SPC 分析");
                break;
            case AlertSource.Abnormal:
                GoTab("异常管理");
                break;
            case AlertSource.Hold:
                GoTab("保留管理");
                break;
            case AlertSource.Tpm:
            case AlertSource.Spare:
                GoTab("点检保养");
                break;
            case AlertSource.Expiry:
                GoTab("WMS 仓储");
                break;
            case AlertSource.Feeding:
                GoTab("上料防错");
                break;
            case AlertSource.Fai:
                GoTab("首件检验");
                SelectFaiRow(item.TargetKey);
                break;
            case AlertSource.Rework:
                GoTab("返工管理");
                SelectReworkRow(item.TargetKey);
                break;
            case AlertSource.Shipment:
                GoTab("出货与召回");
                SelectShipOrder(item.TargetKey);
                break;
            default:
                Warn($"来源「{item.Source}」暂不支持跳转。");
                return;
        }

        if (_lblAlertHint is not null)
            _lblAlertHint.Text = $"已跳转到「{item.Source}」对应页面，请处理：{item.Title}（关联 {item.TargetText}）";
    }

    /// <summary>按页签标题切换顶层页签。</summary>
    private void GoTab(string title)
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            if (page.Text != title) continue;
            _tabs.SelectedTab = page;
            return;
        }
        Warn($"未找到页签「{title}」。");
    }

    /// <summary>导出预警清单（含汇总 + 明细 + 按来源统计，3 个工作表）。</summary>
    private void BtnAlertExport(object? sender, EventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出预警清单",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"预警清单_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var w = new XlsxWriter();
            BuildAlertCenterWorkbook(w);
            var (sheets, rows) = w.Save(dlg.FileName);
            MessageBox.Show($"预警清单已导出：\n{dlg.FileName}\n\n工作表 {sheets} 个 ｜ 数据行 {rows} 行",
                "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"导出失败：{ex.Message}");
        }
    }

    /// <summary>构造预警中心导出工作簿（汇总 / 明细 / 按来源统计）。纯数据构造，便于自测。</summary>
    internal static void BuildAlertCenterWorkbook(XlsxWriter w)
    {
        var (high, medium, low, total) = MesDatabase.GetAlertCenterSummary();
        var alerts = MesDatabase.GetCenterAlerts();

        var s0 = w.AddSheet("汇总");
        s0.SetColumns(("指标", 22), ("数值", 16));
        s0.AddRow("高危预警（条）", high);
        s0.AddRow("中危预警（条）", medium);
        s0.AddRow("低危预警（条）", low);
        s0.AddRow("合计（条）", total);
        s0.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        var s1 = w.AddSheet("预警明细");
        s1.SetColumns(("严重度", 9), ("来源", 13), ("待处理事项", 34), ("详情", 56), ("关联对象", 15), ("触发时间", 14));
        foreach (var a in alerts)
            s1.AddRow(a.Severity, a.Source, a.Title, a.Detail, a.TargetText, a.OccurText);

        var s2 = w.AddSheet("按来源统计");
        s2.SetColumns(("来源", 16), ("数量", 10), ("其中高危", 12));
        foreach (var (source, count, highCount) in MesDatabase.GetAlertBySource())
            s2.AddRow(source, count, highCount);
    }
}
