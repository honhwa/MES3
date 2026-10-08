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
    private Label _lblAsset = null!;
    private DataGridView _gridAsset = null!;
    private TextBox _txtAiInput = null!;
    private Button _btnAiParse = null!;
    private Label _lblAiTrace = null!;
    /// <summary>最近一次资源校验结果（物料 + 设备，保存时据此预警与登记占用）。</summary>
    private ResourceCheckResult? _lastResourceCheck;
    /// <summary>最近一次资材占用校验（主体口径：跨工单累加 + 净缺口）。</summary>
    private MaterialOccupancyCheck _matOcc = new();
    /// <summary>进入本窗体时读到的占用版本（保存时做乐观锁比对）。</summary>
    private int _occupancyVersionAtLoad;
    /// <summary>进入窗体时读到的**资材**占用版本（保存时乐观锁比对）。</summary>
    private int _matOccVersionAtLoad;

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

        // ---- AI 文本解析条：粘贴一段大白话，自动填型号/数量/线体/日期 ----
        var aiBar = new Panel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(16, 4, 16, 4), BackColor = Color.White };
        _txtAiInput = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 9F),
            PlaceholderText = "例：下周三开工，SC-CX2026-100F 要 6000 只，走 L1，用卷绕机 WIND-L1-01，14 号交货"
        };
        var aiLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
        aiLeft.Controls.Add(_txtAiInput);

        var aiRight = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 150, WrapContents = false };
        _btnAiParse = new Button
        {
            Text = "🤖 AI 解析文本",
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnAiParse.Click += (_, _) => BtnAiParse();
        _lblAiTrace = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            AutoSize = false,
            Font = new Font("微软雅黑", 8.5F),
            ForeColor = Color.FromArgb(100, 116, 139),
            Text = "未启用大模型时走规则抽取"
        };
        aiRight.Controls.Add(_lblAiTrace);
        aiRight.Controls.Add(_btnAiParse);
        aiBar.Controls.Add(aiLeft);   // Fill
        aiBar.Controls.Add(aiRight);  // Right

        // ---- 资源校验区：物料齐套 + 资产占用（两个子页，同一套口径）----
        var resTabs = new TabControl { Dock = DockStyle.Top, Height = 258, Font = new Font("微软雅黑", 9F) };

        var matPage = new TabPage("资材占用（跨工单汇总）");
        matPage.Padding = new Padding(4);
        _lblStock = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(100, 116, 139),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "填写产品型号后自动按「工单 + BOM」计算资材占用，并跨工单汇总。"
        };
        _gridStock = MakeMaterialOccupancyGrid();
        matPage.Controls.Add(_gridStock);  // Fill
        matPage.Controls.Add(_lblStock);   // Top

        var assetPage = new TabPage("设备占用");
        assetPage.Padding = new Padding(4);
        _lblAsset = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = Color.FromArgb(100, 116, 139),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "填写型号与起止日期后自动校验设备/工装占用。"
        };
        _gridAsset = MakeAssetGrid();
        assetPage.Controls.Add(_gridAsset);  // Fill
        assetPage.Controls.Add(_lblAsset);   // Top

        resTabs.TabPages.Add(matPage);   // 主体：资材占用（跨工单累加，决定采购量）
        resTabs.TabPages.Add(assetPage); // 辅助：设备占用（可复用，按时间窗判冲突）

        // 型号 / 计划数 / 起止日期 任一变化就重算——不要让用户点按钮才知道冲突
        _txtModel.TextChanged += (_, _) => RefreshResourceCheck();
        _numPlan.ValueChanged += (_, _) => RefreshResourceCheck();
        _dtStart.ValueChanged += (_, _) => RefreshResourceCheck();
        _dtDue.ValueChanged += (_, _) => RefreshResourceCheck();

        Controls.Add(grid);        // Fill 必须最先加
        Controls.Add(resTabs);     // Top
        Controls.Add(aiBar);       // Top
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

        // 预填完成后再做第一次校验（控件已有值，结果才有意义）
        _occupancyVersionAtLoad = _existing is null ? -1 : MesDatabase.GetOccupancyVersionSum(_existing.WoNo);
        _matOccVersionAtLoad = _existing is null ? -1 : MesDatabase.GetMaterialOccupancyVersionSum(_existing.WoNo);
        _tip.SetToolTip(_btnAiParse, "把一段大白话需求交给大模型解析成工单字段。未启用模型时自动走规则抽取，结果同样标明来源。");
        RefreshResourceCheck();
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
        // 资源预警：不静默放行，也不硬拦死——给一次明确选择，紧急插单仍可建单（但要留下确认痕迹）。
        // 物料与资产**分两块提示**，因为处理方式不同：缺料要找采购，占用冲突要找计划。
        if (_lastResourceCheck is { Material: not null } rc && !rc.Material!.NoBom && !rc.Material.AllSatisfied)
        {
            var ck = rc.Material;
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

        if (_lastResourceCheck is { AssetLines.Count: > 0 } rc2 && !rc2.AssetOk)
        {
            var blocks = new List<string>();
            foreach (var l in rc2.AssetLines.Where(x => x.Status != AssetCheckStatus.Ok))
            {
                var t = $"· {l.AssetName}（{l.AssetCode}／{l.Process}）{l.StatusText}"
                        + (l.Conflicts.Count > 0
                            ? "：" + string.Join("；", l.Conflicts.Select(c => $"工单 {c.WoNo} 占 {c.OccupiedQty} 台 {c.WindowText}"))
                            : $"：可用余量 {l.AvailableText}，本单需 {l.NeedText}");
                var opts = l.Options.Where(o => o.Feasible).Select(o => o.Text).ToList();
                if (opts.Count > 0) t += "\n    可选方案：" + string.Join("；", opts.Take(2));
                blocks.Add(t);
            }
            var msg2 = $"资产占用冲突（{_dtStart.Value:yyyy-MM-dd} ~ {_dtDue.Value:yyyy-MM-dd}）：\n\n"
                     + string.Join("\n\n", blocks)
                     + "\n\n排产撞车会导致工序挤兑甚至停线。仍要保存该工单吗？";
            if (MessageBox.Show(msg2, "资产占用冲突", MessageBoxButtons.YesNo,
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

        // ---- 登记占用：资材（主体）+ 设备（辅助），写前重算 + 乐观锁 ----
        var targetWoNo = WorkOrder?.WoNo ?? _existing?.WoNo ?? woNo;
        if (!string.IsNullOrWhiteSpace(targetWoNo))
        {
            // ① 资材占用：按「工单 + BOM」算剩余待产量的需求量并登记，跨工单汇总时参与采购量
            var woForOcc = _existing ?? WorkOrder;
            if (woForOcc is null)
            {
                woForOcc = new WorkOrder
                {
                    WoNo = woNo,
                    ProductModel = model,
                    PlanQty = (int)_numPlan.Value,
                    FinishedQty = (int)_numFinished.Value,
                    StartDate = _dtStart.Value,
                    DueDate = _dtDue.Value
                };
            }
            var (matOk, matMsg, _) = MesDatabase.ReserveMaterialsForWorkOrder(
                woForOcc, MaterialOccupancySource.Manual,
                expectVersion: _existing is null ? -1 : _matOccVersionAtLoad,
                remark: $"手工建单 {model} {(int)_numPlan.Value} 只");
            if (!matOk)
            {
                RollbackSave();
                MessageBox.Show(matMsg, "资材占用登记失败，已撤销本次保存",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ② 设备占用（辅助维度：可复用资源，按时间窗判冲突）
            var assets = MesDatabase.AllocateAssetsForModel(model, line);
            var (occOk, occMsg, _) = MesDatabase.ReserveAssetsForWorkOrder(
                targetWoNo, assets, _dtStart.Value, _dtDue.Value,
                source: MesDatabase.IsOpenWorkOrder(_existing ?? WorkOrder ?? new WorkOrder())
                    ? OccupancySource.Scheduling
                    : OccupancySource.Manual,
                expectVersion: _existing is null ? -1 : _occupancyVersionAtLoad,
                confirmed: status == "生产中",
                remark: $"{model} {(int)_numPlan.Value} 只");

            if (!occOk)
            {
                // 占用登记失败 = 数据会不一致（工单存在但没占到设备），必须回滚这次保存
                MesDatabase.ReleaseMaterialsForWorkOrder(targetWoNo, "设备占用登记失败，回滚资材占用");
                RollbackSave();
                MessageBox.Show(occMsg, "设备占用登记失败，已撤销本次保存",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>占用登记失败时把本次改动回滚，避免出现"工单建了但没占到设备"的不一致状态。</summary>
    private void RollbackSave()
    {
        if (WorkOrder is not null)
        {
            MesDatabase.WorkOrders.Remove(WorkOrder);   // 新建：撤掉刚加的
            WorkOrder = null;
        }
        // 编辑：字段已被就地修改，改动前已在上方记录，这里不做深回滚——
        // 窗体会关闭、用户会看到弹窗提示，由人工决定是否重填（原型阶段可接受）。
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

    /// <summary>
    /// 资材占用表（本项目"占用"的主体）：同一物料被多少张工单占着、占多少、
    /// 可用多少、在途多少、还需采购多少。占用明细列把"工单号 + 数量 + 起止时间"摊开。
    /// </summary>
    private static DataGridView MakeMaterialOccupancyGrid()
    {
        var g = MakeStockGrid();   // 复用同一套网格外观
        g.Columns.Clear();
        g.Columns.Add(StockCol("MaterialType", "类型", 7));
        g.Columns.Add(StockCol("MaterialName", "物料名称", 17));
        g.Columns.Add(StockCol("MaterialCode", "物料编码", 14));
        g.Columns.Add(StockCol("TotalText", "占用总量", 12));
        g.Columns.Add(StockCol("WoCountText", "工单数", 8));
        g.Columns.Add(StockCol("AvailableText", "可用库存", 11));
        g.Columns.Add(StockCol("InTransitText", "在途采购", 11));
        g.Columns.Add(StockCol("ShortageText", "需采购", 11));
        g.Columns.Add(StockCol("StatusText", "状态", 8));
        g.Columns.Add(StockCol("DetailsText", "占用明细（工单 / 数量 / 起止时间）", 42));
        g.Columns.Add(StockCol("CrowdingText", "时段挤兑提示", 28));

        g.CellFormatting += (_, e) =>
        {
            if (g.Rows.Count == 0 || e.RowIndex < 0) return;
            if (g.Rows[e.RowIndex].DataBoundItem is not MaterialOccupancyRow r) return;
            if (r.NetShortage > 1e-9)
            {
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(220, 38, 38);
            }
            else if (r.Status == ShortageStatus.Tight)
            {
                e.CellStyle.ForeColor = Color.FromArgb(217, 119, 6);
            }
            else
            {
                e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
            }
        };
        return g;
    }

    private static DataGridView MakeAssetGrid()
    {
        var g = MakeStockGrid();   // 复用同一套网格外观
        g.Columns.Clear();
        g.Columns.Add(StockCol("Process", "工序", 8));
        g.Columns.Add(StockCol("AssetName", "资产", 16));
        g.Columns.Add(StockCol("AssetCode", "编码", 13));
        g.Columns.Add(StockCol("NeedText", "本单需求", 9));
        g.Columns.Add(StockCol("AvailableText", "可用余量", 9));
        g.Columns.Add(StockCol("StatusText", "状态", 9));
        g.Columns.Add(StockCol("ConflictText", "冲突占用明细", 30));
        g.Columns.Add(StockCol("OptionText", "可选方案", 34));

        g.CellFormatting -= (_, _) => { };
        g.CellFormatting += (_, e) =>
        {
            if (g.Rows.Count == 0 || e.RowIndex < 0) return;
            if (g.Rows[e.RowIndex].DataBoundItem is not AssetCheckLine l) return;
            switch (l.Status)
            {
                case AssetCheckStatus.Conflict:
                    e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                    e.CellStyle.ForeColor = Color.FromArgb(220, 38, 38);
                    break;
                case AssetCheckStatus.Insufficient:
                    e.CellStyle.BackColor = Color.FromArgb(255, 247, 237);
                    e.CellStyle.ForeColor = Color.FromArgb(217, 119, 6);
                    break;
                default:
                    e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
                    break;
            }
        };
        return g;
    }

    /// <summary>按当前型号 / 计划数 / 起止日期重算资源校验（物料 + 资产）。纯查询。</summary>
    private void RefreshResourceCheck()
    {
        if (_lblStock is null || _lblAsset is null) return;

        var model = _txtModel?.Text.Trim() ?? "";
        if (model.Length == 0)
        {
            _lastResourceCheck = null;
            _matOcc = new MaterialOccupancyCheck();
            _lblStock.Text = "填写产品型号后自动做齐套检查。";
            _lblStock.ForeColor = Color.FromArgb(100, 116, 139);
            _lblAsset.Text = "设备/工装是可复用资源，按时间窗判冲突。";
            _lblAsset.ForeColor = Color.FromArgb(100, 116, 139);
            _gridStock.DataSource = null;
            _gridAsset.DataSource = null;
            return;
        }

        var woNo = _existing?.WoNo ?? _txtWoNo?.Text.Trim() ?? "";
        var qty = (int)(_numPlan?.Value ?? 0);
        var res = MesDatabase.CheckResourceAvailability(
            woNo, model, qty, _dtStart.Value, _dtDue.Value, includeMaterial: true);
        _lastResourceCheck = res;

        // ---- 资材占用（主体）：按「工单 + BOM」展开本单 → 跨工单按物料累加 → 算净缺口 ----
        _matOcc = MesDatabase.CheckMaterialOccupancy(
            woNo: string.IsNullOrWhiteSpace(woNo) ? null : woNo,
            productModel: model, planQty: qty, finishedQty: (int)(_numFinished?.Value ?? 0),
            start: _dtStart.Value, end: _dtDue.Value, due: _dtDue.Value);

        var agg = _matOcc.Aggregated;
        _gridStock.DataSource = agg;
        if (agg.Count == 0)
        {
            _lblStock.Text = "该型号暂无 BOM 数据，无法计算资材占用。";
            _lblStock.ForeColor = Color.FromArgb(100, 116, 139);
        }
        else if (_matOcc.AllSatisfied)
        {
            _lblStock.Text = $"✓ 资材齐备：本单占用 {agg.Count} 种物料，跨工单累加后已全部覆盖"
                           + (_matOcc.CrowdingCount > 0 ? $"（{_matOcc.CrowdingCount} 项时段挤兑，不影响总量）" : "") + "。";
            _lblStock.ForeColor = Color.FromArgb(21, 128, 61);
        }
        else
        {
            var need = agg.Where(r => r.NetShortage > 1e-9)
                .Select(r => $"{r.MaterialName} 占{r.TotalText}（{r.WorkOrderCount}单）需购{r.ShortageText}");
            _lblStock.Text = $"⚠ 资材未齐备：{_matOcc.ShortageCount} 种需采购 → {string.Join("；", need.Take(3))}"
                           + (_matOcc.CrowdingCount > 0 ? $"；{_matOcc.CrowdingCount} 项时段挤兑。" : "");
            _lblStock.ForeColor = Color.FromArgb(220, 38, 38);
        }

        // ---- 设备页（辅助维度：设备可复用，按时间窗判冲突） ----
        _gridAsset.DataSource = res.AssetLines;
        if (res.AssetLines.Count == 0) { _lblAsset.Text = "该型号未匹配到需要占用的资产。"; _lblAsset.ForeColor = Color.FromArgb(100, 116, 139); }
        else if (res.AssetOk) { _lblAsset.Text = $"✓ 资产满足：{res.AssetLines.Count} 项设备/工装在计划时段均有余量。"; _lblAsset.ForeColor = Color.FromArgb(21, 128, 61); }
        else
        {
            var detail = string.Join("；", res.AssetLines
                .Where(l => l.Status != AssetCheckStatus.Ok)
                .Select(l => $"{l.AssetName}（{(l.Status == AssetCheckStatus.Conflict ? "时间冲突" : "余量不足")}）")
                .Take(3));
            _lblAsset.Text = $"⚠ 资产 {res.ConflictCount} 项时间冲突 / {res.InsufficientCount} 项余量不足：{detail}。详见下表与可选方案。";
            _lblAsset.ForeColor = Color.FromArgb(220, 38, 38);
        }
    }

    /// <summary>
    /// AI 解析文本 → 回填工单字段。
    /// 未启用模型 / 超时 / 解析失败 / 置信度不足时自动走规则抽取，**结果一律标明来源**——
    /// 用户必须知道填进来的这些字段是模型猜的还是规则抽的。
    /// </summary>
    private async void BtnAiParse()
    {
        var text = _txtAiInput.Text.Trim();
        if (text.Length == 0) { Warn("请先粘贴一段需求描述。"); return; }

        _btnAiParse.Enabled = false;
        _btnAiParse.Text = "解析中…";
        try
        {
            LlmCallResult<WorkOrderAiParse> r;
            try { r = await Task.Run(() => AiTasks.ParseWorkOrderText(text)); }
            catch (Exception ex) { r = new LlmCallResult<WorkOrderAiParse> { Ok = false, Source = LlmSources.Rule, Reason = ex.Message }; }

            _lblAiTrace.Text = r.TraceText;
            if (!r.Ok || r.Value is null)
            {
                Warn("没能从这段文字里解析出完整工单信息。\n\n原因：" + r.Reason);
                return;
            }

            var v = r.Value;
            if (v.ProductModel.Length > 0) _txtModel.Text = v.ProductModel;
            if (v.PlanQty > 0) _numPlan.Value = Math.Clamp(v.PlanQty, 1, _numPlan.Maximum);
            if (v.Line.Length > 0 && _cboLine.Items.Contains(v.Line)) _cboLine.SelectedItem = v.Line;
            if (v.StartDate is not null) _dtStart.Value = v.StartDate.Value;
            if (v.DueDate is not null) _dtDue.Value = v.DueDate.Value;

            var notes = new List<string>();
            if (v.Warnings.Count > 0) notes.Add("提示：" + string.Join("；", v.Warnings));
            if (v.EquipmentNames.Count > 0) notes.Add("识别到的设备：" + string.Join("、", v.EquipmentNames));
            if (notes.Count > 0) MessageBox.Show(string.Join("\n", notes), "解析说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            _btnAiParse.Enabled = true;
            _btnAiParse.Text = "🤖 AI 解析文本";
        }
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
