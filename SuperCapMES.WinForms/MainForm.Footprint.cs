using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：产品碳足迹（阶段三）
//
// 页面：「能源与碳」下新增两页
//   ⑤ 产品碳足迹     选型号 → 环节分解 + 单只足迹 + 物料明细
//   ⑥ 物料碳强度     物料碳强度台账（模拟值，须替换为供应商/数据库值）
//
// 口径（界面必须可见）：
//   · 原材料 = BOM 用量(含损耗) × 物料碳强度
//   · 生产   = 线体期间排放 ÷ 线体同期总良品 × 该型号良品（按产量分摊）
//   · 运输 / 报废回收 = **暂无数据，标"待补"，不计入总量也不当作 0**
// ============================================================================

public partial class MainForm
{
    private static readonly Color FpInk = Color.FromArgb(30, 41, 59);
    private static readonly Color FpGray = Color.FromArgb(110, 125, 145);
    private static readonly Color FpOk = Color.FromArgb(21, 128, 61);
    private static readonly Color FpWarn = Color.FromArgb(217, 119, 6);

    // 产品碳足迹（三个网格字段在 MainForm.cs 声明）
    private ComboBox _cboFootprintModel = null!;
    private Label _lblFpTotal = null!;
    private Label _lblFpPerUnit = null!;
    private Label _lblFpNote = null!;

    // 物料碳强度（_gridMaterialCarbon 在 MainForm.cs 声明）

    // ==================== ⑤ 产品碳足迹 ====================

    private TabPage BuildFootprintTab()
    {
        var page = new TabPage("产品碳足迹");
        page.Padding = new Padding(4);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("总碳足迹", Color.FromArgb(21, 128, 61), out _lblFpTotal));
        cards.Controls.Add(MakeCard("单只碳足迹", Color.FromArgb(30, 95, 168), out _lblFpPerUnit));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 76, Padding = new Padding(6, 6, 4, 0), WrapContents = true
        };
        bar.Controls.Add(new Label { Text = "产品型号：", AutoSize = true, Margin = new Padding(0, 8, 2, 0) });
        _cboFootprintModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(2, 4, 0, 0) };
        foreach (var m in MesDatabase.FootprintProductModels()) _cboFootprintModel.Items.Add(m);
        if (_cboFootprintModel.Items.Count > 0) _cboFootprintModel.SelectedIndex = 0;
        bar.Controls.Add(_cboFootprintModel);

        bar.Controls.Add(MakeToolbarButton("重新计算", (_, _) => LoadFootprint()));
        var btnReset = MakeToolbarButton("重置", (_, _) =>
        {
            if (_cboFootprintModel.Items.Count > 0) _cboFootprintModel.SelectedIndex = 0;
            LoadFootprint();
        });
        bar.Controls.Add(btnReset);

        _lblFpNote = new Label
        {
            Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8, 4, 8, 4),
            ForeColor = FpWarn, Font = new Font("微软雅黑", 8.5F),
            Text = FootprintDisclaimers.Note
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 190, BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup(
            "环节分解（原材料 / 生产 / 运输 / 报废回收；待补环节不计入）", out _gridFpStage));
        split.Panel2.Controls.Add(MakeGridGroup(
            "原材料环节物料明细（BOM 用量含损耗 × 物料碳强度）", out _gridFpMaterial));

        page.Controls.Add(split);
        page.Controls.Add(bar);
        page.Controls.Add(cards);
        page.Controls.Add(_lblFpNote);

        SetupFootprintColumns();
        page.Enter -= FootprintTab_Enter;
        page.Enter += FootprintTab_Enter;
        LoadFootprint();
        return page;
    }

    private void SetupFootprintColumns()
    {
        _gridFpStage.AutoGenerateColumns = false;
        _gridFpStage.Columns.Clear();
        _gridFpStage.Columns.Add(MakeTextCol("Stage", "环节", true, 12));
        _gridFpStage.Columns.Add(MakeTextCol("EmissionTText", "排放量", true, 15));
        _gridFpStage.Columns.Add(MakeTextCol("RatioText", "占比", true, 9));
        _gridFpStage.Columns.Add(MakeTextCol("StatusText", "状态", true, 10));
        _gridFpStage.Columns.Add(MakeTextCol("Basis", "取数口径", true, 52));
        _gridFpStage.Columns.Add(MakeTextCol("Evidence", "数据来源", true, 58));

        _gridFpMaterial.AutoGenerateColumns = false;
        _gridFpMaterial.Columns.Clear();
        _gridFpMaterial.Columns.Add(MakeTextCol("MaterialName", "物料", true, 18));
        _gridFpMaterial.Columns.Add(MakeTextCol("MaterialCode", "编码", true, 16));
        _gridFpMaterial.Columns.Add(MakeTextCol("LossRateText", "损耗", true, 12));
        _gridFpMaterial.Columns.Add(MakeTextCol("QtyText", "单位用量", true, 13));
        _gridFpMaterial.Columns.Add(MakeTextCol("IntensityText", "碳强度", true, 18));
        _gridFpMaterial.Columns.Add(MakeTextCol("IntensitySource", "强度来源", true, 44));
        _gridFpMaterial.Columns.Add(MakeTextCol("PerUnitText", "单只排放", true, 13));
        _gridFpMaterial.Columns.Add(MakeTextCol("TotalTText", "总排放", true, 14));
    }

    private void FootprintTab_Enter(object? sender, EventArgs e)
    {
        if (_cboFootprintModel is not null && _cboFootprintModel.Items.Count == 0)
        {
            foreach (var m in MesDatabase.FootprintProductModels()) _cboFootprintModel.Items.Add(m);
            if (_cboFootprintModel.Items.Count > 0) _cboFootprintModel.SelectedIndex = 0;
        }
        if (_gridFpStage is not null && _gridFpStage.Rows.Count == 0) LoadFootprint();
    }

    private void LoadFootprint()
    {
        if (_gridFpStage is null || _gridFpMaterial is null) return;
        var model = _cboFootprintModel?.SelectedItem?.ToString() ?? "";
        var fp = MesDatabase.BuildProductFootprint(model);

        _gridFpStage.DataSource = null;
        _gridFpStage.DataSource = fp.Stages;
        _gridFpMaterial.DataSource = null;
        _gridFpMaterial.DataSource = fp.Materials;

        if (_lblFpTotal is not null)
        {
            _lblFpTotal.Text = fp.TotalTText;
            _lblFpTotal.ForeColor = fp.TotalKg > 0 ? FpOk : FpWarn;
        }
        if (_lblFpPerUnit is not null)
        {
            _lblFpPerUnit.Text = fp.PerUnitText;
            _lblFpPerUnit.ForeColor = fp.GoodQty > 0 ? FpOk : FpWarn;
        }
        if (_lblFpNote is not null)
        {
            var todo = fp.Stages.Count(s => !s.Calculated);
            _lblFpNote.Text = $"型号 {fp.ProductModel}　工单 {fp.WoCount} 张　良品 {fp.GoodQtyText}"
                + $"　线体 {(fp.Lines.Count > 0 ? string.Join("/", fp.Lines) : "—")}"
                + (todo > 0 ? $"　⚠ {todo} 个环节待补数据（不计入总量）" : "")
                + $"　{fp.Disclaimer}";
            _lblFpNote.ForeColor = todo > 0 ? FpWarn : FpGray;
        }
    }

    // ==================== ⑥ 物料碳强度 ====================

    private TabPage BuildMaterialCarbonTab()
    {
        var page = new TabPage("物料碳强度");
        page.Padding = new Padding(4);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false
        };
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => LoadMaterialCarbons()));
        bar.Controls.Add(new Label
        {
            Text = "物料碳强度 = 生产 1 单位物料的碳排放；当前为**模拟值**，正式核算须替换为供应商提供值或第三方数据库",
            AutoSize = true, ForeColor = FpGray, Margin = new Padding(10, 8, 0, 0)
        });

        page.Controls.Add(MakeGridGroup(
            "物料碳强度台账（按物料编码；含来源标注）", out _gridMaterialCarbon));

        SetupMaterialCarbonColumns();
        page.Enter -= MaterialCarbonTab_Enter;
        page.Enter += MaterialCarbonTab_Enter;
        LoadMaterialCarbons();
        return page;
    }

    private void MaterialCarbonTab_Enter(object? sender, EventArgs e)
    {
        MesDatabase.EnsureMaterialCarbonSeed();
        if (_gridMaterialCarbon is not null && _gridMaterialCarbon.Rows.Count == 0) LoadMaterialCarbons();
    }

    private void SetupMaterialCarbonColumns()
    {
        _gridMaterialCarbon.AutoGenerateColumns = false;
        _gridMaterialCarbon.Columns.Clear();
        _gridMaterialCarbon.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 17));
        _gridMaterialCarbon.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 20));
        _gridMaterialCarbon.Columns.Add(MakeTextCol("Unit", "单位", true, 8));
        _gridMaterialCarbon.Columns.Add(MakeTextCol("ValueText", "碳强度", true, 22));
        _gridMaterialCarbon.Columns.Add(MakeTextCol("ScopeText", "范围", true, 22));
        _gridMaterialCarbon.Columns.Add(MakeTextCol("Source", "来源", true, 56));
    }

    private void LoadMaterialCarbons()
    {
        if (_gridMaterialCarbon is null) return;
        MesDatabase.EnsureMaterialCarbonSeed();
        _gridMaterialCarbon.DataSource = null;
        _gridMaterialCarbon.DataSource = MesDatabase.GetMaterialCarbons();
    }
}