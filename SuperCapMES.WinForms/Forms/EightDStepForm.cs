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
public partial class EightDStepForm : Form
{
    public string Content { get; private set; } = "";

    /// <param name="complaintNo">客诉单号（仅用于标题展示）</param>
    /// <param name="step">目标步骤（见 <see cref="EightDStep"/>）</param>
    /// <param name="existing">已有内容（修订时回填）</param>
    /// <param name="isAdvance">true=推进（提交当前步骤）；false=修订（只改内容）</param>
    public EightDStepForm(string complaintNo, string step, string existing, bool isAdvance)
    {
        InitializeComponent();
        InitializeRuntime(complaintNo, step, existing, isAdvance);
    }

    /// <summary>
    /// 执行期初始化：依赖参数的标题 / 标签 / 按钮文字与回填内容
    /// （Designer 无法解析的条件运算式与插值字串）；其余控件建立 /
    /// 静态配置 / 加入容器均在 EightDStepForm.Designer.cs 的 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(string complaintNo, string step, string existing, bool isAdvance)
    {
        Text = isAdvance ? $"推进 8D - {step}" : $"修订 8D 内容 - {step}";
        lblRequirement.Text = $"填写要求：{EightDStep.RequirementOf(step)}";
        lblStep.Text = $"当前步骤：{step}";
        lblComplaintNo.Text = $"客诉单号：{complaintNo}";
        _txtContent.Text = existing ?? "";
        btnOk.Text = isAdvance ? "提交并推进" : "保存修订";
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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
