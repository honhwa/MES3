using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 出货绑定——候选电芯选择窗体：
/// 列出「尚未绑定到任何有效出货单」的电芯（已排除在其他出货单中的），
/// 支持按 型号 / 极片批次 / 关键字 筛选 + 勾选（含全选/取消全选），
/// 确认后调用 <see cref="MesDatabase.BindShipmentCells"/> 批量绑定到指定物流出库单。
/// 一芯一单：已在有效绑定中的电芯不会出现在候选里，避免串货。
/// 控件宣告与静态配置见 ShipmentBindForm.Designer.cs。
/// </summary>
public partial class ShipmentBindForm : Form
{
    private string _orderNo = "";

    /// <summary>本次成功绑定的电芯数量（供调用方提示/定位）。</summary>
    public int BoundCount { get; private set; }

    /// <summary>网格行视图模型（Chk 承载勾选，其余为只读展示列）。</summary>
    private sealed class CandidateRow
    {
        public bool Chk { get; set; }
        public string CellSn { get; init; } = "";
        public string ProductModel { get; init; } = "";
        public string Status { get; init; } = "";
        public string ElectrodeLot { get; init; } = "";
        public string ElectrolyteLot { get; init; } = "";
        public DateTime CreateTime { get; init; }
    }

    public ShipmentBindForm(string orderNo)
    {
        InitializeComponent();
        InitializeRuntime(orderNo);
    }

    /// <summary>
    /// 执行期初始化：处理依赖建构式参数 / 物件初始设定式 / 回圈建项的内容，
    /// 以及原本建构式在 UI 建立后呼叫的 ReloadFilterItems() / RefreshCandidates()。
    /// 其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(string orderNo)
    {
        _orderNo = orderNo ?? "";
        Text = $"绑定出货电芯 - {_orderNo}";

        // 原 BuildUi 内 _grid 的 DataGridViewCellStyle / RowTemplate 采物件初始设定式，Designer 无法解析，原样保留。
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        _grid.RowTemplate.Height = 26;

        // 原 BuildUi 内 _grid 栏位建立（回圈 / 工厂），Designer 无法解析，原样保留。
        var chk = new DataGridViewCheckBoxColumn
        {
            Name = "Chk",
            HeaderText = "选择",
            DataPropertyName = "Chk",
            FillWeight = 6,
            ReadOnly = false,
            FlatStyle = FlatStyle.Flat
        };
        _grid.Columns.Add(chk);
        _grid.Columns.Add(MakeCol("CellSn", "电芯SN", 18));
        _grid.Columns.Add(MakeCol("ProductModel", "型号", 12));
        _grid.Columns.Add(MakeCol("Status", "状态", 8));
        _grid.Columns.Add(MakeCol("ElectrodeLot", "极片批号", 14));
        _grid.Columns.Add(MakeCol("ElectrolyteLot", "电解液批号", 14));
        _grid.Columns.Add(MakeCol("CreateTime", "生产时间", 14));

        ReloadFilterItems();
        RefreshCandidates();
    }

    private void CboModel_SelectedIndexChanged(object? sender, EventArgs e)
    {
        RefreshCandidates();
    }

    private void CboLot_SelectedIndexChanged(object? sender, EventArgs e)
    {
        RefreshCandidates();
    }

    private void BtnFilter_Click(object? sender, EventArgs e)
    {
        RefreshCandidates();
    }

    private void BtnReset_Click(object? sender, EventArgs e)
    {
        _cboModel.SelectedIndex = 0;
        _cboLot.SelectedIndex = 0;
        _txtKeyword.Text = "";
        RefreshCandidates();
    }

    private void BtnSelectAll_Click(object? sender, EventArgs e)
    {
        SetAllChecked(true);
    }

    private void BtnClearAll_Click(object? sender, EventArgs e)
    {
        SetAllChecked(false);
    }

    private void Grid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
    }

    private void Bottom_Resize(object? sender, EventArgs e)
    {
        btnOk.Location = new Point(bottom.Width - 216, 9);
        btnCancel.Location = new Point(bottom.Width - 116, 9);
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private static DataGridViewTextBoxColumn MakeCol(string prop, string header, int fillWeight) =>
        new()
        {
            Name = prop,
            DataPropertyName = prop,
            HeaderText = header,
            ReadOnly = true,
            FillWeight = fillWeight
        };

    /// <summary>重建筛选下拉项（构造时调用一次；候选集固定为"全部未绑定电芯"的去重型号/批次）。</summary>
    private void ReloadFilterItems()
    {
        var all = MesDatabase.GetShipmentCandidates(_orderNo, null, null, null);
        _cboModel.Items.Clear();
        _cboModel.Items.Add("全部");
        foreach (var m in all.Select(c => c.ProductModel).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal))
            _cboModel.Items.Add(m);
        _cboModel.SelectedIndex = 0;

        _cboLot.Items.Clear();
        _cboLot.Items.Add("全部");
        foreach (var l in all.Select(c => c.ElectrodeLot).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal))
            _cboLot.Items.Add(l);
        _cboLot.SelectedIndex = 0;
    }

    /// <summary>按当前筛选条件刷新候选网格。</summary>
    private void RefreshCandidates()
    {
        if (_grid is null || _cboModel is null || _cboLot is null) return;

        var model = _cboModel.SelectedIndex > 0 ? _cboModel.SelectedItem?.ToString() : null;
        var lot = _cboLot.SelectedIndex > 0 ? _cboLot.SelectedItem?.ToString() : null;
        var kw = _txtKeyword?.Text.Trim() ?? "";

        var candidates = MesDatabase.GetShipmentCandidates(
            _orderNo, model, lot, kw.Length == 0 ? null : kw);

        var rows = candidates.Select(c => new CandidateRow
        {
            Chk = false,
            CellSn = c.Sn,
            ProductModel = c.ProductModel,
            Status = c.Status,
            ElectrodeLot = c.ElectrodeLot,
            ElectrolyteLot = c.ElectrolyteLot,
            CreateTime = c.CreateTime
        }).ToList();

        _grid.DataSource = rows;
        if (_lblCandidate is not null)
            _lblCandidate.Text = $"候选 {rows.Count} 只（仅「合格 / 已入库」且未绑定出货的电芯；在制/化成中/检测中/不良不出货）";
        UpdateSelectedCount();
    }

    private void Grid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name == "Chk") UpdateSelectedCount();
    }

    private void SetAllChecked(bool value)
    {
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            r.Cells["Chk"].Value = value;
        }
        _grid.RefreshEdit();
        UpdateSelectedCount();
    }

    private List<string> SelectedSns()
    {
        var list = new List<string>();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            if (r.Cells["Chk"].Value is bool b && b && r.DataBoundItem is CandidateRow row)
                list.Add(row.CellSn);
        }
        return list;
    }

    private void UpdateSelectedCount()
    {
        if (_lblSelected is null) return;
        var n = SelectedSns().Count;
        _lblSelected.Text = $"已选 {n} 只";
        _lblSelected.ForeColor = n > 0 ? Color.FromArgb(15, 76, 129) : Color.FromArgb(40, 50, 60);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        var sns = SelectedSns();
        if (sns.Count == 0) { Warn("请先勾选要绑定的电芯。"); return; }

        var (ok, msg, bound, skipped, statusRejected) = MesDatabase.BindShipmentCells(_orderNo, sns, "admin");
        if (!ok) { Warn(msg); return; }

        BoundCount = bound;
        var detail = new System.Text.StringBuilder();
        if (skipped.Count > 0)
            detail.AppendLine($"\n跳过（已绑定在其他出货单）{skipped.Count} 只：{string.Join("、", skipped.Take(5))}{(skipped.Count > 5 ? " 等" : "")}");
        if (statusRejected.Count > 0)
            detail.AppendLine($"\n拒绝（状态不可出货）{statusRejected.Count} 只：{string.Join("、", statusRejected.Take(5))}{(statusRejected.Count > 5 ? " 等" : "")}");
        MessageBox.Show(msg + detail, "绑定结果", MessageBoxButtons.OK, MessageBoxIcon.Information);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
