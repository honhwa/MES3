using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 新建返工单窗体：从「保留中」的保留单明细中选择一只电芯，填写返工原因 / 责任工序 / 返工工序 / 操作人。
/// 点「创建返工单」后把选择结果回传给调用方（由调用方执行 <see cref="MesDatabase.CreateReworkFromHold"/>）。
/// </summary>
public class ReworkCreateForm : Form
{
    private DataGridView _grid = null!;
    private TextBox _txtReason = null!;
    private ComboBox _cboResponsible = null!;
    private ComboBox _cboReworkProcess = null!;
    private TextBox _txtOperator = null!;
    private Label _lblCount = null!;

    public string HoldNo { get; private set; } = "";
    public string CellSn { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public string ResponsibleProcess { get; private set; } = "";
    public string ReworkProcess { get; private set; } = "";
    public string Operator { get; private set; } = "";

    private sealed class CandidateRow
    {
        public string HoldNo { get; init; } = "";
        public string CellSn { get; init; } = "";
        public string ProductModel { get; init; } = "";
        public string HoldReason { get; init; } = "";
    }

    public ReworkCreateForm()
    {
        Text = "新建返工单（从保留单发起）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(780, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        BuildUi();
        LoadCandidates();
    }

    private void BuildUi()
    {
        // ---- 候选电芯（保留中）----
        var gridBox = new GroupBox
        {
            Text = "可返工电芯（来自「保留中」的保留单；已有未完成返工单的电芯不出现在此）",
            Dock = DockStyle.Top,
            Height = 210,
            Padding = new Padding(6, 12, 6, 6)
        };
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
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
        AddCol("HoldNo", "保留单号", 16);
        AddCol("CellSn", "电芯SN", 22);
        AddCol("ProductModel", "型号", 16);
        AddCol("HoldReason", "保留原因", 46);
        gridBox.Controls.Add(_grid);

        // ---- 表单 ----
        var formBox = new GroupBox { Text = "返工信息", Dock = DockStyle.Top, Height = 168, Padding = new Padding(10, 14, 10, 6) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        table.Controls.Add(new Label { Text = "返工原因：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
        _txtReason = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 8, 8, 4) };
        table.Controls.Add(_txtReason, 1, 0);

        table.Controls.Add(new Label { Text = "责任工序：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 2, 0);
        _cboResponsible = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 8, 8, 4) };
        _cboResponsible.Items.AddRange(ProcessSequence.Order.Cast<object>().ToArray());
        _cboResponsible.SelectedIndex = 0;
        table.Controls.Add(_cboResponsible, 3, 0);

        table.Controls.Add(new Label { Text = "返工工序 *：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 1);
        _cboReworkProcess = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 8, 8, 4) };
        _cboReworkProcess.Items.AddRange(ProcessSequence.Order.Cast<object>().ToArray());
        _cboReworkProcess.SelectedIndex = 0;
        table.Controls.Add(_cboReworkProcess, 1, 1);

        table.Controls.Add(new Label { Text = "操作人：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 2, 1);
        _txtOperator = new TextBox { Text = "admin", Dock = DockStyle.Fill, Margin = new Padding(4, 8, 8, 4) };
        table.Controls.Add(_txtOperator, 3, 1);

        _lblCount = new Label { Text = "", Dock = DockStyle.Fill, ForeColor = Color.Gray, Margin = new Padding(4, 8, 4, 4) };
        table.Controls.Add(_lblCount, 0, 2);
        table.SetColumnSpan(_lblCount, 4);

        formBox.Controls.Add(table);

        // ---- 底部按钮 ----
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = "创建返工单",
            Size = new Size(120, 32),
            Location = new Point(bottom.Width - 232, 10),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        var btnCancel = new Button { Text = "取消", Size = new Size(90, 32), Location = new Point(bottom.Width - 106, 10) };
        btnOk.Click += BtnOk_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(btnOk);
        bottom.Controls.Add(btnCancel);

        Controls.Add(bottom);
        Controls.Add(formBox);
        Controls.Add(gridBox);
    }

    private void AddCol(string prop, string header, int fillWeight) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = prop,
            DataPropertyName = prop,
            HeaderText = header,
            ReadOnly = true,
            FillWeight = fillWeight
        });

    private void LoadCandidates()
    {
        var candidates = MesDatabase.GetReworkCandidates();
        var rows = candidates.Select(c => new CandidateRow
        {
            HoldNo = c.HoldNo,
            CellSn = c.CellSn,
            ProductModel = c.ProductModel,
            HoldReason = c.HoldReason
        }).ToList();

        _grid.DataSource = rows;
        if (_grid.Rows.Count > 0)
        {
            _grid.ClearSelection();
            _grid.Rows[0].Selected = true;
        }
        _lblCount.Text = $"可返工电芯 {rows.Count} 只（保留中且无未完成返工单）";
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0) { Warn("请先选择要返工的电芯。"); return; }
        if (_grid.SelectedRows[0].DataBoundItem is not CandidateRow row) { Warn("请先选择要返工的电芯。"); return; }

        var reworkProcess = _cboReworkProcess.SelectedItem?.ToString() ?? "";
        if (reworkProcess.Length == 0) { Warn("请选择返工工序。"); return; }
        if (string.IsNullOrWhiteSpace(_txtReason.Text))
        {
            if (MessageBox.Show("返工原因为空，确认继续创建？", "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }

        HoldNo = row.HoldNo;
        CellSn = row.CellSn;
        Reason = _txtReason.Text.Trim();
        ResponsibleProcess = _cboResponsible.SelectedItem?.ToString() ?? "";
        ReworkProcess = reworkProcess;
        Operator = string.IsNullOrWhiteSpace(_txtOperator.Text) ? "admin" : _txtOperator.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
