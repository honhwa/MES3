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
/// </summary>
public class ShipmentBindForm : Form
{
    private readonly string _orderNo;

    private ComboBox _cboModel = null!;
    private ComboBox _cboLot = null!;
    private TextBox _txtKeyword = null!;
    private Label _lblCandidate = null!;
    private Label _lblSelected = null!;
    private DataGridView _grid = null!;

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
        _orderNo = orderNo ?? "";
        Text = $"绑定出货电芯 - {_orderNo}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 620);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        Font = new Font("微软雅黑", 9F);

        BuildUi();
        ReloadFilterItems();
        RefreshCandidates();
    }

    private void BuildUi()
    {
        // ---- 顶部筛选条 ----
        var filter = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(8, 8, 4, 0),
            WrapContents = false
        };

        filter.Controls.Add(new Label { Text = "型号：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(4, 4, 0, 0) };
        _cboModel.SelectedIndexChanged += (_, _) => RefreshCandidates();
        filter.Controls.Add(_cboModel);

        filter.Controls.Add(new Label { Text = "极片批次：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboLot = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(4, 4, 0, 0) };
        _cboLot.SelectedIndexChanged += (_, _) => RefreshCandidates();
        filter.Controls.Add(_cboLot);

        filter.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtKeyword = new TextBox { Width = 150, Margin = new Padding(4, 4, 0, 0) };
        filter.Controls.Add(_txtKeyword);

        filter.Controls.Add(MakeButton("筛选", (_, _) => RefreshCandidates()));
        filter.Controls.Add(MakeButton("重置", (_, _) =>
        {
            _cboModel.SelectedIndex = 0;
            _cboLot.SelectedIndex = 0;
            _txtKeyword.Text = "";
            RefreshCandidates();
        }));

        _lblCandidate = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(14, 10, 0, 0)
        };
        filter.Controls.Add(_lblCandidate);

        // ---- 网格 ----
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15, 76, 129),
                ForeColor = Color.White,
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            ColumnHeadersHeight = 30,
            RowTemplate = { Height = 26 }
        };

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

        // 勾选即时提交，保证"已选 n 只"实时刷新
        _grid.CurrentCellDirtyStateChanged -= Grid_CurrentCellDirtyStateChanged;
        _grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
        _grid.CellValueChanged -= Grid_CellValueChanged;
        _grid.CellValueChanged += Grid_CellValueChanged;
        _grid.DataError += (_, e) => e.ThrowException = false;

        // ---- 底部按钮条 ----
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.WhiteSmoke };
        var btnSelectAll = MakeButton("全选", (_, _) => SetAllChecked(true));
        var btnClearAll = MakeButton("取消全选", (_, _) => SetAllChecked(false));
        btnSelectAll.Location = new Point(10, 10);
        btnClearAll.Location = new Point(94, 10);
        bottom.Controls.Add(btnSelectAll);
        bottom.Controls.Add(btnClearAll);

        _lblSelected = new Label
        {
            Text = "已选 0 只",
            AutoSize = true,
            Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 50, 60),
            Location = new Point(190, 17)
        };
        bottom.Controls.Add(_lblSelected);

        var btnOk = new Button
        {
            Text = "确认绑定",
            Size = new Size(110, 32),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        var btnCancel = new Button { Text = "取消", Size = new Size(90, 32) };
        btnOk.Location = new Point(bottom.Width - 216, 9);
        btnCancel.Location = new Point(bottom.Width - 116, 9);
        bottom.Resize += (_, _) =>
        {
            btnOk.Location = new Point(bottom.Width - 216, 9);
            btnCancel.Location = new Point(bottom.Width - 116, 9);
        };
        btnOk.Click += BtnOk_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(btnOk);
        bottom.Controls.Add(btnCancel);

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(filter);
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

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(78, 30),
            Margin = new Padding(4, 3, 0, 0),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        b.Click += onClick;
        return b;
    }

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
