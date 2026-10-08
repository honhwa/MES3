using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 预警阈值配置对话框。
/// 三类预警的"合理阈值"取决于厂里的物料特性与设备状况，照搬到别的厂一定是错的，
/// 所以全部做成可配项——厂里试跑两周就能按实际情况调到位。
/// </summary>
public class InsightRulesForm : Form
{
    private readonly NumericUpDown _nearExpiry = new() { Minimum = 1, Maximum = 365, Width = 90 };
    private readonly NumericUpDown _horizon = new() { Minimum = 1, Maximum = 180, Width = 90 };
    private readonly NumericUpDown _preBuy = new() { Minimum = 1, Maximum = 120, Width = 90 };
    private readonly NumericUpDown _window = new() { Minimum = 1, Maximum = 365, Width = 90 };
    private readonly NumericUpDown _abnormal = new() { Minimum = 1, Maximum = 50, Width = 90 };
    private readonly NumericUpDown _stale = new() { Minimum = 7, Maximum = 730, Width = 90 };
    private readonly NumericUpDown _slowWindow = new() { Minimum = 7, Maximum = 365, Width = 90 };
    private readonly Label _tip = new()
    {
        AutoSize = false,
        Height = 58,
        ForeColor = Color.FromArgb(100, 116, 139),
        Font = new Font("微软雅黑", 8.5F),
        Text = "阈值改完点「保存并重新分析」立即生效；「恢复默认」回到内置值。"
             + "建议先按默认值跑一周，看预警条目是否有用/太吵，再按厂里实际情况调。"
    };

    public InsightRulesForm()
    {
        Text = "预警阈值配置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 430);
        MaximizeBox = false;
        Font = new Font("微软雅黑", 9F);

        _nearExpiry.Value = MesDatabaseInsightRules.NearExpiryDays;
        _horizon.Value = MesDatabaseInsightRules.DemandHorizonDays;
        _preBuy.Value = (int)MesDatabaseInsightRules.PreBuyCoverDays;
        _window.Value = MesDatabaseInsightRules.DeviceWindowDays;
        _abnormal.Value = MesDatabaseInsightRules.DeviceAbnormalThreshold;
        _stale.Value = MesDatabaseInsightRules.MaintenanceStaleDays;
        _slowWindow.Value = MesDatabaseInsightRules.SlowMovingWindowDays;

        var grid = new TableLayoutPanel
        {
            Location = new Point(16, 14),
            Size = new Size(568, 290),
            ColumnCount = 3,
            RowCount = 8
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Row(int r, string label, Control box, string hint)
        {
            grid.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Anchor = AnchorStyles.Left,
                ForeColor = Color.FromArgb(51, 65, 85)
            }, 0, r);
            grid.Controls.Add(box, 1, r);
            grid.Controls.Add(new Label
            {
                Text = hint, AutoSize = true, Anchor = AnchorStyles.Left,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("微软雅黑", 8.5F)
            }, 2, r);
        }

        grid.Controls.Add(new Label
        {
            Text = "① 物料临期与消耗", AutoSize = true, Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59)
        }, 0, 0);
        Row(1, "　临期预警天数", _nearExpiry, "剩余有效期 ≤ 该值即纳入分析");
        Row(2, "　需求预测视界（天）", _horizon, "按未来这么多天的工单需求推算日均消耗");
        Row(3, "　预防性采购余量（天）", _preBuy, "剩余量可撑不足该天数时判为需提前补货");
        grid.Controls.Add(new Label
        {
            Text = "② 设备异常集中 / ③ 呆滞料", AutoSize = true, Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59)
        }, 0, 4);
        Row(5, "　异常统计窗口（天）", _window, "只看该窗口内的异常");
        Row(6, "　异常频次阈值（次）", _abnormal, "窗口内达到该次数即列为风险设备");
        Row(7, "　保养陈旧天数", _stale, "距上次保养超过该天数判为需补做保养");

        _tip.Location = new Point(16, 312);
        _tip.Width = 568;

        var btnSave = new Button { Text = "保存并重新分析", Width = 130, Height = 32, Location = new Point(300, 378) };
        var btnReset = new Button { Text = "恢复默认", Width = 100, Height = 32, Location = new Point(192, 378) };
        var btnCancel = new Button { Text = "取消", Width = 90, Height = 32, Location = new Point(440, 378), DialogResult = DialogResult.Cancel };

        btnSave.Click += (_, _) =>
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
        };
        btnReset.Click += (_, _) =>
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
        };

        Controls.AddRange(new Control[] { grid, _tip, btnReset, btnSave, btnCancel });
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }
}