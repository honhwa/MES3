using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// <summary>报告元信息填写弹窗（组织/依据/编制人/备注）。</summary>
public partial class CarbonReportForm : Form
{
    public CarbonReportMeta Meta { get; } = new();

    public CarbonReportForm(CarbonAuditTrail audit)
    {
        InitializeComponent();
        InitializeRuntime(audit);
    }

    /// <summary>
    /// 执行期初始化：仅处理依赖传入参数 / Meta 的 Text 与插值字串
    /// （Designer 无法解析）；其余控件建立 / 静态配置 / 加入容器均在
    /// CarbonReportForm.Designer.cs 的 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(CarbonAuditTrail audit)
    {
        _txtOrg.Text = Meta.Organization;
        _txtStandard.Text = Meta.Standard;
        _txtRemark.Text = $"台账 {audit.AuditNo}，边界 {audit.BoundaryText}，期间 {audit.PeriodText}。";
        head.Text = $"为台账 {audit.AuditNo} 生成报告（{audit.ItemCount} 项排放源，总排放 {audit.TotalTText}）。\n"
             + "报告含：核算概要 / 分范围汇总 / **逐项排放源清单** / 因子版本与来源 / 口径与免责。";
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        Meta.Organization = _txtOrg.Text.Trim();
        Meta.Standard = _txtStandard.Text.Trim();
        Meta.PreparedBy = _txtBy.Text.Trim() is "（待填）" or "" ? "" : _txtBy.Text.Trim();
        Meta.Remark = _txtRemark.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
