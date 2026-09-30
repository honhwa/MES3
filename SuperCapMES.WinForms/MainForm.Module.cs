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
/// MainForm 分部类（按功能域拆分，第 10 部分）：模组装配页（模组 ↔ 电芯装配关系 + 模组级追溯与报告导出）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Module」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 模组装配与模组级追溯（顶层第 20 个页签） =================

    /// <summary>模组装配页：模组 ↔ 电芯装配关系维护 + 模组级追溯（导出报告）。</summary>
    private TabPage BuildModuleTab()
    {
        var page = new TabPage("模组装配");

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("模组总数 (个)", Color.FromArgb(15, 76, 129), out _lblModuleTotal));
        cards.Controls.Add(MakeCard("已装配 (个)", Color.FromArgb(39, 174, 96), out _lblModuleAssembled));
        cards.Controls.Add(MakeCard("已出货 (个)", Color.FromArgb(142, 68, 173), out _lblModuleShipped));
        cards.Controls.Add(MakeCard("已装电芯 (只)", Color.FromArgb(230, 126, 34), out _lblModuleCells));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 230,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("模组列表（选中一行 → 下方显示模组内电芯与追溯信息）", out _moduleGrid));
        split.Panel2.Controls.Add(MakeGridGroup("模组内电芯（模组级追溯：电芯 → 材料批次 → 测试结论）", out _moduleCellGrid));

        page.Controls.Add(split);
        page.Controls.Add(BuildModuleActionBar());
        page.Controls.Add(BuildModuleQueryBar());
        page.Controls.Add(cards);

        SetupModuleColumns();
        DoQueryModules();

        page.Enter -= ModuleTab_Enter;
        page.Enter += ModuleTab_Enter;
        return page;
    }

    /// <summary>构建「Module」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildModuleQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboModuleStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(4, 4, 0, 0) };
        _cboModuleStatus.Items.Add("全部");
        _cboModuleStatus.Items.AddRange(ModuleStatus.All.Cast<object>().ToArray());
        _cboModuleStatus.SelectedIndex = 0;
        bar.Controls.Add(_cboModuleStatus);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtModuleKeyword = new TextBox { Width = 200, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtModuleKeyword);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryModules()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _cboModuleStatus.SelectedIndex = 0;
            _txtModuleKeyword.Text = "";
            DoQueryModules();
        }));
        return bar;
    }

    /// <summary>构建「Module」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildModuleActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        bar.Controls.Add(MakeToolbarButton("新建模组装配", BtnModuleAssemble));
        _btnModuleDismantle = MakeToolbarButton("拆解模组", BtnModuleDismantle);
        bar.Controls.Add(_btnModuleDismantle);
        bar.Controls.Add(MakeToolbarButton("导出追溯报告", BtnModuleExport));
        bar.Controls.Add(new Label
        {
            Text = "模组级追溯：模组 SN → 电芯 → 材料批次 / 测试结论（可导出报告）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>设置「ModuleColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupModuleColumns()
    {
        _moduleGrid.AutoGenerateColumns = false;
        _moduleGrid.Columns.Clear();
        _moduleGrid.Columns.Add(MakeTextCol("ModuleSn", "模组SN", true, 18));
        _moduleGrid.Columns.Add(MakeTextCol("ModuleModel", "模组型号", true, 18));
        _moduleGrid.Columns.Add(MakeTextCol("CellModel", "电芯型号", true, 14));
        _moduleGrid.Columns.Add(MakeTextCol("CellCount", "电芯数", true, 8));
        _moduleGrid.Columns.Add(MakeTextCol("Line", "产线", true, 6));
        _moduleGrid.Columns.Add(MakeTextCol("WoNo", "关联工单", true, 14));
        _moduleGrid.Columns.Add(MakeTextCol("StatusText", "状态", true, 9));
        _moduleGrid.Columns.Add(MakeTextCol("Operator", "装配人", true, 8));
        var tCol = MakeTextCol("AssembledTime", "装配时间", true, 13);
        tCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _moduleGrid.Columns.Add(tCol);

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _moduleGrid.SelectionChanged -= ModuleGrid_SelectionChanged;
        _moduleGrid.SelectionChanged += ModuleGrid_SelectionChanged;
        // 先解除再挂接着色事件（同上，防止重复着色）
        _moduleGrid.CellFormatting -= ModuleGrid_CellFormatting;
        _moduleGrid.CellFormatting += ModuleGrid_CellFormatting;

        _moduleCellGrid.AutoGenerateColumns = false;
        _moduleCellGrid.Columns.Clear();
        _moduleCellGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 18));
        _moduleCellGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 13));
        _moduleCellGrid.Columns.Add(MakeTextCol("ElectrodeLot", "极片批号", true, 14));
        _moduleCellGrid.Columns.Add(MakeTextCol("ElectrolyteLot", "电解液批号", true, 14));
        _moduleCellGrid.Columns.Add(MakeTextCol("Result", "分档", true, 7));
        _moduleCellGrid.Columns.Add(MakeTextCol("CellStatus", "电芯状态", true, 9));
        var ttCol = MakeTextCol("TestTime", "最近测试", true, 12);
        ttCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _moduleCellGrid.Columns.Add(ttCol);
        _moduleCellGrid.Columns.Add(MakeTextCol("TestText", "测试结论", true, 8));
        // 先解除再挂接着色事件（同上，防止重复着色）
        _moduleCellGrid.CellFormatting -= ModuleCellGrid_CellFormatting;
        _moduleCellGrid.CellFormatting += ModuleCellGrid_CellFormatting;
    }

    /// <summary>模组状态着色：已装配绿 / 已出货蓝 / 已拆解灰。</summary>
    private void ModuleGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_moduleGrid.Rows[e.RowIndex].DataBoundItem is not ModuleRecord m) return;
        if (_moduleGrid.Columns[e.ColumnIndex].Name != "StatusText") return;

        e.CellStyle.ForeColor = m.Status switch
        {
            ModuleStatus.Assembled => Color.FromArgb(39, 174, 96),
            ModuleStatus.Shipped => Color.FromArgb(15, 76, 129),
            _ => Color.FromArgb(127, 140, 141)
        };
    }

    /// <summary>模组内电芯测试结论着色。</summary>
    private void ModuleCellGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_moduleCellGrid.Rows[e.RowIndex].DataBoundItem is not ModuleCellRow c) return;
        if (_moduleCellGrid.Columns[e.ColumnIndex].Name != "TestText") return;

        e.CellStyle.ForeColor = c.Passed switch
        {
            true => Color.FromArgb(39, 174, 96),
            false => Color.FromArgb(192, 0, 0),
            _ => Color.FromArgb(127, 140, 141)
        };
    }

    /// <summary>进入「ModuleTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void ModuleTab_Enter(object? sender, EventArgs e) => EnsureModuleReady();

    /// <summary>「ModuleGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void ModuleGrid_SelectionChanged(object? sender, EventArgs e)
    {
        LoadModuleCells();
        UpdateModuleButtons();
    }

    /// <summary>在网格中选中「edModule」对应的行（联动加载明细）。</summary>
    private ModuleRecord? SelectedModule()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_moduleGrid.SelectedRows.Count == 0) return null;
        return _moduleGrid.SelectedRows[0].DataBoundItem as ModuleRecord;
    }

    /// <summary>根据当前选中项更新「ModuleButtons」的可用状态/提示。</summary>
    private void UpdateModuleButtons()
    {
        var m = SelectedModule();
        if (_btnModuleDismantle is not null)
            _btnModuleDismantle.Enabled = m is not null && ModuleStatus.IsActive(m.Status);
    }

    /// <summary>查询并加载「Modules」数据到界面（含网格绑定与汇总刷新）。</summary>
    private void DoQueryModules()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_moduleGrid is null) return;
        var status = _cboModuleStatus is not null && _cboModuleStatus.SelectedIndex > 0
            ? _cboModuleStatus.SelectedItem?.ToString() : null;
        var kw = _txtModuleKeyword?.Text.Trim() ?? "";
        var list = MesDatabase.QueryModules(kw.Length == 0 ? null : kw, status);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _moduleGrid.DataSource = null;
        _moduleGrid.DataSource = list;

        var (total, assembled, shipped, cells) = MesDatabase.GetModuleSummary();
        if (_lblModuleTotal is not null) _lblModuleTotal.Text = total.ToString();
        if (_lblModuleAssembled is not null) _lblModuleAssembled.Text = assembled.ToString();
        if (_lblModuleShipped is not null) _lblModuleShipped.Text = shipped.ToString();
        if (_lblModuleCells is not null) _lblModuleCells.Text = cells.ToString();

        TrySelectFirstModule();
        LoadModuleCells();
        UpdateModuleButtons();
    }

    /// <summary>尝试选中列表首行（以控件实测行数为守卫；构造期绑定未物化时安全跳过）。</summary>
    private void TrySelectFirstModule()
    {
        if (_moduleGrid is null || _moduleGrid.Rows.Count == 0) return;
        _moduleGrid.ClearSelection();
        _moduleGrid.Rows[0].Selected = true;
    }

    /// <summary>加载「ModuleCells」并绑定到界面。</summary>
    private void LoadModuleCells()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_moduleCellGrid is null) return;
        var m = SelectedModule();
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _moduleCellGrid.DataSource = null;
        _moduleCellGrid.DataSource = m is null ? null : MesDatabase.GetModuleCells(m.ModuleSn);
    }

    /// <summary>兜底初始化「Module」：页签 Enter/全局刷新时确保数据已加载（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureModuleReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_moduleGrid is null) return;
        if (_moduleGrid.Rows.Count == 0) DoQueryModules();
        if (_moduleGrid.SelectedRows.Count == 0) TrySelectFirstModule();
        LoadModuleCells();
        UpdateModuleButtons();
    }

    /// <summary>在网格中选中「ModuleRow」对应的行（联动加载明细）。</summary>
    private void SelectModuleRow(string moduleSn)
    {
        foreach (DataGridViewRow row in _moduleGrid.Rows)
        {
            if (row.DataBoundItem is ModuleRecord m && string.Equals(m.ModuleSn, moduleSn, StringComparison.OrdinalIgnoreCase))
            {
                _moduleGrid.ClearSelection();
                row.Selected = true;
                return;
            }
        }
    }

    // ---------- 模组操作 ----------

    /// <summary>处理「ModuleAssemble」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnModuleAssemble(object? sender, EventArgs e)
    {
        if (MesDatabase.GetModuleCandidates().Count == 0)
        {
            Warn("当前没有可装配的电芯（需为「合格 / 已入库」且尚未装入任何模组）。");
            return;
        }

        using var form = new ModuleAssembleForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.AssembleModule(
            form.ModuleSn, form.ModuleModel, form.Line, form.WoNo, form.CellSns, "admin");
        MessageBox.Show(msg, ok ? "装配成功" : "装配失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        DoQueryModules();
        if (ok) SelectModuleRow(form.ModuleSn);
    }

    /// <summary>处理「ModuleDismantle」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnModuleDismantle(object? sender, EventArgs e)
    {
        var m = SelectedModule();
        if (m is null) { Warn("请先选择模组。"); return; }
        if (!ModuleStatus.IsActive(m.Status)) { Warn($"模组 {m.ModuleSn} 状态为「{m.Status}」，不能拆解。"); return; }

        if (MessageBox.Show($"确认拆解模组 {m.ModuleSn}？（将解绑其 {m.CellCount} 只电芯）",
                "拆解模组", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var (ok, msg) = MesDatabase.DismantleModule(m.ModuleSn, "admin");
        MessageBox.Show(msg, ok ? "操作成功" : "操作失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryModules();
    }

    /// <summary>导出模组追溯报告（模组信息 + 电芯明细 + 汇总）。</summary>
    private void BtnModuleExport(object? sender, EventArgs e)
    {
        var m = SelectedModule();
        if (m is null) { Warn("请先选择要导出追溯报告的模组。"); return; }

        var dlg = new SaveFileDialog
        {
            Title = "导出模组追溯报告",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"模组追溯报告_{SanitizeFileName(m.ModuleSn)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var w = new XlsxWriter();
            BuildModuleTraceWorkbook(w, m.ModuleSn);
            var (sheets, rows) = w.Save(dlg.FileName);
            MessageBox.Show($"报告已导出：\n{dlg.FileName}\n\n工作表 {sheets} 个 ｜ 数据行 {rows} 行",
                "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"导出失败：{ex.Message}");
        }
    }

    /// <summary>构造模组追溯报告工作簿（3 个工作表）。纯数据构造，便于自测。</summary>
    internal static void BuildModuleTraceWorkbook(XlsxWriter w, string moduleSn)
    {
        var m = MesDatabase.FindModule(moduleSn);
        var cells = MesDatabase.GetModuleCells(moduleSn);

        var s0 = w.AddSheet("模组信息");
        s0.SetColumns(("项目", 20), ("内容", 34));
        s0.AddRow("报告名称", "模组级追溯报告");
        s0.AddRow("模组 SN", m?.ModuleSn ?? moduleSn);
        s0.AddRow("模组型号", m?.ModuleModel ?? "");
        s0.AddRow("电芯型号", m?.CellModel ?? "");
        s0.AddRow("装配电芯数", m?.CellCount ?? 0);
        s0.AddRow("生产线体", m?.Line ?? "");
        s0.AddRow("关联工单", m?.WoNo ?? "");
        s0.AddRow("模组状态", m?.Status ?? "");
        s0.AddRow("装配人", m?.Operator ?? "");
        s0.AddRow("装配时间", m is null ? "" : m.AssembledTime.ToString("yyyy-MM-dd HH:mm"));
        s0.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        s0.AddRow("备注", m?.Remark ?? "");

        var s1 = w.AddSheet("模组内电芯");
        s1.SetColumns(("电芯SN", 22), ("型号", 16), ("极片批号", 16), ("电解液批号", 16),
            ("分档", 8), ("电芯状态", 10), ("最近测试", 18), ("测试结论", 10));
        foreach (var c in cells)
            s1.AddRow(c.CellSn, c.ProductModel, c.ElectrodeLot, c.ElectrolyteLot, c.Result, c.CellStatus,
                c.TestTime?.ToString("yyyy-MM-dd HH:mm") ?? "—", c.TestText);

        var s2 = w.AddSheet("汇总");
        s2.SetColumns(("指标", 20), ("数值", 16));
        s2.AddRow("电芯总数（只）", cells.Count);
        s2.AddRow("测试合格（只）", cells.Count(c => c.Passed == true));
        s2.AddRow("测试不合格（只）", cells.Count(c => c.Passed == false));
        s2.AddRow("未测试（只）", cells.Count(c => c.Passed is null));
        s2.AddRow("涉及极片批次（个）", cells.Select(c => c.ElectrodeLot).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        s2.AddRow("涉及电解液批次（个）", cells.Select(c => c.ElectrolyteLot).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }


}
