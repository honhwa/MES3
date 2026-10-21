using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

/// <summary>
/// 读取「带样式的原始表」：在 PaperlessImport.LoadRaw 的网格/合并之上，
/// 补 xlsx 的 styles.xml（字体/填充/边框/对齐/数字格式）、列宽、行高、每格样式索引。
///
/// 定位：**只服务"原样渲染"新页面**。旧的三页仍用语义模型，互不影响。
/// 读不到样式时退化为"结构正确、样式默认"，绝不因此打不开文件。
/// </summary>
public static class StyledSheetReader
{
    private const string NsSheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>默认行高 15pt ≈ 20px（Excel 新建工作簿的缺省）</summary>
    private const double DefaultRowPx = 20;
    /// <summary>默认列宽 8.43 字符（Excel 缺省），按 MDW 换算成像素</summary>
    private const double DefaultColChars = 8.43;
    /// <summary>兜底 MDW（读不到字体时按 Calibri 11 处理）</summary>
    private const double FallbackMdw = 7.0;

    public static StyledSheet? Load(string filePath, int sheetIndex = 0)
    {
        RawSheet? raw;
        try { raw = PaperlessImport.LoadRaw(filePath, sheetIndex); }
        catch { return null; }
        if (raw is null) return null;

        var sheet = new StyledSheet { SheetName = raw.SheetName, Grid = raw.Grid, Merges = raw.Merges };
        int rows = sheet.RowCount, cols = sheet.ColCount;
        if (rows == 0 || cols == 0) return sheet;

        // 默认：一套基础样式 + 全部格子指向它
        sheet.Styles.Add(new CellStyle());
        sheet.StyleIndex = Enumerable.Range(0, rows)
            .Select(_ => Enumerable.Repeat(0, cols).ToList()).ToList();
        sheet.ColWidthPx = Enumerable.Repeat(ColWidthToPx(DefaultColChars, FallbackMdw), cols).ToList();
        sheet.RowHeightPx = Enumerable.Repeat(DefaultRowPx, rows).ToList();

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xlsm"))
            return sheet;   // Word 的 tblGrid/gridSpan/vMerge 解析属下一期，这里至少给出正确结构与默认样式

        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            ApplyXlsx(zip, filePath, sheetIndex, sheet);
        }
        catch
        {
            // 样式解析失败 → 保留结构，退化为默认外观（宁可少画，不能打不开）
        }
        return sheet;
    }

    /// <summary>把语义模板的列定义与表体范围挂到样式表上，使该表可独立渲染可填控件。</summary>
    public static StyledSheet AttachSemantics(StyledSheet sheet, ChecklistTemplate tpl)
    {
        sheet.Columns = tpl.Columns.Select(c => new SheetColumnMeta
        {
            Header = c.Header,
            Kind = c.Kind,
            Fillable = c.Fillable,
            Required = c.Required,
            Options = c.Options.ToList(),
        }).ToList();
        sheet.BodyStartRow = tpl.BodyStartRow;
        sheet.BodyRowCount = tpl.Rows.Count;
        return sheet;
    }

    /// <summary>
    /// 读入文件的**全部**工作表（每张都带自己的列语义）。
    /// 一张点检表常含「点检表 / 记录台账 / 异常问题追踪表」，只存第一张会让后两张在填写页里消失。
    /// </summary>
    public static List<StyledSheet> LoadAll(string filePath)
    {
        var list = new List<StyledSheet>();
        var sheets = PaperlessImport.ListSheets(filePath);
        for (int i = 0; i < sheets.Count; i++)
        {
            var st = Load(filePath, i);
            if (st is null) continue;
            try { AttachSemantics(st, PaperlessImport.Import(filePath, null, i)); }
            catch { /* 单张语义解析失败不影响其它表 */ }
            list.Add(st);
        }
        return list;
    }

    // ---------------- xlsx ----------------

    private static void ApplyXlsx(ZipArchive zip, string filePath, int sheetIndex, StyledSheet sheet)
    {
        var ns = XNamespace.Get(NsSheet);
        var stylesEntry = zip.GetEntry("xl/styles.xml");

        // 1) 样式表 → CellStyle 列表（下标与 cellXfs 的 s 值对应）
        if (stylesEntry is not null)
        {
            using var st = stylesEntry.Open();
            var stylesDoc = XDocument.Load(st);
            var fonts = ReadFonts(stylesDoc, ns);
            var fills = ReadFills(stylesDoc, ns);
            var borders = ReadBorders(stylesDoc, ns);
            var numFmts = stylesDoc.Descendants(ns + "numFmt")
                .Where(e => e.Attribute("numFmtId") is not null)
                .GroupBy(e => e.Attribute("numFmtId")!.Value)
                .ToDictionary(g => g.Key, g => g.First().Attribute("formatCode")?.Value ?? "");

            var xfs = stylesDoc.Descendants(ns + "cellXfs").Elements(ns + "xf").ToList();
            sheet.Styles.Clear();
            for (int i = 0; i < xfs.Count; i++)
            {
                var xf = xfs[i];
                var cs = new CellStyle
                {
                    NumFmtId = Int(xf.Attribute("numFmtId")?.Value, 0),
                    FontName = "",
                };
                var fid = Int(xf.Attribute("fontId")?.Value, -1);
                if (fid >= 0 && fid < fonts.Count)
                {
                    var f = fonts[fid];
                    cs.FontName = f.Name;
                    cs.FontSizePt = f.Size;
                    cs.Bold = f.Bold;
                    cs.Italic = f.Italic;
                    cs.Underline = f.Underline;
                    cs.FontColor = f.Color;
                }
                var flid = Int(xf.Attribute("fillId")?.Value, -1);
                if (flid >= 0 && flid < fills.Count) cs.FillColor = fills[flid];
                var bid = Int(xf.Attribute("borderId")?.Value, -1);
                if (bid >= 0 && bid < borders.Count) cs.Borders = borders[bid];

                var al = xf.Element(ns + "alignment");
                if (al is not null)
                {
                    var h = al.Attribute("horizontal")?.Value ?? "";
                    cs.HAlign = h switch
                    {
                        "left" => "left",
                        "center" or "centerContinuous" => "center",
                        "right" => "right",
                        "justify" => "justify",
                        _ => "",
                    };
                    cs.VAlign = (al.Attribute("vertical")?.Value ?? "") switch
                    {
                        "top" => "top",
                        "center" => "middle",
                        "bottom" => "bottom",
                        _ => "",
                    };
                    cs.Wrap = string.Equals(al.Attribute("wrapText")?.Value, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (cs.NumFmtId >= 164 && numFmts.TryGetValue(cs.NumFmtId.ToString(CultureInfo.InvariantCulture), out var code))
                    cs.NumFmtCode = code;

                sheet.Styles.Add(cs);
            }
            if (sheet.Styles.Count == 0) sheet.Styles.Add(new CellStyle());
        }

        // 2) 工作表：列宽 / 行高 / 每格样式索引
        var sheets = PaperlessImport.ListSheets(filePath);
        var path = sheetIndex >= 0 && sheetIndex < sheets.Count ? sheets[sheetIndex].EntryPath : "xl/worksheets/sheet1.xml";
        var wsEntry = zip.GetEntry(path) ?? zip.GetEntry("xl/worksheets/sheet1.xml");
        if (wsEntry is null) return;

        using var ws = wsEntry.Open();
        var wsDoc = XDocument.Load(ws);

        // 列宽换算的关键是 MDW（最大数字宽）：中文默认字体（宋体/雅黑）比 Calibri 宽，
        // 一律按 7 算会让整张表偏窄——实测「点检内容及标准」列 299px vs 原件 344px（差 45px），
        // 长文本因此换行增多、被 overflow:hidden 裁断，看起来就是"失真"。
        var mdw = EstimateMaxDigitWidth(
            sheet.Styles.Count > 0 ? sheet.Styles[0].FontName : "",
            sheet.Styles.Count > 0 ? sheet.Styles[0].FontSizePt : 11);

        foreach (var col in wsDoc.Descendants(ns + "cols").Elements(ns + "col"))
        {
            var min = Int(col.Attribute("min")?.Value, 0);
            var max = Int(col.Attribute("max")?.Value, 0);
            var width = Dbl(col.Attribute("width")?.Value, 0);
            if (min <= 0 || max < min || width <= 0) continue;
            var px = ColWidthToPx(width, mdw);
            for (int c = min; c <= max && c <= sheet.ColCount; c++)
                sheet.ColWidthPx[c - 1] = px;
        }

        // 未显式设置 ht 的行用工作表的默认行高（Excel 15pt=20px，但 LibreOffice/部分模板是 13.5pt=18px）
        var fmtPr = wsDoc.Root?.Element(ns + "sheetFormatPr");
        var defRowHt = Dbl(fmtPr?.Attribute("defaultRowHeight")?.Value, 0);
        if (defRowHt > 0)
        {
            var defPx = Math.Round(defRowHt * 96.0 / 72.0, 1);
            for (int r = 0; r < sheet.RowHeightPx.Count; r++) sheet.RowHeightPx[r] = defPx;
        }

        int maxStyle = Math.Max(0, sheet.Styles.Count - 1);
        foreach (var row in wsDoc.Descendants(ns + "row"))
        {
            var r = Int(row.Attribute("r")?.Value, 0) - 1;
            if (r < 0 || r >= sheet.RowCount) continue;
            var ht = Dbl(row.Attribute("ht")?.Value, 0);
            if (ht > 0) sheet.RowHeightPx[r] = Math.Round(ht * 96.0 / 72.0, 1);

            foreach (var c in row.Elements(ns + "c"))
            {
                // 注意顺序：ParseCellRef 返回 (Row, Col)，这里要的是列号
                var (_, ci) = PaperlessImport.ParseCellRefPublic(c.Attribute("r")?.Value ?? "");
                if (ci < 0 || ci >= sheet.ColCount) continue;
                var s = Int(c.Attribute("s")?.Value, 0);
                sheet.StyleIndex[r][ci] = Math.Clamp(s, 0, maxStyle);
            }
        }
    }

    // ---------------- 各样式组 ----------------

    private sealed record FontInfo(string Name, double Size, bool Bold, bool Italic, bool Underline, string Color);

    private static List<FontInfo> ReadFonts(XDocument doc, XNamespace ns)
    {
        var list = new List<FontInfo>();
        foreach (var f in doc.Descendants(ns + "fonts").Elements(ns + "font"))
        {
            var rgb = f.Element(ns + "color")?.Attribute("rgb")?.Value ?? "";
            var theme = f.Element(ns + "color")?.Attribute("theme")?.Value;
            list.Add(new FontInfo(
                f.Element(ns + "name")?.Attribute("val")?.Value ?? "",
                Dbl(f.Element(ns + "sz")?.Attribute("val")?.Value, 11),
                f.Element(ns + "b") is not null && !IsFalse(f.Element(ns + "b")),
                f.Element(ns + "i") is not null && !IsFalse(f.Element(ns + "i")),
                f.Element(ns + "u") is not null && !IsFalse(f.Element(ns + "u")),
                Hex(rgb, theme)));
        }
        return list;
    }

    private static List<string> ReadFills(XDocument doc, XNamespace ns)
    {
        var list = new List<string>();
        foreach (var f in doc.Descendants(ns + "fills").Elements(ns + "fill"))
        {
            var pf = f.Element(ns + "patternFill");
            var pt = pf?.Attribute("patternType")?.Value ?? "none";
            if (!string.Equals(pt, "solid", StringComparison.OrdinalIgnoreCase)) { list.Add(""); continue; }
            var fg = pf!.Element(ns + "fgColor");
            list.Add(Hex(fg?.Attribute("rgb")?.Value ?? "", fg?.Attribute("theme")?.Value));
        }
        return list;
    }

    private static List<CellBorders> ReadBorders(XDocument doc, XNamespace ns)
    {
        var list = new List<CellBorders>();
        foreach (var b in doc.Descendants(ns + "borders").Elements(ns + "border"))
            list.Add(new CellBorders
            {
                Top = CssBorder(b.Element(ns + "top")),
                Right = CssBorder(b.Element(ns + "right")),
                Bottom = CssBorder(b.Element(ns + "bottom")),
                Left = CssBorder(b.Element(ns + "left")),
            });
        return list;
    }

    /// <summary>Excel 边框线型 → CSS。颜色缺省按 Excel 习惯用黑色细线。</summary>
    private static string CssBorder(XElement? side)
    {
        var style = side?.Attribute("style")?.Value ?? "";
        if (style.Length == 0 || style == "none") return "";
        var color = Hex(side?.Element(ColorTag)?.Attribute("rgb")?.Value ?? "", null);
        if (color.Length == 0) color = "#000000";
        return style switch
        {
            "hair" or "thin" or "dotted" or "dashed" or "dashDot" or "dashDotDot" or "slantDashDot" => $"1px solid {color}",
            "medium" or "mediumDashed" or "mediumDashDot" or "mediumDashDotDot" => $"2px solid {color}",
            "thick" => $"3px solid {color}",
            "double" => $"3px double {color}",
            _ => $"1px solid {color}",
        };
    }

    private static readonly XName ColorTag = XName.Get("color", NsSheet);

    // ---------------- 工具 ----------------

    /// <summary>ARGB/RGB → #RRGGBB；theme 色（无 rgb）返回空串，由 CSS 默认色兜底。</summary>
    private static string Hex(string rgb, string? theme)
    {
        if (string.IsNullOrWhiteSpace(rgb)) return "";   // theme 色无法精确还原主题调色板，交给 CSS 默认
        var h = rgb.Trim();
        if (h.Length == 8) h = h[2..];                   // 去掉 alpha
        if (h.Length != 6) return "";
        if (h.Equals("000000", StringComparison.OrdinalIgnoreCase)) return "#000000";
        return "#" + h.ToUpperInvariant();
    }

    /// <summary>
    /// 列宽（字符数）→ 像素，用 Excel/LibreOffice 的规范换算：
    ///   px = (256 * 字符数 + ⌊128 / MDW⌋) / 256 * MDW
    /// 直接 `w*7+5` 只在 Calibri 11 下近似成立，遇到中文默认字体就整体偏窄。
    /// </summary>
    public static double ColWidthToPx(double widthChars, double mdw)
    {
        if (widthChars <= 0) return 0;
        var pad = Math.Floor(128.0 / mdw);
        return Math.Floor((256.0 * widthChars + pad) / 256.0 * mdw);
    }

    /// <summary>
    /// 估最大数字宽（MDW）。xlsx 不存这个值，只能按默认字体推：
    /// 西文 11pt ≈ 7（Calibri/Arial），中日韩字体 ≈ 8.2，再按字号等比缩放。
    /// </summary>
    public static double EstimateMaxDigitWidth(string fontName, double sizePt)
    {
        var n = (fontName ?? "").Trim().ToLowerInvariant();
        bool cjk = n.Length == 0
                   || n.Contains("宋") || n.Contains("simsun") || n.Contains("雅黑") || n.Contains("yahei")
                   || n.Contains("微软") || n.Contains("黑体") || n.Contains("楷") || n.Contains("仿宋")
                   || n.Contains("noto sans cjk") || n.Contains("dengxian") || n.Contains("等线")
                   || n.Contains("思源") || n.Contains("中");
        var baseAt11 = cjk ? 8.2 : 7.0;
        var pt = sizePt <= 0 ? 11 : sizePt;
        return Math.Clamp(baseAt11 * pt / 11.0, 5.0, 20.0);
    }

    private static bool IsFalse(XElement? e)
    {
        var v = e?.Attribute("val")?.Value;
        return string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) || v == "0";
    }

    private static int Int(string? s, int dflt) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : dflt;

    private static double Dbl(string? s, double dflt) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : dflt;
}
