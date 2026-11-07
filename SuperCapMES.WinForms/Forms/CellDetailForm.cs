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
/// 控件宣告与静态配置见 CellDetailForm.Designer.cs。
/// </summary>
public partial class CellDetailForm : Form
{
    private readonly string _sn;
    private readonly TraceResult? _trace;

    public CellDetailForm(string sn)
    {
        _sn = string.IsNullOrWhiteSpace(sn) ? "" : sn.Trim();
        // 未找到 SN 时 GetTrace 返回 null（或 Cell 为 null），下方做防御处理，不抛异常
        _trace = MesDatabase.GetTrace(_sn);

        InitializeComponent();
        InitializeRuntime(sn);
    }

    /// <summary>
    /// 执行期初始化：Text 依赖 readonly 状态栏位（插值字串）；
    /// 未找到 SN 时条件式挂上 Shown 提示（具名方法），挂接时机依赖 _trace，留在原位置。
    /// 静态控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()（含原 Controls.Add 顺序）。
    /// </summary>
    private void InitializeRuntime(string sn)
    {
        Text = $"电芯详情 - {(_sn.Length == 0 ? "未知" : _sn)}";

        BuildUi();

        if (_trace?.Cell is null)
        {
            // 未找到：窗体正常展示后提示一次并自动关闭（ShowDialog 正常返回）
            Shown += CellDetailForm_Shown;
        }
    }

    private void CellDetailForm_Shown(object? sender, EventArgs e)
    {
        MessageBox.Show($"未找到电芯 SN：{_sn}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ================= 整体布局 =================

    /// <summary>
    /// 原 BuildUi() 与 BuildDetailTabs() 的静态段落（概要面板 / 三页签 / 三份网格 /
    /// 底部按钮条之 new、ColumnStyles / RowStyles / Controls.Add / TabPages.Add / SetColumnSpan）
    /// 已全部展开至 CellDetailForm.Designer.cs；此处仅串联动态初始化，保持原呼叫顺序。
    /// </summary>
    private void BuildUi()
    {
        BuildSummaryPanel();

        ApplyGridStyles(matGrid);
        ApplyGridStyles(procGrid);
        ApplyGridStyles(testGrid);
        BindMaterialGrid(matGrid);
        BindProcessGrid(procGrid);
        BindTestGrid(testGrid);
    }

    // ================= 顶部概要 =================

    /// <summary>
    /// 原 BuildSummaryPanel() 的静态段落（Panel / TableLayoutPanel / 各 Label 之 new、
    /// ColumnStyles / RowStyles / Controls.Add / SetColumnSpan）已全部展开至
    /// CellDetailForm.Designer.cs；此处仅保留依赖 _trace 的插值文字与条件着色。
    /// MakeInfoLabel() 的静态属性（Dock / TextAlign / ForeColor / AutoEllipsis）亦已展开。
    /// </summary>
    private void BuildSummaryPanel()
    {
        var cell = _trace?.Cell;
        var statusText = cell is null ? "—" : cell.Status;

        // 第一行：基础身份信息
        lblSn.Text = $"SN：{Dash(cell?.Sn)}";
        lblModel.Text = $"产品型号：{Dash(cell?.ProductModel)}";
        lblLine.Text = $"产线：{Dash(cell?.Line)}";
        lblWo.Text = $"生产工单：{Dash(cell?.WoNo)}";

        // 第二行：状态 / 分档 / 创建时间 / 所属模组
        lblStatus.Text = $"当前状态：{Dash(statusText)}";
        lblResult.Text = $"分档：{ResultText(cell)}";
        lblCreate.Text = $"创建时间：{FmtDateTime(cell?.CreateTime)}";
        lblModule.Text = $"所属模组：{Dash(cell?.ModuleSn)}";

        // 第三行：极片批号 / 电解液批号 / 露点 / 温度
        lblElectrode.Text = $"极片批号：{Dash(cell?.ElectrodeLot)}";
        lblElectrolyte.Text = $"电解液批号：{Dash(cell?.ElectrolyteLot)}";
        lblDew.Text = $"露点：{Dash(_trace?.EnvDewPoint)}";
        lblTemp.Text = $"温度：{Dash(_trace?.EnvTemp)}";

        // 第四行：湿度 + 采样点（环境摘要尾行，跨 4 列）
        lblEnv.Text = BuildEnvLine();

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

    /// <summary>原 bar.Resize lambda：以执行期真实宽度重算 btnClose 位置（Designer 以方法群挂接）。</summary>
    private void BottomBar_Resize(object? sender, EventArgs e)
    {
        btnClose.Location = new Point(bottomPanel.Width - 116, 9);
    }

    /// <summary>原 btnClose.Click lambda。</summary>
    private void BtnClose_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ================= 通用控件工厂 =================

    /// <summary>
    /// 原 MakeGrid() 的三个 DataGridViewCellStyle 物件初始设定式（Designer 无法解析），
    /// 三份网格共用；其余静态属性已由 InitializeComponent() 设定。
    /// </summary>
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
}
