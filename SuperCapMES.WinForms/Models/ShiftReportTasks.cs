using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// ⑥ 班报 / 日报一键生成（AiTasks 家族第 6 类）
//
// 分工铁律（与前 5 类完全一致）：
//   · 规则负责"取数"：产量/良率/OEE/异常/工单全部由 MesDatabase 汇总算出，本地确定、可核对；
//   · 模型只负责"措辞"：把已经算好的指标组织成通顺的班报/日报文字；
//   · 模型给的任何数字都要过白名单核对（LlmService.CallJson 的 allowedNumbers）；
//   · 未启用 / 超时 / 解析失败 / 数字越界 / 置信度不足 → 一律降级到本地模板，页面不翻车。
//
// 与 AI 助手的关系：这里是"数据驱动"（指标已算好，让模型写成报告），
// 助手是"问题驱动"（问一句话 → 取数 → 措辞）。两者共用 LlmService 与同一套数字核对口径。
// ============================================================================

/// <summary>班报/日报取数输入（全部由规则算好，模型不得更改）</summary>
public sealed class ShiftReportInput
{
    public string Kind { get; set; } = "日报";        // 班报 / 日报
    public string DateText { get; set; } = "";
    public int TodayOutput { get; set; }
    public double TodayYield { get; set; }
    public int TodayTests { get; set; }
    public double AvgOee { get; set; }
    public double TargetOee { get; set; } = 85;
    public double? OutputWow { get; set; }
    public int MonthOutput { get; set; }
    public int OpenAbnormal { get; set; }
    public int TodayNewAbnormal { get; set; }
    public List<string> AbnormalTop { get; set; } = new();
    public List<string> LowOeeDevices { get; set; } = new();
    public int RunningWo { get; set; }
    public int DoneWo { get; set; }
}

/// <summary>班报/日报生成结果（模型版与规则版同构，页面统一渲染、无缝降级）</summary>
public sealed class ShiftReportResult
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Highlights { get; set; } = new();   // 产量/良率/OEE 亮点
    public List<string> Concerns { get; set; } = new();      // 异常/关注设备
    public List<string> Handover { get; set; } = new();      // 交接要点(班报) / 改进建议(日报)
    public double Confidence { get; set; } = 1.0;
}

public static class ShiftReportTasks
{
    /// <summary>生成班报/日报：先规则取数与模板兜底，再视云端可用性让模型润色。</summary>
    public static LlmCallResult<ShiftReportResult> Generate(ShiftReportInput input, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rule = BuildRuleReport(input);

        if (!LlmService.Enabled)
            return new LlmCallResult<ShiftReportResult>
            {
                Ok = true, Value = rule, Source = LlmSources.Rule, Confidence = 1.0,
                Reason = "云端大模型未启用或未配置 API Key，已用本地模板生成"
            };

        var allowed = CollectAllowed(input);

        var payload = new
        {
            kind = input.Kind,
            date = input.DateText,
            todayOutput = input.TodayOutput.ToString(),
            todayYield = input.TodayYield.ToString("0.##"),
            todayTests = input.TodayTests.ToString(),
            avgOee = input.AvgOee.ToString("0.#"),
            targetOee = input.TargetOee.ToString("0.#"),
            outputWow = input.OutputWow is null
                ? "无环比数据"
                : (input.OutputWow.Value >= 0 ? "+" : "") + input.OutputWow.Value.ToString("0.#") + "%",
            monthOutput = input.MonthOutput.ToString(),
            openAbnormal = input.OpenAbnormal.ToString(),
            todayNewAbnormal = input.TodayNewAbnormal.ToString(),
            abnormalTop = input.AbnormalTop,
            lowOeeDevices = input.LowOeeDevices,
            runningWo = input.RunningWo.ToString(),
            doneWo = input.DoneWo.ToString(),
            // 授权数字白名单：显式告诉模型哪些数字可原样照抄（实战经验：不给白名单模型会自行推算）
            allowedNumbers = allowed.OrderBy(x => x, StringComparer.Ordinal).ToList()
        };

        var r = LlmService.CallJson<ShiftReportResult>(SysReport(), payload, root =>
        {
            var v = new ShiftReportResult
            {
                Title = LlmService.Str(root, "title"),
                Summary = LlmService.Str(root, "summary"),
                Highlights = LlmService.StrList(root, "highlights"),
                Concerns = LlmService.StrList(root, "concerns"),
                Handover = LlmService.StrList(root, "handover"),
                Confidence = LlmService.Dbl(root, "confidence", 0.5)
            };
            if (v.Summary.Length == 0) return (null, v.Confidence, "模型未给出总评");
            if (v.Highlights.Count == 0) return (null, v.Confidence, "模型未给出产量/良率/OEE 要点");
            if (v.Title.Length == 0) v.Title = rule.Title;
            return (v, v.Confidence, "");
        }, minConfidence: 0.55, allowedNumbers: allowed, http: http, maxTokens: 1600);

        // 模型不可用 / 未过校验 → 降级本地模板（Ok 仍为 true，页面照常展示，Reason 说明原因）
        return r.Ok && r.Value is not null
            ? r
            : new LlmCallResult<ShiftReportResult>
            {
                Ok = true, Value = rule, Source = LlmSources.Rule, Confidence = 1.0,
                Reason = string.IsNullOrEmpty(r.Reason) ? "模型未生效，已用本地模板生成" : r.Reason,
                LatencyMs = r.LatencyMs
            };
    }

    private static string SysReport() => string.Join("\n", new[]
    {
        "你是超级电容工厂的班报/日报撰写助手，面向工厂管理者与现场交接班主管。",
        "",
        "【绝对规则】",
        "1. 只输出 JSON，不要任何解释文字、不要 Markdown 代码块以外的内容。",
        "2. **只能使用用户消息里给出的数字**，原样照抄，严禁编造、推算、四舍五入或换算单位。",
        "   用户消息里的【allowedNumbers】就是授权数字白名单，一个都不许多、一个都不许改。",
        "3. 你的任务只是把系统已经算好的指标组织成通顺的班报/日报文字，不做合格/达标之类的质量判定。",
        "4. 用工厂里的大白话，不客套，不重复罗列数字清单。",
        "",
        "【输出格式】严格遵守：",
        "{\"title\":\"\",\"summary\":\"\",\"highlights\":[],\"concerns\":[],\"handover\":[],\"confidence\":0.0}",
        "- title：班报标题（含类型与日期）",
        "- summary：一句话总评，不超过 60 字",
        "- highlights：2 到 4 条产量/良率/OEE 亮点，每条不超过 40 字",
        "- concerns：0 到 3 条异常/关注设备，每条不超过 40 字；没有问题就给空数组",
        "- handover：2 到 3 条交接要点或改进建议，每条不超过 40 字"
    });

    /// <summary>本地模板班报（降级路径 + 永远可用、零幻觉）。数字全部来自 input，格式与白名单一致。</summary>
    private static ShiftReportResult BuildRuleReport(ShiftReportInput input)
    {
        var wow = input.OutputWow is null
            ? "无环比"
            : (input.OutputWow.Value >= 0 ? $"↑+{input.OutputWow.Value:0.#}%" : $"↓{input.OutputWow.Value:0.#}%");

        var summary = $"{input.DateText} 产量 {input.TodayOutput:N0} 只（环比 {wow}），"
                    + $"良率 {input.TodayYield:0.##}%，OEE 均值 {input.AvgOee:0.#}%；未闭环异常 {input.OpenAbnormal} 单。";

        var highlights = new List<string>
        {
            $"产量：{input.TodayOutput:N0} 只，环比 {wow}；本月累计 {input.MonthOutput:N0} 只",
            $"良率：{input.TodayYield:0.##}%（测试 {input.TodayTests:N0} 只）",
            $"OEE：均值 {input.AvgOee:0.#}%，目标 {input.TargetOee:0.#}%",
            $"工单：生产中 {input.RunningWo} 单，已完成 {input.DoneWo} 单"
        };

        var concerns = new List<string>();
        if (input.OpenAbnormal > 0) concerns.Add($"未闭环异常 {input.OpenAbnormal} 单，今日新增 {input.TodayNewAbnormal} 单");
        concerns.AddRange(input.LowOeeDevices.Take(2));
        if (concerns.Count == 0) concerns.Add("暂无异常与低 OEE 设备");

        var handover = new List<string>();
        if (input.OpenAbnormal > 0) handover.Add($"下一班重点跟进 {input.OpenAbnormal} 单未闭环异常");
        if (input.LowOeeDevices.Count > 0) handover.Add("关注低 OEE 设备，核实停机/换型原因");
        handover.Add(input.Kind == "班报" ? "交接时核对在制工单进度与物料齐套" : "持续跟踪良率与 OEE 趋势，异常不过班");
        if (handover.Count < 2) handover.Add("保持标准作业，工艺参数按版本执行");

        return new ShiftReportResult
        {
            Title = $"{input.Kind}（{input.DateText}）",
            Summary = summary,
            Highlights = highlights,
            Concerns = concerns,
            Handover = handover,
            Confidence = 1.0
        };
    }

    /// <summary>授权数字白名单：从 input 的规整格式字段 + 文本里抽数字（与 payload 同口径，避免误拦）。</summary>
    private static HashSet<string> CollectAllowed(ShiftReportInput input)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Feed(string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            var t = s.Replace(",", "").Replace("，", "");
            foreach (Match m in Regex.Matches(t, @"\d+(?:\.\d+)?"))
            {
                var v = m.Value.Contains('.') ? m.Value.TrimEnd('0').TrimEnd('.') : m.Value;
                if (v.Length > 0 && v != "0") set.Add(v);
            }
        }
        Feed(input.TodayOutput.ToString());
        Feed(input.TodayYield.ToString("0.##"));
        Feed(input.AvgOee.ToString("0.#"));
        Feed(input.TargetOee.ToString("0.#"));
        if (input.OutputWow is not null) Feed(Math.Abs(input.OutputWow.Value).ToString("0.#"));
        Feed(input.MonthOutput.ToString());
        Feed(input.TodayTests.ToString());
        Feed(input.OpenAbnormal.ToString());
        Feed(input.TodayNewAbnormal.ToString());
        Feed(input.RunningWo.ToString());
        Feed(input.DoneWo.ToString());
        foreach (var s in input.AbnormalTop) Feed(s);
        foreach (var s in input.LowOeeDevices) Feed(s);
        Feed(input.DateText);   // 日期数字（2026-10-15），允许模型在标题里引用
        return set;
    }
}
