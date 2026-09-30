using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 解除保留对话框：收集 操作人 与 解除原因，风格与 WorkOrderEditForm 一致。
/// </summary>
public class ReleaseHoldForm : Form
{
    private TextBox _txtOperator = null!;
    private TextBox _txtReason = null!;

    /// <summary>解除操作人（确认后有效）</summary>
    public string OperatorName { get; private set; } = "admin";

    /// <summary>解除原因（确认后有效）</summary>
    public string ReleaseReason { get; private set; } = "";

    public ReleaseHoldForm(string holdNo)
    {
        Text = "解除保留";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 246);
        Font = new Font("微软雅黑", 9F);
        BuildUi(holdNo);
    }

    private void BuildUi(string holdNo)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(16, 16, 16, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        // 0 保留单号（只读展示）
        var lblHold = new Label
        {
            Text = holdNo,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        grid.Controls.Add(MakeLabel("保留单号"), 0, 0);
        grid.Controls.Add(lblHold, 1, 0);

        // 1 操作人
        _txtOperator = new TextBox { Dock = DockStyle.Fill, Text = "admin" };
        grid.Controls.Add(MakeLabel("操作人 *"), 0, 1);
        grid.Controls.Add(_txtOperator, 1, 1);

        // 2 解除原因
        _txtReason = new TextBox { Dock = DockStyle.Fill };
        grid.Controls.Add(MakeLabel("解除原因 *"), 0, 2);
        grid.Controls.Add(_txtReason, 1, 2);

        // 底部按钮
        var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.WhiteSmoke };
        var btnOk = new Button { Text = "确认解除", Width = 110, Height = 32 };
        var btnCancel = new Button { Text = "取消", Width = 100, Height = 32 };
        btnOk.Location = new Point(btnRow.Width - 226, 10);
        btnCancel.Location = new Point(btnRow.Width - 114, 10);
        btnRow.Resize += (_, _) =>
        {
            btnOk.Location = new Point(btnRow.Width - 226, 10);
            btnCancel.Location = new Point(btnRow.Width - 114, 10);
        };
        btnOk.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);

        Controls.Add(grid);
        Controls.Add(btnRow);
    }

    private static Label MakeLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60)
        };

    private void Save()
    {
        var op = _txtOperator.Text.Trim();
        if (string.IsNullOrEmpty(op))
        {
            Warn("请填写操作人。");
            return;
        }
        var reason = _txtReason.Text.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            Warn("请填写解除原因。");
            return;
        }

        OperatorName = op;
        ReleaseReason = reason;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
