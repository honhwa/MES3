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
/// MainForm 分部类（按功能域拆分，第 13 部分）：返工管理页（发现→保留→返工→重检→放行 的返工单操作台）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Rework」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 返工管理（顶层第 16 个页签） =================

    /// <summary>返工管理页：质量故事线「发现 → 保留 → 返工 → 重检 → 放行」的操作台。</summary>
    private TabPage BuildReworkTab()
    {
        var page = new TabPage("返工管理");

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("待返工 (单)", Color.FromArgb(230, 126, 34), out _lblReworkPending));
        cards.Controls.Add(MakeCard("返工中 (单)", Color.FromArgb(41, 128, 185), out _lblReworkInProgress));
        cards.Controls.Add(MakeCard("待重检 (单)", Color.FromArgb(142, 68, 173), out _lblReworkWaitInspect));
        cards.Controls.Add(MakeCard("已放行 (单)", Color.FromArgb(39, 174, 96), out _lblReworkReleased));
        cards.Controls.Add(MakeCard("已报废 (单)", Color.FromArgb(127, 140, 141), out _lblReworkScrapped));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("返工单（选中一行 → 下方显示流转履历；按钮按状态自动启用）", out _reworkGrid));
        split.Panel2.Controls.Add(MakeGridGroup("流转履历（发现 → 保留 → 返工 → 重检 → 放行）", out _reworkHistoryGrid));

        page.Controls.Add(split);
        page.Controls.Add(BuildReworkActionBar());
        page.Controls.Add(BuildReworkQueryBar());
        page.Controls.Add(cards);

        SetupReworkColumns();
        SetupReworkHistoryColumns();
        DoQueryRework();

        page.Enter -= ReworkTab_Enter;
        page.Enter += ReworkTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>构建「Rework」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildReworkActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnReworkCreate = MakeToolbarButton("新建返工单", BtnReworkCreate);
        _btnReworkStart = MakeToolbarButton("开始返工", BtnReworkStart);
        _btnReworkFinish = MakeToolbarButton("返工完成", BtnReworkFinish);
        _btnReworkInspect = MakeToolbarButton("重检登记", BtnReworkInspect);
        _btnReworkScrap = MakeToolbarButton("报废", BtnReworkScrap);
        bar.Controls.Add(MakeToolbarButton("打印返工单", BtnPrintReworkSlip));
        bar.Controls.Add(_btnReworkCreate);
        bar.Controls.Add(_btnReworkStart);
        bar.Controls.Add(_btnReworkFinish);
        bar.Controls.Add(_btnReworkInspect);
        bar.Controls.Add(_btnReworkScrap);
        bar.Controls.Add(new Label
        {
            Text = "重检合格 → 自动解除来源保留单并置电芯「合格」；不合格 → 退回返工并累计次数",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>构建「Rework」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildReworkQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboReworkStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(4, 4, 0, 0) };
        _cboReworkStatus.Items.Add("全部");
        _cboReworkStatus.Items.AddRange(ReworkStatus.All.Cast<object>().ToArray());
        _cboReworkStatus.SelectedIndex = 0;
        bar.Controls.Add(_cboReworkStatus);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtReworkKeyword = new TextBox { Width = 200, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtReworkKeyword);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryRework()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _cboReworkStatus.SelectedIndex = 0;
            _txtReworkKeyword.Text = "";
            DoQueryRework();
        }));
        bar.Controls.Add(new Label
        {
            Text = "（返工单号 / 电芯SN / 保留单号 / 原因）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(8, 8, 0, 0)
        });
        return bar;
    }

    /// <summary>设置「ReworkColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupReworkColumns()
    {
        _reworkGrid.AutoGenerateColumns = false;
        _reworkGrid.Columns.Clear();
        _reworkGrid.Columns.Add(MakeTextCol("ReworkNo", "返工单号", true, 13));
        _reworkGrid.Columns.Add(MakeTextCol("HoldNo", "保留单号", true, 13));
        _reworkGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 16));
        _reworkGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 11));
        _reworkGrid.Columns.Add(MakeTextCol("Reason", "返工原因", true, 20));
        _reworkGrid.Columns.Add(MakeTextCol("ResponsibleProcess", "责任工序", true, 8));
        _reworkGrid.Columns.Add(MakeTextCol("ReworkProcess", "返工工序", true, 8));
        _reworkGrid.Columns.Add(MakeTextCol("StatusText", "状态", true, 10));
        _reworkGrid.Columns.Add(MakeTextCol("InspectResult", "重检结果", true, 8));
        _reworkGrid.Columns.Add(MakeTextCol("CreatedBy", "创建人", true, 8));
        var tCol = MakeTextCol("CreatedTime", "创建时间", true, 13);
        tCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _reworkGrid.Columns.Add(tCol);

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _reworkGrid.SelectionChanged -= ReworkGrid_SelectionChanged;
        _reworkGrid.SelectionChanged += ReworkGrid_SelectionChanged;
        // 先解除再挂接着色事件（同上，防止重复着色）
        _reworkGrid.CellFormatting -= ReworkGrid_CellFormatting;
        _reworkGrid.CellFormatting += ReworkGrid_CellFormatting;
    }

    /// <summary>设置「ReworkHistoryColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupReworkHistoryColumns()
    {
        _reworkHistoryGrid.AutoGenerateColumns = false;
        _reworkHistoryGrid.Columns.Clear();
        var tCol = MakeTextCol("Time", "时间", true, 14);
        tCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _reworkHistoryGrid.Columns.Add(tCol);
        _reworkHistoryGrid.Columns.Add(MakeTextCol("Action", "动作", true, 12));
        _reworkHistoryGrid.Columns.Add(MakeTextCol("Operator", "操作人", true, 10));
        _reworkHistoryGrid.Columns.Add(MakeTextCol("Detail", "说明", true, 60));
    }

    /// <summary>进入「ReworkTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void ReworkTab_Enter(object? sender, EventArgs e) => EnsureReworkReady();

    /// <summary>「ReworkGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void ReworkGrid_SelectionChanged(object? sender, EventArgs e)
    {
        LoadReworkHistories();
        UpdateReworkButtons();
    }

    /// <summary>返工单状态着色：待返工橙 / 返工中蓝 / 待重检紫 / 已放行绿 / 已报废灰。</summary>
    private void ReworkGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_reworkGrid.Rows[e.RowIndex].DataBoundItem is not ReworkOrder r) return;

        var col = _reworkGrid.Columns[e.ColumnIndex].Name;
        if (col != "StatusText" && col != "InspectResult") return;

        e.CellStyle.ForeColor = r.Status switch
        {
            ReworkStatus.Pending => Color.FromArgb(230, 126, 34),
            ReworkStatus.InProgress => Color.FromArgb(41, 128, 185),
            ReworkStatus.WaitInspect => Color.FromArgb(142, 68, 173),
            ReworkStatus.Released => Color.FromArgb(39, 174, 96),
            _ => Color.FromArgb(127, 140, 141)
        };
    }

    /// <summary>在网格中选中「edRework」对应的行（联动加载明细）。</summary>
    private ReworkOrder? SelectedRework()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_reworkGrid.SelectedRows.Count == 0) return null;
        return _reworkGrid.SelectedRows[0].DataBoundItem as ReworkOrder;
    }

    /// <summary>按选中单的状态联动按钮可用性。</summary>
    private void UpdateReworkButtons()
    {
        var r = SelectedRework();
        if (_btnReworkStart is not null) _btnReworkStart.Enabled = r?.CanStart == true;
        if (_btnReworkFinish is not null) _btnReworkFinish.Enabled = r?.CanFinish == true;
        if (_btnReworkInspect is not null) _btnReworkInspect.Enabled = r?.CanInspect == true;
        if (_btnReworkScrap is not null) _btnReworkScrap.Enabled = r?.CanScrap == true;
    }

    /// <summary>加载返工单列表与汇总卡（并联动履历、按钮状态）。</summary>
    private void DoQueryRework()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_reworkGrid is null) return;

        var status = _cboReworkStatus is not null && _cboReworkStatus.SelectedIndex > 0
            ? _cboReworkStatus.SelectedItem?.ToString()
            : null;
        var kw = _txtReworkKeyword?.Text.Trim() ?? "";
        var list = MesDatabase.QueryReworkOrders(status, kw.Length == 0 ? null : kw);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _reworkGrid.DataSource = null;
        _reworkGrid.DataSource = list;

        var summary = MesDatabase.GetReworkSummary();
        if (_lblReworkPending is not null) _lblReworkPending.Text = summary.GetValueOrDefault(ReworkStatus.Pending).ToString();
        if (_lblReworkInProgress is not null) _lblReworkInProgress.Text = summary.GetValueOrDefault(ReworkStatus.InProgress).ToString();
        if (_lblReworkWaitInspect is not null) _lblReworkWaitInspect.Text = summary.GetValueOrDefault(ReworkStatus.WaitInspect).ToString();
        if (_lblReworkReleased is not null) _lblReworkReleased.Text = summary.GetValueOrDefault(ReworkStatus.Released).ToString();
        if (_lblReworkScrapped is not null) _lblReworkScrapped.Text = summary.GetValueOrDefault(ReworkStatus.Scrapped).ToString();

        TrySelectFirstRework();
        LoadReworkHistories();
        UpdateReworkButtons();
    }

    /// <summary>尝试选中列表首行（以控件实测行数为守卫；构造期绑定未物化时安全跳过）。</summary>
    private void TrySelectFirstRework()
    {
        if (_reworkGrid is null || _reworkGrid.Rows.Count == 0) return;   // 构造期绑定未物化时跳过
        _reworkGrid.ClearSelection();
        _reworkGrid.Rows[0].Selected = true;
    }

    /// <summary>加载「ReworkHistories」并绑定到界面。</summary>
    private void LoadReworkHistories()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_reworkHistoryGrid is null) return;
        var r = SelectedRework();
        _reworkHistoryGrid.DataSource = r is null ? null : MesDatabase.GetReworkHistories(r.ReworkNo);
    }

    /// <summary>兜底初始化「Rework」：页签 Enter/全局刷新时确保数据已加载（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureReworkReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_reworkGrid is null) return;
        if (_reworkGrid.Rows.Count == 0) DoQueryRework();
        if (_reworkGrid.SelectedRows.Count == 0) TrySelectFirstRework();
        LoadReworkHistories();
        UpdateReworkButtons();
    }

    /// <summary>在网格中选中「ReworkRow」对应的行（联动加载明细）。</summary>
    private void SelectReworkRow(string reworkNo)
    {
        foreach (DataGridViewRow row in _reworkGrid.Rows)
        {
            if (row.DataBoundItem is ReworkOrder r && string.Equals(r.ReworkNo, reworkNo, StringComparison.OrdinalIgnoreCase))
            {
                _reworkGrid.ClearSelection();
                row.Selected = true;
                return;
            }
        }
    }

    // ---------- 返工操作 ----------

    /// <summary>新建返工单：从「保留中」的保留单发起。</summary>
    private void BtnReworkCreate(object? sender, EventArgs e)
    {
        if (MesDatabase.GetReworkCandidates().Count == 0)
        {
            Warn("当前没有可返工的电芯。\n（需要先有「保留中」的保留单，且该电芯没有未完成的返工单）");
            return;
        }

        using var form = new ReworkCreateForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg, reworkNo) = MesDatabase.CreateReworkFromHold(
            form.HoldNo, form.CellSn, form.Reason, form.ResponsibleProcess, form.ReworkProcess, form.Operator);
        MessageBox.Show(msg, ok ? "创建成功" : "创建失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        DoQueryRework();
        if (ok) SelectReworkRow(reworkNo);
    }

    /// <summary>开始返工（写工序实绩，使返工进入追溯链）。</summary>
    private void BtnReworkStart(object? sender, EventArgs e)
    {
        var r = SelectedRework();
        if (r is null) { Warn("请先选择返工单。"); return; }
        if (!r.CanStart) { Warn($"返工单 {r.ReworkNo} 当前为「{r.Status}」，不能开始返工。"); return; }

        if (MessageBox.Show($"开始返工单 {r.ReworkNo}？\n电芯 {r.CellSn} ｜ 返工工序 {r.ReworkProcess}\n（将写入一条工序实绩，追溯链可见）",
                "开始返工", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var (ok, msg) = MesDatabase.StartRework(r.ReworkNo, "admin", $"标准返工参数（{r.ReworkProcess}）");
        MessageBox.Show(msg, ok ? "操作成功" : "操作失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryRework();
        SelectReworkRow(r.ReworkNo);
    }

    /// <summary>返工完成 → 待重检。</summary>
    private void BtnReworkFinish(object? sender, EventArgs e)
    {
        var r = SelectedRework();
        if (r is null) { Warn("请先选择返工单。"); return; }
        if (!r.CanFinish) { Warn($"返工单 {r.ReworkNo} 当前为「{r.Status}」，不能执行返工完成。"); return; }

        if (MessageBox.Show($"确认返工单 {r.ReworkNo}（电芯 {r.CellSn}）返工完成？\n完成后转入「待重检」。",
                "返工完成", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var (ok, msg) = MesDatabase.FinishRework(r.ReworkNo, "admin");
        MessageBox.Show(msg, ok ? "操作成功" : "操作失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryRework();
        SelectReworkRow(r.ReworkNo);
    }

    /// <summary>重检登记：合格 → 放行（解除保留 + 电芯置合格）；不合格 → 退回返工。</summary>
    private void BtnReworkInspect(object? sender, EventArgs e)
    {
        var r = SelectedRework();
        if (r is null) { Warn("请先选择返工单。"); return; }
        if (!r.CanInspect) { Warn($"返工单 {r.ReworkNo} 当前为「{r.Status}」，只有「待重检」可登记重检结果。"); return; }

        using var form = new ReworkInspectForm(r.ReworkNo, r.CellSn, "容量 ≥95F");
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.RegisterReworkInspection(
            r.ReworkNo, form.Passed, form.Standard, form.Measured, form.Operator, form.Remark);
        MessageBox.Show(msg, ok ? "重检登记成功" : "重检登记失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryRework();
        SelectReworkRow(r.ReworkNo);
    }

    /// <summary>报废（电芯置「不良」，保留单不解除）。</summary>
    private void BtnReworkScrap(object? sender, EventArgs e)
    {
        var r = SelectedRework();
        if (r is null) { Warn("请先选择返工单。"); return; }
        if (!r.CanScrap) { Warn($"返工单 {r.ReworkNo} 已是终态（{r.Status}），不能报废。"); return; }

        if (MessageBox.Show($"确认将返工单 {r.ReworkNo}（电芯 {r.CellSn}）判定报废？\n报废后电芯状态置「不良」，来源保留单保持不解除。",
                "报废确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var (ok, msg) = MesDatabase.ScrapRework(r.ReworkNo, "admin", "返工后仍不合格，判定报废");
        MessageBox.Show(msg, ok ? "操作成功" : "操作失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryRework();
        SelectReworkRow(r.ReworkNo);
    }


}
