using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 工单指派/编辑窗体：
/// 根据 ERP 订单号生成工单号，填写计划生产数据、生产型号、产线、日期等。
/// </summary>
public class WorkOrderEditForm : Form
{
    private readonly WorkOrder? _existing;

    private TextBox _txtErp = null!;
    private TextBox _txtWoNo = null!;
    private CheckBox _chkAuto = null!;
    private ToolTip _tip = null!;
    private TextBox _txtModel = null!;
    private ComboBox _cboLine = null!;
    private NumericUpDown _numPlan = null!;
    private NumericUpDown _numFinished = null!;
    private NumericUpDown _numNg = null!;
    private ComboBox _cboStatus = null!;
    private DateTimePicker _dtStart = null!;
    private DateTimePicker _dtDue = null!;
    private Label _lblStock = null!;
    private DataGridView _gridStock = null!;
    /// <summary>最近一次齐套校验结果（保存时据此做缺料预警）。</summary>
    private MaterialCheckResult? _lastStockCheck;

    /// <summary>新建时返回新工单；编辑时返回 null（直接修改传入对象）</summary>
    public WorkOrder? WorkOrder { get; private set; }

    public WorkOrderEditForm(WorkOrder? existing)
    {
        _existing = existing;
        Text = existing is null ? "新建工单 - 工单指派" : "修改工单";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 700);
        Font = new Font("微软雅黑", 9F);
        BuildUi();
    }

    private void BuildUi()
    {
        _tip = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 6000 };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16, 14, 16, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 10; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        // 0 ERP 订单号（仅新建工单可修改；新建时支持双击从 ERP 已接收 PO 列表选择）
        _txtErp = new TextBox { Dock = DockStyle.Fill };
        _txtErp.TextChanged += (_, _) => SyncWoNo();
        _txtErp.DoubleClick -= TxtErp_DoubleClick;
        _txtErp.DoubleClick += TxtErp_DoubleClick;
        grid.Controls.Add(MakeLabel(_existing is null ? "ERP 订单号（可留空）" : "ERP 订单号（不可修改）"), 0, 0);
        grid.Controls.Add(_txtErp, 1, 0);

        // 1 工单号 + 自动生成
        _txtWoNo = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F) };
        _chkAuto = new CheckBox { Text = "由订单号自动生成", AutoSize = true, Checked = true };
        _chkAuto.CheckedChanged += (_, _) => { _txtWoNo.ReadOnly = _chkAuto.Checked; SyncWoNo(); };
        var woRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        woRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        woRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        woRow.Controls.Add(_txtWoNo, 0, 0);
        woRow.Controls.Add(_chkAuto, 1, 0);
        grid.Controls.Add(MakeLabel("工单号 *"), 0, 1);
        grid.Controls.Add(woRow, 1, 1);

        // 2 产品型号
        _txtModel = new TextBox { Dock = DockStyle.Fill };
        grid.Controls.Add(MakeLabel("生产型号 *"), 0, 2);
        grid.Controls.Add(_txtModel, 1, 2);

        // 3 产线
        _cboLine = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboLine.Items.AddRange(new object[] { "L1", "L2", "L3" });
        grid.Controls.Add(MakeLabel("生产线体 *"), 0, 3);
        grid.Controls.Add(_cboLine, 1, 3);

        // 4 计划生产数
        _numPlan = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 999999, Minimum = 1, Value = 1000 };
        grid.Controls.Add(MakeLabel("计划生产数 *"), 0, 4);
        grid.Controls.Add(_numPlan, 1, 4);

        // 5 完成数
        _numFinished = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 999999, Minimum = 0, Value = 0 };
        grid.Controls.Add(MakeLabel("已完成数"), 0, 5);
        grid.Controls.Add(_numFinished, 1, 5);

        // 6 不良数
        _numNg = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 999999, Minimum = 0, Value = 0 };
        grid.Controls.Add(MakeLabel("不良数"), 0, 6);
        grid.Controls.Add(_numNg, 1, 6);

        // 7 状态
        _cboStatus = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboStatus.Items.AddRange(new object[] { "未开始", "生产中", "已完成", "已关闭" });
        grid.Controls.Add(MakeLabel("工单状态"), 0, 7);
        grid.Controls.Add(_cboStatus, 1, 7);

        // 8 开始日期
        _dtStart = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
        grid.Controls.Add(MakeLabel("开始日期"), 0, 8);
        grid.Controls.Add(_dtStart, 1, 8);

        // 9 交期
        _dtDue = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
        grid.Controls.Add(MakeLabel("计划交期"), 0, 9);
        grid.Controls.Add(_dtDue, 1, 9);

        // 底部按钮
        var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 52 };
        var btnOk = new Button { Text = "保存", Width = 100, Height = 32, DialogResult = DialogResult.None };
        var btnCancel = new Button { Text = "取消", Width = 100, Height = 32 };
        btnOk.Location = new Point(btnRow.Width - 224, 10);
        btnCancel.Location = new Point(btnRow.Width - 112, 10);
        btnRow.Resize += (_, _) =>
        {
            btnOk.Location = new Point(btnRow.Width - 224, 10);
            btnCancel.Location = new Point(btnRow.Width - 112, 10);
        };
        btnOk.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);
        btnRow.BackColor = Color.WhiteSmoke;

        // ---- 齐套检查区：填完型号与计划数即自动算，缺料当场可见 ----
        var stockBox = new GroupBox
        {
            Text = "齐套检查（按上方「生产型号 + 计划数」展开 BOM，与现有库存逐项比对）",
            Dock = DockStyle.Top,
            Height = 236,
            Padding = new Padding(8, 10, 8, 8)
        };
        _lblStock = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(100, 116, 139),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "填写产品型号后自动做齐套检查。"
        };
        _gridStock = MakeStockGrid();
        stockBox.Controls.Add(_gridStock);  // Fill
        stockBox.Controls.Add(_lblStock);   // Top

        // 型号或计划数一变就重算——不要让用户点按钮才知道缺料
        _txtModel.TextChanged += (_, _) => RefreshStockCheck();
        _numPlan.ValueChanged += (_, _) => RefreshStockCheck();

        Controls.Add(grid);        // Fill 必须最先加
        Controls.Add(stockBox);    // Top
        Controls.Add(btnRow);      // Bottom

        // 编辑模式预填
        if (_existing is not null)
        {
            _txtErp.Text = _existing.ErpOrderNo;
            _txtWoNo.Text = _existing.WoNo;
            _txtModel.Text = _existing.ProductModel;
            _cboLine.SelectedItem = _existing.Line;
            _numPlan.Value = Math.Max(1, _existing.PlanQty);
            _numFinished.Value = _existing.FinishedQty;
            _numNg.Value = _existing.NgQty;
            _cboStatus.SelectedItem = _existing.Status;
            _dtStart.Value = _existing.StartDate == default ? DateTime.Today : _existing.StartDate;
            _dtDue.Value = _existing.DueDate == default ? DateTime.Today.AddDays(7) : _existing.DueDate;
            _chkAuto.Checked = false; // 编辑时工单号手动可改

            // ERP 订单号仅在「新增工单」时可修改；修改工单时只读，并做视觉区分。
            _txtErp.ReadOnly = true;
            _txtErp.BackColor = SystemColors.Control;
            _tip.SetToolTip(_txtErp, "ERP 订单号仅在新增工单时可修改，此处不可修改。");
        }
        else
        {
            _cboLine.SelectedIndex = 0;
            _cboStatus.SelectedIndex = 0;
            _dtStart.Value = DateTime.Today;
            _dtDue.Value = DateTime.Today.AddDays(7);

            // 新建工单默认勾选「由订单号自动生成」；ERP 留空时同步生成唯一测试工单号，避免工单号为空。
            _txtWoNo.ReadOnly = _chkAuto.Checked;
            SyncWoNo();

            _tip.SetToolTip(_txtErp, "双击从「已下发」的 ERP PO 列表中选择；测试/手工工单可留空不填。");
        }
    }

    private static Label MakeLabel(string text) =>
        new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(40, 50, 60) };

    /// <summary>
    /// 勾选自动生成时：ERP 非空 → 工单号 = "WO-" + ERP 订单号；ERP 留空 → 生成唯一测试工单号（测试/手工工单场景）。
    /// 未勾选自动生成时不自动改写工单号（保留用户手输）。
    /// </summary>
    private void SyncWoNo()
    {
        if (!_chkAuto.Checked) return;
        var erp = _txtErp.Text.Trim();
        _txtWoNo.Text = erp.Length > 0 ? "WO-" + erp : GenerateTestWoNo();
    }

    /// <summary>
    /// 生成唯一测试工单号：格式 <c>WO-TEST-{yyyyMMdd}-{seq:000}</c>（seq 从 001 起）。
    /// 遍历当天已有工单号取下一个不冲突的序号；当 ERP 留空（测试/手工工单）时使用。
    /// 纯生成（不写入任何集合），冲突时向后顺延。
    /// </summary>
    private static string GenerateTestWoNo()
    {
        var prefix = $"WO-TEST-{DateTime.Today:yyyyMMdd}-";
        for (var seq = 1; seq <= 999; seq++)
        {
            var candidate = prefix + seq.ToString("000");
            if (!MesDatabase.WorkOrders.Any(w => string.Equals(w.WoNo, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        // 极端兜底（同一天 ≥ 999 条测试工单，正常不会发生）：追加时间戳确保唯一。
        return prefix + DateTime.Now.ToString("HHmmssfff");
    }

    /// <summary>
    /// 双击 ERP 订单号文本框：新建工单时弹出「选择 ERP PO（已接收订单）」窗体，
    /// 确认后把所选 PO 号带回文本框（<c>TextChanged</c> 会自动同步工单号）；
    /// 修改工单时该字段只读，双击仅给出提示。
    /// </summary>
    private void TxtErp_DoubleClick(object? sender, EventArgs e)
    {
        if (_existing is not null)
        {
            MessageBox.Show("ERP 订单号仅在新增工单时可修改。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new ErpPoPickerForm();
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(picker.SelectedPoNo)) return;

        _txtErp.Text = picker.SelectedPoNo;   // TextChanged → SyncWoNo()
        if (_chkAuto.Checked) SyncWoNo();     // 防御性：确保自动生成模式下工单号已跟随更新
    }

    private void Save()
    {
        var erp = _txtErp.Text.Trim();
        var woNo = _txtWoNo.Text.Trim();
        var model = _txtModel.Text.Trim();

        // ERP PO 号：新建时可留空（测试/手工工单）；一旦填写，必须存在且状态为「已下发」。
        if (_existing is null && erp.Length > 0)
        {
            var po = MesDatabase.FindErpPo(erp);
            if (po is null)
            {
                Warn($"ERP PO 号 {erp} 不存在，请从「已下发」PO 列表中选择（测试/手工工单可留空不填）。");
                return;
            }
            if (po.Status != ErpPoStatus.Pushed)
            {
                Warn($"ERP PO 号 {erp} 尚未下发，请先在「工单管理」页执行 ERP 下发后再使用（测试/手工工单可留空不填）。");
                return;
            }
        }
        if (string.IsNullOrEmpty(woNo)) { Warn("请填写工单号。"); return; }
        if (string.IsNullOrEmpty(model)) { Warn("请填写生产型号。"); return; }
        if (_cboLine.SelectedItem is null) { Warn("请选择生产线体。"); return; }

        var dup = MesDatabase.WorkOrders.Any(w => w.WoNo == woNo && !ReferenceEquals(w, _existing));
        if (dup) { Warn($"工单号 {woNo} 已存在，请更换。"); return; }

        // 缺料预警：不静默放行，也不硬拦死——给一次明确选择，紧急插单仍可建单（但要留下确认痕迹）。
        if (_lastStockCheck is { NoBom: false, AllSatisfied: false })
        {
            var ck = _lastStockCheck;
            var lines = new List<string>();
            if (ck.ShortCount > 0) lines.Add($"· 缺料 {ck.ShortCount} 项：{ck.ShortageBrief}");
            if (ck.TightCount > 0) lines.Add($"· 紧张 {ck.TightCount} 项：库存刚好够用、无缓冲");
            var warnMsg = $"物料不齐套：\n{string.Join("\n", lines)}"
                        + $"\n\n（按计划数 {(int)_numPlan.Value} 只核算）"
                        + "\n缺料开工会造成生产中断或半成品积压。仍要保存该工单吗？";
            if (MessageBox.Show(warnMsg, "缺料预警", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }

        var line = _cboLine.SelectedItem!.ToString()!;
        var status = _cboStatus.SelectedItem?.ToString() ?? "未开始";

        if (_existing is not null)
        {
            // 修改工单：ERP 订单号不可修改——保留原值，绝不覆盖（防御性：即使 UI 被绕过也不受影响）。
            _existing.WoNo = woNo;
            _existing.ProductModel = model;
            _existing.Line = line;
            _existing.PlanQty = (int)_numPlan.Value;
            _existing.FinishedQty = (int)_numFinished.Value;
            _existing.NgQty = (int)_numNg.Value;
            _existing.Status = status;
            _existing.StartDate = _dtStart.Value.Date;
            _existing.DueDate = _dtDue.Value.Date;
        }
        else
        {
            WorkOrder = new WorkOrder
            {
                ErpOrderNo = erp,
                WoNo = woNo,
                ProductModel = model,
                Line = line,
                PlanQty = (int)_numPlan.Value,
                FinishedQty = (int)_numFinished.Value,
                NgQty = (int)_numNg.Value,
                Status = status,
                StartDate = _dtStart.Value.Date,
                DueDate = _dtDue.Value.Date
            };
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // ================= 齐套检查 =================

    /// <summary>齐套明细网格（列比主站精简，避免把窗体撑太宽）。</summary>
    private static DataGridView MakeStockGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15, 76, 129),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 9F),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(4, 3, 4, 3)
            }
        };
        g.AutoGenerateColumns = false;
        g.Columns.Add(StockCol("MaterialName", "物料名称", 18));
        g.Columns.Add(StockCol("RequirementText", "需求量", 9));
        g.Columns.Add(StockCol("AvailableText", "可用量", 9));
        g.Columns.Add(StockCol("ShortageText", "缺口", 9));
        g.Columns.Add(StockCol("Status", "状态", 8));
        g.Columns.Add(StockCol("Advice", "处置建议", 26));

        g.CellFormatting += (_, e) =>
        {
            if (g.Rows.Count == 0 || e.RowIndex < 0) return;
            if (g.Rows[e.RowIndex].DataBoundItem is not MaterialShortageRow r) return;
            if (r.Status == ShortageStatus.Short)
            {
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(220, 38, 38);
            }
            else if (r.Status == ShortageStatus.Tight)
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 247, 237);
                e.CellStyle.ForeColor = Color.FromArgb(217, 119, 6);
            }
            else
            {
                e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
            }
        };
        return g;
    }

    private static DataGridViewTextBoxColumn StockCol(string prop, string header, int fillWeight) => new()
    {
        Name = prop,
        DataPropertyName = prop,
        HeaderText = header,
        ReadOnly = true,
        FillWeight = fillWeight
    };

    /// <summary>按当前型号与计划数重算齐套。纯查询，不改任何数据。</summary>
    private void RefreshStockCheck()
    {
        var model = _txtModel?.Text.Trim() ?? "";
        if (model.Length == 0 || _gridStock is null)
        {
            _lastStockCheck = null;
            if (_lblStock is not null)
            {
                _lblStock.Text = "填写产品型号后自动做齐套检查。";
                _lblStock.ForeColor = Color.FromArgb(100, 116, 139);
            }
            if (_gridStock is not null) _gridStock.DataSource = null;
            return;
        }

        var r = MesDatabase.CheckBomAvailability(model, (int)(_numPlan?.Value ?? 0));
        _lastStockCheck = r;
        _gridStock.DataSource = r.Rows;

        if (r.NoBom)
        {
            _lblStock.Text = $"型号 {model} 暂无 BOM 数据，无法校验物料。";
            _lblStock.ForeColor = Color.FromArgb(100, 116, 139);
        }
        else if (r.AllSatisfied)
        {
            _lblStock.Text = $"✓ 齐套通过：{r.Rows.Count} 项物料全部满足，可正常投产。";
            _lblStock.ForeColor = Color.FromArgb(21, 128, 61);
        }
        else
        {
            _lblStock.Text = $"⚠ 缺料 {r.ShortCount} 项 → {r.ShortageBrief}"
                           + (r.TightCount > 0 ? $"；另有 {r.TightCount} 项紧张（够用但无缓冲）。" : "");
            _lblStock.ForeColor = Color.FromArgb(220, 38, 38);
        }
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
