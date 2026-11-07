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
public partial class CertificateEditForm : Form
{
    public string EmpNo { get; private set; } = "";
    public string SkillItem { get; private set; } = "";
    public string Level { get; private set; } = CertLevel.Junior;
    public DateTime IssueDate { get; private set; } = DateTime.Today;
    public DateTime? ExpiryDate { get; private set; }
    public string IssuedBy { get; private set; } = "";

    public CertificateEditForm(UserProfile? presetUser = null, string? presetSkill = null)
    {
        InitializeComponent();
        InitializeRuntime(presetUser, presetSkill);
    }

    /// <summary>
    /// 执行期初始化：MesDatabase.Users 取数、foreach/for 建项、presetUser/presetSkill 相关的
    /// SelectedIndex、DateTime 运算，逐行保留原建构式语义。
    /// 控件建立 / 静态配置 / 加入容器均已搬至 CertificateEditForm.Designer.cs。
    /// </summary>
    private void InitializeRuntime(UserProfile? presetUser, string? presetSkill)
    {
        foreach (var u in MesDatabase.Users.Where(u => u.IsActive).OrderBy(u => u.EmpNo, StringComparer.Ordinal))
            _cboEmp.Items.Add(new EmpItem(u));
        if (_cboEmp.Items.Count > 0)
        {
            _cboEmp.SelectedIndex = 0;
            if (presetUser is not null)
                for (var i = 0; i < _cboEmp.Items.Count; i++)
                    if (((EmpItem)_cboEmp.Items[i]!).EmpNo == presetUser.EmpNo) { _cboEmp.SelectedIndex = i; break; }
        }
        _cboSkill.Items.AddRange(SkillCatalog.All.Cast<object>().ToArray());
        _cboSkill.SelectedIndex = presetSkill is null ? 0 : Math.Max(0, Array.IndexOf(SkillCatalog.All, presetSkill));
        _cboLevel.Items.AddRange(CertLevel.All.Cast<object>().ToArray());
        _cboLevel.SelectedIndex = 0;
        _dtpIssue.Value = DateTime.Today.AddMonths(-6);
        _dtpExpiry.Value = DateTime.Today.AddYears(2);
    }

    private void ChkLongTerm_CheckedChanged(object? sender, EventArgs e) =>
        _dtpExpiry.Enabled = !_chkLongTerm.Checked;

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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
