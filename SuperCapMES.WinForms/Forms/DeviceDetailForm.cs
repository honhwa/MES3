using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 设备详情窗体：
/// 顶部设备信息 + 本日/本月产量醒目数字；中部近30天日产量折线趋势图（GDI+ 手绘，零外部依赖）；
/// 下部三页签：每小时产量/OEE 达成率表 + 当日异常列表 + 当天状态履历。
/// </summary>
public class DeviceDetailForm : Form
{
    private readonly Equipment _equipment;
    private readonly List<DeviceDailyProduction> _daily;
    private readonly PictureBox _chartBox;

    // 供自动刷新更新的控件引用
    private DataGridView _hourlyGrid = null!;
    private DataGridView _alarmGrid = null!;
    private DataGridView _statusGrid = null!;
    private Button _btnAlarmToAbnormal = null!;
    private Label _lblTodayValue = null!;
    private Label _lblMonthValue = null!;

    // 自动刷新（底部状态栏 + 定时器，与主窗体风格一致）
    private System.Windows.Forms.Timer _autoTimer = null!;
    private CheckBox _chkAutoRefresh = null!;
    private NumericUpDown _numMinutes = null!;
    private Label _lblAutoStatus = null!;
    private DateTime _nextRefreshTime;
    private const int DefaultRefreshMinutes = 5;
    private const int MinRefreshMinutes = 1;
    private const int MaxRefreshMinutes = 60;

    /// <summary>双缓冲 PictureBox，避免趋势图重绘闪烁</summary>
    private sealed class DoubleBufferedPictureBox : PictureBox
    {
        public DoubleBufferedPictureBox()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
        }
    }

    public DeviceDetailForm(Equipment equipment)
    {
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        _daily = MesDatabase.GetDailyProductions(equipment.Code);

        Text = $"设备详情 - {equipment.Name}（{equipment.Code}）";
        Font = new Font("微软雅黑", 9F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(840, 620);

        _chartBox = new DoubleBufferedPictureBox { Dock = DockStyle.Fill };
        _chartBox.Paint += ChartBox_Paint;

        BuildUi();

        // 自动刷新：底部状态栏 + 定时器（默认停用，间隔 5 分钟）
        _autoTimer = new System.Windows.Forms.Timer { Interval = DefaultRefreshMinutes * 60_000 };
        _autoTimer.Tick += AutoTimer_Tick;
        Controls.Add(BuildAutoRefreshBar());
        UpdateAutoRefreshStatus();

        FormClosed += (_, _) => _autoTimer.Stop();
    }

    private void BuildUi()
    {
        // 顶部信息区
        var top = BuildInfoPanel();

        // 中部趋势图
        var chartGroup = new GroupBox
        {
            Text = "近30天日产量趋势图",
            Dock = DockStyle.Top,
            Height = 260,
            Padding = new Padding(8, 14, 8, 8)
        };
        chartGroup.Controls.Add(_chartBox);

        // 下部三页签：每小时产量与OEE / 当日异常 / 当天状态履历
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabHourly = new TabPage("每小时产量与OEE");
        tabHourly.Controls.Add(MakeGridGroup("每小时产量与 OEE 达成率（当日 0-23 点，目标 OEE 85%）", out var hourlyGrid));
        var tabAlarm = new TabPage("当日异常");
        var alarmGroup = MakeGridGroup("当日异常情况（选中一行 → 底部「转异常登记」可自动生成异常单）", out var alarmGrid);
        var alarmBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Padding = new Padding(8, 6, 4, 0),
            WrapContents = false
        };
        _btnAlarmToAbnormal = MakeStatusButton("转异常登记", BtnAlarmToAbnormal);
        _btnAlarmToAbnormal.Click -= BtnAlarmToAbnormal;
        _btnAlarmToAbnormal.Click += BtnAlarmToAbnormal;
        _btnAlarmToAbnormal.Enabled = false;
        alarmBar.Controls.Add(_btnAlarmToAbnormal);
        var alarmPanel = new Panel { Dock = DockStyle.Fill };
        alarmPanel.Controls.Add(alarmGroup);   // Fill
        alarmPanel.Controls.Add(alarmBar);     // Bottom
        tabAlarm.Controls.Add(alarmPanel);
        var tabStatus = new TabPage("当天状态履历");
        tabStatus.Controls.Add(MakeGridGroup("当天状态履历（0 点起按开始时间升序）", out var statusGrid));
        tabs.TabPages.Add(tabHourly);
        tabs.TabPages.Add(tabAlarm);
        tabs.TabPages.Add(tabStatus);

        // Dock 布局：后添加的控件先贴边（顶部），故先加 fill 再依次加顶栏
        Controls.Add(tabs);
        Controls.Add(chartGroup);
        Controls.Add(top);

        // 保存表格引用，供自动刷新重新绑定
        _hourlyGrid = hourlyGrid;
        _alarmGrid = alarmGrid;
        _statusGrid = statusGrid;
        BindHourlyGrid(hourlyGrid);
        BindAlarmGrid(alarmGrid);
        BindStatusGrid(statusGrid);
    }

    // ================= 顶部信息 =================

    private Control BuildInfoPanel()
    {
        var todayOutput = MesDatabase.GetTodayOutput(_equipment.Code);
        var monthOutput = MesDatabase.GetMonthOutput(_equipment.Code);

        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 118,
            BackColor = Color.White,
            Padding = new Padding(12, 8, 12, 8)
        };

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 第一行：设备基础信息
        var statusColor = _equipment.Status switch
        {
            "运行" => Color.FromArgb(39, 174, 96),
            "维护" => Color.FromArgb(230, 126, 34),
            "停机" => Color.FromArgb(192, 57, 43),
            _ => Color.FromArgb(80, 90, 105)
        };
        grid.Controls.Add(MakeInfoCell($"设备编号：{_equipment.Code}"), 0, 0);
        grid.Controls.Add(MakeInfoCell($"设备名称：{_equipment.Name}"), 1, 0);
        grid.Controls.Add(MakeInfoCell($"工序段：{_equipment.Section}"), 2, 0);
        var statusCell = MakeInfoCell($"状态：{_equipment.Status}");
        statusCell.ForeColor = statusColor;
        grid.Controls.Add(statusCell, 3, 0);

        // 第二行：本日 / 本月产量醒目数字（各跨两列）
        var todayCard = MakeStatCard("本日产量（只）", todayOutput.ToString("N0"), Color.FromArgb(15, 76, 129), out _lblTodayValue);
        grid.Controls.Add(todayCard, 0, 1);
        grid.SetColumnSpan(todayCard, 2);

        var monthCard = MakeStatCard("本月产量（只）", monthOutput.ToString("N0"), Color.FromArgb(230, 126, 34), out _lblMonthValue);
        grid.Controls.Add(monthCard, 2, 1);
        grid.SetColumnSpan(monthCard, 2);

        panel.Controls.Add(grid);
        return panel;
    }

    private static Label MakeInfoCell(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            AutoEllipsis = true
        };

    private static Control MakeStatCard(string title, string value, Color accent, out Label valueLabel)
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 2, 4, 2),
            BackColor = Color.FromArgb(245, 248, 252),
            BorderStyle = BorderStyle.FixedSingle
        };
        var t = new Label
        {
            Text = title,
            Location = new Point(12, 6),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("微软雅黑", 9F)
        };
        var v = new Label
        {
            Text = value,
            Location = new Point(12, 26),
            AutoSize = true,
            ForeColor = accent,
            Font = new Font("微软雅黑", 18F, FontStyle.Bold)
        };
        valueLabel = v;
        p.Controls.Add(t);
        p.Controls.Add(v);
        return p;
    }

    // ================= 近30天日产量趋势图（GDI+ 手绘） =================

    private void ChartBox_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = _chartBox.ClientRectangle;
        if (rect.Width < 160 || rect.Height < 100) return;

        const int padL = 58;
        const int padR = 20;
        const int padT = 36;
        const int padB = 32;
        var plot = new Rectangle(
            padL,
            padT,
            Math.Max(10, rect.Width - padL - padR),
            Math.Max(10, rect.Height - padT - padB));

        var values = _daily.Select(d => d.Output).ToList();
        if (values.Count == 0) return;

        var maxV = values.Max();
        var minV = values.Min();
        var step = maxV switch
        {
            <= 1000 => 100,
            <= 2000 => 200,
            <= 5000 => 500,
            <= 10000 => 1000,
            _ => 2000
        };
        var yMax = maxV <= 0 ? step : (int)Math.Ceiling(maxV / (double)step) * step;

        // 标题
        using var titleFont = new Font("微软雅黑", 10.5F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(15, 76, 129));
        var titleSize = g.MeasureString("近30天日产量趋势", titleFont);
        g.DrawString("近30天日产量趋势", titleFont, titleBrush, (rect.Width - titleSize.Width) / 2f, 5);

        using var labelFont = new Font("微软雅黑", 8F);
        using var labelBrush = new SolidBrush(Color.FromArgb(90, 100, 115));
        g.DrawString("单位：只", labelFont, labelBrush, rect.Right - 62f, 10);

        // 网格线 + Y 轴刻度
        using var gridPen = new Pen(Color.FromArgb(228, 233, 240), 1);
        using var axisPen = new Pen(Color.FromArgb(120, 130, 145), 1);
        const int gridLines = 4;
        for (var i = 0; i <= gridLines; i++)
        {
            var y = plot.Bottom - (int)(plot.Height * i / (double)gridLines);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var val = yMax * i / gridLines;
            g.DrawString(val.ToString("N0"), labelFont, labelBrush, plot.Left - 50f, y - 8f);
        }

        // X 轴日期（每 5 天一个标签）
        for (var i = 0; i < _daily.Count; i += 5)
        {
            var x = plot.Left + (int)(plot.Width * i / (double)(_daily.Count - 1));
            g.DrawString(_daily[i].Date.ToString("M/d"), labelFont, labelBrush, x - 14f, plot.Bottom + 8f);
        }

        g.DrawRectangle(axisPen, plot);
        if (_daily.Count <= 1) return;

        // 折线 + 数据点
        var pts = new PointF[_daily.Count];
        for (var i = 0; i < _daily.Count; i++)
        {
            var x = plot.Left + (int)(plot.Width * i / (double)(_daily.Count - 1));
            var yRatio = values[i] / (double)yMax;
            var y = plot.Bottom - (int)(yRatio * plot.Height);
            pts[i] = new PointF(x, y);
        }

        using var linePen = new Pen(Color.FromArgb(41, 128, 185), 2.2f);
        g.DrawLines(linePen, pts);

        using var dotBrush = new SolidBrush(Color.FromArgb(41, 128, 185));
        foreach (var p in pts)
            g.FillEllipse(dotBrush, p.X - 2.5f, p.Y - 2.5f, 5f, 5f);

        // 最大 / 最小值标注
        var maxIdx = values.IndexOf(maxV);
        var minIdx = values.IndexOf(minV);
        using var maxBrush = new SolidBrush(Color.FromArgb(192, 57, 43));
        using var minBrush = new SolidBrush(Color.FromArgb(39, 174, 96));
        using var annFont = new Font("微软雅黑", 8F, FontStyle.Bold);

        g.FillEllipse(maxBrush, pts[maxIdx].X - 4f, pts[maxIdx].Y - 4f, 8f, 8f);
        var maxText = $"Max {maxV:N0}";
        var maxTextSize = g.MeasureString(maxText, annFont);
        g.DrawString(maxText, annFont, maxBrush, pts[maxIdx].X - maxTextSize.Width / 2f, Math.Max(2f, pts[maxIdx].Y - 18f));

        if (minIdx != maxIdx)
        {
            g.FillEllipse(minBrush, pts[minIdx].X - 4f, pts[minIdx].Y - 4f, 8f, 8f);
            var minText = $"Min {minV:N0}";
            g.DrawString(minText, annFont, minBrush, pts[minIdx].X - 20f, Math.Min(plot.Bottom - 16f, pts[minIdx].Y + 8f));
        }
    }

    // ================= 底部表格 =================

    private void BindHourlyGrid(DataGridView grid)
    {
        var records = MesDatabase.GetHourlyRecords(_equipment.Code);
        grid.AutoGenerateColumns = false;
        grid.DataSource = null;
        grid.Columns.Clear();

        // 注意：手写列必须显式设置 Name，否则 row.Cells["xx"] 会抛异常
        var hourCol = MakeCol("Hour", "小时", "Hour", 14);
        var outputCol = MakeCol("Output", "产量(只)", "Output", 20);
        var oeeCol = MakeCol("OeeRate", "OEE(%)", "OeeRate", 20);
        oeeCol.DefaultCellStyle.Format = "0.0";
        var achCol = MakeCol("AchievementRate", "达成率(%)", "AchievementRate", 20);
        achCol.DefaultCellStyle.Format = "0.0";

        grid.Columns.AddRange(hourCol, outputCol, oeeCol, achCol);
        grid.DataSource = records;

        // 重复刷新时先移除旧处理器，避免事件累积；小时显示为 08:00，达成率按档位着色
        grid.CellFormatting -= HourlyGrid_CellFormatting;
        grid.CellFormatting += HourlyGrid_CellFormatting;
    }

    private void HourlyGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name == "Hour" && e.Value is int h)
            e.Value = $"{h:00}:00";
        if (grid.Columns[e.ColumnIndex].Name == "AchievementRate" && e.Value is double ach)
            e.CellStyle.ForeColor = ach >= 100 ? Color.FromArgb(39, 174, 96)
                : ach >= 90 ? Color.FromArgb(41, 128, 185)
                : Color.FromArgb(230, 126, 34);
    }

    private void BindAlarmGrid(DataGridView grid)
    {
        var alarms = MesDatabase.GetAlarms(_equipment.Code);
        grid.AutoGenerateColumns = false;
        grid.DataSource = null;
        grid.Columns.Clear();

        var timeCol = MakeCol("Time", "时间", "Time", 15);
        timeCol.DefaultCellStyle.Format = "HH:mm:ss";
        var typeCol = MakeCol("AlarmType", "类型", "AlarmType", 13);
        var msgCol = MakeCol("Message", "描述", "Message", 40);
        var durCol = MakeCol("DurationMin", "时长(分)", "DurationMin", 11);
        var statusCol = MakeCol("Status", "状态", "Status", 11);
        var linkedCol = MakeCol("LinkedAbnormalNo", "关联异常单", "LinkedAbnormalNo", 14);

        grid.Columns.AddRange(timeCol, typeCol, msgCol, durCol, statusCol, linkedCol);
        grid.DataSource = alarms;

        // 重复刷新时先移除旧处理器，避免事件累积；状态着色：未处理红 / 处理中橙 / 已处理绿
        grid.CellFormatting -= AlarmGrid_CellFormatting;
        grid.CellFormatting += AlarmGrid_CellFormatting;
        grid.SelectionChanged -= AlarmGrid_SelectionChanged;
        grid.SelectionChanged += AlarmGrid_SelectionChanged;
        AlarmGrid_SelectionChanged(grid, EventArgs.Empty);
    }

    private void AlarmGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name == "Status" && e.Value is string s)
        {
            e.CellStyle.ForeColor = s switch
            {
                "未处理" => Color.FromArgb(192, 57, 43),
                "处理中" => Color.FromArgb(230, 126, 34),
                _ => Color.FromArgb(39, 174, 96)
            };
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
    }

    /// <summary>报警表选中行变化：选中未关联异常单的报警时启用「转异常登记」</summary>
    private void AlarmGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_btnAlarmToAbnormal is null) return;
        var a = SelectedAlarm();
        _btnAlarmToAbnormal.Enabled = a is not null && string.IsNullOrWhiteSpace(a.LinkedAbnormalNo);
    }

    private DeviceAlarm? SelectedAlarm() =>
        _alarmGrid.SelectedRows.Count == 0 ? null : _alarmGrid.SelectedRows[0].DataBoundItem as DeviceAlarm;

    /// <summary>质量事件自动联动：将选中的设备报警转登为异常单（设备异常）。</summary>
    private void BtnAlarmToAbnormal(object? sender, EventArgs e)
    {
        var a = SelectedAlarm();
        if (a is null) return;
        if (!string.IsNullOrWhiteSpace(a.LinkedAbnormalNo))
        {
            MessageBox.Show($"该报警已关联异常单 {a.LinkedAbnormalNo}，无需重复转单。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show($"将设备 {_equipment.Name}({a.DeviceCode}) 的报警转登为异常单？\n转登后可在「异常管理」中跟踪处置。",
                "转异常登记", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var (ok, msg, abNo) = MesDatabase.CreateAbnormalFromDeviceAlarm(a.DeviceCode, a.Time, "设备监控系统");
        MessageBox.Show(msg, "转异常登记", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) AlarmGrid_SelectionChanged(_alarmGrid, EventArgs.Empty);
        BindAlarmGrid(_alarmGrid);   // 刷新关联异常单显示
    }

    private void BindStatusGrid(DataGridView grid)
    {
        var records = MesDatabase.GetStatusHistory(_equipment.Code);
        grid.AutoGenerateColumns = false;
        grid.DataSource = null;
        grid.Columns.Clear();

        // 注意：手写列必须显式设置 Name，否则 row.Cells["xx"] 会抛异常
        var startCol = MakeCol("StartTime", "开始时间", "StartTime", 16);
        startCol.DefaultCellStyle.Format = "HH:mm";
        var statusCol = MakeCol("Status", "状态", "Status", 10);
        var durCol = MakeCol("DurationMin", "持续(分)", "DurationMin", 10);
        var noteCol = MakeCol("Note", "说明", "Note", 34);

        grid.Columns.AddRange(startCol, statusCol, durCol, noteCol);
        grid.DataSource = records;

        // 重复刷新时先移除旧处理器，避免事件累积；状态着色：运行绿 / 待机灰蓝 / 停机红 / 维护橙
        grid.CellFormatting -= StatusGrid_CellFormatting;
        grid.CellFormatting += StatusGrid_CellFormatting;
    }

    private void StatusGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name == "Status" && e.Value is string s)
        {
            e.CellStyle.ForeColor = s switch
            {
                DeviceStatus.Running => Color.FromArgb(39, 174, 96),
                DeviceStatus.Standby => Color.FromArgb(93, 109, 126),
                DeviceStatus.Stopped => Color.FromArgb(192, 57, 43),
                DeviceStatus.Maintenance => Color.FromArgb(230, 126, 34),
                _ => Color.Black
            };
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
    }

    /// <summary>手写列工厂：Name / HeaderText / DataPropertyName 全部显式赋值</summary>
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

    // ================= 自动刷新 =================
    /// <summary>构建底部自动刷新状态栏：CheckBox 开关 + 分钟输入 + 应用 + 立即刷新 + 状态文本（深蓝主题，与主窗体一致）</summary>
    private StatusStrip BuildAutoRefreshBar()
    {
        var bar = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = false,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Padding = new Padding(4, 2, 6, 2)
        };

        _chkAutoRefresh = new CheckBox
        {
            Text = "自动刷新",
            Checked = false,
            AutoSize = true,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Margin = new Padding(2, 4, 2, 4)
        };

        _numMinutes = new NumericUpDown
        {
            Minimum = MinRefreshMinutes,
            Maximum = MaxRefreshMinutes,
            DecimalPlaces = 0,
            Increment = 1,
            Value = DefaultRefreshMinutes,
            Width = 56,
            TextAlign = HorizontalAlignment.Center
        };

        var lblMinutes = new Label
        {
            Text = "分钟",
            AutoSize = true,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Margin = new Padding(2, 4, 2, 4)
        };

        var btnApply = MakeStatusButton("应用", BtnApplyRefreshSettings);
        var btnRefreshNow = MakeStatusButton("立即刷新", BtnRefreshNow);

        _lblAutoStatus = new Label
        {
            AutoSize = true,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Margin = new Padding(10, 4, 2, 4),
            Text = "自动刷新：已停用"
        };

        _chkAutoRefresh.CheckedChanged += ChkAutoRefresh_CheckedChanged;

        bar.Items.Add(new ToolStripControlHost(_chkAutoRefresh) { Margin = new Padding(4, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(_numMinutes) { Margin = new Padding(4, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(lblMinutes) { Margin = new Padding(2, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(btnApply) { Margin = new Padding(8, 0, 0, 0) });
        bar.Items.Add(new ToolStripControlHost(btnRefreshNow) { Margin = new Padding(6, 0, 0, 0) });
        bar.Items.Add(new ToolStripStatusLabel(" ") { Spring = true });
        bar.Items.Add(new ToolStripControlHost(_lblAutoStatus) { Margin = new Padding(0, 0, 4, 0) });

        return bar;
    }

    /// <summary>紧凑状态栏按钮（白底深蓝字，与深蓝状态栏呼应）</summary>
    private static Button MakeStatusButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(82, 26),
            Margin = new Padding(2, 0, 2, 0),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(15, 76, 129),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        b.Click += onClick;
        return b;
    }

    /// <summary>「自动刷新」勾选变化：勾选即按当前分钟数启用，取消即停用</summary>
    private void ChkAutoRefresh_CheckedChanged(object? sender, EventArgs e) => ApplyAutoRefreshSettings();

    /// <summary>「应用」：读取分钟数（1~60）并应用到定时器；非法输入提示并回退</summary>
    private void BtnApplyRefreshSettings(object? sender, EventArgs e) => ApplyAutoRefreshSettings();

    /// <summary>「立即刷新」：手动触发一次 RefreshDeviceData，刷新后重置下次刷新时间</summary>
    private void BtnRefreshNow(object? sender, EventArgs e)
    {
        RefreshDeviceData();
        if (_autoTimer.Enabled)
            _nextRefreshTime = DateTime.Now.AddMilliseconds(_autoTimer.Interval);
        UpdateAutoRefreshStatus();
    }

    /// <summary>统一应用设置：校验分钟数(1~60) → 设置 Interval(分钟×60000ms) → 按勾选状态启停 → 更新状态文本</summary>
    private void ApplyAutoRefreshSettings()
    {
        var minutes = (int)Math.Round(_numMinutes.Value);
        if (minutes < MinRefreshMinutes || minutes > MaxRefreshMinutes)
        {
            Warn($"刷新间隔需在 {MinRefreshMinutes}~{MaxRefreshMinutes} 分钟之间，已恢复为 {DefaultRefreshMinutes} 分钟。");
            _numMinutes.Value = DefaultRefreshMinutes;
            return;
        }

        _autoTimer.Interval = minutes * 60_000;
        if (_chkAutoRefresh.Checked)
        {
            _autoTimer.Start();
            _nextRefreshTime = DateTime.Now.AddMilliseconds(_autoTimer.Interval);
        }
        else
        {
            _autoTimer.Stop();
            _nextRefreshTime = DateTime.MinValue;
        }
        UpdateAutoRefreshStatus();
    }

    /// <summary>定时器到点：先停避免重入 → 刷新详情页全部数据 → 更新「下次刷新」时间并恢复运行</summary>
    private void AutoTimer_Tick(object? sender, EventArgs e)
    {
        _autoTimer.Stop();
        try
        {
            RefreshDeviceData();
            if (_chkAutoRefresh.Checked)
            {
                _nextRefreshTime = DateTime.Now.AddMilliseconds(_autoTimer.Interval);
                _autoTimer.Start();
            }
        }
        finally
        {
            UpdateAutoRefreshStatus();
        }
    }

    /// <summary>更新状态栏文本：启用 → 间隔 + 下次刷新时间；停用 → 已停用</summary>
    private void UpdateAutoRefreshStatus()
    {
        if (_lblAutoStatus is null) return;
        if (_autoTimer is not null && _autoTimer.Enabled)
        {
            var minutes = (int)Math.Round(_autoTimer.Interval / 60_000.0);
            _lblAutoStatus.Text = $"自动刷新：启用 · 每 {minutes} 分钟 · 下次刷新 {_nextRefreshTime:HH:mm:ss}";
        }
        else
        {
            _lblAutoStatus.Text = "自动刷新：已停用";
        }
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // ================= 刷新 =================
    /// <summary>刷新设备详情页全部数据：产量 Label + 趋势图（更新字段后重绘）+ 小时表 + 异常表 + 状态履历表</summary>
    private void RefreshDeviceData()
    {
        _lblTodayValue.Text = MesDatabase.GetTodayOutput(_equipment.Code).ToString("N0");
        _lblMonthValue.Text = MesDatabase.GetMonthOutput(_equipment.Code).ToString("N0");

        // 趋势图 Paint 读取 _daily 字段缓存，先更新字段再触发重绘
        _daily.Clear();
        _daily.AddRange(MesDatabase.GetDailyProductions(_equipment.Code));
        _chartBox.Invalidate();

        BindHourlyGrid(_hourlyGrid);
        BindAlarmGrid(_alarmGrid);
        BindStatusGrid(_statusGrid);
    }
}
