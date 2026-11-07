using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// ============================================================================
// 碳资产录入窗：新增一条配额 / CCER 台账记录（kind = "A" 配额 / "C" CCER）
// ============================================================================

public partial class CarbonAssetEntryForm : Form
{
    private string _kind = "";

    public string Year { get; private set; } = "";
    public string TxType { get; private set; } = "";
    public string Project { get; private set; } = "";
    public double Quantity { get; private set; }
    public double UnitPrice { get; private set; }
    public DateTime TxDate { get; private set; } = DateTime.Now;
    public string Note { get; private set; } = "";

    public CarbonAssetEntryForm(string kind, string defaultYear, IEnumerable<string> years)
    {
        InitializeComponent();
        InitializeRuntime(kind, defaultYear, years);
    }

    /// <summary>
    /// 执行期初始化：依赖参数的标题、ComboBox 选项（foreach / LINQ）、
    /// 可见性判断（Designer 无法解析）；其余控件建立 / 静态配置 / 加入容器
    /// 均在 CarbonAssetEntryForm.Designer.cs 的 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(string kind, string defaultYear, IEnumerable<string> years)
    {
        _kind = kind == "C" ? "C" : "A";
        Text = _kind == "A" ? "新增碳配额记录" : "新增 CCER 记录";

        foreach (var t in (_kind == "A" ? CarbonAssetTypes.AllowanceTypes : CarbonAssetTypes.CcerTypes))
            _cboType.Items.Add(t);
        _cboType.SelectedIndex = 0;

        foreach (var y in years.ToList())
            _cboYear.Items.Add(y);
        _cboYear.SelectedItem = years.FirstOrDefault(x => x == defaultYear) ?? years.FirstOrDefault() ?? "";

        lblProject.Visible = _kind == "C";
        _txtProject.Visible = _kind == "C";
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
