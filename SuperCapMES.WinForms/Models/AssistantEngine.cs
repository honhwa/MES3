using System;
using System.Collections.Generic;
using System.Linq;
using SuperCapMES.WinForms.Data;

namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 规则驱动的助手后端（当前默认后端，**不调用任何外部模型**）。
///
/// 工作方式分三步，全部在本地完成：
///   ① 抽实体 —— 正则从提问里认出 SN / 工单 / 批次 / 工号 / 设备 / 单号；
///   ② 判意图 —— 关键词分两档打分（业务术语档权重高、疑问词档权重低），选出 12 类意图之一；
///   ③ 取数据 —— 命中意图后调用 <see cref="MesDatabase"/> 的**只读汇总方法**取实时值，生成回答。
///
/// 三条硬约束（与方案里给用户的口径一致）：
///   · 只读：绝不调用任何会写数据的仓储方法，问答不产生任何副作用；
///   · 可引用：每条回答都带「数据依据」，写明取自哪个汇总方法；
///   · 不判定：只做汇总与呈现，不给合格/不合格结论——质量判定永远归 SPC 规则与人工。
/// </summary>
public sealed class RuleBasedAssistant : IAssistantBackend
{
    /// <summary>后端标识（界面与回答里都会标注，避免把规则匹配说成模型推理）</summary>
    private const string BackendLabel = "本地规则引擎（未接大模型）";

    /// <inheritdoc />
    public string Name => BackendLabel;

    /// <inheritdoc />
    public string Describe =>
        "先正则抽编号，再按业务术语关键词判 12 类意图，最后调 MesDatabase 只读汇总取实时数据。"
        + "全程本地计算，不联外网、不调模型、不改动任何数据。";

    // ================= 关键词表 =================
    // 分两档：
    //   A 档 = 业务术语（库存 / 客诉 / 返工…），权重高，决定"问的是哪个域"；
    //   B 档 = 疑问词（情况 / 怎么样…），权重低，只起辅助确认作用。
    // 分档是为了避免"客诉情况怎么样"被"情况"两个字带偏到生产概览。

    private static readonly (string Intent, string[] TierA, string[] TierB)[] KeywordTable =
    {
        (AssistantIntent.Alert,
            new[] { "要处理", "要做什么", "该干什么", "要处理什么", "待办", "待处理", "预警中心", "预警", "报警", "有什么问题", "问题清单", "积压", "待办事项" },
            new[] { "今天", "现在", "有什么" }),

        (AssistantIntent.Overview,
            new[] { "产量", "产出", "良率", "在制", "wip", "看板", "概览", "总体", "整体", "生产情况", "产能", "达成率", "大盘", "产线" },
            new[] { "情况", "怎么样", "如何", "多少" }),

        (AssistantIntent.Abnormal,
            new[] { "异常单", "异常记录", "异常", "闭环", "未闭环", "异常登记", "异常管理", "异常总数", "异常单号" },
            new[] { "多少", "几条" }),

        (AssistantIntent.Hold,
            new[] { "保留", "保留单", "隔离", "解保", "解除保留", "封存", "待检区", "保留品" },
            new[] { "多少", "几只", "几个" }),

        (AssistantIntent.Rework,
            new[] { "返工", "返修", "重检", "放行", "报废", "返工单" },
            new[] { "多少", "还剩" }),

        (AssistantIntent.Complaint,
            new[] { "客诉", "客户投诉", "投诉", "8d", "8D报告", "客诉单", "客户抱怨", "客户反馈", "售后" },
            new[] { "怎么样", "进度", "多少" }),

        (AssistantIntent.Personnel,
            new[] { "资质", "上岗", "上岗证", "证书", "持证", "资格", "人员", "员工", "工号", "排班", "班组", "谁有", "谁的证", "在职", "在岗", "能不能干", "能不能开", "能不能操作" },
            new[] { "谁", "哪个" }),

        (AssistantIntent.Inventory,
            new[] { "库存", "存货", "库房", "仓库", "仓储", "物料", "料的", "效期", "过期", "临期", "批次", "库位", "账实", "盘点", "库龄", "呆滞", "fifo", "先进先出", "料号" },
            new[] { "多少", "还有" }),

        (AssistantIntent.Equipment,
            new[] { "设备", "机台", "点检", "保养", "备件", "oee", "稼动", "停机", "维修", "故障", "仪表" },
            new[] { "状态", "怎么样" }),

        (AssistantIntent.Spc,
            new[] { "工艺参数", "参数", "spc", "控制图", "cpk", "失控", "越限", "判异", "过程能力", "工序能力", "规格限", "控制限" },
            new[] { "有没有", "是否" }),

        (AssistantIntent.Trace,
            new[] { "追溯", "溯源", "全链", "反查", "来源", "去向", "用在哪", "上游", "下游", "哪批料", "哪些电芯", "流向" },
            new[] { "这只", "这个" }),

        (AssistantIntent.Guide,
            new[] { "怎么操作", "如何操作", "在哪操作", "操作路径", "步骤", "怎么做", "怎么弄", "去哪", "在哪里", "在哪个页面" },
            new[] { "我要" }),
    };

    // 判定为「在问操作导航」的强信号
    private static readonly string[] GuideSignals =
        { "怎么操作", "如何操作", "在哪操作", "操作路径", "去哪", "在哪里", "在哪个页面", "怎么弄", "我要做", "我想做", "怎么登记", "怎么导出", "怎么打印" };

    // ================= 入口 =================

    /// <inheritdoc />
    public AssistantReply Ask(string question)
    {
        var q = (question ?? "").Trim();
        if (q.Length == 0) return AssistantReply.Empty("", Name);

        var norm = AssistantEntityParser.Normalize(q);
        var ent = AssistantEntityParser.Parse(q);

        // ① 操作导航：用户问"这东西在哪做"时，优先交给手册的模糊检索（已覆盖 25 个页面 / 58 个按钮）
        if (LooksLikeGuide(norm))
        {
            var guide = TryGuide(q, norm, ent);
            if (guide is not null) return guide;
        }

        // ② 带明确编号：直接按编号查实体（提问里给了 ID，就是要看这个东西）
        if (ent.Any)
        {
            var byEntity = TryEntity(q, ent);
            if (byEntity is not null) return byEntity;
        }

        // ③ 只给了工号（没有别的关键词）：直接按人查，别让门面走兜底
        if (ent.EmpNo is { Length: > 0 })
            return AnswerPersonnel(q, norm, ent, 60, new List<string> { ent.EmpNo });

        // ④ 关键词判意图，按意图取实时数据
        var (intent, confidence, hits) = Classify(norm);
        return intent switch
        {
            AssistantIntent.Overview => AnswerOverview(q, confidence, hits),
            AssistantIntent.Alert => AnswerAlert(q, confidence, hits),
            AssistantIntent.Abnormal => AnswerAbnormal(q, confidence, hits),
            AssistantIntent.Hold => AnswerHold(q, confidence, hits),
            AssistantIntent.Rework => AnswerRework(q, confidence, hits),
            AssistantIntent.Complaint => AnswerComplaint(q, confidence, hits),
            AssistantIntent.Personnel => AnswerPersonnel(q, norm, ent, confidence, hits),
            AssistantIntent.Inventory => AnswerInventory(q, confidence, hits),
            AssistantIntent.Equipment => AnswerEquipment(q, ent, confidence, hits),
            AssistantIntent.Spc => AnswerSpc(q, confidence, hits),
            AssistantIntent.Trace => AnswerTraceNeedTarget(q, confidence, hits),
            _ => AnswerFallback(q, hits)
        };
    }

    // ================= ① 导航 =================

    private static bool LooksLikeGuide(string norm) => GuideSignals.Any(g => norm.Contains(g, StringComparison.Ordinal));

    /// <summary>操作导航：把问题交给手册检索，取最强命中作为答复。命中太弱（&lt;110）则放弃交回主流程。</summary>
    private static AssistantReply? TryGuide(string q, string norm, AssistantEntities ent)
    {
        var hits = ManualSearch.Search(q, 5);
        if (hits.Count == 0) return null;

        var top = hits[0];
        // 手册打分口径：≥150 操作名命中 / ≥110 别名命中。低于此说明只是泛泛沾边，不足以直接当导航答案。
        if (top.Score < 110) return null;

        var e = top.Entry;
        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Guide,
            Confidence = Math.Clamp(top.Score / 2, 40, 95),
            Headline = $"这件事在「{e.TargetPage}」做，"
                       + (string.IsNullOrWhiteSpace(e.TargetButton) ? "进入页面后按下面的路径操作。" : $"点「{e.TargetButton}」按钮。"),
            BackendName = BackendLabel,
            Facts =
            {
                new AssistantFact { Label = "操作", Value = e.Title, Level = FactLevel.Info },
                new AssistantFact { Label = "所在页面", Value = e.TargetPage, Note = e.WhereText, Level = FactLevel.Info },
                new AssistantFact { Label = "要点的按钮", Value = e.ButtonText, Level = FactLevel.Info }
            },
            Evidence =
            {
                $"数据来源：使用手册条目 {e.Id}（共 {ManualCatalog.All.Count} 条，覆盖 25 个页面）",
                $"匹配方式：{top.Reason}（相关度 {top.Score}）"
            }
        };

        if (!string.IsNullOrWhiteSpace(e.Summary)) reply.Body.Add(e.Summary);
        foreach (var s in e.Steps) reply.Body.Add(s);
        if (!string.IsNullOrWhiteSpace(e.Tip)) reply.Body.Add("提示：" + e.Tip);

        reply.Actions.Add(new AssistantAction
        {
            Label = $"去「{e.TargetPage}」",
            TargetPage = e.TargetPage,
            TargetSubPage = e.TargetSubPage,
            TargetButton = e.TargetButton,
            Hint = e.Summary
        });
        reply.Actions.Add(new AssistantAction { Label = "打开使用手册", TargetPage = "使用手册", Hint = "想换一种问法或看更多操作" });

        // "还可以看看"：把手册里的相关操作一并列出，省得再问一轮
        foreach (var id in e.SeeAlso.Take(2))
        {
            var rel = ManualSearch.ById(id);
            if (rel is null) continue;
            reply.Actions.Add(new AssistantAction
            {
                Label = "相关：" + rel.Title,
                TargetPage = rel.TargetPage,
                TargetSubPage = rel.TargetSubPage,
                TargetButton = rel.TargetButton,
                Hint = rel.Summary
            });
        }
        return reply;
    }

    // ================= ② 按实体查 =================

    private static AssistantReply? TryEntity(string q, AssistantEntities ent)
    {
        var backend = BackendLabel;

        // 电芯 SN
        if (ent.CellSn is { Length: > 0 } cellSn)
        {
            var trace = MesDatabase.GetTrace(cellSn);
            if (trace?.Cell is null)
                return NotFound(q, "电芯 SN", cellSn, "SN 追溯", "trace.sn", backend,
                    "请核对 SN（形如 SC-A260812-L1-00001）；也可在「SN 追溯」页直接输入。");

            var c = trace.Cell;
            var passed = trace.Tests.Count(t => t.Passed);
            var failed = trace.Tests.Count - passed;

            var reply = new AssistantReply
            {
                Question = q,
                Intent = AssistantIntent.Trace,
                Confidence = 95,
                BackendName = backend,
                Headline = $"{c.Sn}：状态「{c.Status}」，型号 {c.ProductModel}，工单 {c.WoNo}，"
                           + $"已过 {trace.Processes.Count} 道工序、{trace.Tests.Count} 条测试（合格 {passed} / 不合格 {failed}）。",
                Facts =
                {
                    new AssistantFact { Label = "电芯状态", Value = c.Status, Note = CellSnStatus.Describe(c.Status), Level = CellSnStatus.IsShippable(c.Status) ? FactLevel.Normal : FactLevel.Watch },
                    new AssistantFact { Label = "产品型号", Value = c.ProductModel },
                    new AssistantFact { Label = "所属工单", Value = c.WoNo },
                    new AssistantFact { Label = "产线", Value = c.Line },
                    new AssistantFact { Label = "分档结果", Value = string.IsNullOrWhiteSpace(c.Result) ? "—" : c.Result },
                    new AssistantFact { Label = "极片批号", Value = string.IsNullOrWhiteSpace(c.ElectrodeLot) ? "—" : c.ElectrodeLot },
                    new AssistantFact { Label = "电解液批号", Value = string.IsNullOrWhiteSpace(c.ElectrolyteLot) ? "—" : c.ElectrolyteLot },
                    new AssistantFact { Label = "所属模组", Value = string.IsNullOrWhiteSpace(c.ModuleSn) ? "未组模组" : c.ModuleSn },
                    new AssistantFact { Label = "工序实绩", Value = $"{trace.Processes.Count} 条" },
                    new AssistantFact { Label = "测试记录", Value = $"{trace.Tests.Count} 条（不合格 {failed} 条）", Level = failed > 0 ? FactLevel.Warn : FactLevel.Normal }
                },
                Evidence =
                {
                    "数据来源：MesDatabase.GetTrace(sn)（工序实绩 + 材料批次 + 测试记录 + 环境记录）",
                    $"工序实绩来自 MesDatabase.ProcessRecords，测试来自 MesDatabase.TestRecords"
                }
            };

            foreach (var p in trace.Processes.OrderBy(p => p.Time).Take(8))
                reply.Body.Add($"工序：{p.ProcessName} / {p.Equipment} / {p.Operator} / {p.ParamName} {p.ParamValue}（{p.Time:MM-dd HH:mm}）");
            foreach (var t in trace.Tests.Take(3))
                reply.Body.Add($"测试：容量 {t.CapacityF:0.##} F、ESR {t.EsrMohm:0.##} mΩ、漏电流 {t.LeakageUa:0.##} μA、自放电 {t.SelfDischarge:0.##}% → {(t.Passed ? "合格" : "不合格")}（{t.TestTime:MM-dd HH:mm}）");

            // 已保留 / 已出货 —— 这两个是追溯里最常被追问的下游问题
            var hold = MesDatabase.QueryHolds(null, HoldStatus.Active, null, null, null)
                .FirstOrDefault(h => MesDatabase.GetHoldItems(h.HoldNo).Any(i => string.Equals(i.CellSn, cellSn, StringComparison.OrdinalIgnoreCase)));
            if (hold is not null)
            {
                reply.Facts.Add(new AssistantFact { Label = "当前保留", Value = hold.HoldNo, Note = $"{hold.HoldLocation}：{hold.Reason}", Level = FactLevel.Warn });
                reply.Actions.Add(Act("q.holdRelease", "去解除保留")!);
            }
            else
            {
                var ship = MesDatabase.GetActiveShipmentOrderByCell().TryGetValue(c.Sn, out var orderNo) ? orderNo : null;
                if (ship is not null)
                {
                    reply.Facts.Add(new AssistantFact { Label = "已绑定出货单", Value = ship, Level = FactLevel.Warn });
                    reply.Actions.Add(Act("ship.recall", "查这只电芯发给了谁")!);
                }
            }

            reply.Actions.Add(Act("trace.sn", "打开 SN 追溯")!);
            reply.Actions.Add(Act("trace.export", "导出追溯链")!);
            return reply;
        }

        // 模组 SN
        if (ent.ModuleSn is { Length: > 0 } moduleSn)
        {
            var m = MesDatabase.FindModule(moduleSn);
            if (m is null)
                return NotFound(q, "模组 SN", moduleSn, "模组装配", "prod.module", backend,
                    "请核对模组 SN（形如 SM-A260813-L1-00021）。");

            var cells = MesDatabase.GetModuleCells(moduleSn);
            return new AssistantReply
            {
                Question = q,
                Intent = AssistantIntent.Trace,
                Confidence = 93,
                BackendName = backend,
                Headline = $"{m.ModuleSn}：状态「{m.Status}」，{m.CellModel} × {m.CellCount} 只，工单 {m.WoNo}，装配于 {m.AssembledTime:yyyy-MM-dd HH:mm}。",
                Facts =
                {
                    new AssistantFact { Label = "模组状态", Value = m.Status },
                    new AssistantFact { Label = "模组型号", Value = m.ModuleModel },
                    new AssistantFact { Label = "电芯数", Value = $"{m.CellCount} 只" },
                    new AssistantFact { Label = "所属工单", Value = m.WoNo },
                    new AssistantFact { Label = "装配人", Value = string.IsNullOrWhiteSpace(m.Operator) ? "—" : m.Operator }
                },
                Evidence =
                {
                    "数据来源：MesDatabase.FindModule / MesDatabase.GetModuleCells"
                },
                Body = cells.Take(10).Select(x => $"电芯：{x.CellSn}（{x.ProductModel} / {x.CellStatus} / 分档 {x.Result}）").ToList(),
                Actions = ActionsOf(("prod.module", "打开模组装配"), ("trace.sn", "打开 SN 追溯"))
            };
        }

        // 工单号
        if (ent.WoNo is { Length: > 0 } woNo)
        {
            var wo = MesDatabase.WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, woNo, StringComparison.OrdinalIgnoreCase));
            if (wo is null)
                return NotFound(q, "工单", woNo, "工单管理", "wo.edit", backend,
                    "请核对工单号（形如 WO-20260812-001），或在「工单管理」页直接查找。");

            var cells = MesDatabase.Cells.Where(c => string.Equals(c.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase)).ToList();
            var ng = cells.Count(c => c.Status == CellSnStatus.Ng);
            var remain = wo.PlanQty - wo.FinishedQty - wo.NgQty;

            return new AssistantReply
            {
                Question = q,
                Intent = AssistantIntent.Trace,
                Confidence = 90,
                BackendName = backend,
                Headline = $"{wo.WoNo}：状态「{wo.Status}」，计划 {wo.PlanQty} 只 / 已完成 {wo.FinishedQty} 只 / 不良 {wo.NgQty} 只，未完成 {remain} 只，交期 {wo.DueDate:MM-dd}。",
                Facts =
                {
                    new AssistantFact { Label = "工单状态", Value = wo.Status, Level = wo.Status == "生产中" ? FactLevel.Normal : FactLevel.Info },
                    new AssistantFact { Label = "产品型号", Value = wo.ProductModel },
                    new AssistantFact { Label = "计划/完成/不良", Value = $"{wo.PlanQty} / {wo.FinishedQty} / {wo.NgQty}" },
                    new AssistantFact { Label = "未完成", Value = $"{remain} 只", Level = remain > 0 ? FactLevel.Watch : FactLevel.Normal },
                    new AssistantFact { Label = "ERP 报工", Value = wo.ErpReportStatus, Note = wo.ErpReportTime is null ? "无" : $"{wo.ErpReportTime:MM-dd HH:mm}" },
                    new AssistantFact { Label = "已建电芯", Value = $"{cells.Count} 只（不良 {ng} 只）", Level = ng > 0 ? FactLevel.Warn : FactLevel.Normal }
                },
                Evidence =
                {
                    "数据来源：MesDatabase.WorkOrders + MesDatabase.Cells（按工单号过滤）"
                },
                Actions = ActionsOf(("wo.edit", "打开工单管理"), ("wo.erpReport", "ERP 报工"), ("prod.wip", "看 WIP 分布"))
            };
        }

        // 保留单
        if (ent.HoldNo is { Length: > 0 } holdNo)
        {
            var h = MesDatabase.QueryHolds(holdNo, null, null, null, null).FirstOrDefault();
            if (h is null)
                return NotFound(q, "保留单", holdNo, "保留管理", "q.hold", backend,
                    "请核对保留单号（形如 HLD-20260813-001）。");

            var items = MesDatabase.GetHoldItems(h.HoldNo);
            return new AssistantReply
            {
                Question = q,
                Intent = AssistantIntent.Hold,
                Confidence = 92,
                BackendName = backend,
                Headline = $"{h.HoldNo}：状态「{h.Status}」，隔离 {items.Count} 只电芯于「{h.HoldLocation}」，原因：{h.Reason}。",
                Facts =
                {
                    new AssistantFact { Label = "保留状态", Value = h.Status, Level = h.Status == HoldStatus.Active ? FactLevel.Warn : FactLevel.Normal },
                    new AssistantFact { Label = "隔离位置", Value = h.HoldLocation },
                    new AssistantFact { Label = "涉及电芯", Value = $"{items.Count} 只" },
                    new AssistantFact { Label = "物料", Value = $"{h.MaterialName}（{h.MaterialCode}）" },
                    new AssistantFact { Label = "批次", Value = string.IsNullOrWhiteSpace(h.LotNo) ? "—" : h.LotNo },
                    new AssistantFact { Label = "保留人/时间", Value = $"{h.Operator} / {h.HoldTime:MM-dd HH:mm}" },
                    new AssistantFact { Label = "来源", Value = string.IsNullOrWhiteSpace(h.SourceRef) ? h.SourceType : $"{h.SourceType}（{h.SourceRef}）" }
                },
                Evidence =
                {
                    "数据来源：MesDatabase.QueryHolds + MesDatabase.GetHoldItems"
                },
                Body = items.Take(10).Select(i => $"电芯：{i.CellSn}").ToList(),
                Actions = ActionsOf(("q.hold", "打开保留管理"), ("q.rework", "发起返工"), ("q.holdRelease", "解除保留"))
            };
        }

        // 通用单号（异常 / 客诉 / 返工 / 首件 / SPC）
        if (ent.DocNo is { Length: > 0 } docNo)
        {
            var ab = MesDatabase.FindAbnormal(docNo);
            if (ab is not null)
                return DocAbnormal(q, ab.AbNo, backend);

            var cmp = MesDatabase.FindComplaint(docNo);
            if (cmp is not null)
                return DocComplaint(q, cmp.ComplaintNo, backend);

            var rw = MesDatabase.FindRework(docNo);
            if (rw is not null)
                return DocRework(q, rw.ReworkNo, backend);

            var fai = MesDatabase.FindFai(docNo);
            if (fai is not null)
                return DocFai(q, fai.FaiNo, backend);

            var spc = MesDatabase.FindSpcAlert(docNo);
            if (spc is not null)
                return DocSpc(q, spc.AlertNo, backend);

            return NotFound(q, "单号", docNo, "预警中心", "ov.alert", backend,
                "这个单号在异常单 / 客诉单 / 返工单 / 首件单 / SPC 预警里都没找到，请核对。");
        }

        return null;
    }

    private static AssistantReply NotFound(string q, string kind, string value, string page, string manualId, string backend, string hint)
    {
        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Trace,
            Confidence = 60,
            BackendName = backend,
            IsFallback = true,
            Headline = $"没查到{kind}「{value}」。",
            Facts =
            {
                new AssistantFact { Label = "未查到", Value = value, Level = FactLevel.Warn },
                new AssistantFact { Label = "说明", Value = hint }
            },
            Evidence = { "数据来源：按编号在对应仓储集合中精确匹配，未命中任何记录" }
        };
        var act = Act(manualId, $"去「{page}」找找");
        if (act is not null) reply.Actions.Add(act);
        return reply;
    }

    // ================= ③ 各意图回答 =================

    private static AssistantReply AnswerOverview(string q, int confidence, List<string> hits)
    {
        var (todayOutput, wip, yield, runningEq) = MesDatabase.GetDashboard();
        var rc = MesDatabase.GetReportCenterSummary();
        var alert = MesDatabase.GetAlertCenterSummary();
        var rework = MesDatabase.GetReworkSummary();
        var holds = MesDatabase.Holds.Count(h => h.Status == HoldStatus.Active);
        var eqTotal = MesDatabase.Equipments.Count;
        var backlog = rework.TryGetValue(ReworkStatus.Pending, out var rp) ? rp : 0;
        var inProg = rework.TryGetValue(ReworkStatus.InProgress, out var ri) ? ri : 0;
        var waitInsp = rework.TryGetValue(ReworkStatus.WaitInspect, out var rw) ? rw : 0;

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Overview,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = $"今日产量 {rc.TodayOutput} 只，在制 {wip} 只，良率 {yield:0.##}%；"
                       + $"{runningEq}/{eqTotal} 台设备在运行；"
                       + $"待处理预警 {alert.Total} 条（高危 {alert.High} 条）、未闭环异常 {rc.OpenAbnormal} 条。",
            Facts =
            {
                new AssistantFact
                {
                    Label = "今日产量", Value = $"{rc.TodayOutput} 只",
                    Note = rc.OutputWow is null ? "无昨日数据可对比" : $"环比 {(rc.OutputWow >= 0 ? "+" : "")}{rc.OutputWow:0.#}%",
                    Level = rc.OutputWow is not null && rc.OutputWow < 0 ? FactLevel.Watch : FactLevel.Normal
                },
                new AssistantFact { Label = "在制 WIP", Value = $"{wip} 只", Level = wip > 0 ? FactLevel.Normal : FactLevel.Info },
                new AssistantFact { Label = "良率", Value = $"{yield:0.##}%", Note = "全量电性能测试口径（合格数 / 测试总数）", Level = yield >= 98 ? FactLevel.Normal : FactLevel.Watch },
                new AssistantFact { Label = "运行设备", Value = $"{runningEq} / {eqTotal} 台", Level = runningEq == eqTotal ? FactLevel.Normal : FactLevel.Watch },
                new AssistantFact { Label = "平均 OEE", Value = $"{rc.AvgOee:0.#}%", Note = "目标 85%", Level = rc.AvgOee >= 85 ? FactLevel.Normal : FactLevel.Watch },
                new AssistantFact { Label = "本月产量", Value = $"{rc.MonthOutput} 只" },
                new AssistantFact { Label = "未闭环异常", Value = $"{rc.OpenAbnormal} 条", Level = rc.OpenAbnormal > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "待处理预警", Value = $"{alert.Total} 条", Note = $"高 {alert.High} / 中 {alert.Medium} / 低 {alert.Low}", Level = alert.High > 0 ? FactLevel.Warn : alert.Total > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "保留中", Value = $"{holds} 单", Level = holds > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "返工在制", Value = $"{backlog + inProg + waitInsp} 单", Note = $"待返工 {backlog} / 返工中 {inProg} / 待重检 {waitInsp}", Level = backlog > 0 ? FactLevel.Watch : FactLevel.Normal }
            },
            Evidence =
            {
                "数据来源：MesDatabase.GetDashboard()（工单 + 设备 + 测试记录）",
                "数据来源：MesDatabase.GetReportCenterSummary()（设备日产量 + 测试记录 + OEE）",
                "数据来源：MesDatabase.GetAlertCenterSummary()（聚合 10 类待处理事项）",
                "数据来源：MesDatabase.GetReworkSummary() / MesDatabase.Holds"
            }
        };

        reply.Body.Add("口径说明：良率取全部电性能测试记录（不是抽检）；在制 WIP 按「生产中」工单的计划量 − 完成量 − 不良量估算。");
        if (alert.High > 0) reply.Body.Add($"有 {alert.High} 条高危预警，建议先处理高危项，再处理中低危。");
        else if (alert.Total > 0) reply.Body.Add($"暂无高危预警，有 {alert.Total} 条中低危待处理。");
        else reply.Body.Add("当前没有待处理预警。");

        reply.Actions.AddRange(ActionsOf(
            ("ov.dashboard", "看生产看板"),
            ("ov.alert", "看预警中心"),
            ("rep.daily", "看产量报表"),
            ("rep.oee", "看 OEE 与库存")));
        return reply;
    }

    private static AssistantReply AnswerAlert(string q, int confidence, List<string> hits)
    {
        var all = MesDatabase.GetCenterAlerts();
        var (high, medium, low, total) = MesDatabase.GetAlertCenterSummary();
        var bySource = MesDatabase.GetAlertBySource();

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Alert,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = total == 0
                ? "当前没有任何待处理事项。"
                : $"当前共 {total} 条待处理：高危 {high} 条、中危 {medium} 条、低危 {low} 条；"
                  + $"最集中的是「{bySource.FirstOrDefault().Source}」（{bySource.FirstOrDefault().Count} 条）。",
            Evidence =
            {
                "数据来源：MesDatabase.GetCenterAlerts()（聚合 SPC 预警 / 异常未闭环 / 保留超期 / 点检保养 / 库存效期 / 上料被拒 / 首件未放行 / 返工积压 / 备件低库存 / 出货未绑定 共 10 类）",
                "数据来源：MesDatabase.GetAlertCenterSummary() / MesDatabase.GetAlertBySource()"
            }
        };

        foreach (var (src, count, highCount) in bySource.Take(6))
        {
            reply.Facts.Add(new AssistantFact
            {
                Label = src,
                Value = $"{count} 条",
                Note = highCount > 0 ? $"其中高危 {highCount}" : "无高危",
                Level = highCount > 0 ? FactLevel.Warn : FactLevel.Watch
            });
        }

        if (total > 0)
        {
            reply.Body.Add("按严重度排序的前几条：");
            foreach (var a in all.Take(6))
                reply.Body.Add($"· [{a.Severity}] {a.Source}：{a.Title}"
                               + (string.IsNullOrWhiteSpace(a.Detail) ? "" : $" —— {a.Detail}")
                               + (a.OccurTime == default ? "" : $"（{a.OccurTime:MM-dd HH:mm}）"));
        }

        reply.Actions.AddRange(ActionsOf(
            ("ov.alert", "打开预警中心"),
            ("ov.alertFilter", "按来源/严重度筛选"),
            ("ov.alertJump", "从预警跳到业务页面")));
        return reply;
    }

    private static AssistantReply AnswerAbnormal(string q, int confidence, List<string> hits)
    {
        var all = MesDatabase.QueryAbnormals(null, null, null, null, null, null);
        var pending = all.Count(a => a.Status == AbnormalStatus.Pending);
        var confirmed = all.Count(a => a.Status == AbnormalStatus.Confirmed);
        var closed = all.Count(a => a.Status == AbnormalStatus.Closed);
        var open = all.Where(a => a.Status != AbnormalStatus.Closed).OrderByDescending(a => a.OccurTime).ToList();

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Abnormal,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = $"共 {all.Count} 条异常单：待确认 {pending} 条、已确认 {confirmed} 条、已闭环 {closed} 条；未闭环合计 {pending + confirmed} 条。",
            Facts =
            {
                new AssistantFact { Label = "待确认", Value = $"{pending} 条", Level = pending > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "已确认（待分析）", Value = $"{confirmed} 条", Level = confirmed > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "已闭环", Value = $"{closed} 条" },
                new AssistantFact { Label = "未闭环合计", Value = $"{pending + confirmed} 条", Level = pending + confirmed > 0 ? FactLevel.Watch : FactLevel.Normal }
            },
            Evidence =
            {
                "数据来源：MesDatabase.QueryAbnormals(null, null, null, null, null, null)（全量异常单）"
            }
        };

        if (open.Count > 0)
        {
            reply.Body.Add("未闭环的异常单：");
            foreach (var a in open.Take(6))
                reply.Body.Add($"· {a.AbNo} [{a.Status}] {a.Category}/{a.SubType}｜{a.EquipmentName}｜{a.Description}（{a.OccurTime:MM-dd HH:mm} 发现人 {a.FoundBy}）");
        }
        else reply.Body.Add("所有异常单都已闭环。");

        reply.Actions.AddRange(ActionsOf(
            ("q.abnormalNew", "登记新异常"),
            ("q.abnormalClose", "闭环已确认的异常"),
            ("q.abnormalPrint", "打印异常单"),
            ("ov.alert", "看预警中心")));
        return reply;
    }

    private static AssistantReply AnswerHold(string q, int confidence, List<string> hits)
    {
        var holds = MesDatabase.QueryHolds(null, null, null, null, null);
        var active = holds.Where(h => h.Status == HoldStatus.Active).OrderByDescending(h => h.HoldTime).ToList();
        var released = holds.Count - active.Count;
        var cellCount = active.Sum(h => MesDatabase.GetHoldItems(h.HoldNo).Count);

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Hold,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = active.Count == 0
                ? "当前没有处于「保留中」的隔离单。"
                : $"当前 {active.Count} 张保留单处于隔离中，共锁定 {cellCount} 只电芯；历史已解除 {released} 张。",
            Facts =
            {
                new AssistantFact { Label = "保留中", Value = $"{active.Count} 单", Level = active.Count > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "锁定电芯", Value = $"{cellCount} 只", Level = cellCount > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "已解除", Value = $"{released} 单" },
                new AssistantFact { Label = "保留单合计", Value = $"{holds.Count} 单" }
            },
            Evidence =
            {
                "数据来源：MesDatabase.QueryHolds(...) + MesDatabase.GetHoldItems(holdNo)（合计锁定电芯数）"
            }
        };

        if (active.Count > 0)
        {
            reply.Body.Add("隔离中的保留单：");
            foreach (var h in active.Take(6))
                reply.Body.Add($"· {h.HoldNo}｜{h.HoldLocation}｜{h.MaterialName} 批次 {h.LotNo}｜{MesDatabase.GetHoldItems(h.HoldNo).Count} 只｜{h.Reason}（{h.HoldTime:MM-dd HH:mm} {h.Operator}）");
            reply.Body.Add("隔离中的电芯不能出货、不能投入生产，必须走「返工 → 重检 → 放行」或直接解除保留。");
        }

        reply.Actions.AddRange(ActionsOf(
            ("q.hold", "打开保留管理"),
            ("q.rework", "对保留品发起返工"),
            ("q.holdRelease", "解除保留"),
            ("mat.hold", "按料号/批次批量保留")));
        return reply;
    }

    private static AssistantReply AnswerRework(string q, int confidence, List<string> hits)
    {
        var sum = MesDatabase.GetReworkSummary();
        int Get(string k) => sum.TryGetValue(k, out var v) ? v : 0;
        var pending = Get(ReworkStatus.Pending);
        var inProg = Get(ReworkStatus.InProgress);
        var waitInsp = Get(ReworkStatus.WaitInspect);
        var released = Get(ReworkStatus.Released);
        var scrapped = Get(ReworkStatus.Scrapped);
        var open = pending + inProg + waitInsp;

        var orders = MesDatabase.QueryReworkOrders(null, null);
        var openOrders = orders.Where(o => !ReworkStatus.IsFinal(o.Status)).OrderBy(o => o.CreatedTime).ToList();

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Rework,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = open == 0
                ? $"没有在途返工单；历史已放行 {released} 单、报废 {scrapped} 单。"
                : $"在途返工合计 {open} 单：待返工 {pending}、返工中 {inProg}、待重检 {waitInsp}；累计已放行 {released}、报废 {scrapped}。",
            Facts =
            {
                new AssistantFact { Label = "待返工", Value = $"{pending} 单", Level = pending > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "返工中", Value = $"{inProg} 单" },
                new AssistantFact { Label = "待重检", Value = $"{waitInsp} 单", Level = waitInsp > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "已放行", Value = $"{released} 单", Level = FactLevel.Normal },
                new AssistantFact { Label = "已报废", Value = $"{scrapped} 单", Level = scrapped > 0 ? FactLevel.Watch : FactLevel.Normal }
            },
            Evidence =
            {
                "数据来源：MesDatabase.GetReworkSummary()（按 ReworkStatus 全状态计数）",
                "数据来源：MesDatabase.QueryReworkOrders(null, null)"
            }
        };

        if (openOrders.Count > 0)
        {
            reply.Body.Add("在途返工单：");
            foreach (var o in openOrders.Take(6))
                reply.Body.Add($"· {o.ReworkNo}｜{o.CellSn}｜[{o.Status}] 原因：{o.Reason}｜责任工序 {o.ResponsibleProcess}｜返工 {o.ReworkTimes} 次"
                               + (string.IsNullOrWhiteSpace(o.InspectResult) ? "" : $"｜上次重检 {o.InspectResult}"));
        }
        else reply.Body.Add("当前没有在途返工单。");

        reply.Actions.AddRange(ActionsOf(
            ("q.rework", "打开返工管理"),
            ("q.reworkFlow", "按流转推进（开始/完成/重检）"),
            ("q.reworkPrint", "打印返工单")));
        return reply;
    }

    private static AssistantReply AnswerComplaint(string q, int confidence, List<string> hits)
    {
        var (pending, investigating, waitingCustomer, closed, overdue) = MesDatabase.GetComplaintSummary();
        var all = MesDatabase.QueryComplaints(null, null);
        var openList = all.Where(c => !ComplaintStatus.IsClosed(c.Status)).OrderBy(c => c.DueDate).ToList();

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Complaint,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = openList.Count == 0
                ? $"当前没有未关闭的客诉（历史已关闭 {closed} 条）。"
                : $"未关闭客诉 {openList.Count} 条：待受理 {pending}、处理中 {investigating}、待客户确认 {waitingCustomer}"
                  + (overdue > 0 ? $"；其中 {overdue} 条已超期，需优先处理。" : "；暂无超期。"),
            Facts =
            {
                new AssistantFact { Label = "待受理", Value = $"{pending} 条", Level = pending > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "处理中", Value = $"{investigating} 条", Level = investigating > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "待客户确认", Value = $"{waitingCustomer} 条" },
                new AssistantFact { Label = "已关闭", Value = $"{closed} 条", Level = FactLevel.Normal },
                new AssistantFact { Label = "超期", Value = $"{overdue} 条", Level = overdue > 0 ? FactLevel.Warn : FactLevel.Normal }
            },
            Evidence =
            {
                "数据来源：MesDatabase.GetComplaintSummary()（按客诉状态机 + 处理时限判定超期）",
                "数据来源：MesDatabase.QueryComplaints(null, null)"
            }
        };

        if (openList.Count > 0)
        {
            reply.Body.Add("未关闭客诉：");
            foreach (var c in openList.Take(6))
                reply.Body.Add($"· {c.ComplaintNo}｜{c.CustomerName}｜[{c.Status}] {c.Category}/{c.Severity}｜{c.SymptomDesc}"
                               + $"｜8D 当前 {c.CurrentStep}｜应付 {c.DueDate:MM-dd}"
                               + (c.IsSafetyInvolved ? "｜⚠涉及安全" : "")
                               + (c.NeedRecall ? "｜⚠需召回" : ""));
            reply.Body.Add("客诉推进顺序：受理 → 影响范围反查（锁定涉及批次/电芯）→ 8D 逐级 → 客户确认 → 关闭。安全相关或需召回的客诉时限最短（严重 3 天）。");
        }

        reply.Actions.AddRange(ActionsOf(
            ("c.create", "登记新客诉"),
            ("c.8d", "填写 8D 报告"),
            ("c.impact", "反查影响范围")));
        return reply;
    }

    private static AssistantReply AnswerPersonnel(string q, string norm, AssistantEntities ent, int confidence, List<string> hits)
    {
        var (onDuty, certified, expiring, invalid) = MesDatabase.GetPersonnelSummary();
        var allCerts = MesDatabase.QueryCertificates(null, null);

        // 问句里点到了具体的人 / 具体工序 → 走"能不能上岗"的资质校验
        var user = ResolveUser(q, norm, ent);
        var process = ResolveProcess(norm);

        if (user is not null)
            return AnswerQualification(q, user, process, confidence, norm);

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Personnel,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = $"在职 {onDuty} 人，其中 {certified} 人持有可上岗位资质；"
                       + $"{expiring} 张证书即将到期、{invalid} 张已失效/被吊销。",
            Facts =
            {
                new AssistantFact { Label = "在职人数", Value = $"{onDuty} 人" },
                new AssistantFact { Label = "持证可上岗", Value = $"{certified} 人", Level = certified < onDuty ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "即将到期", Value = $"{expiring} 张", Level = expiring > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "失效/吊销", Value = $"{invalid} 张", Level = invalid > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "资质项", Value = $"{SkillCatalog.All.Length} 类", Note = "工序类 6 + 职能类 4 + 特种作业类 2" }
            },
            Evidence =
            {
                "数据来源：MesDatabase.GetPersonnelSummary()（人员主数据 + 资质证书状态）",
                "数据来源：MesDatabase.QueryCertificates(null, null)（证书清单）"
            }
        };

        var expiringList = allCerts
            .Where(c => c.Status == CertStatus.ExpiringSoon || c.Status == CertStatus.Expired || c.Revoked)
            .Take(6)
            .ToList();
        if (expiringList.Count > 0)
        {
            reply.Body.Add("需要关注的证书：");
            foreach (var c in expiringList)
                reply.Body.Add($"· {c.EmpName}（{c.EmpNo}）｜{c.SkillItem}｜{c.Status}｜{(c.ExpiryDate is null ? "长期有效" : $"到期 {c.ExpiryDate:yyyy-MM-dd}")}");
        }
        reply.Body.Add("问「某人能不能做某工序」时，我会直接给资质校验结论（如「张伟能不能开卷绕机」）。");

        reply.Actions.AddRange(ActionsOf(
            ("hr.matrix", "看资质矩阵"),
            ("hr.check", "做上岗资质核查"),
            ("hr.addCert", "新增/续期证书"),
            ("hr.crew", "看班组绩效")));
        return reply;
    }

    private static AssistantReply AnswerQualification(string q, UserProfile user, string? process, int confidence, string norm)
    {
        var skill = SkillCatalog.SkillOfProcess(process);
        var (ok, reason, needSkill, certStatus) = MesDatabase.CheckQualification(user.EmpNo, process);
        var certs = MesDatabase.GetCertificates(user.EmpNo);

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Personnel,
            Confidence = Math.Max(confidence, 88),   // 点名到人 + 工序，匹配强度高于泛问
            BackendName = BackendLabel,
            Headline = process is null
                ? $"{user.Name}（{user.EmpNo}）｜{user.Dept} {user.Position}｜状态「{user.Status}」｜持有 {certs.Count} 张证书（有效 {certs.Count(c => c.CanWork)} 张）。"
                : ok
                    ? $"可以。{reason}"
                    : $"不可以。{reason}",
            Facts =
            {
                new AssistantFact { Label = "人员", Value = $"{user.Name}（{user.EmpNo}）" },
                new AssistantFact { Label = "部门/岗位", Value = $"{user.Dept} / {user.Position}" },
                new AssistantFact { Label = "在职状态", Value = user.Status, Level = user.IsActive ? FactLevel.Normal : FactLevel.Warn },
                new AssistantFact { Label = "班组", Value = user.Crew }
            },
            Evidence =
            {
                "数据来源：MesDatabase.FindUser → MesDatabase.CheckQualification(empNo, process)",
                "判定规则优先级：工序无持证要求 → 人员不存在 → 已离职 → 无证 → 证书失效 → 通过"
            }
        };

        if (process is not null)
        {
            reply.Facts.Add(new AssistantFact { Label = "询问工序", Value = process });
            reply.Facts.Add(new AssistantFact { Label = "所需资质", Value = needSkill.Length == 0 ? "无（该工序不设持证要求）" : needSkill });
            reply.Facts.Add(new AssistantFact
            {
                Label = "证书状态",
                Value = certStatus.Length == 0 ? "—" : certStatus,
                Level = ok ? FactLevel.Normal : FactLevel.Warn
            });
            reply.Body.Add(ok ? "该员工当前具备本工序上岗资格，可按正常流程上岗。" : "系统按规矩拦住：无资质/证书失效一律不允许上岗，需先补证或换人。");
        }
        else
        {
            reply.Body.Add("没有识别到具体工序，下面是这个人的全部证书：");
            foreach (var c in certs.Take(8))
                reply.Body.Add($"· {c.SkillItem}｜{c.Level}｜{c.Status}｜{(c.ExpiryDate is null ? "长期有效" : $"到期 {c.ExpiryDate:yyyy-MM-dd}")}");
            reply.Body.Add("带上工序再问一次（如「{user.Name}能不能开卷绕机」），我可以直接给能不能上岗的结论。".Replace("{user.Name}", user.Name));
        }

        reply.Actions.AddRange(ActionsOf(
            ("hr.check", "打开上岗资质核查"),
            ("hr.matrix", "看资质矩阵"),
            ("hr.addCert", "补证 / 续期")));
        return reply;
    }

    private static AssistantReply AnswerInventory(string q, int confidence, List<string> hits)
    {
        var total = MesDatabase.GetWmsStockTotal();
        var (expired, nearExpiry, noExpiry) = MesDatabase.GetExpirySummary();
        var cmp = MesDatabase.GetWmsStockCompareSummary();
        var fifo = MesDatabase.GetFifoRecommendations();
        var stocks = MesDatabase.QueryWmsStock(null);

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Inventory,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = $"库存在账 {stocks.Count} 条（合计 {total:0.##}）；"
                       + $"已过期 {expired} 条、{30} 天内临期 {nearExpiry} 条；"
                       + $"账实对比：一致 {cmp.MatchedCount} 条、差异 {cmp.MismatchCount} 条、MES 独有 {cmp.MesOnlyCount} 条。",
            Facts =
            {
                new AssistantFact { Label = "库存记录", Value = $"{stocks.Count} 条", Note = $"合计数量 {total:0.##}" },
                new AssistantFact { Label = "已过期", Value = $"{expired} 条", Level = expired > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "30 天内临期", Value = $"{nearExpiry} 条", Level = nearExpiry > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "无有效期", Value = $"{noExpiry} 条", Level = FactLevel.Info },
                new AssistantFact { Label = "账实一致", Value = $"{cmp.MatchedCount} 条", Level = FactLevel.Normal },
                new AssistantFact { Label = "账实差异", Value = $"{cmp.MismatchCount} 条", Level = cmp.MismatchCount > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "MES 独有", Value = $"{cmp.MesOnlyCount} 条", Level = cmp.MesOnlyCount > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "ERP 独有", Value = $"{cmp.ErpOnlyCount} 条", Level = cmp.ErpOnlyCount > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "FIFO 建议", Value = $"{fifo.Count} 条", Note = "同物料多批次时建议先出最早批次" }
            },
            Evidence =
            {
                "数据来源：MesDatabase.QueryWmsStock(null) / MesDatabase.GetWmsStockTotal()",
                "数据来源：MesDatabase.GetExpirySummary(30)（按 WmsStock.ExpiryDate 计算剩余天数）",
                "数据来源：MesDatabase.GetWmsStockCompareSummary()（MES 与 ERP 账实逐键对比）",
                "数据来源：MesDatabase.GetFifoRecommendations()"
            }
        };

        var nearList = MesDatabase.GetExpiryAlerts(30).OrderBy(r => r.DaysLeft ?? int.MaxValue).Take(6).ToList();
        if (nearList.Count > 0)
        {
            reply.Body.Add("效期最紧的几批：");
            foreach (var r in nearList)
                reply.Body.Add($"· {r.MaterialName}｜批次 {r.LotNo}｜{r.Location}｜{r.Quantity:0.##}｜"
                               + (r.DaysLeft is null ? "无有效期" : r.DaysLeft < 0 ? $"已过期 {Math.Abs(r.DaysLeft.Value):0} 天" : $"还剩 {r.DaysLeft:0} 天"));
        }
        if (cmp.MismatchCount > 0 || cmp.MesOnlyCount > 0 || cmp.ErpOnlyCount > 0)
            reply.Body.Add($"账实存在差异（净差额 {cmp.NetDiffTotal:0.##}），建议核对后再发货，避免发了账上没有的料。");

        reply.Actions.AddRange(ActionsOf(
            ("wms.stock", "看库存台账"),
            ("wms.expiry", "看效期与 FIFO"),
            ("wms.order", "看仓储单据")));
        return reply;
    }

    private static AssistantReply AnswerEquipment(string q, AssistantEntities ent, int confidence, List<string> hits)
    {
        var eqs = MesDatabase.Equipments;

        // 点了具体设备编码 → 给这台设备的档案
        if (ent.EquipmentCode is { Length: > 0 } code)
        {
            var one = eqs.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));
            if (one is not null)
            {
                var alarms = MesDatabase.GetAlarms(one.Code);
                var history = MesDatabase.GetStatusHistory(one.Code);
                return new AssistantReply
                {
                    Question = q,
                    Intent = AssistantIntent.Equipment,
                    Confidence = 92,
                    BackendName = BackendLabel,
                    Headline = $"{one.Code} {one.Name}：状态「{one.Status}」，OEE {one.Oee:0.#}%，今日产量 {one.TodayOutput}，工段「{one.Section}」。",
                    Facts =
                    {
                        new AssistantFact { Label = "设备状态", Value = one.Status, Level = one.Status == "运行" ? FactLevel.Normal : FactLevel.Warn },
                        new AssistantFact { Label = "OEE", Value = $"{one.Oee:0.#}%", Note = "目标 85%", Level = one.Oee >= 85 ? FactLevel.Normal : FactLevel.Watch },
                        new AssistantFact { Label = "今日产量", Value = $"{one.TodayOutput}" },
                        new AssistantFact { Label = "所属工段", Value = one.Section },
                        new AssistantFact { Label = "最近事件", Value = string.IsNullOrWhiteSpace(one.LastEvent) ? "—" : one.LastEvent },
                        new AssistantFact { Label = "报警记录", Value = $"{alarms.Count} 条", Level = alarms.Count > 0 ? FactLevel.Watch : FactLevel.Normal },
                        new AssistantFact { Label = "状态履历", Value = $"{history.Count} 条" }
                    },
                    Evidence = { "数据来源：MesDatabase.Equipments + GetAlarms(code) + GetStatusHistory(code)" },
                    Body = alarms.Take(5).Select(a => $"报警：{a.Message}（{a.Time:MM-dd HH:mm}）").ToList(),
                    Actions = ActionsOf(("eq.monitor", "打开设备监控"), ("eq.tpm", "看该设备点检保养"))
                };
            }
        }

        var running = eqs.Count(e => e.Status == "运行");
        var stopped = eqs.Count(e => e.Status == "停机");
        var maint = eqs.Count(e => e.Status == "维护");
        var standby = eqs.Count(e => e.Status == "待机");
        var oee = MesDatabase.GetOeeSummaryReport();
        var avgOee = oee.Count == 0 ? 0 : oee.Average(r => r.TodayOee);
        var (tpmTotal, tpmOverdue, tpmDueSoon) = MesDatabase.GetTpmSummary();
        var (spareTotal, spareLow) = MesDatabase.GetSpareSummary();

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Equipment,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = $"共 {eqs.Count} 台设备：运行 {running}、待机 {standby}、维护 {maint}、停机 {stopped}；"
                       + $"平均 OEE {avgOee:0.#}%；点检保养超期 {tpmOverdue} 项、临期 {tpmDueSoon} 项。",
            Facts =
            {
                new AssistantFact { Label = "运行", Value = $"{running} 台", Level = FactLevel.Normal },
                new AssistantFact { Label = "待机", Value = $"{standby} 台", Level = standby > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "维护", Value = $"{maint} 台", Level = maint > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "停机", Value = $"{stopped} 台", Level = stopped > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "平均 OEE", Value = $"{avgOee:0.#}%", Note = "目标 85%", Level = avgOee >= 85 ? FactLevel.Normal : FactLevel.Watch },
                new AssistantFact { Label = "点检保养计划", Value = $"{tpmTotal} 项", Note = $"超期 {tpmOverdue} / 临期 {tpmDueSoon}", Level = tpmOverdue > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "备件", Value = $"{spareTotal} 项", Note = $"低于安全库存 {spareLow} 项", Level = spareLow > 0 ? FactLevel.Watch : FactLevel.Normal }
            },
            Evidence =
            {
                "数据来源：MesDatabase.Equipments（设备台账状态）",
                "数据来源：MesDatabase.GetOeeSummaryReport() / GetTpmSummary() / GetSpareSummary()"
            }
        };

        var notRunning = eqs.Where(e => e.Status != "运行").ToList();
        if (notRunning.Count > 0)
        {
            reply.Body.Add("非运行设备：");
            foreach (var e in notRunning)
                reply.Body.Add($"· {e.Code} {e.Name}｜{e.Status}｜OEE {e.Oee:0.#}%｜{e.LastEvent}");
        }
        var lowOee = oee.Where(r => r.TodayOee < 85).OrderBy(r => r.TodayOee).Take(5).ToList();
        if (lowOee.Count > 0)
        {
            reply.Body.Add("OEE 低于目标的设备：");
            foreach (var r in lowOee)
                reply.Body.Add($"· {r.DeviceCode} {r.DeviceName}｜今日 {r.TodayOee:0.#}%｜达成率 {r.AchievementText}（{r.Judge}）");
        }

        reply.Actions.AddRange(ActionsOf(
            ("eq.monitor", "打开设备监控"),
            ("eq.tpm", "看/做点检保养"),
            ("eq.spare", "看备件台账"),
            ("rep.oee", "看 OEE 报表")));
        return reply;
    }

    private static AssistantReply AnswerSpc(string q, int confidence, List<string> hits)
    {
        var alerts = MesDatabase.QuerySpcAlerts(null, null, null, null, null, null);
        var pending = alerts.Count(a => a.Status == SpcAlertStatus.Pending);
        var acked = alerts.Count(a => a.Status == SpcAlertStatus.Acked);
        var closed = alerts.Count(a => a.Status == SpcAlertStatus.Closed);
        var open = alerts.Where(a => a.Status != SpcAlertStatus.Closed)
            .OrderBy(a => a.Severity == SpcSeverity.High ? 0 : a.Severity == SpcSeverity.Medium ? 1 : 2)
            .ThenByDescending(a => a.SampleTime).ToList();

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Spc,
            Confidence = confidence,
            BackendName = BackendLabel,
            Headline = alerts.Count == 0
                ? "当前没有任何 SPC 参数预警。"
                : $"SPC 预警累计 {alerts.Count} 条：待处理 {pending}、已确认 {acked}、已关闭 {closed}；未关闭 {open.Count} 条。",
            Facts =
            {
                new AssistantFact { Label = "待处理", Value = $"{pending} 条", Level = pending > 0 ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "已确认", Value = $"{acked} 条", Level = acked > 0 ? FactLevel.Watch : FactLevel.Normal },
                new AssistantFact { Label = "已关闭", Value = $"{closed} 条", Level = FactLevel.Normal },
                new AssistantFact { Label = "未关闭合计", Value = $"{open.Count} 条", Level = open.Count > 0 ? FactLevel.Watch : FactLevel.Normal }
            },
            Evidence =
            {
                "数据来源：MesDatabase.QuerySpcAlerts(null, null, null, null, null, null)",
                "预警由 SPC 规则判定生成（判异 / 能力不足 / 超规格），判定结果不由助手改写"
            }
        };

        if (open.Count > 0)
        {
            reply.Body.Add("未关闭的 SPC 预警：");
            foreach (var a in open.Take(6))
                reply.Body.Add($"· {a.AlertNo}｜[{a.Severity}] {a.AlertType}｜{a.ItemName}｜{a.RuleName}｜"
                               + $"实测 {a.Value:0.###}（CL {a.CenterLine:0.###}，UCL {a.Ucl:0.###} / LCL {a.Lcl:0.###}）"
                               + $"｜设备 {a.Equipment}｜{a.SampleTime:MM-dd HH:mm}"
                               + (string.IsNullOrWhiteSpace(a.LinkedAbnormalNo) ? "｜未转异常" : $"｜已转异常 {a.LinkedAbnormalNo}"));
            reply.Body.Add("参数预警的处理口径：先确认（认领），再判断是否要转异常单做闭环；涉及已产出电芯的要反查影响范围。");
        }

        reply.Actions.AddRange(ActionsOf(
            ("q.spc", "打开 SPC 分析"),
            ("q.spcAlert", "看参数预警"),
            ("q.spcToAbnormal", "把预警转异常单"),
            ("q.spcConfig", "看监控项与控制限")));
        return reply;
    }

    private static AssistantReply AnswerTraceNeedTarget(string q, int confidence, List<string> hits)
    {
        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Trace,
            Confidence = confidence,
            BackendName = BackendLabel,
            IsFallback = true,
            Headline = "追溯需要先给一个具体对象：电芯 SN、模组 SN、工单号或批次号。",
            Facts =
            {
                new AssistantFact { Label = "电芯 SN", Value = "SC-A260812-L1-00001", Level = FactLevel.Info },
                new AssistantFact { Label = "模组 SN", Value = "SM-A260813-L1-00021", Level = FactLevel.Info },
                new AssistantFact { Label = "工单号", Value = "WO-20260812-001", Level = FactLevel.Info },
                new AssistantFact { Label = "批次号", Value = "PC-260812-L1-001", Level = FactLevel.Info }
            },
            Evidence = { "数据来源：从提问中未抽取到可用编号，未查询任何业务数据" },
            Body =
            {
                "可以这样问我：「SC-A260812-L1-00001 这只电芯用的是哪批料」、「WO-20260812-001 做到多少了」、「PC-260812-L1-001 这批料用在哪些电芯上」。"
            }
        };
        reply.Actions.AddRange(ActionsOf(
            ("trace.sn", "打开 SN 追溯"),
            ("mat.trace", "按料号/批次反查电芯"),
            ("ship.recall", "查这批货发给了谁")));
        return reply;
    }

    private static AssistantReply AnswerFallback(string q, List<string> hits)
    {
        var manual = ManualSearch.Search(q, 4);
        var backend = BackendLabel;

        var reply = new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Unknown,
            Confidence = 25,
            BackendName = backend,
            IsFallback = true,
            Headline = manual.Count > 0
                ? $"没听明白具体是哪个数据域，但手册里最接近的是「{manual[0].Entry.Title}」。"
                : "没匹配到已有的数据域，换一种说法或补充关键词（SN / 单号 / 工号）会更准。",
            Evidence =
            {
                "数据来源：12 类意图均未达到匹配阈值；已回退到使用手册检索",
                "可覆盖的数据域：生产概览 / 预警待办 / 异常 / 保留 / 返工 / 客诉 / 人员资质 / 库存 / 设备 / SPC / 追溯 / 操作导航"
            },
            Facts =
            {
                new AssistantFact { Label = "已识别关键词", Value = hits.Count == 0 ? "无" : string.Join("、", hits.Take(6)) }
            },
            Body =
            {
                "可以这样问我：",
                "· 数据类：「今天产量多少」「有哪些高危预警」「库存有没有快过期的」",
                "· 编号类：「SC-A260812-L1-00001 这只电芯怎么回事」「WO-20260812-001 什么状态」",
                "· 人 / 资质：「张伟能不能开卷绕机」「谁的证快到期了」",
                "· 操作类：「不合格品要去哪登记」「怎么导出追溯记录」"
            }
        };

        foreach (var h in manual.Take(3))
            reply.Actions.Add(new AssistantAction
            {
                Label = h.Entry.Title,
                TargetPage = h.Entry.TargetPage,
                TargetSubPage = h.Entry.TargetSubPage,
                TargetButton = h.Entry.TargetButton,
                Hint = h.Entry.Summary
            });

        reply.Actions.Add(new AssistantAction { Label = "打开使用手册", TargetPage = "使用手册" });
        return reply;
    }

    // ================= 单号型回答（供实体分支复用） =================

    private static AssistantReply DocAbnormal(string q, string abNo, string backend)
    {
        var a = MesDatabase.FindAbnormal(abNo)!;
        var hist = MesDatabase.GetAbnormalHistory(abNo);
        return new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Abnormal,
            Confidence = 93,
            BackendName = backend,
            Headline = $"{a.AbNo}：状态「{a.Status}」｜{a.Category}/{a.SubType}｜{a.EquipmentName}（{a.Line}）｜{a.Description}",
            Facts =
            {
                new AssistantFact { Label = "状态", Value = a.Status, Level = a.Status == AbnormalStatus.Closed ? FactLevel.Normal : FactLevel.Warn },
                new AssistantFact { Label = "发生时间", Value = $"{a.OccurTime:yyyy-MM-dd HH:mm}" },
                new AssistantFact { Label = "发现人", Value = a.FoundBy },
                new AssistantFact { Label = "责任人", Value = string.IsNullOrWhiteSpace(a.Owner) ? "—" : a.Owner },
                new AssistantFact { Label = "临时措施", Value = string.IsNullOrWhiteSpace(a.TemporaryMeasure) ? "—" : a.TemporaryMeasure },
                new AssistantFact { Label = "根本原因", Value = string.IsNullOrWhiteSpace(a.RootCause) ? "（尚未分析）" : a.RootCause },
                new AssistantFact { Label = "永久对策", Value = string.IsNullOrWhiteSpace(a.RootMeasure) ? "（尚未填写）" : a.RootMeasure },
                new AssistantFact { Label = "流转履历", Value = $"{hist.Count} 条" }
            },
            Evidence = { "数据来源：MesDatabase.FindAbnormal + MesDatabase.GetAbnormalHistory" },
            Actions = ActionsOf(("q.abnormalClose", "去闭环处理"), ("q.abnormalPrint", "打印异常单"), ("ov.alert", "看预警中心"))
        };
    }

    private static AssistantReply DocComplaint(string q, string cmpNo, string backend)
    {
        var c = MesDatabase.FindComplaint(cmpNo)!;
        var d8 = MesDatabase.GetEightD(cmpNo);
        var hist = MesDatabase.GetComplaintHistories(cmpNo);
        return new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Complaint,
            Confidence = 93,
            BackendName = backend,
            Headline = $"{c.ComplaintNo}：客户「{c.CustomerName}」｜状态「{c.Status}」｜{c.Category}/{c.Severity}｜8D 当前 {c.CurrentStep}",
            Facts =
            {
                new AssistantFact { Label = "状态", Value = c.Status, Level = ComplaintStatus.IsClosed(c.Status) ? FactLevel.Normal : FactLevel.Warn },
                new AssistantFact { Label = "严重度", Value = c.Severity, Note = $"响应时限 {ComplaintSeverity.ResponseDays(c.Severity)} 天", Level = c.Severity == ComplaintSeverity.Critical ? FactLevel.Warn : FactLevel.Info },
                new AssistantFact { Label = "受理时间", Value = $"{c.ReceiveTime:yyyy-MM-dd}" },
                new AssistantFact { Label = "应付日期", Value = $"{c.DueDate:yyyy-MM-dd}", Level = c.DueHealth == "已超期" ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "涉及数量", Value = $"{c.Quantity:0.##}" },
                new AssistantFact { Label = "症状", Value = c.SymptomDesc },
                new AssistantFact { Label = "涉及安全", Value = c.IsSafetyInvolved ? "是" : "否", Level = c.IsSafetyInvolved ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "是否召回", Value = c.NeedRecall ? "是" : "否", Level = c.NeedRecall ? FactLevel.Warn : FactLevel.Normal },
                new AssistantFact { Label = "对策人", Value = string.IsNullOrWhiteSpace(c.Owner) ? "—" : c.Owner },
                new AssistantFact { Label = "8D 报告", Value = d8 is null ? "未建立" : "已建立" }
            },
            Evidence = { "数据来源：MesDatabase.FindComplaint + GetEightD + GetComplaintHistories" },
            Body = hist.Take(6).Select(h => $"履历：{h.Time:MM-dd HH:mm} {h.Operator} {h.ActionType} {h.Step} {h.Remark}").ToList(),
            Actions = ActionsOf(("c.8d", "填写/查看 8D"), ("c.impact", "反查影响范围"), ("c.close", "推进到关闭"))
        };
    }

    private static AssistantReply DocRework(string q, string rwNo, string backend)
    {
        var r = MesDatabase.FindRework(rwNo)!;
        var hist = MesDatabase.GetReworkHistories(rwNo);
        return new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Rework,
            Confidence = 93,
            BackendName = backend,
            Headline = $"{r.ReworkNo}：电芯 {r.CellSn}｜状态「{r.Status}」｜原因 {r.Reason}｜责任工序 {r.ResponsibleProcess}｜返工 {r.ReworkTimes} 次",
            Facts =
            {
                new AssistantFact { Label = "状态", Value = r.Status, Level = ReworkStatus.IsFinal(r.Status) ? FactLevel.Normal : FactLevel.Warn },
                new AssistantFact { Label = "来源保留单", Value = string.IsNullOrWhiteSpace(r.HoldNo) ? "—" : r.HoldNo },
                new AssistantFact { Label = "返工工序", Value = string.IsNullOrWhiteSpace(r.ReworkProcess) ? "—" : r.ReworkProcess },
                new AssistantFact { Label = "返工参数", Value = string.IsNullOrWhiteSpace(r.ReworkParam) ? "—" : r.ReworkParam },
                new AssistantFact { Label = "最近重检", Value = string.IsNullOrWhiteSpace(r.InspectResult) ? "尚未重检" : $"{r.InspectResult}（实测 {r.InspectMeasured} / 标准 {r.InspectStandard}）" },
                new AssistantFact { Label = "流转履历", Value = $"{hist.Count} 条" }
            },
            Evidence = { "数据来源：MesDatabase.FindRework + GetReworkHistories" },
            Body = hist.Take(6).Select(h => $"履历：{h.Time:MM-dd HH:mm} {h.Operator} {h.Action} {h.Detail}").ToList(),
            Actions = ActionsOf(("q.reworkFlow", "按流转推进"), ("q.reworkPrint", "打印返工单"), ("q.hold", "看来源保留单"))
        };
    }

    private static AssistantReply DocFai(string q, string faiNo, string backend)
    {
        var f = MesDatabase.FindFai(faiNo)!;
        var items = MesDatabase.GetFaiItems(faiNo);
        return new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Overview,
            Confidence = 90,
            BackendName = backend,
            Headline = $"{f.FaiNo}：状态「{f.Status}」｜工单 {f.WoNo}｜工序 {f.ProcessName}｜项目 {items.Count} 项",
            Facts =
            {
                new AssistantFact { Label = "状态", Value = f.Status, Level = FaiStatus.IsPassed(f.Status) ? FactLevel.Normal : FactLevel.Warn },
                new AssistantFact { Label = "工单", Value = f.WoNo },
                new AssistantFact { Label = "首检工序", Value = f.ProcessName },
                new AssistantFact { Label = "首件电芯", Value = string.IsNullOrWhiteSpace(f.FirstCellSn) ? "—" : f.FirstCellSn },
                new AssistantFact { Label = "检验项目", Value = $"{items.Count} 项（合格 {f.PassCount} / 不合格 {f.FailCount}）" }
            },
            Evidence = { "数据来源：MesDatabase.FindFai + GetFaiItems" },
            Body = new List<string> { "首件未放行时，该工序的批量生产会被拦住——这是防呆规则，不是提示。" },
            Actions = ActionsOf(("prod.fai", "打开首件检验"), ("prod.faiPrint", "打印首件报告"))
        };
    }

    private static AssistantReply DocSpc(string q, string alertNo, string backend)
    {
        var a = MesDatabase.FindSpcAlert(alertNo)!;
        return new AssistantReply
        {
            Question = q,
            Intent = AssistantIntent.Spc,
            Confidence = 93,
            BackendName = backend,
            Headline = $"{a.AlertNo}：[{a.Severity}] {a.AlertType}｜{a.ItemName}｜{a.RuleName}｜状态「{a.Status}」",
            Facts =
            {
                new AssistantFact { Label = "状态", Value = a.Status, Level = a.Status == SpcAlertStatus.Closed ? FactLevel.Normal : FactLevel.Warn },
                new AssistantFact { Label = "实测值", Value = $"{a.Value:0.###}" },
                new AssistantFact { Label = "控制限", Value = $"CL {a.CenterLine:0.###}／UCL {a.Ucl:0.###}／LCL {a.Lcl:0.###}" },
                new AssistantFact { Label = "规格限", Value = double.IsNaN(a.Usl) ? "未设规格" : $"USL {a.Usl:0.###}／LSL {a.Lsl:0.###}" },
                new AssistantFact { Label = "规格版本", Value = string.IsNullOrWhiteSpace(a.VersionText) ? "—" : a.VersionText },
                new AssistantFact { Label = "触发设备", Value = string.IsNullOrWhiteSpace(a.Equipment) ? "—" : a.Equipment },
                new AssistantFact { Label = "触发样本时间", Value = $"{a.SampleTime:yyyy-MM-dd HH:mm}" },
                new AssistantFact { Label = "关联异常单", Value = string.IsNullOrWhiteSpace(a.LinkedAbnormalNo) ? "尚未转单" : a.LinkedAbnormalNo, Level = string.IsNullOrWhiteSpace(a.LinkedAbnormalNo) ? FactLevel.Watch : FactLevel.Normal }
            },
            Evidence = { "数据来源：MesDatabase.FindSpcAlert" },
            Actions = ActionsOf(("q.spcAck", "确认该预警"), ("q.spcToAbnormal", "转异常单闭环"), ("q.spcAlert", "看参数预警列表"))
        };
    }

    // ================= 分类与工具 =================

    /// <summary>
    /// 关键词判意图。返回 (意图, 置信度, 命中的关键词)。
    /// 打分：A 档词权重 = 10 + 词长 × 2，B 档词权重 = 词长；取最高分意图，
    /// 与第二名分差越大置信度越高。
    /// 最低门槛 <see cref="MinIntentScore"/>：只有疑问词（"怎么样"之类）沾边时不给结论，
    /// 直接走兜底并提示换问法——比硬猜一个数据域更诚实。
    /// </summary>
    private const int MinIntentScore = 8;

    private static (string Intent, int Confidence, List<string> Hits) Classify(string norm)
    {
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        var hitMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (intent, tierA, tierB) in KeywordTable)
        {
            int score = 0;
            var hits = new List<string>();
            foreach (var w in tierA)
            {
                var nw = w.ToUpperInvariant();
                if (norm.Contains(nw, StringComparison.Ordinal)) { score += 10 + nw.Length * 2; hits.Add(w); }
            }
            foreach (var w in tierB)
            {
                var nw = w.ToUpperInvariant();
                if (norm.Contains(nw, StringComparison.Ordinal)) { score += nw.Length; hits.Add(w); }
            }
            if (score > 0) { scores[intent] = score; hitMap[intent] = hits; }
        }

        if (scores.Count == 0) return (AssistantIntent.Unknown, 0, new List<string>());

        var ordered = scores.OrderByDescending(kv => kv.Value).ToList();
        var win = ordered[0];
        if (win.Value < MinIntentScore) return (AssistantIntent.Unknown, 0, hitMap[win.Key]);

        var second = ordered.Count > 1 ? ordered[1].Value : 0;
        var conf = Math.Clamp(40 + win.Value + Math.Min(25, (win.Value - second) * 2), 15, 95);
        return (win.Key, conf, hitMap[win.Key]);
    }

    /// <summary>从提问里找出被点名的人（工号优先，其次姓名全等）。只做精确匹配，避免误伤。</summary>
    private static UserProfile? ResolveUser(string q, string norm, AssistantEntities ent)
    {
        if (ent.EmpNo is { Length: > 0 } emp)
            return MesDatabase.FindUser(emp);

        foreach (var u in MesDatabase.Users)
        {
            if (u.Name.Length >= 2 && q.Contains(u.Name, StringComparison.Ordinal)) return u;
            if (norm.Contains(u.EmpNo.ToUpperInvariant(), StringComparison.Ordinal)) return u;
        }
        return null;
    }

    /// <summary>从提问里找出被点名的工序（按已知工序词典匹配）。</summary>
    private static string? ResolveProcess(string norm)
    {
        // 工序词典取两套既有口径的并集（检验模板 + 设备工序映射 + WIP 工序顺序），与实际业务同源
        var processes = MesDatabase.InspectionTemplates.Keys
            .Concat(MesDatabase.DeviceProcessMap.Values)
            .Concat(ProcessSequence.Order)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(p => p.Length)
            .ToList();

        foreach (var p in processes)
            if (norm.Contains(p.ToUpperInvariant(), StringComparison.Ordinal)) return p;
        return null;
    }

    /// <summary>用使用手册条目构造建议动作（保证"去哪、点什么"与手册口径完全一致）。</summary>
    private static AssistantAction? Act(string manualId, string label)
    {
        var e = ManualSearch.ById(manualId);
        if (e is null) return null;
        return new AssistantAction
        {
            Label = label,
            TargetPage = e.TargetPage,
            TargetSubPage = e.TargetSubPage,
            TargetButton = e.TargetButton,
            Hint = string.IsNullOrWhiteSpace(e.Summary) ? e.Title : e.Summary
        };
    }

    /// <summary>批量构造建议动作，条目缺失的自动跳过（不会产生指向不存在页面的死按钮）。</summary>
    private static List<AssistantAction> ActionsOf(params (string Id, string Label)[] items)
    {
        var list = new List<AssistantAction>();
        foreach (var (id, label) in items)
        {
            var a = Act(id, label);
            if (a is not null) list.Add(a);
        }
        return list;
    }
}
