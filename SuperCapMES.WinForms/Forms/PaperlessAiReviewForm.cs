using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// AI 语义复核的三色确认页。
///
/// 设计要点（对应方案文档的三条边界）：
/// ① 模型结论只在这里给人看，**点「应用」才改模板**——模型绝不直接落库；
/// ② 每行都能点开看"依据在原文哪一格"（sourceRef 反查并高亮），这是可审计的地基；
/// ③ 完整性差集、坐标越界等提示集中显示，不做静默合并。
/// </summary>
internal sealed class PaperlessAiReviewForm : Form
{
    private readonly ChecklistTemplate _ruleTpl;
    private readonly AiAnalysisResult _result;
    private readonly DataGridView _gridReview = new();
    private readonly DataGridView _gridRaw = new();
    private readonly Label _lblSummary = new();
    private readonly CheckBox _chkApplyHeader = new();
    private readonly ToolTip _tip = new();
    private readonly List<AiReviewRow> _rowModels = new();

    /// <summary>人工确认后的模板（未点应用时为 null）</summary>
    public ChecklistTemplate? Result { get; private set; }

    /// <summary>被采纳的条目数</summary>
    public int AcceptedCount => _rowModels.Count(r => r.TakeAi && r.RuleCol >= 0);

    public PaperlessAiReviewForm(ChecklistTemplate ruleTpl, AiAnalysisResult result)
    {
        _ruleTpl = ruleTpl;
        _result = result;

        Text = "AI 语义复核（三色确认）· 模型只给建议，确认后才改模板";
        Width = 1400;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.WhiteSmoke;
        MinimizeBox = false;

        // ---------- 顶部说明 ----------
        var head = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(10, 8, 10, 4) };
        head.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "绿=模型与规则一致（可直接采纳）｜黄=不一致/置信度偏低（需看一眼）｜红=原文找不到或坐标越界（疑似编造，不采纳）",
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
        });
        head.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Color.DimGray,
            AutoEllipsis = true,
            Text = result.TraceText + (result.FromCache ? "" : ""),
        });

        // ---------- 主体左右分栏 ----------
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
        };

        // 左：三色复核表
        _gridReview.Dock = DockStyle.Fill;
        _gridReview.AllowUserToAddRows = false;
        _gridReview.AllowUserToDeleteRows = false;
        _gridReview.RowHeadersVisible = false;
        _gridReview.BackgroundColor = Color.White;
        _gridReview.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridReview.MultiSelect = false;
        _gridReview.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _gridReview.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        AddCol("State", "状态", 62);
        AddCol("RuleHeader", "规则列名", 140);
        AddCol("AiHeader", "模型列名", 140);
        AddCol("KindChange", "类型（规则→模型）", 150);
        AddCol("Conf", "置信度", 58);
        AddCol("Ref", "定位", 62);
        var colTake = new DataGridViewCheckBoxColumn
        {
            Name = "Take",
            HeaderText = "采纳",
            Width = 46,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        _gridReview.Columns.Add(colTake);
        AddCol("Reason", "说明（依据）", 250);
        _gridReview.SelectionChanged += (s, e) => HighlightSource();
        _gridReview.CellValueChanged += (s, e) =>
        {
            if (e.RowIndex < 0 || _gridReview.Columns[e.ColumnIndex].Name != "Take") return;
            if (e.RowIndex < _rowModels.Count)
                _rowModels[e.RowIndex].TakeAi = Convert.ToBoolean(_gridReview.Rows[e.RowIndex].Cells["Take"].Value ?? false);
            UpdateSummary();
        };
        _gridReview.CurrentCellDirtyStateChanged += (s, e) =>
        {
            // 勾选框要立即提交，否则要等离开单元格才触发 CellValueChanged
            if (_gridReview.IsCurrentCellDirty && _gridReview.CurrentCell is { } c && _gridReview.Columns[c.ColumnIndex].Name == "Take")
                _gridReview.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        var leftHead = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "模型判断 vs 规则判断（点一行看原文依据）",
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
        };
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        left.Controls.Add(_gridReview);
        left.Controls.Add(leftHead);

        // 右：原文表（sourceRef 高亮）
        _gridRaw.Dock = DockStyle.Fill;
        _gridRaw.AllowUserToAddRows = false;
        _gridRaw.AllowUserToDeleteRows = false;
        _gridRaw.ReadOnly = true;
        _gridRaw.BackgroundColor = Color.White;
        _gridRaw.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _gridRaw.RowHeadersWidth = 56;
        _gridRaw.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _gridRaw.SelectionMode = DataGridViewSelectionMode.CellSelect;

        var rightHead = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "原文件表格（高亮格 = 模型给出的依据位置）",
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
        };
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        right.Controls.Add(_gridRaw);
        right.Controls.Add(rightHead);

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        // SplitterDistance 只能在控件有真实尺寸后设，放到 Shown 里（构造期设会被重置）
        Shown += (s, e) =>
        {
            try { if (split.Width > 1000) split.SplitterDistance = 930; } catch { /* 忽略尺寸异常 */ }
        };

        // ---------- 底部：提示 + 操作 ----------
        var notes = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 74,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Text = string.Join("\r\n", result.Notes),
        };
        _lblSummary.Dock = DockStyle.Bottom;
        _lblSummary.Height = 24;
        _lblSummary.Font = new Font("微软雅黑", 9F, FontStyle.Bold);

        _chkApplyHeader.Dock = DockStyle.Bottom;
        _chkApplyHeader.Height = 24;
        _chkApplyHeader.Text = "同时按模型结果替换抬头字段（会覆盖规则已识别的项，请先核对「提示」区差异）";
        // 默认不勾：模型少认几项时自动替换反而会丢数据，必须人工显式选择
        _chkApplyHeader.Checked = false;
        _chkApplyHeader.Enabled = result.Claim is { HeaderFields.Count: > 0 };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        var btnGreen = new Button { Text = "全部采纳绿项", AutoSize = true };
        btnGreen.Click += (s, e) => BulkTake(AiMatchState.Agree);
        var btnNone = new Button { Text = "全不选", AutoSize = true };
        btnNone.Click += (s, e) => BulkTake(null);
        var btnClearCache = new Button { Text = "🗑 清空 AI 缓存", AutoSize = true };
        btnClearCache.Click += (s, e) =>
        {
            var n = PaperlessAi.ClearCache();
            MessageBox.Show($"已清空 {n} 条 AI 解析缓存，下次「AI 语义复核」会重新调用模型。", "完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        var btnApply = new Button { Text = "✅ 应用所选并关闭", AutoSize = true };
        btnApply.Click += (s, e) => Apply();
        var btnCancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        bar.Controls.AddRange(new Control[] { btnGreen, btnNone, btnClearCache, btnApply, btnCancel });
        _tip.SetToolTip(btnClearCache, "同文件同模型会命中缓存避免重复计费；改了提示词版本会自动失效。");

        Controls.Add(split);
        Controls.Add(notes);
        Controls.Add(_lblSummary);
        Controls.Add(_chkApplyHeader);
        Controls.Add(bar);
        Controls.Add(head);
        CancelButton = btnCancel;

        BindRows();
        BuildRawGrid();
        UpdateSummary();
    }

    private void AddCol(string name, string header, int width)
        => _gridReview.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

    private void BindRows()
    {
        _rowModels.Clear();
        _rowModels.AddRange(_result.Rows);

        foreach (var r in _result.Rows)
        {
            var idx = _gridReview.Rows.Add(
                r.StateText,
                r.RuleCol >= 0 ? r.RuleHeader : "—（规则无此列）",
                r.AiHeader.Length > 0 ? r.AiHeader : "—（模型未给）",
                KindText(r),
                r.Confidence > 0 ? r.Confidence.ToString("P0") : "",
                r.SourceRef,
                r.TakeAi,
                r.Reason);

            var bg = r.State switch
            {
                AiMatchState.Agree => Color.FromArgb(0xE8, 0xF5, 0xE9),          // 绿
                AiMatchState.Conflict => Color.FromArgb(0xFF, 0xF8, 0xE1),       // 黄
                _ => Color.FromArgb(0xFF, 0xEB, 0xEE),                            // 红
            };
            _gridReview.Rows[idx].DefaultCellStyle.BackColor = bg;
            _gridReview.Rows[idx].Cells["Take"].ReadOnly = r.RuleCol < 0;         // 多出的列不允许采纳
        }
    }

    /// <summary>类型变化一栏：一致时只显示一个，不一致显示「规则→模型」（中文口径）。</summary>
    private static string KindText(AiReviewRow r)
    {
        if (r.AiHeader.Length == 0) return r.RuleCol >= 0 ? FieldKindText.Of(r.RuleKind) : "";
        if (r.RuleCol < 0) return FieldKindText.Of(r.AiKind);
        return r.AiKind == r.RuleKind
            ? FieldKindText.Of(r.RuleKind)
            : $"{FieldKindText.Of(r.RuleKind)} → {FieldKindText.Of(r.AiKind)}";
    }

    /// <summary>把原文表铺出来（行列号与 sourceRef 一致，都是 1 基）。</summary>
    private void BuildRawGrid()
    {
        var raw = _result.Raw;
        if (raw is null) return;

        for (int c = 0; c < raw.ColCount; c++)
            _gridRaw.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "C" + (c + 1),
                HeaderText = "C" + (c + 1),
                Width = 110,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });

        for (int r = 0; r < raw.RowCount; r++)
        {
            var idx = _gridRaw.Rows.Add();
            _gridRaw.Rows[idx].HeaderCell.Value = "R" + (r + 1);
            for (int c = 0; c < raw.ColCount; c++)
                _gridRaw.Rows[idx].Cells[c].Value = raw.Cell(r, c);
        }
    }

    /// <summary>选中左表某行 → 右侧高亮该行 sourceRef 指向的单元格。</summary>
    private void HighlightSource()
    {
        if (_gridReview.CurrentRow is null) return;
        var i = _gridReview.CurrentRow.Index;
        if (i < 0 || i >= _rowModels.Count) return;
        var (r, c) = _rowModels[i].ResolveRef();
        if (r < 0 || r >= _gridRaw.Rows.Count || c < 0 || c >= _gridRaw.Columns.Count) return;

        _gridRaw.ClearSelection();
        _gridRaw.CurrentCell = _gridRaw.Rows[r].Cells[c];
        _gridRaw.Rows[r].Cells[c].Selected = true;
        _gridRaw.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(r, _gridRaw.Rows.Count - 1) - 3);
        if (c > 2) _gridRaw.FirstDisplayedScrollingColumnIndex = c - 2;
    }

    private void BulkTake(AiMatchState? state)
    {
        for (int i = 0; i < _gridReview.Rows.Count && i < _rowModels.Count; i++)
        {
            var r = _rowModels[i];
            if (r.RuleCol < 0) continue;
            r.TakeAi = state is not null && r.State == state;
            _gridReview.Rows[i].Cells["Take"].Value = r.TakeAi;
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        _lblSummary.Text =
            $"🟢 一致 {_result.AgreeCount} 项　🟡 待确认 {_result.ConflictCount} 项　🔴 疑似编造 {_result.HallucinationCount} 项" +
            $"　｜　本次采纳 {AcceptedCount} 项" +
            (_result.FromCache ? "　｜　本次命中缓存（未调用模型）" : "");
    }

    private void Apply()
    {
        if (AcceptedCount == 0 && !(_chkApplyHeader.Checked && _chkApplyHeader.Enabled))
        {
            MessageBox.Show("没有勾选任何要采纳的项。\n（绿项默认已勾选；黄/红项需你判断后手动勾）",
                "无可应用内容", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _result.ApplyHeaderFields = _chkApplyHeader.Enabled && _chkApplyHeader.Checked;
        Result = PaperlessAi.ApplyTo(_ruleTpl, _result);
        DialogResult = DialogResult.OK;
        Close();
    }
}
