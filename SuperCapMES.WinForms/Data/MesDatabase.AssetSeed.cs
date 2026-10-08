using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 生产资产种子 —— 12 项资产 + 初始占用记录
//
// 资产是刻意设计成"有约束冲突"的，否则占用校验看不出效果：
//   · SEAL-L1-01 处于**维护**状态 → 演示"资产不可分配"（硬冲突，与时间冲突分开说）
//   · WIND-L1-01 是 L1 唯一在用卷绕机 → 新单排 L1 会撞车，能演示"错排"与"替代资产"
//   · INJ-L1-02 只做 100F/50F，INJ-L2-01（大机）只做 300F → 演示"型号能力不匹配"
//   · FORM-A01 容量 2（化成可并行）→ 演示"余量不足但不冲突"
//
// 初始占用用**相对当前时间**的滚动窗口（today-1 ~ today+2 / today+3），
// 而不是写死日期——写死的日期过了就失效，演示时看不到冲突。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>生产资产与初始占用种子（幂等）。</summary>
    private static void SeedAssets()
    {
        if (ProductionAssets.Count > 0) return;

        ProductionAssets.AddRange(new[]
        {
            // ---- 卷绕 ----
            new ProductionAsset { Code = "WIND-L1-01", Name = "卷绕机", Type = AssetTypes.Equipment, Section = "电芯成型", Process = "卷绕", Line = "L1", Capacity = 1, Status = "在用", Models = "SC-CX2026-100F,SC-CX2026-50F" },
            new ProductionAsset { Code = "WIND-L2-01", Name = "卷绕机", Type = AssetTypes.Equipment, Section = "电芯成型", Process = "卷绕", Line = "L2", Capacity = 1, Status = "在用", Models = "SC-CX2026-100F,SC-CX2026-300F,SC-CX2026-50F" },
            new ProductionAsset { Code = "WIND-L3-01", Name = "卷绕机", Type = AssetTypes.Equipment, Section = "电芯成型", Process = "卷绕", Line = "L3", Capacity = 1, Status = "备用", Models = "SC-CX2026-100F,SC-CX2026-50F" },

            // ---- 入壳（工装类） ----
            new ProductionAsset { Code = "TOL-CASE-01", Name = "入壳工装", Type = AssetTypes.Tooling, Section = "电芯成型", Process = "入壳", Line = "", Capacity = 1, Status = "在用", Models = "" },
            new ProductionAsset { Code = "TOL-CASE-02", Name = "入壳工装", Type = AssetTypes.Tooling, Section = "电芯成型", Process = "入壳", Line = "", Capacity = 1, Status = "在用", Models = "" },

            // ---- 注液（型号能力差异） ----
            new ProductionAsset { Code = "INJ-L1-02", Name = "注液机", Type = AssetTypes.Equipment, Section = "注液封口", Process = "注液", Line = "L1", Capacity = 1, Status = "在用", Models = "SC-CX2026-100F,SC-CX2026-50F" },
            new ProductionAsset { Code = "INJ-L2-01", Name = "注液机（大机）", Type = AssetTypes.Equipment, Section = "注液封口", Process = "注液", Line = "L2", Capacity = 1, Status = "在用", Models = "SC-CX2026-300F" },

            // ---- 封口（其中一台维护中） ----
            new ProductionAsset { Code = "SEAL-L1-01", Name = "封口机", Type = AssetTypes.Equipment, Section = "注液封口", Process = "封口", Line = "L1", Capacity = 1, Status = "维护", Models = "SC-CX2026-100F,SC-CX2026-50F" },
            new ProductionAsset { Code = "SEAL-L2-01", Name = "封口机", Type = AssetTypes.Equipment, Section = "注液封口", Process = "封口", Line = "L2", Capacity = 1, Status = "在用", Models = "SC-CX2026-100F,SC-CX2026-300F,SC-CX2026-50F" },

            // ---- 化成（容量 2，可并行 → 演示"余量不足但不冲突"） ----
            new ProductionAsset { Code = "FORM-A01", Name = "化成柜组A", Type = AssetTypes.Equipment, Section = "化成老化", Process = "化成", Line = "", Capacity = 2, Status = "在用", Models = "" },

            // ---- 检测 ----
            new ProductionAsset { Code = "TST-L1-03", Name = "电性能测试仪", Type = AssetTypes.Inspector, Section = "分选检测", Process = "检测", Line = "L1", Capacity = 1, Status = "在用", Models = "SC-CX2026-100F,SC-CX2026-50F" },
            new ProductionAsset { Code = "TST-L2-01", Name = "电性能测试仪", Type = AssetTypes.Inspector, Section = "分选检测", Process = "检测", Line = "L2", Capacity = 1, Status = "在用", Models = "SC-CX2026-300F" },

            // ---- 环境类（不参与工单占用：Process 为空，不会被自动分配） ----
            new ProductionAsset { Code = "DRY-RM-01", Name = "干燥房", Type = AssetTypes.Equipment, Section = "环境", Process = "", Line = "", Capacity = 1, Status = "在用", Models = "" },
        });

        // ---- 初始占用：让"时间冲突"在演示时真的能看到 ----
        // 用相对当前时间的滚动窗口；写死日期过了就失效。
        // ⚠ 初始数据本身**不制造超占用**（占用量不超过资产容量）——种子要是自己就违了规，
        //   后面所有校验结果都失去说服力。冲突场景靠"新建工单去抢在制工单的设备"来演示。
        var today = DateTime.Today;
        var t0 = today.AddHours(8);
        var seedOccupancies = new (string WoNo, string AssetCode, int DaysFrom, int DaysTo, string Status, string Source)[]
        {
            // 在制工单（100F / L1）已占住 L1 的卷绕 / 注液 / 检测 / 入壳工装
            ("WO-20260812-001", "WIND-L1-01",  -1,  2, OccupancyStatus.Confirmed, OccupancySource.Manual),
            ("WO-20260812-001", "INJ-L1-02",  -1,  2, OccupancyStatus.Confirmed, OccupancySource.Manual),
            ("WO-20260812-001", "TST-L1-03",  -1,  2, OccupancyStatus.Confirmed, OccupancySource.Manual),
            ("WO-20260812-001", "TOL-CASE-01", -1,  2, OccupancyStatus.Confirmed, OccupancySource.Manual),
            // 未开工工单（100F）排在 L2，占 L2 的设备——排产本来就该避开在制工单占着的 L1
            ("WO-20260814-004", "WIND-L2-01",  1,  4, OccupancyStatus.Reserved, OccupancySource.Scheduling),
            ("WO-20260814-004", "TOL-CASE-02", 1,  4, OccupancyStatus.Reserved, OccupancySource.Scheduling),
            // 在制工单（300F / L2）占大机注液与 L2 检测
            ("WO-20260813-002", "INJ-L2-01",   0,  2, OccupancyStatus.Confirmed, OccupancySource.Manual),
            ("WO-20260813-002", "TST-L2-01",   0,  2, OccupancyStatus.Confirmed, OccupancySource.Manual),
            // 化成柜容量 2：只被占 1 台，演示"有余量但吃紧"（不冲突）
            ("WO-20260813-002", "FORM-A01",    0,  2, OccupancyStatus.Reserved, OccupancySource.Scheduling),
        };

        foreach (var s in seedOccupancies)
        {
            var wo = WorkOrders.FirstOrDefault(w => w.WoNo == s.WoNo);
            AssetOccupancies.Add(new AssetOccupancy
            {
                Id = NextOccupancyId(),
                AssetCode = s.AssetCode,
                WoNo = s.WoNo,
                Source = s.Source,
                Qty = 1,
                StartTime = t0.AddDays(s.DaysFrom),
                EndTime = t0.AddDays(s.DaysTo),
                Status = s.Status,
                CreatedAt = DateTime.Now,
                Version = 1,
                Remark = wo is null ? "初始数据" : $"初始占用（{wo.WoNo} / {wo.ProductModel} / {wo.Status}）"
            });
        }
    }
}
