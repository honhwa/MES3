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
/// MainForm 分部类（按功能域拆分，第 4 部分）：设备监控页（设备台账 + 打开设备详情窗体）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Equipment」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 设备监控 =================
    /// <summary>构建「Equipments」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildEquipmentsTab()
    {
        var page = new TabPage("设备监控");
        page.Controls.Add(MakeGridGroup("设备状态与 OEE（双击设备行查看详情）", out _eqGrid));

        // 双击设备行 → 打开设备详情窗体（模态）；无效区域忽略
        _eqGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_eqGrid.Rows[e.RowIndex].DataBoundItem is not Equipment eq) return;
            using var detail = new DeviceDetailForm(eq);
            detail.ShowDialog(this);
        };

        return page;
    }


}
