using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 资质登记窗体：人员 + 资质项 + 等级 + 发证/有效期 + 发证机构。
/// 业务校验（人员是否在职、资质项与等级是否合法、有效期是否早于发证、是否重复登记）
/// 统一在 <see cref="Data.MesDatabase.AddCertificate"/> 中执行，本窗体不做二次判定。
/// </summary>
public class CertificateEditForm : Form
{
    private readonly ComboBox _cboEmp = null!;
    private readonly ComboBox _cboSkill = null!;
    private readonly ComboBox _cboLevel = null!;
    private readonly DateTimePicker _dtpIssue = null!;
    private readonly DateTimePicker _dtpExpiry = null!;
    private readonly CheckBox _chkLongTerm = null!;
    private readonly TextBox _txtIssuer = null!;

    public string EmpNo { get; private set; } = "";
    public string SkillItem { get; private set; } = "";
    public string Level { get; private set; } = CertLevel.Junior;
    public DateTime IssueDate { get; private set; } = DateTime.Today;
    public DateTime? ExpiryDate { get; private set; }
    public string IssuedBy { get; private set; } = "";

    public CertificateEditForm(UserProfile? presetUser = null, string? presetSkill = null)
    {
        Text = "登记上岗资质";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 280,
            ColumnCount = 2,
            Padding = new Padding(14, 16, 14, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 人员：只列在岗人员（离职人员由仓储层拒绝登记，此处直接不展示，避免误选）
        _cboEmp = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        foreach (var u in MesDatabase.Users.Where(u => u.IsActive).OrderBy(u => u.EmpNo, StringComparer.Ordinal))
            _cboEmp.Items.Add(new EmpItem(u));
        if (_cboEmp.Items.Count > 0)
        {
            _cboEmp.SelectedIndex = 0;
            if (presetUser is not null)
                for (var i = 0; i < _cboEmp.Items.Count; i++)
                    if (((EmpItem)_cboEmp.Items[i]!).EmpNo == presetUser.EmpNo) { _cboEmp.SelectedIndex = i; break; }
        }
        AddRow(table, "人员 *：", _cboEmp, 0);

        _cboSkill = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _cboSkill.Items.AddRange(SkillCatalog.All.Cast<object>().ToArray());
        _cboSkill.SelectedIndex = presetSkill is null ? 0 : Math.Max(0, Array.IndexOf(SkillCatalog.All, presetSkill));
        AddRow(table, "资质项 *：", _cboSkill, 1);

        _cboLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _cboLevel.Items.AddRange(CertLevel.All.Cast<object>().ToArray());
        _cboLevel.SelectedIndex = 0;
        AddRow(table, "等级 *：", _cboLevel, 2);

        _dtpIssue = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _dtpIssue.Value = DateTime.Today.AddMonths(-6);
        AddRow(table, "发证日期 *：", _dtpIssue, 3);

        var expiryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4), WrapContents = false };
        _dtpExpiry = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 130 };
        _dtpExpiry.Value = DateTime.Today.AddYears(2);
        _chkLongTerm = new CheckBox { Text = "长期有效", AutoSize = true, Margin = new Padding(10, 6, 0, 0) };
        _chkLongTerm.CheckedChanged += (_, _) => _dtpExpiry.Enabled = !_chkLongTerm.Checked;
        expiryPanel.Controls.Add(_dtpExpiry);
        expiryPanel.Controls.Add(_chkLongTerm);
        AddRow(table, "有效期至：", expiryPanel, 4);

        _txtIssuer = new TextBox { Text = "泰州工厂能力认定委员会", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "发证机构：", _txtIssuer, 5);

        var tip = new Label
        {
            Text = "提示：同一人员同一资质项只能持有一张有效证书；有效期不得早于发证日期。\n"
                 + "证书到期前 30 天起自动进入「即将到期」，到期次日即失效，届时该人员不可在对应工序上岗。",
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(14, 8, 14, 0),
            ForeColor = Color.Gray
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = "登记资质",
            Size = new Size(120, 32),
            Location = new Point(bottom.Width - 232, 10),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        var btnCancel = new Button { Text = "取消", Size = new Size(90, 32), Location = new Point(bottom.Width - 106, 10) };
        btnOk.Click += BtnOk_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(btnOk);
        bottom.Controls.Add(btnCancel);

        Controls.Add(bottom);
        Controls.Add(tip);
        Controls.Add(table);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control, int row)
    {
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_cboEmp.SelectedItem is not EmpItem item)
        {
            MessageBox.Show("请先选择人员。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        EmpNo = item.EmpNo;
        SkillItem = _cboSkill.SelectedItem?.ToString() ?? "";
        Level = _cboLevel.SelectedItem?.ToString() ?? CertLevel.Junior;
        IssueDate = _dtpIssue.Value.Date;
        ExpiryDate = _chkLongTerm.Checked ? null : _dtpExpiry.Value.Date;
        IssuedBy = _txtIssuer.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>下拉项：显示「姓名（工号 · 部门）」，绑定时保留工号。</summary>
    private sealed class EmpItem
    {
        public string EmpNo { get; }
        public EmpItem(UserProfile u) => EmpNo = u.EmpNo;
        public override string ToString()
        {
            var u = MesDatabase.FindUser(EmpNo);
            return u is null ? EmpNo : $"{u.Name}（{u.EmpNo} · {u.Dept}）";
        }
    }
}
