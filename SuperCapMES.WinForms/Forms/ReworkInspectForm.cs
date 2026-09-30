using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 返工重检登记窗体：填写重检标准值 / 实测值 / 判定（合格 → 放行；不合格 → 退回返工）。
/// 由调用方根据结果执行 <c>MesDatabase.RegisterReworkInspection</c>。
/// </summary>
public class ReworkInspectForm : Form
{
    private TextBox _txtStandard = null!;
    private TextBox _txtMeasured = null!;
    private RadioButton _rbPass = null!;
    private RadioButton _rbFail = null!;
    private TextBox _txtRemark = null!;
    private TextBox _txtOperator = null!;

    public bool Passed { get; private set; }
    public string Standard { get; private set; } = "";
    public string Measured { get; private set; } = "";
    public string Remark { get; private set; } = "";
    public string Operator { get; private set; } = "";

    /// <param name="reworkNo">返工单号（标题展示）</param>
    /// <param name="cellSn">电芯 SN</param>
    /// <param name="defaultStandard">默认重检标准（如 容量 ≥95F）</param>
    public ReworkInspectForm(string reworkNo, string cellSn, string defaultStandard)
    {
        Text = $"重检登记 - {reworkNo}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 190,
            ColumnCount = 2,
            Padding = new Padding(14, 16, 14, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddLabel(table, "返工单号：", 0);
        table.Controls.Add(new Label
        {
            Text = reworkNo,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Consolas", 10F)
        }, 1, 0);

        AddLabel(table, "电芯 SN：", 1);
        table.Controls.Add(new Label
        {
            Text = cellSn,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Consolas", 10F)
        }, 1, 1);

        AddLabel(table, "重检标准：", 2);
        _txtStandard = new TextBox { Text = defaultStandard, Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 4) };
        table.Controls.Add(_txtStandard, 1, 2);

        AddLabel(table, "实测值：", 3);
        _txtMeasured = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 4) };
        table.Controls.Add(_txtMeasured, 1, 3);

        AddLabel(table, "判定结果：", 4);
        var rp = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(2, 4, 2, 2) };
        _rbPass = new RadioButton { Text = "合格（放行并解除保留）", Checked = true, AutoSize = true, Margin = new Padding(0, 4, 18, 0) };
        _rbFail = new RadioButton { Text = "不合格（退回返工）", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        rp.Controls.Add(_rbPass);
        rp.Controls.Add(_rbFail);
        table.Controls.Add(rp, 1, 4);

        AddLabel(table, "备注：", 5);
        _txtRemark = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 4) };
        table.Controls.Add(_txtRemark, 1, 5);

        AddLabel(table, "重检人：", 6);
        _txtOperator = new TextBox { Text = "admin", Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 4) };
        table.Controls.Add(_txtOperator, 1, 6);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = "提交重检结果",
            Size = new Size(130, 32),
            Location = new Point(bottom.Width - 242, 10),
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

    private static void AddLabel(TableLayoutPanel table, string text, int row) =>
        table.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, row);

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_rbPass.Checked && string.IsNullOrWhiteSpace(_txtMeasured.Text))
        {
            if (MessageBox.Show("判定为「合格」但未填写实测值，确认继续？", "提示", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }

        Passed = _rbPass.Checked;
        Standard = _txtStandard.Text.Trim();
        Measured = _txtMeasured.Text.Trim();
        Remark = _txtRemark.Text.Trim();
        Operator = string.IsNullOrWhiteSpace(_txtOperator.Text) ? "admin" : _txtOperator.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }
}
