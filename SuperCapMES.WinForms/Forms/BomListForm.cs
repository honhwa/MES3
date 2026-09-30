using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 「BOM 列表」窗体：按工单（或型号）展示 BOM 明细，含按计划数量计算的需求量。
/// 需求量 = 单位用量 × (1 + 损耗率%) × 计划数量；无工单信息时按计划数量 1 估算并在标题说明。
/// </summary>
public class BomListForm : Form
{
    private readonly string _woNo;
    private readonly string? _modelHint;

    private DataGridView _grid = null!;
    private Label _lblSummary = null!;

    /// <summary>构造：按工单号查看该工单型号的 BOM；可选用 productModelHint 指定型号（工单不存在时使用）。</summary>
    public BomListForm(string woNo, string? productModelHint = null)
    {
        _woNo = woNo ?? "";
        _modelHint = productModelHint;

        Text = BuildTitle();
        Font = new Font("微软雅黑", 9F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 520);
        MinimumSize = new Size(720, 420);
        BuildUi();
        LoadBom();
    }

    private string BuildTitle()
    {
        var model = ResolveModel();
        var modelText = string.IsNullOrEmpty(model) ? "—" : model;
        var woText = string.IsNullOrEmpty(_woNo) ? "—" : _woNo;
        return $"BOM 列表 - {woText}（{modelText}）";
    }

    private string ResolveModel()
    {
        var wo = FindWorkOrder();
        if (wo is not null) return wo.ProductModel;
        return _modelHint ?? "";
    }

    private WorkOrder? FindWorkOrder()
    {
        if (string.IsNullOrWhiteSpace(_woNo)) return null;
        return MesDatabase.WorkOrders.FirstOrDefault(w => string.Equals(w.WoNo, _woNo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10, 8, 10, 8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // 摘要
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 网格
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // 底部

        _lblSummary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            Text = ""
        };
        root.Controls.Add(_lblSummary, 0, 0);
        root.Controls.Add(BuildGridGroup(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildGridGroup()
    {
        var box = new GroupBox
        {
            Text = "BOM 明细（需求量 = 单位用量 × (1 + 损耗率%) × 计划数量）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };

        _grid = new DataGridView
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
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.Black,
                SelectionBackColor = Color.FromArgb(207, 224, 239),
                SelectionForeColor = Color.Black,
                Font = new Font("微软雅黑", 9F)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) }
        };

        _grid.AutoGenerateColumns = false;
        _grid.Columns.Clear();
        _grid.Columns.Add(MakeCol("MaterialCode", "物料编码", 14));
        _grid.Columns.Add(MakeCol("MaterialName", "物料名称", 24));
        _grid.Columns.Add(MakeCol("Spec", "规格", 13));
        _grid.Columns.Add(MakeCol("Unit", "单位", 7));
        var perUnit = MakeCol("QtyPerUnit", "单位用量", 9);
        perUnit.DefaultCellStyle.Format = "0.####";
        _grid.Columns.Add(perUnit);
        var loss = MakeCol("LossRate", "损耗率(%)", 9);
        loss.DefaultCellStyle.Format = "0.##";
        _grid.Columns.Add(loss);
        var req = MakeCol("Requirement", "需求量", 11);
        req.DefaultCellStyle.Format = "0.###";
        _grid.Columns.Add(req);
        _grid.Columns.Add(MakeCol("MaterialType", "物料类型", 8));

        box.Controls.Add(_grid);
        return box;
    }

    private Control BuildFooter()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(2, 10, 2, 0) };
        bar.Controls.Add(MakeButton("刷新", (_, _) => LoadBom()));

        var close = MakeButton("关闭", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        close.BackColor = Color.FromArgb(90, 100, 110);
        bar.Controls.Add(close);
        return bar;
    }

    private void LoadBom()
    {
        var wo = FindWorkOrder();
        var model = ResolveModel();
        var planQty = wo?.PlanQty ?? 1;
        var planNote = wo is null ? "（无工单信息，需求量按计划数量 1 估算）" : $"（计划数量 {planQty:N0} 只）";

        var items = string.IsNullOrWhiteSpace(model)
            ? new List<ErpBomItem>()
            : (wo is not null ? MesDatabase.GetWorkOrderBom(wo.WoNo) : MesDatabase.FetchErpBom(model));

        if (items.Count == 0)
        {
            _grid.DataSource = null;
            _lblSummary.Text = $"该型号暂无 BOM 数据{planNote}。";
            return;
        }

        var rows = MesDatabase.BuildBomRequirementRows(items, planQty);
        _grid.DataSource = null;
        _grid.DataSource = rows;
        var mainCount = rows.Count(r => r.MaterialType == ErpMaterialTypes.Main);
        _lblSummary.Text = $"型号 {model} · 物料项数 {rows.Count} · 主材项数 {mainCount}    {planNote}";
    }

    // ---------------- 控件工厂 ----------------

    private static DataGridViewTextBoxColumn MakeCol(string prop, string header, int fillWeight) =>
        new()
        {
            Name = prop,
            DataPropertyName = prop,
            HeaderText = header,
            ReadOnly = true,
            FillWeight = fillWeight
        };

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(90, 30),
            Margin = new Padding(6, 0, 4, 0),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        b.Click += onClick;
        return b;
    }
}
