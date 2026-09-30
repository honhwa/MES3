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
/// MainForm 分部类（按功能域拆分，第 21 部分）：工单管理页（新建/修改/确认生产/终结 + ERP 下发/报工 + PO 明细 + BOM 明细）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「WorkOrder」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 工单管理 =================
    /// <summary>构建「WorkOrders」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildWorkOrdersTab()
    {
        var page = new TabPage("工单管理");

        // 顶部操作工具栏
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(10, 9, 6, 0)
        };
        toolbar.Controls.Add(MakeToolbarButton("＋ 新建工单", BtnNewWorkOrder));
        toolbar.Controls.Add(MakeToolbarButton("修改工单", BtnEditWorkOrder));
        toolbar.Controls.Add(MakeToolbarButton("确认生产", BtnConfirmWorkOrder));
        toolbar.Controls.Add(MakeToolbarButton("终结工单", BtnCloseWorkOrder));
        toolbar.Controls.Add(MakeToolbarButton("⬆ ERP 下发", BtnErpFetch));
        toolbar.Controls.Add(MakeToolbarButton("📤 ERP 报工", BtnErpReport));
        toolbar.Controls.Add(MakeToolbarButton("打印流转卡", BtnPrintWorkOrderSlip));
        toolbar.Controls.Add(MakeToolbarButton("刷新", (_, _) => RefreshAll()));

        // 主区：上=工单列表；下=（ERP PO 信息 / BOM 列表）明细区，选中工单联动刷新
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            BackColor = SystemColors.Control
        };

        var group = MakeGridGroup("生产工单列表（选中行 → 下方联动明细；双击修改工单；点「查看 BOM」弹出 BOM）", out _woGrid);
        split.Panel1.Controls.Add(group);

        var detailTabs = new TabControl { Dock = DockStyle.Fill };
        detailTabs.TabPages.Add(BuildWoPoDetailTab());
        detailTabs.TabPages.Add(BuildWoBomDetailTab());
        split.Panel2.Controls.Add(detailTabs);

        SetupWorkOrderColumns();

        page.Controls.Add(split);      // Fill
        page.Controls.Add(toolbar);    // Top
        return page;
    }

    /// <summary>工单明细区子页①：ERP PO 信息（只读多行展示该工单关联的 PO）</summary>
    private TabPage BuildWoPoDetailTab()
    {
        var page = new TabPage("ERP PO 信息");
        page.Padding = new Padding(6);

        _lblWoPo = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14, 12, 14, 12),
            Font = new Font("微软雅黑", 9.5F),
            ForeColor = Color.FromArgb(40, 50, 60),
            Text = "请选择上方工单查看其关联的 ERP PO 信息。"
        };
        page.Controls.Add(_lblWoPo);
        return page;
    }

    /// <summary>工单明细区子页②：BOM 列表（按工单计划数量计算需求量）</summary>
    private TabPage BuildWoBomDetailTab()
    {
        var page = new TabPage("BOM 列表");
        page.Padding = new Padding(6);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => LoadWorkOrderBomDetail()));
        _lblWoBomSummary = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 9, 0, 0) };
        bar.Controls.Add(_lblWoBomSummary);

        var gridGroup = MakeGridGroup("BOM 明细（需求量 = 单位用量 × (1 + 损耗率%) × 工单计划数）", out _woBomGrid);
        SetupWoBomColumns();

        page.Controls.Add(gridGroup);  // Fill
        page.Controls.Add(bar);        // Top
        return page;
    }

    /// <summary>
    /// 工单手写列绑定（替代通用 BindGrid，用于支持「查看 BOM」按钮列与 ERP 报工着色）。
    /// 注意：WoHeaders 仍保留给生产看板等通用绑定使用。
    /// </summary>
    private void SetupWorkOrderColumns()
    {
        _woGrid.AutoGenerateColumns = false;
        _woGrid.Columns.Clear();
        _woGrid.Columns.Add(MakeTextCol("WoNo", "工单号", true, 13));
        _woGrid.Columns.Add(MakeTextCol("ErpOrderNo", "ERP订单号", true, 13));
        _woGrid.Columns.Add(MakeTextCol("ProductModel", "产品型号", true, 12));
        _woGrid.Columns.Add(MakeTextCol("Line", "产线", true, 5));
        _woGrid.Columns.Add(MakeTextCol("PlanQty", "计划数", true, 6));
        _woGrid.Columns.Add(MakeTextCol("FinishedQty", "完成数", true, 6));
        _woGrid.Columns.Add(MakeTextCol("NgQty", "不良数", true, 5));
        _woGrid.Columns.Add(MakeTextCol("Status", "状态", true, 6));
        _woGrid.Columns.Add(MakeTextCol("ErpReportStatus", "ERP报工", true, 7));

        var startCol = MakeTextCol("StartDate", "开始日期", true, 12);
        startCol.DefaultCellStyle.Format = "yyyy-MM-dd";
        _woGrid.Columns.Add(startCol);
        var dueCol = MakeTextCol("DueDate", "交期", true, 12);
        dueCol.DefaultCellStyle.Format = "yyyy-MM-dd";
        _woGrid.Columns.Add(dueCol);

        _woGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "BomBtn",
            HeaderText = "BOM",
            Text = "查看 BOM",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat,
            FillWeight = 9
        });

        // 先解除再挂接着色事件（同上，防止重复着色）
        _woGrid.CellFormatting -= WoGrid_CellFormatting;
        _woGrid.CellFormatting += WoGrid_CellFormatting;
        _woGrid.CellContentClick -= WoGrid_CellContentClick;
        _woGrid.CellContentClick += WoGrid_CellContentClick;
        _woGrid.CellDoubleClick -= WoGrid_CellDoubleClick;
        _woGrid.CellDoubleClick += WoGrid_CellDoubleClick;
        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _woGrid.SelectionChanged -= WoGrid_SelectionChanged;
        _woGrid.SelectionChanged += WoGrid_SelectionChanged;
    }

    /// <summary>设置「WoBomColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupWoBomColumns()
    {
        _woBomGrid.AutoGenerateColumns = false;
        _woBomGrid.Columns.Clear();
        _woBomGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 14));
        _woBomGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 24));
        _woBomGrid.Columns.Add(MakeTextCol("Spec", "规格", true, 13));
        _woBomGrid.Columns.Add(MakeTextCol("Unit", "单位", true, 7));
        var perUnit = MakeTextCol("QtyPerUnit", "单位用量", true, 9);
        perUnit.DefaultCellStyle.Format = "0.####";
        _woBomGrid.Columns.Add(perUnit);
        var loss = MakeTextCol("LossRate", "损耗率(%)", true, 9);
        loss.DefaultCellStyle.Format = "0.##";
        _woBomGrid.Columns.Add(loss);
        var req = MakeTextCol("Requirement", "需求量", true, 10);
        req.DefaultCellStyle.Format = "0.###";
        _woBomGrid.Columns.Add(req);
        _woBomGrid.Columns.Add(MakeTextCol("MaterialType", "物料类型", true, 8));
    }

    /// <summary>ERP 报工列着色：「已报工」绿、「未报工」灰。</summary>
    private void WoGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name != "ErpReportStatus") return;

        e.CellStyle.ForeColor = e.Value?.ToString() == ErpReportStatus.Reported
            ? Color.FromArgb(39, 174, 96)
            : Color.Gray;
    }

    /// <summary>点击「查看 BOM」按钮列 → 弹出该工单的 BOM 列表窗体。</summary>
    private void WoGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_woGrid.Columns[e.ColumnIndex].Name != "BomBtn") return;
        if (_woGrid.Rows[e.RowIndex].DataBoundItem is not WorkOrder wo) return;

        using var form = new BomListForm(wo.WoNo);
        form.ShowDialog(this);
    }

    /// <summary>双击工单行 → 修改工单；点「查看 BOM」按钮列不触发（避免误触编辑）。</summary>
    private void WoGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_woGrid.Columns[e.ColumnIndex].Name == "BomBtn") return;

        _woGrid.ClearSelection();
        _woGrid.Rows[e.RowIndex].Selected = true;
        BtnEditWorkOrder(this, EventArgs.Empty);
    }

    /// <summary>「WoGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void WoGrid_SelectionChanged(object? sender, EventArgs e) => LoadWorkOrderDetail();

    /// <summary>工单列表专用绑定：手写列 + 联动明细区（替代通用 BindGrid）。</summary>
    private void BindWorkOrdersGrid()
    {
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _woGrid.DataSource = null;
        _woGrid.DataSource = MesDatabase.WorkOrders;
        LoadWorkOrderDetail();
    }

    /// <summary>选中工单 → 联动刷新「ERP PO 信息」与「BOM 列表」两页</summary>
    private void LoadWorkOrderDetail()
    {
        LoadWorkOrderPoDetail();
        LoadWorkOrderBomDetail();
    }

    /// <summary>加载「WorkOrderPoDetail」并绑定到界面。</summary>
    private void LoadWorkOrderPoDetail()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_lblWoPo is null) return;
        var wo = SelectedWorkOrder();
        if (wo is null)
        {
            _lblWoPo.Text = "请选择上方工单查看其关联的 ERP PO 信息。";
            return;
        }

        var po = MesDatabase.FindErpPo(wo.ErpOrderNo);
        if (po is null)
        {
            _lblWoPo.Text =
                $"工单号：{wo.WoNo}\r\n" +
                $"ERP 订单号：{(string.IsNullOrWhiteSpace(wo.ErpOrderNo) ? "—" : wo.ErpOrderNo)}\r\n\r\n" +
                "该工单未关联 ERP PO（手工创建）。";
            return;
        }

        _lblWoPo.Text =
            $"ERP 单号：{po.PoNo}\r\n" +
            $"客户：{po.Customer}\r\n" +
            $"产品型号：{po.ProductModel}    产品名称：{po.ProductName}\r\n" +
            $"订单数量：{po.OrderQty:N0} 只\r\n" +
            $"交期：{po.DueDate:yyyy-MM-dd}    PO 状态：{po.Status}\r\n" +
            $"下发时间：{(po.PushedTime.HasValue ? po.PushedTime.Value.ToString("yyyy-MM-dd HH:mm") : "—")}\r\n" +
            $"备注：{(string.IsNullOrWhiteSpace(po.Remark) ? "—" : po.Remark)}";
    }

    /// <summary>加载「WorkOrderBomDetail」并绑定到界面。</summary>
    private void LoadWorkOrderBomDetail()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_woBomGrid is null || _lblWoBomSummary is null) return;
        var wo = SelectedWorkOrder();
        if (wo is null)
        {
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _woBomGrid.DataSource = null;
            _lblWoBomSummary.Text = "请选择上方工单查看其 BOM 列表。";
            return;
        }

        var items = MesDatabase.GetWorkOrderBom(wo.WoNo);
        if (items.Count == 0)
        {
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _woBomGrid.DataSource = null;
            _lblWoBomSummary.Text = $"该型号暂无 BOM 数据（型号：{wo.ProductModel}）。";
            return;
        }

        var rows = MesDatabase.BuildBomRequirementRows(items, wo.PlanQty);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _woBomGrid.DataSource = null;
        _woBomGrid.DataSource = rows;
        var mainCount = rows.Count(r => r.MaterialType == ErpMaterialTypes.Main);
        _lblWoBomSummary.Text =
            $"工单 {wo.WoNo} · 型号 {wo.ProductModel} · 计划 {wo.PlanQty:N0} 只    物料项数：{rows.Count}    主材项数：{mainCount}";
    }

    /// <summary>ERP 下发：打开下发窗体，成功后刷新工单列表并选中首张新工单。</summary>
    private void BtnErpFetch(object? sender, EventArgs e)
    {
        using var form = new ErpFetchForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;

        RefreshAll();
        var firstWo = form.CreatedWoNos.Count > 0
            ? MesDatabase.WorkOrders.FirstOrDefault(w => w.WoNo == form.CreatedWoNos[0])
            : null;
        if (firstWo is not null) SelectRow(_woGrid, firstWo);
    }

    /// <summary>ERP 报工：需先选中工单 → 打开报工窗体 → 成功后刷新（显示「已报工」+ 时间）。</summary>
    private void BtnErpReport(object? sender, EventArgs e)
    {
        var wo = SelectedWorkOrder();
        if (wo is null) { Warn("请先选择要报工的工单。"); return; }
        if (!MesDatabase.IsWorkOrderTerminated(wo))
        {
            Warn($"工单 {wo.WoNo} 尚未终结（当前状态：{wo.Status}），请先点「终结工单」后再进行 ERP 报工。");
            return;
        }

        using var form = new ErpReportForm(wo);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            RefreshAll();
            SelectRow(_woGrid, wo);
        }
    }

    // ---------- 工单操作 ----------
    /// <summary>在网格中选中「edWorkOrder」对应的行（联动加载明细）。</summary>
    private WorkOrder? SelectedWorkOrder()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_woGrid.SelectedRows.Count == 0) return null;
        return _woGrid.SelectedRows[0].DataBoundItem as WorkOrder;
    }

    /// <summary>处理「NewWorkOrder」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnNewWorkOrder(object? sender, EventArgs e)
    {
        using var form = new WorkOrderEditForm(null);
        if (form.ShowDialog(this) == DialogResult.OK && form.WorkOrder is not null)
        {
            MesDatabase.WorkOrders.Add(form.WorkOrder);
            RefreshAll();
            SelectRow(_woGrid, form.WorkOrder);
        }
    }

    /// <summary>处理「EditWorkOrder」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnEditWorkOrder(object? sender, EventArgs e)
    {
        var wo = SelectedWorkOrder();
        if (wo is null) { Warn("请先选择要修改的工单。"); return; }

        using var form = new WorkOrderEditForm(wo);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            RefreshAll();
            SelectRow(_woGrid, wo);
        }
    }

    /// <summary>处理「ConfirmWorkOrder」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnConfirmWorkOrder(object? sender, EventArgs e)
    {
        var wo = SelectedWorkOrder();
        if (wo is null) { Warn("请先选择要确认生产的工单。"); return; }
        if (wo.Status == "生产中") { Warn($"工单 {wo.WoNo} 已在生产中。"); return; }
        if (wo.Status is "已完成" or "已关闭") { Warn($"工单 {wo.WoNo} 已终结，不能恢复生产。"); return; }

        if (MessageBox.Show($"确认开始生产工单 {wo.WoNo}？\n状态将变为「生产中」，开始日期设为今天。",
                "确认生产", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            wo.Status = "生产中";
            wo.StartDate = DateTime.Today;
            RefreshAll();
            SelectRow(_woGrid, wo);
        }
    }

    /// <summary>处理「CloseWorkOrder」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnCloseWorkOrder(object? sender, EventArgs e)
    {
        var wo = SelectedWorkOrder();
        if (wo is null) { Warn("请先选择要终结的工单。"); return; }
        if (wo.Status is "已完成" or "已关闭") { Warn($"工单 {wo.WoNo} 已终结。"); return; }

        if (MessageBox.Show($"终结工单 {wo.WoNo}？\n状态将变为「已完成」，此后不可再确认生产或修改。",
                "终结工单", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            wo.Status = "已完成";
            RefreshAll();
            SelectRow(_woGrid, wo);
        }
    }

    /// <summary>Warn：（说明待补充）</summary>
    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    /// <summary>在网格中选中「Row」对应的行（联动加载明细）。</summary>
    private static void SelectRow(DataGridView grid, WorkOrder wo)
    {
        grid.ClearSelection();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is WorkOrder w && w.WoNo == wo.WoNo)
            {
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    /// <summary>创建「ToolbarButton」控件/样式（统一样式的工厂方法）。</summary>
    private static Button MakeToolbarButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(104, 34),
            Margin = new Padding(4, 0, 4, 0),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        b.Click += onClick;
        return b;
    }


}
