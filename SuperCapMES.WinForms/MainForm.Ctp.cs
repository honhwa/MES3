using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类：交期承诺（CTP）+ 换型矩阵页（技术方案第 4 章追加项）。
///
/// 子页一「交期承诺」：输入型号 / 数量 / 期望交期 → 三条线分别算"最早哪天能交" → 给出三档承诺结论。
///   结论不是拍脑袋：换型、排队、生产、物料四段逐段摊开（依据区可直接看）。
///   整单赶不上时不直接拒绝，而是算"期望交期前能交多少"，给出有条件承诺（部分交付）。
/// 子页二「换型矩阵」：型号 × 型号的换型分钟数热力表 + 一天多型号生产的最省换型顺序。
/// </summary>
public partial class MainForm
{
    private static readonly Color CtpInk = Color.FromArgb(30, 41, 59);
    private static readonly Color CtpMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color CtpOk = Color.FromArgb(21, 128, 61);
    private static readonly Color CtpWarn = Color.FromArgb(180, 83, 9);
    private static readonly Color CtpBad = Color.FromArgb(185, 28, 28);

    private ComboBox _cboCtpModel = null!;
    private NumericUpDown _numCtpQty = null!;
    private DateTimePicker _dtpCtpExpected = null!;
    private Label _lblCtpVerdict = null!;
    private Label _lblCtpSummary = null!;
    private DataGridView _gridCtp = null!;
    private TextBox _txtCtpEvidence = null!;
    private DataGridView _gridChangeover = null!;
    private TextBox _txtChangeoverSeq = null!;
    private Label _lblChangeoverNote = null!;

    private readonly ToolTip _ctpTip = new();
    private CtpResult? _ctpResult;

    private TabPage BuildCtpTab()
    {
        var page = new TabPage("交期承诺 CTP");
        page.Padding = new Padding(6);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildCtpQueryPage());
        tabs.TabPages.Add(BuildChangeoverPage());
        page.Controls.Add(tabs);

        page.Enter -= CtpTab_Enter;
        page.Enter += CtpTab_Enter;

        return page;
    }

    // ================= 子页一：交期承诺 =================

    private TabPage BuildCtpQueryPage()
    {
        var tab = new TabPage("交期承诺");
        tab.Padding = new Padding(4);

        // ---- 询单输入条 ----
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(4, 8, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "询单型号：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboCtpModel = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
            Margin = new Padding(0, 4, 12, 0)
        };
        bar.Controls.Add(_cboCtpModel);

        bar.Controls.Add(new Label { Text = "数量：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _numCtpQty = new NumericUpDown
        {
            Width = 112,
            Minimum = 1,
            Maximum = 1000000,
            Increment = 500,
            Value = 5000,
            Margin = new Padding(0, 4, 12, 0)
        };
        bar.Controls.Add(_numCtpQty);

        bar.Controls.Add(new Label { Text = "期望交期：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _dtpCtpExpected = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Width = 130,
            Value = DateTime.Today.AddDays(14),
            Margin = new Padding(0, 4, 12, 0)
        };
        bar.Controls.Add(_dtpCtpExpected);

        var btn = MakeToolbarButton("⚡ 评估交期", (_, _) => RunCtp());
        btn.Width = 120;
        bar.Controls.Add(btn);

        var note = new Label
        {
            AutoSize = false,
            Width = 520,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = CtpMuted,
            Margin = new Padding(10, 8, 0, 0),
            Text = "可交日 = max(排队+换型+生产, 物料到料+生产)；整单赶不上会给「有条件承诺」的部分交付建议。"
        };
        _ctpTip.SetToolTip(note, "可交日 = max(排队 + 换型 + 生产, 物料到料 + 生产)。\n"
            + "注意不是「完工或到料取晚」——料到了还要再生产一轮。\n"
            + "整单赶不上时，会算期望交期前最多能交多少，够 30% 就给有条件承诺（部分交付）。");
        bar.Controls.Add(note);

        // ---- 结论区 ----
        var verdict = new Panel
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 6, 12, 6)
        };
        _lblCtpVerdict = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            AutoSize = false,
            Font = new Font("微软雅黑", 15F, FontStyle.Bold),
            ForeColor = CtpMuted,
            Text = "等待评估"
        };
        _lblCtpSummary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = CtpInk,
            Text = "选择型号与数量后点「⚡ 评估交期」。系统按换型 / 排队 / 生产 / 物料四段逐段推算，结论分可承诺、有条件承诺、不可承诺三档。"
        };
        verdict.Controls.Add(_lblCtpSummary);
        verdict.Controls.Add(_lblCtpVerdict);

        // ---- 依据区 ----
        _txtCtpEvidence = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 104,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = new Font("微软雅黑", 9F),
            Text = "选中一条线体，这里显示它逐段的推算依据（换型 / 排队 / 生产 / 物料 / 最早可交）。"
        };

        var gridGroup = MakeGridGroup(
            "线体对比（同一询单在各线的可交日与瓶颈；选中行看下方推算依据）",
            out _gridCtp);
        SetupCtpColumns();
        _gridCtp.SelectionChanged -= CtpGrid_SelectionChanged;
        _gridCtp.SelectionChanged += CtpGrid_SelectionChanged;
        _gridCtp.CellFormatting += CtpGrid_CellFormatting;
        _gridCtp.CellToolTipTextNeeded += CtpGrid_CellToolTip;

        tab.Controls.Add(gridGroup);   // Fill
        tab.Controls.Add(verdict);     // Top（后加 → 更靠上）
        tab.Controls.Add(bar);         // Top
        tab.Controls.Add(_txtCtpEvidence);   // Bottom
        return tab;
    }

    private void SetupCtpColumns()
    {
        _gridCtp.AutoGenerateColumns = false;
        _gridCtp.Columns.Clear();
        _gridCtp.Columns.Add(MakeTextCol("Line", "线体", true, 6));
        _gridCtp.Columns.Add(MakeTextCol("FromModel", "在制型号", true, 10));
        _gridCtp.Columns.Add(MakeTextCol("ChangeoverText", "换型", true, 10));
        _gridCtp.Columns.Add(MakeTextCol("LineFreeText", "排队腾出", true, 10));
        _gridCtp.Columns.Add(MakeTextCol("StartText", "计划开工", true, 10));
        _gridCtp.Columns.Add(MakeTextCol("MaterialReadyText", "物料就绪", true, 8));
        _gridCtp.Columns.Add(MakeTextCol("EarliestText", "最早可交", true, 10));
        _gridCtp.Columns.Add(MakeTextCol("MeetText", "vs 期望", true, 8));
        _gridCtp.Columns.Add(MakeTextCol("PromiseText", "期望前可交", true, 9));
        _gridCtp.Columns.Add(MakeTextCol("Bottleneck", "瓶颈", true, 8));
        _gridCtp.Columns.Add(MakeTextCol("BottleneckText", "瓶颈说明", true, 30));
        _gridCtp.Columns.Add(MakeTextCol("ScoreText", "评分", true, 6));
        _gridCtp.Columns.Add(MakeTextCol("FeasibleText", "可分配", true, 8));
    }

    private void CtpTab_Enter(object? sender, EventArgs e)
    {
        if (_cboCtpModel is null) return;

        var keep = _cboCtpModel.SelectedItem as string;
        _cboCtpModel.Items.Clear();
        foreach (var m in MesDatabase.GetProductModels()) _cboCtpModel.Items.Add(m);
        if (_cboCtpModel.Items.Count > 0)
        {
            var idx = keep is null ? 0 : Math.Max(0, _cboCtpModel.Items.IndexOf(keep));
            _cboCtpModel.SelectedIndex = idx;
        }

        if (_ctpResult is null) RunCtp();          // 首次进入给一份默认评估，避免空白页
        LoadChangeoverMatrix();
    }

    private void RunCtp()
    {
        var model = _cboCtpModel?.SelectedItem as string ?? "";
        if (model.Length == 0) { Warn("请先选择询单型号。"); return; }

        var qty = (int)(_numCtpQty?.Value ?? 0);
        if (qty <= 0) { Warn("询单数量必须大于 0。"); return; }

        var expected = _dtpCtpExpected?.Value.Date ?? DateTime.Today.AddDays(14);
        var res = MesDatabase.EvaluateCtp(model, qty, expected);
        _ctpResult = res;

        // 结论区
        if (_lblCtpVerdict is not null)
        {
            var dateLabel = res.Verdict == CtpVerdict.Reject ? "最早可交" : "承诺日";
            _lblCtpVerdict.Text = $"{res.Verdict}"
                + (res.CommitDate == default ? "" : $"　{dateLabel} {res.CommitDateText}")
                + (res.Verdict == CtpVerdict.Reject ? "" : $"　承诺量 {res.CommitQtyText}")
                + (res.BestOption is null ? "" : $"　推荐线 {res.BestLine}");
            _lblCtpVerdict.ForeColor = res.Verdict switch
            {
                CtpVerdict.Commit => CtpOk,
                CtpVerdict.Conditional => CtpWarn,
                _ => CtpBad
            };
        }
        if (_lblCtpSummary is not null)
        {
            _lblCtpSummary.Text = res.SummaryText
                + $"\n投产 {res.InputText}｜需产能 {res.HoursText}｜单位工时 {res.UnitHours:0.00000} h/只"
                + (res.BestOption is null ? "" : $"｜瓶颈：{res.BestOption.BottleneckText}");
        }

        _gridCtp.DataSource = null;
        _gridCtp.DataSource = res.Options;

        _txtCtpEvidence.Text = res.Options.Count == 0
            ? "没有可用线体。"
            : "【承诺推算依据】\n" + string.Join("\n", res.Reasons.Select((r, i) => $"{i + 1}. {r}"));

        if (res.Options.Count > 0)
        {
            _gridCtp.Rows[0].Selected = true;
            ShowCtpEvidence(res.Options[0]);
        }
    }

    private void CtpGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_gridCtp?.CurrentRow?.DataBoundItem is CtpLineOption opt) ShowCtpEvidence(opt);
    }

    private void ShowCtpEvidence(CtpLineOption opt)
    {
        if (_txtCtpEvidence is null) return;
        var lines = new List<string> { $"【{opt.Line} 线推算依据】" };
        lines.AddRange(opt.Evidence.Select((x, i) => $"{i + 1}. {x}"));
        if (!opt.Feasible) lines.Add($"⚠ 不可分配：{opt.BlockReason}");
        if (opt.ShortageCount > 0) lines.Add($"⚠ 物料：{opt.MaterialText}");
        _txtCtpEvidence.Text = string.Join("\r\n", lines);
    }

    /// <summary>列宽有限，瓶颈说明/物料状况用悬停补全。</summary>
    private void CtpGrid_CellToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || _gridCtp is null) return;
        if (_gridCtp.Rows[e.RowIndex].DataBoundItem is not CtpLineOption opt) return;
        e.ToolTipText = $"{opt.Line} 线｜在制 {opt.FromModel}｜换型 {opt.Changeover.MinutesText}\n"
                      + $"物料：{opt.MaterialText}\n瓶颈：{opt.BottleneckText}\n"
                      + $"最早可交 {opt.EarliestText}（{opt.MeetText}）｜期望前可交 {opt.PromiseText}"
                      + (opt.Feasible ? "" : $"\n不可分配：{opt.BlockReason}");
    }

    private void CtpGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _gridCtp is null) return;
        if (_gridCtp.Rows[e.RowIndex].DataBoundItem is not CtpLineOption opt) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _gridCtp.Columns.Count) return;

        var name = _gridCtp.Columns[e.ColumnIndex].Name;
        if (name is "MeetText" or "EarliestText")
        {
            if (!opt.Feasible) return;
            e.CellStyle.ForeColor = opt.MeetExpected ? CtpOk : CtpBad;
        }
        else if (name == "FeasibleText")
        {
            e.CellStyle.ForeColor = opt.Feasible ? CtpOk : CtpBad;
        }
        else if (name == "Bottleneck")
        {
            e.CellStyle.ForeColor = opt.Bottleneck switch
            {
                CtpBottleneck.Material => CtpBad,
                CtpBottleneck.Changeover => CtpWarn,
                CtpBottleneck.Capacity => CtpWarn,
                _ => CtpOk
            };
        }
    }

    // ================= 子页二：换型矩阵 =================

    private TabPage BuildChangeoverPage()
    {
        var tab = new TabPage("换型矩阵");
        tab.Padding = new Padding(4);

        _lblChangeoverNote = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            AutoSize = false,
            ForeColor = CtpMuted,
            Padding = new Padding(4, 4, 4, 0),
            Text = "行 = 从（当前在制型号），列 = 到（下一个型号），值 = 换型所需分钟数。"
                 + "换型时间 = 清线 15 + 换模具 25（封装差异）+ 管路清洗 40（电解液体系差异）+ 换辊/切刀 25（极片宽度差异）+ 换化成工装 20（容量段差异）。"
        };

        var seqBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Padding = new Padding(4, 6, 4, 0),
            WrapContents = false
        };
        var btn = MakeToolbarButton("🧮 生成最省换型顺序", (_, _) => RunChangeoverSequence());
        btn.Width = 168;
        seqBar.Controls.Add(btn);
        seqBar.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 700,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = CtpMuted,
            Margin = new Padding(10, 7, 0, 0),
            Text = "把当天要做的型号都排一遍，用最近邻贪心找换型最短的顺序（不是最优解，但比按接单顺序做稳定更好）。"
        });

        _txtChangeoverSeq = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = new Font("微软雅黑", 9F),
            Text = "点「🧮 生成最省换型顺序」，这里给出型号顺序、每步换型时间与节省的分钟数。"
        };

        var gridGroup = MakeGridGroup("换型矩阵（分钟；深色 = 换型代价高，同型号为 0）", out _gridChangeover);
        _gridChangeover.AutoGenerateColumns = false;
        _gridChangeover.CellFormatting += ChangeoverGrid_CellFormatting;

        tab.Controls.Add(gridGroup);          // Fill
        tab.Controls.Add(_lblChangeoverNote); // Top
        tab.Controls.Add(_txtChangeoverSeq);  // Bottom
        tab.Controls.Add(seqBar);             // Bottom（后加 → 更靠上）
        return tab;
    }

    /// <summary>填充换型矩阵（动态列：行=从、列=到）。</summary>
    private void LoadChangeoverMatrix()
    {
        if (_gridChangeover is null) return;
        var matrix = MesDatabase.GetChangeoverMatrix();

        _gridChangeover.DataSource = null;
        _gridChangeover.Rows.Clear();
        _gridChangeover.Columns.Clear();
        _gridChangeover.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "From",
            HeaderText = "从 \\ 到",
            ReadOnly = true,
            FillWeight = 22
        });
        foreach (var m in matrix.Models)
            _gridChangeover.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = m,
                HeaderText = m,
                ReadOnly = true,
                FillWeight = 26
            });

        _gridChangeover.AllowUserToAddRows = false;
        foreach (var from in matrix.Models)
        {
            var idx = _gridChangeover.Rows.Add();
            var row = _gridChangeover.Rows[idx];
            row.Cells[0].Value = from;
            for (var j = 0; j < matrix.Models.Count; j++)
            {
                var to = matrix.Models[j];
                var co = matrix.Get(from, to);
                row.Cells[j + 1].Value = co is null ? "—" : (co.SameModel ? "0（同型号）" : $"{co.Minutes}");
                row.Cells[j + 1].Tag = co;
            }
        }

        if (_lblChangeoverNote is not null)
        {
            _lblChangeoverNote.Text = $"{matrix.Models.Count} 个型号 → 矩阵 {matrix.Models.Count}×{matrix.Models.Count}，"
                + $"平均换型 {matrix.AvgMinutes:0.#} 分钟、最长 {matrix.MaxMinutes} 分钟。"
                + "行 = 从（当前在制型号），列 = 到（下一个型号），值 = 换型所需分钟数。\n"
                + "换型时间 = 清线 15 + 换模具 25（封装差异）+ 管路清洗 40（电解液体系差异）+ 换辊/切刀 25（极片宽度差异）+ 换化成工装 20（容量段差异）。";
        }
    }

    private void ChangeoverGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _gridChangeover is null || e.ColumnIndex <= 0) return;
        if (_gridChangeover.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is not ChangeoverInfo co) return;

        if (co.SameModel)
        {
            e.CellStyle.BackColor = Color.FromArgb(240, 242, 245);
            e.CellStyle.ForeColor = CtpMuted;
            return;
        }
        e.CellStyle.ForeColor = Color.White;
        e.CellStyle.BackColor = co.Minutes >= 80 ? Color.FromArgb(185, 28, 28)
            : co.Minutes >= 60 ? Color.FromArgb(217, 119, 6)
            : co.Minutes >= 40 ? Color.FromArgb(146, 116, 30)
            : Color.FromArgb(21, 128, 61);
    }

    private void RunChangeoverSequence()
    {
        var models = MesDatabase.GetProductModels();
        var seq = MesDatabase.BuildChangeoverSequence(models);
        _txtChangeoverSeq.Text =
            $"【最省换型顺序】{seq.OrderedText}\r\n"
            + $"总换型 {seq.TotalMinutes} 分钟｜按原顺序（{string.Join(" → ", models)}）为 {seq.NaiveMinutes} 分钟 → {seq.SaveText}\r\n"
            + "————————\r\n"
            + string.Join("\r\n", seq.Steps.Select((s, i) => $"{i + 1}. {s}"));
    }
}
