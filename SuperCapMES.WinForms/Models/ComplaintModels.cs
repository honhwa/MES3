namespace SuperCapMES.WinForms.Models;

/// <summary>客诉来源</summary>
public static class ComplaintSource
{
    public const string Customer = "客户投诉";
    public const string AfterSales = "售后反馈";
    public const string Field = "现场失效";
    public const string Internal = "内部转客诉";

    public static readonly string[] All = { Customer, AfterSales, Field, Internal };
}

/// <summary>客诉严重度（影响处理时限与是否需要召回）</summary>
public static class ComplaintSeverity
{
    public const string Critical = "严重";
    public const string Major = "一般";
    public const string Minor = "轻微";

    public static readonly string[] All = { Critical, Major, Minor };

    /// <summary>处理时限（天）：严重 3 天、一般 7 天、轻微 15 天</summary>
    public static int ResponseDays(string severity) => severity switch
    {
        Critical => 3,
        Major => 7,
        _ => 15
    };
}

/// <summary>客诉状态机：待受理 → 处理中 → 待客户确认 → 已关闭</summary>
public static class ComplaintStatus
{
    public const string Pending = "待受理";
    public const string Investigating = "处理中";
    public const string WaitingCustomer = "待客户确认";
    public const string Closed = "已关闭";

    public static readonly string[] All = { Pending, Investigating, WaitingCustomer, Closed };

    /// <summary>是否已终结（终结后不允许再推进 8D）</summary>
    public static bool IsClosed(string status) => status == Closed;
}

/// <summary>客诉症状分类</summary>
public static class ComplaintCategories
{
    public const string CapacityLow = "容量偏低";
    public const string EsrHigh = "内阻偏高";
    public const string Leak = "电解液渗漏";
    public const string Appearance = "外观不良";
    public const string Dimension = "尺寸超差";
    public const string Swelling = "鼓胀";
    public const string Decay = "容量衰减快";
    public const string Other = "其它";

    public static readonly string[] All =
        { CapacityLow, EsrHigh, Leak, Appearance, Dimension, Swelling, Decay, Other };
}

/// <summary>
/// 8D 步骤常量与顺序推进规则。
/// 顺序固定：D1 组建团队 → D2 问题描述 → D3 临时围堵 → D4 根本原因 → D5 永久对策 → D6 实施与验证 → D7 预防再发 → D8 结案与表彰。
/// </summary>
public static class EightDStep
{
    public const string D1 = "D1 组建团队";
    public const string D2 = "D2 问题描述";
    public const string D3 = "D3 临时围堵";
    public const string D4 = "D4 根本原因";
    public const string D5 = "D5 永久对策";
    public const string D6 = "D6 实施与验证";
    public const string D7 = "D7 预防再发";
    public const string D8 = "D8 结案与表彰";

    public static readonly string[] Order = { D1, D2, D3, D4, D5, D6, D7, D8 };

    /// <summary>步骤序号（未知返回 -1）</summary>
    public static int IndexOf(string? step)
    {
        if (string.IsNullOrWhiteSpace(step)) return -1;
        for (var i = 0; i < Order.Length; i++)
            if (string.Equals(Order[i], step.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>下一步（末步返回 null）</summary>
    public static string? Next(string? step)
    {
        var idx = IndexOf(step);
        return idx < 0 || idx >= Order.Length - 1 ? null : Order[idx + 1];
    }

    /// <summary>该步骤的填写要求（用于界面提示与关闭校验错误信息）</summary>
    public static string RequirementOf(string step) => step switch
    {
        D1 => "列出组长与成员（含质量、工艺、生产、供应链）及职责分工",
        D2 => "用 5W2H 描述问题：现象、批次/SN 范围、数量、发生与发现时间、影响",
        D3 => "临时围堵措施：在库/在制/已出货的隔离、筛选、加严检验方案",
        D4 => "根本原因分析（建议 5Why / 鱼骨图），须给出可验证的直接原因",
        D5 => "永久对策：工艺/设计/来料/检验的纠正措施与责任人、完成时间",
        D6 => "对策实施与效果验证：验证方法、样本量、结果数据",
        D7 => "预防再发：FMEA / 控制计划 / 作业标准 / 培训的更新与横向展开",
        D8 => "结案：客户确认结论、团队表彰、经验录入知识库",
        _ => ""
    };
}

/// <summary>客诉单</summary>
public class CustomerComplaint
{
    /// <summary>客诉单号，形如 CMP-yyyyMMdd-001</summary>
    public string ComplaintNo { get; set; } = "";

    public string CustomerName { get; set; } = "";
    public string ContactInfo { get; set; } = "";

    /// <summary>客诉接收时间</summary>
    public DateTime ReceiveTime { get; set; }

    /// <summary>来源（见 <see cref="ComplaintSource"/>）</summary>
    public string Source { get; set; } = ComplaintSource.Customer;

    /// <summary>严重度（见 <see cref="ComplaintSeverity"/>）</summary>
    public string Severity { get; set; } = ComplaintSeverity.Major;

    /// <summary>症状分类（见 <see cref="ComplaintCategories"/>）</summary>
    public string Category { get; set; } = ComplaintCategories.Other;

    public string ProductModel { get; set; } = "";

    /// <summary>关联范围关键字：材料批次 / 工单号 / 电芯 SN / 出货单号（用于触发范围反查）</summary>
    public string LotKeyword { get; set; } = "";

    /// <summary>客诉数量（只/只数）</summary>
    public double Quantity { get; set; }

    /// <summary>客户描述的现象</summary>
    public string SymptomDesc { get; set; } = "";

    /// <summary>是否涉及安全（涉安全必须走召回评估）</summary>
    public bool IsSafetyInvolved { get; set; }

    /// <summary>是否需要召回 / 冻结</summary>
    public bool NeedRecall { get; set; }

    public string Status { get; set; } = ComplaintStatus.Pending;

    /// <summary>责任工程师</summary>
    public string Owner { get; set; } = "";

    /// <summary>要求回复客户的期限（按严重度自动推算）</summary>
    public DateTime DueDate { get; set; }

    /// <summary>当前 8D 步骤（见 <see cref="EightDStep"/>）</summary>
    public string CurrentStep { get; set; } = EightDStep.D1;

    public DateTime? ClosedTime { get; set; }
    public string ClosedBy { get; set; } = "";
    public DateTime? CustomerConfirmTime { get; set; }

    /// <summary>关联的保留单号（触发冻结后回填）</summary>
    public string RelatedHoldNo { get; set; } = "";

    // ---- 展示派生 ----

    public string ReceiveText => ReceiveTime == default ? "—" : ReceiveTime.ToString("yyyy-MM-dd HH:mm");
    public string DueText => DueDate == default ? "—" : DueDate.ToString("yyyy-MM-dd");
    public string ClosedText => ClosedTime is null ? "—" : ClosedTime.Value.ToString("yyyy-MM-dd HH:mm");
    public bool IsClosed => ComplaintStatus.IsClosed(Status);

    /// <summary>超期天数（>0 表示已超期；已关闭返回 0）</summary>
    public double OverdueDays => IsClosed || DueDate == default
        ? 0
        : Math.Max(0, (DateTime.Today - DueDate.Date).TotalDays);

    /// <summary>时限状态：已超期 / 即将到期 / 正常 / 已关闭</summary>
    public string DueHealth
    {
        get
        {
            if (IsClosed) return "已关闭";
            if (DueDate == default) return "正常";
            var left = (DueDate.Date - DateTime.Today).TotalDays;
            if (left < 0) return "已超期";
            return left <= 1 ? "即将到期" : "正常";
        }
    }

    public string DueHealthText => DueHealth == "已超期" ? $"超期 {OverdueDays:0} 天" : DueHealth;

    /// <summary>8D 进度文本，形如「3 / 8」</summary>
    public string StepProgress
    {
        get
        {
            var idx = EightDStep.IndexOf(CurrentStep);
            return idx < 0 ? "—" : $"{idx + 1} / {EightDStep.Order.Length}";
        }
    }

    public string SafetyText => IsSafetyInvolved ? "涉安全" : "—";
    public string RecallText => NeedRecall ? "需召回" : "—";
}

/// <summary>8D 报告：一条客诉一份，D1~D8 八个字段</summary>
public class EightDReport
{
    public string ComplaintNo { get; set; } = "";

    public string D1Team { get; set; } = "";
    public string D2Problem { get; set; } = "";
    public string D3Containment { get; set; } = "";
    public string D4RootCause { get; set; } = "";
    public string D5PermanentAction { get; set; } = "";
    public string D6Verification { get; set; } = "";
    public string D7Prevention { get; set; } = "";
    public string D8Closure { get; set; } = "";

    public DateTime CreatedTime { get; set; }
    public DateTime UpdatedTime { get; set; }
    public string UpdatedBy { get; set; } = "";

    /// <summary>按步骤取内容（未知步骤返回空串）</summary>
    public string ContentOf(string step) => step switch
    {
        EightDStep.D1 => D1Team,
        EightDStep.D2 => D2Problem,
        EightDStep.D3 => D3Containment,
        EightDStep.D4 => D4RootCause,
        EightDStep.D5 => D5PermanentAction,
        EightDStep.D6 => D6Verification,
        EightDStep.D7 => D7Prevention,
        EightDStep.D8 => D8Closure,
        _ => ""
    };

    /// <summary>按步骤写内容（未知步骤不写入，返回 false）</summary>
    public bool SetContentOf(string step, string content)
    {
        switch (step)
        {
            case EightDStep.D1: D1Team = content; return true;
            case EightDStep.D2: D2Problem = content; return true;
            case EightDStep.D3: D3Containment = content; return true;
            case EightDStep.D4: D4RootCause = content; return true;
            case EightDStep.D5: D5PermanentAction = content; return true;
            case EightDStep.D6: D6Verification = content; return true;
            case EightDStep.D7: D7Prevention = content; return true;
            case EightDStep.D8: D8Closure = content; return true;
            default: return false;
        }
    }

    /// <summary>已填写步骤数</summary>
    public int FilledCount => EightDStep.Order.Count(s => !string.IsNullOrWhiteSpace(ContentOf(s)));

    /// <summary>是否八步填齐</summary>
    public bool IsComplete => FilledCount == EightDStep.Order.Length;

    public string CompletionText => $"{FilledCount} / {EightDStep.Order.Length}";
    public string UpdatedText => UpdatedTime == default ? "—" : UpdatedTime.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>客诉处理动作（用于流转履历与按钮可用性判定）</summary>
public static class ComplaintAction
{
    public const string Create = "登记客诉";
    public const string Advance = "推进 8D";
    public const string EditStep = "修订 8D";
    public const string Freeze = "触发冻结";
    public const string Confirm = "客户确认";
    public const string Close = "关闭客诉";

    public static readonly string[] All = { Create, Advance, EditStep, Freeze, Confirm, Close };
}

/// <summary>客诉流转履历</summary>
public class ComplaintHistory
{
    public int Seq { get; set; }
    public string ComplaintNo { get; set; } = "";
    public string ActionType { get; set; } = ComplaintAction.Create;
    public string Step { get; set; } = "";
    public string Operator { get; set; } = "";
    public DateTime Time { get; set; }
    public string Remark { get; set; } = "";

    /// <summary>该动作涉及的电芯数（冻结时使用）</summary>
    public int CellCount { get; set; }

    /// <summary>SN 预览</summary>
    public string SnPreview { get; set; } = "";

    public string TimeText => Time == default ? "—" : Time.ToString("MM-dd HH:mm");
}

/// <summary>客诉统计行（按客户维度聚合，用于客诉 TOP 分析）</summary>
public class ComplaintStatRow
{
    public string CustomerName { get; init; } = "";
    public int Total { get; init; }
    public int CriticalCount { get; init; }
    public int OpenCount { get; init; }
    public int ClosedCount { get; init; }
    public int OverdueCount { get; init; }

    /// <summary>按期关闭率（%）</summary>
    public double CloseRate => Total == 0 ? 0 : Math.Round(ClosedCount * 100.0 / Total, 1);
    public string CloseRateText => $"{CloseRate:0.0}%";
}
