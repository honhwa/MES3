using SuperCapMES.WinForms.Data;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 跟进提问（P0-2 / P0-3）
//
// 为什么要有这个：
//   ① 固定快捷提问是"入门引导"，用第三次就腻了，而且答完"缺什么料"之后，
//      用户真正想点的是"那要采购哪些""建议买多少"——固定列表答不上这个。
//   ② 问了个没覆盖的问法时，现在直接回一句"未识别"就结束了，等于把路堵死。
//      给出"你是不是想问…"的候选，是最低成本的挽回。
//
// 设计原则：候选**按数据域推导**，不是随机凑数；并且每条都是可执行的完整问法。
// ============================================================================

public static class AssistantFollowUp
{
    /// <summary>按本轮回答给出 3 条下一步该问什么（点一下即问）。</summary>
    public static List<string> Suggest(AssistantReply reply)
    {
        var list = new List<string>();
        if (reply is null) return SampleFallback();

        switch (reply.Intent)
        {
            case AssistantIntent.Material:      // 齐套缺料 → 追问采购与库存
                list.Add("要采购哪些料");
                list.Add("建议采购量是多少");
                list.Add("库存安全线还差多少");
                break;

            case AssistantIntent.Inventory:     // 库存 → 追问效期与呆滞
                list.Add("哪些料快过期了");
                list.Add("哪些料是呆滞料");
                list.Add("哪些批次要优先出库");
                break;

            case AssistantIntent.Alert:         // 预警 → 追问逐类明细
                list.Add("高危的都有哪些");
                list.Add("异常未闭环的有几项");
                list.Add("SPC 预警明细");
                break;

            case AssistantIntent.Abnormal:      // 异常 → 追问未闭环与责任人
                list.Add("还有哪些异常没闭环");
                list.Add("这些异常都归谁负责");
                list.Add("最近返工有多少");
                break;

            case AssistantIntent.Overview:      // 概览 → 追问具体域
                list.Add("今天有什么要处理的");
                list.Add("缺什么料");
                list.Add("设备状态怎么样");
                break;

            case AssistantIntent.Equipment:     // 设备 → 追问报警与保养
                list.Add("哪些设备报警频繁");
                list.Add("点检保养有没有超期的");
                list.Add("最近有哪些设备异常");
                break;

            case AssistantIntent.Trace:         // 追溯 → 追问工序与材料
                list.Add("这只电芯走了哪些工序");
                list.Add("用了哪些材料批次");
                list.Add("测试结果怎么样");
                break;

            case AssistantIntent.Hold:
                list.Add("保留的都有哪些电芯");
                list.Add("保留超期的是哪些");
                break;

            case AssistantIntent.Rework:
                list.Add("返工积压有多少");
                list.Add("返工主要是什么原因");
                break;

            case AssistantIntent.Complaint:
                list.Add("有哪些客户投诉没闭环");
                list.Add("客诉主要问题是什么");
                break;

            case AssistantIntent.Personnel:
                list.Add("张伟的资质有哪些");
                list.Add("谁有卷绕资质");
                list.Add("哪些证书快过期了");
                break;

            case AssistantIntent.Spc:
                list.Add("最近有哪些 SPC 预警");
                list.Add("这个参数的过程能力怎么样");
                break;

            case AssistantIntent.Guide:
                list.Add("今天有什么要处理的");
                list.Add("缺什么料");
                break;

            default:
                return SampleFallback();
        }

        // 去掉和本轮问题重复的，避免"刚问完又推荐同一句"
        var cleaned = list.Where(x => !string.IsNullOrWhiteSpace(x)
                                   && !string.Equals(x.Trim(), reply.Question?.Trim(), StringComparison.Ordinal))
            .ToList();
        return cleaned.Count >= 2 ? cleaned.Take(3).ToList() : SampleFallback();
    }

    /// <summary>未识别意图时的候选问法（挽回"问了个没覆盖的问法"）</summary>
    public static List<string> GuideFromUnknown(AssistantReply reply)
    {
        // 从"未识别"的原因反推：如果是因为没给单号/工号，就引导补全实体
        var q = reply?.Question ?? "";
        var tips = new List<string>();

        if (string.IsNullOrWhiteSpace(q))
            tips.Add("今天有什么要处理的");
        else if (!HasIdentifier(q))
        {
            tips.Add("SC-A260812-L1-00001 这只电芯怎么回事");   // SN 示例
            tips.Add("WO-20260812-001 这个工单现在什么状态"); // 工单示例
            tips.Add("张伟能不能开卷绕机");                   // 人名示例
        }
        else
        {
            tips.Add("今天有什么要处理的");
        }

        tips.AddRange(SampleFallback().Take(2));
        return tips.Distinct().Take(4).ToList();
    }

    /// <summary>问题里是否带了实体（单号 / 工号 / SN / 人名 / 物料码）</summary>
    private static bool HasIdentifier(string q) =>
        q.Contains("SC-", StringComparison.OrdinalIgnoreCase)
        || q.Contains("WO-", StringComparison.OrdinalIgnoreCase)
        || q.Contains("PI-", StringComparison.OrdinalIgnoreCase)
        || q.Contains("PO-", StringComparison.OrdinalIgnoreCase)
        || q.Contains("PR-", StringComparison.OrdinalIgnoreCase)
        || q.Contains("E10", StringComparison.OrdinalIgnoreCase)
        || q.Contains("M-", StringComparison.OrdinalIgnoreCase)
        || HasAnyEntity(AssistantEntityParser.Parse(q));

    /// <summary>实体解析结果里是否有任一具体单号</summary>
    private static bool HasAnyEntity(AssistantEntities e) =>
        !string.IsNullOrWhiteSpace(e.CellSn) || !string.IsNullOrWhiteSpace(e.ModuleSn)
        || !string.IsNullOrWhiteSpace(e.WoNo) || !string.IsNullOrWhiteSpace(e.HoldNo)
        || !string.IsNullOrWhiteSpace(e.LotNo) || !string.IsNullOrWhiteSpace(e.EmpNo);

    private static List<string> SampleFallback()
    {
        var list = new List<string>();
        for (var i = 0; i < AssistantEngine.SampleQuestions.Length && list.Count < 4; i += 2)
            list.Add(AssistantEngine.SampleQuestions[i]);
        return list;
    }
}
