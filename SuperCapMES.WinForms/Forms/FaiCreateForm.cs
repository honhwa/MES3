using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 新建首件检验（FAI）窗体：选择工单 + 首检工序（自动带出该工序的检验项目），可选填首件电芯 SN。
/// 点「创建首检单」后把选择结果回传调用方（由调用方执行 <see cref="MesDatabase.CreateFai"/>）。
/// </summary>
public class FaiCreateForm : Form
{
    private readonly List<WorkOrder> _workOrders;
    private readonly List<string> _processes;

    private ComboBox _cboWo = null!;
    private TextBox _txtCellSn = null!;
    private ComboBox _cboProcess = null!;
    private ComboBox _cboReason = null!;
    private TextBox _txtOperator = null!;
    private Label _lblItems = null!;
    private Label _lblFaiState = null!;

    public string WoNo { get; private set; } = "";
    public string FirstCellSn { get; private set; } = "";
    public string ProcessName { get; private set; } = "";
    public string Reason { get; private set; } = FaiReason.Changeover;
    public string Operator { get; private set; } = "admin";

    public FaiCreateForm(List<WorkOrder> workOrders, List<string> processes)
    {
        _workOrders = workOrders ?? new List<WorkOrder>();
        _processes = processes ?? new List<string>();

        Text = "新建首件检验（FAI）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        BuildUi();
    }

    private void BuildUi()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 210,
            ColumnCount = 2,
            Padding = new Padding(14, 16, 14, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddLabel(table, "生产工单 *：", 0);
        _cboWo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 6, 4, 4)
        };
        _cboWo.Items.AddRange(_workOrders
            .Select(w => $"{w.WoNo} ｜ {w.ProductModel} ｜ {w.Status}")
            .Cast<object>().ToArray());
        if (_cboWo.Items.Count > 0) _cboWo.SelectedIndex = 0;
        _cboWo.SelectedIndexChanged += (_, _) => UpdateFaiState();
        table.Controls.Add(_cboWo, 1, 0);

        AddLabel(table, "该工单首检：", 1);
        _lblFaiState = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray };
        table.Controls.Add(_lblFaiState, 1, 1);

        AddLabel(table, "首检工序 *：", 2);
        _cboProcess = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 6, 4, 4)
        };
        _cboProcess.Items.AddRange(_processes.Cast<object>().ToArray());
        if (_cboProcess.Items.Count > 0) _cboProcess.SelectedIndex = 0;
        _cboProcess.SelectedIndexChanged += (_, _) => UpdateItemsPreview();
        table.Controls.Add(_cboProcess, 1, 2);

        AddLabel(table, "检验项目：", 3);
        _lblItems = new Label { Text = "", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(15, 76, 129) };
        table.Controls.Add(_lblItems, 1, 3);

        AddLabel(table, "首件电芯 SN：", 4);
        _txtCellSn = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        table.Controls.Add(_txtCellSn, 1, 4);

        AddLabel(table, "触发原因 *：", 5);
        _cboReason = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 6, 4, 4)
        };
        _cboReason.Items.AddRange(FaiReason.All.Cast<object>().ToArray());
        _cboReason.SelectedIndex = 0;
        table.Controls.Add(_cboReason, 1, 5);

        AddLabel(table, "操作人：", 6);
        _txtOperator = new TextBox { Text = "admin", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        table.Controls.Add(_txtOperator, 1, 6);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = "创建首检单",
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
        Controls.Add(table);

        UpdateFaiState();
        UpdateItemsPreview();
    }

    private static void AddLabel(TableLayoutPanel table, string text, int row) =>
        table.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, row);

    private WorkOrder? CurrentWorkOrder() =>
        _cboWo.SelectedIndex >= 0 && _cboWo.SelectedIndex < _workOrders.Count
            ? _workOrders[_cboWo.SelectedIndex]
            : null;

    /// <summary>显示所选工单的首件检验放行状态（便于用户判断是否需要新建）。</summary>
    private void UpdateFaiState()
    {
        var wo = CurrentWorkOrder();
        if (wo is null || _lblFaiState is null) return;

        var passed = MesDatabase.IsFaiPassed(wo.WoNo);
        var latest = MesDatabase.GetLatestFai(wo.WoNo);
        _lblFaiState.Text = passed
            ? $"✔ 已放行（{latest?.FaiNo}，{latest?.ProcessName}）"
            : (latest is null ? "✖ 尚未首件检验（需新建）" : $"✖ 未放行（最近 {latest.FaiNo} 为「{latest.Status}」）");
        _lblFaiState.ForeColor = passed ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 0, 0);

        // 预填该工单下未在保留中的电芯作为首件候选
        if (string.IsNullOrWhiteSpace(_txtCellSn.Text))
        {
            var candidate = MesDatabase.Cells.FirstOrDefault(c =>
                string.Equals(c.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase)
                && CellSnStatus.IsShippable(c.Status));
            if (candidate is not null) _txtCellSn.Text = candidate.Sn;
        }
    }

    private void UpdateItemsPreview()
    {
        var process = _cboProcess.SelectedItem?.ToString() ?? "";
        var items = MesDatabase.GetFaiTemplate(process);
        if (_lblItems is not null)
            _lblItems.Text = items.Count == 0
                ? "（该工序无检验模板）"
                : $"{items.Count} 项：{string.Join("、", items.Select(i => i.Item))}";
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var wo = CurrentWorkOrder();
        if (wo is null) { Warn("请选择生产工单。"); return; }

        var process = _cboProcess.SelectedItem?.ToString() ?? "";
        if (process.Length == 0) { Warn("请选择首检工序。"); return; }
        if (MesDatabase.GetFaiTemplate(process).Count == 0) { Warn($"工序「{process}」没有检验项目模板。"); return; }

        WoNo = wo.WoNo;
        FirstCellSn = _txtCellSn.Text.Trim();
        ProcessName = process;
        Reason = _cboReason.SelectedItem?.ToString() ?? FaiReason.Changeover;
        Operator = string.IsNullOrWhiteSpace(_txtOperator.Text) ? "admin" : _txtOperator.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
