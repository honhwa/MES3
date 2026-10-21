using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// 「原样渲染」两个新页面（与原有三页完全独立，不改动它们的任何逻辑）：
///   · 原样预览：导入 Excel → 按原排版完整绘制（合并/列宽/行高/字体/边框/填充/对齐/数字格式）
///   · 原样填写：选带样式的模板 → 在单元格内直接填 → 提交时把"那一刻的样子"整页存成归档
/// </summary>
public sealed partial class MainForm
{
    // ====== 原样预览 ======
    private HtmlSheetView _htmlPreview = null!;
    private ComboBox _cboHtmlSheet = null!;
    private Label _lblHtmlFile = null!;
    private Label _lblHtmlStatus = null!;
    private Button _btnHtmlSaveTpl = null!;
    private StyledSheet? _styledSheet;
    private ChecklistTemplate? _styledTpl;
    private string? _styledPath;
    private int _styledSheetIndex;
    private bool _htmlSheetLoading;

    // ====== 原样填写 ======
    private HtmlSheetView _htmlFill = null!;
    private ComboBox _cboHtmlTpl = null!;
    private ComboBox _cboHtmlSheetFill = null!;
    private bool _htmlSheetSwitching;
    private TextBox _txtHtmlSubmitter = null!;
    private Label _lblHtmlFillStatus = null!;
    private DataGridView _gridHtmlArchive = null!;

    private static string HtmlArchiveDir => Path.Combine(PaperlessStore.DataRoot, "html-archive");

    // ==================== 原样预览 ====================

    internal TabPage BuildPaperlessHtmlPreviewTab()
    {
        var page = new TabPage("原样预览")
        {
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(10),
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Height = 34, WrapContents = false };
        top.Controls.Add(Btn("📥 导入文件（保留原排版）", OnHtmlImport));
        _lblHtmlFile = new Label
        {
            AutoSize = true,
            Margin = new Padding(8, 8, 4, 0),
            ForeColor = Color.DimGray,
            Text = "未选择文件",
        };
        top.Controls.Add(_lblHtmlFile);
        top.Controls.Add(new Label { Text = "工作表：", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        _cboHtmlSheet = new ComboBox { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
        _cboHtmlSheet.SelectedIndexChanged += (s, e) =>
        {
            if (_htmlSheetLoading || _cboHtmlSheet.SelectedIndex < 0) return;
            _ = LoadStyledAsync(_cboHtmlSheet.SelectedIndex);
        };
        top.Controls.Add(_cboHtmlSheet);

        _btnHtmlSaveTpl = Btn("💾 保存为模板（含样式）", OnHtmlSaveTemplate);
        _btnHtmlSaveTpl.Enabled = false;
        _tplTip.SetToolTip(_btnHtmlSaveTpl, "把当前原样渲染结果连同语义结构一起存成模板，「原样填写」页才能选到它。");
        top.Controls.Add(_btnHtmlSaveTpl);
        top.Controls.Add(Btn("🌐 导出 HTML", OnHtmlExport));

        _htmlPreview = new HtmlSheetView { Dock = DockStyle.Fill };
        _lblHtmlStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            ForeColor = Color.Gray,
            AutoEllipsis = true,
            Padding = new Padding(4, 6, 4, 4),
            Text = "就绪。点「📥 导入文件」选择 Excel 点检表，系统按原文件排版完整绘制（不改动原有三页）。",
        };

        page.Controls.Add(_htmlPreview);
        page.Controls.Add(_lblHtmlStatus);
        page.Controls.Add(top);
        return page;
    }

    private async void OnHtmlImport(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Excel 工作簿|*.xlsx;*.xlsm|Word 文档|*.docx",
            Title = "选择要按原样渲染的文件",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        var path = dlg.FileName;
        if (Path.GetExtension(path).ToLowerInvariant() is ".doc" or ".docx")
        {
            _styledPath = path;
            _lblHtmlFile.Text = Path.GetFileName(path);
            TabStatus(_lblHtmlStatus,
                "⚠ Word 的原样渲染还没做：现在只解析了文字，表格的列宽/合并/底纹（w:tblGrid、gridSpan、vMerge、tcPr）还没读。" +
                "本期先把 Excel 打通，Word 放下一期——不想给你一个「看着像但错」的效果。", true);
            return;
        }

        _styledPath = path;
        _lblHtmlFile.Text = Path.GetFileName(path);

        var sheets = PaperlessImport.ListSheets(path);
        _htmlSheetLoading = true;
        try
        {
            _cboHtmlSheet.Items.Clear();
            foreach (var sh in sheets) _cboHtmlSheet.Items.Add($"{sh.Index + 1}. {sh.Name}");
            _cboHtmlSheet.Enabled = sheets.Count > 1;
            if (_cboHtmlSheet.Items.Count > 0) _cboHtmlSheet.SelectedIndex = 0;
        }
        finally { _htmlSheetLoading = false; }

        await LoadStyledAsync(0);
    }

    /// <summary>解析样式 + 渲染。异步跑解析，避免大文件卡界面。</summary>
    private async Task LoadStyledAsync(int sheetIndex)
    {
        if (_styledPath is null) return;

        _styledSheetIndex = sheetIndex;
        _btnHtmlSaveTpl.Enabled = false;
        TabStatus(_lblHtmlStatus, "正在解析排版与样式（合并 / 列宽 / 行高 / 字体 / 边框 / 填充）…", false);

        var path = _styledPath;
        StyledSheet? sheet = null;
        ChecklistTemplate? tpl = null;
        string? err = null;
        try
        {
            (sheet, tpl) = await Task.Run(() =>
            {
                var st = StyledSheetReader.Load(path, sheetIndex);
                var t = PaperlessImport.Import(path, null, sheetIndex);
                return (st, t);
            });
        }
        catch (Exception ex) { err = ex.Message; }

        if (err is not null || sheet is null || tpl is null)
        {
            TabStatus(_lblHtmlStatus, "❌ 解析失败：" + (err ?? "读不到表格结构"), true);
            return;
        }

        StyledSheetReader.AttachSemantics(sheet, tpl);   // 该表自带列语义，填写页才能逐张出控件
        _styledSheet = sheet;
        _styledTpl = tpl;

        var html = SheetHtmlRenderer.Render(sheet, new HtmlRenderOptions
        {
            Title = SheetHtmlRenderer.SheetTitle(sheet, tpl.Title),
            Subtitle = $"{Path.GetFileName(path)} ｜ 工作表：{sheet.SheetName} ｜ {sheet.ColCount} 列 × {sheet.RowCount} 行",
        });
        _htmlPreview.ShowHtml(html);
        _btnHtmlSaveTpl.Enabled = sheet.RowCount > 0 && sheet.ColCount > 0;

        int nonDefault = sheet.Styles.Count(s => !s.IsDefault);
        TabStatus(_lblHtmlStatus,
            $"✅ 已按原排版渲染：{sheet.ColCount} 列 × {sheet.RowCount} 行，合并 {sheet.Merges.Count} 处，" +
            $"样式 {sheet.Styles.Count} 种（其中非默认 {nonDefault} 种）；可填列 {tpl.FillableColumns.Count} 个。" +
            $" 保存模板时会把该文件的**全部 {PaperlessImport.ListSheets(path).Count} 个工作表**一并存下，" +
            "到「原样填写」页可逐张切换填写。",
            false);
    }

    private void OnHtmlSaveTemplate(object? s, EventArgs e)
    {
        if (_styledTpl is null || _styledSheet is null)
        {
            TabStatus(_lblHtmlStatus, "没有可保存的内容：先导入文件。", true);
            return;
        }
        using var dlg = new InputDialog("模板名称", "请输入模板名称（原样填写页会用到）：", _styledTpl.Name);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        var tpl = _styledTpl.Clone();
        tpl.Name = string.IsNullOrWhiteSpace(dlg.Value) ? tpl.Title : dlg.Value.Trim();

        // 关键：存**全部**工作表，而不是只存当前这一张。
        // 只存第一张会让「记录台账 / 异常问题追踪表」在填写页里彻底消失（本次修复的问题）。
        var all = _styledPath is not null
            ? StyledSheetReader.LoadAll(_styledPath)
            : new List<StyledSheet>();
        if (all.Count == 0 && _styledSheet is not null) all.Add(_styledSheet);
        tpl.Sheets = all;
        tpl.Style = all.FirstOrDefault() ?? _styledSheet;

        PaperlessStore.SaveTemplate(tpl);
        CallVoidRefreshTplList();
        TabStatus(_lblHtmlStatus,
            $"✅ 已保存模板「{tpl.Name}」：含 {all.Count} 个工作表（{string.Join("、", all.Select(x => x.SheetName))}）。" +
            " 去「✍ 原样填写」页可逐张切换填写。", false);
    }

    private void OnHtmlExport(object? s, EventArgs e)
    {
        if (_styledSheet is null)
        {
            TabStatus(_lblHtmlStatus, "没有可导出的内容：先导入文件。", true);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Filter = "HTML 文件|*.html",
            FileName = (_styledTpl?.Title ?? "点检表") + "_原样.html",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var html = SheetHtmlRenderer.Render(_styledSheet, new HtmlRenderOptions
        {
            Title = _styledTpl?.Title ?? "",
        });
        File.WriteAllText(dlg.FileName, html, new UTF8Encoding(false));
        TabStatus(_lblHtmlStatus, "✅ 已导出：" + dlg.FileName + "（自包含文件，双击即可用浏览器打开/打印）", false);
    }

    // ==================== 原样填写 ====================

    internal TabPage BuildPaperlessHtmlFillTab()
    {
        var page = new TabPage("原样填写")
        {
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(10),
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Height = 34, WrapContents = false };
        top.Controls.Add(new Label { Text = "模板：", AutoSize = true, Margin = new Padding(0, 8, 2, 0) });
        // 模板下拉：Items 里放的是 ChecklistTemplate 对象，显示文本由它的 ToString() 决定。
        // DropDownWidth 必须放大到比控件宽，否则长模板名展开时被截断（看起来像"内容缺失/被遮挡"）。
        _cboHtmlTpl = new ComboBox
        {
            Width = 280,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownWidth = 460,
            MaxDropDownItems = 12,
            IntegralHeight = false,
        };
        _cboHtmlTpl.SelectedIndexChanged += (s, e) => ReloadHtmlFill();
        top.Controls.Add(_cboHtmlTpl);

        // 工作表下拉：多工作表模板必须能逐张切换（本次修复的核心）
        top.Controls.Add(new Label { Text = "工作表：", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        _cboHtmlSheetFill = new ComboBox
        {
            Width = 180,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownWidth = 300,
            IntegralHeight = false,
        };
        _cboHtmlSheetFill.SelectedIndexChanged += (s, e) =>
        {
            if (_htmlSheetSwitching) return;
            RenderCurrentSheet();
        };
        top.Controls.Add(_cboHtmlSheetFill);
        top.Controls.Add(new Label { Text = "填写人：", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        _txtHtmlSubmitter = new TextBox { Width = 90, Margin = new Padding(2, 5, 2, 0) };
        top.Controls.Add(_txtHtmlSubmitter);
        top.Controls.Add(Btn("🔄 重载", (s, e) => ReloadHtmlFill()));
        top.Controls.Add(Btn("✅ 提交并归档", OnHtmlSubmit));

        _htmlFill = new HtmlSheetView { Dock = DockStyle.Fill };

        _gridHtmlArchive = new DataGridView
        {
            Dock = DockStyle.Bottom,
            Height = 132,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _gridHtmlArchive.Columns.Add(MakeTextCol("At", "归档时间", true, 16));
        _gridHtmlArchive.Columns.Add(MakeTextCol("TemplateName", "模板", true, 26));
        _gridHtmlArchive.Columns.Add(MakeTextCol("SheetName", "工作表", true, 16));
        _gridHtmlArchive.Columns.Add(MakeTextCol("Submitter", "填写人", true, 12));
        _gridHtmlArchive.Columns.Add(MakeTextCol("Filled", "填写项", true, 10));
        _gridHtmlArchive.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && _gridHtmlArchive.Rows[e.RowIndex].DataBoundItem is HtmlArchiveItem it)
                ShowHtmlArchive(it);
        };

        _lblHtmlFillStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            ForeColor = Color.Gray,
            AutoEllipsis = true,
            Padding = new Padding(4, 6, 4, 4),
            Text = "选一个模板开始填写。只列「原样预览」页保存过、带样式的模板。下方是已归档记录，双击查看当时的样子。",
        };

        page.Controls.Add(_htmlFill);
        page.Controls.Add(_gridHtmlArchive);
        page.Controls.Add(_lblHtmlFillStatus);
        page.Controls.Add(top);

        page.Enter += (s, e) => RefreshHtmlTplCombo();
        return page;
    }

    private void RefreshHtmlTplCombo()
    {
        var all = PaperlessStore.LoadAllTemplates().Where(t => t.Style is not null).ToList();
        var cur = _cboHtmlTpl.SelectedItem as ChecklistTemplate;
        _cboHtmlTpl.Items.Clear();
        foreach (var t in all) _cboHtmlTpl.Items.Add(t);
        if (_cboHtmlTpl.Items.Count == 0)
        {
            TabStatus(_lblHtmlFillStatus,
                "还没有带样式的模板：先到「📄 原样预览」页导入文件 →「💾 保存为模板（含样式）」。", true);
        }
        else
        {
            var idx = cur is null ? 0 : Math.Max(0, all.FindIndex(t => t.Id == cur.Id));
            _cboHtmlTpl.SelectedIndex = idx;
        }
        LoadHtmlArchiveList();
    }

    /// <summary>切模板：同步工作表下拉后渲染第一张。</summary>
    private void ReloadHtmlFill()
    {
        if (_cboHtmlTpl.SelectedItem is not ChecklistTemplate tpl) return;

        var sheets = tpl.AllSheets;
        if (sheets.Count == 0)
        {
            TabStatus(_lblHtmlFillStatus, "该模板没有原样渲染数据：请到「📄 原样预览」重新导入并保存模板。", true);
            return;
        }

        _htmlSheetSwitching = true;
        try
        {
            _cboHtmlSheetFill.Items.Clear();
            foreach (var sh in sheets)
                _cboHtmlSheetFill.Items.Add(string.IsNullOrWhiteSpace(sh.SheetName) ? "(未命名工作表)" : sh.SheetName);
            _cboHtmlSheetFill.Enabled = sheets.Count > 1;
            if (_cboHtmlSheetFill.Items.Count > 0) _cboHtmlSheetFill.SelectedIndex = 0;
        }
        finally { _htmlSheetSwitching = false; }

        RenderCurrentSheet();
    }

    /// <summary>渲染当前选中的工作表（多工作表时随下拉切换）。</summary>
    private void RenderCurrentSheet()
    {
        if (_cboHtmlTpl.SelectedItem is not ChecklistTemplate tpl) return;
        var sheets = tpl.AllSheets;
        if (sheets.Count == 0) return;

        int idx = Math.Clamp(_cboHtmlSheetFill.SelectedIndex, 0, sheets.Count - 1);
        var sheet = sheets[idx];

        var html = SheetHtmlRenderer.Render(sheet, new HtmlRenderOptions
        {
            Editable = true,
            Template = tpl,                 // 兼容旧数据；列语义实际走 sheet.Columns
            Title = SheetHtmlRenderer.SheetTitle(sheet, tpl.Title),
            Subtitle = $"{tpl.Name} ｜ 工作表：{sheet.SheetName} ｜ 直接在原表单元格内填写",
        });
        _htmlFill.ShowHtml(html);

        var tail = sheets.Count > 1
            ? $" 当前第 {idx + 1}/{sheets.Count} 张「{sheet.SheetName}」，可用左上「工作表」下拉切换。"
            : "";
        if (!sheet.CanFill)
            TabStatus(_lblHtmlFillStatus,
                $"⚠ 工作表「{sheet.SheetName}」没有可填写的数据行（{sheet.ColCount} 列 × 0 行，多是空白台账），" +
                $"只能查看不能填。{tail}", true);
        else
            TabStatus(_lblHtmlFillStatus,
                $"已在原表上生成填写页：{sheet.SheetName}，{sheet.RowCount} 行 × {sheet.ColCount} 列，" +
                $"可填列 {sheet.Columns.Count(c => c.Fillable)} 个。{tail} 填完点「✅ 提交并归档」。", false);
    }

    /// <summary>当前选中的工作表（提交/归档时用）。</summary>
    private StyledSheet? CurrentFillSheet()
    {
        if (_cboHtmlTpl.SelectedItem is not ChecklistTemplate tpl) return null;
        var sheets = tpl.AllSheets;
        if (sheets.Count == 0) return null;
        int idx = Math.Clamp(_cboHtmlSheetFill.SelectedIndex, 0, sheets.Count - 1);
        return sheets[idx];
    }

    private async void OnHtmlSubmit(object? s, EventArgs e)
    {
        if (_cboHtmlTpl.SelectedItem is not ChecklistTemplate tpl || tpl.AllSheets.Count == 0)
        {
            TabStatus(_lblHtmlFillStatus, "请先选择一个带样式的模板。", true);
            return;
        }
        var sheet = CurrentFillSheet();
        if (sheet is null)
        {
            TabStatus(_lblHtmlFillStatus, "请先选择一个工作表。", true);
            return;
        }
        if (!sheet.CanFill)
        {
            TabStatus(_lblHtmlFillStatus,
                $"工作表「{sheet.SheetName}」没有可填写的数据行，不能提交。请切换到有数据的工作表。", true);
            return;
        }
        var submitter = _txtHtmlSubmitter.Text.Trim();
        if (submitter.Length == 0)
        {
            TabStatus(_lblHtmlFillStatus, "请先填「填写人」——归档件上要留责任人。", true);
            return;
        }

        var json = await _htmlFill.CollectValuesJsonAsync();
        if (json is null)
        {
            TabStatus(_lblHtmlFillStatus, "⚠ 读不到填写值：WebView2 未就绪（检查运行时可装好）。", true);
            return;
        }

        Dictionary<string, string> values;
        try { values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (Exception ex)
        {
            TabStatus(_lblHtmlFillStatus, "⚠ 解析填写值失败：" + ex.Message, true);
            return;
        }

        var filled = values.Count(kv => kv.Value.Length > 0);

        // 必填列整体校验：至少要有值（逐行校验需要表体行号映射，放到语义页更合适；
        // 原样页的价值在"像原件"，不在强迫每格都填）
        var emptyRequired = sheet.Columns.Select((c, i) => (c, i))
            .Where(x => x.c.Fillable && x.c.Required)
            .Where(x => !values.Any(kv => kv.Key.EndsWith($"c{x.i}", StringComparison.Ordinal) && kv.Value.Length > 0))
            .Select(x => x.c.Header).ToList();
        if (emptyRequired.Count > 0)
        {
            TabStatus(_lblHtmlFillStatus, "提交未通过：以下必填列一个都没填 —— " + string.Join("、", emptyRequired), true);
            return;
        }

        // 归档：把"提交那一刻的样子"整页存成自包含 HTML（含填好的值），之后打开永远是当时的样子
        var at = DateTime.Now;
        var id = at.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        var html = SheetHtmlRenderer.Render(sheet, new HtmlRenderOptions
        {
            Editable = false,
            Template = tpl,
            Values = values,
            Title = SheetHtmlRenderer.SheetTitle(sheet, tpl.Title),
            Subtitle = $"模板：{tpl.Name} ｜ 工作表：{sheet.SheetName} ｜ 填写人：{submitter} ｜ 归档时间：{at:yyyy-MM-dd HH:mm}",
        });

        try
        {
            Directory.CreateDirectory(HtmlArchiveDir);
            File.WriteAllText(Path.Combine(HtmlArchiveDir, id + ".html"), html, new UTF8Encoding(false));
            var list = LoadHtmlArchiveMeta();
            list.Insert(0, new HtmlArchiveItem
            {
                Id = id,
                TemplateName = tpl.Name,
                SheetName = sheet.SheetName,
                Submitter = submitter,
                At = at,
                Filled = filled,
            });
            File.WriteAllText(Path.Combine(HtmlArchiveDir, "index.json"),
                JsonSerializer.Serialize(list, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            TabStatus(_lblHtmlFillStatus, "❌ 归档失败：" + ex.Message, true);
            return;
        }

        LoadHtmlArchiveList();
        TabStatus(_lblHtmlFillStatus,
            $"✅ 已归档（{id}）：工作表「{sheet.SheetName}」，{filled} 个填写项，填写人 {submitter}。" +
            " 归档件是「那一刻的整页快照」，之后改模板也不影响它。", false);
    }

    private void ShowHtmlArchive(HtmlArchiveItem it)
    {
        var p = Path.Combine(HtmlArchiveDir, it.Id + ".html");
        if (!File.Exists(p))
        {
            TabStatus(_lblHtmlFillStatus, "⚠ 归档文件不存在：" + p, true);
            return;
        }
        _htmlFill.ShowHtml(File.ReadAllText(p, Encoding.UTF8));
        TabStatus(_lblHtmlFillStatus,
            $"正在查看归档 {it.Id}（{it.Submitter}，{it.At:yyyy-MM-dd HH:mm}）——这是提交那一刻的样子，只读。" +
            " 点「🔄 重载」回到填写。", false);
    }

    private void LoadHtmlArchiveList()
    {
        _gridHtmlArchive.DataSource = new BindingSource { DataSource = LoadHtmlArchiveMeta() };
    }

    private static List<HtmlArchiveItem> LoadHtmlArchiveMeta()
    {
        try
        {
            var p = Path.Combine(HtmlArchiveDir, "index.json");
            if (!File.Exists(p)) return new List<HtmlArchiveItem>();
            return JsonSerializer.Deserialize<List<HtmlArchiveItem>>(File.ReadAllText(p, Encoding.UTF8))
                   ?? new List<HtmlArchiveItem>();
        }
        catch { return new List<HtmlArchiveItem>(); }
    }

    /// <summary>让原样预览页保存后，其它页的模板下拉也能看到（不改动原有逻辑，只借用它已有的刷新）。</summary>
    private void CallVoidRefreshTplList()
    {
        try { RefreshTplList(); } catch { /* 原页面未初始化时忽略 */ }
    }
}

/// <summary>原样归档的一条元信息（归档正文是同名的 .html 文件）。</summary>
internal sealed class HtmlArchiveItem
{
    public string Id { get; set; } = "";
    public string TemplateName { get; set; } = "";
    /// <summary>该归档对应的工作表（多工作表模板才有区分度；旧数据为空）</summary>
    public string SheetName { get; set; } = "";
    public string Submitter { get; set; } = "";
    public DateTime At { get; set; }
    public int Filled { get; set; }
}
