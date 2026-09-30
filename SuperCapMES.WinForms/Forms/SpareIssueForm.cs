using System;
using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 备件领用窗体：填写领用数量与操作人，回传调用方执行 <see cref="Data.MesDatabase.IssueSparePart"/>。
/// </summary>
public class SpareIssueForm : Form
{
    private NumericUpDown _numQty = null!;
    private TextBox _txtOperator = null!;
    private readonly double _stock;

    public double Quantity { get; private set; }
    public string Operator { get; private set; } = "admin";

    public SpareIssueForm(SparePart part)
    {
        _stock = part.Stock;
        Text = $"备件领用 - {part.Name}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 140,
            ColumnCount = 2,
            Padding = new Padding(14, 16, 14, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, "备件编码：", new Label { Text = part.Code, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0);
        AddRow(table, "备件名称：", new Label { Text = $"{part.Name}（{part.Spec}）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1);
        AddRow(table, "当前库存：",
            new Label
            {
                Text = $"{part.Stock:0.##} {part.Unit}（安全库存 {part.SafetyStock:0.##}）",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = part.IsLow ? Color.FromArgb(192, 0, 0) : Color.FromArgb(39, 174, 96)
            }, 2);

        _numQty = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1,
            Maximum = (decimal)Math.Max(1, _stock),
            Value = 1,
            DecimalPlaces = 0,
            Margin = new Padding(4, 6, 4, 4)
        };
        AddRow(table, "领用数量 *：", _numQty, 3);

        _txtOperator = new TextBox { Text = "admin", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "领用人：", _txtOperator, 4);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = "确认领用",
            Size = new Size(110, 32),
            Location = new Point(bottom.Width - 222, 10),
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
        Quantity = (double)_numQty.Value;
        if (Quantity <= 0) { MessageBox.Show("领用数量必须大于 0。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (Quantity > _stock + 1e-9)
        {
            MessageBox.Show($"领用数量超过当前库存（{_stock:0.##}）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Operator = string.IsNullOrWhiteSpace(_txtOperator.Text) ? "admin" : _txtOperator.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
