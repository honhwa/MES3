using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：智能分析（六项分析的统一入口）
//
// 六项分三个业务域放置，**沿用既有导航**不新造分类：
//   生产管理 › 交期风险        ①
//   质量管理 › 根因交叉/调机建议/重复送检   ②③⑤
//   设备与人员 › 资质排班     ④
//   追溯与报表 › 异常闭环时长  ⑥
// ============================================================================

public partial class MainForm
{
    private static readonly Color AnMuted = Color.FromArgb(110, 125, 145);
    private static readonly Color AnOk = Color.FromArgb(21, 128, 61);
    private static readonly Color AnWarn = Color.FromArgb(217, 119, 6);
    private static readonly Color AnBad = Color.FromArgb(220, 38, 38);

    private Label _lblAnalyticsNote = null!;
    private AnalyticsReport? _analytics;

    /// <summary>① 工单交期风险（生产管理）</summary>
    private TabPage BuildDeliveryRiskTab()
    {
        var page = new TabPage("交期风险");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(
            "交期风险 = 剩余待产量的产能需求 vs「可开工日→交期日」的可用天数"
            + "（复用产能引擎口径：日产能 16h、单位工时取报工实绩、良率取实绩）"
            + "　【逾期】交期已过仍有活｜【预警】余量不足 15%｜【正常】按期可完成",
            out _gridDelivery));
        SetupDeliveryColumns();
        return page;
    }

    /// <summary>② 质量根因交叉（质量管理）</summary>
    private TabPage BuildRootCauseTab()
    {
        var page = new TabPage("根因交叉");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(
            "按「工序 × 设备 ×（班次/材料批次）」交叉统计不良　"
            + "⚠ TestRecord 无工序字段，靠 CellSn 反查工序实绩关联；样本 <3 条不进主排序；"
            + "⚠ 材料批次维度样本极薄，只展示不排序；本表给的是**集中项**不是根因，根因需人工验证",
            out _gridRootCause));
        SetupRootCauseColumns();
        return page;
    }

    /// <summary>③ SPC 调机建议（质量管理）</summary>
    private TabPage BuildSpcTuningTab()
    {
        var page = new TabPage("调机建议");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(
            "仅针对 Cpk < 1.33 的参数给建议；CV>8% 判「波动大」（指向机械/来料），否则判「均值偏」（指向设定值）"
            + "　Cpk 直接复用 SpcCalculator 的既有结果，不重算",
            out _gridSpcTuning));
        SetupSpcTuningColumns();
        return page;
    }

    /// <summary>④ 资质 × 排班（设备与人员）</summary>
    private TabPage BuildSkillScheduleTab()
    {
        var page = new TabPage("资质排班");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(
            "在岗人员的岗位所需资质 × 实际持证情况　"
            + "【缺资质】在岗无证｜【资质过期】不得独立顶岗｜【即将过期】30 天内到期，提前换证",
            out _gridSkillSchedule));
        SetupSkillScheduleColumns();
        return page;
    }

    /// <summary>⑤ 首检重复送检（质量管理）</summary>
    private TabPage BuildDuplicateFaiTab()
    {
        var page = new TabPage("重复送检");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(
            "同「型号 + 工单」多次送检提示　⚠ 首检后有工艺参数变更的重新送检属正常（不报警）",
            out _gridDuplicateFai));
        SetupDuplicateFaiColumns();
        return page;
    }

    /// <summary>⑥ 异常闭环时长（追溯与报表）</summary>
    private TabPage BuildAbnormalCycleTab()
    {
        var page = new TabPage("闭环时长");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(
            "异常从发生到闭环的时长分布　未闭环单独计数、不混进平均；阈值按大类可配（生产 48h/设备 72h/工艺 120h）",
            out _gridAbnormalCycle));
        SetupAbnormalCycleColumns();
        return page;
    }

    // ---------------- 列定义 ----------------

    private void SetupDeliveryColumns()
    {
        _gridDelivery.AutoGenerateColumns = false;
        _gridDelivery.Columns.Clear();
        _gridDelivery.Columns.Add(MakeTextCol("Risk", "风险", true, 6));
        _gridDelivery.Columns.Add(MakeTextCol("WoNo", "工单", true, 17));
        _gridDelivery.Columns.Add(MakeTextCol("ProductModel", "型号", true, 15));
        _gridDelivery.Columns.Add(MakeTextCol("RemainingText", "剩余", true, 8));
        _gridDelivery.Columns.Add(MakeTextCol("RequiredHoursText", "所需产能", true, 10));
        _gridDelivery.Columns.Add(MakeTextCol("DaysText", "需/可用天数", true, 16));
        _gridDelivery.Columns.Add(MakeTextCol("GapText", "产能缺口", true, 10));
        _gridDelivery.Columns.Add(MakeTextCol("AdviseDaysText", "建议提前", true, 9));
        _gridDelivery.Columns.Add(MakeTextCol("ReasonText", "判断依据", true, 38));
        _gridDelivery.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 32));
        _gridDelivery.Columns.Add(MakeTextCol("Evidence", "数据来源", true, 34));
    }

    private void SetupRootCauseColumns()
    {
        _gridRootCause.AutoGenerateColumns = false;
        _gridRootCause.Columns.Clear();
        _gridRootCause.Columns.Add(MakeTextCol("GroupKind", "交叉维度", true, 12));
        _gridRootCause.Columns.Add(MakeTextCol("Dimension", "组合", true, 26));
        _gridRootCause.Columns.Add(MakeTextCol("BadRateText", "不良率", true, 14));
        _gridRootCause.Columns.Add(MakeTextCol("TotalText", "样本", true, 7));
        _gridRootCause.Columns.Add(MakeTextCol("DowntimeText", "关联停机", true, 10));
        _gridRootCause.Columns.Add(MakeTextCol("ConfidentText", "可下结论", true, 9));
        _gridRootCause.Columns.Add(MakeTextCol("Note", "样本说明", true, 28));
        _gridRootCause.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 32));
    }

    private void SetupSpcTuningColumns()
    {
        _gridSpcTuning.AutoGenerateColumns = false;
        _gridSpcTuning.Columns.Clear();
        _gridSpcTuning.Columns.Add(MakeTextCol("Priority", "优先级", true, 9));
        _gridSpcTuning.Columns.Add(MakeTextCol("ItemName", "参数", true, 13));
        _gridSpcTuning.Columns.Add(MakeTextCol("CpkText", "Cpk", true, 7));
        _gridSpcTuning.Columns.Add(MakeTextCol("CapabilityGrade", "能力分档", true, 24));
        _gridSpcTuning.Columns.Add(MakeTextCol("StatText", "统计量", true, 24));
        _gridSpcTuning.Columns.Add(MakeTextCol("Diagnose", "判因", true, 30));
        _gridSpcTuning.Columns.Add(MakeTextCol("DirectionsText", "调机方向", true, 40));
        _gridSpcTuning.Columns.Add(MakeTextCol("PendingVersionText", "版本提示", true, 28));
        _gridSpcTuning.Columns.Add(MakeTextCol("VerifyMethod", "验证方式", true, 34));
    }

    private void SetupSkillScheduleColumns()
    {
        _gridSkillSchedule.AutoGenerateColumns = false;
        _gridSkillSchedule.Columns.Clear();
        _gridSkillSchedule.Columns.Add(MakeTextCol("Kind", "冲突类型", true, 9));
        _gridSkillSchedule.Columns.Add(MakeTextCol("EmpNo", "工号", true, 9));
        _gridSkillSchedule.Columns.Add(MakeTextCol("EmpName", "姓名", true, 9));
        _gridSkillSchedule.Columns.Add(MakeTextCol("Dept", "部门", true, 10));
        _gridSkillSchedule.Columns.Add(MakeTextCol("Position", "岗位", true, 14));
        _gridSkillSchedule.Columns.Add(MakeTextCol("Crew", "班组", true, 7));
        _gridSkillSchedule.Columns.Add(MakeTextCol("SkillItem", "所需资质", true, 16));
        _gridSkillSchedule.Columns.Add(MakeTextCol("ExpiryText", "有效期", true, 22));
        _gridSkillSchedule.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 34));
    }

    private void SetupDuplicateFaiColumns()
    {
        _gridDuplicateFai.AutoGenerateColumns = false;
        _gridDuplicateFai.Columns.Clear();
        _gridDuplicateFai.Columns.Add(MakeTextCol("ProductModel", "型号", true, 16));
        _gridDuplicateFai.Columns.Add(MakeTextCol("WoNo", "工单", true, 17));
        _gridDuplicateFai.Columns.Add(MakeTextCol("TimesText", "送检次数", true, 9));
        _gridDuplicateFai.Columns.Add(MakeTextCol("FaiNosText", "首检单号", true, 26));
        _gridDuplicateFai.Columns.Add(MakeTextCol("LastTimeText", "最近一次", true, 16));
        _gridDuplicateFai.Columns.Add(MakeTextCol("ParamChangedText", "参数有变更", true, 10));
        _gridDuplicateFai.Columns.Add(MakeTextCol("Reason", "判断说明", true, 40));
        _gridDuplicateFai.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 32));
    }

    private void SetupAbnormalCycleColumns()
    {
        _gridAbnormalCycle.AutoGenerateColumns = false;
        _gridAbnormalCycle.Columns.Clear();
        _gridAbnormalCycle.Columns.Add(MakeTextCol("Category", "大类", true, 10));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("SubType", "细分项", true, 16));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("CountText", "总数", true, 7));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("ClosedText", "已闭环", true, 8));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("StillOpenText", "未闭环", true, 9));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("AvgText", "平均时长", true, 10));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("MedianText", "中位时长", true, 10));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("MaxText", "最长", true, 9));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("LimitText", "超时阈值", true, 10));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("OverdueText", "超时条数", true, 10));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("SlowestText", "最慢环节", true, 10));
        _gridAbnormalCycle.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 40));
    }

    // ---------------- 刷新 ----------------

    private void LoadAnalytics()
    {
        var r = MesDatabase.BuildAnalyticsReport();
        _analytics = r;

        _gridDelivery.DataSource = r.Delivery;
        _gridRootCause.DataSource = r.RootCause;
        _gridSpcTuning.DataSource = r.SpcTuning;
        _gridSkillSchedule.DataSource = r.SkillSchedule;
        _gridDuplicateFai.DataSource = r.DuplicateFai;
        _gridAbnormalCycle.DataSource = r.AbnormalCycle;

        if (_lblAnalyticsNote is not null)
            _lblAnalyticsNote.Text = string.Join("　｜　", r.CoverageNotes);
    }

    /// <summary>数字着色（等级/风险列）</summary>
    private void AnalyticsRiskFormat(DataGridView g)
    {
        g.CellFormatting += (_, e) =>
        {
            if (g.Rows.Count == 0 || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var v = g.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? "";
            e.CellStyle.Font = new Font(g.Font, FontStyle.Bold);
            e.CellStyle.ForeColor = v switch
            {
                DeliveryRisk.Overdue or SkillConflictKind.Expired or SpcTuningPriority.P1 => AnBad,
                DeliveryRisk.Warn or SkillConflictKind.Missing or SpcTuningPriority.P2 => AnWarn,
                SkillConflictKind.Expiring or SpcTuningPriority.P3 => AnOk,
                _ => AnMuted
            };
        };
    }
}
