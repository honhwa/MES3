using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// ============================================================================
// 废弃 / 重新进料检 弹窗
//   必填项一律在界面层先拦一道（不给用户"提交完才报错"的挫败感），
//   后端再拦一道（防止绕过界面直接调接口）。
// ============================================================================

/// <summary>废弃弹窗：原因 + 操作人 必填。</summary>
public partial class ScrapForm : Form
{
    public string Reason => _cboReason.SelectedItem?.ToString() ?? "";
    public string Operator => _txtOperator.Text.Trim();
    public string Remark => _txtRemark.Text.Trim();

    public ScrapForm(WmsStock stock, string defaultOperator = "")
    {
        InitializeComponent();
        InitializeRuntime(stock, defaultOperator);
    }

    /// <summary>
    /// 执行期初始化：仅处理依赖传入参数 / 回圈建项的内容（Designer 无法解析的插值字串、foreach、SelectedIndex）。
    /// 原建构式内除了这些赋值外，其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(WmsStock stock, string defaultOperator)
    {
        head.Text = $"物料：{stock.MaterialName}（{stock.MaterialCode}）\n" +
                   $"批次：{stock.LotNo}　库位：{stock.Location}　数量：{stock.Quantity:0.##} {stock.Unit}\n\n" +
                   "<b>废弃不可撤销</b>：该批次将退出可用库存与 FIFO 队列，数量归零。";

        foreach (var r in ScrapReasons.All) _cboReason.Items.Add(r);
        _cboReason.SelectedIndex = 0;
        _txtOperator.Text = defaultOperator;
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (Operator.Length == 0)
        {
            MessageBox.Show("请填写操作人。", "请完善填写", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Reason.Length == 0)
        {
            MessageBox.Show("请选择废弃原因。", "请完善填写", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>重新进料检弹窗：进料检人 + 新效期 必填，新效期必须晚于今天。</summary>
public partial class ReinspectForm : Form
{
    private DateTime? _oldExpiry;

    public string Inspector => _txtInspector.Text.Trim();
    public DateTime NewExpiry => _dtpNew.Value.Date;
    public string Result => _cboResult.SelectedItem?.ToString() ?? ReinspectResults.Pass;
    public string Remark => _txtRemark.Text.Trim();

    public ReinspectForm(WmsStock stock, string defaultInspector = "")
    {
        InitializeComponent();
        InitializeRuntime(stock, defaultInspector);
    }

    /// <summary>
    /// 执行期初始化：依赖参数 / 回圈 / 时间 / 事件的动态内容。
    /// 其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(WmsStock stock, string defaultInspector)
    {
        head.Text = $"物料：{stock.MaterialName}（{stock.MaterialCode}）\n" +
                   $"批次：{stock.LotNo}　原效期：<b>{(stock.ExpiryDate?.ToString("yyyy-MM-dd") ?? "无")}</b>\n\n" +
                   "进料检通过后，主表效期更新为新效期，查询列表与 FIFO 立即按新效期显示。";

        _txtInspector.Text = defaultInspector;
        // 默认给 90 天，省得用户自己算；最小值锁定为明天（今天没有可检的意义）
        _dtpNew.MinDate = DateTime.Today.AddDays(1);
        _dtpNew.Value = DateTime.Today.AddDays(90);
        _dtpNew.ValueChanged += DtpNew_ValueChanged;
        foreach (var r in ReinspectResults.All) _cboResult.Items.Add(r);
        _cboResult.SelectedIndex = 0;

        CheckDate();
    }

    private void DtpNew_ValueChanged(object? sender, EventArgs e) => CheckDate();

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (Inspector.Length == 0)
        {
            MessageBox.Show("请填写进料检人。", "请完善填写", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (NewExpiry <= DateTime.Today)
        {
            MessageBox.Show($"新效期必须晚于今天（当前 {DateTime.Today:yyyy-MM-dd}）。", "新效期不合法",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>传入原效期以显示"延长了多少天"</summary>
    public void BindOldExpiry(DateTime? old)
    {
        _oldExpiry = old;
        CheckDate();
    }

    private void CheckDate()
    {
        var bad = NewExpiry <= DateTime.Today;
        if (bad)
        {
            _tip.Text = "⚠ 新效期必须晚于今天，否则等同于没检";
            _tip.ForeColor = Color.FromArgb(220, 38, 38);
            return;
        }
        var baseText = _oldExpiry is null ? "原无有效期记录" : $"原效期 {_oldExpiry:yyyy-MM-dd}";
        var ext = _oldExpiry is null ? 0 : (NewExpiry - _oldExpiry.Value.Date).TotalDays;
        _tip.Text = $"新效期 {NewExpiry:yyyy-MM-dd}　{baseText}　"
                    + (_oldExpiry is null ? "" : $"（{(ext >= 0 ? "延长" : "缩短")} {Math.Abs(ext):0} 天）");
        _tip.ForeColor = Color.FromArgb(21, 128, 61);
    }
}
