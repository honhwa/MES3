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
public class ScrapForm : Form
{
    private readonly ComboBox _cboReason = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _txtOperator = new() { Width = 150 };
    private readonly TextBox _txtRemark = new() { Width = 230 };

    public string Reason => _cboReason.SelectedItem?.ToString() ?? "";
    public string Operator => _txtOperator.Text.Trim();
    public string Remark => _txtRemark.Text.Trim();

    public ScrapForm(WmsStock stock, string defaultOperator = "")
    {
        Text = "废弃库存批次";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 320);
        MaximizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var head = new Label
        {
            Dock = DockStyle.Top,
            Height = 88,
            Padding = new Padding(14, 10, 14, 6),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 65, 85),
            Text = $"物料：{stock.MaterialName}（{stock.MaterialCode}）\n" +
                   $"批次：{stock.LotNo}　库位：{stock.Location}　数量：{stock.Quantity:0.##} {stock.Unit}\n\n" +
                   "<b>废弃不可撤销</b>：该批次将退出可用库存与 FIFO 队列，数量归零。"
        };

        foreach (var r in ScrapReasons.All) _cboReason.Items.Add(r);
        _cboReason.SelectedIndex = 0;
        _txtOperator.Text = defaultOperator;

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(14, 8, 14, 6)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(int r, string label, Control ctl)
        {
            form.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Anchor = AnchorStyles.Left,
                ForeColor = Color.FromArgb(51, 65, 85)
            }, 0, r);
            ctl.Anchor = AnchorStyles.Left;
            form.Controls.Add(ctl, 1, r);
        }

        AddRow(0, "废弃原因 *", _cboReason);
        AddRow(1, "操作人 *", _txtOperator);
        AddRow(2, "备注", _txtRemark);
        form.Controls.Add(new Label
        {
            Text = "* 为必填。台账会记录原因、操作人与操作时间，供追溯。",
            AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Left
        }, 1, 3);

        var btnOk = new Button { Text = "下一步", Width = 110, Height = 32 };
        var btnCancel = new Button { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) =>
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

/// <summary>重新进料检弹窗：进料检人 + 新效期 必填，新效期必须晚于今天。</summary>
public class ReinspectForm : Form
{
    private readonly TextBox _txtInspector = new() { Width = 150 };
    private readonly DateTimePicker _dtpNew = new()
    {
        Width = 160, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd"
    };
    private readonly ComboBox _cboResult = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _txtRemark = new() { Width = 230 };
    private readonly Label _tip = new()
    {
        AutoSize = false, Height = 36, ForeColor = Color.FromArgb(220, 38, 38), Font = new Font("微软雅黑", 8.5F)
    };

    private DateTime? _oldExpiry;

    public string Inspector => _txtInspector.Text.Trim();
    public DateTime NewExpiry => _dtpNew.Value.Date;
    public string Result => _cboResult.SelectedItem?.ToString() ?? ReinspectResults.Pass;
    public string Remark => _txtRemark.Text.Trim();

    public ReinspectForm(WmsStock stock, string defaultInspector = "")
    {
        Text = "重新进料检（已过期物料）";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 380);
        MaximizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var head = new Label
        {
            Dock = DockStyle.Top,
            Height = 88,
            Padding = new Padding(14, 10, 14, 6),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 65, 85),
            Text = $"物料：{stock.MaterialName}（{stock.MaterialCode}）\n" +
                   $"批次：{stock.LotNo}　原效期：<b>{(stock.ExpiryDate?.ToString("yyyy-MM-dd") ?? "无")}</b>\n\n" +
                   "进料检通过后，主表效期更新为新效期，查询列表与 FIFO 立即按新效期显示。"
        };

        _txtInspector.Text = defaultInspector;
        // 默认给 90 天，省得用户自己算；最小值锁定为明天（今天没有可检的意义）
        _dtpNew.MinDate = DateTime.Today.AddDays(1);
        _dtpNew.Value = DateTime.Today.AddDays(90);
        _dtpNew.ValueChanged += (_, _) => CheckDate();
        foreach (var r in ReinspectResults.All) _cboResult.Items.Add(r);
        _cboResult.SelectedIndex = 0;

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(14, 8, 14, 6)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
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

        AddRow(0, "进料检人 *", _txtInspector);
        AddRow(1, "新效期 *", _dtpNew);
        AddRow(2, "检验结论", _cboResult);
        AddRow(3, "备注", _txtRemark);
        form.Controls.Add(_tip, 1, 4);

        var btnOk = new Button { Text = "下一步", Width = 110, Height = 32 };
        var btnCancel = new Button { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) =>
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
        CheckDate();
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