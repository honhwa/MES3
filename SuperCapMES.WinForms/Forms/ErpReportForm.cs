using System;
using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 「ERP 报工」窗体：对选中工单上报产出 / 良品 / 不良 / 投入工时。
/// 顶部只读展示工单信息；中部可编辑生产数据（良品随产出/不良自动联动）；
/// 提交时本地校验（产出 &gt; 0、良品 + 不良 == 产出、工时 ≥ 0）→ MesDatabase.PushErpWorkReport。
/// </summary>
public partial class ErpReportForm : Form
{
    private readonly WorkOrder _wo;

    /// <summary>联动写入抑制标志：改产出/不良时重算良品期间置 true，避免触发递归。</summary>
    private bool _suppress;

    public ErpReportForm(WorkOrder wo)
    {
        _wo = wo;

        InitializeComponent();
        InitializeRuntime(wo);
    }

    /// <summary>
    /// 执行期初始化：依赖工单参数的标题 / 只读信息 / 各 NumericUpDown 初始值，
    /// 以及条件性的 Shown 提示（原建构式内逻辑搬移至此）。
    /// 事件本体已具名化挂在 ErpReportForm.Designer.cs；条件性 Shown 仍在此以方法群挂接。
    /// </summary>
    private void InitializeRuntime(WorkOrder wo)
    {
        Text = $"ERP 报工 - {_wo.WoNo}";
        infoLabel.Text =
            $"工单号：{_wo.WoNo}    ERP订单号：{(string.IsNullOrWhiteSpace(_wo.ErpOrderNo) ? "—" : _wo.ErpOrderNo)}\r\n" +
            $"产品型号：{_wo.ProductModel}    产线：{_wo.Line}\r\n" +
            $"计划数：{_wo.PlanQty:N0}    完成数：{_wo.FinishedQty:N0}    不良数：{_wo.NgQty:N0}\r\n" +
            $"当前报工状态：{_wo.ErpReportStatus}    累计已报工时：{_wo.WorkHours:0.##} h" +
            (_wo.ErpReportTime.HasValue ? $"（最近：{_wo.ErpReportTime.Value:yyyy-MM-dd HH:mm}）" : "");

        // 初始值：产出 = 完成数>0 ? 完成数 : 计划数；不良 = 工单不良数；良品 = 产出 − 不良
        var initOutput = _wo.FinishedQty > 0 ? _wo.FinishedQty : _wo.PlanQty;
        var initNg = _wo.NgQty;
        var initGood = Math.Max(0, initOutput - initNg);
        _numOutput.Value = Clamp(initOutput);
        _numGood.Value = Clamp(initGood);
        _numNg.Value = Clamp(initNg);

        // 防御：仅终结工单（已完成 / 已关闭）可 ERP 报工。入口（MainForm）通常已拦截；
        // 若被绕过，窗体仍可正常构造（不抛异常），在 OnShown 中提示一次并自动取消，确保调用方拿不到 OK。
        if (!MesDatabase.IsWorkOrderTerminated(_wo))
        {
            Shown += ErpReportForm_Shown;
        }
    }

    private void NumOutput_ValueChanged(object? sender, EventArgs e) => RecalcGood();

    private void NumNg_ValueChanged(object? sender, EventArgs e) => RecalcGood();

    private void FooterBar_Resize(object? sender, EventArgs e)
    {
        btnUpload.Location = new Point(footerBar.Width - 276, 10);
        btnCancel.Location = new Point(footerBar.Width - 122, 10);
    }

    private void BtnUpload_Click(object? sender, EventArgs e) => Upload();

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void ErpReportForm_Shown(object? sender, EventArgs e)
    {
        Warn($"工单 {_wo.WoNo} 尚未终结（当前状态：{_wo.Status}），不能报工。");
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ---------------- 行为 ----------------

    /// <summary>良品 = 产出 − 不良（下限 0）；抑制递归。</summary>
    private void RecalcGood()
    {
        if (_suppress) return;
        _suppress = true;
        try
        {
            var good = (int)_numOutput.Value - (int)_numNg.Value;
            if (good < 0) good = 0;
            _numGood.Value = Clamp(good);
        }
        finally { _suppress = false; }
    }

    private void Upload()
    {
        var output = (int)_numOutput.Value;
        var good = (int)_numGood.Value;
        var ng = (int)_numNg.Value;
        var hours = (double)_numHours.Value;
        var op = _txtOperator.Text.Trim();
        if (op.Length == 0) op = "admin";

        // 本地校验（与仓储层口径一致）
        if (output <= 0) { Warn("产出数量必须大于 0。"); return; }
        if (good + ng != output) { Warn($"良品数 ({good}) + 不良数 ({ng}) 必须等于产出数量 ({output})。"); return; }
        if (hours < 0) { Warn("投入工时不能为负数。"); return; }

        var (ok, message, reportNo, receipt) = MesDatabase.PushErpWorkReport(
            _wo.WoNo, output, good, ng, hours, op, _txtRemark.Text.Trim());
        if (!ok) { Warn(message); return; }

        MessageBox.Show($"报工成功：报工单号 {reportNo}，ERP回执 {receipt}。",
            "ERP 报工", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------------- 控件工厂 ----------------

    private static decimal Clamp(int v)
    {
        if (v < 0) return 0;
        return v > 9999999 ? 9999999 : v;
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
