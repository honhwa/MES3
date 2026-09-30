using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SuperCapMES.WinForms.Utils;

/// <summary>
/// 极简 xlsx 写入器（**零依赖**：手写 OpenXML + <see cref="ZipArchive"/>）。
/// 支持：多工作表、加粗表头、列宽、文本/数字单元格。
/// 产物是标准 .xlsx，Excel / WPS 可直接双击打开（无"格式与扩展名不匹配"警告）。
/// 用途：追溯报告、召回影响清单等导出。
/// </summary>
public sealed class XlsxWriter
{
    /// <summary>工作表</summary>
    public sealed class Sheet
    {
        internal Sheet(string name) => Name = name;

        /// <summary>工作表名（已清洗，≤31 字符）</summary>
        public string Name { get; }

        /// <summary>列定义（标题 + 列宽）</summary>
        public List<(string Title, double Width)> Columns { get; } = new();

        /// <summary>数据行（不含表头）</summary>
        public List<List<object?>> Rows { get; } = new();

        /// <summary>设置列（标题 + 宽度）</summary>
        public void SetColumns(params (string Title, double Width)[] cols)
        {
            Columns.Clear();
            Columns.AddRange(cols);
        }

        /// <summary>追加一行数据（值与列一一对应）</summary>
        public void AddRow(params object?[] values) => Rows.Add(values.ToList());
    }

    private readonly List<Sheet> _sheets = new();

    /// <summary>新增工作表（重名自动加序号；名称非法字符自动替换）</summary>
    public Sheet AddSheet(string name)
    {
        var safe = SanitizeName(name);
        var final = safe;
        var n = 2;
        while (_sheets.Any(s => string.Equals(s.Name, final, StringComparison.OrdinalIgnoreCase)))
            final = $"{safe}({n++})";
        var sheet = new Sheet(final);
        _sheets.Add(sheet);
        return sheet;
    }

    /// <summary>写出 .xlsx（覆盖同名文件）。返回 (工作表数, 数据行数)。</summary>
    public (int Sheets, int Rows) Save(string path)
    {
        if (_sheets.Count == 0) throw new InvalidOperationException("至少需要一个工作表。");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);

        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "[Content_Types].xml", BuildContentTypes());
            WriteEntry(zip, "_rels/.rels", BuildRootRels());
            WriteEntry(zip, "xl/workbook.xml", BuildWorkbook());
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels());
            WriteEntry(zip, "xl/styles.xml", StylesXml);
            for (var i = 0; i < _sheets.Count; i++)
                WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", BuildSheetXml(_sheets[i]));
        }

        return (_sheets.Count, _sheets.Sum(s => s.Rows.Count));
    }

    // ---------- 内部构建 ----------

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private string BuildContentTypes()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (var i = 1; i <= _sheets.Count; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string BuildRootRels() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private string BuildWorkbook()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
        sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<sheets>");
        for (var i = 0; i < _sheets.Count; i++)
            sb.Append($"<sheet name=\"{Escape(_sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private string BuildWorkbookRels()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var i = 1; i <= _sheets.Count; i++)
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        sb.Append($"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    /// <summary>样式：0 = 普通，1 = 表头（加粗、浅蓝底、居中）。</summary>
    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\">" +
        "<font><sz val=\"11\"/><name val=\"微软雅黑\"/></font>" +
        "<font><b/><sz val=\"11\"/><color rgb=\"FF1F3864\"/><name val=\"微软雅黑\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCE6F1\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"2\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
        "</cellXfs>" +
        "</styleSheet>";

    private static string BuildSheetXml(Sheet sheet)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

        if (sheet.Columns.Count > 0)
        {
            sb.Append("<cols>");
            for (var c = 0; c < sheet.Columns.Count; c++)
            {
                var width = sheet.Columns[c].Width <= 0 ? 14 : sheet.Columns[c].Width;
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width.ToString("0.##", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }

        sb.Append("<sheetData>");
        var rowNo = 1;

        if (sheet.Columns.Count > 0)
        {
            sb.Append($"<row r=\"{rowNo}\">");
            for (var c = 0; c < sheet.Columns.Count; c++)
                sb.Append(CellXml(ColRef(c) + rowNo, sheet.Columns[c].Title, 1));
            sb.Append("</row>");
            rowNo++;
        }

        foreach (var row in sheet.Rows)
        {
            sb.Append($"<row r=\"{rowNo}\">");
            for (var c = 0; c < row.Count; c++)
                sb.Append(CellXml(ColRef(c) + rowNo, row[c], 0));
            sb.Append("</row>");
            rowNo++;
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string CellXml(string reference, object? value, int styleIndex)
    {
        var style = styleIndex > 0 ? $" s=\"{styleIndex}\"" : "";

        if (value is null)
            return $"<c r=\"{reference}\"{style}/>";

        switch (value)
        {
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                return $"<c r=\"{reference}\"{style}><v>{d.ToString("0.######", CultureInfo.InvariantCulture)}</v></c>";
            case int i:
                return $"<c r=\"{reference}\"{style}><v>{i.ToString(CultureInfo.InvariantCulture)}</v></c>";
            case bool b:
                return $"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t>{(b ? "是" : "否")}</t></is></c>";
            case DateTime dt:
                return $"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t>{dt:yyyy-MM-dd HH:mm:ss}</t></is></c>";
            default:
                var text = value.ToString() ?? "";
                if (text.Length == 0) return $"<c r=\"{reference}\"{style}/>";
                return $"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>";
        }
    }

    /// <summary>列号 → 字母（0 → A，25 → Z，26 → AA）</summary>
    private static string ColRef(int index)
    {
        var result = "";
        var i = index;
        do
        {
            result = (char)('A' + i % 26) + result;
            i = i / 26 - 1;
        } while (i >= 0);
        return result;
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

    /// <summary>清洗工作表名：去除非法字符、裁剪到 31 字符、空名兜底。</summary>
    private static string SanitizeName(string name)
    {
        var cleaned = new string((name ?? "").Where(ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Sheet";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
