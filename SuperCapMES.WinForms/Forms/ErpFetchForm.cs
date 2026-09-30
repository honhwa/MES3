using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 「ERP 下发」窗体：
/// 顶部从 ERP 获取最新 PO（状态 / 关键字过滤）；中部 PO 勾选列表（全选 / 取消全选 + 已选计数）；
/// 底部选产线 / 操作人 → 下发选中 PO 生成工单（工单号 = WO-{PoNo}）。成功后返回生成的工单号列表。
/// </summary>
public class ErpFetchForm : Form
{
    private ComboBox _cboStatus = null!;
    private TextBox _txtKeyword = null!;
    private DataGridView _poGrid = null!;
    private Label _lblResult = null!;
    private Label _lblSelected = null!;
    private ComboBox _cboLine = null!;
    private TextBox _txtOperator = null!;

    /// <summary>下发成功后生成的工单号列表（未成功为空列表）</summary>
    public List<string> CreatedWoNos { get; } = new();

    public ErpFetchForm()
    {
        Text = "ERP 下发 - 从 ERP 获取最新 PO 并生成工单";
        Font = new Font("微软雅黑", 9F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 620);
        MinimumSize = new Size(840, 520);
        BuildUi();
        Fetch();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10, 8, 10, 8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // 查询栏
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // PO 网格
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));   // 勾选工具
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));   // 下发栏

        root.Controls.Add(BuildQueryBar(), 0, 0);
        root.Controls.Add(BuildGridGroup(), 0, 1);
        root.Controls.Add(BuildSelectBar(), 0, 2);
        root.Controls.Add(BuildPushBar(), 0, 3);

        Controls.Add(root);
    }

    // ---------------- 查询栏 ----------------

    private Control BuildQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(2, 8, 2, 0) };

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
        _cboStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(4, 2, 0, 0) };
        _cboStatus.Items.Add(ErpPoStatus.Pending);
        _cboStatus.Items.Add("全部");
        _cboStatus.SelectedIndex = 0;   // 默认「待下发」
        bar.Controls.Add(_cboStatus);

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 6, 0, 0) });
        _txtKeyword = new TextBox { Width = 220, Margin = new Padding(4, 2, 0, 0) };
        _txtKeyword.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Fetch(); };
        bar.Controls.Add(_txtKeyword);
        bar.Controls.Add(new Label { Text = "（PO 号 / 客户 / 产品型号）", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(6, 6, 0, 0) });

        bar.Controls.Add(MakeButton("🔄 从 ERP 获取最新 PO", (_, _) => Fetch()));

        _lblResult = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 6, 0, 0) };
        bar.Controls.Add(_lblResult);
        return bar;
    }

    // ---------------- PO 网格 ----------------

    private Control BuildGridGroup()
    {
        var box = new GroupBox { Text = "ERP PO 列表（勾选要下发的 PO，可多选）", Dock = DockStyle.Fill, Padding = new Padding(6, 12, 6, 6) };

        _poGrid = MakePoGrid();
        _poGrid.ReadOnly = false;
        _poGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        SetupColumns();

        box.Controls.Add(_poGrid);
        return box;
    }

    private void SetupColumns()
    {
        _poGrid.AutoGenerateColumns = false;
        _poGrid.Columns.Clear();

        _poGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Chk",
            HeaderText = "选择",
            FillWeight = 6,
            ReadOnly = false,
            ThreeState = false,
            FlatStyle = FlatStyle.Flat
        });
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

        // 勾选状态变化即时生效（提交编辑后再计数）
        _poGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_poGrid.IsCurrentCellDirty) _poGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _poGrid.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex >= 0 && _poGrid.Columns[e.ColumnIndex].Name == "Chk") UpdateSelectedCount();
        };
    }

    // ---------------- 勾选工具 ----------------

    private Control BuildSelectBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(2, 6, 2, 0) };
        bar.Controls.Add(MakeButton("全选", (_, _) => SetAll(true)));
        bar.Controls.Add(MakeButton("取消全选", (_, _) => SetAll(false)));
        _lblSelected = new Label { Text = "已勾选 0 条", AutoSize = true, ForeColor = Color.FromArgb(15, 76, 129), Margin = new Padding(14, 8, 0, 0) };
        bar.Controls.Add(_lblSelected);
        bar.Controls.Add(new Label { Text = "仅「待下发」状态的 PO 会被生成工单；已下发/重复的将自动跳过。", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(18, 8, 0, 0) });
        return bar;
    }

    // ---------------- 下发栏 ----------------

    private Control BuildPushBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(2, 10, 2, 0) };

        bar.Controls.Add(new Label { Text = "产线：", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
        _cboLine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(4, 2, 0, 0) };
        _cboLine.Items.AddRange(new object[] { "L1", "L2", "L3" });
        _cboLine.SelectedIndex = 0;     // 默认 L1
        bar.Controls.Add(_cboLine);

        bar.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(12, 6, 0, 0) });
        _txtOperator = new TextBox { Text = "admin", Width = 110, Margin = new Padding(4, 2, 0, 0) };
        bar.Controls.Add(_txtOperator);

        bar.Controls.Add(MakeButton("⬆ 下发选中 PO 生成工单", (_, _) => Push()));
        return bar;
    }

    // ---------------- 行为 ----------------

    /// <summary>从 ERP 获取最新 PO（按状态 / 关键字过滤）并刷新网格</summary>
    private void Fetch()
    {
        if (_poGrid is null) return;
        if (_poGrid.IsCurrentCellInEditMode) _poGrid.EndEdit();

        var status = _cboStatus.SelectedItem?.ToString();
        if (status is null or "全部") status = null;
        var kw = _txtKeyword.Text.Trim();

        var list = MesDatabase.FetchErpPos(status, string.IsNullOrEmpty(kw) ? null : kw);
        _poGrid.DataSource = null;
        _poGrid.DataSource = list;
        _lblResult.Text = $"共获取到 {list.Count} 条 PO";
        UpdateSelectedCount();
    }

    private void SetAll(bool value)
    {
        foreach (DataGridViewRow row in _poGrid.Rows)
        {
            if (row.IsNewRow) continue;
            row.Cells["Chk"].Value = value;
        }
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        var count = 0;
        foreach (DataGridViewRow row in _poGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["Chk"].Value is true) count++;
        }
        _lblSelected.Text = $"已勾选 {count} 条";
    }

    private List<ErpPo> SelectedPos()
    {
        var list = new List<ErpPo>();
        foreach (DataGridViewRow row in _poGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["Chk"].Value is true && row.DataBoundItem is ErpPo po) list.Add(po);
        }
        return list;
    }

    /// <summary>下发选中 PO 生成工单：调用仓储，成功后汇总提示并返回工单号列表</summary>
    private void Push()
    {
        if (_poGrid.IsCurrentCellInEditMode) _poGrid.EndEdit();
        _poGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        var selected = SelectedPos();
        if (selected.Count == 0) { Warn("请先勾选要下发的 PO。"); return; }

        var line = _cboLine.SelectedItem?.ToString() ?? "L1";
        var op = _txtOperator.Text.Trim();
        if (op.Length == 0) op = "admin";

        var poNos = selected.Select(p => p.PoNo).ToList();
        var (ok, message, woNos) = MesDatabase.PushErpOrdersToWorkOrders(poNos, line, op);
        if (!ok) { Warn(message); return; }

        CreatedWoNos.Clear();
        CreatedWoNos.AddRange(woNos);

        var summary = $"{message}\n\n工单号列表：\n{string.Join("\n", woNos)}";
        MessageBox.Show(summary, "ERP 下发", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------------- 控件工厂 ----------------

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

    private static DataGridView MakePoGrid() =>
        new()
        {
            Dock = DockStyle.Fill,
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

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
