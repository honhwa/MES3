using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// <summary>供应商碳数据录入/更新弹窗。</summary>
public partial class SupplierCarbonForm : Form
{
    public string Supplier => _txtSupplier.Text.Trim();
    public string MaterialCode => (_cboMaterial.SelectedItem as MatItem)?.Code ?? "";
    public double? KgPerUnit => _chkNoData.Checked
        ? null
        : double.TryParse(_txtValue.Text.Trim(), out var v) && v >= 0 ? v : null;
    public string Scope3Category => _cboScope3.SelectedItem?.ToString() ?? Scope3Categories.PurchasedGoods;
    public string Source => _cboSource.SelectedItem?.ToString() ?? SupplierCarbonSources.SupplierProvided;
    public int DataYear => (int)_numYear.Value;
    public string Remark => _txtRemark.Text.Trim();

    private sealed class MatItem
    {
        public string Code { get; init; } = "";
        public string Text { get; init; } = "";
        public string Unit { get; init; } = "";
        public override string ToString() => Text;
    }

    public SupplierCarbonForm(SupplierCarbon? existing, List<WmsMaterial> materials)
    {
        InitializeComponent();
        InitializeRuntime(existing, materials);
    }

    /// <summary>
    /// 执行期初始化：依赖 existing / materials 的 Text / Items / SelectedIndex / Value、
    /// 各 foreach 建项与 CheckInput() 呼叫，逐行保留原建构式语义。
    /// 原本在动态资料赋值之后才挂接的「值变更监控」事件改具名方法、维持挂接顺序，
    /// 避免在初始化期提前触发 CheckInput 而改变表单初始状态。
    /// 控件建立 / 静态配置 / 加入容器均已搬至 SupplierCarbonForm.Designer.cs。
    /// </summary>
    private void InitializeRuntime(SupplierCarbon? existing, List<WmsMaterial> materials)
    {
        Text = existing is null ? "录入供应商碳数据" : $"更新 {existing.Supplier} 的碳数据";

        foreach (var m in materials)
            _cboMaterial.Items.Add(new MatItem
            {
                Code = m.Code, Unit = m.Unit, Text = $"{m.MaterialName}（{m.Code}，{m.Unit}）"
            });
        _cboMaterial.SelectedIndex = 0;

        foreach (var c in Scope3Categories.All) _cboScope3.Items.Add(c);
        _cboScope3.SelectedIndex = 0;
        foreach (var s in SupplierCarbonSources.All) _cboSource.Items.Add(s);
        _cboSource.SelectedIndex = 0;

        if (existing is not null)
        {
            _txtSupplier.Text = existing.Supplier;
            var idx = _cboMaterial.Items.Cast<MatItem>().ToList().FindIndex(x => x.Code == existing.MaterialCode);
            if (idx >= 0) _cboMaterial.SelectedIndex = idx;
            if (existing.KgPerUnit is null) _chkNoData.Checked = true;
            else _txtValue.Text = existing.KgPerUnit.Value.ToString("0.####");
            var si = _cboScope3.Items.Cast<object>().ToList().FindIndex(o => o.ToString() == existing.Scope3Category);
            if (si >= 0) _cboScope3.SelectedIndex = si;
            var src = _cboSource.Items.Cast<object>().ToList().FindIndex(o => o.ToString() == existing.Source);
            if (src >= 0) _cboSource.SelectedIndex = src;
            _numYear.Value = existing.DataYear is >= 2000 and <= 2100 ? existing.DataYear : 2026;
            _txtRemark.Text = existing.Remark;
        }

        _chkNoData.CheckedChanged += new System.EventHandler(this.ChkNoData_CheckedChanged);
        _txtValue.TextChanged += new System.EventHandler(this.TxtValue_TextChanged);
        _txtSupplier.TextChanged += new System.EventHandler(this.TxtSupplier_TextChanged);
        CheckInput();
    }

    private void ChkNoData_CheckedChanged(object? sender, EventArgs e)
    {
        _txtValue.Enabled = !_chkNoData.Checked;
        _cboSource.Enabled = !_chkNoData.Checked;
        CheckInput();
    }

    private void TxtValue_TextChanged(object? sender, EventArgs e) => CheckInput();

    private void TxtSupplier_TextChanged(object? sender, EventArgs e) => CheckInput();

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (Supplier.Length == 0) { ScWarn("请填写供应商名称。"); return; }
        if (MaterialCode.Length == 0) { ScWarn("请选择物料。"); return; }
        if (!_chkNoData.Checked && KgPerUnit is null)
        { ScWarn(_tip.Text.Length > 0 ? _tip.Text : "请填写正确的碳强度（≥ 0），或勾选「未提供」。"); return; }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CheckInput()
    {
        if (_chkNoData.Checked)
        {
            _tip.Text = "已标记为「未提供」——该供应商不参与排名（不是 0 排放）";
            _tip.ForeColor = Color.FromArgb(217, 119, 6);
            return;
        }
        var unit = (_cboMaterial.SelectedItem as MatItem)?.Unit ?? "";
        if (_txtValue.Text.Trim().Length == 0)
        {
            _tip.Text = $"请输入碳强度，单位 kgCO2e/{unit}；或勾选「未提供」";
            _tip.ForeColor = Color.FromArgb(217, 119, 6);
            return;
        }
        if (!double.TryParse(_txtValue.Text.Trim(), out var v) || v < 0)
        {
            _tip.Text = "碳强度必须是不小于 0 的数字";
            _tip.ForeColor = Color.FromArgb(220, 38, 38);
            return;
        }
        _tip.Text = $"已填 {v:0.####} kgCO2e/{unit}";
        _tip.ForeColor = Color.FromArgb(21, 128, 61);
    }

    private static void ScWarn(string m) =>
        MessageBox.Show(m, "请完善填写", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
