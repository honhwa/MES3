namespace SuperCapMES.WinForms.Models;

/// <summary>在职状态</summary>
public static class EmploymentStatus
{
    public const string Active = "在职";
    public const string Resigned = "离职";

    public static readonly string[] All = { Active, Resigned };
}

/// <summary>资质等级</summary>
public static class CertLevel
{
    public const string Junior = "初级";
    public const string Middle = "中级";
    public const string Senior = "高级";
    public const string Expert = "专家";

    public static readonly string[] All = { Junior, Middle, Senior, Expert };
}

/// <summary>
/// 资质状态（由「有效期 + 吊销标记」派生，不落库，避免两处维护）。
/// 业务规则：提前 <see cref="WarningDays"/> 天进入「即将到期」；过期日次日即为「已过期」；吊销优先级最高。
/// </summary>
public static class CertStatus
{
    public const string Valid = "有效";
    public const string ExpiringSoon = "即将到期";
    public const string Expired = "已过期";
    public const string Revoked = "已吊销";

    /// <summary>提前预警天数（到期前 30 天开始提示复审）</summary>
    public const int WarningDays = 30;

    public static readonly string[] All = { Valid, ExpiringSoon, Expired, Revoked };

    /// <summary>判定资质状态：吊销 &gt; 已过期 &gt; 即将到期 &gt; 有效</summary>
    public static string Judge(DateTime? expiryDate, bool revoked)
    {
        if (revoked) return Revoked;
        if (expiryDate is null) return Valid;   // 长期有效
        var daysLeft = (expiryDate.Value.Date - DateTime.Today).TotalDays;
        if (daysLeft < 0) return Expired;
        return daysLeft <= WarningDays ? ExpiringSoon : Valid;
    }

    /// <summary>该状态是否算「可上岗」（有效 / 即将到期均可上岗，过期与吊销不可）</summary>
    public static bool CanWork(string status) => status is Valid or ExpiringSoon;
}

/// <summary>
/// 资质项目录：工序上岗证（与工序一一对应）+ 职能资格 + 特种作业证。
/// 「工序名 → 上岗证」的映射是「无资质不能上岗」防错规则的唯一真源。
/// </summary>
public static class SkillCatalog
{
    // ---- 工序类上岗证 ----
    public const string Coating = "涂布上岗证";
    public const string Winding = "卷绕上岗证";
    public const string Injection = "注液上岗证";
    public const string Sealing = "封口上岗证";
    public const string Formation = "化成上岗证";
    public const string ETest = "电性能检测上岗证";

    // ---- 职能类资格 ----
    public const string TpmOperator = "点检作业资格";
    public const string FaiInspector = "首件检验资格";
    public const string SpcAnalyst = "SPC 分析资格";
    public const string QualityEngineer = "质量工程师资格";

    // ---- 特种作业类 ----
    public const string Forklift = "叉车作业证";
    public const string Electrician = "低压电工作业证";

    public static readonly string[] ProcessSkills =
        { Coating, Winding, Injection, Sealing, Formation, ETest };

    public static readonly string[] FunctionSkills =
        { TpmOperator, FaiInspector, SpcAnalyst, QualityEngineer };

    public static readonly string[] SpecialSkills = { Forklift, Electrician };

    /// <summary>全部资质项（矩阵列顺序：工序类 → 职能类 → 特种类）</summary>
    public static readonly string[] All =
        ProcessSkills.Concat(FunctionSkills).Concat(SpecialSkills).ToArray();

    /// <summary>
    /// 工序名 → 所需上岗证；返回 null 表示该工序不设持证要求（不做拦截）。
    /// 覆盖两套既有工序词典：
    /// <see cref="Data.MesDatabase.DeviceProcessMap"/>（涂布 / 卷绕 / 注液 / 封口 / 化成 / 电性能检测）
    /// 与 <see cref="ProcessSequence.Order"/>（卷绕 / 入壳 / 注液 / 封口 / 化成 / 检测）。
    /// 其中「入壳」与「卷绕」同属电芯成型段且共用设备 WIND-L1-01，取同一张上岗证；
    /// 「检测」即「电性能检测」，共用设备 TST-L1-03。
    /// </summary>
    public static string? SkillOfProcess(string? processName) => (processName ?? "").Trim() switch
    {
        "涂布" => Coating,
        "卷绕" or "入壳" => Winding,
        "注液" => Injection,
        "封口" => Sealing,
        "化成" => Formation,
        "检测" or "电性能检测" => ETest,
        _ => null
    };
}

/// <summary>人员主数据（工号为唯一键）</summary>
public class UserProfile
{
    /// <summary>工号，形如 E1001</summary>
    public string EmpNo { get; set; } = "";

    public string Name { get; set; } = "";
    public string Dept { get; set; } = "";

    /// <summary>岗位</summary>
    public string Position { get; set; } = "";

    /// <summary>班组：A班 / B班 / 常白班</summary>
    public string Crew { get; set; } = "";

    /// <summary>在职状态：在职 / 离职</summary>
    public string Status { get; set; } = EmploymentStatus.Active;

    public string Phone { get; set; } = "";
    public DateTime HireDate { get; set; }

    public bool IsActive => Status == EmploymentStatus.Active;

    public string HireText => HireDate == default ? "—" : HireDate.ToString("yyyy-MM-dd");
}

/// <summary>资质证书（工号 + 资质项 为业务唯一键：同一人同一资质项只保留一条）</summary>
public class SkillCertificate
{
    public string EmpNo { get; set; } = "";
    public string EmpName { get; set; } = "";

    /// <summary>资质项（见 <see cref="SkillCatalog"/>）</summary>
    public string SkillItem { get; set; } = "";

    /// <summary>等级（见 <see cref="CertLevel"/>）</summary>
    public string Level { get; set; } = CertLevel.Junior;

    /// <summary>证书编号</summary>
    public string CertNo { get; set; } = "";

    public DateTime IssueDate { get; set; }

    /// <summary>有效期至（null = 长期有效）</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>发证机构</summary>
    public string IssuedBy { get; set; } = "";

    /// <summary>是否已吊销</summary>
    public bool Revoked { get; set; }

    public string RevokeReason { get; set; } = "";
    public DateTime? RevokeTime { get; set; }

    /// <summary>资质状态：有效 / 即将到期 / 已过期 / 已吊销</summary>
    public string Status => CertStatus.Judge(ExpiryDate, Revoked);

    /// <summary>距到期天数（null 表示长期有效）</summary>
    public double? DaysLeft => ExpiryDate is null
        ? null
        : Math.Round((ExpiryDate.Value.Date - DateTime.Today).TotalDays, 0);

    public string DaysLeftText => Revoked
        ? "—"
        : ExpiryDate is null
            ? "长期有效"
            : DaysLeft < 0 ? $"已过期 {-DaysLeft:0} 天" : $"剩 {DaysLeft:0} 天";

    public string IssueText => IssueDate == default ? "—" : IssueDate.ToString("yyyy-MM-dd");
    public string ExpiryText => ExpiryDate is null ? "长期有效" : ExpiryDate.Value.ToString("yyyy-MM-dd");
    public string PeriodText => $"{IssueText} ~ {ExpiryText}";

    /// <summary>是否可凭此证上岗</summary>
    public bool CanWork => CertStatus.CanWork(Status);
}

/// <summary>人员台账行（人员主数据 + 资质统计；由仓储层投影，供表格绑定）</summary>
public class UserRow
{
    public string EmpNo { get; init; } = "";
    public string Name { get; init; } = "";
    public string Dept { get; init; } = "";
    public string Position { get; init; } = "";
    public string Crew { get; init; } = "";
    public string Status { get; init; } = EmploymentStatus.Active;
    public string Phone { get; init; } = "";
    public DateTime HireDate { get; init; }

    /// <summary>资质项总数</summary>
    public int CertTotal { get; init; }

    /// <summary>有效资质数（含即将到期）</summary>
    public int CertValid { get; init; }

    public int CertExpiring { get; init; }

    /// <summary>已过期 + 已吊销</summary>
    public int CertInvalid { get; init; }

    /// <summary>具备工序类上岗证数量</summary>
    public int ProcessSkillCount { get; init; }

    public string HireText => HireDate == default ? "—" : HireDate.ToString("yyyy-MM-dd");
    public string CertText => CertTotal == 0 ? "无" : $"有效 {CertValid} / 共 {CertTotal}";
    public bool IsActive => Status == EmploymentStatus.Active;

    /// <summary>资质健康度：缺证 / 即将到期 / 已失效 / 正常</summary>
    public string CertHealth => CertTotal == 0
        ? "未持证"
        : CertInvalid > 0 ? "存在无效资质"
        : CertExpiring > 0 ? "即将到期"
        : "正常";
}

/// <summary>资质矩阵行（人员 × 资质项）：Cells/Statuses 与 <see cref="SkillCatalog.All"/> 下标一一对应</summary>
public class SkillMatrixRow
{
    public string EmpNo { get; init; } = "";
    public string EmpName { get; init; } = "";
    public string Dept { get; init; } = "";
    public string Position { get; init; } = "";
    public string Crew { get; init; } = "";
    public string Status { get; init; } = EmploymentStatus.Active;

    /// <summary>每个资质项的格值：等级（持证）/ "—"（无证）</summary>
    public string[] Cells { get; init; } = Array.Empty<string>();

    /// <summary>每格状态（CertStatus 之一，或 "" 表示无证）</summary>
    public string[] CellStatuses { get; init; } = Array.Empty<string>();

    public int CertTotal { get; init; }
    public bool IsActive => Status == EmploymentStatus.Active;
}

/// <summary>班组绩效行（班组维度聚合）</summary>
public class CrewPerformanceRow
{
    public string Crew { get; init; } = "";

    /// <summary>在册人数</summary>
    public int Headcount { get; init; }

    /// <summary>持证人数（至少持 1 项有效资质）</summary>
    public int CertifiedHeadcount { get; init; }

    /// <summary>有效资质项总数</summary>
    public int ValidCertCount { get; init; }

    public int ExpiringCertCount { get; init; }
    public int InvalidCertCount { get; init; }

    /// <summary>线上工序类上岗证覆盖的工序数（去重）</summary>
    public int CoveredProcessCount { get; init; }

    /// <summary>该班组人员的工序实绩条数</summary>
    public int ProcessOpCount { get; init; }

    /// <summary>无资质上岗嫌疑条数（实绩核查未通过）</summary>
    public int UnqualifiedOpCount { get; init; }

    public double CertRate => Headcount == 0 ? 0 : Math.Round(CertifiedHeadcount * 100.0 / Headcount, 1);
    public string CertRateText => $"{CertRate:0.0}%";
    public string CoverageText => $"{CoveredProcessCount} / {SkillCatalog.ProcessSkills.Length} 工序";
    public string HealthText => InvalidCertCount > 0 ? "存在失效资质" : (UnqualifiedOpCount > 0 ? "有无证上岗" : "正常");
}

/// <summary>上岗资质核查行：对一条工序实绩做「操作人是否持证」判定</summary>
public class QualificationCheckRow
{
    public string CellSn { get; init; } = "";
    public string WoNo { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string Equipment { get; init; } = "";
    public string Operator { get; init; } = "";
    public string Shift { get; init; } = "";
    public DateTime Time { get; init; }
    public string TimeText => Time == default ? "—" : Time.ToString("MM-dd HH:mm");

    /// <summary>该工序要求的上岗证（空表示不设持证要求）</summary>
    public string RequiredSkill { get; init; } = "";

    /// <summary>操作人持有的该项资质等级（空表示无证）</summary>
    public string CertLevelText { get; init; } = "";

    /// <summary>操作人该项资质的状态（空表示无证）</summary>
    public string CertStatusText { get; init; } = "";

    /// <summary>是否通过核查</summary>
    public bool Passed { get; init; }

    /// <summary>判定结论</summary>
    public string Result { get; init; } = "";

    public string ResultText => Passed ? "通过" : "不通过";
}
