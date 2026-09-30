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
public class PersonnelEditForm : Form
{
    private readonly bool _isEdit;
    private TextBox _txtEmpNo = null!;
    private TextBox _txtName = null!;
    private ComboBox _cboDept = null!;
    private TextBox _txtPosition = null!;
    private ComboBox _cboCrew = null!;
    private ComboBox _cboStatus = null!;
    private TextBox _txtPhone = null!;
    private DateTimePicker _dtpHire = null!;

    /// <summary>编辑结果：调用方据此写库</summary>
    public UserProfile Profile { get; } = new();

    public PersonnelEditForm(UserProfile? existing = null)
    {
        _isEdit = existing is not null;

        Text = _isEdit ? $"编辑人员 - {existing!.Name}（{existing.EmpNo}）" : "登记新人员";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 300,
            ColumnCount = 2,
            Padding = new Padding(14, 16, 14, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 工号：新增可录；编辑只读（工号是业务主键，不允许改）
        _txtEmpNo = new TextBox { Text = existing?.EmpNo ?? "", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4), ReadOnly = _isEdit };
        AddRow(table, _isEdit ? "工号（不可改）：" : "工号 *：", _txtEmpNo, 0);

        _txtName = new TextBox { Text = existing?.Name ?? "", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "姓名 *：", _txtName, 1);

        _cboDept = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        foreach (var d in MesDatabase.GetDepartments()) _cboDept.Items.Add(d);
        _cboDept.SelectedIndex = Math.Max(0, _cboDept.FindStringExact(existing?.Dept ?? ""));
        AddRow(table, "部门 *：", _cboDept, 2);

        _txtPosition = new TextBox { Text = existing?.Position ?? "", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "岗位：", _txtPosition, 3);

        _cboCrew = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        foreach (var c in MesDatabase.GetCrews()) _cboCrew.Items.Add(c);
        _cboCrew.SelectedIndex = Math.Max(0, _cboCrew.FindStringExact(existing?.Crew ?? ""));
        AddRow(table, "班组：", _cboCrew, 4);

        _cboStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        _cboStatus.Items.AddRange(EmploymentStatus.All.Cast<object>().ToArray());
        _cboStatus.SelectedIndex = Math.Max(0, Array.IndexOf(EmploymentStatus.All, existing?.Status ?? EmploymentStatus.Active));
        AddRow(table, "在职状态：", _cboStatus, 5);

        _txtPhone = new TextBox { Text = existing?.Phone ?? "", Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        AddRow(table, "联系电话：", _txtPhone, 6);

        _dtpHire = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 4) };
        if (existing is not null) _dtpHire.Value = existing.HireDate == default ? DateTime.Today : existing.HireDate;
        else _dtpHire.Value = DateTime.Today;
        AddRow(table, "入职日期：", _dtpHire, 7);

        var tip = new Label
        {
            Text = _isEdit
                ? "提示：工号作为业务主键不可修改；姓名变更会同步更新其名下资质证书的姓名快照。"
                : "提示：登记成功后可在「资质明细」处为该人员登记上岗资质，未持证者不可上岗。",
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(14, 8, 14, 0),
            ForeColor = Color.Gray
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.WhiteSmoke };
        var btnOk = new Button
        {
            Text = _isEdit ? "保存修改" : "登记人员",
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
