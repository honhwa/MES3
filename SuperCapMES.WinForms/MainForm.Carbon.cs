using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：碳排放核算（阶段二）
//
// 页面组织：一级分类「能源与碳」下新增两个二级页
//   ③ 碳排放核算   组织/项目边界自选 + 范围一二三拆分 + 排放源清单逐项留痕
//   ④ 排放因子台账 版本化因子列表（含来源与生效年份）
//
// 核心口径（界面上必须让人看见，否则等于没标）：
//   · 排放量 = 活动数据 × 因子，纯函数可手算复算
//   · 因子按「生效年份 ≤ 账期年份」取最新一条 → 换因子后历史核算结果不变
//   · 当前因子为**模拟园区口径**，正式使用须替换为当地发布值
// ============================================================================

public partial class MainForm
{
    private static readonly Color CbInk = Color.FromArgb(30, 41, 59);
    private static readonly Color CbGray = Color.FromArgb(110, 125, 145);
    private static readonly Color CbOk = Color.FromArgb(21, 128, 61);
    private static readonly Color CbWarn = Color.FromArgb(217, 119, 6);

    // 碳核算（三个网格字段在 MainForm.cs 声明，避免与其它分部类重复）
    private ComboBox _cboCarbonBoundary = null!;
    private ComboBox _cboCarbonPeriodFrom = null!;
    private ComboBox _cboCarbonPeriodTo = null!;
    private Label _lblCarbonTotal = null!;
    private Label _lblCarbonNote = null!;

    // 因子台账（_gridFactor 在 MainForm.cs 声明）

    // ==================== ③ 碳排放核算 ====================

    private TabPage BuildCarbonTab()
    {
        var page = new TabPage("碳排放核算");
        page.Padding = new Padding(4);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("总排放量", Color.FromArgb(21, 128, 61), out _lblCarbonTotal));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 76, Padding = new Padding(6, 6, 4, 0), WrapContents = true
        };
        bar.Controls.Add(new Label { Text = "核算边界：", AutoSize = true, Margin = new Padding(0, 8, 2, 0) });
        _cboCarbonBoundary = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Margin = new Padding(2, 4, 0, 0) };
        FillCarbonBoundaries();
        bar.Controls.Add(_cboCarbonBoundary);

        bar.Controls.Add(new Label { Text = "账期：", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        _cboCarbonPeriodFrom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 96, Margin = new Padding(2, 4, 0, 0) };
        _cboCarbonPeriodTo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 96, Margin = new Padding(2, 4, 0, 0) };
        FillCarbonPeriods();
        bar.Controls.Add(_cboCarbonPeriodFrom);
        bar.Controls.Add(new Label { Text = "至", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
        bar.Controls.Add(_cboCarbonPeriodTo);

        bar.Controls.Add(MakeToolbarButton("重新核算", (_, _) => LoadCarbon()));
        var btnReset = MakeToolbarButton("重置", (_, _) =>
        {
            _cboCarbonBoundary.SelectedIndex = 0;
            _cboCarbonPeriodFrom.SelectedIndex = 0;
            _cboCarbonPeriodTo.SelectedIndex = _cboCarbonPeriodTo.Items.Count - 1;
            LoadCarbon();
        });
        bar.Controls.Add(btnReset);

        _lblCarbonNote = new Label
        {
            Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 4, 8, 4),
            ForeColor = CbWarn, Font = new Font("微软雅黑", 8.5F),
            Text = CarbonDisclaimers.FactorNote
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 210, BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("分范围汇总（范围一/二/三）", out _gridCarbonScope));
        split.Panel2.Controls.Add(MakeGridGroup(
            "排放源清单（逐项：计量点 / 活动数据 / 使用的因子 / 排放量；按排放量降序）", out _gridCarbonItem));

        page.Controls.Add(split);
        page.Controls.Add(bar);
        page.Controls.Add(cards);
        page.Controls.Add(_lblCarbonNote);

        SetupCarbonColumns();
        page.Enter -= CarbonTab_Enter;
        page.Enter += CarbonTab_Enter;
        LoadCarbon();
        return page;
    }

    private void SetupCarbonColumns()
    {
        _gridCarbonScope.AutoGenerateColumns = false;
        _gridCarbonScope.Columns.Clear();
        _gridCarbonScope.Columns.Add(MakeTextCol("ScopeText", "范围", true, 20));
        _gridCarbonScope.Columns.Add(MakeTextCol("EmissionTText", "排放量", true, 14));
        _gridCarbonScope.Columns.Add(MakeTextCol("RatioText", "占比", true, 9));
        _gridCarbonScope.Columns.Add(MakeTextCol("ItemCount", "计量点数", true, 10));

        _gridCarbonItem.AutoGenerateColumns = false;
        _gridCarbonItem.Columns.Clear();
        _gridCarbonItem.Columns.Add(MakeTextCol("ScopeText", "范围", true, 20));
        _gridCarbonItem.Columns.Add(MakeTextCol("MeterName", "计量点", true, 20));
        _gridCarbonItem.Columns.Add(MakeTextCol("TypeName", "能源", true, 8));
        _gridCarbonItem.Columns.Add(MakeTextCol("ActivityText", "活动数据", true, 14));
        _gridCarbonItem.Columns.Add(MakeTextCol("FactorText", "使用因子", true, 17));
        _gridCarbonItem.Columns.Add(MakeTextCol("FactorSource", "因子来源", true, 30));
        _gridCarbonItem.Columns.Add(MakeTextCol("EmissionKgText", "排放量", true, 15));
        _gridCarbonItem.Columns.Add(MakeTextCol("PeriodCountText", "账期数", true, 9));
        _gridCarbonItem.Columns.Add(MakeTextCol("FactorId", "因子编号", true, 15));
    }

    private void FillCarbonBoundaries()
    {
        if (_cboCarbonBoundary is null) return;
        _cboCarbonBoundary.Items.Clear();
        foreach (var (b, v, t) in MesDatabase.CarbonBoundaryOptions())
            _cboCarbonBoundary.Items.Add(new CarbonBoundaryItem { Boundary = b, Value = v, Text = t });
        if (_cboCarbonBoundary.Items.Count > 0) _cboCarbonBoundary.SelectedIndex = 0;
    }

    private void FillCarbonPeriods()
    {
        if (_cboCarbonPeriodFrom is null || _cboCarbonPeriodTo is null) return;
        var periods = MesDatabase.EnergyReadings.Select(r => r.Period).Distinct().OrderBy(p => p).ToList();
        foreach (var c in new[] { _cboCarbonPeriodFrom, _cboCarbonPeriodTo })
        {
            c.Items.Clear();
            c.Items.Add("全部");
            foreach (var p in periods) c.Items.Add(p);
        }
        _cboCarbonPeriodFrom.SelectedIndex = 0;
        _cboCarbonPeriodTo.SelectedIndex = _cboCarbonPeriodTo.Items.Count - 1;
    }

    private sealed class CarbonBoundaryItem
    {
        public string Boundary { get; init; } = "";
        public string Value { get; init; } = "";
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    private void CarbonTab_Enter(object? sender, EventArgs e) => EnsureCarbonReady();

    private void EnsureCarbonReady()
    {
        MesDatabase.EnsureCarbonSeed();
        // ⚠ 条件是 <=1 而不是 ==0：构造期 EnergyMeters 还没建，只填进了「全厂」一项，
        //   用 ==0 判断会漏掉只填了一半的情况（本项目构造期绑定未物化的老坑）。
        if (_cboCarbonBoundary is not null && _cboCarbonBoundary.Items.Count <= 1) FillCarbonBoundaries();
        if (_cboCarbonPeriodFrom is not null && _cboCarbonPeriodFrom.Items.Count <= 1) FillCarbonPeriods();
        if (_gridCarbonItem is not null && _gridCarbonItem.Rows.Count == 0) LoadCarbon();
    }

    /// <summary>按当前选择重新核算。</summary>
    private void LoadCarbon()
    {
        if (_gridCarbonItem is null || _gridCarbonScope is null) return;
        MesDatabase.EnsureCarbonSeed();

        var sel = _cboCarbonBoundary?.SelectedItem as CarbonBoundaryItem;
        var boundary = sel?.Boundary ?? EnergyScopes.Plant;
        var value = sel?.Value ?? "";
        var from = _cboCarbonPeriodFrom?.SelectedItem?.ToString() is "全部" or null ? null : _cboCarbonPeriodFrom!.SelectedItem!.ToString();
        var to = _cboCarbonPeriodTo?.SelectedItem?.ToString() is "全部" or null ? null : _cboCarbonPeriodTo!.SelectedItem!.ToString();

        var inv = MesDatabase.BuildCarbonInventory(boundary, value, from, to);

        _gridCarbonScope.DataSource = null;
        _gridCarbonScope.DataSource = inv.Scopes;
        _gridCarbonItem.DataSource = null;
        _gridCarbonItem.DataSource = inv.Items;

        if (_lblCarbonTotal is not null)
        {
            _lblCarbonTotal.Text = inv.TotalTText;
            _lblCarbonTotal.ForeColor = inv.TotalKg > 0 ? CbOk : CbWarn;
        }
        if (_lblCarbonNote is not null)
        {
            var noFactor = inv.Items.Count(i => i.Scope == "未匹配因子");
            _lblCarbonNote.Text =
                $"边界 {inv.BoundaryText}　账期 {inv.PeriodText}　共 {inv.ItemCount} 个计量点"
                + (noFactor > 0 ? $"　⚠ {noFactor} 项无可用因子未计入（不当作 0 排放）" : "")
                + $"　{inv.Disclaimer}";
            _lblCarbonNote.ForeColor = noFactor > 0 ? CbWarn : CbGray;
        }
    }

    // ==================== ④ 排放因子台账 ====================

    private TabPage BuildCarbonFactorTab()
    {
        var page = new TabPage("排放因子台账");
        page.Padding = new Padding(4);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false
        };
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => LoadFactors()));
        bar.Controls.Add(new Label
        {
            Text = "因子版本化：同一种能源可有多条（不同生效年份）；核算时按「生效年份 ≤ 账期年份」取最新一条，"
                 + "因此新的一年加新因子不会改变历史账期的核算结果",
            AutoSize = true, ForeColor = CbGray, Margin = new Padding(10, 8, 0, 0)
        });

        page.Controls.Add(MakeGridGroup(
            "排放因子台账（含来源与生效年份；当前为模拟园区口径，正式使用须替换为当地发布值）", out _gridFactor));

        SetupFactorColumns();
        page.Enter -= CarbonFactorTab_Enter;
        page.Enter += CarbonFactorTab_Enter;
        LoadFactors();
        return page;
    }

    private void CarbonFactorTab_Enter(object? sender, EventArgs e)
    {
        MesDatabase.EnsureCarbonSeed();
        if (_gridFactor is not null && _gridFactor.Rows.Count == 0) LoadFactors();
    }

    private void SetupFactorColumns()
    {
        _gridFactor.AutoGenerateColumns = false;
        _gridFactor.Columns.Clear();
        _gridFactor.Columns.Add(MakeTextCol("FactorId", "因子编号", true, 16));
        _gridFactor.Columns.Add(MakeTextCol("TypeName", "能源", true, 9));
        _gridFactor.Columns.Add(MakeTextCol("ScopeText", "排放范围", true, 22));
        _gridFactor.Columns.Add(MakeTextCol("ValueText", "因子值", true, 18));
        _gridFactor.Columns.Add(MakeTextCol("YearText", "生效", true, 11));
        _gridFactor.Columns.Add(MakeTextCol("IsActiveText", "启用", true, 7));
        _gridFactor.Columns.Add(MakeTextCol("Source", "来源", true, 48));
        _gridFactor.Columns.Add(MakeTextCol("CreatedAtText", "创建时间", true, 17));
    }

    private void LoadFactors()
    {
        if (_gridFactor is null) return;
        MesDatabase.EnsureCarbonSeed();
        _gridFactor.DataSource = null;
        _gridFactor.DataSource = MesDatabase.GetEmissionFactors();
    }
}