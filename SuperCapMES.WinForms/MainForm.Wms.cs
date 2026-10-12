using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分，第 20 部分）：WMS 仓储页（单据管理 / 库存台账与 ERP 对账 / 效期与 FIFO / 入库检 / 履历）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Wms」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= WMS 仓储 =================
    /// <summary>构建「Wms」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildWmsTab()
    {
        var page = new TabPage("WMS 仓储");
        _wmsSubTabs = new TabControl { Dock = DockStyle.Fill };
        _wmsSubTabs.TabPages.Add(BuildWmsOrdersSubTab());
        _wmsSubTabs.TabPages.Add(BuildWmsStockSubTab());
        _wmsSubTabs.TabPages.Add(BuildExpiryFifoSubTab());   // 库存效期与 FIFO（批次效期预警 + 先进先出推荐）
        page.Controls.Add(_wmsSubTabs);
        return page;
    }

    // ---------- 子页1：单据管理 ----------
    /// <summary>构建「WmsOrdersSub」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildWmsOrdersSubTab()
    {
        var page = new TabPage("单据管理");
        page.Padding = new Padding(6);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 236,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("WMS 单据列表（选中行 → 底部可过账；双击切到明细页）", out _wmsOrderGrid));

        _wmsDetailTabs = new TabControl { Dock = DockStyle.Fill };
        _wmsDetailTabs.TabPages.Add(BuildWmsLineTab());
        _wmsDetailTabs.TabPages.Add(BuildWmsInspectTab());
        _wmsDetailTabs.TabPages.Add(BuildWmsHistoryTab());
        split.Panel2.Controls.Add(_wmsDetailTabs);

        page.Controls.Add(split);                   // Fill
        page.Controls.Add(BuildWmsActionBar());     // Bottom
        page.Controls.Add(BuildWmsQueryBarTime());  // Top2
        page.Controls.Add(BuildWmsQueryBarMain());  // Top1

        SetupWmsOrderColumns();
        SetupWmsLineColumns();
        SetupWmsHistoryColumns();
        DoQueryWmsOrders();
        return page;
    }

    /// <summary>构建「WmsQueryBarMain」界面区块。</summary>
    private Control BuildWmsQueryBarMain()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "单据类型：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboWmsTypeFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(4, 4, 0, 0) };
        _cboWmsTypeFilter.Items.Add("全部");
        _cboWmsTypeFilter.Items.AddRange(WmsOrderTypes.All);
        _cboWmsTypeFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboWmsTypeFilter);

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboWmsStatusFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 94, Margin = new Padding(4, 4, 0, 0) };
        _cboWmsStatusFilter.Items.Add("全部");
        _cboWmsStatusFilter.Items.AddRange(WmsOrderStatus.All);
        _cboWmsStatusFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboWmsStatusFilter);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtWmsKeyword = new TextBox { Width = 190, Margin = new Padding(4, 4, 0, 0) };
        _txtWmsKeyword.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoQueryWmsOrders(); };
        bar.Controls.Add(_txtWmsKeyword);
        bar.Controls.Add(new Label { Text = "（单号 / 往来对象 / 经手人）", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(6, 8, 0, 0) });
        return bar;
    }

    /// <summary>构建「WmsQueryBarTime」界面区块。</summary>
    private Control BuildWmsQueryBarTime()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 4, 4, 0), WrapContents = false };

        _chkWmsUseTimeFilter = new CheckBox { Text = "按创建时间", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        _chkWmsUseTimeFilter.CheckedChanged += (_, _) =>
        {
            var enabled = _chkWmsUseTimeFilter.Checked;
            _dtWmsFrom.Enabled = enabled;
            _dtWmsTo.Enabled = enabled;
        };
        bar.Controls.Add(_chkWmsUseTimeFilter);

        _dtWmsFrom = MakeHoldDatePicker();
        _dtWmsFrom.Value = DateTime.Today.AddDays(-30);
        bar.Controls.Add(_dtWmsFrom);
        bar.Controls.Add(new Label { Text = " ~ ", AutoSize = true, Margin = new Padding(2, 8, 0, 0) });
        _dtWmsTo = MakeHoldDatePicker();
        _dtWmsTo.Value = DateTime.Today;
        bar.Controls.Add(_dtWmsTo);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryWmsOrders()));
        bar.Controls.Add(MakeToolbarButton("重置", BtnResetWmsQuery));

        _lblWmsQueryResult = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 8, 0, 0) };
        bar.Controls.Add(_lblWmsQueryResult);
        return bar;
    }

    /// <summary>构建「Wms」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildWmsActionBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8, 6, 4, 0), WrapContents = false };
        _lblWmsContext = new Label
        {
            Text = "请选择一条单据",
            AutoSize = true,
            Margin = new Padding(2, 9, 0, 0),
            ForeColor = Color.FromArgb(40, 50, 60)
        };
        bar.Controls.Add(_lblWmsContext);

        bar.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(14, 9, 0, 0) });
        _txtWmsOperator = new TextBox { Text = "admin", Width = 100, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtWmsOperator);

        bar.Controls.Add(MakeToolbarButton("新建单据", BtnWmsNewOrder));
        bar.Controls.Add(MakeToolbarButton("打印单据", BtnPrintWmsSlip));
        _btnWmsPost = MakeToolbarButton("过账", BtnWmsPost);
        _btnWmsPost.Enabled = false;
        bar.Controls.Add(_btnWmsPost);
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => { DoQueryWmsOrders(); DoQueryWmsStock(); }));
        return bar;
    }

    /// <summary>构建「WmsLine」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildWmsLineTab()
    {
        var page = new TabPage("单据明细");
        page.Controls.Add(MakeGridGroup("单据明细（选中单据后显示）", out _wmsLineGrid));
        _lblWmsLineTotal = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            Text = "合计数量：0",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        page.Controls.Add(_lblWmsLineTotal);
        return page;
    }

    /// <summary>构建「WmsInspect」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildWmsInspectTab()
    {
        var page = new TabPage("入库检");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _lblWmsInspectInfo = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(15, 76, 129),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "入库检仅适用于「采购入库」单据"
        };
        _lblWmsInspectHint = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _wmsInspectGrid = MakeGrid();
        _wmsInspectGrid.ReadOnly = true;
        // 实测值编辑结束 → 按标准值自动判定（与工序实绩同一口径）
        _wmsInspectGrid.CellEndEdit += (_, e) => WmsAutoJudgeInspection(e.RowIndex);
        // 结果列"不合格"行整行标红：复用与工序实绩相同的事件逻辑（先解除再挂接，防重复订阅）
        _wmsInspectGrid.CellFormatting -= InspectGrid_CellFormatting;
        _wmsInspectGrid.CellFormatting += InspectGrid_CellFormatting;
        _wmsInspectGrid.DataError += (_, e) => e.ThrowException = false;

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _btnWmsLoadInspect = MakeToolbarButton("载入/刷新入库检", BtnWmsLoadInspect);
        _btnWmsSaveInspect = MakeToolbarButton("保存入库检", BtnWmsSaveInspect);
        _btnWmsLoadInspect.Enabled = false;
        _btnWmsSaveInspect.Enabled = false;
        btnRow.Controls.Add(_btnWmsLoadInspect);
        btnRow.Controls.Add(_btnWmsSaveInspect);

        root.Controls.Add(_lblWmsInspectInfo, 0, 0);
        root.Controls.Add(_lblWmsInspectHint, 0, 1);
        root.Controls.Add(_wmsInspectGrid, 0, 2);
        root.Controls.Add(btnRow, 0, 3);
        page.Controls.Add(root);
        return page;
    }

    /// <summary>构建「WmsHistory」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildWmsHistoryTab()
    {
        var page = new TabPage("操作履历");
        page.Controls.Add(MakeGridGroup("操作履历（创建 / 入库检 / 过账）", out _wmsHistoryGrid));
        return page;
    }

    /// <summary>设置「WmsOrderColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupWmsOrderColumns()
    {
        _wmsOrderGrid.AutoGenerateColumns = false;
        _wmsOrderGrid.Columns.Clear();
        _wmsOrderGrid.Columns.Add(MakeTextCol("OrderNo", "单据号", true, 13));
        _wmsOrderGrid.Columns.Add(MakeTextCol("Type", "类型", true, 9));
        _wmsOrderGrid.Columns.Add(MakeTextCol("DirectionDesc", "方向", true, 5));
        _wmsOrderGrid.Columns.Add(MakeTextCol("Status", "状态", true, 7));
        _wmsOrderGrid.Columns.Add(MakeTextCol("Partner", "往来对象", true, 11));
        _wmsOrderGrid.Columns.Add(MakeTextCol("RefNo", "参考号", true, 11));
        _wmsOrderGrid.Columns.Add(MakeTextCol("Reason", "退货原因", true, 12));
        _wmsOrderGrid.Columns.Add(MakeTextCol("LineCount", "物料行数", true, 6));
        var qtyCol = MakeTextCol("TotalQty", "总数量", true, 7);
        qtyCol.DefaultCellStyle.Format = "0.##";
        _wmsOrderGrid.Columns.Add(qtyCol);
        _wmsOrderGrid.Columns.Add(MakeTextCol("Operator", "经手人", true, 7));
        var createCol = MakeTextCol("CreateTime", "创建时间", true, 12);
        createCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _wmsOrderGrid.Columns.Add(createCol);
        var postCol = MakeTextCol("PostTime", "过账时间", true, 12);
        postCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _wmsOrderGrid.Columns.Add(postCol);
        _wmsOrderGrid.Columns.Add(MakeTextCol("PostBy", "过账人", true, 7));

        // 先解除再挂接着色事件（同上，防止重复着色）
        _wmsOrderGrid.CellFormatting -= WmsOrderGrid_CellFormatting;
        _wmsOrderGrid.CellFormatting += WmsOrderGrid_CellFormatting;
        _wmsOrderGrid.SelectionChanged += (_, _) => LoadWmsOrderDetails();
        _wmsOrderGrid.CellDoubleClick -= WmsOrderGrid_CellDoubleClick;
        _wmsOrderGrid.CellDoubleClick += WmsOrderGrid_CellDoubleClick;
    }

    /// <summary>设置「WmsLineColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupWmsLineColumns()
    {
        _wmsLineGrid.AutoGenerateColumns = false;
        _wmsLineGrid.Columns.Clear();
        _wmsLineGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 13));
        _wmsLineGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 22));
        _wmsLineGrid.Columns.Add(MakeTextCol("Spec", "规格", true, 12));
        _wmsLineGrid.Columns.Add(MakeTextCol("Unit", "单位", true, 7));
        _wmsLineGrid.Columns.Add(MakeTextCol("LotNo", "批次", true, 13));
        var qtyCol = MakeTextCol("Quantity", "数量", true, 9);
        qtyCol.DefaultCellStyle.Format = "0.##";
        _wmsLineGrid.Columns.Add(qtyCol);
        _wmsLineGrid.Columns.Add(MakeTextCol("Location", "库位", true, 13));
        _wmsLineGrid.Columns.Add(MakeTextCol("Remark", "备注", true, 14));
    }

    /// <summary>设置「WmsHistoryColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupWmsHistoryColumns()
    {
        _wmsHistoryGrid.AutoGenerateColumns = false;
        _wmsHistoryGrid.Columns.Clear();
        _wmsHistoryGrid.Columns.Add(MakeTextCol("Action", "动作", true, 10));
        var timeCol = MakeTextCol("Time", "时间", true, 18);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm:ss";
        _wmsHistoryGrid.Columns.Add(timeCol);
        _wmsHistoryGrid.Columns.Add(MakeTextCol("Operator", "操作人", true, 10));
        _wmsHistoryGrid.Columns.Add(MakeTextCol("Remark", "备注", true, 62));
    }

    /// <summary>单据状态着色：待过账=橙（加粗）、已过账=绿；方向列为入库存绿、出库存红。</summary>
    private void WmsOrderGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        var col = grid.Columns[e.ColumnIndex].Name;

        if (col == "Status")
        {
            var value = e.Value?.ToString();
            e.CellStyle.Font = value == WmsOrderStatus.Pending
                ? new Font("微软雅黑", 9F, FontStyle.Bold)
                : new Font("微软雅黑", 9F);
            e.CellStyle.ForeColor = value switch
            {
                WmsOrderStatus.Pending => Color.FromArgb(230, 126, 34),
                WmsOrderStatus.Posted => Color.FromArgb(39, 174, 96),
                _ => Color.FromArgb(40, 50, 60)
            };
        }
        else if (col == "DirectionDesc")
        {
            e.CellStyle.ForeColor = e.Value?.ToString() == "入库"
                ? Color.FromArgb(39, 174, 96)
                : Color.FromArgb(192, 0, 0);
        }
    }

    /// <summary>「WmsOrderGrid」双击单元格 → 执行跳转/穿透查看。</summary>
    private void WmsOrderGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_wmsDetailTabs is not null) _wmsDetailTabs.SelectedIndex = 0;
    }

    /// <summary>在网格中选中「edWmsOrder」对应的行（联动加载明细）。</summary>
    private WmsOrder? SelectedWmsOrder()
    {
        if (_wmsOrderGrid is null || _wmsOrderGrid.SelectedRows.Count == 0) return null;
        return _wmsOrderGrid.SelectedRows[0].DataBoundItem as WmsOrder;
    }

    /// <summary>查询并加载「WmsOrders」数据到界面（含网格绑定与汇总刷新）。</summary>
    private void DoQueryWmsOrders()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_wmsOrderGrid is null) return;
        var type = _cboWmsTypeFilter.SelectedItem?.ToString();
        if (type is null or "全部") type = null;
        var status = _cboWmsStatusFilter.SelectedItem?.ToString();
        if (status is null or "全部") status = null;
        var kw = _txtWmsKeyword.Text.Trim();

        DateTime? from = null;
        DateTime? to = null;
        if (_chkWmsUseTimeFilter.Checked)
        {
            from = _dtWmsFrom.Value.Date;
            to = _dtWmsTo.Value.Date;
        }

        var list = MesDatabase.QueryWmsOrders(type, status, string.IsNullOrEmpty(kw) ? null : kw, from, to);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _wmsOrderGrid.DataSource = null;
        _wmsOrderGrid.DataSource = list;
        _lblWmsQueryResult.Text = $"共查询到 {list.Count} 张单据";
        LoadWmsOrderDetails();
    }

    /// <summary>处理「ResetWmsQuery」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnResetWmsQuery(object? sender, EventArgs e)
    {
        _cboWmsTypeFilter.SelectedIndex = 0;
        _cboWmsStatusFilter.SelectedIndex = 0;
        _txtWmsKeyword.Text = "";
        _chkWmsUseTimeFilter.Checked = true;
        _dtWmsFrom.Value = DateTime.Today.AddDays(-30);
        _dtWmsTo.Value = DateTime.Today;
        DoQueryWmsOrders();
    }

    /// <summary>选中单据 → 联动刷新明细 / 入库检 / 履历与过账按钮状态</summary>
    private void LoadWmsOrderDetails()
    {
        if (_wmsOrderGrid is null || _wmsLineGrid is null || _wmsHistoryGrid is null
            || _btnWmsPost is null || _lblWmsContext is null || _lblWmsLineTotal is null) return;

        var order = SelectedWmsOrder();
        if (order is null)
        {
            _lblWmsContext.Text = "请选择一条单据";
            _btnWmsPost.Enabled = false;
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _wmsLineGrid.DataSource = null;
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _wmsHistoryGrid.DataSource = null;
            _lblWmsLineTotal.Text = "合计数量：0";
            LoadWmsInspectionForCurrent(null);
            return;
        }

        _lblWmsContext.Text = $"{order.OrderNo} · {order.Type} · {order.DirectionDesc} · {order.Status} · 往来：{order.Partner}";
        _btnWmsPost.Enabled = order.Status == WmsOrderStatus.Pending;

        var lines = MesDatabase.GetWmsOrderLines(order.OrderNo);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _wmsLineGrid.DataSource = null;
        _wmsLineGrid.DataSource = lines;
        _lblWmsLineTotal.Text = $"共 {lines.Count} 行，合计数量 {lines.Sum(l => l.Quantity):0.##}";

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _wmsHistoryGrid.DataSource = null;
        _wmsHistoryGrid.DataSource = MesDatabase.GetWmsOrderHistory(order.OrderNo);

        LoadWmsInspectionForCurrent(order);
    }

    /// <summary>ReselectWmsOrder：（说明待补充）</summary>
    private void ReselectWmsOrder(string orderNo)
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_wmsOrderGrid is null) return;
        _wmsOrderGrid.ClearSelection();
        foreach (DataGridViewRow row in _wmsOrderGrid.Rows)
        {
            if (row.DataBoundItem is WmsOrder o && o.OrderNo == orderNo)
            {
                row.Selected = true;
                _wmsOrderGrid.CurrentCell = row.Cells[0];
                break;
            }
        }
        LoadWmsOrderDetails();
    }

    /// <summary>处理「WmsNewOrder」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnWmsNewOrder(object? sender, EventArgs e)
    {
        using var form = new WmsOrderEditForm();
        if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(form.CreatedOrderNo))
        {
            DoQueryWmsOrders();
            ReselectWmsOrder(form.CreatedOrderNo);
        }
    }

    /// <summary>处理「WmsPost」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnWmsPost(object? sender, EventArgs e)
    {
        var order = SelectedWmsOrder();
        if (order is null) { Warn("请先选择要过账的单据。"); return; }
        if (order.Status != WmsOrderStatus.Pending)
        {
            Warn($"单据 {order.OrderNo} 当前状态为「{order.Status}」，无需过账。");
            return;
        }

        // 采购入库未填入库检 → 提示是否继续（默认「否」）
        if (WmsOrderTypes.SupportsInspection(order.Type) && MesDatabase.CountWmsInspections(order.OrderNo) == 0)
        {
            var askInspect = MessageBox.Show(
                "该采购入库单尚未填写入库检，是否仍要过账？",
                "入库检提醒",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (askInspect != DialogResult.Yes) return;
        }

        if (MessageBox.Show(
                $"确认过账单据 {order.OrderNo}？\n过账后将按方向增减库存，且单据不可修改。",
                "过账确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1) != DialogResult.Yes)
        {
            return;
        }

        var op = _txtWmsOperator.Text.Trim();
        if (string.IsNullOrEmpty(op)) op = "admin";
        var (ok, message) = MesDatabase.PostWmsOrder(order.OrderNo, op);
        if (!ok)
        {
            Warn(message);
            return;
        }
        MessageBox.Show(message, "过账成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryWmsOrders();
        ReselectWmsOrder(order.OrderNo);
    }

    /// <summary>加载「WmsInspectionForCurrent」并绑定到界面。</summary>
    private void LoadWmsInspectionForCurrent(WmsOrder? order)
    {
        if (_wmsInspectGrid is null || _lblWmsInspectInfo is null || _lblWmsInspectHint is null
            || _btnWmsLoadInspect is null || _btnWmsSaveInspect is null) return;

        if (order is null || !WmsOrderTypes.SupportsInspection(order.Type))
        {
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _wmsInspectGrid.DataSource = null;
            _wmsInspectGrid.Columns.Clear();
            _wmsInspectGrid.ReadOnly = true;
            _btnWmsLoadInspect.Enabled = false;
            _btnWmsSaveInspect.Enabled = false;
            _lblWmsInspectInfo.Text = "入库检仅适用于「采购入库」单据";
            _lblWmsInspectHint.Text = order is null ? "请先在上方选中一张单据。" : $"当前单据类型：{order.Type}，不支持入库检。";
            return;
        }

        var saved = MesDatabase.CountWmsInspections(order.OrderNo);
        _lblWmsInspectInfo.Text = $"入库检对象：{order.OrderNo}（已保存 {saved} 条）";
        _lblWmsInspectHint.Text = "填写实测值后自动判定；不合格行整行标红。保存前若有不合格项会二次确认。";
        _wmsInspectGrid.ReadOnly = false;
        BindWmsInspectGrid(MesDatabase.GetWmsInspectionRows(order.OrderNo));
        _btnWmsLoadInspect.Enabled = true;
        _btnWmsSaveInspect.Enabled = true;
    }

    /// <summary>处理「WmsLoadInspect」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnWmsLoadInspect(object? sender, EventArgs e)
    {
        var order = SelectedWmsOrder();
        if (order is null) { Warn("请先在上方选中一张单据。"); return; }
        if (!WmsOrderTypes.SupportsInspection(order.Type)) { Warn("入库检仅适用于采购入库单。"); return; }
        LoadWmsInspectionForCurrent(order);
    }

    /// <summary>WmsAutoJudgeInspection：（说明待补充）</summary>
    private void WmsAutoJudgeInspection(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _wmsInspectGrid.Rows.Count) return;
        if (_wmsInspectGrid.Rows[rowIndex].IsNewRow) return;

        var row = _wmsInspectGrid.Rows[rowIndex];
        var std = row.Cells["StandardValue"].Value?.ToString() ?? "";
        var measured = row.Cells["MeasuredValue"].Value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(measured)) return;

        var ok = InspectionJudge.TryJudge(std, measured);
        if (ok is null) return;

        var result = ok.Value ? "合格" : "不合格";
        var resultCell = row.Cells["Result"];
        if (resultCell.Value?.ToString() != result)
        {
            resultCell.Value = result;
            _wmsInspectGrid.InvalidateRow(rowIndex);
        }
    }

    /// <summary>处理「WmsSaveInspect」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnWmsSaveInspect(object? sender, EventArgs e)
    {
        var order = SelectedWmsOrder();
        if (order is null || !WmsOrderTypes.SupportsInspection(order.Type))
        {
            Warn("请先在上方选中一张采购入库单。");
            return;
        }

        // 先提交编辑中的单元格：触发自动判定，并让最新值落入行数据
        if (_wmsInspectGrid.IsCurrentCellInEditMode) _wmsInspectGrid.EndEdit();
        _wmsInspectGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        var rows = new List<InspectionRow>();
        foreach (DataGridViewRow r in _wmsInspectGrid.Rows)
        {
            if (r.IsNewRow) continue;
            rows.Add(new InspectionRow
            {
                ItemName = r.Cells["ItemName"].Value?.ToString() ?? "",
                StandardValue = r.Cells["StandardValue"].Value?.ToString() ?? "",
                MeasuredValue = r.Cells["MeasuredValue"].Value?.ToString() ?? "",
                Result = r.Cells["Result"].Value?.ToString() ?? "",
                Inspector = r.Cells["Inspector"].Value?.ToString() ?? ""
            });
        }
        if (rows.Count == 0) { Warn("没有可保存的入库检项目。"); return; }

        var badCount = rows.Count(r => r.Result == "不合格");
        if (badCount > 0)
        {
            var ask = MessageBox.Show(
                $"存在 {badCount} 项入库检不合格，是否仍要保存？",
                "入库检结果提醒",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (ask != DialogResult.Yes) return;
        }

        var op = _txtWmsOperator.Text.Trim();
        if (string.IsNullOrEmpty(op)) op = "admin";
        var (saved, ng) = MesDatabase.SaveWmsInspections(order.OrderNo, op, rows);
        MessageBox.Show($"已保存 {saved} 条（其中不合格 {ng} 条）。",
            "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadWmsOrderDetails();
    }

    /// <summary>BindWmsInspectGrid：（说明待补充）</summary>
    private void BindWmsInspectGrid(List<InspectionRow> rows)
    {
        _wmsInspectGrid.AutoGenerateColumns = false;
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _wmsInspectGrid.DataSource = null;
        _wmsInspectGrid.Columns.Clear();

        _wmsInspectGrid.Columns.Add(MakeTextCol("ItemName", "检验项目", true, 30));
        _wmsInspectGrid.Columns.Add(MakeTextCol("StandardValue", "标准值", true, 22));
        _wmsInspectGrid.Columns.Add(MakeTextCol("MeasuredValue", "实测值", false, 22));

        var resultCol = new DataGridViewComboBoxColumn { Name = "Result", HeaderText = "结果", DataPropertyName = "Result", FillWeight = 14 };
        resultCol.Items.Add("合格");
        resultCol.Items.Add("不合格");
        _wmsInspectGrid.Columns.Add(resultCol);

        _wmsInspectGrid.Columns.Add(MakeTextCol("Inspector", "检验员", false, 12));
        _wmsInspectGrid.DataSource = rows;
    }

    // ---------- 子页2：库存台账 ----------
    /// <summary>构建「WmsStockSub」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildWmsStockSubTab()
    {
        var page = new TabPage("库存台账");
        page.Padding = new Padding(6);
        page.Controls.Add(MakeGridGroup("WMS 库存台账 ↔ ERP 库存对账（按 物料编码+批次+库位 逐键比对；差异 = MES − ERP）", out _wmsStockGrid));

        _lblWmsStockSummary = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Text = "请点「查询」加载库存与 ERP 对账结果",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        page.Controls.Add(_lblWmsStockSummary);
        page.Controls.Add(BuildWmsStockQueryBar());

        SetupWmsStockColumns();
        DoQueryWmsStock();
        return page;
    }

    /// <summary>构建「WmsStock」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildWmsStockQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(6, 8, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _txtWmsStockKeyword = new TextBox { Width = 220, Margin = new Padding(4, 4, 0, 0) };
        _txtWmsStockKeyword.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoQueryWmsStock(); };
        bar.Controls.Add(_txtWmsStockKeyword);
        bar.Controls.Add(new Label { Text = "（物料编码 / 名称 / 批次 / 库位）", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(6, 8, 0, 0) });
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryWmsStock()));
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => { _txtWmsStockKeyword.Text = ""; DoQueryWmsStock(); }));

        _lblWmsStockResult = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 8, 0, 0) };
        bar.Controls.Add(_lblWmsStockResult);
        return bar;
    }

    // ================= WMS 效期与 FIFO 子页 =================

    /// <summary>库存效期预警 + FIFO 推荐子页（挂在 WMS 仓储页下）。</summary>
    private TabPage BuildExpiryFifoSubTab()
    {
        var page = new TabPage("效期与 FIFO");
        page.Padding = new Padding(6);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("已过期 (批次)", Color.FromArgb(192, 0, 0), out _lblExpiryExpired));
        cards.Controls.Add(MakeCard("临期 ≤30 天 (批次)", Color.FromArgb(230, 126, 34), out _lblExpiryNear));
        cards.Controls.Add(MakeCard("不做效期管理 (批次)", Color.FromArgb(127, 140, 141), out _lblExpiryNone));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _txtExpiryKeyword = new TextBox { Width = 200, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtExpiryKeyword);
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryExpiryFifo()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _txtExpiryKeyword.Text = "";
            DoQueryExpiryFifo();
        }));
        _btnReinspect = MakeToolbarButton("🔬 重新进料检", BtnReinspect);
        _btnReinspect.Width = 118;
        _btnReinspect.Enabled = false;
        bar.Controls.Add(_btnReinspect);

        _btnScrap = MakeToolbarButton("🗑 废弃批次", BtnScrap);
        _btnScrap.Width = 108;
        _btnScrap.Enabled = false;
        bar.Controls.Add(_btnScrap);

        bar.Controls.Add(new Label
        {
            Text = "（物料编码 / 名称 / 批次）｜ FIFO：同物料多批次时，入库最早的批次标 ★ 优先出库",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(10, 8, 0, 0)
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 220,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("效期预警（已过期 / 临期 ≤30 天；按剩余天数升序）", out _expiryAlertGrid));
        split.Panel2.Controls.Add(MakeGridGroup("FIFO 视图（按物料 + 批次 + 库位；入库时间升序，★ 为优先出库批次）", out _fifoGrid));

        // 进料检历史：按需求"在物料明细下方以列表形式追加" → 用 Bottom 面板，
        // 不做嵌套 SplitContainer（构造期设 SplitterDistance 会抛异常，本项目已踩过 3 次）
        var historyHost = new Panel { Dock = DockStyle.Bottom, Height = 176, Padding = new Padding(0, 4, 0, 0) };
        _lblReinspectHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font("微软雅黑", 8.5F),
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "重新进料检历史（选中上方任一批次即按该物料过滤；按进料检时间倒序）"
        };
        historyHost.Controls.Add(MakeGridGroup(
            "进料检历史（原效期 → 新效期 / 进料检人 / 进料检时间 / 结论）；同一批次可多次进料检，全部留痕",
            out _reinspectGrid));
        historyHost.Controls.Add(_lblReinspectHint);
        SetupReinspectColumns();

        page.Controls.Add(split);
        page.Controls.Add(historyHost);
        page.Controls.Add(bar);
        page.Controls.Add(cards);

        SetupExpiryFifoColumns();
        WireExpiryFifoSelection();
        DoQueryExpiryFifo();

        page.Enter -= ExpiryFifoTab_Enter;
        page.Enter += ExpiryFifoTab_Enter;
        return page;
    }

    /// <summary>设置「ExpiryFifoColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupExpiryFifoColumns()
    {
        _expiryAlertGrid.AutoGenerateColumns = false;
        _expiryAlertGrid.Columns.Clear();
        _expiryAlertGrid.Columns.Add(MakeTextCol("ExpiryStatus", "效期状态", true, 9));
        _expiryAlertGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 13));
        _expiryAlertGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 20));
        _expiryAlertGrid.Columns.Add(MakeTextCol("LotNo", "批次", true, 14));
        _expiryAlertGrid.Columns.Add(MakeTextCol("Location", "库位", true, 12));
        _expiryAlertGrid.Columns.Add(MakeTextCol("Quantity", "数量", true, 8));
        _expiryAlertGrid.Columns.Add(MakeTextCol("ExpiryText", "到期日", true, 12));
        _expiryAlertGrid.Columns.Add(MakeTextCol("DaysLeftText", "剩余", true, 11));
        // 先解除再挂接着色事件（同上，防止重复着色）
        _expiryAlertGrid.CellFormatting -= ExpiryAlertGrid_CellFormatting;
        _expiryAlertGrid.CellFormatting += ExpiryAlertGrid_CellFormatting;

        _fifoGrid.AutoGenerateColumns = false;
        _fifoGrid.Columns.Clear();
        _fifoGrid.Columns.Add(MakeTextCol("FifoText", "FIFO", true, 10));
        _fifoGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 13));
        _fifoGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 20));
        _fifoGrid.Columns.Add(MakeTextCol("LotNo", "批次", true, 14));
        _fifoGrid.Columns.Add(MakeTextCol("Location", "库位", true, 12));
        _fifoGrid.Columns.Add(MakeTextCol("Quantity", "数量", true, 8));
        var tCol = MakeTextCol("ReceivedDate", "入库/更新", true, 12);
        tCol.DefaultCellStyle.Format = "yyyy-MM-dd";
        _fifoGrid.Columns.Add(tCol);
        _fifoGrid.Columns.Add(MakeTextCol("ExpiryText", "到期日", true, 12));
        _fifoGrid.Columns.Add(MakeTextCol("ExpiryStatus", "效期状态", true, 9));
        // 先解除再挂接着色事件（同上，防止重复着色）
        _fifoGrid.CellFormatting -= FifoGrid_CellFormatting;
        _fifoGrid.CellFormatting += FifoGrid_CellFormatting;
    }

    /// <summary>效期着色：已过期红 / 临期橙 / 正常绿。</summary>
    private void ExpiryAlertGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_expiryAlertGrid.Rows[e.RowIndex].DataBoundItem is not StockFifoRow r) return;
        var col = _expiryAlertGrid.Columns[e.ColumnIndex].Name;
        if (col != "ExpiryStatus" && col != "DaysLeftText") return;

        e.CellStyle.ForeColor = r.ExpiryStatus switch
        {
            StockExpiryStatus.Expired => Color.FromArgb(192, 0, 0),
            StockExpiryStatus.NearExpiry => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(39, 174, 96)
        };
    }

    /// <summary>FIFO 视图着色：推荐批次绿加粗感（用颜色表达）+ 效期状态色。</summary>
    private void FifoGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_fifoGrid.Rows[e.RowIndex].DataBoundItem is not StockFifoRow r) return;

        var col = _fifoGrid.Columns[e.ColumnIndex].Name;
        if (col == "FifoText" && r.IsFifoRecommended)
        {
            e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);
            return;
        }
        if (col != "ExpiryStatus") return;

        e.CellStyle.ForeColor = r.ExpiryStatus switch
        {
            StockExpiryStatus.Expired => Color.FromArgb(192, 0, 0),
            StockExpiryStatus.NearExpiry => Color.FromArgb(230, 126, 34),
            StockExpiryStatus.Normal => Color.FromArgb(39, 174, 96),
            _ => Color.FromArgb(127, 140, 141)
        };
    }

    // ================= 废弃 / 重新进料检 =================

    /// <summary>
    /// 取当前行的库存批次（FIFO 优先，其次效期预警表）。
    /// 说明：WinForms 的 DataGridView **始终存在一个 CurrentRow**（"完全没有当前行"是做不到的，
    /// CurrentCell 置空后它会立刻回到第一行），所以这里按当前行判定即可。
    /// 防误操作不靠"必须先选中"——靠废弃弹窗：它把物料、批次、库位、数量全部列出来，
    /// 用户在点确认前有机会发现"这不是我要废弃的那一行"。
    /// </summary>
    private WmsStock? SelectedStock()
    {
        foreach (var grid in new[] { _fifoGrid, _expiryAlertGrid })
        {
            if (grid is null || grid.Rows.Count == 0) continue;
            if (grid.CurrentRow?.DataBoundItem is not StockFifoRow r) continue;
            var hit = MesDatabase.WmsStocks.FirstOrDefault(s =>
                string.Equals(s.MaterialCode, r.MaterialCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.LotNo, r.LotNo, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Location, r.Location, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>两张表的选中行联动 + 按钮启用态。</summary>
    private void WireExpiryFifoSelection()
    {
        void OnSel(object? sender, EventArgs e)
        {
            RefreshScrapButtons();
            LoadReinspectHistory(SelectedStock()?.MaterialCode);
        }
        _expiryAlertGrid.SelectionChanged -= OnSel;
        _expiryAlertGrid.SelectionChanged += OnSel;
        _fifoGrid.SelectionChanged -= OnSel;
        _fifoGrid.SelectionChanged += OnSel;
        RefreshScrapButtons();
    }

    /// <summary>按钮启用态：禁用时把原因写进 ToolTip（不让用户对着灰按钮猜）。</summary>
    private void RefreshScrapButtons()
    {
        if (_btnScrap is null || _btnReinspect is null) return;
        var s = SelectedStock();
        var sReason = MesDatabase.ScrapBlockedReason(s);
        var rReason = MesDatabase.ReinspectBlockedReason(s);
        _btnScrap.Enabled = sReason.Length == 0;
        _btnReinspect.Enabled = rReason.Length == 0;
        try
        {
            _btnScrapToolTip.SetToolTip(_btnScrap,
                sReason.Length == 0 ? "废弃该批次（不可撤销）" : sReason);
            _btnReinspectToolTip.SetToolTip(_btnReinspect,
                rReason.Length == 0 ? "对已过期批次发起重新进料检" : rReason);
        }
        catch { }
    }

    /// <summary>按物料加载进料检历史（时间倒序）。</summary>
    private void LoadReinspectHistory(string? materialCode)
    {
        if (_reinspectGrid is null) return;
        var list = MesDatabase.GetReinspections(materialCode);
        _reinspectGrid.DataSource = null;
        _reinspectGrid.DataSource = list;

        if (_lblReinspectHint is null) return;
        _lblReinspectHint.Text = materialCode is { Length: > 0 }
            ? $"「{materialCode}」的重新进料检历史：{list.Count} 条（按进料检时间倒序）"
              + (list.Count > 0 ? $"　最近一次新效期 {list[0].NewExpiryText}（{list[0].Inspector}）" : "　该物料尚无进料检记录")
            : $"全部物料的进料检历史：{list.Count} 条（选中上方批次可按物料过滤）";
    }

    private void SetupReinspectColumns()
    {
        _reinspectGrid.AutoGenerateColumns = false;
        _reinspectGrid.Columns.Clear();
        _reinspectGrid.Columns.Add(MakeTextCol("ReinspectNo", "进料检单号", true, 17));
        _reinspectGrid.Columns.Add(MakeTextCol("MaterialName", "物料", true, 15));
        _reinspectGrid.Columns.Add(MakeTextCol("LotNo", "批次", true, 16));
        _reinspectGrid.Columns.Add(MakeTextCol("Location", "库位", true, 12));
        _reinspectGrid.Columns.Add(MakeTextCol("OldExpiryText", "原效期", true, 11));
        _reinspectGrid.Columns.Add(MakeTextCol("NewExpiryText", "新效期", true, 11));
        _reinspectGrid.Columns.Add(MakeTextCol("ExtendedText", "延长", true, 8));
        _reinspectGrid.Columns.Add(MakeTextCol("Inspector", "进料检人", true, 10));
        _reinspectGrid.Columns.Add(MakeTextCol("InspectAtText", "进料检时间", true, 16));
        _reinspectGrid.Columns.Add(MakeTextCol("Result", "结论", true, 8));
        _reinspectGrid.Columns.Add(MakeTextCol("Remark", "备注", true, 24));
    }

    /// <summary>点「🗑 废弃批次」：弹窗填原因/操作人 → 二次确认 → 写台账 + 主表打标记 → 刷新。</summary>
    private void BtnScrap(object? sender, EventArgs e)
    {
        var s = SelectedStock();
        var blocked = MesDatabase.ScrapBlockedReason(s);
        if (blocked.Length > 0) { Warn(blocked); return; }

        using var dlg = new Forms.ScrapForm(s!, CurrentWmsOperator);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // 二次确认：不可逆动作必须让用户明确知道后果
        var confirm = $"确认废弃批次 <b>{s!.LotNo}</b>（{s.MaterialName} {s.Quantity:0.##} {s.Unit}）吗？\n\n"
                    + "<b>废弃不可撤销</b>：该批次数量将归零，并退出可用库存、FIFO 队列与效期待处理列表。\n"
                    + "如只是效期问题、想争取使用时间，请改用「重新进料检」。";
        if (MessageBox.Show(confirm, "废弃批次（不可撤销）", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var (ok, msg, _) = MesDatabase.ScrapMaterial(
            s.MaterialCode, s.LotNo, s.Location, dlg.Reason, dlg.Operator, dlg.Remark);
        if (!ok) { MessageBox.Show(msg, "废弃失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        MessageBox.Show(msg, "已废弃", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryExpiryFifo();
        RefreshScrapButtons();
    }

    /// <summary>点「🔬 重新进料检」：弹窗填进料检人/新效期 → 写台账 + 更新主表效期 → 刷新。</summary>
    private void BtnReinspect(object? sender, EventArgs e)
    {
        var s = SelectedStock();
        var blocked = MesDatabase.ReinspectBlockedReason(s);
        if (blocked.Length > 0) { Warn(blocked); return; }

        using var dlg = new Forms.ReinspectForm(s!, CurrentWmsOperator);
        dlg.BindOldExpiry(s!.ExpiryDate);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg, _) = MesDatabase.ReinspectMaterial(
            s.MaterialCode, s.LotNo, s.Location, dlg.NewExpiry, dlg.Inspector, dlg.Result, dlg.Remark);
        if (!ok) { MessageBox.Show(msg, "进料检失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        MessageBox.Show(msg, "进料检完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryExpiryFifo();
        LoadReinspectHistory(s.MaterialCode);
        RefreshScrapButtons();
    }

    /// <summary>当前 WMS 操作人（弹窗默认值；项目无登录体系，沿用既有"操作人"约定）</summary>
    private string CurrentWmsOperator => _txtWmsOperator?.Text.Trim() ?? "";

    /// <summary>进入「ExpiryFifoTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void ExpiryFifoTab_Enter(object? sender, EventArgs e) => EnsureExpiryFifoReady();

    /// <summary>兜底初始化「ExpiryFifo」：页签 Enter/全局刷新时确保数据已加载（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureExpiryFifoReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_expiryAlertGrid is null) return;
        if (_expiryAlertGrid.Rows.Count == 0 && _fifoGrid?.Rows.Count == 0) DoQueryExpiryFifo();
    }

    /// <summary>加载效期预警与 FIFO 视图。</summary>
    private void DoQueryExpiryFifo()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_expiryAlertGrid is null) return;
        var kw = _txtExpiryKeyword?.Text.Trim() ?? "";

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _expiryAlertGrid.DataSource = null;
        _expiryAlertGrid.DataSource = MesDatabase.GetExpiryAlerts();
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _fifoGrid.DataSource = null;
        _fifoGrid.DataSource = MesDatabase.GetStockFifo(kw.Length == 0 ? null : kw);

        // 重新绑定后按当前行刷新按钮状态（DataGridView 必有 CurrentRow，WinForms 特性）
        RefreshScrapButtons();
        LoadReinspectHistory(SelectedStock()?.MaterialCode);

        var (expired, near, none) = MesDatabase.GetExpirySummary();
        if (_lblExpiryExpired is not null)
        {
            _lblExpiryExpired.Text = expired.ToString();
            _lblExpiryExpired.ForeColor = expired > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }
        if (_lblExpiryNear is not null) _lblExpiryNear.Text = near.ToString();
        if (_lblExpiryNone is not null) _lblExpiryNone.Text = none.ToString();
    }

    /// <summary>设置「WmsStockColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupWmsStockColumns()
    {
        _wmsStockGrid.AutoGenerateColumns = false;
        _wmsStockGrid.Columns.Clear();
        _wmsStockGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 13));
        _wmsStockGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 21));
        _wmsStockGrid.Columns.Add(MakeTextCol("Spec", "规格", true, 11));
        _wmsStockGrid.Columns.Add(MakeTextCol("Unit", "单位", true, 6));
        _wmsStockGrid.Columns.Add(MakeTextCol("LotNo", "批次", true, 14));
        _wmsStockGrid.Columns.Add(MakeTextCol("Location", "库位", true, 12));
        _wmsStockGrid.Columns.Add(MakeTextCol("MesQtyText", "MES 库存", true, 9));
        _wmsStockGrid.Columns.Add(MakeTextCol("ErpQtyText", "ERP 库存", true, 9));
        _wmsStockGrid.Columns.Add(MakeTextCol("DiffText", "差异（MES−ERP）", true, 13));
        _wmsStockGrid.Columns.Add(MakeTextCol("Status", "对账状态", true, 10));
        var updCol = MakeTextCol("MesLastUpdate", "最后更新时间", true, 13);
        updCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _wmsStockGrid.Columns.Add(updCol);

        // 先解除再挂接着色事件（同上，防止重复着色）
        _wmsStockGrid.CellFormatting -= WmsStockGrid_CellFormatting;
        _wmsStockGrid.CellFormatting += WmsStockGrid_CellFormatting;
    }

    /// <summary>库存对账着色：差异=红、ERP 无此键=橙、MES 无此键（ERP 独有）=紫、一致=绿。</summary>
    private void WmsStockGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_wmsStockGrid.Rows[e.RowIndex].DataBoundItem is not WmsStockCompareRow row) return;

        var col = _wmsStockGrid.Columns[e.ColumnIndex].Name;
        if (col != "ErpQtyText" && col != "DiffText" && col != "Status") return;

        e.CellStyle.ForeColor = row.Status switch
        {
            StockCompareStatus.Mismatch => Color.FromArgb(192, 0, 0),    // 差异：红
            StockCompareStatus.MesOnly => Color.FromArgb(230, 126, 34),  // ERP 无此键：橙
            StockCompareStatus.ErpOnly => Color.FromArgb(142, 68, 173),  // MES 无此键：紫
            _ => Color.FromArgb(39, 174, 96)                             // 一致：绿
        };
    }

    /// <summary>查询并加载「WmsStock」数据到界面（含网格绑定与汇总刷新）。</summary>
    private void DoQueryWmsStock()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_wmsStockGrid is null) return;
        var kw = _txtWmsStockKeyword.Text.Trim();
        var filter = string.IsNullOrEmpty(kw) ? null : kw;

        // 库存台账 = MES 台账 ↔ ERP 库存 逐键对账（物料编码 + 批次 + 库位）
        var list = MesDatabase.GetWmsStockCompare(filter);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _wmsStockGrid.DataSource = null;
        _wmsStockGrid.DataSource = list;

        var sum = MesDatabase.GetWmsStockCompareSummary(filter);
        _lblWmsStockResult.Text =
            $"共查询到 {list.Count} 行库存（数量差异 {sum.MismatchCount} 行 / ERP 无记录 {sum.MesOnlyCount} 行）";

        if (_lblWmsStockSummary is not null)
        {
            var kinds = list.Select(s => s.MaterialCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            static string Sign(double v) => Math.Abs(v) < 1e-9 ? "0" : (v > 0 ? $"+{v:0.##}" : $"{v:0.##}");

            _lblWmsStockSummary.Text =
                $"物料种类数：{kinds}    MES 合计：{sum.MesTotal:0.##}    ERP 合计：{sum.ErpTotal:0.##}    "
                + $"净差额：{Sign(sum.NetDiffTotal)}    "
                + $"（账面差异 {Sign(sum.MatchedDiffTotal)}｜MES 独有 {sum.MesOnlyQty:0.##}｜ERP 独有 {sum.ErpOnlyQty:0.##}）    "
                + $"差异行 {sum.MismatchCount} 行";
            _lblWmsStockSummary.ForeColor = sum.MismatchCount + sum.MesOnlyCount + sum.ErpOnlyCount > 0
                ? Color.FromArgb(192, 0, 0)
                : Color.FromArgb(15, 76, 129);
        }
    }


}
