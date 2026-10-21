namespace SuperCapMES.WinForms.Models;

/// <summary>单元格四边边框的 CSS 线型（空串 = 该边无线）。</summary>
public sealed class CellBorders
{
    public string Top { get; set; } = "";
    public string Right { get; set; } = "";
    public string Bottom { get; set; } = "";
    public string Left { get; set; } = "";

    public bool IsEmpty => Top.Length == 0 && Right.Length == 0 && Bottom.Length == 0 && Left.Length == 0;
}

/// <summary>
/// 单元格视觉样式：从 xlsx 的 styles.xml（cellXfs → font/fill/border/alignment/numFmt）解析而来。
/// 只承载"怎么画"，不承载"是什么意思"——语义仍在 ChecklistColumn 上，两层互不干扰。
/// </summary>
public sealed class CellStyle
{
    public string FontName { get; set; } = "";
    /// <summary>字号（point）</summary>
    public double FontSizePt { get; set; } = 11;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    /// <summary>字色 #RRGGBB；空 = 默认</summary>
    public string FontColor { get; set; } = "";
    /// <summary>填充色 #RRGGBB；空 = 无填充</summary>
    public string FillColor { get; set; } = "";
    /// <summary>left / center / right；空 = 按类型默认</summary>
    public string HAlign { get; set; } = "";
    /// <summary>top / middle / bottom</summary>
    public string VAlign { get; set; } = "";
    public bool Wrap { get; set; }
    public CellBorders Borders { get; set; } = new();
    public int NumFmtId { get; set; }
    /// <summary>自定义数字格式串（numFmtId 不在内置表时使用）</summary>
    public string NumFmtCode { get; set; } = "";

    public bool IsDefault =>
        FontName.Length == 0 && Math.Abs(FontSizePt - 11) < 0.01 && !Bold && !Italic && !Underline
        && FontColor.Length == 0 && FillColor.Length == 0 && HAlign.Length == 0 && VAlign.Length == 0
        && !Wrap && Borders.IsEmpty;
}

/// <summary>某一列在原样渲染时的语义（哪列可填、什么控件、候选项）。</summary>
public sealed class SheetColumnMeta
{
    public string Header { get; set; } = "";
    public FieldKind Kind { get; set; } = FieldKind.Context;
    public bool Fillable { get; set; }
    public bool Required { get; set; }
    public List<string> Options { get; set; } = new();
}

/// <summary>
/// 带样式的原始表：渲染"原样文档"所需的全部几何 + 视觉数据。
/// Grid/Merges 与规则解析共用同一份（sourceRef 坐标一致），额外补 StyleIndex / 列宽 / 行高。
/// </summary>
public sealed class StyledSheet
{
    public string SheetName { get; set; } = "";
    public List<List<string>> Grid { get; set; } = new();
    public List<MergedRange> Merges { get; set; } = new();
    /// <summary>去重后的样式表</summary>
    public List<CellStyle> Styles { get; set; } = new();
    /// <summary>每个单元格指向 Styles 的下标</summary>
    public List<List<int>> StyleIndex { get; set; } = new();
    /// <summary>列宽（px，按原文件字符宽度换算）</summary>
    public List<double> ColWidthPx { get; set; } = new();
    /// <summary>行高（px，按原文件 point 换算）</summary>
    public List<double> RowHeightPx { get; set; } = new();

    /// <summary>本表的列语义（可填/类型/候选项）。多工作表时每张表各有一份，互不干扰。</summary>
    public List<SheetColumnMeta> Columns { get; set; } = new();

    /// <summary>表体第一行在本表网格里的 0 基行号；可填控件只允许出现在表体行</summary>
    public int BodyStartRow { get; set; }

    /// <summary>表体行数</summary>
    public int BodyRowCount { get; set; }

    /// <summary>本表是否支持按单元格填写（有列语义且表体非空）</summary>
    public bool CanFill => Columns.Count > 0 && BodyRowCount > 0;

    public int RowCount => Grid.Count;
    public int ColCount => Grid.Count == 0 ? 0 : Grid.Max(r => r.Count);

    public string Cell(int r, int c) =>
        r >= 0 && r < Grid.Count && c >= 0 && c < Grid[r].Count ? (Grid[r][c] ?? "") : "";

    public CellStyle StyleAt(int r, int c)
    {
        if (r < 0 || r >= StyleIndex.Count) return new CellStyle();
        var row = StyleIndex[r];
        if (c < 0 || c >= row.Count) return new CellStyle();
        var i = row[c];
        return i >= 0 && i < Styles.Count ? Styles[i] : new CellStyle();
    }

    /// <summary>某格是否是合并区域（非左上角）被覆盖的格</summary>
    public bool IsCovered(int r, int c) =>
        Merges.Any(m => r >= m.R1 && r <= m.R2 && c >= m.C1 && c <= m.C2 && !(r == m.R1 && c == m.C1));
}
