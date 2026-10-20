namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 智能排产三级选线 + 六维工艺相似度评分 —— 模型层（技术方案 2.1 / 2.2）
//
// 文档口径：
//   2.1 同型号在制线体优先（+ 三项硬约束：设备无故障 / 工装在线 / 人员资质；产能不足则拆分）
//   2.2 工艺相似度六维评分（核心工序重合 35 / 设备参数兼容 20 / 换型时间成本 20 /
//       工装夹具通用 15 / 人员技能 5 / 质量历史 FTT 5 = 满分 100）→ 取最高分 + 换型指导书
//
// 原则：结论可追问——每个维度都带 score 与 reason，界面摊开给人看。
//      评分是**纯规则**算出来的，不是模型给的（判定权归规则）。
// ============================================================================

/// <summary>选线级别</summary>
public static class LineMatchLevel
{
    public const string SameModelInProduction = "同型号在制线体优先";
    public const string ProcessSimilarity = "工艺相似度匹配";
    public const string LoadFallback = "负载兜底";
}

/// <summary>六维评分中的一维</summary>
public sealed class LineScoreDetail
{
    public string Dimension { get; set; } = "";
    /// <summary>权重（0-1）</summary>
    public double Weight { get; set; }
    /// <summary>本维得分（0-100）</summary>
    public double Score { get; set; }
    /// <summary>加权分 = Score × Weight</summary>
    public double Weighted => Math.Round(Score * Weight, 1);
    public string Reason { get; set; } = "";
    public string DimensionText => $"{Dimension}（{Weight * 100:0}%）";
    public string ScoreText => $"{Score:0.#}";
    public string WeightedText => $"{Weighted:0.#}";
}

/// <summary>单项硬约束校验（2.1 的三项前置条件之一）</summary>
public sealed class HardConstraintCheck
{
    public string Name { get; set; } = "";
    public bool Ok { get; set; }
    public string Note { get; set; } = "";
    public string StateText => Ok ? "通过" : "不通过";
}

/// <summary>一条线体的六维评分</summary>
public sealed class LineScore
{
    public string Line { get; set; } = "";
    public string SegmentName { get; set; } = "";
    /// <summary>综合得分（满分 100）</summary>
    public double Total { get; set; }
    public List<LineScoreDetail> Details { get; set; } = new();
    /// <summary>硬约束是否全部通过（不通过 → 不参与推荐）</summary>
    public bool Allocatable { get; set; } = true;
    public string TotalText => $"{Total:0.#}";
}

/// <summary>换型指导书（2.2 执行逻辑：明确需调整的参数、需更换的工装、预计换型时长）</summary>
public sealed class ChangeoverGuide
{
    public string Line { get; set; } = "";
    public string FromModel { get; set; } = "";
    public string ToModel { get; set; } = "";
    public List<string> NeedParamChange { get; set; } = new();
    public List<string> NeedToolingChange { get; set; } = new();
    public int EstimatedMinutes { get; set; }
    public List<string> Notes { get; set; } = new();
    public string Summary => NeedParamChange.Count == 0 && NeedToolingChange.Count == 0
        ? $"线体 {Line} 与目标型号工艺兼容，无需换型（预计 {EstimatedMinutes} 分钟确认）。"
        : $"线体 {Line} 需换型：调参 {NeedParamChange.Count} 项、换工装 {NeedToolingChange.Count} 项，预计 {EstimatedMinutes} 分钟。";
}

/// <summary>排产选线评估结果（三级命中 + 六维评分 + 硬约束 + 换型指导书）</summary>
public sealed class LineMatchEvaluation
{
    public string PoNo { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string MainSegment { get; set; } = "";

    /// <summary>命中的级别（LineMatchLevel）</summary>
    public string Level { get; set; } = LineMatchLevel.ProcessSimilarity;

    /// <summary>2.1 同型号在制线体（命中线；未命中为空）</summary>
    public string Level1Line { get; set; } = "";
    public string Level1Reason { get; set; } = "";
    /// <summary>产能拆分说明（在制线余产能不足时给出）</summary>
    public string Level1SplitNote { get; set; } = "";

    /// <summary>推荐线体</summary>
    public string RecommendedLine { get; set; } = "";
    public string RecommendedReason { get; set; } = "";

    /// <summary>推荐线体的三项硬约束校验</summary>
    public List<HardConstraintCheck> HardConstraints { get; set; } = new();

    /// <summary>各线六维评分（按总分倒序，供页面展示与选择）</summary>
    public List<LineScore> Candidates { get; set; } = new();

    /// <summary>推荐线体的六维明细（未命中则为空）</summary>
    public LineScore? RecommendedScore { get; set; }

    /// <summary>换型指导书（同型号在制连续生产时为 null）</summary>
    public ChangeoverGuide? Changeover { get; set; }

    /// <summary>可读依据（逐条）</summary>
    public List<string> Evidence { get; set; } = new();

    public bool HardAllOk => HardConstraints.Count > 0 && HardConstraints.All(h => h.Ok);
    public string LevelText => Level;
}
