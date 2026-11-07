using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 人员新增/编辑窗体：工号（新增时可录、编辑时只读）、姓名、部门、岗位、班组、在职状态、电话、入职日期。
/// 校验在 <see cref="Data.MesDatabase.AddUser"/> / <see cref="Data.MesDatabase.UpdateUser"/> 中统一处理，
/// 本窗体只做「必填 + 格式」的最前端提示，由调用方落库。
/// </summary>
public partial class PersonnelEditForm : Form
{
    private bool _isEdit;

    /// <summary>编辑结果：调用方据此写库</summary>
    public UserProfile Profile { get; } = new();

    public PersonnelEditForm(UserProfile? existing = null)
    {
        InitializeComponent();
        InitializeRuntime(existing);
    }

    /// <summary>
    /// 执行期初始化：依赖 existing 的 Text / Items / SelectedIndex / Value / ReadOnly、
    /// MesDatabase 取数与 foreach 建项，逐行保留原建构式语义。
    /// 控件建立 / 静态配置 / 加入容器均已搬至 PersonnelEditForm.Designer.cs。
    /// </summary>
    private void InitializeRuntime(UserProfile? existing)
    {
        _isEdit = existing is not null;

        Text = _isEdit ? $"编辑人员 - {existing!.Name}（{existing.EmpNo}）" : "登记新人员";
        lblEmpNo.Text = _isEdit ? "工号（不可改）：" : "工号 *：";
        _txtEmpNo.Text = existing?.EmpNo ?? "";
        _txtEmpNo.ReadOnly = _isEdit;
        _txtName.Text = existing?.Name ?? "";
        foreach (var d in MesDatabase.GetDepartments()) _cboDept.Items.Add(d);
        _cboDept.SelectedIndex = Math.Max(0, _cboDept.FindStringExact(existing?.Dept ?? ""));
        _txtPosition.Text = existing?.Position ?? "";
        foreach (var c in MesDatabase.GetCrews()) _cboCrew.Items.Add(c);
        _cboCrew.SelectedIndex = Math.Max(0, _cboCrew.FindStringExact(existing?.Crew ?? ""));
        _cboStatus.Items.AddRange(EmploymentStatus.All.Cast<object>().ToArray());
        _cboStatus.SelectedIndex = Math.Max(0, Array.IndexOf(EmploymentStatus.All, existing?.Status ?? EmploymentStatus.Active));
        _txtPhone.Text = existing?.Phone ?? "";
        if (existing is not null) _dtpHire.Value = existing.HireDate == default ? DateTime.Today : existing.HireDate;
        else _dtpHire.Value = DateTime.Today;
        tip.Text = _isEdit
            ? "提示：工号作为业务主键不可修改；姓名变更会同步更新其名下资质证书的姓名快照。"
            : "提示：登记成功后可在「资质明细」处为该人员登记上岗资质，未持证者不可上岗。";
        btnOk.Text = _isEdit ? "保存修改" : "登记人员";
    }

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
        var empNo = _txtEmpNo.Text.Trim();
        var name = _txtName.Text.Trim();
        if (empNo.Length == 0 || name.Length == 0)
        {
            MessageBox.Show("工号与姓名为必填项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var dept = (_cboDept.SelectedItem?.ToString() ?? _cboDept.Text).Trim();
        if (dept.Length == 0)
        {
            MessageBox.Show("部门为必填项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Profile.EmpNo = empNo;
        Profile.Name = name;
        Profile.Dept = dept;
        Profile.Position = _txtPosition.Text.Trim();
        Profile.Crew = (_cboCrew.SelectedItem?.ToString() ?? _cboCrew.Text).Trim();
        Profile.Status = _cboStatus.SelectedItem?.ToString() ?? EmploymentStatus.Active;
        Profile.Phone = _txtPhone.Text.Trim();
        Profile.HireDate = _dtpHire.Value.Date;

        DialogResult = DialogResult.OK;
        Close();
    }
}
