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
/// MainForm 分部类（按功能域拆分，第 19 部分）：WIP 看板页（工序在制数 / 节拍 / 滞留 / 瓶颈预警 + 模拟推进）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Wip」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= WIP 看板（顶层第 15 个页签） =================

    /// <summary>WIP 看板：工序在制流转（在制数 / 过站数 / 节拍 / 滞留 / 瓶颈预警）+ 模拟推进演示。</summary>
    private TabPage BuildWipTab()
    {
        var page = new TabPage("WIP 看板");

        var cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 96,
            Padding = new Padding(6, 4, 6, 4)
        };
        cards.Controls.Add(MakeCard("WIP 总数 (只)", Color.FromArgb(230, 126, 34), out _lblWipTotal));
        cards.Controls.Add(MakeCard("今日过站 (次)", Color.FromArgb(15, 76, 129), out _lblWipToday));
        cards.Controls.Add(MakeCard("平均滞留 (h)", Color.FromArgb(142, 68, 173), out _lblWipStay));
        cards.Controls.Add(MakeCard("瓶颈工序", Color.FromArgb(192, 0, 0), out _lblWipBottleneck));
        cards.Controls.Add(MakeCard("可推进 (只)", Color.FromArgb(39, 174, 96), out _lblWipMovable));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 200,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("工序流转（选中一行 → 下方显示该工序在制电芯；在制口径 = 电芯最近一次过站工序）", out _wipFlowGrid));
        split.Panel2.Controls.Add(MakeGridGroup("在制电芯明细（按滞留时长降序）", out _wipCellGrid));

        page.Controls.Add(split);
        page.Controls.Add(BuildWipActionBar());
        page.Controls.Add(cards);

        SetupWipFlowColumns();
        SetupWipCellColumns();
        DoQueryWip();

        page.Enter -= WipTab_Enter;
        page.Enter += WipTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>构建「Wip」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildWipActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _btnWipSimulate = MakeToolbarButton("模拟推进5只", BtnWipSimulate);
        bar.Controls.Add(_btnWipSimulate);
        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => DoQueryWip()));
        bar.Controls.Add(new Label
        {
            Text = "在制口径：电芯最近一次过站工序；推进只影响在制电芯，已入库 / 已出货数据不变",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>设置「WipFlowColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupWipFlowColumns()
    {
        _wipFlowGrid.AutoGenerateColumns = false;
        _wipFlowGrid.Columns.Clear();
        _wipFlowGrid.Columns.Add(MakeTextCol("Seq", "顺序", true, 6));
        _wipFlowGrid.Columns.Add(MakeTextCol("ProcessName", "工序", true, 10));
        _wipFlowGrid.Columns.Add(MakeTextCol("WipCount", "在制数", true, 9));
        _wipFlowGrid.Columns.Add(MakeTextCol("TodayCount", "今日过站", true, 9));
        _wipFlowGrid.Columns.Add(MakeTextCol("YesterdayCount", "昨日过站", true, 9));
        _wipFlowGrid.Columns.Add(MakeTextCol("CycleText", "平均节拍(分/只)", true, 12));
        _wipFlowGrid.Columns.Add(MakeTextCol("MaxStayText", "最长滞留(h)", true, 11));
        _wipFlowGrid.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _wipFlowGrid.Columns.Add(MakeTextCol("Alert", "预警说明", true, 22));

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _wipFlowGrid.SelectionChanged -= WipFlowGrid_SelectionChanged;
        _wipFlowGrid.SelectionChanged += WipFlowGrid_SelectionChanged;
        // 先解除再挂接着色事件（同上，防止重复着色）
        _wipFlowGrid.CellFormatting -= WipFlowGrid_CellFormatting;
        _wipFlowGrid.CellFormatting += WipFlowGrid_CellFormatting;
    }

    /// <summary>设置「WipCellColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupWipCellColumns()
    {
        _wipCellGrid.AutoGenerateColumns = false;
        _wipCellGrid.Columns.Clear();
        _wipCellGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 18));
        _wipCellGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 12));
        _wipCellGrid.Columns.Add(MakeTextCol("WoNo", "工单", true, 14));
        _wipCellGrid.Columns.Add(MakeTextCol("Status", "状态", true, 8));
        _wipCellGrid.Columns.Add(MakeTextCol("ProcessName", "当前工序", true, 10));
        _wipCellGrid.Columns.Add(MakeTextCol("PrevProcess", "上一工序", true, 10));
        _wipCellGrid.Columns.Add(MakeTextCol("Equipment", "设备", true, 12));
        var tCol = MakeTextCol("LastStationTime", "最近过站", true, 13);
        tCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _wipCellGrid.Columns.Add(tCol);
        _wipCellGrid.Columns.Add(MakeTextCol("StayHours", "滞留(h)", true, 8));
    }

    /// <summary>进入「WipTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void WipTab_Enter(object? sender, EventArgs e) => EnsureWipReady();

    /// <summary>「WipFlowGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void WipFlowGrid_SelectionChanged(object? sender, EventArgs e) => LoadWipCells();

    /// <summary>流转表着色：瓶颈红 / 偏忙橙 / 正常绿。</summary>
    private void WipFlowGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_wipFlowGrid.Rows[e.RowIndex].DataBoundItem is not WipFlowRow row) return;

        var col = _wipFlowGrid.Columns[e.ColumnIndex].Name;
        if (col != "Status" && col != "WipCount" && col != "Alert") return;

        e.CellStyle.ForeColor = row.Status switch
        {
            WipFlowStatus.Bottleneck => Color.FromArgb(192, 0, 0),
            WipFlowStatus.Busy => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(39, 174, 96)
        };
    }

    /// <summary>在网格中选中「edWipFlow」对应的行（联动加载明细）。</summary>
    private WipFlowRow? SelectedWipFlow()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_wipFlowGrid.SelectedRows.Count == 0) return null;
        return _wipFlowGrid.SelectedRows[0].DataBoundItem as WipFlowRow;
    }

    /// <summary>加载 WIP 看板：汇总卡片 + 工序流转表（并联动电芯明细）。纯读取聚合，不改集合。</summary>
    private void DoQueryWip()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_wipFlowGrid is null) return;

        var rows = MesDatabase.GetWipFlowRows();
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _wipFlowGrid.DataSource = null;
        _wipFlowGrid.DataSource = rows;

        var total = rows.Sum(r => r.WipCount);
        var todayTotal = rows.Sum(r => r.TodayCount);
        var wipCells = MesDatabase.GetAllWipCells();
        var avgStay = wipCells.Count == 0 ? 0 : Math.Round(wipCells.Average(c => c.StayHours), 1);
        var bottleneck = rows.FirstOrDefault(r => r.Status == WipFlowStatus.Bottleneck)?.ProcessName ?? "无";

        if (_lblWipTotal is not null) _lblWipTotal.Text = total.ToString("N0");
        if (_lblWipToday is not null) _lblWipToday.Text = todayTotal.ToString("N0");
        if (_lblWipStay is not null) _lblWipStay.Text = avgStay.ToString("0.#");
        if (_lblWipBottleneck is not null)
        {
            _lblWipBottleneck.Text = bottleneck;
            _lblWipBottleneck.ForeColor = bottleneck == "无" ? Color.White : Color.FromArgb(255, 130, 130);
        }
        if (_lblWipMovable is not null)
            _lblWipMovable.Text = wipCells.Count(c => ProcessSequence.Next(c.ProcessName) is not null).ToString("N0");

        TrySelectFirstWipRow();
        LoadWipCells();
    }

    /// <summary>尝试选中列表首行（以控件实测行数为守卫；构造期绑定未物化时安全跳过）。</summary>
    private void TrySelectFirstWipRow()
    {
        if (_wipFlowGrid is null || _wipFlowGrid.Rows.Count == 0) return;   // 构造期绑定未物化时跳过
        _wipFlowGrid.ClearSelection();
        _wipFlowGrid.Rows[0].Selected = true;
    }

    /// <summary>加载选中工序的在制电芯明细。</summary>
    private void LoadWipCells()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_wipCellGrid is null) return;
        var row = SelectedWipFlow();
        _wipCellGrid.DataSource = row is null ? null : MesDatabase.GetWipCellsByProcess(row.ProcessName);
    }

    /// <summary>进入 WIP 看板时兜底加载（构造期数据绑定未物化）。</summary>
    private void EnsureWipReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_wipFlowGrid is null) return;
        if (_wipFlowGrid.Rows.Count == 0) DoQueryWip();
        if (_wipFlowGrid.SelectedRows.Count == 0) TrySelectFirstWipRow();
        LoadWipCells();
    }

    /// <summary>模拟推进一批（滞留最长的 5 只在制电芯推进到下一道工序）。</summary>
    private void BtnWipSimulate(object? sender, EventArgs e)
    {
        if (MessageBox.Show("模拟推进滞留最长的 5 只在制电芯到下一道工序？\n（演示用：写过站记录并同步电芯状态；已入库 / 已出货数据不受影响）",
                "模拟推进", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var (moved, msg) = MesDatabase.SimulateWipFlow(5);
        MessageBox.Show(msg, moved > 0 ? "推进成功" : "未推进", MessageBoxButtons.OK,
            moved > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQueryWip();
    }


}
