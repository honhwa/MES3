using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：碳资产管理（阶段六，纯演示）
//
// 页面：「能源与碳」下新增一页
//   ⑨ 碳资产管理   碳配额台账 + CCER 台账 + 履约测算看板
//
// 核心口径（界面必须可见）：
//   · 配额/CCER 是"持有量变动"流水，按方向算运行余额
//   · 履约 = 实际排放（阶段二）vs 配额持有 + 可用 CCER 抵消 → 缺口/盈余/成本
//   · CCER 抵消有比例上限（示范 5%），超出部分只能买配额
// ============================================================================

public partial class MainForm
{
    private static readonly Color AsInk = Color.FromArgb(30, 41, 59);
    private static readonly Color AsGray = Color.FromArgb(110, 125, 145);
    private static readonly Color AsOk = Color.FromArgb(21, 128, 61);
    private static readonly Color AsWarn = Color.FromArgb(200, 120, 20);

    // （_gridAllowance / _gridCcer 在 MainForm.cs 声明）
    private ComboBox _cboAssetYear = null!;
    private ComboBox _cboAssetRatio = null!;
    private Label _lblAssetYear = null!;
    private Label _lblAssetAllowBal = null!;
    private Label _lblAssetCcerBal = null!;
    private Label _lblAssetEmission = null!;
    private Label _lblAssetStatus = null!;
    private Label _lblAssetCost = null!;
    private Label _lblAssetNote = null!;

    /// <summary>
    /// CCER 抵消比例候选值（纯演示试算用）。第 1 项必须是规则默认 5%（下拉默认选中它）。
    /// </summary>
    private static readonly double[] RatioOptions = { 0.05, 0.03, 0.08, 0.10, 0.0 };

    private TabPage BuildCarbonAssetTab()
    {
        var page = new TabPage("碳资产管理");
        page.Padding = new Padding(4);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4), WrapContents = true };
        cards.Controls.Add(MakeCard("履约年度", Color.FromArgb(37, 99, 235), out _lblAssetYear));
        cards.Controls.Add(MakeCard("可用于履约配额", AsOk, out _lblAssetAllowBal));
        cards.Controls.Add(MakeCard("CCER 可用余量", AsOk, out _lblAssetCcerBal));
        cards.Controls.Add(MakeCard("年度实际排放", AsInk, out _lblAssetEmission));
        cards.Controls.Add(MakeCard("履约状态", AsOk, out _lblAssetStatus));
        cards.Controls.Add(MakeCard("履约成本估算", AsWarn, out _lblAssetCost));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 6, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "年度：", AutoSize = true, Margin = new Padding(4, 8, 2, 0) });
        _cboAssetYear = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, Margin = new Padding(2, 4, 0, 0) };
        foreach (var y in MesDatabase.CarbonAssetYears()) _cboAssetYear.Items.Add(y);
        if (_cboAssetYear.Items.Count > 0) _cboAssetYear.SelectedIndex = _cboAssetYear.Items.Count - 1;
        _cboAssetYear.SelectedIndexChanged += (_, _) => LoadCarbonAsset();
        bar.Controls.Add(_cboAssetYear);

        // CCER 抵消比例：默认 5% 为政策口径，其余为 what-if 试算（纯计算，不改台账）
        bar.Controls.Add(new Label { Text = " CCER 抵消比例：", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        _cboAssetRatio = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, Margin = new Padding(2, 4, 0, 0) };
        foreach (var r in RatioOptions)
            _cboAssetRatio.Items.Add($"{r * 100:0.##}%" + (Math.Abs(r - CarbonAssetRules.CcerOffsetRatio) < 1e-9 ? "（默认）" : ""));
        _cboAssetRatio.SelectedIndex = 0;
        _cboAssetRatio.SelectedIndexChanged += (_, _) => LoadCarbonAsset();
        bar.Controls.Add(_cboAssetRatio);
        bar.Controls.Add(MakeToolbarButton("恢复默认比例", (_, _) => ResetAssetRatio()));

        bar.Controls.Add(MakeToolbarButton("新增配额", (_, _) => BtnAddAllowance()));
        bar.Controls.Add(MakeToolbarButton("新增 CCER", (_, _) => BtnAddCcer()));
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => LoadCarbonAsset()));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 240, BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup(
            "碳配额台账（政府发放 / 市场交易 / 履约清缴；流入+ 流出-）", out _gridAllowance));
        split.Panel2.Controls.Add(MakeGridGroup(
            "CCER 台账（项目签发 / 市场交易 / 履约抵消；流入+ 流出-）", out _gridCcer));

        _lblAssetNote = new Label
        {
            Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8, 4, 8, 4),
            ForeColor = AsGray, Font = new Font("微软雅黑", 8.5F),
            Text = "碳资产台账为演示数据（内存，重启即重置）。CCER 履约抵消比例上限示范 5%，正式以主管部门规定为准；"
                 + "市场价格与年度实际排放为模拟值，实际应对接碳市场行情与经核查的排放核算结果。"
        };

        page.Controls.Add(split);
        page.Controls.Add(bar);
        page.Controls.Add(cards);
        page.Controls.Add(_lblAssetNote);

        SetupCarbonAssetColumns();
        page.Enter -= CarbonAssetTab_Enter;
        page.Enter += CarbonAssetTab_Enter;
        LoadCarbonAsset();
        return page;
    }

    private void SetupCarbonAssetColumns()
    {
        _gridAllowance.AutoGenerateColumns = false;
        _gridAllowance.Columns.Clear();
        _gridAllowance.Columns.Add(MakeTextCol("Seq", "序号", true, 6));
        _gridAllowance.Columns.Add(MakeTextCol("Year", "年度", true, 8));
        _gridAllowance.Columns.Add(MakeTextCol("TxType", "类型", true, 14));
        _gridAllowance.Columns.Add(MakeTextCol("QtyText", "数量(t)", true, 12));
        _gridAllowance.Columns.Add(MakeTextCol("PriceText", "单价", true, 12));
        _gridAllowance.Columns.Add(MakeTextCol("AmountText", "金额变动", true, 14));
        _gridAllowance.Columns.Add(MakeTextCol("DateText", "日期", true, 12));
        _gridAllowance.Columns.Add(MakeTextCol("BalanceText", "结余(t)", true, 12));
        _gridAllowance.Columns.Add(MakeTextCol("Note", "备注", true, 30));

        _gridCcer.AutoGenerateColumns = false;
        _gridCcer.Columns.Clear();
        _gridCcer.Columns.Add(MakeTextCol("Seq", "序号", true, 6));
        _gridCcer.Columns.Add(MakeTextCol("Year", "年度", true, 8));
        _gridCcer.Columns.Add(MakeTextCol("Project", "来源/项目", true, 20));
        _gridCcer.Columns.Add(MakeTextCol("TxType", "类型", true, 14));
        _gridCcer.Columns.Add(MakeTextCol("QtyText", "数量(t)", true, 12));
        _gridCcer.Columns.Add(MakeTextCol("PriceText", "单价", true, 12));
        _gridCcer.Columns.Add(MakeTextCol("AmountText", "金额变动", true, 14));
        _gridCcer.Columns.Add(MakeTextCol("DateText", "日期", true, 12));
        _gridCcer.Columns.Add(MakeTextCol("BalanceText", "结余(t)", true, 12));
        _gridCcer.Columns.Add(MakeTextCol("Note", "备注", true, 28));
    }

    private void CarbonAssetTab_Enter(object? sender, EventArgs e)
    {
        MesDatabase.EnsureCarbonAssetSeed();
        if (_cboAssetYear is not null && _cboAssetYear.Items.Count == 0)
        {
            foreach (var y in MesDatabase.CarbonAssetYears()) _cboAssetYear.Items.Add(y);
            if (_cboAssetYear.Items.Count > 0) _cboAssetYear.SelectedIndex = _cboAssetYear.Items.Count - 1;
        }
        LoadCarbonAsset();
    }

    private string CurrentAssetYear() => _cboAssetYear?.SelectedItem?.ToString() ?? "";

    /// <summary>当前选中的抵消比例；下拉未初始化时回落规则默认值。</summary>
    private double CurrentAssetRatio()
    {
        if (_cboAssetRatio is null || _cboAssetRatio.SelectedIndex < 0) return CarbonAssetRules.CcerOffsetRatio;
        var i = _cboAssetRatio.SelectedIndex;
        return i < RatioOptions.Length ? RatioOptions[i] : CarbonAssetRules.CcerOffsetRatio;
    }

    private void ResetAssetRatio()
    {
        if (_cboAssetRatio is null) return;
        _cboAssetRatio.SelectedIndex = 0;   // 触发 SelectedIndexChanged → 自动重算
    }

    private void LoadCarbonAsset()
    {
        if (_gridAllowance is null || _gridCcer is null) return;
        var year = CurrentAssetYear();
        if (year.Length == 0)
        {
            var ys = MesDatabase.CarbonAssetYears();
            if (ys.Count == 0) return;
            year = ys[^1];
        }

        var allow = MesDatabase.GetAllowances(year);
        _gridAllowance.DataSource = null;
        _gridAllowance.DataSource = allow;

        var ccer = MesDatabase.GetCcerRecords(year);
        _gridCcer.DataSource = null;
        _gridCcer.DataSource = ccer;

        var view = MesDatabase.BuildComplianceView(year, CurrentAssetRatio());
        _lblAssetYear.Text = view.Year;
        _lblAssetAllowBal.Text = view.AllowanceText;
        _lblAssetCcerBal.Text = view.CcerText;
        _lblAssetEmission.Text = view.ActualText;
        _lblAssetStatus.Text = view.StatusText;
        _lblAssetStatus.ForeColor = view.Compliant ? AsOk : AsWarn;
        _lblAssetCost.Text = view.CostText;

        // 比例非默认时明确标注是 what-if 试算，避免把试算结果当成政策口径对外汇报
        var isDefault = Math.Abs(view.CcerOffsetRatioUsed - CarbonAssetRules.CcerOffsetRatio) < 1e-9;
        _lblAssetNote.Text = isDefault
            ? $"履约口径：实际排放 {_lblAssetEmission.Text}，先用配额抵（已用 {view.AllowanceUsedT:0.##} t），"
              + $"余量再用 CCER 抵（上限 {view.RatioText}，本次可用 {view.CcerUsableT:0.##} t）。"
              + "　碳资产台账为演示数据（内存，重启即重置）；市场价格与排放为模拟值，正式须对接碳市场行情与经核查的核算结果。"
            : $"⚠ 当前为 what-if 试算：CCER 抵消上限按 {view.RatioText} 计算（非默认 5% 政策口径），"
              + $"结果仅供比较不同政策情景，不作为履约申报依据。";
        _lblAssetNote.ForeColor = isDefault ? AsGray : AsWarn;
    }

    private void BtnAddAllowance()
    {
        MesDatabase.EnsureCarbonAssetSeed();
        var years = MesDatabase.CarbonAssetYears();
        using var dlg = new CarbonAssetEntryForm("A", CurrentAssetYear(), years);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var (ok, msg) = MesDatabase.AddAllowance(dlg.Year, dlg.TxType, dlg.Quantity, dlg.UnitPrice, dlg.TxDate, dlg.Note);
        if (!ok) { MessageBox.Show(msg, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        SyncAssetYear(dlg.Year);
        LoadCarbonAsset();
    }

    private void BtnAddCcer()
    {
        MesDatabase.EnsureCarbonAssetSeed();
        var years = MesDatabase.CarbonAssetYears();
        using var dlg = new CarbonAssetEntryForm("C", CurrentAssetYear(), years);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var (ok, msg) = MesDatabase.AddCcer(dlg.Year, dlg.Project, dlg.TxType, dlg.Quantity, dlg.UnitPrice, dlg.TxDate, dlg.Note);
        if (!ok) { MessageBox.Show(msg, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        SyncAssetYear(dlg.Year);
        LoadCarbonAsset();
    }

    /// <summary>录入后若年度是新的一年，补进下拉并选中。</summary>
    private void SyncAssetYear(string year)
    {
        if (_cboAssetYear is null) return;
        if (_cboAssetYear.Items.Cast<string>().All(x => x != year))
            _cboAssetYear.Items.Add(year);
        _cboAssetYear.SelectedItem = year;
    }
}
