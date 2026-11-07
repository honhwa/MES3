using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 保留明细窗体：双击「保留管理」页保留单行后弹出，
/// 顶部展示保留单概要，主体以只读表格展示该保留组内被保留电芯的明细
/// （SN/型号/线体/当前状态/分档/极片批号/电解液批号/生产时间）。
/// 数据按 holdNo 实时查询（MesDatabase.GetHoldItemDetails），保持数据新鲜。
/// 控件宣告与静态配置见 HoldDetailForm.Designer.cs。
/// </summary>
public partial class HoldDetailForm : Form
{
    private readonly string _holdNo;

    // 概要标签引用（保留单可能不存在/已变更，均按最新数据刷新文本）；栏位宣告见 HoldDetailForm.Designer.cs

    public HoldDetailForm(string holdNo)
    {
        _holdNo = string.IsNullOrWhiteSpace(holdNo) ? "" : holdNo.Trim();
        InitializeComponent();
        InitializeRuntime(holdNo);
    }

    /// <summary>
    /// 执行期初始化：Text 依赖 readonly 状态栏位（插值字串）。
    /// 静态控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()（含原 Controls.Add 顺序）；
    /// 此处仅串联仍依赖执行期资料的 helper。
    /// </summary>
    private void InitializeRuntime(string holdNo)
    {
        Text = $"保留明细 - {(_holdNo.Length == 0 ? "未知" : _holdNo)}";
        BuildUi();
    }

    /// <summary>
    /// 原 BuildUi() 的静态段落（概要面板 / 表格群组 / 底部按钮条之 new 与 Controls.Add）
    /// 已全部展开至 HoldDetailForm.Designer.cs；此处仅串联动态初始化，保持原呼叫顺序。
    /// </summary>
    private void BuildUi()
    {
        BuildSummaryPanel();
        ApplyGridStyles(_detailGrid);
        BindDetailGrid();
    }

    // ================= 顶部概要 =================

    /// <summary>
    /// 原 BuildSummaryPanel() 的静态段落（Panel / TableLayoutPanel / 各 Label 之 new、
    /// ColumnStyles / RowStyles / Controls.Add / SetColumnSpan）已全部展开至
    /// HoldDetailForm.Designer.cs；此处仅保留依赖 MesDatabase 即时查询的插值文字与条件着色。
    /// MakeInfoLabel() 的静态属性（Dock / TextAlign / ForeColor / AutoEllipsis）亦已展开。
    /// </summary>
    private void BuildSummaryPanel()
    {
        var hold = MesDatabase.Holds.FirstOrDefault(h => h.HoldNo == _holdNo);

        _lblHoldNo.Text = $"保留编号：{_holdNo}";
        _lblLocation.Text = $"保留位置：{hold?.HoldLocation ?? "—"}";
        _lblStatus.Text = $"状态：{hold?.Status ?? "—"}";
        _lblCount.Text = $"电芯数：{MesDatabase.GetHoldItems(_holdNo).Count} 只";
        _lblOperator.Text = $"操作人：{hold?.Operator ?? "—"}";
        _lblHoldTime.Text = $"保留时间：{(hold is null ? "—" : hold.HoldTime.ToString("yyyy-MM-dd HH:mm"))}";

        // 保留中/已解除着色：与主窗体保留管理页风格一致
        if (hold is not null)
        {
            var statusColor = hold.Status == HoldStatus.Active
                ? Color.FromArgb(192, 0, 0)
                : Color.FromArgb(39, 174, 96);
            _lblStatus.ForeColor = statusColor;
            _lblStatus.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }

        _lblReason.Text = $"保留原因：{hold?.Reason ?? "—"}";

        // 第三行：解除信息（若已解除）与材料信息，作为补充概要
        var extra = new List<string>();
        if (hold is not null && hold.Status == HoldStatus.Released)
            extra.Add($"解除人：{hold.ReleaseOperator} / 解除时间：{(hold.ReleaseTime.HasValue ? hold.ReleaseTime.Value.ToString("yyyy-MM-dd HH:mm") : "—")}");
        if (hold is not null && !string.IsNullOrEmpty(hold.LotNo))
            extra.Add($"关联批次：{hold.LotNo}");
        lblExtra.Text = extra.Count == 0 ? "" : string.Join("    ", extra);
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

    // ================= 明细网格 =================

    /// <summary>
    /// 原 MakeGrid() 的三个 DataGridViewCellStyle 物件初始设定式（Designer 无法解析）。
    /// 其余静态属性已由 InitializeComponent() 设定。
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

    private void BindDetailGrid()
    {
        var rows = MesDatabase.GetHoldItemDetails(_holdNo);

        _detailGrid.AutoGenerateColumns = false;
        _detailGrid.DataSource = null;
        _detailGrid.Columns.Clear();

        // 注意：手写列必须显式设置 Name，否则 row.Cells["xx"] 会抛异常
        _detailGrid.Columns.Add(MakeCol("CellSn", "电芯SN", "CellSn", 18));
        _detailGrid.Columns.Add(MakeCol("ProductModel", "型号", "ProductModel", 10));
        _detailGrid.Columns.Add(MakeCol("Line", "线体", "Line", 6));
        _detailGrid.Columns.Add(MakeCol("Status", "当前状态", "Status", 8));
        _detailGrid.Columns.Add(MakeCol("Result", "分档", "Result", 6));
        _detailGrid.Columns.Add(MakeCol("ElectrodeLot", "极片批号", "ElectrodeLot", 13));
        _detailGrid.Columns.Add(MakeCol("ElectrolyteLot", "电解液批号", "ElectrolyteLot", 13));
        var timeCol = MakeCol("CreateTime", "生产时间", "CreateTime", 14);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _detailGrid.Columns.Add(timeCol);

        _detailGrid.DataSource = rows;

        _detailGrid.CellFormatting -= DetailGrid_CellFormatting;
        _detailGrid.CellFormatting += DetailGrid_CellFormatting;
    }

    /// <summary>状态/分档/生产时间着色与缺省值展示：不良与 NG 标红，合格标绿，缺省时间显示"—"</summary>
    private void DetailGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        var colName = grid.Columns[e.ColumnIndex].Name;

        if (colName == "CreateTime" && e.Value is DateTime dt && dt == default)
        {
            e.Value = "—";
            e.FormattingApplied = true;
            return;
        }

        if (colName is "Status" or "Result")
        {
            var text = e.Value?.ToString() ?? "";
            if (text == "不良" || text == "NG")
            {
                e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
                e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            }
            else if (text == "合格" || text == "OK" || text == "A1" || text == "A2" || text == "B1")
            {
                e.CellStyle.ForeColor = Color.FromArgb(39, 174, 96);
            }
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
}
