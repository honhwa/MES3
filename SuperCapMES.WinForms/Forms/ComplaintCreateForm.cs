using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 客诉登记窗体：客户信息 + 客诉描述 + 关联范围（批次 / 工单 / SN / 出货单）。
/// 业务校验（必填项、数量、严重度/分类/来源合法性、接收时间）统一在
/// <see cref="Data.MesDatabase.CreateComplaint"/> 中执行，失败时由调用方弹出原因且不落库。
/// </summary>
public partial class ComplaintCreateForm : Form
{
    /// <summary>登记结果：调用方据此调用仓储层落库</summary>
    public CustomerComplaint Complaint { get; private set; } = new();

    public ComplaintCreateForm()
    {
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：依赖 MesDatabase 的下拉项目（既有客户 / 责任工程师）、
    /// 各静态来源集合的项目与选中项，以及原本建构式末尾的时限提示更新。
    /// 事件本体已具名化并挂在 ComplaintCreateForm.Designer.cs。
    /// </summary>
    private void InitializeRuntime()
    {
        cboKnown.Items.Add("（从既有客户选择）");
        foreach (var p in MesDatabase.WmsOrders
                     .Where(o => o.Type == WmsOrderTypes.LogisticsOutbound)
                     .Select(o => o.Partner).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            cboKnown.Items.Add(p);
        cboKnown.SelectedIndex = 0;

        _cboSource.Items.AddRange(ComplaintSource.All.Cast<object>().ToArray());
        _cboSource.SelectedIndex = 0;

        _cboSeverity.Items.AddRange(ComplaintSeverity.All.Cast<object>().ToArray());
        _cboSeverity.SelectedIndex = 1;   // 默认「一般」

        _cboCategory.Items.AddRange(ComplaintCategories.All.Cast<object>().ToArray());
        _cboCategory.SelectedIndex = 0;

        foreach (var u in MesDatabase.Users.Where(u => u.IsActive).OrderBy(u => u.EmpNo, StringComparer.Ordinal))
            _cboOwner.Items.Add($"{u.Name}（{u.Dept}）");
        if (_cboOwner.Items.Count > 0) _cboOwner.SelectedIndex = 0;

        UpdateDueHint();
    }

    private void CboKnown_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cboKnown.SelectedIndex > 0) _txtCustomer.Text = cboKnown.SelectedItem?.ToString() ?? "";
    }

    private void CboSeverity_SelectedIndexChanged(object? sender, EventArgs e) => UpdateDueHint();

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    /// <summary>严重度变化时同步展示自动推算的回复时限（与仓储层同一口径）。</summary>
    private void UpdateDueHint()
    {
        var severity = _cboSeverity.SelectedItem?.ToString() ?? ComplaintSeverity.Major;
        var days = ComplaintSeverity.ResponseDays(severity);
        _lblDue.Text = $"接收日 + {days} 天（{severity}），逾期会在列表标红并计入超期统计";
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (_txtCustomer.Text.Trim().Length == 0)
        {
            MessageBox.Show("客户名称为必填项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_txtSymptom.Text.Trim().Length == 0)
        {
            MessageBox.Show("请填写客户描述的现象。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Complaint = new CustomerComplaint
        {
            CustomerName = _txtCustomer.Text.Trim(),
            ContactInfo = _txtContact.Text.Trim(),
            Source = _cboSource.SelectedItem?.ToString() ?? ComplaintSource.Customer,
            Severity = _cboSeverity.SelectedItem?.ToString() ?? ComplaintSeverity.Major,
            Category = _cboCategory.SelectedItem?.ToString() ?? ComplaintCategories.Other,
            ProductModel = _txtModel.Text.Trim(),
            LotKeyword = _txtLot.Text.Trim(),
            Quantity = (double)_numQty.Value,
            SymptomDesc = _txtSymptom.Text.Trim(),
            IsSafetyInvolved = _chkSafety.Checked,
            NeedRecall = _chkRecall.Checked,
            Owner = _cboOwner.Text.Trim(),
            ReceiveTime = DateTime.Now
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
