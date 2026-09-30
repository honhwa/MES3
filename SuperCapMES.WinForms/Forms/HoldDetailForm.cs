using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 保留明细窗体：双击「保留管理」页保留单行后弹出，
/// 顶部展示保留单概要，主体以只读表格展示该保留组内被保留电芯的明细
/// （SN/型号/线体/当前状态/分档/极片批号/电解液批号/生产时间）。
/// 数据按 holdNo 实时查询（MesDatabase.GetHoldItemDetails），保持数据新鲜。
/// </summary>
public class HoldDetailForm : Form
{
    private readonly string _holdNo;

    // 概要标签引用（保留单可能不存在/已变更，均按最新数据刷新文本）
    private Label _lblHoldNo = null!;
    private Label _lblLocation = null!;
    private Label _lblStatus = null!;
    private Label _lblCount = null!;
    private Label _lblOperator = null!;
    private Label _lblHoldTime = null!;
    private Label _lblReason = null!;
    private DataGridView _detailGrid = null!;

    public HoldDetailForm(string holdNo)
    {
        _holdNo = string.IsNullOrWhiteSpace(holdNo) ? "" : holdNo.Trim();

        Text = $"保留明细 - {(_holdNo.Length == 0 ? "未知" : _holdNo)}";
        Font = new Font("微软雅黑", 9F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 620);
        MinimumSize = new Size(860, 520);

        BuildUi();
    }

    private void BuildUi()
    {
        var summary = BuildSummaryPanel();
        var gridGroup = MakeGridGroup("被保留电芯明细", out _detailGrid);
        var bottom = BuildBottomBar();

        // Dock 布局：后添加的控件先贴边，故先加 fill（主体表格）再依次加顶部概要、底部按钮条
        Controls.Add(gridGroup);
        Controls.Add(summary);
        Controls.Add(bottom);

        BindDetailGrid();
    }

    // ================= 顶部概要 =================

    private Control BuildSummaryPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 120,
            BackColor = Color.White,
            Padding = new Padding(10, 6, 10, 4)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hold = MesDatabase.Holds.FirstOrDefault(h => h.HoldNo == _holdNo);

        _lblHoldNo = MakeInfoLabel($"保留编号：{_holdNo}");
        _lblLocation = MakeInfoLabel($"保留位置：{hold?.HoldLocation ?? "—"}");
        _lblStatus = MakeInfoLabel($"状态：{hold?.Status ?? "—"}");
        _lblCount = MakeInfoLabel($"电芯数：{MesDatabase.GetHoldItems(_holdNo).Count} 只");
        _lblOperator = MakeInfoLabel($"操作人：{hold?.Operator ?? "—"}");
        _lblHoldTime = MakeInfoLabel($"保留时间：{(hold is null ? "—" : hold.HoldTime.ToString("yyyy-MM-dd HH:mm"))}");

        // 保留中/已解除着色：与主窗体保留管理页风格一致
        if (hold is not null)
        {
            var statusColor = hold.Status == HoldStatus.Active
                ? Color.FromArgb(192, 0, 0)
                : Color.FromArgb(39, 174, 96);
            _lblStatus.ForeColor = statusColor;
            _lblStatus.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }

        _lblReason = MakeInfoLabel($"保留原因：{hold?.Reason ?? "—"}");
        _lblReason.Dock = DockStyle.Fill;
        _lblReason.TextAlign = ContentAlignment.MiddleLeft;
        _lblReason.AutoEllipsis = true;

        grid.Controls.Add(_lblHoldNo, 0, 0);
        grid.Controls.Add(_lblLocation, 1, 0);
        grid.Controls.Add(_lblStatus, 2, 0);
        grid.Controls.Add(_lblCount, 3, 0);
        grid.Controls.Add(_lblOperator, 0, 1);
        grid.Controls.Add(_lblHoldTime, 1, 1);
        grid.Controls.Add(_lblReason, 2, 1);
        grid.SetColumnSpan(_lblReason, 2);

        // 第三行：解除信息（若已解除）与材料信息，作为补充概要
        var extra = new List<string>();
        if (hold is not null && hold.Status == HoldStatus.Released)
            extra.Add($"解除人：{hold.ReleaseOperator} / 解除时间：{(hold.ReleaseTime.HasValue ? hold.ReleaseTime.Value.ToString("yyyy-MM-dd HH:mm") : "—")}");
        if (hold is not null && !string.IsNullOrEmpty(hold.LotNo))
            extra.Add($"关联批次：{hold.LotNo}");
        var lblExtra = MakeInfoLabel(extra.Count == 0 ? "" : string.Join("    ", extra));
        grid.Controls.Add(lblExtra, 0, 2);
        grid.SetColumnSpan(lblExtra, 4);

        panel.Controls.Add(grid);
        return panel;
    }

    private static Label MakeInfoLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            AutoEllipsis = true
        };

    // ================= 底部按钮条 =================

    private Control BuildBottomBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.WhiteSmoke
        };
        var btnClose = new Button
        {
            Text = "关闭",
            Width = 100,
            Height = 32,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        btnClose.Location = new Point(bar.Width - 116, 9);
        bar.Resize += (_, _) => btnClose.Location = new Point(bar.Width - 116, 9);
        btnClose.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        bar.Controls.Add(btnClose);
        return bar;
    }

    // ================= 明细网格 =================

    private void BindDetailGrid()
    {
        var rows = MesDatabase.GetHoldItemDetails(_holdNo);

        _detailGrid.AutoGenerateColumns = false;
        _detailGrid.DataSource = null;
        _detailGrid.Columns.Clear();

        // 注意：手写列必须显式设置 Name，否则 row.Cells["xx"] 会抛异常
        _detailGrid.Columns.Add(MakeCol("CellSn", "电芯SN", "CellSn", 18));
        _detailGrid.Columns.Add(MakeCol("ProductModel", "型号", "ProductModel", 10));
        _detailGrid.Columns.Add(MakeCol("Line", "线体", "Line", 6));
        _detailGrid.Columns.Add(MakeCol("Status", "当前状态", "Status", 8));
        _detailGrid.Columns.Add(MakeCol("Result", "分档", "Result", 6));
        _detailGrid.Columns.Add(MakeCol("ElectrodeLot", "极片批号", "ElectrodeLot", 13));
        _detailGrid.Columns.Add(MakeCol("ElectrolyteLot", "电解液批号", "ElectrolyteLot", 13));
        var timeCol = MakeCol("CreateTime", "生产时间", "CreateTime", 14);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _detailGrid.Columns.Add(timeCol);

        _detailGrid.DataSource = rows;

        _detailGrid.CellFormatting -= DetailGrid_CellFormatting;
        _detailGrid.CellFormatting += DetailGrid_CellFormatting;
    }

    /// <summary>状态/分档/生产时间着色与缺省值展示：不良与 NG 标红，合格标绿，缺省时间显示"—"</summary>
    private void DetailGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        var colName = grid.Columns[e.ColumnIndex].Name;

        if (colName == "CreateTime" && e.Value is DateTime dt && dt == default)
        {
            e.Value = "—";
            e.FormattingApplied = true;
            return;
        }

        if (colName is "Status" or "Result")
        {
            var text = e.Value?.ToString() ?? "";
            if (text == "不良" || text == "NG")
            {
                e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
                e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            }
            else if (text == "合格" || text == "OK" || text == "A1" || text == "A2" || text == "B1")
            {
                e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);
            }
        }
    }

    /// <summary>手写列工厂：Name / HeaderText / DataPropertyName 全部显式赋值</summary>
    private static DataGridViewTextBoxColumn MakeCol(string name, string header, string prop, int fillWeight) =>
        new()
        {
            Name = name,
            HeaderText = header,
            DataPropertyName = prop,
            ReadOnly = true,
            FillWeight = fillWeight
        };

    private static DataGridView MakeGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15, 76, 129),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.Black,
                SelectionBackColor = Color.FromArgb(207, 224, 239),
                SelectionForeColor = Color.Black,
                Font = new Font("微软雅黑", 9F)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) }
        };
        return g;
    }

    private static Control MakeGridGroup(string title, out DataGridView grid)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6, 12, 6, 6) };
        grid = MakeGrid();
        box.Controls.Add(grid);
        return box;
    }
}
