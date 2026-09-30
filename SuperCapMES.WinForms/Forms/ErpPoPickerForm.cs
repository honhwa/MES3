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
public class ErpPoPickerForm : Form
{
    private const string StatusAll = "全部";

    private ComboBox _cboStatus = null!;
    private TextBox _txtKeyword = null!;
    private Label _lblResult = null!;
    private DataGridView _poGrid = null!;
    private SplitContainer _split = null!;
    private GroupBox _bomGroup = null!;
    private DataGridView _bomGrid = null!;
    private Label _lblBomHint = null!;

    /// <summary>确认选择后返回的 ERP PO 号；未选择时为空串。</summary>
    public string SelectedPoNo { get; private set; } = "";

    public ErpPoPickerForm()
    {
        Text = "选择 ERP PO（已接收订单）";
        Font = new Font("微软雅黑", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 620);

        BuildUi();
        Fetch();
    }

    // ---------------- 布局 ----------------

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10, 8, 10, 8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // 工具栏
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 上：PO 列表 / 下：BOM
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));   // 底部按钮

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildSplit(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(2, 8, 2, 0) };

        bar.Controls.Add(MakeButton("🔄 刷新", (_, _) => Fetch()));

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(12, 6, 0, 0) });
        _cboStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(4, 2, 0, 0) };
        _cboStatus.Items.Add(StatusAll);
        _cboStatus.Items.Add(ErpPoStatus.Pending);
        _cboStatus.Items.Add(ErpPoStatus.Pushed);
        _cboStatus.SelectedItem = ErpPoStatus.Pushed;   // 默认「已下发」：仅已下发的 PO 可被选择（不硬编码索引，避免项顺序变化后失效）
        _cboStatus.SelectedIndexChanged += (_, _) => Fetch();
        bar.Controls.Add(_cboStatus);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 6, 0, 0) });
        _txtKeyword = new TextBox { Width = 200, Margin = new Padding(4, 2, 0, 0) };
        _txtKeyword.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Fetch(); };
        bar.Controls.Add(_txtKeyword);

        bar.Controls.Add(MakeButton("搜索", (_, _) => Fetch()));
        bar.Controls.Add(new Label { Text = "（PO 号 / 客户 / 产品型号）", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(6, 6, 0, 0) });

        _lblResult = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 6, 0, 0) };
        bar.Controls.Add(_lblResult);
        return bar;
    }

    private Control BuildSplit()
    {
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            Panel1MinSize = 100,
            Panel2MinSize = 100
        };

        _split.Panel1.Controls.Add(BuildPoGroup());
        _split.Panel2.Controls.Add(BuildBomGroup());
        return _split;
    }

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

    private Control BuildPoGroup()
    {
        var box = new GroupBox
        {
            Text = "已接收 ERP PO 列表（仅「已下发」可选；选中一行查看其 BOM，双击行 = 确认选择）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };

        _poGrid = MakeGrid();
        SetupPoColumns();
        box.Controls.Add(_poGrid);
        return box;
    }

    private Control BuildBomGroup()
    {
        _bomGroup = new GroupBox
        {
            Text = "BOM 明细",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };

        // 内部两行：网格（填充） + 提示标签（底部）
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        _bomGrid = MakeGrid();
        SetupBomColumns();

        _lblBomHint = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
            Text = ""
        };

        inner.Controls.Add(_bomGrid, 0, 0);
        inner.Controls.Add(_lblBomHint, 0, 1);
        _bomGroup.Controls.Add(inner);
        return _bomGroup;
    }

    private Control BuildFooter()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(2, 10, 2, 0)
        };

        var cancel = MakeButton("取消", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        cancel.BackColor = Color.FromArgb(90, 100, 110);
        bar.Controls.Add(cancel);

        bar.Controls.Add(MakeButton("确认选择", (_, _) => Confirm()));

        // 提示文案：明确「仅已下发可选」规则（双击仍可直接确认所选行）。
        var tip = new Label
        {
            Text = "提示：仅可选择「已下发」的 PO；待下发 PO 需先在「工单管理」页执行「ERP 下发」。",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(0, 8, 12, 0)
        };
        bar.Controls.Add(tip);
        return bar;
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

    private static DataGridView MakeGrid() =>
        new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
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

    private static DataGridViewTextBoxColumn ReadOnlyCol(string prop, string header, int fillWeight) =>
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
            MinimumSize = new Size(96, 30),
            Margin = new Padding(6, 0, 4, 0),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        b.Click += onClick;
        return b;
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
