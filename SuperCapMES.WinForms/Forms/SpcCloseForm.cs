using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// SPC 预警关闭对话框：收集「处理措施」与「操作人」，二者均必填，风格与 ReleaseHoldForm 一致。
/// </summary>
public class SpcCloseForm : Form
{
    private TextBox _txtOperator = null!;
    private TextBox _txtAction = null!;

    /// <summary>处理人（确认后有效）</summary>
    public string OperatorName { get; private set; } = "admin";

    /// <summary>处理措施（确认后有效）</summary>
    public string HandleAction { get; private set; } = "";

    public SpcCloseForm(string alertNo, string alertDesc)
    {
        Text = "关闭预警";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 268);
        Font = new Font("微软雅黑", 9F);
        BuildUi(alertNo, alertDesc);
    }

    private void BuildUi(string alertNo, string alertDesc)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(16, 16, 16, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(MakeLabel("预警单号"), 0, 0);
        grid.Controls.Add(new Label
        {
            Text = alertNo,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        }, 1, 0);

        grid.Controls.Add(MakeLabel("预警摘要"), 0, 1);
        grid.Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(alertDesc) ? "—" : alertDesc,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            AutoEllipsis = true
        }, 1, 1);

        grid.Controls.Add(MakeLabel("处理措施 *"), 0, 2);
        grid.Controls.Add(new Label { Text = "", Dock = DockStyle.Fill }, 1, 2);

        _txtAction = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White
        };
        grid.Controls.Add(_txtAction, 1, 3);

        // 操作人（放底部按钮条上方，避免占用多行区）
        var opRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(16, 4, 16, 0),
            WrapContents = false
        };
        opRow.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
        _txtOperator = new TextBox { Text = "admin", Width = 160, Margin = new Padding(4, 2, 0, 0) };
        opRow.Controls.Add(_txtOperator);

        // 底部按钮
        var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.WhiteSmoke };
        var btnOk = new Button { Text = "确认关闭", Width = 110, Height = 32 };
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

        // Dock 顺序：后加的先贴边 → 先 Fill，再 opRow(Bottom)，再 btnRow(Bottom 最外)
        Controls.Add(grid);
        Controls.Add(opRow);
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
        var action = _txtAction.Text.Trim();
        if (string.IsNullOrEmpty(action))
        {
            Warn("请填写处理措施。");
            return;
        }

        OperatorName = op;
        HandleAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
