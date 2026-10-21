using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

/// <summary>
/// 导入解析出的原始表结构。真实点检表不是「从第一行起就是表」：
/// 上方有标题与抬头信息（车间/线别、点检日期、班次…），中间才是表体，
/// 下方还有说明/签字行。这里按这四段拆开，避免把抬头行当表头。
/// </summary>
public sealed class ImportedSheet
{
    /// <summary>表标题（通常是最上方跨整行的合并单元格）</summary>
    public string Title { get; set; } = "";

    /// <summary>抬头字段（表头上方的信息行，按「标签：提示」拆分）</summary>
    public List<ChecklistHeaderField> HeaderFields { get; set; } = new();

    /// <summary>表体：第 0 行为表头，其余为数据行（已按列数对齐，纵向合并已展开到每行）</summary>
    public List<List<string>> Grid { get; set; } = new();

    /// <summary>表体之后的说明 / 签字行（按原行合并为文本）</summary>
    public List<string> FooterNotes { get; set; } = new();

    /// <summary>开头被剥离的标题行数（0 或 1）。渲染"原样页"时要用它换算回原文件行号。</summary>
    public int TitleRowCount { get; set; }

    /// <summary>表体第一行在**原文件网格**里的 0 基行号（标题 + 抬头 + 表头之后的下一行）</summary>
    public int BodyStartRow { get; set; }
}

/// <summary>
/// 现场无纸化：Excel/Word 点检表导入解析。
/// 零外部依赖——xlsx/docx 本质都是 ZIP 包内的 XML，用框架自带的
/// System.IO.Compression + System.Xml.Linq 直接读。
/// </summary>
public static class PaperlessImport
{
    private const string NsSheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsDoc = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>从文件导入：按扩展名分流到 xlsx/docx 解析，再推断列类型生成模板。</summary>
    /// <param name="sheetIndex">工作表序号（0 基）；真实点检文件常有「台账/追踪表」多个 sheet</param>
    public static ChecklistTemplate Import(string filePath, string? templateName = null, int sheetIndex = 0)
    {
        var sheet = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" => ParseXlsx(filePath, sheetIndex),
            ".docx" => ParseDocx(filePath),
            _ => throw new NotSupportedException($"不支持的文件类型：{Path.GetExtension(filePath)}（仅支持 .xlsx / .docx）")
        };

        var name = string.IsNullOrWhiteSpace(templateName)
            ? Path.GetFileNameWithoutExtension(filePath)
            : templateName.Trim();

        return BuildTemplate(name, Path.GetFileName(filePath), sheet);
    }

    // ---------------- 原始表（规则解析与 AI 复核共用同一份数据） ----------------

    /// <summary>工作表引用：序号 + 显示名 + 包内路径</summary>
    public sealed record SheetRef(int Index, string Name, string EntryPath);

    /// <summary>列出文件里的全部工作表（xlsx 按工作簿顺序；docx 视为单表）。失败返回单表兜底。</summary>
    public static List<SheetRef> ListSheets(string filePath)
    {
        try
        {
            if (Path.GetExtension(filePath).ToLowerInvariant() is not (".xlsx" or ".xlsm"))
                return new List<SheetRef> { new(0, "(Word 表格)", "word/document.xml") };
            using var zip = ZipFile.OpenRead(filePath);
            return ListSheets(zip);
        }
        catch
        {
            return new List<SheetRef> { new(0, "Sheet1", "xl/worksheets/sheet1.xml") };
        }
    }

    private static List<SheetRef> ListSheets(ZipArchive zip)
    {
        var list = new List<SheetRef>();
        var ns = XNamespace.Get(NsSheet);
        var relNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        // workbook.xml.rels：rId → 包内路径
        var rels = new Dictionary<string, string>();
        var relEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relEntry is not null)
        {
            try
            {
                using var s = relEntry.Open();
                var relDoc = XDocument.Load(s);
                foreach (var r in relDoc.Descendants().Where(e => e.Name.LocalName == "Relationship"))
                {
                    var id = r.Attribute("Id")?.Value;
                    var target = r.Attribute("Target")?.Value;
                    if (id is null || target is null) continue;
                    rels[id] = target.StartsWith('/')
                        ? target.TrimStart('/')
                        : "xl/" + target.TrimStart('.', '/');
                }
            }
            catch { /* rels 坏了就走兜底路径 */ }
        }

        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry is not null)
        {
            try
            {
                using var s = wbEntry.Open();
                var wb = XDocument.Load(s);
                int i = 0;
                foreach (var sh in wb.Descendants(ns + "sheet"))
                {
                    var name = sh.Attribute("name")?.Value ?? $"Sheet{i + 1}";
                    var rid = sh.Attribute(relNs + "id")?.Value;
                    var path = rid is not null && rels.TryGetValue(rid, out var p)
                        ? p
                        : $"xl/worksheets/sheet{i + 1}.xml";
                    list.Add(new SheetRef(i++, name, path));
                }
            }
            catch { /* 忽略，下面兜底 */ }
        }

        if (list.Count == 0)
            list.Add(new SheetRef(0, "Sheet1", "xl/worksheets/sheet1.xml"));
        return list;
    }

    /// <summary>读取原始表（不做标题剥离与结构切分）。失败返回 null，不抛异常。</summary>
    public static RawSheet? LoadRaw(string filePath, int sheetIndex = 0)
    {
        try
        {
            return Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".xlsx" or ".xlsm" => ReadXlsx(filePath, sheetIndex),
                ".docx" => ReadDocxRaw(filePath),
                _ => null,
            };
        }
        catch { return null; }
    }

    // ---------------- XLSX ----------------

    private static RawSheet? ReadXlsx(string file, int sheetIndex)
    {
        using var zip = ZipFile.OpenRead(file);
        var ns = XNamespace.Get(NsSheet);

        // 1) 共享字符串表（兼容富文本 <r><t>）
        var shared = new List<string>();
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry is not null)
        {
            var doc = XDocument.Load(ssEntry.Open());
            foreach (var si in doc.Descendants(ns + "si"))
                shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
        }

        // 2) 指定序号的工作表（按 workbook.xml 顺序解析真实路径，不硬编码 sheet1）
        var sheets = ListSheets(zip);
        var pick = sheetIndex >= 0 && sheetIndex < sheets.Count ? sheets[sheetIndex] : sheets[0];
        var sheetEntry = zip.GetEntry(pick.EntryPath)
                         ?? zip.GetEntry("xl/worksheets/sheet1.xml")
                         ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry is null) return null;

        var sdoc = XDocument.Load(sheetEntry.Open());
        var cellNodes = sdoc.Descendants(ns + "c").ToList();
        if (cellNodes.Count == 0) return null;

        // 3) 单元格 → 稀疏映射（行列均 0 基）
        int maxRow = -1, maxCol = -1;
        var map = new Dictionary<(int R, int C), string>();
        foreach (var c in cellNodes)
        {
            var (r, col) = ParseCellRef(c.Attribute("r")?.Value ?? "");
            if (r < 0 || col < 0) continue;
            maxRow = Math.Max(maxRow, r);
            maxCol = Math.Max(maxCol, col);
            var t = c.Attribute("t")?.Value;
            var v = c.Element(ns + "v")?.Value ?? "";
            var text = t switch
            {
                "s" => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                       && idx >= 0 && idx < shared.Count ? shared[idx] : "",
                "inlineStr" => string.Concat(c.Descendants(ns + "t").Select(x => x.Value)),
                "str" => c.Element(ns + "v")?.Value ?? "",
                _ => v,
            };
            map[(r, col)] = text;
        }
        if (maxRow < 0) return null;

        // 4) 合并单元格区域（纵向合并用于把分组名展开到每一行）
        var merges = new List<MergedRange>();
        foreach (var mc in sdoc.Descendants(ns + "mergeCell"))
        {
            var refAttr = mc.Attribute("ref")?.Value ?? "";
            var parts = refAttr.Split(':');
            if (parts.Length != 2) continue;
            var (r1, c1) = ParseCellRef(parts[0]);
            var (r2, c2) = ParseCellRef(parts[1]);
            if (r1 < 0 || r2 < 0) continue;
            merges.Add(new MergedRange(Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2)));
        }

        // 5) 组装原始网格（不做合并展开，先判结构）
        var grid = new List<List<string>>();
        for (int r = 0; r <= maxRow; r++)
        {
            var row = new List<string>();
            for (int c = 0; c <= maxCol; c++)
                row.Add(map.TryGetValue((r, c), out var v) ? v : "");
            grid.Add(row);
        }

        return new RawSheet { SheetName = pick.Name, Grid = grid, Merges = merges };
    }

    private static string ReadSheetName(ZipArchive zip, XNamespace ns)
    {
        try
        {
            var wb = zip.GetEntry("xl/workbook.xml");
            if (wb is null) return "";
            using var s = wb.Open();
            var doc = XDocument.Load(s);
            return doc.Descendants(ns + "sheet").FirstOrDefault()?.Attribute("name")?.Value ?? "";
        }
        catch { return ""; }
    }

    private static ImportedSheet ParseXlsx(string file, int sheetIndex)
    {
        var raw = ReadXlsx(file, sheetIndex);
        if (raw is null) return new ImportedSheet();

        var grid = raw.Grid;
        var merges = raw.Merges;

        // 6) 标题识别：首行从 A1 起横向跨列的合并 → 标题；否则首行仅 1 个非空也视为标题
        string title = "";
        var titleMerge = merges.FirstOrDefault(m => m.R1 == 0 && m.C1 == 0 && m.R2 == 0 && m.C2 >= 1);
        if (titleMerge != default && !string.IsNullOrWhiteSpace(grid[0][0]))
        {
            title = grid[0][0].Trim();
        }
        else
        {
            var nonEmpty = grid[0].Count(x => !string.IsNullOrWhiteSpace(x));
            if (nonEmpty == 1) title = grid[0].First(x => !string.IsNullOrWhiteSpace(x)).Trim();
        }
        int titleRows = 0;
        if (title.Length > 0)
        {
            grid.RemoveAt(0);
            titleRows = 1;
            // 合并区域随之上移一行；整行落在标题行的合并直接丢弃
            merges = merges
                .Select(m => m with { R1 = m.R1 - 1, R2 = m.R2 - 1 })
                .Where(m => m.R2 >= 0)
                .ToList();
        }

        var built = BuildSheet(grid, title, merges);
        built.TitleRowCount = titleRows;
        built.BodyStartRow += titleRows;      // BuildSheet 拿到的是剥离后的网格，补回标题行偏移
        return built;
    }

    // ---------------- DOCX ----------------

    private static RawSheet? ReadDocxRaw(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var ns = XNamespace.Get(NsDoc);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return null;

        var doc = XDocument.Load(entry.Open());
        var tables = doc.Descendants(ns + "tbl").ToList();
        if (tables.Count == 0) return null;

        // 取行数最多的表作为点检表
        var tbl = tables.OrderByDescending(t => t.Descendants(ns + "tr").Count()).First();

        var grid = tbl.Descendants(ns + "tr")
            .Select(tr => tr.Descendants(ns + "tc")
                .Select(tc => string.Concat(tc.Descendants(ns + "t").Select(t => t.Value)))
                .ToList())
            .Where(row => row.Count > 0)
            .ToList();
        if (grid.Count == 0) return null;

        return new RawSheet { SheetName = "(Word 表格)", Grid = grid, Merges = new List<MergedRange>() };
    }

    private static ImportedSheet ParseDocx(string file)
    {
        var raw = ReadDocxRaw(file);
        if (raw is null) return new ImportedSheet();

        var grid = raw.Grid;

        // 标题：首行仅 1 个非空单元格
        string title = "";
        int titleRows = 0;
        if (grid.Count > 1)
        {
            var nonEmpty = grid[0].Count(x => !string.IsNullOrWhiteSpace(x));
            if (nonEmpty == 1)
            {
                title = grid[0].First(x => !string.IsNullOrWhiteSpace(x)).Trim();
                grid.RemoveAt(0);
                titleRows = 1;
            }
        }

        var built = BuildSheet(grid, title, new List<MergedRange>());
        built.TitleRowCount = titleRows;
        built.BodyStartRow += titleRows;
        return built;
    }

    // ---------------- 结构切分（xlsx/docx 共用） ----------------

    /// <summary>
    /// 把原始网格切成「抬头字段 / 表体 / 表尾说明」三段：
    /// 表头行 = 前 12 行里第一个非空单元格数达到 max(3, 列数×0.6) 的行
    /// （抬头信息行只有寥寥几个单元格，天然被排除）；
    /// 表体到第一个全空行为止；其后非空行归入表尾说明。
    /// </summary>
    private static ImportedSheet BuildSheet(List<List<string>> grid, string title, List<MergedRange> merges)
    {
        var sheet = new ImportedSheet { Title = title };
        if (grid.Count == 0) return sheet;

        int colCount = grid.Max(r => r.Count);
        foreach (var row in grid) while (row.Count < colCount) row.Add("");

        // 1) 表头行
        int threshold = Math.Max(3, (int)Math.Ceiling(colCount * 0.6));
        int headerRow = -1;
        for (int i = 0; i < Math.Min(grid.Count, 12); i++)
        {
            if (grid[i].Count(x => !string.IsNullOrWhiteSpace(x)) >= threshold) { headerRow = i; break; }
        }
        if (headerRow < 0)
        {
            // 退化：取前 12 行中非空最多的一行；再不行取首行
            int best = -1, bestCount = -1;
            for (int i = 0; i < Math.Min(grid.Count, 12); i++)
            {
                int ne = grid[i].Count(x => !string.IsNullOrWhiteSpace(x));
                if (ne > bestCount) { bestCount = ne; best = i; }
            }
            headerRow = bestCount >= 2 ? best : 0;
        }

        // 2) 表体边界：表头行之后第一个全空行
        int bodyEnd = grid.Count;
        for (int i = headerRow + 1; i < grid.Count; i++)
        {
            if (grid[i].All(string.IsNullOrWhiteSpace)) { bodyEnd = i; break; }
        }
        if (bodyEnd == grid.Count)
        {
            // 没有全空行分隔时，只在**尾部**找说明行：非空≤1 且那一格是长文本。
            // 不能一见"非空≤1"就切断——空白记录表常常只预填了序号列（"1"、"2"…），
            // 那种行属于表体（实测：异常问题追踪表被误判成 0 行数据 + 20 条表尾说明）。
            for (int i = headerRow + 1; i < grid.Count; i++)
            {
                var f = Filled(grid[i]);
                if (f.Count != 1 || f[0].Length < 8) continue;      // 只可能是"说明/签字"这种长文本
                bool tailAllFooter = true;
                for (int j = i + 1; j < grid.Count; j++)
                {
                    var g2 = Filled(grid[j]);
                    if (g2.Count == 0) continue;
                    if (g2.Count != 1 || g2[0].Length < 8) { tailAllFooter = false; break; }
                }
                if (tailAllFooter) { bodyEnd = i; break; }
            }
        }

        // 2.5) 表体尾部的「汇总行」剥离
        //      典型：R49「本班点检结果汇总（自动统计）｜0 项 OK｜0 项 NG」（三块合并单元格）。
        //      它不是检查项：留着会被当成一条数据行，而「0 项 OK」落到「点检结果」下拉列上
        //      直接触发 DataGridViewComboBoxCell 值无效（实测报错）。剥离后归入表尾说明，信息不丢。
        var summaryRows = new List<string>();
        int bodyEndRaw = bodyEnd;      // 剥离前的表尾起点：后面扫表尾说明要从这里起，否则汇总行会被收两遍
        while (bodyEnd > headerRow + 1 && summaryRows.Count < 2)
        {
            var tail = Filled(grid[bodyEnd - 1]);
            if (tail.Count == 0) { bodyEnd--; continue; }                      // 尾部空白行一并收掉
            if (tail.Count <= 3 && IsSummaryLine(tail[0]))
            {
                summaryRows.Insert(0, string.Join("　", tail));
                bodyEnd--;
                continue;
            }
            break;
        }

        // 3) 抬头字段：表头行之前的非空行，按「标签：提示」拆分
        for (int i = 0; i < headerRow; i++)
        {
            foreach (var cell in grid[i])
            {
                if (string.IsNullOrWhiteSpace(cell)) continue;
                var (label, hint) = SplitLabel(cell);
                if (label.Length == 0) continue;
                if (sheet.HeaderFields.Any(h => h.Label == label)) continue;   // 避免同一标签重复
                sheet.HeaderFields.Add(new ChecklistHeaderField { Label = label, Hint = hint });
            }
        }

        // 4) 纵向合并展开：分组列（如 B4:B7「静电防护」）只有首行有值，展开到每行才不丢数据
        foreach (var m in merges)
        {
            if (m.R2 <= m.R1 || m.R1 < headerRow + 1 || m.R2 >= bodyEnd) continue;
            var val = grid[m.R1][m.C1];
            if (string.IsNullOrWhiteSpace(val)) continue;
            for (int r = m.R1 + 1; r <= m.R2; r++)
                if (string.IsNullOrWhiteSpace(grid[r][m.C1])) grid[r][m.C1] = val;
        }

        sheet.BodyStartRow = headerRow + 1 + sheet.TitleRowCount;   // 表体第一行（不含表头）在原文件里的 0 基行号

        // 5) 表体（含表头行）与表尾说明；被剥离的汇总行排在说明之前
        for (int i = headerRow; i < bodyEnd; i++) sheet.Grid.Add(grid[i].ToList());
        sheet.FooterNotes.AddRange(summaryRows);
        for (int i = bodyEndRaw; i < grid.Count; i++)
        {
            var parts = grid[i].Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (parts.Count == 0) continue;
            sheet.FooterNotes.Add(string.Join("　", parts));
        }

        return sheet;
    }

    /// <summary>一行里的非空文本（去空白，保留原顺序）。</summary>
    private static List<string> Filled(List<string> row) =>
        row.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

    private static readonly string[] SummaryWords = { "汇总", "合计", "总计", "小计", "统计", "累计", "平均" };

    /// <summary>是否像"合计/汇总"行（表体尾部的统计行，不是要填的检查项）。</summary>
    private static bool IsSummaryLine(string text) =>
        text.Length > 0 && SummaryWords.Any(w => text.Contains(w, StringComparison.Ordinal));

    /// <summary>把「车间/线别：________」拆成（标签, 提示）；无冒号时整段当标签。</summary>
    private static (string Label, string Hint) SplitLabel(string raw)
    {
        var s = raw.Trim();
        int i = s.IndexOf('：');
        if (i < 0) i = s.IndexOf(':');
        if (i <= 0) return (s, "");
        return (s[..i].Trim(), s[(i + 1)..].Trim());
    }

    // ---------------- 列类型推断 ----------------

    /// <summary>
    /// 关键词规则表。**顺序即优先级**：上下文列（序号/项目/内容/方法/周期/基准）最先，
    /// 多行其次（避免「异常情况说明」被「情况」抢成下拉），下拉再后。
    /// </summary>
    private static readonly (FieldKind Kind, string[] Words)[] KeywordRules =
    {
        (FieldKind.Context,   new[] { "序号", "编号", "项目", "内容", "标准", "基准", "要求", "名称", "设备", "区域", "部位", "规格", "参数", "依据", "方法", "周期", "频次", "工序", "工位" }),
        (FieldKind.Multiline, new[] { "备注", "说明", "异常", "现象", "措施", "处理", "描述", "原因" }),
        (FieldKind.Date,      new[] { "日期", "时间", "年月日", "时点", "时刻" }),
        (FieldKind.Number,    new[] { "数量", "数值", "温度", "压力", "电流", "电压", "次数", "时长", "小时", "分钟", "转速", "流量", "读数", "实测", "值" }),
        (FieldKind.Select,    new[] { "结果", "结论", "判定", "状态", "等级", "评价" }),
        (FieldKind.Checkbox,  new[] { "是否", "有无", "存在" }),
        (FieldKind.Signature, new[] { "签字", "签名", "确认人", "巡检人", "负责人", "点检人", "记录人", "复核", "操作人" }),
    };

    /// <summary>把表头文本规范化：换行压成空格、连续空白合并。</summary>
    private static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return "";
        var s = header.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }

    /// <summary>把解析出的表结构转换成点检表模板，并按列头关键字推断每列字段类型。</summary>
    public static ChecklistTemplate BuildTemplate(string name, string sourceFile, ImportedSheet sheet)
    {
        var tpl = new ChecklistTemplate
        {
            Name = name,
            SourceFileName = sourceFile,
            Title = string.IsNullOrWhiteSpace(sheet.Title) ? name : sheet.Title,
            ImportedAt = DateTime.Now,
            HeaderFields = sheet.HeaderFields,
            FooterNotes = sheet.FooterNotes,
            BodyStartRow = sheet.BodyStartRow,
        };

        if (sheet.Grid.Count == 0) return tpl;

        var colCount = sheet.Grid.Max(r => r.Count);
        foreach (var row in sheet.Grid) while (row.Count < colCount) row.Add("");

        var header = sheet.Grid[0];
        for (int c = 0; c < colCount; c++)
        {
            var h = NormalizeHeader(header[c]);
            var (kind, options, required) = InferColumn(h, c);
            tpl.Columns.Add(new ChecklistColumn
            {
                Header = string.IsNullOrWhiteSpace(h) ? $"列{c + 1}" : h,
                Kind = kind,
                Options = options,
                Required = required,
            });
        }

        for (int r = 1; r < sheet.Grid.Count; r++)
            tpl.Rows.Add(new ChecklistRow { Cells = sheet.Grid[r].ToList() });

        return tpl;
    }

    /// <summary>兼容旧签名（无抬头/表尾信息时使用）。</summary>
    public static ChecklistTemplate BuildTemplate(string name, string sourceFile, string title, List<List<string>> grid)
        => BuildTemplate(name, sourceFile, new ImportedSheet { Title = title, Grid = grid });

    private static (FieldKind, List<string>, bool) InferColumn(string header, int colIndex)
    {
        var h = header.Trim();
        var upper = h.ToUpperInvariant();

        foreach (var (kind, words) in KeywordRules)
        {
            if (!words.Any(k => h.Contains(k, StringComparison.Ordinal) || upper.Contains(k.ToUpperInvariant()))) continue;

            var options = new List<string>();
            bool required = false;
            switch (kind)
            {
                case FieldKind.Select:
                    if (upper.Contains("OK") || upper.Contains("NG"))
                        options = new() { "OK", "NG" };                     // 原表写 OK/NG 就照原表
                    else if (h.Contains("结果") || h.Contains("判定") || h.Contains("结论") || h.Contains("等级") || h.Contains("评价"))
                        options = new() { "合格", "不合格", "待处理" };
                    else if (h.Contains("正常") || h.Contains("异常"))
                        options = new() { "正常", "异常", "待处理" };
                    else
                        options = new() { "是", "否", "待处理" };
                    required = h.Contains("结果") || h.Contains("判定");
                    break;
                case FieldKind.Signature:
                case FieldKind.Date:
                    required = true;
                    break;
            }
            return (kind, options, required);
        }
        // 第一列通常是序号/项目，兜底为上下文
        if (colIndex == 0) return (FieldKind.Context, new(), false);
        return (FieldKind.Text, new(), false);
    }

    // ---------------- 工具 ----------------

    /// <summary>解析 A1 形式引用为 0 基行列；非法返回 (-1,-1)。供样式解析等外部调用方复用。</summary>
    public static (int Row, int Col) ParseCellRefPublic(string r) => ParseCellRef(r);

    /// <summary>解析 A1 形式引用为 0 基行列；非法返回 (-1,-1)。</summary>
    private static (int Row, int Col) ParseCellRef(string r)
    {
        int i = 0, col = 0;
        while (i < r.Length && !char.IsDigit(r[i]))
        {
            var ch = char.ToUpperInvariant(r[i]);
            if (ch < 'A' || ch > 'Z') return (-1, -1);
            col = col * 26 + (ch - 'A' + 1);
            i++;
        }
        if (i == 0 || i >= r.Length) return (-1, -1);
        if (!int.TryParse(r[i..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row)) return (-1, -1);
        return (row - 1, col - 1);
    }

    // ---------------- 示例模板（免准备文件即可演示全链路） ----------------

    /// <summary>内置「空压机日常点检表」示例，用户点「载入示例」即可体验导入→填写→提交→查询闭环。</summary>
    public static ChecklistTemplate BuildSampleTemplate()
    {
        var tpl = new ChecklistTemplate
        {
            Name = "空压机日常点检表（示例）",
            SourceFileName = "（内置示例，非文件导入）",
            Title = "空压机日常点检表",
            ImportedAt = DateTime.Now,
        };
        tpl.HeaderFields.Add(new ChecklistHeaderField { Label = "车间/线别", Hint = "________" });
        tpl.HeaderFields.Add(new ChecklistHeaderField { Label = "点检日期", Hint = "______年___月___日" });
        tpl.HeaderFields.Add(new ChecklistHeaderField { Label = "班次", Hint = "早班/中班/晚班" });

        tpl.Columns.Add(new ChecklistColumn { Header = "序号", Kind = FieldKind.Context });
        tpl.Columns.Add(new ChecklistColumn { Header = "检查项目", Kind = FieldKind.Context });
        tpl.Columns.Add(new ChecklistColumn { Header = "检查标准", Kind = FieldKind.Context });
        tpl.Columns.Add(new ChecklistColumn { Header = "点检结果", Kind = FieldKind.Select, Options = new() { "合格", "不合格", "待处理" }, Required = true });
        tpl.Columns.Add(new ChecklistColumn { Header = "实测值", Kind = FieldKind.Number });
        tpl.Columns.Add(new ChecklistColumn { Header = "异常情况说明", Kind = FieldKind.Multiline });
        tpl.Columns.Add(new ChecklistColumn { Header = "点检人", Kind = FieldKind.Signature, Required = true });
        tpl.Columns.Add(new ChecklistColumn { Header = "点检日期", Kind = FieldKind.Date, Required = true });

        var items = new[]
        {
            ("1", "运行声音", "无异常异响，运转平稳"),
            ("2", "排气温度", "≤ 110℃"),
            ("3", "出口压力", "0.6~0.8 MPa"),
            ("4", "油位", "在油窗 1/2~2/3 之间"),
            ("5", "冷却水", "通畅无泄漏"),
            ("6", "过滤器", "无堵塞、压差正常"),
        };
        foreach (var (no, item, std) in items)
            tpl.Rows.Add(new ChecklistRow { Cells = new() { no, item, std, "", "", "", "", "" } });

        tpl.FooterNotes.Add("点检说明：点检结果不合格须在异常说明栏注明并上报处理；本表每班点检一次。");
        return tpl;
    }
}
