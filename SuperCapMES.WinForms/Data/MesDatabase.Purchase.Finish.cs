using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 采购申请「完结入库」—— 资材采购申请 → WMS 库存
//
// 补的是一条缺失的关键链路：**采购实际完成后，库存必须跟着变**。
// 此前申请单流转到最终态就结束了，货到了、库存却没动，齐套校验永远算缺料，
// 下一轮采购还会重复请购同一批料。
//
// 为什么复用 WMS「采购入库单」而不是直接改库存台账：
//   直接改 WmsStocks 会绕过单据，账实不符——货到了却没有入库单号，
//   将来对账、批次追溯、效期管理全都断了。所以走"生成入库单 → 过账"，
//   复用 PostWmsOrder / ApplyWmsStockChange，入库单天然留痕。
//
// 触发时机：状态流转 **已下单 → 已完结**。只有已下单才谈得上"采购完成"。
//   草稿 / 待审批 / 已批准 / 已驳回 / 已关闭 一律**不触发**——
//   尤其「已驳回」与「已关闭」：后者语义就是"这次采购取消了、货没来"，
//   让它们入库等于凭空造库存。
//
// 顺序保证：**先过账成功，最后才改状态**。任何一步失败状态都不变，
//   不会出现"状态显示完结、库存却没加"的脏数据。
//
// 换算规则：采购申请的 RequestQty 与 WMS Quantity **同单位同量，无换算**；
//   但入库前校验申请单位与物料主数据单位一致，不一致直接拒收——
//   否则会往库存里塞一条"数量对但单位错"的记录，后面全是坑。
//
// 并发：内存仓储单线程本身不会真并发，但完结做了三道幂等闸（见下方注释），
//   换成数据库后这三道直接沿用，仍能挡住重复入库。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>完结按钮的禁用原因（界面直接显示原因，不让用户对着灰按钮猜）。</summary>
    public static string FinishBlockedReason(PurchaseRequest? req)
    {
        if (req is null) return "请先选择一张申请单。";
        if (req.Status == PurchaseRequestStatus.Finished)
            return $"已完结入库（{req.WmsInOrderNo}），不可重复操作";   // 与 FinishRequest 的拒绝话术保持一致
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Finish))
            return $"仅「已下单」可完结入库（当前「{req.StatusText}」）";
        if (req.Lines.Count == 0) return "该单没有明细，无法完结入库。";
        if (req.Lines.Any(l => l.RequestQty <= 1e-9)) return "明细里存在申请量为 0 的行，无法完结入库。";
        if (req.WmsInOrderNo.Length > 0) return $"该单已入库（{req.WmsInOrderNo}），不可重复完结。";
        return "";
    }

    /// <summary>
    /// 完结入库：把本单的采购数量累加进 WMS 库存。
    /// 成功返回 WMS 入库单号；失败时**状态不变、库存不变**，并给出可读原因。
    /// </summary>
    public static (bool Ok, string Message, string WmsOrderNo) FinishRequest(string reqNo, string @operator)
    {
        var req = FindPurchaseRequest(reqNo);
        if (req is null) return (false, $"采购申请 {reqNo} 不存在。", "");

        // ---- ① 状态前置：只有「已下单」能完结 ----
        if (!PurchaseRequestActions.CanDo(req.Status, PurchaseRequestActions.Finish))
        {
            // 已完结的单重复操作要说清"你已经完结过了"，而不是笼统报"只有已下单可以"——
            // 用户看到的是自己刚做过的操作，提示对不上号会以为系统出问题。
            if (req.Status == PurchaseRequestStatus.Finished)
                return (false, $"该申请单已完结入库（{req.WmsInOrderNo}），不可重复操作。", req.WmsInOrderNo);
            return (false, $"只有「已下单」的申请单可以完结入库（当前「{req.StatusText}」）。", "");
        }

        // ---- ② 幂等三道闸：任一命中都拒绝，防止库存被加两次 ----
        // 闸一：本单已记过入库单号
        if (req.WmsInOrderNo.Length > 0)
            return (false, $"该申请单已于入库单 {req.WmsInOrderNo} 完成入库，不可重复完结。", req.WmsInOrderNo);

        // 闸二：WMS 里已存在引用本单的采购入库单（状态被回改过等脏数据场景）
        var existed = FindWmsOrderByRefNo(req.ReqNo, WmsOrderTypes.PurchaseInbound);
        if (existed is not null)
        {
            req.WmsInOrderNo = existed.OrderNo;   // 补记并拒绝，绝不静默再加一次库存
            return (false,
                $"WMS 中已存在引用本单的采购入库单 {existed.OrderNo}（状态：{existed.Status}），不可重复完结。",
                existed.OrderNo);
        }

        // 闸三：明细本身可用
        if (req.Lines.Count == 0) return (false, "该单没有明细，无法完结入库。", "");
        if (req.Lines.Any(l => l.RequestQty <= 1e-9))
            return (false, "明细里存在申请量为 0 的行，请先修正再完结。", "");

        // ---- ③ 逐行构建入库明细（含物料主数据与单位一致性校验）----
        var lines = new List<WmsOrderLine>();
        foreach (var l in req.Lines)
        {
            var mat = FindWmsMaterial(l.MaterialCode);
            if (mat is null)
                return (false, $"物料 {l.MaterialCode}（{l.MaterialName}）不在物料主数据中，无法入库，请先补建主数据。", "");

            if (!string.IsNullOrWhiteSpace(l.Unit) && !string.IsNullOrWhiteSpace(mat.Unit)
                && !string.Equals(l.Unit.Trim(), mat.Unit.Trim(), StringComparison.Ordinal))
                return (false, $"「{l.MaterialName}」申请单位「{l.Unit}」与物料主数据单位「{mat.Unit}」不一致，不能直接累加入库。", "");

            lines.Add(new WmsOrderLine
            {
                MaterialCode = l.MaterialCode,
                MaterialName = l.MaterialName.Length > 0 ? l.MaterialName : mat.MaterialName,
                Spec = l.Spec.Length > 0 ? l.Spec : mat.Spec,
                Unit = mat.Unit,
                // 批次号由申请单号派生：一次采购的货可追溯到是哪张申请单来的
                LotNo = $"PR{req.ReqNo.Replace("-", "")}-{l.MaterialCode}",
                Quantity = l.RequestQty,
                Location = WmsLocations.RawA,
                Remark = $"采购申请 {req.ReqNo} 完结入库"
            });
        }

        // ---- ④ 生成 WMS 采购入库单（RefNo 写申请单号，供反查与防重复）----
        var op = string.IsNullOrWhiteSpace(@operator) ? "计划员" : @operator.Trim();
        var partners = req.Lines.Select(l => l.Supplier)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(2).ToList();
        var (cOk, cMsg, orderNo) = CreateWmsOrder(
            WmsOrderTypes.PurchaseInbound,
            partners.Count > 0 ? string.Join("、", partners) : "待定供应商",
            req.ReqNo, "采购完结入库", op,
            $"由采购申请 {req.ReqNo}（完结）自动生成", lines);
        if (!cOk) return (false, "生成入库单失败：" + cMsg, "");

        // ---- ⑤ 过账（内部走 ApplyWmsStockChange：无台账的物料会自动新建行）----
        var (pOk, pMsg) = PostWmsOrder(orderNo, op);
        if (!pOk)
            return (false, $"入库单 {orderNo} 过账失败：{pMsg}。申请单状态保持「{req.StatusText}」未变。", "");

        // ---- ⑥ 全部成功，最后才改状态 ----
        var now = DateTime.Now;
        req.Status = PurchaseRequestStatus.Finished;
        req.WmsInOrderNo = orderNo;
        req.Remark = string.IsNullOrWhiteSpace(req.Remark)
            ? $"完结入库 {now:yyyy-MM-dd HH:mm}，单据 {orderNo}"
            : req.Remark + $"；完结入库 {now:yyyy-MM-dd HH:mm}，单据 {orderNo}";

        return (true,
            $"采购申请 {req.ReqNo} 已完结，{req.Lines.Count} 项合计 {req.FinishedQty:0.##} 已累加入库（入库单 {orderNo}）。",
            orderNo);
    }

    /// <summary>按参考号反查某类型的 WMS 单据（用于幂等防重复入库）。纯查询。</summary>
    public static WmsOrder? FindWmsOrderByRefNo(string refNo, string? type = null)
    {
        if (string.IsNullOrWhiteSpace(refNo)) return null;
        var r = refNo.Trim();
        return WmsOrders.FirstOrDefault(o =>
            string.Equals(o.RefNo, r, StringComparison.OrdinalIgnoreCase)
            && (type is null || string.Equals(o.Type, type, StringComparison.Ordinal)));
    }

    /// <summary>
    /// 某物料的采购入库累计量（近 N 天，已过账的采购入库单）。用于核对"采购是否真到货"。
    /// </summary>
    public static double GetMaterialPurchasedQty(string materialCode, int days = 90)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return 0;
        var code = materialCode.Trim();
        var from = DateTime.Today.AddDays(-days);
        var orders = WmsOrders
            .Where(o => o.Type == WmsOrderTypes.PurchaseInbound
                        && o.Status == WmsOrderStatus.Posted
                        && o.PostTime is not null && o.PostTime.Value >= from)
            .Select(o => o.OrderNo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GetWmsOrderLines(orders).Where(l => string.Equals(l.MaterialCode, code, StringComparison.OrdinalIgnoreCase))
            .Sum(l => l.Quantity);
    }

    /// <summary>批量取多张单据的明细（内部使用，避免在循环里反复全表扫）。</summary>
    private static List<WmsOrderLine> GetWmsOrderLines(IEnumerable<string> orderNos)
    {
        var set = orderNos.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return WmsOrderLines.Where(l => set.Contains(l.OrderNo)).ToList();
    }
}