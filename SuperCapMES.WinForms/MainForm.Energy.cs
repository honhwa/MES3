using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：能源管理（阶段一）—— 能源台账 + 能耗查询
//
// 页面组织：新增一级分类「能源与碳」，下设两个二级页（阶段一只做这两个，
// 碳核算/足迹/供应链/核查/资产属阶段二~六）。
//   ① 能源台账   计量点管理 + 读数录入（含录入弹窗）
//   ② 能耗查询   时间 × 区域 × 线体 × 设备 × 能源 多维查询 + 汇总报表
//
// 组织边界（全厂 / 线体 / 设备）由用户在查询页自选，不写死。
// ============================================================================

public partial class MainForm
{
    private static readonly Color EnInk = Color.FromArgb(30, 41, 59);
    private static readonly Color EnGray = Color.FromArgb(110, 125, 145);
    private static readonly Color EnOk = Color.FromArgb(21, 128, 61);
    private static readonly Color EnWarn = Color.FromArgb(217, 119, 6);

    // 能源台账（_gridMeter / _gridEnergyReading / _gridEnergySummary 在 MainForm.cs 声明）
    private TextBox _txtMeterKeyword = null!;
    private ComboBox _cboMeterType = null!;

    // 能耗查询（_gridEnergyQuery 与台账页的 _gridEnergyReading 分开，避免互相覆盖）
    private ComboBox _cboEnergyType = null!;
    private ComboBox _cboEnergyScope = null!;
    private ComboBox _cboEnergyLine = null!;
    private ComboBox _cboEnergySection = null!;
    private ComboBox _cboEnergyPeriodFrom = null!;
    private ComboBox _cboEnergyPeriodTo = null!;
    private ComboBox _cboEnergyGroup = null!;
    private Label _lblEnergyNote = null!;

    // ==================== ① 能源台账 ====================

    private TabPage BuildEnergyMeterTab()
    {
        var page = new TabPage("能源台账");
        page.Padding = new Padding(4);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "能源：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboMeterType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(2, 4, 0, 0) };
        _cboMeterType.Items.Add("全部");
        foreach (var t in MesDatabase.EnergyTypeDefs) _cboMeterType.Items.Add(t.Name);
        _cboMeterType.SelectedIndex = 0;
        bar.Controls.Add(_cboMeterType);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
        _txtMeterKeyword = new TextBox { Width = 180, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtMeterKeyword);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => LoadEnergyMeters()));
        var btnReset = MakeToolbarButton("重置", (_, _) =>
        {
            _cboMeterType.SelectedIndex = 0;
            _txtMeterKeyword.Text = "";
            LoadEnergyMeters();
        });
        bar.Controls.Add(btnReset);

        var btnEntry = MakeToolbarButton("录入抄表数", BtnEnergyEntry);
        btnEntry.Width = 106;
        bar.Controls.Add(btnEntry);

        bar.Controls.Add(new Label
        {
            Text = "三级计量点：全厂总表 / 线体级 / 设备级；演示数据每次启动按当前日期补生成最近 6 个月",
            AutoSize = true, ForeColor = EnGray, Margin = new Padding(10, 8, 0, 0)
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 260, BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("计量点台账", out _gridMeter));
        split.Panel2.Controls.Add(MakeGridGroup(
            "能耗读数（当前选中计量点；账期降序；同账期重复录入会覆盖修正）", out _gridEnergyReading));

        page.Controls.Add(split);
        page.Controls.Add(bar);

        SetupMeterColumns();
        SetupEnergyReadingColumns();
        WireMeterSelection();
        page.Enter -= EnergyMeterTab_Enter;
        page.Enter += EnergyMeterTab_Enter;
        LoadEnergyMeters();
        return page;
    }

    private void SetupMeterColumns()
    {
        _gridMeter.AutoGenerateColumns = false;
        _gridMeter.Columns.Clear();
        _gridMeter.Columns.Add(MakeTextCol("MeterId", "计量点编号", true, 17));
        _gridMeter.Columns.Add(MakeTextCol("ScopeText", "边界", true, 7));
        _gridMeter.Columns.Add(MakeTextCol("DisplayName", "位置/设备", true, 20));
        _gridMeter.Columns.Add(MakeTextCol("TypeName", "能源", true, 8));
        _gridMeter.Columns.Add(MakeTextCol("Unit", "单位", true, 7));
        _gridMeter.Columns.Add(MakeTextCol("SectionText", "工序段", true, 9));
        _gridMeter.Columns.Add(MakeTextCol("LineText", "线体", true, 7));
        _gridMeter.Columns.Add(MakeTextCol("EquipmentCodeText", "设备编码", true, 13));
        _gridMeter.Columns.Add(MakeTextCol("EquipmentNameText", "设备名称", true, 15));
        _gridMeter.Columns.Add(MakeTextCol("Location", "安装位置", true, 16));
        _gridMeter.Columns.Add(MakeTextCol("EnabledText", "启用", true, 7));
    }

    private void SetupEnergyReadingColumns()
    {
        _gridEnergyReading.AutoGenerateColumns = false;
        _gridEnergyReading.Columns.Clear();
        _gridEnergyReading.Columns.Add(MakeTextCol("PeriodText", "账期", true, 11));
        _gridEnergyReading.Columns.Add(MakeTextCol("ValueText", "消耗量", true, 13));
        _gridEnergyReading.Columns.Add(MakeTextCol("Source", "来源", true, 10));
        _gridEnergyReading.Columns.Add(MakeTextCol("EnteredBy", "录入人", true, 10));
        _gridEnergyReading.Columns.Add(MakeTextCol("RecordedAtText", "记录时间", true, 17));
        _gridEnergyReading.Columns.Add(MakeTextCol("Remark", "备注", true, 34));
    }

    /// <summary>
    /// 进入「能源台账」页签时兜底初始化。
    /// ⚠ 页面是在 RegisterPage 阶段构建的，那时 EnsureEnergySeed 还没跑，EnergyMeters 是空的
    /// （WinForms 构造期数据绑定不物化，本项目已踩过多次）——所以必须在 Enter 时补一次。
    /// </summary>
    private void EnergyMeterTab_Enter(object? sender, EventArgs e) => EnsureEnergyReady();

    /// <summary>进入「能耗查询」页签时兜底初始化。</summary>
    private void EnergyQueryTab_Enter(object? sender, EventArgs e) => EnsureEnergyReady();

    /// <summary>兜底：确保种子已生成 + 下拉已填充 + 数据已加载（幂等）。</summary>
    private void EnsureEnergyReady()
    {
        if (MesDatabase.EnergyMeters.Count == 0) MesDatabase.EnsureEnergySeed();
        if (_cboMeterType is not null && _cboMeterType.Items.Count <= 1)
        {
            _cboMeterType.Items.Clear();
            _cboMeterType.Items.Add("全部");
            foreach (var t in MesDatabase.EnergyTypeDefs) _cboMeterType.Items.Add(t.Name);
            _cboMeterType.SelectedIndex = 0;
        }
        if (_cboEnergyPeriodFrom is not null && _cboEnergyPeriodTo is not null
            && _cboEnergyPeriodFrom.Items.Count <= 1)
            FillPeriodCombos();
        if (_gridMeter is not null && _gridMeter.Rows.Count == 0) LoadEnergyMeters();
        if (_gridEnergySummary is not null && _gridEnergySummary.Rows.Count == 0) LoadEnergyReadings();
    }

    private void WireMeterSelection()
    {
        void OnSel(object? s, EventArgs e) => LoadMeterReadings(_gridMeter.CurrentRow?.DataBoundItem as EnergyMeter);
        _gridMeter.SelectionChanged -= OnSel;
        _gridMeter.SelectionChanged += OnSel;
        LoadMeterReadings(_gridMeter.CurrentRow?.DataBoundItem as EnergyMeter);
    }

    /// <summary>按计量点刷新右侧读数表（选中联动与兜底初始化共用）。</summary>
    private void LoadMeterReadings(EnergyMeter? meter)
    {
        if (_gridEnergyReading is null) return;
        if (meter is null)
        {
            _gridEnergyReading.DataSource = null;
            _gridEnergyReading.DataSource = new List<EnergyReading>();
            return;
        }
        var rows = MesDatabase.QueryByMeter(meter.MeterId);
        _gridEnergyReading.DataSource = null;
        _gridEnergyReading.DataSource = rows;
    }

    private void LoadEnergyMeters()
    {
        if (_gridMeter is null) return;
        var type = _cboMeterType?.SelectedItem?.ToString() ?? "全部";
        var kw = _txtMeterKeyword?.Text.Trim() ?? "";
        var rows = MesDatabase.EnergyMeters
            .Where(m => type == "全部" || m.TypeName == type)
            .Where(m => kw.Length == 0
                        || m.MeterId.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || m.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || (m.EquipmentCode?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (m.EquipmentName?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(m => m.Scope == EnergyScopes.Plant ? 0 : m.Scope == EnergyScopes.Line ? 1 : 2)
            .ThenBy(m => m.MeterId)
            .ToList();
        _gridMeter.DataSource = null;
        _gridMeter.DataSource = rows;
    }

    /// <summary>点「录入抄表数」：弹窗选计量点 + 账期 + 用量。</summary>
    private void BtnEnergyEntry(object? sender, EventArgs e)
    {
        using var dlg = new EnergyEntryForm(MesDatabase.EnergyMeters.ToList());
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.UpsertEnergyReading(
            dlg.MeterId, dlg.Period, dlg.Value,
            dlg.Source, CurrentWmsOperator, dlg.Remark);
        if (!ok) { MessageBox.Show(msg, "录入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        MessageBox.Show(msg, "已录入", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadEnergyMeters();
        LoadEnergyReadings();
    }

    // ==================== ② 能耗查询 ====================

    private TabPage BuildEnergyQueryTab()
    {
        var page = new TabPage("能耗查询");
        page.Padding = new Padding(4);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(6, 4, 6, 4) };
        var energyCardRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        _lblEnergyNote = new Label
        {
            AutoSize = true, ForeColor = EnGray, Font = new Font("微软雅黑", 8.5F),
            Margin = new Padding(8, 4, 0, 0), Text = "区间汇总："
        };
        cards.Controls.Add(_lblEnergyNote);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 76, Padding = new Padding(6, 6, 4, 0), WrapContents = true
        };
        bar.Controls.Add(new Label { Text = "组织边界：", AutoSize = true, Margin = new Padding(0, 8, 2, 0) });
        _cboEnergyScope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, Margin = new Padding(2, 4, 0, 0) };
        _cboEnergyScope.Items.Add("全部");
        foreach (var s in EnergyScopes.All) _cboEnergyScope.Items.Add(s);
        _cboEnergyScope.SelectedIndex = 0;
        bar.Controls.Add(_cboEnergyScope);

        bar.Controls.Add(new Label { Text = "能源：", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        _cboEnergyType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, Margin = new Padding(2, 4, 0, 0) };
        _cboEnergyType.Items.Add("全部");
        foreach (var t in MesDatabase.EnergyTypeDefs) _cboEnergyType.Items.Add(t.Name);
        _cboEnergyType.SelectedIndex = 0;
        bar.Controls.Add(_cboEnergyType);

        bar.Controls.Add(new Label { Text = "线体：", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        _cboEnergyLine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 74, Margin = new Padding(2, 4, 0, 0) };
        _cboEnergyLine.Items.Add("全部");
        foreach (var l in new[] { "L1", "L2", "L3" }) _cboEnergyLine.Items.Add(l);
        _cboEnergyLine.SelectedIndex = 0;
        bar.Controls.Add(_cboEnergyLine);

        bar.Controls.Add(new Label { Text = "工序段：", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        _cboEnergySection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, Margin = new Padding(2, 4, 0, 0) };
        _cboEnergySection.Items.Add("全部");
        foreach (var s in new[] { "涂布", "卷绕", "注液", "化成", "测试" }) _cboEnergySection.Items.Add(s);
        _cboEnergySection.SelectedIndex = 0;
        bar.Controls.Add(_cboEnergySection);

        bar.Controls.Add(new Label { Text = "账期：", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        _cboEnergyPeriodFrom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, Margin = new Padding(2, 4, 0, 0) };
        _cboEnergyPeriodTo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, Margin = new Padding(2, 4, 0, 0) };
        FillPeriodCombos();
        bar.Controls.Add(_cboEnergyPeriodFrom);
        bar.Controls.Add(new Label { Text = "至", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
        bar.Controls.Add(_cboEnergyPeriodTo);

        bar.Controls.Add(new Label { Text = "汇总维度：", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        _cboEnergyGroup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, Margin = new Padding(2, 4, 0, 0) };
        _cboEnergyGroup.Items.AddRange(new object[] { "账期", "线体", "工序段", "设备", "能源" });
        _cboEnergyGroup.SelectedIndex = 0;
        bar.Controls.Add(_cboEnergyGroup);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => LoadEnergyReadings()));
        var btnReset = MakeToolbarButton("重置", (_, _) =>
        {
            _cboEnergyScope.SelectedIndex = 0;
            _cboEnergyType.SelectedIndex = 0;
            _cboEnergyLine.SelectedIndex = 0;
            _cboEnergySection.SelectedIndex = 0;
            _cboEnergyGroup.SelectedIndex = 0;
            LoadEnergyReadings();
        });
        bar.Controls.Add(btnReset);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 260, BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("能耗明细（账期降序）", out _gridEnergyQuery));
        split.Panel2.Controls.Add(MakeGridGroup("汇总报表（按所选维度；不同能源单位不合并）", out _gridEnergySummary));

        page.Controls.Add(split);
        page.Controls.Add(bar);
        page.Controls.Add(cards);

        SetupEnergyReadingColumns();
        SetupEnergySummaryColumns();
        page.Enter -= EnergyQueryTab_Enter;
        page.Enter += EnergyQueryTab_Enter;
        LoadEnergyReadings();
        return page;
    }

    private void SetupEnergySummaryColumns()
    {
        _gridEnergySummary.AutoGenerateColumns = false;
        _gridEnergySummary.Columns.Clear();
        _gridEnergySummary.Columns.Add(MakeTextCol("Dimension", "维度值", true, 16));
        _gridEnergySummary.Columns.Add(MakeTextCol("SubDimension", "能源", true, 10));
        _gridEnergySummary.Columns.Add(MakeTextCol("ValueText", "消耗量", true, 14));
        _gridEnergySummary.Columns.Add(MakeTextCol("Source", "来源", true, 10));
        _gridEnergySummary.Columns.Add(MakeTextCol("EstCostText", "估算成本", true, 13));
    }

    private void FillPeriodCombos()
    {
        if (_cboEnergyPeriodFrom is null || _cboEnergyPeriodTo is null) return;
        var periods = MesDatabase.EnergyReadings.Select(r => r.Period).Distinct().OrderBy(p => p).ToList();
        if (periods.Count == 0) return;
        foreach (var c in new[] { _cboEnergyPeriodFrom, _cboEnergyPeriodTo })
        {
            c.Items.Clear();
            c.Items.Add("全部");
            foreach (var p in periods) c.Items.Add(p);
            c.SelectedIndex = 0;
        }
    }

    /// <summary>按查询条件刷新明细 + 汇总 + 区间卡片。</summary>
    private void LoadEnergyReadings()
    {
        if (_gridEnergyQuery is null || _gridEnergySummary is null) return;
        var type = _cboEnergyType?.SelectedItem?.ToString();
        var scope = _cboEnergyScope?.SelectedItem?.ToString();
        var line = _cboEnergyLine?.SelectedItem?.ToString();
        var section = _cboEnergySection?.SelectedItem?.ToString();
        var from = _cboEnergyPeriodFrom?.SelectedItem?.ToString();
        var to = _cboEnergyPeriodTo?.SelectedItem?.ToString();
        var group = _cboEnergyGroup?.SelectedItem?.ToString() ?? "账期";

        string? N(string? v) => v is null or "全部" ? null : v;

        var rows = MesDatabase.QueryEnergyReadings(
            typeId: null, section: N(section), line: N(line), equipmentCode: null,
            periodFrom: N(from), periodTo: N(to), scope: N(scope));

        // 能源类型下拉选的是中文名，转成 TypeId 过滤
        if (N(type) is { Length: > 0 } tn)
        {
            var tid = MesDatabase.EnergyTypeDefs.FirstOrDefault(t => t.Name == tn)?.TypeId;
            if (tid is not null) rows = rows.Where(r => r.TypeId == tid).ToList();
        }

        _gridEnergyQuery.DataSource = null;
        _gridEnergyQuery.DataSource = rows;

        var summary = MesDatabase.SummarizeEnergy(group, null, N(section), N(line), N(from), N(to), N(scope));
        if (N(type) is { Length: > 0 } tn2)
        {
            var tid2 = MesDatabase.EnergyTypeDefs.FirstOrDefault(t => t.Name == tn2)?.TypeId;
            if (tid2 is not null) summary = summary.Where(r => r.SubDimension == tid2).ToList();
        }
        _gridEnergySummary.DataSource = null;
        _gridEnergySummary.DataSource = summary;

        if (_lblEnergyNote is not null)
        {
            var parts = new List<string>();
            foreach (var g in rows.GroupBy(r => r.TypeId))
            {
                var unit = g.First().Unit;
                parts.Add($"{g.First().TypeName} {g.Sum(r => r.Value):0.##} {unit}");
            }
            _lblEnergyNote.Text = parts.Count == 0
                ? "区间汇总：当前条件下无数据"
                : "区间汇总：" + string.Join("　|　", parts)
                  + $"　（明细 {rows.Count} 条，边界 {scope}）";
            _lblEnergyNote.ForeColor = parts.Count == 0 ? EnWarn : EnOk;
        }
    }
}