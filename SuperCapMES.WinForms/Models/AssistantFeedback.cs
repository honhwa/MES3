using System.Text;
using SuperCapMES.WinForms.Data;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 答案反馈（P1-6）与主动推送（P1-8）
//
// 【反馈的用途不是收集，而是反哺】
//   收集点赞点踩本身没价值，**按意图分组统计差评率**才有价值：
//   它直接告诉你"哪一类问法答不好" → 该给关键词表补词、该给手册补条目。
//   所以统计维度是**意图**，不是时间也不是总数。
//
// 【为什么落盘】反馈全在内存里的话重启就没了，等于收不到任何长期信号。
//   落盘到 %APPDATA%，按行追加写，不进项目目录。
// ============================================================================

/// <summary>反馈评价</summary>
public static class FeedbackRating
{
    public const string Good = "有帮助";
    public const string Bad = "没答到点";
    public static readonly string[] All = { Good, Bad };
}

/// <summary>一条答案反馈</summary>
public class AssistantFeedback
{
    public string Id { get; set; } = "";
    public string Question { get; set; } = "";
    public string Intent { get; set; } = "";
    public string Rating { get; set; } = FeedbackRating.Good;
    public string Comment { get; set; } = "";
    public string Backend { get; set; } = "";
    public int Confidence { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
}

/// <summary>按意图聚合的反馈统计</summary>
public class IntentFeedbackStat
{
    public string Intent { get; set; } = "";
    public int Good { get; set; }
    public int Bad { get; set; }
    public int Total => Good + Bad;

    /// <summary>差评率（0~1）；总样本 &lt; 3 时不给出结论——样本太少没有意义</summary>
    public double BadRate => Total == 0 ? 0 : (double)Bad / Total;

    /// <summary>样本是否够下结论</summary>
    public bool EnoughSamples => Total >= 3;

    public string BadRateText => Total == 0 ? "—" : $"{BadRate:P0}（{Bad}/{Total}）";
}

/// <summary>反馈总览</summary>
public class FeedbackSummary
{
    public int Total { get; set; }
    public int Good { get; set; }
    public int Bad { get; set; }
    public int WithComment { get; set; }
    public double BadRate => Total == 0 ? 0 : (double)Bad / Total;
    public List<IntentFeedbackStat> ByIntent { get; set; } = new();

    /// <summary>差评率最高且样本够的意图 —— 这就是要补关键词/补手册的地方</summary>
    public List<IntentFeedbackStat> WorstIntents() =>
        ByIntent.Where(i => i.EnoughSamples).OrderByDescending(i => i.BadRate).ThenByDescending(i => i.Total).ToList();

    /// <summary>给操作者看的改进建议（可直接照着做）</summary>
    public List<string> ImprovementHints()
    {
        var hints = new List<string>();
        if (Total == 0) { hints.Add("还没有反馈。答得不准时点 👎 并写一句，统计会告诉你该补哪个域的词。"); return hints; }

        hints.Add($"共 {Total} 条反馈，差评率 {BadRate:P0}（{Bad} 条负反馈）");
        if (WithComment > 0) hints.Add($"其中 {WithComment} 条写了具体意见，建议导出后逐条看");

        var worst = WorstIntents().Take(3).ToList();
        if (worst.Count == 0)
        {
            hints.Add("各意图样本都还不够（每个需 ≥3 条），暂时看不出哪类问法最容易答不准");
            return hints;
        }

        foreach (var w in worst)
        {
            var advice = w.Intent switch
            {
                AssistantIntent.Material or AssistantIntent.Inventory => "补齐料/库存域的关键词（物料名、料号、库位、批次、效期、呆滞、FIFO…）",
                AssistantIntent.Equipment => "补齐设备域的关键词（设备名、报警、点检、保养、稼动…）",
                AssistantIntent.Trace => "补齐追溯域的关键词（SN、模组号、工单号、材料批次…）",
                AssistantIntent.Personnel => "补齐人员资质域的关键词（工号、岗位、证书、有效期…）",
                AssistantIntent.Guide => "补手册条目（操作导航走手册检索，缺条目就是查不到）",
                _ => $"补齐「{w.Intent}」域的关键词，并核对这一域的取数口径是否覆盖用户真正想问的东西"
            };
            hints.Add($"「{w.Intent}」差评率 {w.BadRateText} → {advice}");
        }
        return hints;
    }
}

/// <summary>反馈的存取与统计</summary>
public static class AssistantFeedbackStore
{
    private static readonly List<AssistantFeedback> Items = new();
    private static int _seq;

    /// <summary>落盘路径（%APPDATA%，不进项目目录）</summary>
    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SuperCapMES", "assistant-feedback.csv");

    public static IReadOnlyList<AssistantFeedback> All => Items;

    /// <summary>启动时载入已有反馈</summary>
    public static void Load()
    {
        Items.Clear();
        try
        {
            var path = DefaultFilePath;
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 7) continue;
                Items.Add(new AssistantFeedback
                {
                    Id = parts[0], Question = parts[1], Intent = parts[2], Rating = parts[3],
                    Comment = parts[4], Backend = parts[5], At = DateTime.Parse(parts[6])
                });
            }
            _seq = Items.Count;
        }
        catch { Items.Clear(); }   // 读不了就当没有，不阻断启动
    }

    /// <summary>记录一条反馈（内存 + 追加落盘）</summary>
    public static (bool Ok, string Message) Add(AssistantReply reply, string rating, string comment = "")
    {
        if (reply is null) return (false, "没有可评价的回答。");
        if (!FeedbackRating.All.Contains(rating)) return (false, $"评价取值非法：{rating}");

        var item = new AssistantFeedback
        {
            Id = $"FB-{DateTime.Now:yyyyMMddHHmmss}-{++_seq:000}",
            Question = reply.Question,
            Intent = reply.Intent,
            Rating = rating,
            Comment = (comment ?? "").Trim(),
            Backend = reply.BackendName,
            Confidence = reply.Confidence,
            At = DateTime.Now
        };
        Items.Add(item);

        try
        {
            var path = DefaultFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sb = new StringBuilder();
            sb.Append(item.Id).Append('\t')
              .Append(item.Question.Replace('\t', ' ')).Append('\t')
              .Append(item.Intent).Append('\t')
              .Append(item.Rating).Append('\t')
              .Append(item.Comment.Replace('\t', ' ')).Append('\t')
              .Append(item.Backend).Append('\t')
              .Append(item.At.ToString("yyyy-MM-dd HH:mm:ss"));
            File.AppendAllText(path, sb + Environment.NewLine, new UTF8Encoding(true));
            return (true, "已记录，谢谢反馈。");
        }
        catch (Exception ex)
        {
            return (true, "已记录（本次未能落盘，重启后会丢失这条反馈）。" + ex.Message);
        }
    }

    /// <summary>统计（纯查询）</summary>
    public static FeedbackSummary Summarize()
    {
        var s = new FeedbackSummary
        {
            Total = Items.Count,
            Good = Items.Count(f => f.Rating == FeedbackRating.Good),
            Bad = Items.Count(f => f.Rating == FeedbackRating.Bad),
            WithComment = Items.Count(f => !string.IsNullOrWhiteSpace(f.Comment))
        };
        foreach (var g in Items.GroupBy(f => f.Intent))
        {
            s.ByIntent.Add(new IntentFeedbackStat
            {
                Intent = g.Key,
                Good = g.Count(f => f.Rating == FeedbackRating.Good),
                Bad = g.Count(f => f.Rating == FeedbackRating.Bad)
            });
        }
        return s;
    }

    /// <summary>导出为 Markdown（拿去汇报/评审/给开发看改进点）</summary>
    public static string ExportMarkdown()
    {
        var s = Summarize();
        var sb = new StringBuilder();
        sb.AppendLine("# AI 助手反馈统计");
        sb.AppendLine();
        sb.AppendLine($"> 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}　|　共 {s.Total} 条｜有帮助 {s.Good}｜没答到点 {s.Bad}｜差评率 {s.BadRate:P0}");
        sb.AppendLine();
        sb.AppendLine("## 改进建议（按差评率排序，样本 ≥3 才给结论）");
        sb.AppendLine();
        foreach (var h in s.ImprovementHints()) sb.AppendLine($"- {h}");
        sb.AppendLine();

        if (s.ByIntent.Count > 0)
        {
            sb.AppendLine("## 分意图明细");
            sb.AppendLine();
            sb.AppendLine("| 意图 | 有帮助 | 没答到点 | 差评率 | 样本是否够 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var i in s.ByIntent.OrderByDescending(x => x.BadRate))
                sb.AppendLine($"| {i.Intent} | {i.Good} | {i.Bad} | {i.BadRateText} | {(i.EnoughSamples ? "够" : "不够")} |");
            sb.AppendLine();
        }

        var withComment = Items.Where(f => !string.IsNullOrWhiteSpace(f.Comment)).ToList();
        if (withComment.Count > 0)
        {
            sb.AppendLine("## 具体意见");
            sb.AppendLine();
            foreach (var f in withComment)
                sb.AppendLine($"- 「{f.Question}」→ {f.Comment}　（{f.Intent}，{f.At:yyyy-MM-dd}）");
        }
        return sb.ToString();
    }
}

// ============================================================================
// 主动推送：接「主动预警与决策」当数据源
//
// 形态选择：不做系统弹窗（会打断、也没人看），而是放在助手页顶部一个常驻提醒区——
//   打开助手页就知道"现在有几件事要处理"，点一下直接问到答案。
//   问与答之间没有断层，这才是主动推送该有的样子。
// ============================================================================

public static class AssistantPush
{
    /// <summary>一条待处理提醒</summary>
    public class PushItem
    {
        public string Title { get; set; } = "";        // 提醒标题
        public string Detail { get; set; } = "";       // 一句话说明
        public string Question { get; set; } = "";     // 点一下会问的问题
        public string TargetPage { get; set; } = "";   // 顺带可跳的页面
        public string Level { get; set; } = InsightLevels.Medium;
    }

    /// <summary>从「主动预警与决策」生成待处理提醒（纯查询）</summary>
    public static List<PushItem> BuildPushItems(int max = 4)
    {
        var items = new List<PushItem>();
        InsightReport rep;
        try { rep = MesDatabase.BuildInsightReport(); }
        catch { return items; }   // 分析挂了不该让助手页白屏

        // ① 已过期物料最紧急
        var expired = rep.Expiry.FirstOrDefault(r => r.Action == ExpiryActions.WillWaste);
        if (expired is not null)
            items.Add(new PushItem
            {
                Level = InsightLevels.Urgent,
                Title = $"{expired.MaterialName} 已过期 {Math.Abs(expired.DaysLeft ?? 0)} 天，需立即处置",
                Detail = $"剩余 {expired.StockText}，系统建议 {expired.ActionText}",
                Question = $"{expired.MaterialName}的库存和效期情况",
                TargetPage = "库存物料"
            });

        // ② 需要预防性采购的临期料
        var preBuy = rep.Expiry.FirstOrDefault(r => r.Action == ExpiryActions.PreBuy);
        if (preBuy is not null)
            items.Add(new PushItem
            {
                Level = preBuy.Level,
                Title = $"{preBuy.MaterialName} {preBuy.DaysText}到期，效期内用不完",
                Detail = $"建议预防性采购 {preBuy.SuggestedBuyQty:0.##} {preBuy.Unit}",
                Question = "现在缺什么料，要采购哪些",
                TargetPage = "资材采购申请"
            });

        // ③ 设备异常集中
        var risky = rep.Equipment.FirstOrDefault(r => r.Level == InsightLevels.High);
        if (risky is not null)
            items.Add(new PushItem
            {
                Level = risky.Level,
                Title = $"{risky.EquipmentName} 近期异常集中（{risky.AbnormalText}，{risky.TopSubType}）",
                Detail = string.IsNullOrWhiteSpace(risky.ActionText) ? risky.ReasonText : risky.ActionText,
                Question = "最近有哪些设备异常",
                TargetPage = "设备监控"
            });

        // ④ 呆滞料资金占压
        var slow = rep.SlowMoving.FirstOrDefault();
        if (slow is not null && items.Count < max)
            items.Add(new PushItem
            {
                Level = slow.Level,
                Title = $"{slow.MaterialName} {slow.Grade}，占压 {slow.IdleQtyText}",
                Detail = slow.ActionText,
                Question = "哪些料是呆滞料",
                TargetPage = "库存物料"
            });

        return items.OrderByDescending(i => InsightLevels.Weight(i.Level)).Take(max).ToList();
    }

    /// <summary>一句话摘要，用于状态栏/提醒区标题</summary>
    public static string SummaryText(IReadOnlyList<PushItem> items)
        => items.Count == 0
            ? "当前没有需要马上处理的预警"
            : $"当前有 {items.Count} 项待处理（紧急 {items.Count(i => i.Level == InsightLevels.Urgent)} 项）";
}
