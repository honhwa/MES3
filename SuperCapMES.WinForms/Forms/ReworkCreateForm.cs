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
public partial class ReworkCreateForm : Form
{
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
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：DataGridView 表头样式 / 栏位、工序下拉项目、候选电芯取数等
    /// 依赖执行期资料或 Designer 无法解析的陈述（原 BuildUi() 内相应逻辑搬移至此）。
    /// </summary>
    private void InitializeRuntime()
    {
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        _grid.RowTemplate.Height = 26;
        AddCol("HoldNo", "保留单号", 16);
        AddCol("CellSn", "电芯SN", 22);
        AddCol("ProductModel", "型号", 16);
        AddCol("HoldReason", "保留原因", 46);
        _cboResponsible.Items.AddRange(ProcessSequence.Order.Cast<object>().ToArray());
        _cboResponsible.SelectedIndex = 0;
        _cboReworkProcess.Items.AddRange(ProcessSequence.Order.Cast<object>().ToArray());
        _cboReworkProcess.SelectedIndex = 0;
        LoadCandidates();
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

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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
