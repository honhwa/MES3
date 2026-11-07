using System;
using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 备件领用窗体：填写领用数量与操作人，回传调用方执行 <see cref="Data.MesDatabase.IssueSparePart"/>。
/// </summary>
public partial class SpareIssueForm : Form
{
    private double _stock;

    public double Quantity { get; private set; }
    public string Operator { get; private set; } = "admin";

    public SpareIssueForm(SparePart part)
    {
        InitializeComponent();
        InitializeRuntime(part);
    }

    /// <summary>
    /// 执行期初始化：依赖参数的标题 / 资料列文字与颜色 / 数量上限
    /// （Designer 无法解析的插值与运算式）；其余控件建立 / 静态配置 /
    /// 加入容器均在 SpareIssueForm.Designer.cs 的 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(SparePart part)
    {
        _stock = part.Stock;
        Text = $"备件领用 - {part.Name}";
        lblCode.Text = part.Code;
        lblName.Text = $"{part.Name}（{part.Spec}）";
        lblStock.Text = $"{part.Stock:0.##} {part.Unit}（安全库存 {part.SafetyStock:0.##}）";
        lblStock.ForeColor = part.IsLow ? Color.FromArgb(192, 0, 0) : Color.FromArgb(39, 174, 96);
        _numQty.Maximum = (decimal)Math.Max(1, _stock);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control, int row)
    {
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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
