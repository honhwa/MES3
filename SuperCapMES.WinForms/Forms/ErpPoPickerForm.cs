using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 「选择 ERP PO」窗体：从「已接收的 ERP PO」列表中选择一个 PO，并可查看该 PO 对应的 BOM
/// （需求量按该 PO 的订单数量计算），确认后返回所选 PO 号。
/// 说明：所谓「已接收」包含「已下发」与「待下发」两类状态；但仅「已下发」的 PO 可被选择，
/// 故默认筛选即「已下发」（待下发 PO 需先在「工单管理」页执行 ERP 下发）。
/// 用法：<c>using var f = new ErpPoPickerForm(); if (f.ShowDialog(owner) == DialogResult.OK) use f.SelectedPoNo;</c>
/// </summary>
public partial class ErpPoPickerForm : Form
{
    private const string StatusAll = "全部";

    /// <summary>确认选择后返回的 ERP PO 号；未选择时为空串。</summary>
    public string SelectedPoNo { get; private set; } = "";

    public ErpPoPickerForm()
    {
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：DataGridView 样式物件 / 栏位定义（Designer 白名单无法表达）、
    /// 状态下拉项目（含 Models 常数与 SelectedItem）、PO 列表取数。
    /// lambda 事件本体已具名化并挂在 ErpPoPickerForm.Designer.cs；
    /// SetupPoColumns 内的「先 -= 再 +=」事件挂接保留在原 helper。
    /// </summary>
    private void InitializeRuntime()
    {
        ApplyGridStyles(_poGrid);
        ApplyGridStyles(_bomGrid);
        SetupPoColumns();
        SetupBomColumns();

        _cboStatus.Items.Add(StatusAll);
        _cboStatus.Items.Add(ErpPoStatus.Pending);
        _cboStatus.Items.Add(ErpPoStatus.Pushed);
        _cboStatus.SelectedItem = ErpPoStatus.Pushed;   // 默认「已下发」：仅已下发的 PO 可被选择（不硬编码索引，避免项顺序变化后失效）

        Fetch();
    }

    private void CboStatus_SelectedIndexChanged(object? sender, EventArgs e) => Fetch();

    private void TxtKeyword_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) Fetch();
    }

    private void BtnRefresh_Click(object? sender, EventArgs e) => Fetch();

    private void BtnSearch_Click(object? sender, EventArgs e) => Fetch();

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void BtnConfirm_Click(object? sender, EventArgs e) => Confirm();

    // ---------------- 布局 ----------------

    /// <summary>窗体加载（尺寸就绪）后再设置分割位置，避免构造期尺寸未定导致异常。</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        TrySetSplitterDistance();
    }

    /// <summary>把上下分割位置设为 55%（上方 PO 列表略大），并做边界保护。</summary>
    private void TrySetSplitterDistance()
    {
        try
        {
            var height = _split.Height;
            if (height <= _split.Panel1MinSize + _split.Panel2MinSize + _split.SplitterWidth) return;
            var target = (int)(height * 0.55);
            var min = _split.Panel1MinSize;
            var max = height - _split.Panel2MinSize - _split.SplitterWidth;
            _split.SplitterDistance = Math.Max(min, Math.Min(target, max));
        }
        catch (InvalidOperationException)
        {
            // 尺寸尚未就绪：忽略，保持默认分割。
        }
    }

    // ---------------- 列定义 ----------------

    private void SetupPoColumns()
    {
        _poGrid.AutoGenerateColumns = false;
        _poGrid.Columns.Clear();

        _poGrid.Columns.Add(ReadOnlyCol("PoNo", "ERP单号", 16));
        _poGrid.Columns.Add(ReadOnlyCol("Customer", "客户", 16));
        _poGrid.Columns.Add(ReadOnlyCol("ProductModel", "产品型号", 14));
        var qtyCol = ReadOnlyCol("OrderQty", "订单数量", 9);
        qtyCol.DefaultCellStyle.Format = "N0";
        _poGrid.Columns.Add(qtyCol);
        var dueCol = ReadOnlyCol("DueDate", "交期", 11);
        dueCol.DefaultCellStyle.Format = "yyyy-MM-dd";
        _poGrid.Columns.Add(dueCol);
        _poGrid.Columns.Add(ReadOnlyCol("Status", "状态", 8));
        var pushedCol = ReadOnlyCol("PushedTime", "下发时间", 12);
        pushedCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _poGrid.Columns.Add(pushedCol);

        // 选中行变化 → 刷新下方 BOM（先 -= 再 +=，避免重复挂接）
        _poGrid.SelectionChanged -= PoGrid_SelectionChanged;
        _poGrid.SelectionChanged += PoGrid_SelectionChanged;
        // 状态着色（先 -= 再 +=）
        _poGrid.CellFormatting -= PoGrid_CellFormatting;
        _poGrid.CellFormatting += PoGrid_CellFormatting;
        // 双击 PO 行 = 确认选择
        _poGrid.CellDoubleClick -= PoGrid_CellDoubleClick;
        _poGrid.CellDoubleClick += PoGrid_CellDoubleClick;
    }

    private void SetupBomColumns()
    {
        _bomGrid.AutoGenerateColumns = false;
        _bomGrid.Columns.Clear();

        _bomGrid.Columns.Add(ReadOnlyCol("MaterialCode", "物料编码", 14));
        _bomGrid.Columns.Add(ReadOnlyCol("MaterialName", "物料名称", 24));
        _bomGrid.Columns.Add(ReadOnlyCol("Spec", "规格", 13));
        _bomGrid.Columns.Add(ReadOnlyCol("Unit", "单位", 7));
        var perUnit = ReadOnlyCol("QtyPerUnit", "单位用量", 9);
        perUnit.DefaultCellStyle.Format = "0.####";
        _bomGrid.Columns.Add(perUnit);
        var loss = ReadOnlyCol("LossRate", "损耗率(%)", 9);
        loss.DefaultCellStyle.Format = "0.##";
        _bomGrid.Columns.Add(loss);
        _bomGrid.Columns.Add(ReadOnlyCol("MaterialType", "物料类型", 8));
        var req = ReadOnlyCol("Requirement", "需求量", 11);
        req.DefaultCellStyle.Format = "0.###";
        _bomGrid.Columns.Add(req);
    }

    // ---------------- 行为 ----------------

    /// <summary>按当前状态 / 关键字过滤重新加载「已接收」PO 列表（纯查询，不修改数据）。</summary>
    private void Fetch()
    {
        if (_poGrid is null) return;
        if (_poGrid.IsCurrentCellInEditMode) _poGrid.EndEdit();

        var status = _cboStatus.SelectedItem?.ToString();
        if (status is null or StatusAll) status = null;
        var kw = _txtKeyword.Text.Trim();

        var list = MesDatabase.FetchErpPos(status, string.IsNullOrEmpty(kw) ? null : kw);
        _poGrid.DataSource = null;
        _poGrid.DataSource = list;
        _lblResult.Text = $"共 {list.Count} 条 PO";

        if (list.Count > 0)
        {
            _poGrid.ClearSelection();
            _poGrid.Rows[0].Selected = true;
            _poGrid.CurrentCell = _poGrid.Rows[0].Cells[0];
        }
        LoadBomForSelected();
    }

    private ErpPo? SelectedPo() =>
        _poGrid.CurrentRow?.DataBoundItem as ErpPo;

    private void PoGrid_SelectionChanged(object? sender, EventArgs e) => LoadBomForSelected();

    /// <summary>状态着色：「待下发」橙、「已下发」绿。</summary>
    private void PoGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name != "Status") return;

        // 仅改动「显示值」，不触碰绑定数据（ErpPos 集合 / DataBoundItem 保持不变）。
        var status = e.Value?.ToString();
        if (status == ErpPoStatus.Pushed)
        {
            e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);
            e.Value = ErpPoStatus.Pushed;              // 显示「已下发」
            e.FormattingApplied = true;
        }
        else if (status == ErpPoStatus.Pending)
        {
            e.CellStyle.ForeColor = Color.FromArgb(230, 126, 34);
            e.Value = ErpPoStatus.Pending + "（不可选）"; // 显示「待下发（不可选）」
            e.FormattingApplied = true;
        }
        else
        {
            e.CellStyle.ForeColor = Color.Black;
        }
    }

    /// <summary>双击 PO 行 = 确认选择。</summary>
    private void PoGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        Confirm();
    }

    /// <summary>加载当前选中 PO 的 BOM：需求量按该 PO 的订单数量计算；无 BOM 时给出提示。</summary>
    private void LoadBomForSelected()
    {
        var po = SelectedPo();
        if (po is null)
        {
            _bomGrid.DataSource = null;
            _bomGroup.Text = "BOM 明细";
            _lblBomHint.Text = "请在上方选择一个 ERP PO 以查看其 BOM。";
            return;
        }

        var items = MesDatabase.FetchErpBom(po.ProductModel);
        _bomGroup.Text = $"BOM 明细（按订单数量 {po.OrderQty:N0} 计算需求量）";

        if (items.Count == 0)
        {
            _bomGrid.DataSource = null;
            _lblBomHint.Text = $"型号 {po.ProductModel} 暂无 BOM 数据。";
            return;
        }

        var rows = MesDatabase.BuildBomRequirementRows(items, po.OrderQty);
        _bomGrid.DataSource = null;
        _bomGrid.DataSource = rows;
        _lblBomHint.Text = $"型号 {po.ProductModel} · 物料项数 {rows.Count} · 需求量 = 单位用量 × (1 + 损耗率%) × 订单数量 {po.OrderQty:N0}";
    }

    /// <summary>确认选择：未选中 → 提示；待下发 PO → 拦截（不可选）；已下发 PO → 二次确认（默认「否」）。</summary>
    private void Confirm()
    {
        var po = SelectedPo();
        if (po is null) { Warn("请先选择一个 ERP PO。"); return; }

        // 仅「已下发」的 PO 可被选择：待下发的 PO 需先在「工单管理」页执行 ERP 下发。
        if (po.Status != ErpPoStatus.Pushed)
        {
            Warn($"PO {po.PoNo} 尚未下发，请先在「工单管理」页执行 ERP 下发后再选择。");
            return;   // 不设置 SelectedPoNo、不关闭窗体
        }

        if (po.Status == ErpPoStatus.Pushed)
        {
            var answer = MessageBox.Show(
                $"该 PO 已下发并关联工单，是否仍要使用？\n\nPO 号：{po.PoNo}\n客户：{po.Customer}\n型号：{po.ProductModel}",
                "PO 已下发", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        SelectedPoNo = po.PoNo;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------------- 控件工厂 ----------------

    /// <summary>原 MakeGrid() 中 Designer 白名单无法表达的样式物件设定（两个 DataGridView 共用）。</summary>
    private static void ApplyGridStyles(DataGridView grid)
    {
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.Black,
            SelectionBackColor = Color.FromArgb(207, 224, 239),
            SelectionForeColor = Color.Black,
            Font = new Font("微软雅黑", 9F)
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) };
    }

    private static DataGridViewTextBoxColumn ReadOnlyCol(string prop, string header, int fillWeight) =>
        new()
        {
            Name = prop,
            DataPropertyName = prop,
            HeaderText = header,
            ReadOnly = true,
            FillWeight = fillWeight
        };

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
