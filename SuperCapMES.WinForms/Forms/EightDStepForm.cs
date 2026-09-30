using System;
using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 8D 步骤内容窗体：填写 / 修订某个步骤的对策内容。
/// 用于「推进 8D」（提交当前步骤并前进）与「修订步骤」（只改内容、不改进度）两种场景。
/// 校验（内容非空、步骤是否已推进到、客诉是否已关闭）由仓储层负责，本窗体只收集输入。
/// </summary>
public class EightDStepForm : Form
{
    private readonly TextBox _txtContent = null!;

    public string Content { get; private set; } = "";

    /// <param name="complaintNo">客诉单号（仅用于标题展示）</param>
    /// <param name="step">目标步骤（见 <see cref="EightDStep"/>）</param>
    /// <param name="existing">已有内容（修订时回填）</param>
    /// <param name="isAdvance">true=推进（提交当前步骤）；false=修订（只改内容）</param>
    public EightDStepForm(string complaintNo, string step, string existing, bool isAdvance)
    {
        Text = isAdvance ? $"推进 8D - {step}" : $"修订 8D 内容 - {step}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var header = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(14, 12, 14, 0) };
        // 停靠规则：同向 Top 控件按「后加入的先停靠」排列，故按视觉顺序的反序加入
        header.Controls.Add(new Label
        {
            Text = $"填写要求：{EightDStep.RequirementOf(step)}",
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = Color.FromArgb(120, 90, 20)
        });
        header.Controls.Add(new Label
        {
            Text = $"当前步骤：{step}",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("微软雅黑", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 76, 129)
        });
        header.Controls.Add(new Label
        {
            Text = $"客诉单号：{complaintNo}",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.Gray
        });

        _txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = existing ?? "",
            Font = new Font("微软雅黑", 9.5F)
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 8) };
        body.Controls.Add(_txtContent);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = isAdvance ? "提交并推进" : "保存修订",
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

        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(header);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        Content = _txtContent.Text.Trim();
        if (Content.Length == 0)
        {
            MessageBox.Show("内容不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
