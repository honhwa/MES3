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
/// MainForm 分部类（按功能域拆分，第 8 部分）：保留管理页（保留单查询 / 解除保留 / 履历 / 扫描不合格自动保留）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Hold」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 保留管理 =================
    /// <summary>构建「HoldManage」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildHoldManageTab()
    {
        var page = new TabPage("保留管理");
        page.Padding = new Padding(6);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("保留单列表（选中一行 → 下方查看履历；双击一行 → 查看被保留电芯明细；可解除）", out _holdGrid));
        split.Panel2.Controls.Add(MakeGridGroup("保留 / 解除履历", out _holdHistoryGrid));
        page.Controls.Add(split);

        page.Controls.Add(BuildHoldReleaseBar());
        page.Controls.Add(BuildHoldQueryBar());

        SetupHoldGridColumns();
        SetupHoldHistoryColumns();
        DoQueryHolds();
        return page;
    }

    /// <summary>构建「Hold」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildHoldQueryBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(6, 8, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "保留编号：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _txtQueryHoldNo = new TextBox { Width = 170, Margin = new Padding(6, 4, 0, 0) };
        _txtQueryHoldNo.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoQueryHolds(); };
        bar.Controls.Add(_txtQueryHoldNo);

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboHoldStatusFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90,
            Margin = new Padding(6, 4, 0, 0)
        };
        _cboHoldStatusFilter.Items.AddRange(new object[] { "全部", HoldStatus.Active, HoldStatus.Released });
        _cboHoldStatusFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboHoldStatusFilter);

        bar.Controls.Add(new Label { Text = "保留位置：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboHoldLocationFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110,
            Margin = new Padding(6, 4, 0, 0)
        };
        _cboHoldLocationFilter.Items.Add("全部");
        _cboHoldLocationFilter.Items.AddRange(HoldLocations.All);
        _cboHoldLocationFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboHoldLocationFilter);

        _chkUseTimeFilter = new CheckBox
        {
            Text = "按时间",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(12, 8, 0, 0)
        };
        _chkUseTimeFilter.CheckedChanged += (_, _) =>
        {
            var enabled = _chkUseTimeFilter.Checked;
            _dtHoldFrom.Enabled = enabled;
            _dtHoldTo.Enabled = enabled;
        };
        bar.Controls.Add(_chkUseTimeFilter);

        _dtHoldFrom = MakeHoldDatePicker();
        _dtHoldFrom.Value = DateTime.Today.AddDays(-30);
        bar.Controls.Add(_dtHoldFrom);
        bar.Controls.Add(new Label { Text = " ~ ", AutoSize = true, Margin = new Padding(2, 8, 0, 0) });
        _dtHoldTo = MakeHoldDatePicker();
        _dtHoldTo.Value = DateTime.Today;
        bar.Controls.Add(_dtHoldTo);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryHolds()));
        bar.Controls.Add(MakeToolbarButton("重置", BtnResetHoldQuery));

        _btnScanNonconforming = MakeToolbarButton("扫描不合格电芯并自动保留", BtnScanNonconforming);
        _btnScanNonconforming.Click -= BtnScanNonconforming;
        _btnScanNonconforming.Click += BtnScanNonconforming;
        bar.Controls.Add(_btnScanNonconforming);

        _lblHoldQueryResult = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(14, 8, 0, 0)
        };
        bar.Controls.Add(_lblHoldQueryResult);
        return bar;
    }

    /// <summary>创建「HoldDatePicker」控件/样式（统一样式的工厂方法）。</summary>
    private static DateTimePicker MakeHoldDatePicker() =>
        new()
        {
            Width = 150,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Margin = new Padding(4, 4, 0, 0)
        };

    /// <summary>构建「HoldReleaseBar」界面区块。</summary>
    private Control BuildHoldReleaseBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(8, 6, 4, 0),
            WrapContents = false
        };
        _lblHoldContext = new Label
        {
            Text = "请选择一条保留记录",
            AutoSize = true,
            Margin = new Padding(2, 8, 0, 0),
            ForeColor = Color.FromArgb(40, 50, 60)
        };
        bar.Controls.Add(_lblHoldContext);
        bar.Controls.Add(MakeToolbarButton("打印保留单", BtnPrintHoldSlip));
        _btnReleaseHold = MakeToolbarButton("解除保留", BtnReleaseHold);
        _btnReleaseHold.Enabled = false;
        bar.Controls.Add(_btnReleaseHold);
        return bar;
    }

    /// <summary>设置「HoldGridColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupHoldGridColumns()
    {
        _holdGrid.AutoGenerateColumns = false;
        _holdGrid.Columns.Clear();

        _holdGrid.Columns.Add(MakeTextCol("HoldNo", "保留编号", true, 15));
        _holdGrid.Columns.Add(MakeTextCol("HoldLocation", "保留位置", true, 10));
        _holdGrid.Columns.Add(MakeTextCol("CellCount", "电芯数", true, 6));
        _holdGrid.Columns.Add(MakeTextCol("Reason", "保留原因", true, 18));
        _holdGrid.Columns.Add(MakeTextCol("Operator", "操作人", true, 8));
        var timeCol = MakeTextCol("HoldTime", "保留时间", true, 13);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _holdGrid.Columns.Add(timeCol);
        _holdGrid.Columns.Add(MakeTextCol("Status", "状态", true, 7));
        _holdGrid.Columns.Add(MakeTextCol("SourceType", "来源", true, 9));
        _holdGrid.Columns.Add(MakeTextCol("ReleaseOperator", "解除人", true, 8));
        var relCol = MakeTextCol("ReleaseTime", "解除时间", true, 13);
        relCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _holdGrid.Columns.Add(relCol);

        // 先解除再挂接着色事件（同上，防止重复着色）
        _holdGrid.CellFormatting -= HoldGrid_CellFormatting;
        _holdGrid.CellFormatting += HoldGrid_CellFormatting;
        _holdGrid.SelectionChanged += (_, _) => LoadHoldDetails();
        _holdGrid.CellDoubleClick -= HoldGrid_CellDoubleClick;
        _holdGrid.CellDoubleClick += HoldGrid_CellDoubleClick;
    }

    /// <summary>设置「HoldHistoryColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupHoldHistoryColumns()
    {
        _holdHistoryGrid.AutoGenerateColumns = false;
        _holdHistoryGrid.Columns.Clear();
        _holdHistoryGrid.Columns.Add(MakeTextCol("ActionType", "动作", true, 8));
        var timeCol = MakeTextCol("Time", "时间", true, 16);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm:ss";
        _holdHistoryGrid.Columns.Add(timeCol);
        _holdHistoryGrid.Columns.Add(MakeTextCol("Operator", "操作人", true, 10));
        _holdHistoryGrid.Columns.Add(MakeTextCol("Reason", "原因", true, 26));
        _holdHistoryGrid.Columns.Add(MakeTextCol("SnPreview", "SN摘要", true, 34));
    }

    /// <summary>「HoldGrid」网格单元格着色：按状态/结果区分颜色（差异红、异常橙、正常绿等）。</summary>
    private void HoldGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name != "Status") return;

        var value = e.Value?.ToString();
        if (value == HoldStatus.Active)
        {
            e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
        else if (value == HoldStatus.Released)
        {
            e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);
        }
    }

    /// <summary>双击保留单行 → 弹出该保留组内被保留电芯的明细窗体（只读查询，无需回写刷新）</summary>
    private void HoldGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_holdGrid.Rows[e.RowIndex].DataBoundItem is not CellHold hold) return;
        if (string.IsNullOrEmpty(hold.HoldNo)) return;

        using var form = new HoldDetailForm(hold.HoldNo);
        form.ShowDialog(this);
    }

    /// <summary>查询并加载「Holds」数据到界面（含网格绑定与汇总刷新）。</summary>
    private void DoQueryHolds()
    {
        var noText = _txtQueryHoldNo.Text.Trim();
        var status = _cboHoldStatusFilter.SelectedItem?.ToString();
        if (status is null or "全部") status = null;
        var location = _cboHoldLocationFilter.SelectedItem?.ToString();
        if (location is null or "全部") location = null;

        DateTime? from = null;
        DateTime? to = null;
        if (_chkUseTimeFilter.Checked)
        {
            from = _dtHoldFrom.Value.Date;
            to = _dtHoldTo.Value.Date;
        }

        var list = MesDatabase.QueryHolds(
            string.IsNullOrEmpty(noText) ? null : noText,
            status,
            location,
            from,
            to);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _holdGrid.DataSource = null;
        _holdGrid.DataSource = list;
        _lblHoldQueryResult.Text = $"共查询到 {list.Count} 条保留单";
        LoadHoldDetails();
    }

    /// <summary>处理「ResetHoldQuery」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnResetHoldQuery(object? sender, EventArgs e)
    {
        _txtQueryHoldNo.Text = "";
        _cboHoldStatusFilter.SelectedIndex = 0;
        _cboHoldLocationFilter.SelectedIndex = 0;
        _chkUseTimeFilter.Checked = true;
        _dtHoldFrom.Value = DateTime.Today.AddDays(-30);
        _dtHoldTo.Value = DateTime.Today;
        DoQueryHolds();
    }

    /// <summary>质量事件自动联动入口：扫描测试/检验不合格电芯 → 自动合并成一张保留单（待检区）。</summary>
    private void BtnScanNonconforming(object? sender, EventArgs e)
    {
        var (ok, msg, holdNo, _, _) = MesDatabase.AutoHoldNonconforming(null, HoldLocations.PendingInspection, "质量系统");
        if (ok) MessageBox.Show(msg, "自动保留", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else Warn(msg);
        DoQueryHolds();
        if (ok && !string.IsNullOrEmpty(holdNo)) SelectHold(holdNo);
    }

    /// <summary>在网格中选中「Hold」对应的行（联动加载明细）。</summary>
    private void SelectHold(string holdNo)
    {
        if (_holdGrid is null || string.IsNullOrEmpty(holdNo)) return;
        foreach (DataGridViewRow row in _holdGrid.Rows)
        {
            if (row.DataBoundItem is CellHold h && string.Equals(h.HoldNo, holdNo, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                _holdGrid.CurrentCell = row.Cells[0];
                _holdGrid.FirstDisplayedScrollingRowIndex = row.Index;
                LoadHoldDetails();
                break;
            }
        }
    }

    /// <summary>在网格中选中「edHold」对应的行（联动加载明细）。</summary>
    private CellHold? SelectedHold()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_holdGrid.SelectedRows.Count == 0) return null;
        return _holdGrid.SelectedRows[0].DataBoundItem as CellHold;
    }

    /// <summary>加载「HoldDetails」并绑定到界面。</summary>
    private void LoadHoldDetails()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_holdGrid is null || _holdHistoryGrid is null || _btnReleaseHold is null || _lblHoldContext is null) return;
        var hold = SelectedHold();
        if (hold is null)
        {
            _lblHoldContext.Text = "请选择一条保留记录";
            _btnReleaseHold.Enabled = false;
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _holdHistoryGrid.DataSource = null;
            return;
        }
        _lblHoldContext.Text = $"{hold.HoldNo} · {hold.Status} · {hold.CellCount} 只电芯 · 保留位置 {hold.HoldLocation}";
        _btnReleaseHold.Enabled = hold.Status == HoldStatus.Active;
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _holdHistoryGrid.DataSource = null;
        _holdHistoryGrid.DataSource = MesDatabase.GetHoldHistory(hold.HoldNo);
    }

    /// <summary>处理「ReleaseHold」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnReleaseHold(object? sender, EventArgs e)
    {
        var hold = SelectedHold();
        if (hold is null)
        {
            Warn("请先选择要解除的保留单。");
            return;
        }
        if (hold.Status != HoldStatus.Active)
        {
            Warn($"保留单 {hold.HoldNo} 当前状态为「{hold.Status}」，无需解除。");
            return;
        }

        using var form = new ReleaseHoldForm(hold.HoldNo);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, message) = MesDatabase.ReleaseHold(hold.HoldNo, form.OperatorName, form.ReleaseReason);
        if (!ok)
        {
            Warn(message);
            return;
        }
        MessageBox.Show(message, "解除保留", MessageBoxButtons.OK, MessageBoxIcon.Information);

        DoQueryHolds();
        // 若解除后的保留单仍在当前筛选结果中，重新选中它
        foreach (DataGridViewRow row in _holdGrid.Rows)
        {
            if (row.DataBoundItem is CellHold h && h.HoldNo == hold.HoldNo)
            {
                _holdGrid.ClearSelection();
                row.Selected = true;
                _holdGrid.CurrentCell = row.Cells[0];
                break;
            }
        }
        LoadHoldDetails();
    }


}
