using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 客诉登记窗体：客户信息 + 客诉描述 + 关联范围（批次 / 工单 / SN / 出货单）。
/// 业务校验（必填项、数量、严重度/分类/来源合法性、接收时间）统一在
/// <see cref="Data.MesDatabase.CreateComplaint"/> 中执行，失败时由调用方弹出原因且不落库。
/// </summary>
public class ComplaintCreateForm : Form
{
    private TextBox _txtCustomer = null!;
    private TextBox _txtContact = null!;
    private ComboBox _cboSource = null!;
    private ComboBox _cboSeverity = null!;
    private ComboBox _cboCategory = null!;
    private TextBox _txtModel = null!;
    private TextBox _txtLot = null!;
    private NumericUpDown _numQty = null!;
    private TextBox _txtSymptom = null!;
    private CheckBox _chkSafety = null!;
    private CheckBox _chkRecall = null!;
    private ComboBox _cboOwner = null!;

    /// <summary>登记结果：调用方据此调用仓储层落库</summary>
    public CustomerComplaint Complaint { get; private set; } = new();

    public ComplaintCreateForm()
    {
        Text = "登记客户投诉";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(660, 580);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 480,
            ColumnCount = 2,
            Padding = new Padding(14, 14, 14, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;

        // 客户：给出既有客户下拉（可自由录入），避免与出货单往来对象不一致
        _txtCustomer = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        var custPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        _txtCustomer.Width = 260;
        var cboKnown = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(8, 5, 0, 0) };
        cboKnown.Items.Add("（从既有客户选择）");
        foreach (var p in MesDatabase.WmsOrders
                     .Where(o => o.Type == WmsOrderTypes.LogisticsOutbound)
                     .Select(o => o.Partner).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            cboKnown.Items.Add(p);
        cboKnown.SelectedIndex = 0;
        cboKnown.SelectedIndexChanged += (_, _) =>
        {
            if (cboKnown.SelectedIndex > 0) _txtCustomer.Text = cboKnown.SelectedItem?.ToString() ?? "";
        };
        custPanel.Controls.Add(_txtCustomer);
        custPanel.Controls.Add(cboKnown);
        AddRow(table, "客户名称 *：", custPanel, row++);

        _txtContact = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "客户联系人：", _txtContact, row++);

        _cboSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _cboSource.Items.AddRange(ComplaintSource.All.Cast<object>().ToArray());
        _cboSource.SelectedIndex = 0;
        AddRow(table, "客诉来源 *：", _cboSource, row++);

        _cboSeverity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _cboSeverity.Items.AddRange(ComplaintSeverity.All.Cast<object>().ToArray());
        _cboSeverity.SelectedIndex = 1;   // 默认「一般」
        _cboSeverity.SelectedIndexChanged += (_, _) => UpdateDueHint();
        AddRow(table, "严重度 *：", _cboSeverity, row++);

        _cboCategory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _cboCategory.Items.AddRange(ComplaintCategories.All.Cast<object>().ToArray());
        _cboCategory.SelectedIndex = 0;
        AddRow(table, "症状分类 *：", _cboCategory, row++);

        _txtModel = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "产品型号：", _txtModel, row++);

        _txtLot = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "关联范围：", _txtLot, row++);

        var lotTip = new Label
        {
            Text = "可填 材料批次号 / 工单号 / 电芯 SN / 出货单号 / 客户名；填了它就能一键反查触发范围并冻结",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 22,
            ForeColor = Color.Gray,
            Margin = new Padding(4, 0, 4, 0)
        };
        AddRow(table, "", lotTip, row++);

        _numQty = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 1000000, Value = 1, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "客诉数量 *：", _numQty, row++);

        _txtSymptom = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Margin = new Padding(4, 6, 4, 4)
        };
        AddRow(table, "客户描述现象 *：", _txtSymptom, row++);
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

        var chkPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        _chkSafety = new CheckBox { Text = "涉及安全（强制走召回评估）", AutoSize = true, Margin = new Padding(4, 8, 18, 0) };
        _chkRecall = new CheckBox { Text = "需要召回 / 冻结", AutoSize = true, Margin = new Padding(4, 8, 0, 0) };
        chkPanel.Controls.Add(_chkSafety);
        chkPanel.Controls.Add(_chkRecall);
        AddRow(table, "风险标记：", chkPanel, row++);

        _cboOwner = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        foreach (var u in MesDatabase.Users.Where(u => u.IsActive).OrderBy(u => u.EmpNo, StringComparer.Ordinal))
            _cboOwner.Items.Add($"{u.Name}（{u.Dept}）");
        if (_cboOwner.Items.Count > 0) _cboOwner.SelectedIndex = 0;
        AddRow(table, "责任工程师：", _cboOwner, row++);

        _lblDue = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = Color.FromArgb(15, 76, 129),
            Margin = new Padding(4, 8, 4, 0)
        };
        AddRow(table, "回复客户时限：", _lblDue, row);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = "登记客诉",
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
        UpdateDueHint();
    }

    private Label _lblDue = null!;

    /// <summary>严重度变化时同步展示自动推算的回复时限（与仓储层同一口径）。</summary>
    private void UpdateDueHint()
    {
        var severity = _cboSeverity.SelectedItem?.ToString() ?? ComplaintSeverity.Major;
        var days = ComplaintSeverity.ResponseDays(severity);
        _lblDue.Text = $"接收日 + {days} 天（{severity}），逾期会在列表标红并计入超期统计";
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control, int row)
    {
        if (label.Length > 0)
            table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, row);
        else
            table.Controls.Add(new Label { Text = "", Dock = DockStyle.Fill }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_txtCustomer.Text.Trim().Length == 0)
        {
            MessageBox.Show("客户名称为必填项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_txtSymptom.Text.Trim().Length == 0)
        {
            MessageBox.Show("请填写客户描述的现象。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Complaint = new CustomerComplaint
        {
            CustomerName = _txtCustomer.Text.Trim(),
            ContactInfo = _txtContact.Text.Trim(),
            Source = _cboSource.SelectedItem?.ToString() ?? ComplaintSource.Customer,
            Severity = _cboSeverity.SelectedItem?.ToString() ?? ComplaintSeverity.Major,
            Category = _cboCategory.SelectedItem?.ToString() ?? ComplaintCategories.Other,
            ProductModel = _txtModel.Text.Trim(),
            LotKeyword = _txtLot.Text.Trim(),
            Quantity = (double)_numQty.Value,
            SymptomDesc = _txtSymptom.Text.Trim(),
            IsSafetyInvolved = _chkSafety.Checked,
            NeedRecall = _chkRecall.Checked,
            Owner = _cboOwner.Text.Trim(),
            ReceiveTime = DateTime.Now
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
