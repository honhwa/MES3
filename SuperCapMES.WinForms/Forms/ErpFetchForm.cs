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
/// 控件宣告与静态配置见 ErpFetchForm.Designer.cs。
/// </summary>
public partial class ErpFetchForm : Form
{
    /// <summary>下发成功后生成的工单号列表（未成功为空列表）</summary>
    public List<string> CreatedWoNos { get; } = new();

    public ErpFetchForm()
    {
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：处理依赖静态类别值 / 物件初始设定式 / 回圈建项的内容，
    /// 以及原本建构式在 UI 建立后呼叫的 Fetch()。其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime()
    {
        // 原 BuildQueryBar：_cboStatus 下拉项取静态类别 const，Designer 无法解析，原样保留。
        _cboStatus.Items.Add(ErpPoStatus.Pending);
        _cboStatus.Items.Add("全部");
        _cboStatus.SelectedIndex = 0;   // 默认「待下发」

        // 原 MakePoGrid：DataGridViewCellStyle 采物件初始设定式，Designer 无法解析，原样保留。
        _poGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        _poGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.Black,
            SelectionBackColor = Color.FromArgb(207, 224, 239),
            SelectionForeColor = Color.Black,
            Font = new Font("微软雅黑", 9F)
        };
        _poGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) };

        SetupColumns();

        Fetch();
    }

    private void TxtKeyword_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) Fetch();
    }

    private void BtnFetch_Click(object? sender, EventArgs e)
    {
        Fetch();
    }

    private void BtnSelectAll_Click(object? sender, EventArgs e)
    {
        SetAll(true);
    }

    private void BtnClearAll_Click(object? sender, EventArgs e)
    {
        SetAll(false);
    }

    private void BtnPush_Click(object? sender, EventArgs e)
    {
        Push();
    }

    private void PoGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_poGrid.IsCurrentCellDirty) _poGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void PoGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex >= 0 && _poGrid.Columns[e.ColumnIndex].Name == "Chk") UpdateSelectedCount();
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
        // 原为 lambda，已改为具名方法 PoGrid_CurrentCellDirtyStateChanged / PoGrid_CellValueChanged，
        // 于 Designer.cs 以方法群挂上。
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

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
