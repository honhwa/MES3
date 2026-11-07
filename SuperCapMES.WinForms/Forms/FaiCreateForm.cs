using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 新建首件检验（FAI）窗体：选择工单 + 首检工序（自动带出该工序的检验项目），可选填首件电芯 SN。
/// 点「创建首检单」后把选择结果回传调用方（由调用方执行 <see cref="MesDatabase.CreateFai"/>）。
/// </summary>
public partial class FaiCreateForm : Form
{
    private readonly List<WorkOrder> _workOrders;
    private readonly List<string> _processes;

    public string WoNo { get; private set; } = "";
    public string FirstCellSn { get; private set; } = "";
    public string ProcessName { get; private set; } = "";
    public string Reason { get; private set; } = FaiReason.Changeover;
    public string Operator { get; private set; } = "admin";

    public FaiCreateForm(List<WorkOrder> workOrders, List<string> processes)
    {
        _workOrders = workOrders ?? new List<WorkOrder>();
        _processes = processes ?? new List<string>();

        InitializeComponent();
        InitializeRuntime(_workOrders, _processes);
    }

    /// <summary>
    /// 执行期初始化：依赖建构式参数的下拉项目 / 选中项，以及原本 BuildUi() 末尾的预览更新。
    /// 事件本体已具名化并挂在 FaiCreateForm.Designer.cs。
    /// </summary>
    private void InitializeRuntime(List<WorkOrder> workOrders, List<string> processes)
    {
        _cboWo.Items.AddRange(_workOrders
            .Select(w => $"{w.WoNo} ｜ {w.ProductModel} ｜ {w.Status}")
            .Cast<object>().ToArray());
        if (_cboWo.Items.Count > 0) _cboWo.SelectedIndex = 0;
        _cboProcess.Items.AddRange(_processes.Cast<object>().ToArray());
        if (_cboProcess.Items.Count > 0) _cboProcess.SelectedIndex = 0;
        _cboReason.Items.AddRange(FaiReason.All.Cast<object>().ToArray());
        _cboReason.SelectedIndex = 0;
        UpdateFaiState();
        UpdateItemsPreview();
    }

    private void CboWo_SelectedIndexChanged(object? sender, EventArgs e) => UpdateFaiState();

    private void CboProcess_SelectedIndexChanged(object? sender, EventArgs e) => UpdateItemsPreview();

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private WorkOrder? CurrentWorkOrder() =>
        _cboWo.SelectedIndex >= 0 && _cboWo.SelectedIndex < _workOrders.Count
            ? _workOrders[_cboWo.SelectedIndex]
            : null;

    /// <summary>显示所选工单的首件检验放行状态（便于用户判断是否需要新建）。</summary>
    private void UpdateFaiState()
    {
        var wo = CurrentWorkOrder();
        if (wo is null || _lblFaiState is null) return;

        var passed = MesDatabase.IsFaiPassed(wo.WoNo);
        var latest = MesDatabase.GetLatestFai(wo.WoNo);
        _lblFaiState.Text = passed
            ? $"✔ 已放行（{latest?.FaiNo}，{latest?.ProcessName}）"
            : (latest is null ? "✖ 尚未首件检验（需新建）" : $"✖ 未放行（最近 {latest.FaiNo} 为「{latest.Status}」）");
        _lblFaiState.ForeColor = passed ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 0, 0);

        // 预填该工单下未在保留中的电芯作为首件候选
        if (string.IsNullOrWhiteSpace(_txtCellSn.Text))
        {
            var candidate = MesDatabase.Cells.FirstOrDefault(c =>
                string.Equals(c.WoNo, wo.WoNo, StringComparison.OrdinalIgnoreCase)
                && CellSnStatus.IsShippable(c.Status));
            if (candidate is not null) _txtCellSn.Text = candidate.Sn;
        }
    }

    private void UpdateItemsPreview()
    {
        var process = _cboProcess.SelectedItem?.ToString() ?? "";
        var items = MesDatabase.GetFaiTemplate(process);
        if (_lblItems is not null)
            _lblItems.Text = items.Count == 0
                ? "（该工序无检验模板）"
                : $"{items.Count} 项：{string.Join("、", items.Select(i => i.Item))}";
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var wo = CurrentWorkOrder();
        if (wo is null) { Warn("请选择生产工单。"); return; }

        var process = _cboProcess.SelectedItem?.ToString() ?? "";
        if (process.Length == 0) { Warn("请选择首检工序。"); return; }
        if (MesDatabase.GetFaiTemplate(process).Count == 0) { Warn($"工序「{process}」没有检验项目模板。"); return; }

        WoNo = wo.WoNo;
        FirstCellSn = _txtCellSn.Text.Trim();
        ProcessName = process;
        Reason = _cboReason.SelectedItem?.ToString() ?? FaiReason.Changeover;
        Operator = string.IsNullOrWhiteSpace(_txtOperator.Text) ? "admin" : _txtOperator.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
