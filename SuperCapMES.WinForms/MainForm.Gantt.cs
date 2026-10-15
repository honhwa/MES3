using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类：智能排产甘特图页（生产管理分类）。
/// 把待下发 ERP 订单的排产建议（推荐线体 + 开工/完工）画成甘特：纵轴产线、横轴日期。
/// 口径：各单独立按当前线负载推算，未累加同批订单相互影响（演示近似）；赶不及交期标红。
/// </summary>
public partial class MainForm
{
    private DataGridView _ganttGrid = null!;
    private ComboBox _cboGanttDays = null!;
    private Label _lblGanttDetail = null!, _lblGanttHint = null!;
    private ScheduleGantt _gantt = null!;

    private TabPage BuildScheduleGanttTab()
    {
        var page = new TabPage("排产甘特图");
        page.Padding = new Padding(4);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "时间窗口：", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cboGanttDays = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(0, 4, 10, 0) };
        _cboGanttDays.Items.AddRange(new object[] { "未来 14 天", "未来 30 天", "未来 60 天" });
        _cboGanttDays.SelectedIndex = 1;   // 默认 30 天
        _cboGanttDays.SelectedIndexChanged += (_, _) => LoadScheduleGantt();
        bar.Controls.Add(_cboGanttDays);
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => LoadScheduleGantt()));
        _lblGanttHint = new Label
        {
            AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(12, 10, 0, 0),
            Text = "口径：同线订单按交期顺序错开排布（批量排产近似，起点接该线在制忙完时间）；赶不及交期标红。"
        };
        bar.Controls.Add(_lblGanttHint);

        _ganttGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
            RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            Font = new Font("微软雅黑", 8.5F),
            RowTemplate = { Height = 40 },
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            EditMode = DataGridViewEditMode.EditProgrammatically
        };
        _ganttGrid.CellClick += GanttGrid_CellClick;
        _ganttGrid.EnableHeadersVisualStyles = false;

        _lblGanttDetail = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 88, ForeColor = Color.FromArgb(30, 41, 59),
            Font = new Font("微软雅黑", 9F), Padding = new Padding(6, 6, 6, 0),
            Text = "点击甘特图中任意格子，查看该产线当天的订单明细。"
        };

        var legend = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 24, Padding = new Padding(6, 2, 6, 0),
            Font = new Font("微软雅黑", 8.5F), ForeColor = Color.Gray,
            Text = "图例：■ 绿色=按期有余量　■ 黄色=偏紧　■ 红色=赶不及交期　■ 橙色格=当天多单竞争同线　▎金色列表头=今天"
        };

        page.Controls.Add(_ganttGrid);       // Fill
        page.Controls.Add(bar);              // Top
        page.Controls.Add(legend);           // Bottom
        page.Controls.Add(_lblGanttDetail);  // Bottom

        page.Enter -= ScheduleGanttTab_Enter;
        page.Enter += ScheduleGanttTab_Enter;
        LoadScheduleGantt();
        return page;
    }

    private void ScheduleGanttTab_Enter(object? sender, EventArgs e) => LoadScheduleGantt();

    private int GanttDays() => _cboGanttDays?.SelectedIndex switch
    {
        0 => 14,
        2 => 60,
        _ => 30
    };

    /// <summary>取数 + 渲染甘特（构造期控件未建则跳过）。</summary>
    private void LoadScheduleGantt()
    {
        if (_ganttGrid is null) return;
        _gantt = MesDatabase.GetScheduleGantt(GanttDays());
        var g = _gantt;

        _ganttGrid.SuspendLayout();
        _ganttGrid.Columns.Clear();
        _ganttGrid.Rows.Clear();

        // 列 0：产线（行头）
        _ganttGrid.Columns.Add("line", "产线");
        _ganttGrid.Columns["line"]!.Width = 66;
        _ganttGrid.Columns["line"]!.Frozen = true;
        _ganttGrid.Columns["line"]!.SortMode = DataGridViewColumnSortMode.NotSortable;
        // 列 1..N：日期
        for (int i = 0; i < g.Dates.Count; i++)
        {
            var d = g.Dates[i];
            var isToday = i == g.TodayIndex;
            var col = new DataGridViewTextBoxColumn
            {
                Name = "d" + i,
                HeaderText = (isToday ? "今·" : "") + d.ToString("MM-dd"),
                Width = 50,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter, Font = new Font("微软雅黑", 8.5F) }
            };
            if (isToday) col.HeaderCell.Style.BackColor = Color.FromArgb(245, 200, 66);
            _ganttGrid.Columns.Add(col);
        }

        // 行：每产线一行
        foreach (var line in g.Lines)
        {
            var rowIndex = _ganttGrid.Rows.Add();
            var row = _ganttGrid.Rows[rowIndex];
            row.Cells[0].Value = line;
            row.Cells[0].Style.BackColor = Color.FromArgb(241, 245, 249);
            row.Cells[0].Style.Font = new Font("微软雅黑", 9F, FontStyle.Bold);

            for (int i = 0; i < g.Dates.Count; i++)
            {
                var day = g.Dates[i];
                var cell = row.Cells[i + 1];
                var bars = g.BarsOn(line, day);
                if (bars.Count == 0) continue;

                bool late = bars.Any(b => b.Late);
                bool tight = bars.Any(b => b.Risk.Contains("紧"));
                Color fill = late ? Color.FromArgb(244, 120, 120)
                           : tight ? Color.FromArgb(245, 200, 120)
                           : bars.Count > 1 ? Color.FromArgb(245, 165, 90)
                           : Color.FromArgb(150, 214, 170);
                cell.Style.BackColor = fill;
                cell.Style.ForeColor = Color.FromArgb(20, 30, 45);
                cell.ToolTipText = string.Join("\n", bars.Select(b =>
                    $"{b.PoNo} ｜ {b.ProductModel} ｜ {b.OrderQty:N0} 只\n  开工 {b.Start:MM-dd} → 完工 {b.Finish:MM-dd} ｜ 交期 {b.DueDate:MM-dd}"
                    + (b.Late ? $" ｜ 赶不及 {Math.Abs(b.SlackDays):0.#} 天" : $" ｜ 余量 {b.SlackDays:0.#} 天")));
                cell.Value = bars.Count > 1
                    ? bars.Count + "单"
                    : TailNo(bars[0].PoNo) + "\n" + (bars[0].OrderQty / 1000.0).ToString("0.#") + "k";
            }
        }
        _ganttGrid.ResumeLayout();

        if (g.Bars.Count == 0)
            _lblGanttDetail.Text = "当前没有待下发的 ERP 订单，甘特为空。可到「工单管理 › ERP 下发」拉取订单。";
    }

    private static string TailNo(string poNo)
    {
        if (string.IsNullOrEmpty(poNo)) return "";
        var parts = poNo.Split('-');
        return parts.Length > 0 ? parts[^1] : poNo;
    }

    private void GanttGrid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (_gantt is null || e.RowIndex < 0 || e.ColumnIndex < 1) return;
        var line = _ganttGrid.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "";
        var day = _gantt.Dates[e.ColumnIndex - 1];
        var bars = _gantt.BarsOn(line, day);
        if (bars.Count == 0) { _lblGanttDetail.Text = $"{line} · {day:MM-dd}：无订单占用。"; return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{line} · {day:yyyy-MM-dd}（{bars.Count} 单）：");
        foreach (var b in bars)
            sb.AppendLine($"· {b.PoNo} ｜ {b.ProductModel} ｜ {b.OrderQty:N0} 只 ｜ 开工 {b.Start:MM-dd} → 完工 {b.Finish:MM-dd}"
                        + (b.Late ? $" ｜ 赶不及 {Math.Abs(b.SlackDays):0.#} 天" : $" ｜ 余量 {b.SlackDays:0.#} 天"));
        _lblGanttDetail.Text = sb.ToString();
    }
}
