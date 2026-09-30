using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 单只电芯详情窗体：双击「资材追溯」页结果行（_matHoldGrid）后弹出，
/// 顶部展示电芯概要（SN/型号/线体/工单/状态/分档/创建时间/极片批号/电解液批号/所属模组 + 环境摘要），
/// 主体以三页签只读表格展示：关联资材（材料批次）/ 工序参数 / 测试数据。
/// 数据按 SN 实时查询（MesDatabase.GetTrace），MesDatabase 零 API 改动。
/// </summary>
public class CellDetailForm : Form
{
    private readonly string _sn;
    private readonly TraceResult? _trace;

    public CellDetailForm(string sn)
    {
        _sn = string.IsNullOrWhiteSpace(sn) ? "" : sn.Trim();
        // 未找到 SN 时 GetTrace 返回 null（或 Cell 为 null），下方做防御处理，不抛异常
        _trace = MesDatabase.GetTrace(_sn);

        Text = $"电芯详情 - {(_sn.Length == 0 ? "未知" : _sn)}";
        Font = new Font("微软雅黑", 9F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 700);
        MinimumSize = new Size(900, 560);

        BuildUi();

        if (_trace?.Cell is null)
        {
            // 未找到：窗体正常展示后提示一次并自动关闭（ShowDialog 正常返回）
            Shown += (_, _) =>
            {
                MessageBox.Show($"未找到电芯 SN：{_sn}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.Cancel;
                Close();
            };
        }
    }

    // ================= 整体布局 =================

    private void BuildUi()
    {
        var summary = BuildSummaryPanel();
        var tabs = BuildDetailTabs();
        var bottom = BuildBottomBar();

        // Dock 布局：后添加的控件先贴边，故先加 fill（主体页签）再依次加顶部概要、底部按钮条
        Controls.Add(tabs);
        Controls.Add(summary);
        Controls.Add(bottom);
    }

    // ================= 顶部概要 =================

    private Control BuildSummaryPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 158,
            BackColor = Color.White,
            Padding = new Padding(10, 8, 10, 6)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cell = _trace?.Cell;
        var statusText = cell is null ? "—" : cell.Status;

        // 第一行：基础身份信息
        var lblSn = MakeInfoLabel($"SN：{Dash(cell?.Sn)}");
        var lblModel = MakeInfoLabel($"产品型号：{Dash(cell?.ProductModel)}");
        var lblLine = MakeInfoLabel($"产线：{Dash(cell?.Line)}");
        var lblWo = MakeInfoLabel($"生产工单：{Dash(cell?.WoNo)}");

        // 第二行：状态 / 分档 / 创建时间 / 所属模组
        var lblStatus = MakeInfoLabel($"当前状态：{Dash(statusText)}");
        var lblResult = MakeInfoLabel($"分档：{ResultText(cell)}");
        var lblCreate = MakeInfoLabel($"创建时间：{FmtDateTime(cell?.CreateTime)}");
        var lblModule = MakeInfoLabel($"所属模组：{Dash(cell?.ModuleSn)}");

        // 第三行：极片批号 / 电解液批号 / 露点 / 温度
        var lblElectrode = MakeInfoLabel($"极片批号：{Dash(cell?.ElectrodeLot)}");
        var lblElectrolyte = MakeInfoLabel($"电解液批号：{Dash(cell?.ElectrolyteLot)}");
        var lblDew = MakeInfoLabel($"露点：{Dash(_trace?.EnvDewPoint)}");
        var lblTemp = MakeInfoLabel($"温度：{Dash(_trace?.EnvTemp)}");

        // 第四行：湿度 + 采样点（环境摘要尾行，跨 4 列）
        var lblEnv = MakeInfoLabel(BuildEnvLine());
        lblEnv.AutoEllipsis = true;

        // 保留语义着色：与保留管理页口径一致 —— 不良/NG 红、合格绿、合格分档绿
        if (cell is not null && cell.Result == "NG")
        {
            lblResult.ForeColor = Color.FromArgb(192, 0, 0);
            lblResult.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
        else if (cell is not null && !string.IsNullOrEmpty(cell.Result))
        {
            lblResult.ForeColor = Color.FromArgb(39, 174, 96);
        }

        grid.Controls.Add(lblSn, 0, 0);
        grid.Controls.Add(lblModel, 1, 0);
        grid.Controls.Add(lblLine, 2, 0);
        grid.Controls.Add(lblWo, 3, 0);
        grid.Controls.Add(lblStatus, 0, 1);
        grid.Controls.Add(lblResult, 1, 1);
        grid.Controls.Add(lblCreate, 2, 1);
        grid.Controls.Add(lblModule, 3, 1);
        grid.Controls.Add(lblElectrode, 0, 2);
        grid.Controls.Add(lblElectrolyte, 1, 2);
        grid.Controls.Add(lblDew, 2, 2);
        grid.Controls.Add(lblTemp, 3, 2);
        grid.Controls.Add(lblEnv, 0, 3);
        grid.SetColumnSpan(lblEnv, 4);

        panel.Controls.Add(grid);
        return panel;
    }

    /// <summary>组装环境摘要单行文本：采样点 · 露点 · 温度 · 湿度（有值才拼，避免空信息）</summary>
    private string BuildEnvLine()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_trace?.EnvPoint)) parts.Add($"采样点：{_trace.EnvPoint}");
        if (!string.IsNullOrWhiteSpace(_trace?.EnvDewPoint)) parts.Add($"露点：{_trace.EnvDewPoint}");
        if (!string.IsNullOrWhiteSpace(_trace?.EnvTemp)) parts.Add($"温度：{_trace.EnvTemp}");
        if (!string.IsNullOrWhiteSpace(_trace?.EnvHumidity)) parts.Add($"湿度：{_trace.EnvHumidity}");
        return parts.Count == 0 ? "环境摘要：—" : "环境摘要：" + string.Join("　·　", parts);
    }

    /// <summary>分档展示文本：NG → 不合格（NG）；空 → 待分档；其余 → 合格（X档）</summary>
    private static string ResultText(CellSn? cell)
    {
        if (cell is null) return "—";
        if (cell.Result == "NG") return "不合格（NG）";
        return string.IsNullOrEmpty(cell.Result) ? "待分档" : $"合格（{cell.Result}档）";
    }

    private static string FmtDateTime(DateTime? dt)
    {
        if (dt is null || dt.Value == default) return "—";
        return dt.Value.ToString("yyyy-MM-dd HH:mm");
    }

    private static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private static Label MakeInfoLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            AutoEllipsis = true
        };

    // ================= 主体三页签 =================

    private Control BuildDetailTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };

        var tabMat = new TabPage("关联资材");
        tabMat.Controls.Add(MakeGridGroup("关联资材（材料批次：极片 / 电解液 / 隔膜 / 集流体等，正反向追溯）", out var matGrid));
        BindMaterialGrid(matGrid);
        tabs.TabPages.Add(tabMat);

        var tabProc = new TabPage("工序参数");
        tabProc.Controls.Add(MakeGridGroup("工序参数记录（生产该工序时设备非「运行」的行整行标红）", out var procGrid));
        BindProcessGrid(procGrid);
        tabs.TabPages.Add(tabProc);

        var tabTest = new TabPage("测试数据");
        tabTest.Controls.Add(MakeGridGroup("电性能测试数据", out var testGrid));
        BindTestGrid(testGrid);
        tabs.TabPages.Add(tabTest);

        return tabs;
    }

    /// <summary>绑定「关联资材」网格：MaterialLot 字段全量展示</summary>
    private void BindMaterialGrid(DataGridView grid)
    {
        grid.AutoGenerateColumns = false;
        grid.DataSource = null;
        grid.Columns.Clear();

        grid.Columns.Add(MakeCol("LotNo", "批次号", "LotNo", 13));
        grid.Columns.Add(MakeCol("MaterialName", "材料名称", "MaterialName", 18));
        grid.Columns.Add(MakeCol("MaterialCode", "物料编码", "MaterialCode", 12));
        grid.Columns.Add(MakeCol("Supplier", "供应商", "Supplier", 12));
        grid.Columns.Add(MakeCol("SupplierLot", "供应商批号", "SupplierLot", 12));
        var recvCol = MakeCol("ReceivedDate", "到货日期", "ReceivedDate", 9);
        recvCol.DefaultCellStyle.Format = "yyyy-MM-dd";
        grid.Columns.Add(recvCol);
        grid.Columns.Add(MakeCol("IqcResult", "IQC判定", "IqcResult", 7));
        grid.Columns.Add(MakeCol("UsedFor", "关联对象", "UsedFor", 10));

        grid.DataSource = _trace?.Materials ?? new List<MaterialLot>();

        // 重复绑定前先移除旧处理器，避免事件累积
        grid.CellFormatting -= MaterialGrid_CellFormatting;
        grid.CellFormatting += MaterialGrid_CellFormatting;
    }

    /// <summary>绑定「工序参数」网格：ProcessRecord 字段全量展示，含末列「设备状态」</summary>
    private void BindProcessGrid(DataGridView grid)
    {
        grid.AutoGenerateColumns = false;
        grid.DataSource = null;
        grid.Columns.Clear();

        grid.Columns.Add(MakeCol("ProcessName", "工序", "ProcessName", 10));
        grid.Columns.Add(MakeCol("Equipment", "设备", "Equipment", 12));
        grid.Columns.Add(MakeCol("Operator", "操作员", "Operator", 8));
        grid.Columns.Add(MakeCol("Shift", "班次", "Shift", 6));
        grid.Columns.Add(MakeCol("ParamName", "参数", "ParamName", 12));
        grid.Columns.Add(MakeCol("ParamValue", "参数值", "ParamValue", 12));
        var timeCol = MakeCol("Time", "时间", "Time", 13);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        grid.Columns.Add(timeCol);
        grid.Columns.Add(MakeCol("DeviceStatusAt", "设备状态", "DeviceStatusAt", 8));

        grid.DataSource = _trace?.Processes ?? new List<ProcessRecord>();

        // 非「运行」工序行整行标红；先移除再挂接避免事件累积
        grid.CellFormatting -= ProcessGrid_CellFormatting;
        grid.CellFormatting += ProcessGrid_CellFormatting;
    }

    /// <summary>绑定「测试数据」网格：TestRecord 字段全量展示，判定列以 合格/不合格 展示</summary>
    private void BindTestGrid(DataGridView grid)
    {
        grid.AutoGenerateColumns = false;
        grid.DataSource = null;
        grid.Columns.Clear();

        grid.Columns.Add(MakeCol("CellSn", "电芯SN", "CellSn", 16));
        grid.Columns.Add(MakeCol("CapacityF", "容量(F)", "CapacityF", 10));
        grid.Columns.Add(MakeCol("EsrMohm", "ESR(mΩ)", "EsrMohm", 10));
        grid.Columns.Add(MakeCol("LeakageUa", "漏电流(μA)", "LeakageUa", 11));
        grid.Columns.Add(MakeCol("SelfDischarge", "自放电率(%)", "SelfDischarge", 11));
        grid.Columns.Add(MakeCol("Tester", "测试仪", "Tester", 10));
        var timeCol = MakeCol("TestTime", "测试时间", "TestTime", 15);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        grid.Columns.Add(timeCol);
        grid.Columns.Add(MakeCol("Passed", "判定", "Passed", 8));

        grid.DataSource = _trace?.Tests ?? new List<TestRecord>();

        grid.CellFormatting -= TestGrid_CellFormatting;
        grid.CellFormatting += TestGrid_CellFormatting;
    }

    // ================= 网格着色 =================

    /// <summary>材料批次：到货日期缺省显示"—"；IQC 不合格红 / 合格绿</summary>
    private void MaterialGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        var colName = grid.Columns[e.ColumnIndex].Name;

        if (colName == "ReceivedDate" && e.Value is DateTime dt && dt == default)
        {
            e.Value = "—";
            e.FormattingApplied = true;
            return;
        }

        if (colName == "IqcResult")
        {
            var text = e.Value?.ToString() ?? "";
            if (text == "不合格")
            {
                e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
                e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            }
            else if (text == "合格")
            {
                e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);
            }
        }
    }

    /// <summary>工序参数：生产该工序时设备非"运行"（待机/停机/维护）的行整行标红；运行/—/空保持默认。</summary>
    private void ProcessGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Rows[e.RowIndex].DataBoundItem is not ProcessRecord p) return;

        var statusAt = p.DeviceStatusAt;
        if (string.IsNullOrWhiteSpace(statusAt) || statusAt == DeviceStatus.Running || statusAt == "—") return;

        // 仅命中非运行行：整行浅红底 + 深红字；选中态用红系保证可读
        e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
        e.CellStyle.BackColor = Color.FromArgb(255, 235, 235);
        e.CellStyle.SelectionForeColor = Color.FromArgb(192, 0, 0);
        e.CellStyle.SelectionBackColor = Color.FromArgb(255, 200, 200);
    }

    /// <summary>测试数据：判定列 bool → 合格/不合格文本，绿/红着色</summary>
    private void TestGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name != "Passed") return;

        if (e.Value is bool passed)
        {
            e.Value = passed ? "合格" : "不合格";
            e.FormattingApplied = true;
            e.CellStyle.ForeColor = passed ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 0, 0);
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
    }

    // ================= 底部按钮条 =================

    private Control BuildBottomBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.WhiteSmoke
        };
        var btnClose = new Button
        {
            Text = "关闭",
            Width = 100,
            Height = 32,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        btnClose.Location = new Point(bar.Width - 116, 9);
        bar.Resize += (_, _) => btnClose.Location = new Point(bar.Width - 116, 9);
        btnClose.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        bar.Controls.Add(btnClose);
        return bar;
    }

    // ================= 通用控件工厂 =================

    /// <summary>手写列工厂：Name / HeaderText / DataPropertyName 全部显式赋值（历史坑：列必须显式 Name）</summary>
    private static DataGridViewTextBoxColumn MakeCol(string name, string header, string prop, int fillWeight) =>
        new()
        {
            Name = name,
            HeaderText = header,
            DataPropertyName = prop,
            ReadOnly = true,
            FillWeight = fillWeight
        };

    private static DataGridView MakeGrid()
    {
        var g = new DataGridView
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
        return g;
    }

    private static Control MakeGridGroup(string title, out DataGridView grid)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6, 12, 6, 6) };
        grid = MakeGrid();
        box.Controls.Add(grid);
        return box;
    }
}
