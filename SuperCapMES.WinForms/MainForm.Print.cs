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
/// MainForm 分部类（按功能域拆分，第 11 部分）：单据打印（工单流转卡/保留单/异常单/返工单/WMS 单据/首检单，含 Code128 条码）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Print」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 单据打印（含 Code128 条码） =================

    /// <summary>打印模型为空时的统一提示；否则弹出打印预览。</summary>
    private void PrintSlip(PrintModel? model, string emptyHint)
    {
        if (model is null) { Warn(emptyHint); return; }
        try
        {
            DocumentPrinter.Preview(this, model);
        }
        catch (Exception ex)
        {
            Warn($"打印预览失败：{ex.Message}");
        }
    }

    /// <summary>工单流转卡（含该工单 BOM 明细）。纯数据构造，便于自测。</summary>
    internal static PrintModel? BuildWorkOrderSlip(string woNo)
    {
        var wo = MesDatabase.WorkOrders.FirstOrDefault(w =>
            string.Equals(w.WoNo, (woNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (wo is null) return null;

        var model = new PrintModel
        {
            Title = "工单流转卡",
            SubTitle = wo.WoNo,
            BarcodeText = wo.WoNo,
            Footer = $"打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        model.Header.Add(("ERP 订单号", wo.ErpOrderNo));
        model.Header.Add(("产品型号", wo.ProductModel));
        model.Header.Add(("生产线体", wo.Line));
        model.Header.Add(("工单状态", wo.Status));
        model.Header.Add(("计划数量", wo.PlanQty.ToString()));
        model.Header.Add(("已完成数", wo.FinishedQty.ToString()));
        model.Header.Add(("不良数", wo.NgQty.ToString()));
        model.Header.Add(("开始日期", wo.StartDate.ToString("yyyy-MM-dd")));
        model.Header.Add(("计划交期", wo.DueDate.ToString("yyyy-MM-dd")));
        model.Header.Add(("ERP 报工", wo.ErpReportStatus));

        model.DetailHeaders.AddRange(new[] { "物料编码", "物料名称", "规格", "单位", "单位用量", "损耗率(%)" });
        foreach (var b in MesDatabase.GetWorkOrderBom(wo.WoNo))
            model.DetailRows.Add(new List<string>
            {
                b.MaterialCode, b.MaterialName, b.Spec, b.Unit,
                b.QtyPerUnit.ToString("0.####"), b.LossRate.ToString("0.##")
            });
        return model;
    }

    /// <summary>保留单（含保留电芯明细）。纯数据构造。</summary>
    internal static PrintModel? BuildHoldSlip(string holdNo)
    {
        var hold = MesDatabase.Holds.FirstOrDefault(h =>
            string.Equals(h.HoldNo, (holdNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (hold is null) return null;

        var model = new PrintModel
        {
            Title = "保留单",
            SubTitle = hold.HoldNo,
            BarcodeText = hold.HoldNo,
            Footer = $"打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        model.Header.Add(("物料编码", hold.MaterialCode));
        model.Header.Add(("物料名称", hold.MaterialName));
        model.Header.Add(("批次号", hold.LotNo));
        model.Header.Add(("保留位置", hold.HoldLocation));
        model.Header.Add(("状态", hold.Status));
        model.Header.Add(("电芯数", hold.CellCount.ToString()));
        model.Header.Add(("保留原因", hold.Reason));
        model.Header.Add(("操作人", hold.Operator));
        model.Header.Add(("保留时间", hold.HoldTime.ToString("yyyy-MM-dd HH:mm")));
        model.Header.Add(("来源", string.IsNullOrWhiteSpace(hold.SourceType) ? "手工" : hold.SourceType));

        model.DetailHeaders.AddRange(new[] { "序号", "电芯SN", "型号", "极片批号", "电解液批号", "当前状态" });
        var index = 1;
        foreach (var item in MesDatabase.HoldItems.Where(i =>
                     string.Equals(i.HoldNo, hold.HoldNo, StringComparison.OrdinalIgnoreCase)))
        {
            var cell = MesDatabase.Cells.FirstOrDefault(c =>
                string.Equals(c.Sn, item.CellSn, StringComparison.OrdinalIgnoreCase));
            model.DetailRows.Add(new List<string>
            {
                (index++).ToString(), item.CellSn, cell?.ProductModel ?? "",
                cell?.ElectrodeLot ?? "", cell?.ElectrolyteLot ?? "", cell?.Status ?? ""
            });
        }
        return model;
    }

    /// <summary>异常登记单（含流转履历）。纯数据构造。</summary>
    internal static PrintModel? BuildAbnormalSlip(string abNo)
    {
        var rec = MesDatabase.AbnormalRecords.FirstOrDefault(a =>
            string.Equals(a.AbNo, (abNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (rec is null) return null;

        var model = new PrintModel
        {
            Title = "异常登记单",
            SubTitle = rec.AbNo,
            BarcodeText = rec.AbNo,
            Footer = $"打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        model.Header.Add(("发生时间", rec.OccurTime.ToString("yyyy-MM-dd HH:mm")));
        model.Header.Add(("发现人", rec.FoundBy));
        model.Header.Add(("线体", rec.Line));
        model.Header.Add(("设备", $"{rec.EquipmentName}（{rec.EquipmentCode}）"));
        model.Header.Add(("大类", rec.Category));
        model.Header.Add(("细分", rec.SubType));
        model.Header.Add(("状态", rec.Status));
        model.Header.Add(("负责人", rec.Owner));
        model.Header.Add(("临时措施", rec.TemporaryMeasure));
        model.Header.Add(("来源", rec.SourceType));

        model.DetailHeaders.AddRange(new[] { "时间", "动作", "操作人", "说明" });
        foreach (var h in MesDatabase.AbnormalHistories
                     .Where(h => string.Equals(h.AbNo, rec.AbNo, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(h => h.Time))
            model.DetailRows.Add(new List<string>
            {
                h.Time.ToString("yyyy-MM-dd HH:mm"), h.Action, h.Operator, h.Remark
            });
        return model;
    }

    /// <summary>返工单（含流转履历）。纯数据构造。</summary>
    internal static PrintModel? BuildReworkSlip(string reworkNo)
    {
        var r = MesDatabase.FindRework(reworkNo);
        if (r is null) return null;

        var model = new PrintModel
        {
            Title = "返工单",
            SubTitle = r.ReworkNo,
            BarcodeText = r.ReworkNo,
            Footer = $"打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        model.Header.Add(("来源保留单", r.HoldNo));
        model.Header.Add(("电芯SN", r.CellSn));
        model.Header.Add(("产品型号", r.ProductModel));
        model.Header.Add(("返工原因", r.Reason));
        model.Header.Add(("责任工序", string.IsNullOrWhiteSpace(r.ResponsibleProcess) ? "—" : r.ResponsibleProcess));
        model.Header.Add(("返工工序", r.ReworkProcess));
        model.Header.Add(("返工次数", r.ReworkTimes.ToString()));
        model.Header.Add(("当前状态", r.StatusText));
        model.Header.Add(("重检结果", string.IsNullOrWhiteSpace(r.InspectResult) ? "—" : r.InspectResult));
        model.Header.Add(("返工参数", string.IsNullOrWhiteSpace(r.ReworkParam) ? "—" : r.ReworkParam));

        model.DetailHeaders.AddRange(new[] { "时间", "动作", "操作人", "说明" });
        foreach (var h in MesDatabase.GetReworkHistories(r.ReworkNo))
            model.DetailRows.Add(new List<string>
            {
                h.Time.ToString("yyyy-MM-dd HH:mm"), h.Action, h.Operator, h.Detail
            });
        return model;
    }

    /// <summary>WMS 单据（含单据行）。纯数据构造。</summary>
    internal static PrintModel? BuildWmsSlip(string orderNo)
    {
        var order = MesDatabase.FindWmsOrder(orderNo);
        if (order is null) return null;

        var model = new PrintModel
        {
            Title = $"WMS {order.Type}单",
            SubTitle = order.OrderNo,
            BarcodeText = order.OrderNo,
            Footer = $"打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}"
        };
        model.Header.Add((order.PartnerLabel, order.Partner));
        model.Header.Add(("单据状态", order.Status));
        model.Header.Add(("关联单号", string.IsNullOrWhiteSpace(order.RefNo) ? "—" : order.RefNo));
        model.Header.Add(("经手人", order.Operator));
        model.Header.Add(("创建时间", order.CreateTime.ToString("yyyy-MM-dd HH:mm")));
        model.Header.Add(("过账时间", order.PostTime?.ToString("yyyy-MM-dd HH:mm") ?? "—"));
        model.Header.Add(("明细行数", order.LineCount.ToString()));
        model.Header.Add(("合计数量", order.TotalQty.ToString("0.##")));

        model.DetailHeaders.AddRange(new[] { "物料编码", "物料名称", "规格", "单位", "批次", "数量", "库位" });
        foreach (var l in MesDatabase.GetWmsOrderLines(order.OrderNo))
            model.DetailRows.Add(new List<string>
            {
                l.MaterialCode, l.MaterialName, l.Spec, l.Unit, l.LotNo,
                l.Quantity.ToString("0.##"), l.Location
            });
        return model;
    }

    // ---------- 打印按钮处理器 ----------

    /// <summary>处理「PrintWorkOrderSlip」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnPrintWorkOrderSlip(object? sender, EventArgs e) =>
        PrintSlip(BuildWorkOrderSlip(SelectedWorkOrder()?.WoNo ?? ""), "请先选择要打印的工单。");

    /// <summary>处理「PrintHoldSlip」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnPrintHoldSlip(object? sender, EventArgs e)
    {
        if (_holdGrid.SelectedRows.Count == 0) { Warn("请先选择要打印的保留单。"); return; }
        var hold = _holdGrid.SelectedRows[0].DataBoundItem as CellHold;
        PrintSlip(BuildHoldSlip(hold?.HoldNo ?? ""), "请先选择要打印的保留单。");
    }

    /// <summary>处理「PrintAbnormalSlip」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnPrintAbnormalSlip(object? sender, EventArgs e)
    {
        if (_abGrid.SelectedRows.Count == 0) { Warn("请先选择要打印的异常单。"); return; }
        var rec = _abGrid.SelectedRows[0].DataBoundItem as AbnormalRecord;
        PrintSlip(BuildAbnormalSlip(rec?.AbNo ?? ""), "请先选择要打印的异常单。");
    }

    /// <summary>处理「PrintReworkSlip」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnPrintReworkSlip(object? sender, EventArgs e) =>
        PrintSlip(BuildReworkSlip(SelectedRework()?.ReworkNo ?? ""), "请先选择要打印的返工单。");

    /// <summary>处理「PrintWmsSlip」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnPrintWmsSlip(object? sender, EventArgs e)
    {
        if (_wmsOrderGrid.SelectedRows.Count == 0) { Warn("请先选择要打印的单据。"); return; }
        var order = _wmsOrderGrid.SelectedRows[0].DataBoundItem as WmsOrder;
        PrintSlip(BuildWmsSlip(order?.OrderNo ?? ""), "请先选择要打印的单据。");
    }


}
