using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：主动预警与辅助决策页
//
// 与既有「预警中心」页的区别（界面上也要能让人一眼分清）：
//   预警中心：被动列表，把各模块"待处理事项"投影出来
//   本页：给**结论 + 建议动作**，回答"这批料会不会废""这台设备要不要提前修"
// ============================================================================

public partial class MainForm
{
    private static readonly Color InUrgent = Color.FromArgb(185, 28, 28);
    private static readonly Color InHigh = Color.FromArgb(220, 38, 38);
    private static readonly Color InMid = Color.FromArgb(217, 119, 6);
    private static readonly Color InLow = Color.FromArgb(21, 128, 61);

    private Label _insightSummary = null!;
    private Label _insightRules = null!;
    private DataGridView _gridExpiry = null!;
    private DataGridView _gridEquipment = null!;
    private DataGridView _gridSlow = null!;
    private InsightReport? _insightReport;

    private TabPage BuildInsightTab()
    {
        var page = new TabPage("主动预警与决策");

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(10, 8, 6, 0),
            WrapContents = false
        };
        var btnRefresh = MakeToolbarButton("🔄 重新分析", (_, _) => LoadInsights());
        btnRefresh.Width = 116;
        toolbar.Controls.Add(btnRefresh);

        var btnRules = MakeToolbarButton("⚙ 阈值配置", (_, _) => OpenInsightRules());
        btnRules.Width = 110;
        toolbar.Controls.Add(btnRules);

        _insightSummary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(30, 41, 59),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "正在分析…"
        };
        toolbar.Controls.Add(_insightSummary);

        _insightRules = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            AutoSize = false,
            Font = new Font("微软雅黑", 8.5F),
            ForeColor = Color.FromArgb(100, 116, 139),
            BackColor = Color.White,
            Padding = new Padding(10, 4, 10, 4),
            Text = "阈值：…"
        };

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("微软雅黑", 9F) };

        // ① 物料临期 × 消耗匹配
        var expPage = new TabPage("① 物料临期与消耗");
        expPage.Padding = new Padding(4);
        expPage.Controls.Add(MakeGridGroup(
            "临期批次处置判定（FIFO 模拟：效期内能消耗完吗？效期过后还有需求吗？）"
            + "　【可消耗完】按 FIFO 优先出库｜【建议预防性采购】效期后仍要用，提前备货｜【将过期报废】效期后无需求",
            out _gridExpiry));
        SetupExpiryColumns();
        tabs.TabPages.Add(expPage);

        // ② 设备异常集中度
        var eqPage = new TabPage("② 设备异常集中");
        eqPage.Padding = new Padding(4);
        eqPage.Controls.Add(MakeGridGroup(
            "设备风险判定（频次高 ≠ 要修；真正指向部件问题的是**集中度**）"
            + "　【集中度高】疑似单部件劣化 → 针对性维护｜【频次高但分散】整体老化 → 整机预防性维护｜【保养到期】先补保养",
            out _gridEquipment));
        SetupEquipmentColumns();
        tabs.TabPages.Add(eqPage);

        // ③ 呆滞料
        var slowPage = new TabPage("③ 呆滞料占压");
        slowPage.Padding = new Padding(4);
        slowPage.Controls.Add(MakeGridGroup(
            "呆滞判定：库存 − 未来工单需求 − 安全库存 > 0 的部分 = 占着钱但用不掉",
            out _gridSlow));
        SetupSlowColumns();
        tabs.TabPages.Add(slowPage);

        page.Controls.Add(tabs);        // Fill
        page.Controls.Add(_insightRules); // Top
        page.Controls.Add(toolbar);      // Top
        return page;
    }

    private void SetupExpiryColumns()
    {
        _gridExpiry.AutoGenerateColumns = false;
        _gridExpiry.Columns.Clear();
        _gridExpiry.Columns.Add(MakeTextCol("Level", "等级", true, 6));
        _gridExpiry.Columns.Add(MakeTextCol("MaterialName", "物料", true, 14));
        _gridExpiry.Columns.Add(MakeTextCol("LotNo", "批次", true, 16));
        _gridExpiry.Columns.Add(MakeTextCol("DaysText", "剩余天数", true, 8));
        _gridExpiry.Columns.Add(MakeTextCol("ExpiryText", "到期日", true, 10));
        _gridExpiry.Columns.Add(MakeTextCol("StockText", "库存", true, 10));
        _gridExpiry.Columns.Add(MakeTextCol("DailyDemand", "日均需求", true, 9));
        _gridExpiry.Columns.Add(MakeTextCol("ConsumableText", "效期内可消耗", true, 11));
        _gridExpiry.Columns.Add(MakeTextCol("LeftoverText", "到期剩余", true, 10));
        _gridExpiry.Columns.Add(MakeTextCol("Action", "处置判定", true, 12));
        _gridExpiry.Columns.Add(MakeTextCol("SuggestedBuyQty", "建议采购", true, 9));
        _gridExpiry.Columns.Add(MakeTextCol("WasteQty", "预计报废", true, 9));
        _gridExpiry.Columns.Add(MakeTextCol("ReasonText", "判断依据", true, 34));
        _gridExpiry.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 32));
        LevelFormat(_gridExpiry);
    }

    private void SetupEquipmentColumns()
    {
        _gridEquipment.AutoGenerateColumns = false;
        _gridEquipment.Columns.Clear();
        _gridEquipment.Columns.Add(MakeTextCol("Level", "等级", true, 6));
        _gridEquipment.Columns.Add(MakeTextCol("EquipmentName", "设备", true, 14));
        _gridEquipment.Columns.Add(MakeTextCol("EquipmentCode", "编码", true, 13));
        _gridEquipment.Columns.Add(MakeTextCol("Section", "所属段", true, 10));
        _gridEquipment.Columns.Add(MakeTextCol("AbnormalText", "异常次数", true, 9));
        _gridEquipment.Columns.Add(MakeTextCol("OpenCount", "未闭环", true, 8));
        _gridEquipment.Columns.Add(MakeTextCol("AlarmCount", "报警次数", true, 9));
        _gridEquipment.Columns.Add(MakeTextCol("ConcentrationText", "部件集中度", true, 18));
        _gridEquipment.Columns.Add(MakeTextCol("SubTypeText", "异常分布", true, 22));
        _gridEquipment.Columns.Add(MakeTextCol("MaintText", "距上次保养", true, 10));
        _gridEquipment.Columns.Add(MakeTextCol("TpmText", "点检到期", true, 10));
        _gridEquipment.Columns.Add(MakeTextCol("ReasonText", "判断依据", true, 34));
        _gridEquipment.Columns.Add(MakeTextCol("MaintItemsText", "建议维护项目", true, 26));
        _gridEquipment.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 32));
        LevelFormat(_gridEquipment);
    }

    private void SetupSlowColumns()
    {
        _gridSlow.AutoGenerateColumns = false;
        _gridSlow.Columns.Clear();
        _gridSlow.Columns.Add(MakeTextCol("Level", "等级", true, 6));
        _gridSlow.Columns.Add(MakeTextCol("MaterialName", "物料", true, 16));
        _gridSlow.Columns.Add(MakeTextCol("MaterialCode", "编码", true, 13));
        _gridSlow.Columns.Add(MakeTextCol("MaterialType", "类型", true, 7));
        _gridSlow.Columns.Add(MakeTextCol("StockQtyText", "当前库存", true, 10));
        _gridSlow.Columns.Add(MakeTextCol("FutureDemandText", "未来需求", true, 10));
        _gridSlow.Columns.Add(MakeTextCol("IdleQtyText", "占压量", true, 10));
        _gridSlow.Columns.Add(MakeTextCol("Grade", "分级", true, 8));
        _gridSlow.Columns.Add(MakeTextCol("ReasonText", "判断依据", true, 36));
        _gridSlow.Columns.Add(MakeTextCol("ActionText", "建议动作", true, 34));
        LevelFormat(_gridSlow);
    }

    /// <summary>等级列着色（紧急/高/中/低 四档）。</summary>
    private static void LevelFormat(DataGridView g)
    {
        g.CellFormatting -= (_, _) => { };
        g.CellFormatting += (_, e) =>
        {
            if (g.Rows.Count == 0 || e.RowIndex < 0) return;
            if (e.ColumnIndex != 0) return;
            var lv = g.Columns[0].DataPropertyName switch
            {
                "Level" => g.Rows[e.RowIndex].DataBoundItem?.GetType().GetProperty("Level")?.GetValue(g.Rows[e.RowIndex].DataBoundItem) as string,
                _ => null
            };
            e.CellStyle.Font = new Font(g.Font, FontStyle.Bold);
            e.CellStyle.ForeColor = lv switch
            {
                InsightLevels.Urgent => InUrgent,
                InsightLevels.High => InHigh,
                InsightLevels.Medium => InMid,
                _ => InLow
            };
        };
    }

    private void LoadInsights()
    {
        var r = MesDatabase.BuildInsightReport();
        _insightReport = r;

        _gridExpiry.DataSource = r.Expiry;
        _gridEquipment.DataSource = r.Equipment;
        _gridSlow.DataSource = r.SlowMoving;

        _insightSummary.Text = r.SummaryText;
        _insightRules.Text = "本次使用的阈值：" + r.Rules.Text
            + "　（阈值可在「⚙ 阈值配置」调整；结果只给建议动作，不代替人做处置决定）";
    }

    private void OpenInsightRules()
    {
        using var dlg = new InsightRulesForm();
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadInsights();
    }
}