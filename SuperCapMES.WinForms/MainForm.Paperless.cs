using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

public sealed partial class MainForm
{
    // ====== 模板管理 ======
    private DataGridView _gridTplList = null!;
    private DataGridView _gridTplPreview = null!;
    private DataGridView _gridTplData = null!;
    private Label _lblTplMeta = null!;
    private Label _lblTplStatus = null!;
    private Button _btnAi = null!;
    private Button _btnAiDetail = null!;
    private readonly ToolTip _tplTip = new();
    private ChecklistTemplate? _previewTpl;
    /// <summary>刷新模板列表时会触发选中 → 不能让它把刚导入的预览覆盖掉</summary>
    private bool _suppressPreviewOnListChange;
    /// <summary>最近一次 AI 解析结果（用于列定义三色标注与详情页）</summary>
    private AiAnalysisResult? _aiResult;
    /// <summary>导入时的规则基线（AI 比对必须对它，不能拿 AI 结果自我印证）</summary>
    private ChecklistTemplate? _ruleTpl;
    /// <summary>最近一次导入的原始文件路径（AI 解析要按原文件坐标定位，必须留着）</summary>
    private string? _lastImportPath;
    private int _lastSheetIndex;

    // ====== 填写提交 ======
    private ComboBox _cboFillTpl = null!;
    private DataGridView _gridFill = null!;
    private ContextMenuStrip _menuFillCol = null!;
    private int _bulkFillColumn = -1;
    private FlowLayoutPanel _pnlFillHeader = null!;
    private Label _lblFillFooter = null!;
    private readonly List<(string Label, TextBox Box)> _fillHeaderBoxes = new();
    private TextBox _txtSubmitter = null!;
    private TextBox _txtShift = null!;
    private TextBox _txtDevice = null!;
    private TextBox _txtFillNote = null!;
    private Label _lblFillStatus = null!;

    // ====== 历史查询 ======
    private ComboBox _cboHistTpl = null!;
    private TextBox _txtHistSubmitter = null!;
    private DateTimePicker _dtFrom = null!;
    private DateTimePicker _dtTo = null!;
    private TextBox _txtHistKeyword = null!;
    private DataGridView _gridHist = null!;
    private Label _lblHistStatus = null!;

    // ---------------- 模板管理 ----------------

    internal TabPage BuildPaperlessTemplateTab()
    {
        var page = new TabPage("模板管理")
        {
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(10),
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };

        // 左：模板列表 + 操作
        var left = new Panel { Dock = DockStyle.Fill };
        var tool = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Height = 34,
        };
        var btnXlsx = Btn("📥 导入 Excel", OnImportXlsx);
        var btnDocx = Btn("📥 导入 Word", OnImportDocx);
        var btnSample = Btn("✨ 载入示例", OnLoadSample);
        var btnDel = Btn("🗑 删除选中", OnDeleteTemplate);
        tool.Controls.AddRange(new Control[] { btnXlsx, btnDocx, btnSample, btnDel });

        _gridTplList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
        };
        _gridTplList.Columns.Add(MakeTextCol("Name", "模板名称", true, 22));
        _gridTplList.Columns.Add(MakeTextCol("Source", "来源", true, 18));
        _gridTplList.Columns.Add(MakeTextCol("Title", "表标题", true, 18));
        _gridTplList.Columns.Add(MakeTextCol("ColInfo", "列/行", true, 10));
        _gridTplList.Columns.Add(MakeTextCol("Imported", "导入时间", true, 16));
        _gridTplList.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || _gridTplList.Rows[e.RowIndex].DataBoundItem is not ChecklistTemplate t) return;
            if (e.ColumnIndex == 3) e.Value = $"{t.Columns.Count}列/{t.Rows.Count}行";
            if (e.ColumnIndex == 4) e.Value = t.ImportedAt.ToString("yyyy-MM-dd HH:mm");
        };
        _gridTplList.SelectionChanged += (s, e) =>
        {
            // 刷新列表会触发选中：此时绝不能把刚导入的预览换成历史模板（这是"导入后没反应"的元凶之一）
            if (_suppressPreviewOnListChange) return;
            if (_gridTplList.SelectedRows.Count > 0 && _gridTplList.SelectedRows[0].DataBoundItem is ChecklistTemplate t)
            {
                _aiResult = null;   // 切到别的模板，AI 标注不再适用
                LoadPreview(t);
            }
        };

        left.Controls.Add(_gridTplList);
        left.Controls.Add(tool);

        // 右：上=列定义（可改类型后保存）；下=表体数据预览（只读，用于核对导入是否完整）
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var head = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "导入预览（可调整列类型后保存；上下文列不可填）",
            Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
        };
        _lblTplMeta = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.DimGray,
            AutoEllipsis = true,
            Text = "尚未导入文件。点左上「导入 Excel / 导入 Word」，或「载入示例」。",
        };
        _gridTplPreview = new DataGridView
        {
            Dock = DockStyle.Top,
            Height = 290,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _gridTplPreview.Columns.Add(MakeTextCol("Header", "列名", true, 22));        var colKind = new DataGridViewComboBoxColumn
        {
            Name = "Kind",
            HeaderText = "字段类型",
            DataPropertyName = "KindText",
            DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
            FlatStyle = FlatStyle.Flat,
        };
        colKind.Items.AddRange(FieldKindText.All.Cast<object>().ToArray());
        _gridTplPreview.Columns.Add(colKind);
        var colReq = new DataGridViewCheckBoxColumn { Name = "Required", HeaderText = "必填", Width = 50 };
        _gridTplPreview.Columns.Add(colReq);
        _gridTplPreview.Columns.Add(MakeTextCol("OptionsText", "候选项（下拉类有效）", true, 30));
        _gridTplPreview.Columns.Add(MakeTextCol("AiStateText", "AI 校验", true, 16));
        // 兜底：列类型下拉若拿到不在候选集里的值，WinForms 会弹「DataGridViewComboBoxCell 值无效」
        // 系统错误框，把整个操作打断。这里统一吞掉显示异常（数据本身不动），只在状态栏提示一句。
        _gridTplPreview.DataError += (s, e) =>
        {
            e.ThrowException = false;
            if (_lblTplStatus is not null)
                TabStatus(_lblTplStatus, "⚠ 有单元格的值与列类型不匹配，已忽略其显示异常（数据未改动）。", true);
        };
        _gridTplPreview.CellValueChanged += TplPreview_CellValueChanged;

        var lblDataHead = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "表体预览（文件中的全部数据行，只读）",
            ForeColor = Color.DarkSlateBlue,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
        };
        _gridTplData = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };

        var btnSave = Btn("💾 保存此模板", OnSaveTemplate);
        // AI 复核放在右栏保存旁边：作用对象就是当前预览模板；左栏只有 360px 宽，塞第 5 个按钮会被裁掉
        _btnAi = Btn("🤖 AI 语义复核", OnAiReview);
        // 默认走规则：模型未配置时入口直接置灰，不给用户"点了没反应"的悬空感
        _btnAi.Enabled = LlmService.Enabled;
        _tplTip.SetToolTip(_btnAi, LlmService.Enabled
            ? "调用大模型判断表结构（哪列要填、什么类型），与规则结果比对后三色确认；确认后才改模板。"
            : "默认走规则解析，不用配置也能用。需要 AI 复核时，先在「AI 助手设置」里启用云端模型。");
        var barRight = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false };
        barRight.Controls.Add(btnSave);
        barRight.Controls.Add(_btnAi);
        _btnAiDetail = Btn("🔍 查看 AI 复核详情", OnShowAiDetail);
        _btnAiDetail.Enabled = false;
        _tplTip.SetToolTip(_btnAiDetail, "打开三色确认页，逐项核对 AI 判断与原文依据位置。");
        barRight.Controls.Add(_btnAiDetail);
        // 停靠顺序：先加 Fill，最后加 Top 的最外层
        right.Controls.Add(_gridTplData);
        right.Controls.Add(barRight);
        right.Controls.Add(lblDataHead);
        right.Controls.Add(_gridTplPreview);
        right.Controls.Add(_lblTplMeta);
        right.Controls.Add(head);

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        page.Controls.Add(split);
        // 本页自己的状态栏：导入成功/失败/解析结果必须显示在**用户正看着的这一页**
        _lblTplStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            ForeColor = Color.Gray,
            AutoEllipsis = true,
            Padding = new Padding(4, 6, 4, 4),
            Text = "就绪。点「📥 导入 Excel / 📥 导入 Word」选择点检表文件。",
        };
        page.Controls.Add(_lblTplStatus);
        split.SplitterDistance = 360;
        split.SplitterMoved += (s, e) => split.SplitterDistance = 360;
        // 首次进入自动加载已有模板（否则列表空白像没做完）
        page.Enter += (s, e) => { if (_gridTplList.Rows.Count == 0) RefreshTplList(); };
        return page;
    }

    private void TplPreview_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_previewTpl is null || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var col = _previewTpl.Columns[e.RowIndex];
        if (_gridTplPreview.Columns[e.ColumnIndex].Name == "Kind")
        {
            var v = _gridTplPreview.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            col.Kind = FieldKindText.Parse(v);
        }
        else if (_gridTplPreview.Columns[e.ColumnIndex].Name == "Required")
        {
            col.Required = Convert.ToBoolean(_gridTplPreview.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
        }
    }

    private void LoadPreview(ChecklistTemplate tpl)
    {
        _previewTpl = tpl.Clone();
        // AI 校验结果按"规则列下标"对齐；AI 多出来的列（RuleCol=-1）不在列定义表里出现
        var aiByCol = new Dictionary<int, AiReviewRow>();
        if (_aiResult is not null)
            foreach (var r in _aiResult.Rows.Where(x => x.RuleCol >= 0)) aiByCol[r.RuleCol] = r;

        var bs = new BindingSource
        {
            DataSource = _previewTpl.Columns.Select((c, i) => new
            {
                c.Header,
                KindText = c.KindText,
                c.Required,
                OptionsText = c.Options.Count == 0 ? "" : string.Join(" / ", c.Options),
                AiStateText = aiByCol.TryGetValue(i, out var ar)
                    ? ar.State switch
                    {
                        AiMatchState.Agree => "🟢 一致",
                        AiMatchState.Conflict => "🟡 待确认",
                        _ => "🔴 疑似编造",
                    }
                    : "",
            }).ToList()
        };
        _gridTplPreview.DataSource = bs;

        _lblTplMeta.Text = BuildTplMetaText(_previewTpl);
        if (_aiResult is { Ok: true } ai)
            _lblTplMeta.Text += $"　🤖 AI 解析：🟢{ai.AgreeCount} 🟡{ai.ConflictCount} 🔴{ai.HallucinationCount}"
                                + (ai.FromCache ? "（缓存）" : "");
        BuildDataPreview(_previewTpl);
        PaintAiStates();
    }

    /// <summary>把 AI 三色结论刷到列定义表：行底色 + 依据位置/原因进悬停提示。</summary>
    private void PaintAiStates()
    {
        if (_aiResult is null) return;
        foreach (var r in _aiResult.Rows.Where(x => x.RuleCol >= 0))
        {
            if (r.RuleCol < 0 || r.RuleCol >= _gridTplPreview.Rows.Count) continue;
            var row = _gridTplPreview.Rows[r.RuleCol];
            row.DefaultCellStyle.BackColor = r.State switch
            {
                AiMatchState.Agree => Color.FromArgb(0xE8, 0xF5, 0xE9),
                AiMatchState.Conflict => Color.FromArgb(0xFF, 0xF8, 0xE1),
                _ => Color.FromArgb(0xFF, 0xEB, 0xEE),
            };
            var stateCell = row.Cells["AiStateText"];
            if (stateCell is not null) stateCell.ToolTipText = r.Reason;
            if (row.Cells["Header"] is { } h)
                h.ToolTipText = (r.SourceRef.Length > 0 ? $"AI 依据位置：{r.SourceRef}" : "")
                                + (r.Reason.Length > 0 ? (r.SourceRef.Length > 0 ? "\n" : "") + r.Reason : "");
        }
    }

    /// <summary>一行摘要：列数/行数 + 抬头 + 表尾，让用户一眼确认「数据是否导全」。</summary>
    private static string BuildTplMetaText(ChecklistTemplate tpl)
    {
        var s = $"{tpl.Columns.Count} 列 × {tpl.Rows.Count} 行 ｜ 抬头 {tpl.HeaderFields.Count} 项 ｜ 表尾说明 {tpl.FooterNotes.Count} 条" +
                $"　（来源：{tpl.SourceFileName}）";
        if (tpl.HeaderFields.Count > 0)
            s += "　抬头：" + string.Join(" / ", tpl.HeaderFields.Select(h => h.Label));
        return s;
    }

    /// <summary>渲染表体全部数据行（只读），用于核对导入完整性。</summary>
    private void BuildDataPreview(ChecklistTemplate tpl)
    {
        _gridTplData.Columns.Clear();
        _gridTplData.Rows.Clear();
        for (int c = 0; c < tpl.Columns.Count; c++)
        {
            var col = tpl.Columns[c];
            _gridTplData.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "d" + c,
                HeaderText = col.Header,
                Width = col.Kind == FieldKind.Context ? 132 : 100,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        }
        for (int r = 0; r < tpl.Rows.Count; r++)
        {
            var idx = _gridTplData.Rows.Add();
            for (int c = 0; c < tpl.Columns.Count; c++)
            {
                var v = c < tpl.Rows[r].Cells.Count ? tpl.Rows[r].Cells[c] : "";
                _gridTplData.Rows[idx].Cells[c].Value = v;
                if (!tpl.Columns[c].Fillable) _gridTplData.Rows[idx].Cells[c].Style.BackColor = Color.WhiteSmoke;
            }
        }
    }

    private void OnImportXlsx(object? s, EventArgs e) => ImportFile(".xlsx");
    private void OnImportDocx(object? s, EventArgs e) => ImportFile(".docx");

    private async void ImportFile(string ext)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = ext == ".xlsx" ? "Excel 工作簿|*.xlsx;*.xlsm" : "Word 文档|*.docx",
            Title = "选择点检表文件",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        var path = dlg.FileName;
        var sheets = PaperlessImport.ListSheets(path);
        var sheetIndex = 0;
        if (sheets.Count > 1)
        {
            using var pick = new SheetPickerDialog(sheets);
            if (pick.ShowDialog(FindForm()) != DialogResult.OK) return;
            sheetIndex = pick.SelectedIndex;
        }

        await ImportAndAnalyzeAsync(path, sheetIndex, sheets.Count);
    }

    /// <summary>
    /// 导入主流程：规则解析（先出可用结果）→ 自动追问大模型 → AI 结果同步到界面 → 三色标注待确认项。
    /// 所有反馈都写本页状态栏，保证"点了导入一定有动静"。
    /// </summary>
    private async Task ImportAndAnalyzeAsync(string path, int sheetIndex, int sheetCount)
    {
        _lastImportPath = path;
        _lastSheetIndex = sheetIndex;
        _aiResult = null;
        _btnAiDetail.Enabled = false;

        var sheetName = PaperlessImport.ListSheets(path).ElementAtOrDefault(sheetIndex)?.Name ?? "";
        var sheetNote = sheetCount > 1 ? $"（文件共 {sheetCount} 个 sheet，本次解析第 {sheetIndex + 1} 个「{sheetName}」）" : "";

        ChecklistTemplate ruleTpl;
        try
        {
            ruleTpl = PaperlessImport.Import(path, null, sheetIndex);
        }
        catch (Exception ex)
        {
            TabStatus(_lblTplStatus, "❌ 导入失败：" + ex.Message, true);
            return;
        }

        _ruleTpl = ruleTpl.Clone();

        if (ruleTpl.Columns.Count == 0)
        {
            LoadPreview(ruleTpl);
            RefreshTplList();
            TabStatus(_lblTplStatus,
                $"⚠ 已读取「{sheetName}」，但没识别到表头（0 列）。该 sheet 可能不是点检表，" +
                "请重新导入并换一个 sheet 试试。", true);
            return;
        }
        if (ruleTpl.Rows.Count == 0)
        {
            // 表头认出来了但一行数据都没有：多半是空白台账（只印了表头等人手填）
            LoadPreview(ruleTpl);
            RefreshTplList();
            TabStatus(_lblTplStatus,
                $"⚠ 「{sheetName}」识别到表头 {ruleTpl.Columns.Count} 列（{string.Join("、", ruleTpl.Columns.Take(4).Select(c => c.Header))}…），" +
                "但没有数据行——这是空白台账/待填表，不是点检表。点检模板至少要有一条检查项，请换一个 sheet。", true);
            return;
        }

        LoadPreview(ruleTpl);
        RefreshTplList();
        TabStatus(_lblTplStatus,
            $"✅ 规则解析完成：{ruleTpl.Columns.Count} 列 × {ruleTpl.Rows.Count} 行，抬头 {ruleTpl.HeaderFields.Count} 项，表尾说明 {ruleTpl.FooterNotes.Count} 条{sheetNote}。" +
            "下方可核对；核对无误点「💾 保存此模板」。", false);

        if (!LlmService.Enabled)
        {
            TabStatus(_lblTplStatus,
                _lblTplStatus.Text + "\n未启用云端模型：以上为规则解析结果（默认路径，可直接保存使用）。", false);
            return;
        }

        // 导入即追加大模型解析（用户要求：解析内容直接同步到界面，随后再做语义复核）
        await AnalyzeWithAiAsync(ignoreCache: false);
    }

    // ---------------- AI 解析 + 语义复核 ----------------

    /// <summary>按钮：重新解析（忽略缓存）。</summary>
    private async void OnAiReview(object? s, EventArgs e)
    {
        if (_previewTpl is null)
        {
            TabStatus(_lblTplStatus, "请先导入文件（或载入示例）后再做 AI 解析。", true);
            return;
        }
        if (_lastImportPath is null || !File.Exists(_lastImportPath))
        {
            TabStatus(_lblTplStatus,
                "AI 解析需要原始文件按坐标定位：请用「📥 导入 Excel / Word」现场导入一次（内置示例无法解析）。", true);
            return;
        }
        if (!LlmService.Enabled)
        {
            TabStatus(_lblTplStatus, "云端大模型未启用：默认走规则解析，结果已可用。需要 AI 解析请先配置 API Key。", true);
            return;
        }
        await AnalyzeWithAiAsync(ignoreCache: true);
    }

    /// <summary>
    /// 跑 AI 解析并把结果同步到界面。基线始终是导入时的规则模板（不能拿 AI 结果当基线再比一次，
    /// 那样会变成自我印证，全绿但没意义）。
    /// </summary>
    private async Task AnalyzeWithAiAsync(bool ignoreCache)
    {
        if (_ruleTpl is null || _lastImportPath is null) return;

        var ruleTpl = _ruleTpl;
        var path = _lastImportPath;
        var sheetIndex = _lastSheetIndex;

        _btnAi.Enabled = false;
        TabStatus(_lblTplStatus, "🤖 正在调用大模型解析文档结构（约 2~8 秒，界面不阻塞，可继续操作）…", false);

        try
        {
            // UI 线程不能同步调模型：会假死，必须异步跑
            var result = await Task.Run(() => PaperlessAi.Analyze(path, ruleTpl, null, null, ignoreCache, sheetIndex));

            if (!result.Ok)
            {
                TabStatus(_lblTplStatus,
                    $"🤖 AI 解析未生效：{result.Reason}\n已按规则解析结果展示，不影响保存与填写。", true);
                return;
            }

            _aiResult = result;
            var applied = PaperlessAi.ApplyTo(ruleTpl, result);
            LoadPreview(applied);          // ← AI 解析出的列/类型/抬头直接同步到界面
            RefreshTplList();
            _btnAiDetail.Enabled = true;

            TabStatus(_lblTplStatus,
                $"🤖 AI 解析完成并已同步到界面（{result.TraceText}）：🟢 一致 {result.AgreeCount} 项 / 🟡 待确认 {result.ConflictCount} 项 / 🔴 疑似编造 {result.HallucinationCount} 项。" +
                "\n绿=已直接采用；黄/红请点「🔍 查看 AI 复核详情」对着原文依据核一遍，确认后点「💾 保存此模板」。", false);
        }
        catch (Exception ex)
        {
            TabStatus(_lblTplStatus, "🤖 AI 解析异常：" + ex.Message + "\n已按规则解析结果展示。", true);
        }
        finally
        {
            _btnAi.Enabled = LlmService.Enabled;
        }
    }

    /// <summary>打开三色确认页做逐项复核（人工确认后才改当前预览）。</summary>
    private void OnShowAiDetail(object? s, EventArgs e)
    {
        if (_aiResult is null || _previewTpl is null)
        {
            TabStatus(_lblTplStatus, "还没有 AI 解析结果：先导入文件（需已启用云端模型）。", true);
            return;
        }
        using var dlg = new PaperlessAiReviewForm(_previewTpl, _aiResult);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK && dlg.Result is not null)
        {
            LoadPreview(dlg.Result);
            TabStatus(_lblTplStatus,
                $"已按你的勾选更新 {dlg.AcceptedCount} 项。核对后点「💾 保存此模板」。", false);
        }
    }

    private void OnLoadSample(object? s, EventArgs e)
    {
        _lastImportPath = null;   // 内置示例没有原始文件，AI 解析用不了
        _ruleTpl = null;
        _aiResult = null;
        _btnAiDetail.Enabled = false;
        LoadPreview(PaperlessImport.BuildSampleTemplate());
        RefreshTplList();
        TabStatus(_lblTplStatus, "已载入示例模板，可直接保存或去「填写提交」体验。", false);
    }

    private void OnSaveTemplate(object? s, EventArgs e)
    {
        if (_previewTpl is null) { TabStatus(_lblTplStatus, "没有可保存的预览模板。", true); return; }
        using var dlg = new InputDialog("模板名称", "请输入模板名称：", _previewTpl.Name);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        _previewTpl.Name = string.IsNullOrWhiteSpace(dlg.Value) ? _previewTpl.Title : dlg.Value.Trim();
        PaperlessStore.SaveTemplate(_previewTpl);
        RefreshTplList();
        TabStatus(_lblTplStatus,
            $"✅ 已保存模板「{_previewTpl.Name}」（{_previewTpl.Columns.Count} 列 × {_previewTpl.Rows.Count} 行）。去「填写提交」即可使用。",
            false);
    }

    private void OnDeleteTemplate(object? s, EventArgs e)
    {
        if (_gridTplList.SelectedRows.Count == 0) { TabStatus(_lblTplStatus, "请先选中要删除的模板。", true); return; }
        if (_gridTplList.SelectedRows[0].DataBoundItem is not ChecklistTemplate t) return;
        if (MessageBox.Show($"确认删除模板「{t.Name}」？\n（历史提交自带模板快照，删除不影响已提交记录还原）",
                "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        PaperlessStore.DeleteTemplate(t.Id);
        _previewTpl = null;
        _gridTplPreview.DataSource = null;
        _gridTplData.Columns.Clear();
        _gridTplData.Rows.Clear();
        _lblTplMeta.Text = "尚未导入文件。点左上「导入 Excel / 导入 Word」，或「载入示例」。";
        RefreshTplList();
        TabStatus(_lblTplStatus, $"已删除模板「{t.Name}」（历史提交仍可按快照还原）。", false);
    }

    private void RefreshTplList()
    {
        var list = PaperlessStore.LoadAllTemplates();
        // 绑 DataSource 会让 DataGridView 自动选中首行并触发 SelectionChanged；
        // 提前挂起，避免把刚导入待保存的预览顶掉
        _suppressPreviewOnListChange = true;
        try { _gridTplList.DataSource = new BindingSource { DataSource = list }; }
        finally { _suppressPreviewOnListChange = false; }
        // 同步填充页与查询页的下拉
        SyncTplCombos(list);
    }

    // ---------------- 填写提交 ----------------

    internal TabPage BuildPaperlessFillTab()
    {
        var page = new TabPage("填写提交")
        {
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(10),
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Height = 30, WrapContents = false };
        top.Controls.Add(new Label { Text = "选择模板：", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _cboFillTpl = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboFillTpl.SelectedIndexChanged += (s, e) => RebuildFillGrid();
        top.Controls.Add(_cboFillTpl);
        top.Controls.Add(Btn("⚡ 批量填充", OnBulkFillClick));
        var btnSubmit = Btn("✅ 提交并归档", OnSubmitFill);
        top.Controls.Add(btnSubmit);

        _gridFill = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            BackgroundColor = Color.White,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
        };
        // 兜底：下拉列拿到的值不在候选集时会弹系统错误框（导入带汇总行的表就会踩到），
        // 这里统一吞掉显示异常，避免"填到一半弹框、流程中断"。
        _gridFill.DataError += (s, e) =>
        {
            e.ThrowException = false;
            if (_lblFillStatus is not null)
                TabStatus(_lblFillStatus, "⚠ 有单元格的值与列类型不匹配，已忽略其显示异常（数据未改动，可重新选择该列的值）。", true);
        };
        // 列头右键 →「批量填充此列」：同一列几十行同一个值（如点检人签名），不要让人填几十遍
        _menuFillCol = new ContextMenuStrip();
        _menuFillCol.Items.Add(new ToolStripMenuItem("⚡ 批量填充此列…", null, (s, e) => ShowBulkFillDialog(_bulkFillColumn)));
        _gridFill.CellMouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex != -1 || e.ColumnIndex < 0) return;
            _bulkFillColumn = e.ColumnIndex;
            _menuFillCol.Show(_gridFill, e.Location);
        };
        // Ctrl+D 向下填充：把当前单元格的值填到下方所有空白同列单元格
        _gridFill.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.D) { FillDown(); e.Handled = true; }
        };

        var meta = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Height = 30, WrapContents = false };
        _txtSubmitter = MetaBox("填写人", 90);
        _txtShift = MetaBox("班次", 70);
        _txtDevice = MetaBox("设备/区域", 110);
        meta.Controls.AddRange(new Control[] { _txtSubmitter, _txtShift, _txtDevice });

        _txtFillNote = new TextBox { Dock = DockStyle.Bottom, Height = 22, PlaceholderText = "备注（选填）" };
        _lblFillStatus = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray, Text = "选择模板后自动生成填写页。" };
        _lblFillFooter = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            ForeColor = Color.DimGray,
            AutoEllipsis = true,
            Padding = new Padding(0, 4, 0, 4),
            Text = "",
        };
        // 抬头字段区（车间/线别、点检日期、班次…）：由模板的 HeaderFields 自动生成
        _pnlFillHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(0, 2, 0, 2),
            Visible = false,
        };

        page.Controls.Add(_gridFill);
        page.Controls.Add(meta);
        page.Controls.Add(_txtFillNote);
        page.Controls.Add(_lblFillStatus);
        page.Controls.Add(_lblFillFooter);
        page.Controls.Add(_pnlFillHeader);
        page.Controls.Add(top);
        page.Enter += (s, e) =>
        {
            if (_cboFillTpl.Items.Count == 0)
            {
                var list = PaperlessStore.LoadAllTemplates();
                SyncTplCombos(list);
                if (_cboFillTpl.Items.Count > 0) _cboFillTpl.SelectedIndex = 0;
            }
        };
        return page;
    }

    private void RebuildFillGrid()
    {
        _gridFill.Columns.Clear();
        _gridFill.Rows.Clear();
        _pnlFillHeader.Controls.Clear();
        _fillHeaderBoxes.Clear();
        _lblFillFooter.Text = "";
        if (_cboFillTpl.SelectedItem is not ChecklistTemplate tpl)
        {
            _pnlFillHeader.Visible = false;
            return;
        }

        // 抬头字段：车间/线别、点检日期、班次、温湿度…（原表在合并单元格里，不属于表体列）
        foreach (var hf in tpl.HeaderFields)
        {
            _pnlFillHeader.Controls.Add(new Label
            {
                Text = hf.Label + "：",
                AutoSize = true,
                Margin = new Padding(0, 7, 2, 0),
            });
            var box = new TextBox
            {
                Width = 148,
                PlaceholderText = hf.Placeholder,
                Margin = new Padding(2, 4, 12, 4),
            };
            _pnlFillHeader.Controls.Add(box);
            _fillHeaderBoxes.Add((hf.Label, box));
        }
        _pnlFillHeader.Visible = tpl.HeaderFields.Count > 0;

        // 表尾说明（点检说明/签字要求）：只读提示，随提交一并快照
        _lblFillFooter.Text = tpl.FooterNotes.Count > 0
            ? "说明：" + string.Join("　", tpl.FooterNotes)
            : "";

        for (int c = 0; c < tpl.Columns.Count; c++)
        {
            var col = tpl.Columns[c];
            DataGridViewColumn gcol = col.Kind switch
            {
                FieldKind.Select => new DataGridViewComboBoxColumn
                {
                    HeaderText = col.Header,
                    Items = { },
                    FlatStyle = FlatStyle.Flat,
                    DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                },
                FieldKind.Checkbox => new DataGridViewCheckBoxColumn { HeaderText = col.Header },
                _ => new DataGridViewTextBoxColumn { HeaderText = col.Header },
            };
            gcol.Name = "c" + c;
            gcol.ReadOnly = !col.Fillable;
            gcol.DefaultCellStyle.BackColor = col.Fillable ? Color.White : Color.WhiteSmoke;
            if (col.Kind == FieldKind.Select && gcol is DataGridViewComboBoxColumn cb)
                cb.Items.AddRange(col.Options.ToArray());
            _gridFill.Columns.Add(gcol);
        }

        foreach (var row in tpl.Rows)
        {
            var idx = _gridFill.Rows.Add();
            for (int c = 0; c < tpl.Columns.Count; c++)
            {
                var col = tpl.Columns[c];
                var raw = c < row.Cells.Count ? (row.Cells[c] ?? "") : "";
                switch (col.Kind)
                {
                    case FieldKind.Checkbox:
                        _gridFill.Rows[idx].Cells[c].Value = false;
                        break;
                    case FieldKind.Select:
                        // 下拉列只接受候选项：上下文里带来的「0 项 OK」「—」之类塞进去会直接抛
                        // DataGridViewComboBoxCell 值无效（汇总行踩过）。不在候选里就留空让人选。
                        if (col.Options.Contains(raw)) _gridFill.Rows[idx].Cells[c].Value = raw;
                        break;
                    default:
                        _gridFill.Rows[idx].Cells[c].Value = raw;
                        break;
                }
            }
        }
    }

    private void OnSubmitFill(object? s, EventArgs e)
    {
        if (_cboFillTpl.SelectedItem is not ChecklistTemplate tpl) { TabStatus(_lblFillStatus, "请先选择模板。", true); return; }
        var submitter = _txtSubmitter.Text.Trim();
        if (submitter.Length == 0) { TabStatus(_lblFillStatus, "请填写「填写人」。", true); return; }

        var values = new Dictionary<string, string>();
        var errors = new List<string>();
        for (int r = 0; r < tpl.Rows.Count; r++)
        {
            for (int c = 0; c < tpl.Columns.Count; c++)
            {
                var col = tpl.Columns[c];
                if (!col.Fillable) continue;
                var cell = _gridFill.Rows[r].Cells[c];
                var raw = col.Kind == FieldKind.Checkbox
                    ? (Convert.ToBoolean(cell.Value) ? "是" : "否")
                    : (cell.Value?.ToString() ?? "").Trim();
                if (col.Required && raw.Length == 0) errors.Add($"第{r + 1}行[{col.Header}]为必填项");
                if (col.Kind == FieldKind.Number && raw.Length > 0 && !double.TryParse(raw, out _))
                    errors.Add($"第{r + 1}行[{col.Header}]需为数值");
                if (col.Kind == FieldKind.Date && raw.Length > 0 && !DateTime.TryParse(raw, out _))
                    errors.Add($"第{r + 1}行[{col.Header}]需为日期");
                values[$"r{r}c{c}"] = raw;
            }
        }
        if (errors.Count > 0) { TabStatus(_lblFillStatus, "提交未通过：" + string.Join("；", errors), true); return; }

        // 抬头字段值（车间/线别、点检日期、班次…）
        var headerValues = new Dictionary<string, string>();
        foreach (var (label, box) in _fillHeaderBoxes)
            headerValues[label] = box.Text.Trim();

        var sub = new ChecklistSubmission
        {
            TemplateId = tpl.Id,
            TemplateName = tpl.Name,
            SubmittedAt = DateTime.Now,
            Submitter = submitter,
            Shift = _txtShift.Text.Trim(),
            Device = _txtDevice.Text.Trim(),
            Note = _txtFillNote.Text.Trim(),
            TemplateSnapshot = tpl.Clone(),   // 提交时刻模板快照内嵌，后期改模板不影响历史
            Values = values,
            HeaderValues = headerValues,
        };
        PaperlessStore.SaveSubmission(sub);
        TabStatus(_lblFillStatus,
            $"✅ 已归档（提交号 {sub.Id}，{values.Count} 个填写项 + {headerValues.Count} 个抬头字段）。模板快照已锁定，历史可还原。",
            false);
    }

    // ---------------- 批量填充（同列多行一次填） ----------------

    private void OnBulkFillClick(object? s, EventArgs e) => ShowBulkFillDialog(-1);

    /// <summary>
    /// 批量填充：点检表常出现「同一列几十行填同一个值」（如点检人签名、检查结果），
    /// 逐格敲几十遍是纯浪费。选列 + 选值 + 选范围，一次写入。
    /// </summary>
    internal void ShowBulkFillDialog(int presetColumn)
    {
        if (_cboFillTpl.SelectedItem is not ChecklistTemplate tpl)
        {
            TabStatus(_lblFillStatus, "请先选择模板。", true);
            return;
        }
        if (tpl.FillableColumns.Count == 0)
        {
            TabStatus(_lblFillStatus, "该模板没有可填列。", true);
            return;
        }
        var selRows = _gridFill.SelectedCells.Cast<DataGridViewCell>()
            .Select(c => c.RowIndex).Where(i => i >= 0).Distinct().Count();
        using var dlg = new BulkFillDialog(tpl, presetColumn, selRows);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        ApplyBulkFill(tpl, dlg.ColumnIndex, dlg.Value, dlg.Scope);
    }

    /// <summary>把值写到某列的目标行（只写可填列）。返回（写入, 跳过, 覆盖）行数。</summary>
    internal (int Changed, int Skipped, int Overwritten) ApplyBulkFill(
        ChecklistTemplate tpl, int col, string value, BulkFillScope scope)
    {
        if (col < 0 || col >= tpl.Columns.Count || col >= _gridFill.Columns.Count) return (0, 0, 0);
        // 上下文列只读：不能通过批量填充绕过（单元格 ReadOnly 挡不住程序赋值，必须显式拦）
        if (!tpl.Columns[col].Fillable)
        {
            TabStatus(_lblFillStatus, $"「{tpl.Columns[col].Header}」是上下文列（只读），不能批量填充。", true);
            return (0, 0, 0);
        }

        var kind = tpl.Columns[col].Kind;
        int changed = 0, skipped = 0, overwritten = 0;

        var rows = scope == BulkFillScope.Selected
            ? _gridFill.SelectedCells.Cast<DataGridViewCell>()
                .Select(c => c.RowIndex).Where(i => i >= 0).Distinct().OrderBy(i => i).ToList()
            : Enumerable.Range(0, _gridFill.Rows.Count).ToList();

        foreach (var r in rows)
        {
            if (r < 0 || r >= _gridFill.Rows.Count) continue;
            var cell = _gridFill.Rows[r].Cells[col];
            bool blank = kind == FieldKind.Checkbox
                ? !Convert.ToBoolean(cell.Value ?? false)
                : string.IsNullOrWhiteSpace(cell.Value?.ToString());
            if (scope == BulkFillScope.BlankOnly && !blank) { skipped++; continue; }
            if (!blank) overwritten++;
            cell.Value = kind == FieldKind.Checkbox ? (object)(value == "是") : value;
            changed++;
        }

        TabStatus(_lblFillStatus,
            $"⚡ 批量填充「{tpl.Columns[col].Header}」：写入 {changed} 行"
            + (skipped > 0 ? $"，跳过已填 {skipped} 行" : "")
            + (overwritten > 0 ? $"，覆盖原值 {overwritten} 行" : "") + "。",
            false);
        return (changed, skipped, overwritten);
    }

    /// <summary>Ctrl+D：把当前单元格的值填到下方所有空白同列单元格（只填空，不覆盖）。</summary>
    private void FillDown()
    {
        if (_gridFill.CurrentCell is not { } cur) return;
        if (_cboFillTpl.SelectedItem is not ChecklistTemplate tpl) return;
        var col = cur.ColumnIndex;
        if (col < 0 || col >= tpl.Columns.Count || !tpl.Columns[col].Fillable) return;

        var kind = tpl.Columns[col].Kind;
        var text = kind == FieldKind.Checkbox
            ? (Convert.ToBoolean(cur.Value ?? false) ? "是" : "否")
            : (cur.Value?.ToString() ?? "");
        if (text.Length == 0) { TabStatus(_lblFillStatus, "当前单元格为空，无法向下填充。", true); return; }

        int n = 0;
        for (int r = cur.RowIndex + 1; r < _gridFill.Rows.Count; r++)
        {
            var cell = _gridFill.Rows[r].Cells[col];
            bool blank = kind == FieldKind.Checkbox
                ? !Convert.ToBoolean(cell.Value ?? false)
                : string.IsNullOrWhiteSpace(cell.Value?.ToString());
            if (!blank) continue;
            cell.Value = kind == FieldKind.Checkbox ? (object)(text == "是") : text;
            n++;
        }
        TabStatus(_lblFillStatus, $"已向下填充 {n} 行（Ctrl+D 只填空白行，不覆盖已有值）。", false);
    }

    // ---------------- 历史查询 ----------------

    internal TabPage BuildPaperlessHistoryTab()
    {
        var page = new TabPage("历史查询")
        {
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(10),
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Height = 32, WrapContents = false };
        _cboHistTpl = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        _txtHistSubmitter = new TextBox { Width = 90, PlaceholderText = "填写人" };
        _dtFrom = new DateTimePicker { Width = 110, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddMonths(-1) };
        _dtTo = new DateTimePicker { Width = 110, Format = DateTimePickerFormat.Short, Value = DateTime.Today };
        _txtHistKeyword = new TextBox { Width = 120, PlaceholderText = "关键字（值/设备）" };
        var btnQuery = Btn("🔍 查询", OnQueryHistory);
        var btnReset = Btn("↺ 重置", OnResetHistory);
        top.Controls.AddRange(new Control[]
        {
            Lbl("模板"), _cboHistTpl, Lbl("填写人"), _txtHistSubmitter,
            Lbl("起"), _dtFrom, Lbl("止"), _dtTo, Lbl("关键字"), _txtHistKeyword, btnQuery, btnReset,
        });

        _gridHist = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _gridHist.Columns.Add(MakeTextCol("SubmittedAt", "提交时间", true, 16));
        _gridHist.Columns.Add(MakeTextCol("TemplateName", "模板", true, 20));
        _gridHist.Columns.Add(MakeTextCol("Submitter", "填写人", true, 12));
        _gridHist.Columns.Add(MakeTextCol("Shift", "班次", true, 8));
        _gridHist.Columns.Add(MakeTextCol("Device", "设备/区域", true, 14));
        _gridHist.Columns.Add(MakeTextCol("Summary", "摘要", true, 30));
        _gridHist.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || _gridHist.Rows[e.RowIndex].DataBoundItem is not ChecklistSubmission sub) return;
            if (e.ColumnIndex == 0) e.Value = sub.SubmittedAt.ToString("yyyy-MM-dd HH:mm");
            if (e.ColumnIndex == 5) e.Value = sub.Summary();
        };
        _gridHist.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && _gridHist.Rows[e.RowIndex].DataBoundItem is ChecklistSubmission sub)
                ShowEArchive(sub);
        };

        _lblHistStatus = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray, Text = "设置条件后点「查询」；双击记录查看当时格式与数据的电子档。" };

        page.Controls.Add(_gridHist);
        page.Controls.Add(_lblHistStatus);
        page.Controls.Add(top);
        page.Enter += (s, ev) =>
        {
            if (_cboHistTpl.Items.Count == 0)
            {
                SyncTplCombos(PaperlessStore.LoadAllTemplates());
                OnQueryHistory(null, EventArgs.Empty);
            }
        };
        return page;
    }

    private void OnQueryHistory(object? s, EventArgs e)
    {
        var tplId = _cboHistTpl.SelectedItem is ChecklistTemplate t ? t.Id : "";
        var list = PaperlessStore.Query(new PaperlessStore.QueryFilter
        {
            TemplateId = tplId,
            Submitter = _txtHistSubmitter.Text.Trim(),
            From = _dtFrom.Value.Date,
            To = _dtTo.Value.Date,
            Keyword = _txtHistKeyword.Text.Trim(),
        });
        _gridHist.DataSource = new BindingSource { DataSource = list };
        _lblHistStatus.Text = $"共 {list.Count} 条记录。" + (list.Count > 0 ? " 双击查看电子档。" : "");
    }

    private void OnResetHistory(object? s, EventArgs e)
    {
        if (_cboHistTpl.Items.Count > 0) _cboHistTpl.SelectedIndex = 0;
        _txtHistSubmitter.Text = "";
        _dtFrom.Value = DateTime.Today.AddMonths(-1);
        _dtTo.Value = DateTime.Today;
        _txtHistKeyword.Text = "";
        OnQueryHistory(null, EventArgs.Empty);
    }

    // ---------------- 电子档还原 ----------------

    private void ShowEArchive(ChecklistSubmission sub)
    {
        using var dlg = new EArchiveForm(sub);
        dlg.ShowDialog();
    }

    // ---------------- 公共辅助 ----------------

    private void SyncTplCombos(List<ChecklistTemplate> list)
    {
        var withAll = new List<ChecklistTemplate> { null! };
        withAll.AddRange(list);
        // 填写页
        var curFill = _cboFillTpl.SelectedItem as ChecklistTemplate;
        _cboFillTpl.DataSource = null;
        _cboFillTpl.DisplayMember = "Name";
        _cboFillTpl.Items.Clear();
        _cboFillTpl.Items.AddRange(list.ToArray());
        if (curFill is not null)
        {
            var idx = list.FindIndex(t => t.Id == curFill.Id);
            if (idx >= 0) _cboFillTpl.SelectedIndex = idx;
        }
        // 查询页（含"全部"）
        var curHist = _cboHistTpl.SelectedItem as ChecklistTemplate;
        _cboHistTpl.DisplayMember = "Name";
        _cboHistTpl.Items.Clear();
        _cboHistTpl.Items.Add(new AllItem());
        _cboHistTpl.Items.AddRange(list.ToArray());
        if (curHist is not null)
        {
            var idx = list.FindIndex(t => t.Id == curHist.Id);
            if (idx >= 0) _cboHistTpl.SelectedIndex = idx + 1;
        }
        else if (_cboHistTpl.Items.Count > 0) _cboHistTpl.SelectedIndex = 0;
    }

    private static Button Btn(string text, EventHandler handler)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(2), UseVisualStyleBackColor = true };
        b.Click -= handler; b.Click += handler;
        return b;
    }

    private static Label Lbl(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(6, 6, 2, 0) };

    private static TextBox MetaBox(string placeholder, int w)
        => new() { Width = w, PlaceholderText = placeholder, Margin = new Padding(2) };

    private void TabStatus(Label lbl, string msg, bool error)
    {
        if (lbl is null) return;
        lbl.Text = msg;
        lbl.ForeColor = error ? Color.Firebrick : Color.Gray;
    }

    // 查询页 ComboBox 的"全部"占位
    private sealed class AllItem
    {
        public override string ToString() => "（全部模板）";
    }
}

/// <summary>模板名称输入对话框（保存时复用）。</summary>
internal sealed class InputDialog : Form
{
    private readonly TextBox _txt;
    public string Value => _txt.Text;
    public InputDialog(string title, string prompt, string defaultValue)
    {
        Text = title;
        Width = 360; Height = 140;
        StartPosition = FormStartPosition.CenterParent;
        var lbl = new Label { Left = 12, Top = 14, Width = 320, Text = prompt };
        _txt = new TextBox { Left = 12, Top = 44, Width = 320, Text = defaultValue };
        var ok = new Button { Text = "确定", Left = 172, Top = 80, Width = 72, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 252, Top = 80, Width = 72, DialogResult = DialogResult.Cancel };
        Controls.AddRange(new Control[] { lbl, _txt, ok, cancel });
        AcceptButton = ok; CancelButton = cancel;
    }
}

/// <summary>电子档查看窗：还原提交时刻的模板格式 + 填写值，支持导出 HTML 与打印。</summary>
internal sealed class EArchiveForm : Form
{
    private readonly ChecklistSubmission _sub;
    public EArchiveForm(ChecklistSubmission sub)
    {
        _sub = sub;
        Text = $"电子档 · {sub.TemplateName} · {sub.SubmittedAt:yyyy-MM-dd HH:mm}";
        Width = 900; Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.WhiteSmoke;

        var meta = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Height = 56, WrapContents = true, Padding = new Padding(8) };
        // 抬头字段（按提交时刻快照的字段名还原）
        var snap = sub.TemplateSnapshot ?? new ChecklistTemplate();
        foreach (var hf in snap.HeaderFields)
            meta.Controls.Add(MetaLabel(hf.Label, sub.GetHeaderValue(hf.Label)));
        meta.Controls.Add(MetaLabel("提交人", sub.Submitter));
        meta.Controls.Add(MetaLabel("班次", sub.Shift));
        meta.Controls.Add(MetaLabel("设备/区域", sub.Device));
        meta.Controls.Add(MetaLabel("备注", sub.Note));
        meta.Controls.Add(MetaLabel("模板版本锁定", "提交时刻快照（后期改模板不影响本档）"));

        var grid = BuildReadonlyGrid(sub);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Height = 38, Padding = new Padding(8, 4, 8, 4) };
        var btnHtml = new Button { Text = "🌐 导出 HTML 电子档", AutoSize = true };
        btnHtml.Click += (s, e) => ExportHtml(sub);
        var btnPrint = new Button { Text = "🖨 打印", AutoSize = true };
        btnPrint.Click += (s, e) => PrintArchive(sub, grid);
        var btnClose = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.OK };
        bar.Controls.AddRange(new Control[] { btnHtml, btnPrint, btnClose });

        Controls.Add(grid);
        if (snap.FooterNotes.Count > 0)
        {
            Controls.Add(new Label
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                AutoEllipsis = true,
                ForeColor = Color.DimGray,
                Padding = new Padding(10, 4, 10, 4),
                Text = string.Join("　", snap.FooterNotes),
            });
        }
        Controls.Add(bar);
        Controls.Add(meta);
    }

    private static FlowLayoutPanel MetaLabel(string k, string v)
    {
        var p = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 14, 0) };
        p.Controls.Add(new Label { Text = k + "：", AutoSize = true, Font = new Font("微软雅黑", 9F, FontStyle.Bold) });
        p.Controls.Add(new Label { Text = string.IsNullOrEmpty(v) ? "—" : v, AutoSize = true });
        return p;
    }

    private static DataGridView BuildReadonlyGrid(ChecklistSubmission sub)
    {
        var tpl = sub.TemplateSnapshot ?? new ChecklistTemplate();
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            ReadOnly = true,
            BackgroundColor = Color.White,
            // 按内容自适应列宽（封顶 320px）：电子档要还原可读，宁可横向滚动也不把
            // 「点检内容及标准」截成几个字；超长内容鼠标悬停仍可看全文。
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            RowHeadersVisible = false,
        };
        for (int c = 0; c < tpl.Columns.Count; c++)
        {
            var col = tpl.Columns[c];
            var dgvCol = new DataGridViewTextBoxColumn
            {
                HeaderText = col.Header,
                Name = "c" + c,
                ReadOnly = true,
                MinimumWidth = 80,
            };
            grid.Columns.Add(dgvCol);
        }
        for (int r = 0; r < tpl.Rows.Count; r++)
        {
            var idx = grid.Rows.Add();
            for (int c = 0; c < tpl.Columns.Count; c++)
            {
                var val = tpl.Columns[c].Fillable ? sub.GetValue(r, c) : Cell(tpl, r, c);
                grid.Rows[idx].Cells[c].Value = val;
                if (tpl.Columns[c].Fillable) grid.Rows[idx].Cells[c].Style.BackColor = Color.Honeydew;
            }
        }
        return grid;
    }

    private static void ExportHtml(ChecklistSubmission sub)
    {
        var tpl = sub.TemplateSnapshot ?? new ChecklistTemplate();
        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html lang='zh'><head><meta charset='utf-8'><title>")
          .Append(sub.TemplateName).Append("</title>")
          .Append("<style>body{font-family:'Microsoft YaHei',sans-serif;padding:24px}")
          .Append("h2{margin:0 0 4px}.meta{color:#555;margin-bottom:12px}")
          .Append("table{border-collapse:collapse;width:100%}th,td{border:1px solid #999;padding:6px 8px;text-align:left}")
          .Append("th{background:#f0f0f0}.fill{background:#eafbea}caption{caption-side:top;text-align:left;font-weight:bold;margin-bottom:6px}")
          .Append(".hdr{width:auto;margin-bottom:10px}.hdr th{width:110px;white-space:nowrap}")
          .Append(".foot{color:#555;margin-top:10px;font-size:13px;line-height:1.8}")
          .Append("</style></head><body>");
        sb.Append("<h2>").Append(tpl.Title).Append("</h2>");
        sb.Append("<div class='meta'>模板：").Append(sub.TemplateName)
          .Append(" ｜ 提交人：").Append(sub.Submitter)
          .Append(" ｜ 班次：").Append(sub.Shift)
          .Append(" ｜ 设备/区域：").Append(sub.Device)
          .Append(" ｜ 提交时间：").Append(sub.SubmittedAt.ToString("yyyy-MM-dd HH:mm"))
          .Append("</div>");
        // 抬头字段（车间/线别、点检日期、班次…）
        if (tpl.HeaderFields.Count > 0)
        {
            sb.Append("<table class='hdr'>");
            foreach (var hf in tpl.HeaderFields)
            {
                var hv = sub.GetHeaderValue(hf.Label);
                sb.Append("<tr><th>").Append(Esc(hf.Label)).Append("</th><td>")
                  .Append(Esc(string.IsNullOrEmpty(hv) ? "—" : hv)).Append("</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<table><caption>提交时刻模板快照 + 填写值（后期改模板不影响本档）</caption><tr>");
        foreach (var c in tpl.Columns) sb.Append("<th>").Append(Esc(c.Header)).Append("</th>");
        sb.Append("</tr>");
        for (int r = 0; r < tpl.Rows.Count; r++)
        {
            sb.Append("<tr>");
            for (int c = 0; c < tpl.Columns.Count; c++)
            {
                var val = tpl.Columns[c].Fillable ? sub.GetValue(r, c) : Cell(tpl, r, c);
                sb.Append(tpl.Columns[c].Fillable ? "<td class='fill'>" : "<td>").Append(Esc(val)).Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        if (tpl.FooterNotes.Count > 0)
            sb.Append("<div class='foot'>").Append(string.Join("<br>", tpl.FooterNotes.Select(Esc))).Append("</div>");
        sb.Append("</body></html>");

        using var dlg = new SaveFileDialog { Filter = "HTML 电子档|*.html", FileName = $"{sub.TemplateName}_{sub.SubmittedAt:yyyyMMddHHmm}.html" };
        if (dlg.ShowDialog() == DialogResult.OK)
            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(false));
    }

    private static void PrintArchive(ChecklistSubmission sub, DataGridView grid)
    {
        var tpl = sub.TemplateSnapshot ?? new ChecklistTemplate();
        var pd = new PrintDocument();
        pd.DocumentName = $"{sub.TemplateName}_{sub.SubmittedAt:yyyyMMddHHmm}";
        int row = 0, page = 0;
        pd.PrintPage += (s, e) =>
        {
            var g = e.Graphics!;
            // 列多时缩小字号，避免等分列宽后文字互相压叠
            float fs = tpl.Columns.Count > 8 ? 7.5F : 10F;
            var font = new Font("微软雅黑", fs);
            float y = e.MarginBounds.Top;
            if (page == 0)
            {
                g.DrawString(tpl.Title, new Font("微软雅黑", 16, FontStyle.Bold), Brushes.Black, e.MarginBounds.Left, y);
                y += 28;
                // 抬头字段（车间/线别、点检日期、班次…）
                if (tpl.HeaderFields.Count > 0)
                {
                    var hdr = string.Join("　　", tpl.HeaderFields.Select(h =>
                    {
                        var hv = sub.GetHeaderValue(h.Label);
                        return $"{h.Label}：{(string.IsNullOrEmpty(hv) ? "—" : hv)}";
                    }));
                    g.DrawString(hdr, font, Brushes.Black, e.MarginBounds.Left, y);
                    y += 24;
                }
                g.DrawString($"提交人：{sub.Submitter}　班次：{sub.Shift}　设备：{sub.Device}　时间：{sub.SubmittedAt:yyyy-MM-dd HH:mm}",
                    font, Brushes.Gray, e.MarginBounds.Left, y);
                y += 22;
            }
            // 表头
            float x = e.MarginBounds.Left;
            float[] widths = new float[tpl.Columns.Count];
            float totalW = e.MarginBounds.Width;
            for (int c = 0; c < tpl.Columns.Count; c++)
                widths[c] = totalW / tpl.Columns.Count;
            if (row == 0)
            {
                for (int c = 0; c < tpl.Columns.Count; c++)
                {
                    g.DrawString(tpl.Columns[c].Header, font, Brushes.Black, x, y);
                    x += widths[c];
                }
                y += 20; g.DrawLine(Pens.Gray, e.MarginBounds.Left, y, e.MarginBounds.Right, y); y += 4;
            }
            while (row < tpl.Rows.Count && y < e.MarginBounds.Bottom - 20)
            {
                x = e.MarginBounds.Left;
                for (int c = 0; c < tpl.Columns.Count; c++)
                {
                    var val = tpl.Columns[c].Fillable ? sub.GetValue(row, c) : Cell(tpl, row, c);
                    g.DrawString(val, font, Brushes.Black, x, y);
                    x += widths[c];
                }
                y += 20; row++;
            }
            page++;
            e.HasMorePages = row < tpl.Rows.Count;
            // 表尾说明（点检说明/签字要求）画在最后一页底部
            if (!e.HasMorePages && tpl.FooterNotes.Count > 0 && y < e.MarginBounds.Bottom - 28)
            {
                y += 10;
                var noteFont = new Font("微软雅黑", 8.5F);
                var rect = new RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, e.MarginBounds.Bottom - y);
                g.DrawString(string.Join("\n", tpl.FooterNotes), noteFont, Brushes.DimGray, rect);
            }
        };
        using var ppd = new PrintPreviewDialog { Document = pd, Width = 800, Height = 600 };
        ppd.ShowDialog();
    }

    /// <summary>取上下文列文本（提交时锁定的快照），null 安全。</summary>
    private static string Cell(ChecklistTemplate tpl, int r, int c) =>
        r < tpl.Rows.Count && tpl.Rows[r] is { } row && c < row.Cells.Count ? row.Cells[c] : "";

    private static string Esc(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>批量填充的作用范围。</summary>
public enum BulkFillScope
{
    /// <summary>全部数据行（会覆盖已填值）</summary>
    All,
    /// <summary>仅空白单元格（推荐，不覆盖手填内容）</summary>
    BlankOnly,
    /// <summary>仅当前选中的行</summary>
    Selected,
}

/// <summary>
/// 批量填充对话框：选列 → 选值 → 选范围。
/// 默认「仅空白行」，避免一次误操作把手填结果全盖掉。
/// </summary>
internal sealed class BulkFillDialog : Form
{
    private readonly ChecklistTemplate _tpl;
    private readonly ComboBox _cboCol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 92, Top = 16, Width = 320 };
    private readonly Panel _valueHost = new() { Left = 92, Top = 52, Width = 320, Height = 28 };
    private readonly ComboBox _cboScope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 92, Top = 92, Width = 320 };
    private readonly Label _lblHint = new() { Left = 14, Top = 126, Width = 398, Height = 40, ForeColor = Color.DimGray };

    private readonly TextBox _txtValue = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox _cboValue = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkValue = new() { Dock = DockStyle.Fill, Text = "勾选（是）" };

    /// <summary>选中的可填列索引（对应模板 Columns 下标）</summary>
    public int ColumnIndex => _cboCol.SelectedItem is ColumnItem ci ? ci.Index : -1;

    /// <summary>要写入的值（Checkbox 列为「是/否」）</summary>
    public string Value { get; private set; } = "";

    public BulkFillScope Scope => _cboScope.SelectedIndex switch
    {
        1 => BulkFillScope.BlankOnly,
        2 => BulkFillScope.Selected,
        _ => BulkFillScope.All,
    };

    public BulkFillDialog(ChecklistTemplate tpl, int presetColumn, int selectedRowCount)
    {
        _tpl = tpl;
        Text = "批量填充（同列多行一次填）";
        Width = 440;
        Height = 250;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.WhiteSmoke;

        Controls.Add(new Label { Text = "列：", Left = 14, Top = 19, Width = 70, AutoSize = false });
        Controls.Add(new Label { Text = "值：", Left = 14, Top = 56, Width = 70, AutoSize = false });
        Controls.Add(new Label { Text = "范围：", Left = 14, Top = 95, Width = 70, AutoSize = false });

        // 只列可填列（上下文列不可填）
        foreach (var c in tpl.Columns.Select((c, i) => (c, i)).Where(x => x.c.Fillable))
            _cboCol.Items.Add(new ColumnItem(c.i, $"{c.c.Header}（{c.c.KindText}）"));
        if (_cboCol.Items.Count > 0)
        {
            var pick = 0;
            if (presetColumn >= 0)
            {
                var idx = _cboCol.Items.Cast<ColumnItem>().ToList().FindIndex(x => x.Index == presetColumn);
                if (idx >= 0) pick = idx;
            }
            _cboCol.SelectedIndex = pick;
        }
        _cboCol.SelectedIndexChanged += (s, e) => RebuildValueControl();

        _cboScope.Items.AddRange(new object[] { "全部行", "仅空白行（推荐，不覆盖手填）", "仅当前选中的行" });
        _cboScope.SelectedIndex = 1;
        if (selectedRowCount <= 0)
        {
            // 没有选中行时该选项无意义，直接禁用并标注
            _cboScope.Items[2] = "仅当前选中的行（未选中）";
        }

        _valueHost.Controls.Add(_txtValue);
        _valueHost.Controls.Add(_cboValue);
        _valueHost.Controls.Add(_chkValue);

        Controls.Add(_cboCol);
        Controls.Add(_valueHost);
        Controls.Add(_cboScope);
        Controls.Add(_lblHint);

        var ok = new Button { Text = "确定填充", Left = 236, Top = 172, Width = 88, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 330, Top = 172, Width = 72, DialogResult = DialogResult.Cancel };
        ok.Click += (s, e) => { Value = ReadValue(); };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        RebuildValueControl();
    }

    private void RebuildValueControl()
    {
        var kind = _cboCol.SelectedItem is ColumnItem ci ? _tpl.Columns[ci.Index].Kind : FieldKind.Text;
        _txtValue.Visible = kind is FieldKind.Text or FieldKind.Multiline or FieldKind.Number or FieldKind.Date or FieldKind.Signature;
        _cboValue.Visible = kind == FieldKind.Select;
        _chkValue.Visible = kind == FieldKind.Checkbox;

        if (kind == FieldKind.Select)
        {
            var opts = _cboCol.SelectedItem is ColumnItem ci2 ? _tpl.Columns[ci2.Index].Options : new List<string>();
            _cboValue.Items.Clear();
            _cboValue.Items.AddRange(opts.Cast<object>().ToArray());
            if (_cboValue.Items.Count > 0) _cboValue.SelectedIndex = 0;
        }

        _lblHint.Text = kind switch
        {
            FieldKind.Select => "该列为下拉列，只能选候选项；分组合并的同类项目可一次选完。",
            FieldKind.Signature => "签名列最常用：一个人点检几十项，名字一次填满。\n之后仍可单独改某几行。",
            FieldKind.Number => "该列为数值列，仅接受数字；提交时还会再校验一次。",
            FieldKind.Date => "该列为日期列，建议 yyyy-MM-dd；提交时还会再校验一次。",
            _ => "提示：默认只填空白行，已手填的内容不会被覆盖。",
        };
    }

    private string ReadValue()
    {
        var kind = _cboCol.SelectedItem is ColumnItem ci ? _tpl.Columns[ci.Index].Kind : FieldKind.Text;
        return kind switch
        {
            FieldKind.Select => (_cboValue.SelectedItem?.ToString() ?? "").Trim(),
            FieldKind.Checkbox => _chkValue.Checked ? "是" : "否",
            _ => _txtValue.Text.Trim(),
        };
    }

    /// <summary>下拉列表项：显示列名，携带真实列下标。</summary>
    private sealed record ColumnItem(int Index, string Label)
    {
        public override string ToString() => Label;
    }
}

/// <summary>
/// 多工作表选择对话框。
/// 真实点检文件经常一个文件里塞「点检表 / 点检记录台账 / 异常问题追踪表」，
/// 默认只读第一个会让用户误以为"后面的表也导进来了"——必须先让人选清楚。
/// </summary>
internal sealed class SheetPickerDialog : Form
{
    private readonly ComboBox _cbo = new() { Left = 14, Top = 46, Width = 386, DropDownStyle = ComboBoxStyle.DropDownList };

    public int SelectedIndex => _cbo.SelectedIndex < 0 ? 0 : _cbo.SelectedIndex;

    public SheetPickerDialog(List<PaperlessImport.SheetRef> sheets)
    {
        Text = "选择要导入的工作表";
        Width = 434;
        Height = 156;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.WhiteSmoke;

        Controls.Add(new Label
        {
            Left = 14,
            Top = 16,
            Width = 392,
            Height = 22,
            Text = $"该文件有 {sheets.Count} 个工作表，请选择要解析成点检表的那一个：",
        });
        foreach (var s in sheets) _cbo.Items.Add($"{s.Index + 1}. {s.Name}");
        if (_cbo.Items.Count > 0) _cbo.SelectedIndex = 0;
        Controls.Add(_cbo);

        var ok = new Button { Text = "确定", Left = 240, Top = 84, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 324, Top = 84, Width = 76, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
