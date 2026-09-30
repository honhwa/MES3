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
/// MainForm 分部类（按功能域拆分，第 3 部分）：生产看板页（今日产量/WIP/直通率/设备运行 + 进行中工单 + 设备总览）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Dashboard」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 生产看板 =================
    /// <summary>构建「Dashboard」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildDashboardTab()
    {
        var page = new TabPage("生产看板");

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 96,
            Padding = new Padding(6, 4, 6, 4)
        };
        flow.Controls.Add(MakeCard("今日产量 (只)", Color.FromArgb(15, 76, 129), out _lblOutput));
        flow.Controls.Add(MakeCard("在制数量 WIP (只)", Color.FromArgb(230, 126, 34), out _lblWip));
        flow.Controls.Add(MakeCard("测试直通率 (%)", Color.FromArgb(39, 174, 96), out _lblYield));
        flow.Controls.Add(MakeCard("设备运行 (台)", Color.FromArgb(23, 122, 139), out _lblRunningEq));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 260,
            BackColor = SystemColors.Control
        };

        var woPanel = MakeGridGroup("进行中的生产工单", out _dashWoGrid);
        split.Panel1.Controls.Add(woPanel);

        var eqPanel = MakeGridGroup("设备状态总览", out _dashEqGrid);
        split.Panel2.Controls.Add(eqPanel);

        var root = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(split);
        root.Controls.Add(flow);
        page.Controls.Add(root);
        return page;
    }


}
