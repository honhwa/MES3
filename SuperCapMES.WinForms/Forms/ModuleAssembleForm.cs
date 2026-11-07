using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 新建模组装配窗体：填写模组 SN / 型号 / 产线 / 关联工单，并勾选要装入的电芯
/// （候选 = 合格或已入库、且尚未属于任何模组的电芯）。
/// 确定后由调用方执行 <see cref="MesDatabase.AssembleModule"/>。
/// 控件宣告与静态配置见 ModuleAssembleForm.Designer.cs。
/// </summary>
public partial class ModuleAssembleForm : Form
{
    public string ModuleSn { get; private set; } = "";
    public string ModuleModel { get; private set; } = "";
    public string Line { get; private set; } = "";
    public string WoNo { get; private set; } = "";
    public List<string> CellSns { get; } = new();

    public ModuleAssembleForm()
    {
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：静态控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()
    /// （含原 Controls.Add 顺序）；此处仅串联仍依赖执行期资料的 helper。
    /// </summary>
    private void InitializeRuntime()
    {
        BuildUi();
        LoadCandidates();
    }

    /// <summary>
    /// 原 BuildUi() 的静态段落（header / 四个提示标签 / 输入控件 / group / _grid /
    /// bottom / top / 各按钮之 new 与所有 Controls.Add）已全部展开至
    /// ModuleAssembleForm.Designer.cs；Label() 工厂（全静态）亦已展开为具名栏位。
    /// 此处仅保留依赖 DateTime / MesDatabase / LINQ 的设定与 DataGridView 手写列。
    /// </summary>
    private void BuildUi()
    {
        // ---- 模组信息 ----
        _txtModuleSn.Text = $"SM-A{DateTime.Today:yyMMdd}-L1-{DateTime.Now:HHmmss}";
        _cboLine.Items.AddRange(LineOptions.All.Cast<object>().ToArray());
        _cboLine.SelectedIndex = 0;
        _cboWo.Items.AddRange(MesDatabase.WorkOrders
            .Select(w => $"{w.WoNo} ｜ {w.ProductModel}").Cast<object>().ToArray());
        if (_cboWo.Items.Count > 0) _cboWo.SelectedIndex = 0;

        // ---- 候选电芯 ----
        _grid.RowTemplate.Height = 26;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Chk", HeaderText = "选择", DataPropertyName = "Chk", FillWeight = 6, FlatStyle = FlatStyle.Flat
        });
        _grid.Columns.Add(Col("CellSn", "电芯SN", 18));
        _grid.Columns.Add(Col("ProductModel", "型号", 14));
        _grid.Columns.Add(Col("CellStatus", "状态", 10));
        _grid.Columns.Add(Col("ElectrodeLot", "极片批号", 14));
        _grid.Columns.Add(Col("CreateTime", "生产时间", 14));
    }

    private static DataGridViewTextBoxColumn Col(string prop, string header, int weight) => new()
    {
        Name = prop,
        DataPropertyName = prop,
        HeaderText = header,
        ReadOnly = true,
        FillWeight = weight
    };

    // ================= 网格事件（原 lambda 具名化，Designer 以方法群挂接） =================

    /// <summary>原 _grid.CurrentCellDirtyStateChanged lambda。</summary>
    private void Grid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    /// <summary>原 _grid.CellValueChanged lambda。</summary>
    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Chk") UpdateSelected();
    }

    /// <summary>原 _grid.DataError lambda。</summary>
    private void Grid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
    }

    /// <summary>原 btnAll.Click lambda。</summary>
    private void BtnAll_Click(object? sender, EventArgs e) => SetAll(true);

    /// <summary>原 btnNone.Click lambda。</summary>
    private void BtnNone_Click(object? sender, EventArgs e) => SetAll(false);

    /// <summary>原 btnCancel.Click lambda。</summary>
    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void LoadCandidates()
    {
        var rows = MesDatabase.GetModuleCandidates();
        _grid.DataSource = rows;
        if (_lblCandidate is not null) _lblCandidate.Text = $"候选 {rows.Count} 只";
        UpdateSelected();
    }

    private void SetAll(bool value)
    {
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            r.Cells["Chk"].Value = value;
        }
        _grid.RefreshEdit();
        UpdateSelected();
    }

    private List<string> SelectedSns()
    {
        var list = new List<string>();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            if (r.Cells["Chk"].Value is bool b && b && r.DataBoundItem is ModuleCellItem item)
                list.Add(item.CellSn);
        }
        return list;
    }

    private void UpdateSelected()
    {
        if (_lblSelected is null) return;
        var n = SelectedSns().Count;
        _lblSelected.Text = $"已选 {n} 只";
        _lblSelected.ForeColor = n > 0 ? Color.FromArgb(39, 174, 96) : Color.FromArgb(15, 76, 129);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        if (string.IsNullOrWhiteSpace(_txtModuleSn.Text)) { Warn("请填写模组 SN。"); return; }

        var sns = SelectedSns();
        if (sns.Count == 0) { Warn("请至少勾选 1 只电芯。"); return; }
        if (sns.Select(s => s).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sns.Count)
        {
            Warn("存在重复电芯，请重新选择。");
            return;
        }

        ModuleSn = _txtModuleSn.Text.Trim();
        ModuleModel = _txtModuleModel.Text.Trim();
        Line = _cboLine.SelectedItem?.ToString() ?? "L1";
        WoNo = _cboWo.SelectedIndex >= 0 && _cboWo.SelectedIndex < MesDatabase.WorkOrders.Count
            ? MesDatabase.WorkOrders[_cboWo.SelectedIndex].WoNo
            : "";
        CellSns.Clear();
        CellSns.AddRange(sns);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
