using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类：物料策略页（技术方案第 4 章追加项：替代料 + 安全库存补货）。
///
/// 子页一「替代料」：某型号某数量下，主料缺口能不能用替代料顶、顶多少、还剩多少。
///   硬门槛：只有「已认可」的替代关系才参与计算；待客户批准的只展示（这是最容易被系统放过的坑）。
/// 子页二「安全库存补货」：逐物料算补货点 ROP，给出补货量、下单日，并可一键生成补货申请。
///   补货点 ROP = 日均消耗 × 采购提前期 + 安全库存——低于它就会在等料期间断料。
/// </summary>
public partial class MainForm
{
    private static readonly Color StInk = Color.FromArgb(30, 41, 59);
    private static readonly Color StMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color StOk = Color.FromArgb(21, 128, 61);
    private static readonly Color StWarn = Color.FromArgb(180, 83, 9);
    private static readonly Color StBad = Color.FromArgb(185, 28, 28);

    private readonly ToolTip _stTip = new();

    // 替代料
    private ComboBox _cboSubModel = null!;
    private NumericUpDown _numSubQty = null!;
    private DataGridView _gridSubRules = null!;
    private DataGridView _gridSubResult = null!;
    private Label _lblSubSummary = null!;

    // 补货
    private DataGridView _gridReplenish = null!;
    private Label _lblReplenishNote = null!;
    private TextBox _txtReplenishOperator = null!;
    private Label? _cardRepTotal, _cardRepUrgent, _cardRepNeed, _cardRepIdle, _cardRepItems;

    private TabPage BuildMaterialStrategyTab()
    {
        var page = new TabPage("物料策略");
        page.Padding = new Padding(6);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSubstitutePage());
        tabs.TabPages.Add(BuildReplenishPage());
        page.Controls.Add(tabs);

        page.Enter -= StrategyTab_Enter;
        page.Enter += StrategyTab_Enter;
        return page;
    }

    private void StrategyTab_Enter(object? sender, EventArgs e)
    {
        LoadSubstituteRules();

        if (_cboSubModel is not null)
        {
            var keep = _cboSubModel.SelectedItem as string;
            _cboSubModel.Items.Clear();
            foreach (var m in MesDatabase.GetProductModels()) _cboSubModel.Items.Add(m);
            if (_cboSubModel.Items.Count > 0)
                _cboSubModel.SelectedIndex = keep is null ? 0 : Math.Max(0, _cboSubModel.Items.IndexOf(keep));
        }

        LoadReplenishment();

        // 首次进入自动跑一次试算，避免页面一片空白（用户改数量/型号后可手动重算）
        if (_gridSubResult is not null && _gridSubResult.Rows.Count == 0) RunSubstituteCheck();
    }

    // ================= 子页一：替代料 =================

    private TabPage BuildSubstitutePage()
    {
        var tab = new TabPage("替代料");
        tab.Padding = new Padding(4);

        // ---- 试算条 ----
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(4, 8, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "型号：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboSubModel = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190,
            Margin = new Padding(0, 4, 12, 0)
        };
        bar.Controls.Add(_cboSubModel);

        bar.Controls.Add(new Label { Text = "数量：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _numSubQty = new NumericUpDown
        {
            Width = 112,
            Minimum = 1,
            Maximum = 1000000,
            Increment = 500,
            Value = 6000,
            Margin = new Padding(0, 4, 12, 0)
        };
        bar.Controls.Add(_numSubQty);

        var btn = MakeToolbarButton("🔍 替代齐套试算", (_, _) => RunSubstituteCheck());
        btn.Width = 148;
        bar.Controls.Add(btn);

        var note = new Label
        {
            AutoSize = false,
            Width = 520,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = StMuted,
            Margin = new Padding(10, 8, 0, 0),
            Text = "只有「已认可」且有余量的替代料才顶缺口；待客户批准的关系只展示、不参与计算。"
        };
        _stTip.SetToolTip(note, "替代三道门槛：关系成立（用量系数）+ 客户/工艺已认可 + 替代料自己有货。缺一不可。");
        bar.Controls.Add(note);

        // ---- 结论条 ----
        _lblSubSummary = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            AutoSize = false,
            ForeColor = StInk,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 6, 10, 6),
            Text = "选型号与数量后点「🔍 替代齐套试算」，看缺口能不能用替代料顶上。"
        };

        // ---- 替代关系台账（上）----
        var rulesGroup = MakeGridGroup("替代关系台账（唯一键 = 主料 + 替代料；认可状态是硬门槛）", out _gridSubRules);
        SetupSubRuleColumns();

        // ---- 试算结果（下）----
        var resultGroup = MakeGridGroup("替代齐套试算结果（按 BOM 行给出：需求 / 主料可用 / 缺口 / 替代可用 / 实替量 / 剩余缺口）", out _gridSubResult);
        SetupSubResultColumns();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6
        };
        split.SizeChanged += (_, _) =>
        {
            var h = split.Height;
            if (h <= 0) return;
            var want = (int)(h * 0.40);
            if (want > 90 && h - want - split.SplitterWidth > 90) split.SplitterDistance = want;
        };
        split.Panel1.Controls.Add(rulesGroup);
        split.Panel2.Controls.Add(resultGroup);

        tab.Controls.Add(split);            // Fill
        tab.Controls.Add(_lblSubSummary);   // Top（后加 → 更靠上）
        tab.Controls.Add(bar);              // Top
        return tab;
    }

    private void SetupSubRuleColumns()
    {
        _gridSubRules.AutoGenerateColumns = false;
        _gridSubRules.Columns.Clear();
        _gridSubRules.Columns.Add(MakeTextCol("MainName", "主料", true, 16));
        _gridSubRules.Columns.Add(MakeTextCol("MainCode", "主料编码", true, 11));
        _gridSubRules.Columns.Add(MakeTextCol("SubName", "替代料", true, 16));
        _gridSubRules.Columns.Add(MakeTextCol("SubCode", "替代料编码", true, 12));
        _gridSubRules.Columns.Add(MakeTextCol("RatioText", "用量系数", true, 9));
        _gridSubRules.Columns.Add(MakeTextCol("PriorityText", "优先级", true, 7));
        _gridSubRules.Columns.Add(MakeTextCol("ApprovalText", "认可状态", true, 13));
        _gridSubRules.Columns.Add(MakeTextCol("Condition", "适用条件", true, 30));
    }

    private void SetupSubResultColumns()
    {
        _gridSubResult.AutoGenerateColumns = false;
        _gridSubResult.Columns.Clear();
        _gridSubResult.Columns.Add(MakeTextCol("MaterialName", "BOM 物料", true, 14));
        _gridSubResult.Columns.Add(MakeTextCol("RequirementText", "需求量", true, 10));
        _gridSubResult.Columns.Add(MakeTextCol("AvailableText", "主料可用", true, 10));
        _gridSubResult.Columns.Add(MakeTextCol("ShortageText", "主料缺口", true, 10));
        _gridSubResult.Columns.Add(MakeTextCol("SubName", "替代料", true, 14));
        _gridSubResult.Columns.Add(MakeTextCol("SubAvailableText", "替代可用", true, 10));
        _gridSubResult.Columns.Add(MakeTextCol("AppliedText", "实替量", true, 10));
        _gridSubResult.Columns.Add(MakeTextCol("RemainText", "剩余缺口", true, 10));
        _gridSubResult.Columns.Add(MakeTextCol("StatusText", "结论", true, 10));
        _gridSubResult.Columns.Add(MakeTextCol("Note", "说明", true, 30));
    }

    private void LoadSubstituteRules()
    {
        if (_gridSubRules is null) return;
        _gridSubRules.DataSource = null;
        _gridSubRules.DataSource = MesDatabase.GetSubstitutes();
    }

    private void RunSubstituteCheck()
    {
        var model = _cboSubModel?.SelectedItem as string ?? "";
        if (model.Length == 0) { Warn("请先选择产品型号。"); return; }

        var qty = (int)(_numSubQty?.Value ?? 0);
        if (qty <= 0) { Warn("数量必须大于 0。"); return; }

        var res = MesDatabase.CheckBomAvailabilityWithSubstitutes(model, qty);
        _gridSubResult.DataSource = null;
        _gridSubResult.DataSource = res.Rows;

        if (_lblSubSummary is not null)
        {
            _lblSubSummary.Text = res.Rows.Count == 0
                ? $"型号 {model} 没有 BOM 数据，无法做替代齐套试算。"
                : res.SummaryText;
            _lblSubSummary.ForeColor = res.ShortAfter > 0 ? StBad : res.AppliedCount > 0 ? StWarn : StOk;
        }

        if (_gridSubResult.Rows.Count > 0) _gridSubResult.Rows[0].Selected = true;
    }

    // ================= 子页二：安全库存补货 =================

    private TabPage BuildReplenishPage()
    {
        var tab = new TabPage("安全库存补货");
        tab.Padding = new Padding(4);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(4, 8, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _txtReplenishOperator = new TextBox { Width = 110, Margin = new Padding(0, 4, 12, 0), Text = "计划员" };
        bar.Controls.Add(_txtReplenishOperator);

        var btn = MakeToolbarButton("⚡ 按建议生成补货申请", (_, _) => CreateReplenishment());
        btn.Width = 176;
        bar.Controls.Add(btn);

        _lblReplenishNote = new Label
        {
            AutoSize = false,
            Width = 620,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = StMuted,
            Margin = new Padding(10, 8, 0, 0),
            Text = "补货点 ROP = 日均消耗 × 采购提前期 + 安全库存；净库存含可用 + 在途 + 草稿。"
        };
        _stTip.SetToolTip(_lblReplenishNote, "补货点含义：在等料的这段时间里会消耗掉的量，再加上安全线。低于它必须下单，否则会断料。");
        bar.Controls.Add(_lblReplenishNote);

        var cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 104,
            Padding = new Padding(4, 4, 4, 4),
            WrapContents = false,
            AutoScroll = true
        };
        cards.Controls.Add(MakeCard("监控物料", Color.FromArgb(15, 76, 129), out var c1));
        cards.Controls.Add(MakeCard("紧急补货", StBad, out var c2));
        cards.Controls.Add(MakeCard("需补货", StWarn, out var c3));
        cards.Controls.Add(MakeCard("无消耗依据", StMuted, out var c4));
        cards.Controls.Add(MakeCard("建议下单项", Color.FromArgb(15, 76, 129), out var c5));
        _cardRepTotal = c1; _cardRepUrgent = c2; _cardRepNeed = c3; _cardRepIdle = c4; _cardRepItems = c5;

        var gridGroup = MakeGridGroup(
            "安全库存补货看板（按物料：日均消耗 × 提前期 + 安全库存 = 补货点；净库存低于补货点即需下单）",
            out _gridReplenish);
        SetupReplenishColumns();
        _gridReplenish.CellFormatting += ReplenishGrid_CellFormatting;
        _gridReplenish.CellToolTipTextNeeded += ReplenishGrid_CellToolTip;

        tab.Controls.Add(gridGroup);   // Fill
        tab.Controls.Add(cards);       // Top（后加 → 更靠上）
        tab.Controls.Add(bar);         // Top
        return tab;
    }

    private void SetupReplenishColumns()
    {
        _gridReplenish.AutoGenerateColumns = false;
        _gridReplenish.Columns.Clear();
        _gridReplenish.Columns.Add(MakeTextCol("MaterialName", "物料", true, 13));
        _gridReplenish.Columns.Add(MakeTextCol("MaterialCode", "编码", true, 11));
        _gridReplenish.Columns.Add(MakeTextCol("DailyText", "日均消耗（来源）", true, 15));
        _gridReplenish.Columns.Add(MakeTextCol("LeadText", "提前期", true, 7));
        _gridReplenish.Columns.Add(MakeTextCol("SafetyText", "安全库存", true, 9));
        _gridReplenish.Columns.Add(MakeTextCol("QtyText", "可用", true, 8));
        _gridReplenish.Columns.Add(MakeTextCol("NetText", "净库存", true, 9));
        _gridReplenish.Columns.Add(MakeTextCol("RopText", "补货点", true, 9));
        _gridReplenish.Columns.Add(MakeTextCol("SuggestText", "建议补货", true, 13));
        _gridReplenish.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _gridReplenish.Columns.Add(MakeTextCol("DateText", "建议下单日", true, 9));
        _gridReplenish.Columns.Add(MakeTextCol("VerdictText", "判定说明", true, 26));
    }

    private void LoadReplenishment()
    {
        if (_gridReplenish is null) return;
        var board = MesDatabase.BuildReplenishmentBoard();

        _gridReplenish.DataSource = null;
        _gridReplenish.DataSource = board.Rows;

        if (_cardRepTotal is not null) _cardRepTotal.Text = board.Total.ToString();
        if (_cardRepUrgent is not null) _cardRepUrgent.Text = board.UrgentCount.ToString();
        if (_cardRepNeed is not null) _cardRepNeed.Text = board.NeedCount.ToString();
        if (_cardRepIdle is not null) _cardRepIdle.Text = board.IdleCount.ToString();
        if (_cardRepItems is not null) _cardRepItems.Text = board.OrderCount.ToString();

        if (_lblReplenishNote is not null)
        {
            _lblReplenishNote.Text = board.SummaryText;
            _stTip.SetToolTip(_lblReplenishNote, board.SummaryText + "\n补货点 ROP = 日均消耗 × 采购提前期 + 安全库存");
        }
    }

    private void ReplenishGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _gridReplenish is null) return;
        if (_gridReplenish.Rows[e.RowIndex].DataBoundItem is not ReplenishmentRow row) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _gridReplenish.Columns.Count) return;

        var name = _gridReplenish.Columns[e.ColumnIndex].Name;
        if (name is "Status" or "SuggestText" or "DateText")
        {
            e.CellStyle.ForeColor = row.Status switch
            {
                ReplenishStatus.Urgent => StBad,
                ReplenishStatus.Need => StWarn,
                ReplenishStatus.Idle => StMuted,
                _ => StOk
            };
        }
    }

    private void ReplenishGrid_CellToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || _gridReplenish is null) return;
        if (_gridReplenish.Rows[e.RowIndex].DataBoundItem is not ReplenishmentRow row) return;
        e.ToolTipText = $"{row.MaterialName}（{row.MaterialCode}）{row.Spec}\n"
                      + $"日均消耗 {row.DailyText}｜提前期 {row.LeadDays} 天｜安全库存 {row.SafetyText}\n"
                      + $"可用 {row.QtyText} + 在途 {row.InTransit:0.##} + 草稿 {row.DraftQty:0.##} = 净库存 {row.NetText}\n"
                      + $"补货点 ROP {row.RopText}｜{row.VerdictText}";
    }

    private void CreateReplenishment()
    {
        var op = _txtReplenishOperator?.Text?.Trim() ?? "";
        if (op.Length == 0) { Warn("请填写操作人。"); return; }

        var answer = MessageBox.Show(
            "将按补货建议生成**一张**采购申请（草稿，无工单来源）：\n"
            + "· 只包含「紧急 / 需补货」且当前没有未完成申请单的物料\n"
            + "· 建议量已按 MPQ 取整、不超单次采购限额\n\n确认生成？",
            "按建议生成补货申请", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        var (ok, msg, _) = MesDatabase.CreateReplenishmentRequest(op, "安全库存补货（页面执行）");
        MessageBox.Show(msg, ok ? "生成结果" : "未生成申请",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        if (ok) LoadReplenishment();   // 生成后草稿计入净库存，必须重算
    }
}
