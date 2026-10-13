using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 能源管理（EMS）—— 仓储层：计量点 / 能耗读数 / 多维查询 / 录入
//
// 种子数据设计（演示用，不跑真实环境）：
//   · **每次启动都按当前日期补生成**最近 6 个月，不是只生成一次
//   · 按 (MeterId, Period) upsert：新账期插入、已有账期不覆盖
//     → 每周打开都能看到新月份，且不会随启动次数堆积
//   · 三级计量点：全厂总表 / 线体级 / 设备级（7 台设备全覆盖）
//   · 数值与当月产量挂钩（产量高→能耗高），并叠加季节性波动，
//     这样"能耗查询"页才有同比环比可看，而不是一串随机数
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>能源种类定义</summary>
    public static List<EnergyTypeDef> EnergyTypeDefs { get; } = new()
    {
        new EnergyTypeDef { TypeId = "ELEC", Name = EnergyTypes.Electricity, Unit = "kWh", Remark = "外购电力" },
        new EnergyTypeDef { TypeId = "WATER", Name = EnergyTypes.Water, Unit = "t", Remark = "市政自来水 + 纯水制备" },
        new EnergyTypeDef { TypeId = "GAS", Name = EnergyTypes.Gas, Unit = "Nm³", Remark = "天然气（干燥/加热工序）" },
        new EnergyTypeDef { TypeId = "STEAM", Name = EnergyTypes.Steam, Unit = "t", Remark = "外购蒸汽" }
    };

    /// <summary>计量点台账</summary>
    public static List<EnergyMeter> EnergyMeters { get; } = new();

    /// <summary>能耗读数（唯一键 = MeterId + Period）</summary>
    public static List<EnergyReading> EnergyReadings { get; } = new();

    private static bool _energyMetersReady;

    /// <summary>账期格式：2026-09</summary>
    public static string ToPeriod(DateTime d) => $"{d.Year:0000}-{d.Month:00}";

    // ========================================================================
    // 种子数据：每次启动补生成最近 N 个月（默认 36，幂等 upsert）
    // ========================================================================

    /// <summary>
    /// 确保计量点已建，并按当前日期补生成最近 <paramref name="months"/> 个月读数。
    /// **幂等**：计量点只建一次；读数按 (MeterId, Period) upsert，已存在的账期跳过。
    /// </summary>
    /// <param name="now">基准日期（默认今天）。</param>
    /// <param name="months">
    /// 回溯月数。默认 36（三年）——碳资产「碳资产管理」页要按**年度**测算履约，
    /// 若读数只有最近 6 个月，2024/2025 等年度会算出 0 排放并误判为「履约达标」。
    /// </param>
    public static void EnsureEnergySeed(DateTime? now = null, int months = 36)
    {
        var at = now ?? DateTime.Now;
        EnsureEnergyMeters();

        if (months < 1) months = 1;
        if (months > 120) months = 120;
        var periods = new List<string>();
        for (var i = months - 1; i >= 0; i--)
            periods.Add(ToPeriod(new DateTime(at.Year, at.Month, 1).AddMonths(-i)));

        // 当月产量（用于让能耗与产量挂钩，做出"真实感"）
        var monthlyOutput = BuildMonthlyOutput(periods);

        foreach (var m in EnergyMeters)
        {
            foreach (var p in periods)
            {
                if (FindEnergyReading(m.MeterId, p) is not null) continue;   // 已有账期不重复
                var v = Math.Round(SimulateEnergy(m, p, monthlyOutput), 2);
                var r = new EnergyReading
                {
                    MeterId = m.MeterId, Period = p, Value = v,
                    Source = "模拟", EnteredBy = "系统", RecordedAt = at,
                    Remark = "演示数据：按计量点等级 × 当月产量 × 季节系数生成"
                };
                EnergyReadings.Add(Decorate(r));   // 展示字段当场填齐，别留给下游
            }
        }
    }

    /// <summary>建立三级计量点（只建一次）</summary>
    private static void EnsureEnergyMeters()
    {
        if (_energyMetersReady) return;

        var lines = Equipments.Select(e => e.Section).Where(s => s.Length > 0).Distinct().ToList();
        var lineIds = new[] { "L1", "L2", "L3" };
        var sections = new[] { "涂布", "卷绕", "注液", "化成", "测试" };

        foreach (var t in EnergyTypeDefs)
        {
            // ① 全厂总表
            EnergyMeters.Add(new EnergyMeter
            {
                MeterId = $"M-PLANT-{t.TypeId}",
                TypeId = t.TypeId, TypeName = t.Name, Unit = t.Unit,
                Scope = EnergyScopes.Plant, Section = "全厂", Line = "",
                EquipmentCode = "", EquipmentName = "",
                Location = "总配电房 / 总表"
            });

            // ② 线体级（3 条线）
            foreach (var (line, sec) in lineIds.Zip(sections))
            {
                EnergyMeters.Add(new EnergyMeter
                {
                    MeterId = $"M-{line}-{t.TypeId}",
                    TypeId = t.TypeId, TypeName = t.Name, Unit = t.Unit,
                    Scope = EnergyScopes.Line, Section = sec, Line = line,
                    EquipmentCode = "", EquipmentName = "",
                    Location = $"{line} 线体总表"
                });
            }
        }

        // ③ 设备级（7 台设备全覆盖，每种能源都挂）
        foreach (var e in Equipments)
        {
            var line = e.Section.Length >= 2 && e.Section.StartsWith("L") ? e.Section[..2] : "L1";
            foreach (var t in EnergyTypeDefs)
            {
                // 蒸汽/燃气只挂在有加热工艺的段（化成/干燥），其余只挂电气水
                if ((t.TypeId is "STEAM" or "GAS")
                    && !(e.Name.Contains("化成") || e.Name.Contains("干燥") || e.Name.Contains("注液")))
                    continue;

                EnergyMeters.Add(new EnergyMeter
                {
                    MeterId = $"M-{e.Code}-{t.TypeId}",
                    TypeId = t.TypeId, TypeName = t.Name, Unit = t.Unit,
                    Scope = EnergyScopes.Device, Section = e.Section, Line = line,
                    EquipmentCode = e.Code, EquipmentName = e.Name,
                    Location = $"{e.Code} 就地表"
                });
            }
        }

        _energyMetersReady = true;
    }

    /// <summary>按账期取当月产量（来自报工实绩，无实绩的账期给一个稳定的基准值）</summary>
    private static Dictionary<string, int> BuildMonthlyOutput(List<string> periods)
    {
        var result = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var (y, m) = (int.Parse(p[..4]), int.Parse(p[5..7]));
            var from = new DateTime(y, m, 1);
            var to = from.AddMonths(1);
            var qty = ErpWorkReports
                .Where(r => r.ReportTime >= from && r.ReportTime < to)
                .Sum(r => r.GoodQty);
            // 无实绩的账期：给一个与「年+月」相关的稳定基准
            //（**必须带年份**，否则同月不同年会算出同一个值，多年度台账看起来是复制粘贴的；
            //  且不做随机，保证同一天重复打开结果一致）
            result[p] = qty > 0 ? (int)qty : 4000 + (m * 137) % 900 + (y % 5) * 110;
        }
        return result;
    }

    /// <summary>
    /// 模拟某计量点某账期的能耗。
    /// 确定性算法（不用 Random）：同一天重复打开软件，生成结果完全一致。
    /// </summary>
    private static double SimulateEnergy(EnergyMeter m, string period, Dictionary<string, int> output)
    {
        var (y, mo) = (int.Parse(period[..4]), int.Parse(period[5..7]));
        var qty = output.GetValueOrDefault(period, 4200);

        // ① 基础负荷：与产量成正比（度/只 之类）
        var perUnit = m.TypeId switch
        {
            "ELEC" => 1.85, "WATER" => 0.62, "GAS" => 0.34, "STEAM" => 0.09, _ => 1.0
        };

        // ② 计量等级系数：设备级最小、线体中等、全厂最大
        var scopeFactor = m.Scope switch
        {
            EnergyScopes.Device => 0.18,
            EnergyScopes.Line => 0.34,
            _ => 1.0
        };

        // ③ 季节系数：夏冬高、春秋低（用电尤明显）
        var season = mo switch
        {
            1 or 2 or 12 => 1.16,
            7 or 8 => 1.22,
            4 or 5 or 9 or 10 => 0.98,
            _ => 1.02
        };
        if (m.TypeId is "WATER" or "GAS") season = 1.0 + (mo % 3) * 0.02;   // 水气季节性弱

        // ④ 稳定扰动：按计量点 + 年 + 月，保证可复现且不平滑
        //⚠ 不能用 string.GetHashCode()：.NET 每次进程启动都会随机化字符串哈希种子，
        //    会导致"同一天重复打开软件读数却变了"，与确定性算法的承诺相悖。
        var noise = 0.92 + (Math.Abs(StableHash(m.MeterId) + y * 131 + mo * 31) % 17) / 100.0;

        var v = qty * perUnit * scopeFactor * season * noise;
        return m.TypeId == "ELEC" && m.Scope == EnergyScopes.Plant ? v * 1.04 : v;   // 厂级含辅助用电
    }

    /// <summary>跨进程稳定的字符串哈希（FNV-1a），替代会被随机化的 GetHashCode。</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 2166136261;
            foreach (var ch in s ?? "") { h ^= ch; h *= 16777619; }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    // ========================================================================
    // 查询（纯查询）
    // ========================================================================

    /// <summary>按 (MeterId, Period) 取单条读数</summary>
    public static EnergyReading? FindEnergyReading(string meterId, string period)
        => EnergyReadings.FirstOrDefault(r =>
            string.Equals(r.MeterId, meterId?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Period, period?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 按**计量点**取读数（账期降序）。
    /// 单独一个方法而不是给 QueryEnergyReadings 加参数：后者是按"能源/区域/线体/设备"过滤的，
    /// 把 MeterId 塞进去会和 EquipmentCode 混为一谈（实测踩过：传 MeterId 被当设备编码用，返回空）。
    /// </summary>
    public static List<EnergyReading> QueryByMeter(string? meterId)
    {
        if (string.IsNullOrWhiteSpace(meterId)) return new List<EnergyReading>();
        var id = meterId.Trim();
        return EnergyReadings
            .Where(r => string.Equals(r.MeterId, id, StringComparison.OrdinalIgnoreCase))
            .Select(Decorate)
            .OrderByDescending(r => r.Period)
            .ToList();
    }

    /// <summary>能耗明细查询：按能源/区域/线体/设备/账期范围过滤，账期降序。</summary>
    public static List<EnergyReading> QueryEnergyReadings(
        string? typeId = null, string? section = null, string? line = null,
        string? equipmentCode = null, string? periodFrom = null, string? periodTo = null,
        string? scope = null)
    {
        var t = (typeId ?? "").Trim();
        var s = (section ?? "").Trim();
        var l = (line ?? "").Trim();
        var e = (equipmentCode ?? "").Trim();
        var sc = (scope ?? "").Trim();
        var from = ToPeriodKey(periodFrom);
        var to = ToPeriodKey(periodTo);

        return EnergyReadings.Where(r =>
                (t.Length == 0 || string.Equals(r.TypeId, t, StringComparison.OrdinalIgnoreCase))
                && (s.Length == 0 || r.Section.Contains(s, StringComparison.OrdinalIgnoreCase))
                && (l.Length == 0 || r.Line.Contains(l, StringComparison.OrdinalIgnoreCase))
                && (e.Length == 0 || r.EquipmentCode.Contains(e, StringComparison.OrdinalIgnoreCase))
                && (sc.Length == 0 || string.Equals(r.Scope, sc, StringComparison.OrdinalIgnoreCase))
                && (from.Length == 0 || string.Compare(r.Period, from, StringComparison.Ordinal) >= 0)
                && (to.Length == 0 || string.Compare(r.Period, to, StringComparison.Ordinal) <= 0))
            .Select(r => Decorate(r))
            .OrderByDescending(r => r.Period)
            .ThenBy(r => r.MeterId)
            .ToList();
    }

    /// <summary>
    /// 把传入的账期条件归一成 "yyyy-MM" 再比较。
    /// ⚠ 读数的 Period 恒为 7 位（"2026-05"），若直接拿它和界面上传的 10 位
    /// "2026-01-01" 做字符串比较，**年初那一月会被当成"小于起点"而整月漏掉**
    /// （"2026-01" &lt; "2026-01-01"）。这里统一截到年月，避免静默丢数据。
    /// </summary>
    private static string ToPeriodKey(string? period)
    {
        var s = (period ?? "").Trim();
        if (s.Length == 0) return "";
        return s.Length >= 7 ? s[..7] : s;
    }

    /// <summary>把计量点信息冗余到读数行（展示用，避免界面反复 join）</summary>
    private static EnergyReading Decorate(EnergyReading r)
    {
        var m = EnergyMeters.FirstOrDefault(x => x.MeterId == r.MeterId);
        if (m is null) return r;
        r.TypeId = m.TypeId; r.TypeName = m.TypeName; r.Unit = m.Unit;
        r.Scope = m.Scope; r.Section = m.Section; r.Line = m.Line;
        r.EquipmentCode = m.EquipmentCode; r.EquipmentName = m.EquipmentName;
        r.DisplayName = m.DisplayName;
        return r;
    }

    /// <summary>
    /// 按维度汇总能耗（能耗查询页的统计报表用）。
    /// groupBy：period / line / section / equipment / type
    /// </summary>
    public static List<EnergySummaryRow> SummarizeEnergy(
        string groupBy, string? typeId = null, string? section = null, string? line = null,
        string? periodFrom = null, string? periodTo = null, string? scope = null)
    {
        var rows = QueryEnergyReadings(typeId, section, line, null, periodFrom, periodTo, scope);

        // ⚠ 维度名必须同时认中文（界面下拉）和英文（脚本/测试）—— 曾经只认英文小写，
        //   界面传"线体"时静默落到默认分支按账期分组，汇总维度整个不对却没报错。
        var dim = (groupBy ?? "").Trim();
        IEnumerable<IGrouping<string, EnergyReading>> groups = dim switch
        {
            "线体" or "line" => rows.GroupBy(r => r.LineText + " " + r.SectionText),
            "工序段" or "section" => rows.GroupBy(r => r.SectionText),
            "设备" or "equipment" => rows.GroupBy(r => r.EquipmentCodeText + " " + r.EquipmentNameText),
            "能源" or "type" => rows.GroupBy(r => r.TypeName),
            _ => rows.GroupBy(r => r.PeriodText)
        };

        // 不同能源单位不同，汇总时按「维度 + 能源」分开展示，避免把 kWh 和 t 加在一起
        // ⚠ Dimension 必须用分组键 g.Key —— 曾经错写成能源名，结果"按线体汇总"展示出来还是能源名，维度整个丢了
        return groups.SelectMany(g => g.GroupBy(r => r.TypeId)
                .Select(g2 => new EnergySummaryRow
                {
                    Dimension = g.Key,
                    SubDimension = g2.First().TypeName,
                    Value = Math.Round(g2.Sum(r => r.Value), 2),
                    Unit = g2.First().Unit,
                    Source = g2.First().Source
                }))
            .OrderBy(r => r.Dimension)
            .ThenByDescending(r => r.Value)
            .ToList();
    }

    /// <summary>能耗汇总卡片：按能源类型汇总区间总量。</summary>
    public static List<EnergySummaryRow> SummarizeEnergyByType(
        string? periodFrom = null, string? periodTo = null, string? scope = null)
    {
        var rows = QueryEnergyReadings(null, null, null, null, periodFrom, periodTo, scope);
        return rows.GroupBy(r => r.TypeId)
            .Select(g => new EnergySummaryRow
            {
                Dimension = g.First().TypeName,
                SubDimension = g.Key,
                Value = Math.Round(g.Sum(r => r.Value), 2),
                Unit = g.First().Unit,
                Source = g.First().Source
            })
            .OrderByDescending(r => r.Value)
            .ToList();
    }

    // ========================================================================
    // 录入（唯一写入口）
    // ========================================================================

    /// <summary>
    /// 人工录入 / 修正一次抄表数。**按 (MeterId, Period) upsert**：
    /// 同账期重复录入会覆盖（修正），不会产生重复行。
    /// </summary>
    public static (bool Ok, string Message) UpsertEnergyReading(
        string meterId, string period, double value,
        string source = "人工录入", string? enteredBy = null, string? remark = null)
    {
        var m = EnergyMeters.FirstOrDefault(x => string.Equals(x.MeterId, meterId?.Trim() ?? "",
            StringComparison.OrdinalIgnoreCase));
        if (m is null) return (false, $"未找到计量点 {meterId}。");

        var p = (period ?? "").Trim();
        if (p.Length != 7 || !p.Contains('-'))
            return (false, $"账期格式应为 yyyy-MM，当前为 {period}。");
        if (value < 0) return (false, "消耗量不能为负。");

        var exist = FindEnergyReading(m.MeterId, p);
        if (exist is not null)
        {
            var old = exist.Value;
            exist.Value = Math.Round(value, 2);
            exist.Source = source;
            exist.EnteredBy = (enteredBy ?? "系统").Trim();
            exist.RecordedAt = DateTime.Now;
            exist.Remark = (remark ?? "").Trim();
            return (true, $"已修正 {m.DisplayName} {p} 的{m.TypeName}用量：{old:0.##} → {exist.Value:0.##} {m.Unit}。");
        }

        EnergyReadings.Add(new EnergyReading
        {
            MeterId = m.MeterId, Period = p, Value = Math.Round(value, 2),
            Source = source, EnteredBy = (enteredBy ?? "系统").Trim(),
            RecordedAt = DateTime.Now, Remark = (remark ?? "").Trim()
        });
        return (true, $"已录入 {m.DisplayName} {p} 的{m.TypeName}用量 {value:0.##} {m.Unit}。");
    }
}