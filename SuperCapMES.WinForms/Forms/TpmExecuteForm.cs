using System;
using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 点检/保养执行窗体：选择结果（正常 / 异常）并填写备注（异常时必填）。
/// 结果回传调用方，由调用方执行 <see cref="Data.MesDatabase.ExecuteTpm"/>。
/// </summary>
public class TpmExecuteForm : Form
{
    private ComboBox _cboResult = null!;
    private TextBox _txtRemark = null!;
    private TextBox _txtOperator = null!;

    public string Result { get; private set; } = "正常";
    public string Remark { get; private set; } = "";
    public string Operator { get; private set; } = "admin";

    public TpmExecuteForm(TpmPlan plan)
    {
        Text = $"执行点检/保养 - {plan.PlanId}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 290);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 180,
            ColumnCount = 2,
            Padding = new Padding(14, 16, 14, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, "计划编号：", new Label { Text = plan.PlanId, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0);
        AddRow(table, "设备：", new Label { Text = $"{plan.DeviceName}（{plan.DeviceCode}）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1);
        AddRow(table, "点检项目：", new Label { Text = plan.ItemName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2);
        AddRow(table, "判定标准：", new Label { Text = plan.Standard, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(15, 76, 129) }, 3);
        AddRow(table, "周期：", new Label { Text = plan.CycleText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4);

        _cboResult = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _cboResult.Items.AddRange(new object[] { "正常", "异常" });
        _cboResult.SelectedIndex = 0;
        AddRow(table, "点检结果 *：", _cboResult, 5);

        _txtRemark = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "备注：", _txtRemark, 6);

        _txtOperator = new TextBox { Text = "admin", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "执行人：", _txtOperator, 7);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = "提交点检结果",
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
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control, int row)
    {
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        Result = _cboResult.SelectedItem?.ToString() ?? "正常";
        Remark = _txtRemark.Text.Trim();
        if (Result == "异常" && Remark.Length == 0)
        {
            MessageBox.Show("点检结果为「异常」时必须填写备注说明。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Operator = string.IsNullOrWhiteSpace(_txtOperator.Text) ? "admin" : _txtOperator.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
