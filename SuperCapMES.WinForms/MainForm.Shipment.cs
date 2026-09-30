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
/// MainForm 分部类（按功能域拆分，第 14 部分）：出货与召回页（出货绑定 / 新建出库单 / 召回反查与一键冻结）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Shipment」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 出货与召回（顶层第 14 个页签） =================

    /// <summary>
    /// 出货单列表展示行：<see cref="WmsOrder"/> 本身不含「已绑定」计数，
    /// 故用轻量视图行承载「已绑定 / 应出」对照，便于一眼看出绑定是否齐全。
    /// </summary>
    private sealed class ShipOrderRow
    {
        public string OrderNo { get; init; } = "";
        public string Status { get; init; } = "";
        public string Partner { get; init; } = "";
        public string RefNo { get; init; } = "";
        public int LineCount { get; init; }
        public double TotalQty { get; init; }
        public int BoundCount { get; init; }
        public DateTime CreateTime { get; init; }
        public DateTime? PostTime { get; init; }
    }

    /// <summary>出货与召回页：子页1=出货绑定（物流出库单 ↔ 出货电芯 SN）；子页2=召回反查（影响范围 + 一键冻结）。</summary>
    private TabPage BuildShipmentTab()
    {
        var page = new TabPage("出货与召回");
        _shipSubTabs = new TabControl { Dock = DockStyle.Fill };
        _shipSubTabs.TabPages.Add(BuildShipmentBindSubTab());
        _shipSubTabs.TabPages.Add(BuildNewShipmentSubTab());
        _shipSubTabs.TabPages.Add(BuildRecallSubTab());
        page.Controls.Add(_shipSubTabs);
        page.Enter -= ShipPage_Enter;
        page.Enter += ShipPage_Enter;   // 顶层兜底：进入本页时确保出货绑定子页已初始化
        return page;
    }

    // ---------- 子页1：出货绑定 ----------

    /// <summary>构建「ShipmentBindSub」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildShipmentBindSubTab()
    {
        var page = new TabPage("出货绑定");
        page.Padding = new Padding(6);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 226,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("物流出库单（选中一行 → 下方查看/维护该单绑定的出货电芯）", out _shipOrderGrid));
        split.Panel2.Controls.Add(MakeGridGroup("已绑定电芯（一芯一单：同一电芯同一时间只能在一个有效出货单中）", out _shipItemGrid));

        page.Controls.Add(split);
        page.Controls.Add(BuildShipActionBar());
        page.Controls.Add(BuildShipQueryBar());

        SetupShipOrderColumns();
        SetupShipItemColumns();
        DoQueryShipOrders();

        // 切到本子页时兜底初始化（补选首行 / 加载明细）：此时控件已挂载、数据绑定已物化
        page.Enter -= ShipBindTab_Enter;
        page.Enter += ShipBindTab_Enter;
        return page;
    }

    /// <summary>构建「Ship」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildShipQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboShipStatusFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 96, Margin = new Padding(4, 4, 0, 0) };
        _cboShipStatusFilter.Items.Add("全部");
        _cboShipStatusFilter.Items.AddRange(WmsOrderStatus.All);
        _cboShipStatusFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboShipStatusFilter);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtShipKeyword = new TextBox { Width = 200, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtShipKeyword);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryShipOrders()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _cboShipStatusFilter.SelectedIndex = 0;
            _txtShipKeyword.Text = "";
            DoQueryShipOrders();
        }));
        bar.Controls.Add(new Label
        {
            Text = "提示：出货前先绑定电芯，召回时才能反查到客户与流向",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(16, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>构建「Ship」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildShipActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnShipBind = MakeToolbarButton("绑定电芯…", BtnShipBind);
        _btnShipUnbind = MakeToolbarButton("解除绑定", BtnShipUnbind);
        bar.Controls.Add(_btnShipBind);
        bar.Controls.Add(_btnShipUnbind);
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => LoadShipItems()));

        _lblShipBound = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.FromArgb(40, 50, 60),
            Margin = new Padding(16, 10, 0, 0)
        };
        bar.Controls.Add(_lblShipBound);
        return bar;
    }

    /// <summary>设置「ShipOrderColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupShipOrderColumns()
    {
        _shipOrderGrid.AutoGenerateColumns = false;
        _shipOrderGrid.Columns.Clear();
        _shipOrderGrid.Columns.Add(MakeTextCol("OrderNo", "出货单号", true, 14));
        _shipOrderGrid.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _shipOrderGrid.Columns.Add(MakeTextCol("Partner", "客户", true, 12));
        _shipOrderGrid.Columns.Add(MakeTextCol("RefNo", "物流单号", true, 13));
        _shipOrderGrid.Columns.Add(MakeTextCol("LineCount", "明细行数", true, 8));
        _shipOrderGrid.Columns.Add(MakeTextCol("TotalQty", "应出数量", true, 9));
        _shipOrderGrid.Columns.Add(MakeTextCol("BoundCount", "已绑定", true, 8));
        _shipOrderGrid.Columns.Add(MakeTextCol("CreateTime", "创建时间", true, 13));
        _shipOrderGrid.Columns.Add(MakeTextCol("PostTime", "过账时间", true, 13));

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _shipOrderGrid.SelectionChanged -= ShipOrderGrid_SelectionChanged;
        _shipOrderGrid.SelectionChanged += ShipOrderGrid_SelectionChanged;
        // 先解除再挂接着色事件（同上，防止重复着色）
        _shipOrderGrid.CellFormatting -= ShipOrderGrid_CellFormatting;
        _shipOrderGrid.CellFormatting += ShipOrderGrid_CellFormatting;
    }

    /// <summary>设置「ShipItemColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupShipItemColumns()
    {
        _shipItemGrid.AutoGenerateColumns = false;
        _shipItemGrid.Columns.Clear();
        _shipItemGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 16));
        _shipItemGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 12));
        _shipItemGrid.Columns.Add(MakeTextCol("CellStatus", "出货时状态", true, 9));
        _shipItemGrid.Columns.Add(MakeTextCol("ElectrodeLot", "极片批号", true, 13));
        _shipItemGrid.Columns.Add(MakeTextCol("ElectrolyteLot", "电解液批号", true, 13));
        _shipItemGrid.Columns.Add(MakeTextCol("ModuleSn", "所属模组", true, 12));
        _shipItemGrid.Columns.Add(MakeTextCol("WarehouseLot", "成品批次", true, 13));
        _shipItemGrid.Columns.Add(MakeTextCol("BindTime", "绑定时间", true, 13));
        _shipItemGrid.Columns.Add(MakeTextCol("BindOperator", "绑定人", true, 8));
        _shipItemGrid.Columns.Add(MakeTextCol("Status", "状态", true, 7));
    }

    /// <summary>「ShipOrderGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void ShipOrderGrid_SelectionChanged(object? sender, EventArgs e) => LoadShipItems();

    /// <summary>「ShipOrderGrid」网格单元格着色：按状态/结果区分颜色（差异红、异常橙、正常绿等）。</summary>
    private void ShipOrderGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_shipOrderGrid.Rows[e.RowIndex].DataBoundItem is not ShipOrderRow row) return;

        switch (_shipOrderGrid.Columns[e.ColumnIndex].Name)
        {
            case "Status":
                if (row.Status == WmsOrderStatus.Posted) e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);
                else if (row.Status == WmsOrderStatus.Pending) e.CellStyle.ForeColor = Color.FromArgb(230, 126, 34);
                break;
            case "BoundCount":
                // 已绑定数 < 应出数量 → 尚未绑完（橙色提示）；绑完显示绿色
                e.CellStyle.ForeColor = row.BoundCount < row.TotalQty
                    ? Color.FromArgb(230, 126, 34)
                    : Color.FromArgb(39, 174, 96);
                break;
        }
    }

    /// <summary>查询物流出库单（含已绑定计数）；选中行联动下方绑定清单。</summary>
    private void DoQueryShipOrders()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_shipOrderGrid is null) return;
        var status = _cboShipStatusFilter is not null && _cboShipStatusFilter.SelectedIndex > 0
            ? _cboShipStatusFilter.SelectedItem?.ToString()
            : null;
        var kw = _txtShipKeyword?.Text.Trim() ?? "";
        var orders = MesDatabase.QueryShipmentOrders(status, kw.Length == 0 ? null : kw);
        var rows = orders.Select(o => new ShipOrderRow
        {
            OrderNo = o.OrderNo,
            Status = o.Status,
            Partner = o.Partner,
            RefNo = o.RefNo,
            LineCount = o.LineCount,
            TotalQty = o.TotalQty,
            BoundCount = MesDatabase.GetShipmentBoundCount(o.OrderNo),
            CreateTime = o.CreateTime,
            PostTime = o.PostTime
        }).ToList();

        _shipOrderGrid.DataSource = rows;
        TrySelectFirstShipOrder();   // 构造期绑定尚未物化时自动跳过（由页签 Enter 兜底）
        LoadShipItems();
    }

    /// <summary>
    /// 选中出货单列表首行：以**网格实测行数**为守卫。
    /// 注意：构造期控件尚未挂载时，<c>DataSource</c> 赋值不会立即物化行，<c>Rows</c> 仍为空，
    /// 此时直接跳过，待页签 Enter（控件已挂载、绑定已物化）时由 <see cref="EnsureShipmentReady"/> 补选，
    /// 避免 <c>Rows[0]</c> 抛 ArgumentOutOfRangeException。
    /// </summary>
    private void TrySelectFirstShipOrder()
    {
        if (_shipOrderGrid is null || _shipOrderGrid.Rows.Count == 0) return;
        _shipOrderGrid.ClearSelection();
        _shipOrderGrid.Rows[0].Selected = true;
    }

    /// <summary>「出货绑定」首次进入时执行：补查询 / 补选首行 / 加载明细（幂等，每次进入都安全）。</summary>
    private void EnsureShipmentReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_shipOrderGrid is null) return;
        if (_shipOrderGrid.Rows.Count == 0) DoQueryShipOrders();
        if (_shipOrderGrid.SelectedRows.Count == 0) TrySelectFirstShipOrder();
        LoadShipItems();
    }

    /// <summary>进入「ShipBindTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void ShipBindTab_Enter(object? sender, EventArgs e) => EnsureShipmentReady();

    /// <summary>进入「ShipPage」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void ShipPage_Enter(object? sender, EventArgs e) => EnsureShipmentReady();

    /// <summary>在网格中选中「edShipOrder」对应的行（联动加载明细）。</summary>
    private WmsOrder? SelectedShipOrder()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_shipOrderGrid.SelectedRows.Count == 0) return null;
        if (_shipOrderGrid.SelectedRows[0].DataBoundItem is not ShipOrderRow row) return null;
        return MesDatabase.FindWmsOrder(row.OrderNo);
    }

    /// <summary>加载选中出货单的绑定电芯清单，并显示「已绑定 / 应出」对照。</summary>
    private void LoadShipItems()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_shipItemGrid is null) return;
        var order = SelectedShipOrder();
        if (order is null)
        {
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _shipItemGrid.DataSource = null;
            if (_lblShipBound is not null) _lblShipBound.Text = "请选择一张物流出库单。";
            return;
        }

        var items = MesDatabase.GetShipmentItems(order.OrderNo);
        _shipItemGrid.DataSource = items;

        var planned = MesDatabase.GetWmsOrderLines(order.OrderNo).Sum(l => l.Quantity);
        if (_lblShipBound is not null)
        {
            _lblShipBound.Text = $"已绑定 {items.Count} 只 / 应出 {planned:0.##}（{order.OrderNo}）";
            _lblShipBound.ForeColor = items.Count < planned
                ? Color.FromArgb(230, 126, 34)
                : Color.FromArgb(39, 174, 96);
        }
    }

    /// <summary>绑定电芯：打开候选选择窗体（可勾选/按型号批次筛选），确认后刷新。</summary>
    private void BtnShipBind(object? sender, EventArgs e)
    {
        var order = SelectedShipOrder();
        if (order is null) { Warn("请先选择一张物流出库单。"); return; }
        if (order.Type != WmsOrderTypes.LogisticsOutbound) { Warn("仅「物流出库」类型单据可绑定出货电芯。"); return; }

        using var form = new ShipmentBindForm(order.OrderNo);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            DoQueryShipOrders();
            SelectShipOrder(order.OrderNo);
        }
    }

    /// <summary>处理「ShipUnbind」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnShipUnbind(object? sender, EventArgs e)
    {
        var order = SelectedShipOrder();
        if (order is null) { Warn("请先选择一张物流出库单。"); return; }
        if (_shipItemGrid.SelectedRows.Count == 0) { Warn("请先在下方选择要解绑的电芯。"); return; }
        if (_shipItemGrid.SelectedRows[0].DataBoundItem is not WmsShipmentItem item) { Warn("请先选择要解绑的电芯。"); return; }

        if (MessageBox.Show($"解除电芯 {item.CellSn} 与出货单 {order.OrderNo} 的绑定？\n解绑后该电芯可重新绑定到其他出货单。",
                "解除绑定", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var (ok, msg) = MesDatabase.UnbindShipmentCell(order.OrderNo, item.CellSn, "admin", "手工解绑");
        MessageBox.Show(msg, ok ? "解除成功" : "解除失败", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryShipOrders();
        SelectShipOrder(order.OrderNo);
    }

    /// <summary>在网格中选中「ShipOrder」对应的行（联动加载明细）。</summary>
    private void SelectShipOrder(string orderNo)
    {
        foreach (DataGridViewRow r in _shipOrderGrid.Rows)
        {
            if (r.DataBoundItem is ShipOrderRow row && string.Equals(row.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase))
            {
                _shipOrderGrid.ClearSelection();
                r.Selected = true;
                return;
            }
        }
    }

    // ---------- 子页2：新建出库单（物流出库） ----------

    /// <summary>新建出库单-候选电芯行（含勾选状态）。</summary>
    private sealed class NewShipCellRow
    {
        public bool Chk { get; set; }
        public string Sn { get; init; } = "";
        public string ProductModel { get; init; } = "";
        public string Status { get; init; } = "";
        public string ElectrodeLot { get; init; } = "";
        public DateTime CreateTime { get; init; }
    }

    /// <summary>新建出库单-明细录入行（可编辑；数量以文本录入，提交时解析校验）。</summary>
    private sealed class NewShipLineRow
    {
        public string MaterialCode { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public string Spec { get; set; } = "";
        public string Unit { get; set; } = "只";
        public string LotNo { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Location { get; set; } = WmsLocations.FinishedB;
    }

    /// <summary>
    /// 新建出库单子页：① 勾选可出货电芯（仅「合格 / 已入库」）→ ② 生成/编辑出库明细 →
    /// ③ 一步创建「物流出库」单（待过账）并绑定所选电芯。
    /// </summary>
    private TabPage BuildNewShipmentSubTab()
    {
        var page = new TabPage("新建出库单");
        page.Padding = new Padding(6);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 290,
            BackColor = SystemColors.Control
        };

        // ---- 上：出货电芯候选（仅合格 / 已入库）----
        var cellGroup = new GroupBox
        {
            Text = "① 选择出货电芯（只有「合格 / 已入库」的电芯可出货；在制 / 化成中 / 检测中 / 不良不会出现在候选里）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        _newShipCellGrid = MakeEditableGrid();
        SetupNewShipCellColumns();
        cellGroup.Controls.Add(_newShipCellGrid);
        cellGroup.Controls.Add(BuildNewShipCellBar());
        split.Panel1.Controls.Add(cellGroup);

        // ---- 下：出库明细 ----
        var lineGroup = new GroupBox
        {
            Text = "② 出库明细（点「生成明细」按已选电芯按型号汇总；也可手工增删行）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        _newShipLineGrid = MakeEditableGrid();
        _newShipLineGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        SetupNewShipLineColumns();
        lineGroup.Controls.Add(_newShipLineGrid);
        lineGroup.Controls.Add(BuildNewShipLineBar());
        split.Panel2.Controls.Add(lineGroup);

        page.Controls.Add(split);
        page.Controls.Add(BuildNewShipActionBar());
        page.Controls.Add(BuildNewShipHeaderBar());

        page.Enter -= NewShipTab_Enter;
        page.Enter += NewShipTab_Enter;   // 构造期数据绑定未物化，候选在页签 Enter 时加载
        return page;
    }

    /// <summary>可编辑网格（勾选列 / 明细录入共用；只读展示仍用 MakeGrid）。</summary>
    private static DataGridView MakeEditableGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15, 76, 129),
                ForeColor = Color.White,
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            ColumnHeadersHeight = 30
        };
        g.RowTemplate.Height = 26;
        return g;
    }

    /// <summary>构建「NewShipHeaderBar」界面区块。</summary>
    private Control BuildNewShipHeaderBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(6, 8, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "客户 *：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _txtNewShipCustomer = new TextBox { Width = 150, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtNewShipCustomer);

        bar.Controls.Add(new Label { Text = "物流单号 *：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtNewShipRefNo = new TextBox { Width = 150, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtNewShipRefNo);

        bar.Controls.Add(new Label { Text = "经手人：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtNewShipOperator = new TextBox { Width = 90, Text = "admin", Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtNewShipOperator);

        bar.Controls.Add(new Label { Text = "备注：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtNewShipRemark = new TextBox { Width = 220, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtNewShipRemark);
        return bar;
    }

    /// <summary>构建「NewShipCellBar」界面区块。</summary>
    private Control BuildNewShipCellBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(2, 4, 2, 0), WrapContents = false };
        bar.Controls.Add(MakeToolbarButton("刷新候选", (_, _) => LoadNewShipCells()));
        bar.Controls.Add(MakeToolbarButton("全选", (_, _) => SetNewShipCellsChecked(true)));
        bar.Controls.Add(MakeToolbarButton("取消全选", (_, _) => SetNewShipCellsChecked(false)));
        bar.Controls.Add(MakeToolbarButton("生成明细", (_, _) => GenerateLinesFromSelectedCells()));

        _lblNewShipSelected = new Label
        {
            Text = "已选 0 只",
            AutoSize = true,
            Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
            Margin = new Padding(12, 10, 0, 0)
        };
        bar.Controls.Add(_lblNewShipSelected);

        _lblNewShipCandidate = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(16, 10, 0, 0)
        };
        bar.Controls.Add(_lblNewShipCandidate);
        return bar;
    }

    /// <summary>构建「NewShipLineBar」界面区块。</summary>
    private Control BuildNewShipLineBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(2, 4, 2, 0), WrapContents = false };
        bar.Controls.Add(MakeToolbarButton("添加明细行", (_, _) => AddNewShipLine()));
        bar.Controls.Add(MakeToolbarButton("删除选中行", (_, _) => RemoveNewShipLines()));
        bar.Controls.Add(new Label
        {
            Text = "数量填数字后回车；库位默认「成品库-B区」",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>构建「NewShip」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildNewShipActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnNewShipCreate = MakeToolbarButton("创建出库单并绑定", BtnNewShipCreate);
        bar.Controls.Add(_btnNewShipCreate);
        bar.Controls.Add(new Label
        {
            Text = "创建后单据状态为「待过账」；可在「出货绑定」子页查看，到「WMS 仓储」页过账出库",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>设置「NewShipCellColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupNewShipCellColumns()
    {
        _newShipCellGrid.Columns.Clear();
        _newShipCellGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Chk",
            HeaderText = "选择",
            DataPropertyName = "Chk",
            FillWeight = 6,
            FlatStyle = FlatStyle.Flat
        });
        _newShipCellGrid.Columns.Add(MakeTextCol("Sn", "电芯SN", true, 18));
        _newShipCellGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 12));
        _newShipCellGrid.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _newShipCellGrid.Columns.Add(MakeTextCol("ElectrodeLot", "极片批号", true, 14));
        _newShipCellGrid.Columns.Add(MakeTextCol("CreateTime", "生产时间", true, 14));

        _newShipCellGrid.CurrentCellDirtyStateChanged -= NewShipCellGrid_CurrentCellDirtyStateChanged;
        _newShipCellGrid.CurrentCellDirtyStateChanged += NewShipCellGrid_CurrentCellDirtyStateChanged;
        _newShipCellGrid.CellValueChanged -= NewShipCellGrid_CellValueChanged;
        _newShipCellGrid.CellValueChanged += NewShipCellGrid_CellValueChanged;
        _newShipCellGrid.DataError += (_, e) => e.ThrowException = false;
    }

    /// <summary>设置「NewShipLineColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupNewShipLineColumns()
    {
        _newShipLineGrid.Columns.Clear();
        _newShipLineGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", false, 14));
        _newShipLineGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", false, 20));
        _newShipLineGrid.Columns.Add(MakeTextCol("Spec", "规格", false, 12));
        _newShipLineGrid.Columns.Add(MakeTextCol("Unit", "单位", false, 7));
        _newShipLineGrid.Columns.Add(MakeTextCol("LotNo", "批次", false, 14));
        _newShipLineGrid.Columns.Add(MakeTextCol("Quantity", "数量", false, 8));
        _newShipLineGrid.Columns.Add(MakeTextCol("Location", "库位", false, 12));
    }

    // ---------- 新建出库单：交互逻辑 ----------

    /// <summary>进入「NewShipTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void NewShipTab_Enter(object? sender, EventArgs e) => EnsureNewShipmentReady();

    /// <summary>首次进入「新建出库单」子页时加载候选与空明细（构造期数据绑定未物化）。</summary>
    private void EnsureNewShipmentReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_newShipCellGrid is null) return;
        if (_newShipCellGrid.Rows.Count == 0) LoadNewShipCells();
        if (_newShipLineGrid.DataSource is null) _newShipLineGrid.DataSource = new List<NewShipLineRow>();
    }

    /// <summary>加载候选电芯：只有「合格 / 已入库」且未绑定出货的电芯（仓储层已过滤，UI 只负责展示）。</summary>
    private void LoadNewShipCells()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_newShipCellGrid is null) return;
        _loadingNewShipCells = true;
        try
        {
            var cells = MesDatabase.GetShipmentCandidates("", null, null, null);
            var rows = cells.Select(c => new NewShipCellRow
            {
                Chk = false,
                Sn = c.Sn,
                ProductModel = c.ProductModel,
                Status = c.Status,
                ElectrodeLot = c.ElectrodeLot,
                CreateTime = c.CreateTime
            }).ToList();
            _newShipCellGrid.DataSource = rows;
            if (_lblNewShipCandidate is not null)
                _lblNewShipCandidate.Text = $"候选 {rows.Count} 只（合格 / 已入库，且未绑定出货）";
        }
        finally
        {
            _loadingNewShipCells = false;
        }
        UpdateNewShipSelected();
    }

    /// <summary>「NewShipCellGrid」单元格脏状态变化 → 立即提交勾选/编辑（保证计数与判定实时刷新）。</summary>
    private void NewShipCellGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_newShipCellGrid.IsCurrentCellDirty && _newShipCellGrid.CurrentCell is DataGridViewCheckBoxCell)
            _newShipCellGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    /// <summary>「NewShipCellGrid」单元格值变化 → 刷新已选计数等派生显示。</summary>
    private void NewShipCellGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loadingNewShipCells) return;
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_newShipCellGrid.Columns[e.ColumnIndex].Name == "Chk") UpdateNewShipSelected();
    }

    /// <summary>SetNewShipCellsChecked：（说明待补充）</summary>
    private void SetNewShipCellsChecked(bool value)
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_newShipCellGrid is null) return;
        _loadingNewShipCells = true;
        try
        {
            foreach (DataGridViewRow r in _newShipCellGrid.Rows)
            {
                if (r.IsNewRow) continue;
                r.Cells["Chk"].Value = value;
            }
            _newShipCellGrid.RefreshEdit();
        }
        finally
        {
            _loadingNewShipCells = false;
        }
        UpdateNewShipSelected();
    }

    /// <summary>List：（说明待补充）</summary>
    private List<NewShipCellRow> SelectedNewShipCells()
    {
        var list = new List<NewShipCellRow>();
        if (_newShipCellGrid is null) return list;
        foreach (DataGridViewRow r in _newShipCellGrid.Rows)
        {
            if (r.IsNewRow) continue;
            if (r.Cells["Chk"].Value is bool b && b && r.DataBoundItem is NewShipCellRow row) list.Add(row);
        }
        return list;
    }

    /// <summary>根据当前选中项更新「NewShipSelected」的可用状态/提示。</summary>
    private void UpdateNewShipSelected()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_lblNewShipSelected is null) return;
        var n = SelectedNewShipCells().Count;
        _lblNewShipSelected.Text = $"已选 {n} 只";
        _lblNewShipSelected.ForeColor = n > 0 ? Color.FromArgb(15, 76, 129) : Color.FromArgb(40, 50, 60);
    }

    /// <summary>按已选电芯生成明细：按型号分组汇总数量；物料/批次/库位取成品主数据与成品库存批次。</summary>
    private void GenerateLinesFromSelectedCells()
    {
        var selected = SelectedNewShipCells();
        if (selected.Count == 0) { Warn("请先勾选要出货的电芯。"); return; }

        var lines = new List<NewShipLineRow>();
        foreach (var grp in selected.GroupBy(c => c.ProductModel, StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var mat = MesDatabase.WmsMaterials.FirstOrDefault(m =>
                string.Equals(m.Category, WmsCategories.Finished, StringComparison.OrdinalIgnoreCase)
                && m.MaterialName.Contains(grp.Key, StringComparison.OrdinalIgnoreCase));
            var lot = mat is null
                ? ""
                : MesDatabase.QueryWmsStock(mat.Code).FirstOrDefault()?.LotNo ?? "";
            lines.Add(new NewShipLineRow
            {
                MaterialCode = mat?.Code ?? "",
                MaterialName = mat?.MaterialName ?? grp.Key,
                Spec = mat?.Spec ?? "",
                Unit = string.IsNullOrWhiteSpace(mat?.Unit) ? "只" : mat!.Unit,
                LotNo = lot,
                Quantity = grp.Count().ToString(),
                Location = WmsLocations.FinishedB
            });
        }

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _newShipLineGrid.DataSource = null;
        _newShipLineGrid.DataSource = lines;
        MessageBox.Show($"已按型号生成 {lines.Count} 行明细（合计 {selected.Count} 只）。\n可手工修改数量/批次后再创建。",
            "生成明细", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>AddNewShipLine：（说明待补充）</summary>
    private void AddNewShipLine()
    {
        var rows = _newShipLineGrid.DataSource as List<NewShipLineRow> ?? new List<NewShipLineRow>();
        rows.Add(new NewShipLineRow());
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _newShipLineGrid.DataSource = null;
        _newShipLineGrid.DataSource = rows;
    }

    /// <summary>RemoveNewShipLines：（说明待补充）</summary>
    private void RemoveNewShipLines()
    {
        if (_newShipLineGrid.DataSource is not List<NewShipLineRow> rows) return;
        var idx = new HashSet<int>();
        foreach (DataGridViewRow r in _newShipLineGrid.SelectedRows) idx.Add(r.Index);
        foreach (DataGridViewCell c in _newShipLineGrid.SelectedCells) idx.Add(c.RowIndex);
        if (idx.Count == 0) { Warn("请先选择要删除的明细行。"); return; }

        foreach (var i in idx.OrderByDescending(x => x))
            if (i >= 0 && i < rows.Count) rows.RemoveAt(i);

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _newShipLineGrid.DataSource = null;
        _newShipLineGrid.DataSource = rows;
    }

    /// <summary>创建「物流出库」单（待过账）并绑定所选电芯——出库电芯必须为合格 / 已入库状态。</summary>
    private void BtnNewShipCreate(object? sender, EventArgs e)
    {
        // 提交编辑中的单元格：确保最新输入落入数据对象后再读取
        if (_newShipLineGrid.IsCurrentCellInEditMode) _newShipLineGrid.EndEdit();
        _newShipLineGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        var customer = _txtNewShipCustomer.Text.Trim();
        var refNo = _txtNewShipRefNo.Text.Trim();
        var op = _txtNewShipOperator.Text.Trim();
        var remark = _txtNewShipRemark.Text.Trim();

        if (customer.Length == 0) { Warn("请填写客户。"); return; }
        if (refNo.Length == 0) { Warn("物流出库必须填写物流单号。"); return; }

        var selected = SelectedNewShipCells();
        if (selected.Count == 0) { Warn("请至少勾选 1 只出货电芯（只有「合格 / 已入库」的电芯可出货）。"); return; }

        var lines = new List<WmsOrderLine>();
        foreach (DataGridViewRow r in _newShipLineGrid.Rows)
        {
            if (r.IsNewRow || r.DataBoundItem is not NewShipLineRow row) continue;
            if (string.IsNullOrWhiteSpace(row.MaterialCode) && string.IsNullOrWhiteSpace(row.MaterialName)) continue;
            if (!double.TryParse(row.Quantity?.Trim(), out var qty) || qty <= 0)
            {
                Warn($"明细「{row.MaterialName}」的数量无效，请填写大于 0 的数字。");
                return;
            }
            lines.Add(new WmsOrderLine
            {
                MaterialCode = row.MaterialCode.Trim(),
                MaterialName = row.MaterialName.Trim(),
                Spec = row.Spec.Trim(),
                Unit = string.IsNullOrWhiteSpace(row.Unit) ? "只" : row.Unit.Trim(),
                LotNo = row.LotNo.Trim(),
                Quantity = qty,
                Location = string.IsNullOrWhiteSpace(row.Location) ? WmsLocations.FinishedB : row.Location.Trim()
            });
        }
        if (lines.Count == 0) { Warn("请至少填写 1 行出库明细（可点「生成明细」按已选电芯自动汇总）。"); return; }

        var (ok, msg, orderNo) = MesDatabase.CreateWmsOrder(
            WmsOrderTypes.LogisticsOutbound, customer, refNo, "", op.Length == 0 ? "admin" : op, remark, lines);
        if (!ok) { Warn(msg); return; }

        var (_, bindMsg, bound, skipped, rejected) = MesDatabase.BindShipmentCells(
            orderNo, selected.Select(c => c.Sn).ToList(), op.Length == 0 ? "admin" : op);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"出库单创建成功：{orderNo}");
        sb.AppendLine($"客户 {customer} ｜ 物流单号 {refNo} ｜ 明细 {lines.Count} 行 / 合计 {lines.Sum(l => l.Quantity):0.##}");
        sb.AppendLine($"电芯绑定：成功 {bound} 只" + (bound < selected.Count ? $"（未绑定 {selected.Count - bound} 只）" : ""));
        if (skipped.Count > 0)
            sb.AppendLine($"  · 已在其他出货单 {skipped.Count} 只：{string.Join("、", skipped.Take(5))}{(skipped.Count > 5 ? " 等" : "")}");
        if (rejected.Count > 0)
            sb.AppendLine($"  · 状态不可出货 {rejected.Count} 只：{string.Join("、", rejected.Take(5))}{(rejected.Count > 5 ? " 等" : "")}");
        if (bound == 0 && bindMsg.Length > 0) sb.AppendLine($"(绑定提示：{bindMsg})");
        MessageBox.Show(sb.ToString(), "创建出库单", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // 刷新「出货绑定」子页并选中新单；重置本页表单
        DoQueryShipOrders();
        SelectShipOrder(orderNo);
        _txtNewShipCustomer.Text = "";
        _txtNewShipRefNo.Text = "";
        _txtNewShipRemark.Text = "";
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _newShipLineGrid.DataSource = null;
        LoadNewShipCells();
    }

    // ---------- 子页3：召回反查 ----------

    /// <summary>构建「RecallSub」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildRecallSubTab()
    {
        var page = new TabPage("召回反查");
        page.Padding = new Padding(6);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 130,
            BackColor = SystemColors.Control
        };

        var sumPanel = new GroupBox
        {
            Text = "影响范围汇总（按下方条件反查后更新）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        _lblRecallSummary = new Label
        {
            Dock = DockStyle.Fill,
            Text = "请选择反查方式、输入查询值后点「反查」。",
            Font = new Font("微软雅黑", 10F),
            ForeColor = Color.FromArgb(40, 50, 60),
            Padding = new Padding(8, 6, 6, 6)
        };
        sumPanel.Controls.Add(_lblRecallSummary);
        split.Panel1.Controls.Add(sumPanel);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(MakeRecallGridTab("出货记录", out _recallShipGrid));
        tabs.TabPages.Add(MakeRecallGridTab("受影响电芯", out _recallCellGrid));
        tabs.TabPages.Add(MakeRecallGridTab("受影响材料批次", out _recallLotGrid));
        split.Panel2.Controls.Add(tabs);

        page.Controls.Add(split);
        page.Controls.Add(BuildRecallActionBar());
        page.Controls.Add(BuildRecallQueryBar());

        SetupRecallColumns();
        return page;
    }

    /// <summary>创建「RecallGridTab」控件/样式（统一样式的工厂方法）。</summary>
    private static TabPage MakeRecallGridTab(string title, out DataGridView grid)
    {
        var page = new TabPage(title);
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup(title, out grid));
        return page;
    }

    /// <summary>构建「Recall」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildRecallQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "反查方式：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboRecallMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(4, 4, 0, 0) };
        _cboRecallMode.Items.AddRange(RecallMode.All.Cast<object>().ToArray());
        _cboRecallMode.SelectedIndex = 0;
        bar.Controls.Add(_cboRecallMode);

        bar.Controls.Add(new Label { Text = "查询值：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtRecallKeyword = new TextBox { Width = 230, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtRecallKeyword);

        bar.Controls.Add(MakeToolbarButton("反查", (_, _) => DoRecallQuery()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            _txtRecallKeyword.Text = "";
            _lastRecall = null;
            _lblRecallSummary.Text = "请选择反查方式、输入查询值后点「反查」。";
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _recallShipGrid.DataSource = null;
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _recallCellGrid.DataSource = null;
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _recallLotGrid.DataSource = null;
        }));

        bar.Controls.Add(new Label
        {
            Text = "示例：材料批次 PC-260812-L1-001 ｜ 客户 华能储能 ｜ 电芯 SN SC-A260910-L1-00008",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(16, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>构建「Recall」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildRecallActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnRecallFreeze = MakeToolbarButton("一键冻结影响电芯", BtnRecallFreeze);
        _btnRecallCopy = MakeToolbarButton("复制清单", BtnRecallCopy);
        _btnRecallExport = MakeToolbarButton("导出报告", BtnRecallExport);
        bar.Controls.Add(_btnRecallFreeze);
        bar.Controls.Add(_btnRecallCopy);
        bar.Controls.Add(_btnRecallExport);
        bar.Controls.Add(new Label
        {
            Text = "口径：仅冻结「未出货且未保留」的电芯；已出货电芯需客户侧处置（见「出货记录」）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>设置「RecallColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupRecallColumns()
    {
        _recallShipGrid.AutoGenerateColumns = false;
        _recallShipGrid.Columns.Clear();
        _recallShipGrid.Columns.Add(MakeTextCol("OrderNo", "出货单号", true, 13));
        _recallShipGrid.Columns.Add(MakeTextCol("Customer", "客户", true, 11));
        _recallShipGrid.Columns.Add(MakeTextCol("ShipTime", "出货时间", true, 12));
        _recallShipGrid.Columns.Add(MakeTextCol("RefNo", "物流单号", true, 12));
        _recallShipGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 15));
        _recallShipGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 11));
        _recallShipGrid.Columns.Add(MakeTextCol("ElectrodeLot", "极片批号", true, 12));
        _recallShipGrid.Columns.Add(MakeTextCol("ElectrolyteLot", "电解液批号", true, 12));
        _recallShipGrid.Columns.Add(MakeTextCol("WarehouseLot", "成品批次", true, 12));

        _recallCellGrid.AutoGenerateColumns = false;
        _recallCellGrid.Columns.Clear();
        _recallCellGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 16));
        _recallCellGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 11));
        _recallCellGrid.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _recallCellGrid.Columns.Add(MakeTextCol("Shipped", "出货状态", true, 9));
        _recallCellGrid.Columns.Add(MakeTextCol("OrderNo", "出货单号", true, 13));
        _recallCellGrid.Columns.Add(MakeTextCol("Customer", "客户", true, 11));
        _recallCellGrid.Columns.Add(MakeTextCol("StockLocation", "在库库位", true, 11));
        _recallCellGrid.Columns.Add(MakeTextCol("Held", "已保留", true, 7));
        _recallCellGrid.Columns.Add(MakeTextCol("HolderNo", "保留单号", true, 13));
        // 先解除再挂接着色事件（同上，防止重复着色）
        _recallCellGrid.CellFormatting -= RecallCellGrid_CellFormatting;
        _recallCellGrid.CellFormatting += RecallCellGrid_CellFormatting;

        _recallLotGrid.AutoGenerateColumns = false;
        _recallLotGrid.Columns.Clear();
        _recallLotGrid.Columns.Add(MakeTextCol("LotNo", "批次号", true, 20));
        _recallLotGrid.Columns.Add(MakeTextCol("MaterialName", "材料名称", true, 30));
        _recallLotGrid.Columns.Add(MakeTextCol("CellCount", "使用电芯数", true, 12));
        _recallLotGrid.Columns.Add(MakeTextCol("ShipmentCount", "流向出货单数", true, 14));
    }

    /// <summary>「RecallCellGrid」网格单元格着色：按状态/结果区分颜色（差异红、异常橙、正常绿等）。</summary>
    private void RecallCellGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_recallCellGrid.Rows[e.RowIndex].DataBoundItem is not RecallCellRow row) return;

        switch (_recallCellGrid.Columns[e.ColumnIndex].Name)
        {
            case "Shipped":
                e.Value = row.Shipped ? "已出货" : "在库";
                e.FormattingApplied = true;
                e.CellStyle.ForeColor = row.Shipped
                    ? Color.FromArgb(15, 76, 129)    // 已出货：蓝（需客户侧处置）
                    : Color.FromArgb(39, 174, 96);   // 在库：绿（可冻结）
                break;
            case "Held":
                e.Value = row.Held ? "是" : "否";
                e.FormattingApplied = true;
                if (row.Held) e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
                break;
        }
    }

    /// <summary>执行召回反查：刷新影响范围汇总与三视图（出货记录 / 受影响电芯 / 受影响材料批次）。</summary>
    private void DoRecallQuery()
    {
        var mode = _cboRecallMode?.SelectedItem?.ToString() ?? RecallMode.CellSn;
        var kw = _txtRecallKeyword?.Text.Trim() ?? "";
        if (kw.Length == 0) { Warn("请输入查询值（电芯SN / 材料批次 / 出货单号 / 客户）。"); return; }

        var result = MesDatabase.RecallQuery(mode, kw);
        _lastRecall = result;

        _recallShipGrid.DataSource = result.Shipments;
        _recallCellGrid.DataSource = result.Cells;
        _recallLotGrid.DataSource = result.Lots;

        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_lblRecallSummary is null) return;
        var s = result.Summary;
        if (result.Cells.Count == 0)
        {
            _lblRecallSummary.Text = result.Message;
            _lblRecallSummary.ForeColor = Color.FromArgb(40, 50, 60);
            return;
        }
        _lblRecallSummary.Text =
            $"反查方式：{mode}「{kw}」 —— 涉及出货单 {s.ShipmentCount} 张 ｜ 客户 {s.CustomerCount} 家 ｜ 影响电芯 {s.CellCount} 只 ｜ 涉及材料批次 {s.LotCount} 个\n"
            + $"其中：已出货 {s.ShippedCellCount} 只（需客户侧处置）｜ 在库 {s.InStockCellCount} 只（其中可冻结 {s.FreezableCellCount} 只）｜ 已在保留中 {s.HeldCellCount} 只";
        _lblRecallSummary.ForeColor = s.FreezableCellCount > 0
            ? Color.FromArgb(192, 0, 0)
            : Color.FromArgb(40, 50, 60);
    }

    /// <summary>一键冻结：把「未出货且未保留」的影响电芯批量生成保留单（来源=召回冻结）。</summary>
    private void BtnRecallFreeze(object? sender, EventArgs e)
    {
        if (_lastRecall is null || _lastRecall.Cells.Count == 0) { Warn("请先执行反查。"); return; }

        var targets = _lastRecall.Cells
            .Where(c => !c.Shipped && !c.Held)
            .Select(c => c.CellSn)
            .ToList();
        if (targets.Count == 0)
        {
            Warn("当前反查结果中没有可冻结的电芯（均已出货或已在保留中）。");
            return;
        }

        if (MessageBox.Show($"将冻结 {targets.Count} 只「在库且未保留」的影响电芯？\n冻结后生成一张保留单（位置：待检区，来源：召回冻结）。",
                "一键冻结影响电芯", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var (ok, msg, holdNo, held, skipped) = MesDatabase.FreezeRecallCells(targets, "admin");
        MessageBox.Show(
            ok ? $"冻结完成：保留单 {holdNo}，冻结 {held} 只，跳过 {skipped} 只（已在保留中）。"
               : $"未冻结：{msg}",
            ok ? "冻结成功" : "冻结未执行",
            MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        DoRecallQuery();   // 重新反查，刷新「已保留」状态
    }

    /// <summary>把汇总与三视图拼成文本复制到剪贴板（便于贴进邮件/质量报告）。</summary>
    private void BtnRecallCopy(object? sender, EventArgs e)
    {
        if (_lastRecall is null) { Warn("请先执行反查。"); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【召回影响清单】");
        sb.AppendLine(_lblRecallSummary?.Text ?? "");
        sb.AppendLine();
        sb.AppendLine("—— 出货记录 ——");
        foreach (var r in _lastRecall.Shipments)
            sb.AppendLine($"{r.OrderNo}\t{r.Customer}\t{r.ShipTime:yyyy-MM-dd HH:mm}\t{r.RefNo}\t{r.CellSn}\t{r.ProductModel}\t{r.ElectrodeLot}\t{r.ElectrolyteLot}\t{r.WarehouseLot}");
        sb.AppendLine();
        sb.AppendLine("—— 受影响电芯 ——");
        foreach (var c in _lastRecall.Cells)
            sb.AppendLine($"{c.CellSn}\t{c.ProductModel}\t{c.Status}\t{(c.Shipped ? "已出货" : "在库")}\t{c.OrderNo}\t{c.Customer}\t{c.StockLocation}\t{(c.Held ? "已保留 " + c.HolderNo : "未保留")}");
        sb.AppendLine();
        sb.AppendLine("—— 受影响材料批次 ——");
        foreach (var l in _lastRecall.Lots)
            sb.AppendLine($"{l.LotNo}\t{l.MaterialName}\t使用电芯 {l.CellCount} 只\t流向 {l.ShipmentCount} 张出货单");

        try
        {
            Clipboard.SetText(sb.ToString());
            MessageBox.Show(
                $"已复制到剪贴板（出货记录 {_lastRecall.Shipments.Count} 行 / 电芯 {_lastRecall.Cells.Count} 行 / 批次 {_lastRecall.Lots.Count} 行）。",
                "复制清单", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"复制失败：{ex.Message}");
        }
    }

    /// <summary>构建「SpcConfig」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildSpcConfigTab()
    {
        var page = new TabPage("SPC 项目管理");
        page.Padding = new Padding(6);

        // ---- 顶部工具栏 ----
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(6, 9, 4, 0),
            WrapContents = false
        };
        toolbar.Controls.Add(MakeToolbarButton("新建版本", BtnSpcVersionNew));
        _btnSpcVersionEdit = MakeToolbarButton("修改待生效版本", BtnSpcVersionEdit);
        _btnSpcVersionEdit.Enabled = false;
        toolbar.Controls.Add(_btnSpcVersionEdit);
        _btnSpcVersionDelete = MakeToolbarButton("删除待生效版本", BtnSpcVersionDelete);
        _btnSpcVersionDelete.Enabled = false;
        toolbar.Controls.Add(_btnSpcVersionDelete);
        toolbar.Controls.Add(MakeToolbarButton("刷新", (_, _) => LoadSpcConfig()));

        _lblSpcConfigHint = new Label
        {
            Text = "规则：已生效版本不可修改 / 不可删除，只能新建版本；新版本在其生效时间到达之后才参与判定"
                 + "（样本按采样时间逐点选择生效版本）；待生效版本可修改 / 删除。",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(16, 11, 0, 0)
        };
        toolbar.Controls.Add(_lblSpcConfigHint);

        // ---- 上部项目列表 / 下部版本列表 ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 240,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup(
            "SPC 项目列表（只读；选中项目 → 下方查看 / 维护其规格版本）", out _spcItemConfigGrid));
        split.Panel2.Controls.Add(MakeGridGroup(
            "规格版本列表（橙色=待生效 / 绿色=当前生效；已生效版本不可修改或删除）", out _spcVersionGrid));

        page.Controls.Add(split);       // Fill
        page.Controls.Add(toolbar);     // Top

        SetupSpcItemConfigColumns();
        SetupSpcVersionColumns();
        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _spcItemConfigGrid.SelectionChanged -= SpcConfigItemGrid_SelectionChanged;
        _spcItemConfigGrid.SelectionChanged += SpcConfigItemGrid_SelectionChanged;
        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _spcVersionGrid.SelectionChanged -= SpcVersionGrid_SelectionChanged;
        _spcVersionGrid.SelectionChanged += SpcVersionGrid_SelectionChanged;

        LoadSpcConfig();
        page.Enter += (_, _) => LoadSpcConfig();
        return page;
    }

    /// <summary>设置「SpcItemConfigColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupSpcItemConfigColumns()
    {
        _spcItemConfigGrid.AutoGenerateColumns = false;
        _spcItemConfigGrid.Columns.Clear();
        _spcItemConfigGrid.Columns.Add(MakeTextCol("ItemId", "项目编号", true, 8));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("ItemName", "项目名称", true, 10));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("Category", "类别", true, 7));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("Unit", "单位", true, 5));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("CurrentVersionText", "当前版本", true, 8));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("CurrentSpecText", "当前规格", true, 12));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("PendingVersionText", "下一版本（生效时间）", true, 15));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("SampleCount", "样本数", true, 6));
        _spcItemConfigGrid.Columns.Add(MakeTextCol("Remark", "备注", true, 20));
    }

    /// <summary>设置「SpcVersionColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupSpcVersionColumns()
    {
        _spcVersionGrid.AutoGenerateColumns = false;
        _spcVersionGrid.Columns.Clear();
        _spcVersionGrid.Columns.Add(MakeTextCol("VersionText", "版本号", true, 6));
        _spcVersionGrid.Columns.Add(MakeTextCol("StatusText", "状态", true, 6));
        var eff = MakeTextCol("EffectiveFrom", "生效时间", true, 13);
        eff.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _spcVersionGrid.Columns.Add(eff);
        _spcVersionGrid.Columns.Add(MakeTextCol("UslText", "USL", true, 8));
        _spcVersionGrid.Columns.Add(MakeTextCol("LslText", "LSL", true, 8));
        _spcVersionGrid.Columns.Add(MakeTextCol("SpecText", "规格文本", true, 12));
        _spcVersionGrid.Columns.Add(MakeTextCol("Remark", "备注", true, 18));
        _spcVersionGrid.Columns.Add(MakeTextCol("CreatedBy", "创建人", true, 7));
        var ct = MakeTextCol("CreatedTime", "创建时间", true, 13);
        ct.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _spcVersionGrid.Columns.Add(ct);

        // 先解除再挂接着色事件（同上，防止重复着色）
        _spcVersionGrid.CellFormatting -= SpcVersionGrid_CellFormatting;
        _spcVersionGrid.CellFormatting += SpcVersionGrid_CellFormatting;
    }

    /// <summary>「SpcVersionGrid」网格单元格着色：按状态/结果区分颜色（差异红、异常橙、正常绿等）。</summary>
    private void SpcVersionGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Rows.Count <= e.RowIndex) return;
        if (grid.Rows[e.RowIndex].DataBoundItem is not SpcVersionRow row) return;

        if (row.IsCurrent)
        {
            e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);      // 绿色：当前生效
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
        else if (row.IsPending)
        {
            e.CellStyle.ForeColor = Color.FromArgb(230, 126, 34);     // 橙色：待生效
        }
    }

    /// <summary>规格版本列表展示行（供网格绑定；含状态着色与动作判定标志）。</summary>
    private sealed class SpcVersionRow
    {
        public int Version { get; init; }
        public string VersionText { get; init; } = "";
        public string StatusText { get; init; } = "";
        public DateTime EffectiveFrom { get; init; }
        public string UslText { get; init; } = "";
        public string LslText { get; init; } = "";
        public string SpecText { get; init; } = "";
        public string Remark { get; init; } = "";
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedTime { get; init; }
        public bool IsPending { get; init; }
        public bool IsCurrent { get; init; }
    }

    /// <summary>刷新项目管理页（项目列表 + 版本列表 + 按钮可用性）。</summary>
    private void LoadSpcConfig()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_spcItemConfigGrid is null || _spcVersionGrid is null) return;

        MesDatabase.SyncSpcItemVersionInfo();

        var prevId = SelectedConfigItem()?.ItemId;
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _spcItemConfigGrid.DataSource = null;
        _spcItemConfigGrid.DataSource = MesDatabase.SpcItems;

        var count = _spcItemConfigGrid.Rows.Count;
        if (count > 0)
        {
            var idx = 0;
            if (prevId is not null)
            {
                for (var i = 0; i < MesDatabase.SpcItems.Count && i < count; i++)
                {
                    if (MesDatabase.SpcItems[i].ItemId == prevId) { idx = i; break; }
                }
            }
            idx = Math.Min(idx, count - 1);
            _spcItemConfigGrid.ClearSelection();
            _spcItemConfigGrid.CurrentCell = _spcItemConfigGrid.Rows[idx].Cells[0];
            _spcItemConfigGrid.Rows[idx].Selected = true;
        }

        LoadSpcVersionGrid();
    }

    /// <summary>加载「SpcVersionGrid」并绑定到界面。</summary>
    private void LoadSpcVersionGrid()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_spcVersionGrid is null) return;
        var item = SelectedConfigItem();
        var rows = item is null ? new List<SpcVersionRow>() : BuildSpcVersionRows(item.ItemId);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _spcVersionGrid.DataSource = null;
        _spcVersionGrid.DataSource = rows;
        UpdateSpcVersionButtons();
    }

    /// <summary>List：（说明待补充）</summary>
    private static List<SpcVersionRow> BuildSpcVersionRows(string itemId)
    {
        var now = DateTime.Now;
        var current = MesDatabase.GetCurrentSpcVersion(itemId);
        var rows = new List<SpcVersionRow>();
        foreach (var v in MesDatabase.GetSpcItemVersions(itemId))
        {
            rows.Add(new SpcVersionRow
            {
                Version = v.Version,
                VersionText = v.VersionText,
                StatusText = v.StatusText(now),
                EffectiveFrom = v.EffectiveFrom,
                UslText = v.HasUsl ? v.Usl.ToString("F3") : "—",
                LslText = v.HasLsl ? v.Lsl.ToString("F3") : "—",
                SpecText = v.SpecText,
                Remark = v.Remark,
                CreatedBy = v.CreatedBy,
                CreatedTime = v.CreatedTime,
                IsPending = !v.IsEffective(now),
                IsCurrent = current is not null && current.Version == v.Version
            });
        }
        return rows;
    }

    /// <summary>根据当前选中项更新「SpcVersionButtons」的可用状态/提示。</summary>
    private void UpdateSpcVersionButtons()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_btnSpcVersionEdit is null || _btnSpcVersionDelete is null) return;
        var row = SelectedVersionRow();
        var canManage = row is not null && row.IsPending;
        _btnSpcVersionEdit.Enabled = canManage;
        _btnSpcVersionDelete.Enabled = canManage;
    }

    /// <summary>「SpcConfigItemGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void SpcConfigItemGrid_SelectionChanged(object? sender, EventArgs e) => LoadSpcVersionGrid();

    /// <summary>「SpcVersionGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void SpcVersionGrid_SelectionChanged(object? sender, EventArgs e) => UpdateSpcVersionButtons();

    /// <summary>在网格中选中「edConfigItem」对应的行（联动加载明细）。</summary>
    private SpcItem? SelectedConfigItem() =>
        _spcItemConfigGrid is null || _spcItemConfigGrid.SelectedRows.Count == 0
            ? null
            : _spcItemConfigGrid.SelectedRows[0].DataBoundItem as SpcItem;

    /// <summary>在网格中选中「edVersionRow」对应的行（联动加载明细）。</summary>
    private SpcVersionRow? SelectedVersionRow() =>
        _spcVersionGrid is null || _spcVersionGrid.SelectedRows.Count == 0
            ? null
            : _spcVersionGrid.SelectedRows[0].DataBoundItem as SpcVersionRow;

    /// <summary>新建版本的默认生效时间：晚于现有最新版本（未来预约则再顺延 1 天），否则为当前时刻 +1 分钟。</summary>
    private static DateTime DefaultNewEffective(SpcItemVersion? latest)
    {
        var now = DateTime.Now;
        if (latest is not null && latest.EffectiveFrom > now)
            return latest.EffectiveFrom.AddDays(1);
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
    }

    /// <summary>处理「SpcVersionNew」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSpcVersionNew(object? sender, EventArgs e)
    {
        var item = SelectedConfigItem();
        if (item is null)
        {
            Warn("请先在上方项目列表选择一条监控项。");
            return;
        }

        var versions = MesDatabase.GetSpcItemVersions(item.ItemId);
        var latest = versions.Count == 0 ? null : versions[^1];
        var current = MesDatabase.GetCurrentSpcVersion(item.ItemId);
        var baseUsl = current?.Usl ?? item.Usl;
        var baseLsl = current?.Lsl ?? item.Lsl;
        var nextVersion = versions.Count == 0 ? 1 : versions.Max(v => v.Version) + 1;

        using var dlg = new SpcVersionForm(
            item.ItemName, nextVersion, baseUsl, baseLsl, DefaultNewEffective(latest), "", "admin",
            latest?.EffectiveFrom, false);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg, _) = MesDatabase.AddSpcItemVersion(
            item.ItemId, dlg.Usl, dlg.Lsl, dlg.EffectiveFrom, dlg.RemarkText, dlg.OperatorName);
        MessageBox.Show(msg, "新建版本", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) LoadSpcConfig();
    }

    /// <summary>处理「SpcVersionEdit」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSpcVersionEdit(object? sender, EventArgs e)
    {
        var item = SelectedConfigItem();
        var row = SelectedVersionRow();
        if (item is null || row is null)
        {
            Warn("请先在下方版本列表选择一条待生效版本。");
            return;
        }
        if (!row.IsPending)
        {
            Warn("仅「待生效」版本可修改（已生效版本只能新建版本）。");
            return;
        }

        var entity = MesDatabase.GetSpcItemVersions(item.ItemId).FirstOrDefault(v => v.Version == row.Version);
        if (entity is null) return;

        var previous = MesDatabase.GetSpcItemVersions(item.ItemId)
            .Where(v => v.Version != row.Version && v.EffectiveFrom < entity.EffectiveFrom)
            .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.Version)
            .FirstOrDefault();

        using var dlg = new SpcVersionForm(
            item.ItemName, row.Version, entity.Usl, entity.Lsl, entity.EffectiveFrom, entity.Remark, "admin",
            previous?.EffectiveFrom, true);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.UpdateSpcItemVersion(
            item.ItemId, row.Version, dlg.Usl, dlg.Lsl, dlg.EffectiveFrom, dlg.RemarkText, dlg.OperatorName);
        MessageBox.Show(msg, "修改待生效版本", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) LoadSpcConfig();
    }

    /// <summary>处理「SpcVersionDelete」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSpcVersionDelete(object? sender, EventArgs e)
    {
        var item = SelectedConfigItem();
        var row = SelectedVersionRow();
        if (item is null || row is null)
        {
            Warn("请先在下方版本列表选择一条待生效版本。");
            return;
        }
        if (!row.IsPending)
        {
            Warn("仅「待生效」版本可删除（已生效版本不可删除）。");
            return;
        }

        if (MessageBox.Show(
                $"确认删除「{item.ItemName}」的待生效版本 {row.VersionText}（生效 {row.EffectiveFrom:yyyy-MM-dd HH:mm}）？\n删除后不可恢复。",
                "删除待生效版本", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var (ok, msg) = MesDatabase.DeleteSpcItemVersion(item.ItemId, row.Version);
        MessageBox.Show(msg, "删除待生效版本", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) LoadSpcConfig();
    }


}
