using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// <summary>抄表数录入弹窗：选计量点 + 账期 + 用量。</summary>
public class EnergyEntryForm : Form
{
    private readonly ComboBox _cboMeter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly ComboBox _cboPeriod = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _txtValue = new() { Width = 130 };
    private readonly ComboBox _cboSource = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _txtRemark = new() { Width = 260 };
    private readonly Label _tip = new()
    {
        AutoSize = false, Height = 34, ForeColor = Color.FromArgb(217, 119, 6), Font = new Font("微软雅黑", 8.5F)
    };

    public string MeterId => (_cboMeter.SelectedItem as MeterItem)?.MeterId ?? "";
    public string Period => _cboPeriod.SelectedItem?.ToString() ?? "";
    public double Value => double.TryParse(_txtValue.Text.Trim(), out var v) ? v : -1;
    public string Source => _cboSource.SelectedItem?.ToString() ?? "人工录入";
    public string Remark => _txtRemark.Text.Trim();

    private sealed class MeterItem
    {
        public string MeterId { get; init; } = "";
        public string Text { get; init; } = "";
        public string Unit { get; init; } = "";
        public override string ToString() => Text;
    }

    public EnergyEntryForm(List<EnergyMeter> meters)
    {
        Text = "录入抄表数";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 400);
        MaximizeBox = false;
        Font = new Font("微软雅黑", 9F);

        foreach (var m in meters.Where(x => x.Enabled))
            _cboMeter.Items.Add(new MeterItem
            {
                MeterId = m.MeterId, Unit = m.Unit,
                Text = $"[{m.Scope}] {m.DisplayName}　{m.TypeName}（{m.MeterId}）"
            });
        if (_cboMeter.Items.Count > 0) _cboMeter.SelectedIndex = 0;

        // 账期：当前月 + 过去 6 个月
        var now = DateTime.Now;
        for (var i = 0; i >= -6; i--)
            _cboPeriod.Items.Add(MesDatabase.ToPeriod(new DateTime(now.Year, now.Month, 1).AddMonths(i)));
        _cboPeriod.SelectedIndex = 0;

        _cboSource.Items.AddRange(new object[] { "人工录入", "网关采集", "Excel 导入" });
        _cboSource.SelectedIndex = 0;

        var head = new Label
        {
            Dock = DockStyle.Top, Height = 62, Padding = new Padding(14, 10, 14, 6),
            BackColor = Color.White, ForeColor = Color.FromArgb(51, 65, 85),
            Text = "录入某计量点某账期的能耗消耗量。\n"
                 + "**同计量点同账期重复录入会覆盖修正**（修正已有读数），不会产生重复行。"
        };

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(14, 8, 14, 6)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(int r, string label, Control ctl)
        {
            form.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(51, 65, 85)
            }, 0, r);
            ctl.Anchor = AnchorStyles.Left;
            form.Controls.Add(ctl, 1, r);
        }

        AddRow(0, "计量点 *", _cboMeter);
        AddRow(1, "账期 *", _cboPeriod);
        AddRow(2, "消耗量 *", _txtValue);
        AddRow(3, "数据来源", _cboSource);
        AddRow(4, "备注", _txtRemark);
        form.Controls.Add(_tip, 1, 5);

        _txtValue.TextChanged += (_, _) => CheckValue();
        _cboMeter.SelectedIndexChanged += (_, _) => CheckValue();
        _txtValue.Text = "";

        var btnOk = new Button { Text = "下一步", Width = 110, Height = 32 };
        var btnCancel = new Button { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) =>
        {
            if (MeterId.Length == 0) { Warn("请选择计量点。"); return; }
            if (Period.Length != 7) { Warn("请选择账期。"); return; }
            if (Value < 0)
            {
                Warn(_tip.Text.Length > 0 ? _tip.Text : "请填写正确的消耗量（≥ 0 的数字）。");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 5, 12, 0)
        };
        btnRow.Controls.Add(btnCancel);
        btnRow.Controls.Add(btnOk);

        Controls.Add(form);
        Controls.Add(head);
        Controls.Add(btnRow);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void CheckValue()
    {
        var unit = (_cboMeter.SelectedItem as MeterItem)?.Unit ?? "";
        if (_txtValue.Text.Trim().Length == 0)
        {
            _tip.Text = $"请输入消耗量，单位 {unit}";
            _tip.ForeColor = Color.FromArgb(217, 119, 6);
            return;
        }
        if (!double.TryParse(_txtValue.Text.Trim(), out var v) || v < 0)
        {
            _tip.Text = "消耗量必须是不小于 0 的数字";
            _tip.ForeColor = Color.FromArgb(220, 38, 38);
            return;
        }
        _tip.Text = $"已填 {v:0.##} {unit}";
        _tip.ForeColor = Color.FromArgb(21, 128, 61);
    }

    private static void Warn(string m) =>
        MessageBox.Show(m, "请完善填写", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}