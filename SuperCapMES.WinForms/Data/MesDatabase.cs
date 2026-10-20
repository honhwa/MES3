using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms.Data;

/// <summary>
/// 原型用内存数据仓储：提供种子数据与追溯查询。
/// 正式系统替换为数据库（PostgreSQL + 时序库）即可，接口保持不变。
/// </summary>
public static partial class MesDatabase
{
    public static List<WorkOrder> WorkOrders { get; } = new();
    public static List<MaterialLot> Materials { get; } = new();
    public static List<CellSn> Cells { get; } = new();
    public static List<ProcessRecord> ProcessRecords { get; } = new();
    public static List<TestRecord> TestRecords { get; } = new();
    public static List<Equipment> Equipments { get; } = new();
    public static List<ProcessActualRecord> ProcessActuals { get; } = new();
    public static List<InspectionRecord> Inspections { get; } = new();
    public static List<DeviceDailyProduction> DeviceDailyRecords { get; } = new();
    public static List<DeviceHourlyRecord> DeviceHourlyRecords { get; } = new();
    public static List<DeviceAlarm> DeviceAlarms { get; } = new();
    public static List<DeviceStatusRecord> DeviceStatusRecords { get; } = new();
    public static List<CellHold> Holds { get; } = new();
    public static List<CellHoldItem> HoldItems { get; } = new();
    public static List<HoldHistory> HoldHistories { get; } = new();
    public static List<AbnormalRecord> AbnormalRecords { get; } = new();
    public static List<AbnormalHistory> AbnormalHistories { get; } = new();

    // ---- WMS 仓储（增量模块） ----
    public static List<WmsMaterial> WmsMaterials { get; } = new();
    public static List<WmsStock> WmsStocks { get; } = new();
    public static List<WmsOrder> WmsOrders { get; } = new();
    public static List<WmsOrderLine> WmsOrderLines { get; } = new();
    public static List<WmsInspectionRecord> WmsInspections { get; } = new();
    public static List<WmsOrderHistory> WmsOrderHistories { get; } = new();

    // ---- 出货绑定（增量模块：质量追溯链最后一公里） ----
    public static List<WmsShipmentItem> WmsShipmentItems { get; } = new();

    // ---- ERP 集成（增量模块） ----
    /// <summary>ERP 采购/生产订单（PO）：模拟「从 ERP 获取最新 PO」的结果</summary>
    public static List<ErpPo> ErpPos { get; } = new();

    /// <summary>ERP BOM 明细（按产品型号）</summary>
    public static List<ErpBomItem> ErpBomItems { get; } = new();

    /// <summary>ERP 报工记录</summary>
    public static List<ErpWorkReport> ErpWorkReports { get; } = new();

    /// <summary>ERP 账面库存快照（模拟从 ERP 同步；与 MES 台账按 物料编码+批次+库位 逐键对账）</summary>
    public static List<ErpStockItem> ErpStockItems { get; } = new();

    // ---- 返工 / 返修（增量模块：质量故事线 发现→保留→返工→重检→放行） ----
    /// <summary>返工单</summary>
    public static List<ReworkOrder> ReworkOrders { get; } = new();

    /// <summary>返工流转履历</summary>
    public static List<ReworkHistory> ReworkHistories { get; } = new();

    // ---- 首件检验 FAI（增量模块：换型/开班首件必检） ----
    /// <summary>首件检验单</summary>
    public static List<FaiOrder> FaiOrders { get; } = new();

    /// <summary>首件检验明细项</summary>
    public static List<FaiItem> FaiItems { get; } = new();

    // ---- 上料防错（增量模块：扫码校验 工单-BOM-批次） ----
    /// <summary>上料（投料）记录（通过与被拒均留痕）</summary>
    public static List<FeedingRecord> FeedingRecords { get; } = new();

    // ---- 模组装配与模组级追溯（增量模块） ----
    /// <summary>模组装配记录（模组级追溯根节点）</summary>
    public static List<ModuleRecord> Modules { get; } = new();

    // ---- 点检保养 TPM 与备件（增量模块） ----
    /// <summary>点检/保养计划</summary>
    public static List<TpmPlan> TpmPlans { get; } = new();

    /// <summary>点检/保养执行记录</summary>
    public static List<TpmRecord> TpmRecords { get; } = new();

    /// <summary>备件台账</summary>
    public static List<SparePart> SpareParts { get; } = new();

    // ---- 人员与资质管理 ----
    /// <summary>人员主数据（工号唯一）</summary>
    public static List<UserProfile> Users { get; } = new();

    /// <summary>资质证书（工号 + 资质项 唯一）</summary>
    public static List<SkillCertificate> SkillCertificates { get; } = new();

    // ---- 客户投诉与 8D 归零 ----
    /// <summary>客诉单</summary>
    public static List<CustomerComplaint> Complaints { get; } = new();

    /// <summary>8D 报告（一客诉一份）</summary>
    public static List<EightDReport> EightDReports { get; } = new();

    /// <summary>客诉流转履历</summary>
    public static List<ComplaintHistory> ComplaintHistories { get; } = new();

    // ---- SPC 统计过程控制（增量模块） ----
    /// <summary>SPC 监控项定义</summary>
    public static List<SpcItem> SpcItems { get; } = new();

    /// <summary>SPC 样本点</summary>
    public static List<SpcSample> SpcSamples { get; } = new();

    /// <summary>SPC 预警记录</summary>
    public static List<SpcAlert> SpcAlerts { get; } = new();

    /// <summary>SPC 监控项规格限版本（版本化规格管理，判定真值以此为准）</summary>
    public static List<SpcItemVersion> SpcItemVersions { get; } = new();

    /// <summary>
    /// 各监控项「已用最高版本号」高水位（版本号单调不回退）：
    /// 删除待生效版本不会降低高水位，因此后续新建不会复用已删除的版本号，避免同一版本号先后对应不同规格
    /// 造成 <see cref="SpcAlert.VersionText"/> 等历史引用歧义。键忽略大小写。
    /// </summary>
    private static readonly Dictionary<string, int> _spcVersionHighWater = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SPC 预警单号序号（跨天自然延续，不按天重置）</summary>
    private static int _spcAlertSeq;

    /// <summary>保留单号序号（跨天自然延续，不按天重置）</summary>
    private static int _holdSeq;

    /// <summary>履历全局自增序号</summary>
    private static int _historySeq;

    /// <summary>异常单号序号（跨天自然延续，不按天重置）</summary>
    private static int _abSeq;

    /// <summary>异常履历全局自增序号</summary>
    private static int _abHistorySeq;

    /// <summary>WMS 单据号序号（跨天自然延续，不按天重置）</summary>
    private static int _wmsSeq;

    /// <summary>WMS 履历全局自增序号</summary>
    private static int _wmsHistorySeq;

    /// <summary>ERP 报工单号序号（跨天自然延续，不按天重置）</summary>
    private static int _erpReportSeq;

    static MesDatabase()
    {
        Seed();
        // 人员主数据必须先于任何「产生工序实绩」的播种执行：
        // 种子生成实绩时按「工序 → 持证人」派工，若此时人员尚未登记会退化为未登记人员。
        SeedPersonnel();
        SeedActuals();
        EnsureDeviceStats();
        SeedAbnormals();
        SeedWms();
        SeedShipment();
        SeedErp();
        SeedSchedulingHistory();   // 历史工单 + 完工报工：为智能排产推理提供足量样本
        SeedAssets();              // 生产资产 + 初始占用：为资产占用校验提供可冲突的样本
        SeedErpStocks();
        SeedSpc();
        SeedWipFlow();
        SeedRework();
        SeedYieldHistory();
        SeedAbnormalHistory();
        SeedFai();
        SeedFeeding();
        SeedModules();
        SeedTpm();
        SeedStockExpiry();
        SeedComplaints();
    }

    private static void Seed()
    {
        // ---- 工单 ----
        WorkOrders.AddRange(new[]
        {
            new WorkOrder { WoNo = "WO-20260812-001", ProductModel = "SC-CX2026-100F", PlanQty = 5000, FinishedQty = 4321, NgQty = 23, Status = "生产中", StartDate = new DateTime(2026,8,12,8,0,0), DueDate = new DateTime(2026,8,14,20,0,0), Line = "L1" },
            new WorkOrder { WoNo = "WO-20260813-002", ProductModel = "SC-CX2026-300F", PlanQty = 3000, FinishedQty = 1876, NgQty = 9,  Status = "生产中", StartDate = new DateTime(2026,8,13,8,0,0), DueDate = new DateTime(2026,8,16,20,0,0), Line = "L2" },
            new WorkOrder { WoNo = "WO-20260811-003", ProductModel = "SC-CX2026-50F",  PlanQty = 8000, FinishedQty = 8000, NgQty = 41, Status = "已完成", StartDate = new DateTime(2026,8,11,8,0,0), DueDate = new DateTime(2026,8,12,20,0,0), Line = "L1" },
            new WorkOrder { WoNo = "WO-20260814-004", ProductModel = "SC-CX2026-100F", PlanQty = 6000, FinishedQty = 0,    NgQty = 0,  Status = "未开始", StartDate = new DateTime(2026,8,14,8,0,0), DueDate = new DateTime(2026,8,17,20,0,0), Line = "L1" },
        });

        // ---- 材料批次 ----
        Materials.AddRange(new[]
        {
            new MaterialLot { LotNo = "PC-260812-L1-001", MaterialCode = "M-ACT-CAR-01", MaterialName = "活性炭电极材料", Supplier = "碳源科技", SupplierLot = "CY-C-20260801", ReceivedDate = new DateTime(2026,8,10), IqcResult = "合格", UsedFor = "SC-A260812-L1-00001" },
            new MaterialLot { LotNo = "EL-260812-012",   MaterialCode = "M-ELE-ACN-02", MaterialName = "电解液（ACN基）", Supplier = "电解液股份", SupplierLot = "DJY-260807-012", ReceivedDate = new DateTime(2026,8,9), IqcResult = "合格", UsedFor = "SC-A260812-L1-00001" },
            new MaterialLot { LotNo = "SP-260812-007",   MaterialCode = "M-SEP-PP-03",  MaterialName = "隔膜（PP）", Supplier = "膜材新材", SupplierLot = "MC-X-20260805", ReceivedDate = new DateTime(2026,8,8), IqcResult = "合格", UsedFor = "SC-A260812-L1-00001" },
            new MaterialLot { LotNo = "CC-260812-003",   MaterialCode = "M-COL-AL-04",  MaterialName = "铝箔集流体", Supplier = "铝业精工", SupplierLot = "LY-260801-03", ReceivedDate = new DateTime(2026,8,7), IqcResult = "合格", UsedFor = "SC-A260812-L1-00001" },
            new MaterialLot { LotNo = "PC-260813-L2-002", MaterialCode = "M-ACT-CAR-01", MaterialName = "活性炭电极材料", Supplier = "碳源科技", SupplierLot = "CY-C-20260802", ReceivedDate = new DateTime(2026,8,11), IqcResult = "合格", UsedFor = "SC-A260813-L2-00006" },
            new MaterialLot { LotNo = "EL-260813-015",   MaterialCode = "M-ELE-ACN-02", MaterialName = "电解液（ACN基）", Supplier = "电解液股份", SupplierLot = "DJY-260808-015", ReceivedDate = new DateTime(2026,8,10), IqcResult = "合格", UsedFor = "SC-A260813-L2-00006" },

            // ---- 辅料 / 包材的材料批次（2026-10-08 采购申请轮补充）----
            // 起因：供应商信息只挂在 MaterialLot 上（物料主数据里没有供应商字段），
            // 原先只给四大主材建了批次 → 采购申请单里辅料/包材的"建议供应商"全是待定，
            // 下单校验直接被拦。批次号与 WMS 库存台账里的批次保持一致，便于材料追溯反查。
            new MaterialLot { LotNo = "CAN-260810-001",  MaterialCode = "M-CAN-ALU-05", MaterialName = "铝壳外壳", Supplier = "精密金属制品厂", SupplierLot = "JX-20260805-021", ReceivedDate = new DateTime(2026,8,8), IqcResult = "合格", UsedFor = "" },
            new MaterialLot { LotNo = "SEL-260811-002",  MaterialCode = "M-SEL-RUB-06", MaterialName = "密封圈", Supplier = "氟塑密封件厂", SupplierLot = "FS-20260806-114", ReceivedDate = new DateTime(2026,8,9), IqcResult = "合格", UsedFor = "" },
            new MaterialLot { LotNo = "TAB-260811-001",  MaterialCode = "M-TAB-NIC-07", MaterialName = "镍带极耳", Supplier = "有色金属冶炼厂", SupplierLot = "YS-20260804-057", ReceivedDate = new DateTime(2026,8,7), IqcResult = "合格", UsedFor = "" },
            new MaterialLot { LotNo = "BOX-260811-001",  MaterialCode = "M-PKG-BOX-11", MaterialName = "彩盒（100F 专用）", Supplier = "华瑞包装印刷", SupplierLot = "HR-20260803-036", ReceivedDate = new DateTime(2026,8,6), IqcResult = "合格", UsedFor = "" },

            // ---- 双供应商演示（供应链碳管理·阶段四）----
            // 活性炭与电解液各补一家备选供应商的真实到货，使"同物料多供应商对比"可演示；
            // 供应商碳数据种子里对这两家分别给"已提供（更优）"与"未提供（待跟进）"以覆盖两种状态。
            new MaterialLot { LotNo = "PC-260815-B-009", MaterialCode = "M-ACT-CAR-01", MaterialName = "活性炭电极材料", Supplier = "绿源碳材", SupplierLot = "LY-C-20260812", ReceivedDate = new DateTime(2026,8,14), IqcResult = "合格", UsedFor = "" },
            new MaterialLot { LotNo = "EL-260815-B-011", MaterialCode = "M-ELE-ACN-02", MaterialName = "电解液（ACN基）", Supplier = "新能源电解液", SupplierLot = "XNY-260808-011", ReceivedDate = new DateTime(2026,8,13), IqcResult = "合格", UsedFor = "" },
        });

        // ---- 电芯 SN ----
        var cells = new[]
        {
            new CellSn { Sn = "SC-A260812-L1-00001", WoNo = "WO-20260812-001", ProductModel = "SC-CX2026-100F", Line = "L1", Status = "合格", CreateTime = new DateTime(2026,8,12,9,30,0), ElectrodeLot = "PC-260812-L1-001", ElectrolyteLot = "EL-260812-012", ModuleSn = "SM-A260813-L1-00021", Result = "A1" },
            new CellSn { Sn = "SC-A260812-L1-00002", WoNo = "WO-20260812-001", ProductModel = "SC-CX2026-100F", Line = "L1", Status = "合格", CreateTime = new DateTime(2026,8,12,9,32,0), ElectrodeLot = "PC-260812-L1-001", ElectrolyteLot = "EL-260812-012", ModuleSn = "SM-A260813-L1-00021", Result = "A1" },
            new CellSn { Sn = "SC-A260812-L1-00003", WoNo = "WO-20260812-001", ProductModel = "SC-CX2026-100F", Line = "L1", Status = "合格", CreateTime = new DateTime(2026,8,12,9,35,0), ElectrodeLot = "PC-260812-L1-001", ElectrolyteLot = "EL-260812-012", ModuleSn = "SM-A260813-L1-00021", Result = "B1" },
            new CellSn { Sn = "SC-A260812-L1-00004", WoNo = "WO-20260812-001", ProductModel = "SC-CX2026-100F", Line = "L1", Status = "不良", CreateTime = new DateTime(2026,8,12,9,38,0), ElectrodeLot = "PC-260812-L1-001", ElectrolyteLot = "EL-260812-012", ModuleSn = "", Result = "NG" },
            new CellSn { Sn = "SC-A260812-L1-00005", WoNo = "WO-20260812-001", ProductModel = "SC-CX2026-100F", Line = "L1", Status = "化成中", CreateTime = new DateTime(2026,8,12,10,0,0), ElectrodeLot = "PC-260812-L1-001", ElectrolyteLot = "EL-260812-012", ModuleSn = "", Result = "" },
            new CellSn { Sn = "SC-A260813-L2-00006", WoNo = "WO-20260813-002", ProductModel = "SC-CX2026-300F", Line = "L2", Status = "合格", CreateTime = new DateTime(2026,8,13,9,20,0), ElectrodeLot = "PC-260813-L2-002", ElectrolyteLot = "EL-260813-015", ModuleSn = "SM-A260813-L2-00009", Result = "A2" },
            new CellSn { Sn = "SC-A260813-L2-00007", WoNo = "WO-20260813-002", ProductModel = "SC-CX2026-300F", Line = "L2", Status = "检测中", CreateTime = new DateTime(2026,8,13,9,25,0), ElectrodeLot = "PC-260813-L2-002", ElectrolyteLot = "EL-260813-015", ModuleSn = "", Result = "" },
        };
        Cells.AddRange(cells);

        // ---- 工序记录（参数挂载到 SN） ----
        var procs = new List<ProcessRecord>();
        foreach (var c in cells)
        {
            procs.Add(new ProcessRecord { CellSn = c.Sn, ProcessName = "卷绕", Equipment = "WIND-L1-01", Operator = "张伟", Shift = "白班", ParamName = "卷绕张力", ParamValue = "1.8 N", Time = c.CreateTime });
            procs.Add(new ProcessRecord { CellSn = c.Sn, ProcessName = "入壳", Equipment = "CAN-L1-01", Operator = "张伟", Shift = "白班", ParamName = "入壳对位精度", ParamValue = "±0.05 mm", Time = c.CreateTime.AddMinutes(3) });
            procs.Add(new ProcessRecord { CellSn = c.Sn, ProcessName = "注液", Equipment = "INJ-L1-02", Operator = "李娜", Shift = "白班", ParamName = "注液量", ParamValue = "3.2 mL", Time = c.CreateTime.AddMinutes(6) });
            procs.Add(new ProcessRecord { CellSn = c.Sn, ProcessName = "封口", Equipment = "SEAL-L1-01", Operator = "李娜", Shift = "白班", ParamName = "封口温度", ParamValue = "210 ℃", Time = c.CreateTime.AddMinutes(8) });
            procs.Add(new ProcessRecord { CellSn = c.Sn, ProcessName = "化成", Equipment = "FORM-A01-C12", Operator = "王强", Shift = "白班", ParamName = "化成电流/时间", ParamValue = "0.5C / 240 min", Time = c.CreateTime.AddHours(1) });
        }
        ProcessRecords.AddRange(procs);

        // ---- 测试数据（容量 / ESR / 漏电流 / 自放电） ----
        var rnd = new Random(42);
        TestRecords.AddRange(cells.Select(c => new TestRecord
        {
            CellSn = c.Sn,
            CapacityF = c.Result == "NG" ? 88.4 : Math.Round(100.0 + (rnd.NextDouble() - 0.5) * 4, 2),
            EsrMohm = c.Result == "NG" ? 42.1 : Math.Round(18.5 + rnd.NextDouble() * 3, 2),
            LeakageUa = c.Result == "NG" ? 28.6 : Math.Round(3.2 + rnd.NextDouble() * 2, 2),
            SelfDischarge = Math.Round(0.9 + rnd.NextDouble() * 1.5, 2),
            Tester = c.Line == "L1" ? "TST-L1-03" : "TST-L2-03",
            TestTime = c.CreateTime.AddDays(0).AddHours(6),
            Passed = c.Result != "NG"
        }));

        // ---- 演示用「检验不合格」电芯（不在任何保留单中，供「质量扫描自动保留」演示新增保留单）----
        // SC-A260812-L1-00004 已是 NG 且已被种子保留单 HLD-20260813-001 保留，故另取未保留的 00006 造一条检验不合格记录。
        var demoNgCell = Cells.FirstOrDefault(c => c.Sn == "SC-A260813-L2-00006");
        if (demoNgCell is not null)
        {
            Inspections.Add(new InspectionRecord
            {
                CellSn = demoNgCell.Sn,
                Equipment = "WIND-L2-01",
                ProcessName = "卷绕",
                ItemName = "卷绕张力",
                StandardValue = "1.5~2.0 N",
                MeasuredValue = "2.4 N",
                Result = "不合格",
                Inspector = "seed",
                InspectTime = demoNgCell.CreateTime.AddHours(7)
            });
        }

        // ---- 设备 ----
        Equipments.AddRange(new[]
        {
            new Equipment { Code = "COAT-L1-01", Name = "涂布机", Section = "电极制备", Status = "运行", Oee = 86.5, TodayOutput = 3260, LastEvent = "正常" },
            new Equipment { Code = "WIND-L1-01", Name = "卷绕机", Section = "电芯成型", Status = "运行", Oee = 91.2, TodayOutput = 3510, LastEvent = "正常" },
            new Equipment { Code = "INJ-L1-02", Name = "注液机", Section = "注液封口", Status = "运行", Oee = 88.0, TodayOutput = 3420, LastEvent = "正常" },
            new Equipment { Code = "FORM-A01", Name = "化成柜组A", Section = "化成老化", Status = "运行", Oee = 95.3, TodayOutput = 2980, LastEvent = "正常" },
            new Equipment { Code = "TST-L1-03", Name = "电性能测试仪", Section = "分选检测", Status = "运行", Oee = 83.9, TodayOutput = 3180, LastEvent = "正常" },
            new Equipment { Code = "DRY-RM-01", Name = "干燥房", Section = "环境", Status = "运行", Oee = 99.1, TodayOutput = 0, LastEvent = "露点 -52.3℃ 正常" },
            new Equipment { Code = "SEAL-L1-01", Name = "封口机", Section = "注液封口", Status = "维护", Oee = 62.4, TodayOutput = 1540, LastEvent = "计划维护" },
        });

        // ---- 保留演示数据：SC-A260812-L1-00004 电性能复测不合格，保留在化成外观 ----
        var demoCell = Cells.FirstOrDefault(c => c.Sn == "SC-A260812-L1-00004");
        if (demoCell is not null)
        {
            const string demoHoldNo = "HLD-20260813-001";
            var demoHoldTime = new DateTime(2026, 8, 13, 10, 30, 0);
            Holds.Add(new CellHold
            {
                HoldNo = demoHoldNo,
                MaterialCode = "M-ACT-CAR-01",
                MaterialName = "活性炭电极材料",
                LotNo = "PC-260812-L1-001",
                HoldLocation = HoldLocations.FormationInspection,
                Reason = "电性能复测不合格，待评审",
                Operator = "seed",
                HoldTime = demoHoldTime,
                Status = HoldStatus.Active
            });
            HoldItems.Add(new CellHoldItem { HoldNo = demoHoldNo, CellSn = demoCell.Sn });
            HoldHistories.Add(new HoldHistory
            {
                Seq = 1,
                HoldNo = demoHoldNo,
                ActionType = HoldActionType.Hold,
                Operator = "seed",
                Time = demoHoldTime,
                Reason = "电性能复测不合格，待评审",
                CellCount = 1,
                SnPreview = demoCell.Sn
            });
            _holdSeq = 1;
            _historySeq = 1;
        }
    }

    // ---------------- 查询 ---------------- 

    /// <summary>按 SN 精确查询电芯</summary>
    public static CellSn? FindCell(string sn) =>
        Cells.FirstOrDefault(c => string.Equals(c.Sn, sn.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>构建单体内外全链路追溯结果</summary>
    public static TraceResult? GetTrace(string sn)
    {
        var cell = FindCell(sn);
        if (cell is null) return null;

        var trace = new TraceResult
        {
            Cell = cell,
            Materials = Materials.Where(m => m.UsedFor == cell.Sn
                || (cell.ElectrodeLot.Length > 0 && m.LotNo == cell.ElectrodeLot)
                || (cell.ElectrolyteLot.Length > 0 && m.LotNo == cell.ElectrolyteLot))
                .DistinctBy(m => m.LotNo)
                .ToList(),
            Processes = ProcessRecords.Where(p => p.CellSn == cell.Sn).OrderBy(p => p.Time).ToList(),
            Tests = TestRecords.Where(t => t.CellSn == cell.Sn).ToList(),
            EnvDewPoint = cell.Line == "L1" ? "-52.3 ℃" : "-49.8 ℃",
            EnvTemp = cell.Line == "L1" ? "21.5 ℃" : "22.0 ℃",
            EnvHumidity = cell.Line == "L1" ? "1.2 %RH" : "1.5 %RH",
            EnvPoint = cell.Line == "L1" ? "干燥房 L1 区 · 采样点 D-103" : "干燥房 L2 区 · 采样点 D-207"
        };

        // 材料未单独入库时，用极片/电解液批号兜底展示
        if (trace.Materials.Count == 0 && !string.IsNullOrEmpty(cell.ElectrodeLot))
        {
            trace.Materials.Add(new MaterialLot
            {
                LotNo = cell.ElectrodeLot,
                MaterialName = "活性炭电极材料（极片批次）",
                Supplier = "碳源科技",
                IqcResult = "合格",
                UsedFor = cell.Sn
            });
        }
        if (trace.Materials.Count == 0 && !string.IsNullOrEmpty(cell.ElectrolyteLot))
        {
            trace.Materials.Add(new MaterialLot
            {
                LotNo = cell.ElectrolyteLot,
                MaterialName = "电解液（ACN基）",
                Supplier = "电解液股份",
                IqcResult = "合格",
                UsedFor = cell.Sn
            });
        }

        // 工序挂载"生产时设备状态"：判定设备在该工序发生时刻是否为"运行"；无法判定显示 "—"
        foreach (var p in trace.Processes)
            p.DeviceStatusAt = GetDeviceStatusAt(p.Equipment, p.Time) ?? "—";

        return trace;
    }

    /// <summary>看板统计</summary>
    public static (int TodayOutput, int Wip, double Yield, int RunningEq) GetDashboard()
    {
        int todayOutput = WorkOrders
            .Where(w => w.Status == "生产中")
            .Sum(w => w.FinishedQty);
        int wip = WorkOrders
            .Where(w => w.Status == "生产中")
            .Sum(w => w.PlanQty - w.FinishedQty - w.NgQty);
        var tested = TestRecords.Count;
        var passed = TestRecords.Count(t => t.Passed);
        double yield = tested == 0 ? 100 : Math.Round(passed * 100.0 / tested, 2);
        int running = Equipments.Count(e => e.Status == "运行");
        return (todayOutput, wip, yield, running);
    }

    // ============================================================
    // 设备工序实绩 & 检验
    // ============================================================

    /// <summary>设备 → 工序映射</summary>
    public static readonly Dictionary<string, string> DeviceProcessMap = new()
    {
        ["COAT-L1-01"] = "涂布",
        ["WIND-L1-01"] = "卷绕",
        ["INJ-L1-02"] = "注液",
        ["SEAL-L1-01"] = "封口",
        ["FORM-A01"] = "化成",
        ["TST-L1-03"] = "电性能检测",
    };

    /// <summary>检验项目模板（按工序）</summary>
    public static readonly Dictionary<string, List<(string Item, string Standard)>> InspectionTemplates = new()
    {
        ["涂布"] = new List<(string, string)> { ("涂布厚度", "95~110 μm"), ("面密度偏差", "±2 %") },
        ["卷绕"] = new List<(string, string)> { ("卷绕张力", "1.5~2.0 N"), ("极片对位精度", "±0.05 mm"), ("隔膜包覆", "无褶皱") },
        ["注液"] = new List<(string, string)> { ("注液量", "3.0~3.4 mL"), ("外观检查", "无划痕") },
        ["封口"] = new List<(string, string)> { ("封口温度", "200~220 ℃"), ("封口密封性", "无泄漏") },
        ["化成"] = new List<(string, string)> { ("化成电流", "0.4~0.6 C"), ("化成时间", "≥240 min"), ("端电压", "2.70±0.05 V") },
        ["电性能检测"] = new List<(string, string)> { ("容量", "≥95 F"), ("ESR", "≤25 mΩ"), ("漏电流", "≤8 μA"), ("自放电率", "≤3 %") },
    };

    /// <summary>模拟设备上传：为若干电芯生成指定设备的工序实绩</summary>
    public static int SimulateDeviceUpload(string equipmentCode)
    {
        if (!DeviceProcessMap.TryGetValue(equipmentCode, out var process)) return 0;

        var cells = Cells.Where(c => c.Status is "在制" or "化成中" or "检测中" or "合格").ToList();
        if (cells.Count == 0) cells = Cells.Take(6).ToList();
        cells = cells.Take(6).ToList();

        var rnd = new Random();
        var added = 0;
        foreach (var c in cells)
        {
            var (pname, pvalue) = MakeParam(process, rnd);
            ProcessActuals.Add(new ProcessActualRecord
            {
                CellSn = c.Sn,
                WoNo = c.WoNo,
                ProductModel = c.ProductModel,
                ProcessName = process,
                Equipment = equipmentCode,
                // 按资质派工：优先持该工序上岗证者，少量刻意派无证人（供资质核查演示）
                Operator = PickOperator(process, rnd, "张伟"),
                Shift = rnd.Next(2) == 0 ? "白班" : "夜班",
                ParamName = pname,
                ParamValue = pvalue,
                Time = DateTime.Now.AddMinutes(-rnd.Next(240))
            });
            added++;
        }
        return added;
    }

    private static void SeedActuals()
    {
        foreach (var code in new[] { "WIND-L1-01", "INJ-L1-02", "TST-L1-03" })
            SimulateDeviceUpload(code);
    }

    // ============================================================
    // 设备统计种子数据（日产量 / 小时产量 OEE / 当日异常）
    // ============================================================

    /// <summary>各设备可能出现的异常类型</summary>
    private static readonly Dictionary<string, string[]> AlarmTypes = new()
    {
        ["COAT-L1-01"] = new[] { "过温", "张力超差", "涂布厚度超差" },
        ["WIND-L1-01"] = new[] { "张力超差", "极片跑偏", "隔膜褶皱" },
        ["INJ-L1-02"] = new[] { "注液量异常", "注液压力低", "漏液报警" },
        ["FORM-A01"] = new[] { "过温", "化成电流异常", "端电压超差" },
        ["TST-L1-03"] = new[] { "通讯中断", "探针接触不良", "测试超时" },
        ["DRY-RM-01"] = new[] { "露点超限", "温湿度异常", "风机故障" },
        ["SEAL-L1-01"] = new[] { "封口温度超差", "密封性报警", "计划维护" },
    };

    /// <summary>最近一次设备统计生成的时刻；跨天或跨小时后触发重新生成</summary>
    private static DateTime _statsAsOf = DateTime.MinValue;

    /// <summary>懒刷新设备统计：当天同一小时内数据稳定，跨天/跨小时后自动滚动重新生成</summary>
    private static void EnsureDeviceStats()
    {
        var now = DateTime.Now;
        if (_statsAsOf.Date == now.Date && _statsAsOf.Hour == now.Hour) return;
        RegenerateDeviceStats(now);
        _statsAsOf = now;
    }

    /// <summary>为每台设备生成近30天日产量、当日 0~当前小时 产量/OEE、当日异常记录（种子由当天日期派生）</summary>
    private static void RegenerateDeviceStats(DateTime now)
    {
        DeviceDailyRecords.Clear();
        DeviceHourlyRecords.Clear();
        DeviceAlarms.Clear();
        DeviceStatusRecords.Clear();

        var today = now.Date;
        var currentHour = now.Hour;

        for (var i = 0; i < Equipments.Count; i++)
        {
            var eq = Equipments[i];
            // 随机种子 = 当天日期(YYYYMMDD) + 设备序号：同一天内数据稳定，跨天自动滚动变化
            var r = new Random(today.Year * 10000 + today.Month * 100 + today.Day + i);
            var baseOutput = eq.TodayOutput;

            // ---- 近 30 天日产量：缓慢爬坡趋势 + 周末低谷 + 随机噪声；今日与设备 TodayOutput 一致 ----
            for (var d = 0; d < 30; d++)
            {
                var date = today.AddDays(d - 29);
                int output;
                if (d == 29)
                {
                    output = eq.TodayOutput;
                }
                else
                {
                    var weekday = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0.72 : 1.0;
                    var trend = 0.92 + d * 0.003;                 // 缓慢爬坡趋势
                    var noise = 0.85 + r.NextDouble() * 0.30;     // ±15% 噪声
                    output = (int)Math.Round(baseOutput * weekday * trend * noise);
                    output = Math.Max(0, output);
                }
                DeviceDailyRecords.Add(new DeviceDailyProduction
                {
                    DeviceCode = eq.Code,
                    Date = date,
                    Output = output
                });
            }

            // ---- 当日 0 ~ 当前小时：产量按班次权重分配（合计 = TodayOutput），未来小时不生成，OEE 围绕设备 Oee 波动 ----
            var hourCount = currentHour + 1;
            var weights = new double[hourCount];
            for (var h = 0; h < hourCount; h++)
            {
                if (h >= 8 && h <= 20) weights[h] = 1.5 + r.NextDouble() * 0.7;        // 主班次高峰
                else if (h >= 21 || h < 6) weights[h] = 0.12 + r.NextDouble() * 0.18;  // 深夜低产
                else weights[h] = 0.45 + r.NextDouble() * 0.35;                        // 早晚班过渡
            }
            var totalWeight = weights.Sum();
            var allocated = 0;
            for (var h = 0; h < hourCount - 1; h++)
            {
                var output = (int)Math.Round(eq.TodayOutput * weights[h] / totalWeight);
                allocated += output;
                DeviceHourlyRecords.Add(new DeviceHourlyRecord
                {
                    DeviceCode = eq.Code,
                    Date = today,
                    Hour = h,
                    Output = output,
                    OeeRate = Math.Round(Math.Clamp(eq.Oee + (r.NextDouble() * 14 - 7), 40, 99), 1),
                    TargetOee = 85
                });
            }
            // 当前小时吸收余量，保证当日已生成小时产量合计 = TodayOutput
            var lastHourOutput = Math.Max(0, eq.TodayOutput - allocated);
            DeviceHourlyRecords.Add(new DeviceHourlyRecord
            {
                DeviceCode = eq.Code,
                Date = today,
                Hour = currentHour,
                Output = lastHourOutput,
                OeeRate = Math.Round(Math.Clamp(eq.Oee + (r.NextDouble() * 14 - 7), 40, 99), 1),
                TargetOee = 85
            });

            // ---- 当日异常 1~3 条（类型多样，状态混合）；时间限定在今日 0 点 ~ 当前时间之前 ----
            var types = AlarmTypes.TryGetValue(eq.Code, out var ts) ? ts : new[] { "设备报警" };
            var count = 1 + r.Next(3);
            var statuses = new[] { "已处理", "处理中", "未处理" };
            var elapsedMinutes = (int)now.TimeOfDay.TotalMinutes;
            if (elapsedMinutes > 0)
            {
                for (var a = 0; a < count; a++)
                {
                    var type = types[r.Next(types.Length)];
                    var time = today.AddMinutes(r.Next(0, elapsedMinutes));   // 必然早于当前时间
                    DeviceAlarms.Add(new DeviceAlarm
                    {
                        DeviceCode = eq.Code,
                        Time = time,
                        AlarmType = type,
                        Message = MakeAlarmMessage(type, eq.Name, r),
                        DurationMin = 3 + r.Next(120),
                        Status = statuses[r.Next(statuses.Length)]
                    });
                }
            }

            // 兜底：保证当日首台设备至少存在 1 条「未处理」报警，便于演示「设备报警→异常单」联动
            if (i == 0 && !DeviceAlarms.Any(x => x.DeviceCode == eq.Code && x.Status == "未处理"))
            {
                DeviceAlarms.Add(new DeviceAlarm
                {
                    DeviceCode = eq.Code,
                    Time = today.AddHours(8).AddMinutes(10),
                    AlarmType = "设备报警",
                    Message = $"{eq.Name} 触发设备报警，待处理",
                    DurationMin = 6,
                    Status = "未处理"
                });
            }

            // ---- 当天状态履历：0 点 ~ now，从"运行"开始交替出现 待机/停机/维护，最后一段进行中且与设备当前状态一致 ----
            var nonRunning = new[] { DeviceStatus.Standby, DeviceStatus.Stopped, DeviceStatus.Maintenance };
            var elapsed = Math.Max(1, (int)now.TimeOfDay.TotalMinutes);

            // 闭合段数（不含最后进行中段）：每段至少 5 分钟并为末段预留 ≥1 分钟；
            // 末段=运行 → 闭合段数取偶数，末段≠运行 → 闭合段数取奇数（保证交替，不出现连续同状态）
            var maxClosed = Math.Max(3, (elapsed - 1) / 5);
            var baseClosed = Math.Min(maxClosed, 2 + r.Next(6));              // 2~7
            var closedCount = (baseClosed % 2 == 0) == (eq.Status == DeviceStatus.Running)
                ? baseClosed
                : (baseClosed + 1 <= maxClosed ? baseClosed + 1 : baseClosed - 1);
            closedCount = Math.Max(2, closedCount);
            var segCount = closedCount + 1;

            // 状态序列：偶数位=运行，奇数位=随机非运行；末位强制为设备当前状态
            var seq = new List<string>(segCount);
            for (var idx = 0; idx < segCount; idx++)
                seq.Add(idx % 2 == 0 ? DeviceStatus.Running : nonRunning[r.Next(nonRunning.Length)]);
            seq[segCount - 1] = eq.Status;

            // 时长分配：闭合段 5~180 分钟按随机权重分配，总闭合时长 ≤ 0 点~now 且为末段预留 ≥1 分钟
            var budget = elapsed - 1;
            var closedWeights = new int[closedCount];
            for (var idx = 0; idx < closedCount; idx++) closedWeights[idx] = 5 + r.Next(176);
            var closedWSum = closedWeights.Sum();
            var durations = new int[closedCount];
            var closedAllocated = 0;
            for (var idx = 0; idx < closedCount; idx++)
            {
                var dur = (int)Math.Round((double)closedWeights[idx] / closedWSum * budget);
                if (dur < 1) dur = 1;
                if (idx == closedCount - 1) dur = Math.Max(1, budget - closedAllocated);   // 末闭合段吸收余量
                closedAllocated += dur;
                durations[idx] = dur;
            }
            var over = closedAllocated - budget;                                           // 吸收舍入误差，保证总闭合时长 ≤ budget
            for (var idx = closedCount - 1; idx >= 0 && over > 0; idx--)
            {
                var cut = Math.Min(over, durations[idx] - 1);
                durations[idx] -= cut;
                over -= cut;
            }

            var cursor = today;
            for (var idx = 0; idx < segCount; idx++)
            {
                var start = cursor;
                DateTime? end;
                if (idx < closedCount)
                {
                    end = start.AddMinutes(durations[idx]);
                    if (end > now) end = now;                                        // 保底：闭合段不越过当前时间
                }
                else
                {
                    end = null;                                                      // 最后一段进行中
                    if (start >= now) start = now.AddMinutes(-1);                    // 防御性兜底：start 必须早于 now
                    if (start < today) start = today;
                }
                DeviceStatusRecords.Add(new DeviceStatusRecord
                {
                    DeviceCode = eq.Code,
                    StartTime = start,
                    EndTime = end,
                    Status = seq[idx],
                    Note = MakeStatusNote(seq[idx], r)
                });
                cursor = end ?? start.AddMinutes(1);
            }
        }
    }

    /// <summary>根据异常类型生成可读的描述信息</summary>
    private static string MakeAlarmMessage(string type, string deviceName, Random r) => type switch
    {
        "过温" => $"设备温度达到 {85 + r.Next(15)}℃，超过 80℃ 上限",
        "张力超差" => $"卷绕张力 {1.5 + r.NextDouble() * 0.8:0.00} N，超出 1.5~2.0 N 范围",
        "注液量异常" => $"注液量 {2.6 + r.NextDouble() * 1.0:0.00} mL，偏离标准 3.0~3.4 mL",
        "封口温度超差" => $"封口温度 {195 + r.Next(25)}℃，超出 200~220℃ 范围",
        "露点超限" => $"露点 {-48 + r.Next(6)}℃，高于 -50℃ 警戒线",
        "通讯中断" => $"与上位机通讯中断 {r.Next(1, 30)} 秒",
        "计划维护" => "设备进入计划维护窗口，暂停生产",
        _ => $"{deviceName} 触发「{type}」报警，请及时处理"
    };

    /// <summary>根据状态生成简短中文说明（与状态匹配）</summary>
    private static string MakeStatusNote(string status, Random r) => status switch
    {
        DeviceStatus.Running => "正常运行",
        DeviceStatus.Standby => r.Next(3) switch
        {
            0 => "待料",
            1 => "换型准备",
            _ => "等待排产"
        },
        DeviceStatus.Stopped => r.Next(3) switch
        {
            0 => "张力超差停机",
            1 => "故障检修",
            _ => "临时停机"
        },
        DeviceStatus.Maintenance => r.Next(3) switch
        {
            0 => "计划维护",
            1 => "保养维护",
            _ => "设备点检"
        },
        _ => "状态更新"
    };

    /// <summary>按设备取工序实绩</summary>
    public static List<ProcessActualRecord> GetActualsByEquipment(string equipmentCode) =>
        ProcessActuals.Where(a => a.Equipment == equipmentCode).OrderByDescending(a => a.Time).ToList();

    // ---------------- 设备统计查询 ----------------

    /// <summary>近 30 天日产量（按日期升序）</summary>
    public static List<DeviceDailyProduction> GetDailyProductions(string deviceCode)
    {
        EnsureDeviceStats();
        return DeviceDailyRecords.Where(r => r.DeviceCode == deviceCode).OrderBy(r => r.Date).ToList();
    }

    /// <summary>当日 0 点 ~ 当前小时每小时产量/OEE/达成率（按小时升序）</summary>
    public static List<DeviceHourlyRecord> GetHourlyRecords(string deviceCode)
    {
        EnsureDeviceStats();
        return DeviceHourlyRecords.Where(r => r.DeviceCode == deviceCode && r.Date == DateTime.Today).OrderBy(r => r.Hour).ToList();
    }

    /// <summary>当日异常事件（按时间倒序）</summary>
    public static List<DeviceAlarm> GetAlarms(string deviceCode)
    {
        EnsureDeviceStats();
        return DeviceAlarms.Where(a => a.DeviceCode == deviceCode && a.Time.Date == DateTime.Today).OrderByDescending(a => a.Time).ToList();
    }

    /// <summary>当天状态履历（按开始时间升序；前置 EnsureDeviceStats）</summary>
    public static List<DeviceStatusRecord> GetStatusHistory(string deviceCode)
    {
        EnsureDeviceStats();
        return DeviceStatusRecords
            .Where(r => r.DeviceCode == deviceCode)
            .OrderBy(r => r.StartTime)
            .ToList();
    }

    // ---------------- 设备按时刻状态查询 ----------------

    /// <summary>查询某设备在指定时刻的状态（运行/待机/停机/维护）；无法判定返回 null。</summary>
    /// <remarks>
    /// 判定优先级：
    /// 1) 设备不在设备台账（Equipments 按 Code 精确匹配）→ null；
    /// 2) 当天 0 点 ~ 当前时刻：读取当天滚动状态履历 DeviceStatusRecords，返回覆盖 moment 的片段状态；
    ///    找不到覆盖片段 → null；
    /// 3) moment 晚于当前时刻（含未来日期）→ null；
    /// 4) 历史日（moment &lt; today）：确定性重建该日全天状态时间线后判定——
    ///    随机种子与当天滚动机制一致（YYYYMMDD + 设备序号），同一 (设备, 时刻) 每次调用结果一致。
    ///    该重建为纯函数式，仅用于演示/原型数据支撑：不写入 DeviceStatusRecords、不影响当天滚动数据。
    /// </remarks>
    public static string? GetDeviceStatusAt(string deviceCode, DateTime moment)
    {
        EnsureDeviceStats();

        if (!Equipments.Any(e => e.Code == deviceCode)) return null;

        var now = DateTime.Now;
        var today = now.Date;

        // 当天 0 点 ~ now：直接查滚动履历
        if (moment.Date == today)
        {
            if (moment > now) return null;   // 未来时刻（含未来几小时）无法判定
            var rec = DeviceStatusRecords
                .Where(r => r.DeviceCode == deviceCode)
                .FirstOrDefault(r => r.StartTime <= moment && (r.EndTime is null || moment < r.EndTime.Value));
            return rec?.Status;
        }

        // moment < today：历史日确定性重建；moment > today：未来日期 → null
        return moment < today ? RebuildHistoricalStatusAt(deviceCode, moment) : null;
    }

    /// <summary>确定性重建指定历史日的全天状态时间线，返回 moment 所处状态（覆盖 0 点 ~ 次日 0 点，不留空洞）。</summary>
    /// <remarks>
    /// 风格与当天滚动履历一致：0 点从"运行"开始，偶数段=运行、奇数段=非运行（相邻状态天然不同），
    /// 各段按随机权重分配全天 1440 分钟、末段吸收余量铺满全天。
    /// 随机种子 = moment 日期(YYYYMMDD) + 设备在 Equipments 的序号，与 RegenerateDeviceStats 相同公式 →
    /// 同参调用重复结果一致；纯函数式重建，不修改 DeviceStatusRecords。
    /// </remarks>
    private static string? RebuildHistoricalStatusAt(string deviceCode, DateTime moment)
    {
        var eqIndex = Equipments.FindIndex(e => e.Code == deviceCode);
        if (eqIndex < 0) return null;

        var date = moment.Date;
        var r = new Random(date.Year * 10000 + date.Month * 100 + date.Day + eqIndex);
        var nonRunning = new[] { DeviceStatus.Standby, DeviceStatus.Stopped, DeviceStatus.Maintenance };
        const int totalMinutes = 24 * 60;

        // 段数 8~16：偶数段=运行、奇数段=非运行
        var segCount = 8 + r.Next(9);
        var statuses = new string[segCount];
        for (var s = 0; s < segCount; s++)
            statuses[s] = s % 2 == 0 ? DeviceStatus.Running : nonRunning[r.Next(nonRunning.Length)];

        // 每段随机权重 → 把全天 1440 分钟按权重分配到各段（每段 ≥1 分钟，末段吸收余量，保证铺满全天）
        var weights = new int[segCount];
        var weightSum = 0;
        for (var s = 0; s < segCount; s++)
        {
            weights[s] = 1 + r.Next(100);
            weightSum += weights[s];
        }

        var durations = new int[segCount];
        var allocated = 0;
        for (var s = 0; s < segCount; s++)
        {
            if (s == segCount - 1)
            {
                // 末段吸收余量，使时间线恰好铺满 0 点 ~ 次日 0 点
                durations[s] = Math.Max(0, totalMinutes - allocated);
                break;
            }
            var reserve = segCount - 1 - s;                             // 其后未分配段各留 ≥1 分钟
            var maxDur = Math.Max(1, totalMinutes - allocated - reserve);
            var dur = (int)Math.Round((double)totalMinutes * weights[s] / weightSum);
            durations[s] = Math.Clamp(dur, 1, maxDur);
            allocated += durations[s];
        }

        // 找到包含 moment（按 [start, end) 半开区间）的段；若因舍入出现空洞则兜底运行态
        var minutesIntoDay = (int)moment.TimeOfDay.TotalMinutes;
        var cursor = 0;
        for (var s = 0; s < segCount; s++)
        {
            if (minutesIntoDay >= cursor && minutesIntoDay < cursor + durations[s])
                return statuses[s];
            cursor += durations[s];
        }
        return DeviceStatus.Running;
    }

    /// <summary>本日产量（来自日产量集合）</summary>
    public static int GetTodayOutput(string deviceCode)
    {
        EnsureDeviceStats();
        var today = DateTime.Today;
        return DeviceDailyRecords
            .Where(r => r.DeviceCode == deviceCode && r.Date == today)
            .Sum(r => r.Output);
    }

    /// <summary>本月产量（当月 1 日至今日，来自日产量集合按月汇总）</summary>
    public static int GetMonthOutput(string deviceCode)
    {
        EnsureDeviceStats();
        var now = DateTime.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        return DeviceDailyRecords
            .Where(r => r.DeviceCode == deviceCode && r.Date >= monthStart && r.Date <= now.Date)
            .Sum(r => r.Output);
    }

    /// <summary>生成某电芯+工序的检验填写行（已有记录则预填）</summary>
    public static List<InspectionRow> GetInspectionRows(string cellSn, string equipment, string processName)
    {
        var templates = InspectionTemplates.TryGetValue(processName, out var list)
            ? list
            : new List<(string, string)> { ("外观检查", "目视无异常") };

        var saved = Inspections
            .Where(i => i.CellSn == cellSn && i.Equipment == equipment && i.ProcessName == processName)
            .ToList();

        var rows = new List<InspectionRow>();
        foreach (var (item, std) in templates)
        {
            var s = saved.FirstOrDefault(x => x.ItemName == item);
            rows.Add(new InspectionRow
            {
                ItemName = item,
                StandardValue = std,
                MeasuredValue = s?.MeasuredValue ?? "",
                Result = s?.Result ?? "合格",
                Inspector = s?.Inspector ?? ""
            });
        }
        return rows;
    }

    /// <summary>保存检验记录（同电芯+设备+工序先清后写）</summary>
    public static int SaveInspections(string cellSn, string equipment, string processName, List<InspectionRow> rows)
    {
        Inspections.RemoveAll(i => i.CellSn == cellSn && i.Equipment == equipment && i.ProcessName == processName);
        var now = DateTime.Now;
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.ItemName)) continue;
            Inspections.Add(new InspectionRecord
            {
                CellSn = cellSn,
                Equipment = equipment,
                ProcessName = processName,
                ItemName = r.ItemName,
                StandardValue = r.StandardValue,
                MeasuredValue = r.MeasuredValue?.Trim() ?? "",
                Result = string.IsNullOrWhiteSpace(r.Result) ? "合格" : r.Result,
                Inspector = r.Inspector?.Trim() ?? "",
                InspectTime = now
            });
        }
        return Inspections.Count(i => i.CellSn == cellSn && i.Equipment == equipment && i.ProcessName == processName);
    }

    /// <summary>该电芯+工序已保存的检验条数</summary>
    public static int CountInspections(string cellSn, string equipment, string processName) =>
        Inspections.Count(i => i.CellSn == cellSn && i.Equipment == equipment && i.ProcessName == processName);

    // ============================================================
    // 资材追溯与电芯保留（增量功能）
    // ============================================================

    /// <summary>按资材关键字查询关联电芯：批号/物料编码/物料名称忽略大小写包含匹配；三路径收集关联 SN 后去重。</summary>
    public static List<CellHoldCandidate> QueryCellsByMaterial(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return new List<CellHoldCandidate>();
        var kw = keyword.Trim();
        var materials = Materials.Where(m =>
                (m.LotNo.Length > 0 && m.LotNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (m.MaterialCode.Length > 0 && m.MaterialCode.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (m.MaterialName.Length > 0 && m.MaterialName.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (materials.Count == 0) return new List<CellHoldCandidate>();

        var cellMap = Cells.ToDictionary(c => c.Sn, StringComparer.OrdinalIgnoreCase);
        var matsBySn = new Dictionary<string, List<MaterialLot>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in materials)
        {
            var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // ① 该批次被电芯用作极片批号；② 该批次被电芯用作电解液批号；③ UsedFor 直接指向电芯 SN
            foreach (var c in Cells)
            {
                if ((c.ElectrodeLot.Length > 0 && string.Equals(c.ElectrodeLot, m.LotNo, StringComparison.OrdinalIgnoreCase))
                    || (c.ElectrolyteLot.Length > 0 && string.Equals(c.ElectrolyteLot, m.LotNo, StringComparison.OrdinalIgnoreCase)))
                {
                    related.Add(c.Sn);
                }
            }
            if (m.UsedFor.Length > 0 && cellMap.ContainsKey(m.UsedFor)) related.Add(m.UsedFor);

            foreach (var sn in related)
            {
                if (!matsBySn.TryGetValue(sn, out var list))
                {
                    list = new List<MaterialLot>();
                    matsBySn[sn] = list;
                }
                if (!list.Contains(m)) list.Add(m);
            }
        }

        var activeByCell = BuildActiveHoldByCell();
        var result = new List<CellHoldCandidate>();
        foreach (var sn in matsBySn.Keys.OrderBy(s => s, StringComparer.Ordinal))
        {
            if (!cellMap.TryGetValue(sn, out var cell)) continue;
            var mats = matsBySn[sn];
            var activeHold = activeByCell.TryGetValue(sn, out var hold) ? hold : null;
            var candidate = new CellHoldCandidate
            {
                Sn = cell.Sn,
                ProductModel = cell.ProductModel,
                Line = cell.Line,
                Status = cell.Status,
                Result = cell.Result,
                ElectrodeLot = cell.ElectrodeLot,
                ElectrolyteLot = cell.ElectrolyteLot,
                MaterialDesc = string.Join("  /  ", mats.Select(m => $"{m.MaterialName} {m.LotNo}")),
                HoldStatus = activeHold is null ? "" : $"保留中-{activeHold.HoldLocation}"
            };
            foreach (var m in mats) candidate.HitMaterials.Add(m);
            result.Add(candidate);
        }
        return result;
    }

    /// <summary>对若干电芯执行保留：已处于保留中的 SN 自动剔除到 Skipped，其余生成保留单+明细+履历。</summary>
    /// <param name="sourceType">来源类型（可选，默认「手工」）</param>
    /// <param name="sourceRef">来源说明/单号（可选，默认空）</param>
    public static (bool Ok, string Message, string HoldNo, List<string> Skipped) HoldCells(
        string materialCode, string materialName, string lotNo, string holdLocation,
        string reason, string @operator, IReadOnlyCollection<string> cellSns,
        string? sourceType = null, string? sourceRef = null)
    {
        var sns = (cellSns ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sns.Count == 0) return (false, "请先选择要保留的电芯。", "", new List<string>());

        var activeByCell = BuildActiveHoldByCell();
        var toHold = new List<string>();
        var skipped = new List<string>();
        foreach (var sn in sns)
        {
            if (activeByCell.ContainsKey(sn)) skipped.Add(sn);
            else toHold.Add(sn);
        }
        if (toHold.Count == 0)
            return (false, "所选电芯均已处于保留中，无需重复保留。", "", skipped);

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var loc = string.IsNullOrWhiteSpace(holdLocation) ? HoldLocations.FormationInspection : holdLocation.Trim();
        var note = string.IsNullOrWhiteSpace(reason) ? "质量保留" : reason.Trim();

        var holdNo = NextHoldNo();
        var now = DateTime.Now;
        Holds.Add(new CellHold
        {
            HoldNo = holdNo,
            MaterialCode = materialCode ?? "",
            MaterialName = materialName ?? "",
            LotNo = lotNo ?? "",
            HoldLocation = loc,
            Reason = note,
            Operator = op,
            HoldTime = now,
            Status = HoldStatus.Active,
            SourceType = sourceType is null ? "手工" : sourceType.Trim(),
            SourceRef = sourceRef ?? ""
        });
        foreach (var sn in toHold)
            HoldItems.Add(new CellHoldItem { HoldNo = holdNo, CellSn = sn });
        HoldHistories.Add(MakeHoldHistory(holdNo, HoldActionType.Hold, op, note, toHold));

        var skipText = skipped.Count > 0
            ? $"\n已跳过 {skipped.Count} 只已在保留中的电芯：{string.Join("、", skipped)}"
            : "";
        return (true, $"保留单 {holdNo} 已生成，共保留 {toHold.Count} 只电芯。{skipText}", holdNo, skipped);
    }

    /// <summary>解除保留单：状态置为已解除并追加解除履历；仅"保留中"可解除。</summary>
    public static (bool Ok, string Message) ReleaseHold(string holdNo, string @operator, string reason)
    {
        var hold = Holds.FirstOrDefault(h => h.HoldNo == holdNo);
        if (hold is null) return (false, $"未找到保留单 {holdNo}。");
        if (hold.Status == HoldStatus.Released) return (false, $"保留单 {holdNo} 已解除，请勿重复操作。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var note = string.IsNullOrWhiteSpace(reason) ? "质量解除" : reason.Trim();
        hold.Status = HoldStatus.Released;
        hold.ReleaseOperator = op;
        hold.ReleaseReason = note;
        hold.ReleaseTime = DateTime.Now;

        var sns = HoldItems.Where(i => i.HoldNo == holdNo).Select(i => i.CellSn).ToList();
        HoldHistories.Add(MakeHoldHistory(holdNo, HoldActionType.Release, op, note, sns));
        return (true, $"保留单 {holdNo} 已解除，共解除 {sns.Count} 只电芯。");
    }

    /// <summary>按条件查询保留单：空条件不过滤；from/to 按日期闭区间（含当日全天）；结果按保留时间倒序并填充电芯数。</summary>
    public static List<CellHold> QueryHolds(string? holdNo, string? status, string? location, DateTime? from, DateTime? to)
    {
        IEnumerable<CellHold> query = Holds;
        if (!string.IsNullOrWhiteSpace(holdNo))
        {
            var kw = holdNo!.Trim();
            query = query.Where(h => h.HoldNo.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status!.Trim();
            query = query.Where(h => h.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(location))
        {
            var loc = location!.Trim();
            query = query.Where(h => h.HoldLocation == loc);
        }
        if (from.HasValue)
        {
            var start = from.Value.Date;
            query = query.Where(h => h.HoldTime >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(h => h.HoldTime <= end);
        }

        var list = query.OrderByDescending(h => h.HoldTime).ToList();
        foreach (var h in list)
            h.CellCount = HoldItems.Count(i => i.HoldNo == h.HoldNo);
        return list;
    }

    /// <summary>某保留单的电芯明细（按 SN 升序）</summary>
    public static List<CellHoldItem> GetHoldItems(string holdNo) =>
        HoldItems.Where(i => i.HoldNo == holdNo).OrderBy(i => i.CellSn, StringComparer.Ordinal).ToList();

    /// <summary>
    /// 某保留单的被保留电芯明细（平铺含台账详情，按 SN 升序）。
    /// join HoldItems（过滤 holdNo）↔ Cells（CellSn==Sn）：台账中找不到的 SN 用空串填充、不丢行。
    /// </summary>
    public static List<CellHoldItemDetail> GetHoldItemDetails(string holdNo)
    {
        var cellMap = Cells.ToDictionary(c => c.Sn, StringComparer.OrdinalIgnoreCase);
        return HoldItems
            .Where(i => i.HoldNo == holdNo)
            .OrderBy(i => i.CellSn, StringComparer.Ordinal)
            .Select(i =>
            {
                cellMap.TryGetValue(i.CellSn, out var cell);
                return new CellHoldItemDetail
                {
                    HoldNo = i.HoldNo,
                    CellSn = i.CellSn,
                    ProductModel = cell?.ProductModel ?? "",
                    Line = cell?.Line ?? "",
                    Status = cell?.Status ?? "",
                    Result = cell?.Result ?? "",
                    ElectrodeLot = cell?.ElectrodeLot ?? "",
                    ElectrolyteLot = cell?.ElectrolyteLot ?? "",
                    CreateTime = cell?.CreateTime ?? default
                };
            })
            .ToList();
    }

    /// <summary>某保留单的保留/解除履历（按时间倒序）</summary>
    public static List<HoldHistory> GetHoldHistory(string holdNo) =>
        HoldHistories.Where(h => h.HoldNo == holdNo)
            .OrderByDescending(h => h.Time)
            .ThenByDescending(h => h.Seq)
            .ToList();

    /// <summary>构建 电芯SN → 保留中保留单 映射（同一 SN 存在多条时取第一条，正常业务不应出现）</summary>
    private static Dictionary<string, CellHold> BuildActiveHoldByCell()
    {
        var activeNoMap = Holds
            .Where(h => h.Status == HoldStatus.Active)
            .ToDictionary(h => h.HoldNo, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, CellHold>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in HoldItems)
        {
            if (!activeNoMap.TryGetValue(item.HoldNo, out var hold)) continue;
            if (!map.ContainsKey(item.CellSn)) map[item.CellSn] = hold;
        }
        return map;
    }

    /// <summary>生成下一个保留单号：HLD-yyyyMMdd-序号(3 位)；序号全局递增、跨天自然延续。</summary>
    private static string NextHoldNo()
    {
        _holdSeq++;
        return $"HLD-{DateTime.Now:yyyyMMdd}-{_holdSeq:000}";
    }

    /// <summary>构造一条履历（Seq 自增；SN 摘要：单只显示 SN，多只显示"首个等 N 只"）</summary>
    private static HoldHistory MakeHoldHistory(string holdNo, string actionType, string @operator, string reason, List<string> cellSns)
    {
        _historySeq++;
        var first = cellSns.OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault() ?? "";
        var preview = cellSns.Count <= 1 ? first : $"{first} 等 {cellSns.Count} 只";
        return new HoldHistory
        {
            Seq = _historySeq,
            HoldNo = holdNo,
            ActionType = actionType,
            Operator = @operator,
            Time = DateTime.Now,
            Reason = reason,
            CellCount = cellSns.Count,
            SnPreview = preview
        };
    }

    private static (string, string) MakeParam(string process, Random rnd) => process switch
    {
        "涂布" => ("涂布厚度", $"{rnd.Next(98, 112)} μm"),
        "卷绕" => ("卷绕张力", $"{1.5 + rnd.NextDouble() * 0.4:0.0} N"),
        "注液" => ("注液量", $"{3.0 + rnd.NextDouble() * 0.3:0.00} mL"),
        "封口" => ("封口温度", $"{rnd.Next(202, 216)} ℃"),
        "化成" => ("化成电流", $"{0.4 + rnd.NextDouble() * 0.2:0.00} C"),
        "电性能检测" => ("容量", $"{97 + rnd.NextDouble() * 5:0.00} F"),
        _ => ("工序参数", "--")
    };

    // ============================================================
    // 现场异常登记与异常管理（增量功能）
    // ============================================================

    /// <summary>播种现场异常演示数据：3 条覆盖 待确认/已确认/已闭环，时间相对今天，保证默认近 30 天查询可命中。</summary>
    private static void SeedAbnormals()
    {
        if (AbnormalRecords.Count > 0) return;
        var now = DateTime.Now;
        var today = now.Date;

        // ① 待确认：L1 卷绕机 生产异常/漏工序（今日-2）
        var wind = Equipments.FirstOrDefault(e => e.Code == "WIND-L1-01");
        var eqCode1 = wind?.Code ?? "WIND-L1-01";
        var eqName1 = wind?.Name ?? "卷绕机";
        var t1 = today.AddDays(-2).AddHours(9).AddMinutes(30);
        var no1 = NextAbNo();
        var rec1 = new AbnormalRecord
        {
            AbNo = no1,
            OccurTime = t1,
            FoundBy = "李强",
            Line = "L1",
            EquipmentCode = eqCode1,
            EquipmentName = eqName1,
            Category = AbnormalCategories.Production,
            SubType = "漏工序",
            Description = "卷绕工序发现部分电芯极片对位偏移，疑似上一工序漏检导致，需工艺复核。",
            TemporaryMeasure = "同批次在制电芯已隔离待检；暂停该机台卷绕作业并通知工艺人员复核。",
            Owner = "王芳",
            Status = AbnormalStatus.Pending,
            RegisterBy = "张伟",
            RegisterTime = t1.AddMinutes(6)
        };
        AbnormalRecords.Add(rec1);
        AbnormalHistories.Add(MakeAbHistory(rec1.AbNo, AbnormalAction.Register, rec1.RegisterBy, rec1.RegisterTime,
            $"登记：{rec1.Category}（{rec1.SubType}）· {eqName1}·线体{rec1.Line}"));

        // ② 已确认（待根因闭环）：L2 化成柜A 工艺异常/工艺参数超限（今日-1）
        var form = Equipments.FirstOrDefault(e => e.Code == "FORM-A01");
        var eqCode2 = form?.Code ?? "FORM-A01";
        var eqName2 = form?.Name ?? "化成柜组A";
        var t2 = today.AddDays(-1).AddHours(14).AddMinutes(10);
        var no2 = NextAbNo();
        var rec2 = new AbnormalRecord
        {
            AbNo = no2,
            OccurTime = t2,
            FoundBy = "赵敏",
            Line = "L2",
            EquipmentCode = eqCode2,
            EquipmentName = eqName2,
            Category = AbnormalCategories.Process,
            SubType = "工艺参数超限",
            Description = "化成工序监控发现某化成柜端电压偏高，超出工艺上限，需分析根因并评估在制品。",
            TemporaryMeasure = "该化成柜已暂停投料；涉及电芯挂牌标识待评审。",
            Owner = "周涛",
            Status = AbnormalStatus.Confirmed,
            RegisterBy = "王强",
            RegisterTime = t2.AddMinutes(4),
            ConfirmBy = "周涛",
            ConfirmTime = t2.AddHours(1).AddMinutes(20)
        };
        AbnormalRecords.Add(rec2);
        AbnormalHistories.Add(MakeAbHistory(rec2.AbNo, AbnormalAction.Register, rec2.RegisterBy, rec2.RegisterTime,
            $"登记：{rec2.Category}（{rec2.SubType}）· {eqName2}·线体{rec2.Line}"));
        AbnormalHistories.Add(MakeAbHistory(rec2.AbNo, AbnormalAction.Confirm, rec2.ConfirmBy, rec2.ConfirmTime!.Value,
            "确认：登记内容核对无误，转后续负责人开展根因分析。"));

        // ③ 已闭环（含横展=需要+范围）：L1 注液机 设备异常/设备故障停机（今日稍早，保证不晚于当前时间）
        var inj = Equipments.FirstOrDefault(e => e.Code == "INJ-L1-02");
        var eqCode3 = inj?.Code ?? "INJ-L1-02";
        var eqName3 = inj?.Name ?? "注液机";
        var t3 = now.AddHours(-5);
        var no3 = NextAbNo();
        var rec3 = new AbnormalRecord
        {
            AbNo = no3,
            OccurTime = t3,
            FoundBy = "孙磊",
            Line = "L1",
            EquipmentCode = eqCode3,
            EquipmentName = eqName3,
            Category = AbnormalCategories.Equipment,
            SubType = "设备故障停机",
            Description = "注液机注液泵压力不足触发停机，疑隔膜泵磨损泄漏导致注液量波动。",
            TemporaryMeasure = "切换备用注液泵恢复生产；故障泵下线待检修。",
            Owner = "周涛",
            Status = AbnormalStatus.Closed,
            RegisterBy = "王强",
            RegisterTime = t3.AddMinutes(3),
            ConfirmBy = "周涛",
            ConfirmTime = t3.AddMinutes(20),
            RootCause = "注液泵隔膜疲劳破损，密封失效导致压力不足、注液量偏低。",
            RootMeasure = "更换隔膜组件并校准注液量；将隔膜更换纳入季度保养清单；增加泵出口压力点检频次。",
            AnalysisBy = "周涛",
            AnalysisTime = t3.AddHours(1),
            HorizonNeeded = AbnormalHorizon.Needed,
            HorizonScope = "L2 同型注液机（INJ-L2-02）同步检查隔膜磨损情况；横向推广至封口机压力类部件点检，计划本周内完成。",
            ClosedTime = now.AddMinutes(-30)
        };
        AbnormalRecords.Add(rec3);
        AbnormalHistories.Add(MakeAbHistory(rec3.AbNo, AbnormalAction.Register, rec3.RegisterBy, rec3.RegisterTime,
            $"登记：{rec3.Category}（{rec3.SubType}）· {eqName3}·线体{rec3.Line}"));
        AbnormalHistories.Add(MakeAbHistory(rec3.AbNo, AbnormalAction.Confirm, rec3.ConfirmBy, rec3.ConfirmTime!.Value,
            "确认：登记内容核对无误，转后续负责人开展根因分析。"));
        AbnormalHistories.Add(MakeAbHistory(rec3.AbNo, AbnormalAction.Close, rec3.AnalysisBy, rec3.ClosedTime!.Value,
            $"闭环：根因=隔膜破损；措施=更换隔膜+保养清单+点检频次；横展=需要（L2 同型注液机及封口机压力部件）。"));
    }

    /// <summary>生成下一个异常单号：AB-yyyyMMdd-序号(3 位)；序号全局递增、跨天自然延续。</summary>
    private static string NextAbNo()
    {
        _abSeq++;
        return $"AB-{DateTime.Now:yyyyMMdd}-{_abSeq:000}";
    }

    /// <summary>构造一条异常履历（Seq 自增，Time 倒序展示时以 Seq 辅助稳定排序）</summary>
    private static AbnormalHistory MakeAbHistory(string abNo, string action, string @operator, DateTime time, string remark)
    {
        _abHistorySeq++;
        return new AbnormalHistory
        {
            Seq = _abHistorySeq,
            AbNo = abNo,
            Action = action,
            Operator = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim(),
            Time = time,
            Remark = remark
        };
    }

    /// <summary>
    /// 登记现场异常：写单（状态=待确认）并追加「登记」履历。
    /// 校验交由界面层完成；此处对空值做防御性兜底，返回新生成的异常编号。
    /// </summary>
    public static string CreateAbnormal(
        DateTime occurTime, string foundBy, string line, string equipmentCode, string equipmentName,
        string category, string subType, string description, string tempMeasure, string owner, string @operator,
        string? sourceType = null, string? sourceRef = null)
    {
        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var abNo = NextAbNo();
        var now = DateTime.Now;
        var rec = new AbnormalRecord
        {
            AbNo = abNo,
            OccurTime = occurTime == default ? now : occurTime,
            FoundBy = foundBy?.Trim() ?? "",
            Line = string.IsNullOrWhiteSpace(line) ? "L1" : line.Trim(),
            EquipmentCode = equipmentCode?.Trim() ?? "",
            EquipmentName = equipmentName?.Trim() ?? "",
            Category = string.IsNullOrWhiteSpace(category) ? AbnormalCategories.Production : category.Trim(),
            SubType = subType?.Trim() ?? "",
            Description = description?.Trim() ?? "",
            TemporaryMeasure = tempMeasure?.Trim() ?? "",
            Owner = owner?.Trim() ?? "",
            Status = AbnormalStatus.Pending,
            RegisterBy = op,
            RegisterTime = now,
            SourceType = sourceType is null ? "人工登记" : sourceType.Trim(),
            SourceRef = sourceRef ?? ""
        };
        AbnormalRecords.Add(rec);
        AbnormalHistories.Add(MakeAbHistory(rec.AbNo, AbnormalAction.Register, op, now,
            $"登记：{rec.Category}（{(string.IsNullOrWhiteSpace(rec.SubType) ? "未细分" : rec.SubType)}）· "
            + $"{(string.IsNullOrWhiteSpace(rec.EquipmentName) ? rec.EquipmentCode : rec.EquipmentName)}·线体{rec.Line}"));
        return abNo;
    }

    // ============================================================
    // 质量事件自动联动（不合格 → 保留单 / SPC预警 → 异常单 / 设备报警 → 异常单）
    // 仅做增量：既有 CreateAbnormal / HoldCells 通过末尾可选参数写入来源，既有调用不变。
    // ============================================================

    /// <summary>
    /// 取全部「不合格」电芯 SN：合并 TestRecords 中 Passed==false 与 Inspections 中 Result=="不合格"（去重、忽略大小写）。
    /// 纯查询，供自动保留与 UI 预览数量使用。
    /// </summary>
    public static List<string> GetNonconformingCellSns()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TestRecords)
            if (t.Passed == false && !string.IsNullOrWhiteSpace(t.CellSn)) set.Add(t.CellSn!);
        foreach (var i in Inspections)
            if (string.Equals(i.Result, "不合格", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(i.CellSn))
                set.Add(i.CellSn!);
        return set.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 自动保留不合格电芯：扫描测试/检验不合格 SN（可经 keyword 过滤）→ 排除已在保留中 → 用 HoldCells 合并成一张保留单。
    /// 返回元数据供 UI 提示；无新增时 Ok=false 并给出「无新增」说明（幂等：已保留的 SN 不会重复建单）。
    /// </summary>
    /// <returns>
    /// Ok=是否新建了保留单；Message=自洽提示文案；HoldNo=新建保留单号（无新增时为空）；
    /// Skipped=已处于保留中、被跳过的 SN；
    /// <c>NewSnCount</c>=本次<b>实际新增</b>进入保留的电芯数（非扫描总数），无新增时为 0。
    /// </returns>
    public static (bool Ok, string Message, string HoldNo, List<string> Skipped, int NewSnCount) AutoHoldNonconforming(
        string? keyword, string holdLocation, string @operator)
    {
        var sns = GetNonconformingCellSns();

        // keyword 可选过滤：命中电芯 SN 或产品型号
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword!.Trim();
            var cellMap = Cells.ToDictionary(c => c.Sn, StringComparer.OrdinalIgnoreCase);
            sns = sns.Where(s =>
                    s.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || (cellMap.TryGetValue(s, out var c) && c.ProductModel.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var activeByCell = BuildActiveHoldByCell();
        var toHold = new List<string>();
        var skipped = new List<string>();
        foreach (var sn in sns)
        {
            if (activeByCell.ContainsKey(sn)) skipped.Add(sn);
            else toHold.Add(sn);
        }

        if (toHold.Count == 0)
        {
            return (false,
                $"无新增：不合格电芯共 {skipped.Count} 只，均已在保留中，未生成保留单。",
                "", skipped, 0);
        }

        var reason = $"质量数据扫描：测试/检验不合格自动保留（{toHold.Count} 只）";
        var (ok, msg, holdNo, _) = HoldCells(
            "", "质量扫描", "", holdLocation, reason, @operator, toHold, "质量扫描", "扫描不合格电芯");
        if (!ok) return (false, msg, "", skipped, 0);

        var total = sns.Count;
        return (true,
            $"扫描完成：不合格电芯 {total} 只，新建保留单 {holdNo}（{toHold.Count} 只），已在保留中 {skipped.Count} 只。",
            holdNo, skipped, toHold.Count);
    }

    /// <summary>
    /// 单个不合格电芯自动保留（保存检验时调用）：走 HoldCells 合并；已保留 → Ok=false 但消息含「已在保留中」（幂等，调用方据此提示，不算错误）。
    /// </summary>
    public static (bool Ok, string Message) HoldSingleNonconformingCell(
        string cellSn, string equipment, string processName, string reason, string @operator)
    {
        if (string.IsNullOrWhiteSpace(cellSn)) return (false, "电芯 SN 为空，无法保留。");
        var sn = cellSn.Trim();
        var activeByCell = BuildActiveHoldByCell();
        if (activeByCell.ContainsKey(sn))
            return (false, $"电芯 {sn} 已在保留中，未重复保留。");

        var (ok, msg, _, _) = HoldCells(
            "", "质量扫描", "", HoldLocations.PendingInspection, reason, @operator,
            new[] { sn }, "质量扫描", $"检验不合格：{processName}");
        return (ok, msg);
    }

    /// <summary>按 设备编码（忽略大小写）+ 报警时间 定位设备报警；找不到返回 null。</summary>
    public static DeviceAlarm? FindDeviceAlarm(string deviceCode, DateTime alarmTime)
    {
        if (string.IsNullOrWhiteSpace(deviceCode)) return null;
        var code = deviceCode.Trim();
        return DeviceAlarms.FirstOrDefault(a =>
            string.Equals(a.DeviceCode, code, StringComparison.OrdinalIgnoreCase) && a.Time == alarmTime);
    }

    /// <summary>
    /// 从 SPC 预警生成异常单：校验预警存在且未关联；大类=工艺异常，细分按预警类型映射；
    /// 写单后回写 alert.LinkedAbnormalNo，并把「待处理」预警自动流转为「已确认」、在 Remark 追加转单记录。
    /// 失败路径集合零变化（不产生异常单、不写履历、不改状态）。
    /// <para>口径：自动流转只写 Status + Remark 关联记录，<b>不</b>写 Handler/HandleTime（那是人工「确认/关闭」的语义，避免与自动流转混淆）。</para>
    /// </summary>
    public static (bool Ok, string Message, string AbNo) CreateAbnormalFromSpcAlert(string alertNo, string @operator, string? owner = null)
    {
        var alert = FindSpcAlert(alertNo);
        if (alert is null) return (false, $"未找到预警 {alertNo}。", "");
        if (!string.IsNullOrWhiteSpace(alert.LinkedAbnormalNo))
            return (false, $"该预警已关联异常单 {alert.LinkedAbnormalNo}，无需重复转单。", "");

        var subType = MapSpcAlertSubType(alert.AlertType);
        var eqName = Equipments.FirstOrDefault(e => string.Equals(e.Code, alert.Equipment, StringComparison.OrdinalIgnoreCase))?.Name
                     ?? alert.Equipment;
        var specText = BuildSpecText(alert.Usl, alert.Lsl, alert.VersionText);
        var desc =
            $"[SPC预警 {alert.AlertNo}] {alert.ItemName} · {alert.AlertType}/{alert.RuleName}；" +
            $"当前值 {Fmt(alert.Value)}，CL {Fmt(alert.CenterLine)}，UCL {Fmt(alert.Ucl)}，LCL {Fmt(alert.Lcl)}；" +
            $"规格 {specText}（版本 {alert.VersionText}）";

        var abNo = CreateAbnormal(
            alert.SampleTime, "SPC系统", ParseLineFromEquipment(alert.Equipment), alert.Equipment, eqName,
            AbnormalCategories.Process, subType, desc,
            "暂停该工序投料，隔离关联电芯，通知工艺工程师分析",
            owner ?? "工艺工程师", @operator,
            sourceType: "SPC预警", sourceRef: alert.AlertNo);

        alert.LinkedAbnormalNo = abNo;

        // 自动流转：仅当预警仍为「待处理」时置「已确认」并追加转单记录；不改 Handler/HandleTime（人工确认语义）。
        if (alert.Status == SpcAlertStatus.Pending)
        {
            alert.Status = SpcAlertStatus.Acked;
            var stamp = $"已转异常单 {abNo}（{DateTime.Now:yyyy-MM-dd HH:mm}）";
            alert.Remark = string.IsNullOrWhiteSpace(alert.Remark) ? stamp : alert.Remark + "；" + stamp;
        }

        return (true, $"已生成异常单 {abNo}（大类：工艺异常 / 细分：{subType}，负责人：{owner ?? "工艺工程师"}）", abNo);
    }

    /// <summary>
    /// 从设备报警生成异常单：校验报警存在且未关联；大类=设备异常，细分按 AlarmType 文本映射；
    /// 写单后回写 alarm.LinkedAbnormalNo，并把「未处理」报警自动流转为「处理中」（表示已转异常流程跟进）。
    /// 失败路径集合零变化（不写单、不改状态）。
    /// </summary>
    public static (bool Ok, string Message, string AbNo) CreateAbnormalFromDeviceAlarm(
        string deviceCode, DateTime alarmTime, string @operator, string? owner = null)
    {
        var alarm = FindDeviceAlarm(deviceCode, alarmTime);
        if (alarm is null)
            return (false, $"未找到设备报警（设备 {deviceCode}，时间 {alarmTime:yyyy-MM-dd HH:mm}）。", "");
        if (!string.IsNullOrWhiteSpace(alarm.LinkedAbnormalNo))
            return (false, $"该报警已关联异常单 {alarm.LinkedAbnormalNo}，无需重复转单。", "");

        var subType = MapAlarmSubType(alarm.AlarmType);
        var eqName = Equipments.FirstOrDefault(e => string.Equals(e.Code, deviceCode, StringComparison.OrdinalIgnoreCase))?.Name
                     ?? deviceCode;
        var desc =
            $"[设备报警] {eqName}({alarm.DeviceCode}) {alarm.AlarmType}：{alarm.Message}；持续 {alarm.DurationMin} 分钟";

        var abNo = CreateAbnormal(
            alarmTime, "设备监控系统", ParseLineFromEquipment(deviceCode), deviceCode, eqName,
            AbnormalCategories.Equipment, subType, desc,
            "设备停机检查，必要时切换备用设备，由设备工程师现场确认",
            owner ?? "设备工程师", @operator,
            sourceType: "设备报警", sourceRef: $"{deviceCode} {alarmTime:yyyy-MM-dd HH:mm}");

        alarm.LinkedAbnormalNo = abNo;

        // 自动流转：仅当报警仍为「未处理」时置「处理中」，表示已转异常流程跟进。
        if (alarm.Status == "未处理") alarm.Status = "处理中";

        return (true, $"已生成异常单 {abNo}（大类：设备异常 / 细分：{subType}，负责人：{owner ?? "设备工程师"}）", abNo);
    }

    /// <summary>SPC 预警类型 → 工艺异常 细分项（找不到取该类第 1 项）</summary>
    private static string MapSpcAlertSubType(string alertType)
    {
        var list = AbnormalSubTypes.Get(AbnormalCategories.Process);
        var first = list.Length > 0 ? list[0] : "";
        return alertType switch
        {
            SpcAlertType.RuleViolation => list.FirstOrDefault(x => x.Contains("波动")) ?? first,
            SpcAlertType.Incapable => list.FirstOrDefault(x => x.Contains("参数")) ?? first,
            SpcAlertType.OutOfSpec => list.FirstOrDefault(x => x.Contains("标准")) ?? first,
            _ => first
        };
    }

    /// <summary>设备报警类型文本 → 设备异常 细分项（找不到取该类第 1 项）</summary>
    private static string MapAlarmSubType(string alarmType)
    {
        var list = AbnormalSubTypes.Get(AbnormalCategories.Equipment);
        var first = list.Length > 0 ? list[0] : "";
        if (string.IsNullOrWhiteSpace(alarmType)) return first;
        // 映射优先级（关键字命中即短路返回）：
        //   含「过温 / 张力」→ 精度漂移；含「注液 / 故障」→ 设备故障停机；含「报警」→ 报警频繁。
        //   仅当以上关键字完全无命中时，才回退到该大类第 1 项（first）。
        //   因此「未知报警XYZ」这类含「报警」的串会归入「报警频繁」，属有意规则，而非回退。
        if (alarmType.Contains("过温") || alarmType.Contains("张力")) return list.FirstOrDefault(x => x.Contains("精度漂移")) ?? first;
        if (alarmType.Contains("注液") || alarmType.Contains("故障")) return list.FirstOrDefault(x => x.Contains("设备故障停机")) ?? first;
        if (alarmType.Contains("报警")) return list.FirstOrDefault(x => x.Contains("报警频繁")) ?? first;
        return first;
    }

    /// <summary>从设备编码解析线体：含 L1/L2/L3 取之，否则回退 L1</summary>
    private static string ParseLineFromEquipment(string equipmentCode)
    {
        if (!string.IsNullOrWhiteSpace(equipmentCode))
            foreach (var l in LineOptions.All)
                if (equipmentCode.Contains(l, StringComparison.OrdinalIgnoreCase)) return l;
        return "L1";
    }

    /// <summary>拼装规格文本：USL/LSL 为 NaN 显示「—」；二者皆无则为「无」</summary>
    private static string BuildSpecText(double usl, double lsl, string versionText)
    {
        var hasUsl = double.IsFinite(usl);
        var hasLsl = double.IsFinite(lsl);
        if (!hasUsl && !hasLsl) return "无";
        var parts = new List<string>();
        if (hasUsl) parts.Add($"USL {Fmt(usl)}");
        if (hasLsl) parts.Add($"LSL {Fmt(lsl)}");
        return string.Join(" / ", parts);
    }

    /// <summary>数值格式化：NaN/非有限 →「—」</summary>
    private static string Fmt(double v) => double.IsFinite(v) ? v.ToString("0.###") : "—";

    /// <summary>
    /// 按条件查询异常单：空条件不过滤；abNo 关键字忽略大小写包含匹配；
    /// from/to 按发生时间日期闭区间（含当日全天）；结果按发生时间倒序、编号倒序。
    /// </summary>
    public static List<AbnormalRecord> QueryAbnormals(
        string? abNo, string? status, string? category, string? line, DateTime? from, DateTime? to)
    {
        IEnumerable<AbnormalRecord> query = AbnormalRecords;
        if (!string.IsNullOrWhiteSpace(abNo))
        {
            var kw = abNo!.Trim();
            query = query.Where(a => a.AbNo.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status!.Trim();
            query = query.Where(a => a.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category!.Trim();
            query = query.Where(a => a.Category == cat);
        }
        if (!string.IsNullOrWhiteSpace(line))
        {
            var ln = line!.Trim();
            query = query.Where(a => a.Line == ln);
        }
        if (from.HasValue)
        {
            var start = from.Value.Date;
            query = query.Where(a => a.OccurTime >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(a => a.OccurTime <= end);
        }
        return query
            .OrderByDescending(a => a.OccurTime)
            .ThenByDescending(a => a.AbNo, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>按异常编号精确查询单张异常单（忽略大小写）</summary>
    public static AbnormalRecord? FindAbnormal(string abNo)
    {
        if (string.IsNullOrWhiteSpace(abNo)) return null;
        return AbnormalRecords.FirstOrDefault(a => string.Equals(a.AbNo, abNo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>确认登记：仅「待确认」可确认 → 状态置「已确认」+ 确认人/时间 + 追加「确认」履历。</summary>
    public static (bool Ok, string Message) ConfirmAbnormal(string abNo, string @operator)
    {
        var rec = FindAbnormal(abNo);
        if (rec is null) return (false, $"未找到异常单 {abNo}。");
        if (rec.Status == AbnormalStatus.Confirmed) return (false, $"异常单 {abNo} 已确认，请勿重复确认。");
        if (rec.Status == AbnormalStatus.Closed) return (false, $"异常单 {abNo} 已闭环，无法再确认。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        rec.Status = AbnormalStatus.Confirmed;
        rec.ConfirmBy = op;
        rec.ConfirmTime = now;
        AbnormalHistories.Add(MakeAbHistory(rec.AbNo, AbnormalAction.Confirm, op, now,
            "确认：登记内容核对无误，转后续负责人开展根因分析。"));
        return (true, $"异常单 {abNo} 已确认登记，状态变更为「{AbnormalStatus.Confirmed}」。");
    }

    /// <summary>
    /// 闭环分析：仅「已确认」可闭环 → 填写根本原因/根本措施/横展结论，
    /// 状态置「已闭环」+ 分析人/闭环时间 + 追加「闭环」履历（Remark 简述根本措施与横展结论）。
    /// </summary>
    public static (bool Ok, string Message) CloseAbnormal(
        string abNo, string rootCause, string rootMeasure, string horizonNeeded, string horizonScope, string @operator)
    {
        var rec = FindAbnormal(abNo);
        if (rec is null) return (false, $"未找到异常单 {abNo}。");
        if (rec.Status == AbnormalStatus.Pending) return (false, $"异常单 {abNo} 尚未确认登记，不能直接闭环。");
        if (rec.Status == AbnormalStatus.Closed) return (false, $"异常单 {abNo} 已闭环，请勿重复操作。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var cause = (rootCause ?? "").Trim();
        var measure = (rootMeasure ?? "").Trim();
        var horizon = AbnormalHorizon.All.Contains(horizonNeeded ?? "") ? horizonNeeded!.Trim() : AbnormalHorizon.NotNeeded;
        var scope = (horizonScope ?? "").Trim();
        var now = DateTime.Now;

        rec.RootCause = cause;
        rec.RootMeasure = measure;
        rec.AnalysisBy = op;
        rec.AnalysisTime = now;
        rec.HorizonNeeded = horizon;
        rec.HorizonScope = horizon == AbnormalHorizon.Needed ? scope : "";
        rec.Status = AbnormalStatus.Closed;
        rec.ClosedTime = now;

        var remark = $"闭环：根因={cause}；措施={measure}；横展={horizon}"
            + (horizon == AbnormalHorizon.Needed && scope.Length > 0 ? $"（{scope}）" : "");
        AbnormalHistories.Add(MakeAbHistory(rec.AbNo, AbnormalAction.Close, op, now, remark));
        return (true, $"异常单 {abNo} 已闭环，状态变更为「{AbnormalStatus.Closed}」。");
    }

    /// <summary>某异常单的操作履历（按时间倒序，同刻按 Seq 倒序）</summary>
    public static List<AbnormalHistory> GetAbnormalHistory(string abNo) =>
        AbnormalHistories
            .Where(h => h.AbNo == abNo)
            .OrderByDescending(h => h.Time)
            .ThenByDescending(h => h.Seq)
            .ToList();

    // ============================================================
    // WMS 仓储（增量功能）
    // 采购入库 / 现场领用 / 现场入库 / 现场退货 / 采购退货 / 物流出库 / 客户退货
    // ============================================================

    /// <summary>
    /// 播种 WMS 演示数据：物料主数据 10 条 + 期初基线库存 + 5 张演示单据（覆盖双向/双状态）。
    /// 种子库存 = 期初基线 + 所有「已过账」种子单据的净变更（与过账逻辑共用 ApplyWmsStockChange，逐键勾稽天然成立）。
    /// 单号内日期 == 该单 CreateTime.Date。
    /// </summary>
    private static void SeedWms()
    {
        if (WmsMaterials.Count > 0) return;
        var today = DateTime.Today;

        // ---- 物料主数据（原料 / 辅料 / 成品） ----
        WmsMaterials.AddRange(new[]
        {
            new WmsMaterial { Code = "M-ACT-CAR-01", MaterialName = "活性炭电极材料", Spec = "200F/g", Unit = "kg", Category = WmsCategories.Raw, SafetyStock = 100, MaxPurchaseQty = 500 },
            new WmsMaterial { Code = "M-ELE-ACN-02", MaterialName = "电解液（ACN基）", Spec = "1 mol/L", Unit = "L", Category = WmsCategories.Raw, SafetyStock = 80, MaxPurchaseQty = 200 },
            new WmsMaterial { Code = "M-SEP-PP-03", MaterialName = "隔膜（PP）", Spec = "25 μm", Unit = "m²", Category = WmsCategories.Raw, SafetyStock = 500, MaxPurchaseQty = 1000 },
            new WmsMaterial { Code = "M-COL-AL-04", MaterialName = "铝箔集流体", Spec = "20 μm", Unit = "m²", Category = WmsCategories.Raw, SafetyStock = 300, MaxPurchaseQty = 500 },
            new WmsMaterial { Code = "M-CAN-ALU-05", MaterialName = "铝壳外壳", Spec = "Φ18×40", Unit = "只", Category = WmsCategories.Raw, SafetyStock = 1000, MaxPurchaseQty = 10000 },
            new WmsMaterial { Code = "M-SEL-RUB-06", MaterialName = "密封圈", Spec = "Φ18", Unit = "只", Category = WmsCategories.Raw, SafetyStock = 1000, MaxPurchaseQty = 10000 },
            new WmsMaterial { Code = "M-TAB-NIC-07", MaterialName = "镍带极耳", Spec = "0.1×5 mm", Unit = "m", Category = WmsCategories.Aux, SafetyStock = 200, MaxPurchaseQty = 500 },
            // 包材主数据（2026-10-08 补）：彩盒此前只出现在 BOM 里，物料台账缺主数据，
            // 齐套校验与库存台账都会拿不到名称/单位。
            new WmsMaterial { Code = "M-PKG-BOX-11", MaterialName = "彩盒（100F 专用）", Spec = "200×60×40 mm", Unit = "个", Category = WmsCategories.Aux, SafetyStock = 300, MaxPurchaseQty = 800 },
            new WmsMaterial { Code = "M-INS-PET-08", MaterialName = "绝缘胶带（PET）", Spec = "10 mm", Unit = "m", Category = WmsCategories.Aux, SafetyStock = 150 },
            // 替代料主数据（技术方案第 4 章「替代料」用）：只作为主料的替代候选，不进任何 BOM。
            // 单独建主数据而不是硬编码名字，是为了让替代料也有库存、安全库存与采购前置期，
            // 否则"替代"只是纸面关系，算不出能顶多少。
            new WmsMaterial { Code = "M-COL-AL-04B", MaterialName = "铝箔集流体（18μm）", Spec = "18 μm", Unit = "m²", Category = WmsCategories.Raw, SafetyStock = 200, MaxPurchaseQty = 500 },
            new WmsMaterial { Code = "M-SEP-PE-03", MaterialName = "隔膜（PE）", Spec = "20 μm", Unit = "m²", Category = WmsCategories.Raw, SafetyStock = 300, MaxPurchaseQty = 1000 },
            new WmsMaterial { Code = "M-ELE-ACN-02B", MaterialName = "电解液（PC基）", Spec = "1 mol/L", Unit = "L", Category = WmsCategories.Raw, SafetyStock = 60, MaxPurchaseQty = 200 },
            new WmsMaterial { Code = "M-CAN-ALU-05B", MaterialName = "铝壳外壳（加高型）", Spec = "Φ18×42", Unit = "只", Category = WmsCategories.Raw, SafetyStock = 800, MaxPurchaseQty = 10000 },
            new WmsMaterial { Code = "M-FG-100F-09", MaterialName = "超级电容成品 SC-CX2026-100F", Spec = "100F/2.7V", Unit = "只", Category = WmsCategories.Finished, SafetyStock = 100 },
            new WmsMaterial { Code = "M-FG-300F-10", MaterialName = "超级电容成品 SC-CX2026-300F", Spec = "300F/2.7V", Unit = "只", Category = WmsCategories.Finished, SafetyStock = 80 },
        });

        // ---- 期初基线库存（尚未套用任何已过账种子单据；随后对已过账种子单逐行套用库存变更） ----
        var baseTime = today.AddDays(-4).AddHours(8);
        WmsStocks.AddRange(new[]
        {
            new WmsStock { MaterialCode = "M-ELE-ACN-02", MaterialName = "电解液（ACN基）", Spec = "1 mol/L", Unit = "L", LotNo = "EL-260812-012", Location = WmsLocations.RawA, Quantity = 60, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-COL-AL-04", MaterialName = "铝箔集流体", Spec = "20 μm", Unit = "m²", LotNo = "CC-260812-003", Location = WmsLocations.RawA, Quantity = 500, LastUpdate = baseTime },
            // ---- 辅材备料：铝壳 / 密封圈按只配套，用量最大（1 只/只 + 损耗）----
            // 数量口径（2026-10-08 智能排产轮调整）：原先 2000 / 1500 只，撑不住任何一个待下发 PO，
            // 导致每张单都缺料、"齐套通过"的正常路径演示不出来。调到 5000 后：
            //   · 小单（4000 只级）→ 满足，可演示齐套通过后正常开工
            //   · 大单（6000~10000 只级）→ 短缺，可演示缺料预警与补料建议
            new WmsStock { MaterialCode = "M-CAN-ALU-05", MaterialName = "铝壳外壳", Spec = "Φ18×40", Unit = "只", LotNo = "CAN-260810-001", Location = WmsLocations.RawA, Quantity = 5000, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-SEL-RUB-06", MaterialName = "密封圈", Spec = "Φ18", Unit = "只", LotNo = "SEL-260811-002", Location = WmsLocations.RawA, Quantity = 5000, LastUpdate = baseTime },
            // 极耳与彩盒：BOM 用量小但必须有料，缺了整单做不出来（此前库存台账里完全没有这两项）
            new WmsStock { MaterialCode = "M-TAB-NIC-07", MaterialName = "镍带极耳", Spec = "0.1×5 mm", Unit = "m", LotNo = "TAB-260811-001", Location = WmsLocations.RawA, Quantity = 120, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-PKG-BOX-11", MaterialName = "彩盒（100F 专用）", Spec = "200×60×40 mm", Unit = "个", LotNo = "BOX-260811-001", Location = WmsLocations.RawA, Quantity = 700, LastUpdate = baseTime },
            // 替代料库存（第 4 章替代料演示）：有货才谈得上"顶缺口"，数量刻意给成部分覆盖
            new WmsStock { MaterialCode = "M-COL-AL-04B", MaterialName = "铝箔集流体（18μm）", Spec = "18 μm", Unit = "m²", LotNo = "CC-260915-011", Location = WmsLocations.RawA, Quantity = 200, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-SEP-PE-03", MaterialName = "隔膜（PE）", Spec = "20 μm", Unit = "m²", LotNo = "SP-260918-012", Location = WmsLocations.RawA, Quantity = 300, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-ELE-ACN-02B", MaterialName = "电解液（PC基）", Spec = "1 mol/L", Unit = "L", LotNo = "EL-260920-013", Location = WmsLocations.RawA, Quantity = 60, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-CAN-ALU-05B", MaterialName = "铝壳外壳（加高型）", Spec = "Φ18×42", Unit = "只", LotNo = "CAN-260916-012", Location = WmsLocations.RawA, Quantity = 800, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-FG-100F-09", MaterialName = "超级电容成品 SC-CX2026-100F", Spec = "100F/2.7V", Unit = "只", LotNo = "FG-100F-260812-01", Location = WmsLocations.FinishedB, Quantity = 500, LastUpdate = baseTime },
            new WmsStock { MaterialCode = "M-FG-300F-10", MaterialName = "超级电容成品 SC-CX2026-300F", Spec = "300F/2.7V", Unit = "只", LotNo = "FG-300F-260813-01", Location = WmsLocations.FinishedB, Quantity = 220, LastUpdate = baseTime },
        });

        // ① 采购入库 - 已过账（含 3 条入库检记录，其中 1 条不合格）
        var t1 = today.AddDays(-3).AddHours(9).AddMinutes(20);
        var no1 = NextWmsNo(WmsOrderTypes.PurchaseInbound, t1);
        WmsOrders.Add(new WmsOrder
        {
            OrderNo = no1, Type = WmsOrderTypes.PurchaseInbound, Status = WmsOrderStatus.Posted,
            Partner = "碳源科技", RefNo = "", Reason = "", Operator = "张伟", Remark = "常规采购入库",
            CreateTime = t1, PostTime = t1.AddMinutes(40), PostBy = "张伟", LineCount = 2, TotalQty = 1100
        });
        var l1a = new WmsOrderLine { OrderNo = no1, MaterialCode = "M-ACT-CAR-01", MaterialName = "活性炭电极材料", Spec = "200F/g", Unit = "kg", LotNo = "PC-260812-L1-001", Quantity = 500, Location = WmsLocations.RawA };
        var l1b = new WmsOrderLine { OrderNo = no1, MaterialCode = "M-SEP-PP-03", MaterialName = "隔膜（PP）", Spec = "25 μm", Unit = "m²", LotNo = "SP-260812-007", Quantity = 600, Location = WmsLocations.RawA };
        WmsOrderLines.Add(l1a);
        WmsOrderLines.Add(l1b);
        WmsOrderHistories.Add(MakeWmsHistory(no1, WmsAction.Create, "张伟", t1, "创建：采购入库（供应商：碳源科技），共 2 行 / 合计 1100。"));
        WmsOrderHistories.Add(MakeWmsHistory(no1, WmsAction.Inspect, "张伟", t1.AddMinutes(20), "入库检：保存 3 条（不合格 1 条）。"));
        WmsOrderHistories.Add(MakeWmsHistory(no1, WmsAction.Post, "张伟", t1.AddMinutes(40), "过账：采购入库（供应商：碳源科技），共 2 行 / 合计 1100。"));
        WmsInspections.AddRange(new[]
        {
            new WmsInspectionRecord { OrderNo = no1, ItemName = "外观检查", StandardValue = "目视无异常", MeasuredValue = "无划痕变形", Result = "合格", Inspector = "张伟", InspectTime = t1.AddMinutes(20) },
            new WmsInspectionRecord { OrderNo = no1, ItemName = "尺寸(厚度)", StandardValue = "3.20±0.10 mm", MeasuredValue = "3.25 mm", Result = "合格", Inspector = "张伟", InspectTime = t1.AddMinutes(20) },
            new WmsInspectionRecord { OrderNo = no1, ItemName = "容量", StandardValue = "≥95 F", MeasuredValue = "88.4 F", Result = "不合格", Inspector = "张伟", InspectTime = t1.AddMinutes(20) },
        });
        // 套用已过账库存变更（与 PostWmsOrder 共用同一实现，保证逐键勾稽）
        ApplyWmsStockChange(l1a, l1a.Quantity, t1.AddMinutes(40));
        ApplyWmsStockChange(l1b, l1b.Quantity, t1.AddMinutes(40));

        // ② 现场领用 - 待过账（出库：L1 线体领用）
        var t2 = today.AddDays(-1).AddHours(10).AddMinutes(5);
        var no2 = NextWmsNo(WmsOrderTypes.SiteIssue, t2);
        WmsOrders.Add(new WmsOrder
        {
            OrderNo = no2, Type = WmsOrderTypes.SiteIssue, Status = WmsOrderStatus.Pending,
            Partner = "L1", RefNo = "", Reason = "", Operator = "李娜", Remark = "L1 线体领用",
            CreateTime = t2, LineCount = 2, TotalQty = 50
        });
        WmsOrderLines.Add(new WmsOrderLine { OrderNo = no2, MaterialCode = "M-ELE-ACN-02", MaterialName = "电解液（ACN基）", Spec = "1 mol/L", Unit = "L", LotNo = "EL-260812-012", Quantity = 20, Location = WmsLocations.RawA });
        WmsOrderLines.Add(new WmsOrderLine { OrderNo = no2, MaterialCode = "M-COL-AL-04", MaterialName = "铝箔集流体", Spec = "20 μm", Unit = "m²", LotNo = "CC-260812-003", Quantity = 30, Location = WmsLocations.RawA });
        WmsOrderHistories.Add(MakeWmsHistory(no2, WmsAction.Create, "李娜", t2, "创建：现场领用（领用线体：L1），共 2 行 / 合计 50。"));

        // ③ 物流出库 - 已过账（出库：客户，含物流单号）
        var t3 = today.AddDays(-2).AddHours(15).AddMinutes(30);
        var no3 = NextWmsNo(WmsOrderTypes.LogisticsOutbound, t3);
        WmsOrders.Add(new WmsOrder
        {
            OrderNo = no3, Type = WmsOrderTypes.LogisticsOutbound, Status = WmsOrderStatus.Posted,
            Partner = "华能储能", RefNo = "SF1234567890", Reason = "", Operator = "王强", Remark = "成品发运",
            CreateTime = t3, PostTime = t3.AddMinutes(30), PostBy = "王强", LineCount = 2, TotalQty = 300
        });
        var l3a = new WmsOrderLine { OrderNo = no3, MaterialCode = "M-FG-100F-09", MaterialName = "超级电容成品 SC-CX2026-100F", Spec = "100F/2.7V", Unit = "只", LotNo = "FG-100F-260812-01", Quantity = 200, Location = WmsLocations.FinishedB };
        var l3b = new WmsOrderLine { OrderNo = no3, MaterialCode = "M-FG-300F-10", MaterialName = "超级电容成品 SC-CX2026-300F", Spec = "300F/2.7V", Unit = "只", LotNo = "FG-300F-260813-01", Quantity = 100, Location = WmsLocations.FinishedB };
        WmsOrderLines.Add(l3a);
        WmsOrderLines.Add(l3b);
        WmsOrderHistories.Add(MakeWmsHistory(no3, WmsAction.Create, "王强", t3, "创建：物流出库（客户：华能储能，物流单号 SF1234567890），共 2 行 / 合计 300。"));
        WmsOrderHistories.Add(MakeWmsHistory(no3, WmsAction.Post, "王强", t3.AddMinutes(30), "过账：物流出库（客户：华能储能），共 2 行 / 合计 300。"));
        // 套用已过账库存变更（出库为负）
        ApplyWmsStockChange(l3a, -l3a.Quantity, t3.AddMinutes(30));
        ApplyWmsStockChange(l3b, -l3b.Quantity, t3.AddMinutes(30));

        // ④ 采购退货 - 待过账（出库：退供应商）
        var t4 = today.AddHours(9);
        var no4 = NextWmsNo(WmsOrderTypes.PurchaseReturn, t4);
        WmsOrders.Add(new WmsOrder
        {
            OrderNo = no4, Type = WmsOrderTypes.PurchaseReturn, Status = WmsOrderStatus.Pending,
            Partner = "膜材新材", RefNo = "", Reason = "批次外观异常退回供应商", Operator = "赵敏", Remark = "隔膜退货",
            CreateTime = t4, LineCount = 1, TotalQty = 50
        });
        WmsOrderLines.Add(new WmsOrderLine { OrderNo = no4, MaterialCode = "M-SEP-PP-03", MaterialName = "隔膜（PP）", Spec = "25 μm", Unit = "m²", LotNo = "SP-260812-007", Quantity = 50, Location = WmsLocations.RawA });
        WmsOrderHistories.Add(MakeWmsHistory(no4, WmsAction.Create, "赵敏", t4, "创建：采购退货（供应商：膜材新材），共 1 行 / 合计 50。"));

        // ⑤ 客户退货 - 已过账（入库：客户退回，默认入待检区，退货原因已填）
        var t5 = today.AddDays(-1).AddHours(16).AddMinutes(10);
        var no5 = NextWmsNo(WmsOrderTypes.CustomerReturn, t5);
        WmsOrders.Add(new WmsOrder
        {
            OrderNo = no5, Type = WmsOrderTypes.CustomerReturn, Status = WmsOrderStatus.Posted,
            Partner = "华能储能", RefNo = "SF9876543210", Reason = "客户反馈分档异常", Operator = "王强", Remark = "客户退回待检",
            CreateTime = t5, PostTime = t5.AddMinutes(25), PostBy = "王强", LineCount = 1, TotalQty = 30
        });
        var l5a = new WmsOrderLine { OrderNo = no5, MaterialCode = "M-FG-100F-09", MaterialName = "超级电容成品 SC-CX2026-100F", Spec = "100F/2.7V", Unit = "只", LotNo = "FG-100F-260812-01", Quantity = 30, Location = WmsLocations.PendingInspection };
        WmsOrderLines.Add(l5a);
        WmsOrderHistories.Add(MakeWmsHistory(no5, WmsAction.Create, "王强", t5, "创建：客户退货（客户：华能储能），共 1 行 / 合计 30。"));
        WmsOrderHistories.Add(MakeWmsHistory(no5, WmsAction.Post, "王强", t5.AddMinutes(25), "过账：客户退货（客户：华能储能），共 1 行 / 合计 30。"));
        // 套用已过账库存变更（入库为正 → 待检区 +30）
        ApplyWmsStockChange(l5a, l5a.Quantity, t5.AddMinutes(25));
    }

    /// <summary>
    /// ERP 集成种子：
    /// ① 6 条 PO（2 条已下发，分别关联既有工单 WO-20260812-001 / WO-20260813-002；4 条待下发，覆盖三个产品型号）；
    /// ② 三个产品型号的 BOM（各 6~8 项，含单位用量与损耗率）；
    /// ③ 1 条历史报工（关联已完成工单 WO-20260811-003），并同步该工单的报工状态 / 时间 / 累计工时，
    ///    用于演示工单列表「已报工」状态着色。
    /// </summary>
    /// <summary>
    /// 出货绑定种子（质量追溯链最后一公里）：
    /// ①补 14 只「已入库」成品电芯（8 只 SC-CX2026-100F + 6 只 SC-CX2026-300F，材料批次复用既有极片/电解液批次）；
    /// ②把其中 9 只（100F 6 只 + 300F 3 只）绑定到既有「物流出库」单（客户：华能储能），其余 5 只保持未绑定——
    /// 用于演示「召回反查」中同时出现"已出货"与"在库可冻结"电芯，以及「一键冻结影响电芯」。
    /// 注意：本方法使 Cells 由 7 只增至 21 只（其余既有基线不受影响）。
    /// </summary>
    private static void SeedShipment()
    {
        var today = DateTime.Today;
        const string wo100 = "WO-20260812-001";
        const string wo300 = "WO-20260813-002";

        // ---- ① 成品电芯：8 只 SC-CX2026-100F（极片批 PC-260812-L1-001 / 电解液批 EL-260812-012）----
        var fg100 = new[]
        {
            "SC-A260910-L1-00008", "SC-A260910-L1-00009", "SC-A260910-L1-00010", "SC-A260910-L1-00011",
            "SC-A260910-L1-00012", "SC-A260910-L1-00013", "SC-A260910-L1-00014", "SC-A260910-L1-00015"
        };
        for (var i = 0; i < fg100.Length; i++)
        {
            Cells.Add(new CellSn
            {
                Sn = fg100[i],
                WoNo = wo100,
                ProductModel = "SC-CX2026-100F",
                Line = "L1",
                Status = "已入库",
                CreateTime = today.AddDays(-5).AddHours(8 + i),
                ElectrodeLot = "PC-260812-L1-001",
                ElectrolyteLot = "EL-260812-012",
                ModuleSn = i < 4 ? "SM-A260913-L1-00031" : "",
                Result = "A1"
            });
        }

        // ---- 6 只 SC-CX2026-300F（极片批 PC-260813-L2-002 / 电解液批 EL-260813-015）----
        var fg300 = new[]
        {
            "SC-A260911-L2-00016", "SC-A260911-L2-00017", "SC-A260911-L2-00018",
            "SC-A260911-L2-00019", "SC-A260911-L2-00020", "SC-A260911-L2-00021"
        };
        for (var i = 0; i < fg300.Length; i++)
        {
            Cells.Add(new CellSn
            {
                Sn = fg300[i],
                WoNo = wo300,
                ProductModel = "SC-CX2026-300F",
                Line = "L2",
                Status = "已入库",
                CreateTime = today.AddDays(-4).AddHours(9 + i),
                ElectrodeLot = "PC-260813-L2-002",
                ElectrolyteLot = "EL-260813-015",
                ModuleSn = "",
                Result = "A2"
            });
        }

        // ---- ② 出货绑定：绑到既有物流出库单（已过账，客户「华能储能」）----
        var shipOrder = WmsOrders
            .Where(o => o.Type == WmsOrderTypes.LogisticsOutbound)
            .OrderBy(o => o.CreateTime)
            .FirstOrDefault();
        if (shipOrder is null) return;

        var bindTime = (shipOrder.PostTime ?? shipOrder.CreateTime).AddMinutes(-10); // 出货前扫码绑定
        var bindOp = string.IsNullOrWhiteSpace(shipOrder.PostBy) ? "王强" : shipOrder.PostBy;
        var boundSns = fg100.Take(6).Concat(fg300.Take(3)).ToList();   // 共 9 只

        foreach (var sn in boundSns)
        {
            var cell = Cells.First(c => string.Equals(c.Sn, sn, StringComparison.OrdinalIgnoreCase));
            var warehouseLot = WmsOrderLines
                .Where(l => string.Equals(l.OrderNo, shipOrder.OrderNo, StringComparison.OrdinalIgnoreCase)
                            && l.MaterialName.Contains(cell.ProductModel, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.LotNo)
                .FirstOrDefault() ?? "";
            WmsShipmentItems.Add(new WmsShipmentItem
            {
                OrderNo = shipOrder.OrderNo,
                CellSn = cell.Sn,
                ProductModel = cell.ProductModel,
                ElectrodeLot = cell.ElectrodeLot,
                ElectrolyteLot = cell.ElectrolyteLot,
                ModuleSn = cell.ModuleSn,
                Customer = shipOrder.Partner,
                WarehouseLot = warehouseLot,
                CellStatus = cell.Status,   // 出货时状态快照（种子电芯均为"已入库"）
                BindTime = bindTime,
                BindOperator = bindOp,
                Status = WmsShipmentStatus.Active
            });
        }
        // 其余 5 只（100F 最后 2 只 + 300F 最后 3 只）保持未绑定 → 演示「在库可冻结」
    }

    private static void SeedErp()
    {
        if (ErpPos.Count > 0) return;
        var today = DateTime.Today;

        // ---- ERP PO（6 条：2 已下发 + 4 待下发） ----
        ErpPos.AddRange(new[]
        {
            new ErpPo
            {
                PoNo = "PO-2026-0812-001", Customer = "国网系储能集成商", ProductModel = "SC-CX2026-100F",
                ProductName = "超级电容单体 100F/2.7V", OrderQty = 5000,
                DueDate = new DateTime(2026, 8, 14, 20, 0, 0), Status = ErpPoStatus.Pushed,
                PushedTime = new DateTime(2026, 8, 12, 7, 40, 0), Remark = "紧急交付，优先排产"
            },
            new ErpPo
            {
                PoNo = "PO-2026-0813-002", Customer = "新能源客车厂", ProductModel = "SC-CX2026-300F",
                ProductName = "超级电容单体 300F/2.7V", OrderQty = 3000,
                DueDate = new DateTime(2026, 8, 16, 20, 0, 0), Status = ErpPoStatus.Pushed,
                PushedTime = new DateTime(2026, 8, 13, 7, 35, 0), Remark = "分批发运"
            },
            new ErpPo
            {
                PoNo = "PO-2026-0914-003", Customer = "国网系储能集成商", ProductModel = "SC-CX2026-100F",
                ProductName = "超级电容单体 100F/2.7V", OrderQty = 6000,
                DueDate = today.AddDays(5), Status = ErpPoStatus.Pending, Remark = "追加订单"
            },
            new ErpPo
            {
                PoNo = "PO-2026-0914-004", Customer = "轨交牵引装备厂", ProductModel = "SC-CX2026-300F",
                ProductName = "超级电容单体 300F/2.7V", OrderQty = 4000,
                DueDate = today.AddDays(8), Status = ErpPoStatus.Pending, Remark = "需随货提供检测报告"
            },
            new ErpPo
            {
                PoNo = "PO-2026-0914-005", Customer = "新能源客车厂", ProductModel = "SC-CX2026-50F",
                ProductName = "超级电容单体 50F/2.7V", OrderQty = 8000,
                DueDate = today.AddDays(3), Status = ErpPoStatus.Pending, Remark = "首单试制"
            },
            new ErpPo
            {
                PoNo = "PO-2026-0914-006", Customer = "风电变桨系统厂", ProductModel = "SC-CX2026-50F",
                ProductName = "超级电容单体 50F/2.7V", OrderQty = 10000,
                DueDate = today.AddDays(10), Status = ErpPoStatus.Pending, Remark = ""
            },
        });

        // 已下发 PO 与既有工单建立 ERP 关联：仅补充工单的 ErpOrderNo 关联字段，不改动其余既有字段。
        // 目的：使工单页「ERP PO 信息」明细区可展示这两张既有工单的 PO 详情（演示用）。
        var linked1 = WorkOrders.FirstOrDefault(w => w.WoNo == "WO-20260812-001");
        if (linked1 is not null) linked1.ErpOrderNo = "PO-2026-0812-001";
        var linked2 = WorkOrders.FirstOrDefault(w => w.WoNo == "WO-20260813-002");
        if (linked2 is not null) linked2.ErpOrderNo = "PO-2026-0813-002";

        // ---- BOM：三个产品型号 ----
        AddBom("SC-CX2026-100F", new[]
        {
            ("M-ACT-CAR-01", "活性炭电极材料", "200F/g", "kg", 0.0125, 1.5, ErpMaterialTypes.Main),
            ("M-ELE-ACN-02", "电解液（ACN基）", "1 mol/L", "L", 0.0035, 2.0, ErpMaterialTypes.Main),
            ("M-SEP-PP-03", "隔膜（PP）", "25 μm", "m²", 0.0060, 2.0, ErpMaterialTypes.Main),
            ("M-COL-AL-04", "铝箔集流体", "20 μm", "m²", 0.0042, 3.0, ErpMaterialTypes.Main),
            ("M-CAN-ALU-05", "铝壳外壳", "Φ18×40", "只", 1.0, 0.5, ErpMaterialTypes.Aux),
            ("M-SEL-RUB-06", "密封圈", "Φ18", "只", 1.0, 0.5, ErpMaterialTypes.Aux),
            ("M-TAB-NIC-07", "镍带极耳", "0.1×5 mm", "m", 0.012, 3.0, ErpMaterialTypes.Aux),
            ("M-PKG-BOX-11", "彩盒（100F 专用）", "200×60×40 mm", "个", 0.1, 1.0, ErpMaterialTypes.Pack),
        });

        AddBom("SC-CX2026-300F", new[]
        {
            ("M-ACT-CAR-01", "活性炭电极材料", "200F/g", "kg", 0.0340, 1.5, ErpMaterialTypes.Main),
            ("M-ELE-ACN-02", "电解液（ACN基）", "1 mol/L", "L", 0.0095, 2.0, ErpMaterialTypes.Main),
            ("M-SEP-PP-03", "隔膜（PP）", "25 μm", "m²", 0.0150, 2.0, ErpMaterialTypes.Main),
            ("M-COL-AL-04", "铝箔集流体", "20 μm", "m²", 0.0110, 3.0, ErpMaterialTypes.Main),
            ("M-CAN-ALU-05", "铝壳外壳", "Φ18×40", "只", 1.0, 0.5, ErpMaterialTypes.Aux),
            ("M-SEL-RUB-06", "密封圈", "Φ18", "只", 1.0, 0.5, ErpMaterialTypes.Aux),
            ("M-TAB-NIC-07", "镍带极耳", "0.1×5 mm", "m", 0.018, 3.0, ErpMaterialTypes.Aux),
        });

        AddBom("SC-CX2026-50F", new[]
        {
            ("M-ACT-CAR-01", "活性炭电极材料", "200F/g", "kg", 0.0060, 1.5, ErpMaterialTypes.Main),
            ("M-ELE-ACN-02", "电解液（ACN基）", "1 mol/L", "L", 0.0018, 2.0, ErpMaterialTypes.Main),
            ("M-SEP-PP-03", "隔膜（PP）", "25 μm", "m²", 0.0032, 2.0, ErpMaterialTypes.Main),
            ("M-COL-AL-04", "铝箔集流体", "20 μm", "m²", 0.0024, 3.0, ErpMaterialTypes.Main),
            ("M-CAN-ALU-05", "铝壳外壳", "Φ18×40", "只", 1.0, 0.5, ErpMaterialTypes.Aux),
            ("M-SEL-RUB-06", "密封圈", "Φ18", "只", 1.0, 0.5, ErpMaterialTypes.Aux),
        });

        // ---- 历史报工（已完成工单 → 演示「已报工」状态着色） ----
        var doneWo = WorkOrders.FirstOrDefault(w => w.WoNo == "WO-20260811-003");
        if (doneWo is not null)
        {
            var reportTime = new DateTime(2026, 8, 12, 21, 30, 0);
            _erpReportSeq = 1;
            const double seededHours = 16.0;
            ErpWorkReports.Add(new ErpWorkReport
            {
                ReportNo = $"RPT-{reportTime:yyyyMMdd}-{_erpReportSeq:000}",
                WoNo = doneWo.WoNo,
                ErpOrderNo = doneWo.ErpOrderNo,
                OutputQty = 8000,
                GoodQty = 7959,
                NgQty = 41,
                WorkHours = seededHours,
                Operator = "王强",
                ReportTime = reportTime,
                ErpStatus = ErpUploadStatus.Success,
                ErpReceipt = "ACK-000001",
                Remark = "完工报工（种子数据）"
            });
            doneWo.ErpReportStatus = ErpReportStatus.Reported;
            doneWo.ErpReportTime = reportTime;
            doneWo.WorkHours = seededHours;
        }
    }

    /// <summary>按型号批量追加 BOM 行（元组字段：物料编码 / 名称 / 规格 / 单位 / 单位用量 / 损耗率% / 类型）</summary>
    private static void AddBom(
        string productModel,
        (string Code, string Name, string Spec, string Unit, double QtyPerUnit, double LossRate, string Type)[] rows)
    {
        foreach (var r in rows)
        {
            ErpBomItems.Add(new ErpBomItem
            {
                ProductModel = productModel,
                MaterialCode = r.Code,
                MaterialName = r.Name,
                Spec = r.Spec,
                Unit = r.Unit,
                QtyPerUnit = r.QtyPerUnit,
                LossRate = r.LossRate,
                MaterialType = r.Type
            });
        }
    }

    /// <summary>
    /// 生成下一个 WMS 单据号：{前缀}-{createTime:yyyyMMdd}-序号(3 位)；
    /// 单号内日期取自传入时间（== 该单 CreateTime.Date），序号全局递增、跨天自然延续。
    /// </summary>
    private static string NextWmsNo(string type, DateTime createTime)
    {
        _wmsSeq++;
        return $"{WmsOrderTypes.Prefix(type)}-{createTime:yyyyMMdd}-{_wmsSeq:000}";
    }

    /// <summary>
    /// 套用一条库存台账变更（入库为正、出库为负）：按 (物料,批次,库位) 查找；
    /// 不存在且为增量时新建台账行（补物料名称/规格/单位）；变更后若数量 ≈ 0（|q| &lt;= 1e-9）则移除该台账行。
    /// 与 PostWmsOrder 共用同一实现，保证种子勾稽与线上过账口径一致（不出现两份库存逻辑）。
    /// </summary>
    private static void ApplyWmsStockChange(WmsOrderLine line, double signedQty, DateTime updateTime)
    {
        const double eps = 1e-9;
        var stock = FindWmsStock(line.MaterialCode, line.LotNo, line.Location);
        if (stock is null)
        {
            if (signedQty <= eps) return; // 无台账行且非增量变更：无可操作
            var mat = FindWmsMaterial(line.MaterialCode);
            WmsStocks.Add(new WmsStock
            {
                MaterialCode = line.MaterialCode,
                MaterialName = string.IsNullOrWhiteSpace(line.MaterialName) ? (mat?.MaterialName ?? "") : line.MaterialName,
                Spec = string.IsNullOrWhiteSpace(line.Spec) ? (mat?.Spec ?? "") : line.Spec,
                Unit = string.IsNullOrWhiteSpace(line.Unit) ? (mat?.Unit ?? "") : line.Unit,
                LotNo = line.LotNo,
                Location = line.Location,
                Quantity = signedQty,
                LastUpdate = updateTime
            });
            return;
        }

        // 同物料+批次+库位多次变更 → 合并数量，不产生重复台账行
        stock.Quantity += signedQty;
        stock.LastUpdate = updateTime;
        if (string.IsNullOrWhiteSpace(stock.MaterialName) && !string.IsNullOrWhiteSpace(line.MaterialName)) stock.MaterialName = line.MaterialName;
        if (string.IsNullOrWhiteSpace(stock.Spec)) stock.Spec = line.Spec;
        if (string.IsNullOrWhiteSpace(stock.Unit)) stock.Unit = line.Unit;

        // 出库恰好清空（或异常为负）→ 移除该台账行（WMS 习惯：无库存不显示该键）
        if (stock.Quantity <= eps) WmsStocks.Remove(stock);
    }

    /// <summary>构造一条 WMS 履历（Seq 自增）</summary>
    private static WmsOrderHistory MakeWmsHistory(string orderNo, string action, string @operator, DateTime time, string remark)
    {
        _wmsHistorySeq++;
        return new WmsOrderHistory
        {
            Seq = _wmsHistorySeq,
            OrderNo = orderNo,
            Action = action,
            Operator = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim(),
            Time = time,
            Remark = remark
        };
    }

    // ---------------- WMS 物料主数据 ----------------

    /// <summary>按编码精确查询物料主数据（忽略大小写）</summary>
    public static WmsMaterial? FindWmsMaterial(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return WmsMaterials.FirstOrDefault(m => string.Equals(m.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // ---------------- WMS 单据 ----------------

    /// <summary>
    /// 新建 WMS 单据：校验类型/往来对象/明细非空 → 生成单号（状态=待过账）→ 写明细 + 履历(创建)。
    /// </summary>
    public static (bool Ok, string Message, string OrderNo) CreateWmsOrder(
        string type, string partner, string refNo, string reason, string @operator, string remark, List<WmsOrderLine> lines)
    {
        if (string.IsNullOrWhiteSpace(type) || !WmsOrderTypes.All.Contains(type.Trim()))
            return (false, "单据类型无效。", "");
        var t = type.Trim();
        if (string.IsNullOrWhiteSpace(partner))
            return (false, $"请填写{WmsOrderTypes.PartnerLabel(t)}。", "");

        var validLines = (lines ?? new List<WmsOrderLine>())
            .Where(l => !string.IsNullOrWhiteSpace(l.MaterialCode) && l.Quantity > 0)
            .ToList();
        if (validLines.Count == 0)
            return (false, "单据至少需要 1 行有效明细（物料编码非空且数量 > 0）。", "");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();

        // 仓储层防御性校验（避免绕过 WmsOrderEditForm 时缺关键字段）
        if (WmsOrderTypes.RequiresRefNo(t) && string.IsNullOrWhiteSpace(refNo))
            return (false, "物流出库需填写物流单号。", "");
        if (WmsOrderTypes.RequiresReturnReason(t) && string.IsNullOrWhiteSpace(reason))
            return (false, "退货类单据需填写退货原因。", "");

        var now = DateTime.Now;
        var orderNo = NextWmsNo(t, now);

        WmsOrders.Add(new WmsOrder
        {
            OrderNo = orderNo,
            Type = t,
            Status = WmsOrderStatus.Pending,
            Partner = partner.Trim(),
            RefNo = refNo?.Trim() ?? "",
            Reason = reason?.Trim() ?? "",
            Operator = op,
            Remark = remark?.Trim() ?? "",
            CreateTime = now,
            LineCount = validLines.Count,
            TotalQty = validLines.Sum(l => l.Quantity)
        });

        foreach (var l in validLines)
        {
            var mat = FindWmsMaterial(l.MaterialCode);
            WmsOrderLines.Add(new WmsOrderLine
            {
                OrderNo = orderNo,
                MaterialCode = l.MaterialCode.Trim(),
                MaterialName = string.IsNullOrWhiteSpace(l.MaterialName) ? (mat?.MaterialName ?? "") : l.MaterialName.Trim(),
                Spec = string.IsNullOrWhiteSpace(l.Spec) ? (mat?.Spec ?? "") : l.Spec.Trim(),
                Unit = string.IsNullOrWhiteSpace(l.Unit) ? (mat?.Unit ?? "") : l.Unit.Trim(),
                LotNo = l.LotNo?.Trim() ?? "",
                Quantity = l.Quantity,
                Location = string.IsNullOrWhiteSpace(l.Location) ? WmsLocations.RawA : l.Location.Trim(),
                Remark = l.Remark?.Trim() ?? ""
            });
        }

        var order = WmsOrders.First(o => o.OrderNo == orderNo);
        WmsOrderHistories.Add(MakeWmsHistory(orderNo, WmsAction.Create, op, now,
            $"创建：{t}（{WmsOrderTypes.PartnerLabel(t)}：{order.Partner}），共 {validLines.Count} 行 / 合计 {order.TotalQty:0.##}。"));
        return (true, $"单据 {orderNo} 已创建（{WmsOrderStatus.Pending}）。", orderNo);
    }

    /// <summary>
    /// 按条件查询 WMS 单据：空条件不过滤；keyword 匹配 单号/往来对象/经手人（忽略大小写包含）；
    /// from/to 按创建时间日期闭区间（含当日全天）；按创建时间倒序、同刻按单号倒序；并填充 LineCount/TotalQty。
    /// </summary>
    public static List<WmsOrder> QueryWmsOrders(string? type, string? status, string? keyword, DateTime? from, DateTime? to)
    {
        IEnumerable<WmsOrder> query = WmsOrders;
        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type!.Trim();
            query = query.Where(o => o.Type == t);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status!.Trim();
            query = query.Where(o => o.Status == s);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword!.Trim();
            query = query.Where(o =>
                (o.OrderNo.Length > 0 && o.OrderNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (o.Partner.Length > 0 && o.Partner.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (o.Operator.Length > 0 && o.Operator.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }
        if (from.HasValue)
        {
            var start = from.Value.Date;
            query = query.Where(o => o.CreateTime >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(o => o.CreateTime <= end);
        }

        var list = query
            .OrderByDescending(o => o.CreateTime)
            .ThenByDescending(o => o.OrderNo, StringComparer.Ordinal)
            .ToList();
        foreach (var o in list)
        {
            var lines = WmsOrderLines.Where(l => l.OrderNo == o.OrderNo).ToList();
            o.LineCount = lines.Count;
            o.TotalQty = lines.Sum(l => l.Quantity);
        }
        return list;
    }

    /// <summary>按单据号精确查询 WMS 单据（忽略大小写）</summary>
    public static WmsOrder? FindWmsOrder(string orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) return null;
        return WmsOrders.FirstOrDefault(o => string.Equals(o.OrderNo, orderNo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>某单据的明细行（按录入顺序）</summary>
    public static List<WmsOrderLine> GetWmsOrderLines(string orderNo) =>
        WmsOrderLines.Where(l => l.OrderNo == orderNo).ToList();

    /// <summary>
    /// 过账 WMS 单据：校验存在且状态=待过账且明细非空。
    /// 出库类型：逐 (物料,批次,库位) 汇总需求与库存比对，任一不足 → 失败并列出不足明细（库存与状态零变化）；
    /// 入库类型：逐行加库存（台账不存在则新建并补 MaterialName/Spec/Unit）。
    /// 成功后 status=已过账 + PostTime/PostBy + 履历(过账)；重复过账 / 不存在 → 失败消息。
    /// </summary>
    public static (bool Ok, string Message) PostWmsOrder(string orderNo, string @operator)
    {
        var order = FindWmsOrder(orderNo);
        if (order is null) return (false, $"未找到单据 {orderNo}。");
        if (order.Status == WmsOrderStatus.Posted) return (false, $"单据 {order.OrderNo} 已过账，请勿重复过账。");

        var lines = GetWmsOrderLines(order.OrderNo);
        if (lines.Count == 0) return (false, $"单据 {order.OrderNo} 没有明细，无法过账。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;

        if (WmsOrderTypes.IsInbound(order.Type))
        {
            // 入库：共用台账变更实现（新键自动建行、同键合并累加）
            foreach (var l in lines)
                ApplyWmsStockChange(l, l.Quantity, now);
        }
        else
        {
            // 出库：先整体校验再扣减，保证任一不足时库存与状态零变化
            var demand = lines
                .GroupBy(l => (Code: l.MaterialCode, Lot: l.LotNo, Loc: l.Location))
                .Select(g => new { g.Key.Code, g.Key.Lot, g.Key.Loc, Need = g.Sum(x => x.Quantity) })
                .ToList();

            var shortages = new List<string>();
            foreach (var d in demand)
            {
                var stock = FindWmsStock(d.Code, d.Lot, d.Loc);
                var avail = stock?.Quantity ?? 0;
                // 容差比较：d.Need 为多行同键需求求和，浮点误差（如 0.1+0.2=0.30000000000000004）不得误判为不足；
                // 与 ApplyWmsStockChange 的 |q|<=1e-9 归零判定保持同一容差口径。真实缺口 > 1e-9 一律拒绝。
                if (avail < d.Need - 1e-9)
                {
                    var lotText = string.IsNullOrWhiteSpace(d.Lot) ? "—" : d.Lot;
                    shortages.Add($"物料 {d.Code} / 批次 {lotText} / 库位 {d.Loc}：需求 {d.Need:0.##}，可用 {avail:0.##}");
                }
            }
            if (shortages.Count > 0)
                return (false, "库存不足，过账失败：\n" + string.Join("\n", shortages));

            // 逐键扣减（共用台账变更实现；扣减后清空的台账行会被移除）
            foreach (var d in demand)
                ApplyWmsStockChange(
                    new WmsOrderLine { MaterialCode = d.Code, LotNo = d.Lot, Location = d.Loc },
                    -d.Need, now);
        }

        order.Status = WmsOrderStatus.Posted;
        order.PostTime = now;
        order.PostBy = op;
        WmsOrderHistories.Add(MakeWmsHistory(order.OrderNo, WmsAction.Post, op, now,
            $"过账：{order.Type}（{WmsOrderTypes.PartnerLabel(order.Type)}：{order.Partner}），共 {lines.Count} 行 / 合计 {lines.Sum(l => l.Quantity):0.##}。"));
        return (true, $"单据 {order.OrderNo} 已过账（{WmsOrderStatus.Posted}）。");
    }

    /// <summary>某单据的操作履历（按时间倒序，同刻按 Seq 倒序）</summary>
    public static List<WmsOrderHistory> GetWmsOrderHistory(string orderNo) =>
        WmsOrderHistories
            .Where(h => h.OrderNo == orderNo)
            .OrderByDescending(h => h.Time)
            .ThenByDescending(h => h.Seq)
            .ToList();

    // ---------------- WMS 库存台账 ----------------

    /// <summary>按 (物料,批次,库位) 精确查找台账行（均忽略大小写）</summary>
    private static WmsStock? FindWmsStock(string materialCode, string lotNo, string location) =>
        WmsStocks.FirstOrDefault(s =>
            string.Equals(s.MaterialCode, materialCode ?? "", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.LotNo ?? "", lotNo ?? "", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Location ?? "", location ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 按条件查询库存台账：keyword 匹配 编码/名称/批次/库位（忽略大小写包含）；
    /// 排序：物料编码 → 批次 → 库位。
    /// </summary>
    public static List<WmsStock> QueryWmsStock(string? keyword)
    {
        IEnumerable<WmsStock> query = WmsStocks;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword!.Trim();
            query = query.Where(s =>
                (s.MaterialCode.Length > 0 && s.MaterialCode.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (s.MaterialName.Length > 0 && s.MaterialName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (s.LotNo.Length > 0 && s.LotNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (s.Location.Length > 0 && s.Location.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }
        return query
            .OrderBy(s => s.MaterialCode, StringComparer.Ordinal)
            .ThenBy(s => s.LotNo, StringComparer.Ordinal)
            .ThenBy(s => s.Location, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>库存合计数量（可按关键字过滤）</summary>
    public static double GetWmsStockTotal(string? keyword = null) =>
        QueryWmsStock(keyword).Sum(s => s.Quantity);

    /// <summary>
    /// 某物料在库存台账中的现存批次（仅返回数量 &gt; 1e-9 的台账行所对应的批次）：
    /// 去重（忽略大小写比较）、按批次排序（Ordinal）。
    /// 用途：WMS 出库类单据「批次」列联动的候选来源；也为库存查询等页面复用。
    /// 纯只读查询，不修改任何集合。
    /// </summary>
    public static List<string> GetWmsStockLots(string materialCode)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return new List<string>();
        var code = materialCode.Trim();
        return WmsStocks
            .Where(s => s.Quantity > 1e-9
                && string.Equals(s.MaterialCode, code, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.LotNo ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 某「物料 + 批次」下有库存的库位（仅返回数量 &gt; 1e-9 的台账行所对应的库位）：
    /// 去重（忽略大小写比较）、按库位排序（Ordinal）。
    /// 用途：WMS 出库类单据「库位」列联动的候选来源；也为库存查询等页面复用。
    /// 纯只读查询，不修改任何集合。
    /// </summary>
    public static List<string> GetWmsStockLocations(string materialCode, string lotNo)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return new List<string>();
        var code = materialCode.Trim();
        var lot = lotNo ?? "";
        return WmsStocks
            .Where(s => s.Quantity > 1e-9
                && string.Equals(s.MaterialCode, code, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.LotNo ?? "", lot, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Location ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 某物料在库存台账中的有库存台账行（数量 &gt; 1e-9，按 批次 → 库位 排序）。
    /// 用途：便于库存查询等页面复用（可在 UI 侧进一步按批次/库位聚合）。
    /// 纯只读查询，不修改任何集合。
    /// </summary>
    public static List<WmsStock> GetWmsStockEntries(string materialCode)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return new List<WmsStock>();
        var code = materialCode.Trim();
        return WmsStocks
            .Where(s => s.Quantity > 1e-9
                && string.Equals(s.MaterialCode, code, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.LotNo, StringComparer.Ordinal)
            .ThenBy(s => s.Location, StringComparer.Ordinal)
            .ToList();
    }

    // ================= 出货绑定（物流出库 ↔ 电芯SN） =================

    /// <summary>
    /// 查询可绑定出货电芯的单据：仅「物流出库」类型；关键字匹配 单号/客户/物流单号（RefNo）；
    /// 创建时间倒序；Result 已填充 LineCount/TotalQty（复用 QueryWmsOrders 的 type 过滤 + 填充）。
    /// </summary>
    public static List<WmsOrder> QueryShipmentOrders(string? status, string? keyword)
    {
        return QueryWmsOrders(WmsOrderTypes.LogisticsOutbound, status, keyword, null, null);
    }

    /// <summary>
    /// 某出货单的绑定明细（默认只返回「有效」状态；includeUnbound=true 时含已解绑）。
    /// 按绑定时间升序、SN 升序。
    /// </summary>
    public static List<WmsShipmentItem> GetShipmentItems(string orderNo, bool includeUnbound = false)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) return new List<WmsShipmentItem>();
        var no = orderNo.Trim();
        IEnumerable<WmsShipmentItem> q = WmsShipmentItems
            .Where(i => string.Equals(i.OrderNo, no, StringComparison.OrdinalIgnoreCase));
        if (!includeUnbound) q = q.Where(i => i.Status == WmsShipmentStatus.Active);
        return q
            .OrderBy(i => i.BindTime)
            .ThenBy(i => i.CellSn, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>某出货单当前有效绑定的电芯数量</summary>
    public static int GetShipmentBoundCount(string orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) return 0;
        var no = orderNo.Trim();
        return WmsShipmentItems.Count(i =>
            string.Equals(i.OrderNo, no, StringComparison.OrdinalIgnoreCase)
            && i.Status == WmsShipmentStatus.Active);
    }

    /// <summary>
    /// 候选绑定电芯：Cells 中满足「**可出货状态**（合格 / 已入库，见 <see cref="CellSnStatus"/>）」
    /// 且「未在任何有效出货绑定中」的电芯（含本单已绑的也排除，因其已显示在下清单）。
    /// 支持按 型号 / 极片批号 / 关键字（SN 或 型号 或 批号）过滤；SN 升序。
    /// 纯查询，不修改任何集合。
    /// </summary>
    public static List<CellSn> GetShipmentCandidates(string orderNo, string? productModel, string? lotNo, string? keyword)
    {
        var boundSns = new HashSet<string>(
            WmsShipmentItems.Where(i => i.Status == WmsShipmentStatus.Active).Select(i => i.CellSn),
            StringComparer.OrdinalIgnoreCase);

        // 业务约束：出库电芯必须"生产完成且合格"——不合规状态（在制/化成中/检测中/不良）不进候选
        IEnumerable<CellSn> q = Cells.Where(c => !boundSns.Contains(c.Sn) && CellSnStatus.IsShippable(c.Status));

        if (!string.IsNullOrWhiteSpace(productModel))
        {
            var pm = productModel!.Trim();
            q = q.Where(c => string.Equals(c.ProductModel, pm, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(lotNo))
        {
            var lot = lotNo!.Trim();
            q = q.Where(c => string.Equals(c.ElectrodeLot, lot, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword!.Trim();
            q = q.Where(c =>
                c.Sn.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || c.ProductModel.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || c.ElectrodeLot.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || c.ElectrolyteLot.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || c.ModuleSn.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderBy(c => c.Sn, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 绑定出货电芯：校验单据存在且类型=物流出库；SN 去重非空；
    /// ① **状态不合规**（非「合格 / 已入库」，见 <see cref="CellSnStatus"/>）→ StatusRejected；
    /// ② 已在其他有效绑定的 SN（含本单已绑）→ Skipped（一芯一单）；
    /// ③ 其余写 WmsShipmentItem（快照 型号/状态/极片批/电解液批/模组/客户/成品批次）。
    /// </summary>
    public static (bool Ok, string Message, int Bound, List<string> Skipped, List<string> StatusRejected) BindShipmentCells(
        string orderNo, IReadOnlyCollection<string> cellSns, string @operator)
    {
        var order = FindWmsOrder(orderNo);
        if (order is null) return (false, $"未找到单据 {orderNo}。", 0, new List<string>(), new List<string>());
        if (order.Type != WmsOrderTypes.LogisticsOutbound)
            return (false, $"仅「物流出库」类型单据可绑定出货电芯（当前类型：{order.Type}）。", 0, new List<string>(), new List<string>());

        var input = (cellSns ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (input.Count == 0) return (false, "请先选择要绑定的电芯。", 0, new List<string>(), new List<string>());

        var activeByCell = GetActiveShipmentOrderByCell();
        var cellMap = Cells.ToDictionary(c => c.Sn, StringComparer.OrdinalIgnoreCase);
        var lines = GetWmsOrderLines(order.OrderNo);

        var bound = 0;
        var skipped = new List<string>();
        var statusRejected = new List<string>();
        var now = DateTime.Now;
        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();

        foreach (var sn in input)
        {
            if (activeByCell.TryGetValue(sn, out _))
            {
                // 已在其他（或本单）有效绑定 → 跳过，不重复写入（一芯一单）
                skipped.Add(sn);
                continue;
            }
            if (!cellMap.TryGetValue(sn, out var cell))
            {
                skipped.Add(sn); // 未知 SN，无法快照，跳过
                continue;
            }
            // 业务约束兜底：出库电芯必须"生产完成且合格"（UI 候选已过滤，此处防绕过）
            if (!CellSnStatus.IsShippable(cell.Status))
            {
                statusRejected.Add($"{sn}（{cell.Status}）");
                continue;
            }
            var warehouseLot = lines
                .Where(l => !string.IsNullOrWhiteSpace(l.MaterialName)
                            && l.MaterialName.Contains(cell.ProductModel, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.LotNo)
                .FirstOrDefault() ?? "";
            WmsShipmentItems.Add(new WmsShipmentItem
            {
                OrderNo = order.OrderNo,
                CellSn = cell.Sn,
                ProductModel = cell.ProductModel,
                CellStatus = cell.Status,
                ElectrodeLot = cell.ElectrodeLot,
                ElectrolyteLot = cell.ElectrolyteLot,
                ModuleSn = cell.ModuleSn,
                Customer = order.Partner,
                WarehouseLot = warehouseLot,
                BindTime = now,
                BindOperator = op,
                Status = WmsShipmentStatus.Active
            });
            bound++;
        }

        var reasons = new List<string>();
        if (skipped.Count > 0) reasons.Add($"已在其他出货单 {skipped.Count} 只");
        if (statusRejected.Count > 0)
            reasons.Add($"状态不可出货 {statusRejected.Count} 只（{string.Join("、", statusRejected.Take(3))}{(statusRejected.Count > 3 ? " 等" : "")}）");

        var msg = bound > 0
            ? $"绑定完成：成功 {bound} 只" + (reasons.Count > 0 ? $"，跳过：{string.Join("；", reasons)}。" : "。")
            : $"未绑定：{input.Count} 只电芯均不可绑定" + (reasons.Count > 0 ? $"（{string.Join("；", reasons)}）。" : "。");
        return (true, msg, bound, skipped, statusRejected);
    }

    /// <summary>
    /// 解除出货绑定：标记「已解绑」并记录解绑时间/人/备注；找不到有效绑定 → 失败零变化。
    /// </summary>
    public static (bool Ok, string Message) UnbindShipmentCell(string orderNo, string cellSn, string @operator, string remark = "")
    {
        if (string.IsNullOrWhiteSpace(orderNo) || string.IsNullOrWhiteSpace(cellSn))
            return (false, "单据号与电芯 SN 均不可为空。");
        var no = orderNo.Trim();
        var sn = cellSn.Trim();
        var item = WmsShipmentItems.FirstOrDefault(i =>
            string.Equals(i.OrderNo, no, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.CellSn, sn, StringComparison.OrdinalIgnoreCase)
            && i.Status == WmsShipmentStatus.Active);
        if (item is null) return (false, $"未找到电芯 {sn} 在出货单 {no} 的有效绑定记录。");

        item.Status = WmsShipmentStatus.Unbound;
        item.UnbindTime = DateTime.Now;
        item.UnbindOperator = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        item.UnbindRemark = remark?.Trim() ?? "";
        return (true, $"电芯 {sn} 已解除与出货单 {no} 的绑定。");
    }

    /// <summary>构建 电芯SN → 有效出货单号 映射（供候选/反查复用）；同一 SN 取第一条有效绑定。</summary>
    public static Dictionary<string, string> GetActiveShipmentOrderByCell()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in WmsShipmentItems)
        {
            if (i.Status != WmsShipmentStatus.Active) continue;
            if (!map.ContainsKey(i.CellSn)) map[i.CellSn] = i.OrderNo;
        }
        return map;
    }

    // ================= 召回反查 =================

    /// <summary>
    /// 召回反查：给定一个「问题源」（电芯SN / 材料批次 / 出货单号 / 客户），反查影响范围。
    /// 纯查询，不修改任何集合；空/找不到时返回 Message 且各列表为空，不抛异常。
    /// 同源扩散：以电芯的极片批/电解液批为纽带，找出共用这些批次的其他电芯（潜在受影响）。
    /// </summary>
    public static RecallQueryResult RecallQuery(string mode, string keyword)
    {
        var result = new RecallQueryResult { Mode = mode, Keyword = keyword ?? "" };
        keyword = (keyword ?? "").Trim();

        var activeByCell = GetActiveShipmentOrderByCell();
        var orderMap = WmsOrders.ToDictionary(o => o.OrderNo, StringComparer.OrdinalIgnoreCase);
        var matMap = Materials.ToDictionary(m => m.LotNo, StringComparer.OrdinalIgnoreCase);
        var holdByCell = BuildActiveHoldByCell();

        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (mode == RecallMode.CellSn)
        {
            var cell = Cells.FirstOrDefault(c => string.Equals(c.Sn, keyword, StringComparison.OrdinalIgnoreCase));
            if (cell is null) { result.Message = $"未找到电芯 {keyword}。"; return result; }
            affected.Add(cell.Sn);
            ExpandByLots(affected, cell.ElectrodeLot, cell.ElectrolyteLot);
        }
        else if (mode == RecallMode.MaterialLot)
        {
            if (string.IsNullOrWhiteSpace(keyword)) { result.Message = "请输入材料批次号。"; return result; }
            var lot = keyword;
            var found = false;
            foreach (var c in Cells)
            {
                if (string.Equals(c.ElectrodeLot, lot, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.ElectrolyteLot, lot, StringComparison.OrdinalIgnoreCase))
                {
                    affected.Add(c.Sn);
                    found = true;
                }
            }
            if (!found) { result.Message = $"未找到使用批次 {keyword} 的电芯。"; return result; }
        }
        else if (mode == RecallMode.ShipmentOrder)
        {
            var order = FindWmsOrder(keyword);
            if (order is null) { result.Message = $"未找到出货单 {keyword}。"; return result; }
            var bound = GetShipmentItems(order.OrderNo, false);
            if (bound.Count == 0) { result.Message = $"出货单 {keyword} 暂无有效绑定电芯。"; return result; }
            var seedSns = bound.Select(b => b.CellSn).ToList();
            foreach (var sn in seedSns) affected.Add(sn);
            ExpandByCells(affected, seedSns);
        }
        else if (mode == RecallMode.Customer)
        {
            if (string.IsNullOrWhiteSpace(keyword)) { result.Message = "请输入客户名称。"; return result; }
            var custOrders = WmsOrders.Where(o =>
                o.Type == WmsOrderTypes.LogisticsOutbound
                && string.Equals(o.Partner, keyword, StringComparison.OrdinalIgnoreCase)).ToList();
            var bound = custOrders.SelectMany(o => GetShipmentItems(o.OrderNo, false)).ToList();
            if (bound.Count == 0) { result.Message = $"客户 {keyword} 暂无有效绑定电芯。"; return result; }
            var seedSns = bound.Select(b => b.CellSn).ToList();
            foreach (var sn in seedSns) affected.Add(sn);
            ExpandByCells(affected, seedSns);
        }
        else
        {
            result.Message = "未知的反查方式。";
            return result;
        }

        // ---- 构建受影响电芯行 + 批次集合 ----
        var cellMap = Cells.ToDictionary(c => c.Sn, StringComparer.OrdinalIgnoreCase);
        var shipmentRows = new List<RecallShipmentRow>();
        var cellRows = new List<RecallCellRow>();
        var lotSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shipmentOrderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lotCellCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lotShipmentSet = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sn in affected)
        {
            if (!cellMap.TryGetValue(sn, out var cell)) continue;
            var shipped = activeByCell.TryGetValue(sn, out var shipOrderNo);
            var orderNo = shipped ? shipOrderNo! : "";
            var cust = "";
            DateTime? shipTime = null;
            var refNo = "";
            if (shipped && orderMap.TryGetValue(orderNo, out var ord))
            {
                cust = ord.Partner;
                shipTime = ord.PostTime;
                refNo = ord.RefNo;
            }
            var held = holdByCell.TryGetValue(sn, out var hold);
            var holderNo = held ? hold!.HoldNo : "";

            // 成品批次（绑定记录中的 WarehouseLot）
            var warehouseLot = "";
            if (shipped)
            {
                var bind = WmsShipmentItems.FirstOrDefault(i =>
                    string.Equals(i.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(i.CellSn, sn, StringComparison.OrdinalIgnoreCase)
                    && i.Status == WmsShipmentStatus.Active);
                warehouseLot = bind?.WarehouseLot ?? "";
            }

            cellRows.Add(new RecallCellRow
            {
                CellSn = cell.Sn,
                ProductModel = cell.ProductModel,
                Status = cell.Status,
                Shipped = shipped,
                OrderNo = orderNo,
                Customer = cust,
                StockLocation = StockLocationOf(cell),
                StockQty = StockQtyOf(cell),
                Held = held,
                HolderNo = holderNo,
                ElectrodeLot = cell.ElectrodeLot,
                ElectrolyteLot = cell.ElectrolyteLot
            });

            if (shipped)
            {
                shipmentOrderSet.Add(orderNo);
                if (!string.IsNullOrWhiteSpace(cust)) customerSet.Add(cust);
                shipmentRows.Add(new RecallShipmentRow
                {
                    OrderNo = orderNo,
                    Customer = cust,
                    ShipTime = shipTime,
                    RefNo = refNo,
                    CellSn = cell.Sn,
                    ProductModel = cell.ProductModel,
                    ElectrodeLot = cell.ElectrodeLot,
                    ElectrolyteLot = cell.ElectrolyteLot,
                    WarehouseLot = warehouseLot
                });
            }

            RegisterLot(lotSet, lotCellCount, lotShipmentSet, cell.ElectrodeLot, shipped, orderNo);
            RegisterLot(lotSet, lotCellCount, lotShipmentSet, cell.ElectrolyteLot, shipped, orderNo);
        }

        // ---- 批次视图行 ----
        var lotRows = new List<RecallLotRow>();
        foreach (var lot in lotSet)
        {
            if (string.IsNullOrWhiteSpace(lot)) continue;
            var name = matMap.TryGetValue(lot, out var m) ? m.MaterialName : "未知材料";
            lotRows.Add(new RecallLotRow
            {
                LotNo = lot,
                MaterialName = name,
                CellCount = lotCellCount.TryGetValue(lot, out var cnt) ? cnt : 0,
                ShipmentCount = lotShipmentSet.TryGetValue(lot, out var set) ? set.Count : 0
            });
        }
        lotRows = lotRows.OrderBy(r => r.LotNo, StringComparer.Ordinal).ToList();

        // ---- 汇总 ----
        var shippedCount = cellRows.Count(r => r.Shipped);
        var inStockCount = cellRows.Count - shippedCount;
        var heldCount = cellRows.Count(r => r.Held);
        var freezableCount = cellRows.Count(r => !r.Shipped && !r.Held);

        result.Summary = new RecallSummary
        {
            ShipmentCount = shipmentOrderSet.Count,
            CustomerCount = customerSet.Count,
            CellCount = cellRows.Count,
            ShippedCellCount = shippedCount,
            InStockCellCount = inStockCount,
            HeldCellCount = heldCount,
            LotCount = lotRows.Count,
            FreezableCellCount = freezableCount
        };
        result.Shipments = shipmentRows
            .OrderBy(r => r.OrderNo, StringComparer.Ordinal)
            .ThenBy(r => r.CellSn, StringComparer.Ordinal)
            .ToList();
        result.Cells = cellRows.OrderBy(r => r.CellSn, StringComparer.Ordinal).ToList();
        result.Lots = lotRows;
        result.Message = $"反查完成：涉及出货单 {result.Summary.ShipmentCount} 张、客户 {result.Summary.CustomerCount} 家、电芯 {result.Summary.CellCount} 只、材料批次 {result.Summary.LotCount} 个。";
        return result;
    }

    private static void RegisterLot(
        HashSet<string> lotSet,
        Dictionary<string, int> lotCellCount,
        Dictionary<string, HashSet<string>> lotShipmentSet,
        string lot, bool shipped, string orderNo)
    {
        if (string.IsNullOrWhiteSpace(lot)) return;
        lotSet.Add(lot);
        if (!lotCellCount.ContainsKey(lot)) lotCellCount[lot] = 0;
        lotCellCount[lot]++;
        if (shipped)
        {
            if (!lotShipmentSet.ContainsKey(lot)) lotShipmentSet[lot] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(orderNo)) lotShipmentSet[lot].Add(orderNo);
        }
    }

    private static void ExpandByLots(HashSet<string> set, params string[] lots)
    {
        var valid = lots.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (valid.Count == 0) return;
        foreach (var c in Cells)
        {
            foreach (var lot in valid)
            {
                if (string.Equals(c.ElectrodeLot, lot, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.ElectrolyteLot, lot, StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(c.Sn);
                    break;
                }
            }
        }
    }

    private static void ExpandByCells(HashSet<string> set, IEnumerable<string> seedSns)
    {
        var cellMap = Cells.ToDictionary(c => c.Sn, StringComparer.OrdinalIgnoreCase);
        var lots = new List<string>();
        foreach (var sn in seedSns)
            if (cellMap.TryGetValue(sn, out var c)) { lots.Add(c.ElectrodeLot); lots.Add(c.ElectrolyteLot); }
        ExpandByLots(set, lots.ToArray());
    }

    /// <summary>按电芯型号在库存台账中最佳匹配成品位置（原型近似口径，可能为""）。</summary>
    private static string StockLocationOf(CellSn cell)
    {
        if (string.IsNullOrWhiteSpace(cell.ProductModel)) return "";
        var stock = WmsStocks.FirstOrDefault(s =>
            !string.IsNullOrWhiteSpace(s.MaterialName) && s.MaterialName.Contains(cell.ProductModel, StringComparison.OrdinalIgnoreCase)
            && s.Quantity > 1e-9);
        return stock?.Location ?? "";
    }

    /// <summary>按电芯型号在库存台账中最佳匹配成品数量（原型近似口径，可能为 null）。</summary>
    private static double? StockQtyOf(CellSn cell)
    {
        if (string.IsNullOrWhiteSpace(cell.ProductModel)) return null;
        var stock = WmsStocks.FirstOrDefault(s =>
            !string.IsNullOrWhiteSpace(s.MaterialName) && s.MaterialName.Contains(cell.ProductModel, StringComparison.OrdinalIgnoreCase)
            && s.Quantity > 1e-9);
        return stock is null ? null : stock.Quantity;
    }

    // ================= 一键冻结（召回影响电芯） =================

    /// <summary>
    /// 一键冻结召回影响电芯：只对传入集合操作（调用方应已过滤为「在库未保留」）；
    /// 已保留/已在保留中的 SN 进 Skipped；无新增时 Ok=false + "无新增" 消息。
    /// 内部走 HoldCells（holdLocation=待检区、reason=召回冻结、sourceType="召回冻结"、sourceRef="召回反查"）。
    /// 冻结不改变 WmsShipmentItems/Cells 等其它集合（仅新增保留单与明细）。
    /// </summary>
    public static (bool Ok, string Message, string HoldNo, int Held, int Skipped) FreezeRecallCells(
        IReadOnlyCollection<string> cellSns, string @operator)
    {
        var input = (cellSns ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (input.Count == 0) return (false, "未提供可冻结的电芯。", "", 0, 0);

        var activeByCell = BuildActiveHoldByCell();
        var toHold = new List<string>();
        var skipped = new List<string>();
        foreach (var sn in input)
        {
            if (activeByCell.ContainsKey(sn)) skipped.Add(sn);
            else toHold.Add(sn);
        }
        if (toHold.Count == 0)
            return (false, "无新增：所选电芯均已处于保留中（或不在范围内）。", "", 0, skipped.Count);

        var first = Cells.FirstOrDefault(c => string.Equals(c.Sn, toHold[0], StringComparison.OrdinalIgnoreCase));
        var lot = first?.ElectrodeLot ?? "";
        var reason = $"召回冻结：影响范围电芯（{toHold.Count} 只）";
        var (ok, msg, holdNo, _) = HoldCells(
            lot, "召回影响电芯", lot, WmsLocations.PendingInspection, reason, @operator, toHold, "召回冻结", "召回反查");
        return (ok, msg, holdNo, toHold.Count, skipped.Count);
    }

    // ================= 库存效期与 FIFO =================

    /// <summary>
    /// 库存效期种子：为部分批次设置有效期（含 1 条已过期且超兜底期限的「密封圈」、1 条临期「电解液」），演示效期预警与临期处置；
    /// 成品不做效期管理。**仅设置 ExpiryDate，不改变任何数量**（库存总量基线不变）。
    /// </summary>
    private static void SeedStockExpiry()
    {
        if (WmsStocks.Count == 0) return;
        var today = DateTime.Today;
        var rnd = new Random(20260930);

        foreach (var s in WmsStocks)
        {
            if (string.Equals(s.MaterialCode, "M-ELE-ACN-02", StringComparison.OrdinalIgnoreCase))
                s.ExpiryDate = today.AddDays(12);      // 电解液：临期（有效期敏感物料）
            else if (string.Equals(s.MaterialCode, "M-SEL-RUB-06", StringComparison.OrdinalIgnoreCase))
                s.ExpiryDate = today.AddDays(-20);     // 密封圈：已过期且**超过兜底期限**（演示兜底报废规则）
            else if (s.MaterialCode.StartsWith("M-FG-", StringComparison.OrdinalIgnoreCase))
                s.ExpiryDate = null;                   // 成品不设效期
            else
                s.ExpiryDate = today.AddDays(60 + rnd.Next(180));
        }
    }

    /// <summary>
    /// FIFO / 效期视图：按「物料 + 批次 + 库位」展开库存行；
    /// 同物料内按入库时间（LastUpdate）升序，**最早批次标记为「★ 优先出库」**（FIFO 推荐）。纯查询。
    /// </summary>
    public static List<StockFifoRow> GetStockFifo(string? keyword = null)
    {
        var kw = (keyword ?? "").Trim();
        var stocks = WmsStocks
            .Where(s => s.Quantity > 1e-9)
            .Where(s => !s.IsScrapped)   // 已废弃批次退出 FIFO 队列（数量已归零，这里再加一道标记过滤）
            .Where(s => kw.Length == 0
                        || s.MaterialCode.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || s.MaterialName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || s.LotNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rows = new List<StockFifoRow>();
        foreach (var g in stocks.GroupBy(s => s.MaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = g.OrderBy(s => s.LastUpdate).ThenBy(s => s.LotNo, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var s = ordered[i];
                rows.Add(new StockFifoRow
                {
                    MaterialCode = s.MaterialCode,
                    MaterialName = s.MaterialName,
                    Spec = s.Spec,
                    Unit = s.Unit,
                    LotNo = s.LotNo,
                    Location = s.Location,
                    Quantity = s.Quantity,
                    ReceivedDate = s.LastUpdate,
                    ExpiryDate = s.ExpiryDate,
                    DaysLeft = s.ExpiryDate is null
                        ? null
                        : Math.Round((s.ExpiryDate.Value - DateTime.Now).TotalDays, 1),
                    IsFifoRecommended = i == 0 && ordered.Count > 1
                });
            }
        }

        return rows.OrderBy(r => r.MaterialCode, StringComparer.Ordinal)
                   .ThenBy(r => r.ReceivedDate)
                   .ToList();
    }

    /// <summary>效期预警清单（已过期 + 临期 ≤ alertDays；按剩余天数升序）。纯查询。</summary>
    public static List<StockFifoRow> GetExpiryAlerts(int alertDays = 30) =>
        GetStockFifo()
            .Where(r => r.DaysLeft is not null && r.DaysLeft <= alertDays)
            .OrderBy(r => r.DaysLeft)
            .ToList();

    /// <summary>效期汇总（已过期 / 临期 / 无效期 批次数）。纯查询。</summary>
    public static (int Expired, int NearExpiry, int NoExpiry) GetExpirySummary(int alertDays = 30)
    {
        var all = GetStockFifo();
        return (all.Count(r => r.DaysLeft is < 0),
            all.Count(r => r.DaysLeft is >= 0 && r.DaysLeft <= alertDays),
            all.Count(r => r.DaysLeft is null));
    }

    /// <summary>FIFO 推荐清单（同物料多批次时建议优先出库的最早批次）。纯查询。</summary>
    public static List<StockFifoRow> GetFifoRecommendations() =>
        GetStockFifo().Where(r => r.IsFifoRecommended).ToList();

    // ================= 预警中心（聚合各模块待处理事项） =================

    /// <summary>
    /// 预警中心：把散落在各模块的「待处理事项」投影成统一列表（**纯查询，不修改任何集合**）。
    /// 覆盖 10 类：SPC 待处理预警 / 异常未闭环 / 保留超期 / 点检保养超期临期 / 库存效期 /
    /// 上料被拒 / 首件未放行 / 返工积压 / 备件低库存 / 出货未绑定。
    /// 排序：严重度（高→低）优先，其次按触发时间倒序。
    /// </summary>
    public static List<AlertItem> GetCenterAlerts()
    {
        var list = new List<AlertItem>();
        var now = DateTime.Now;

        // ① SPC 待处理预警（判异 / 能力不足 / 超规格）
        foreach (var a in SpcAlerts.Where(x => x.Status == SpcAlertStatus.Pending))
            list.Add(new AlertItem
            {
                Source = AlertSource.Spc,
                Severity = a.Severity switch
                {
                    SpcSeverity.High => AlertSeverity.High,
                    SpcSeverity.Medium => AlertSeverity.Medium,
                    _ => AlertSeverity.Low
                },
                Title = $"{a.ItemName}：{a.RuleName}",
                Detail = $"{a.AlertType} ｜ 当前值 {a.Value:0.##} ｜ 设备 {a.Equipment} ｜ 版本 {a.VersionText}",
                OccurTime = a.SampleTime,
                TargetKey = a.AlertNo
            });

        // ② 异常未闭环（待确认=高，已确认未闭环=中）
        foreach (var r in AbnormalRecords.Where(x => x.Status != AbnormalStatus.Closed))
            list.Add(new AlertItem
            {
                Source = AlertSource.Abnormal,
                Severity = r.Status == AbnormalStatus.Pending ? AlertSeverity.High : AlertSeverity.Medium,
                Title = $"{r.Category} / {r.SubType}",
                Detail = $"{r.Description} ｜ 状态 {r.Status} ｜ 负责人 {r.Owner} ｜ 设备 {r.EquipmentName}",
                OccurTime = r.OccurTime,
                TargetKey = r.AbNo
            });

        // ③ 保留超期（保留中且超过 3 天未处置；超 7 天升级为高）
        foreach (var h in Holds.Where(x => x.Status == HoldStatus.Active && (now - x.HoldTime).TotalDays > 3))
        {
            var days = (now - h.HoldTime).TotalDays;
            list.Add(new AlertItem
            {
                Source = AlertSource.Hold,
                Severity = days > 7 ? AlertSeverity.High : AlertSeverity.Medium,
                Title = $"保留单 {h.HoldNo} 已保留 {days:0} 天未处置",
                Detail = $"原因 {h.Reason} ｜ 位置 {h.HoldLocation} ｜ 电芯 {h.CellCount} 只 ｜ 来源 {h.SourceType}",
                OccurTime = h.HoldTime,
                TargetKey = h.HoldNo
            });
        }

        // ④ 点检保养超期 / 即将到期
        foreach (var p in TpmPlans.Where(x => x.DueStatus != TpmDueStatus.Normal))
            list.Add(new AlertItem
            {
                Source = AlertSource.Tpm,
                Severity = p.DueStatus == TpmDueStatus.Overdue ? AlertSeverity.High : AlertSeverity.Medium,
                Title = $"{p.DeviceName}「{p.ItemName}」{p.DueStatus}",
                Detail = $"周期 {p.CycleText} ｜ {p.DaysLeftText} ｜ 责任人 {p.Owner}",
                OccurTime = p.NextDueTime ?? DateTime.Today,
                TargetKey = p.PlanId
            });

        // ⑤ 库存效期（已过期=高，临期=中）
        foreach (var s in GetExpiryAlerts())
            list.Add(new AlertItem
            {
                Source = AlertSource.Expiry,
                Severity = s.ExpiryStatus == StockExpiryStatus.Expired ? AlertSeverity.High : AlertSeverity.Medium,
                Title = $"{s.MaterialName} 批次 {s.LotNo} {s.ExpiryStatus}",
                Detail = $"到期 {s.ExpiryText} ｜ {s.DaysLeftText} ｜ 数量 {s.Quantity:0.##} {s.Unit} ｜ 库位 {s.Location}",
                OccurTime = s.ExpiryDate ?? DateTime.Today,
                TargetKey = s.LotNo
            });

        // ⑥ 上料被拒留痕（近 7 天；不在 BOM = 高）
        foreach (var f in FeedingRecords.Where(x => !x.IsPass && (now - x.FeedTime).TotalDays <= 7))
            list.Add(new AlertItem
            {
                Source = AlertSource.Feeding,
                Severity = f.Result == FeedingResult.NotInBom ? AlertSeverity.High : AlertSeverity.Medium,
                Title = $"投料被拒：{f.Result}",
                Detail = $"工单 {f.WoNo} ｜ 物料 {f.MaterialName}（{f.MaterialCode}）｜ 批次 {f.LotNo} ｜ 操作人 {f.Operator}",
                OccurTime = f.FeedTime,
                TargetKey = f.FeedNo
            });

        // ⑦ 首件检验：已开工但未放行（未做首检或首检不合格）
        foreach (var wo in WorkOrders.Where(w => w.Status is "生产中" or "已完成" or "已关闭"))
        {
            if (IsFaiPassed(wo.WoNo)) continue;
            if (!ProcessActuals.Any(a => string.Equals(a.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase))) continue;

            var latest = GetLatestFai(wo.WoNo);
            list.Add(new AlertItem
            {
                Source = AlertSource.Fai,
                Severity = latest is null || latest.Status == FaiStatus.Failed ? AlertSeverity.High : AlertSeverity.Medium,
                Title = latest is null
                    ? $"工单 {wo.WoNo} 已开工但未做首件检验"
                    : $"工单 {wo.WoNo} 首检未放行（{latest.Status}）",
                Detail = $"状态 {wo.Status} ｜ 型号 {wo.ProductModel} ｜ 产线 {wo.Line} ｜ 完成 {wo.FinishedQty}/{wo.PlanQty}",
                OccurTime = latest?.CreatedTime ?? wo.StartDate,
                TargetKey = wo.WoNo
            });
        }

        // ⑧ 返工积压（待返工 / 待重检超过 1 天）
        foreach (var r in ReworkOrders.Where(x =>
                     (x.Status == ReworkStatus.Pending || x.Status == ReworkStatus.WaitInspect)
                     && (now - x.CreatedTime).TotalDays > 1))
        {
            var days = (now - r.CreatedTime).TotalDays;
            list.Add(new AlertItem
            {
                Source = AlertSource.Rework,
                Severity = r.Status == ReworkStatus.Pending ? AlertSeverity.High : AlertSeverity.Medium,
                Title = $"返工单 {r.ReworkNo} 停留 {r.Status} {days:0} 天",
                Detail = $"电芯 {r.CellSn} ｜ 原因 {r.Reason} ｜ 返工 {r.ReworkTimes} 次 ｜ 工序 {r.ReworkProcess}",
                OccurTime = r.CreatedTime,
                TargetKey = r.ReworkNo
            });
        }

        // ⑨ 备件低库存（为 0 升级为高）
        foreach (var p in SpareParts.Where(x => x.IsLow))
            list.Add(new AlertItem
            {
                Source = AlertSource.Spare,
                Severity = p.Stock <= 1e-9 ? AlertSeverity.High : AlertSeverity.Low,
                Title = $"备件「{p.Name}」低于安全库存",
                Detail = $"现有 {p.Stock:0.##} {p.Unit} ｜ 安全库存 {p.SafetyStock:0.##} ｜ 库位 {p.Location} ｜ 供应商 {p.Supplier}",
                OccurTime = p.LastIssueTime ?? DateTime.Today,
                TargetKey = p.Code
            });

        // ⑩ 出货未绑定 / 绑定不足
        foreach (var o in WmsOrders.Where(x => x.Type == WmsOrderTypes.LogisticsOutbound))
        {
            var bound = GetShipmentBoundCount(o.OrderNo);
            var planned = GetWmsOrderLines(o.OrderNo).Sum(l => l.Quantity);
            if (planned <= 0 || bound + 1e-9 >= planned) continue;

            list.Add(new AlertItem
            {
                Source = AlertSource.Shipment,
                Severity = bound == 0 ? AlertSeverity.Medium : AlertSeverity.Low,
                Title = bound == 0 ? $"出货单 {o.OrderNo} 未绑定出货电芯" : $"出货单 {o.OrderNo} 电芯绑定不足",
                Detail = $"客户 {o.Partner} ｜ 已绑 {bound} / 应出 {planned:0.##} ｜ 单据状态 {o.Status}",
                OccurTime = o.PostTime ?? o.CreateTime,
                TargetKey = o.OrderNo
            });
        }

        return list
            .OrderBy(a => a.SeverityOrder)
            .ThenByDescending(a => a.OccurTime)
            .ToList();
    }

    /// <summary>预警中心汇总（高 / 中 / 低 / 合计）。纯查询。</summary>
    public static (int High, int Medium, int Low, int Total) GetAlertCenterSummary()
    {
        var all = GetCenterAlerts();
        return (all.Count(a => a.Severity == AlertSeverity.High),
            all.Count(a => a.Severity == AlertSeverity.Medium),
            all.Count(a => a.Severity == AlertSeverity.Low),
            all.Count);
    }

    /// <summary>预警中心按来源统计（来源 / 总数 / 其中高危数）。纯查询。</summary>
    public static List<(string Source, int Count, int HighCount)> GetAlertBySource() =>
        GetCenterAlerts()
            .GroupBy(a => a.Source, StringComparer.Ordinal)
            .Select(g => (Source: g.Key, Count: g.Count(), HighCount: g.Count(a => a.Severity == AlertSeverity.High)))
            .OrderByDescending(x => x.HighCount)
            .ThenByDescending(x => x.Count)
            .ToList();

    // ================= 点检保养 TPM 与备件 =================

    private static int _tpmRecordSeq;
    private static string NextTpmRecordNo() => $"TPMR-{DateTime.Now:yyyyMMdd}-{++_tpmRecordSeq:000}";

    /// <summary>
    /// TPM 种子：为每台设备生成点检/保养计划（刻意含 2 条超期、1 条即将到期），
    /// 加执行记录与 8 项备件（含 2 项低于安全库存）。仅新增 TPM/备件集合。
    /// </summary>
    private static void SeedTpm()
    {
        if (Equipments.Count == 0) return;
        var today = DateTime.Today;

        var templates = new Dictionary<string, (string Item, string Criterion)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["电极制备"] = new[] { ("涂布辊清洁度", "无残留、无划伤"), ("烘箱温度", "90~120 ℃") },
            ["电芯成型"] = new[] { ("卷针与张力轮", "无松动、无异响"), ("极片导辊", "转动灵活") },
            ["注液封口"] = new[] { ("注液管路密封", "无渗漏"), ("封口头清洁", "无残料") },
            ["化成老化"] = new[] { ("化成柜温控", "25±3 ℃"), ("化成探针接触", "接触良好") },
            ["分选检测"] = new[] { ("测试探针磨损", "无氧化、无变形") },
            ["环境"] = new[] { ("干燥房露点", "≤ -40 ℃") }
        };

        var ops = new[] { "张伟", "王强", "赵敏" };
        var planSeq = 0;
        var i = 0;

        foreach (var eq in Equipments)
        {
            var list = templates.TryGetValue(eq.Section, out var t)
                ? t
                : new[] { ("设备外观与清洁", "无油污、无异响") };

            foreach (var (item, criterion) in list)
            {
                planSeq++;
                var cycle = planSeq % 3 == 0 ? TpmCycle.Monthly : (planSeq % 2 == 0 ? TpmCycle.Weekly : TpmCycle.Daily);
                var cycleDays = TpmCycle.DaysOf(cycle);
                var offsetDays = planSeq switch
                {
                    1 => -2.5,                        // 已超期
                    2 => 1.2,                         // 即将到期
                    _ => cycleDays * 0.7              // 正常
                };

                TpmPlans.Add(new TpmPlan
                {
                    PlanId = $"TPM-{planSeq:000}",
                    DeviceCode = eq.Code,
                    DeviceName = eq.Name,
                    Section = eq.Section,
                    ItemName = item,
                    CycleType = cycle,
                    Standard = criterion,
                    Owner = ops[i % ops.Length],
                    LastDoneTime = today.AddDays(-cycleDays).AddHours(9),
                    NextDueTime = DateTime.Now.AddDays(offsetDays)
                });
                i++;
            }
        }

        foreach (var plan in TpmPlans.Take(3))
            TpmRecords.Add(new TpmRecord
            {
                RecordNo = NextTpmRecordNo(),
                PlanId = plan.PlanId,
                DeviceCode = plan.DeviceCode,
                DeviceName = plan.DeviceName,
                ItemName = plan.ItemName,
                ExecuteTime = plan.LastDoneTime,
                Result = "正常",
                Operator = plan.Owner,
                Remark = "点检正常"
            });

        var parts = new (string Code, string Name, string Spec, string Unit, double Stock, double Safety, string Loc, string Sup)[]
        {
            ("SP-TIP-001", "测试探针", "Φ2.0 四线制", "支", 42, 20, "备件库-A1", "苏州精测"),
            ("SP-SEAL-002", "封口密封圈", "Φ18 氟橡胶", "只", 16, 20, "备件库-A2", "宁波橡塑"),
            ("SP-BELT-003", "卷绕传送带", "PU 300×10mm", "条", 9, 6, "备件库-B1", "无锡传动"),
            ("SP-FUSE-004", "化成柜熔断器", "50A/500V", "只", 30, 15, "备件库-B2", "正泰电器"),
            ("SP-NOZ-005", "注液针头", "Φ0.8 陶瓷", "支", 7, 12, "备件库-A3", "深圳微针"),
            ("SP-OIL-006", "真空泵油", "20L/桶", "桶", 4, 2, "备件库-C1", "北京真空"),
            ("SP-PAD-007", "测试仪压板", "铝制 200×80", "块", 6, 3, "备件库-C2", "天津工装"),
            ("SP-SENS-008", "温度传感器", "PT100 M8", "只", 18, 10, "备件库-B3", "上海自动化")
        };
        foreach (var p in parts)
            SpareParts.Add(new SparePart
            {
                Code = p.Code, Name = p.Name, Spec = p.Spec, Unit = p.Unit,
                Stock = p.Stock, SafetyStock = p.Safety, Location = p.Loc, Supplier = p.Sup
            });
    }

    /// <summary>点检/保养计划查询（关键字 + 到期状态）；按剩余天数升序（最紧急在前）。纯查询。</summary>
    public static List<TpmPlan> QueryTpmPlans(string? keyword, string? dueStatus)
    {
        IEnumerable<TpmPlan> q = TpmPlans;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(p => p.DeviceCode.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || p.DeviceName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || p.ItemName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || p.Owner.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(dueStatus)) q = q.Where(p => p.DueStatus == dueStatus.Trim());
        return q.OrderBy(p => p.DaysLeft).ThenBy(p => p.PlanId, StringComparer.Ordinal).ToList();
    }

    public static TpmPlan? FindTpmPlan(string planId) =>
        TpmPlans.FirstOrDefault(p => string.Equals(p.PlanId, (planId ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 执行点检/保养：写执行记录、更新最近执行时间并按周期顺延下次到期。
    /// 结果「异常」时必须填写备注（建议同步在异常登记页登记）。
    /// </summary>
    public static (bool Ok, string Message) ExecuteTpm(string planId, string result, string remark, string @operator)
    {
        var plan = FindTpmPlan(planId);
        if (plan is null) return (false, $"未找到点检计划 {planId}。");

        var isAbnormal = string.Equals((result ?? "").Trim(), "异常", StringComparison.Ordinal);
        if (isAbnormal && string.IsNullOrWhiteSpace(remark))
            return (false, "点检结果为「异常」时必须填写备注说明。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        plan.LastDoneTime = now;
        plan.NextDueTime = now.AddDays(TpmCycle.DaysOf(plan.CycleType));

        TpmRecords.Add(new TpmRecord
        {
            RecordNo = NextTpmRecordNo(),
            PlanId = plan.PlanId,
            DeviceCode = plan.DeviceCode,
            DeviceName = plan.DeviceName,
            ItemName = plan.ItemName,
            ExecuteTime = now,
            Result = isAbnormal ? "异常" : "正常",
            Operator = op,
            Remark = (remark ?? "").Trim()
        });

        return (true, isAbnormal
            ? $"点检完成（结果：异常）—— 已留痕，建议同步登记设备异常。下次到期：{plan.NextDueTime:yyyy-MM-dd}"
            : $"点检完成（结果：正常）—— 下次到期：{plan.NextDueTime:yyyy-MM-dd}");
    }

    /// <summary>点检执行记录查询（设备/项目/操作人关键字）；时间倒序。纯查询。</summary>
    public static List<TpmRecord> QueryTpmRecords(string? keyword)
    {
        IEnumerable<TpmRecord> q = TpmRecords;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(r => r.DeviceCode.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.DeviceName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.ItemName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.Operator.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderByDescending(r => r.ExecuteTime).ThenBy(r => r.RecordNo, StringComparer.Ordinal).ToList();
    }

    /// <summary>TPM 汇总（计划数 / 已超期 / 即将到期）。纯查询。</summary>
    public static (int Total, int Overdue, int DueSoon) GetTpmSummary() =>
        (TpmPlans.Count,
            TpmPlans.Count(p => p.DueStatus == TpmDueStatus.Overdue),
            TpmPlans.Count(p => p.DueStatus == TpmDueStatus.DueSoon));

    /// <summary>备件查询（关键字：编码/名称/规格/库位）；低库存优先。纯查询。</summary>
    public static List<SparePart> QuerySpareParts(string? keyword)
    {
        IEnumerable<SparePart> q = SpareParts;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(p => p.Code.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || p.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || p.Spec.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || p.Location.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderByDescending(p => p.IsLow).ThenBy(p => p.Code, StringComparer.Ordinal).ToList();
    }

    public static SparePart? FindSparePart(string code) =>
        SpareParts.FirstOrDefault(p => string.Equals(p.Code, (code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>备件领用：校验库存后扣减并记录领用时间（低于安全库存时提示补货）。</summary>
    public static (bool Ok, string Message) IssueSparePart(string code, double qty, string @operator)
    {
        var part = FindSparePart(code);
        if (part is null) return (false, $"未找到备件 {code}。");
        if (qty <= 0) return (false, "领用数量必须大于 0。");
        if (part.Stock + 1e-9 < qty)
            return (false, $"备件 {part.Name} 库存不足（现有 {part.Stock:0.##} {part.Unit}，领用 {qty:0.##}）。");

        part.Stock = Math.Round(part.Stock - qty, 2);
        part.LastIssueTime = DateTime.Now;
        var lowHint = part.IsLow ? "（领用后已低于安全库存，请及时补货）" : "";
        return (true, $"已领用 {part.Name} × {qty:0.##} {part.Unit}，剩余 {part.Stock:0.##}{lowHint}");
    }

    /// <summary>备件汇总（品种数 / 低库存品种数）。纯查询。</summary>
    public static (int Total, int LowCount) GetSpareSummary() =>
        (SpareParts.Count, SpareParts.Count(p => p.IsLow));

    // ================= 模组装配与模组级追溯 =================

    /// <summary>
    /// 模组种子：按既有 <c>Cells.ModuleSn</c> 反查生成模组（不新建电芯、不改电芯数据，保证既有基线不变），
    /// 另加 1 个「已出货」模组示例。
    /// </summary>
    private static void SeedModules()
    {
        foreach (var g in Cells.Where(c => !string.IsNullOrWhiteSpace(c.ModuleSn))
                     .GroupBy(c => c.ModuleSn, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First();
            Modules.Add(new ModuleRecord
            {
                ModuleSn = g.Key,
                ModuleModel = first.ProductModel.Contains("100F", StringComparison.OrdinalIgnoreCase)
                    ? "模组 SC-MOD-100F-24V"
                    : "模组 SC-MOD-300F-48V",
                CellModel = first.ProductModel,
                CellCount = g.Count(),
                Line = first.Line,
                WoNo = first.WoNo,
                Status = ModuleStatus.Assembled,
                Operator = "王强",
                AssembledTime = DateTime.Today.AddDays(-15).AddHours(14),
                Remark = "由既有电芯装配关系反查生成（演示数据）"
            });
        }

        Modules.Add(new ModuleRecord
        {
            ModuleSn = "SM-A260925-L1-00120",   // 历史示例模组：不占用既有电芯的模组号，避免与反查结果重复
            ModuleModel = "模组 SC-MOD-100F-24V",
            CellModel = "SC-CX2026-100F",
            CellCount = 24,
            Line = "L1",
            WoNo = "WO-20260812-001",
            Status = ModuleStatus.Shipped,
            Operator = "王强",
            AssembledTime = DateTime.Today.AddDays(-30).AddHours(10),
            Remark = "已随出货单发出（历史示例，电芯明细不并入演示台账）"
        });
    }

    /// <summary>
    /// 装配模组：校验模组号未占用、电芯存在且未组模组、电芯状态可出货（合格/已入库）；
    /// 成功后写模组记录并把所选电芯的 <c>ModuleSn</c> 绑定为该模组。
    /// </summary>
    public static (bool Ok, string Message) AssembleModule(
        string moduleSn, string moduleModel, string line, string woNo, IReadOnlyCollection<string>? cellSns, string @operator)
    {
        var sn = (moduleSn ?? "").Trim();
        if (sn.Length == 0) return (false, "请填写模组 SN。");
        if (Modules.Any(m => string.Equals(m.ModuleSn, sn, StringComparison.OrdinalIgnoreCase)
                             && m.Status != ModuleStatus.Dismantled))
            return (false, $"模组 SN {sn} 已存在，请勿重复装配。");

        var input = (cellSns ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (input.Count == 0) return (false, "请先选择要装入模组的电芯。");

        var targets = new List<CellSn>();
        foreach (var c in input)
        {
            var cell = Cells.FirstOrDefault(x => string.Equals(x.Sn, c, StringComparison.OrdinalIgnoreCase));
            if (cell is null) return (false, $"未找到电芯 {c}。");
            if (!string.IsNullOrWhiteSpace(cell.ModuleSn))
                return (false, $"电芯 {c} 已属于模组 {cell.ModuleSn}，不能重复装配。");
            if (!CellSnStatus.IsShippable(cell.Status))
                return (false, $"电芯 {c} 当前状态「{cell.Status}」不可装配（需合格或已入库）。");
            targets.Add(cell);
        }

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        foreach (var cell in targets) cell.ModuleSn = sn;

        Modules.Add(new ModuleRecord
        {
            ModuleSn = sn,
            ModuleModel = (moduleModel ?? "").Trim(),
            CellModel = targets[0].ProductModel,
            CellCount = targets.Count,
            Line = string.IsNullOrWhiteSpace(line) ? targets[0].Line : line.Trim(),
            WoNo = string.IsNullOrWhiteSpace(woNo) ? targets[0].WoNo : woNo.Trim(),
            Status = ModuleStatus.Assembled,
            Operator = op,
            AssembledTime = DateTime.Now,
            Remark = $"装配 {targets.Count} 只电芯"
        });
        return (true, $"模组 {sn} 装配成功（{targets.Count} 只电芯：{string.Join("、", targets.Select(t => t.Sn))}）。");
    }

    /// <summary>拆解模组：解绑其电芯并把模组状态置「已拆解」（已出货模组不可拆解）。</summary>
    public static (bool Ok, string Message) DismantleModule(string moduleSn, string @operator)
    {
        var m = FindModule(moduleSn);
        if (m is null) return (false, $"未找到模组 {moduleSn}。");
        if (m.Status == ModuleStatus.Dismantled) return (false, $"模组 {m.ModuleSn} 已是「已拆解」状态。");
        if (m.Status == ModuleStatus.Shipped) return (false, $"模组 {m.ModuleSn} 已出货，不能拆解。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var count = 0;
        foreach (var cell in Cells.Where(c => string.Equals(c.ModuleSn, m.ModuleSn, StringComparison.OrdinalIgnoreCase)))
        {
            cell.ModuleSn = "";
            count++;
        }
        m.Status = ModuleStatus.Dismantled;
        m.Remark = $"由 {op} 拆解（解绑 {count} 只电芯）";
        return (true, $"模组 {m.ModuleSn} 已拆解（解绑 {count} 只电芯）。");
    }

    public static ModuleRecord? FindModule(string moduleSn) =>
        Modules.FirstOrDefault(m => string.Equals(m.ModuleSn, (moduleSn ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>模组查询（关键字：模组SN/型号/工单 + 状态）；装配时间倒序。纯查询。</summary>
    public static List<ModuleRecord> QueryModules(string? keyword, string? status)
    {
        IEnumerable<ModuleRecord> q = Modules;
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(m => m.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(m => m.ModuleSn.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || m.ModuleModel.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || m.WoNo.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderByDescending(m => m.AssembledTime).ThenBy(m => m.ModuleSn, StringComparer.Ordinal).ToList();
    }

    /// <summary>模组级追溯：模组内电芯明细（含材料批次 + 最近一次测试结论）。纯查询。</summary>
    public static List<ModuleCellRow> GetModuleCells(string moduleSn)
    {
        var m = FindModule(moduleSn);
        if (m is null) return new List<ModuleCellRow>();

        return Cells
            .Where(c => string.Equals(c.ModuleSn, m.ModuleSn, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Sn, StringComparer.Ordinal)
            .Select(c =>
            {
                var test = TestRecords
                    .Where(t => string.Equals(t.CellSn, c.Sn, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.TestTime)
                    .FirstOrDefault();
                return new ModuleCellRow
                {
                    ModuleSn = m.ModuleSn,
                    CellSn = c.Sn,
                    ProductModel = c.ProductModel,
                    ElectrodeLot = c.ElectrodeLot,
                    ElectrolyteLot = c.ElectrolyteLot,
                    Result = string.IsNullOrWhiteSpace(c.Result) ? "—" : c.Result,
                    CellStatus = c.Status,
                    TestTime = test?.TestTime,
                    Passed = test?.Passed
                };
            }).ToList();
    }

    /// <summary>可装配电芯候选（合格/已入库 且 未属于任何模组）。纯查询。</summary>
    public static List<ModuleCellItem> GetModuleCandidates() =>
        Cells.Where(c => CellSnStatus.IsShippable(c.Status) && string.IsNullOrWhiteSpace(c.ModuleSn))
            .OrderBy(c => c.Sn, StringComparer.Ordinal)
            .Select(c => new ModuleCellItem
            {
                Chk = false,
                CellSn = c.Sn,
                ProductModel = c.ProductModel,
                CellStatus = c.Status,
                ElectrodeLot = c.ElectrodeLot,
                CreateTime = c.CreateTime
            }).ToList();

    /// <summary>模组汇总（模组数 / 已装配 / 已出货 / 已装电芯数）。纯查询。</summary>
    public static (int Total, int Assembled, int Shipped, int CellsInModules) GetModuleSummary() =>
        (Modules.Count,
            Modules.Count(m => m.Status == ModuleStatus.Assembled),
            Modules.Count(m => m.Status == ModuleStatus.Shipped),
            Cells.Count(c => !string.IsNullOrWhiteSpace(c.ModuleSn)));

    // ================= 上料防错（扫码校验 工单-BOM-批次） =================

    private static int _feedSeq;
    private static string NextFeedNo() => $"FD-{DateTime.Now:yyyyMMdd}-{++_feedSeq:000}";

    /// <summary>
    /// 上料记录种子：为 WO-20260812-001 造 3 条（2 通过 + 1 被拒「不在 BOM」），演示防错留痕。
    /// 仅新增 FeedingRecords。数量刻意小于需求量，便于演示"部分投料"。
    /// </summary>
    private static void SeedFeeding()
    {
        var wo = WorkOrders.FirstOrDefault(w => w.WoNo == "WO-20260812-001");
        if (wo is null) return;
        var bom = GetWorkOrderBom(wo.WoNo);
        if (bom.Count < 2) return;

        var today = DateTime.Today;
        var ops = new[] { "张伟", "李娜" };

        var m1 = bom[0];
        var lot1 = WmsStocks.FirstOrDefault(s => string.Equals(s.MaterialCode, m1.MaterialCode, StringComparison.OrdinalIgnoreCase) && s.Quantity > 0);
        FeedingRecords.Add(new FeedingRecord
        {
            FeedNo = NextFeedNo(), WoNo = wo.WoNo, ProductModel = wo.ProductModel,
            ScanCode = $"{m1.MaterialCode}|{lot1?.LotNo}",
            MaterialCode = m1.MaterialCode, MaterialName = m1.MaterialName, Spec = m1.Spec, Unit = m1.Unit,
            LotNo = lot1?.LotNo ?? "", Location = lot1?.Location ?? "",
            Quantity = Math.Round(m1.QtyPerUnit * wo.PlanQty * 0.6, 2),
            Result = FeedingResult.Passed, Operator = ops[0], FeedTime = today.AddHours(8).AddMinutes(40)
        });

        var m2 = bom[Math.Min(1, bom.Count - 1)];
        var lot2 = WmsStocks.FirstOrDefault(s => string.Equals(s.MaterialCode, m2.MaterialCode, StringComparison.OrdinalIgnoreCase) && s.Quantity > 0);
        FeedingRecords.Add(new FeedingRecord
        {
            FeedNo = NextFeedNo(), WoNo = wo.WoNo, ProductModel = wo.ProductModel,
            ScanCode = $"{m2.MaterialCode}|{lot2?.LotNo}",
            MaterialCode = m2.MaterialCode, MaterialName = m2.MaterialName, Spec = m2.Spec, Unit = m2.Unit,
            LotNo = lot2?.LotNo ?? "", Location = lot2?.Location ?? "",
            Quantity = Math.Round(m2.QtyPerUnit * wo.PlanQty * (1 + m2.LossRate / 100), 2),
            Result = FeedingResult.Passed, Operator = ops[1], FeedTime = today.AddHours(9).AddMinutes(10)
        });

        var outsider = WmsStocks.FirstOrDefault(s =>
            s.Quantity > 0 && bom.All(b => !string.Equals(b.MaterialCode, s.MaterialCode, StringComparison.OrdinalIgnoreCase)));
        if (outsider is not null)
            FeedingRecords.Add(new FeedingRecord
            {
                FeedNo = NextFeedNo(), WoNo = wo.WoNo, ProductModel = wo.ProductModel,
                ScanCode = $"{outsider.MaterialCode}|{outsider.LotNo}",
                MaterialCode = outsider.MaterialCode, MaterialName = outsider.MaterialName,
                Spec = outsider.Spec, Unit = outsider.Unit, LotNo = outsider.LotNo, Location = outsider.Location,
                Quantity = 0, Result = FeedingResult.NotInBom, Operator = ops[0], FeedTime = today.AddHours(9).AddMinutes(25)
            });
    }

    /// <summary>
    /// 解析扫码内容：① 三段「工单|物料|批次」② 两段「物料|批次」③ 单段（按物料编码或批次匹配）。
    /// </summary>
    private static (string WoNo, string MaterialCode, string LotNo) ParseScan(string? scanCode)
    {
        var parts = (scanCode ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 3 => (parts[0], parts[1], parts[2]),
            2 => ("", parts[0], parts[1]),
            1 => ("", parts[0], ""),
            _ => ("", "", "")
        };
    }

    /// <summary>
    /// 上料校验（防错核心）：工单存在 → 物料在工单 BOM 中 → 批次有可用库存；
    /// commit=true 时写投料记录（通过与被拒均留痕）。**不修改库存**（库存由 WMS 过账控制）。
    /// </summary>
    public static FeedingCheckOutcome CheckFeeding(string woNo, string scanCode, string @operator, double quantity = 0, bool commit = true)
    {
        var outcome = new FeedingCheckOutcome { WoNo = (woNo ?? "").Trim() };

        var wo = WorkOrders.FirstOrDefault(w =>
            string.Equals(w.WoNo, outcome.WoNo, StringComparison.OrdinalIgnoreCase));
        if (wo is null)
            return Finish(outcome, FeedingResult.OrderNotFound, $"未找到工单 {outcome.WoNo}。", scanCode, quantity, @operator, commit);

        var (scanWo, materialCode, lotNo) = ParseScan(scanCode);
        if (!string.IsNullOrWhiteSpace(scanWo))
        {
            var scanOrder = WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, scanWo, StringComparison.OrdinalIgnoreCase));
            if (scanOrder is not null) wo = scanOrder;
        }

        if (string.IsNullOrWhiteSpace(materialCode) && string.IsNullOrWhiteSpace(lotNo))
            return Finish(outcome, FeedingResult.EmptyScan, "扫码内容为空，请扫描物料批次条码。", scanCode, quantity, @operator, commit);

        var stock = !string.IsNullOrWhiteSpace(lotNo)
            ? WmsStocks.FirstOrDefault(s => string.Equals(s.LotNo, lotNo, StringComparison.OrdinalIgnoreCase)
                                            && (string.IsNullOrWhiteSpace(materialCode)
                                                || string.Equals(s.MaterialCode, materialCode, StringComparison.OrdinalIgnoreCase)))
            : WmsStocks.FirstOrDefault(s => string.Equals(s.MaterialCode, materialCode, StringComparison.OrdinalIgnoreCase));
        if (stock is null)
            return Finish(outcome, FeedingResult.LotNotFound,
                $"未找到批次/物料（物料 {materialCode}，批次 {lotNo}）。", scanCode, quantity, @operator, commit);

        outcome.MaterialCode = stock.MaterialCode;
        outcome.MaterialName = stock.MaterialName;
        outcome.Spec = stock.Spec;
        outcome.Unit = stock.Unit;
        outcome.LotNo = stock.LotNo;
        outcome.Location = stock.Location;
        outcome.AvailableQty = stock.Quantity;

        var bomItem = GetWorkOrderBom(wo.WoNo)
            .FirstOrDefault(b => string.Equals(b.MaterialCode, stock.MaterialCode, StringComparison.OrdinalIgnoreCase));
        if (bomItem is null)
            return Finish(outcome, FeedingResult.NotInBom,
                $"物料 {stock.MaterialName}（{stock.MaterialCode}）不在工单 {wo.WoNo} 的 BOM 中 —— 拒绝投料。",
                scanCode, quantity, @operator, commit);

        outcome.RequiredQty = Math.Round(bomItem.QtyPerUnit * wo.PlanQty * (1 + bomItem.LossRate / 100), 2);
        if (stock.Quantity <= 1e-9)
            return Finish(outcome, FeedingResult.NoStock,
                $"批次 {stock.LotNo} 无可用库存（{stock.Quantity:0.##} {stock.Unit}）。", scanCode, quantity, @operator, commit);

        outcome.IsFirstFeed = !FeedingRecords.Any(r =>
            string.Equals(r.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.MaterialCode, stock.MaterialCode, StringComparison.OrdinalIgnoreCase)
            && r.IsPass);

        var qty = quantity > 1e-9 ? quantity : Math.Min(stock.Quantity, outcome.RequiredQty ?? stock.Quantity);
        outcome.WoNo = wo.WoNo;
        outcome.Ok = true;
        outcome.Result = FeedingResult.Passed;
        outcome.Message = $"✔ 校验通过：{stock.MaterialName}（{stock.MaterialCode}）批次 {stock.LotNo}"
                          + $" ｜ 可用 {stock.Quantity:0.##} {stock.Unit} ｜ 本工单需求 {outcome.RequiredQty:0.##}"
                          + (outcome.IsFirstFeed ? " ｜ 该物料首次投料" : " ｜ 该物料已投过料");

        if (commit)
            FeedingRecords.Add(new FeedingRecord
            {
                FeedNo = NextFeedNo(), WoNo = outcome.WoNo, ProductModel = wo.ProductModel,
                ScanCode = scanCode ?? "",
                MaterialCode = outcome.MaterialCode, MaterialName = outcome.MaterialName,
                Spec = outcome.Spec, Unit = outcome.Unit, LotNo = outcome.LotNo, Location = outcome.Location,
                Quantity = qty, Result = FeedingResult.Passed,
                Operator = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim(),
                FeedTime = DateTime.Now
            });
        return outcome;
    }

    /// <summary>拒绝路径：写留痕记录并返回结论</summary>
    private static FeedingCheckOutcome Finish(FeedingCheckOutcome outcome, string result, string message,
        string? scanCode, double quantity, string @operator, bool commit)
    {
        outcome.Ok = false;
        outcome.Result = result;
        outcome.Message = "✖ " + message;
        if (commit)
            FeedingRecords.Add(new FeedingRecord
            {
                FeedNo = NextFeedNo(),
                WoNo = outcome.WoNo,
                ProductModel = WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, outcome.WoNo, StringComparison.OrdinalIgnoreCase))?.ProductModel ?? "",
                ScanCode = scanCode ?? "",
                MaterialCode = outcome.MaterialCode, MaterialName = outcome.MaterialName,
                Spec = outcome.Spec, Unit = outcome.Unit, LotNo = outcome.LotNo, Location = outcome.Location,
                Quantity = quantity, Result = result,
                Operator = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim(),
                FeedTime = DateTime.Now
            });
        return outcome;
    }

    /// <summary>投料记录查询（工单 + 关键字：单号/物料/批次/结果）；时间倒序。纯查询。</summary>
    public static List<FeedingRecord> QueryFeedingRecords(string? woNo, string? keyword)
    {
        IEnumerable<FeedingRecord> q = FeedingRecords;
        if (!string.IsNullOrWhiteSpace(woNo))
            q = q.Where(r => string.Equals(r.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(r => r.FeedNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.MaterialCode.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.MaterialName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.LotNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.Result.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderByDescending(r => r.FeedTime).ThenBy(r => r.FeedNo, StringComparer.Ordinal).ToList();
    }

    public static FeedingRecord? FindFeeding(string feedNo) =>
        FeedingRecords.FirstOrDefault(r => string.Equals(r.FeedNo, (feedNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>工单 BOM 投料进度（需求量 vs 已投量）。纯查询。</summary>
    public static List<FeedingProgressRow> GetFeedingProgress(string woNo)
    {
        var wo = WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, (woNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (wo is null) return new List<FeedingProgressRow>();

        return GetWorkOrderBom(wo.WoNo).Select(b => new FeedingProgressRow
        {
            MaterialCode = b.MaterialCode,
            MaterialName = b.MaterialName,
            Spec = b.Spec,
            Unit = b.Unit,
            MaterialType = b.MaterialType,
            RequiredQty = Math.Round(b.QtyPerUnit * wo.PlanQty * (1 + b.LossRate / 100), 2),
            FedQty = Math.Round(FeedingRecords
                .Where(r => string.Equals(r.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.MaterialCode, b.MaterialCode, StringComparison.OrdinalIgnoreCase)
                            && r.IsPass)
                .Sum(r => r.Quantity), 2)
        }).ToList();
    }

    /// <summary>上料防错汇总（投料次数 / 通过 / 拒绝 / BOM 投足率）。纯查询。</summary>
    public static (int Total, int Passed, int Rejected, double BomRate) GetFeedingSummary(string? woNo = null)
    {
        var records = string.IsNullOrWhiteSpace(woNo)
            ? FeedingRecords.ToList()
            : FeedingRecords.Where(r => string.Equals(r.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var passed = records.Count(r => r.IsPass);
        var progress = string.IsNullOrWhiteSpace(woNo) ? new List<FeedingProgressRow>() : GetFeedingProgress(woNo!);
        var bomRate = progress.Count == 0 ? 0 : Math.Round(progress.Count(p => p.Status == "已投足") * 100.0 / progress.Count, 1);
        return (records.Count, passed, records.Count - passed, bomRate);
    }

    // ================= 首件检验（FAI） =================

    /// <summary>首件检验单号序号（跨天自然延续）</summary>
    private static int _faiSeq;

    private static string NextFaiNo() => $"FAI-{DateTime.Now:yyyyMMdd}-{++_faiSeq:000}";

    /// <summary>
    /// 首件检验种子：① 1 张「合格（已放行）」（WO-20260812-001，换型）；② 1 张「待检验」（WO-20260813-002，开班）。
    /// 仅新增 FAI 集合与明细，不改动既有集合。
    /// </summary>
    private static void SeedFai()
    {
        var wo1 = WorkOrders.FirstOrDefault(w => w.WoNo == "WO-20260812-001");
        var wo2 = WorkOrders.FirstOrDefault(w => w.WoNo == "WO-20260813-002");

        if (wo1 is not null)
        {
            var no = NextFaiNo();
            var items = BuildFaiItems(no, "卷绕");
            var measuredValues = new[] { "1.72", "+0.02", "无褶皱" };
            for (var i = 0; i < items.Count; i++)
            {
                items[i].Measured = i < measuredValues.Length ? measuredValues[i] : "符合";
                items[i].Result = "合格";
            }
            FaiItems.AddRange(items);
            FaiOrders.Add(new FaiOrder
            {
                FaiNo = no,
                WoNo = wo1.WoNo,
                ProductModel = wo1.ProductModel,
                Line = wo1.Line,
                FirstCellSn = "SC-A260812-L1-00001",
                ProcessName = "卷绕",
                Reason = FaiReason.Changeover,
                Status = FaiStatus.Passed,
                Inspector = "赵敏",
                InspectTime = DateTime.Today.AddDays(-18).AddHours(9),
                PassCount = items.Count,
                FailCount = 0,
                CreatedTime = DateTime.Today.AddDays(-18).AddHours(8).AddMinutes(40),
                Remark = "换型首件检验合格，已放行批量"
            });
        }

        if (wo2 is not null)
        {
            var no = NextFaiNo();
            FaiItems.AddRange(BuildFaiItems(no, "注液"));
            FaiOrders.Add(new FaiOrder
            {
                FaiNo = no,
                WoNo = wo2.WoNo,
                ProductModel = wo2.ProductModel,
                Line = wo2.Line,
                FirstCellSn = "SC-A260813-L2-00006",
                ProcessName = "注液",
                Reason = FaiReason.ShiftStart,
                Status = FaiStatus.Pending,
                CreatedTime = DateTime.Today.AddHours(8).AddMinutes(35),
                Remark = "开班首件，等待检验"
            });
        }
    }

    /// <summary>按工序模板生成首件检验项（实测值留空）</summary>
    private static List<FaiItem> BuildFaiItems(string faiNo, string processName)
    {
        var template = InspectionTemplates.TryGetValue(processName, out var list)
            ? list
            : new List<(string Item, string Standard)>();
        return template.Select(t => new FaiItem
        {
            FaiNo = faiNo,
            ItemName = t.Item,
            Standard = t.Standard,
            Measured = "",
            Result = ""
        }).ToList();
    }

    /// <summary>某工序的检验项目（项目名 + 标准）；无模板返回空。纯查询。</summary>
    public static List<(string Item, string Standard)> GetFaiTemplate(string processName) =>
        InspectionTemplates.TryGetValue(processName ?? "", out var list)
            ? list
            : new List<(string, string)>();

    /// <summary>可首检的工序（有检验模板的工序）。纯查询。</summary>
    public static List<string> GetFaiProcessOptions() => InspectionTemplates.Keys.ToList();

    /// <summary>新建首件检验单（待检验）：校验工单存在、该工单无「待检验」单；按工序模板生成检验项。</summary>
    public static (bool Ok, string Message, string FaiNo) CreateFai(
        string woNo, string firstCellSn, string processName, string reason, string @operator)
    {
        var wo = WorkOrders.FirstOrDefault(w =>
            string.Equals(w.WoNo, (woNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (wo is null) return (false, $"未找到工单 {woNo}。", "");
        if (string.IsNullOrWhiteSpace(processName)) return (false, "请选择首检工序。", "");
        if (!InspectionTemplates.ContainsKey(processName))
            return (false, $"工序「{processName}」没有检验项目模板，无法首检。", "");

        var pending = FaiOrders.FirstOrDefault(f =>
            string.Equals(f.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase) && f.Status == FaiStatus.Pending);
        if (pending is not null)
            return (false, $"工单 {wo.WoNo} 已有待检验的首件检验单 {pending.FaiNo}，请先完成它。", "");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var no = NextFaiNo();
        FaiOrders.Add(new FaiOrder
        {
            FaiNo = no,
            WoNo = wo.WoNo,
            ProductModel = wo.ProductModel,
            Line = wo.Line,
            FirstCellSn = (firstCellSn ?? "").Trim(),
            ProcessName = processName,
            Reason = string.IsNullOrWhiteSpace(reason) ? FaiReason.Changeover : reason,
            Status = FaiStatus.Pending,
            CreatedTime = DateTime.Now,
            Remark = $"由 {op} 创建"
        });
        FaiItems.AddRange(BuildFaiItems(no, processName));
        return (true, $"首件检验单 {no} 已创建（工序 {processName}，共 {GetFaiTemplate(processName).Count} 项）。", no);
    }

    /// <summary>
    /// 提交首件检验判定：逐项按实测值判定（复用 InspectionJudge；非数值项按文本一致判定）。
    /// 全部合格 → 「合格（已放行）」；任一项不合格 → 「不合格」（可复检）。检验结论写入检验记录（追溯链可见）。
    /// </summary>
    public static (bool Ok, string Message) SubmitFai(
        string faiNo, IReadOnlyList<(string ItemName, string Measured)>? measured, string @operator)
    {
        var fai = FindFai(faiNo);
        if (fai is null) return (false, $"未找到首件检验单 {faiNo}。");
        if (FaiStatus.IsPassed(fai.Status)) return (false, $"首件检验单 {fai.FaiNo} 已放行，无需重复提交。");

        var items = GetFaiItems(fai.FaiNo);
        if (items.Count == 0) return (false, $"首件检验单 {fai.FaiNo} 没有检验项目。");

        var measuredMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (measured is not null)
            foreach (var m in measured)
                measuredMap[m.ItemName] = m.Measured ?? "";

        var pass = 0;
        var fail = 0;
        var unfilled = 0;
        foreach (var item in items)
        {
            var value = measuredMap.TryGetValue(item.ItemName, out var v) ? v.Trim() : item.Measured.Trim();
            item.Measured = value;
            if (value.Length == 0) { item.Result = ""; unfilled++; continue; }

            var judged = InspectionJudge.TryJudge(item.Standard, value);
            if (judged == true) { item.Result = "合格"; pass++; }
            else if (judged == false) { item.Result = "不合格"; fail++; }
            else if (string.Equals(item.Standard.Trim(), value, StringComparison.OrdinalIgnoreCase))
            {
                // 非数值型项目（如「无褶皱」「无泄漏」）：实测与标准文本一致视为合格
                item.Result = "合格";
                pass++;
            }
            else { item.Result = "无法判定"; unfilled++; }
        }

        if (unfilled > 0)
            return (false, $"还有 {unfilled} 项未填写或无法判定，请补全后再提交。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        fai.PassCount = pass;
        fai.FailCount = fail;
        fai.Inspector = op;
        fai.InspectTime = now;
        fai.Status = fail == 0 ? FaiStatus.Passed : FaiStatus.Failed;

        // 结论写入检验记录（工序「首件检验」，ItemName 记首检单号），使首检进入追溯链
        var cellSn = string.IsNullOrWhiteSpace(fai.FirstCellSn) ? "—" : fai.FirstCellSn;
        Inspections.RemoveAll(i => string.Equals(i.CellSn, cellSn, StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(i.ProcessName, "首件检验", StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(i.ItemName, fai.FaiNo, StringComparison.OrdinalIgnoreCase));
        foreach (var item in items)
            Inspections.Add(new InspectionRecord
            {
                CellSn = cellSn,
                Equipment = ProcessSequence.DefaultEquipment.TryGetValue(fai.ProcessName, out var eq) ? eq : "",
                ProcessName = "首件检验",
                ItemName = fai.FaiNo,
                StandardValue = item.Standard,
                MeasuredValue = item.Measured,
                Result = item.Result,
                Inspector = op,
                InspectTime = now
            });

        return fail == 0
            ? (true, $"首件检验单 {fai.FaiNo}：{pass} 项全部合格 —— 已放行批量生产（工单 {fai.WoNo}）。")
            : (true, $"首件检验单 {fai.FaiNo}：{fail} 项不合格（合格 {pass} 项），状态「不合格」，调试后可复检。");
    }

    public static FaiOrder? FindFai(string faiNo) =>
        FaiOrders.FirstOrDefault(f => string.Equals(f.FaiNo, (faiNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>首件检验单查询（状态 + 关键字：单号/工单/首件SN）；创建时间倒序。纯查询。</summary>
    public static List<FaiOrder> QueryFaiOrders(string? status, string? keyword)
    {
        IEnumerable<FaiOrder> q = FaiOrders;
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(f => f.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(f => f.FaiNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || f.WoNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || f.FirstCellSn.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderByDescending(f => f.CreatedTime).ThenBy(f => f.FaiNo, StringComparer.Ordinal).ToList();
    }

    /// <summary>某单的检验项目明细。纯查询。</summary>
    public static List<FaiItem> GetFaiItems(string faiNo) =>
        FaiItems.Where(i => string.Equals(i.FaiNo, (faiNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>工单是否已通过首件检验放行（批量生产门禁）。纯查询。</summary>
    public static bool IsFaiPassed(string woNo) =>
        FaiOrders.Any(f => string.Equals(f.WoNo, (woNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                           && FaiStatus.IsPassed(f.Status));

    /// <summary>工单最近一次首件检验单（无则 null）。纯查询。</summary>
    public static FaiOrder? GetLatestFai(string woNo) =>
        FaiOrders.Where(f => string.Equals(f.WoNo, (woNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.CreatedTime)
            .FirstOrDefault();

    /// <summary>首件检验汇总（待检验/已放行/不合格 单数 + 未放行工单数）。纯查询。</summary>
    public static (int Pending, int Passed, int Failed, int NotReleased) GetFaiSummary()
    {
        var pending = FaiOrders.Count(f => f.Status == FaiStatus.Pending);
        var passed = FaiOrders.Count(f => f.Status == FaiStatus.Passed);
        var failed = FaiOrders.Count(f => f.Status == FaiStatus.Failed);
        // 未放行只统计"还在做"的工单：已完成/已关闭的工单本就已经做完，不该再挂在待放行列里。
        // 口径修正（2026-10-08）：原先统计全部工单，补入历史已完工工单后该数字会虚高十几张。
        var notReleased = WorkOrders.Count(w => IsOpenWorkOrder(w) && !IsFaiPassed(w.WoNo));
        return (pending, passed, failed, notReleased);
    }

    // ================= 报表中心（聚合统计） =================

    /// <summary>
    /// 测试数据历史种子（供报表「产量日报 / 良率趋势」使用）：近 13 天（不含今天）每天 10~18 条电性能测试记录，
    /// 良率约 97%（含少量 NG）。固定随机种子保证可复现；仅新增 TestRecords，不影响其它基线。
    /// </summary>
    private static void SeedYieldHistory()
    {
        var rnd = new Random(20260930);
        var testers = new[] { "TST-L1-03", "TST-L2-01" };
        var pool = Cells.Where(c => c.Status is "合格" or "已入库" or "在制" or "化成中" or "检测中").ToList();
        if (pool.Count == 0) return;

        for (var d = 13; d >= 1; d--)
        {
            var date = DateTime.Today.AddDays(-d);
            var count = rnd.Next(10, 19);
            for (var i = 0; i < count; i++)
            {
                var cell = pool[rnd.Next(pool.Count)];
                var is100F = cell.ProductModel.Contains("100F", StringComparison.OrdinalIgnoreCase);
                var passed = rnd.NextDouble() > 0.028;
                var capacity = is100F
                    ? (passed ? 96 + rnd.NextDouble() * 6 : 88 + rnd.NextDouble() * 6)
                    : (passed ? 292 + rnd.NextDouble() * 16 : 270 + rnd.NextDouble() * 15);

                TestRecords.Add(new TestRecord
                {
                    CellSn = cell.Sn,
                    CapacityF = Math.Round(capacity, 2),
                    EsrMohm = Math.Round(passed ? 8 + rnd.NextDouble() * 5 : 13 + rnd.NextDouble() * 5, 2),
                    LeakageUa = Math.Round(1.5 + rnd.NextDouble() * 5, 2),
                    SelfDischarge = Math.Round(1 + rnd.NextDouble() * 3, 2),
                    Tester = testers[rnd.Next(testers.Length)],
                    TestTime = date.AddHours(8 + rnd.Next(11)).AddMinutes(rnd.Next(60)),
                    Passed = passed
                });
            }
        }
    }

    /// <summary>
    /// 异常历史种子（供报表「异常 TOP」与异常管理页演示）：近 14 天 12 条历史异常
    /// （8 条已闭环 / 2 条已确认 / 2 条待确认），覆盖 4 大类、多设备与 3 条线体，含完整履历。
    /// 注意：会改变 AbnormalRecords 数量基线（原 3 条 → 15 条）。
    /// </summary>
    private static void SeedAbnormalHistory()
    {
        if (Equipments.Count == 0) return;
        var rnd = new Random(20260930);
        var foundBys = new[] { "张伟", "李娜", "王强", "赵敏" };
        var owners = new[] { "张伟（生产）", "李娜（工艺）", "王强（设备）", "赵敏（质量）" };

        for (var i = 0; i < 12; i++)
        {
            var occur = DateTime.Today.AddDays(-13 + rnd.Next(14))
                .AddHours(8 + rnd.Next(10)).AddMinutes(rnd.Next(60));
            var category = AbnormalCategories.All[rnd.Next(AbnormalCategories.All.Length)];
            var subTypes = AbnormalSubTypes.Get(category);
            var sub = subTypes.Length == 0 ? "其它" : subTypes[rnd.Next(subTypes.Length)];
            var eq = Equipments[rnd.Next(Equipments.Count)];
            var line = eq.Code.Contains("-L2-") ? "L2" : (eq.Code.Contains("-L3-") ? "L3" : "L1");
            var status = i < 8 ? AbnormalStatus.Closed : (i < 10 ? AbnormalStatus.Confirmed : AbnormalStatus.Pending);
            var abNo = $"AB-{occur:yyyyMMdd}-{100 + i:000}";
            var owner = owners[rnd.Next(owners.Length)];
            var foundBy = foundBys[rnd.Next(foundBys.Length)];

            AbnormalRecords.Add(new AbnormalRecord
            {
                AbNo = abNo,
                OccurTime = occur,
                FoundBy = foundBy,
                Line = line,
                EquipmentCode = eq.Code,
                EquipmentName = eq.Name,
                Category = category,
                SubType = sub,
                Description = $"{eq.Name} 发生「{sub}」（历史记录，供报表统计）",
                TemporaryMeasure = "现场隔离并记录参数，通知责任部门处理",
                Owner = owner,
                Status = status,
                RegisterBy = foundBy,
                RegisterTime = occur.AddMinutes(10),
                SourceType = "人工登记"
            });

            AbnormalHistories.Add(MakeAbHistory(abNo, AbnormalAction.Register, foundBy, occur.AddMinutes(10),
                $"现场登记：{category} / {sub}"));
            if (status != AbnormalStatus.Pending)
                AbnormalHistories.Add(MakeAbHistory(abNo, AbnormalAction.Confirm, "车间主管", occur.AddHours(1),
                    "确认登记内容，转责任人处置"));
            if (status == AbnormalStatus.Closed)
                AbnormalHistories.Add(MakeAbHistory(abNo, AbnormalAction.Close, owner, occur.AddHours(6),
                    "根因已消除，验证有效，闭环"));
        }
    }

    /// <summary>产量月报（按月聚合，默认近 3 个月，含当月；近→远）。纯查询。</summary>
    public static List<OutputReportRow> GetMonthlyOutputReport(int months = 3)
    {
        var cursor = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var rows = new List<OutputReportRow>();
        for (var i = 0; i < Math.Max(1, months); i++)
        {
            var monthStart = cursor.AddMonths(-i);
            rows.Add(BuildOutputRow(monthStart, monthStart.AddMonths(1), monthStart.ToString("yyyy-MM")));
        }
        return rows;
    }

    /// <summary>产量日报（按日聚合：设备日产量 + 过站数 + 测试良率），日期倒序。纯查询。</summary>
    public static List<OutputReportRow> GetDailyOutputReport(DateTime? from = null, DateTime? to = null)
    {
        var start = (from ?? DateTime.Today.AddDays(-13)).Date;
        var end = (to ?? DateTime.Today).Date;
        if (end < start) (start, end) = (end, start);

        var rows = new List<OutputReportRow>();
        for (var d = start; d <= end; d = d.AddDays(1))
            rows.Add(BuildOutputRow(d, d.AddDays(1), d.ToString("yyyy-MM-dd")));
        return rows.OrderByDescending(r => r.Period, StringComparer.Ordinal).ToList();
    }

    /// <summary>构造一个周期的产量 / 过站 / 良率聚合行。</summary>
    private static OutputReportRow BuildOutputRow(DateTime from, DateTime to, string period)
    {
        var daily = DeviceDailyRecords.Where(x => x.Date >= from && x.Date < to).ToList();
        var tests = TestRecords.Where(t => t.TestTime >= from && t.TestTime < to).ToList();
        var pass = tests.Count(t => t.Passed);

        return new OutputReportRow
        {
            Period = period,
            DeviceCount = daily.Where(x => x.Output > 0)
                .Select(x => x.DeviceCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Output = daily.Sum(x => x.Output),
            StationCount = ProcessActuals.Count(a => a.Time >= from && a.Time < to),
            TestCount = tests.Count,
            PassCount = pass,
            NgCount = tests.Count - pass,
            YieldRate = tests.Count == 0 ? 0 : Math.Round(pass * 100.0 / tests.Count, 2)
        };
    }

    /// <summary>良率趋势（按日，含与前一日的环比变化，单位百分点）。纯查询。</summary>
    public static List<YieldTrendRow> GetYieldTrendReport(DateTime? from = null, DateTime? to = null)
    {
        var start = (from ?? DateTime.Today.AddDays(-13)).Date;
        var end = (to ?? DateTime.Today).Date;
        if (end < start) (start, end) = (end, start);

        var rows = new List<YieldTrendRow>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var tests = TestRecords.Where(t => t.TestTime >= d && t.TestTime < d.AddDays(1)).ToList();
            var pass = tests.Count(t => t.Passed);
            rows.Add(new YieldTrendRow
            {
                Period = d.ToString("yyyy-MM-dd"),
                TestCount = tests.Count,
                PassCount = pass,
                NgCount = tests.Count - pass,
                YieldRate = tests.Count == 0 ? 0 : Math.Round(pass * 100.0 / tests.Count, 2)
            });
        }

        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].TestCount == 0 || rows[i - 1].TestCount == 0) continue;
            rows[i].DeltaPoints = Math.Round(rows[i].YieldRate - rows[i - 1].YieldRate, 2);
        }
        return rows.OrderByDescending(r => r.Period, StringComparer.Ordinal).ToList();
    }

    /// <summary>异常 TOP（大类 / 细分 / 设备 / 线体 四维度，各取前 topN，含占比）。纯查询。</summary>
    public static List<AbnormalTopRow> GetAbnormalTopReport(DateTime? from = null, int topN = 8)
    {
        var start = from ?? DateTime.Today.AddDays(-29);
        var data = AbnormalRecords.Where(a => a.OccurTime >= start).ToList();
        var rows = new List<AbnormalTopRow>();
        if (data.Count == 0) return rows;

        void AddDimension(string dimension, Func<AbnormalRecord, string> selector)
        {
            var groups = data
                .Select(a => selector(a) ?? "")
                .Where(name => name.Length > 0)
                .GroupBy(name => name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.Ordinal)
                .Take(topN).ToList();

            foreach (var g in groups)
                rows.Add(new AbnormalTopRow
                {
                    Dimension = dimension,
                    Name = g.Name,
                    Count = g.Count,
                    Share = Math.Round(g.Count * 100.0 / data.Count, 1)
                });
        }

        AddDimension("大类", a => a.Category);
        AddDimension("细分", a => a.SubType);
        AddDimension("设备", a => string.IsNullOrWhiteSpace(a.EquipmentName) ? a.EquipmentCode : a.EquipmentName);
        AddDimension("线体", a => a.Line);
        return rows;
    }

    /// <summary>OEE 汇总（按设备：今日小时记录均值，无记录回退台账值；按达成率倒序）。纯查询。</summary>
    public static List<OeeSummaryRow> GetOeeSummaryReport()
    {
        var today = DateTime.Today;
        return Equipments.Select(e =>
        {
            var hourly = DeviceHourlyRecords
                .Where(h => string.Equals(h.DeviceCode, e.Code, StringComparison.OrdinalIgnoreCase)
                            && h.Date.Date == today)
                .ToList();
            var oee = hourly.Count > 0 ? Math.Round(hourly.Average(h => h.OeeRate), 1) : e.Oee;
            var target = hourly.Count > 0 ? hourly[0].TargetOee : 85;
            return new OeeSummaryRow
            {
                DeviceCode = e.Code,
                DeviceName = e.Name,
                Section = e.Section,
                Status = e.Status,
                TodayOee = oee,
                TargetOee = target,
                Achievement = target <= 0 ? 0 : Math.Round(oee / target * 100, 1)
            };
        }).OrderByDescending(r => r.Achievement).ToList();
    }

    /// <summary>库存汇总（类别维度 + 物料 TOP，含占比）。纯查询。</summary>
    public static List<StockSummaryRow> GetStockSummaryReport(int topN = 10)
    {
        var rows = new List<StockSummaryRow>();
        var stock = WmsStocks.Where(s => s.Quantity > 1e-9).ToList();
        if (stock.Count == 0) return rows;

        var total = stock.Sum(s => s.Quantity);

        rows.AddRange(stock
            .GroupBy(s =>
            {
                var mat = WmsMaterials.FirstOrDefault(m =>
                    string.Equals(m.Code, s.MaterialCode, StringComparison.OrdinalIgnoreCase));
                return string.IsNullOrWhiteSpace(mat?.Category) ? "未分类" : mat!.Category;
            })
            .Select(g => new StockSummaryRow
            {
                Dimension = "类别",
                Name = g.Key,
                ItemCount = g.Count(),
                TotalQty = Math.Round(g.Sum(x => x.Quantity), 2),
                Share = Math.Round(g.Sum(x => x.Quantity) * 100.0 / total, 1)
            })
            .OrderByDescending(r => r.TotalQty));

        rows.AddRange(stock
            .GroupBy(s => s.MaterialName)
            .Select(g => new StockSummaryRow
            {
                Dimension = "物料",
                Name = g.Key,
                ItemCount = g.Count(),
                TotalQty = Math.Round(g.Sum(x => x.Quantity), 2),
                Share = Math.Round(g.Sum(x => x.Quantity) * 100.0 / total, 1)
            })
            .OrderByDescending(r => r.TotalQty).Take(Math.Max(1, topN)));

        return rows;
    }

    /// <summary>报表中心汇总卡（今日/本月产量、本月良率、未闭环异常、设备 OEE 均值、产量环比）。纯查询。</summary>
    public static ReportCenterSummary GetReportCenterSummary()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var todayOutput = DeviceDailyRecords.Where(x => x.Date.Date == today).Sum(x => x.Output);
        var yesterdayOutput = DeviceDailyRecords.Where(x => x.Date.Date == today.AddDays(-1)).Sum(x => x.Output);
        var monthOutput = DeviceDailyRecords
            .Where(x => x.Date.Date >= monthStart && x.Date.Date <= today)
            .Sum(x => x.Output);

        var monthTests = TestRecords.Where(t => t.TestTime >= monthStart).ToList();
        var avgYield = monthTests.Count == 0
            ? 0
            : Math.Round(monthTests.Count(t => t.Passed) * 100.0 / monthTests.Count, 2);
        var oeeList = GetOeeSummaryReport();

        return new ReportCenterSummary
        {
            TodayOutput = todayOutput,
            MonthOutput = monthOutput,
            AvgYield = avgYield,
            OpenAbnormal = AbnormalRecords.Count(a => a.Status != AbnormalStatus.Closed),
            AvgOee = oeeList.Count == 0 ? 0 : Math.Round(oeeList.Average(r => r.TodayOee), 1),
            OutputWow = yesterdayOutput <= 0
                ? null
                : Math.Round((todayOutput - yesterdayOutput) * 100.0 / yesterdayOutput, 1)
        };
    }

    // ================= 返工 / 返修管理 =================

    /// <summary>返工单号序号（跨天自然延续）</summary>
    private static int _reworkSeq;

    private static string NextReworkNo() => $"RW-{DateTime.Now:yyyyMMdd}-{++_reworkSeq:000}";

    private static void AddReworkHistory(string reworkNo, string action, string @operator, DateTime time, string detail) =>
        ReworkHistories.Add(new ReworkHistory
        {
            ReworkNo = reworkNo,
            Action = action,
            Operator = @operator,
            Time = time,
            Detail = detail
        });

    /// <summary>
    /// 返工种子（演示"发现 → 保留 → 返工 → 重检 → 放行"故事线）：三张不同状态的返工单
    /// （待返工 / 待重检 / 已放行），覆盖卡片与状态着色。
    /// 注意：**不修改任何既有集合**（保留单与电芯状态保持原样），仅新增返工单与履历。
    /// </summary>
    private static void SeedRework()
    {
        var today = DateTime.Today;
        if (Holds.Count == 0 || HoldItems.Count == 0) return;   // 保留数据缺失时不播种，保持演示数据自洽

        // ① 待返工（历史示例单：不占用真实保留单，便于演示时从保留单新建完整闭环）
        //    创建时间刻意放到 2 天前：既是"历史"单的自然语义，也让「预警中心 · 返工积压」有可演示样本。
        var r1 = NextReworkNo();
        ReworkOrders.Add(new ReworkOrder
        {
            ReworkNo = r1,
            HoldNo = "（历史单）",
            CellSn = "SC-A260813-L2-00007",
            ProductModel = "SC-CX2026-100F",
            Reason = "卷绕张力超差导致容量偏低（NG）",
            ResponsibleProcess = "卷绕",
            ReworkProcess = "卷绕",
            ReworkTimes = 0,
            Status = ReworkStatus.Pending,
            CreatedBy = "质量工程师",
            CreatedTime = today.AddDays(-2).AddHours(8).AddMinutes(20),
            Remark = "历史示例单（隔离后重新卷绕并复测容量）"
        });
        AddReworkHistory(r1, ReworkAction.Create, "质量工程师", today.AddDays(-2).AddHours(8).AddMinutes(20),
            $"由保留单（历史）发起；电芯 SC-A260813-L2-00007；责任工序 卷绕；返工工序 卷绕");

        // ② 待重检（返工已完成，等待复测；历史单不占活跃保留单）
        var r2 = NextReworkNo();
        ReworkOrders.Add(new ReworkOrder
        {
            ReworkNo = r2,
            HoldNo = "（历史单）",
            CellSn = "SC-A260813-L2-00006",
            ProductModel = "SC-CX2026-100F",
            Reason = "注液量偏差复测",
            ResponsibleProcess = "注液",
            ReworkProcess = "注液",
            ReworkTimes = 1,
            Status = ReworkStatus.WaitInspect,
            CreatedBy = "工艺工程师",
            CreatedTime = today.AddHours(9),
            Operator = "李娜",
            StartTime = today.AddHours(9).AddMinutes(30),
            FinishTime = today.AddHours(11),
            ReworkParam = "注液量 5.02g（目标 5.00g）",
            Remark = "返工完成，等待电性能复测"
        });
        AddReworkHistory(r2, ReworkAction.Create, "工艺工程师", today.AddHours(9), "由保留单发起；返工工序 注液");
        AddReworkHistory(r2, ReworkAction.Start, "李娜", today.AddHours(9).AddMinutes(30), "开始返工；参数：注液量 5.02g");
        AddReworkHistory(r2, ReworkAction.Finish, "李娜", today.AddHours(11), "返工完成，转重检");

        // ③ 已放行（历史完整闭环样本）
        var r3 = NextReworkNo();
        ReworkOrders.Add(new ReworkOrder
        {
            ReworkNo = r3,
            HoldNo = "（历史单）",
            CellSn = "SC-A260812-L1-00003",
            ProductModel = "SC-CX2026-100F",
            Reason = "封口压力不足复检",
            ResponsibleProcess = "封口",
            ReworkProcess = "封口",
            ReworkTimes = 1,
            Status = ReworkStatus.Released,
            CreatedBy = "质量工程师",
            CreatedTime = today.AddDays(-2).AddHours(10),
            Operator = "王强",
            StartTime = today.AddDays(-2).AddHours(11),
            FinishTime = today.AddDays(-2).AddHours(14),
            ReworkParam = "封口压力 0.42MPa",
            InspectResult = "合格",
            InspectStandard = "容量 ≥95F",
            InspectMeasured = "96.8F",
            InspectBy = "赵敏",
            InspectTime = today.AddDays(-2).AddHours(16),
            InspectTimes = 1,
            ReleaseTime = today.AddDays(-2).AddHours(16).AddMinutes(10),
            ReleaseBy = "质量工程师",
            Remark = "重检合格，已放行"
        });
        AddReworkHistory(r3, ReworkAction.Create, "质量工程师", today.AddDays(-2).AddHours(10), "由保留单发起；返工工序 封口");
        AddReworkHistory(r3, ReworkAction.Start, "王强", today.AddDays(-2).AddHours(11), "开始返工；参数：封口压力 0.42MPa");
        AddReworkHistory(r3, ReworkAction.Finish, "王强", today.AddDays(-2).AddHours(14), "返工完成，转重检");
        AddReworkHistory(r3, ReworkAction.InspectPass, "赵敏", today.AddDays(-2).AddHours(16), "重检合格（容量 96.8F）");
        AddReworkHistory(r3, ReworkAction.Release, "质量工程师", today.AddDays(-2).AddHours(16).AddMinutes(10), "解除保留，电芯状态置合格");
    }

    /// <summary>
    /// 由保留单发起返工：校验保留单存在且「保留中」、电芯在该单明细内、该电芯无未完成返工单。
    /// </summary>
    public static (bool Ok, string Message, string ReworkNo) CreateReworkFromHold(
        string holdNo, string cellSn, string reason, string responsibleProcess, string reworkProcess, string @operator)
    {
        var hold = Holds.FirstOrDefault(h => string.Equals(h.HoldNo, (holdNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (hold is null) return (false, $"未找到保留单 {holdNo}。", "");
        if (hold.Status != HoldStatus.Active)
            return (false, $"保留单 {hold.HoldNo} 当前为「{hold.Status}」，只有「保留中」的保留单可发起返工。", "");

        var sn = (cellSn ?? "").Trim();
        if (sn.Length == 0) return (false, "请选择要返工的电芯。", "");
        var inHold = HoldItems.Any(i => string.Equals(i.HoldNo, hold.HoldNo, StringComparison.OrdinalIgnoreCase)
                                        && string.Equals(i.CellSn, sn, StringComparison.OrdinalIgnoreCase));
        if (!inHold) return (false, $"电芯 {sn} 不在保留单 {hold.HoldNo} 的明细中。", "");
        if (string.IsNullOrWhiteSpace(reworkProcess)) return (false, "请选择返工工序。", "");

        var active = ReworkOrders.FirstOrDefault(r => string.Equals(r.CellSn, sn, StringComparison.OrdinalIgnoreCase)
                                                      && !ReworkStatus.IsFinal(r.Status));
        if (active is not null)
            return (false, $"电芯 {sn} 已有未完成的返工单 {active.ReworkNo}（{active.Status}），请先完成或报废。", "");

        var cell = Cells.FirstOrDefault(c => string.Equals(c.Sn, sn, StringComparison.OrdinalIgnoreCase));
        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        var no = NextReworkNo();

        ReworkOrders.Add(new ReworkOrder
        {
            ReworkNo = no,
            HoldNo = hold.HoldNo,
            CellSn = sn,
            ProductModel = cell?.ProductModel ?? "",
            Reason = (reason ?? "").Trim(),
            ResponsibleProcess = (responsibleProcess ?? "").Trim(),
            ReworkProcess = reworkProcess.Trim(),
            ReworkTimes = 0,
            Status = ReworkStatus.Pending,
            CreatedBy = op,
            CreatedTime = now
        });
        AddReworkHistory(no, ReworkAction.Create, op, now,
            $"由保留单 {hold.HoldNo} 发起；电芯 {sn}；责任工序 {responsibleProcess}；返工工序 {reworkProcess}；原因 {reason}");

        return (true, $"返工单 {no} 创建成功（电芯 {sn}，返工工序 {reworkProcess}）。", no);
    }

    /// <summary>
    /// 开始返工（待返工 → 返工中）：记录执行人 / 开始时间 / 返工参数，
    /// 并写入一条工序实绩（ProcessActuals，使返工动作进入追溯链）。
    /// </summary>
    public static (bool Ok, string Message) StartRework(string reworkNo, string @operator, string param)
    {
        var r = FindRework(reworkNo);
        if (r is null) return (false, $"未找到返工单 {reworkNo}。");
        if (r.Status != ReworkStatus.Pending) return (false, $"返工单 {r.ReworkNo} 当前为「{r.Status}」，只有「待返工」可开始返工。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        r.Status = ReworkStatus.InProgress;
        r.Operator = op;
        r.StartTime = now;
        r.ReworkParam = (param ?? "").Trim();

        var cell = Cells.FirstOrDefault(c => string.Equals(c.Sn, r.CellSn, StringComparison.OrdinalIgnoreCase));
        if (cell is not null && ProcessSequence.IndexOf(r.ReworkProcess) >= 0)
        {
            ProcessActuals.Add(new ProcessActualRecord
            {
                CellSn = cell.Sn,
                WoNo = cell.WoNo,
                ProductModel = cell.ProductModel,
                ProcessName = r.ReworkProcess,
                Equipment = ProcessSequence.DefaultEquipment.TryGetValue(r.ReworkProcess, out var eq) ? eq : "",
                Operator = op,
                Shift = "白班",
                ParamName = "返工",
                ParamValue = r.ReworkParam,
                Time = now
            });
        }

        AddReworkHistory(r.ReworkNo, ReworkAction.Start, op, now,
            string.IsNullOrWhiteSpace(r.ReworkParam) ? "开始返工" : $"开始返工；参数：{r.ReworkParam}");
        return (true, $"返工单 {r.ReworkNo} 已开始返工（执行人 {op}）。");
    }

    /// <summary>返工完成（返工中 → 待重检）。</summary>
    public static (bool Ok, string Message) FinishRework(string reworkNo, string @operator)
    {
        var r = FindRework(reworkNo);
        if (r is null) return (false, $"未找到返工单 {reworkNo}。");
        if (r.Status != ReworkStatus.InProgress) return (false, $"返工单 {r.ReworkNo} 当前为「{r.Status}」，只有「返工中」可执行返工完成。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        r.Status = ReworkStatus.WaitInspect;
        r.FinishTime = now;
        AddReworkHistory(r.ReworkNo, ReworkAction.Finish, op, now, "返工完成，转重检");
        return (true, $"返工单 {r.ReworkNo} 返工完成，等待重检。");
    }

    /// <summary>
    /// 重检登记（仅「待重检」可操作）：
    /// 合格 → 状态「已放行」+ **解除来源保留单** + 电芯状态置「合格」；
    /// 不合格 → 退回「返工中」并累计返工次数。同时写入一条检验记录（项目「返工后重检」）。
    /// </summary>
    public static (bool Ok, string Message) RegisterReworkInspection(
        string reworkNo, bool passed, string standard, string measured, string @operator, string remark)
    {
        var r = FindRework(reworkNo);
        if (r is null) return (false, $"未找到返工单 {reworkNo}。");
        if (r.Status != ReworkStatus.WaitInspect)
            return (false, $"返工单 {r.ReworkNo} 当前为「{r.Status}」，只有「待重检」可登记重检结果。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        var cell = Cells.FirstOrDefault(c => string.Equals(c.Sn, r.CellSn, StringComparison.OrdinalIgnoreCase));

        // 重检记录（复用检验集合，工序标注「返工后重检」；同电芯同工序替换式保存）
        Inspections.RemoveAll(i => string.Equals(i.CellSn, r.CellSn, StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(i.ProcessName, "返工后重检", StringComparison.OrdinalIgnoreCase));
        Inspections.Add(new InspectionRecord
        {
            CellSn = r.CellSn,
            Equipment = ProcessSequence.DefaultEquipment.TryGetValue("检测", out var eq) ? eq : "",
            ProcessName = "返工后重检",
            ItemName = "返工后重检",
            StandardValue = (standard ?? "").Trim(),
            MeasuredValue = (measured ?? "").Trim(),
            Result = passed ? "合格" : "不合格",
            Inspector = op,
            InspectTime = now
        });

        r.InspectTimes++;
        r.InspectResult = passed ? "合格" : "不合格";
        r.InspectStandard = (standard ?? "").Trim();
        r.InspectMeasured = (measured ?? "").Trim();
        r.InspectBy = op;
        r.InspectTime = now;
        if (!string.IsNullOrWhiteSpace(remark)) r.Remark = remark.Trim();

        if (passed)
        {
            r.Status = ReworkStatus.Released;
            r.ReleaseTime = now;
            r.ReleaseBy = op;
            AddReworkHistory(r.ReworkNo, ReworkAction.InspectPass, op, now,
                $"重检合格（标准 {r.InspectStandard}／实测 {r.InspectMeasured}）");

            // 放行：解除来源保留单（若仍在保留中）+ 电芯状态置「合格」
            var extra = "（无来源保留单需解除）";
            var hold = Holds.FirstOrDefault(h => string.Equals(h.HoldNo, r.HoldNo, StringComparison.OrdinalIgnoreCase));
            if (hold is not null && hold.Status == HoldStatus.Active)
            {
                var (rok, rmsg) = ReleaseHold(hold.HoldNo, op, $"返工单 {r.ReworkNo} 重检合格放行");
                if (rok)
                {
                    extra = $"；已解除保留单 {hold.HoldNo}";
                    AddReworkHistory(r.ReworkNo, ReworkAction.Release, op, now, $"解除保留单 {hold.HoldNo}，电芯状态置合格");
                }
                else
                {
                    extra = $"；解除保留失败：{rmsg}";
                }
            }
            if (cell is not null) cell.Status = "合格";

            return (true, $"返工单 {r.ReworkNo} 重检合格，已放行{extra}。");
        }

        r.ReworkTimes++;
        r.Status = ReworkStatus.InProgress;
        AddReworkHistory(r.ReworkNo, ReworkAction.InspectFail, op, now,
            $"重检不合格（标准 {r.InspectStandard}／实测 {r.InspectMeasured}），退回返工（累计 {r.ReworkTimes} 次）");
        return (true, $"返工单 {r.ReworkNo} 重检不合格，已退回返工（累计返工 {r.ReworkTimes} 次）。");
    }

    /// <summary>报废（任意非终态 → 已报废）：电芯状态置「不良」，保留单保持不解除。</summary>
    public static (bool Ok, string Message) ScrapRework(string reworkNo, string @operator, string reason)
    {
        var r = FindRework(reworkNo);
        if (r is null) return (false, $"未找到返工单 {reworkNo}。");
        if (ReworkStatus.IsFinal(r.Status)) return (false, $"返工单 {r.ReworkNo} 已是终态（{r.Status}），不能报废。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        r.Status = ReworkStatus.Scrapped;
        r.ScrapReason = (reason ?? "").Trim();
        var cell = Cells.FirstOrDefault(c => string.Equals(c.Sn, r.CellSn, StringComparison.OrdinalIgnoreCase));
        if (cell is not null) cell.Status = "不良";

        AddReworkHistory(r.ReworkNo, ReworkAction.Scrap, op, now,
            $"报废；原因：{(string.IsNullOrWhiteSpace(r.ScrapReason) ? "未填写" : r.ScrapReason)}");
        return (true, $"返工单 {r.ReworkNo} 已报废。");
    }

    public static ReworkOrder? FindRework(string reworkNo) =>
        ReworkOrders.FirstOrDefault(r => string.Equals(r.ReworkNo, (reworkNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>查询返工单（状态 + 关键字：单号/电芯/保留单/原因）；创建时间倒序。纯查询。</summary>
    public static List<ReworkOrder> QueryReworkOrders(string? status, string? keyword)
    {
        IEnumerable<ReworkOrder> q = ReworkOrders;
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(r => r.ReworkNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.CellSn.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.HoldNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                             || r.Reason.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderByDescending(r => r.CreatedTime)
                .ThenBy(r => r.ReworkNo, StringComparer.Ordinal)
                .ToList();
    }

    public static List<ReworkHistory> GetReworkHistories(string reworkNo) =>
        ReworkHistories.Where(h => string.Equals(h.ReworkNo, (reworkNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => h.Time).ThenBy(h => h.Action, StringComparer.Ordinal).ToList();

    /// <summary>按状态统计返工单数量（汇总卡用）。纯查询。</summary>
    public static Dictionary<string, int> GetReworkSummary() =>
        ReworkStatus.All.ToDictionary(s => s, s => ReworkOrders.Count(r => r.Status == s));

    /// <summary>可发起返工的候选（「保留中」保留单的电芯，且该电芯无未完成返工单）。纯查询。</summary>
    public static List<(string HoldNo, string CellSn, string ProductModel, string HoldReason)> GetReworkCandidates()
    {
        var result = new List<(string, string, string, string)>();
        foreach (var h in Holds.Where(x => x.Status == HoldStatus.Active))
        {
            foreach (var item in HoldItems.Where(i => string.Equals(i.HoldNo, h.HoldNo, StringComparison.OrdinalIgnoreCase)))
            {
                var hasActive = ReworkOrders.Any(r => string.Equals(r.CellSn, item.CellSn, StringComparison.OrdinalIgnoreCase)
                                                      && !ReworkStatus.IsFinal(r.Status));
                if (hasActive) continue;
                var cell = Cells.FirstOrDefault(c => string.Equals(c.Sn, item.CellSn, StringComparison.OrdinalIgnoreCase));
                result.Add((h.HoldNo, item.CellSn, cell?.ProductModel ?? "", h.Reason));
            }
        }
        return result;
    }

    // ================= WIP 流转看板 =================

    /// <summary>
    /// WIP 流转种子：补 20 只「在制」电芯并生成其过站历史（卷绕→…→当前工序），
    /// 加上原有 00005（化成中）/00007（检测中）补历史，形成各工序在制分布：
    /// **卷绕 6（瓶颈）/ 入壳 4 / 注液 4 / 封口 3 / 化成 3 / 检测 2，合计 22 只**。
    /// 注意：Cells 由 21 只增至 41 只、ProcessActuals 增加约 60 条；既有出货/保留等基线不受影响
    /// （已入库 / 已出货电芯状态不在"在制三态"内，不参与流转）。
    /// </summary>
    private static void SeedWipFlow()
    {
        const string el100 = "PC-260812-L1-001";
        const string jl100 = "EL-260812-012";
        const string el300 = "PC-260813-L2-002";
        const string jl300 = "EL-260813-015";

        // (SN, 型号, 产线, 工单, 极片批, 电解液批, 当前工序下标, 最近过站距现在小时数)
        var plans = new List<(string Sn, string Model, string Line, string Wo, string ELot, string JLot, int Cur, double StayH)>
        {
            // 卷绕在制 6 只（滞留 2~46h，刻意制造瓶颈）
            ("SC-A260915-L1-00022", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 0, 2),
            ("SC-A260915-L1-00023", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 0, 3),
            ("SC-A260915-L1-00024", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 0, 6),
            ("SC-A260915-L1-00025", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 0, 26),
            ("SC-A260915-L1-00026", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 0, 34),
            ("SC-A260915-L1-00027", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 0, 46),
            // 入壳在制 4 只
            ("SC-A260915-L1-00028", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 1, 5),
            ("SC-A260915-L1-00029", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 1, 7),
            ("SC-A260915-L1-00030", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 1, 28),
            ("SC-A260915-L1-00031", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 1, 30),
            // 注液在制 4 只
            ("SC-A260915-L2-00032", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 2, 4),
            ("SC-A260915-L2-00033", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 2, 8),
            ("SC-A260915-L2-00034", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 2, 9),
            ("SC-A260915-L2-00035", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 2, 32),
            // 封口在制 3 只
            ("SC-A260915-L2-00036", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 3, 6),
            ("SC-A260915-L2-00037", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 3, 26),
            ("SC-A260915-L2-00038", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 3, 28),
            // 化成在制 2 只
            ("SC-A260915-L2-00039", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 4, 1),
            ("SC-A260915-L2-00040", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 4, 2),
            // 检测在制 1 只
            ("SC-A260915-L2-00041", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 5, 5),
            // 原有电芯补过站历史（不新建，仅归类到既有状态对应工序）
            ("SC-A260812-L1-00005", "SC-CX2026-100F", "L1", "WO-20260812-001", el100, jl100, 4, 3),
            ("SC-A260813-L2-00007", "SC-CX2026-300F", "L2", "WO-20260813-002", el300, jl300, 5, 5)
        };

        var rnd = new Random(20260930);   // 固定种子，保证每次运行分布一致

        foreach (var p in plans)
        {
            var status = ProcessSequence.StatusAt(ProcessSequence.Order[p.Cur]);
            var cell = Cells.FirstOrDefault(c => string.Equals(c.Sn, p.Sn, StringComparison.OrdinalIgnoreCase));
            if (cell is null)
            {
                cell = new CellSn
                {
                    Sn = p.Sn,
                    WoNo = p.Wo,
                    ProductModel = p.Model,
                    Line = p.Line,
                    Status = status,
                    CreateTime = DateTime.Today.AddDays(-3).AddHours(8),
                    ElectrodeLot = p.ELot,
                    ElectrolyteLot = p.JLot,
                    ModuleSn = "",
                    Result = ""
                };
                Cells.Add(cell);
            }
            else
            {
                cell.Status = status;   // 原有电芯同步（00005/00007 原本即为化成中/检测中，值不变）
            }

            // 过站历史：从首道到当前道，时间从"最近过站"往前按每道 6 小时递推
            var t = DateTime.Now.AddHours(-p.StayH);
            for (var i = p.Cur; i >= 0; i--)
            {
                var proc = ProcessSequence.Order[i];
                ProcessActuals.Add(new ProcessActualRecord
                {
                    CellSn = cell.Sn,
                    WoNo = cell.WoNo,
                    ProductModel = cell.ProductModel,
                    ProcessName = proc,
                    Equipment = ProcessSequence.DefaultEquipment.TryGetValue(proc, out var eq) ? eq : "",
                    // 按资质派工：优先持该工序上岗证者，少量刻意派无证人（供资质核查演示）
                    Operator = PickOperator(proc, rnd, "张伟"),
                    Shift = i % 2 == 0 ? "白班" : "夜班",
                    ParamName = "",
                    ParamValue = "",
                    Time = t
                });
                t = t.AddHours(-6);
            }
        }
    }

    /// <summary>
    /// 电芯 SN → 最近一次过站记录（仅统计流转顺序内的工序）。
    /// 归类口径：按**工序流转深度**取最深处（时间乱序 / 补录数据不影响"当前工序"判定）；
    /// 同一工序取时间最大的一条作为最近过站时间。纯查询。
    /// </summary>
    private static Dictionary<string, ProcessActualRecord> BuildLastStationByCell()
    {
        var map = new Dictionary<string, ProcessActualRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in ProcessActuals)
        {
            var idx = ProcessSequence.IndexOf(a.ProcessName);
            if (string.IsNullOrWhiteSpace(a.CellSn) || idx < 0) continue;
            if (!map.TryGetValue(a.CellSn, out var cur))
            {
                map[a.CellSn] = a;
                continue;
            }
            var curIdx = ProcessSequence.IndexOf(cur.ProcessName);
            if (idx > curIdx || (idx == curIdx && a.Time > cur.Time)) map[a.CellSn] = a;
        }
        return map;
    }

    /// <summary>
    /// 所有"在制"电芯（状态 ∈ 在制/化成中/检测中）及其最近过站归类；
    /// 无过站记录的电芯不计入（原型口径）。按滞留时长降序。纯查询。
    /// </summary>
    public static List<WipCellRow> GetAllWipCells()
    {
        var lastByCell = BuildLastStationByCell();
        var result = new List<WipCellRow>();
        foreach (var c in Cells)
        {
            if (c.Status is not ("在制" or "化成中" or "检测中")) continue;
            if (!lastByCell.TryGetValue(c.Sn, out var last)) continue;
            var idx = ProcessSequence.IndexOf(last.ProcessName);
            result.Add(new WipCellRow
            {
                CellSn = c.Sn,
                ProductModel = c.ProductModel,
                WoNo = c.WoNo,
                Status = c.Status,
                LastStationTime = last.Time,
                StayHours = Math.Round((DateTime.Now - last.Time).TotalHours, 1),
                ProcessName = last.ProcessName,
                Equipment = last.Equipment,
                PrevProcess = idx > 0 ? ProcessSequence.Order[idx - 1] : "—"
            });
        }
        return result.OrderByDescending(r => r.StayHours).ToList();
    }

    /// <summary>某道工序的在制电芯明细（按滞留时长降序）。纯查询。</summary>
    public static List<WipCellRow> GetWipCellsByProcess(string processName) =>
        GetAllWipCells()
            .Where(r => string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// 工序流转看板：每道工序一行的聚合视图。
    /// 在制口径：电芯最近一次过站所在工序（状态 ∈ 在制/化成中/检测中）。
    /// 阈值规则：均衡水位 = 总在制 / 工序数；在制数 ≥ 水位×1.5 → 瓶颈；≥ 水位 → 偏忙；否则正常。
    /// 纯查询，不修改任何集合。
    /// </summary>
    public static List<WipFlowRow> GetWipFlowRows()
    {
        var wip = GetAllWipCells();
        var total = wip.Count;
        var today = DateTime.Today;

        var rows = new List<WipFlowRow>();
        for (var i = 0; i < ProcessSequence.Order.Length; i++)
        {
            var proc = ProcessSequence.Order[i];
            var cells = wip
                .Where(c => string.Equals(c.ProcessName, proc, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var stationRecords = ProcessActuals
                .Where(a => string.Equals(a.ProcessName, proc, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Time)
                .ToList();

            double? cycle = null;
            if (stationRecords.Count >= 2)
            {
                var gaps = new List<double>();
                for (var k = 1; k < stationRecords.Count; k++)
                    gaps.Add((stationRecords[k].Time - stationRecords[k - 1].Time).TotalMinutes);
                cycle = Math.Round(gaps.Average(), 1);
            }

            var wipCount = cells.Count;
            double? maxStay = wipCount == 0 ? null : Math.Round(cells.Max(c => c.StayHours), 1);

            var status = WipFlowStatus.Normal;
            var alert = "";
            if (total > 0)
            {
                var balanced = (double)total / ProcessSequence.Order.Length;
                if (wipCount >= balanced * 1.5)
                {
                    status = WipFlowStatus.Bottleneck;
                    alert = $"在制 {wipCount} 只，超出均衡水位 {balanced:0.#}，建议疏导";
                }
                else if (wipCount >= balanced)
                {
                    status = WipFlowStatus.Busy;
                    alert = $"在制 {wipCount} 只，接近均衡水位 {balanced:0.#}";
                }
            }

            rows.Add(new WipFlowRow
            {
                Seq = i + 1,
                ProcessName = proc,
                EquipmentCount = stationRecords
                    .Select(a => a.Equipment)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                WipCount = wipCount,
                TodayCount = stationRecords.Count(a => a.Time >= today),
                YesterdayCount = stationRecords.Count(a => a.Time >= today.AddDays(-1) && a.Time < today),
                CycleMinutes = cycle,
                MaxStayHours = maxStay,
                Status = status,
                Alert = alert
            });
        }
        return rows;
    }

    /// <summary>
    /// 模拟推进（演示）：把滞留最长的 count 只在制电芯推进到下一道工序——写过站记录 + 同步电芯状态。
    /// **已入库 / 已出货电芯不参与**（状态不在在制三态内，出货演示数据不受影响）。
    /// 返回 (推进数, 消息)。
    /// </summary>
    public static (int Moved, string Message) SimulateWipFlow(int count)
    {
        if (count <= 0) return (0, "推进数量必须大于 0。");

        var movable = GetAllWipCells()
            .Where(c => ProcessSequence.Next(c.ProcessName) is not null)
            .OrderByDescending(c => c.StayHours)
            .Take(count)
            .ToList();
        if (movable.Count == 0) return (0, "没有可推进的电芯（均已到末道工序）。");

        var rnd = new Random();
        var moved = 0;
        var detail = new List<string>();
        foreach (var c in movable)
        {
            var next = ProcessSequence.Next(c.ProcessName)!;
            ProcessActuals.Add(new ProcessActualRecord
            {
                CellSn = c.CellSn,
                WoNo = c.WoNo,
                ProductModel = c.ProductModel,
                ProcessName = next,
                Equipment = ProcessSequence.DefaultEquipment.TryGetValue(next, out var eq) ? eq : "",
                // 按资质派工：优先持该工序上岗证者，少量刻意派无证人（供资质核查演示）
                Operator = PickOperator(next, rnd, "张伟"),
                Shift = "白班",
                ParamName = "",
                ParamValue = "",
                Time = DateTime.Now
            });
            var cell = Cells.FirstOrDefault(x => string.Equals(x.Sn, c.CellSn, StringComparison.OrdinalIgnoreCase));
            if (cell is not null) cell.Status = ProcessSequence.StatusAt(next);
            detail.Add($"{c.CellSn}：{c.ProcessName}→{next}");
            moved++;
        }
        return (moved, $"已推进 {moved} 只：" + string.Join("；", detail));
    }

    // ---------------- WMS 入库检 ----------------

    /// <summary>
    /// 某采购入库单的入库检填写行：已有记录则回填 MeasuredValue/Result/Inspector；
    /// 无记录则返回 WmsInspectionTemplate 的空行副本（MeasuredValue 空、Result 空）。
    /// 保存记录中若含模板外项目，一并追加返回。
    /// </summary>
    public static List<InspectionRow> GetWmsInspectionRows(string orderNo)
    {
        var saved = WmsInspections.Where(i => i.OrderNo == orderNo).ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<InspectionRow>();

        foreach (var (item, standard) in WmsInspectionTemplate.Items)
        {
            var s = saved.FirstOrDefault(x => x.ItemName == item);
            rows.Add(new InspectionRow
            {
                ItemName = item,
                StandardValue = string.IsNullOrWhiteSpace(s?.StandardValue) ? standard : s!.StandardValue,
                MeasuredValue = s?.MeasuredValue ?? "",
                Result = s?.Result ?? "",
                Inspector = s?.Inspector ?? ""
            });
            used.Add(item);
        }
        foreach (var s in saved.Where(x => !used.Contains(x.ItemName)))
        {
            rows.Add(new InspectionRow
            {
                ItemName = s.ItemName,
                StandardValue = s.StandardValue,
                MeasuredValue = s.MeasuredValue,
                Result = s.Result,
                Inspector = s.Inspector
            });
        }
        return rows;
    }

    /// <summary>
    /// 替换式保存入库检：先删该单旧记录再写新记录（Inspector 取行内值，空则用操作人）；
    /// 追加履历(入库检)。返回 (Saved, NgCount)。
    /// </summary>
    public static (int Saved, int NgCount) SaveWmsInspections(string orderNo, string @operator, List<InspectionRow> rows)
    {
        WmsInspections.RemoveAll(i => i.OrderNo == orderNo);
        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var now = DateTime.Now;
        var saved = 0;
        var ng = 0;

        foreach (var r in rows ?? new List<InspectionRow>())
        {
            if (string.IsNullOrWhiteSpace(r.ItemName)) continue;
            var result = string.IsNullOrWhiteSpace(r.Result) ? "合格" : r.Result.Trim();
            WmsInspections.Add(new WmsInspectionRecord
            {
                OrderNo = orderNo,
                ItemName = r.ItemName.Trim(),
                StandardValue = r.StandardValue?.Trim() ?? "",
                MeasuredValue = r.MeasuredValue?.Trim() ?? "",
                Result = result,
                Inspector = string.IsNullOrWhiteSpace(r.Inspector) ? op : r.Inspector.Trim(),
                InspectTime = now
            });
            saved++;
            if (result == "不合格") ng++;
        }

        WmsOrderHistories.Add(MakeWmsHistory(orderNo, WmsAction.Inspect, op, now,
            $"入库检：保存 {saved} 条（不合格 {ng} 条）。"));
        return (saved, ng);
    }

    /// <summary>某单据已保存的入库检条数</summary>
    public static int CountWmsInspections(string orderNo) =>
        WmsInspections.Count(i => i.OrderNo == orderNo);

    /// <summary>某单据已保存的入库检不合格条数</summary>
    public static int CountWmsInspectionNg(string orderNo) =>
        WmsInspections.Count(i => i.OrderNo == orderNo && i.Result == "不合格");

    // ================= ERP 集成（增量模块） =================

    /// <summary>
    /// 模拟「从 ERP 获取最新 PO」：按状态 / 关键字过滤，按 PO 号倒序返回（口径统一、可复现）。
    /// 关键字匹配 PO 号 / 客户 / 产品型号（忽略大小写）；status 为空 = 全部。
    /// 纯查询，不修改任何集合。
    /// </summary>
    public static List<ErpPo> FetchErpPos(string? status, string? keyword)
    {
        IEnumerable<ErpPo> query = ErpPos;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status!.Trim();
            query = query.Where(p => p.Status == s);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword!.Trim();
            query = query.Where(p =>
                (p.PoNo.Length > 0 && p.PoNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (p.Customer.Length > 0 && p.Customer.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || (p.ProductModel.Length > 0 && p.ProductModel.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }
        return query.OrderByDescending(p => p.PoNo, StringComparer.Ordinal).ToList();
    }

    /// <summary>按 PO 号精确查询 ERP PO（忽略大小写）</summary>
    public static ErpPo? FindErpPo(string poNo)
    {
        if (string.IsNullOrWhiteSpace(poNo)) return null;
        return ErpPos.FirstOrDefault(p => string.Equals(p.PoNo, poNo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>模拟从 ERP 拉取某产品型号的 BOM；型号为空 / 未知型号 → 空列表。纯查询，不修改集合。</summary>
    public static List<ErpBomItem> FetchErpBom(string productModel)
    {
        if (string.IsNullOrWhiteSpace(productModel)) return new List<ErpBomItem>();
        var model = productModel.Trim();
        return ErpBomItems
            .Where(b => string.Equals(b.ProductModel, model, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>取某工单的 BOM（按工单产品型号）；工单不存在或无 BOM → 空列表。纯查询，不修改集合。</summary>
    public static List<ErpBomItem> GetWorkOrderBom(string woNo)
    {
        if (string.IsNullOrWhiteSpace(woNo)) return new List<ErpBomItem>();
        var wo = WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase));
        if (wo is null) return new List<ErpBomItem>();
        return FetchErpBom(wo.ProductModel);
    }

    /// <summary>单条 BOM 需求量 = 单位用量 × (1 + 损耗率/100) × 计划数量</summary>
    public static double GetBomRequirement(ErpBomItem item, int planQty) =>
        item.QtyPerUnit * (1 + item.LossRate / 100.0) * planQty;

    /// <summary>将 BOM 明细投影为「含需求量」的展示行（供工单页明细区与 BOM 窗体复用）</summary>
    public static List<ErpBomRequirementRow> BuildBomRequirementRows(IReadOnlyList<ErpBomItem> items, int planQty)
    {
        var rows = new List<ErpBomRequirementRow>(items.Count);
        foreach (var b in items)
        {
            rows.Add(new ErpBomRequirementRow
            {
                MaterialCode = b.MaterialCode,
                MaterialName = b.MaterialName,
                Spec = b.Spec,
                Unit = b.Unit,
                QtyPerUnit = b.QtyPerUnit,
                LossRate = b.LossRate,
                Requirement = GetBomRequirement(b, planQty),
                MaterialType = b.MaterialType
            });
        }
        return rows;
    }

    /// <summary>
    /// 「ERP 下发」：将勾选的待下发 PO 逐条生成工单（工单号 = WO-{PoNo}）。
    /// 校验：PO 存在、状态 = 待下发；同号工单已存在 → 跳过该条并计入消息。
    /// 成功：新增工单（ErpOrderNo/ProductModel/PlanQty/Line/DueDate/Status=未开始/ErpReportStatus=未报工）
    ///       + 该 PO 置「已下发」并落 PushedTime；返回生成的工单号列表。
    /// 全部失败 / 跳过 → Ok=false 并给出原因；空入参 → 失败且集合零变化。
    /// </summary>
    public static (bool Ok, string Message, List<string> WoNos) PushErpOrdersToWorkOrders(
        IReadOnlyList<string> poNos, string line, string @operator)
    {
        var created = new List<string>();
        if (poNos is null || poNos.Count == 0)
            return (false, "请先勾选要下发的 ERP PO。", created);

        // 去重（同一次勾选重复提交同一 PO 只处理一次），并剔除空白项
        var distinct = poNos
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0)
            return (false, "请先勾选要下发的 ERP PO。", created);

        var lineValue = string.IsNullOrWhiteSpace(line) ? "L1" : line.Trim();
        var now = DateTime.Now;
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var poNo in distinct)
        {
            var po = FindErpPo(poNo);
            if (po is null)
            {
                failed.Add($"{poNo}：ERP 中不存在");
                continue;
            }
            if (po.Status == ErpPoStatus.Pushed)
            {
                skipped.Add($"{poNo}：已下发，跳过");
                continue;
            }

            var woNo = "WO-" + po.PoNo;
            if (WorkOrders.Any(w => string.Equals(w.WoNo, woNo, StringComparison.OrdinalIgnoreCase)))
            {
                skipped.Add($"{poNo}：工单 {woNo} 已存在，跳过");
                continue;
            }

            WorkOrders.Add(new WorkOrder
            {
                WoNo = woNo,
                ErpOrderNo = po.PoNo,
                ProductModel = po.ProductModel,
                Line = lineValue,
                PlanQty = po.OrderQty,
                FinishedQty = 0,
                NgQty = 0,
                Status = "未开始",
                StartDate = now,
                DueDate = po.DueDate,
                ErpReportStatus = ErpReportStatus.NotReported
            });
            po.Status = ErpPoStatus.Pushed;
            po.PushedTime = now;
            created.Add(woNo);
        }

        if (created.Count == 0)
        {
            var reasons = skipped.Concat(failed).ToList();
            var reasonText = reasons.Count > 0 ? string.Join("；", reasons) : "无可下发的 PO。";
            return (false, $"未生成任何工单：{reasonText}", created);
        }

        var msg = $"下发成功 {created.Count} 张工单：{string.Join("、", created)}。";
        if (skipped.Count > 0) msg += $" 跳过 {skipped.Count} 条（{string.Join("；", skipped)}）。";
        if (failed.Count > 0) msg += $" 失败 {failed.Count} 条（{string.Join("；", failed)}）。";
        return (true, msg, created);
    }

    /// <summary>工单是否已终结（已完成 / 已关闭）——ERP 报工的前置条件。</summary>
    public static bool IsWorkOrderTerminated(WorkOrder wo) => wo.Status is "已完成" or "已关闭";

    /// <summary>
    /// 「ERP 报工」：把一次报工（产出 / 良品 / 不良 / 工时）上传 ERP，写报工记录并累计工单工时。
    /// 校验：工单存在；工单已终结（已完成 / 已关闭）；产出 &gt; 0；良品 + 不良 == 产出；工时 ≥ 0。
    /// 任一校验失败 → 不写记录、不改工单状态，返回失败原因。
    /// 成功：写 ErpWorkReport（含 ReportNo / ErpReceipt）+ 工单 ErpReportStatus=已报工 / ErpReportTime=now / WorkHours 累加。
    /// 允许同一工单多次报工（每次一条记录、工时累加）。
    /// </summary>
    public static (bool Ok, string Message, string ReportNo, string ErpReceipt) PushErpWorkReport(
        string woNo, int outputQty, int goodQty, int ngQty, double workHours, string @operator, string remark)
    {
        var wo = string.IsNullOrWhiteSpace(woNo)
            ? null
            : WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, woNo.Trim(), StringComparison.OrdinalIgnoreCase));
        if (wo is null) return (false, $"未找到工单 {woNo}。", "", "");
        if (!IsWorkOrderTerminated(wo))
            return (false, $"工单 {wo.WoNo} 尚未终结（当前状态：{wo.Status}），请先执行「终结工单」后再报工。", "", "");
        if (outputQty <= 0) return (false, "产出数量必须大于 0。", "", "");
        if (goodQty + ngQty != outputQty)
            return (false, $"良品数 ({goodQty}) + 不良数 ({ngQty}) 必须等于产出数量 ({outputQty})。", "", "");
        if (workHours < 0) return (false, "投入工时不能为负数。", "", "");

        var now = DateTime.Now;
        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        _erpReportSeq++;
        var reportNo = $"RPT-{now:yyyyMMdd}-{_erpReportSeq:000}";
        var receipt = NextErpReceipt();

        ErpWorkReports.Add(new ErpWorkReport
        {
            ReportNo = reportNo,
            WoNo = wo.WoNo,
            ErpOrderNo = wo.ErpOrderNo,
            OutputQty = outputQty,
            GoodQty = goodQty,
            NgQty = ngQty,
            WorkHours = workHours,
            Operator = op,
            ReportTime = now,
            ErpStatus = ErpUploadStatus.Success,
            ErpReceipt = receipt,
            Remark = remark
        });

        wo.ErpReportStatus = ErpReportStatus.Reported;
        wo.ErpReportTime = now;
        wo.WorkHours += workHours;

        return (true, $"报工成功：报工单号 {reportNo}，ERP 回执 {receipt}。", reportNo, receipt);
    }

    /// <summary>ERP 报工记录（按报工时间倒序，同刻按报工单号倒序）；woNo 为空 = 全部。纯查询，不修改集合。</summary>
    public static List<ErpWorkReport> GetErpWorkReports(string? woNo = null)
    {
        IEnumerable<ErpWorkReport> query = ErpWorkReports;
        if (!string.IsNullOrWhiteSpace(woNo))
        {
            var w = woNo!.Trim();
            query = query.Where(r => string.Equals(r.WoNo, w, StringComparison.OrdinalIgnoreCase));
        }
        return query
            .OrderByDescending(r => r.ReportTime)
            .ThenByDescending(r => r.ReportNo, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 某工单累计上报工时（h）：按报工记录汇总（与工单 WorkHours 字段口径一致 —— 二者均由「ERP 报工」同步维护）。
    /// </summary>
    public static double GetWorkOrderWorkHours(string woNo)
    {
        if (string.IsNullOrWhiteSpace(woNo)) return 0;
        var w = woNo.Trim();
        return ErpWorkReports
            .Where(r => string.Equals(r.WoNo, w, StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.WorkHours);
    }

    /// <summary>模拟 ERP 回执号：ACK- + 6 位数字</summary>
    private static string NextErpReceipt() => $"ACK-{Random.Shared.Next(100000, 1000000)}";

    // ============================================================
    // SPC 统计过程控制（增量模块）
    // 监控项定义 / 样本点 / 控制图统计与过程能力 / 参数预警闭环
    // ============================================================

    /// <summary>追加模拟样本使用的确定性随机源（与种子随机源分离，保证结果可复现）</summary>
    private static readonly Random _spcAppendRnd = new(20260915);

    /// <summary>
    /// 播种 SPC 演示数据：4 个监控项 × 60 个样本点（固定随机种子，结果可复现），并重算生成初始预警。
    /// 各监控项数据形态刻意做出对比，用于演示「过程稳定 / 能力不足 / 趋势漂移 / 单点尖峰」四类场景：
    ///   ① 卷绕张力：过程稳定（均值≈1.75、σ≈0.05 → Cpk≈1.6，无判异）；
    ///   ② 注液量：能力不足（均值≈5.00、σ≈0.085 → Cpk≈0.78，含超规格点）；
    ///   ③ 容量：后段向下漂移（前 40 点稳定在 98 附近，后段逐步降到 93 附近并出现 1 个低点 85）→ 命中「连续 6 点递减」「单点超控制限」及超规格；
    ///   ④ ESR：含 2 个尖峰（30.5 / 27.8）→ 命中「单点超出控制限」。
    /// </summary>
    /// <summary>
    /// ERP 库存种子（模拟 ERP 同步结果）：与 MES 台账按 物料编码+批次+库位 对账，
    /// 刻意做出「一致 / 差异 / ERP 无此键 / ERP 独有」四类样本，便于演示对账效果。
    /// 注意：不修改 MES 台账（WMS 库存总量仍为 5610）。
    /// </summary>
    private static void SeedErpStocks()
    {
        var syncTime = DateTime.Today.AddHours(7).AddMinutes(30);   // 当日 07:30 从 ERP 同步

        var seeds = new (string Code, string Name, string Spec, string Unit, string Lot, string Loc, double Qty, string Remark)[]
        {
            ("M-ELE-ACN-02", "电解液（ACN基）", "1 mol/L", "L", "EL-260812-012", WmsLocations.RawA, 60, "一致"),
            ("M-COL-AL-04", "铝箔集流体", "20 μm", "m²", "CC-260812-003", WmsLocations.RawA, 520, "ERP 多记 20"),
            ("M-CAN-ALU-05", "铝壳外壳", "Φ18×40", "只", "CAN-260810-001", WmsLocations.RawA, 2000, "一致"),
            ("M-SEL-RUB-06", "密封圈", "Φ18", "只", "SEL-260811-002", WmsLocations.RawA, 1480, "ERP 少记 20"),
            ("M-FG-100F-09", "超级电容成品 SC-CX2026-100F", "100F/2.7V", "只", "FG-100F-260812-01", WmsLocations.FinishedB, 320, "ERP 多记 20"),
            ("M-FG-300F-10", "超级电容成品 SC-CX2026-300F", "300F/2.7V", "只", "FG-300F-260813-01", WmsLocations.FinishedB, 120, "一致"),
            ("M-ACT-CAR-01", "活性炭电极材料", "YP-50F", "kg", "PC-260812-L1-001", WmsLocations.RawA, 500, "一致"),
            // 注：M-SEP-PP-03 / SP-260812-007（MES 600）刻意不建 ERP 记录 → 演示「ERP 无此键」
            ("M-FG-100F-09", "超级电容成品 SC-CX2026-100F", "100F/2.7V", "只", "FG-100F-260812-01", WmsLocations.PendingInspection, 30, "一致（待检区）"),
            // ERP 独有（MES 无此键）→ 演示反向差异
            ("M-COL-AL-04", "铝箔集流体", "20 μm", "m²", "CC-260809-001", WmsLocations.RawA, 80, "ERP 独有：MES 未记账")
        };

        foreach (var s in seeds)
        {
            ErpStockItems.Add(new ErpStockItem
            {
                MaterialCode = s.Code,
                MaterialName = s.Name,
                Spec = s.Spec,
                Unit = s.Unit,
                LotNo = s.Lot,
                Location = s.Loc,
                Quantity = s.Qty,
                LastSyncTime = syncTime,
                Remark = s.Remark
            });
        }
    }

    // ================= ERP 库存对账 =================

    /// <summary>按 物料编码+批次+库位 查 ERP 库存（忽略大小写与首尾空白）；无记录返回 null。纯查询。</summary>
    public static ErpStockItem? FindErpStock(string materialCode, string lotNo, string location)
    {
        var code = (materialCode ?? "").Trim();
        var lot = (lotNo ?? "").Trim();
        var loc = (location ?? "").Trim();
        if (code.Length == 0) return null;
        return ErpStockItems.FirstOrDefault(e =>
            string.Equals(e.MaterialCode, code, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.LotNo, lot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Location, loc, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>查询 ERP 库存（关键字匹配 物料编码/名称/批次/库位；按 编码→批次→库位 排序）。纯查询。</summary>
    public static List<ErpStockItem> QueryErpStocks(string? keyword = null)
    {
        IEnumerable<ErpStockItem> q = ErpStockItems;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(e =>
                e.MaterialCode.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || e.MaterialName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || e.LotNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || e.Location.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderBy(e => e.MaterialCode, StringComparer.Ordinal)
                .ThenBy(e => e.LotNo, StringComparer.Ordinal)
                .ThenBy(e => e.Location, StringComparer.Ordinal)
                .ToList();
    }

    /// <summary>
    /// MES ↔ ERP 库存对账：以 MES 台账为基准逐键比对（物料编码+批次+库位），差异 = MES − ERP；
    /// ERP 无该键时 Diff=null、状态「ERP 无此键」。includeErpOnly=true 时附加「ERP 独有」行。
    /// 纯查询，不修改任何集合。
    /// </summary>
    public static List<WmsStockCompareRow> GetWmsStockCompare(string? keyword = null, bool includeErpOnly = false)
    {
        var rows = new List<WmsStockCompareRow>();
        var usedErpKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in QueryWmsStock(keyword))
        {
            var erp = FindErpStock(m.MaterialCode, m.LotNo, m.Location);
            if (erp is not null)
                usedErpKeys.Add($"{erp.MaterialCode}|{erp.LotNo}|{erp.Location}");

            var diff = erp is null ? (double?)null : m.Quantity - erp.Quantity;
            rows.Add(new WmsStockCompareRow
            {
                MaterialCode = m.MaterialCode,
                MaterialName = m.MaterialName,
                Spec = m.Spec,
                Unit = m.Unit,
                LotNo = m.LotNo,
                Location = m.Location,
                MesQty = m.Quantity,
                ErpQty = erp?.Quantity,
                Diff = diff,
                Status = erp is null
                    ? StockCompareStatus.MesOnly
                    : (Math.Abs(diff ?? 0) < 1e-9 ? StockCompareStatus.Matched : StockCompareStatus.Mismatch),
                MesLastUpdate = m.LastUpdate,
                ErpSyncTime = erp?.LastSyncTime
            });
        }

        if (includeErpOnly)
        {
            foreach (var e in QueryErpStocks(keyword))
            {
                if (usedErpKeys.Contains($"{e.MaterialCode}|{e.LotNo}|{e.Location}")) continue;
                rows.Add(new WmsStockCompareRow
                {
                    MaterialCode = e.MaterialCode,
                    MaterialName = e.MaterialName,
                    Spec = e.Spec,
                    Unit = e.Unit,
                    LotNo = e.LotNo,
                    Location = e.Location,
                    MesQty = 0,
                    ErpQty = e.Quantity,
                    Diff = null,
                    Status = StockCompareStatus.ErpOnly,
                    MesLastUpdate = null,
                    ErpSyncTime = e.LastSyncTime
                });
            }
        }

        return rows;
    }

    /// <summary>
    /// 库存对账汇总。口径：
    /// ・MesTotal = MES 各行合计（含 MES 独有）；ErpTotal = ERP 各行合计（含 ERP 独有）；
    /// ・MatchedDiffTotal = 仅双方都有记录的键的差异合计（MES − ERP）；
    /// ・MesOnlyQty / ErpOnlyQty = 两侧独有数量；NetDiffTotal = MesTotal − ErpTotal（属性计算）。
    /// </summary>
    public static WmsStockCompareSummary GetWmsStockCompareSummary(string? keyword = null)
    {
        var rows = GetWmsStockCompare(keyword, true);
        var matchedOnly = rows
            .Where(r => r.Status == StockCompareStatus.Matched || r.Status == StockCompareStatus.Mismatch)
            .ToList();

        return new WmsStockCompareSummary
        {
            RowCount = rows.Count,
            MatchedCount = rows.Count(r => r.Status == StockCompareStatus.Matched),
            MismatchCount = rows.Count(r => r.Status == StockCompareStatus.Mismatch),
            MesOnlyCount = rows.Count(r => r.Status == StockCompareStatus.MesOnly),
            ErpOnlyCount = rows.Count(r => r.Status == StockCompareStatus.ErpOnly),
            MesTotal = rows.Where(r => r.Status != StockCompareStatus.ErpOnly).Sum(r => r.MesQty),
            ErpTotal = rows.Sum(r => r.ErpQty ?? 0),
            MatchedDiffTotal = matchedOnly.Sum(r => r.Diff ?? 0),
            MesOnlyQty = rows.Where(r => r.Status == StockCompareStatus.MesOnly).Sum(r => r.MesQty),
            ErpOnlyQty = rows.Where(r => r.Status == StockCompareStatus.ErpOnly).Sum(r => r.ErpQty ?? 0)
        };
    }

    private static void SeedSpc()
    {
        if (SpcItems.Count > 0) return;

        // ---- 监控项定义 ----
        SpcItems.AddRange(new[]
        {
            new SpcItem
            {
                ItemId = "SPC-P01", ItemName = "卷绕张力", Category = SpcCategory.ProcessParam,
                SourceKey = "卷绕:卷绕张力", Unit = "N", Lsl = 1.50, Usl = 2.00, DeviceScope = "WIND-L1-01",
                Remark = "规格来自检验标准 卷绕张力 1.5~2.0 N"
            },
            new SpcItem
            {
                ItemId = "SPC-P02", ItemName = "注液量", Category = SpcCategory.ProcessParam,
                SourceKey = "注液:注液量", Unit = "g", Lsl = 4.80, Usl = 5.20, DeviceScope = "INJ-L1-02",
                Remark = "规格来自检验标准（演示口径 4.80~5.20 g）"
            },
            new SpcItem
            {
                ItemId = "SPC-T01", ItemName = "容量 CapacityF", Category = SpcCategory.TestData,
                SourceKey = "CapacityF", Unit = "F", Lsl = 95.0, Usl = double.NaN, DeviceScope = "TST-L1-03",
                Remark = "规格来自检验标准 容量 ≥95 F（单边下限）"
            },
            new SpcItem
            {
                ItemId = "SPC-T02", ItemName = "ESR EsrMohm", Category = SpcCategory.TestData,
                SourceKey = "EsrMohm", Unit = "mΩ", Usl = 25.0, Lsl = double.NaN, DeviceScope = "TST-L1-03",
                Remark = "规格来自检验标准 ESR ≤25 mΩ（单边上限）"
            }
        });

        const int count = 60;
        var baseTime = DateTime.Today.AddDays(-7).AddHours(8);
        var endTime = DateTime.Now;
        if (endTime <= baseTime) endTime = baseTime.AddHours(count);
        var spanTicks = (endTime - baseTime).Ticks;

        DateTime TimeAt(int i) => baseTime + TimeSpan.FromTicks(spanTicks * i / (count - 1));

        var cellSns = Cells.Select(c => c.Sn).ToArray();
        string SnAt(int i) => cellSns.Length == 0 ? "" : cellSns[i % cellSns.Length];

        void Add(string itemId, int seq, double value, DateTime time, string equipment, string cellSn) =>
            SpcSamples.Add(new SpcSample
            {
                ItemId = itemId,
                Seq = seq,
                Value = Math.Round(value, 3),
                SampleTime = time,
                Equipment = equipment,
                CellSn = cellSn,
                Source = SpcSampleSource.Measured
            });

        // ① 卷绕张力：正负交替 + 小幅随机幅值 → 均值≈1.75、σ≈0.05，严格避免同侧/单调长串 → 无判异
        var r1 = new Random(20260914);
        for (var i = 0; i < count; i++)
        {
            var d = 0.025 + r1.NextDouble() * 0.050;
            Add("SPC-P01", i + 1, 1.75 + (i % 2 == 0 ? d : -d), TimeAt(i), "WIND-L1-01", SnAt(i));
        }

        // ② 注液量：正态（均值 5.00 / σ 0.085）→ Cpk≈0.78（能力不足），含超规格点
        var r2 = new Random(20260915);
        for (var i = 0; i < count; i++)
            Add("SPC-P02", i + 1, 5.00 + 0.085 * NextGaussian(r2), TimeAt(i), "INJ-L1-02", SnAt(i + 1));

        // ③ 容量：前 40 点稳定在 98 附近；后段严格递减至 93 附近；末点 85（单点超控制限）
        var r3 = new Random(20260916);
        var prev = 98.0;
        for (var i = 0; i < count; i++)
        {
            double v;
            if (i < 40)
            {
                v = 98.0 + 0.7 * NextGaussian(r3);
            }
            else if (i < 59)
            {
                v = prev - (0.18 + r3.NextDouble() * 0.12);
            }
            else
            {
                v = 85.0; // 单点异常低值：诱发「单点超控制限」判异
            }
            prev = v;
            Add("SPC-T01", i + 1, v, TimeAt(i), "TST-L1-03", SnAt(i + 2));
        }

        // ④ ESR：正态（均值 19 / σ 1.4）叠加 2 个尖峰 → 命中「单点超出控制限」判异
        var r4 = new Random(20260917);
        for (var i = 0; i < count; i++)
        {
            var v = 19.0 + 1.4 * NextGaussian(r4);
            if (i == 14) v = 30.5;
            if (i == 45) v = 27.8;
            Add("SPC-T02", i + 1, v, TimeAt(i), "TST-L1-03", SnAt(i + 3));
        }

        // ---- 规格限版本（版本化：判定真值以此为准；V1 的 Usl/Lsl 必须与 SpcItem 现有种子值一致）----
        // 各项目形态刻意做出对比，演示「预约生效」「历史分段判定 + 当前生效版本变化」「单版本」三类场景。
        var today = DateTime.Today;
        void SeedVersion(string itemId, int version, double usl, double lsl, DateTime from, string remark) =>
            SpcItemVersions.Add(new SpcItemVersion
            {
                ItemId = itemId,
                Version = version,
                Usl = usl,
                Lsl = lsl,
                TargetValue = double.NaN,
                EffectiveFrom = from,
                CreatedBy = "admin",
                CreatedTime = from,
                Remark = remark
            });

        // ① 卷绕张力：V1 已生效 + V2「预约生效」（今天+2 天）→ 当前判定仍用 V1（Cpk 基线不变）
        SeedVersion("SPC-P01", 1, 2.00, 1.50, today.AddDays(-30), "初始版本（检验标准 1.5~2.0 N）");
        SeedVersion("SPC-P01", 2, 2.05, 1.55, today.AddDays(2), "预约生效：收紧至 1.55~2.05 N");

        // ② 注液量：V1 已生效 + V2 已于 3 天前生效 → 演示历史分段判定 + 当前生效版本变化
        SeedVersion("SPC-P02", 1, 5.20, 4.80, today.AddDays(-30), "初始版本（演示口径 4.80~5.20 g）");
        SeedVersion("SPC-P02", 2, 5.15, 4.85, today.AddDays(-3), "收紧至 4.85~5.15 g（已生效）");

        // ③ 容量：V1 单边下限 95 + V2 已生效下限 96 → 前段按 95、后段按 96 判定
        SeedVersion("SPC-T01", 1, double.NaN, 95.0, today.AddDays(-30), "初始版本（容量 ≥95 F，单边下限）");
        SeedVersion("SPC-T01", 2, double.NaN, 96.0, today.AddDays(-5), "收紧下限至 ≥96 F（已生效）");

        // ④ ESR：仅 V1（单边上限 25）
        SeedVersion("SPC-T02", 1, 25.0, double.NaN, today.AddDays(-30), "初始版本（ESR ≤25 mΩ，单边上限）");

        // ---- 记录各监控项已用最高版本号（版本号单调不回退；删除待生效版本不降水位）----
        _spcVersionHighWater.Clear();
        foreach (var g in SpcItemVersions.GroupBy(v => v.ItemId, StringComparer.OrdinalIgnoreCase))
            _spcVersionHighWater[g.Key] = g.Max(v => v.Version);

        // ---- 重算生成初始预警，保证进入页面即可看到待处理预警 ----
        RefreshSpcAlerts(null, "seed");

        // ---- 演示不同状态着色：将最早的 2 条分别置为「已确认」「已关闭」 ----
        var ordered = SpcAlerts.OrderBy(a => a.AlertNo, StringComparer.Ordinal).ToList();
        if (ordered.Count >= 1)
        {
            ordered[0].Status = SpcAlertStatus.Acked;
            ordered[0].Handler = "李工";
            ordered[0].HandleTime = ordered[0].SampleTime.AddHours(2);
        }
        if (ordered.Count >= 2)
        {
            ordered[1].Status = SpcAlertStatus.Closed;
            ordered[1].Handler = "王工";
            ordered[1].HandleTime = ordered[1].SampleTime.AddHours(4);
            ordered[1].Action = "已调整设备参数并复测合格，闭环。";
        }

        // ---- 版本化展示字段回填（当前版本 / 当前规格 / 待生效版本 / 样本数）----
        SyncSpcItemVersionInfo();
    }

    /// <summary>标准正态随机数（Box-Muller 变换）</summary>
    private static double NextGaussian(Random r)
    {
        var u1 = 1.0 - r.NextDouble();   // (0,1] 避免 log(0)
        var u2 = 1.0 - r.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>生成下一个 SPC 预警单号：SPC-yyyyMMdd-序号(3 位)；序号全局递增、跨天自然延续。</summary>
    private static string NextSpcAlertNo()
    {
        _spcAlertSeq++;
        return $"SPC-{DateTime.Now:yyyyMMdd}-{_spcAlertSeq:000}";
    }

    /// <summary>按监控项编号精确查询监控项定义（忽略大小写）</summary>
    public static SpcItem? FindSpcItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        return SpcItems.FirstOrDefault(i => string.Equals(i.ItemId, itemId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 查询某监控项的样本点：equipment 为空 / "全部" 不过滤；from/to 按采样时间日期闭区间（含当日全天）；
    /// 结果按采样时间升序、同刻按 Seq 升序。itemId 为空 → 空列表。
    /// version 非空时仅返回「该点生效版本 == version」的样本（默认 null 不过滤，保持既有调用兼容）。
    /// </summary>
    public static List<SpcSample> GetSpcSamples(string itemId, string? equipment, DateTime? from, DateTime? to, int? version = null)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return new List<SpcSample>();
        var id = itemId.Trim();
        IEnumerable<SpcSample> query = SpcSamples
            .Where(s => string.Equals(s.ItemId, id, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(equipment) && equipment!.Trim() != "全部")
        {
            var eq = equipment.Trim();
            query = query.Where(s => string.Equals(s.Equipment, eq, StringComparison.OrdinalIgnoreCase));
        }
        if (from.HasValue)
        {
            var start = from.Value.Date;
            query = query.Where(s => s.SampleTime >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(s => s.SampleTime <= end);
        }

        var list = query.OrderBy(s => s.SampleTime).ThenBy(s => s.Seq).ToList();

        if (version.HasValue)
        {
            var versions = GetSpcItemVersions(id);
            list = list
                .Where(s => (SpcCalculator.GetVersionAt(versions, s.SampleTime)?.Version ?? 0) == version.Value)
                .ToList();
        }
        return list;
    }

    // ================= SPC 规格限版本管理 =================

    /// <summary>获取某监控项的全部版本（按 EffectiveFrom 升序，同刻按 Version 升序）。itemId 为空 → 空列表。</summary>
    public static List<SpcItemVersion> GetSpcItemVersions(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return new List<SpcItemVersion>();
        var id = itemId.Trim();
        return SpcItemVersions
            .Where(v => string.Equals(v.ItemId, id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.EffectiveFrom)
            .ThenBy(v => v.Version)
            .ToList();
    }

    /// <summary>取 moment 时刻生效的版本（委托 <see cref="SpcCalculator.GetVersionAt"/>；无版本 → null）。</summary>
    public static SpcItemVersion? GetSpcVersionAt(string itemId, DateTime moment)
        => SpcCalculator.GetVersionAt(GetSpcItemVersions(itemId), moment);

    /// <summary>取当前生效版本（now 时刻）。</summary>
    public static SpcItemVersion? GetCurrentSpcVersion(string itemId)
        => GetSpcVersionAt(itemId, DateTime.Now);

    /// <summary>取待生效版本（EffectiveFrom &gt; now 的最早一版；无 → null）。</summary>
    public static SpcItemVersion? GetPendingSpcVersion(string itemId)
    {
        var now = DateTime.Now;
        return GetSpcItemVersions(itemId).FirstOrDefault(v => v.EffectiveFrom > now);
    }

    /// <summary>
    /// 新增版本：校验生效时间严格晚于现有最新版本、至少一侧规格有效、双边时 Lsl&lt;Usl。
    /// 新版本号 = max(该监控项已用最高版本号高水位, 现有最大版本号) + 1 → 版本号<b>单调不回退</b>
    /// （删除待生效版本不会复用其版本号）。写入 CreatedBy/CreatedTime。允许未来时间（预约生效）。
    /// </summary>
    public static (bool Ok, string Message, int NewVersion) AddSpcItemVersion(
        string itemId, double usl, double lsl, DateTime effectiveFrom, string remark, string @operator)
    {
        var item = FindSpcItem(itemId);
        if (item is null) return (false, $"未找到监控项 {itemId}。", 0);

        var specError = ValidateVersionSpec(usl, lsl);
        if (specError is not null) return (false, specError, 0);

        var list = GetSpcItemVersions(item.ItemId);
        var latest = list.Count == 0 ? null : list[^1];
        if (latest is not null && effectiveFrom <= latest.EffectiveFrom)
            return (false,
                $"新版本的生效时间必须严格晚于现有最新版本 {latest.VersionText}（{latest.EffectiveFrom:yyyy-MM-dd HH:mm}）。", 0);

        // 版本号取「高水位」与「现有最大号」的较大者 + 1，保证单调不回退（删除待生效版本后不复用旧号）
        var existingMax = list.Count == 0 ? 0 : list.Max(v => v.Version);
        var highWater = _spcVersionHighWater.TryGetValue(item.ItemId, out var hw) ? hw : 0;
        var newVersion = Math.Max(highWater, existingMax) + 1;

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        var entity = new SpcItemVersion
        {
            ItemId = item.ItemId,
            Version = newVersion,
            Usl = usl,
            Lsl = lsl,
            TargetValue = double.NaN,
            EffectiveFrom = effectiveFrom,
            CreatedBy = op,
            CreatedTime = DateTime.Now,
            Remark = remark ?? ""
        };
        SpcItemVersions.Add(entity);
        _spcVersionHighWater[item.ItemId] = newVersion;   // 抬高水位
        SyncSpcItemVersionInfo();
        return (true,
            $"已为「{item.ItemName}」新增版本 {entity.VersionText}（生效 {effectiveFrom:yyyy-MM-dd HH:mm}，规格 {entity.SpecText}）。",
            entity.Version);
    }

    /// <summary>
    /// 修改版本：仅「待生效」版本可改；同样校验至少一侧规格有效、双边时 Lsl&lt;Usl、
    /// 生效时间严格晚于上一版本且早于下一版本（若存在）。已生效版本 → 拒绝。
    /// </summary>
    public static (bool Ok, string Message) UpdateSpcItemVersion(
        string itemId, int version, double usl, double lsl, DateTime effectiveFrom, string remark, string @operator)
    {
        var item = FindSpcItem(itemId);
        if (item is null) return (false, $"未找到监控项 {itemId}。");

        var list = GetSpcItemVersions(item.ItemId);
        var target = list.FirstOrDefault(v => v.Version == version);
        if (target is null) return (false, $"未找到版本 V{version}。");

        if (target.IsEffective(DateTime.Now))
            return (false, $"版本 {target.VersionText} 已生效，不可修改（只能新建版本）。");

        var specError = ValidateVersionSpec(usl, lsl);
        if (specError is not null) return (false, specError);

        var previous = list
            .Where(v => !ReferenceEquals(v, target) && v.EffectiveFrom < target.EffectiveFrom)
            .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.Version)
            .FirstOrDefault();
        if (previous is not null && effectiveFrom <= previous.EffectiveFrom)
            return (false,
                $"生效时间必须严格晚于上一版本 {previous.VersionText}（{previous.EffectiveFrom:yyyy-MM-dd HH:mm}）。");

        var next = list
            .Where(v => !ReferenceEquals(v, target) && v.EffectiveFrom > target.EffectiveFrom)
            .OrderBy(v => v.EffectiveFrom).ThenBy(v => v.Version)
            .FirstOrDefault();
        if (next is not null && effectiveFrom >= next.EffectiveFrom)
            return (false,
                $"生效时间必须早于下一版本 {next.VersionText}（{next.EffectiveFrom:yyyy-MM-dd HH:mm}）。");

        target.Usl = usl;
        target.Lsl = lsl;
        target.EffectiveFrom = effectiveFrom;
        target.Remark = remark ?? "";
        SyncSpcItemVersionInfo();
        return (true, $"已更新版本 {target.VersionText}（生效 {effectiveFrom:yyyy-MM-dd HH:mm}，规格 {target.SpecText}）。");
    }

    /// <summary>
    /// 删除版本：仅「待生效」版本可删；且删除后该监控项的已生效版本不能为空。已生效版本 → 拒绝。
    /// </summary>
    public static (bool Ok, string Message) DeleteSpcItemVersion(string itemId, int version)
    {
        var item = FindSpcItem(itemId);
        if (item is null) return (false, $"未找到监控项 {itemId}。");

        var list = GetSpcItemVersions(item.ItemId);
        var target = list.FirstOrDefault(v => v.Version == version);
        if (target is null) return (false, $"未找到版本 V{version}。");

        if (target.IsEffective(DateTime.Now))
            return (false, $"版本 {target.VersionText} 已生效，不可删除。");

        var remainingEffective = list.Count(v => !ReferenceEquals(v, target) && v.IsEffective(DateTime.Now));
        if (remainingEffective == 0)
            return (false, "删除后该监控项将没有已生效版本，操作被拒绝。");

        SpcItemVersions.Remove(target);
        SyncSpcItemVersionInfo();
        return (true, $"已删除待生效版本 {target.VersionText}。");
    }

    /// <summary>版本规格校验：至少一侧有效；双边时 Lsl&lt;Usl。返回 null 表示通过。</summary>
    private static string? ValidateVersionSpec(double usl, double lsl)
    {
        var hasUsl = !double.IsNaN(usl);
        var hasLsl = !double.IsNaN(lsl);
        if (!hasUsl && !hasLsl) return "至少需要填写一侧规格限（上限 USL 或下限 LSL）。";
        if (hasUsl && hasLsl && lsl >= usl) return "规格下限 LSL 必须小于规格上限 USL。";
        return null;
    }

    /// <summary>
    /// 为全部监控项回填版本化展示字段：CurrentVersion / CurrentVersionText / CurrentSpecText /
    /// PendingVersionText / SampleCount。版本增删改后与 SeedSpc 末尾调用。
    /// </summary>
    public static void SyncSpcItemVersionInfo()
    {
        var now = DateTime.Now;
        foreach (var item in SpcItems)
        {
            var versions = GetSpcItemVersions(item.ItemId);
            var current = SpcCalculator.GetVersionAt(versions, now);
            var pending = versions.FirstOrDefault(v => v.EffectiveFrom > now);

            item.CurrentVersion = current?.Version ?? 0;
            item.CurrentVersionText = current?.VersionText ?? "";
            item.CurrentSpecText = current is not null ? current.SpecText : "";
            item.PendingVersionText = pending is not null
                ? $"{pending.VersionText}（{pending.EffectiveFrom:yyyy-MM-dd HH:mm} 生效）"
                : "";
            item.SampleCount = SpcSamples.Count(s =>
                string.Equals(s.ItemId, item.ItemId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 重算指定监控项（itemId 为空 / null = 全部项）的预警：用 <see cref="SpcCalculator.ComputeWithVersions"/> 按
    /// 「样本采样时间逐点选择生效版本」分段判定后，仅新增「尚未存在」的预警
    /// （去重口径：ItemId + AlertType + RuleName + SampleTime 全同视为重复）。
    /// 生成的预警写入判定所用版本 Version/VersionText。返回 (本次新增数, 当前预警总数)。
    /// </summary>
    public static (int Added, int Total) RefreshSpcAlerts(string? itemId = null, string @operator = "system")
    {
        var op = string.IsNullOrWhiteSpace(@operator) ? "system" : @operator.Trim();

        var targets = new List<SpcItem>();
        if (string.IsNullOrWhiteSpace(itemId))
        {
            targets.AddRange(SpcItems);
        }
        else
        {
            var one = FindSpcItem(itemId);
            if (one is not null) targets.Add(one);
        }

        var added = 0;
        foreach (var item in targets)
        {
            foreach (var candidate in BuildCandidateAlerts(item, op))
            {
                // 已知限制：去重键为 ItemId + AlertType + RuleName + SampleTime，**不含判定所用规格版本(Version)**，
                // 因此当规格版本发生变更（新建 / 生效）后，同一「监控项 + 预警类型 + 规则 + 采样时刻」的预警
                // 不会重发，也不会自动收敛/更新为按新版本判定的历史预警；仅对「新出现的采样时刻 / 规则」生成新预警。
                var dup = SpcAlerts.Any(a =>
                    string.Equals(a.ItemId, candidate.ItemId, StringComparison.OrdinalIgnoreCase)
                    && a.AlertType == candidate.AlertType
                    && a.RuleName == candidate.RuleName
                    && a.SampleTime == candidate.SampleTime);
                if (dup) continue;

                candidate.AlertNo = NextSpcAlertNo();
                SpcAlerts.Add(candidate);
                added++;
            }
        }
        return (added, SpcAlerts.Count);
    }

    /// <summary>按监控项构造候选预警（不落库、不生成单号），供 RefreshSpcAlerts 去重后插入。按样本时间逐点选择生效版本判定。</summary>
    private static List<SpcAlert> BuildCandidateAlerts(SpcItem item, string op)
    {
        var list = new List<SpcAlert>();
        var samples = GetSpcSamples(item.ItemId, null, null, null);
        var versions = GetSpcItemVersions(item.ItemId);
        var res = SpcCalculator.ComputeWithVersions(samples, versions, DateTime.Now);

        // 超规格：每个越界点一条（按该点生效版本判定，记录该点生效版本）
        for (var i = 0; i < samples.Count; i++)
        {
            var ver = SpcCalculator.GetVersionAt(versions, samples[i].SampleTime);
            if (ver is null) continue;
            var v = samples[i].Value;
            if (ver.HasUsl && v > ver.Usl)
                list.Add(MakeSpcAlert(item, samples[i], SpcAlertType.OutOfSpec, "单点超规格（> 上限）", SpcSeverity.High, res, op, ver));
            else if (ver.HasLsl && v < ver.Lsl)
                list.Add(MakeSpcAlert(item, samples[i], SpcAlertType.OutOfSpec, "单点超规格（< 下限）", SpcSeverity.High, res, op, ver));
        }

        // 判异：每条命中规则一条（样本时间取该规则命中点中的最后一点；规格取该点生效版本）
        foreach (var (ruleName, indexes) in res.RuleHits)
        {
            if (indexes.Count == 0) continue;
            var last = indexes.Max();
            if (last < 0 || last >= samples.Count) continue;
            var ver = SpcCalculator.GetVersionAt(versions, samples[last].SampleTime);
            list.Add(MakeSpcAlert(item, samples[last], SpcAlertType.RuleViolation, ruleName, SeverityOfRule(ruleName), res, op, ver));
        }

        // 能力不足：Cpk<1.0 高；1.0≤Cpk<1.33 中（用「当前生效版本」，样本时间取最后一点）
        if (double.IsFinite(res.Cpk) && samples.Count > 0)
        {
            var lastSample = samples[^1];
            var currentVer = SpcCalculator.GetVersionAt(versions, DateTime.Now);
            if (res.Cpk < 1.0)
                list.Add(MakeSpcAlert(item, lastSample, SpcAlertType.Incapable, "Cpk<1.0", SpcSeverity.High, res, op, currentVer));
            else if (res.Cpk < 1.33)
                list.Add(MakeSpcAlert(item, lastSample, SpcAlertType.Incapable, "Cpk<1.33", SpcSeverity.Medium, res, op, currentVer));
        }

        return list;
    }

    /// <summary>构造一条候选预警（单号留空，由 RefreshSpcAlerts 统一分配）。规格与版本取判定所用版本。</summary>
    private static SpcAlert MakeSpcAlert(
        SpcItem item, SpcSample sample, string type, string ruleName, string severity,
        SpcResult res, string op, SpcItemVersion? ver) =>
        new()
        {
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            AlertType = type,
            RuleName = ruleName,
            Severity = severity,
            Value = sample.Value,
            CenterLine = res.Cl,
            Ucl = res.Ucl,
            Lcl = res.Lcl,
            Usl = ver is not null && ver.HasUsl ? ver.Usl : double.NaN,
            Lsl = ver is not null && ver.HasLsl ? ver.Lsl : double.NaN,
            Version = ver?.Version ?? 0,
            VersionText = ver?.VersionText ?? "",
            SampleTime = sample.SampleTime,
            Equipment = sample.Equipment,
            CellSn = sample.CellSn,
            Status = SpcAlertStatus.Pending,
            Remark = $"系统重算生成 · 操作人 {op}"
        };

    /// <summary>判异规则 → 严重度</summary>
    private static string SeverityOfRule(string ruleName) => ruleName switch


    {
        SpcRuleNames.TwoOfThree => SpcSeverity.Low,
        _ => SpcSeverity.Medium
    };

    /// <summary>
    /// 按条件查询预警：空条件不过滤；from/to 按样本时间日期闭区间（含当日全天）；
    /// 结果按样本时间倒序、同刻按预警单号倒序。
    /// </summary>
    public static List<SpcAlert> QuerySpcAlerts(
        string? itemId, string? alertType, string? severity, string? status, DateTime? from, DateTime? to)
    {
        IEnumerable<SpcAlert> query = SpcAlerts;
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            var id = itemId!.Trim();
            query = query.Where(a => string.Equals(a.ItemId, id, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(alertType))
        {
            var t = alertType!.Trim();
            query = query.Where(a => a.AlertType == t);
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            var sv = severity!.Trim();
            query = query.Where(a => a.Severity == sv);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status!.Trim();
            query = query.Where(a => a.Status == st);
        }
        if (from.HasValue)
        {
            var start = from.Value.Date;
            query = query.Where(a => a.SampleTime >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(a => a.SampleTime <= end);
        }
        return query
            .OrderByDescending(a => a.SampleTime)
            .ThenByDescending(a => a.AlertNo, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>某监控项的全部预警（按样本时间倒序）</summary>
    public static List<SpcAlert> GetSpcAlertsByItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return new List<SpcAlert>();
        var id = itemId.Trim();
        return SpcAlerts
            .Where(a => string.Equals(a.ItemId, id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.SampleTime)
            .ThenByDescending(a => a.AlertNo, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>按预警单号精确查询（忽略大小写）</summary>
    public static SpcAlert? FindSpcAlert(string alertNo)
    {
        if (string.IsNullOrWhiteSpace(alertNo)) return null;
        return SpcAlerts.FirstOrDefault(a => string.Equals(a.AlertNo, alertNo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>确认预警：仅「待处理」→「已确认」（写入处理人/时间）；重复确认或已关闭均拒绝。</summary>
    public static (bool Ok, string Message) AckSpcAlert(string alertNo, string @operator)
    {
        var rec = FindSpcAlert(alertNo);
        if (rec is null) return (false, $"未找到预警 {alertNo}。");
        if (rec.Status == SpcAlertStatus.Acked) return (false, $"预警 {alertNo} 已确认，请勿重复确认。");
        if (rec.Status == SpcAlertStatus.Closed) return (false, $"预警 {alertNo} 已关闭，无法再确认。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        rec.Status = SpcAlertStatus.Acked;
        rec.Handler = op;
        rec.HandleTime = DateTime.Now;
        return (true, $"预警 {alertNo} 已确认，状态变更为「{SpcAlertStatus.Acked}」。");
    }

    /// <summary>关闭预警：「待处理 / 已确认」→「已关闭」；处理措施必填；重复关闭拒绝。</summary>
    public static (bool Ok, string Message) CloseSpcAlert(string alertNo, string @operator, string action)
    {
        var rec = FindSpcAlert(alertNo);
        if (rec is null) return (false, $"未找到预警 {alertNo}。");
        if (rec.Status == SpcAlertStatus.Closed) return (false, $"预警 {alertNo} 已关闭，请勿重复操作。");

        var act = (action ?? "").Trim();
        if (act.Length == 0) return (false, "请填写处理措施后再关闭。");

        var op = string.IsNullOrWhiteSpace(@operator) ? "admin" : @operator.Trim();
        rec.Action = act;
        rec.Status = SpcAlertStatus.Closed;
        rec.Handler = op;
        rec.HandleTime = DateTime.Now;
        return (true, $"预警 {alertNo} 已关闭，状态变更为「{SpcAlertStatus.Closed}」。");
    }

    /// <summary>
    /// 演示用：为指定监控项追加 count 个「模拟」样本点（在最近值附近小幅随机游走，约 1/15 概率插入越界点），
    /// 采样时间顺延至当前时间。返回实际追加数；监控项不存在或 count≤0 → 0。
    /// </summary>
    public static int AppendSpcSamples(string itemId, int count)
    {
        if (count <= 0) return 0;
        var item = FindSpcItem(itemId);
        if (item is null) return 0;

        var existing = GetSpcSamples(item.ItemId, null, null, null);
        var seq = existing.Count == 0 ? 0 : existing.Max(s => s.Seq);

        var sigma = SpcCalculator.SampleSigma(existing.Select(s => s.Value).ToList());
        if (!double.IsFinite(sigma) || sigma <= 0)
            sigma = item.HasUsl && item.HasLsl ? (item.Usl - item.Lsl) / 8.0 : 1.0;

        // 游走起点：优先取最近值；若已越界则回到规格中心附近，避免模拟数据持续越界
        var current = existing.Count > 0
            ? existing[^1].Value
            : item.HasUsl && item.HasLsl ? (item.Usl + item.Lsl) / 2.0 : 0.0;
        if (item.HasUsl && current > item.Usl)
            current = item.HasLsl ? (item.Usl + item.Lsl) / 2.0 : item.Usl * 0.95;
        if (item.HasLsl && current < item.Lsl)
            current = item.HasUsl ? (item.Usl + item.Lsl) / 2.0 : item.Lsl * 1.05;

        var equipment = !string.IsNullOrWhiteSpace(item.DeviceScope)
            ? item.DeviceScope
            : existing.Count > 0 ? existing[^1].Equipment : "";
        var cellSns = Cells.Select(c => c.Sn).ToArray();

        // 采样时间从「原末点」之后逐点顺延（每点 +30 分钟）；无样本时以当前时间往前铺满 count 点。
        const int stepMinutes = 30;
        var baseTime = existing.Count > 0
            ? existing[^1].SampleTime
            : DateTime.Now.AddMinutes(-count * (double)stepMinutes);

        var added = 0;
        for (var i = 0; i < count; i++)
        {
            double value;
            if (_spcAppendRnd.Next(15) == 0) // 约 1/15 概率插入一个越界「尖峰」点（不改变游走基线）
            {
                value = item.HasUsl ? item.Usl * 1.08 : item.HasLsl ? item.Lsl * 0.92 : current;
            }
            else
            {
                current += sigma * 0.3 * NextGaussian(_spcAppendRnd);
                value = current;
            }

            seq++;
            var time = baseTime.AddMinutes((i + 1) * (double)stepMinutes);
            var sn = cellSns.Length == 0 ? "" : cellSns[seq % cellSns.Length];
            SpcSamples.Add(new SpcSample
            {
                ItemId = item.ItemId,
                Seq = seq,
                Value = Math.Round(value, 3),
                SampleTime = time,
                Equipment = equipment,
                CellSn = sn,
                Source = SpcSampleSource.Simulated
            });
            added++;
        }
        return added;
    }

    // ============================================================
    // 人员与资质管理
    // 定位：把「人」这一维补上——无资质不能上岗操作，是本模块唯一的业务价值主张。
    // 设计：人员主数据（Users）+ 资质证书（SkillCertificates）两张表；
    //       资质状态不落库，由「有效期 + 吊销标记」实时派生（避免两处维护、天然自洽）。
    // ============================================================

    /// <summary>证书编号自增序号</summary>
    private static int _certSeq;

    /// <summary>生成证书编号，形如 CERT-2026-0001</summary>
    private static string NextCertNo() => $"CERT-{DateTime.Now:yyyy}-{++_certSeq:0000}";

    /// <summary>
    /// 人员与资质种子：15 名员工（含 1 名离职）、16 张证书。
    /// 刻意构造 3 张「即将到期」+ 1 张「已过期」+ 1 张「已吊销」，让预警与核查都有可演示样本；
    /// 姓名与既有演示数据（工序实绩的操作人）保持一致，使「实绩资质核查」能直接产出真实结论。
    /// </summary>
    private static void SeedPersonnel()
    {
        if (Users.Count > 0) return;

        var today = DateTime.Today;

        Users.AddRange(new[]
        {
            new UserProfile { EmpNo = "E1001", Name = "张伟", Dept = "生产部", Position = "卷绕班长", Crew = "A班", Status = EmploymentStatus.Active, Phone = "138****1001", HireDate = new DateTime(2021, 3, 15) },
            new UserProfile { EmpNo = "E1002", Name = "李娜", Dept = "生产部", Position = "注液操作员", Crew = "A班", Status = EmploymentStatus.Active, Phone = "138****1002", HireDate = new DateTime(2022, 7, 1) },
            new UserProfile { EmpNo = "E1003", Name = "王强", Dept = "设备部", Position = "设备维护工程师", Crew = "常白班", Status = EmploymentStatus.Active, Phone = "138****1003", HireDate = new DateTime(2019, 5, 20) },
            new UserProfile { EmpNo = "E1004", Name = "赵敏", Dept = "质量部", Position = "质量工程师", Crew = "常白班", Status = EmploymentStatus.Active, Phone = "138****1004", HireDate = new DateTime(2020, 9, 10) },
            new UserProfile { EmpNo = "E1005", Name = "周涛", Dept = "生产部", Position = "化成操作员", Crew = "B班", Status = EmploymentStatus.Active, Phone = "138****1005", HireDate = new DateTime(2023, 2, 6) },
            new UserProfile { EmpNo = "E1006", Name = "王芳", Dept = "质量部", Position = "检验员", Crew = "B班", Status = EmploymentStatus.Active, Phone = "138****1006", HireDate = new DateTime(2023, 8, 14) },
            new UserProfile { EmpNo = "E1007", Name = "刘洋", Dept = "生产部", Position = "涂布操作员", Crew = "A班", Status = EmploymentStatus.Active, Phone = "138****1007", HireDate = new DateTime(2022, 11, 3) },
            new UserProfile { EmpNo = "E1008", Name = "陈晨", Dept = "生产部", Position = "卷绕操作员", Crew = "B班", Status = EmploymentStatus.Active, Phone = "138****1008", HireDate = new DateTime(2024, 4, 18) },
            new UserProfile { EmpNo = "E1009", Name = "孙磊", Dept = "生产部", Position = "封口操作员", Crew = "A班", Status = EmploymentStatus.Active, Phone = "138****1009", HireDate = new DateTime(2021, 12, 1) },
            new UserProfile { EmpNo = "E1010", Name = "郑爽", Dept = "生产部", Position = "注液操作员", Crew = "B班", Status = EmploymentStatus.Active, Phone = "138****1010", HireDate = new DateTime(2024, 9, 2) },
            new UserProfile { EmpNo = "E1011", Name = "杨帆", Dept = "生产部", Position = "电性能测试员", Crew = "A班", Status = EmploymentStatus.Active, Phone = "138****1011", HireDate = new DateTime(2023, 5, 22) },
            new UserProfile { EmpNo = "E1012", Name = "吴迪", Dept = "仓储部", Position = "叉车司机", Crew = "常白班", Status = EmploymentStatus.Active, Phone = "138****1012", HireDate = new DateTime(2022, 3, 8) },
            new UserProfile { EmpNo = "E1013", Name = "冯磊", Dept = "生产部", Position = "卷绕操作员", Crew = "B班", Status = EmploymentStatus.Resigned, Phone = "138****1013", HireDate = new DateTime(2020, 1, 6) },
            new UserProfile { EmpNo = "E1014", Name = "何静", Dept = "质量部", Position = "SPC 工程师", Crew = "常白班", Status = EmploymentStatus.Active, Phone = "138****1014", HireDate = new DateTime(2021, 6, 15) },
            // 每个工序至少要有 1 名持有效证的在岗人员，否则自动生成实绩时无合格人员可派
            new UserProfile { EmpNo = "E1015", Name = "韩磊", Dept = "生产部", Position = "涂布操作员", Crew = "A班", Status = EmploymentStatus.Active, Phone = "138****1015", HireDate = new DateTime(2021, 10, 11) },
            new UserProfile { EmpNo = "ADMIN", Name = "系统管理员", Dept = "信息部", Position = "系统管理员", Crew = "常白班", Status = EmploymentStatus.Active, Phone = "138****0000", HireDate = new DateTime(2018, 1, 1) },
        });

        // 资质证书：到期日一律用「今天 + 偏移」构造，保证任何运行日期下状态判定都稳定
        void Cert(string empNo, string item, string level, int issueDaysAgo, int? expiryDaysFromNow,
            string issuer, bool revoked = false, string revokeReason = "", int revokeDaysAgo = 0)
        {
            var emp = Users.First(u => u.EmpNo == empNo);
            SkillCertificates.Add(new SkillCertificate
            {
                EmpNo = empNo,
                EmpName = emp.Name,
                SkillItem = item,
                Level = level,
                CertNo = NextCertNo(),
                IssueDate = today.AddDays(-issueDaysAgo),
                ExpiryDate = expiryDaysFromNow is null ? null : today.AddDays(expiryDaysFromNow.Value),
                IssuedBy = issuer,
                Revoked = revoked,
                RevokeReason = revokeReason,
                RevokeTime = revoked ? today.AddDays(-revokeDaysAgo) : null
            });
        }

        const string inner = "泰州工厂能力认定委员会";
        const string thirdParty = "江苏省特种作业培训中心";

        // ① 生产一线（持工序上岗证）
        Cert("E1001", SkillCatalog.Winding, CertLevel.Senior, 1200, 730, inner);              // 张伟 · 卷绕高级
        Cert("E1001", SkillCatalog.TpmOperator, CertLevel.Middle, 900, 365, inner);            // 张伟 · 点检资格
        Cert("E1002", SkillCatalog.Injection, CertLevel.Middle, 1000, 540, inner);             // 李娜 · 注液中级
        Cert("E1002", SkillCatalog.Sealing, CertLevel.Junior, 1100, 12, inner);                // 李娜 · 封口 → 即将到期
        Cert("E1003", SkillCatalog.TpmOperator, CertLevel.Senior, 1500, 900, inner);           // 王强 · 点检资格
        Cert("E1003", SkillCatalog.Electrician, CertLevel.Senior, 1400, 400, thirdParty);      // 王强 · 低压电工
        Cert("E1003", SkillCatalog.ETest, CertLevel.Middle, 800, 600, inner);                  // 王强 · 电性能检测
        Cert("E1004", SkillCatalog.FaiInspector, CertLevel.Senior, 1600, 800, inner);          // 赵敏 · 首件检验
        Cert("E1004", SkillCatalog.SpcAnalyst, CertLevel.Middle, 1000, 500, inner);            // 赵敏 · SPC
        Cert("E1004", SkillCatalog.QualityEngineer, CertLevel.Middle, 1300, 700, inner);       // 赵敏 · 质量工程师
        Cert("E1004", SkillCatalog.ETest, CertLevel.Junior, 700, 25, inner);                   // 赵敏 · 电性能检测 → 即将到期
        Cert("E1005", SkillCatalog.Formation, CertLevel.Junior, 600, 450, inner);              // 周涛 · 化成
        Cert("E1006", SkillCatalog.FaiInspector, CertLevel.Junior, 500, 20, inner);            // 王芳 · 首件检验 → 即将到期
        Cert("E1007", SkillCatalog.Coating, CertLevel.Middle, 1100, -40, inner);               // 刘洋 · 涂布 → 已过期
        Cert("E1008", SkillCatalog.Winding, CertLevel.Junior, 300, 700, inner);                // 陈晨 · 卷绕
        Cert("E1009", SkillCatalog.Sealing, CertLevel.Middle, 1000, 800, inner);               // 孙磊 · 封口
        Cert("E1010", SkillCatalog.Injection, CertLevel.Junior, 200, 900, inner);              // 郑爽 · 注液
        Cert("E1010", SkillCatalog.Formation, CertLevel.Junior, 300, 700, inner);              // 郑爽 · 化成（多能工）
        Cert("E1011", SkillCatalog.ETest, CertLevel.Junior, 500, 750, inner);                  // 杨帆 · 电性能检测
        Cert("E1012", SkillCatalog.Forklift, CertLevel.Middle, 900, 300, thirdParty);          // 吴迪 · 叉车证
        Cert("E1013", SkillCatalog.Winding, CertLevel.Middle, 1600, 400, inner,
            revoked: true, revokeReason: "离职收回", revokeDaysAgo: 120);                       // 冯磊 · 已吊销
        Cert("E1014", SkillCatalog.SpcAnalyst, CertLevel.Senior, 1200, 800, inner);            // 何静 · SPC
        Cert("E1014", SkillCatalog.QualityEngineer, CertLevel.Senior, 1000, 700, inner);       // 何静 · 质量工程师
        Cert("E1015", SkillCatalog.Coating, CertLevel.Middle, 800, 620, inner);                // 韩磊 · 涂布（涂布段唯一有效持证者）
    }

    /// <summary>
    /// 生成演示用工序实绩时的「按资质派工」取样间隔：每 N 条刻意派 1 名不合规人员，
    /// 使「上岗资质核查」既有真实结论（绝大多数合规），又能抓出可追溯的问题样本。
    /// </summary>
    private const int UnqualifiedPickInterval = 17;

    /// <summary>
    /// 为某工序挑选操作人：**优先从持有该工序上岗证且在岗的人员中随机选取**，
    /// 每 <see cref="UnqualifiedPickInterval"/> 条刻意选 1 名未持证者（用于演示核查能抓到人）。
    /// 返回人员姓名；若无任何在册人员或无对应工序要求，返回传入的兜底值。
    /// </summary>
    private static string PickOperator(string? processName, Random rnd, string fallback)
    {
        var active = Users.Where(u => u.IsActive).ToList();
        if (active.Count == 0) return fallback;
        if (SkillCatalog.SkillOfProcess(processName) is null) return fallback;

        var qualified = active.Where(u => CheckQualification(u.EmpNo, processName).Ok).ToList();
        var unqualified = active.Where(u => !CheckQualification(u.EmpNo, processName).Ok).ToList();

        var pick = ProcessActuals.Count % UnqualifiedPickInterval == UnqualifiedPickInterval - 1;
        if (pick && unqualified.Count > 0) return unqualified[rnd.Next(unqualified.Count)].Name;
        if (qualified.Count == 0) return unqualified.Count == 0 ? fallback : unqualified[rnd.Next(unqualified.Count)].Name;
        return qualified[rnd.Next(qualified.Count)].Name;
    }

    // ---------------- 人员查询 ----------------

    /// <summary>按工号（忽略大小写）或姓名精确查找人员；找不到返回 null。</summary>
    public static UserProfile? FindUser(string? empNoOrName)
    {
        var key = (empNoOrName ?? "").Trim();
        if (key.Length == 0) return null;
        return Users.FirstOrDefault(u =>
                   string.Equals(u.EmpNo, key, StringComparison.OrdinalIgnoreCase))
               ?? Users.FirstOrDefault(u => string.Equals(u.Name, key, StringComparison.Ordinal));
    }

    /// <summary>
    /// 人员台账查询（关键字匹配 工号/姓名/岗位；部门与在职状态可选精准过滤）。
    /// 返回投影行 <see cref="UserRow"/>（含资质统计），按工号升序。
    /// </summary>
    public static List<UserRow> QueryUsers(string? keyword = null, string? dept = null, string? status = null)
    {
        var kw = (keyword ?? "").Trim();

        return Users
            .Where(u => dept is null || u.Dept == dept)
            .Where(u => status is null || u.Status == status)
            .Where(u => kw.Length == 0
                        || u.EmpNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || u.Name.Contains(kw, StringComparison.Ordinal)
                        || u.Position.Contains(kw, StringComparison.Ordinal))
            .OrderBy(u => u.EmpNo, StringComparer.Ordinal)
            .Select(u =>
            {
                var certs = SkillCertificates.Where(c => c.EmpNo == u.EmpNo).ToList();
                return new UserRow
                {
                    EmpNo = u.EmpNo,
                    Name = u.Name,
                    Dept = u.Dept,
                    Position = u.Position,
                    Crew = u.Crew,
                    Status = u.Status,
                    Phone = u.Phone,
                    HireDate = u.HireDate,
                    CertTotal = certs.Count,
                    CertValid = certs.Count(c => c.CanWork),
                    CertExpiring = certs.Count(c => c.Status == CertStatus.ExpiringSoon),
                    CertInvalid = certs.Count(c => !c.CanWork),
                    ProcessSkillCount = certs.Count(c => c.CanWork && SkillCatalog.ProcessSkills.Contains(c.SkillItem))
                };
            })
            .ToList();
    }

    /// <summary>全部部门（去重、字典序），供筛选下拉使用。</summary>
    public static List<string> GetDepartments() =>
        Users.Select(u => u.Dept).Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();

    /// <summary>全部班组（去重，按「A班 / B班 / 常白班」习惯顺序）。</summary>
    public static List<string> GetCrews() =>
        Users.Select(u => u.Crew).Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal).ToList();

    // ---------------- 资质查询 ----------------

    /// <summary>某人全部资质（按资质项顺序、状态严重度排序：失效在前）。</summary>
    public static List<SkillCertificate> GetCertificates(string? empNo)
    {
        var key = (empNo ?? "").Trim();
        if (key.Length == 0) return new List<SkillCertificate>();
        return SkillCertificates
            .Where(c => string.Equals(c.EmpNo, key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => StatusOrder(c.Status))
            .ThenBy(c => Array.IndexOf(SkillCatalog.All, c.SkillItem))
            .ToList();
    }

    /// <summary>资质状态排序权重：已吊销 0 &lt; 已过期 1 &lt; 即将到期 2 &lt; 有效 3</summary>
    private static int StatusOrder(string status) => status switch
    {
        CertStatus.Revoked => 0,
        CertStatus.Expired => 1,
        CertStatus.ExpiringSoon => 2,
        _ => 3
    };

    /// <summary>
    /// 资质台账查询：关键字匹配 工号/姓名/资质项/证书编号；状态可选（有效/即将到期/已过期/已吊销）。
    /// 按「状态严重度 → 剩余天数 → 工号」排序，让需要处理的排在最前面。
    /// </summary>
    public static List<SkillCertificate> QueryCertificates(string? keyword = null, string? status = null)
    {
        var kw = (keyword ?? "").Trim();
        return SkillCertificates
            .Where(c => status is null || c.Status == status)
            .Where(c => kw.Length == 0
                        || c.EmpNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || c.EmpName.Contains(kw, StringComparison.Ordinal)
                        || c.SkillItem.Contains(kw, StringComparison.Ordinal)
                        || c.CertNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => StatusOrder(c.Status))
            .ThenBy(c => c.DaysLeft ?? 9999)
            .ThenBy(c => c.EmpNo, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 人员与资质汇总：在册人数 / 持证人数 / 即将到期资质数 / 已失效资质数。
    /// 口径：只统计**在职**人员；「持证」= 至少持 1 项可上岗资质（有效或即将到期）；
    /// 「已失效」= 已过期 + 已吊销。纯查询。
    /// </summary>
    public static (int OnDuty, int Certified, int ExpiringCert, int InvalidCert) GetPersonnelSummary()
    {
        var active = Users.Where(u => u.IsActive).ToList();
        var activeNos = active.Select(u => u.EmpNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var certs = SkillCertificates.Where(c => activeNos.Contains(c.EmpNo)).ToList();

        var certified = certs.Where(c => c.CanWork).Select(c => c.EmpNo)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return (active.Count,
            certified,
            certs.Count(c => c.Status == CertStatus.ExpiringSoon),
            certs.Count(c => !c.CanWork));
    }

    // ---------------- 上岗资质校验（核心防错规则） ----------------

    /// <summary>
    /// 上岗资质校验：判断「某人能否在某工序上岗操作」。
    /// 返回 (是否通过, 结论说明, 所需资质项, 证书状态)。
    /// 规则优先级：工序无持证要求 → 人员不存在 → 人员离职 → 无证 → 证已失效 → 通过。
    /// 纯查询，不修改任何数据。
    /// </summary>
    public static (bool Ok, string Reason, string Skill, string CertStatus) CheckQualification(
        string? empNoOrName, string? processName)
    {
        var skill = SkillCatalog.SkillOfProcess(processName);
        if (skill is null)
            return (true, $"工序「{(processName ?? "").Trim()}」未设置持证要求，无需资质校验。", "", "");

        var user = FindUser(empNoOrName);
        if (user is null)
            return (false, $"操作人「{(empNoOrName ?? "").Trim()}」未在人员主数据登记，不允许上岗。", skill, "未登记");

        if (!user.IsActive)
            return (false, $"{user.Name}（{user.EmpNo}）已于 {user.HireText} 后离职，状态为「{user.Status}」，不允许上岗。", skill, "已离职");

        var cert = SkillCertificates.FirstOrDefault(c =>
            string.Equals(c.EmpNo, user.EmpNo, StringComparison.OrdinalIgnoreCase)
            && c.SkillItem == skill && !c.Revoked);

        if (cert is null)
            return (false, $"{user.Name}（{user.EmpNo}）未取得「{skill}」，不允许在「{processName}」工序上岗。", skill, "无证");

        if (!cert.CanWork)
            return (false, $"{user.Name}（{user.EmpNo}）的「{skill}」{cert.Status}（{cert.DaysLeftText}），不允许上岗。", skill, cert.Status);

        return (true, $"{user.Name}（{user.EmpNo}）持「{skill}」{cert.Level}，状态 {cert.Status}（{cert.DaysLeftText}），允许上岗。", skill, cert.Status);
    }

    /// <summary>
    /// 资质矩阵：行=在职人员，列=<see cref="SkillCatalog.All"/>，格值=资质等级（无证为 "—"）。
    /// 返回 (资质项列, 矩阵行)，供界面手工建列渲染（避免字典列绑定带来的列名漂移）。
    /// </summary>
    public static (List<string> Items, List<SkillMatrixRow> Rows) GetSkillMatrix(string? dept = null)
    {
        var items = SkillCatalog.All.ToList();
        var users = Users
            .Where(u => u.IsActive)
            .Where(u => dept is null || u.Dept == dept)
            .OrderBy(u => u.EmpNo, StringComparer.Ordinal)
            .ToList();

        var rows = users.Select(u =>
        {
            var mine = SkillCertificates
                .Where(c => string.Equals(c.EmpNo, u.EmpNo, StringComparison.OrdinalIgnoreCase) && !c.Revoked)
                .ToList();

            var cells = new string[items.Count];
            var statuses = new string[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var hit = mine.FirstOrDefault(c => c.SkillItem == items[i]);
                cells[i] = hit is null ? "—" : hit.Level;
                statuses[i] = hit?.Status ?? "";
            }

            return new SkillMatrixRow
            {
                EmpNo = u.EmpNo,
                EmpName = u.Name,
                Dept = u.Dept,
                Position = u.Position,
                Crew = u.Crew,
                Status = u.Status,
                Cells = cells,
                CellStatuses = statuses,
                CertTotal = cells.Count(c => c != "—")
            };
        }).ToList();

        return (items, rows);
    }

    /// <summary>
    /// 班组绩效：按班组聚合 人数 / 持证人数 / 有效资质项 / 即将到期 / 失效 / 工序覆盖 / 实绩条数 / 无证上岗嫌疑条数。
    /// 只统计在职人员；「工序覆盖」= 该班组持有的工序类上岗证覆盖的工序数（同一工序多证只计一次）。纯查询。
    /// </summary>
    public static List<CrewPerformanceRow> GetCrewPerformance()
    {
        var checks = GetQualificationChecks(null, onlyFailed: true);

        return Users
            .Where(u => u.IsActive)
            .GroupBy(u => u.Crew, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var empNos = g.Select(u => u.EmpNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var names = g.Select(u => u.Name).ToHashSet(StringComparer.Ordinal);
                var certs = SkillCertificates.Where(c => empNos.Contains(c.EmpNo)).ToList();

                var coveredProcesses = certs
                    .Where(c => c.CanWork && SkillCatalog.ProcessSkills.Contains(c.SkillItem))
                    .Select(c => ProcessOfSkill(c.SkillItem))
                    .Where(p => p.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                return new CrewPerformanceRow
                {
                    Crew = g.Key,
                    Headcount = g.Count(),
                    CertifiedHeadcount = certs.Where(c => c.CanWork).Select(c => c.EmpNo)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    ValidCertCount = certs.Count(c => c.CanWork),
                    ExpiringCertCount = certs.Count(c => c.Status == CertStatus.ExpiringSoon),
                    InvalidCertCount = certs.Count(c => !c.CanWork),
                    CoveredProcessCount = coveredProcesses,
                    ProcessOpCount = ProcessActuals.Count(a => names.Contains(a.Operator)),
                    UnqualifiedOpCount = checks.Count(c => names.Contains(c.Operator))
                };
            })
            .ToList();
    }

    /// <summary>资质项 → 对应工序名（<see cref="SkillCatalog.SkillOfProcess"/> 的逆映射；非工序类返回空串）。</summary>
    private static string ProcessOfSkill(string skill) => skill switch
    {
        SkillCatalog.Coating => "涂布",
        SkillCatalog.Winding => "卷绕",
        SkillCatalog.Injection => "注液",
        SkillCatalog.Sealing => "封口",
        SkillCatalog.Formation => "化成",
        SkillCatalog.ETest => "电性能检测",
        _ => ""
    };

    /// <summary>
    /// 实绩资质核查：对工序实绩逐条判定「操作人是否持证上岗」。
    /// 参数：工序名（null=全部）、是否只看不通过；按时间倒序。纯查询。
    /// </summary>
    public static List<QualificationCheckRow> GetQualificationChecks(string? processName = null, bool onlyFailed = false)
    {
        var proc = (processName ?? "").Trim();

        return ProcessActuals
            .Where(a => proc.Length == 0 || string.Equals(a.ProcessName, proc, StringComparison.Ordinal))
            .Select(a =>
            {
                var skill = SkillCatalog.SkillOfProcess(a.ProcessName);
                var (ok, reason, _, status) = CheckQualification(a.Operator, a.ProcessName);
                var user = FindUser(a.Operator);
                var cert = skill is null || user is null
                    ? null
                    : SkillCertificates.FirstOrDefault(c =>
                        string.Equals(c.EmpNo, user.EmpNo, StringComparison.OrdinalIgnoreCase)
                        && c.SkillItem == skill && !c.Revoked);

                return new QualificationCheckRow
                {
                    CellSn = a.CellSn,
                    WoNo = a.WoNo,
                    ProcessName = a.ProcessName,
                    Equipment = a.Equipment,
                    Operator = a.Operator,
                    Shift = a.Shift,
                    Time = a.Time,
                    RequiredSkill = skill ?? "（不设持证要求）",
                    CertLevelText = cert?.Level ?? "—",
                    CertStatusText = status.Length == 0 ? "—" : status,
                    Passed = ok,
                    Result = reason
                };
            })
            .Where(r => !onlyFailed || !r.Passed)
            .OrderByDescending(r => r.Time)
            .ToList();
    }

    /// <summary>实绩资质核查汇总：核查总数 / 通过 / 不通过 / 不通过率。纯查询。</summary>
    public static (int Total, int Passed, int Failed) GetQualificationCheckSummary(string? processName = null)
    {
        var all = GetQualificationChecks(processName);
        var failed = all.Count(r => !r.Passed);
        return (all.Count, all.Count - failed, failed);
    }

    // ---------------- 人员与资质维护 ----------------

    /// <summary>
    /// 新增人员：校验 工号非空且唯一、姓名非空、部门非空、入职日期不晚于今天。
    /// 校验失败返回 (false, 原因) 且**不产生任何副作用**。
    /// </summary>
    public static (bool Ok, string Message) AddUser(UserProfile user)
    {
        if (user is null) return (false, "人员信息为空。");

        var empNo = (user.EmpNo ?? "").Trim();
        var name = (user.Name ?? "").Trim();

        if (empNo.Length == 0) return (false, "工号不能为空。");
        if (name.Length == 0) return (false, "姓名不能为空。");
        if (string.IsNullOrWhiteSpace(user.Dept)) return (false, "部门不能为空。");
        if (Users.Any(u => string.Equals(u.EmpNo, empNo, StringComparison.OrdinalIgnoreCase)))
            return (false, $"工号 {empNo} 已存在，不能重复登记。");
        if (user.HireDate > DateTime.Today) return (false, "入职日期不能晚于今天。");

        var status = EmploymentStatus.All.Contains(user.Status) ? user.Status : EmploymentStatus.Active;

        Users.Add(new UserProfile
        {
            EmpNo = empNo,
            Name = name,
            Dept = user.Dept.Trim(),
            Position = (user.Position ?? "").Trim(),
            Crew = string.IsNullOrWhiteSpace(user.Crew) ? "常白班" : user.Crew.Trim(),
            Status = status,
            Phone = (user.Phone ?? "").Trim(),
            HireDate = user.HireDate
        });

        return (true, $"人员 {name}（{empNo}）登记成功。");
    }

    /// <summary>更新人员基础信息（工号不可改）。校验同上；失败零副作用。</summary>
    public static (bool Ok, string Message) UpdateUser(UserProfile user)
    {
        if (user is null) return (false, "人员信息为空。");
        var empNo = (user.EmpNo ?? "").Trim();
        var existing = Users.FirstOrDefault(u => string.Equals(u.EmpNo, empNo, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return (false, $"未找到工号 {empNo}。");
        if (string.IsNullOrWhiteSpace(user.Name)) return (false, "姓名不能为空。");
        if (string.IsNullOrWhiteSpace(user.Dept)) return (false, "部门不能为空。");

        existing.Name = user.Name.Trim();
        existing.Dept = user.Dept.Trim();
        existing.Position = (user.Position ?? "").Trim();
        existing.Crew = string.IsNullOrWhiteSpace(user.Crew) ? "常白班" : user.Crew.Trim();
        existing.Status = EmploymentStatus.All.Contains(user.Status) ? user.Status : EmploymentStatus.Active;
        existing.Phone = (user.Phone ?? "").Trim();

        // 姓名变更需同步证书上的姓名快照，避免两处不一致
        foreach (var c in SkillCertificates.Where(c => string.Equals(c.EmpNo, empNo, StringComparison.OrdinalIgnoreCase)))
            c.EmpName = existing.Name;

        return (true, $"人员 {existing.Name}（{empNo}）信息已更新。");
    }

    /// <summary>
    /// 登记资质：校验 人员在职 / 资质项与等级合法 / 有效期不早于发证日期 / 不重复登记。
    /// 若该人同资质项存在**已吊销**记录，视为复审换证：原地恢复并写入新证书信息。
    /// 校验失败返回 (false, 原因) 且**不产生任何副作用**。
    /// </summary>
    public static (bool Ok, string Message) AddCertificate(
        string empNo, string skillItem, string level, DateTime issueDate, DateTime? expiryDate, string issuedBy)
    {
        var emp = FindUser(empNo);
        if (emp is null) return (false, $"未找到人员「{(empNo ?? "").Trim()}」，请先在人员台账登记。");
        if (!emp.IsActive) return (false, $"{emp.Name}（{emp.EmpNo}）已离职，不能登记新资质。");
        if (!SkillCatalog.All.Contains(skillItem)) return (false, $"资质项「{skillItem}」不在资质目录中。");
        if (!CertLevel.All.Contains(level)) return (false, $"资质等级「{level}」不合法。");
        if (issueDate == default) return (false, "发证日期不能为空。");
        if (expiryDate is not null && expiryDate.Value.Date < issueDate.Date)
            return (false, "有效期不能早于发证日期。");

        var existing = SkillCertificates.FirstOrDefault(c =>
            string.Equals(c.EmpNo, emp.EmpNo, StringComparison.OrdinalIgnoreCase) && c.SkillItem == skillItem);

        if (existing is not null && !existing.Revoked)
            return (false, $"{emp.Name} 已持有「{skillItem}」（{existing.Level}，{existing.Status}），请先吊销后再重新登记。");

        if (existing is not null)
        {
            // 复审换证：恢复被吊销的记录并写入新证书信息
            existing.Level = level;
            existing.CertNo = NextCertNo();
            existing.IssueDate = issueDate;
            existing.ExpiryDate = expiryDate;
            existing.IssuedBy = (issuedBy ?? "").Trim();
            existing.Revoked = false;
            existing.RevokeReason = "";
            existing.RevokeTime = null;
            return (true, $"{emp.Name} 的「{skillItem}」已复审换证（{level}）。");
        }

        SkillCertificates.Add(new SkillCertificate
        {
            EmpNo = emp.EmpNo,
            EmpName = emp.Name,
            SkillItem = skillItem,
            Level = level,
            CertNo = NextCertNo(),
            IssueDate = issueDate,
            ExpiryDate = expiryDate,
            IssuedBy = (issuedBy ?? "").Trim()
        });

        return (true, $"{emp.Name}（{emp.EmpNo}）的「{skillItem}」（{level}）登记成功。");
    }

    /// <summary>吊销资质：记录原因与时间。找不到 / 已吊销 / 原因为空 → 失败且零副作用。</summary>
    public static (bool Ok, string Message) RevokeCertificate(string empNo, string skillItem, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "吊销原因不能为空。");

        var cert = SkillCertificates.FirstOrDefault(c =>
            string.Equals(c.EmpNo, (empNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase) && c.SkillItem == skillItem);
        if (cert is null) return (false, $"未找到 {empNo} 的「{skillItem}」资质记录。");
        if (cert.Revoked) return (false, $"{cert.EmpName} 的「{skillItem}」已处于吊销状态，无需重复操作。");

        cert.Revoked = true;
        cert.RevokeReason = reason.Trim();
        cert.RevokeTime = DateTime.Now;
        // 吊销后该人若在对应工序已有实绩，核查会自动转为「不通过」，无需额外处理
        return (true, $"{cert.EmpName} 的「{skillItem}」（{cert.Level}）已吊销。原因：{cert.RevokeReason}");
    }

    /// <summary>撤销吊销（误操作纠正）：恢复为按有效期判定的状态。</summary>
    public static (bool Ok, string Message) RestoreCertificate(string empNo, string skillItem)
    {
        var cert = SkillCertificates.FirstOrDefault(c =>
            string.Equals(c.EmpNo, (empNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase) && c.SkillItem == skillItem);
        if (cert is null) return (false, $"未找到 {empNo} 的「{skillItem}」资质记录。");
        if (!cert.Revoked) return (false, $"{cert.EmpName} 的「{skillItem}」当前未吊销，无需恢复。");

        cert.Revoked = false;
        cert.RevokeReason = "";
        cert.RevokeTime = null;
        return (true, $"{cert.EmpName} 的「{skillItem}」已恢复，当前状态 {cert.Status}。");
    }

    /// <summary>新增人员的默认示例（供「新增人员」对话框初始化）。</summary>
    public static UserProfile NewUserTemplate() => new()
    {
        EmpNo = "",
        Name = "",
        Dept = "生产部",
        Position = "",
        Crew = "A班",
        Status = EmploymentStatus.Active,
        Phone = "",
        HireDate = DateTime.Today
    };

    // ============================================================
    // 客户投诉与 8D 归零
    // 定位：客诉归零是客户审核的硬要求，且可以直接复用既有的「召回反查 + 完整追溯链」能力。
    // 设计：客诉单（Complaints）+ 8D 报告（EightDReports）+ 流转履历（ComplaintHistories）。
    //       8D 顺序推进不可跳步；关闭前置校验 D3 临时围堵 / D4 根本原因 / D5 永久对策 必须齐备。
    // ============================================================

    /// <summary>客诉单号自增序号</summary>
    private static int _complaintSeq;

    /// <summary>客诉流转履历自增序号</summary>
    private static int _complaintHistorySeq;

    /// <summary>生成客诉单号，形如 CMP-yyyyMMdd-001</summary>
    private static string NextComplaintNo() => $"CMP-{DateTime.Now:yyyyMMdd}-{++_complaintSeq:000}";

    /// <summary>
    /// 客诉与 8D 种子：4 条客诉覆盖「待受理 / 处理中 / 待客户确认 / 已关闭」四种状态，
    /// 客户名取自既有出货单的往来对象，保证与出货、召回反查的数据链路自洽。
    /// </summary>
    private static void SeedComplaints()
    {
        if (Complaints.Count > 0) return;

        var today = DateTime.Today;

        // ① 已关闭（8D 八步齐备的完整闭环样本）
        var c1 = AddSeedComplaint("华能储能", "售后-王经理 138****2201", today.AddDays(-22).AddHours(9).AddMinutes(20),
            ComplaintSource.Customer, ComplaintSeverity.Critical, ComplaintCategories.CapacityLow,
            "SC-CX2026-100F", "PC-260812-L1-001", 120, "客户端分选工位反馈：整批放电容量低于规格下限 5%~8%，已停线待处理。",
            safety: false, recall: true, ComplaintStatus.Closed, "赵敏（质量部）", today.AddDays(-19));        var r1 = GetOrCreateEightD(c1.ComplaintNo);
        r1.D1Team = "组长：赵敏（质量）；成员：张伟（生产）、李娜（工艺）、王强（设备）、吴迪（供应链）";
        r1.D2Problem = "5W2H：2026 年 8 月 12 日 L1 线卷绕工序产出电芯，客户端分选容量 92~95F（规格 ≥95F）。涉及极片批次 PC-260812-L1-001，共 120 只。";
        r1.D3Containment = "① 在库同极片批次电芯全部转入待检区并冻结；② 已出货 3 张出货单全部发出召回通知；③ 客户端剩余库存加严 100% 容量复检。";
        r1.D4RootCause = "5Why 定位：卷绕张力设定值漂移 → 张力闭环未随极片厚度补偿 → 卷芯松紧不均 → 有效极片面积不足 → 容量偏低。直接原因：卷绕张力实测 2.4N（标准 2.8±0.2N）。";
        r1.D5PermanentAction = "① 卷绕机张力闭环增加厚度补偿算法（王强，已完成）；② 建立张力日点检项并纳入 TPM（王强）；③ 极片来料厚度 Cpk 纳入 SPC 监控（赵敏）。";
        r1.D6Verification = "对策实施后连续 5 批各抽 30 只复测：容量均值 99.2F，Cpk 1.42，全部合格。客户复检 120 只中 117 只合格、3 只降级为 B 档。";
        r1.D7Prevention = "① PFMEA 增加「张力闭环失效」失效模式（RPN 由 168 降至 36）；② 控制计划增加张力点检；③ 作业标准书升版至 V2.1 并完成 12 人培训。";
        r1.D8Closure = "客户于 8 月 11 日签署结案确认；团队成员表彰已完成；经验录入质量知识库编号 KB-2026-0031。";
        r1.UpdatedTime = today.AddDays(-19).AddHours(16);
        r1.UpdatedBy = "赵敏";
        c1.CustomerConfirmTime = today.AddDays(-19).AddHours(15);
        c1.CurrentStep = EightDStep.D8;
        AddComplaintHistory(c1.ComplaintNo, ComplaintAction.Confirm, EightDStep.D8, "赵敏", today.AddDays(-19).AddHours(15), "客户书面确认 8D 报告，同意结案");
        AddComplaintHistory(c1.ComplaintNo, ComplaintAction.Close, EightDStep.D8, "赵敏", today.AddDays(-19).AddHours(16), "8D 归零完成，客诉关闭");

        // ② 处理中（D1~D3 已填，正卡在根因分析；已超期，用于演示时限预警）
        //    注：客户名与关联批次均取自系统内真实可追溯数据（客户取自定义客户，批次取真实极片批号）
        var c2 = AddSeedComplaint("中车时代电气", "品质部-李工 139****3302", today.AddDays(-12).AddHours(14),
            ComplaintSource.AfterSales, ComplaintSeverity.Major, ComplaintCategories.EsrHigh,
            "SC-CX2026-100F", "PC-260813-L2-002", 45, "客户端老化测试发现内阻偏高（均值 12.8mΩ，规格 ≤10mΩ），比例约 8%。",
            safety: false, recall: false, ComplaintStatus.Investigating, "何静（质量部）", today.AddDays(-5));
        var r2 = GetOrCreateEightD(c2.ComplaintNo);
        r2.D1Team = "组长：何静（质量）；成员：郑爽（注液）、孙磊（封口）、杨帆（测试）";
        r2.D2Problem = "5W2H：交付中车时代电气的 100F 产品，极片批次 PC-260813-L2-002，抽检 45 只，内阻 11.2~13.6mΩ 超标。";
        r2.D3Containment = "① 冻结在库同极片批次电芯；② 客户端剩余 260 只加严内阻全检；③ 库存同型号产品暂停发货。";
        r2.UpdatedTime = today.AddDays(-8).AddHours(10);
        r2.UpdatedBy = "何静";
        c2.CurrentStep = EightDStep.D4;

        // ③ 待客户确认（D1~D7 已填，等待客户签署；关联范围取真实出货电芯 SN）
        var c3 = AddSeedComplaint("阳光电源", "采购部-周经理 137****4403", today.AddDays(-8).AddHours(10).AddMinutes(30),
            ComplaintSource.Customer, ComplaintSeverity.Major, ComplaintCategories.Appearance,
            "SC-CX2026-100F", "SC-A260812-L1-00001", 30, "来料检验发现 30 只电芯外壳顶部有轻微划痕，要求给出围堵与改善措施。",
            safety: false, recall: false, ComplaintStatus.WaitingCustomer, "王芳（质量部）", today.AddDays(-1));
        var r3 = GetOrCreateEightD(c3.ComplaintNo);
        r3.D1Team = "组长：王芳（质量）；成员：孙磊（封口）、陈晨（卷绕）、吴迪（仓储）";
        r3.D2Problem = "5W2H：交付阳光电源的 30 只电芯，客户来料检验发现顶部环向划痕，划痕深度 ≤0.05mm，未伤及密封结构。";
        r3.D3Containment = "① 客户端 30 只全部换货，已发出替代品；② 在库同批成品 100% 外观全检；③ 包装内衬增加 PE 垫片。";
        r3.D4RootCause = "5Why 定位：成品周转箱隔板局部变形 → 电芯在运输中相互摩擦 → 顶部环向划痕。直接原因：周转箱隔板累计使用超期未更换。";
        r3.D5PermanentAction = "① 周转箱隔板更换周期由 24 个月缩短至 12 个月并纳入 TPM 点检（王强）；② 包装作业标准增加内衬检查项；③ 出货前外观抽检比例由 5% 提至 20%。";
        r3.D6Verification = "改善后连续 3 批出货抽检各 60 只，外观不良 0 只。";
        r3.D7Prevention = "PFMEA 增加「周转容器磨损致外观不良」失效模式；《成品包装作业指导书》升版 V1.3 并培训 8 人。";
        r3.UpdatedTime = today.AddDays(-2).AddHours(17);
        r3.UpdatedBy = "王芳";
        c3.CurrentStep = EightDStep.D8;

        // ④ 待受理（刚收到，尚未组建团队；未登记关联范围 → 反查自动回落到按客户名）
        var c4 = AddSeedComplaint("华能储能", "售后-王经理 138****2201", today.AddHours(8).AddMinutes(40),
            ComplaintSource.Customer, ComplaintSeverity.Minor, ComplaintCategories.Dimension,
            "SC-CX2026-300F", "", 6, "客户反馈 6 只电芯外径尺寸接近上限，装配时偶发卡滞，请求评估。",
            safety: false, recall: false, ComplaintStatus.Pending, "", today.AddDays(15));
        GetOrCreateEightD(c4.ComplaintNo);
        c4.CurrentStep = EightDStep.D1;
    }

    /// <summary>种子辅助：建一条客诉单并写入登记履历（返回单对象以便继续填 8D）。</summary>
    private static CustomerComplaint AddSeedComplaint(
        string customer, string contact, DateTime receiveTime, string source, string severity, string category,
        string productModel, string lotKeyword, double quantity, string symptom,
        bool safety, bool recall, string status, string owner, DateTime dueDate)
    {
        var no = NextComplaintNo();
        var c = new CustomerComplaint
        {
            ComplaintNo = no,
            CustomerName = customer,
            ContactInfo = contact,
            ReceiveTime = receiveTime,
            Source = source,
            Severity = severity,
            Category = category,
            ProductModel = productModel,
            LotKeyword = lotKeyword,
            Quantity = quantity,
            SymptomDesc = symptom,
            IsSafetyInvolved = safety,
            NeedRecall = recall,
            Status = status,
            Owner = owner,
            DueDate = dueDate
        };
        if (ComplaintStatus.IsClosed(status))
        {
            c.ClosedTime = receiveTime.AddDays(3);
            c.ClosedBy = owner.Length > 0 ? owner : "质量工程师";
        }
        Complaints.Add(c);
        AddComplaintHistory(no, ComplaintAction.Create, EightDStep.D1, "客服", receiveTime,
            $"登记客诉：{customer} / {category} / {quantity:0.##} 只；来源 {source}；严重度 {severity}");
        return c;
    }

    /// <summary>种子辅助：取或建 8D 报告。</summary>
    private static EightDReport GetOrCreateEightD(string complaintNo)
    {
        var exist = EightDReports.FirstOrDefault(r =>
            string.Equals(r.ComplaintNo, complaintNo, StringComparison.OrdinalIgnoreCase));
        if (exist is not null) return exist;

        var report = new EightDReport
        {
            ComplaintNo = complaintNo,
            CreatedTime = DateTime.Now,
            UpdatedTime = DateTime.Now,
            UpdatedBy = ""
        };
        EightDReports.Add(report);
        return report;
    }

    /// <summary>追加一条客诉流转履历（内部使用）。</summary>
    private static void AddComplaintHistory(string complaintNo, string action, string step, string @operator,
        DateTime time, string remark, int cellCount = 0, string snPreview = "")
    {
        ComplaintHistories.Add(new ComplaintHistory
        {
            Seq = ++_complaintHistorySeq,
            ComplaintNo = complaintNo,
            ActionType = action,
            Step = step,
            Operator = @operator,
            Time = time,
            Remark = remark,
            CellCount = cellCount,
            SnPreview = snPreview
        });
    }

    // ---------------- 客诉查询 ----------------

    /// <summary>客诉台账查询：状态可选、关键字匹配 单号/客户/型号/分类/症状/责任人；按接收时间倒序。</summary>
    public static List<CustomerComplaint> QueryComplaints(string? status = null, string? keyword = null)
    {
        var kw = (keyword ?? "").Trim();
        return Complaints
            .Where(c => status is null || c.Status == status)
            .Where(c => kw.Length == 0
                        || c.ComplaintNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || c.CustomerName.Contains(kw, StringComparison.Ordinal)
                        || c.ProductModel.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || c.Category.Contains(kw, StringComparison.Ordinal)
                        || c.SymptomDesc.Contains(kw, StringComparison.Ordinal)
                        || c.Owner.Contains(kw, StringComparison.Ordinal))
            .OrderByDescending(c => c.ReceiveTime)
            .ToList();
    }

    /// <summary>按单号精确查找客诉（忽略大小写）。</summary>
    public static CustomerComplaint? FindComplaint(string? complaintNo)
    {
        var no = (complaintNo ?? "").Trim();
        if (no.Length == 0) return null;
        return Complaints.FirstOrDefault(c => string.Equals(c.ComplaintNo, no, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>取某客诉的 8D 报告（不存在返回 null）。</summary>
    public static EightDReport? GetEightD(string? complaintNo)
    {
        var no = (complaintNo ?? "").Trim();
        if (no.Length == 0) return null;
        return EightDReports.FirstOrDefault(r => string.Equals(r.ComplaintNo, no, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>取某客诉的流转履历（按序号升序）。</summary>
    public static List<ComplaintHistory> GetComplaintHistories(string? complaintNo)
    {
        var no = (complaintNo ?? "").Trim();
        if (no.Length == 0) return new List<ComplaintHistory>();
        return ComplaintHistories
            .Where(h => string.Equals(h.ComplaintNo, no, StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => h.Seq)
            .ToList();
    }

    /// <summary>客诉汇总：待受理 / 处理中 / 待客户确认 / 已关闭 / 超期未关闭。纯查询。</summary>
    public static (int Pending, int Investigating, int WaitingCustomer, int Closed, int Overdue) GetComplaintSummary()
    {
        return (Complaints.Count(c => c.Status == ComplaintStatus.Pending),
            Complaints.Count(c => c.Status == ComplaintStatus.Investigating),
            Complaints.Count(c => c.Status == ComplaintStatus.WaitingCustomer),
            Complaints.Count(c => c.Status == ComplaintStatus.Closed),
            Complaints.Count(c => !c.IsClosed && c.DueHealth == "已超期"));
    }

    /// <summary>按客户聚合客诉统计（总数 / 严重 / 未关闭 / 已关闭 / 超期），按总数倒序。纯查询。</summary>
    public static List<ComplaintStatRow> GetComplaintStats() =>
        Complaints
            .GroupBy(c => c.CustomerName, StringComparer.Ordinal)
            .Select(g => new ComplaintStatRow
            {
                CustomerName = g.Key,
                Total = g.Count(),
                CriticalCount = g.Count(c => c.Severity == ComplaintSeverity.Critical),
                OpenCount = g.Count(c => !c.IsClosed),
                ClosedCount = g.Count(c => c.IsClosed),
                OverdueCount = g.Count(c => !c.IsClosed && c.DueHealth == "已超期")
            })
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.CustomerName, StringComparer.Ordinal)
            .ToList();

    // ---------------- 客诉登记与流转 ----------------

    /// <summary>
    /// 登记客诉：校验 客户名称、症状描述、数量、严重度/分类/来源合法性；单号与时限由系统生成。
    /// 同时创建空的 8D 报告并写入登记履历。校验失败返回 (false, 原因) 且**零副作用**。
    /// </summary>
    public static (bool Ok, string Message, string ComplaintNo) CreateComplaint(CustomerComplaint input)
    {
        if (input is null) return (false, "客诉信息为空。", "");
        if (string.IsNullOrWhiteSpace(input.CustomerName)) return (false, "客户名称为必填项。", "");
        if (string.IsNullOrWhiteSpace(input.SymptomDesc)) return (false, "客户描述的现象为必填项。", "");
        if (input.Quantity <= 0) return (false, "客诉数量必须大于 0。", "");
        if (!ComplaintSeverity.All.Contains(input.Severity)) return (false, $"严重度「{input.Severity}」不合法。", "");
        if (!ComplaintCategories.All.Contains(input.Category)) return (false, $"症状分类「{input.Category}」不合法。", "");
        if (!ComplaintSource.All.Contains(input.Source)) return (false, $"来源「{input.Source}」不合法。", "");
        if (input.ReceiveTime == default) return (false, "接收时间不能为空。", "");
        if (input.ReceiveTime > DateTime.Now.AddMinutes(1)) return (false, "接收时间不能晚于当前时间。", "");

        var no = NextComplaintNo();
        var c = new CustomerComplaint
        {
            ComplaintNo = no,
            CustomerName = input.CustomerName.Trim(),
            ContactInfo = (input.ContactInfo ?? "").Trim(),
            ReceiveTime = input.ReceiveTime,
            Source = input.Source,
            Severity = input.Severity,
            Category = input.Category,
            ProductModel = (input.ProductModel ?? "").Trim(),
            LotKeyword = (input.LotKeyword ?? "").Trim(),
            Quantity = input.Quantity,
            SymptomDesc = input.SymptomDesc.Trim(),
            IsSafetyInvolved = input.IsSafetyInvolved,
            NeedRecall = input.NeedRecall || input.IsSafetyInvolved,   // 涉安全一律标记需召回评估
            Status = ComplaintStatus.Pending,
            Owner = (input.Owner ?? "").Trim(),
            DueDate = input.DueDate == default
                ? input.ReceiveTime.Date.AddDays(ComplaintSeverity.ResponseDays(input.Severity))
                : input.DueDate,
            CurrentStep = EightDStep.D1
        };
        Complaints.Add(c);
        GetOrCreateEightD(no);
        AddComplaintHistory(no, ComplaintAction.Create, EightDStep.D1,
            c.Owner.Length > 0 ? c.Owner : "客服", DateTime.Now,
            $"登记客诉：{c.CustomerName} / {c.Category} / {c.Quantity:0.##} 只；来源 {c.Source}；严重度 {c.Severity}"
            + (c.IsSafetyInvolved ? "；【涉安全】" : "")
            + (c.NeedRecall ? "；需召回评估" : ""));

        return (true, $"客诉 {no} 登记成功（要求 {c.DueText} 前回复客户）。", no);
    }

    /// <summary>
    /// 推进 8D：把内容写入**当前步骤**，然后前进到下一步。
    /// 规则：客诉存在 / 未关闭 / 内容非空 / 未越过 D8；写完后 D8 → 状态转「待客户确认」，其余 → 状态转「处理中」。
    /// 失败返回 (false, 原因) 且零副作用。
    /// </summary>
    public static (bool Ok, string Message) AdvanceEightD(string complaintNo, string content, string @operator)
    {
        var c = FindComplaint(complaintNo);
        if (c is null) return (false, $"未找到客诉 {complaintNo}。");
        if (c.IsClosed) return (false, $"客诉 {c.ComplaintNo} 已关闭，不能再推进 8D。");
        if (string.IsNullOrWhiteSpace(content)) return (false, $"「{c.CurrentStep}」内容不能为空。");

        var report = GetEightD(c.ComplaintNo);
        if (report is null) return (false, $"客诉 {c.ComplaintNo} 缺少 8D 报告，数据异常。");

        var step = c.CurrentStep;
        var next = EightDStep.Next(step);
        // 末步（D8）已提交过则视为 8D 齐备，不允许重复提交覆盖结案内容；如需改内容走「修订」
        if (next is null && !string.IsNullOrWhiteSpace(report.ContentOf(step)))
            return (false, $"「{step}」已提交，8D 八步已齐备，等待客户确认；如需修改请使用「修订」。");

        if (!report.SetContentOf(step, content.Trim()))
            return (false, $"步骤「{step}」不合法，无法写入。");

        report.UpdatedTime = DateTime.Now;
        report.UpdatedBy = (@operator ?? "").Trim();

        var statusBefore = c.Status;
        if (next is null)
        {
            // D8 完成：8D 归零完成，等待客户确认
            c.Status = ComplaintStatus.WaitingCustomer;
        }
        else
        {
            c.CurrentStep = next;
            if (c.Status == ComplaintStatus.Pending) c.Status = ComplaintStatus.Investigating;
        }

        AddComplaintHistory(c.ComplaintNo, ComplaintAction.Advance, step, report.UpdatedBy, DateTime.Now,
            $"完成「{step}」并推进{(next is null ? "至结案（等待客户确认）" : $"到「{next}」")}"
            + (statusBefore != c.Status ? $"；状态 {statusBefore} → {c.Status}" : ""));

        return (true, next is null
            ? $"「{step}」已提交，8D 八步齐备，客诉 {c.ComplaintNo} 转为「{c.Status}」。"
            : $"「{step}」已提交，当前推进到「{next}」。");
    }

    /// <summary>
    /// 修订已填写过的 8D 步骤内容（不改变当前进度）。
    /// 规则：不允许修订尚未推进到的步骤（防止跳步写入）；内容非空；客诉未关闭。
    /// </summary>
    public static (bool Ok, string Message) UpdateEightDStep(string complaintNo, string step, string content, string @operator)
    {
        var c = FindComplaint(complaintNo);
        if (c is null) return (false, $"未找到客诉 {complaintNo}。");
        if (c.IsClosed) return (false, $"客诉 {c.ComplaintNo} 已关闭，8D 已锁定不可修改。");
        if (string.IsNullOrWhiteSpace(content)) return (false, "内容不能为空。");

        var targetIdx = EightDStep.IndexOf(step);
        var currentIdx = EightDStep.IndexOf(c.CurrentStep);
        if (targetIdx < 0) return (false, $"步骤「{step}」不合法。");
        if (targetIdx > currentIdx) return (false, $"「{step}」尚未推进到，不能提前填写（当前：{c.CurrentStep}）。");

        var report = GetEightD(c.ComplaintNo);
        if (report is null) return (false, $"客诉 {c.ComplaintNo} 缺少 8D 报告，数据异常。");
        if (!report.SetContentOf(step, content.Trim())) return (false, $"步骤「{step}」不合法。");

        report.UpdatedTime = DateTime.Now;
        report.UpdatedBy = (@operator ?? "").Trim();
        AddComplaintHistory(c.ComplaintNo, ComplaintAction.EditStep, step, report.UpdatedBy, DateTime.Now,
            $"修订「{step}」内容");

        return (true, $"「{step}」已修订。");
    }

    /// <summary>
    /// 客户确认：要求 8D 八步齐备；确认后状态转「待客户确认」并记录确认时间。
    /// 已确认过 / 8D 未齐备 / 客诉已关闭 → 失败且零副作用。
    /// </summary>
    public static (bool Ok, string Message) ConfirmByCustomer(string complaintNo, string remark, string @operator = "")
    {
        var c = FindComplaint(complaintNo);
        if (c is null) return (false, $"未找到客诉 {complaintNo}。");
        if (c.IsClosed) return (false, $"客诉 {c.ComplaintNo} 已关闭，无需重复确认。");
        if (c.CustomerConfirmTime is not null) return (false, $"客诉 {c.ComplaintNo} 已记录客户确认，无需重复操作。");

        var report = GetEightD(c.ComplaintNo);
        if (report is null) return (false, $"客诉 {c.ComplaintNo} 缺少 8D 报告，数据异常。");
        if (!report.IsComplete)
            return (false, $"8D 尚未齐备（已完成 {report.CompletionText}），不能提交客户确认。"
                           + $"待填：{string.Join("、", EightDStep.Order.Where(s => string.IsNullOrWhiteSpace(report.ContentOf(s))))}");

        c.CustomerConfirmTime = DateTime.Now;
        c.Status = ComplaintStatus.WaitingCustomer;
        c.CurrentStep = EightDStep.D8;
        AddComplaintHistory(c.ComplaintNo, ComplaintAction.Confirm, EightDStep.D8,
            (@operator ?? "").Trim(), DateTime.Now,
            remark.Length == 0 ? "客户确认 8D 报告" : $"客户确认：{remark}");

        return (true, $"客诉 {c.ComplaintNo} 已记录客户确认。");
    }

    /// <summary>
    /// 关闭客诉（8D 归零完成）。
    /// 前置校验：D3 临时围堵 / D4 根本原因 / D5 永久对策 必须有内容（客户审核关注项）；
    /// 涉安全客诉还要求 NeedRecall 已评估且已记录客户确认。失败零副作用。
    /// </summary>
    public static (bool Ok, string Message) CloseComplaint(string complaintNo, string @operator, string remark)
    {
        var c = FindComplaint(complaintNo);
        if (c is null) return (false, $"未找到客诉 {complaintNo}。");
        if (c.IsClosed) return (false, $"客诉 {c.ComplaintNo} 已关闭，无需重复操作。");

        var report = GetEightD(c.ComplaintNo);
        if (report is null) return (false, $"客诉 {c.ComplaintNo} 缺少 8D 报告，数据异常。");

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(report.D3Containment)) missing.Add(EightDStep.D3);
        if (string.IsNullOrWhiteSpace(report.D4RootCause)) missing.Add(EightDStep.D4);
        if (string.IsNullOrWhiteSpace(report.D5PermanentAction)) missing.Add(EightDStep.D5);
        if (missing.Count > 0)
            return (false, $"关闭前必须完成：{string.Join("、", missing)}（客户审核关注项）。");

        if (c.IsSafetyInvolved && c.CustomerConfirmTime is null)
            return (false, "涉安全客诉关闭前必须记录客户确认。");

        c.Status = ComplaintStatus.Closed;
        c.ClosedTime = DateTime.Now;
        c.ClosedBy = (@operator ?? "").Trim();
        AddComplaintHistory(c.ComplaintNo, ComplaintAction.Close, c.CurrentStep, c.ClosedBy, DateTime.Now,
            remark.Length == 0 ? "8D 归零完成，客诉关闭" : remark);

        return (true, $"客诉 {c.ComplaintNo} 已关闭。");
    }

    // ---------------- 与召回反查 / 冻结的联动 ----------------

    /// <summary>
    /// 由客诉关联范围推断召回反查方式：电芯SN → 出货单号 → 材料批次 → 客户。
    /// 依据「该关键字在本系统数据中真的能命中什么」来决定，避免误判。
    /// </summary>
    public static string DetectRecallMode(string? keyword)
    {
        var kw = (keyword ?? "").Trim();
        if (kw.Length == 0) return RecallMode.MaterialLot;

        if (Cells.Any(c => string.Equals(c.Sn, kw, StringComparison.OrdinalIgnoreCase))) return RecallMode.CellSn;
        if (WmsOrders.Any(o => string.Equals(o.OrderNo, kw, StringComparison.OrdinalIgnoreCase))) return RecallMode.ShipmentOrder;
        if (Cells.Any(c => string.Equals(c.ElectrodeLot, kw, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(c.ElectrolyteLot, kw, StringComparison.OrdinalIgnoreCase)))
            return RecallMode.MaterialLot;
        if (Complaints.Any(c => string.Equals(c.CustomerName, kw, StringComparison.Ordinal))
            || WmsOrders.Any(o => string.Equals(o.Partner, kw, StringComparison.Ordinal)))
            return RecallMode.Customer;

        return RecallMode.MaterialLot;
    }

    /// <summary>
    /// 客诉触发范围反查：直接复用召回反查能力，返回受影响出货/电芯/批次结果。
    /// 关联范围为空时：若客诉有客户名则按客户反查，否则返回空结果并给出说明。纯查询。
    /// </summary>
    public static RecallQueryResult GetComplaintImpact(string? complaintNo)
    {
        var c = FindComplaint(complaintNo);
        if (c is null)
            return new RecallQueryResult { Mode = "", Keyword = "", Message = $"未找到客诉 {complaintNo}。" };

        var keyword = c.LotKeyword;
        if (string.IsNullOrWhiteSpace(keyword)) keyword = c.CustomerName;
        if (string.IsNullOrWhiteSpace(keyword))
            return new RecallQueryResult { Mode = "", Keyword = "", Message = "该客诉未登记关联范围（批次 / 工单 / SN / 出货单）与客户名，无法反查。" };

        var mode = DetectRecallMode(keyword);
        var result = RecallQuery(mode, keyword);
        if (result.Message.Length == 0)
            result.Message = $"按「{mode}」反查命中：受影响电芯 {result.Summary.CellCount} 只（已出货 {result.Summary.ShippedCellCount} 只）。";
        return result;
    }

    /// <summary>
    /// 客诉触发冻结：把「可冻结电芯」转入保留单（复用召回冻结能力），并回填客诉的关联保留单号与履历。
    /// 无可冻结电芯 / 客诉不存在 → 失败且零副作用。
    /// </summary>
    public static (bool Ok, string Message, string HoldNo, int Held) FreezeComplaintImpact(
        string complaintNo, string @operator, string reason)
    {
        var c = FindComplaint(complaintNo);
        if (c is null) return (false, $"未找到客诉 {complaintNo}。", "", 0);

        var impact = GetComplaintImpact(c.ComplaintNo);
        if (impact.Summary.CellCount == 0)
            return (false, $"客诉 {c.ComplaintNo} 未反查到受影响的电芯，无法冻结。", "", 0);

        var freezable = impact.Cells.Where(x => !x.Shipped && !x.Held).Select(x => x.CellSn).ToList();
        if (freezable.Count == 0)
            return (false, $"客诉 {c.ComplaintNo} 影响范围内无「未出货且未在保留中」的可冻结电芯。", "", 0);

        var (ok, msg, holdNo, held, _) = FreezeRecallCells(freezable, @operator);
        if (!ok) return (false, msg, "", 0);

        c.RelatedHoldNo = holdNo;
        c.NeedRecall = true;
        if (c.Status == ComplaintStatus.Pending) c.Status = ComplaintStatus.Investigating;

        // 同步推进 8D 到至少 D3（临时围堵已实际执行）
        var report = GetEightD(c.ComplaintNo);
        if (report is not null && string.IsNullOrWhiteSpace(report.D3Containment))
        {
            report.D3Containment = $"自动化围堵：冻结影响范围电芯 {held} 只（保留单 {holdNo}）；原因：{reason}";
            report.UpdatedTime = DateTime.Now;
            report.UpdatedBy = (@operator ?? "").Trim();
        }

        AddComplaintHistory(c.ComplaintNo, ComplaintAction.Freeze, EightDStep.D3, (@operator ?? "").Trim(), DateTime.Now,
            $"按「{impact.Mode}」反查并冻结电芯 {held} 只，保留单 {holdNo}；原因：{reason}",
            held, string.Join("、", freezable.Take(5)));

        return (true, $"已冻结 {held} 只电芯，保留单 {holdNo}。", holdNo, held);
    }
}

