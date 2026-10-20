using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类：物料全链管控页（技术方案 3.2 临期三色 + 3.3 三方案决策）。
///
/// 这一页要回答的不是"哪些料快过期"（那是效期预警已经做的事），而是：
///   **快过期的料，现在到底该怎么办、花多少钱、谁有权批、做完留什么痕。**
///   ① 临期三色：剩余效期 + 按实测消耗速度推算的「预计用完日」→ 进度条式判断会不会报废
///   ② 三方案  ：提前排产消耗 / IQC 重检延期 / 报废处置，逐条给可行性、代价、评分、推荐
///   ③ 分级审批：金额 ≤500 班组长、500~5000 仓储主管、>5000 生产经理——级别不够后端直接拒
///   ④ 处置留痕：每次决策落一条台账，关联废弃单 / 重检单真实单据号
///
/// 所有判定在数据层（`MesDatabase.MaterialAge.cs`），页面只负责呈现、配色与交互。
/// </summary>
public partial class MainForm
{
    private static readonly Color AgeInk = Color.FromArgb(30, 41, 59);
    private static readonly Color AgeMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color AgeWarn = Color.FromArgb(180, 83, 9);
    private static readonly Color AgeBad = Color.FromArgb(185, 28, 28);
    private static readonly Font AgeBoldFont = new("微软雅黑", 9F, FontStyle.Bold);

    private readonly ToolTip _ageTip = new();

    private ComboBox _cboAgeFilter = null!;
    private ComboBox _cboAgeRole = null!;
    private Label _lblAgeNote = null!;
    private DataGridView _gridAge = null!;
    private DataGridView _gridAgeDecisions = null!;
    private FlowLayoutPanel _ageCards = null!;
    private Label _lblAgeVerdict = null!;
    private Button _btnAgeExecute = null!;
    private TextBox _txtAgeOperator = null!;

    private Label? _cardAgeBatch, _cardAgeNear, _cardAgeExpired, _cardAgeFallback, _cardAgeRisk;
    private MaterialAgeBoard? _ageBoard;
    private AgeSimulation? _ageSim;
    private AgePlanOption? _ageSelected;

    private bool _ageLoading = true;

    private TabPage BuildMaterialAgeTab()
    {
        var page = new TabPage("物料全链管控");
        page.Padding = new Padding(6);

        // ---- 工具条 ----
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(6, 8, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "显示：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboAgeFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 130,
            Margin = new Padding(0, 4, 10, 0)
        };
        foreach (var f in AgeFilters.AllItems) _cboAgeFilter.Items.Add(f);
        _cboAgeFilter.SelectedIndex = 0;
        _cboAgeFilter.SelectedIndexChanged += (_, _) => { if (!_ageLoading) RebuildAgeBoard(); };
        bar.Controls.Add(_cboAgeFilter);

        bar.Controls.Add(new Label { Text = "审批角色：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboAgeRole = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110,
            Margin = new Padding(0, 4, 10, 0)
        };
        foreach (var r in AgeApproval.All) _cboAgeRole.Items.Add(r);
        _cboAgeRole.SelectedIndex = 0;   // 默认班组长：金额超限会被拦下，正好演示分级审批
        _cboAgeRole.SelectedIndexChanged += (_, _) => { if (!_ageLoading) RenderAgeSim(); };
        bar.Controls.Add(_cboAgeRole);

        var btnRefresh = MakeToolbarButton("🔄 重算", (_, _) => RebuildAgeBoard());
        btnRefresh.Width = 84;
        bar.Controls.Add(btnRefresh);

        _lblAgeNote = new Label
        {
            AutoSize = false,
            Width = 560,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = AgeMuted,
            Margin = new Padding(10, 8, 0, 0),
            Text = "三色同时看「剩余效期」和「按实测速度能否在到期前用完」。"
        };
        _ageTip.SetToolTip(_lblAgeNote, "三色同时看「剩余效期」和「按实测速度能否在到期前用完」。");
        bar.Controls.Add(_lblAgeNote);

        // ---- 指标卡 ----
        var cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 104,
            Padding = new Padding(6, 4, 6, 4),
            WrapContents = false,
            AutoScroll = true
        };
        cards.Controls.Add(MakeCard("库存批次", Color.FromArgb(15, 76, 129), out var c1));
        cards.Controls.Add(MakeCard("临期批次", AgeWarn, out var c2));
        cards.Controls.Add(MakeCard("已过期批次", AgeBad, out var c3));
        cards.Controls.Add(MakeCard("超兜底期限", Color.FromArgb(127, 29, 29), out var c4));
        cards.Controls.Add(MakeCard("风险金额（元）", Color.FromArgb(15, 76, 129), out var c5));
        _cardAgeBatch = c1; _cardAgeNear = c2; _cardAgeExpired = c3; _cardAgeFallback = c4; _cardAgeRisk = c5;

        // ---- 执行条（页面底部，跨两个子页共用） ----
        var execBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.FromArgb(248, 250, 252)
        };
        execBar.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _txtAgeOperator = new TextBox { Width = 110, Margin = new Padding(0, 3, 12, 0), Text = "计划员" };
        execBar.Controls.Add(_txtAgeOperator);

        _btnAgeExecute = MakeToolbarButton("✔ 执行选中方案", (_, _) => ExecuteAgeDecisionUi());
        _btnAgeExecute.Width = 148;
        _btnAgeExecute.Enabled = false;
        execBar.Controls.Add(_btnAgeExecute);

        _lblAgeExecHint = new Label
        {
            AutoSize = false,
            Width = 700,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = AgeMuted,
            Margin = new Padding(12, 8, 0, 0),
            Text = "先在下方选一个方案，再点「执行」；报废与重检会写既有单据台账，不可撤销。"
        };
        _ageTip.SetToolTip(_lblAgeExecHint, AgeApproval.RuleText);
        execBar.Controls.Add(_lblAgeExecHint);

        // ---- 主体 ----
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
            var want = (int)(h * 0.46);
            if (want > 110 && h - want - split.SplitterWidth > 150) split.SplitterDistance = want;
        };

        var topGroup = MakeGridGroup(
            "临期三色看板（按批次：剩余效期 + 按实测速度推算的预计用完日 + 预计报废量与金额）",
            out _gridAge);
        SetupAgeColumns();
        _gridAge.SelectionChanged -= AgeGrid_SelectionChanged;
        _gridAge.SelectionChanged += AgeGrid_SelectionChanged;
        _gridAge.CellFormatting += AgeGrid_CellFormatting;
        _gridAge.CellToolTipTextNeeded += AgeGrid_CellToolTip;
        split.Panel1.Controls.Add(topGroup);

        // 下部：方案评估 / 处置留痕
        var subTabs = new TabControl { Dock = DockStyle.Fill };

        var planPage = new TabPage("三方案评估");
        _ageCards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 196,
            Padding = new Padding(4, 4, 4, 2),
            WrapContents = true,
            AutoScroll = true
        };
        _lblAgeVerdict = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = AgeInk,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 6, 10, 6),
            Text = "选中上方任一临期/过期批次，这里显示三条处置方案的代价、审批级别与推荐。"
        };
        planPage.Controls.Add(_lblAgeVerdict);
        planPage.Controls.Add(_ageCards);
        subTabs.TabPages.Add(planPage);

        var logPage = new TabPage("处置留痕");
        var logGroup = MakeGridGroup(
            "处置决策台账（每条记录关联废弃单 / 重检单 / 优先消耗指令号，只追加不改写）",
            out _gridAgeDecisions);
        SetupAgeDecisionColumns();
        logPage.Controls.Add(logGroup);
        subTabs.TabPages.Add(logPage);

        split.Panel2.Controls.Add(subTabs);

        // ---- 脚注 ----
        var foot = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 82,
            ForeColor = Color.Gray,
            Padding = new Padding(6, 6, 6, 0),
            Text = "口径：剩余效期 = 到期日 − 今天；预计用完日 = 今天 + 在库量 ÷ 日均消耗。日均消耗优先取近 90 天已过账领用实测，"
                 + "样本不足改用未完工工单 BOM 需求 ÷ 30 天，两者都没有标「不可估」、不做预测。\n"
                 + AgeApproval.RuleText + "。\n"
                 + AgeRules.RuleText + "单价与阈值为演示参数；报废走既有废弃台账（不可逆），重检走既有进料检台账。"
        };

        page.Controls.Add(split);       // Fill
        page.Controls.Add(cards);       // Top
        page.Controls.Add(bar);         // Top（后加 → 更靠上）
        page.Controls.Add(execBar);     // Bottom
        page.Controls.Add(foot);        // Bottom

        page.Enter -= AgeTab_Enter;
        page.Enter += AgeTab_Enter;

        _ageLoading = false;
        return page;
    }

    private Label _lblAgeExecHint = null!;

    private void SetupAgeColumns()
    {
        _gridAge.AutoGenerateColumns = false;
        _gridAge.Columns.Clear();
        _gridAge.Columns.Add(MakeTextCol("AgeLevel", "等级", true, 7));
        _gridAge.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 10));
        _gridAge.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 11));
        _gridAge.Columns.Add(MakeTextCol("LotNo", "批次", true, 11));
        _gridAge.Columns.Add(MakeTextCol("Location", "库位", true, 9));
        _gridAge.Columns.Add(MakeTextCol("QtyText", "在库量", true, 9));
        _gridAge.Columns.Add(MakeTextCol("ExpiryText", "到期日", true, 8));
        _gridAge.Columns.Add(MakeTextCol("RemainingText", "剩余效期", true, 9));
        _gridAge.Columns.Add(MakeTextCol("DailyText", "日均消耗（来源）", true, 12));
        _gridAge.Columns.Add(MakeTextCol("RunOutText", "预计用完", true, 8));
        _gridAge.Columns.Add(MakeTextCol("WasteText", "到期前用不完", true, 14));
        _gridAge.Columns.Add(MakeTextCol("AmountText", "在库金额", true, 10));
        _gridAge.Columns.Add(MakeTextCol("VerdictBrief", "判定说明", true, 22));
    }

    private void SetupAgeDecisionColumns()
    {
        _gridAgeDecisions.AutoGenerateColumns = false;
        _gridAgeDecisions.Columns.Clear();
        _gridAgeDecisions.Columns.Add(MakeTextCol("DecisionNo", "决策号", true, 12));
        _gridAgeDecisions.Columns.Add(MakeTextCol("DecidedAtText", "决策时间", true, 12));
        _gridAgeDecisions.Columns.Add(MakeTextCol("MaterialName", "物料", true, 11));
        _gridAgeDecisions.Columns.Add(MakeTextCol("LotNo", "批次", true, 11));
        _gridAgeDecisions.Columns.Add(MakeTextCol("Plan", "方案", true, 11));
        _gridAgeDecisions.Columns.Add(MakeTextCol("AmountText", "金额", true, 9));
        _gridAgeDecisions.Columns.Add(MakeTextCol("ApprovalLevel", "审批级别", true, 9));
        _gridAgeDecisions.Columns.Add(MakeTextCol("Approver", "操作人", true, 8));
        _gridAgeDecisions.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _gridAgeDecisions.Columns.Add(MakeTextCol("RefNo", "关联单据", true, 13));
    }

    // ================= 刷新 =================

    private void AgeTab_Enter(object? sender, EventArgs e)
    {
        RebuildAgeBoard();
        LoadAgeDecisions();
    }

    /// <summary>重算看板并刷新界面（纯查询）。</summary>
    private void RebuildAgeBoard()
    {
        if (_gridAge is null) return;

        var filter = _cboAgeFilter?.SelectedItem as string ?? AgeFilters.NearExpired;
        var board = MesDatabase.BuildMaterialAgeBoard(filter);
        _ageBoard = board;

        _gridAge.DataSource = null;
        _gridAge.DataSource = board.Rows;

        // 卡片用**全量**统计，不随显示范围变化（否则切一下过滤，风险金额就变了，容易误解）
        var all = MesDatabase.BuildMaterialAgeBoard(AgeFilters.All);
        if (_cardAgeBatch is not null) _cardAgeBatch.Text = all.BatchCount.ToString();
        if (_cardAgeNear is not null) _cardAgeNear.Text = all.NearCount.ToString();
        if (_cardAgeExpired is not null) _cardAgeExpired.Text = all.ExpiredCount.ToString();
        if (_cardAgeFallback is not null) _cardAgeFallback.Text = all.FallbackCount.ToString();
        if (_cardAgeRisk is not null) _cardAgeRisk.Text = all.RiskAmount.ToString("0.#");

        if (_lblAgeNote is not null)
        {
            _lblAgeNote.Text = board.SummaryText;
            _ageTip.SetToolTip(_lblAgeNote, board.SummaryText + "\n" + AgeRules.RuleText);
        }

        _ageSim = null;
        _ageSelected = null;
        if (_ageCards is not null) _ageCards.Controls.Clear();
        if (_btnAgeExecute is not null) _btnAgeExecute.Enabled = false;

        if (board.Rows.Count > 0)
        {
            _gridAge.Rows[0].Selected = true;
            SelectAgeRow(board.Rows[0]);
        }
        else if (_lblAgeVerdict is not null)
        {
            _lblAgeVerdict.Text = $"当前范围（{filter}）没有需要处置的批次。";
        }
    }

    private void AgeGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_gridAge?.CurrentRow?.DataBoundItem is MaterialAgeRow row) SelectAgeRow(row);
    }

    /// <summary>选中一个批次 → 模拟三方案 → 渲染卡片。</summary>
    private void SelectAgeRow(MaterialAgeRow row)
    {
        _ageSim = MesDatabase.SimulateAgePlans(row.MaterialCode, row.LotNo, row.Location);
        _ageSelected = _ageSim?.Options.FirstOrDefault(o => o.Recommended) ?? _ageSim?.Options.FirstOrDefault(o => o.Feasible);
        RenderAgeSim();
    }

    /// <summary>渲染三张方案卡片 + 结论行 + 执行按钮状态。</summary>
    private void RenderAgeSim()
    {
        if (_ageCards is null) return;
        _ageCards.Controls.Clear();

        var sim = _ageSim;
        if (sim?.Row is null)
        {
            if (_lblAgeVerdict is not null)
                _lblAgeVerdict.Text = "选中上方任一临期/过期批次，这里显示三条处置方案的代价、审批级别与推荐。";
            if (_btnAgeExecute is not null) _btnAgeExecute.Enabled = false;
            return;
        }

        var role = _cboAgeRole?.SelectedItem as string ?? AgeApproval.Team;

        foreach (var plan in sim.Options)
        {
            bool isSelected = _ageSelected is not null && plan.Id == _ageSelected.Id;
            bool approved = plan.Amount <= 1e-9 || AgeApproval.CanApprove(role, plan.Amount);
            var card = new Panel
            {
                Width = 404,
                Height = 168,
                Margin = new Padding(0, 0, 10, 8),
                BackColor = isSelected ? Color.FromArgb(232, 242, 255) : Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(9),
                Cursor = plan.Feasible ? Cursors.Hand : Cursors.Default,
                Tag = plan
            };
            var title = plan.Name + (plan.Recommended ? "　⭐推荐" : "") + (plan.Feasible ? "" : "　（不可行）");
            var lbl = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = new Font("微软雅黑", 9F),
                ForeColor = plan.Feasible ? AgeInk : AgeMuted,
                Text = $"{title}\n"
                     + $"综合评分：{plan.ScoreText}　{(plan.Amount > 1e-9 ? $"金额 {plan.AmountText}" : "金额 —（无需资产审批）")}\n"
                     + $"审批：{(plan.Amount > 1e-9 ? plan.RequiredLevel + (approved ? "" : "（级别不足）") : AgeApproval.Team + "（登记即可）")}\n"
                     + $"{(plan.Feasible ? plan.EffectText : plan.BlockReason)}\n"
                     + (plan.Pros.Count > 0 ? "✔ " + plan.Pros[0] : "")
                     + (plan.Cons.Count > 0 ? "\n✘ " + plan.Cons[0] : "")
            };
            _ageTip.SetToolTip(lbl, plan.Feasible
                ? $"{plan.Name}\n{plan.EffectText}\n金额 {plan.Amount:0.##} 元 → 需「{plan.RequiredLevel}」审批\n"
                  + string.Join("\n", plan.Pros.Select(p => "✔ " + p)) + "\n" + string.Join("\n", plan.Cons.Select(c => "✘ " + c))
                : $"{plan.Name}\n不可执行：{plan.BlockReason}");

            if (plan.Feasible)
            {
                EventHandler click = (_, _) => { _ageSelected = plan; RenderAgeSim(); };
                card.Click += click;
                lbl.Click += click;
            }
            card.Controls.Add(lbl);
            _ageCards.Controls.Add(card);
        }

        var row = sim.Row;
        if (_lblAgeVerdict is not null)
        {
            var sel = _ageSelected;
            _lblAgeVerdict.Text = $"{row.MaterialName}（{row.MaterialCode}）批次 {row.LotNo}｜{row.AgeLevel}｜{row.RemainingText}｜"
                + $"预计用完 {row.RunOutText}｜在库 {row.QtyText}｜金额 {row.AmountText}\n"
                + $"判定：{row.VerdictText}\n"
                + (sel is null
                    ? "三条方案均不可行，请检查领用记录或改用报废。"
                    : $"已选方案：{sel.Name}（评分 {sel.ScoreText}）→ {(sel.Amount > 1e-9 ? AgeApproval.Explain(sel.Amount) : "无需资产审批，登记即生效")}"
                      + (sel.Amount > 1e-9 && !AgeApproval.CanApprove(role, sel.Amount) ? $"　⚠ 当前审批角色「{role}」级别不足，执行会被拒绝。" : ""));
        }

        if (_btnAgeExecute is not null)
        {
            _btnAgeExecute.Enabled = _ageSelected is { Feasible: true };
            _btnAgeExecute.Text = _ageSelected is null ? "✔ 执行选中方案" : $"✔ 执行：{_ageSelected.Name}";
        }
    }

    private void AgeGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _gridAge is null) return;
        if (_gridAge.Rows[e.RowIndex].DataBoundItem is not MaterialAgeRow row) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _gridAge.Columns.Count) return;

        var name = _gridAge.Columns[e.ColumnIndex].Name;
        if (name is "AgeLevel" or "RemainingText")
        {
            if (row.AgeLevel == AgeLevels.Expired)
            {
                e.CellStyle.ForeColor = AgeBad;
                e.CellStyle.Font = AgeBoldFont;
            }
            else if (row.AgeLevel == AgeLevels.Near)
            {
                e.CellStyle.ForeColor = AgeWarn;
                e.CellStyle.Font = AgeBoldFont;
            }
        }
    }

    /// <summary>列宽有限，长文案（完整判定 / 来源 / 缺口）用悬停补全，避免被截断后看不全。</summary>
    private void AgeGrid_CellToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || _gridAge is null) return;
        if (_gridAge.Rows[e.RowIndex].DataBoundItem is not MaterialAgeRow row) return;
        e.ToolTipText = $"{row.MaterialName}（{row.MaterialCode}）批次 {row.LotNo} / {row.Location}\n"
                      + $"在库 {row.QtyText}｜到期日 {row.ExpiryText}（{row.RemainingText}）｜在库金额 {row.AmountText}\n"
                      + $"日均消耗 {row.DailyText}｜预计用完 {row.RunOutText}｜到期前用不完 {row.WasteText}\n"
                      + $"判定：{row.VerdictText}";
    }

    private void LoadAgeDecisions()
    {
        if (_gridAgeDecisions is null) return;
        _gridAgeDecisions.DataSource = null;
        _gridAgeDecisions.DataSource = MesDatabase.GetAgeDecisions(null, 200);
    }

    // ================= 执行 =================

    private void ExecuteAgeDecisionUi()
    {
        var sim = _ageSim;
        var plan = _ageSelected;
        if (sim?.Row is null || plan is null)
        {
            Warn("请先选中一个批次与处置方案。");
            return;
        }

        var row = sim.Row;
        var role = _cboAgeRole?.SelectedItem as string ?? AgeApproval.Team;
        var op = _txtAgeOperator?.Text?.Trim() ?? "";
        if (op.Length == 0)
        {
            Warn("请填写操作人。");
            return;
        }

        var irreversible = plan.Id != AgePlans.Consume;
        var answer = MessageBox.Show(
            $"即将执行处置决策：\n"
            + $"· 批次：{row.MaterialName} / {row.LotNo}（{row.Location}）\n"
            + $"· 方案：{plan.Name}\n"
            + $"· 数量：{row.QtyText}　金额：{(plan.Amount > 1e-9 ? plan.AmountText : "—")}\n"
            + $"· 审批：{(plan.Amount > 1e-9 ? plan.RequiredLevel : AgeApproval.Team + "（登记即可）")}　操作人：{op}\n"
            + (irreversible ? "\n报废 / 重检会写入既有单据台账" + (plan.Id == AgePlans.Scrap ? "，且报废不可撤销" : "") + "。确认执行？" : "\n确认登记优先消耗指令？"),
            "执行处置决策", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (answer != DialogResult.OK) return;

        var (ok, msg, _) = MesDatabase.ExecuteAgeDecision(
            row.MaterialCode, row.LotNo, row.Location, plan.Id, role, op, "临期三方案决策（页面执行）");

        MessageBox.Show(msg, ok ? "执行结果" : "执行被拒绝",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        // 处置后批次状态变了（报废数量归零、重检效期延长），必须重算
        RebuildAgeBoard();
        LoadAgeDecisions();
    }
}
