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
public partial class BomListForm : Form
{
    private readonly string _woNo;
    private readonly string? _modelHint;

    /// <summary>构造：按工单号查看该工单型号的 BOM；可选用 productModelHint 指定型号（工单不存在时使用）。</summary>
    public BomListForm(string woNo, string? productModelHint = null)
    {
        _woNo = woNo ?? "";
        _modelHint = productModelHint;

        InitializeComponent();
        InitializeRuntime(_woNo, _modelHint);
    }

    /// <summary>
    /// 执行期初始化：动态标题、DataGridView 样式物件与栏位定义、BOM 取数。
    /// 样式物件 / Columns.Add 属 Designer 白名单无法表达的陈述，保留于 .cs（控件本身已在 Designer.cs 建立）。
    /// </summary>
    private void InitializeRuntime(string woNo, string? productModelHint)
    {
        Text = BuildTitle();
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.Black,
            SelectionBackColor = Color.FromArgb(207, 224, 239),
            SelectionForeColor = Color.Black,
            Font = new Font("微软雅黑", 9F)
        };
        _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) };
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
        LoadBom();
    }

    private void BtnRefresh_Click(object? sender, EventArgs e) => LoadBom();

    private void BtnClose_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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
}
