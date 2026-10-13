using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// <summary>供应商碳数据录入/更新弹窗。</summary>
public class SupplierCarbonForm : Form
{
    private readonly TextBox _txtSupplier = new() { Width = 180 };
    private readonly ComboBox _cboMaterial = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _txtValue = new() { Width = 130 };
    private readonly CheckBox _chkNoData = new() { Text = "该供应商未提供数据", AutoSize = true };
    private readonly ComboBox _cboScope3 = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _cboSource = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly NumericUpDown _numYear = new() { Width = 90, Minimum = 2000, Maximum = 2100, Value = 2026 };
    private readonly TextBox _txtRemark = new() { Width = 260 };
    private readonly Label _tip = new()
    {
        AutoSize = false, Height = 34, ForeColor = Color.FromArgb(217, 119, 6), Font = new Font("微软雅黑", 8.5F)
    };

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
        Text = existing is null ? "录入供应商碳数据" : $"更新 {existing.Supplier} 的碳数据";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 440);
        MaximizeBox = false;
        Font = new Font("微软雅黑", 9F);

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

        var head = new Label
        {
            Dock = DockStyle.Top, Height = 88, Padding = new Padding(14, 10, 14, 6),
            BackColor = Color.White, ForeColor = Color.FromArgb(51, 65, 85),
            Text = "录入某供应商供应的某物料的碳强度（kgCO2e/单位）。\n\n"
                 + "<b>未提供数据 ≠ 0 排放</b>：勾选「未提供」后该供应商不参与排名，"
                 + "避免因数据缺失反而排到第一名。"
        };

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(14, 8, 14, 6)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
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

        AddRow(0, "供应商 *", _txtSupplier);
        AddRow(1, "物料 *", _cboMaterial);
        AddRow(2, "碳强度 *", _txtValue);
        form.Controls.Add(_chkNoData, 1, 3);
        AddRow(4, "范围三类别", _cboScope3);
        AddRow(5, "数据来源", _cboSource);
        AddRow(6, "数据年份", _numYear);
        AddRow(7, "备注", _txtRemark);

        _chkNoData.CheckedChanged += (_, _) =>
        {
            _txtValue.Enabled = !_chkNoData.Checked;
            _cboSource.Enabled = !_chkNoData.Checked;
            CheckInput();
        };
        _txtValue.TextChanged += (_, _) => CheckInput();
        _txtSupplier.TextChanged += (_, _) => CheckInput();
        _txtValue.Enabled = true;
        CheckInput();

        var btnOk = new Button { Text = "下一步", Width = 110, Height = 32 };
        var btnCancel = new Button { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) =>
        {
            if (Supplier.Length == 0) { ScWarn("请填写供应商名称。"); return; }
            if (MaterialCode.Length == 0) { ScWarn("请选择物料。"); return; }
            if (!_chkNoData.Checked && KgPerUnit is null)
            { ScWarn(_tip.Text.Length > 0 ? _tip.Text : "请填写正确的碳强度（≥ 0），或勾选「未提供」。"); return; }
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