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
/// </summary>
public class ModuleAssembleForm : Form
{
    private TextBox _txtModuleSn = null!;
    private TextBox _txtModuleModel = null!;
    private ComboBox _cboLine = null!;
    private ComboBox _cboWo = null!;
    private DataGridView _grid = null!;
    private Label _lblSelected = null!;
    private Label _lblCandidate = null!;

    public string ModuleSn { get; private set; } = "";
    public string ModuleModel { get; private set; } = "";
    public string Line { get; private set; } = "";
    public string WoNo { get; private set; } = "";
    public List<string> CellSns { get; } = new();

    public ModuleAssembleForm()
    {
        Text = "新建模组装配";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(860, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        BuildUi();
        LoadCandidates();
    }

    private void BuildUi()
    {
        // ---- 模组信息 ----
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            ColumnCount = 4,
            Padding = new Padding(12, 12, 12, 0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        header.Controls.Add(Label("模组 SN *："), 0, 0);
        _txtModuleSn = new TextBox
        {
            Text = $"SM-A{DateTime.Today:yyMMdd}-L1-{DateTime.Now:HHmmss}",
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F),
            Margin = new Padding(4, 6, 8, 4)
        };
        header.Controls.Add(_txtModuleSn, 1, 0);

        header.Controls.Add(Label("模组型号："), 2, 0);
        _txtModuleModel = new TextBox { Text = "模组 SC-MOD-100F-24V", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 8, 4) };
        header.Controls.Add(_txtModuleModel, 3, 0);

        header.Controls.Add(Label("生产线体："), 0, 1);
        _cboLine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 8, 4) };
        _cboLine.Items.AddRange(LineOptions.All.Cast<object>().ToArray());
        _cboLine.SelectedIndex = 0;
        header.Controls.Add(_cboLine, 1, 1);

        header.Controls.Add(Label("关联工单："), 2, 1);
        _cboWo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 8, 4) };
        _cboWo.Items.AddRange(MesDatabase.WorkOrders
            .Select(w => $"{w.WoNo} ｜ {w.ProductModel}").Cast<object>().ToArray());
        if (_cboWo.Items.Count > 0) _cboWo.SelectedIndex = 0;
        header.Controls.Add(_cboWo, 3, 1);

        // ---- 候选电芯 ----
        var group = new GroupBox
        {
            Text = "选择要装入模组的电芯（仅「合格 / 已入库」且未属于任何模组的电芯）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
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
            ColumnHeadersHeight = 30
        };
        _grid.RowTemplate.Height = 26;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Chk", HeaderText = "选择", DataPropertyName = "Chk", FillWeight = 6, FlatStyle = FlatStyle.Flat
        });
        _grid.Columns.Add(Col("CellSn", "电芯SN", 18));
        _grid.Columns.Add(Col("ProductModel", "型号", 14));
        _grid.Columns.Add(Col("CellStatus", "状态", 10));
        _grid.Columns.Add(Col("ElectrodeLot", "极片批号", 14));
        _grid.Columns.Add(Col("CreateTime", "生产时间", 14));
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Chk") UpdateSelected(); };
        _grid.DataError += (_, e) => e.ThrowException = false;
        group.Controls.Add(_grid);

        // ---- 底部 ----
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.WhiteSmoke };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(8, 6, 0, 0) };
        var btnAll = new Button { Text = "全选", Size = new Size(70, 28) };
        var btnNone = new Button { Text = "取消全选", Size = new Size(84, 28) };
        btnAll.Click += (_, _) => SetAll(true);
        btnNone.Click += (_, _) => SetAll(false);
        top.Controls.Add(btnAll);
        top.Controls.Add(btnNone);
        _lblCandidate = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(12, 8, 0, 0) };
        top.Controls.Add(_lblCandidate);
        _lblSelected = new Label
        {
            Text = "已选 0 只",
            AutoSize = true,
            Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 76, 129),
            Margin = new Padding(16, 8, 0, 0)
        };
        top.Controls.Add(_lblSelected);

        var btnOk = new Button
        {
            Text = "装配模组",
            Size = new Size(110, 32),
            Location = new Point(bottom.Width - 226, 52),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        var btnCancel = new Button { Text = "取消", Size = new Size(90, 32), Location = new Point(bottom.Width - 106, 52) };
        btnOk.Click += BtnOk_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(top);
        bottom.Controls.Add(btnOk);
        bottom.Controls.Add(btnCancel);

        Controls.Add(group);
        Controls.Add(header);
        Controls.Add(bottom);
    }

    private static Label Label(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight
    };

    private static DataGridViewTextBoxColumn Col(string prop, string header, int weight) => new()
    {
        Name = prop,
        DataPropertyName = prop,
        HeaderText = header,
        ReadOnly = true,
        FillWeight = weight
    };

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
