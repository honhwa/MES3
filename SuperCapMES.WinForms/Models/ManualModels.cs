using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 用户手册条目：一条 = 一个可被检索的「操作」。
/// 设计要点：条目不描述界面，而描述「用户想干的事」——用户输入的是意图（"我要改工单数量"），
/// 手册要回答三件事：去哪个页面、点哪个按钮、按什么路径走。
/// </summary>
public class ManualEntry
{
    /// <summary>唯一编号（供"相关操作"互相引用）</summary>
    public string Id { get; init; } = "";

    /// <summary>手册分类（与左侧/顶部导航的一级分类保持一致，便于分组展示）</summary>
    public string Category { get; init; } = "";

    /// <summary>操作名（尽量用动词短语，如「修改工单数量」）</summary>
    public string Title { get; init; } = "";

    /// <summary>口语化说法（用户可能怎么说这件事，权重仅次于操作名）</summary>
    public string[] Aliases { get; init; } = Array.Empty<string>();

    /// <summary>关键词（业务术语、字段名、状态名等）</summary>
    public string[] Keywords { get; init; } = Array.Empty<string>();

    /// <summary>一句话说明（什么时候用、解决什么问题）</summary>
    public string Summary { get; init; } = "";

    /// <summary>去哪：目标二级页面标题（必须与导航中真实页签标题完全一致，跳转依赖它）</summary>
    public string TargetPage { get; init; } = "";

    /// <summary>切哪个子页（页面内的三级页签，仅用于提示，不参与跳转；无则留空）</summary>
    public string TargetSubPage { get; init; } = "";

    /// <summary>点什么：按钮文本（必须与页面上的按钮文字一致；无则留空）</summary>
    public string TargetButton { get; init; } = "";

    /// <summary>操作路径（分步，按顺序显示）</summary>
    public string[] Steps { get; init; } = Array.Empty<string>();

    /// <summary>提示 / 前置条件 / 常见坑</summary>
    public string Tip { get; init; } = "";

    /// <summary>相关操作的 Id（用于"还可以看看"）</summary>
    public string[] SeeAlso { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 手册内的编写序号（由 <see cref="ManualCatalog"/> 构建时自动赋值）。
    /// 用途：搜索结果同分时的排序依据——手册是按业务主次顺序编写的，
    /// 同分时保持这个顺序，比按分类名字母序排更符合设计意图
    /// （否则"仓"字恰好小于"质"字就会让仓储条目压过质量条目，纯属偶然）。
    /// </summary>
    public int Order { get; set; }

    /// <summary>展示用：一句话概括去哪儿（"总览 › 预警中心"）</summary>
    public string WhereText => string.IsNullOrWhiteSpace(TargetSubPage)
        ? $"{Category} › {TargetPage}"
        : $"{Category} › {TargetPage} › {TargetSubPage}";

    /// <summary>展示用：点什么（无按钮时给占位）</summary>
    public string ButtonText => string.IsNullOrWhiteSpace(TargetButton) ? "—（本页无需点按钮，直接查看或输入）" : TargetButton;
}

/// <summary>手册搜索结果：条目 + 相关度 + 命中原因（让用户明白"为什么返回这条"）。</summary>
public class ManualSearchHit
{
    public ManualEntry Entry { get; init; } = new();

    /// <summary>相关度得分（越大越相关）</summary>
    public int Score { get; init; }

    /// <summary>命中原因（如「口语说法包含『改工单数量』」），展示给用户以建立信任</summary>
    public string Reason { get; init; } = "";

    /// <summary>结果列表展示：匹配程度标签</summary>
    public string MatchLevel => Score >= 175 ? "精确"
        : Score >= 120 ? "高度相关"
        : Score >= 80 ? "相关"
        : "模糊";

    /// <summary>结果列表展示：相关度条（1~5 格）</summary>
    public int Stars => Score >= 200 ? 5
        : Score >= 150 ? 4
        : Score >= 110 ? 3
        : Score >= 70 ? 2
        : 1;
}

/// <summary>
/// 手册模糊搜索：纯本地打分排序，无外部依赖。
///
/// 打分模型 = 「主命中分」+「覆盖加成（≤40）」。这样设计的原因：
///   只取最高命中项会出现大量同分（"登记客诉" 与 "登记新员工" 都只命中关键词"登记"），
///   导致排序退化成按分类名排——用户就会看到"问客诉却推荐人员档案"。
///   加入覆盖加成后，命中了查询里更多内容的条目自然胜出。
///
/// 主命中分档（取最高）：
///   300 操作名完全相同    180 别名完全相同    150 操作名包含
///   110 别名命中（双向）  70~105 关键词命中（按覆盖查询的比例加权）
///   75 按钮名包含         70 目标页面名包含
///   65 近似匹配（编辑距离=1，用于错别字）
///   55 说明包含           45 步骤包含
///
/// 覆盖加成：字符二元组覆盖率×55 + 单字覆盖率(超 0.3 部分)×45，上限 40。
/// 无任何主命中时，覆盖加成直接作为得分（纯模糊召回）。
/// </summary>
public static class ManualSearch
{
    /// <summary>低于该分值不返回（过滤噪音）</summary>
    public const int Threshold = 32;

    /// <summary>执行检索。空查询返回全部条目（按分类/标题排序），便于浏览。</summary>
    public static List<ManualSearchHit> Search(string? query, int limit = 60)
    {
        var q = Normalize(query);
        if (q.Length == 0)
        {
            return ManualCatalog.All
                .OrderBy(e => e.Order)   // 浏览模式按手册编写顺序展示
                .Take(limit)
                .Select(e => new ManualSearchHit { Entry = e, Score = 0, Reason = "浏览全部操作" })
                .ToList();
        }

        var hits = new List<ManualSearchHit>();
        foreach (var e in ManualCatalog.All)
        {
            var (score, reason) = Score(e, q);
            if (score >= Threshold)
                hits.Add(new ManualSearchHit { Entry = e, Score = score, Reason = reason });
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Entry.Order)   // 同分保持手册的编写顺序（业务主次），而非按分类名字母序
            .Take(limit)
            .ToList();
    }

    /// <summary>按 Id 精确取条目（供"相关操作"跳转）。</summary>
    public static ManualEntry? ById(string id)
        => string.IsNullOrWhiteSpace(id) ? null : ManualCatalog.All.FirstOrDefault(e => e.Id == id);

    /// <summary>对单条目打分并给出命中原因。</summary>
    private static (int Score, string Reason) Score(ManualEntry e, string q)
    {
        int main = 0;
        string reason = "";
        void Put(int score, string why)
        {
            if (score <= main) return;
            main = score;
            reason = why;
        }

        // 1) 操作名
        var title = Normalize(e.Title);
        if (title == q) return (300, $"操作名完全匹配：{e.Title}");
        if (title.Contains(q, StringComparison.Ordinal)) Put(150, $"操作名包含「{q}」");

        // 2) 口语化说法（双向：用户的话里含别名，或别名含用户的话）
        foreach (var a in e.Aliases)
        {
            var na = Normalize(a);
            if (na.Length == 0) continue;
            if (na == q) Put(180, $"口语说法完全匹配：{a}");
            else if (na.Contains(q, StringComparison.Ordinal) || q.Contains(na, StringComparison.Ordinal))
                Put(110, $"口语说法命中：{a}");
        }

        // 3) 关键词：按"关键词覆盖了查询多少内容"加权——命中两个词的条目强于只命中一个的
        int kwHitChars = 0;
        foreach (var k in e.Keywords)
        {
            var nk = Normalize(k);
            if (nk.Length == 0) continue;
            if (nk.Contains(q, StringComparison.Ordinal) || q.Contains(nk, StringComparison.Ordinal))
                kwHitChars += nk.Length;
        }
        if (kwHitChars > 0)
        {
            double cov = Math.Min(1.0, (double)kwHitChars / q.Length);
            Put(70 + (int)Math.Round(cov * 35), $"关键词命中（覆盖查询的 {cov:0%}）");
        }

        // 4) 按钮名 / 目标页面名
        if (e.TargetButton.Length > 0 && Normalize(e.TargetButton).Contains(q, StringComparison.Ordinal))
            Put(75, $"按钮名包含「{q}」");
        if (e.TargetPage.Length > 0 && Normalize(e.TargetPage).Contains(q, StringComparison.Ordinal))
            Put(70, $"位于「{e.TargetPage}」页面");

        // 5) 说明 / 步骤
        if (Normalize(e.Summary).Contains(q, StringComparison.Ordinal))
            Put(55, "操作说明命中");
        if (e.Steps.Any(s => Normalize(s).Contains(q, StringComparison.Ordinal)))
            Put(45, "操作步骤命中");

        // 6) 近似匹配兜底：短查询与某个别名/关键词/操作名只差一个字（打错字、同音字）
        //    只对 2~5 字查询生效，且长度必须相近，避免把长句误配到无关短词上
        if (q.Length is >= 2 and <= 5)
        {
            foreach (var w in e.Aliases.Concat(e.Keywords).Append(e.Title).Select(Normalize))
            {
                if (w.Length == 0 || Math.Abs(w.Length - q.Length) > 1) continue;
                if (Levenshtein(w, q) == 1)
                {
                    Put(65, $"近似匹配（可能有个字写错了）：{w}");
                    break;
                }
            }
        }

        // 7) 覆盖加成：让"命中内容更多"的条目在同上分时胜出；无主命中时作为纯模糊召回分
        var hay = Normalize(string.Join(" ",
            new[] { e.Title, string.Join(" ", e.Aliases), string.Join(" ", e.Keywords), e.Summary, e.TargetPage, e.TargetButton, e.Category, e.TargetSubPage }));

        double bigram = BigramCoverage(q, hay);
        double chars = CharCoverage(q, hay);
        int fuzzy = (int)Math.Round(bigram * 55 + Math.Max(0, chars - 0.3) / 0.7 * 45);

        if (main > 0)
        {
            int bonus = Math.Min(40, fuzzy);
            if (bonus > 0) main += bonus;
        }
        else
        {
            main = fuzzy;
            reason = $"模糊匹配（词组相关度 {bigram:0%}，单字覆盖 {chars:0%}）";
        }

        return (main, reason);
    }

    /// <summary>字符二元组覆盖率：查询切成相邻两字一组，统计有多少组在目标文本中出现过。</summary>
    private static double BigramCoverage(string q, string hay)
    {
        if (q.Length < 2) return 0;
        int total = 0, hit = 0;
        for (int i = 0; i <= q.Length - 2; i++)
        {
            total++;
            if (hay.Contains(q.Substring(i, 2), StringComparison.Ordinal)) hit++;
        }
        return total == 0 ? 0 : (double)hit / total;
    }

    /// <summary>单字覆盖率：查询里出现过的不同字符，有多少能在目标文本里找到（兜底容错，处理倒装与漏字）。</summary>
    private static double CharCoverage(string q, string hay)
    {
        if (q.Length == 0) return 0;
        var distinct = q.Distinct().ToArray();
        if (distinct.Length == 0) return 0;
        return (double)distinct.Count(c => hay.IndexOf(c) >= 0) / distinct.Length;
    }

    /// <summary>编辑距离（Levenshtein）：用两行的滚动数组实现，查询都很短，够快也够简单。</summary>
    internal static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>归一化：去空白、全角转半角、统一小写——让「ERP 报工」与「erp报工」「ＥＲＰ报工」等价。</summary>
    internal static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\u3000' || char.IsWhiteSpace(ch)) continue;   // 半角/全角空格一并去掉
            char c = ch;
            if (ch >= '\uFF01' && ch <= '\uFF5E') c = (char)(ch - 0xFEE0);  // 全角 ASCII → 半角
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
