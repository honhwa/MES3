using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 返工重检登记窗体：填写重检标准值 / 实测值 / 判定（合格 → 放行；不合格 → 退回返工）。
/// 由调用方根据结果执行 <c>MesDatabase.RegisterReworkInspection</c>。
/// </summary>
public partial class ReworkInspectForm : Form
{
    public bool Passed { get; private set; }
    public string Standard { get; private set; } = "";
    public string Measured { get; private set; } = "";
    public string Remark { get; private set; } = "";
    public string Operator { get; private set; } = "";

    /// <param name="reworkNo">返工单号（标题展示）</param>
    /// <param name="cellSn">电芯 SN</param>
    /// <param name="defaultStandard">默认重检标准（如 容量 ≥95F）</param>
    public ReworkInspectForm(string reworkNo, string cellSn, string defaultStandard)
    {
        InitializeComponent();
        InitializeRuntime(reworkNo, cellSn, defaultStandard);
    }

    /// <summary>
    /// 执行期初始化：依赖参数的标题 / 资料列文字 / 默认标准
    /// （Designer 无法解析的插值字串）；其余控件建立 / 静态配置 /
    /// 加入容器均在 ReworkInspectForm.Designer.cs 的 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(string reworkNo, string cellSn, string defaultStandard)
    {
        Text = $"重检登记 - {reworkNo}";
        lblReworkNoValue.Text = reworkNo;
        lblCellSnValue.Text = cellSn;
        _txtStandard.Text = defaultStandard;
    }

    private static void AddLabel(TableLayoutPanel table, string text, int row) =>
        table.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 0, row);

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_rbPass.Checked && string.IsNullOrWhiteSpace(_txtMeasured.Text))
        {
            if (MessageBox.Show("判定为「合格」但未填写实测值，确认继续？", "提示", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }

        Passed = _rbPass.Checked;
        Standard = _txtStandard.Text.Trim();
        Measured = _txtMeasured.Text.Trim();
        Remark = _txtRemark.Text.Trim();
        Operator = string.IsNullOrWhiteSpace(_txtOperator.Text) ? "admin" : _txtOperator.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }
}
