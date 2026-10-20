namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 交期承诺（CTP）+ 换型矩阵 —— 模型层
//
// CTP 要回答的是销售/客户最常问的那句话：**"这个型号 X 只，最快什么时候能交？"**
// 这个问题的难点不在算法，而在**敢不敢承诺**。承诺错了比不承诺更糟，所以本模块把
// 一个交期拆成四段、逐段摊开给人看：
//     ① 换型时间   —— 线体当前在制型号 → 询单型号，要停多久（换工装 / 清洗 / 调参）
//     ② 排队时间   —— 线体在手工单还要占多久（GetLineLoad）
//     ③ 生产时间   —— 投产数（良率补偿）× 单位工时 ÷ 每日产能工时
//     ④ 物料就绪   —— 短缺物料的最长采购前置期（L/T + 检验），料不到就开不了工
//   可承诺日 = max(②+①+③, ④+③)——**不是** max(完工, 到料)，到料后还要再生产。
//
// 承诺分三档，避免"要么答应要么不答应"的二元回答丢掉商机：
//     可承诺     ：最早可交日 ≤ 期望交期
//     有条件承诺 ：整单赶不上，但期望交期前能交一部分 → 承诺「X 只 + 余量 Y 日前」
//     不可承诺   ：连 30% 都交不出（线体被占满 / 物料前置期太长）
//
// 换型矩阵是 CTP 的输入之一：型号 × 型号 → 换型分钟数 + 要换什么。
// 时间不是拍脑袋给的：基础清线 15 分钟 + 四个维度差异（封装尺寸 25 / 电解液体系 40 /
// 极片宽度 25 / 容量段 20）。容量段可从型号解析（100F/300F），其余维度为演示特征。
// 另外给出"最小换型序列"（最近邻贪心）——把一天要做的几个型号排个顺序，能少换几次型。
// ============================================================================

/// <summary>CTP 承诺结论（三档）</summary>
public static class CtpVerdict
{
    /// <summary>可承诺：最早可交日满足期望交期</summary>
    public const string Commit = "可承诺";

    /// <summary>有条件承诺：整单赶不上，但能先交一部分</summary>
    public const string Conditional = "有条件承诺";

    /// <summary>不可承诺</summary>
    public const string Reject = "不可承诺";

    public static readonly string[] All = { Commit, Conditional, Reject };
}

/// <summary>交期瓶颈分类（用于告诉用户"卡在哪一段"）</summary>
public static class CtpBottleneck
{
    public const string None = "无瓶颈";
    public const string Material = "物料齐套";
    public const string Capacity = "线体产能";
    public const string Changeover = "换型停机";
    public const string LineFull = "线体满载";
}

/// <summary>换型要素（换型时间由这些维度累加，界面逐项展示）</summary>
public class ChangeoverInfo
{
    public string FromModel { get; set; } = "";
    public string ToModel { get; set; } = "";

    /// <summary>是否同型号（同型号 = 0 分钟）</summary>
    public bool SameModel { get; set; }

    /// <summary>基础清线（分钟）</summary>
    public int CleanMinutes { get; set; }

    /// <summary>换模具（封装尺寸差异，分钟）</summary>
    public int ToolingMinutes { get; set; }

    /// <summary>管路清洗（电解液体系差异，分钟）</summary>
    public int FlushMinutes { get; set; }

    /// <summary>换辊/切刀（极片宽度差异，分钟）</summary>
    public int RollerMinutes { get; set; }

    /// <summary>换化成工装（容量段差异，分钟）</summary>
    public int FormMinutes { get; set; }

    /// <summary>合计换型时间（分钟）</summary>
    public int Minutes => SameModel ? 0 : CleanMinutes + ToolingMinutes + FlushMinutes + RollerMinutes + FormMinutes;

    public string MinutesText => SameModel ? "0（同型号）" : $"{Minutes} 分钟";

    /// <summary>换型要素说明（一句话说清要动什么）</summary>
    public string DetailText
    {
        get
        {
            if (SameModel) return "同型号连续生产，无需换型。";
            var parts = new List<string> { $"清线 {CleanMinutes}" };
            if (ToolingMinutes > 0) parts.Add($"换模具 {ToolingMinutes}");
            if (FlushMinutes > 0) parts.Add($"管路清洗 {FlushMinutes}");
            if (RollerMinutes > 0) parts.Add($"换辊/切刀 {RollerMinutes}");
            if (FormMinutes > 0) parts.Add($"换化成工装 {FormMinutes}");
            return string.Join(" + ", parts) + " 分钟";
        }
    }
}

/// <summary>换型矩阵（型号 × 型号；行 = 从，列 = 到）</summary>
public class ChangeoverMatrix
{
    public List<string> Models { get; set; } = new();

    /// <summary>矩阵单元：key = "from→to"</summary>
    public Dictionary<string, ChangeoverInfo> Cells { get; set; } = new();

    public ChangeoverInfo? Get(string from, string to)
        => Cells.TryGetValue(Key(from, to), out var c) ? c : null;

    public static string Key(string from, string to) => $"{from}→{to}";

    /// <summary>矩阵中最大换型时间（界面热力着色用）</summary>
    public int MaxMinutes => Cells.Values.Where(c => !c.SameModel).Select(c => c.Minutes).DefaultIfEmpty(0).Max();

    /// <summary>平均换型时间（不含同型号）</summary>
    public double AvgMinutes
    {
        get
        {
            var list = Cells.Values.Where(c => !c.SameModel).Select(c => (double)c.Minutes).ToList();
            return list.Count == 0 ? 0 : Math.Round(list.Average(), 1);
        }
    }
}

/// <summary>换型序列建议（一天要生产多个型号时的最优顺序）</summary>
public class ChangeoverSequence
{
    /// <summary>按建议顺序排列的型号</summary>
    public List<string> Ordered { get; set; } = new();

    /// <summary>建议顺序的总换型时间（分钟）</summary>
    public int TotalMinutes { get; set; }

    /// <summary>按输入顺序的总换型时间（对比用）</summary>
    public int NaiveMinutes { get; set; }

    /// <summary>每一步的换型明细</summary>
    public List<string> Steps { get; set; } = new();

    public int SavedMinutes => Math.Max(0, NaiveMinutes - TotalMinutes);

    public string OrderedText => Ordered.Count == 0 ? "—" : string.Join(" → ", Ordered);
    public string SaveText => SavedMinutes > 0 ? $"比原顺序省 {SavedMinutes} 分钟" : "与原顺序持平";
}

/// <summary>CTP 询单（一次交期询问）</summary>
public class CtpQuery
{
    public string ProductModel { get; set; } = "";
    public string ProductName { get; set; } = "";

    /// <summary>询单数量（只）</summary>
    public int Qty { get; set; }

    /// <summary>客户期望交期</summary>
    public DateTime ExpectedDate { get; set; } = DateTime.Today.AddDays(14);

    public string QtyText => $"{Qty} 只";
    public string ExpectedText => ExpectedDate.ToString("yyyy-MM-dd");
}

/// <summary>某条线对本次询单的可承诺评估（一条线一行）</summary>
public class CtpLineOption
{
    public string Line { get; set; } = "";

    /// <summary>线体当前在制型号（换型起点；无在制为空）</summary>
    public string FromModel { get; set; } = "";

    /// <summary>换型信息</summary>
    public ChangeoverInfo Changeover { get; set; } = new();

    /// <summary>线体最早可腾出手的时间（排队结束）</summary>
    public DateTime LineFreeTime { get; set; }

    /// <summary>计划开工 = 腾出手 + 换型</summary>
    public DateTime PlannedStart { get; set; }

    /// <summary>计划完工（不含物料等待）</summary>
    public DateTime PlannedFinish { get; set; }

    /// <summary>物料就绪日（无短缺 = 今天）</summary>
    public DateTime MaterialReadyDate { get; set; }

    /// <summary>短缺物料条数</summary>
    public int ShortageCount { get; set; }

    /// <summary>短缺物料说明（含最长前置期）</summary>
    public string MaterialText { get; set; } = "";

    /// <summary>最早可交日 = max(计划完工, 物料就绪 + 生产时间)</summary>
    public DateTime EarliestDeliver { get; set; }

    /// <summary>客户期望交期（用于判定是否满足 / 差几天）</summary>
    public DateTime ExpectedDate { get; set; } = DateTime.Today;

    /// <summary>是否满足期望交期</summary>
    public bool MeetExpected => EarliestDeliver.Date <= ExpectedDate.Date;

    /// <summary>超出期望交期的天数（0 = 满足）</summary>
    public int DelayDays => Math.Max(0, (int)(EarliestDeliver.Date - ExpectedDate.Date).TotalDays);

    /// <summary>该线是否可分配（硬约束：设备/工装/人员）</summary>
    public bool Feasible { get; set; } = true;

    /// <summary>不可分配原因</summary>
    public string BlockReason { get; set; } = "";

    /// <summary>评分（越早、瓶颈越少越高）</summary>
    public double Score { get; set; }

    /// <summary>瓶颈分类</summary>
    public string Bottleneck { get; set; } = CtpBottleneck.None;

    /// <summary>瓶颈说明</summary>
    public string BottleneckText { get; set; } = "";

    /// <summary>期望交期前最多能交多少（部分承诺用）</summary>
    public int PromiseQty { get; set; }

    /// <summary>推理依据（逐条，界面上摊开）</summary>
    public List<string> Evidence { get; set; } = new();

    public string LineFreeText => LineFreeTime.ToString("MM-dd HH:mm");
    public string StartText => PlannedStart.ToString("MM-dd HH:mm");
    public string FinishText => PlannedFinish.ToString("MM-dd");
    public string MaterialReadyText => MaterialReadyDate.ToString("MM-dd");
    public string EarliestText => EarliestDeliver.ToString("yyyy-MM-dd");
    public string ChangeoverText => Changeover.MinutesText;
    public string MeetText => MeetExpected ? "满足" : $"超 {DelayDays} 天";
    public string PromiseText => $"{PromiseQty} 只";
    public string ScoreText => Feasible ? $"{Score:0.#}" : "—";
    public string FeasibleText => Feasible ? "可分配" : "不可分配";
}

/// <summary>CTP 评估结果</summary>
public class CtpResult
{
    public CtpQuery Query { get; set; } = new();

    /// <summary>各线体评估（已按可交日排序）</summary>
    public List<CtpLineOption> Options { get; set; } = new();

    /// <summary>推荐线体</summary>
    public string BestLine { get; set; } = "";

    /// <summary>推荐线的评估（推荐线 = 可分配里最早可交的那条）</summary>
    public CtpLineOption? BestOption { get; set; }

    /// <summary>承诺结论（可承诺 / 有条件承诺 / 不可承诺）</summary>
    public string Verdict { get; set; } = CtpVerdict.Reject;

    /// <summary>建议承诺日</summary>
    public DateTime CommitDate { get; set; }

    /// <summary>建议承诺数量（整单或部分）</summary>
    public int CommitQty { get; set; }

    /// <summary>结论说明（给销售/客户看的一段话）</summary>
    public string SummaryText { get; set; } = "";

    /// <summary>逐条依据</summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>投产数量（良率补偿后）</summary>
    public int InputQty { get; set; }

    /// <summary>良率</summary>
    public double Yield { get; set; }

    /// <summary>单位工时（h/只）</summary>
    public double UnitHours { get; set; }

    /// <summary>总需求工时</summary>
    public double RequiredHours { get; set; }

    public string CommitDateText => CommitDate == default ? "—" : CommitDate.ToString("yyyy-MM-dd");
    public string CommitQtyText => $"{CommitQty} 只";
    public string InputText => $"{InputQty} 只（良率 {Yield:P1} 补偿）";
    public string HoursText => $"{RequiredHours:0.##} h";
}
