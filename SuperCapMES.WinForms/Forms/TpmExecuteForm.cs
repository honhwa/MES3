using System;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 点检/保养执行窗体：选择结果（正常 / 异常）并填写备注（异常时必填）。
/// 结果回传调用方，由调用方执行 <see cref="Data.MesDatabase.ExecuteTpm"/>。
/// 控件宣告与静态配置见 TpmExecuteForm.Designer.cs。
/// </summary>
public partial class TpmExecuteForm : Form
{
    public string Result { get; private set; } = "正常";
    public string Remark { get; private set; } = "";
    public string Operator { get; private set; } = "admin";

    public TpmExecuteForm(TpmPlan plan)
    {
        InitializeComponent();
        InitializeRuntime(plan);
    }

    /// <summary>
    /// 执行期初始化：仅处理依赖传入参数的内容（Designer 无法解析的插值字串）。
    /// 原建构式内除了这些赋值外，其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(TpmPlan plan)
    {
        Text = $"执行点检/保养 - {plan.PlanId}";
        lblPlanId.Text = plan.PlanId;
        lblDevice.Text = $"{plan.DeviceName}（{plan.DeviceCode}）";
        lblItem.Text = plan.ItemName;
        lblStandard.Text = plan.Standard;
        lblCycle.Text = plan.CycleText;
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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
