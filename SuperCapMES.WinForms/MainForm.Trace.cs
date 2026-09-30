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
/// MainForm 分部类（按功能域拆分，第 18 部分）：SN 追溯页（单芯全链路：材料批次 / 工序参数 / 测试数据 / 干燥房环境）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Trace」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= SN 追溯 =================
    /// <summary>构建「Trace」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildTraceTab()
    {
        var page = new TabPage("SN 追溯");
        page.Padding = new Padding(10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // 查询行
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));  // 电芯信息 + 环境
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));  // 材料批次
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));  // 工序记录
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 测试数据

        // ---- 查询行 ----
        var qRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        qRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        qRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        qRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        qRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        var lblQ = new Label { Text = "电芯 SN：", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };
        _txtSn = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 11F) };
        var btnQuery = new Button { Text = "查询追溯", Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 76, 129), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 6, 2, 6) };
        btnQuery.Click += (_, _) => DoTrace();
        _txtSn.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoTrace(); };

        _btnTraceExport = new Button { Text = "导出报告", Dock = DockStyle.Fill, BackColor = Color.FromArgb(39, 174, 96), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 6, 2, 6) };
        _btnTraceExport.Click += BtnTraceExport;

        qRow.Controls.Add(lblQ, 0, 0);
        qRow.Controls.Add(_txtSn, 1, 0);
        qRow.Controls.Add(btnQuery, 2, 0);
        qRow.Controls.Add(_btnTraceExport, 3, 0);
        root.Controls.Add(qRow, 0, 0);

        // ---- 电芯信息 + 环境 ----
        var infoRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var cellBox = new GroupBox { Text = "电芯信息", Dock = DockStyle.Fill };
        _lblCellInfo = new Label { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 0, 0), AutoSize = false };
        cellBox.Controls.Add(_lblCellInfo);

        var envBox = new GroupBox { Text = "干燥房环境（绑定）", Dock = DockStyle.Fill };
        _lblEnv = new Label { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 0, 0), AutoSize = false };
        envBox.Controls.Add(_lblEnv);

        infoRow.Controls.Add(cellBox, 0, 0);
        infoRow.Controls.Add(envBox, 1, 0);
        root.Controls.Add(infoRow, 0, 1);

        // ---- 材料批次 / 工序记录 / 测试数据 ----
        var matBox = MakeGridGroup("材料批次（正反向追溯）", out _matGrid);
        root.Controls.Add(matBox, 0, 2);
        var procBox = MakeGridGroup("工序参数记录", out _procGrid);
        // 生产时设备非运行（待机/停机/维护）的工序行整行标红；仅在创建处挂一次
        _procGrid.CellFormatting += ProcGrid_CellFormatting;
        root.Controls.Add(procBox, 0, 3);
        var testBox = MakeGridGroup("电性能全检数据", out _traceTestGrid);
        root.Controls.Add(testBox, 0, 4);

        page.Controls.Add(root);
        return page;
    }


}
