using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms;

/// <summary>
/// 工厂 MES 原型主窗体（WinForms）
/// 功能：生产看板 / 工单管理 / SN 追溯 / 测试数据 / 设备监控
/// 数据：内存仓储（Data/MesDatabase.cs），与 Web 版共用同一套数据模型与种子数据
/// </summary>
public partial class MainForm : Form
{
    // ================= 顶部导航（一级业务分类 → 二级功能页面） =================
    /// <summary>顶层页签容器：每个页签 = 一个一级业务分类，页内容是该分类的二级页签容器。</summary>
    private TabControl _tabs = null!;

    /// <summary>一级分类名 → 该分类下的二级页签容器（子 TabControl）。</summary>
    private readonly Dictionary<string, TabControl> _categoryTabs = new();

    /// <summary>一级分类名 → 顶层分类页。跨层跳转时需先选中该分类页。</summary>
    private readonly Dictionary<string, TabPage> _categoryPages = new();

    /// <summary>二级页面 → 所属一级分类名。跳转时反查用。</summary>
    private readonly Dictionary<TabPage, string> _pageCategory = new();

    /// <summary>全部二级页面（按注册顺序）。供按标题跳转查找与遍历使用。</summary>
    private readonly List<TabPage> _allPages = new();

    // ================= 使用手册 =================
    private TextBox _txtManualQuery = null!;
    private Button _btnManualSearch = null!;
    private Button _btnManualClear = null!;
    private DataGridView _manualResultGrid = null!;
    private RichTextBox _rtbManualDetail = null!;
    private Button _btnManualGo = null!;
    private Label _lblManualHint = null!;
    /// <summary>最近一次检索结果（列表与详情共用，避免重复计算）。</summary>
    private List<ManualSearchHit> _manualHits = new();
    /// <summary>手册页首次加载守卫（幂等）。</summary>
    private bool _manualLoaded;

    // ================= AI 助手 =================
    private RichTextBox _rtbAssistantChat = null!;
    private TextBox _txtAssistantInput = null!;
    private Button _btnAssistantSend = null!;
    private Button _btnAssistantClear = null!;
    private Button _btnAssistantSettings = null!;
    private FlowLayoutPanel _flpAssistantActions = null!;
    private RichTextBox _rtbAssistantInspector = null!;
    private Label _lblAssistantStatus = null!;
    private ToolTip _assistantTip = null!;
    /// <summary>最近一次回答（供建议动作回放 / 测试断言）。</summary>
    private AssistantReply? _lastAssistantReply;
    /// <summary>助手页首次加载守卫（幂等）。</summary>
    private bool _assistantLoaded;
    /// <summary>提问轮次（展示用）。</summary>
    private int _assistantRound;
    /// <summary>待渲染的对话段落（动画播放队列）。</summary>
    private readonly Queue<Action> _assistantRenderQueue = new();

    // ================= 智能排产 =================
    private ComboBox _cboSchedulePo = null!;
    private Button _btnScheduleGen = null!;
    private Button _btnScheduleCreate = null!;
    private Label _lblScheduleHeadline = null!;
    private Label _lblScheduleRisk = null!;
    private Label _lblScheduleStock = null!;
    private Label _lblScheduleLine = null!;
    private Label _lblScheduleInputQty = null!;
    private Label _lblScheduleFinish = null!;
    private Label _lblScheduleSlack = null!;
    private ListBox _lstScheduleEvidence = null!;
    private DataGridView _gridScheduleStock = null!;
    private ToolTip _scheduleTip = null!;
    /// <summary>当前排产建议（供"按建议生成工单"与自测断言复用）。</summary>
    private ScheduleRecommendation? _currentSchedule;
    /// <summary>排产页首次加载守卫（幂等）。</summary>
    private bool _scheduleLoaded;
    private Button _btnScheduleAi = null!;
    private Label _lblScheduleResource = null!;
    private Label _lblScheduleAi = null!;
    private DataGridView _gridScheduleAsset = null!;

    // ================= 资材采购申请 =================
    private ComboBox _cboReqWo = null!;
    private ComboBox _cboReqStatus = null!;
    private TextBox _txtReqSearch = null!;
    private Button _btnReqSubmit = null!;
    private Button _btnReqApprove = null!;
    private Button _btnReqReject = null!;
    private Button _btnReqOrder = null!;
    private Button _btnReqClose = null!;
    private Button _btnReqFinish = null!;
    private Label _lblReqSummary = null!;
    private Label _lblReqInfo = null!;
    private DataGridView _gridReqList = null!;
    private DataGridView _gridReqLines = null!;
    private ToolTip _reqTip = null!;
    private bool _reqLoaded;
    /// <summary>当前选中的申请单（供状态流转与明细刷新复用）。</summary>
    private PurchaseRequest? _currentRequest;
    private Button _btnReqDelete = null!;
    private ComboBox _cboReqOperator = null!;
    /// <summary>当前操作人（决定草稿删除权限；项目无登录体系，沿用既有"操作人"约定）</summary>
    private string CurrentOperator => (_cboReqOperator?.SelectedItem as string) ?? "计划员";
    private Button _btnReqAi = null!;
    private Button _btnReqAiGen = null!;
    private Label _lblReqAi = null!;
    private Label _lblReqRisk = null!;
    /// <summary>最近一次采购分析结果（供「按建议生成申请」与自测复用）。</summary>
    private PurchaseAiAnalysis? _currentAiAnalysis;
    /// <summary>分析时的输入快照（生成申请时按它预填，保证与分析一致）。</summary>
    private PurchaseAnalysisInput? _currentAiInput;
    /// <summary>分段渲染定时器（懒创建，避免构造期占用资源）。</summary>
    private System.Windows.Forms.Timer? _assistantRenderTimer;

    // 看板
    private Label _lblOutput = null!;
    private Label _lblWip = null!;
    private Label _lblYield = null!;
    private Label _lblRunningEq = null!;
    private DataGridView _dashWoGrid = null!;
    private DataGridView _dashEqGrid = null!;

    // 工单
    private DataGridView _woGrid = null!;
    private Label _lblWoPo = null!;
    private DataGridView _woBomGrid = null!;
    private Label _lblWoBomSummary = null!;

    // 追溯
    private TabPage _tracePage = null!;
    private TextBox _txtSn = null!;
    private Label _lblCellInfo = null!;
    private Label _lblEnv = null!;
    private DataGridView _matGrid = null!;
    private DataGridView _procGrid = null!;
    private DataGridView _traceTestGrid = null!;

    // 测试
    private DataGridView _testGrid = null!;

    // 设备
    private DataGridView _eqGrid = null!;

    // 工序实绩
    private ComboBox _cboActualDevice = null!;
    private Label _lblActualStatus = null!;
    private DataGridView _actualGrid = null!;
    private DataGridView _inspectGrid = null!;
    private Label _lblInspectTarget = null!;

    // 资材追溯（电芯保留）
    private TextBox _txtMatKeyword = null!;
    private DataGridView _matHoldGrid = null!;
    private Label _lblMatHint = null!;
    private Label _lblMatCount = null!;
    private Label _lblMatSelected = null!;
    private ComboBox _cboHoldLocation = null!;
    private TextBox _txtHoldReason = null!;
    private TextBox _txtHoldOperator = null!;
    private Button _btnDoHold = null!;
    /// <summary>List：（说明待补充）</summary>
    private List<CellHoldCandidate> _lastMatCandidates = new();

    // 保留管理
    private TextBox _txtQueryHoldNo = null!;
    private ComboBox _cboHoldStatusFilter = null!;
    private ComboBox _cboHoldLocationFilter = null!;
    private CheckBox _chkUseTimeFilter = null!;
    private DateTimePicker _dtHoldFrom = null!;
    private DateTimePicker _dtHoldTo = null!;
    private Label _lblHoldQueryResult = null!;
    private DataGridView _holdGrid = null!;
    private DataGridView _holdHistoryGrid = null!;
    private Label _lblHoldContext = null!;
    private Button _btnReleaseHold = null!;
    private Button _btnScanNonconforming = null!;

    // 异常登记
    private DateTimePicker _dtAbOccurTime = null!;
    private TextBox _txtAbFoundBy = null!;
    private ComboBox _cboAbLine = null!;
    private ComboBox _cboAbEquipment = null!;
    private ComboBox _cboAbCategory = null!;
    private ComboBox _cboAbSubType = null!;
    private TextBox _txtAbDescription = null!;
    private TextBox _txtAbTempMeasure = null!;
    private TextBox _txtAbOwner = null!;
    private TextBox _txtAbRegisterBy = null!;
    private DataGridView _abRecentGrid = null!;

    // 异常管理
    private TextBox _txtAbQueryNo = null!;
    private ComboBox _cboAbStatusFilter = null!;
    private ComboBox _cboAbCategoryFilter = null!;
    private ComboBox _cboAbLineFilter = null!;
    private CheckBox _chkAbUseTimeFilter = null!;
    private DateTimePicker _dtAbFrom = null!;
    private DateTimePicker _dtAbTo = null!;
    private Label _lblAbQueryResult = null!;
    private DataGridView _abGrid = null!;
    private Label _lblAbContext = null!;
    private TextBox _txtAbOperator = null!;
    private Button _btnAbConfirm = null!;
    private Button _btnAbClose = null!;
    private Button _btnAbView = null!;

    // WMS 仓储（子页1 单据管理）
    private TabControl _wmsSubTabs = null!;
    private ComboBox _cboWmsTypeFilter = null!;
    private ComboBox _cboWmsStatusFilter = null!;
    private TextBox _txtWmsKeyword = null!;
    private CheckBox _chkWmsUseTimeFilter = null!;
    private DateTimePicker _dtWmsFrom = null!;
    private DateTimePicker _dtWmsTo = null!;
    private Label _lblWmsQueryResult = null!;
    private DataGridView _wmsOrderGrid = null!;
    private Label _lblWmsContext = null!;
    private TextBox _txtWmsOperator = null!;
    private Button _btnWmsPost = null!;
    private TabControl _wmsDetailTabs = null!;
    private DataGridView _wmsLineGrid = null!;
    private Label _lblWmsLineTotal = null!;
    private DataGridView _wmsInspectGrid = null!;
    private Label _lblWmsInspectInfo = null!;
    private Label _lblWmsInspectHint = null!;
    private Button _btnWmsLoadInspect = null!;
    private Button _btnWmsSaveInspect = null!;
    private DataGridView _wmsHistoryGrid = null!;
    // WMS 仓储（子页2 库存台账）
    private TextBox _txtWmsStockKeyword = null!;
    private Label _lblWmsStockResult = null!;
    private DataGridView _wmsStockGrid = null!;
    private Label _lblWmsStockSummary = null!;

    // SPC 分析（子页1 控制图与能力）
    private TabControl _spcSubTabs = null!;
    private ComboBox _cboSpcItem = null!;
    private ComboBox _cboSpcEquipment = null!;
    private ComboBox _cboSpcRange = null!;
    private SpcControlChart _spcChart = null!;
    private DataGridView _spcSampleGrid = null!;
    private Label _lblSpcCapN = null!;
    private Label _lblSpcCapMean = null!;
    private Label _lblSpcCapSigma = null!;
    private Label _lblSpcCapCp = null!;
    private Label _lblSpcCapCpk = null!;
    private Label _lblSpcCapGrade = null!;
    private Label _lblSpcCapOut = null!;
    private Label _lblSpcCapUsl = null!;
    private Label _lblSpcCapLsl = null!;
    private Label _lblSpcCapVersion = null!;
    private bool _spcUpdating;
    // SPC 分析（子页2 参数预警）
    private ComboBox _cboSpcAlertItem = null!;
    private ComboBox _cboSpcAlertType = null!;
    private ComboBox _cboSpcAlertSeverity = null!;
    private ComboBox _cboSpcAlertStatus = null!;
    private CheckBox _chkSpcAlertUseTime = null!;
    private DateTimePicker _dtSpcAlertFrom = null!;
    private DateTimePicker _dtSpcAlertTo = null!;
    private Label _lblSpcAlertResult = null!;
    private Label _lblSpcAlertContext = null!;
    private TextBox _txtSpcAlertOperator = null!;
    private DataGridView _spcAlertGrid = null!;
    private Button _btnSpcAlertAck = null!;
    private Button _btnSpcAlertClose = null!;
    private Button _btnSpcAlertToAbnormal = null!;
    // SPC 分析（子页1）版本信息行
    private Label _lblSpcVersionInfo = null!;

    // SPC 项目管理页（顶层第 13 个页签）
    private DataGridView _spcItemConfigGrid = null!;
    private DataGridView _spcVersionGrid = null!;
    private Button _btnSpcVersionEdit = null!;
    private Button _btnSpcVersionDelete = null!;
    private Label _lblSpcConfigHint = null!;

    // ---- 出货与召回页（顶层第 14 个页签）：出货绑定 + 召回反查 ----
    private TabControl _shipSubTabs = null!;
    // 子页1：出货绑定
    private ComboBox _cboShipStatusFilter = null!;
    private TextBox _txtShipKeyword = null!;
    private DataGridView _shipOrderGrid = null!;
    private DataGridView _shipItemGrid = null!;
    private Button _btnShipBind = null!;
    private Button _btnShipUnbind = null!;
    private Label _lblShipBound = null!;
    // 子页2：召回反查
    private ComboBox _cboRecallMode = null!;
    private TextBox _txtRecallKeyword = null!;
    private Label _lblRecallSummary = null!;
    private DataGridView _recallShipGrid = null!;
    private DataGridView _recallCellGrid = null!;
    private DataGridView _recallLotGrid = null!;
    private Button _btnRecallFreeze = null!;
    private Button _btnRecallCopy = null!;
    private RecallQueryResult? _lastRecall = null;
    // 子页2：新建出库单（物流出库，一步创建 + 绑定合格电芯）
    private TextBox _txtNewShipCustomer = null!;
    private TextBox _txtNewShipRefNo = null!;
    private TextBox _txtNewShipOperator = null!;
    private TextBox _txtNewShipRemark = null!;
    private DataGridView _newShipCellGrid = null!;
    private DataGridView _newShipLineGrid = null!;
    private Label _lblNewShipSelected = null!;
    private Label _lblNewShipCandidate = null!;
    private Button _btnNewShipCreate = null!;
    private bool _loadingNewShipCells;
    // ---- WIP 看板页（顶层第 15 个页签）：工序在制流转 ----
    private Label _lblWipTotal = null!;
    private Label _lblWipToday = null!;
    private Label _lblWipStay = null!;
    private Label _lblWipBottleneck = null!;
    private Label _lblWipMovable = null!;
    private DataGridView _wipFlowGrid = null!;
    private DataGridView _wipCellGrid = null!;
    private Button _btnWipSimulate = null!;
    // ---- 报告导出（xlsx） ----
    private Button _btnTraceExport = null!;
    private Button _btnRecallExport = null!;
    // ---- 返工管理页（顶层第 16 个页签） ----
    private Label _lblReworkPending = null!;
    private Label _lblReworkInProgress = null!;
    private Label _lblReworkWaitInspect = null!;
    private Label _lblReworkReleased = null!;
    private Label _lblReworkScrapped = null!;
    private ComboBox _cboReworkStatus = null!;
    private TextBox _txtReworkKeyword = null!;
    private DataGridView _reworkGrid = null!;
    private DataGridView _reworkHistoryGrid = null!;
    private Button _btnReworkCreate = null!;
    private Button _btnReworkStart = null!;
    private Button _btnReworkFinish = null!;
    private Button _btnReworkInspect = null!;
    private Button _btnReworkScrap = null!;
    // ---- 报表中心页（顶层第 17 个页签） ----
    private Label _lblReportTodayOutput = null!;
    private Label _lblReportMonthOutput = null!;
    private Label _lblReportYield = null!;
    private Label _lblReportOpenAbnormal = null!;
    private Label _lblReportOee = null!;
    private ComboBox _cboReportRange = null!;
    private Label _lblReportHint = null!;
    private TabControl _reportSubTabs = null!;
    private DataGridView _dailyOutputGrid = null!;
    private DataGridView _monthlyOutputGrid = null!;
    private DataGridView _yieldTrendGrid = null!;
    private DataGridView _abnormalTopGrid = null!;
    private DataGridView _oeeGrid = null!;
    private DataGridView _stockGrid = null!;
    // ---- WMS 效期与 FIFO 子页 ----
    private Label _lblExpiryExpired = null!;
    private Label _lblExpiryNear = null!;
    private Label _lblExpiryNone = null!;
    private DataGridView _expiryAlertGrid = null!;
    private DataGridView _fifoGrid = null!;
    private TextBox _txtExpiryKeyword = null!;
    // ---- 首件检验页（顶层第 18 个页签） ----
    private Label _lblFaiPending = null!;
    private Label _lblFaiPassed = null!;
    private Label _lblFaiFailed = null!;
    private Label _lblFaiNotReleased = null!;
    private ComboBox _cboFaiStatus = null!;
    private TextBox _txtFaiKeyword = null!;
    private DataGridView _faiGrid = null!;
    private DataGridView _faiItemGrid = null!;
    private Button _btnFaiSubmit = null!;
    private Label _lblFaiHint = null!;
    // ---- 上料防错页（顶层第 19 个页签） ----
    private Label _lblFeedTotal = null!;
    private Label _lblFeedPassed = null!;
    private Label _lblFeedRejected = null!;
    private Label _lblFeedBomRate = null!;
    private ComboBox _cboFeedWo = null!;
    private TextBox _txtFeedScan = null!;
    private Label _lblFeedResult = null!;
    private DataGridView _feedProgressGrid = null!;
    private DataGridView _feedRecordGrid = null!;
    // ---- 模组装配页（顶层第 20 个页签） ----
    private Label _lblModuleTotal = null!;
    private Label _lblModuleAssembled = null!;
    private Label _lblModuleShipped = null!;
    private Label _lblModuleCells = null!;
    private ComboBox _cboModuleStatus = null!;
    private TextBox _txtModuleKeyword = null!;
    private DataGridView _moduleGrid = null!;
    private DataGridView _moduleCellGrid = null!;
    private Button _btnModuleDismantle = null!;
    // ---- 点检保养页（顶层第 21 个页签） ----
    private Label _lblTpmTotal = null!;
    private Label _lblTpmOverdue = null!;
    private Label _lblTpmDueSoon = null!;
    private Label _lblTpmLowSpare = null!;
    private TextBox _txtTpmKeyword = null!;
    private ComboBox _cboTpmDueStatus = null!;
    private DataGridView _tpmPlanGrid = null!;
    private DataGridView _tpmRecordGrid = null!;
    private DataGridView _spareGrid = null!;
    private Button _btnTpmExecute = null!;
    private Button _btnSpareIssue = null!;
    // ---- 预警中心页（顶层第 22 个页签） ----
    private Label _lblAlertHigh = null!;          // 汇总卡：高危条数
    private Label _lblAlertMedium = null!;        // 汇总卡：中危条数
    private Label _lblAlertLow = null!;           // 汇总卡：低危条数
    private Label _lblAlertTotal = null!;         // 汇总卡：合计条数
    private ComboBox _cboAlertSource = null!;     // 筛选：来源模块
    private ComboBox _cboAlertSeverity = null!;   // 筛选：严重度
    private DataGridView _alertGrid = null!;      // 预警明细表
    private Button _btnAlertNavigate = null!;     // 操作：跳转处理
    private Label _lblAlertHint = null!;          // 筛选条提示 + 跳转回执
    // ---- 人员与资质页（顶层第 23 个页签） ----
    private Label _lblPsOnDuty = null!;          // 汇总卡：在册人数
    private Label _lblPsCertified = null!;        // 汇总卡：持证人数
    private Label _lblPsExpiring = null!;         // 汇总卡：资质即将到期项数
    private Label _lblPsInvalid = null!;          // 汇总卡：资质已失效项数
    private ComboBox _cboPsDept = null!;          // 筛选：部门
    private ComboBox _cboPsCertStatus = null!;    // 筛选：资质状态
    private TextBox _txtPsKeyword = null!;        // 筛选：关键字
    private DataGridView _psGrid = null!;         // 人员台账
    private DataGridView _psCertGrid = null!;     // 资质明细
    private DataGridView _psMatrixGrid = null!;   // 资质矩阵（人员 × 资质项）
    private DataGridView _psCrewGrid = null!;     // 班组绩效
    private ComboBox _cboPsCheckEmp = null!;      // 单人校验：人员
    private ComboBox _cboPsCheckProcess = null!;  // 单人校验：工序
    private Label _lblPsCheckResult = null!;      // 单人校验：结论
    private Button _btnPsCheck = null!;           // 单人校验：按钮
    private CheckBox _chkPsOnlyFailed = null!;    // 实绩核查：只看不通过
    private DataGridView _psCheckGrid = null!;    // 实绩资质核查
    private Button _btnPsEdit = null!;            // 操作：编辑人员
    private Button _btnPsRevoke = null!;          // 操作：吊销资质
    // ---- 客户投诉与 8D 页（顶层第 24 个页签） ----
    private Label _lblCmpPending = null!;         // 汇总卡：待受理
    private Label _lblCmpInvestigating = null!;   // 汇总卡：处理中
    private Label _lblCmpWaiting = null!;         // 汇总卡：待客户确认
    private Label _lblCmpOverdue = null!;         // 汇总卡：超期未关闭
    private ComboBox _cboCmpStatus = null!;       // 筛选：状态
    private TextBox _txtCmpKeyword = null!;       // 筛选：关键字
    private DataGridView _cmpGrid = null!;        // 客诉台账
    private DataGridView _cmpEightDGrid = null!;  // 8D 八步
    private DataGridView _cmpHistoryGrid = null!; // 流转履历
    private DataGridView _cmpCellGrid = null!;    // 触发范围：受影响电芯
    private DataGridView _cmpLotGrid = null!;     // 触发范围：受影响材料批次
    private DataGridView _cmpStatGrid = null!;    // 客户维度统计
    private Label _lblCmpEightDProgress = null!;  // 8D 进度说明
    private Label _lblCmpImpactSummary = null!;   // 触发范围反查结论
    private Button _btnCmpAdvance = null!;        // 操作：推进 8D
    private Button _btnCmpEditStep = null!;       // 操作：修订步骤
    private Button _btnCmpConfirm = null!;        // 操作：客户确认
    private Button _btnCmpClose = null!;          // 操作：关闭客诉
    private Button _btnCmpFreeze = null!;         // 操作：冻结影响范围

    // 自动刷新（底部状态栏 + 定时器）
    private System.Windows.Forms.Timer _autoTimer = null!;
    private CheckBox _chkAutoRefresh = null!;
    private NumericUpDown _numMinutes = null!;
    private Label _lblAutoStatus = null!;
    private DateTime _nextRefreshTime;
    private const int DefaultRefreshMinutes = 5;
    private const int MinRefreshMinutes = 1;
    private const int MaxRefreshMinutes = 60;

    // ---------- 列头中文映射 ----------
    /// <summary>Dictionary：（说明待补充）</summary>
    private static readonly Dictionary<string, string> WoHeaders = new()
    {
        ["WoNo"] = "工单号", ["ErpOrderNo"] = "ERP订单号", ["ProductModel"] = "产品型号", ["Line"] = "产线",
        ["PlanQty"] = "计划数", ["FinishedQty"] = "完成数", ["NgQty"] = "不良数",
        ["Status"] = "状态", ["StartDate"] = "开始日期", ["DueDate"] = "交期"
    };
    /// <summary>Dictionary：（说明待补充）</summary>
    private static readonly Dictionary<string, string> EqHeaders = new()
    {
        ["Code"] = "设备编号", ["Name"] = "名称", ["Section"] = "工序段",
        ["Status"] = "状态", ["Oee"] = "OEE(%)", ["TodayOutput"] = "今日产出", ["LastEvent"] = "最近事件"
    };
    /// <summary>Dictionary：（说明待补充）</summary>
    private static readonly Dictionary<string, string> MatHeaders = new()
    {
        ["LotNo"] = "批次号", ["MaterialName"] = "材料", ["Supplier"] = "供应商",
        ["SupplierLot"] = "供应商批号", ["ReceivedDate"] = "到货日期", ["IqcResult"] = "IQC"
    };
    /// <summary>Dictionary：（说明待补充）</summary>
    private static readonly Dictionary<string, string> ProcHeaders = new()
    {
        ["ProcessName"] = "工序", ["Equipment"] = "设备", ["Operator"] = "操作员",
        ["Shift"] = "班次", ["ParamName"] = "参数", ["ParamValue"] = "参数值", ["Time"] = "时间",
        ["DeviceStatusAt"] = "设备状态"   // 生产该工序时刻的设备状态；自动生成列排于末列
    };
    /// <summary>Dictionary：（说明待补充）</summary>
    private static readonly Dictionary<string, string> TestHeaders = new()
    {
        ["CellSn"] = "电芯SN", ["CapacityF"] = "容量(F)", ["EsrMohm"] = "ESR(mΩ)",
        ["LeakageUa"] = "漏电流(μA)", ["SelfDischarge"] = "自放电率(%)",
        ["Tester"] = "测试仪", ["TestTime"] = "测试时间", ["Passed"] = "判定"
    };
    /// <summary>Dictionary：（说明待补充）</summary>
    private static readonly Dictionary<string, string> ActualHeaders = new()
    {
        ["CellSn"] = "电芯SN", ["WoNo"] = "工单", ["ProductModel"] = "型号",
        ["ProcessName"] = "工序", ["Equipment"] = "设备", ["Operator"] = "操作员",
        ["Shift"] = "班次", ["ParamName"] = "参数", ["ParamValue"] = "参数值", ["Time"] = "时间"
    };

    /// <summary>MainForm：（说明待补充）</summary>
    public MainForm()
    {
        Text = "MES 原型 - WinForms";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        Font = new Font("微软雅黑", 9F);

        // AI 助手后端：按本机配置决定走「云端大模型」还是「本地规则引擎」。
        // 配置齐全（启用 + 有 Key）才挂云端；否则一律本地计算，数据不出本机。
        // 这就是"接大模型只改一行"的那一行——界面与页面完全不需要知道后端是谁。
        AssistantEngine.Backend = AssistantBackendFactory.CreateFromSettings();

        // 顶层容器：页签 = 一级业务分类（字号加大加粗以示层级）
        // 注意：一级容器字体必须与二级容器显式区分——WinForms 字体沿控件树继承，
        //       二级容器若不显式设回 9F，页内所有控件都会继承成 11F 粗体。
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 11F, FontStyle.Regular),
            Padding = new Point(22, 7)
        };

        // ========== 按业务模块归类注册页面（一级分类 → 二级功能页面） ==========
        // 分类顺序即顶部页签顺序；同一分类内按注册顺序排列。

        // 【总览】管理者视角的入口：先看全局，再看待办，再直接问
        RegisterPage(NavOverview, BuildDashboardTab());      // 生产看板：产量 / WIP / 良率 / 运行设备
        RegisterPage(NavOverview, BuildAlertCenterTab());    // 预警中心：聚合其余所有页面的待处理事项
        RegisterPage(NavOverview, BuildAssistantTab());      // AI 助手：一句话问现状，回答带数据依据并可跳转

        // 【生产管理】从工单下达到装配下线的执行主线
        RegisterPage(NavProduction, BuildWorkOrdersTab());   // 工单管理：新建 / 确认生产 / 终结 / ERP 下发报工
        RegisterPage(NavProduction, BuildActualsTab());      // 工序实绩：设备上传实绩 + 检验录入
        RegisterPage(NavProduction, BuildWipTab());          // WIP 看板：在制品在各工序的分布
        RegisterPage(NavProduction, BuildFeedingTab());      // 上料防错：上料校验，防止用错料
        RegisterPage(NavProduction, BuildFaiTab());          // 首件检验：换型/开班首件判定
        RegisterPage(NavProduction, BuildModuleTab());       // 模组装配：模组级装配与追溯
        RegisterPage(NavProduction, BuildSchedulingTab());   // 智能排产：历史工单推理 → 投产数/线体/工期 + 齐套预警

        // 【质量管理】不合格的发现 → 隔离 → 处置 → 归零闭环
        RegisterPage(NavQuality, BuildAbnormalTab());        // 异常登记：发现异常时录单
        RegisterPage(NavQuality, BuildAbnormalManageTab());  // 异常管理：闭环分析 / 打印异常单
        RegisterPage(NavQuality, BuildHoldManageTab());      // 保留管理：不合格品隔离与解保
        RegisterPage(NavQuality, BuildReworkTab());          // 返工管理：返工 → 重检 → 放行 / 报废
        RegisterPage(NavQuality, BuildSpcTab());             // SPC 分析：控制图与过程能力、参数预警
        RegisterPage(NavQuality, BuildSpcConfigTab());       // SPC 项目管理：控制限与版本生效管理
        RegisterPage(NavQuality, BuildComplaintTab());       // 客户投诉与 8D：客诉归零 + 影响范围反查

        // 【仓储与出货】物料入库到成品出库
        RegisterPage(NavWarehouse, BuildWmsTab());           // WMS 仓储：单据 / 库存台账 / 效期与 FIFO
        RegisterPage(NavWarehouse, BuildShipmentTab());      // 出货与召回：出库单绑定 + 召回反查
        RegisterPage(NavWarehouse, BuildMaterialHoldTab());  // 资材追溯：按料号/批次反查电芯并批量保留
        RegisterPage(NavWarehouse, BuildPurchaseReqTab());   // 资材采购申请：工单缺料 → 请购 → 审批 → 下单

        // 【设备与人员】六维里的「机」与「人」
        RegisterPage(NavResource, BuildEquipmentsTab());     // 设备监控：设备状态一览
        RegisterPage(NavResource, BuildTpmTab());            // 点检保养：点检计划 / 执行记录 / 备件台账
        RegisterPage(NavResource, BuildPersonnelTab());      // 人员与资质：资质矩阵 + 无资质不能上岗

        // 【追溯与报表】查数据、出材料
        _tracePage = RegisterPage(NavTrace, BuildTraceTab());// SN 追溯：单只电芯的全链追溯
        RegisterPage(NavTrace, BuildTestsTab());             // 测试数据：电性能全检数据（双击 SN 跳追溯）
        RegisterPage(NavTrace, BuildReportTab());            // 报表中心：产量 / 良率 / 异常 / OEE 与库存

        // 【帮助】手册放在最后一个分类，新人从这入门
        RegisterPage(NavHelp, BuildManualTab());             // 使用手册：模糊搜索「我要做什么」→ 告诉你去哪、点什么

        Controls.Add(_tabs);

        // 自动刷新：底部状态栏 + 定时器（默认停用，间隔 5 分钟）
        _autoTimer = new System.Windows.Forms.Timer { Interval = DefaultRefreshMinutes * 60_000 };
        _autoTimer.Tick += AutoTimer_Tick;
        Controls.Add(BuildAutoRefreshBar());
        UpdateAutoRefreshStatus();

        Shown += (_, _) => RefreshAll();
        FormClosed += (_, _) => _autoTimer.Stop();
    }

    // ================= 顶部导航：一级分类 + 二级页面 =================

    // 一级业务分类名称（集中定义，避免注册处与跳转处写错字面量不一致）
    internal const string NavOverview = "总览";
    internal const string NavProduction = "生产管理";
    internal const string NavQuality = "质量管理";
    internal const string NavWarehouse = "仓储与出货";
    internal const string NavResource = "设备与人员";
    internal const string NavTrace = "追溯与报表";
    internal const string NavHelp = "帮助";

    /// <summary>
    /// 把页面注册到指定一级分类下：分类不存在则自动创建分类页与二级页签容器。
    /// 这是唯一的页面注册入口——新增页面只需在此处按分类调用一次即可出现在导航里。
    /// </summary>
    /// <param name="category">一级分类名（用 Nav* 常量）</param>
    /// <param name="page">页面本体（各 Build*Tab() 的返回值）</param>
    /// <returns>原样返回 <paramref name="page"/>，便于链式赋值给字段引用</returns>
    internal TabPage RegisterPage(string category, TabPage page)
    {
        if (!_categoryTabs.TryGetValue(category, out var sub))
        {
            // 二级容器显式设回 9F 常规字重：阻断一级容器 11F 字体向下继承，保持页内控件外观不变
            sub = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 9F, FontStyle.Regular)
            };
            var catPage = new TabPage(category);
            catPage.Controls.Add(sub);
            _tabs.TabPages.Add(catPage);
            _categoryTabs[category] = sub;
            _categoryPages[category] = catPage;
        }

        sub.TabPages.Add(page);
        _pageCategory[page] = category;
        _allPages.Add(page);
        return page;
    }

    /// <summary>选中某个页面：先切到它所属的一级分类，再切到该页面本身（跨层跳转的唯一实现）。</summary>
    internal void ActivatePage(TabPage? page)
    {
        if (page is null) return;
        if (!_pageCategory.TryGetValue(page, out var category)) return;

        if (_categoryPages.TryGetValue(category, out var catPage))
            _tabs.SelectedTab = catPage;
        if (_categoryTabs.TryGetValue(category, out var sub))
            sub.SelectedTab = page;
    }

    /// <summary>按页签标题在全部二级页面中查找（标题需完全一致）。</summary>
    internal TabPage? FindPage(string title)
        => _allPages.FirstOrDefault(p => string.Equals(p.Text, title, StringComparison.Ordinal));

    /// <summary>当前选中的二级页面（取不到时返回 null）。</summary>
    internal TabPage? CurrentPage
    {
        get
        {
            if (_tabs.SelectedTab is not TabPage catPage) return null;
            return _categoryTabs.TryGetValue(catPage.Text, out var sub) ? sub.SelectedTab : null;
        }
    }

    /// <summary>全部一级分类名（按显示顺序）。</summary>
    internal IReadOnlyList<string> CategoryNames
        => _tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToList();

    /// <summary>取某个一级分类下的全部二级页面（按显示顺序），供手册与导航展示。</summary>
    internal IReadOnlyList<TabPage> PagesOf(string category)
        => _categoryTabs.TryGetValue(category, out var sub)
            ? sub.TabPages.Cast<TabPage>().ToList()
            : new List<TabPage>();

    // ================= 自动刷新 =================
    /// <summary>构建底部自动刷新状态栏：CheckBox 开关 + 分钟输入 + 应用 + 立即刷新 + 状态文本</summary>
    private StatusStrip BuildAutoRefreshBar()
    {
        var bar = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = false,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Padding = new Padding(4, 2, 6, 2)
        };

        _chkAutoRefresh = new CheckBox
        {
            Text = "自动刷新",
            Checked = false,
            AutoSize = true,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Margin = new Padding(2, 4, 2, 4)
        };

        _numMinutes = new NumericUpDown
        {
            Minimum = MinRefreshMinutes,
            Maximum = MaxRefreshMinutes,
            DecimalPlaces = 0,
            Increment = 1,
            Value = DefaultRefreshMinutes,
            Width = 56,
            TextAlign = HorizontalAlignment.Center
        };

        var lblMinutes = new Label
        {
            Text = "分钟",
            AutoSize = true,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Margin = new Padding(2, 4, 2, 4)
        };

        var btnApply = MakeStatusButton("应用", BtnApplyRefreshSettings);
        var btnRefreshNow = MakeStatusButton("立即刷新", BtnRefreshNow);

        _lblAutoStatus = new Label
        {
            AutoSize = true,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Margin = new Padding(10, 4, 2, 4),
            Text = "自动刷新：已停用"
        };

        _chkAutoRefresh.CheckedChanged += ChkAutoRefresh_CheckedChanged;

        bar.Items.Add(new ToolStripControlHost(_chkAutoRefresh) { Margin = new Padding(4, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(_numMinutes) { Margin = new Padding(4, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(lblMinutes) { Margin = new Padding(2, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(btnApply) { Margin = new Padding(8, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(btnRefreshNow) { Margin = new Padding(6, 0, 0, 0) });
        bar.Items.Add(new ToolStripStatusLabel(" ") { Spring = true });
        bar.Items.Add(new ToolStripControlHost(_lblAutoStatus) { Margin = new Padding(0, 0, 4, 0) });

        return bar;
    }

    /// <summary>紧凑状态栏按钮（白底深蓝字，与深蓝状态栏呼应）</summary>
    private static Button MakeStatusButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(82, 26),
            Margin = new Padding(2, 0, 2, 0),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(15, 76, 129),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        b.Click += onClick;
        return b;
    }

    /// <summary>「自动刷新」勾选变化：勾选即按当前分钟数启用，取消即停用</summary>
    private void ChkAutoRefresh_CheckedChanged(object? sender, EventArgs e) => ApplyAutoRefreshSettings();

    /// <summary>「应用」：读取分钟数（1~60）并应用到定时器；非法输入提示并回退</summary>
    private void BtnApplyRefreshSettings(object? sender, EventArgs e) => ApplyAutoRefreshSettings();

    /// <summary>「立即刷新」：手动触发一次 RefreshAll，刷新后重置下次刷新时间</summary>
    private void BtnRefreshNow(object? sender, EventArgs e)
    {
        RefreshAll();
        if (_autoTimer.Enabled)
            _nextRefreshTime = DateTime.Now.AddMilliseconds(_autoTimer.Interval);
        UpdateAutoRefreshStatus();
    }

    /// <summary>统一应用设置：校验分钟数(1~60) → 设置 Interval(分钟×60000ms) → 按勾选状态启停 → 更新状态文本</summary>
    private void ApplyAutoRefreshSettings()
    {
        var minutes = (int)Math.Round(_numMinutes.Value);
        if (minutes < MinRefreshMinutes || minutes > MaxRefreshMinutes)
        {
            Warn($"刷新间隔需在 {MinRefreshMinutes}~{MaxRefreshMinutes} 分钟之间，已恢复为 {DefaultRefreshMinutes} 分钟。");
            _numMinutes.Value = DefaultRefreshMinutes;
            return;
        }

        _autoTimer.Interval = minutes * 60_000;
        if (_chkAutoRefresh.Checked)
        {
            _autoTimer.Start();
            _nextRefreshTime = DateTime.Now.AddMilliseconds(_autoTimer.Interval);
        }
        else
        {
            _autoTimer.Stop();
            _nextRefreshTime = DateTime.MinValue;
        }
        UpdateAutoRefreshStatus();
    }

    /// <summary>定时器到点：先停避免重入 → 刷新全部页面数据 → 更新「下次刷新」时间并恢复运行</summary>
    private void AutoTimer_Tick(object? sender, EventArgs e)
    {
        _autoTimer.Stop();
        try
        {
            RefreshAll();
            if (_chkAutoRefresh.Checked)
            {
                _nextRefreshTime = DateTime.Now.AddMilliseconds(_autoTimer.Interval);
                _autoTimer.Start();
            }
        }
        finally
        {
            UpdateAutoRefreshStatus();
        }
    }

    /// <summary>更新状态栏文本：启用 → 间隔 + 下次刷新时间；停用 → 已停用</summary>
    private void UpdateAutoRefreshStatus()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_lblAutoStatus is null) return;
        if (_autoTimer is not null && _autoTimer.Enabled)
        {
            var minutes = (int)Math.Round(_autoTimer.Interval / 60_000.0);
            _lblAutoStatus.Text = $"自动刷新：启用 · 每 {minutes} 分钟 · 下次刷新 {_nextRefreshTime:HH:mm:ss}";
        }
        else
        {
            _lblAutoStatus.Text = "自动刷新：已停用";
        }
    }

    // ================= 刷新 =================
    /// <summary>刷新「All」的显示。</summary>
    private void RefreshAll()
    {
        var (output, wip, yield, running) = MesDatabase.GetDashboard();
        _lblOutput.Text = output.ToString("N0");
        _lblWip.Text = wip.ToString("N0");
        _lblYield.Text = yield.ToString("0.0");
        _lblRunningEq.Text = $"{running} / {MesDatabase.Equipments.Count}";

        BindGrid(_dashWoGrid, MesDatabase.WorkOrders.Where(w => w.Status == "生产中").ToList(), WoHeaders);
        BindGrid(_dashEqGrid, MesDatabase.Equipments, EqHeaders);
        BindWorkOrdersGrid();
        BindGrid(_testGrid, MesDatabase.TestRecords.OrderByDescending(t => t.TestTime).ToList(), TestHeaders);
        BindGrid(_eqGrid, MesDatabase.Equipments, EqHeaders);
        EnsureShipmentReady();   // 出货与召回页兜底初始化（幂等：未加载时才查询，始终刷新明细）
        EnsureWipReady();        // WIP 看板兜底初始化（幂等）
        EnsureReworkReady();     // 返工管理页兜底初始化（幂等）
        EnsureReportReady();     // 报表中心兜底初始化（幂等）
        EnsureFaiReady();        // 首件检验兜底初始化（幂等）
        EnsureFeedingReady();    // 上料防错兜底初始化（幂等）
        EnsureModuleReady();     // 模组装配兜底初始化（幂等）
        EnsureTpmReady();        // 点检保养兜底初始化（幂等）
        EnsureAlertReady();      // 预警中心兜底初始化（幂等）
        EnsurePersonnelReady();  // 人员与资质兜底初始化（幂等）
        EnsureComplaintReady();  // 客户投诉与 8D 兜底初始化（幂等）
        EnsureManualReady();     // 使用手册兜底初始化（幂等，空查询=浏览全部条目）
        EnsureAssistantReady();  // AI 助手兜底初始化（幂等：写欢迎语 + 渲染边界说明）
        EnsureScheduleReady();   // 智能排产兜底初始化（幂等：载入待下发 PO 并生成首张建议）
        EnsurePurchaseReady(); // 资材采购申请兜底初始化（幂等：载入申请单列表）
    }

    /// <summary>执行「Trace」业务动作（校验 → 执行 → 刷新界面）。</summary>
    private void DoTrace()
    {
        var sn = _txtSn.Text.Trim();
        if (string.IsNullOrEmpty(sn)) return;

        var result = MesDatabase.GetTrace(sn);
        if (result is null)
        {
            MessageBox.Show($"未找到电芯 SN：{sn}\n\n示例：SC-A260812-L1-00001",
                "查询结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _lblCellInfo.Text = "";
            _lblEnv.Text = "";
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _matGrid.DataSource = null;
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _procGrid.DataSource = null;
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _traceTestGrid.DataSource = null;
            return;
        }

        var cell = result.Cell!;
        var status = cell.Result == "NG" ? "不合格" :
                     string.IsNullOrEmpty(cell.Result) ? "待分档" : $"合格（{cell.Result}档）";
        _lblCellInfo.Text =
            $"SN：{cell.Sn}\r\n" +
            $"产品型号：{cell.ProductModel}    生产工单：{cell.WoNo}\r\n" +
            $"产线：{cell.Line}    状态：{status}\r\n" +
            $"极片批号：{cell.ElectrodeLot}    电解液批号：{cell.ElectrolyteLot}\r\n" +
            $"所属模组：{cell.ModuleSn}    生成时间：{cell.CreateTime:yyyy-MM-dd HH:mm:ss}";

        _lblEnv.Text =
            $"采样点：{result.EnvPoint}\r\n" +
            $"露点：{result.EnvDewPoint}    温度：{result.EnvTemp}    湿度：{result.EnvHumidity}";

        BindGrid(_matGrid, result.Materials, MatHeaders);
        BindGrid(_procGrid, result.Processes, ProcHeaders);
        BindGrid(_traceTestGrid, result.Tests, TestHeaders);
    }

    /// <summary>工序参数记录：生产该工序时设备非"运行"（待机/停机/维护）的行整行标红；运行/—/空保持默认。</summary>
    private void ProcGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Rows[e.RowIndex].DataBoundItem is not ProcessRecord p) return;

        var statusAt = p.DeviceStatusAt;
        if (string.IsNullOrWhiteSpace(statusAt) || statusAt == DeviceStatus.Running || statusAt == "—") return;

        // 仅命中非运行行：整行浅红底 + 深红字；选中态用红系保证可读
        e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
        e.CellStyle.BackColor = Color.FromArgb(255, 235, 235);
        e.CellStyle.SelectionForeColor = Color.FromArgb(192, 0, 0);
        e.CellStyle.SelectionBackColor = Color.FromArgb(255, 200, 200);
    }

    // ================= 通用控件工厂 =================
    /// <summary>创建「Card」控件/样式（统一样式的工厂方法）。</summary>
    private static Panel MakeCard(string title, Color accent, out Label valueLabel)
    {
        var p = new Panel
        {
            Size = new Size(270, 86),
            Margin = new Padding(8, 4, 8, 4),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var t = new Label
        {
            Text = title,
            Location = new Point(14, 8),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("微软雅黑", 9F)
        };
        valueLabel = new Label
        {
            Location = new Point(14, 32),
            AutoSize = true,
            ForeColor = accent,
            Font = new Font("微软雅黑", 18F, FontStyle.Bold)
        };
        p.Controls.Add(t);
        p.Controls.Add(valueLabel);
        return p;
    }

    /// <summary>创建「GridGroup」控件/样式（统一样式的工厂方法）。</summary>
    private static Control MakeGridGroup(string title, out DataGridView grid)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6, 12, 6, 6) };
        grid = MakeGrid();
        box.Controls.Add(grid);
        return box;
    }

    /// <summary>创建「Grid」控件/样式（统一样式的工厂方法）。</summary>
    private static DataGridView MakeGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,

            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15, 76, 129),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.Black,
                SelectionBackColor = Color.FromArgb(207, 224, 239),
                SelectionForeColor = Color.Black,
                Font = new Font("微软雅黑", 9F)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) }
        };
        return g;
    }

    /// <summary>BindGrid：（说明待补充）</summary>
    private static void BindGrid(DataGridView grid, object? dataSource, Dictionary<string, string> headers)
    {
        grid.DataSource = null;
        grid.DataSource = dataSource;

        foreach (DataGridViewColumn col in grid.Columns)
        {
            if (headers.TryGetValue(col.Name, out var header))
            {
                col.HeaderText = header;
                col.Visible = true;
                if (col.Name is "StartDate" or "DueDate" or "ReceivedDate")
                    col.DefaultCellStyle.Format = "yyyy-MM-dd";
                if (col.Name is "CreateTime" or "TestTime" or "Time")
                    col.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
                if (col.Name is "Oee")
                    col.DefaultCellStyle.Format = "0.0";
            }
            else
            {
                col.Visible = false;
            }
        }
    }
}
