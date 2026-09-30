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
/// MainForm 分部类（按功能域拆分，第 6 部分）：首件检验页（FAI：换型/开班首件必检，合格才放行批量生产）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Fai」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 首件检验 FAI（顶层第 18 个页签） =================

    /// <summary>首件检验页：换型/开班首件必检，合格后放行批量生产（工序实绩保存时会据此提示）。</summary>
    private TabPage BuildFaiTab()
    {
        var page = new TabPage("首件检验");

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("待检验 (单)", Color.FromArgb(230, 126, 34), out _lblFaiPending));
        cards.Controls.Add(MakeCard("已放行 (单)", Color.FromArgb(39, 174, 96), out _lblFaiPassed));
        cards.Controls.Add(MakeCard("不合格 (单)", Color.FromArgb(192, 0, 0), out _lblFaiFailed));
        cards.Controls.Add(MakeCard("未放行工单 (个)", Color.FromArgb(142, 68, 173), out _lblFaiNotReleased));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 260,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("首件检验单（选中一行 → 下方填写检验实绩并提交判定）", out _faiGrid));

        var itemGroup = new GroupBox
        {
            Text = "检验项目（在「实测值」列填入实测结果后点「提交判定」；全部合格即放行批量）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        _faiItemGrid = MakeEditableGrid();
        SetupFaiItemColumns();
        itemGroup.Controls.Add(_faiItemGrid);
        split.Panel2.Controls.Add(itemGroup);

        page.Controls.Add(split);
        page.Controls.Add(BuildFaiActionBar());
        page.Controls.Add(BuildFaiQueryBar());
        page.Controls.Add(cards);

        SetupFaiColumns();
        DoQueryFai();

        page.Enter -= FaiTab_Enter;
        page.Enter += FaiTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>构建「Fai」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildFaiQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboFaiStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(4, 4, 0, 0) };
        _cboFaiStatus.Items.Add("全部");
        _cboFaiStatus.Items.AddRange(FaiStatus.All.Cast<object>().ToArray());
        _cboFaiStatus.SelectedIndex = 0;
        bar.Controls.Add(_cboFaiStatus);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtFaiKeyword = new TextBox { Width = 190, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtFaiKeyword);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryFai()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _cboFaiStatus.SelectedIndex = 0;
            _txtFaiKeyword.Text = "";
            DoQueryFai();
        }));

        _lblFaiHint = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 10, 0, 0) };
        bar.Controls.Add(_lblFaiHint);
        return bar;
    }

    /// <summary>构建「Fai」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildFaiActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        bar.Controls.Add(MakeToolbarButton("新建首件检验", BtnFaiCreate));
        _btnFaiSubmit = MakeToolbarButton("提交判定", BtnFaiSubmit);
        bar.Controls.Add(_btnFaiSubmit);
        bar.Controls.Add(MakeToolbarButton("打印首检单", BtnPrintFaiSlip));
        bar.Controls.Add(new Label
        {
            Text = "门禁：工单未通过首件检验放行时，工序实绩保存会提示（默认不继续）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>设置「FaiColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupFaiColumns()
    {
        _faiGrid.AutoGenerateColumns = false;
        _faiGrid.Columns.Clear();
        _faiGrid.Columns.Add(MakeTextCol("FaiNo", "首检单号", true, 14));
        _faiGrid.Columns.Add(MakeTextCol("WoNo", "工单号", true, 14));
        _faiGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 12));
        _faiGrid.Columns.Add(MakeTextCol("Line", "产线", true, 6));
        _faiGrid.Columns.Add(MakeTextCol("FirstCellSn", "首件SN", true, 16));
        _faiGrid.Columns.Add(MakeTextCol("ProcessName", "首检工序", true, 9));
        _faiGrid.Columns.Add(MakeTextCol("Reason", "原因", true, 8));
        _faiGrid.Columns.Add(MakeTextCol("StatusText", "状态", true, 12));
        _faiGrid.Columns.Add(MakeTextCol("Inspector", "检验人", true, 8));
        var tCol = MakeTextCol("InspectTime", "检验时间", true, 12);
        tCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _faiGrid.Columns.Add(tCol);

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _faiGrid.SelectionChanged -= FaiGrid_SelectionChanged;
        _faiGrid.SelectionChanged += FaiGrid_SelectionChanged;
        // 先解除再挂接着色事件（同上，防止重复着色）
        _faiGrid.CellFormatting -= FaiGrid_CellFormatting;
        _faiGrid.CellFormatting += FaiGrid_CellFormatting;
    }

    /// <summary>设置「FaiItemColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupFaiItemColumns()
    {
        _faiItemGrid.Columns.Clear();
        _faiItemGrid.Columns.Add(MakeTextCol("ItemName", "检验项目", true, 22));
        _faiItemGrid.Columns.Add(MakeTextCol("Standard", "标准值", true, 18));
        _faiItemGrid.Columns.Add(MakeTextCol("Measured", "实测值", false, 16));
        _faiItemGrid.Columns.Add(MakeTextCol("ResultText", "判定", true, 12));
    }

    /// <summary>首检单状态着色：待检验橙 / 已放行绿 / 不合格红。</summary>
    private void FaiGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_faiGrid.Rows[e.RowIndex].DataBoundItem is not FaiOrder f) return;

        var col = _faiGrid.Columns[e.ColumnIndex].Name;
        if (col != "StatusText") return;

        e.CellStyle.ForeColor = f.Status switch
        {
            FaiStatus.Passed => Color.FromArgb(39, 174, 96),
            FaiStatus.Pending => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(192, 0, 0)
        };
    }

    /// <summary>进入「FaiTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void FaiTab_Enter(object? sender, EventArgs e) => EnsureFaiReady();

    /// <summary>「FaiGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void FaiGrid_SelectionChanged(object? sender, EventArgs e)
    {
        LoadFaiItems();
        UpdateFaiButtons();
    }

    /// <summary>在网格中选中「edFai」对应的行（联动加载明细）。</summary>
    private FaiOrder? SelectedFai()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_faiGrid.SelectedRows.Count == 0) return null;
        return _faiGrid.SelectedRows[0].DataBoundItem as FaiOrder;
    }

    /// <summary>根据当前选中项更新「FaiButtons」的可用状态/提示。</summary>
    private void UpdateFaiButtons()
    {
        var f = SelectedFai();
        if (_btnFaiSubmit is not null) _btnFaiSubmit.Enabled = f?.CanSubmit == true;
    }

    /// <summary>加载首件检验单列表 + 汇总卡（并联动明细与按钮）。</summary>
    private void DoQueryFai()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_faiGrid is null) return;

        var status = _cboFaiStatus is not null && _cboFaiStatus.SelectedIndex > 0
            ? _cboFaiStatus.SelectedItem?.ToString()
            : null;
        var kw = _txtFaiKeyword?.Text.Trim() ?? "";
        var list = MesDatabase.QueryFaiOrders(status, kw.Length == 0 ? null : kw);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _faiGrid.DataSource = null;
        _faiGrid.DataSource = list;

        var (pending, passed, failed, notReleased) = MesDatabase.GetFaiSummary();
        if (_lblFaiPending is not null) _lblFaiPending.Text = pending.ToString();
        if (_lblFaiPassed is not null) _lblFaiPassed.Text = passed.ToString();
        if (_lblFaiFailed is not null) _lblFaiFailed.Text = failed.ToString();
        if (_lblFaiNotReleased is not null)
        {
            _lblFaiNotReleased.Text = notReleased.ToString();
            _lblFaiNotReleased.ForeColor = notReleased > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }
        if (_lblFaiHint is not null)
            _lblFaiHint.Text = $"共 {list.Count} 单 ｜ 未放行工单 {notReleased} 个（工序实绩保存时会提示）";

        TrySelectFirstFai();
        LoadFaiItems();
        UpdateFaiButtons();
    }

    /// <summary>尝试选中列表首行（以控件实测行数为守卫；构造期绑定未物化时安全跳过）。</summary>
    private void TrySelectFirstFai()
    {
        if (_faiGrid is null || _faiGrid.Rows.Count == 0) return;   // 构造期绑定未物化时跳过
        _faiGrid.ClearSelection();
        _faiGrid.Rows[0].Selected = true;
    }

    /// <summary>加载「FaiItems」并绑定到界面。</summary>
    private void LoadFaiItems()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_faiItemGrid is null) return;
        var f = SelectedFai();
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _faiItemGrid.DataSource = null;
        _faiItemGrid.DataSource = f is null ? null : MesDatabase.GetFaiItems(f.FaiNo);
    }

    /// <summary>兜底初始化「Fai」：页签 Enter/全局刷新时确保数据已加载（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureFaiReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_faiGrid is null) return;
        if (_faiGrid.Rows.Count == 0) DoQueryFai();
        if (_faiGrid.SelectedRows.Count == 0) TrySelectFirstFai();
        LoadFaiItems();
        UpdateFaiButtons();
    }

    /// <summary>在网格中选中「FaiRow」对应的行（联动加载明细）。</summary>
    private void SelectFaiRow(string faiNo)
    {
        foreach (DataGridViewRow row in _faiGrid.Rows)
        {
            if (row.DataBoundItem is FaiOrder f && string.Equals(f.FaiNo, faiNo, StringComparison.OrdinalIgnoreCase))
            {
                _faiGrid.ClearSelection();
                row.Selected = true;
                return;
            }
        }
    }

    // ---------- 首件检验操作 ----------

    /// <summary>新建首件检验：选择工单 + 首检工序，自动带出检验项目。</summary>
    private void BtnFaiCreate(object? sender, EventArgs e)
    {
        var processes = MesDatabase.GetFaiProcessOptions();
        if (processes.Count == 0) { Warn("没有可用的检验项目模板。"); return; }

        using var form = new FaiCreateForm(MesDatabase.WorkOrders.ToList(), processes);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg, faiNo) = MesDatabase.CreateFai(
            form.WoNo, form.FirstCellSn, form.ProcessName, form.Reason, form.Operator);
        MessageBox.Show(msg, ok ? "创建成功" : "创建失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        DoQueryFai();
        if (ok) SelectFaiRow(faiNo);
    }

    /// <summary>提交首件检验判定：全部合格 → 放行批量；否则状态不合格。</summary>
    private void BtnFaiSubmit(object? sender, EventArgs e)
    {
        var f = SelectedFai();
        if (f is null) { Warn("请先选择首件检验单。"); return; }
        if (!f.CanSubmit) { Warn($"首件检验单 {f.FaiNo} 已放行，无需重复提交。"); return; }

        // 提交编辑中的单元格：确保最新输入落入数据对象后再读取
        if (_faiItemGrid.IsCurrentCellInEditMode) _faiItemGrid.EndEdit();
        _faiItemGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        var measured = new List<(string ItemName, string Measured)>();
        foreach (DataGridViewRow row in _faiItemGrid.Rows)
        {
            if (row.DataBoundItem is not FaiItem item) continue;
            var value = row.Cells["Measured"].Value?.ToString() ?? item.Measured;
            measured.Add((item.ItemName, value.Trim()));
        }

        if (measured.All(m => m.Measured.Length == 0))
        {
            Warn("请先填写至少一项实测值再提交判定。");
            return;
        }

        var (ok, msg) = MesDatabase.SubmitFai(f.FaiNo, measured, "admin");
        MessageBox.Show(msg, ok ? "首件检验结论" : "提交失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        DoQueryFai();
        SelectFaiRow(f.FaiNo);
    }

    /// <summary>打印首件检验单（复用通用打印器）。</summary>
    private void BtnPrintFaiSlip(object? sender, EventArgs e) =>
        PrintSlip(BuildFaiSlip(SelectedFai()?.FaiNo ?? ""), "请先选择要打印的首件检验单。");

    /// <summary>首件检验单打印模型（含检验项目明细）。纯数据构造。</summary>
    internal static PrintModel? BuildFaiSlip(string faiNo)
    {
        var f = MesDatabase.FindFai(faiNo);
        if (f is null) return null;

        var model = new PrintModel
        {
            Title = "首件检验单（FAI）",
            SubTitle = f.FaiNo,
            BarcodeText = f.FaiNo,
            Footer = $"打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        model.Header.Add(("工单号", f.WoNo));
        model.Header.Add(("产品型号", f.ProductModel));
        model.Header.Add(("生产线体", f.Line));
        model.Header.Add(("首件电芯SN", f.FirstCellSn));
        model.Header.Add(("首检工序", f.ProcessName));
        model.Header.Add(("触发原因", f.Reason));
        model.Header.Add(("检验结论", f.StatusText));
        model.Header.Add(("检验人", string.IsNullOrWhiteSpace(f.Inspector) ? "—" : f.Inspector));
        model.Header.Add(("检验时间", f.InspectTime?.ToString("yyyy-MM-dd HH:mm") ?? "—"));
        model.Header.Add(("合格/不合格项", $"{f.PassCount} / {f.FailCount}"));

        model.DetailHeaders.AddRange(new[] { "检验项目", "标准值", "实测值", "判定" });
        foreach (var item in MesDatabase.GetFaiItems(f.FaiNo))
            model.DetailRows.Add(new List<string>
            {
                item.ItemName, item.Standard, item.Measured, item.ResultText
            });
        return model;
    }


}
