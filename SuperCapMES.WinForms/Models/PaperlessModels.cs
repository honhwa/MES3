namespace SuperCapMES.WinForms.Models;

/// <summary>字段类型：决定填写页渲染的控件与电子档还原方式。</summary>
public enum FieldKind
{
    /// <summary>只读上下文（序号/检查项目/标准等，来自导入模板，不可填）</summary>
    Context,
    /// <summary>单行文本</summary>
    Text,
    /// <summary>多行文本（备注/异常描述）</summary>
    Multiline,
    /// <summary>数值</summary>
    Number,
    /// <summary>日期 yyyy-MM-dd</summary>
    Date,
    /// <summary>下拉选择（Options 给出候选项）</summary>
    Select,
    /// <summary>是否勾选</summary>
    Checkbox,
    /// <summary>签名 / 责任人</summary>
    Signature,
}

/// <summary>
/// 字段类型的中文显示名与反向解析。
/// 界面上一律显示中文（「上下文/选择/数值」），代码里一律用枚举——中间必须有一处唯一映射，
/// 否则下拉框会显示成 Context/Select，与手册口径对不上。
/// </summary>
public static class FieldKindText
{
    public static string Of(FieldKind kind) => kind switch
    {
        FieldKind.Context => "上下文",
        FieldKind.Text => "文本",
        FieldKind.Multiline => "多行",
        FieldKind.Number => "数值",
        FieldKind.Date => "日期",
        FieldKind.Select => "选择",
        FieldKind.Checkbox => "勾选",
        FieldKind.Signature => "签名",
        _ => "文本",
    };

    /// <summary>全部中文名（按枚举顺序，供下拉框使用）</summary>
    public static IReadOnlyList<string> All { get; } =
        Enum.GetValues<FieldKind>().Select(Of).ToList();

    /// <summary>中文名 → 枚举；也兼容直接传英文枚举名（历史数据/外部输入）</summary>
    public static FieldKind Parse(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return FieldKind.Text;
        foreach (var k in Enum.GetValues<FieldKind>())
            if (Of(k) == t) return k;
        return Enum.TryParse<FieldKind>(t, true, out var e) ? e : FieldKind.Text;
    }
}

/// <summary>点检表一列（字段）的定义。</summary>
public sealed class ChecklistColumn
{
    public string Header { get; set; } = "";
    public FieldKind Kind { get; set; } = FieldKind.Text;
    public List<string> Options { get; set; } = new();
    public bool Required { get; set; }

    /// <summary>是否可填（上下文列不可填）</summary>
    public bool Fillable => Kind != FieldKind.Context;

    public string KindText => FieldKindText.Of(Kind);
}

/// <summary>
/// 未做任何结构切分的原始表：0 基网格（第 0 行 = 文件第 1 行）+ 合并区域 + 表名。
/// 规则解析与 AI 复核必须基于同一份原始数据，AI 给出的 sourceRef（"R3C7" = 第 3 行第 7 列）
/// 才能对回真实文件坐标。
/// </summary>
public sealed class RawSheet
{
    public string SheetName { get; set; } = "";
    public List<List<string>> Grid { get; set; } = new();
    public List<MergedRange> Merges { get; set; } = new();
    public int RowCount => Grid.Count;
    public int ColCount => Grid.Count == 0 ? 0 : Grid.Max(r => r.Count);

    /// <summary>取 0 基单元格文本，越界返回空串</summary>
    public string Cell(int r, int c) =>
        r >= 0 && r < Grid.Count && c >= 0 && c < Grid[r].Count ? (Grid[r][c] ?? "") : "";
}

/// <summary>合并单元格区域（0 基，含首尾）</summary>
public readonly record struct MergedRange(int R1, int C1, int R2, int C2);

/// <summary>点检表一行（一个检查项）。Cells 与模板 Columns 对齐；可填列的值在提交时由 Values 覆盖。</summary>
public sealed class ChecklistRow
{
    public List<string> Cells { get; set; } = new();
    /// <summary>主键显示，默认取第一列文本</summary>
    public string ItemLabel => Cells.Count > 0 ? Cells[0] : "";
}

/// <summary>
/// 点检表抬头字段（表体上方的信息行，如「车间/线别：____」「点检日期：__年_月_日」）。
/// 原表这些内容在合并单元格里，不属于表体列，但每次提交都要填写 → 单独建模。
/// </summary>
public sealed class ChecklistHeaderField
{
    /// <summary>字段名（冒号前的部分），如「车间/线别」</summary>
    public string Label { get; set; } = "";
    /// <summary>原表的填写提示（冒号后的下划线/年月日占位），仅用于占位文本</summary>
    public string Hint { get; set; } = "";

    /// <summary>占位提示：去掉下划线后剩余的可读部分</summary>
    public string Placeholder => Hint.Replace("_", "").Trim();
}

/// <summary>点检表模板：由导入的 Excel/Word 解析而来，可手工微调列类型。</summary>
public sealed class ChecklistTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public DateTime ImportedAt { get; set; } = DateTime.Now;
    /// <summary>表标题（来自导入文件顶部合并单元格或文件名）</summary>
    public string Title { get; set; } = "";
    public List<ChecklistColumn> Columns { get; set; } = new();
    public List<ChecklistRow> Rows { get; set; } = new();
    public int Version { get; set; } = 1;

    /// <summary>抬头字段（车间/线别、点检日期、班次…），来源于表体上方的信息行</summary>
    public List<ChecklistHeaderField> HeaderFields { get; set; } = new();

    /// <summary>表尾说明（点检说明、签字行等），按原表行合并为文本</summary>
    public List<string> FooterNotes { get; set; } = new();

    /// <summary>
    /// 表体第一行在**原文件网格**里的 0 基行号。
    /// 原样渲染靠它把"可填控件"限制在表体行内——否则抬头行（车间/线别、点检日期…）
    /// 会被套上下面某些列的下拉框（实测把「点检日期」渲染成了下拉）。
    /// </summary>
    public int BodyStartRow { get; set; }

    /// <summary>
    /// 原样渲染主工作表的样式与几何（= <see cref="Sheets"/> 的第一张，保留给旧数据/单表场景）。
    /// 语义与样式分离：这一层只影响"怎么画"，不参与任何校验或取值。
    /// </summary>
    public StyledSheet? Style { get; set; }

    /// <summary>
    /// 全部工作表的原样渲染数据（一张 Excel 常含「点检表 / 记录台账 / 异常追踪表」多张）。
    /// 每张表自带列语义与表体范围，所以填写页可以逐张切换渲染，不会只有第一张能用。
    /// 旧模板此集合为空 → 回落到 <see cref="Style"/>。
    /// </summary>
    public List<StyledSheet> Sheets { get; set; } = new();

    /// <summary>可用于原样渲染的工作表集合（兼容旧数据）</summary>
    public List<StyledSheet> AllSheets =>
        Sheets.Count > 0 ? Sheets : (Style is not null ? new List<StyledSheet> { Style } : new List<StyledSheet>());

    /// <summary>
    /// 下拉框里显示模板名。
    /// **必须重写**：这些下拉用的是 Items.Add 方式（不是 DataSource 绑定），
    /// 此前的 DisplayMember="Name" 对 Items 方式无效，于是下拉框直接显示成类名
    /// 「SuperCapMES.WinForms.Models.ChecklistTemplate」并被 280px 宽度截断。
    /// </summary>
    public override string ToString() => Name;

    /// <summary>可填列索引</summary>
    public List<int> FillableColumns => Columns.Select((c, i) => (c, i)).Where(x => x.c.Fillable).Select(x => x.i).ToList();

    /// <summary>深拷贝（提交时作为快照内嵌，确保后期改模板不影响历史还原）</summary>
    public ChecklistTemplate Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<ChecklistTemplate>(json) ?? new ChecklistTemplate();
    }
}

/// <summary>一次填写提交。TemplateSnapshot 锁定提交时刻的模板格式，Values 锁定当时填写值。</summary>
public sealed class ChecklistSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string TemplateId { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public DateTime SubmittedAt { get; set; } = DateTime.Now;
    public string Submitter { get; set; } = "";
    public string Shift { get; set; } = "";
    public string Device { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>提交时刻的模板完整快照（后期模板修改不影响本记录还原）</summary>
    public ChecklistTemplate TemplateSnapshot { get; set; } = new();

    /// <summary>填写值：key = "r{row}c{col}" → 文本值（仅 Fillable 列）</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    /// <summary>抬头字段填写值：key = 字段名（与 TemplateSnapshot.HeaderFields.Label 对应）</summary>
    public Dictionary<string, string> HeaderValues { get; set; } = new();

    /// <summary>取某抬头字段值</summary>
    public string GetHeaderValue(string label) => HeaderValues.TryGetValue(label, out var v) ? v : "";

    /// <summary>取某单元格值（优先 Values，否则回退快照上下文）</summary>
    public string GetValue(int row, int col)
    {
        var key = $"r{row}c{col}";
        return Values.TryGetValue(key, out var v) ? v : "";
    }

    /// <summary>一行摘要，用于查询列表展示</summary>
    public string Summary(int max = 60)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Submitter)) parts.Add("填写人:" + Submitter);
        if (!string.IsNullOrEmpty(Device)) parts.Add("设备:" + Device);
        if (!string.IsNullOrEmpty(Shift)) parts.Add("班次:" + Shift);
        var s = string.Join("，", parts);
        return s.Length <= max ? s : s[..max] + "…";
    }
}
