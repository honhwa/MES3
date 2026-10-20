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
        new ManualEntry
        {
            Id = "ai.backend", Category = "总览", Title = "配置 AI 助手后端（接云端大模型）",
            Aliases = new[] { "接大模型", "配置API", "填密钥", "deepseek", "用大模型回答", "AI后端在哪设置", "怎么接人工智能" },
            Keywords = new[] { "AI", "后端", "大模型", "API", "Key", "密钥", "deepseek", "云端", "设置", "模型" },
            Summary = "在「AI 助手」页点「后端设置」，可启用云端大模型（默认关闭）。启用后数字仍由本地仓储算出，模型只负责把结果组织成人话。",
            TargetPage = "AI 助手", TargetSubPage = "", TargetButton = "后端设置",
            Steps = new[]
            {
                "切到「总览 › AI 助手」页",
                "点右上角「后端设置」",
                "勾选「启用云端大模型」",
                "填服务地址（默认 https://api.deepseek.com）与模型（默认 deepseek-chat）",
                "填 API Key（也可改用环境变量 DEEPSEEK_API_KEY，Key 就不落盘）",
                "点「测试连通性」确认能通",
                "点「保存」——顶栏会显示当前后端与数据发往的域名"
            },
            Tip = "默认是关闭的：不勾选启用，全部计算在本机完成，数据不出门。"
                  + "开启后发送的只是【关键数据】里的汇总数字，不含客户名、供应商名与原始明细行；"
                  + "模型返回后系统会逐个数字核对，出现未授权的数字就整段丢弃、退回本地原文。"
                  + "断网或超时会自动降级到本地规则引擎，页面不会报错。",
            SeeAlso = new[] { "ai.ask", "ai.explain" }
        },

        // ============================================================
        // 生产管理 —— 工单
        // ============================================================
        new ManualEntry
        {
            Id = "scheduling", Category = "生产管理", Title = "智能排产（三级选线 → 投产数 / 线体 / 工期 / 六维评分）",
            Aliases = new[] { "智能排产", "排产建议", "帮我排产", "AI排产", "自动排产", "算排产", "排下产", "投产多少", "哪条线做", "缺料了吗", "物料够不够", "齐套吗", "同型号优先", "六维评分", "换型指导" },
            Keywords = new[] { "排产", "智能排产", "投产数量", "开工", "完工", "线体", "交期", "齐套", "缺料", "备料", "历史工单", "同型号", "六维", "换型", "硬约束" },
            Summary = "选一张待下发的 ERP 订单，系统按三级选线（同型号在制优先 → 六维工艺相似度评分 → 负载兜底）推荐线体，"
                      + "推出「投产多少只、排哪条线、几号开工几号完工」，并给出换型指导书；同时按 BOM 展开物料逐项比对库存，缺料直接点名并给补料建议。",
            TargetPage = "智能排产", TargetSubPage = "", TargetButton = "⚡ 生成排产建议",
            Steps = new[]
            {
                "进入「生产管理 › 智能排产」",
                "顶部「ERP 订单」下拉里选一张待下发订单（只列未生成过工单的）",
                "点「⚡ 生成排产建议」（换订单会自动重算）",
                "看左侧四张卡：推荐线体 / 投产数量 / 预计完工 / 交期余量",
                "展开左下「排产决策」页：看三级命中（同型号在制 / 六维评分）、三项硬约束校验、各线六维评分表与换型指导书",
                "看右侧「齐套校验」表：逐物料的需求量、可用量、缺口与处置建议",
                "展开左下「推理依据」可逐条追问——样本是哪几张工单、公式怎么算的",
                "确认无误后点「✅ 按建议生成工单」（工单号 = WO-订单号）"
            },
            Tip = "口径说明：① 可用库存不含待检区与退货待处理区（物料未判定合格不能投产）；"
                  + "② 1 个生产日按 16h 有效产能折算，未含周末与节假日，正式排程需接工作日历；"
                  + "③ 历史样本不足 3 张时，良率与节拍会采用默认值并在依据里明示「样本不足」，不会假装有依据；"
                  + "④ 缺料或交期来不及时会先弹预警，要你确认后才建工单——不静默放行，也不硬拦死；"
                  + "⑤ 选线口径（技术方案 2.1/2.2）：三级——同型号在制线体优先（含设备/工装/人员三硬约束，产能不足可拆分）→ 六维工艺相似度评分（工序重合35/参数兼容20/换型成本20/工装通用15/人员技能5/FTT5）→ 负载兜底；线体能力矩阵与换型时间为演示种子，可替换为真实能力矩阵；"
                  + "⑥ 时间线口径（与「插单模拟」完全同源）：新单排在队尾 = 队尾完工 + 换型（队尾型号→本型号）+ 本单工时÷16h；在制工单之间也计换型。"
                  + "「排产决策」页会同时给出「插队对照」（若允许老单顺延，最早何时能交、可提前几天），这就是插单页方案一的数字来源。",
            SeeAlso = new[] { "wo.create", "stock.kitting", "wo.erpPull" }
        },
        new ManualEntry
        {
            Id = "scheduling.gantt", Category = "生产管理", Title = "排产甘特图（待下发订单建议排期一览）",
            Aliases = new[] { "甘特图", "排产甘特", "排期图", "产线占用", "排产可视化", "订单排期", "哪条线什么时候做什么", "排程图" },
            Keywords = new[] { "甘特图", "排产", "排期", "产线", "占用", "可视化", "待下发", "交期", "L1", "L2", "L3" },
            Summary = "把待下发 ERP 订单的排产建议画成甘特图：纵轴产线、横轴日期，一眼看出各线未来被哪些订单占用、哪里赶不及交期。",
            TargetPage = "排产甘特图", TargetSubPage = "", TargetButton = "",
            Steps = new[]
            {
                "进入「生产管理 › 排产甘特图」",
                "横轴是日期（金色表头 = 今天），纵轴是 L1/L2/L3 产线",
                "每个色块是一张待下发订单的建议排期（绿=有余量、黄=偏紧、红=赶不及交期）",
                "点任意色块看该产线当天的订单明细（型号 / 数量 / 开工完工 / 交期余量）",
                "用「时间窗口」下拉切 14 / 30 / 60 天"
            },
            Tip = "口径：同线订单按交期顺序错开排布（批量排产近似：保持各单推荐线与工期，起点接该线在制忙完时间）；赶不及交期标红。甘特展示的是排产建议，非已下发工单。",
            SeeAlso = new[] { "scheduling", "wo.erpPull" }
        },
        new ManualEntry
        {
            Id = "urgent.insert", Category = "生产管理", Title = "紧急插单模拟（三方案 What-if + 评分推荐）",
            Aliases = new[] { "紧急插单", "插单", "插急单", "加急", "插单模拟", "冲突重排", "急单怎么办", "插单影响" },
            Keywords = new[] { "紧急", "插单", "加急", "重排", "冲突", "方案", "评分", "老单", "换型", "交期" },
            Summary = "选一张紧急订单，模拟「老单延后 / 拆分插空 / 转次优线」三种局部重排方案，按准时率/老单影响/换型成本/负载均衡四维评分推荐最优。",
            TargetPage = "插单模拟", TargetSubPage = "", TargetButton = "🧩 模拟插单",
            Steps = new[]
            {
                "进入「生产管理 › 插单模拟」",
                "在「紧急订单」下拉选一张待下发订单",
                "点「🧩 模拟插单」，说明栏先给出「基线」（= 智能排产的排队结论），再列三张方案卡（含推荐标与「比排队提前几天」）",
                "点任一方案卡，下方显示受影响的老工单（原完工 vs 插单后完工、顺延量、交期影响）",
                "对比综合得分与优缺点后，据此人工确认排程"
            },
            Tip = "口径（2026-10-20 与智能排产统一）：顶部「基线」= 把紧急单排到线体队尾，数字与「智能排产」的预计完工完全一致（同一条时间线内核）；"
                  + "三方案只是在同一条时间线上换插入位置——队首插入（最早完工、老单顺延）/ 分段插空（搜索老单总顺延最小的插入点）/ 转次优线队尾（对主线零影响但要排队）。"
                  + "评分：准时40% > 老单影响30%（按顺延量，老单自身历史超期不计入）> 换型20% > 负载10%；「应用建议」为演示，不改真实排程。",
            SeeAlso = new[] { "scheduling", "scheduling.gantt" }
        },
        new ManualEntry
        {
            Id = "prod.ctp", Category = "生产管理", Title = "交期承诺 CTP（这个型号多少只，最快哪天能交）",
            Aliases = new[] { "最快什么时候能交", "能不能按期交货", "交期承诺", "ctp", "客户催交期", "多久能做完", "部分交货" },
            Keywords = new[] { "交期", "承诺", "CTP", "可交日", "换型", "排队", "物料就绪", "部分承诺", "瓶颈" },
            Summary = "输入型号、数量、期望交期，按「换型 + 排队 + 生产 + 物料」四段逐段推算各线最早可交日，给出可承诺 / 有条件承诺 / 不可承诺三档结论。",
            TargetPage = "交期承诺 CTP", TargetSubPage = "交期承诺", TargetButton = "⚡ 评估交期",
            Steps = new[]
            {
                "进入「生产管理 › 交期承诺 CTP」子页「交期承诺」",
                "选询单型号、填数量与期望交期，点「⚡ 评估交期」",
                "看上方结论：可承诺（给出承诺日）／有条件承诺（能先交多少、余量什么时候补）／不可承诺（最早可交日）",
                "看线体对比表：各线换型分钟、排队腾出、开工、物料就绪、最早可交、瓶颈",
                "选中某条线，下方显示它逐段推算依据（可逐条核对）",
                "点「换型矩阵」子页可看型号两两换型时间，并生成当天最省换型的生产顺序"
            },
            Tip = "可交日 = max(排队 + 换型 + 生产, 物料到料 + 生产)——不是「完工或到料取晚」，料到了还要再生产。整单赶不上时会算「期望交期前能交多少」，够 30% 就给有条件承诺，避免一刀切拒绝丢单。",
            SeeAlso = new[] { "scheduling", "urgent.insert", "mat.chain" }
        },
        new ManualEntry
        {
            Id = "stock.kitting", Category = "生产管理", Title = "建工单时自动齐套检查（BOM 需求 vs 现有库存）",
            Aliases = new[] { "料够不够", "缺料", "齐套检查", "物料齐套吗", "建单查缺料", "备料够吗", "这个型号能开工吗" },
            Keywords = new[] { "齐套", "缺料", "物料", "备料", "库存", "开工", "BOM", "投产" },
            Summary = "新建 / 修改工单时，填完产品型号与计划数量即自动展开该型号 BOM，逐项算出需求量并与现有库存比对，"
                      + "缺料当场列在表里；保存时缺料会弹预警让你确认。",
            TargetPage = "工单管理", TargetSubPage = "", TargetButton = "＋ 新建工单",
            Steps = new[]
            {
                "进入「生产管理 › 工单管理」，点「＋ 新建工单」",
                "填「生产型号」与「计划数」——下方「齐套检查」区会立即自动重算",
                "看汇总行：齐套通过 / 缺 N 项 / 紧张 N 项（紧张 = 够用但没有缓冲）",
                "明细表逐项给出需求量、可用量、缺口、状态与处置建议",
                "确认无误点「保存」；若缺料，会先弹「缺料预警」，选择「否」即中止保存"
            },
            Tip = "口径：可用库存不含待检区与退货待处理区；需求 = 单位用量 × (1 + 损耗率%) × 计划数。"
                  + "「紧张」表示库存刚好够用、没有缓冲，来料一旦有波动就会变成缺料。",
            SeeAlso = new[] { "scheduling", "wo.create", "wo.poInfo" }
        },
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
        new ManualEntry
        {
            Id = "mat.chain", Category = "仓储与出货", Title = "缺料三级校验（什么时候必须下单 / 建议买多少）",
            Aliases = new[] { "缺料三级", "什么时候下单", "最晚下单日", "采购前置期", "L/T", "该买多少料", "自动建单", "缺料预警" },
            Keywords = new[] { "缺料", "三级校验", "需求合并", "可用", "在途", "安全库存", "净缺口", "前置期", "交期", "MPQ", "损耗", "自动建单" },
            Summary = "把缺料拆成三级算清楚，并按采购前置期倒推最晚下单日，避免「料不够但没人提前买」。",
            TargetPage = "缺料三级校验", TargetSubPage = "", TargetButton = "🔄 重算",
            Steps = new[]
            {
                "进入「仓储与出货 › 缺料三级校验」",
                "「校验范围」选全部未完工工单（跨工单合并）或某一张工单",
                "看「① 需求合并」= 几张工单共用多少料；「② 扣减」= 可用 / 在途 / 冻结 / 安全库存",
                "看「③ 净缺口」与「建议采购量」（已含 10% 损耗、MPQ 取整、超量线钳制）",
                "看「最晚下单 / 下单时点 / 距下单」：红色=已逾期、橙色=7 天内须下单",
                "需要下单就点「⚡ 按缺口自动建单」（按开工先后最多生成 3 张草稿），再到「资材采购申请」核对提交"
            },
            Tip = "红色「逾期」表示按标准前置期已赶不上开工，只能加急插单、让步接收或换料——这是要立刻上报的事，不是点一下就好。L/T、检验缓冲、MPQ 为演示参数，真实环境取自 ERP 供应商主数据。",
            SeeAlso = new[] { "mat.purchase", "stock.kitting", "scheduling" }
        },        new ManualEntry
        {
            Id = "mat.purchase", Category = "仓储与出货", Title = "资材采购申请（工单缺料 → 请购 → 审批 → 下单 → 完结入库）",
            Aliases = new[] { "请购", "采购申请", "缺料怎么买", "提交审批", "下单", "完结入库", "采购单" },
            Keywords = new[] { "采购申请", "请购单", "草稿", "待审批", "已批准", "已下单", "已完结", "供应商", "申请量", "在途" },
            Summary = "把工单缺料转成可流转、可追溯的请购单，一张申请单对应一张工单，走完审批后下单、收货完结入库。",
            TargetPage = "资材采购申请", TargetSubPage = "", TargetButton = "⚡ 按工单生成申请",
            Steps = new[]
            {
                "进入「仓储与出货 › 资材采购申请」，在「缺料工单」下拉选一张工单",
                "点「⚡ 按工单生成申请」→ 自动带出物料 / 需求量 / 可用量 / 缺口，生成草稿",
                "核对申请量（可按「🤖 AI 库存分析」参考建议量），改完点「提交」",
                "在「当前操作人」切到审批人，点「批准」或「驳回」（驳回必须填意见）",
                "点「下单」通知供应商；货到了点「完结入库」，采购量自动累加进 WMS 库存"
            },
            Tip = "同一工单已有未关闭申请单时不会重复生成；同物料已在在途（待审批/已批准/已下单）的申请量会从缺口里扣掉，避免重复请购。草稿可删（仅申请人本人或采购部主管），已流转的单据只能「关闭」留痕。",
            SeeAlso = new[] { "mat.chain", "wms.post", "wms.inspect" }
        },
        new ManualEntry
        {
            Id = "mat.age", Category = "仓储与出货", Title = "物料全链管控（临期三色 + 提前消耗 / 重检延期 / 报废三方案）",
            Aliases = new[] { "临期料怎么办", "快过期了", "物料过期", "要不要报废", "重检延期", "提前排产消耗", "分级审批", "保质期预警" },
            Keywords = new[] { "临期", "过期", "效期", "保质期", "预计用完日", "报废", "重检", "IQC", "延长效期", "分级审批", "审批金额", "兜底" },
            Summary = "对临期与已过期批次给出三条处置路径（提前排产消耗 / IQC 重检延期 / 报废），按金额分级审批并留下决策台账。",
            TargetPage = "物料全链管控", TargetSubPage = "", TargetButton = "🔄 重算",
            Steps = new[]
            {
                "进入「仓储与出货 › 物料全链管控」，看上方三色看板（等级 / 剩余效期 / 预计用完 / 到期前用不完 / 在库金额）",
                "先看「预计用完」和「到期前用不完」两列——剩余效期长但消耗慢的也会判临期，这才是真风险",
                "点一行批次，下「三方案评估」给出三条路的评分、金额、所需审批级别与推荐",
                "在「审批角色」选本次审批级别（班组长 ≤500 元 / 仓储主管 500~5000 元 / 生产经理 >5000 元），级别不够会被拒绝",
                "选中方案后点「✔ 执行选中方案」：提前消耗 = 登记优先消耗指令；重检 = 已过期走进料检延期、临期只登记待检；报废 = 写废弃台账并清零",
                "到「处置留痕」页看每次决策的记录与关联单据号（废弃单 / 重检单 / 优先消耗指令）"
            },
            Tip = "已过期超过 15 天仍未处置的批次只剩「兜底报废」一条路——这是防止越拖越没人管的硬规则。报废不可逆，执行前会二次确认；消耗速度用近 90 天实物领用测算，样本不足会标「不可估」，不要拿它当结论。",
            SeeAlso = new[] { "wms.expiry", "wms.inspect", "mat.chain" }
        },
        new ManualEntry
        {
            Id = "mat.strategy", Category = "仓储与出货", Title = "物料策略（替代料齐套试算 / 安全库存补货点）",
            Aliases = new[] { "替代料能不能用", "缺料顶替", "替代关系", "安全库存补货", "什么时候下单", "补货点", "ROP", "采购策略" },
            Keywords = new[] { "替代料", "替代关系", "用量系数", "客户认可", "齐套试算", "安全库存", "补货点", "ROP", "提前期", "MPQ", "补货申请" },
            Summary = "主料不够时用已认可的替代料顶缺口；同时按补货点（ROP）算出每个料什么时候该下单、下多少。",
            TargetPage = "物料策略", TargetSubPage = "替代料", TargetButton = "🔍 替代齐套试算",
            Steps = new[]
            {
                "进入「仓储与出货 › 物料策略 › 替代料」",
                "选型号与数量，点「🔍 替代齐套试算」：下方逐条给出 BOM 行 需求 / 主料可用 / 缺口 / 替代可用 / 实替量 / 剩余缺口",
                "看上方「替代关系台账」确认某条替代的认可状态——**待客户批准的关系不参与顶缺口**，只做展示",
                "切到「安全库存补货」子页：看日均消耗（领用实测 / 工单需求）、提前期、安全库存、补货点、净库存与建议补货量",
                "点「⚡ 按建议生成补货申请」→ 生成一张草稿（只包含紧急/需补货且无未完成申请单的物料），到「资材采购申请」提交审批"
            },
            Tip = "替代三道门槛缺一不可：关系成立（用量系数）、客户/工艺已认可、替代料自己有货。补货点 = 日均消耗 × 采购提前期 + 安全库存，净库存含可用 + 在途 + 草稿，所以刚建完补货申请不会立刻又提示重复下单。",
            SeeAlso = new[] { "mat.chain", "mat.age", "mat.purchase" }
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
            Id = "rep.aireport", Category = "追溯与报表", Title = "AI 班报 / 日报一键生成",
            Aliases = new[] { "班报", "日报", "交接班", "一键生成班报", "生产日报", "AI 班报", "自动写班报", "交班" },
            Keywords = new[] { "AI", "班报", "日报", "报表", "一键生成", "交接班", "产量", "良率", "OEE", "异常", "大模型" },
            Summary = "报表中心的「AI 班报」子页：一键按当前指标生成班报/日报，产量/良率/OEE/异常/工单一屏呈现，可复制或导出 txt。",
            TargetPage = "报表中心", TargetSubPage = "AI 班报", TargetButton = "一键生成",
            Steps = new[]
            {
                "进入「追溯与报表 › 报表中心 › AI 班报」",
                "在「类型」下拉选「日报」或「班报」",
                "点「⚡ 一键生成」，稍候即在下方生成班报正文",
                "点「复制」可粘贴到微信/邮件，点「导出 txt」可存成文本文件"
            },
            Tip = "数字全部取自本地报表汇总（MesDatabase），AI 只负责组织措辞、不生成数字；未配置云端大模型时自动用本地模板生成，断网/超时也不影响。判定（达标与否）以 SPC 规则与人工为准，本页只做汇总陈述。本系统为演示原型，数据为内存模拟。",
            SeeAlso = new[] { "rep.daily", "rep.yield", "rep.oee" }
        },
        new ManualEntry
        {
            Id = "energy.peakvalley", Category = "能源与碳", Title = "峰谷电费移峰填谷节约测算",
            Aliases = new[] { "峰谷", "移峰填谷", "电费", "节约", "分时电价", "谷电", "省电费", "削峰填谷", "电价" },
            Keywords = new[] { "峰谷", "移峰填谷", "电费", "分时电价", "尖峰", "高峰", "平段", "低谷", "节约", "能源", "降本" },
            Summary = "按当月用电与分时电价，测算把峰段负荷移到低谷可节约的电费（移峰填谷），并可调转移比例看理论节约空间。",
            TargetPage = "峰谷电费测算", TargetSubPage = "", TargetButton = "重新测算",
            Steps = new[]
            {
                "进入「能源与碳 › 峰谷电费测算」",
                "顶部卡显示本月用电、当前月电费、月度/年化节约",
                "在「峰段转移比例」下拉选 0%~50%，卡片与四时段表实时重算",
                "下表列出尖峰/高峰/平段/低谷的电量、占比与电价"
            },
            Tip = "本月用电取自能耗台账；日负荷曲线与分时电价为演示假设（非实测、非财务决算），移峰填谷能否实施要结合生产工艺由人决策，本页仅反映电价差的理论节约空间。",
            SeeAlso = new string[0]
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

        // ============================================================
        // 数字孪生（网页看板入口：运营大屏 / 生产 / 设备 / 网络 / 碳排 / 质量 / 工艺 / 成本 / 仓储 / 集成 / 防呆 等）
        // 说明：孪生页是独立 HTML 网页，不在 WinForms 页签体系内，故 TargetPage 统一指向「数字孪生」入口页，
        //       具体看哪张页由步骤里的卡片名决定；打开方式见 twin.enter。
        // ============================================================
        new ManualEntry
        {
            Id = "twin.enter", Category = "数字孪生", Title = "进入数字孪生 / 打开任意孪生页",
            Aliases = new[] { "数字孪生", "孪生页在哪", "大屏怎么开", "看板在哪", "打开运营大屏", "网页看板", "孪生入口" },
            Keywords = new[] { "数字孪生", "大屏", "看板", "孪生", "网页", "运营大屏", "入口", "投屏" },
            Summary = "「数字孪生」分类下是一组网页看板的入口卡（运营大屏 / 生产 / 设备 / 网络 / 碳排 / 质量 / 工艺 / 成本 / 仓储 / 集成 / 防呆等），点卡片即可在新标签页或窗体内打开。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "在顶部一级分类选「数字孪生」，进入孪生页入口",
                "页面列出全部孪生页入口卡，每张卡有「↗ 新标签页打开」和「⊞ 内嵌展示」两个按钮",
                "默认用「新标签页打开」（浏览器，零依赖，永远可用）；若已引用 WebView2，可用「内嵌展示」在 MES3 窗体内显示",
                "页内右上角「← 返回运营大屏 / 返回 MES3」可回到对应入口或主界面",
                "想直接拿到 HTML 文件做投屏演示，点「📂 打开页面所在目录」即可"
            },
            Tip = "孪生页是独立静态网页（零外部依赖），也可直接双击 HTML 文件打开；修改页面后重新构建即生效，无需改 C#。数据均为演示用模拟数据，自洽但非真实生产数据。",
            SeeAlso = new[] { "twin.dashboard", "twin.production", "twin.carbon", "twin.quality", "twin.integration" }
        },
        new ManualEntry
        {
            Id = "twin.factory3d", Category = "数字孪生", Title = "看 3D 工厂数据流（厂区点位 + 数据流转）",
            Aliases = new[] { "3D工厂", "工厂数据流", "厂区点位", "数据流转", "3d-factory", "网络数据流", "厂区三维", "三维工厂" },
            Keywords = new[] { "3D", "工厂", "数据流", "厂区", "点位", "网络", "应用层", "拓扑", "等距", "交换机" },
            Summary = "等距 3D 厂区视图：应用系统与网络设备以立体点位呈现，叠加应用层数据流（协议/频率）与网络四层拓扑，悬停/点击看详情。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "在顶部一级分类选「数字孪生」，找到「3D 工厂数据流」入口卡",
                "点「↗ 新标签页打开」（或「⊞ 内嵌展示」）",
                "悬停厂区内的点位（系统/交换机/设备）看摘要，点击看完整信息",
                "用右侧「数据流图层」切 应用层 / 全部叠加 / 网络层，「链路频率筛选」按实时/分钟/日级过滤",
                "也可从运营大屏「网络情况」面板的「3D 工厂数据流 ↗」进入"
            },
            Tip = "厂区点位与数据链路为演示用模拟数据（非实时实测）；QMS/供应商协同标「规划中」。应用层链路带协议与频率（粒子流速反映节拍），网络层为核心→汇聚→接入→终端四层。",
            SeeAlso = new[] { "twin.integration", "twin.dashboard" }
        },
        new ManualEntry
        {
            Id = "twin.dashboard", Category = "数字孪生", Title = "看运营大屏（生产/网络/设备/碳排放总览）",
            Aliases = new[] { "运营大屏", "看大屏", "总览看板", "四大指标", "生产网络设备碳排", "工厂总览" },
            Keywords = new[] { "运营大屏", "大屏", "总览", "生产", "网络", "设备", "碳排放", "KPI" },
            Summary = "四大类核心指标总览：生产（产量/良率/WIP/开机）、网络（带宽/时延/丢包）、设备（稼动/故障）、碳排放（速率/总量），支持模块折叠、时间范围筛选与自动轮播。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「运营大屏」点「新标签页打开」",
                "顶部控制条可切模块、选时间范围（1h/6h/24h/7d）、重置、开自动轮播",
                "点设备/网络类 KPI 卡可直接跳进对应孪生页；点模块面板上的「⇢ 进入 XX 孪生」可下钻到子页",
                "底部告警条汇总全厂异常，全绿时显示正常"
            },
            Tip = "这是对外演示的总入口，建议从这里再跳到各专项孪生页，叙事更顺。",
            SeeAlso = new[] { "twin.enter", "twin.production", "twin.cockpit" }
        },
        new ManualEntry
        {
            Id = "twin.cockpit", Category = "数字孪生", Title = "看经营驾驶舱（管理层决策视图）",
            Aliases = new[] { "经营驾驶舱", "驾驶舱", "管理层看板", "健康度", "决策视图", "总评" },
            Keywords = new[] { "驾驶舱", "健康度", "KPI", "告警", "趋势", "经营", "管理层" },
            Summary = "面向管理层的决策视图：健康度总评与自动结论、8 项核心 KPI、告警风险、产量/良率/碳排/OEE 趋势，适合汇报与经营分析。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「经营驾驶舱」点「新标签页打开」",
                "首屏看「健康度总评」与自动生成的结论层（瓶颈/风险/建议）",
                "下行查看 8 项核心 KPI 与告警风险清单",
                "底部趋势区对比产量、良率、碳排放、OEE 的走势"
            },
            SeeAlso = new[] { "twin.dashboard", "twin.cost" }
        },
        new ManualEntry
        {
            Id = "twin.showcase", Category = "数字孪生", Title = "展厅总控（自动巡演 + 逐幕旁白）",
            Aliases = new[] { "展厅总控", "展厅", "巡演", "自动播放", "导演脚本", "大屏轮播" },
            Keywords = new[] { "展厅", "总控", "巡演", "导演脚本", "旁白", "自动" },
            Summary = "展厅大屏专用：导演脚本场景列表 + 自动巡演播放 + 逐幕旁白，支持上一幕/下一幕与暂停，适合对外展示与参观。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「展厅总控」点「新标签页打开」",
                "点「▶ 开始巡演」按导演脚本自动逐幕切换并播放旁白",
                "需要时可用「上一幕 / 下一幕 / 暂停」手动控制节奏"
            },
            Tip = "参观动线演示首选此页，脚本即叙事顺序，无需现场手忙脚乱切页。",
            SeeAlso = new[] { "twin.dashboard" }
        },
        new ManualEntry
        {
            Id = "twin.gov", Category = "数字孪生", Title = "看政府申报版（梯度培育 / CMMM 自评）",
            Aliases = new[] { "政府申报版", "申报版", "梯度培育", "CMMM", "智能工厂申报", "能力域自评" },
            Keywords = new[] { "申报", "梯度培育", "CMMM", "能力域", "自评", "智能工厂", "量化收益" },
            Summary = "面向政府申报与审厂的材料化视图：梯度培育对标、CMMM 能力域自评、指标达成度与统计口径、智能化场景量化收益。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「政府申报版」点「新标签页打开」",
                "看梯度培育对标与各等级差距",
                "看 CMMM 能力域自评矩阵与达标情况",
                "看指标达成度、统计口径与场景量化收益清单"
            },
            Tip = "所有等级与能力域自评均带「以正式评审为准」免责说明；页内不构成对标准条款号的引用，演示时口径如实陈述、不编造条款号，对外场合用页内说明而非弹窗。",
            SeeAlso = new[] { "twin.cockpit", "twin.integration" }
        },
        new ManualEntry
        {
            Id = "twin.production", Category = "数字孪生", Title = "看生产情况孪生（产线/设备实时指标）",
            Aliases = new[] { "生产情况孪生", "生产孪生", "产线视图", "设备实时产量", "在制品详情" },
            Keywords = new[] { "生产孪生", "产线", "设备", "产量", "良率", "稼动", "在制品" },
            Summary = "产线架构等距视图 + 每台设备实时产量/良率/稼动指标，悬停设备查看当前在制品（工单/批次/工序/进度/班组）详情。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「生产情况孪生」点「新标签页打开」",
                "看各产线设备状态三色（绿=运行 / 灰=未运行 / 红=故障）",
                "把鼠标悬停到某台设备，弹出在制面板看工单、批次、工序、进度与班组",
                "顶部产量按口径倍率折算、良率与稼动不折算（见页内口径说明）"
            },
            SeeAlso = new[] { "twin.dashboard", "twin.device" }
        },
        new ManualEntry
        {
            Id = "twin.device", Category = "数字孪生", Title = "看设备数字孪生（2.5D 厂房/设备点位）",
            Aliases = new[] { "设备数字孪生", "设备孪生", "厂房布局", "设备点位", "2.5D" },
            Keywords = new[] { "设备孪生", "2.5D", "厂房", "点位", "布局", "监控设备" },
            Summary = "2.5D 等距厂房布局，标注各产线全部监控设备点位，点击查看实时运行参数与告警。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「设备数字孪生」点「新标签页打开」",
                "看等距厂房里各产线设备的布局与状态色",
                "点击某个设备点位，查看其实时运行参数与告警信息"
            },
            SeeAlso = new[] { "twin.production" }
        },
        new ManualEntry
        {
            Id = "twin.carbon", Category = "数字孪生", Title = "看碳排放孪生（产线碳排/阈值预警）",
            Aliases = new[] { "碳排放孪生", "碳排孪生", "碳排视图", "排放曲线", "碳排放预警" },
            Keywords = new[] { "碳排孪生", "碳排放", "排放", "阈值", "预警", "曲线" },
            Summary = "产线碳排等距视图，悬停设备查看近一小时排放曲线，超阈值红色预警，全厂指标约 3 秒刷新，阈值滑块可调（20~160）。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「碳排放孪生」点「新标签页打开」",
                "拖动阈值滑块（20~160）设定预警线",
                "悬停设备看近一小时排放速率曲线，超阈值设备标红",
                "看顶栏全厂碳排放速率与累计量（速率=(运行?功率×负荷:待机)×0.5568，取 2026 电网因子）"
            },
            Tip = "演示数据为模拟自洽，口径见页内说明；对外如实陈述，不夸大减排量。",
            SeeAlso = new[] { "twin.dashboard", "twin.production" }
        },
        new ManualEntry
        {
            Id = "twin.network", Category = "数字孪生", Title = "看网络数字孪生（四层拓扑）",
            Aliases = new[] { "网络数字孪生", "网络孪生", "网络拓扑", "四层网络", "链路连通" },
            Keywords = new[] { "网络孪生", "拓扑", "核心", "汇聚", "接入", "终端", "链路" },
            Summary = "核心 / 汇聚 / 接入 / 终端 四层网络拓扑，展示节点在线状态与链路连通性，适合网络规划与故障定位演示。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「网络数字孪生」点「新标签页打开」",
                "画布支持滚轮以鼠标为锚缩放、拖拽平移，右上角有 ＋/−/重置视图控制条（仅缩放画布，不影响页面）",
                "链路按层级着色（核心紫 / 汇聚青 / 接入蓝 / 终端）、按带宽调粗细；节点旁显示实时 CPU/带宽/时延",
                "点节点看详情；点核心层节点可下钻数据中心机柜（42U 设备布局 / U 位占用 / 上行端口连接）",
                "链路或节点告警时对应链路红色虚线 + 断口 + 脉冲高亮，右侧「网络异常汇总」可定位"
            },
            Tip = "节点状态与性能指标为演示模拟数据（3s 刷新，贴近真实：核心层最稳、终端层最易波动）；机柜与端口为演示布局。故障语义：任一端点故障 → 关联链路不可用。",
            SeeAlso = new[] { "twin.integration", "twin.factory3d" }
        },
        new ManualEntry
        {
            Id = "twin.trace", Category = "数字孪生", Title = "批次追溯查询（客户审厂工具）",
            Aliases = new[] { "批次追溯查询", "追溯查询", "正向工艺链路", "物料反查", "审厂追溯" },
            Keywords = new[] { "追溯", "批次", "工艺链路", "检验记录", "反查", "审厂" },
            Summary = "客户审厂工具：输入批次号秒级输出正向工艺链路与检验记录，点击物料批号反查该批料投放到了哪些电芯。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「批次追溯查询」点「新标签页打开」",
                "输入产品批次号，查看正向工艺链路与逐工序检验记录",
                "点击某条物料批号，反查该批料投放到的下游电芯范围"
            },
            Tip = "与系统内「SN 追溯 / 出货与召回」同源互补：本页偏对外审厂演示，系统页偏内部追溯与召回。",
            SeeAlso = new[] { "twin.quality", "ship.recall" }
        },
        new ManualEntry
        {
            Id = "twin.quality", Category = "数字孪生", Title = "看质量孪生（良率/SPC/帕累托/热力/CAPA）",
            Aliases = new[] { "质量孪生", "质量分析", "SPC控制图", "缺陷帕累托", "良率热力", "CAPA" },
            Keywords = new[] { "质量孪生", "良率", "SPC", "帕累托", "热力图", "CAPA", "分析" },
            Summary = "质量追溯主线分析：全厂/产线良率、SPC 控制图、缺陷帕累托、良率热力（工序×班次）、CAPA 闭环，客户审厂与质量申报核心视图。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「质量孪生」点「新标签页打开」",
                "看全厂/产线良率与 8 项质量 KPI",
                "看 SPC 控制图（化成电压 X-bar，±3σ 控制限）与缺陷帕累托",
                "看良率热力图（工序×班次）定位薄弱环节，看 CAPA 四步闭环进展"
            },
            Tip = "分析视图，结论与口径见页内说明；数据为演示模拟，对外如实陈述。",
            SeeAlso = new[] { "twin.recipe", "twin.trace", "q.spc" }
        },
        new ManualEntry
        {
            Id = "twin.recipe", Category = "数字孪生", Title = "看工艺/配方孪生（版本对比/CPK）",
            Aliases = new[] { "工艺孪生", "配方孪生", "工艺版本", "参数偏移", "CPK", "工序能力" },
            Keywords = new[] { "工艺孪生", "配方", "版本对比", "偏移带", "CPK", "过程能力" },
            Summary = "工艺参数版本管控：版本对比（v2.2→v2.3）、关键工序参数偏移带、工序能力 CPK 矩阵、版本切换对良率影响。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「工艺/配方孪生」点「新标签页打开」",
                "看工艺版本对比表与参数偏移带（spec band）",
                "看工序能力 CPK 矩阵，识别能力不足的工序",
                "看版本切换对良率影响的对比曲线"
            },
            Tip = "分析视图，口径见页内说明；数据为演示模拟。",
            SeeAlso = new[] { "twin.quality", "q.spcConfig" }
        },
        new ManualEntry
        {
            Id = "twin.cost", Category = "数字孪生", Title = "看成本孪生（单只成本/瀑布/峰谷电费）",
            Aliases = new[] { "成本孪生", "成本分析", "单只成本", "成本瀑布", "峰谷电费", "降本增效" },
            Keywords = new[] { "成本孪生", "成本", "单只成本", "瀑布图", "峰谷", "电费", "降本" },
            Summary = "经营视角成本分析：单只成本与目标对比、成本构成占比、成本瀑布、良率损失成本、峰谷电费，支撑央企降本增效汇报。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「成本孪生」点「新标签页打开」",
                "看单只成本（演示测算约 8.42 元/只）与 8.00 元/只目标的差距",
                "看成本构成占比、成本瀑布与良率损失成本",
                "看峰谷电量与峰谷电费分布"
            },
            Tip = "页内明确标注「演示测算非财务决算」，口径为材料/人工/能耗/制费/质量损失之和；对外汇报按此口径陈述。",
            SeeAlso = new[] { "twin.cockpit" }
        },
        new ManualEntry
        {
            Id = "twin.warehouse", Category = "数字孪生", Title = "看仓储孪生（立体库/AGV/齐套）",
            Aliases = new[] { "仓储孪生", "仓库孪生", "立体库", "AGV", "库位", "齐套" },
            Keywords = new[] { "仓储孪生", "立体库", "AGV", "库位", "物料批次", "齐套", "出入库" },
            Summary = "2.5D 立体库库位状态 / 物料批次 / AGV 调度 / 出入库流水 / 工单齐套看板，是仓储数字化最重的演示视图。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「仓储孪生」点「新标签页打开」",
                "看 2.5D 立体库库位五色状态，悬停库位看物料/批次/数量/库龄/供应商",
                "看 AGV 在通道内实时调度移动",
                "看出入库流水、工单齐套看板与库区库存分布"
            },
            Tip = "可用顶部库区筛选切换视角；可用库存不含待检区与退货待处理区（与系统内 WMS 口径一致）。",
            SeeAlso = new[] { "wms.stock", "stock.kitting" }
        },
        new ManualEntry
        {
            Id = "twin.integration", Category = "数字孪生", Title = "看系统集成拓扑（SAP/PLC/SCADA↔MES↔下游）",
            Aliases = new[] { "系统集成拓扑", "集成拓扑", "系统架构", "接口", "SAP对接", "数据中台" },
            Keywords = new[] { "集成拓扑", "SAP", "PLC", "SCADA", "QMS", "WMS", "MES", "数据中台", "协议" },
            Summary = "应用层集成视图：SAP / PLC / SCADA / QMS / WMS ↔ MES 数据中台 ↔ 孪生看板 / 政府申报 / AI 助手，协议与频率一目了然。点击节点/链路看详情。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「系统集成拓扑」点「新标签页打开」",
                "看三层节点（左：SAP/PLC/SCADA/QMS/WMS；中：MES 数据中台；右：下游消费方）",
                "看 9 条链路的协议（RFC/ODATA/OPC UA/MQTT/REST）、频率、方向与状态",
                "点击节点或链路查看其详情与交互关系"
            },
            Tip = "QMS / WMS / 供应商 等接口标「规划中」（虚线），属自研 MES 覆盖范畴、尚未对接真实系统；演示时如实说「规划中/建设中」，避免说成已对接。",
            SeeAlso = new[] { "twin.gov", "twin.network", "ai.ask" }
        },
        new ManualEntry
        {
            Id = "twin.guard", Category = "数字孪生", Title = "看过站防呆演示台（五大规则判定）",
            Aliases = new[] { "过站防呆演示台", "防呆演示", "工位防呆", "放行拦截", "返工分流" },
            Keywords = new[] { "防呆", "过站", "规则", "放行", "拦截", "返工", "演示" },
            Summary = "交互式工位防呆演示：五大规则判定（跳站/参数超限/版本不符/质量挂起/物料齐套）→ 放行/拦截/返工分流，可现场一键触发。",
            TargetPage = "数字孪生", TargetSubPage = "", TargetButton = "新标签页打开",
            Steps = new[]
            {
                "进入「数字孪生」，在入口卡找到「过站防呆演示台」点「新标签页打开」",
                "选批次与工序，点 6 个场景按钮之一触发校验",
                "看判定结论：放行（pass）或拦截（block），以及命中的规则",
                "被拦截的看返工分流建议（回上一工序），下方事件日志记录全过程"
            },
            Tip = "这是演示用的交互沙盘：规则判定权在规则不在模型，失败零副作用，可反复点不同场景对比。",
            SeeAlso = new[] { "prod.feeding", "twin.integration" }
        },
        };

        // 编写顺序即业务主次顺序（总览 → 生产 → 质量 → 仓储 → 设备人员 → 追溯报表 → 帮助），
        // 赋序号供搜索结果同分时排序使用，避免退化成按分类名字母序。
        for (int i = 0; i < list.Count; i++) list[i].Order = i;
        return list;
    }
}
