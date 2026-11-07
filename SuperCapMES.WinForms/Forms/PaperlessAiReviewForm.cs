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
/// 控件宣告与静态配置见 PaperlessAiReviewForm.Designer.cs。
/// </summary>
internal sealed partial class PaperlessAiReviewForm : Form
{
    private ChecklistTemplate _ruleTpl = null!;
    private AiAnalysisResult _result = null!;
    private readonly List<AiReviewRow> _rowModels = new();

    /// <summary>人工确认后的模板（未点应用时为 null）</summary>
    public ChecklistTemplate? Result { get; private set; }

    /// <summary>被采纳的条目数</summary>
    public int AcceptedCount => _rowModels.Count(r => r.TakeAi && r.RuleCol >= 0);

    public PaperlessAiReviewForm(ChecklistTemplate ruleTpl, AiAnalysisResult result)
    {
        InitializeComponent();
        InitializeRuntime(ruleTpl, result);
    }

    /// <summary>
    /// 执行期初始化：处理依赖传入参数 / 回圈建项 / 物件初始设定式 / 方法呼叫的内容，
    /// 以及原本建构式在 UI 建立后呼叫的 BindRows() / BuildRawGrid() / UpdateSummary()。
    /// 其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(ChecklistTemplate ruleTpl, AiAnalysisResult result)
    {
        _ruleTpl = ruleTpl;
        _result = result;

        lblHeadTrace.Text = result.TraceText + (result.FromCache ? "" : "");
        notes.Text = string.Join("\r\n", result.Notes);
        _chkApplyHeader.Enabled = result.Claim is { HeaderFields.Count: > 0 };

        // 原建构式内的 _gridReview 栏位建立：AddCol 回圈 / 物件初始设定式，Designer 无法解析，原样保留。
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

        _tip.SetToolTip(btnClearCache, "同文件同模型会命中缓存避免重复计费；改了提示词版本会自动失效。");

        BindRows();
        BuildRawGrid();
        UpdateSummary();
    }

    private void GridReview_SelectionChanged(object? sender, EventArgs e)
    {
        HighlightSource();
    }

    private void GridReview_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _gridReview.Columns[e.ColumnIndex].Name != "Take") return;
        if (e.RowIndex < _rowModels.Count)
            _rowModels[e.RowIndex].TakeAi = Convert.ToBoolean(_gridReview.Rows[e.RowIndex].Cells["Take"].Value ?? false);
        UpdateSummary();
    }

    private void GridReview_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        // 勾选框要立即提交，否则要等离开单元格才触发 CellValueChanged
        if (_gridReview.IsCurrentCellDirty && _gridReview.CurrentCell is { } c && _gridReview.Columns[c.ColumnIndex].Name == "Take")
            _gridReview.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    // SplitterDistance 只能在控件有真实尺寸后设，放到 Shown 里（构造期设会被重置）
    private void PaperlessAiReviewForm_Shown(object? sender, EventArgs e)
    {
        try { if (split.Width > 1000) split.SplitterDistance = 930; } catch { /* 忽略尺寸异常 */ }
    }

    private void BtnGreen_Click(object? sender, EventArgs e)
    {
        BulkTake(AiMatchState.Agree);
    }

    private void BtnNone_Click(object? sender, EventArgs e)
    {
        BulkTake(null);
    }

    private void BtnClearCache_Click(object? sender, EventArgs e)
    {
        var n = PaperlessAi.ClearCache();
        MessageBox.Show($"已清空 {n} 条 AI 解析缓存，下次「AI 语义复核」会重新调用模型。", "完成",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnApply_Click(object? sender, EventArgs e)
    {
        Apply();
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
