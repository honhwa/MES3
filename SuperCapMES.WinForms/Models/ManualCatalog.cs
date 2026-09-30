using System.Collections.Generic;

namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 用户手册条目库。
/// 编写约定：
///   1. TargetPage 必须与导航中真实的二级页面标题完全一致（跳转依赖它，写错会跳不过去）；
///   2. TargetButton 必须与页面上按钮文字完全一致（用中文书名号「」引用按钮时不含符号本体）；
///   3. Aliases 尽量收口语说法，这是模糊搜索"听得懂人话"的关键；
///   4. 每条都要回答：去哪、点什么、按什么顺序。
/// </summary>
public static class ManualCatalog
{
    /// <summary>全部手册条目。</summary>
    public static readonly IReadOnlyList<ManualEntry> All = Build();

    private static List<ManualEntry> Build()
    {
        var list = new List<ManualEntry>
        {
        // ============================================================
        // 帮助 / 导航
        // ============================================================
        new ManualEntry
        {
            Id = "help.nav", Category = "帮助", Title = "找不到页面 / 系统有哪些功能",
            Aliases = new[] { "页面在哪", "都有什么功能", "功能清单", "系统能干什么", "菜单在哪", "找不到按钮" },
            Keywords = new[] { "导航", "分类", "页签", "功能清单", "总览", "生产管理", "质量管理" },
            Summary = "系统按业务模块分成 7 个一级分类，每个分类下的子页签就是具体功能页面。",
            TargetPage = "使用手册", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "顶部一排页签是一级分类：总览 / 生产管理 / 质量管理 / 仓储与出货 / 设备与人员 / 追溯与报表 / 帮助",
                "点某个一级分类，它下面的第二排页签就是该分类包含的具体页面",
                "例如「质量管理」下有：异常登记、异常管理、保留管理、返工管理、SPC 分析、SPC 项目管理、客户投诉与 8D",
                "在「使用手册」页输入你想做的事（如「不合格怎么办」），手册会直接告诉你该去哪个页面、点哪个按钮"
            },
            Tip = "分类归属速记：跟单/做工属于「生产管理」，管不合格品/客诉属于「质量管理」，收货发货属于「仓储与出货」，设备与人员属于「设备与人员」，查数据出报表属于「追溯与报表」。"
        },
        new ManualEntry
        {
            Id = "help.autoRefresh", Category = "帮助", Title = "设置自动刷新 / 手动刷新数据",
            Aliases = new[] { "数据不更新", "怎么刷新", "页面数据是旧的", "定时刷新", "自动刷新" },
            Keywords = new[] { "刷新", "自动刷新", "定时", "状态栏", "实时" },
            Summary = "底部状态栏可开启自动刷新并设置间隔；也可随时点「立即刷新」手动拉一次。",
            TargetPage = "使用手册", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "看窗体最下方的状态栏（深蓝色）",
                "勾选「自动刷新」开关，在后面的输入框填间隔分钟数，点「应用」",
                "想立刻刷新一次，直接点「立即刷新」"
            },
            Tip = "这是原型系统：数据全在内存里，关掉程序再打开会回到初始演示数据。"
        },
        new ManualEntry
        {
            Id = "help.dataReset", Category = "帮助", Title = "数据会丢吗 / 权限怎么设",
            Aliases = new[] { "数据会丢吗", "重启数据", "没有数据库", "权限设置", "用户登录" },
            Keywords = new[] { "原型", "演示", "内存", "数据库", "权限", "重启" },
            Summary = "当前定位是测试/演示原型：数据存在内存中，重启即回到初始演示数据；未启用登录与权限。",
            TargetPage = "使用手册", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "本系统为演示原型，所有数据在内存中，关闭程序后新增的数据不会保留",
                "每次启动会自动播种一套完整的演示数据（工单、实绩、客诉、证书等），便于直接演示",
                "系统目前没有登录与权限控制，人员模块里的「人员」是档案与资质数据，不等于登录账号"
            },
            Tip = "正式落地时需要补三件事：数据库持久化、登录与权限、与 ERP/培训系统的接口。"
        },

        // ============================================================
        // 总览
        // ============================================================
        new ManualEntry
        {
            Id = "ov.dashboard", Category = "总览", Title = "看今天的产量 / 在制 / 良率 / 开机设备",
            Aliases = new[] { "今天的产量", "良率多少", "在制品有多少", "看板数据", "生产情况怎么样", "开机率" },
            Keywords = new[] { "看板", "产量", "WIP", "良率", "开机", "运行设备", "概览" },
            Summary = "生产看板汇总四个关键指标 + 生产中工单列表 + 设备状态，是每天开工第一个看的页面。",
            TargetPage = "生产看板", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "顶部一级分类选「总览」，子页选「生产看板」",
                "上方四张卡片依次是：总产量、在制数量、良率(%)、运行设备数/总设备数",
                "下方左侧表格是「生产中」的工单，右侧表格是全部设备及其状态"
            },
            SeeAlso = new[] { "ov.alert", "prod.wip" }
        },
        new ManualEntry
        {
            Id = "ov.alert", Category = "总览", Title = "看今天有哪些事要处理（预警中心）",
            Aliases = new[] { "今天有事吗", "待办事项", "有哪些预警", "要处理什么", "报警汇总", "有什么问题" },
            Keywords = new[] { "预警", "报警", "待办", "警报", "严重度", "高优先级", "汇总" },
            Summary = "预警中心把全系统 10 类待处理事项聚到一张表，按严重度和时间排序，是管理者视角的「今天干什么」。",
            TargetPage = "预警中心", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "顶部一级分类选「总览」，子页选「预警中心」",
                "上方四张卡片是高 / 中 / 低 / 总数，一眼看出压在手上的量",
                "表格按严重度（高→中→低）、同级按时间倒序排列，排第一的就是最该先看的"
            },
            Tip = "预警来源共 10 类：SPC 预警、异常未闭环、保留超期、点检保养、库存效期、上料防错、首件检验、返工积压、备件库存、出货绑定。",
            SeeAlso = new[] { "ov.alertFilter", "ov.alertJump", "ov.alertExport" }
        },
        new ManualEntry
        {
            Id = "ov.alertFilter", Category = "总览", Title = "只看某一类或高优先级的预警",
            Aliases = new[] { "只看严重问题", "筛选预警", "只看某类预警", "过滤待办" },
            Keywords = new[] { "筛选", "过滤", "来源", "严重度", "只看", "高" },
            Summary = "用来源和严重度两个下拉框叠加筛选，快速收敛到你要看的那一撮。",
            TargetPage = "预警中心", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "在「预警中心」页顶部找到两个下拉框：来源、严重度",
                "来源可选具体模块（如「异常未闭环」）或「全部」",
                "严重度可选 高 / 中 / 低 或「全部」，两个条件同时生效"
            },
            SeeAlso = new[] { "ov.alert", "ov.alertJump" }
        },
        new ManualEntry
        {
            Id = "ov.alertJump", Category = "总览", Title = "从预警直接跳到出问题的单据",
            Aliases = new[] { "预警怎么处理", "跳到单据", "去处理这条预警", "定位问题单据" },
            Keywords = new[] { "跳转", "定位", "处理", "双击", "联动" },
            Summary = "选中预警后一键跳转到对应页面，并自动选中那条单据，不用自己去翻。",
            TargetPage = "预警中心", TargetSubPage = "", TargetButton = "跳转处理",
            Steps = new[]
            {
                "在「预警中心」列表里点中要处理的那一行（或直接双击该行）",
                "点上方按钮「跳转处理」",
                "系统会自动切到对应页面并把关联单据选中，接着在该页面继续操作",
                "页面上的提示文字会告诉你跳到了哪里、要处理什么"
            },
            SeeAlso = new[] { "ov.alert", "help.nav" }
        },
        new ManualEntry
        {
            Id = "ov.alertExport", Category = "总览", Title = "导出预警清单",
            Aliases = new[] { "把预警导出来", "导出待办", "预警表格发我", "导出excel" },
            Keywords = new[] { "导出", "excel", "xlsx", "清单", "打印" },
            Summary = "导出为真 .xlsx，含预警总表、按来源汇总、按严重度汇总三个工作表。",
            TargetPage = "预警中心", TargetSubPage = "", TargetButton = "导出清单",
            Steps = new[]
            {
                "在「预警中心」页点按钮「导出清单」",
                "选择保存位置和文件名",
                "导出成功会提示工作表数量与数据行数"
            },
            SeeAlso = new[] { "ov.alert" }
        },

        // ============================================================
        // 总览 —— AI 助手
        // ============================================================
        new ManualEntry
        {
            Id = "ai.ask", Category = "总览", Title = "问 AI 助手工厂现状",
            Aliases = new[] { "问一下", "AI助手怎么用", "怎么问系统", "有没有人工智能", "智能问答", "帮我查一下", "问机器人" },
            Keywords = new[] { "AI", "助手", "问答", "提问", "智能", "汇总", "现状", "数据依据" },
            Summary = "在「AI 助手」页用一句话问现状（产量 / 预警 / 异常 / 保留 / 返工 / 客诉 / 人员 / 库存 / 设备 / SPC / 追溯），回答实时取自系统数据并附数据出处。",
            TargetPage = "AI 助手", TargetSubPage = "", TargetButton = "提问",
            Steps = new[]
            {
                "切到「总览 › AI 助手」页签",
                "在输入框里用大白话问，例如「今天有什么要处理的」",
                "点「提问」或直接回车",
                "看回答的「关键数据」和「数据依据」，确认识别是否正确",
                "点底部「建议动作」按钮可直接跳到对应页面处理"
            },
            Tip = "助手只汇总现有数据、不做合格判定，也不改动任何数据；回答附带数据出处，可反查追责。"
                  + "带编号提问最准（如「SC-A260812-L1-00001 这只电芯怎么回事」）。",
            SeeAlso = new[] { "ai.explain", "help.nav", "ov.alert" }
        },
        new ManualEntry
        {
            Id = "ai.explain", Category = "总览", Title = "看助手为什么这么回答（可解释面板）",
            Aliases = new[] { "它怎么理解的", "置信度是什么意思", "为什么给这个答案", "数据是哪来的", "可解释", "回答依据" },
            Keywords = new[] { "置信度", "意图", "数据依据", "可解释", "出处", "识别", "编号", "后端" },
            Summary = "AI 助手右侧「系统理解与数据依据」面板显示：识别到的意图、置信度、从提问里抽到的编号、本条回答的数据出处，以及当前回答由哪个后端产生。",
            TargetPage = "AI 助手", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "在「AI 助手」页提一个问题",
                "看右侧面板「系统理解」——意图 / 置信度 / 识别到的编号",
                "看「数据依据」——每个数取自哪个汇总方法",
                "看面板底部「本轮回答由…生成」——确认当前是用规则引擎还是大模型"
            },
            Tip = "置信度是「匹配强度」而不是模型准确率：低置信度说明问题里关键词太少，补上编号或业务术语会更准。",
            SeeAlso = new[] { "ai.ask" }
        },

        // ============================================================
        // 生产管理 —— 工单
        // ============================================================
        new ManualEntry
        {
            Id = "wo.create", Category = "生产管理", Title = "新建工单",
            Aliases = new[] { "开一张工单", "下生产任务", "建单", "排产", "加个工单", "手动作业单" },
            Keywords = new[] { "新建", "工单", "下单", "排产", "生产任务" },
            Summary = "手工创建生产工单。测试生产场景可以不填 ERP PO 号。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "＋ 新建工单",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」",
                "点按钮「＋ 新建工单」",
                "在弹出的表单里填产品型号、计划数量、交期等信息后保存",
                "保存后工单出现在左侧列表，状态为待生产"
            },
            Tip = "PO 号可以不填——适用于测试生产时手工生成工单的场景。",
            SeeAlso = new[] { "wo.confirm", "wo.erpPull" }
        },
        new ManualEntry
        {
            Id = "wo.edit", Category = "生产管理", Title = "修改工单（数量 / 交期 / 备注）",
            Aliases = new[] { "改工单数量", "工单填错了", "修改工单信息", "改交期", "工单能改吗" },
            Keywords = new[] { "修改", "编辑", "工单", "数量", "交期" },
            Summary = "对已创建但尚未终结的工单修改内容。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "修改工单",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」",
                "在左侧列表里点中要修改的工单",
                "点按钮「修改工单」，改完后保存"
            },
            Tip = "已终结的工单通常不允许再改数量；ERP 来源的工单改动会与 ERP 数据不一致，改动前先确认。",
            SeeAlso = new[] { "wo.create", "wo.close" }
        },
        new ManualEntry
        {
            Id = "wo.confirm", Category = "生产管理", Title = "确认生产（工单开工）",
            Aliases = new[] { "工单开工", "开始生产", "下达生产", "确认投产" },
            Keywords = new[] { "确认生产", "开工", "投产", "状态", "待生产" },
            Summary = "把工单从待生产推进到生产中，之后才能录实绩。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "确认生产",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」，点中目标工单",
                "点按钮「确认生产」",
                "工单状态变为「生产中」，此时该工单会出现在生产看板的在制工单列表里"
            },
            SeeAlso = new[] { "wo.erpReport", "wo.close" }
        },
        new ManualEntry
        {
            Id = "wo.close", Category = "生产管理", Title = "终结工单",
            Aliases = new[] { "关闭工单", "工单做完了", "结单", "收尾" },
            Keywords = new[] { "终结", "关闭", "结单", "完结", "工单结束" },
            Summary = "生产完成后的收尾动作。只有终结状态的工单才能做 ERP 报工。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "终结工单",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」，点中要终结的工单",
                "确认数量、实绩都已录完",
                "点按钮「终结工单」"
            },
            Tip = "报工的前置条件是工单已终结——如果「ERP 报工」点不动，先回来看工单是不是还是生产中。",
            SeeAlso = new[] { "wo.erpReport" }
        },
        new ManualEntry
        {
            Id = "wo.erpPull", Category = "生产管理", Title = "从 ERP 下发 / 拉取工单（PO）",
            Aliases = new[] { "从erp拿单", "同步工单", "erp下发", "拉取采购订单", "引入erp订单" },
            Keywords = new[] { "ERP", "PO", "下发", "同步", "拉取", "接口" },
            Summary = "从 ERP 把已下发的采购订单（PO）拉到 MES，作为工单来源。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "⬆ ERP 下发",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」",
                "点按钮「⬆ ERP 下发」",
                "ERP 中「已下发」状态的 PO 会出现在可选列表里",
                "选中 PO 创建工单"
            },
            Tip = "只有已下发的 PO 能被选中；待下发的 PO 必须先下发。",
            SeeAlso = new[] { "wo.erpReport", "wo.poInfo" }
        },
        new ManualEntry
        {
            Id = "wo.erpReport", Category = "生产管理", Title = "向 ERP 报工",
            Aliases = new[] { "报工", "回传产量", "同步完工数量", "erp报工", "上报生产结果" },
            Keywords = new[] { "报工", "ERP", "上报", "回传", "完工" },
            Summary = "把 MES 的实际生产结果回传给 ERP，是 ERP 与 MES 闭环的最后一步。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "📤 ERP 报工",
            Steps = new[]
            {
                "先确认工单已经「终结」（生产未结束不允许报工）",
                "进入「生产管理 › 工单管理」，点中该工单",
                "点按钮「📤 ERP 报工」",
                "报工完成后工单上会留下报工记录"
            },
            Tip = "报工按钮点不动，九成是工单还没终结。",
            SeeAlso = new[] { "wo.close", "wo.erpPull" }
        },
        new ManualEntry
        {
            Id = "wo.poInfo", Category = "生产管理", Title = "查看工单对应的 ERP PO 信息 / BOM",
            Aliases = new[] { "看bom", "查po信息", "工单用哪些料", "物料清单" },
            Keywords = new[] { "PO", "BOM", "物料清单", "采购订单", "子页", "明细" },
            Summary = "工单管理页下半部分有两个子页：ERP PO 信息与 BOM 列表。",
            TargetPage = "工单管理", TargetSubPage = "ERP PO 信息 / BOM 列表", TargetButton = "",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」，点中一条工单",
                "在下半部分的子页签里选「ERP PO 信息」，查看来源订单的供应商、数量、交期",
                "切到「BOM 列表」查看该工单需要消耗的物料清单"
            },
            SeeAlso = new[] { "wo.erpPull" }
        },
        new ManualEntry
        {
            Id = "wo.print", Category = "生产管理", Title = "打印工单流转卡",
            Aliases = new[] { "打印流转卡", "工单打出来", "纸质工单", "随工单" },
            Keywords = new[] { "打印", "流转卡", "工单", "纸质", "单据" },
            Summary = "生成可打印的工单流转卡文本，用于车间随工流转。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "打印流转卡",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」，点中要打印的工单",
                "点按钮「打印流转卡」",
                "在弹出的文本窗口里核对内容后打印"
            }
        },

        // ============================================================
        // 生产管理 —— 实绩 / WIP / 上料 / 首件 / 模组
        // ============================================================
        new ManualEntry
        {
            Id = "act.upload", Category = "生产管理", Title = "录入 / 上传工序实绩",
            Aliases = new[] { "录实绩", "工序数据录入", "设备上传数据", "报工给设备", "记录生产数据" },
            Keywords = new[] { "实绩", "工序", "上传", "采集", "设备数据", "PLC" },
            Summary = "把某台设备在某道工序上的生产实绩录入系统，是后续追溯与派工合规核查的数据源。",
            TargetPage = "工序实绩", TargetSubPage = "", TargetButton = "模拟设备上传",
            Steps = new[]
            {
                "进入「生产管理 › 工序实绩」",
                "在顶部下拉框选择要录数据的设备",
                "点按钮「模拟设备上传」生成一条实绩记录",
                "记录会出现在下方表格，含工单、工序、操作员、参数、时间"
            },
            Tip = "操作员由系统按资质自动派工（无资质者会被刻意抽中少量样本，用于演示资质核查能抓到人）。",
            SeeAlso = new[] { "act.inspect", "hr.check" }
        },
        new ManualEntry
        {
            Id = "act.inspect", Category = "生产管理", Title = "录入工序检验结果",
            Aliases = new[] { "录检验", "填检验数据", "工序检验", "录入检测结果" },
            Keywords = new[] { "检验", "检测", "录入", "判定", "合格" },
            Summary = "对当前工序的在制品做检验并记录结果。",
            TargetPage = "工序实绩", TargetSubPage = "", TargetButton = "载入检验项目",
            Steps = new[]
            {
                "进入「生产管理 › 工序实绩」，先选中要检验的对象",
                "点按钮「载入检验项目」把待检项拉进表格",
                "逐项填写实测值",
                "点按钮「保存检验」提交判定结果"
            },
            SeeAlso = new[] { "act.upload", "q.abnormalNew" }
        },
        new ManualEntry
        {
            Id = "prod.wip", Category = "生产管理", Title = "看在制品（WIP）在各工序的分布",
            Aliases = new[] { "在制品多少", "wip", "半成品堆在哪", "工序间积压", "瓶颈在哪" },
            Keywords = new[] { "WIP", "在制", "工序", "分布", "积压", "瓶颈" },
            Summary = "按工序展示在制品的数量与滞留时长，用来看产线哪里堵了。",
            TargetPage = "WIP 看板", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "进入「生产管理 › WIP 看板」",
                "表格按工序列出在制数量，重点看数量最多、滞留最久的那道工序"
            },
            Tip = "演示时可用「模拟推进5只」让数据动起来，制造「产线在跑」的直观感受。",
            SeeAlso = new[] { "prod.wipSim", "ov.dashboard" }
        },
        new ManualEntry
        {
            Id = "prod.wipSim", Category = "生产管理", Title = "模拟推进在制品（让产线动起来）",
            Aliases = new[] { "让数据动起来", "模拟生产", "推进在制品", "演示数据变化" },
            Keywords = new[] { "模拟", "推进", "演示", "在制", "流转" },
            Summary = "把 5 只在制品推进到下一道工序，演示时制造产线持续运转的观感。",
            TargetPage = "WIP 看板", TargetSubPage = "", TargetButton = "模拟推进5只",
            Steps = new[]
            {
                "进入「生产管理 › WIP 看板」",
                "点按钮「模拟推进5只」",
                "观察各工序在制数量与看板数据随之变化"
            },
            SeeAlso = new[] { "prod.wip" }
        },
        new ManualEntry
        {
            Id = "prod.feeding", Category = "生产管理", Title = "上料防错 / 校验用的物料对不对",
            Aliases = new[] { "上料", "用料对不对", "换料校验", "防错", "用错料", "扫码上料" },
            Keywords = new[] { "上料", "防错", "校验", "物料", "批次", "扫码" },
            Summary = "在换料/上料环节校验物料与工单要求是否一致，防止用错料。",
            TargetPage = "上料防错", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "进入「生产管理 › 上料防错」",
                "选择当前工单与工位",
                "扫描或选择要上的物料批次，系统比对 BOM 要求给出通过 / 拦截结论",
                "拦截记录会汇总到预警中心的「上料防错」来源下"
            },
            SeeAlso = new[] { "ov.alert", "wo.poInfo" }
        },
        new ManualEntry
        {
            Id = "prod.fai", Category = "生产管理", Title = "首件检验（FAI）",
            Aliases = new[] { "首件", "fai", "换型首件", "开班首件", "首件确认", "批次开头检验" },
            Keywords = new[] { "首件", "FAI", "换型", "开班", "检验", "判定" },
            Summary = "换型或开班时对首件做检验与判定，通过后才允许批量生产。",
            TargetPage = "首件检验", TargetSubPage = "", TargetButton = "新建首件检验",
            Steps = new[]
            {
                "进入「生产管理 › 首件检验」",
                "点按钮「新建首件检验」，选择工单、工序、设备",
                "填写首件的检验项实测值",
                "点按钮「提交判定」得出结论；需要纸质留档时点「打印首检单」"
            },
            Tip = "首件不合格会进入预警中心，未处置前建议暂停批量生产。",
            SeeAlso = new[] { "prod.faiPrint", "q.abnormalNew" }
        },
        new ManualEntry
        {
            Id = "prod.faiPrint", Category = "生产管理", Title = "打印首检单",
            Aliases = new[] { "首件单打印", "打首检单" },
            Keywords = new[] { "打印", "首件", "首检单" },
            Summary = "生成首件检验单的打印文本。",
            TargetPage = "首件检验", TargetSubPage = "", TargetButton = "打印首检单",
            Steps = new[]
            {
                "进入「生产管理 › 首件检验」，点中一条首件记录",
                "点按钮「打印首检单」"
            },
            SeeAlso = new[] { "prod.fai" }
        },
        new ManualEntry
        {
            Id = "prod.module", Category = "生产管理", Title = "模组装配（组模）",
            Aliases = new[] { "组模", "模组", "装配", "模组追溯", "打包成组" },
            Keywords = new[] { "模组", "装配", "组模", "追溯", "批次" },
            Summary = "把若干只电芯装配为一个模组，并建立模组到电芯的追溯关系。",
            TargetPage = "模组装配", TargetSubPage = "", TargetButton = "新建模组装配",
            Steps = new[]
            {
                "进入「生产管理 › 模组装配」",
                "点按钮「新建模组装配」",
                "选择要装入的电芯（SN），系统生成模组编号并记录装配关系",
                "后续可基于模组反向查出内部电芯"
            },
            Tip = "电芯序列放在产线最末端，模组追溯能支撑「客户投诉到模组 → 定位到电芯」的反查。",
            SeeAlso = new[] { "prod.moduleExport", "ship.recall" }
        },
        new ManualEntry
        {
            Id = "prod.moduleDismantle", Category = "生产管理", Title = "拆解模组",
            Aliases = new[] { "拆模组", "解开模组", "模组拆开" },
            Keywords = new[] { "拆解", "模组", "解除" },
            Summary = "解除模组与内部电芯的装配关系（返工、拆检场景使用）。",
            TargetPage = "模组装配", TargetSubPage = "", TargetButton = "拆解模组",
            Steps = new[]
            {
                "进入「生产管理 › 模组装配」，点中要拆解的模组",
                "点按钮「拆解模组」并确认",
                "模组解散，内部电芯回到可单独处置的状态"
            },
            SeeAlso = new[] { "prod.module" }
        },
        new ManualEntry
        {
            Id = "prod.moduleExport", Category = "生产管理", Title = "导出模组追溯报告",
            Aliases = new[] { "模组报告导出", "模组追溯表" },
            Keywords = new[] { "导出", "模组", "追溯报告" },
            Summary = "把模组及其内部电芯的追溯信息导出为 Excel。",
            TargetPage = "模组装配", TargetSubPage = "", TargetButton = "导出追溯报告",
            Steps = new[]
            {
                "进入「生产管理 › 模组装配」，点中目标模组",
                "点按钮「导出追溯报告」，选择保存位置"
            },
            SeeAlso = new[] { "prod.module", "trace.export" }
        },

        // ============================================================
        // 质量管理 —— 异常
        // ============================================================
        new ManualEntry
        {
            Id = "q.abnormalNew", Category = "质量管理", Title = "发现不合格品 / 异常的登记",
            Aliases = new[] { "不合格怎么办", "东西做坏了", "有异常", "发现不良", "要开异常单", "质量事故登记", "出问题了" },
            Keywords = new[] { "异常", "不合格", "不良", "登记", "开单", "质量事件" },
            Summary = "车间发现不合格或异常时开异常单，是质量问题处置链的起点。",
            TargetPage = "异常登记", TargetSubPage = "", TargetButton = "保存登记",
            Steps = new[]
            {
                "进入「质量管理 › 异常登记」",
                "填写发现人、发现时间、异常描述、临时措施、责任人、登记人",
                "点按钮「保存登记」暂存，核对无误后点「确认登记」正式提交",
                "提交后异常单进入「异常管理」等待闭环分析"
            },
            Tip = "填错了点「重置」清空重填。异常单会同时出现在预警中心的「异常未闭环」里。",
            SeeAlso = new[] { "q.abnormalClose", "q.hold" }
        },
        new ManualEntry
        {
            Id = "q.abnormalClose", Category = "质量管理", Title = "异常单闭环分析",
            Aliases = new[] { "异常闭环", "关闭异常单", "处理异常", "填原因对策", "异常分析" },
            Keywords = new[] { "闭环", "分析", "关闭", "原因", "对策", "异常单" },
            Summary = "对已登记的异常做根因分析与对策落实，完成后闭环。",
            TargetPage = "异常管理", TargetSubPage = "", TargetButton = "闭环分析",
            Steps = new[]
            {
                "进入「质量管理 › 异常管理」",
                "在列表里点中要处理的异常单",
                "点按钮「闭环分析」，填写根因与纠正措施",
                "保存后异常单状态推进到已闭环，从预警中心消失"
            },
            Tip = "只点「查看详情」是只看不改；要处理必须点「闭环分析」。",
            SeeAlso = new[] { "q.abnormalNew", "q.abnormalPrint" }
        },
        new ManualEntry
        {
            Id = "q.abnormalPrint", Category = "质量管理", Title = "打印异常单",
            Aliases = new[] { "异常单打出来", "打印异常" },
            Keywords = new[] { "打印", "异常单", "单据" },
            Summary = "生成异常单的打印文本，用于签字与留档。",
            TargetPage = "异常管理", TargetSubPage = "", TargetButton = "打印异常单",
            Steps = new[]
            {
                "进入「质量管理 › 异常管理」，点中目标异常单",
                "点按钮「打印异常单」"
            },
            SeeAlso = new[] { "q.abnormalClose" }
        },

        // ============================================================
        // 质量管理 —— 保留 / 返工
        // ============================================================
        new ManualEntry
        {
            Id = "q.hold", Category = "质量管理", Title = "不合格品隔离 / 保留（Hold）",
            Aliases = new[] { "隔离不良品", "把坏的锁起来", "保留单", "hold", "冻结不合格品", "不能流出去" },
            Keywords = new[] { "保留", "隔离", "冻结", "hold", "不合格品", "锁" },
            Summary = "把不合格电芯锁进保留区，防止误流到下道工序或发货。",
            TargetPage = "保留管理", TargetSubPage = "", TargetButton = "扫描不合格电芯并自动保留",
            Steps = new[]
            {
                "进入「质量管理 › 保留管理」",
                "点按钮「扫描不合格电芯并自动保留」，系统自动把不合格状态的电芯捞出来生成保留单",
                "在列表里核对被保留的电芯明细",
                "需要纸质单据时点「打印保留单」"
            },
            Tip = "保留单超期未处置会出现在预警中心的「保留超期」来源下。",
            SeeAlso = new[] { "q.holdRelease", "q.holdPrint", "mat.hold" }
        },
        new ManualEntry
        {
            Id = "q.holdRelease", Category = "质量管理", Title = "解除保留 / 放行",
            Aliases = new[] { "解保留", "放行", "处理完了放出来", "解除隔离" },
            Keywords = new[] { "解除", "保留", "放行", "解保" },
            Summary = "处置完成后解除保留，让电芯回到可用状态。",
            TargetPage = "保留管理", TargetSubPage = "", TargetButton = "解除保留",
            Steps = new[]
            {
                "进入「质量管理 › 保留管理」，点中要处置的保留单",
                "点按钮「解除保留」并确认",
                "电芯状态恢复为合格（若判定报废，则走返工管理里的报废流程）"
            },
            Tip = "解除保留意味着产品被放行，务必确认对应异常单已完成闭环判定。",
            SeeAlso = new[] { "q.hold", "q.abnormalClose" }
        },
        new ManualEntry
        {
            Id = "q.holdPrint", Category = "质量管理", Title = "打印保留单",
            Aliases = new[] { "保留单打印", "打hold单" },
            Keywords = new[] { "打印", "保留单" },
            Summary = "生成保留单的打印文本，用于现场贴签与留档。",
            TargetPage = "保留管理", TargetSubPage = "", TargetButton = "打印保留单",
            Steps = new[]
            {
                "进入「质量管理 › 保留管理」，点中目标保留单",
                "点按钮「打印保留单」"
            },
            SeeAlso = new[] { "q.hold" }
        },
        new ManualEntry
        {
            Id = "q.rework", Category = "质量管理", Title = "创建返工单 / 返修",
            Aliases = new[] { "返工", "返修", "重新做", "修一下", "返工单", "坏了要修" },
            Keywords = new[] { "返工", "返修", "维修", "返工单", "重做" },
            Summary = "对可修复的不合格品开返工单，走「返工 → 重检 → 放行」流程。",
            TargetPage = "返工管理", TargetSubPage = "", TargetButton = "新建返工单",
            Steps = new[]
            {
                "进入「质量管理 › 返工管理」",
                "点按钮「新建返工单」，选择要返工的电芯或批次、填写返工原因",
                "保存后返工单进入待返工状态"
            },
            Tip = "返工单创建后长时间未开工，会出现在预警中心的「返工积压」来源下。",
            SeeAlso = new[] { "q.reworkFlow", "q.reworkPrint" }
        },
        new ManualEntry
        {
            Id = "q.reworkFlow", Category = "质量管理", Title = "返工流程推进：开始 → 完成 → 重检 → 放行/报废",
            Aliases = new[] { "返工怎么走完", "返工完事重检", "返工放行", "重检", "返工报废" },
            Keywords = new[] { "返工", "开始", "完成", "重检", "放行", "报废", "流程" },
            Summary = "返工单是一条状态机：开始返工 → 返工完成 → 重检登记，最后由重检结果决定放行还是报废。",
            TargetPage = "返工管理", TargetSubPage = "", TargetButton = "开始返工",
            Steps = new[]
            {
                "进入「质量管理 › 返工管理」，点中目标返工单",
                "点按钮「开始返工」，状态变为返工中",
                "返修完成后点按钮「返工完成」",
                "送检后点按钮「重检登记」录入检验结果",
                "重检合格自动放行；重检不合格点按钮「报废」做报废处置"
            },
            SeeAlso = new[] { "q.rework", "q.hold" }
        },
        new ManualEntry
        {
            Id = "q.reworkPrint", Category = "质量管理", Title = "打印返工单",
            Aliases = new[] { "返工单打印", "打印返修单" },
            Keywords = new[] { "打印", "返工单", "返修单" },
            Summary = "生成返工单的打印文本，随返修品流转。",
            TargetPage = "返工管理", TargetSubPage = "", TargetButton = "打印返工单",
            Steps = new[]
            {
                "进入「质量管理 › 返工管理」，点中目标返工单",
                "点按钮「打印返工单」"
            },
            SeeAlso = new[] { "q.reworkFlow" }
        },

        // ============================================================
        // 质量管理 —— SPC
        // ============================================================
        new ManualEntry
        {
            Id = "q.spc", Category = "质量管理", Title = "看 SPC 控制图与过程能力（CPK）",
            Aliases = new[] { "spc", "控制图", "cpk", "过程能力", "参数稳定吗", "工序能力" },
            Keywords = new[] { "SPC", "控制图", "CPK", "过程能力", "参数", "趋势" },
            Summary = "按参数查看控制图与过程能力指数，判断工序是否稳定。",
            TargetPage = "SPC 分析", TargetSubPage = "控制图与能力", TargetButton = "",
            Steps = new[]
            {
                "进入「质量管理 › SPC 分析」",
                "在上方下拉框选择要看的产品/参数",
                "子页切到「控制图与能力」查看控制图与 CPK"
            },
            SeeAlso = new[] { "q.spcSample", "q.spcConfig" }
        },
        new ManualEntry
        {
            Id = "q.spcSample", Category = "质量管理", Title = "生成模拟样本（30 点）刷新控制图",
            Aliases = new[] { "模拟数据画图", "加点样本", "控制图没数据", "生成数据看控制图" },
            Keywords = new[] { "模拟", "样本", "控制图", "演示", "刷新" },
            Summary = "往选中参数上追加 30 个模拟样本点，用于演示控制图形态。",
            TargetPage = "SPC 分析", TargetSubPage = "控制图与能力", TargetButton = "生成模拟样本(30 点)",
            Steps = new[]
            {
                "进入「质量管理 › SPC 分析」，选中参数",
                "点按钮「生成模拟样本(30 点)」",
                "控制图与能力指数随即重算并刷新"
            },
            SeeAlso = new[] { "q.spc" }
        },
        new ManualEntry
        {
            Id = "q.spcAlert", Category = "质量管理", Title = "重算并生成参数预警",
            Aliases = new[] { "参数预警", "参数超限", "重算预警", "生成报警" },
            Keywords = new[] { "预警", "参数", "重算", "越界", "判异" },
            Summary = "按当前控制限重算所有参数，把越界/判异的点生成预警。",
            TargetPage = "SPC 分析", TargetSubPage = "参数预警", TargetButton = "重算并生成预警",
            Steps = new[]
            {
                "进入「质量管理 › SPC 分析」，切到子页「参数预警」",
                "点按钮「重算并生成预警」",
                "新生成的预警会进入预警中心的「SPC 预警」来源下"
            },
            SeeAlso = new[] { "q.spcAck", "q.spcToAbnormal", "ov.alert" }
        },
        new ManualEntry
        {
            Id = "q.spcAck", Category = "质量管理", Title = "确认 / 关闭参数预警（填写措施）",
            Aliases = new[] { "确认报警", "关闭参数预警", "处理spc预警", "填改善措施" },
            Keywords = new[] { "确认", "关闭", "预警", "措施", "ack" },
            Summary = "参数预警要么确认（知道了，观察），要么填措施后关闭（已处置）。",
            TargetPage = "SPC 分析", TargetSubPage = "参数预警", TargetButton = "确认",
            Steps = new[]
            {
                "进入「质量管理 › SPC 分析」，切到子页「参数预警」",
                "点中一条预警",
                "只是知情观察 → 点按钮「确认」",
                "已采取处置措施 → 点按钮「关闭（填写措施）」，填写措施内容后提交"
            },
            SeeAlso = new[] { "q.spcAlert", "q.spcToAbnormal" }
        },
        new ManualEntry
        {
            Id = "q.spcToAbnormal", Category = "质量管理", Title = "参数异常转成异常单",
            Aliases = new[] { "参数异常转异常单", "spc转异常", "参数问题开单" },
            Keywords = new[] { "转", "异常单", "SPC", "联动" },
            Summary = "参数预警严重时可以直接转成异常单，进入完整的异常闭环流程。",
            TargetPage = "SPC 分析", TargetSubPage = "参数预警", TargetButton = "转异常登记",
            Steps = new[]
            {
                "进入「质量管理 › SPC 分析」，切到子页「参数预警」",
                "点中要升级处理的参数预警",
                "点按钮「转异常登记」",
                "系统自动生成一张异常单，转到「异常管理」继续闭环"
            },
            SeeAlso = new[] { "q.spcAlert", "q.abnormalClose" }
        },
        new ManualEntry
        {
            Id = "q.spcConfig", Category = "质量管理", Title = "调整 SPC 控制限 / 上下限值",
            Aliases = new[] { "改控制限", "上限值不对", "调整规格线", "改usl", "参数上下限", "控制线设置" },
            Keywords = new[] { "控制限", "USL", "LSL", "上限", "下限", "规格", "调整" },
            Summary = "在每个参数的 SPC 项目管理里调整控制限与规格上下限。",
            TargetPage = "SPC 项目管理", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "进入「质量管理 › SPC 项目管理」",
                "在列表里选中要调整的参数",
                "修改中心值 / 上下控制限 / 规格上下限",
                "保存后按新控制限判定"
            },
            Tip = "调整会走版本管理（见「SPC 版本管理」条目），不会直接覆盖正在使用中的版本。",
            SeeAlso = new[] { "q.spcVersion" }
        },
        new ManualEntry
        {
            Id = "q.spcVersion", Category = "质量管理", Title = "SPC 版本管理 / 新版本什么时候生效",
            Aliases = new[] { "控制限版本", "新版本没生效", "什么时候用新标准", "生效时间", "版本号" },
            Keywords = new[] { "版本", "生效", "开始使用时间", "待生效", "版本号" },
            Summary = "控制限改动走版本：新版本可设定开始使用时间，到点后系统才按新版本判定。",
            TargetPage = "SPC 项目管理", TargetSubPage = "", TargetButton = "新建版本",
            Steps = new[]
            {
                "进入「质量管理 › SPC 项目管理」，选中参数",
                "点按钮「新建版本」，填新的控制限与「开始使用时间」",
                "未到生效时间的版本状态为待生效，可点「修改待生效版本」调整，或点「删除待生效版本」撤销",
                "到了设定时间，该版本自动成为生效版本，后续判定按新控制限执行"
            },
            Tip = "查询 SPC 项目的页面会显示版本号，便于确认当时用的是哪一版标准。",
            SeeAlso = new[] { "q.spcConfig" }
        },

        // ============================================================
        // 质量管理 —— 客诉与 8D
        // ============================================================
        new ManualEntry
        {
            Id = "c.create", Category = "质量管理", Title = "登记客户投诉",
            Aliases = new[] { "客户投诉了", "客诉登记", "客户抱怨", "客户反馈问题", "售后投诉" },
            Keywords = new[] { "客诉", "投诉", "客户", "登记", "抱怨" },
            Summary = "客户反馈质量问题时的登记入口，登记后可走 8D 归零流程。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "", TargetButton = "登记客诉",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」",
                "点按钮「登记客诉」",
                "填写客户、联系方式、严重度、类别、涉及产品/批次/SN、诉求内容",
                "保存后客诉进入台账，状态为待受理"
            },
            Tip = "严重度决定响应时限：严重 3 天 / 一般 7 天 / 轻微 15 天，超期会在台账上标红。",
            SeeAlso = new[] { "c.8d", "c.impact" }
        },
        new ManualEntry
        {
            Id = "c.8d", Category = "质量管理", Title = "推进 8D 报告（D1~D8）",
            Aliases = new[] { "8d", "八步法", "写8d报告", "推进8d", "客户要8d" },
            Keywords = new[] { "8D", "八步", "D1", "D8", "报告", "推进" },
            Summary = "8D 八步必须顺序推进，不可跳步；每步都有填写要求。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "8D 报告", TargetButton = "推进 8D",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」，点中一条客诉",
                "切到子页「8D 报告」，可看到当前推进到第几步、已填几步",
                "点按钮「推进 8D」进入下一步并填写内容",
                "顺序：D1 成立小组 → D2 问题描述 → D3 临时对策 → D4 根本原因 → D5 永久对策 → D6 实施验证 → D7 预防再发 → D8 小组总结"
            },
            Tip = "D8 提交后 8D 即齐备，不允许重复提交覆盖结案内容；要改内容用「修订步骤」。",
            SeeAlso = new[] { "c.8dEdit", "c.confirm", "c.close" }
        },
        new ManualEntry
        {
            Id = "c.8dEdit", Category = "质量管理", Title = "修订已填写的 8D 步骤内容",
            Aliases = new[] { "8d写错了", "改8d内容", "修订步骤", "8d某一步要改" },
            Keywords = new[] { "修订", "修改", "8D", "步骤", "内容" },
            Summary = "对已提交的 8D 某一步内容做修订，不改变整体推进进度。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "8D 报告", TargetButton = "修订步骤",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」，点中客诉，切到子页「8D 报告」",
                "点按钮「修订步骤」",
                "选择要修改的步骤（D1~D8），改写内容后保存"
            },
            SeeAlso = new[] { "c.8d" }
        },
        new ManualEntry
        {
            Id = "c.impact", Category = "质量管理", Title = "反查客诉的影响范围（涉及哪些批次 / 客户 / 电芯）",
            Aliases = new[] { "影响范围", "这批货还发给谁了", "客诉涉及多少", "反查批次", "问题扩散到哪" },
            Keywords = new[] { "影响范围", "反查", "追溯", "批次", "电芯", "召回" },
            Summary = "以客诉关联的批次/SN/单号为起点，反查全部受影响对象，复用了召回反查能力。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "触发范围反查", TargetButton = "",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」，点中客诉",
                "切到子页「触发范围反查」",
                "点查询，系统列出受影响的电芯、材料批次与出货记录"
            },
            Tip = "这里的反查逻辑与「出货与召回 › 召回反查」是同一套能力，结果一致。",
            SeeAlso = new[] { "c.freeze", "ship.recall" }
        },
        new ManualEntry
        {
            Id = "c.freeze", Category = "质量管理", Title = "冻结客诉影响范围（挡住风险品）",
            Aliases = new[] { "冻结影响范围", "把风险货锁住", "客诉冻结", "别让问题货再发出去" },
            Keywords = new[] { "冻结", "影响范围", "保留", "客诉", "拦截" },
            Summary = "一键把影响范围内的电芯冻结，系统自动生成保留单，防止风险品继续流出。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "触发范围反查", TargetButton = "冻结影响范围",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」，点中客诉，切到子页「触发范围反查」",
                "先执行反查确认影响范围",
                "点按钮「冻结影响范围」并确认",
                "系统自动生成保留单，被冻结电芯可在「保留管理」里看到"
            },
            Tip = "冻结会写入客诉流转履历，留痕可追溯。",
            SeeAlso = new[] { "c.impact", "q.hold", "c.history" }
        },
        new ManualEntry
        {
            Id = "c.confirm", Category = "质量管理", Title = "客户确认",
            Aliases = new[] { "客户认可了吗", "客户确认结果", "客户签字" },
            Keywords = new[] { "客户确认", "认可", "确认" },
            Summary = "8D 措施与结果取得客户认可后登记客户确认。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "8D 报告", TargetButton = "客户确认",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」，点中客诉",
                "点按钮「客户确认」",
                "填写确认人与确认结论后保存"
            },
            Tip = "涉及安全的客诉必须有客户确认才能关闭。",
            SeeAlso = new[] { "c.close" }
        },
        new ManualEntry
        {
            Id = "c.close", Category = "质量管理", Title = "关闭客诉",
            Aliases = new[] { "客诉结案", "关闭投诉", "客诉完事" },
            Keywords = new[] { "关闭", "结案", "客诉", "完成" },
            Summary = "客诉闭环的最后一步。关闭前系统会校验前置条件，防止草率结案。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "", TargetButton = "关闭客诉",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」，点中要关闭的客诉",
                "点按钮「关闭客诉」",
                "系统校验：D3/D4/D5 必须已填写齐备；涉安全客诉必须有客户确认",
                "校验通过后客诉状态变为已关闭"
            },
            Tip = "被拒绝关闭时提示会告诉你缺哪一步，照提示补齐即可。",
            SeeAlso = new[] { "c.8d", "c.confirm" }
        },
        new ManualEntry
        {
            Id = "c.history", Category = "质量管理", Title = "查客诉的流转履历",
            Aliases = new[] { "客诉谁改的", "客诉处理记录", "投诉履历", "操作痕迹" },
            Keywords = new[] { "履历", "记录", "时间轴", "留痕" },
            Summary = "按时间轴展示客诉每一次状态变更、8D 推进、冻结动作。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "流转履历", TargetButton = "",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」，点中客诉",
                "切到子页「流转履历」查看全部动作与时间"
            },
            SeeAlso = new[] { "c.create" }
        },
        new ManualEntry
        {
            Id = "c.export", Category = "质量管理", Title = "导出 8D 报告 / 客户统计",
            Aliases = new[] { "导出8d", "8d报告给客户", "客诉统计导出", "客诉报表" },
            Keywords = new[] { "导出", "8D", "报告", "统计", "客诉" },
            Summary = "导出含客诉台账、8D 内容、影响范围、统计的 Excel 工作簿。",
            TargetPage = "客户投诉与 8D", TargetSubPage = "", TargetButton = "导出 8D 报告",
            Steps = new[]
            {
                "进入「质量管理 › 客户投诉与 8D」",
                "点按钮「导出 8D 报告」",
                "选择保存位置，导出后可直接发客户或归档"
            },
            SeeAlso = new[] { "c.8d" }
        },

        // ============================================================
        // 仓储与出货
        // ============================================================
        new ManualEntry
        {
            Id = "wms.order", Category = "仓储与出货", Title = "新建仓储单据（入库 / 出库）",
            Aliases = new[] { "做入库单", "开出库单", "仓库单据", "收料", "发料" },
            Keywords = new[] { "入库", "出库", "单据", "仓储", "WMS" },
            Summary = "在 WMS 里创建入库单 / 出库单等仓储单据。",
            TargetPage = "WMS 仓储", TargetSubPage = "单据管理", TargetButton = "新建单据",
            Steps = new[]
            {
                "进入「仓储与出货 › WMS 仓储」，子页选「单据管理」",
                "点按钮「新建单据」，选择单据类型（入库 / 出库）",
                "填物料、批次、数量等信息后保存",
                "需要时点「打印单据」出纸质凭证"
            },
            SeeAlso = new[] { "wms.post", "wms.stock" }
        },
        new ManualEntry
        {
            Id = "wms.post", Category = "仓储与出货", Title = "仓储单据过账",
            Aliases = new[] { "过账", "确认入库", "确认出库", "账做实" },
            Keywords = new[] { "过账", "确认", "记账", "库存" },
            Summary = "过账后单据才真正影响库存数量，是「单据生效」的动作。",
            TargetPage = "WMS 仓储", TargetSubPage = "单据管理", TargetButton = "过账",
            Steps = new[]
            {
                "进入「仓储与出货 › WMS 仓储」，子页选「单据管理」",
                "点中要过账的单据",
                "核对明细后点按钮「过账」",
                "库存台账上该物料的在库数量随之增减"
            },
            Tip = "未过账的单据不影响库存；发现填错要在过账前改。",
            SeeAlso = new[] { "wms.order", "wms.stock" }
        },
        new ManualEntry
        {
            Id = "wms.stock", Category = "仓储与出货", Title = "查库存台账（含 ERP 库存与差异）",
            Aliases = new[] { "查库存", "还有多少料", "库存多少", "mes和erp对不上", "库存差异" },
            Keywords = new[] { "库存", "台账", "ERP", "差异", "在库", "数量" },
            Summary = "库存台账列出各物料的在库数量，并对比 ERP 库存、标出差异。",
            TargetPage = "WMS 仓储", TargetSubPage = "库存台账", TargetButton = "",
            Steps = new[]
            {
                "进入「仓储与出货 › WMS 仓储」，子页选「库存台账」",
                "查看「MES 库存」「ERP 库存」两列及「差异」列",
                "差异不为 0 的物料说明 MES 与 ERP 账不一致，需要核对单据是否都过账了"
            },
            SeeAlso = new[] { "wms.post" }
        },
        new ManualEntry
        {
            Id = "wms.expiry", Category = "仓储与出货", Title = "物料效期预警与 FIFO 先进先出",
            Aliases = new[] { "这个料过期了吗", "有效期", "还能用吗", "先进先出", "fifo", "电解液过期" },
            Keywords = new[] { "效期", "有效期", "过期", "FIFO", "先进先出", "批次" },
            Summary = "按批次展示物料效期状态并给出 FIFO 推荐的取用顺序，防止用超期料。",
            TargetPage = "WMS 仓储", TargetSubPage = "效期与 FIFO", TargetButton = "",
            Steps = new[]
            {
                "进入「仓储与出货 › WMS 仓储」，子页选「效期与 FIFO」",
                "列表按效期紧迫程度排序，临期/超期批次会高亮",
                "发料前按 FIFO 建议优先取最早入库的批次"
            },
            Tip = "临期物料会进入预警中心的「库存效期」来源，提前提醒。",
            SeeAlso = new[] { "ov.alert", "wms.stock" }
        },
        new ManualEntry
        {
            Id = "wms.inspect", Category = "仓储与出货", Title = "入库检（来料检验）",
            Aliases = new[] { "来料检验", "入库检", "进料检验", "iqc" },
            Keywords = new[] { "入库检", "来料", "检验", "IQC", "收货" },
            Summary = "物料到货后做入库检验并记录结论。",
            TargetPage = "WMS 仓储", TargetSubPage = "单据明细 › 入库检", TargetButton = "载入/刷新入库检",
            Steps = new[]
            {
                "进入「仓储与出货 › WMS 仓储」，子页选「单据管理」，选中入库单",
                "在下半部分切到子页「入库检」",
                "点按钮「载入/刷新入库检」把待检项拉进来",
                "填写检验结果后点按钮「保存入库检」"
            },
            SeeAlso = new[] { "wms.order", "q.abnormalNew" }
        },
        new ManualEntry
        {
            Id = "wms.print", Category = "仓储与出货", Title = "打印仓储单据",
            Aliases = new[] { "打出入库单", "打印单据", "仓库单子打出来" },
            Keywords = new[] { "打印", "单据", "入库单", "出库单" },
            Summary = "生成仓储单据的打印文本。",
            TargetPage = "WMS 仓储", TargetSubPage = "单据管理", TargetButton = "打印单据",
            Steps = new[]
            {
                "进入「仓储与出货 › WMS 仓储」，子页选「单据管理」",
                "点中要打印的单据",
                "点按钮「打印单据」"
            },
            SeeAlso = new[] { "wms.order" }
        },
        new ManualEntry
        {
            Id = "ship.create", Category = "仓储与出货", Title = "创建出库单并绑定电芯（发货）",
            Aliases = new[] { "发货", "出货", "做出库单", "卖出去", "发运", "给客户发货" },
            Keywords = new[] { "出库单", "出货", "发货", "绑定", "电芯", "SN" },
            Summary = "创建成品出库单并绑定电芯 SN，建立「这一批货包含哪些电芯」的记录。",
            TargetPage = "出货与召回", TargetSubPage = "新建出库单", TargetButton = "创建出库单并绑定",
            Steps = new[]
            {
                "进入「仓储与出货 › 出货与召回」，子页选「新建出库单」",
                "选择客户、填写出库单信息",
                "从可选电芯里挑选要发的电芯（只有生产完成且合格的电芯才允许出库）",
                "点按钮「创建出库单并绑定」完成出库"
            },
            Tip = "化成中、检测中、不合格的电芯不允许出库——这是系统的硬性校验。",
            SeeAlso = new[] { "ship.bind", "ship.recall" }
        },
        new ManualEntry
        {
            Id = "ship.bind", Category = "仓储与出货", Title = "出货绑定 / 解除绑定电芯",
            Aliases = new[] { "绑定电芯", "解除绑定", "这单发了哪些电芯", "出货明细" },
            Keywords = new[] { "绑定", "解绑", "电芯", "出货单", "明细" },
            Summary = "对已有出货单补充绑定电芯，或解除误绑的电芯。",
            TargetPage = "出货与召回", TargetSubPage = "出货绑定", TargetButton = "绑定电芯…",
            Steps = new[]
            {
                "进入「仓储与出货 › 出货与召回」，子页选「出货绑定」",
                "在左侧选中一张出货单",
                "点按钮「绑定电芯…」选择电芯加入；误绑时选中后点按钮「解除绑定」"
            },
            Tip = "绑定关系是召回反查的数据基础——没有绑定就查不出货发给了谁。",
            SeeAlso = new[] { "ship.create", "ship.recall" }
        },
        new ManualEntry
        {
            Id = "ship.recall", Category = "仓储与出货", Title = "召回反查：查某批次 / 电芯发给了哪些客户",
            Aliases = new[] { "召回", "反查", "这批货发给谁了", "出问题的货流到哪了", "追回", "要撤回", "客户那边有没有" },
            Keywords = new[] { "召回", "反查", "追溯", "客户", "批次", "流向" },
            Summary = "以批次 / SN / 出货单为起点，反查全部流向：发给了哪些客户、涉及哪些电芯。",
            TargetPage = "出货与召回", TargetSubPage = "召回反查", TargetButton = "",
            Steps = new[]
            {
                "进入「仓储与出货 › 出货与召回」，子页选「召回反查」",
                "选择反查维度（按电芯 SN / 按材料批次 / 按出货单）并输入关键字",
                "点查询，下方三个子表分别列出：出货记录、受影响电芯、受影响材料批次"
            },
            Tip = "必须先有出货绑定数据才能反查；发现同批材料流出到多个客户时，可用「一键冻结影响电芯」先行拦截。",
            SeeAlso = new[] { "ship.freeze", "ship.export", "c.impact" }
        },
        new ManualEntry
        {
            Id = "ship.freeze", Category = "仓储与出货", Title = "一键冻结召回影响电芯",
            Aliases = new[] { "冻结召回", "把问题货锁住", "拦住风险品" },
            Keywords = new[] { "冻结", "召回", "保留", "影响电芯" },
            Summary = "把召回反查出的影响电芯一键冻结，自动生成保留单。",
            TargetPage = "出货与召回", TargetSubPage = "召回反查", TargetButton = "一键冻结影响电芯",
            Steps = new[]
            {
                "先执行「召回反查」并确认影响范围",
                "点按钮「一键冻结影响电芯」并确认",
                "系统生成保留单，可在「保留管理」中查看与后续处置"
            },
            SeeAlso = new[] { "ship.recall", "q.hold" }
        },
        new ManualEntry
        {
            Id = "ship.export", Category = "仓储与出货", Title = "导出召回报告 / 复制清单",
            Aliases = new[] { "导出召回", "召回清单", "复制影响清单" },
            Keywords = new[] { "导出", "召回", "报告", "复制", "清单" },
            Summary = "把召回反查结果导出为 Excel，或直接复制清单发给销售/客户。",
            TargetPage = "出货与召回", TargetSubPage = "召回反查", TargetButton = "导出报告",
            Steps = new[]
            {
                "在「召回反查」子页完成反查",
                "需要文件 → 点按钮「导出报告」；只想要文本 → 点按钮「复制清单」"
            },
            SeeAlso = new[] { "ship.recall" }
        },
        new ManualEntry
        {
            Id = "mat.trace", Category = "仓储与出货", Title = "按料号 / 批次反查用到了哪些电芯",
            Aliases = new[] { "料出问题影响哪些电芯", "批次反查", "资材追溯", "这批料做成哪些电池" },
            Keywords = new[] { "资材", "追溯", "料号", "批次", "反查", "电芯" },
            Summary = "以原材料批次为起点，反查这批料被做进了哪些电芯，用于供应商来料问题的追责与拦截。",
            TargetPage = "资材追溯", TargetSubPage = "", TargetButton = "查询",
            Steps = new[]
            {
                "进入「仓储与出货 › 资材追溯」",
                "在关键字框输入料号或批次号",
                "点按钮「查询」，下方列出受影响的电芯候选"
            },
            Tip = "这是材料供应商索赔时最有用的证据链。",
            SeeAlso = new[] { "mat.hold", "ship.recall" }
        },
        new ManualEntry
        {
            Id = "mat.hold", Category = "仓储与出货", Title = "对反查出的电芯批量执行保留",
            Aliases = new[] { "批量保留", "全选保留", "把这些电芯都锁住" },
            Keywords = new[] { "保留", "批量", "全选", "资材", "执行" },
            Summary = "把资材反查出的候选电芯批量锁入保留区。",
            TargetPage = "资材追溯", TargetSubPage = "", TargetButton = "执行保留",
            Steps = new[]
            {
                "进入「仓储与出货 › 资材追溯」，先查询出候选电芯",
                "点按钮「全选」全选（或手工勾选部分）",
                "点按钮「执行保留」，系统生成保留单",
                "勾错了点「取消全选」重来"
            },
            SeeAlso = new[] { "mat.trace", "q.hold" }
        },

        // ============================================================
        // 设备与人员
        // ============================================================
        new ManualEntry
        {
            Id = "eq.monitor", Category = "设备与人员", Title = "查看设备状态",
            Aliases = new[] { "设备状态", "机器在跑吗", "设备列表", "哪台机停了", "设备开动率" },
            Keywords = new[] { "设备", "状态", "运行", "停机", "监控" },
            Summary = "列出全部设备及其运行状态，用于快速判断产线可用性。",
            TargetPage = "设备监控", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "进入「设备与人员 › 设备监控」",
                "查看设备列表及其状态列（运行 / 停机 / 维保等）"
            },
            SeeAlso = new[] { "eq.tpm", "ov.dashboard" }
        },
        new ManualEntry
        {
            Id = "eq.tpm", Category = "设备与人员", Title = "执行点检 / 保养",
            Aliases = new[] { "点检", "保养", "设备维护", "日常点检", "tpm", "设备巡检" },
            Keywords = new[] { "点检", "保养", "TPM", "维护", "巡检", "计划" },
            Summary = "按点检/保养计划执行并留痕，未按期执行会进入预警中心。",
            TargetPage = "点检保养", TargetSubPage = "点检/保养计划", TargetButton = "执行点检",
            Steps = new[]
            {
                "进入「设备与人员 › 点检保养」，子页选「点检/保养计划」",
                "在列表里点中到期的计划项",
                "点按钮「执行点检」，逐项确认结果并提交",
                "执行记录可在子页「执行记录」里查历史"
            },
            Tip = "到期未执行的计划会出现在预警中心的「点检保养」来源下。",
            SeeAlso = new[] { "eq.spare", "ov.alert" }
        },
        new ManualEntry
        {
            Id = "eq.spare", Category = "设备与人员", Title = "备件领用 / 查看备件台账",
            Aliases = new[] { "备件", "领备件", "配件还有吗", "备件库存", "换零件" },
            Keywords = new[] { "备件", "领用", "台账", "库存", "安全库存" },
            Summary = "查看备件库存并办理领用，低于安全库存会进入预警中心。",
            TargetPage = "点检保养", TargetSubPage = "备件台账", TargetButton = "领用备件",
            Steps = new[]
            {
                "进入「设备与人员 › 点检保养」，子页选「备件台账」",
                "查看各备件在库数量与安全库存线",
                "点中要领用的备件，点按钮「领用备件」并填领用数量与用途"
            },
            SeeAlso = new[] { "eq.tpm" }
        },
        new ManualEntry
        {
            Id = "hr.addUser", Category = "设备与人员", Title = "登记新员工 / 人员档案",
            Aliases = new[] { "加人", "新人入职", "录入员工", "人员档案", "登记人员" },
            Keywords = new[] { "人员", "员工", "登记", "入职", "档案" },
            Summary = "把新员工录入人员台账，之后才能给他登记资质证书。",
            TargetPage = "人员与资质", TargetSubPage = "人员台账", TargetButton = "登记人员",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「人员台账」",
                "点按钮「登记人员」",
                "填写工号、姓名、部门、岗位、班组、状态、入职日期等信息后保存"
            },
            Tip = "工号是唯一标识，资质与实绩都挂在这个工号上。",
            SeeAlso = new[] { "hr.addCert", "hr.editUser" }
        },
        new ManualEntry
        {
            Id = "hr.editUser", Category = "设备与人员", Title = "修改人员信息 / 办理离职",
            Aliases = new[] { "改人员信息", "人走了", "离职", "换岗位", "调岗" },
            Keywords = new[] { "编辑", "修改", "人员", "离职", "调岗", "状态" },
            Summary = "修改人员岗位、班组、状态等信息；设为离职后系统会禁止其上岗。",
            TargetPage = "人员与资质", TargetSubPage = "人员台账", TargetButton = "编辑人员",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「人员台账」",
                "点中要修改的人员",
                "点按钮「编辑人员」，改完后保存"
            },
            Tip = "人员离职后，即使他的证书还在有效期内，上岗校验也会直接拦截——离职优先于证书状态。",
            SeeAlso = new[] { "hr.check", "hr.addUser" }
        },
        new ManualEntry
        {
            Id = "hr.addCert", Category = "设备与人员", Title = "登记资质证书 / 复审换证",
            Aliases = new[] { "登记证书", "新证书", "复审换证", "换证", "证书续期", "发证" },
            Keywords = new[] { "资质", "证书", "登记", "复审", "换证", "有效期" },
            Summary = "给人员登记资质证书并设有效期；同一资质换证时在原有记录上恢复，不会产生重复。",
            TargetPage = "人员与资质", TargetSubPage = "人员台账", TargetButton = "登记资质",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「人员台账」，点中人员",
                "点按钮「登记资质」",
                "选择资质项（如「涂布」作业证）、证书编号、发证日期、有效期至",
                "保存后该证书出现在资质矩阵里"
            },
            Tip = "有效期剩余不足 30 天会标为「即将到期」并给出警告提示，但仍允许上岗；过期才拦截。",
            SeeAlso = new[] { "hr.matrix", "hr.revoke", "hr.check" }
        },
        new ManualEntry
        {
            Id = "hr.revoke", Category = "设备与人员", Title = "吊销 / 恢复资质证书",
            Aliases = new[] { "吊销证书", "取消资格", "证被收了", "恢复证书", "暂停资质" },
            Keywords = new[] { "吊销", "恢复", "资质", "证书", "取消" },
            Summary = "违规或考核不通过时吊销证书；整改合格后可恢复。",
            TargetPage = "人员与资质", TargetSubPage = "人员台账", TargetButton = "吊销资质",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「人员台账」，点中人员与要处理的证书",
                "点按钮「吊销资质」并确认",
                "吊销后该证书状态为「已吊销」，此人无法再上对应工序的岗",
                "整改完成后点「恢复证书」恢复其上岗资格"
            },
            SeeAlso = new[] { "hr.check", "hr.addCert" }
        },
        new ManualEntry
        {
            Id = "hr.matrix", Category = "设备与人员", Title = "看某个人 / 某个工序有哪些资质（资质矩阵）",
            Aliases = new[] { "谁会干这个", "谁有证", "资质矩阵", "某个人有什么证", "持证情况" },
            Keywords = new[] { "矩阵", "资质", "人员", "持证", "对照表" },
            Summary = "人员 × 资质 的二维矩阵，四色标注有效 / 即将到期 / 已过期 / 已吊销。",
            TargetPage = "人员与资质", TargetSubPage = "资质矩阵", TargetButton = "",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「资质矩阵」",
                "行是人员、列是 12 项资质，颜色含义看顶部图例",
                "想找「谁能干涂布」，就顺着「涂布」这一列往下看绿色的行"
            },
            SeeAlso = new[] { "hr.check", "hr.addCert" }
        },
        new ManualEntry
        {
            Id = "hr.check", Category = "设备与人员", Title = "校验某人能不能上这个岗（无资质拦截）",
            Aliases = new[] { "他能不能干这个", "上岗校验", "有没有资格", "无证上岗", "能不能操作这台设备" },
            Keywords = new[] { "上岗", "校验", "资质", "拦截", "防错", "资格" },
            Summary = "核心防错规则：无证、证过期、证被吊销、人员已离职，一律禁止上岗。",
            TargetPage = "人员与资质", TargetSubPage = "上岗资质核查", TargetButton = "上岗校验",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「上岗资质核查」",
                "选择人员与工序",
                "点按钮「上岗校验」",
                "通过会提示可上岗；不通过会明确告诉你原因（无证 / 已过期 / 已吊销 / 已离职）"
            },
            Tip = "校验规则有优先级：先看工序是否需要持证，再看人员是否存在、是否离职，最后才看证书状态。",
            SeeAlso = new[] { "hr.matrix", "hr.addCert", "hr.revoke" }
        },
        new ManualEntry
        {
            Id = "hr.checkHistory", Category = "设备与人员", Title = "回查历史实绩的资质合规性",
            Aliases = new[] { "以前的操作合规吗", "历史违规", "实绩核查", "谁无证干过活" },
            Keywords = new[] { "核查", "实绩", "历史", "合规", "追溯" },
            Summary = "对历史工序实绩逐条回查操作人资质，输出通过/不通过及原因。",
            TargetPage = "人员与资质", TargetSubPage = "上岗资质核查", TargetButton = "",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「上岗资质核查」",
                "下方表格列出历史实绩的核查结果",
                "不通过的记录会标明具体人与原因，可据此追溯责任"
            },
            SeeAlso = new[] { "hr.check", "hr.crew" }
        },
        new ManualEntry
        {
            Id = "hr.crew", Category = "设备与人员", Title = "看班组绩效",
            Aliases = new[] { "班组表现", "哪个班好", "班组产量", "班组合格率" },
            Keywords = new[] { "班组", "绩效", "产量", "合格率", "持证率" },
            Summary = "按班组统计产量、合格率、资质覆盖率与持证人数。",
            TargetPage = "人员与资质", TargetSubPage = "班组绩效", TargetButton = "",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」，子页选「班组绩效」",
                "查看各班组的产量、合格率、持证覆盖率对比"
            },
            SeeAlso = new[] { "hr.matrix" }
        },
        new ManualEntry
        {
            Id = "hr.export", Category = "设备与人员", Title = "导出人员资质台账",
            Aliases = new[] { "导出资质表", "人员台账导出", "证书清单" },
            Keywords = new[] { "导出", "人员", "资质", "台账", "excel" },
            Summary = "导出含人员、证书、资质矩阵等内容的 Excel 工作簿。",
            TargetPage = "人员与资质", TargetSubPage = "", TargetButton = "导出人员资质",
            Steps = new[]
            {
                "进入「设备与人员 › 人员与资质」",
                "点按钮「导出人员资质」，选择保存位置"
            },
            SeeAlso = new[] { "hr.matrix" }
        },

        // ============================================================
        // 追溯与报表
        // ============================================================
        new ManualEntry
        {
            Id = "trace.sn", Category = "追溯与报表", Title = "查一只电芯的完整追溯链",
            Aliases = new[] { "追溯", "查电芯", "这个电池怎么做的", "sn查询", "完整性追溯", "这只电池的历史" },
            Keywords = new[] { "追溯", "SN", "电芯", "批次", "工序", "参数", "用料" },
            Summary = "输入电芯 SN，展示它的用料批次、各工序参数实绩与测试结果——追溯的核心页面。",
            TargetPage = "SN 追溯", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "进入「追溯与报表 › SN 追溯」",
                "在 SN 输入框填入电芯序列号（示例：SC-A260812-L1-00001）",
                "执行查询，页面自上而下展示：电芯信息、环境条件、用料明细、工序实绩、测试结果",
                "想留档就点「导出报告」"
            },
            Tip = "在「测试数据」页双击某行的电芯 SN，也能直接跳到这里并自动查询。",
            SeeAlso = new[] { "trace.export", "trace.test" }
        },
        new ManualEntry
        {
            Id = "trace.export", Category = "追溯与报表", Title = "导出追溯报告",
            Aliases = new[] { "追溯报告导出", "客户要追溯材料", "出一份追溯证明" },
            Keywords = new[] { "导出", "追溯", "报告", "客户" },
            Summary = "把当前 SN 的完整追溯信息导出为 Excel，可直接作为对客户的追溯证明材料。",
            TargetPage = "SN 追溯", TargetSubPage = "", TargetButton = "导出报告",
            Steps = new[]
            {
                "在「SN 追溯」页先查出目标电芯",
                "点按钮「导出报告」，选择保存位置"
            },
            SeeAlso = new[] { "trace.sn" }
        },
        new ManualEntry
        {
            Id = "trace.test", Category = "追溯与报表", Title = "查电性能测试数据",
            Aliases = new[] { "测试数据", "电性能", "容量内阻", "全检数据", "测试记录" },
            Keywords = new[] { "测试", "电性能", "数据", "检验", "记录" },
            Summary = "电性能全检全量数据的清单，任意一行可双击跳转到该电芯的追溯页。",
            TargetPage = "测试数据", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "进入「追溯与报表 › 测试数据」",
                "按列查看各电芯的测试参数与结论",
                "双击某行的「电芯SN」列，自动跳到 SN 追溯页并查询该电芯"
            },
            SeeAlso = new[] { "trace.sn" }
        },
        new ManualEntry
        {
            Id = "rep.daily", Category = "追溯与报表", Title = "看产量日报 / 月报",
            Aliases = new[] { "产量日报", "昨天做了多少", "月产量", "产量统计", "报表在哪" },
            Keywords = new[] { "报表", "产量", "日报", "月报", "统计" },
            Summary = "报表中心按子页组织各类报表：产量日报、产量月报、良率趋势、异常 TOP、OEE 与库存。",
            TargetPage = "报表中心", TargetSubPage = "产量日报", TargetButton = "",
            Steps = new[]
            {
                "进入「追溯与报表 › 报表中心」",
                "子页选「产量日报」看按日汇总的产出",
                "切到「产量月报」看按月汇总"
            },
            SeeAlso = new[] { "rep.yield", "rep.export" }
        },
        new ManualEntry
        {
            Id = "rep.yield", Category = "追溯与报表", Title = "看良率趋势 / 异常 TOP",
            Aliases = new[] { "良率趋势", "良率走势", "异常排行", "问题最多的", "top异常" },
            Keywords = new[] { "良率", "趋势", "异常", "TOP", "排名", "走势" },
            Summary = "良率趋势看质量走势；异常 TOP 看哪类问题最多、该先解决什么。",
            TargetPage = "报表中心", TargetSubPage = "良率趋势", TargetButton = "",
            Steps = new[]
            {
                "进入「追溯与报表 › 报表中心」",
                "子页选「良率趋势」观察质量随时间的变化",
                "子页选「异常 TOP」看各类异常的发生次数排名"
            },
            SeeAlso = new[] { "rep.daily", "rep.oee" }
        },
        new ManualEntry
        {
            Id = "rep.oee", Category = "追溯与报表", Title = "看 OEE 与库存周转",
            Aliases = new[] { "oee", "设备综合效率", "库存周转", "设备利用率" },
            Keywords = new[] { "OEE", "设备效率", "库存", "周转", "汇总" },
            Summary = "设备综合效率与库存周转汇总，用于评估产能利用与库存健康度。",
            TargetPage = "报表中心", TargetSubPage = "OEE 与库存", TargetButton = "",
            Steps = new[]
            {
                "进入「追溯与报表 › 报表中心」",
                "子页选「OEE 与库存」查看设备效率与库存周转指标"
            },
            SeeAlso = new[] { "rep.daily" }
        },
        new ManualEntry
        {
            Id = "rep.export", Category = "追溯与报表", Title = "导出报表",
            Aliases = new[] { "报表导出", "把报表发我", "导出月度报表", "报表excel" },
            Keywords = new[] { "导出", "报表", "excel", "xlsx" },
            Summary = "把报表中心的全部报表导出为一个 Excel 工作簿（多工作表）。",
            TargetPage = "报表中心", TargetSubPage = "", TargetButton = "导出报表",
            Steps = new[]
            {
                "进入「追溯与报表 › 报表中心」",
                "点按钮「导出报表」，选择保存位置",
                "导出成功后提示工作表数量与数据行数"
            },
            SeeAlso = new[] { "rep.daily", "rep.yield", "rep.oee" }
        },
        };

        // 编写顺序即业务主次顺序（总览 → 生产 → 质量 → 仓储 → 设备人员 → 追溯报表 → 帮助），
        // 赋序号供搜索结果同分时排序使用，避免退化成按分类名字母序。
        for (int i = 0; i < list.Count; i++) list[i].Order = i;
        return list;
    }
}
