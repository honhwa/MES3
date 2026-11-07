using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 预警阈值配置对话框。
/// 三类预警的"合理阈值"取决于厂里的物料特性与设备状况，照搬到别的厂一定是错的，
/// 所以全部做成可配项——厂里试跑两周就能按实际情况调到位。
/// </summary>
public partial class InsightRulesForm : Form
{
    public InsightRulesForm()
    {
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：7 个 NumericUpDown 的 Value 初值来自
    /// MesDatabaseInsightRules（执行期静态属性，Designer 无法解析）；
    /// 其余控件建立 / 静态配置 / 加入容器均在
    /// InsightRulesForm.Designer.cs 的 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime()
    {
        _nearExpiry.Value = MesDatabaseInsightRules.NearExpiryDays;
        _horizon.Value = MesDatabaseInsightRules.DemandHorizonDays;
        _preBuy.Value = (int)MesDatabaseInsightRules.PreBuyCoverDays;
        _window.Value = MesDatabaseInsightRules.DeviceWindowDays;
        _abnormal.Value = MesDatabaseInsightRules.DeviceAbnormalThreshold;
        _stale.Value = MesDatabaseInsightRules.MaintenanceStaleDays;
        _slowWindow.Value = MesDatabaseInsightRules.SlowMovingWindowDays;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        MesDatabaseInsightRules.NearExpiryDays = (int)_nearExpiry.Value;
        MesDatabaseInsightRules.DemandHorizonDays = (int)_horizon.Value;
        MesDatabaseInsightRules.PreBuyCoverDays = (double)_preBuy.Value;
        MesDatabaseInsightRules.DeviceWindowDays = (int)_window.Value;
        MesDatabaseInsightRules.DeviceAbnormalThreshold = (int)_abnormal.Value;
        MesDatabaseInsightRules.MaintenanceStaleDays = (int)_stale.Value;
        MesDatabaseInsightRules.SlowMovingWindowDays = (int)_slowWindow.Value;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BtnReset_Click(object? sender, EventArgs e)
    {
        MesDatabaseInsightRules.ResetToDefault();
        _nearExpiry.Value = MesDatabaseInsightRules.NearExpiryDays;
        _horizon.Value = MesDatabaseInsightRules.DemandHorizonDays;
        _preBuy.Value = (int)MesDatabaseInsightRules.PreBuyCoverDays;
        _window.Value = MesDatabaseInsightRules.DeviceWindowDays;
        _abnormal.Value = MesDatabaseInsightRules.DeviceAbnormalThreshold;
        _stale.Value = MesDatabaseInsightRules.MaintenanceStaleDays;
        _slowWindow.Value = MesDatabaseInsightRules.SlowMovingWindowDays;
        MessageBox.Show("已恢复默认阈值，点「保存并重新分析」生效。", "恢复默认",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
