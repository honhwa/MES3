using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类：缺料三级校验页（技术方案 3.1）。
///
/// 一句话：齐套校验给的是"缺多少"，这一页给的是"什么时候必须下单、建议买多少、买多少会被拦"。
///   ① 一级 需求合并 —— 几工单共用一批料，跨工单累加（各算各的会重复采购）
///   ② 二级 扣减     —— 可用 / 在途 / 冻结 / 安全库存，四个口径分列
///   ③ 三级 净缺口   —— 净缺口 → 建议采购量（损耗 + MPQ 取整 + 限额钳制）
///   → 采购前置期：最晚下单日 = 需求日 − (L/T + 检验缓冲)，三色标记逾期 / 临近 / 充裕
///
/// 这一页只读不写（除了「自动建单」按钮，它会走既有请购流程生成草稿单），
/// 所有判定都在数据层，页面只负责呈现与配色。
/// </summary>
public partial class MainForm
{
    private static readonly Color ChainInk = Color.FromArgb(30, 41, 59);
    private static readonly Color ChainMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color ChainWarn = Color.FromArgb(180, 83, 9);
    private static readonly Color ChainBad = Color.FromArgb(185, 28, 28);
    private static readonly Font ChainBoldFont = new("微软雅黑", 9F, FontStyle.Bold);

    private readonly ToolTip _chainTip = new();
    private ComboBox _cboChainScope = null!;
    private ComboBox _cboChainFilter = null!;
    private Label _lblChainNote = null!;
    private DataGridView _gridChain = null!;
    private DataGridView _gridChainDetail = null!;
    private Label _lblChainAdvice = null!;
    private Label? _cardChainWo, _cardChainMat, _cardChainShort, _cardChainLate, _cardChainNear;
    private ShortageChainResult? _chainResult;

    // 构造期只装下拉、不物化数据（WinForms 构造期绑定的老坑：句柄未建时 Rows 不可靠）
    private bool _chainScopeLoading = true;

    private TabPage BuildShortageChainTab()
    {
        var page = new TabPage("缺料三级校验");
        page.Padding = new Padding(6);

        // ---- 工具条 ----
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(6, 8, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "校验范围：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboChainScope = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 380,
            Margin = new Padding(0, 4, 10, 0),
            DropDownWidth = 560
        };
        _cboChainScope.Format += (_, e) =>
        {
            if (e.ListItem is not string no) return;
            var wo = MesDatabase.WorkOrders.FirstOrDefault(w =>
                string.Equals(w.WoNo, no, StringComparison.OrdinalIgnoreCase));
            if (wo is null) return;
            e.Value = $"{wo.WoNo} ｜ {wo.ProductModel} ｜ 待产 {Math.Max(0, wo.PlanQty - wo.FinishedQty)} 只"
                    + $" ｜ 开工 {wo.StartDate:MM-dd} ｜ 交期 {wo.DueDate:MM-dd}";
        };
        _cboChainScope.SelectedIndexChanged += (_, _) =>
        {
            if (_chainScopeLoading) return;
            RebuildChain();
        };
        bar.Controls.Add(_cboChainScope);

        bar.Controls.Add(new Label { Text = "显示：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboChainFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            Margin = new Padding(0, 4, 10, 0)
        };
        foreach (var f in ChainFilters.AllItems) _cboChainFilter.Items.Add(f);
        _cboChainFilter.SelectedIndex = 1;
        _cboChainFilter.SelectedIndexChanged += (_, _) => RebuildChain();
        bar.Controls.Add(_cboChainFilter);

        var btnRefresh = MakeToolbarButton("🔄 重算", (_, _) => { LoadChainScope(); RebuildChain(); });
        btnRefresh.Width = 84;
        bar.Controls.Add(btnRefresh);

        var btnAuto = MakeToolbarButton("⚡ 按缺口自动建单", (_, _) => AutoCreateFromChain());
        btnAuto.Width = 152;
        bar.Controls.Add(btnAuto);

        _lblChainNote = new Label
        {
            AutoSize = false,
            Width = 430,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = ChainMuted,
            Margin = new Padding(10, 8, 0, 0),
            Text = "三级校验：需求合并 → 扣减 → 净缺口，再按前置期倒推最晚下单日。"
        };
        _chainTip.SetToolTip(_lblChainNote, "三级校验：需求合并 → 扣减（可用/在途/冻结/安全） → 净缺口，再按采购前置期倒推最晚下单日。");
        bar.Controls.Add(_lblChainNote);

        // ---- 指标卡 ----
        var cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 104,
            Padding = new Padding(6, 4, 6, 4),
            WrapContents = false,
            AutoScroll = true
        };
        cards.Controls.Add(MakeCard("参与校验工单", Color.FromArgb(15, 76, 129), out var c1));
        cards.Controls.Add(MakeCard("涉及物料", Color.FromArgb(15, 76, 129), out var c2));
        cards.Controls.Add(MakeCard("短缺项（需采购）", ChainBad, out var c3));
        cards.Controls.Add(MakeCard("已过最晚下单日", Color.FromArgb(185, 28, 28), out var c4));
        cards.Controls.Add(MakeCard("7 天内须下单", ChainWarn, out var c5));
        _cardChainWo = c1; _cardChainMat = c2; _cardChainShort = c3; _cardChainLate = c4; _cardChainNear = c5;

        // ---- 主体：上三级校验表 / 下占用来源 ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = SystemColors.Control
        };
        split.SizeChanged += (_, _) =>
        {
            var h = split.Height;
            if (h <= 0) return;
            var want = (int)(h * 0.62);
            if (want > 90 && h - want - split.SplitterWidth > 90) split.SplitterDistance = want;
        };

        var topGroup = MakeGridGroup(
            "缺料三级校验（① 需求合并 → ② 扣减 → ③ 净缺口＋建议采购量；右侧为采购前置期倒推的最晚下单日）",
            out _gridChain);
        SetupChainColumns();
        _gridChain.SelectionChanged -= ChainGrid_SelectionChanged;
        _gridChain.SelectionChanged += ChainGrid_SelectionChanged;
        _gridChain.CellFormatting += ChainGrid_CellFormatting;
        split.Panel1.Controls.Add(topGroup);

        var bottomGroup = MakeGridGroup(
            "选中物料的占用来源（哪几张工单 / 各占多少 / 生产时间窗 / 占用公式）",
            out _gridChainDetail);
        SetupChainDetailColumns();

        // 选中行的完整结论（表格里放不下长句子，结论单独一行显示，避免被列宽截断）
        _lblChainAdvice = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            AutoSize = false,
            ForeColor = ChainInk,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8, 4, 8, 4),
            Text = "选中上方任一物料，这里显示它的三级校验结论与处理建议。"
        };

        var lower = new Panel { Dock = DockStyle.Fill };
        lower.Controls.Add(bottomGroup);        // Fill 先加
        lower.Controls.Add(_lblChainAdvice);    // Top 后加
        split.Panel2.Controls.Add(lower);

        var foot = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 62,
            ForeColor = Color.Gray,
            Padding = new Padding(6, 6, 6, 0),
            Text = "口径：需求 = 跨工单累加的资材占用（单位用量 ×(1+损耗率)× 剩余待产）；净缺口 = max(0, 需求 − 可用 − 在途)，草稿单不计入在途。\n"
                 + "建议采购量 = (未覆盖缺口 + 安全库存缺口) × 1.10 → MPQ 向上取整 → 按「需求 ×1.2 与单次限额取小」钳制。\n"
                 + "采购前置期（L/T、检验缓冲、MPQ）为演示参数，按物料类别 + 编码稳定推导；真实环境取自 ERP 供应商主数据。本页只读，不写库存、不改单据。"
        };

        page.Controls.Add(split);      // Fill
        page.Controls.Add(cards);      // Top
        page.Controls.Add(bar);        // Top（后加 → 更靠上）
        page.Controls.Add(foot);       // Bottom

        page.Enter -= ChainTab_Enter;
        page.Enter += ChainTab_Enter;

        LoadChainScope();
        return page;
    }

    private void SetupChainColumns()
    {
        _gridChain.AutoGenerateColumns = false;
        _gridChain.Columns.Clear();
        _gridChain.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 10));
        _gridChain.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 11));
        _gridChain.Columns.Add(MakeTextCol("VerifyLevelText", "校验等级", true, 7));
        _gridChain.Columns.Add(MakeTextCol("MergeText", "① 需求合并", true, 14));
        _gridChain.Columns.Add(MakeTextCol("DeductText", "② 扣减（可用/在途/草稿/冻结/安全）", true, 19));
        _gridChain.Columns.Add(MakeTextCol("NetShortageText", "③ 净缺口", true, 9));
        _gridChain.Columns.Add(MakeTextCol("SuggestedText", "建议采购量", true, 16));
        _gridChain.Columns.Add(MakeTextCol("LeadText", "前置期 L/T+检验", true, 9));
        _gridChain.Columns.Add(MakeTextCol("DemandDateText", "需求日", true, 7));
        _gridChain.Columns.Add(MakeTextCol("LatestOrderText", "最晚下单", true, 8));
        _gridChain.Columns.Add(MakeTextCol("TimingStatus", "下单时点", true, 8));
        _gridChain.Columns.Add(MakeTextCol("DaysLeftText", "距下单", true, 9));
    }

    private void SetupChainDetailColumns()
    {
        _gridChainDetail.AutoGenerateColumns = false;
        _gridChainDetail.Columns.Clear();
        _gridChainDetail.Columns.Add(MakeTextCol("WoNo", "工单", true, 12));
        _gridChainDetail.Columns.Add(MakeTextCol("ProductModel", "产品型号", true, 14));
        _gridChainDetail.Columns.Add(MakeTextCol("RemainingQty", "剩余待产(只)", true, 10));
        _gridChainDetail.Columns.Add(MakeTextCol("OccupiedQtyText", "占用", true, 12));
        _gridChainDetail.Columns.Add(MakeTextCol("WindowText", "生产时间窗", true, 22));
        _gridChainDetail.Columns.Add(MakeTextCol("FormulaText", "占用公式", true, 24));
    }

    private void ChainTab_Enter(object? sender, EventArgs e)
    {
        LoadChainScope();
        RebuildChain();
    }

    /// <summary>填充校验范围下拉（全部未完工工单 + 逐张工单）。只装控件，不绑数据。</summary>
    private void LoadChainScope()
    {
        if (_cboChainScope is null) return;
        _chainScopeLoading = true;
        try
        {
            var keep = _cboChainScope.SelectedItem as string;

            _cboChainScope.Items.Clear();
            _cboChainScope.Items.Add("全部未完工工单");
            foreach (var wo in MesDatabase.WorkOrders
                         .Where(MesDatabase.IsOpenWorkOrder)
                         .OrderBy(w => w.StartDate)
                         .ThenBy(w => w.WoNo, StringComparer.Ordinal))
                _cboChainScope.Items.Add(wo.WoNo);

            var idx = string.IsNullOrWhiteSpace(keep) ? 0 : _cboChainScope.Items.IndexOf(keep);
            _cboChainScope.SelectedIndex = idx < 0 ? 0 : idx;
        }
        finally
        {
            _chainScopeLoading = false;
        }
    }

    /// <summary>重算并刷新界面（纯查询，不改数据）。</summary>
    private void RebuildChain()
    {
        if (_gridChain is null || _cboChainFilter is null) return;

        var scopeWo = _cboChainScope?.SelectedItem as string;
        var scopeWoNos = string.IsNullOrWhiteSpace(scopeWo) || scopeWo!.StartsWith("全部", StringComparison.Ordinal)
            ? null
            : new[] { scopeWo! };
        var filter = _cboChainFilter.SelectedItem as string ?? ChainFilters.ShortAndTight;

        var res = MesDatabase.BuildShortageChain(scopeWoNos, filter);
        _chainResult = res;

        _gridChain.DataSource = null;
        _gridChain.DataSource = res.Rows;

        if (_cardChainWo is not null) _cardChainWo.Text = res.WorkOrderCount.ToString();
        if (_cardChainMat is not null) _cardChainMat.Text = res.MaterialCount.ToString();
        if (_cardChainShort is not null) _cardChainShort.Text = res.ShortCount.ToString();
        if (_cardChainLate is not null) _cardChainLate.Text = res.LateCount.ToString();
        if (_cardChainNear is not null) _cardChainNear.Text = res.NearCount.ToString();

        if (_lblChainNote is not null)
        {
            _lblChainNote.Text = res.SummaryText;
            _chainTip.SetToolTip(_lblChainNote, res.SummaryText + "\n" + res.Filter);
        }

        _gridChainDetail.DataSource = null;
        if (res.Rows.Count > 0)
        {
            _gridChain.Rows[0].Selected = true;
            FillChainDetail(res.Rows[0]);
        }
    }

    private void ChainGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_gridChain?.CurrentRow?.DataBoundItem is ShortageChainRow row) FillChainDetail(row);
    }

    private void FillChainDetail(ShortageChainRow row)
    {
        if (_gridChainDetail is null) return;
        _gridChainDetail.DataSource = null;
        _gridChainDetail.DataSource = row.Details;

        if (_lblChainAdvice is not null)
        {
            _lblChainAdvice.Text = $"{row.MaterialName}（{row.MaterialCode}）｜{row.VerifyLevelText}｜{row.TimingStatus}：{row.Advice}";
            _chainTip.SetToolTip(_lblChainAdvice, _lblChainAdvice.Text);
        }
    }

    private void ChainGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _gridChain is null) return;
        if (_gridChain.Rows[e.RowIndex].DataBoundItem is not ShortageChainRow row) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _gridChain.Columns.Count) return;

        var name = _gridChain.Columns[e.ColumnIndex].Name;
        if (name is "VerifyLevelText")
        {
            if (row.VerifyLevel == ShortageStatus.Short)
            {
                e.CellStyle.ForeColor = ChainBad;
                e.CellStyle.Font = ChainBoldFont;
            }
            else if (row.VerifyLevel == ShortageStatus.Tight)
            {
                e.CellStyle.ForeColor = ChainWarn;
            }
        }
        else if (name is "TimingStatus" or "DaysLeftText" or "LatestOrderText")
        {
            if (row.NeedsPurchase && row.TimingStatus == OrderTiming.Late)
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 235, 235);
                e.CellStyle.ForeColor = ChainBad;
                e.CellStyle.Font = ChainBoldFont;
            }
            else if (row.NeedsPurchase && row.TimingStatus == OrderTiming.Near)
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 247, 230);
                e.CellStyle.ForeColor = ChainWarn;
            }
        }
    }

    /// <summary>按当前缺口自动建采购申请（草稿）。走既有请购流程，不另建一套。</summary>
    private void AutoCreateFromChain()
    {
        var preview = _chainResult;
        if (preview is null || preview.ShortCount == 0)
        {
            Warn("当前范围内没有需要采购的短缺项，无需建单。");
            return;
        }

        var answer = MessageBox.Show(
            $"将按缺料清单自动生成采购申请（草稿）：\n"
            + $"· 范围：{preview.ScopeText}，短缺 {preview.ShortCount} 项\n"
            + $"· 按工单开工先后依次建单，最多 3 张（一单一工单，跨工单需求合并、在途不重复申请）\n"
            + $"· 建议量含 {MesDatabase.ChainLossFactor:P0} 损耗与 MPQ 取整，并做数量校验复核（被拦下自动回退净缺口）\n\n"
            + "生成后为草稿，请核对再提交。是否继续？",
            "按缺口自动建单", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        var (ok, msg, _) = MesDatabase.AutoCreateRequestsFromShortage("计划员");
        MessageBox.Show(msg, ok ? "自动建单结果" : "未生成申请",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        // 建单后缺口与在途都变了，必须重算——否则界面还显示旧的短缺清单
        RebuildChain();
    }
}
