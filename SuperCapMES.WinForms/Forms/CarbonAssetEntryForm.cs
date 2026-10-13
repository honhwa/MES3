using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// ============================================================================
// 碳资产录入窗：新增一条配额 / CCER 台账记录（kind = "A" 配额 / "C" CCER）
// ============================================================================

public class CarbonAssetEntryForm : Form
{
    private readonly string _kind;

    public string Year { get; private set; } = "";
    public string TxType { get; private set; } = "";
    public string Project { get; private set; } = "";
    public double Quantity { get; private set; }
    public double UnitPrice { get; private set; }
    public DateTime TxDate { get; private set; } = DateTime.Now;
    public string Note { get; private set; } = "";

    private readonly ComboBox _cboYear = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _txtProject = new() { Width = 200 };
    private readonly TextBox _txtQty = new() { Width = 200 };
    private readonly TextBox _txtPrice = new() { Width = 200 };
    private readonly DateTimePicker _dtp = new() { Width = 200, Format = DateTimePickerFormat.Short };
    private readonly TextBox _txtNote = new() { Width = 200, Height = 48, Multiline = true };
    private readonly Label _lblMsg = new() { ForeColor = System.Drawing.Color.FromArgb(200, 30, 30), Height = 32, AutoSize = false, Width = 320 };

    public CarbonAssetEntryForm(string kind, string defaultYear, IEnumerable<string> years)
    {
        _kind = kind == "C" ? "C" : "A";
        Text = _kind == "A" ? "新增碳配额记录" : "新增 CCER 记录";
        Size = new System.Drawing.Size(380, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        foreach (var t in (_kind == "A" ? CarbonAssetTypes.AllowanceTypes : CarbonAssetTypes.CcerTypes))
            _cboType.Items.Add(t);
        _cboType.SelectedIndex = 0;

        foreach (var y in years.ToList())
            _cboYear.Items.Add(y);
        _cboYear.SelectedItem = years.FirstOrDefault(x => x == defaultYear) ?? years.FirstOrDefault() ?? "";

        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
        ok.Click += OnOk;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(12, 12, 12, 4),
            ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.AutoSize) }
        };
        void AddRow(Label label, Control ctrl)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(label);
            grid.Controls.Add(ctrl);
        }
        AddRow(new Label { Text = "年度", Width = 80 }, _cboYear);
        AddRow(new Label { Text = "记录类型", Width = 80 }, _cboType);
        AddRow(new Label { Text = "来源/项目", Width = 80, Visible = _kind == "C" }, _txtProject);
        _txtProject.Visible = _kind == "C";
        AddRow(new Label { Text = "数量(tCO2e)", Width = 80 }, _txtQty);
        AddRow(new Label { Text = "单价(元/t)", Width = 80 }, _txtPrice);
        AddRow(new Label { Text = "日期", Width = 80 }, _dtp);
        AddRow(new Label { Text = "备注", Width = 80 }, _txtNote);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12, 6, 0, 0) };
        bar.Controls.Add(ok);
        bar.Controls.Add(cancel);

        Controls.Add(bar);
        Controls.Add(_lblMsg);
        _lblMsg.Dock = DockStyle.Bottom;
        Controls.Add(grid);

        _txtPrice.Text = "0"; // 流出类（清缴/抵消/收回/卖出）单价常填 0
    }

    private void OnOk(object? sender, EventArgs e)
    {
        Year = _cboYear.SelectedItem?.ToString() ?? "";
        TxType = _cboType.SelectedItem?.ToString() ?? "";
        Project = _txtProject.Text.Trim();
        Note = _txtNote.Text.Trim();
        TxDate = _dtp.Value.Date;

        if (!double.TryParse(_txtQty.Text.Trim(), out var q) || q <= 0)
        {
            _lblMsg.Text = "数量必须填大于 0 的数字。";
            DialogResult = DialogResult.None;
            return;
        }
        if (!double.TryParse(_txtPrice.Text.Trim(), out var p) || p < 0)
        {
            _lblMsg.Text = "单价必须填不小于 0 的数字。";
            DialogResult = DialogResult.None;
            return;
        }
        if (_kind == "C" && Project.Length == 0)
        {
            _lblMsg.Text = "CCER 必须填写来源/项目。";
            DialogResult = DialogResult.None;
            return;
        }
        Quantity = q;
        UnitPrice = p;
    }
}
