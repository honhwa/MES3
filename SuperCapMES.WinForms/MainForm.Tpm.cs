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
/// MainForm 分部类（按功能域拆分，第 17 部分）：点检保养页（点检/保养计划与到期预警 + 执行记录 + 备件台账与领用）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Tpm」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 点检保养 TPM 与备件（顶层第 21 个页签） =================

    /// <summary>点检保养页：设备点检/保养计划与到期预警 + 执行记录 + 备件台账（含低库存预警）。</summary>
    private TabPage BuildTpmTab()
    {
        var page = new TabPage("点检保养");

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("点检计划 (项)", Color.FromArgb(15, 76, 129), out _lblTpmTotal));
        cards.Controls.Add(MakeCard("已超期 (项)", Color.FromArgb(192, 0, 0), out _lblTpmOverdue));
        cards.Controls.Add(MakeCard("即将到期 (项)", Color.FromArgb(230, 126, 34), out _lblTpmDueSoon));
        cards.Controls.Add(MakeCard("备件低库存 (项)", Color.FromArgb(142, 68, 173), out _lblTpmLowSpare));

        var sub = new TabControl { Dock = DockStyle.Fill };
        sub.TabPages.Add(BuildTpmPlanTab());
        sub.TabPages.Add(BuildTpmRecordTab());
        sub.TabPages.Add(BuildSpareTab());

        page.Controls.Add(sub);
        page.Controls.Add(BuildTpmQueryBar());
        page.Controls.Add(cards);

        SetupTpmColumns();
        DoQueryTpm();

        page.Enter -= TpmTab_Enter;
        page.Enter += TpmTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>构建「TpmPlan」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildTpmPlanTab()
    {
        var page = new TabPage("点检/保养计划");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup("点检/保养计划（按剩余天数升序；红色=已超期、橙色=即将到期）", out _tpmPlanGrid));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnTpmExecute = MakeToolbarButton("执行点检", BtnTpmExecute);
        bar.Controls.Add(_btnTpmExecute);
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => DoQueryTpm()));
        bar.Controls.Add(new Label
        {
            Text = "执行点检后，下次到期时间按周期自动顺延；结果为「异常」需填写备注（建议同步登记异常）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });

        page.Controls.Add(bar);
        return page;
    }

    /// <summary>构建「TpmRecord」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildTpmRecordTab()
    {
        var page = new TabPage("执行记录");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup("点检/保养执行记录（时间倒序；异常为红色）", out _tpmRecordGrid));
        return page;
    }

    /// <summary>构建「Spare」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildSpareTab()
    {
        var page = new TabPage("备件台账");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup("备件台账（低于安全库存的置顶并标红）", out _spareGrid));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnSpareIssue = MakeToolbarButton("领用备件", BtnSpareIssue);
        bar.Controls.Add(_btnSpareIssue);
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => DoQueryTpm()));
        page.Controls.Add(bar);
        return page;
    }

    /// <summary>构建「Tpm」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildTpmQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "到期状态：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboTpmDueStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(4, 4, 0, 0) };
        _cboTpmDueStatus.Items.AddRange(new object[] { "全部", TpmDueStatus.Overdue, TpmDueStatus.DueSoon, TpmDueStatus.Normal });
        _cboTpmDueStatus.SelectedIndex = 0;
        bar.Controls.Add(_cboTpmDueStatus);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtTpmKeyword = new TextBox { Width = 180, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtTpmKeyword);
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryTpm()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _cboTpmDueStatus.SelectedIndex = 0;
            _txtTpmKeyword.Text = "";
            DoQueryTpm();
        }));
        bar.Controls.Add(new Label
        {
            Text = "（设备 / 项目 / 责任人 / 备件编码名称）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(8, 8, 0, 0)
        });
        return bar;
    }

    /// <summary>设置「TpmColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupTpmColumns()
    {
        _tpmPlanGrid.AutoGenerateColumns = false;
        _tpmPlanGrid.Columns.Clear();
        _tpmPlanGrid.Columns.Add(MakeTextCol("PlanId", "计划编号", true, 10));
        _tpmPlanGrid.Columns.Add(MakeTextCol("DeviceName", "设备", true, 12));
        _tpmPlanGrid.Columns.Add(MakeTextCol("Section", "工段", true, 10));
        _tpmPlanGrid.Columns.Add(MakeTextCol("ItemName", "点检/保养项目", true, 16));
        _tpmPlanGrid.Columns.Add(MakeTextCol("CycleText", "周期", true, 12));
        _tpmPlanGrid.Columns.Add(MakeTextCol("Standard", "判定标准", true, 14));
        var lastCol = MakeTextCol("LastDoneTime", "最近执行", true, 12);
        lastCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _tpmPlanGrid.Columns.Add(lastCol);
        var dueCol = MakeTextCol("NextDueTime", "下次到期", true, 12);
        dueCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _tpmPlanGrid.Columns.Add(dueCol);
        _tpmPlanGrid.Columns.Add(MakeTextCol("DaysLeftText", "剩余", true, 10));
        _tpmPlanGrid.Columns.Add(MakeTextCol("DueStatus", "到期状态", true, 9));
        _tpmPlanGrid.Columns.Add(MakeTextCol("Owner", "责任人", true, 8));

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _tpmPlanGrid.SelectionChanged -= TpmPlanGrid_SelectionChanged;
        _tpmPlanGrid.SelectionChanged += TpmPlanGrid_SelectionChanged;
        // 先解除再挂接着色事件（同上，防止重复着色）
        _tpmPlanGrid.CellFormatting -= TpmPlanGrid_CellFormatting;
        _tpmPlanGrid.CellFormatting += TpmPlanGrid_CellFormatting;

        _tpmRecordGrid.AutoGenerateColumns = false;
        _tpmRecordGrid.Columns.Clear();
        _tpmRecordGrid.Columns.Add(MakeTextCol("RecordNo", "记录号", true, 14));
        _tpmRecordGrid.Columns.Add(MakeTextCol("DeviceName", "设备", true, 14));
        _tpmRecordGrid.Columns.Add(MakeTextCol("ItemName", "项目", true, 18));
        var tCol = MakeTextCol("ExecuteTime", "执行时间", true, 13);
        tCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _tpmRecordGrid.Columns.Add(tCol);
        _tpmRecordGrid.Columns.Add(MakeTextCol("Result", "结果", true, 8));
        _tpmRecordGrid.Columns.Add(MakeTextCol("Operator", "执行人", true, 9));
        _tpmRecordGrid.Columns.Add(MakeTextCol("Remark", "备注", true, 24));
        // 先解除再挂接着色事件（同上，防止重复着色）
        _tpmRecordGrid.CellFormatting -= TpmRecordGrid_CellFormatting;
        _tpmRecordGrid.CellFormatting += TpmRecordGrid_CellFormatting;

        _spareGrid.AutoGenerateColumns = false;
        _spareGrid.Columns.Clear();
        _spareGrid.Columns.Add(MakeTextCol("Code", "备件编码", true, 13));
        _spareGrid.Columns.Add(MakeTextCol("Name", "名称", true, 16));
        _spareGrid.Columns.Add(MakeTextCol("Spec", "规格", true, 16));
        _spareGrid.Columns.Add(MakeTextCol("StockText", "库存", true, 11));
        _spareGrid.Columns.Add(MakeTextCol("SafetyStock", "安全库存", true, 10));
        _spareGrid.Columns.Add(MakeTextCol("Location", "库位", true, 12));
        _spareGrid.Columns.Add(MakeTextCol("Supplier", "供应商", true, 12));
        _spareGrid.Columns.Add(MakeTextCol("StockStatus", "库存状态", true, 13));
        var issueCol = MakeTextCol("LastIssueTime", "最近领用", true, 12);
        issueCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _spareGrid.Columns.Add(issueCol);
        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _spareGrid.SelectionChanged -= SpareGrid_SelectionChanged;
        _spareGrid.SelectionChanged += SpareGrid_SelectionChanged;
        // 先解除再挂接着色事件（同上，防止重复着色）
        _spareGrid.CellFormatting -= SpareGrid_CellFormatting;
        _spareGrid.CellFormatting += SpareGrid_CellFormatting;
    }

    /// <summary>计划到期状态着色：超期红 / 即将到期橙 / 正常绿。</summary>
    private void TpmPlanGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_tpmPlanGrid.Rows[e.RowIndex].DataBoundItem is not TpmPlan p) return;
        var col = _tpmPlanGrid.Columns[e.ColumnIndex].Name;
        if (col != "DueStatus" && col != "DaysLeftText") return;

        e.CellStyle.ForeColor = p.DueStatus switch
        {
            TpmDueStatus.Overdue => Color.FromArgb(192, 0, 0),
            TpmDueStatus.DueSoon => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(39, 174, 96)
        };
    }

    /// <summary>「TpmRecordGrid」网格单元格着色：按状态/结果区分颜色（差异红、异常橙、正常绿等）。</summary>
    private void TpmRecordGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_tpmRecordGrid.Rows[e.RowIndex].DataBoundItem is not TpmRecord r) return;
        if (_tpmRecordGrid.Columns[e.ColumnIndex].Name != "Result") return;
        e.CellStyle.ForeColor = r.IsAbnormal ? Color.FromArgb(192, 0, 0) : Color.FromArgb(39, 174, 96);
    }

    /// <summary>备件状态着色：低库存红 / 充足绿。</summary>
    private void SpareGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_spareGrid.Rows[e.RowIndex].DataBoundItem is not SparePart p) return;
        var col = _spareGrid.Columns[e.ColumnIndex].Name;
        if (col != "StockStatus" && col != "StockText") return;
        e.CellStyle.ForeColor = p.IsLow ? Color.FromArgb(192, 0, 0) : Color.FromArgb(39, 174, 96);
    }

    /// <summary>进入「TpmTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void TpmTab_Enter(object? sender, EventArgs e) => EnsureTpmReady();

    /// <summary>「TpmPlanGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void TpmPlanGrid_SelectionChanged(object? sender, EventArgs e) => UpdateTpmButtons();

    /// <summary>「SpareGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void SpareGrid_SelectionChanged(object? sender, EventArgs e) => UpdateTpmButtons();

    /// <summary>根据当前选中项更新「TpmButtons」的可用状态/提示。</summary>
    private void UpdateTpmButtons()
    {
        if (_btnTpmExecute is not null) _btnTpmExecute.Enabled = SelectedTpmPlan() is not null;
        if (_btnSpareIssue is not null) _btnSpareIssue.Enabled = SelectedSpare() is not null;
    }

    /// <summary>在网格中选中「edTpmPlan」对应的行（联动加载明细）。</summary>
    private TpmPlan? SelectedTpmPlan()
    {
        if (_tpmPlanGrid is null || _tpmPlanGrid.SelectedRows.Count == 0) return null;
        return _tpmPlanGrid.SelectedRows[0].DataBoundItem as TpmPlan;
    }

    /// <summary>在网格中选中「edSpare」对应的行（联动加载明细）。</summary>
    private SparePart? SelectedSpare()
    {
        if (_spareGrid is null || _spareGrid.SelectedRows.Count == 0) return null;
        return _spareGrid.SelectedRows[0].DataBoundItem as SparePart;
    }

    /// <summary>兜底初始化「Tpm」：页签 Enter/全局刷新时确保数据已加载（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureTpmReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_tpmPlanGrid is null) return;
        if (_tpmPlanGrid.Rows.Count == 0) DoQueryTpm();
    }

    /// <summary>加载点检保养页：计划 / 记录 / 备件 + 汇总卡。</summary>
    private void DoQueryTpm()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_tpmPlanGrid is null) return;

        var kw = _txtTpmKeyword?.Text.Trim() ?? "";
        var due = _cboTpmDueStatus is not null && _cboTpmDueStatus.SelectedIndex > 0
            ? _cboTpmDueStatus.SelectedItem?.ToString()
            : null;

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _tpmPlanGrid.DataSource = null;
        _tpmPlanGrid.DataSource = MesDatabase.QueryTpmPlans(kw.Length == 0 ? null : kw, due);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _tpmRecordGrid.DataSource = null;
        _tpmRecordGrid.DataSource = MesDatabase.QueryTpmRecords(kw.Length == 0 ? null : kw);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _spareGrid.DataSource = null;
        _spareGrid.DataSource = MesDatabase.QuerySpareParts(kw.Length == 0 ? null : kw);

        var (total, overdue, dueSoon) = MesDatabase.GetTpmSummary();
        var (_, lowCount) = MesDatabase.GetSpareSummary();
        if (_lblTpmTotal is not null) _lblTpmTotal.Text = total.ToString();
        if (_lblTpmOverdue is not null)
        {
            _lblTpmOverdue.Text = overdue.ToString();
            _lblTpmOverdue.ForeColor = overdue > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }
        if (_lblTpmDueSoon is not null) _lblTpmDueSoon.Text = dueSoon.ToString();
        if (_lblTpmLowSpare is not null)
        {
            _lblTpmLowSpare.Text = lowCount.ToString();
            _lblTpmLowSpare.ForeColor = lowCount > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }

        // 选中首个计划（供"执行点检"使用）
        if (_tpmPlanGrid.Rows.Count > 0 && _tpmPlanGrid.SelectedRows.Count == 0)
        {
            _tpmPlanGrid.ClearSelection();
            _tpmPlanGrid.Rows[0].Selected = true;
        }
        if (_spareGrid.Rows.Count > 0 && _spareGrid.SelectedRows.Count == 0)
        {
            _spareGrid.ClearSelection();
            _spareGrid.Rows[0].Selected = true;
        }
        UpdateTpmButtons();
    }

    /// <summary>执行点检/保养（记录 + 顺延下次到期）。</summary>
    private void BtnTpmExecute(object? sender, EventArgs e)
    {
        var plan = SelectedTpmPlan();
        if (plan is null) { Warn("请先选择点检/保养计划。"); return; }

        using var form = new TpmExecuteForm(plan);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.ExecuteTpm(plan.PlanId, form.Result, form.Remark, form.Operator);
        MessageBox.Show(msg, ok ? "点检已完成" : "点检提交失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryTpm();
    }

    /// <summary>领用备件（扣减库存）。</summary>
    private void BtnSpareIssue(object? sender, EventArgs e)
    {
        var part = SelectedSpare();
        if (part is null) { Warn("请先选择备件。"); return; }
        if (part.Stock <= 1e-9) { Warn($"备件 {part.Name} 库存为 0，无法领用。"); return; }

        using var form = new SpareIssueForm(part);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.IssueSparePart(part.Code, form.Quantity, form.Operator);
        MessageBox.Show(msg, ok ? "领用成功" : "领用失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryTpm();
    }


}
