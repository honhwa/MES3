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
/// MainForm 分部类（按功能域拆分，第 19 部分）：**客户投诉与 8D 归零**页。
/// 职责：客诉台账 / 8D 报告（D1~D8）/ 流转履历 / 触发范围反查 / 客户维度统计，
/// 并提供「登记客诉、推进 8D、修订步骤、客户确认、关闭客诉、冻结影响范围、导出」七项操作。
/// 复用点：触发范围反查与冻结**直接复用召回反查能力**（<see cref="Data.MesDatabase.GetComplaintImpact"/>），
/// 不另起一套影响面算法，保证「客诉 → 召回 → 保留 → 追溯」是同一条链路。
/// </summary>
public partial class MainForm
{
    // ================= 客户投诉与 8D 归零（顶层第 24 个页签） =================

    /// <summary>构建「客户投诉与 8D」页签：汇总卡 + 筛选条 + 主从视图 + 操作条。</summary>
    private TabPage BuildComplaintTab()
    {
        var page = new TabPage("客户投诉与 8D");

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("待受理 (条)", Color.FromArgb(230, 126, 34), out _lblCmpPending));
        cards.Controls.Add(MakeCard("处理中 (条)", Color.FromArgb(41, 128, 185), out _lblCmpInvestigating));
        cards.Controls.Add(MakeCard("待客户确认 (条)", Color.FromArgb(142, 68, 173), out _lblCmpWaiting));
        cards.Controls.Add(MakeCard("超期未关闭 (条)", Color.FromArgb(192, 0, 0), out _lblCmpOverdue));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("客诉台账（选中一行 → 下方显示 8D 报告 / 流转履历 / 触发范围）", out _cmpGrid));

        var sub = new TabControl { Dock = DockStyle.Fill };
        sub.TabPages.Add(BuildCmpEightDTab());
        sub.TabPages.Add(BuildCmpImpactTab());
        sub.TabPages.Add(BuildCmpHistoryTab());
        sub.TabPages.Add(BuildCmpStatTab());
        split.Panel2.Controls.Add(sub);

        page.Controls.Add(split);
        page.Controls.Add(BuildComplaintActionBar());
        page.Controls.Add(BuildComplaintQueryBar());
        page.Controls.Add(cards);

        SetupCmpColumns();
        DoQueryComplaints();

        page.Enter -= ComplaintTab_Enter;
        page.Enter += ComplaintTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>构建「8D 报告」子页：进度 + 八步明细表。</summary>
    private TabPage BuildCmpEightDTab()
    {
        var page = new TabPage("8D 报告");
        page.Padding = new Padding(4);

        _lblCmpEightDProgress = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8, 10, 8, 0),
            ForeColor = Color.FromArgb(15, 76, 129),
            BackColor = Color.WhiteSmoke,
            Text = "请选择一条客诉。"
        };
        page.Controls.Add(MakeGridGroup("8D 八步（蓝色=当前步骤；已填=绿、待填=灰）", out _cmpEightDGrid));
        page.Controls.Add(_lblCmpEightDProgress);
        return page;
    }

    /// <summary>构建「触发范围」子页：反查结论 + 受影响电芯 / 材料批次两张表。</summary>
    private TabPage BuildCmpImpactTab()
    {
        var page = new TabPage("触发范围反查");
        page.Padding = new Padding(4);

        _lblCmpImpactSummary = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8, 8, 8, 0),
            BackColor = Color.WhiteSmoke,
            ForeColor = Color.Gray,
            Text = "请选择一条客诉，将按关联范围自动选择「电芯SN / 出货单 / 材料批次 / 客户」进行反查。"
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 190,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("受影响电芯（复用召回反查能力）", out _cmpCellGrid));
        split.Panel2.Controls.Add(MakeGridGroup("受影响材料批次", out _cmpLotGrid));

        page.Controls.Add(split);
        page.Controls.Add(_lblCmpImpactSummary);
        return page;
    }

    /// <summary>构建「流转履历」子页。</summary>
    private TabPage BuildCmpHistoryTab()
    {
        var page = new TabPage("流转履历");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup("客诉流转履历（登记 → 推进 8D → 冻结 → 客户确认 → 关闭）", out _cmpHistoryGrid));
        return page;
    }

    /// <summary>构建「客户统计」子页。</summary>
    private TabPage BuildCmpStatTab()
    {
        var page = new TabPage("客户统计");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup("按客户维度统计（总量 / 严重数 / 未结 / 已结 / 超期 / 关闭率）", out _cmpStatGrid));
        return page;
    }

    /// <summary>构建「客诉」筛选条：状态 / 关键字。</summary>
    private Control BuildComplaintQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboCmpStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(4, 4, 0, 0) };
        _cboCmpStatus.Items.Add("全部");
        foreach (var s in ComplaintStatus.All) _cboCmpStatus.Items.Add(s);
        _cboCmpStatus.SelectedIndex = 0;

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtCmpKeyword = new TextBox { Width = 230, Margin = new Padding(4, 4, 0, 0) };

        bar.Controls.Add(_cboCmpStatus);
        bar.Controls.Add(_txtCmpKeyword);
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryComplaints()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            if (_cboCmpStatus.Items.Count > 0) _cboCmpStatus.SelectedIndex = 0;
            _txtCmpKeyword.Text = "";
            DoQueryComplaints();
        }));
        bar.Controls.Add(new Label
        {
            Text = "（单号 / 客户 / 型号 / 分类 / 现象 / 责任人）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(8, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>构建「客诉」操作条：登记 / 推进 / 修订 / 确认 / 关闭 / 冻结 / 导出。</summary>
    private Control BuildComplaintActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        bar.Controls.Add(MakeToolbarButton("登记客诉", BtnCmpCreate));
        _btnCmpAdvance = MakeToolbarButton("推进 8D", BtnCmpAdvance);
        bar.Controls.Add(_btnCmpAdvance);
        _btnCmpEditStep = MakeToolbarButton("修订步骤", BtnCmpEditStep);
        bar.Controls.Add(_btnCmpEditStep);
        _btnCmpConfirm = MakeToolbarButton("客户确认", BtnCmpConfirm);
        bar.Controls.Add(_btnCmpConfirm);
        _btnCmpClose = MakeToolbarButton("关闭客诉", BtnCmpClose);
        bar.Controls.Add(_btnCmpClose);
        _btnCmpFreeze = MakeToolbarButton("冻结影响范围", BtnCmpFreeze);
        bar.Controls.Add(_btnCmpFreeze);
        bar.Controls.Add(MakeToolbarButton("导出 8D 报告", BtnCmpExport));
        bar.Controls.Add(new Label
        {
            Text = "8D 顺序推进不可跳步；关闭前 D3 临时围堵 / D4 根本原因 / D5 永久对策 必须齐备",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    // ---------------- 列定义 ----------------

    /// <summary>设置客诉 / 8D / 履历 / 电芯 / 批次 / 统计六张表的列定义。</summary>
    private void SetupCmpColumns()
    {
        _cmpGrid.AutoGenerateColumns = false;
        _cmpGrid.Columns.Clear();
        _cmpGrid.Columns.Add(MakeTextCol("ComplaintNo", "客诉单号", true, 13));
        _cmpGrid.Columns.Add(MakeTextCol("CustomerName", "客户", true, 10));
        _cmpGrid.Columns.Add(MakeTextCol("Category", "症状分类", true, 9));
        _cmpGrid.Columns.Add(MakeTextCol("Severity", "严重度", true, 7));
        _cmpGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 12));
        _cmpGrid.Columns.Add(MakeTextCol("Quantity", "数量", true, 7));
        _cmpGrid.Columns.Add(MakeTextCol("Status", "状态", true, 9));
        _cmpGrid.Columns.Add(MakeTextCol("StepProgress", "8D 进度", true, 8));
        _cmpGrid.Columns.Add(MakeTextCol("DueText", "回复时限", true, 10));
        _cmpGrid.Columns.Add(MakeTextCol("DueHealthText", "时限状态", true, 10));
        _cmpGrid.Columns.Add(MakeTextCol("SafetyText", "安全", true, 7));
        _cmpGrid.Columns.Add(MakeTextCol("RecallText", "召回", true, 7));
        _cmpGrid.Columns.Add(MakeTextCol("Owner", "责任人", true, 11));
        _cmpGrid.Columns.Add(MakeTextCol("ReceiveText", "接收时间", true, 12));
        _cmpGrid.Columns.Add(MakeTextCol("SymptomDesc", "客户描述现象", true, 34));

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _cmpGrid.SelectionChanged -= CmpGrid_SelectionChanged;
        _cmpGrid.SelectionChanged += CmpGrid_SelectionChanged;
        _cmpGrid.CellFormatting -= CmpGrid_CellFormatting;
        _cmpGrid.CellFormatting += CmpGrid_CellFormatting;

        _cmpEightDGrid.AutoGenerateColumns = false;
        _cmpEightDGrid.Columns.Clear();
        _cmpEightDGrid.Columns.Add(MakeTextCol("Step", "步骤", true, 13));
        _cmpEightDGrid.Columns.Add(MakeTextCol("State", "状态", true, 9));
        _cmpEightDGrid.Columns.Add(MakeTextCol("Content", "内容", true, 52));
        _cmpEightDGrid.Columns.Add(MakeTextCol("Requirement", "填写要求", true, 26));
        _cmpEightDGrid.CellFormatting -= CmpEightDGrid_CellFormatting;
        _cmpEightDGrid.CellFormatting += CmpEightDGrid_CellFormatting;

        _cmpHistoryGrid.AutoGenerateColumns = false;
        _cmpHistoryGrid.Columns.Clear();
        _cmpHistoryGrid.Columns.Add(MakeTextCol("Seq", "序号", true, 6));
        _cmpHistoryGrid.Columns.Add(MakeTextCol("ActionType", "动作", true, 11));
        _cmpHistoryGrid.Columns.Add(MakeTextCol("Step", "步骤", true, 13));
        _cmpHistoryGrid.Columns.Add(MakeTextCol("Operator", "操作人", true, 12));
        _cmpHistoryGrid.Columns.Add(MakeTextCol("TimeText", "时间", true, 10));
        _cmpHistoryGrid.Columns.Add(MakeTextCol("CellCount", "涉及电芯", true, 9));
        _cmpHistoryGrid.Columns.Add(MakeTextCol("Remark", "说明", true, 39));

        _cmpCellGrid.AutoGenerateColumns = false;
        _cmpCellGrid.Columns.Clear();
        _cmpCellGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 16));
        _cmpCellGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 13));
        _cmpCellGrid.Columns.Add(MakeTextCol("Status", "状态", true, 9));
        _cmpCellGrid.Columns.Add(MakeTextCol("Shipped", "已出货", true, 8));
        _cmpCellGrid.Columns.Add(MakeTextCol("OrderNo", "出货单", true, 12));
        _cmpCellGrid.Columns.Add(MakeTextCol("Customer", "客户", true, 10));
        _cmpCellGrid.Columns.Add(MakeTextCol("Held", "在保留", true, 8));
        _cmpCellGrid.Columns.Add(MakeTextCol("HolderNo", "保留单", true, 12));
        _cmpCellGrid.Columns.Add(MakeTextCol("StockLocation", "库位", true, 12));

        _cmpLotGrid.AutoGenerateColumns = false;
        _cmpLotGrid.Columns.Clear();
        _cmpLotGrid.Columns.Add(MakeTextCol("LotNo", "批次号", true, 20));
        _cmpLotGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 30));
        _cmpLotGrid.Columns.Add(MakeTextCol("CellCount", "涉及电芯", true, 12));
        _cmpLotGrid.Columns.Add(MakeTextCol("ShipmentCount", "涉及出货单", true, 12));

        _cmpStatGrid.AutoGenerateColumns = false;
        _cmpStatGrid.Columns.Clear();
        _cmpStatGrid.Columns.Add(MakeTextCol("CustomerName", "客户", true, 16));
        _cmpStatGrid.Columns.Add(MakeTextCol("Total", "客诉总数", true, 11));
        _cmpStatGrid.Columns.Add(MakeTextCol("CriticalCount", "其中严重", true, 11));
        _cmpStatGrid.Columns.Add(MakeTextCol("OpenCount", "未结", true, 9));
        _cmpStatGrid.Columns.Add(MakeTextCol("ClosedCount", "已结", true, 9));
        _cmpStatGrid.Columns.Add(MakeTextCol("OverdueCount", "超期", true, 9));
        _cmpStatGrid.Columns.Add(MakeTextCol("CloseRateText", "关闭率", true, 10));
    }

    // ---------------- 着色 ----------------

    /// <summary>客诉列表着色：严重度、状态、时限状态分级着色。</summary>
    private void CmpGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_cmpGrid.Rows[e.RowIndex].DataBoundItem is not CustomerComplaint c) return;
        var col = _cmpGrid.Columns[e.ColumnIndex].Name;

        switch (col)
        {
            case "Severity":
                e.CellStyle.ForeColor = c.Severity switch
                {
                    ComplaintSeverity.Critical => Color.FromArgb(192, 0, 0),
                    ComplaintSeverity.Major => Color.FromArgb(230, 126, 34),
                    _ => Color.FromArgb(127, 140, 141)
                };
                break;
            case "Status":
                e.CellStyle.ForeColor = c.Status switch
                {
                    ComplaintStatus.Closed => Color.FromArgb(39, 174, 96),
                    ComplaintStatus.WaitingCustomer => Color.FromArgb(142, 68, 173),
                    ComplaintStatus.Investigating => Color.FromArgb(41, 128, 185),
                    _ => Color.FromArgb(230, 126, 34)
                };
                break;
            case "DueHealthText":
                e.CellStyle.ForeColor = c.DueHealth switch
                {
                    "已超期" => Color.FromArgb(192, 0, 0),
                    "即将到期" => Color.FromArgb(230, 126, 34),
                    "已关闭" => Color.FromArgb(127, 140, 141),
                    _ => Color.FromArgb(39, 174, 96)
                };
                break;
            case "SafetyText" when c.IsSafetyInvolved:
            case "RecallText" when c.NeedRecall:
                e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
                break;
        }
    }

    /// <summary>8D 步骤表着色：当前步骤蓝、已填绿、待填灰。</summary>
    private void CmpEightDGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_cmpEightDGrid.Rows[e.RowIndex].DataBoundItem is not CmpStepRow r) return;
        var col = _cmpEightDGrid.Columns[e.ColumnIndex].Name;
        if (col != "State" && col != "Step") return;

        e.CellStyle.ForeColor = r.IsCurrent
            ? Color.FromArgb(15, 76, 129)
            : r.IsFilled ? Color.FromArgb(39, 174, 96) : Color.FromArgb(150, 150, 150);
    }

    // ---------------- 数据加载 ----------------

    private void ComplaintTab_Enter(object? sender, EventArgs e) => EnsureComplaintReady();

    /// <summary>兜底初始化「客户投诉与 8D」：页签 Enter / 全局刷新时确保数据已加载（幂等）。</summary>
    private void EnsureComplaintReady()
    {
        if (_cmpGrid is null) return;   // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_cmpGrid.Rows.Count == 0) DoQueryComplaints();
    }

    /// <summary>加载客诉页：台账 / 8D / 触发范围 / 履历 / 客户统计 + 汇总卡。</summary>
    private void DoQueryComplaints()
    {
        if (_cmpGrid is null) return;   // 控件尚未创建（构造期）：直接返回

        var kw = _txtCmpKeyword?.Text.Trim() ?? "";
        var status = _cboCmpStatus is not null && _cboCmpStatus.SelectedIndex > 0
            ? _cboCmpStatus.SelectedItem?.ToString()
            : null;

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _cmpGrid.DataSource = null;
        _cmpGrid.DataSource = MesDatabase.QueryComplaints(status, kw.Length == 0 ? null : kw);

        // 汇总卡始终反映全量（不受筛选影响）
        var (pending, investigating, waiting, _, overdue) = MesDatabase.GetComplaintSummary();
        if (_lblCmpPending is not null) _lblCmpPending.Text = pending.ToString();
        if (_lblCmpInvestigating is not null) _lblCmpInvestigating.Text = investigating.ToString();
        if (_lblCmpWaiting is not null) _lblCmpWaiting.Text = waiting.ToString();
        if (_lblCmpOverdue is not null)
        {
            _lblCmpOverdue.Text = overdue.ToString();
            _lblCmpOverdue.ForeColor = overdue > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }

        if (_cmpStatGrid is not null)
        {
            _cmpStatGrid.DataSource = null;
            _cmpStatGrid.DataSource = MesDatabase.GetComplaintStats();
        }

        // 选中首行（延后到此处，避免构造期 Rows 未物化时越界）
        if (_cmpGrid.Rows.Count > 0 && _cmpGrid.SelectedRows.Count == 0)
        {
            _cmpGrid.ClearSelection();
            _cmpGrid.Rows[0].Selected = true;
        }
        DoQueryCmpDetail();
    }

    /// <summary>加载选中客诉的 8D 内容 / 履历 / 触发范围。</summary>
    private void DoQueryCmpDetail()
    {
        var c = SelectedComplaint();
        if (_cmpEightDGrid is null) return;

        if (c is null)
        {
            _cmpEightDGrid.DataSource = null;
            _cmpHistoryGrid.DataSource = null;
            _cmpCellGrid.DataSource = null;
            _cmpLotGrid.DataSource = null;
            if (_lblCmpEightDProgress is not null) _lblCmpEightDProgress.Text = "请选择一条客诉。";
            if (_lblCmpImpactSummary is not null) _lblCmpImpactSummary.Text = "请选择一条客诉。";
            UpdateCmpButtons();
            return;
        }

        // 8D：手工构造 8 行（步骤 / 状态 / 内容 / 要求），便于高亮当前步骤
        var report = MesDatabase.GetEightD(c.ComplaintNo);
        var rows = EightDStep.Order.Select(s =>
        {
            var content = report?.ContentOf(s) ?? "";
            var isCurrent = c.CurrentStep == s && !c.IsClosed;
            var state = isCurrent ? "当前步骤" : (content.Length > 0 ? "已填写" : "待填写");
            return new CmpStepRow
            {
                Step = s,
                State = state,
                Content = content.Length == 0 ? "—" : content,
                Requirement = EightDStep.RequirementOf(s),
                IsCurrent = isCurrent,
                IsFilled = content.Length > 0
            };
        }).ToList();

        _cmpEightDGrid.DataSource = null;
        _cmpEightDGrid.DataSource = rows;

        if (_lblCmpEightDProgress is not null)
        {
            var filled = report?.FilledCount ?? 0;
            _lblCmpEightDProgress.Text = $"客诉 {c.ComplaintNo} ｜ 客户 {c.CustomerName} ｜ 状态 {c.Status} ｜ "
                                         + $"8D 进度 {c.StepProgress}（已填 {filled}/{EightDStep.Order.Length}）｜ "
                                         + $"当前步骤 {c.CurrentStep}"
                                         + (c.IsClosed ? $" ｜ 已于 {c.ClosedText} 关闭" : "");
        }

        _cmpHistoryGrid.DataSource = null;
        _cmpHistoryGrid.DataSource = MesDatabase.GetComplaintHistories(c.ComplaintNo);

        DoQueryCmpImpact();
        UpdateCmpButtons();
    }

    /// <summary>加载触发范围反查（复用召回反查）。</summary>
    private void DoQueryCmpImpact()
    {
        if (_cmpCellGrid is null) return;
        var c = SelectedComplaint();
        if (c is null)
        {
            _cmpCellGrid.DataSource = null;
            _cmpLotGrid.DataSource = null;
            return;
        }

        var impact = MesDatabase.GetComplaintImpact(c.ComplaintNo);
        _cmpCellGrid.DataSource = null;
        _cmpCellGrid.DataSource = impact.Cells;
        _cmpLotGrid.DataSource = null;
        _cmpLotGrid.DataSource = impact.Lots;

        if (_lblCmpImpactSummary is not null)
        {
            var s = impact.Summary;
            _lblCmpImpactSummary.Text =
                $"关联范围「{(c.LotKeyword.Length == 0 ? c.CustomerName + "（回落到客户）" : c.LotKeyword)}」→ 反查方式：{impact.Mode} ｜ "
                + $"受影响电芯 {s.CellCount} 只（已出货 {s.ShippedCellCount} / 在库 {s.InStockCellCount} / 已在保留 {s.HeldCellCount}）｜ "
                + $"涉及出货单 {s.ShipmentCount} 张 / 客户 {s.CustomerCount} 家 ｜ 可冻结 {s.FreezableCellCount} 只"
                + (impact.Message.Length > 0 ? $"\n{impact.Message}" : "");
        }
    }

    // ---------------- 交互 ----------------

    private void CmpGrid_SelectionChanged(object? sender, EventArgs e)
    {
        DoQueryCmpDetail();   // 选中客诉 → 联动刷新 8D / 履历 / 触发范围
    }

    /// <summary>按当前客诉状态更新按钮可用性（终结态锁死推进与关闭）。</summary>
    private void UpdateCmpButtons()
    {
        var c = SelectedComplaint();
        var open = c is not null && !c.IsClosed;
        var report = c is null ? null : MesDatabase.GetEightD(c.ComplaintNo);

        if (_btnCmpAdvance is not null) _btnCmpAdvance.Enabled = open;
        if (_btnCmpEditStep is not null) _btnCmpEditStep.Enabled = open && report is { FilledCount: > 0 };
        if (_btnCmpConfirm is not null) _btnCmpConfirm.Enabled = open && report is { IsComplete: true } && c!.CustomerConfirmTime is null;
        if (_btnCmpClose is not null) _btnCmpClose.Enabled = open;
        if (_btnCmpFreeze is not null) _btnCmpFreeze.Enabled = c is not null;
    }

    private CustomerComplaint? SelectedComplaint()
    {
        if (_cmpGrid is null || _cmpGrid.SelectedRows.Count == 0) return null;
        return _cmpGrid.SelectedRows[0].DataBoundItem as CustomerComplaint;
    }

    /// <summary>登记客诉。</summary>
    private void BtnCmpCreate(object? sender, EventArgs e)
    {
        using var form = new ComplaintCreateForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg, _) = MesDatabase.CreateComplaint(form.Complaint);
        if (!ok) { Warn(msg); return; }
        MessageBox.Show(msg, "登记成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryComplaints();
    }

    /// <summary>推进 8D：填写当前步骤内容并前进。</summary>
    private void BtnCmpAdvance(object? sender, EventArgs e)
    {
        var c = SelectedComplaint();
        if (c is null) { Warn("请先选择一条客诉。"); return; }
        if (c.IsClosed) { Warn($"客诉 {c.ComplaintNo} 已关闭，不能再推进 8D。"); return; }

        var report = MesDatabase.GetEightD(c.ComplaintNo);
        var existing = report?.ContentOf(c.CurrentStep) ?? "";

        using var form = new EightDStepForm(c.ComplaintNo, c.CurrentStep, existing, isAdvance: true);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.AdvanceEightD(c.ComplaintNo, form.Content, "质量工程师");
        MessageBox.Show(msg, ok ? "推进成功" : "推进失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryComplaints();
    }

    /// <summary>修订已填写的 8D 步骤内容。</summary>
    private void BtnCmpEditStep(object? sender, EventArgs e)
    {
        var c = SelectedComplaint();
        if (c is null) { Warn("请先选择一条客诉。"); return; }
        if (c.IsClosed) { Warn($"客诉 {c.ComplaintNo} 已关闭，8D 已锁定。"); return; }

        var report = MesDatabase.GetEightD(c.ComplaintNo);
        if (report is null) { Warn("该客诉缺少 8D 报告。"); return; }

        // 只列出已填写的步骤供选择（未推进到的步骤不允许提前填写）
        var candidates = EightDStep.Order.Where(s => !string.IsNullOrWhiteSpace(report.ContentOf(s))).ToList();
        if (candidates.Count == 0) { Warn("该客诉尚无已填写的 8D 步骤。"); return; }

        var step = PickStepDialog(c.ComplaintNo, candidates);
        if (step is null) return;

        using var form = new EightDStepForm(c.ComplaintNo, step, report.ContentOf(step), isAdvance: false);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.UpdateEightDStep(c.ComplaintNo, step, form.Content, "质量工程师");
        MessageBox.Show(msg, ok ? "修订成功" : "修订失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryComplaints();
    }

    /// <summary>客户确认（要求 8D 八步齐备）。</summary>
    private void BtnCmpConfirm(object? sender, EventArgs e)
    {
        var c = SelectedComplaint();
        if (c is null) { Warn("请先选择一条客诉。"); return; }

        var (ok, msg) = MesDatabase.ConfirmByCustomer(c.ComplaintNo, "客户确认 8D 报告", "质量工程师");
        MessageBox.Show(msg, ok ? "确认成功" : "确认失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryComplaints();
    }

    /// <summary>关闭客诉（8D 归零完成，需确认）。</summary>
    private void BtnCmpClose(object? sender, EventArgs e)
    {
        var c = SelectedComplaint();
        if (c is null) { Warn("请先选择一条客诉。"); return; }

        var confirm = MessageBox.Show(
            $"确认关闭客诉 {c.ComplaintNo}（客户 {c.CustomerName}）？\n\n"
            + "关闭前置要求：D3 临时围堵 / D4 根本原因 / D5 永久对策 必须已填写。关闭后 8D 将锁定不可修改。",
            "关闭确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        var (ok, msg) = MesDatabase.CloseComplaint(c.ComplaintNo, "质量工程师", "8D 归零完成，客诉关闭");
        MessageBox.Show(msg, ok ? "关闭成功" : "关闭失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryComplaints();
    }

    /// <summary>按客诉关联范围冻结影响电芯（复用召回冻结）。</summary>
    private void BtnCmpFreeze(object? sender, EventArgs e)
    {
        var c = SelectedComplaint();
        if (c is null) { Warn("请先选择一条客诉。"); return; }

        var impact = MesDatabase.GetComplaintImpact(c.ComplaintNo);
        if (impact.Summary.CellCount == 0)
        {
            Warn($"客诉 {c.ComplaintNo} 未反查到受影响的电芯。\n\n"
                 + $"关联范围：{(c.LotKeyword.Length == 0 ? "（未登记）" : c.LotKeyword)}\n{impact.Message}");
            return;
        }

        var confirm = MessageBox.Show(
            $"将按「{impact.Mode}」冻结影响范围内的可冻结电芯。\n\n"
            + $"受影响电芯 {impact.Summary.CellCount} 只 ｜ 可冻结 {impact.Summary.FreezableCellCount} 只"
            + $"（已出货 {impact.Summary.ShippedCellCount} / 已在保留 {impact.Summary.HeldCellCount}）\n\n"
            + "冻结后电芯进入保留单，需走保留评审流程。是否继续？",
            "冻结确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        var (ok, msg, holdNo, _) = MesDatabase.FreezeComplaintImpact(c.ComplaintNo, "质量工程师", $"客诉 {c.ComplaintNo} 触发冻结");
        MessageBox.Show(msg + (holdNo.Length > 0 ? $"\n\n保留单号：{holdNo}" : ""),
            ok ? "冻结完成" : "冻结失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryComplaints();
    }

    /// <summary>导出 8D 报告（汇总 / 客诉 / 8D / 履历 / 触发范围 / 客户统计，6 个工作表）。</summary>
    private void BtnCmpExport(object? sender, EventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出 8D 报告",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"客诉与8D报告_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var w = new XlsxWriter();
            BuildComplaintWorkbook(w);
            var (sheets, rows) = w.Save(dlg.FileName);
            MessageBox.Show($"8D 报告已导出：\n{dlg.FileName}\n\n工作表 {sheets} 个 ｜ 数据行 {rows} 行",
                "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"导出失败：{ex.Message}");
        }
    }

    /// <summary>构造客诉与 8D 导出工作簿。纯数据构造，便于自测。</summary>
    internal static void BuildComplaintWorkbook(XlsxWriter w)
    {
        var (pending, investigating, waiting, closed, overdue) = MesDatabase.GetComplaintSummary();

        var s0 = w.AddSheet("汇总");
        s0.SetColumns(("指标", 22), ("数值", 16));
        s0.AddRow("客诉总数（条）", MesDatabase.Complaints.Count);
        s0.AddRow("待受理（条）", pending);
        s0.AddRow("处理中（条）", investigating);
        s0.AddRow("待客户确认（条）", waiting);
        s0.AddRow("已关闭（条）", closed);
        s0.AddRow("超期未关闭（条）", overdue);
        s0.AddRow("8D 齐备（份）", MesDatabase.EightDReports.Count(r => r.IsComplete));
        s0.AddRow("8D 平均完成度", MesDatabase.EightDReports.Count == 0
            ? "—"
            : $"{MesDatabase.EightDReports.Average(r => r.FilledCount):0.0} / {EightDStep.Order.Length}");
        s0.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        var s1 = w.AddSheet("客诉台账");
        s1.SetColumns(("客诉单号", 16), ("客户", 14), ("联系人", 20), ("来源", 12), ("严重度", 9),
            ("症状分类", 12), ("型号", 16), ("关联范围", 20), ("数量", 9), ("状态", 12),
            ("8D进度", 9), ("接收时间", 18), ("回复时限", 12), ("时限状态", 11),
            ("涉安全", 8), ("需召回", 8), ("责任人", 14), ("客户描述现象", 50));
        foreach (var c in MesDatabase.QueryComplaints())
            s1.AddRow(c.ComplaintNo, c.CustomerName, c.ContactInfo, c.Source, c.Severity, c.Category,
                c.ProductModel, c.LotKeyword, c.Quantity, c.Status, c.StepProgress, c.ReceiveText,
                c.DueText, c.DueHealthText, c.SafetyText, c.RecallText, c.Owner, c.SymptomDesc);

        var s2 = w.AddSheet("8D 明细");
        s2.SetColumns(("客诉单号", 16), ("客户", 14), ("步骤", 14), ("状态", 11), ("内容", 80), ("填写要求", 50));
        foreach (var c in MesDatabase.QueryComplaints())
        {
            var r = MesDatabase.GetEightD(c.ComplaintNo);
            foreach (var step in EightDStep.Order)
            {
                var content = r?.ContentOf(step) ?? "";
                var isCurrent = c.CurrentStep == step && !c.IsClosed;
                var state = isCurrent ? "当前步骤" : (content.Length > 0 ? "已填写" : "待填写");
                s2.AddRow(c.ComplaintNo, c.CustomerName, step, state,
                    content.Length == 0 ? "" : content, EightDStep.RequirementOf(step));
            }
        }

        var s3 = w.AddSheet("流转履历");
        s3.SetColumns(("客诉单号", 16), ("序号", 8), ("动作", 12), ("步骤", 14), ("操作人", 14),
            ("时间", 18), ("涉及电芯", 10), ("说明", 60));
        foreach (var c in MesDatabase.QueryComplaints())
            foreach (var h in MesDatabase.GetComplaintHistories(c.ComplaintNo))
                s3.AddRow(h.ComplaintNo, h.Seq, h.ActionType, h.Step, h.Operator,
                    h.Time.ToString("yyyy-MM-dd HH:mm"), h.CellCount, h.Remark);

        var s4 = w.AddSheet("触发范围");
        s4.SetColumns(("客诉单号", 16), ("客户", 14), ("反查方式", 12), ("关联范围", 20),
            ("受影响电芯", 12), ("已出货", 9), ("在库", 8), ("已在保留", 10),
            ("涉及出货单", 12), ("涉及客户", 10), ("可冻结", 9), ("结论", 50));
        foreach (var c in MesDatabase.QueryComplaints())
        {
            var im = MesDatabase.GetComplaintImpact(c.ComplaintNo);
            s4.AddRow(c.ComplaintNo, c.CustomerName, im.Mode, c.LotKeyword,
                im.Summary.CellCount, im.Summary.ShippedCellCount, im.Summary.InStockCellCount,
                im.Summary.HeldCellCount, im.Summary.ShipmentCount, im.Summary.CustomerCount,
                im.Summary.FreezableCellCount, im.Message);
        }

        var s5 = w.AddSheet("客户统计");
        s5.SetColumns(("客户", 16), ("客诉总数", 11), ("其中严重", 11), ("未结", 9),
            ("已结", 9), ("超期", 9), ("关闭率", 10));
        foreach (var r in MesDatabase.GetComplaintStats())
            s5.AddRow(r.CustomerName, r.Total, r.CriticalCount, r.OpenCount,
                r.ClosedCount, r.OverdueCount, r.CloseRateText);
    }

    /// <summary>弹出步骤选择框（修订时使用）。返回 null 表示取消。</summary>
    private string? PickStepDialog(string complaintNo, List<string> steps)
    {
        using var dlg = new Form
        {
            Text = $"选择要修订的 8D 步骤 - {complaintNo}",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(340, 210),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("微软雅黑", 9F)
        };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var s in steps) list.Items.Add(s);
        if (list.Items.Count > 0) list.SelectedIndex = 0;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.WhiteSmoke };
        var ok = new Button { Text = "确定", Size = new Size(84, 30), Location = new Point(150, 9), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Size = new Size(84, 30), Location = new Point(242, 9), DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);

        dlg.Controls.Add(list);
        dlg.Controls.Add(bottom);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;

        return dlg.ShowDialog(this) == DialogResult.OK ? list.SelectedItem?.ToString() : null;
    }

    /// <summary>8D 步骤表的行模型（仅用于展示：步骤 / 状态 / 内容 / 填写要求）。</summary>
    public sealed class CmpStepRow
    {
        public string Step { get; init; } = "";
        public string State { get; init; } = "";
        public string Content { get; init; } = "";
        public string Requirement { get; init; } = "";
        public bool IsCurrent { get; init; }
        public bool IsFilled { get; init; }
    }
}
