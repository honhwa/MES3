using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// <summary>抄表数录入弹窗：选计量点 + 账期 + 用量。</summary>
public partial class EnergyEntryForm : Form
{
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
        InitializeComponent();
        InitializeRuntime(meters);
    }

    /// <summary>
    /// 执行期初始化：依赖参数的 Items / SelectedIndex、MesDatabase 取数、回圈，
    /// 以及原本在动态资料赋值之后才挂接的「值变更监控」事件（改具名方法、维持挂接顺序，
    /// 避免在初始化期提前触发 CheckValue 而改变表单初始状态）。
    /// </summary>
    private void InitializeRuntime(List<EnergyMeter> meters)
    {
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

        _txtValue.TextChanged += new System.EventHandler(this.TxtValue_TextChanged);
        _cboMeter.SelectedIndexChanged += new System.EventHandler(this.CboMeter_SelectedIndexChanged);
        _txtValue.Text = "";
    }

    private void TxtValue_TextChanged(object? sender, EventArgs e) => CheckValue();

    private void CboMeter_SelectedIndexChanged(object? sender, EventArgs e) => CheckValue();

    private void BtnOk_Click(object? sender, EventArgs e)
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
