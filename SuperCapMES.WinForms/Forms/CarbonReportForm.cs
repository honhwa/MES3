using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// <summary>报告元信息填写弹窗（组织/依据/编制人/备注）。</summary>
public class CarbonReportForm : Form
{
    private readonly TextBox _txtOrg = new() { Width = 260 };
    private readonly TextBox _txtStandard = new() { Width = 260 };
    private readonly TextBox _txtBy = new() { Width = 200 };
    private readonly TextBox _txtRemark = new() { Width = 260, Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical };
    private readonly Label _tip = new()
    {
        AutoSize = false, Height = 32, ForeColor = Color.FromArgb(21, 128, 61), Font = new Font("微软雅黑", 8.5F)
    };

    public CarbonReportMeta Meta { get; } = new();

    public CarbonReportForm(CarbonAuditTrail audit)
    {
        Text = "生成碳核查报告";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 400);
        MaximizeBox = false;
        Font = new Font("微软雅黑", 9F);

        _txtOrg.Text = Meta.Organization;
        _txtStandard.Text = Meta.Standard;
        _txtBy.Text = "（待填）";
        _txtRemark.Text = $"台账 {audit.AuditNo}，边界 {audit.BoundaryText}，期间 {audit.PeriodText}。";

        var head = new Label
        {
            Dock = DockStyle.Top, Height = 62, Padding = new Padding(14, 10, 14, 6),
            BackColor = Color.White, ForeColor = Color.FromArgb(51, 65, 85),
            Text = $"为台账 {audit.AuditNo} 生成报告（{audit.ItemCount} 项排放源，总排放 {audit.TotalTText}）。\n"
                 + "报告含：核算概要 / 分范围汇总 / **逐项排放源清单** / 因子版本与来源 / 口径与免责。"
        };

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(14, 8, 14, 6)
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

        AddRow(0, "组织名称", _txtOrg);
        AddRow(1, "核算依据", _txtStandard);
        AddRow(2, "编制人", _txtBy);
        AddRow(3, "备注", _txtRemark);
        form.Controls.Add(_tip, 1, 4);
        _tip.Text = "编制人将写入报告头；核算依据默认 GB/T 32150-2015，可按实际更换。";

        var btnOk = new Button { Text = "下一步", Width = 110, Height = 32 };
        var btnCancel = new Button { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) =>
        {
            Meta.Organization = _txtOrg.Text.Trim();
            Meta.Standard = _txtStandard.Text.Trim();
            Meta.PreparedBy = _txtBy.Text.Trim() is "（待填）" or "" ? "" : _txtBy.Text.Trim();
            Meta.Remark = _txtRemark.Text.Trim();
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
}