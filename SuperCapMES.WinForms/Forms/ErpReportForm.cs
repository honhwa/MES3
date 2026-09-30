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
public class ErpReportForm : Form
{
    private readonly WorkOrder _wo;

    private NumericUpDown _numOutput = null!;
    private NumericUpDown _numGood = null!;
    private NumericUpDown _numNg = null!;
    private NumericUpDown _numHours = null!;
    private TextBox _txtOperator = null!;
    private TextBox _txtRemark = null!;

    /// <summary>联动写入抑制标志：改产出/不良时重算良品期间置 true，避免触发递归。</summary>
    private bool _suppress;

    public ErpReportForm(WorkOrder wo)
    {
        _wo = wo;
        Text = $"ERP 报工 - {wo.WoNo}";
        Font = new Font("微软雅黑", 9F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 520);
        MinimumSize = new Size(500, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BuildUi();

        // 防御：仅终结工单（已完成 / 已关闭）可 ERP 报工。入口（MainForm）通常已拦截；
        // 若被绕过，窗体仍可正常构造（不抛异常），在 OnShown 中提示一次并自动取消，确保调用方拿不到 OK。
        if (!MesDatabase.IsWorkOrderTerminated(wo))
        {
            Shown += (_, _) =>
            {
                Warn($"工单 {wo.WoNo} 尚未终结（当前状态：{wo.Status}），不能报工。");
                DialogResult = DialogResult.Cancel;
                Close();
            };
        }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 12, 14, 8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));  // 只读信息
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 可编辑
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));   // 按钮

        root.Controls.Add(BuildInfoBox(), 0, 0);
        root.Controls.Add(BuildEditBox(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
    }

    // ---------------- 只读信息 ----------------

    private Control BuildInfoBox()
    {
        var box = new GroupBox { Text = "工单信息（只读）", Dock = DockStyle.Fill, Padding = new Padding(8, 12, 8, 6) };
        var lbl = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            Text =
                $"工单号：{_wo.WoNo}    ERP订单号：{(string.IsNullOrWhiteSpace(_wo.ErpOrderNo) ? "—" : _wo.ErpOrderNo)}\r\n" +
                $"产品型号：{_wo.ProductModel}    产线：{_wo.Line}\r\n" +
                $"计划数：{_wo.PlanQty:N0}    完成数：{_wo.FinishedQty:N0}    不良数：{_wo.NgQty:N0}\r\n" +
                $"当前报工状态：{_wo.ErpReportStatus}    累计已报工时：{_wo.WorkHours:0.##} h" +
                (_wo.ErpReportTime.HasValue ? $"（最近：{_wo.ErpReportTime.Value:yyyy-MM-dd HH:mm}）" : "")
        };
        box.Controls.Add(lbl);
        return box;
    }

    // ---------------- 可编辑 ----------------

    private Control BuildEditBox()
    {
        var box = new GroupBox { Text = "本次报工数据（* 为必填）", Dock = DockStyle.Fill, Padding = new Padding(8, 14, 8, 6) };
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        // 初始值：产出 = 完成数>0 ? 完成数 : 计划数；不良 = 工单不良数；良品 = 产出 − 不良
        var initOutput = _wo.FinishedQty > 0 ? _wo.FinishedQty : _wo.PlanQty;
        var initNg = _wo.NgQty;
        var initGood = Math.Max(0, initOutput - initNg);

        _numOutput = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 9999999, Value = Clamp(initOutput) };
        _numGood = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 9999999, Value = Clamp(initGood) };
        _numNg = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 9999999, Value = Clamp(initNg) };
        _numHours = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 9999, DecimalPlaces = 1, Increment = 0.5m, Value = 8.0m };

        tbl.Controls.Add(MakeLabel("产出数量 *"), 0, 0);
        tbl.Controls.Add(_numOutput, 1, 0);
        tbl.Controls.Add(MakeLabel("良品数 *"), 0, 1);
        tbl.Controls.Add(_numGood, 1, 1);
        tbl.Controls.Add(MakeLabel("不良数 *"), 0, 2);
        tbl.Controls.Add(_numNg, 1, 2);
        tbl.Controls.Add(MakeLabel("投入工时 (h) *"), 0, 3);
        tbl.Controls.Add(_numHours, 1, 3);

        _txtOperator = new TextBox { Dock = DockStyle.Fill, Text = "admin" };
        tbl.Controls.Add(MakeLabel("操作人 *"), 0, 4);
        tbl.Controls.Add(_txtOperator, 1, 4);

        _txtRemark = new TextBox { Dock = DockStyle.Fill };
        tbl.Controls.Add(MakeLabel("备注"), 0, 5);
        tbl.Controls.Add(_txtRemark, 1, 5);

        // 联动：改产出或不良 → 自动重算良品（良品 = 产出 − 不良，下限 0）
        _numOutput.ValueChanged += (_, _) => RecalcGood();
        _numNg.ValueChanged += (_, _) => RecalcGood();

        box.Controls.Add(tbl);
        return box;
    }

    private Control BuildFooter()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke };
        var btnUpload = new Button
        {
            Text = "上传至 ERP 报工",
            Width = 150,
            Height = 34,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        var btnCancel = new Button { Text = "取消", Width = 100, Height = 34 };
        btnUpload.Location = new Point(bar.Width - 276, 10);
        btnCancel.Location = new Point(bar.Width - 122, 10);
        bar.Resize += (_, _) =>
        {
            btnUpload.Location = new Point(bar.Width - 276, 10);
            btnCancel.Location = new Point(bar.Width - 122, 10);
        };
        btnUpload.Click += (_, _) => Upload();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bar.Controls.Add(btnUpload);
        bar.Controls.Add(btnCancel);
        return bar;
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

    private static Label MakeLabel(string text) =>
        new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(40, 50, 60) };

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
