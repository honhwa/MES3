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
/// MainForm 分部类（按功能域拆分，第 5 部分）：报告导出（召回影响清单 / 追溯报告 / 报表中心，零依赖 xlsx）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Export」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 报告导出（xlsx，零依赖） =================

    /// <summary>导出召回影响清单（4 个工作表：汇总 / 出货记录 / 受影响电芯 / 受影响材料批次）。</summary>
    private void BtnRecallExport(object? sender, EventArgs e)
    {
        if (_lastRecall is null || _lastRecall.Cells.Count == 0) { Warn("请先执行反查，再导出报告。"); return; }

        var dlg = new SaveFileDialog
        {
            Title = "导出召回影响清单",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"召回影响清单_{SanitizeFileName(_lastRecall.Keyword)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var w = new XlsxWriter();
            BuildRecallReport(w, _lastRecall);
            var (sheets, rows) = w.Save(dlg.FileName);
            MessageBox.Show($"报告已导出：\n{dlg.FileName}\n\n工作表 {sheets} 个 ｜ 数据行 {rows} 行",
                "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"导出失败：{ex.Message}");
        }
    }

    /// <summary>导出单芯全链路追溯报告（基本信息 / 材料批次 / 工序记录 / 测试数据 / 保留记录）。</summary>
    private void BtnTraceExport(object? sender, EventArgs e)
    {
        var sn = _txtSn.Text.Trim();
        if (sn.Length == 0) { Warn("请先输入电芯 SN 并点「查询追溯」。"); return; }

        var result = MesDatabase.GetTrace(sn);
        if (result is null) { Warn($"未找到电芯 SN：{sn}"); return; }

        var dlg = new SaveFileDialog
        {
            Title = "导出追溯报告",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"追溯报告_{SanitizeFileName(sn)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var w = new XlsxWriter();
            BuildTraceReport(w, result);
            var (sheets, rows) = w.Save(dlg.FileName);
            MessageBox.Show($"报告已导出：\n{dlg.FileName}\n\n工作表 {sheets} 个 ｜ 数据行 {rows} 行",
                "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"导出失败：{ex.Message}");
        }
    }

    /// <summary>构造召回影响清单内容（纯数据构造，无 UI / 文件依赖，便于自测）。</summary>
    internal static void BuildRecallReport(XlsxWriter w, RecallQueryResult r)
    {
        var s = r.Summary;

        var summary = w.AddSheet("汇总");
        summary.SetColumns(("项目", 24), ("内容", 42));
        summary.AddRow("报告名称", "召回影响清单");
        summary.AddRow("反查方式", r.Mode);
        summary.AddRow("查询值", r.Keyword);
        summary.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        summary.AddRow("涉及出货单（张）", s.ShipmentCount);
        summary.AddRow("涉及客户（家）", s.CustomerCount);
        summary.AddRow("影响电芯（只）", s.CellCount);
        summary.AddRow("其中：已出货（只）", s.ShippedCellCount);
        summary.AddRow("其中：在库（只）", s.InStockCellCount);
        summary.AddRow("其中：可冻结（只）", s.FreezableCellCount);
        summary.AddRow("其中：已在保留中（只）", s.HeldCellCount);
        summary.AddRow("涉及材料批次（个）", s.LotCount);
        summary.AddRow("处置口径", "仅冻结「未出货且未保留」的电芯；已出货电芯需客户侧处置。");

        var ship = w.AddSheet("出货记录");
        ship.SetColumns(("出货单号", 18), ("客户", 14), ("出货时间", 20), ("物流单号", 16),
            ("电芯SN", 22), ("型号", 16), ("极片批号", 16), ("电解液批号", 16), ("成品批次", 18));
        foreach (var x in r.Shipments)
            ship.AddRow(x.OrderNo, x.Customer, x.ShipTime?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                x.RefNo, x.CellSn, x.ProductModel, x.ElectrodeLot, x.ElectrolyteLot, x.WarehouseLot);

        var cells = w.AddSheet("受影响电芯");
        cells.SetColumns(("电芯SN", 22), ("型号", 16), ("状态", 10), ("出货状态", 10), ("出货单号", 18),
            ("客户", 14), ("在库库位", 14), ("已保留", 9), ("保留单号", 18), ("极片批号", 16), ("电解液批号", 16));
        foreach (var c in r.Cells)
            cells.AddRow(c.CellSn, c.ProductModel, c.Status, c.Shipped ? "已出货" : "在库",
                c.OrderNo, c.Customer, c.StockLocation, c.Held ? "是" : "否", c.HolderNo,
                c.ElectrodeLot, c.ElectrolyteLot);

        var lots = w.AddSheet("受影响材料批次");
        lots.SetColumns(("批次号", 20), ("材料名称", 32), ("使用电芯数", 12), ("流向出货单数", 13));
        foreach (var l in r.Lots)
            lots.AddRow(l.LotNo, l.MaterialName, l.CellCount, l.ShipmentCount);
    }

    /// <summary>构造单芯追溯报告内容（纯数据构造，无 UI / 文件依赖，便于自测）。</summary>
    internal static void BuildTraceReport(XlsxWriter w, TraceResult t)
    {
        var cell = t.Cell;

        var info = w.AddSheet("基本信息");
        info.SetColumns(("项目", 20), ("内容", 44));
        info.AddRow("报告名称", "电芯全链路追溯报告");
        info.AddRow("电芯 SN", cell?.Sn ?? "");
        info.AddRow("产品型号", cell?.ProductModel ?? "");
        info.AddRow("生产工单", cell?.WoNo ?? "");
        info.AddRow("产线", cell?.Line ?? "");
        info.AddRow("电芯状态", cell?.Status ?? "");
        info.AddRow("分档结果", string.IsNullOrWhiteSpace(cell?.Result) ? "待分档" : cell!.Result);
        info.AddRow("极片批号", cell?.ElectrodeLot ?? "");
        info.AddRow("电解液批号", cell?.ElectrolyteLot ?? "");
        info.AddRow("所属模组", string.IsNullOrWhiteSpace(cell?.ModuleSn) ? "未组模组" : cell!.ModuleSn);
        info.AddRow("创建时间", cell?.CreateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
        info.AddRow("干燥房采样点", t.EnvPoint);
        info.AddRow("露点 / 温度 / 湿度", $"{t.EnvDewPoint} / {t.EnvTemp} / {t.EnvHumidity}");
        info.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        AddListSheet(w, "材料批次", MatHeaders, t.Materials);
        AddListSheet(w, "工序记录", ProcHeaders, t.Processes);
        AddListSheet(w, "测试数据", TestHeaders, t.Tests);

        // 保留记录（保留单 ↔ 电芯通过保留明细关联）
        var sn = cell?.Sn ?? "";
        var holds = MesDatabase.Holds
            .Where(h => MesDatabase.HoldItems.Any(i =>
                string.Equals(i.HoldNo, h.HoldNo, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.CellSn, sn, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var holdSheet = w.AddSheet("保留记录");
        holdSheet.SetColumns(("单号", 20), ("保留位置", 14), ("原因", 40), ("状态", 10),
            ("操作人", 10), ("保留时间", 20), ("来源", 12));
        foreach (var h in holds)
            holdSheet.AddRow(h.HoldNo, h.HoldLocation, h.Reason, h.Status, h.Operator,
                h.HoldTime.ToString("yyyy-MM-dd HH:mm"),
                string.IsNullOrWhiteSpace(h.SourceType) ? "手工" : h.SourceType);
    }

    /// <summary>按列头字典（属性名 → 中文列名）把列表导出为工作表。</summary>
    private static void AddListSheet<T>(XlsxWriter w, string name, IReadOnlyDictionary<string, string> headers, IReadOnlyList<T> items)
    {
        var sheet = w.AddSheet(name);
        sheet.SetColumns(headers.Select(h => (h.Value, EstimateColumnWidth(h.Value))).ToArray());
        foreach (var item in items)
        {
            var row = new List<object?>();
            foreach (var prop in headers.Keys)
                row.Add(typeof(T).GetProperty(prop)?.GetValue(item));
            sheet.AddRow(row.ToArray());
        }
    }

    /// <summary>按中文标题估算列宽（中文按 2 字符计，限制 10~40）。</summary>
    private static double EstimateColumnWidth(string title)
    {
        double width = 0;
        foreach (var ch in title) width += ch > 0x2E80 ? 2 : 1;
        return Math.Min(40, Math.Max(10, width + 4));
    }

    /// <summary>文件名安全化（去除非法字符并限长）。</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string((name ?? "").Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (cleaned.Length == 0) return "report";
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }


}
