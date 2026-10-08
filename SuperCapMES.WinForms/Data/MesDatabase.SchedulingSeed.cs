using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 智能排产的种子数据：历史工单 + 对应完工报工
//
// 为什么必须补这批数据：
//   排产推理要从历史工单里统计"良率"和"单位工时"。原有 4 张工单里每个型号只有 1 张有产出、
//   且全库只有 1 张报过工时——样本量远低于门槛（SchedulingRules.MinSampleCount = 3），
//   推理结论会全部退回"样本不足，采用默认值"，拿去做决策没有说服力。
//
// 数据是"算得出来"的，不是随手编的：
//   · 计划数 = 完工数 + 不良数（已完成工单应把计划跑完，口径自洽）
//   · 工时   = 完工数 × 该型号节拍（100F ≈ 7.5 秒/只、300F ≈ 12 秒/只、50F ≈ 6 秒/只），
//              于是反算出来的"单位工时"正好落在真实区间内
//   · 良率   落在 97.6%~98.8%，三个型号各有差异（300F 更慢也更易出不良），符合工艺直觉
//   · 线体   刻意不均衡（100F 偏 L1、300F 全在 L2、50F 以 L1 为主），
//            这样"推荐线体"才有依据可循，而不是随便挑一条
//   · 每张已完成工单都配一条完工报工记录：否则 WorkHours 有值却没有报工单，语义自相矛盾，
//     且工单列表的"已报工"着色会与报工页对不上。
//
// ⚠ 追加位置：必须 AddRange 到 WorkOrders **末尾**。
//   ModuleAssembleForm 与 MainForm.Feeding 用 `MesDatabase.WorkOrders[index]` 与
//   ComboBox 条目按索引配对（两处都是无过滤的顺序枚举），插到中间会整体错位取到别的工单。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>
    /// 历史工单种子：12 张 2026-08 上旬的已完工工单，为排产推理提供足量样本。
    /// 在 <see cref="SeedErp"/> 之后调用——那里已初始化报工序号，且已建立 PO↔工单关联。
    /// </summary>
    private static void SeedSchedulingHistory()
    {
        if (WorkOrders.Count > 4) return; // 幂等：已播过就直接返回
        var today = DateTime.Today;

        // ---- 历史工单（追加在末尾，不打乱既有 4 张的顺序） ----
        WorkOrders.AddRange(new[]
        {
            // ===== SC-CX2026-100F：节拍约 7.5 秒/只，良率约 98.8%，主力线 L1 =====
            new WorkOrder { WoNo = "WO-20260801-005", ProductModel = "SC-CX2026-100F", PlanQty = 5976, FinishedQty = 5904, NgQty = 72, Status = "已完成", Line = "L1", StartDate = new DateTime(2026,8,1,8,0,0), DueDate = new DateTime(2026,8,3,20,0,0) },
            new WorkOrder { WoNo = "WO-20260803-006", ProductModel = "SC-CX2026-100F", PlanQty = 5011, FinishedQty = 4950, NgQty = 61, Status = "已完成", Line = "L1", StartDate = new DateTime(2026,8,3,8,0,0), DueDate = new DateTime(2026,8,5,20,0,0) },
            new WorkOrder { WoNo = "WO-20260805-007", ProductModel = "SC-CX2026-100F", PlanQty = 6975, FinishedQty = 6880, NgQty = 95, Status = "已完成", Line = "L2", StartDate = new DateTime(2026,8,5,8,0,0), DueDate = new DateTime(2026,8,7,20,0,0) },
            new WorkOrder { WoNo = "WO-20260807-008", ProductModel = "SC-CX2026-100F", PlanQty = 3164, FinishedQty = 3120, NgQty = 44, Status = "已关闭", Line = "L1", StartDate = new DateTime(2026,8,7,8,0,0), DueDate = new DateTime(2026,8,9,20,0,0) },

            // ===== SC-CX2026-300F：节拍约 12 秒/只，良率略低，全程在 L2 =====
            new WorkOrder { WoNo = "WO-20260801-010", ProductModel = "SC-CX2026-300F", PlanQty = 2485, FinishedQty = 2430, NgQty = 55, Status = "已完成", Line = "L2", StartDate = new DateTime(2026,8,1,8,0,0), DueDate = new DateTime(2026,8,4,20,0,0) },
            new WorkOrder { WoNo = "WO-20260804-011", ProductModel = "SC-CX2026-300F", PlanQty = 3015, FinishedQty = 2952, NgQty = 63, Status = "已完成", Line = "L2", StartDate = new DateTime(2026,8,4,8,0,0), DueDate = new DateTime(2026,8,7,20,0,0) },
            new WorkOrder { WoNo = "WO-20260806-012", ProductModel = "SC-CX2026-300F", PlanQty = 1962, FinishedQty = 1915, NgQty = 47, Status = "已完成", Line = "L2", StartDate = new DateTime(2026,8,6,8,0,0), DueDate = new DateTime(2026,8,8,20,0,0) },
            new WorkOrder { WoNo = "WO-20260808-013", ProductModel = "SC-CX2026-300F", PlanQty = 1800, FinishedQty = 0, NgQty = 0, Status = "已关闭", Line = "L2", StartDate = new DateTime(2026,8,8,8,0,0), DueDate = new DateTime(2026,8,12,20,0,0) },

            // ===== SC-CX2026-50F：节拍约 6 秒/只，良率最好，兼有 L3 试产 =====
            new WorkOrder { WoNo = "WO-20260802-014", ProductModel = "SC-CX2026-50F", PlanQty = 9078, FinishedQty = 8970, NgQty = 108, Status = "已完成", Line = "L1", StartDate = new DateTime(2026,8,2,8,0,0), DueDate = new DateTime(2026,8,5,20,0,0) },
            new WorkOrder { WoNo = "WO-20260804-015", ProductModel = "SC-CX2026-50F", PlanQty = 7523, FinishedQty = 7432, NgQty = 91, Status = "已完成", Line = "L3", StartDate = new DateTime(2026,8,4,8,0,0), DueDate = new DateTime(2026,8,6,20,0,0) },
            new WorkOrder { WoNo = "WO-20260806-016", ProductModel = "SC-CX2026-50F", PlanQty = 5864, FinishedQty = 5790, NgQty = 74, Status = "已完成", Line = "L1", StartDate = new DateTime(2026,8,6,8,0,0), DueDate = new DateTime(2026,8,8,20,0,0) },
        });

        // ---- 完工报工：逐张配套，让 WorkHours / 已报工着色与报工页对得上 ----
        // 工时 = 完工数 × 节拍（100F 7.5~7.9 秒、300F 12 秒、50F 6.0~6.1 秒）
        var reports = new (string WoNo, double Hours, string Operator)[]
        {
            ("WO-20260801-005", 12.30, "王强"),
            ("WO-20260803-006", 10.31, "王强"),
            ("WO-20260805-007", 14.91, "刘洋"),
            ("WO-20260807-008",  6.85, "刘洋"),
            ("WO-20260801-010",  8.10, "陈晓东"),
            ("WO-20260804-011",  9.84, "陈晓东"),
            ("WO-20260806-012",  6.38, "李娜"),
            ("WO-20260802-014", 14.95, "张伟"),
            ("WO-20260804-015", 12.59, "张伟"),
            ("WO-20260806-016",  9.81, "李娜"),
        };

        foreach (var (woNo, hours, op) in reports)
        {
            var wo = WorkOrders.FirstOrDefault(w => w.WoNo == woNo);
            if (wo is null || wo.FinishedQty <= 0) continue;

            // 报工时间取该工单开工后一天的收班时刻（比 DueDate 早，符合"完工即报"）
            var reportTime = wo.StartDate.AddDays(1).AddHours(10);
            _erpReportSeq++;
            ErpWorkReports.Add(new ErpWorkReport
            {
                ReportNo = $"RPT-{reportTime:yyyyMMdd}-{_erpReportSeq:000}",
                WoNo = wo.WoNo,
                ErpOrderNo = wo.ErpOrderNo,
                OutputQty = wo.PlanQty,
                GoodQty = wo.FinishedQty,
                NgQty = wo.NgQty,
                WorkHours = hours,
                Operator = op,
                ReportTime = reportTime,
                ErpStatus = ErpUploadStatus.Success,
                ErpReceipt = $"ACK-{_erpReportSeq:000000}",
                Remark = "完工报工（历史数据）"
            });

            wo.ErpReportStatus = ErpReportStatus.Reported;
            wo.ErpReportTime = reportTime;
            wo.WorkHours = hours;
        }

        // WO-20260808-013（订单取消未开工）与 WO-20260807-008（客户变更关闭）不生成报工，
        // 它们代表"没做完就停了"的真实情形；其中 013 无产出，不进入任何统计样本。

        // ---- 加急 PO：覆盖"偏紧"与"来不及"两种交期风险，供排产页演示 ----
        // 交期 = 今天+2 天 → 按历史节拍算下来余量不足 1 天，落在「偏紧」
        // 交期 = 今天+1 天 → 现有产能赶不上，会触发「改排到别的线 / 补多少小时产能」的处置建议
        ErpPos.AddRange(new[]
        {
            new ErpPo
            {
                PoNo = "PO-2026-0914-007", Customer = "国网系储能集成商", ProductModel = "SC-CX2026-100F",
                ProductName = "超级电容单体 100F/2.7V", OrderQty = 4000,
                DueDate = today.AddDays(2), Status = ErpPoStatus.Pending, Remark = "常规追加，交付窗口偏紧"
            },
            new ErpPo
            {
                PoNo = "PO-2026-0914-008", Customer = "轨交牵引装备厂", ProductModel = "SC-CX2026-100F",
                ProductName = "超级电容单体 100F/2.7V", OrderQty = 6500,
                DueDate = today.AddDays(1), Status = ErpPoStatus.Pending, Remark = "加急插单：客户项目节点提前，需优先保障"
            },
        });
    }
}
