using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：供应链碳管理（阶段四）
//
// 页面：「能源与碳」下新增一页
//   ⑧ 供应链碳管理   供应商碳数据台账 + 同物料供应商对比排名
//
// 核心口径（界面必须可见）：
//   · **未提供数据 ≠ 0 排放**——未提供的供应商不参与排名、排最后、差距标"—"
//     （当成 0 会让它排第一名，直接导致错误的采购决策）
//   · 数据来源分三档：供应商提供 / 第三方数据库 / 行业均值估算，可信度不同不能混排
// ============================================================================

public partial class MainForm
{
    private static readonly Color ScInk = Color.FromArgb(30, 41, 59);
    private static readonly Color ScGray = Color.FromArgb(110, 125, 145);
    private static readonly Color ScOk = Color.FromArgb(21, 128, 61);
    private static readonly Color ScWarn = Color.FromArgb(217, 119, 6);

    // （_gridSupplierCarbon / _gridSupplierCompare 在 MainForm.cs 声明）
    private ComboBox _cboCompareMaterial = null!;
    private Label _lblCompareNote = null!;
    private Label _lblScCoverage = null!;

    private TabPage BuildSupplyChainTab()
    {
        var page = new TabPage("供应链碳管理");
        page.Padding = new Padding(4);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("碳数据覆盖率", Color.FromArgb(21, 128, 61), out _lblScCoverage));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 76, Padding = new Padding(6, 6, 4, 0), WrapContents = true
        };

        var btnEdit = MakeToolbarButton("录入/更新", BtnSupplierCarbonEdit);
        btnEdit.Width = 106;
        bar.Controls.Add(btnEdit);

        bar.Controls.Add(new Label { Text = "同物料对比：", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        _cboCompareMaterial = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(2, 4, 0, 0) };
        foreach (var m in MesDatabase.SupplyChainMaterials()) _cboCompareMaterial.Items.Add(m);
        if (_cboCompareMaterial.Items.Count > 0) _cboCompareMaterial.SelectedIndex = 0;
        bar.Controls.Add(_cboCompareMaterial);

        bar.Controls.Add(MakeToolbarButton("对比", (_, _) => LoadSupplierCompare()));

        bar.Controls.Add(new Label
        {
            Text = "未提供数据的供应商**不参与排名**（不当作 0 排放）；来源分 供应商提供/第三方数据库/行业均值估算",
            AutoSize = true, ForeColor = ScGray, Margin = new Padding(10, 8, 0, 0)
        });

        _lblCompareNote = new Label
        {
            Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 4, 8, 4),
            ForeColor = ScWarn, Font = new Font("微软雅黑", 8.5F),
            Text = SupplyChainDisclaimers.Note
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 240, BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup(
            "供应商碳数据台账（供应商 × 物料；选中行可录入/更新）", out _gridSupplierCarbon));
        split.Panel2.Controls.Add(MakeGridGroup(
            "同物料供应商对比（按碳强度升序；未提供的排最后且不排名）", out _gridSupplierCompare));

        page.Controls.Add(split);
        page.Controls.Add(bar);
        page.Controls.Add(cards);
        page.Controls.Add(_lblCompareNote);

        SetupSupplyChainColumns();
        page.Enter -= SupplyChainTab_Enter;
        page.Enter += SupplyChainTab_Enter;
        LoadSupplierCarbons();
        LoadSupplierCompare();
        return page;
    }

    private void SetupSupplyChainColumns()
    {
        _gridSupplierCarbon.AutoGenerateColumns = false;
        _gridSupplierCarbon.Columns.Clear();
        _gridSupplierCarbon.Columns.Add(MakeTextCol("Supplier", "供应商", true, 14));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("MaterialName", "物料", true, 18));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 16));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("ValueText", "碳强度", true, 18));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("Scope3Text", "范围三类别", true, 20));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("SourceText", "数据来源", true, 14));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("YearText", "数据年份", true, 10));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("Remark", "备注", true, 34));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("UpdatedBy", "更新人", true, 10));
        _gridSupplierCarbon.Columns.Add(MakeTextCol("UpdatedAtText", "更新时间", true, 17));

        _gridSupplierCompare.AutoGenerateColumns = false;
        _gridSupplierCompare.Columns.Clear();
        _gridSupplierCompare.Columns.Add(MakeTextCol("RankText", "排名", true, 9));
        _gridSupplierCompare.Columns.Add(MakeTextCol("Supplier", "供应商", true, 14));
        _gridSupplierCompare.Columns.Add(MakeTextCol("ValueText", "碳强度", true, 18));
        _gridSupplierCompare.Columns.Add(MakeTextCol("GapText", "与最优差距", true, 13));
        _gridSupplierCompare.Columns.Add(MakeTextCol("SourceText", "来源", true, 14));
        _gridSupplierCompare.Columns.Add(MakeTextCol("YearText", "年份", true, 9));
        _gridSupplierCompare.Columns.Add(MakeTextCol("LotCountText", "供货批次", true, 10));
        _gridSupplierCompare.Columns.Add(MakeTextCol("LastReceivedText", "最近到货", true, 13));
    }

    private void SupplyChainTab_Enter(object? sender, EventArgs e)
    {
        MesDatabase.EnsureSupplierCarbonSeed();
        if (_cboCompareMaterial is not null && _cboCompareMaterial.Items.Count == 0)
        {
            foreach (var m in MesDatabase.SupplyChainMaterials()) _cboCompareMaterial.Items.Add(m);
            if (_cboCompareMaterial.Items.Count > 0) _cboCompareMaterial.SelectedIndex = 0;
        }
        if (_gridSupplierCarbon is not null && _gridSupplierCarbon.Rows.Count == 0) LoadSupplierCarbons();
        if (_gridSupplierCompare is not null && _gridSupplierCompare.Rows.Count == 0) LoadSupplierCompare();
    }

    private void LoadSupplierCarbons()
    {
        if (_gridSupplierCarbon is null) return;
        MesDatabase.EnsureSupplierCarbonSeed();
        var list = MesDatabase.GetSupplierCarbons();
        _gridSupplierCarbon.DataSource = null;
        _gridSupplierCarbon.DataSource = list;

        if (_lblScCoverage is not null)
        {
            var (total, provided, cov) = MesDatabase.SupplierCarbonCoverage();
            _lblScCoverage.Text = $"{cov:P0}";
            _lblScCoverage.ForeColor = cov >= 0.75 ? ScOk : cov >= 0.5 ? ScWarn : Color.FromArgb(220, 38, 38);
        }
    }

    private void LoadSupplierCompare()
    {
        if (_gridSupplierCompare is null) return;
        var code = _cboCompareMaterial?.SelectedItem?.ToString() ?? "";
        var res = MesDatabase.CompareSuppliers(code);
        _gridSupplierCompare.DataSource = null;
        _gridSupplierCompare.DataSource = res.Rows;

        if (_lblCompareNote is null) return;
        var missing = res.SupplierCount - res.ProvidedCount;
        _lblCompareNote.Text = $"物料 {res.MaterialName}（{res.MaterialCode}）　供应商 {res.SupplierCount} 家"
            + $"　覆盖率 {res.CoverageText}"
            + (res.BestKg is null ? "　⚠ 暂无任何供应商提供碳数据" : $"　最优：{res.BestSupplier}（{res.BestText}）")
            + (missing > 0 ? $"　⚠ {missing} 家未提供（不参与排名）" : "")
            + $"　{SupplyChainDisclaimers.ZeroNote}";
        _lblCompareNote.ForeColor = missing > 0 || res.BestKg is null ? ScWarn : ScGray;
    }

    /// <summary>当前选中的供应商碳数据行。</summary>
    private SupplierCarbon? SelectedSupplierCarbon()
        => _gridSupplierCarbon?.CurrentRow?.DataBoundItem as SupplierCarbon;

    /// <summary>点「录入/更新」：弹窗填碳强度/来源/类别。</summary>
    private void BtnSupplierCarbonEdit(object? sender, EventArgs e)
    {
        MesDatabase.EnsureSupplierCarbonSeed();
        var cur = SelectedSupplierCarbon();
        using var dlg = new SupplierCarbonForm(cur, MesDatabase.WmsMaterials.ToList());
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.UpsertSupplierCarbon(
            dlg.Supplier, dlg.MaterialCode, dlg.KgPerUnit,
            dlg.Scope3Category, dlg.Source, dlg.DataYear, dlg.Remark, CurrentWmsOperator);
        if (!ok) { MessageBox.Show(msg, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        MessageBox.Show(msg, "已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadSupplierCarbons();
        LoadSupplierCompare();
    }
}