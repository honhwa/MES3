namespace SuperCapMES.WinForms.Models;

/// <summary>AI 与规则结果的比对结论（三色确认页的取值）。</summary>
public enum AiMatchState
{
    /// <summary>绿：模型与规则一致，且模型给的值能在原文找到 → 可直接采纳</summary>
    Agree,
    /// <summary>黄：模型与规则不一致 / 置信度偏低 / 模型漏项 → 需人工过一眼</summary>
    Conflict,
    /// <summary>红：模型给的值在原文里找不到，或坐标越界 → 疑似编造，不采纳</summary>
    Hallucination,
}

/// <summary>模型对「一列（一个填写项）」的判断。Header/Options 一律要求是原文，模型不得改写。</summary>
public sealed class AiFieldClaim
{
    /// <summary>列名（必须是原文单元格里的文本）</summary>
    public string Header { get; set; } = "";
    public FieldKind Kind { get; set; } = FieldKind.Text;
    public bool Fillable { get; set; } = true;
    public bool Required { get; set; }
    public List<string> Options { get; set; } = new();

    /// <summary>依据位置，如 "R3C7"（1 基行号 + 1 基列号）</summary>
    public string SourceRef { get; set; } = "";
    public double Confidence { get; set; }
    /// <summary>模型的判断理由（给人工复核时看）</summary>
    public string Evidence { get; set; } = "";
}

/// <summary>模型对整张表的判断结果（不含数据行内容，只判结构）。</summary>
public sealed class AiSheetClaim
{
    public string Title { get; set; } = "";
    /// <summary>表头所在行（1 基，对回原文件）</summary>
    public int HeaderRow { get; set; }

    public List<AiFieldClaim> Columns { get; set; } = new();
    public List<AiFieldClaim> HeaderFields { get; set; } = new();
    public List<string> FooterNotes { get; set; } = new();

    // ---- 审计信息：出问题时必须能复现 ----
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
}

/// <summary>三色确认页的一行：规则一列 vs 模型一列。</summary>
public sealed class AiReviewRow
{
    /// <summary>规则列下标；-1 表示模型多出来的列（规则里没有）</summary>
    public int RuleCol { get; set; } = -1;
    public string RuleHeader { get; set; } = "";
    public FieldKind RuleKind { get; set; }
    public bool RuleRequired { get; set; }

    public string AiHeader { get; set; } = "";
    public FieldKind AiKind { get; set; }
    public bool AiRequired { get; set; }
    public List<string> AiOptions { get; set; } = new();

    public double Confidence { get; set; }
    public string SourceRef { get; set; } = "";
    public AiMatchState State { get; set; }
    public string Reason { get; set; } = "";

    /// <summary>是否采纳 AI 判断（绿默认勾选，黄/红默认不勾，人工可改）</summary>
    public bool TakeAi { get; set; }

    public string StateText => State switch
    {
        AiMatchState.Agree => "一致",
        AiMatchState.Conflict => "待确认",
        _ => "疑似编造",
    };

    /// <summary>原文定位：解析 sourceRef → 0 基 (行, 列)；失败返回 (-1,-1)</summary>
    public (int Row, int Col) ResolveRef()
    {
        var s = (SourceRef ?? "").Trim().ToUpperInvariant();
        if (!s.StartsWith('R')) return (-1, -1);
        int cIdx = s.IndexOf('C');
        if (cIdx <= 1) return (-1, -1);
        if (!int.TryParse(s[1..cIdx], out var r)) return (-1, -1);
        if (!int.TryParse(s[(cIdx + 1)..], out var c)) return (-1, -1);
        return (r - 1, c - 1);
    }
}

/// <summary>AI 复核的整体结果（含降级原因、缓存标记、提示清单）。</summary>
public sealed class AiAnalysisResult
{
    public bool Ok { get; set; }
    /// <summary>未生效的原因（未配置 / 超时 / 校验不通过…），UI 直接展示这句</summary>
    public string Reason { get; set; } = "";
    /// <summary>来源说明（模型/缓存/降级），界面必须显示</summary>
    public string TraceText { get; set; } = "";
    public bool FromCache { get; set; }
    public RawSheet? Raw { get; set; }
    public AiSheetClaim? Claim { get; set; }
    public List<AiReviewRow> Rows { get; set; } = new();
    /// <summary>完整性差集、位置非法等提示（必须显式呈现，禁止静默丢弃）</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>抬头字段与模型不一致时，是否一并按模型结果替换（需人工勾选，默认否）</summary>
    public bool ApplyHeaderFields { get; set; }

    public int AgreeCount => Rows.Count(r => r.State == AiMatchState.Agree);
    public int ConflictCount => Rows.Count(r => r.State == AiMatchState.Conflict);
    public int HallucinationCount => Rows.Count(r => r.State == AiMatchState.Hallucination);
}
