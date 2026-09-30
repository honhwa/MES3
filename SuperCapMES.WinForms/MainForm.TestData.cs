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
/// MainForm 分部类（按功能域拆分，第 16 部分）：测试数据页（电性能测试清单，双击 SN 跳转追溯）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「TestData」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 测试数据 =================
    /// <summary>构建「Tests」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildTestsTab()
    {
        var page = new TabPage("测试数据");
        var group = MakeGridGroup("电性能测试数据（全检全量）", out _testGrid);
        page.Controls.Add(group);

        // 双击「电芯SN」列 → 跳转 SN 追溯页并自动查询
        _testGrid.CellDoubleClick += TestsGrid_CellDoubleClick;
        _testGrid.CellFormatting += (s, e) =>
        {
            if (e.ColumnIndex >= 0 && _testGrid.Columns[e.ColumnIndex].Name == "CellSn" && e.RowIndex >= 0)
            {
                e.CellStyle.ForeColor = Color.FromArgb(15, 76, 129);
                e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Underline);
                e.CellStyle.SelectionForeColor = Color.FromArgb(15, 76, 129);
            }
        };
        return page;
    }


    // ================= 事件处理 =================
    /// <summary>测试数据页双击「电芯SN」列 → 跳转 SN 追溯页并自动查询</summary>
    private void TestsGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_testGrid.Columns[e.ColumnIndex].Name != "CellSn") return;

        var sn = _testGrid.Rows[e.RowIndex].Cells["CellSn"].Value?.ToString();
        if (string.IsNullOrEmpty(sn)) return;

        _tabs.SelectedTab = _tracePage; // 切到 SN 追溯页
        _txtSn.Text = sn;
        DoTrace();
    }


}
