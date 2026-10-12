using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

// ============================================================================
// 物料废弃 + 重新进料检 —— 计算/写入口
//
// 两条路互斥：过期物料只能「废弃」或「重新进料检」二选一。
//   废弃      —— 不可逆终态；写废弃台账 + 主表打标记 + 数量归零（物料离场）
//   重新进料检 —— 可重复；写进料检台账 + 更新主表 ExpiryDate（物料留下，效期延长）
//
// 顺序保证：**先写台账，再改主表**。台账写失败就不动主表，避免"改了状态没台账"。
// 幂等：废弃重复调用会被 IsScrapped 拦下；进料检每次生成新单号，允许多次。
// ============================================================================

public static partial class MesDatabase
{
    /// <summary>废弃台账（只追加）</summary>
    public static List<MaterialScrapRecord> MaterialScraps { get; } = new();

    /// <summary>重新进料检台账（只追加，一个批次可有多条）</summary>
    public static List<MaterialReinspection> MaterialReinspections { get; } = new();

    private static int _scrapSeq, _reinspectSeq;

    /// <summary>按物料 + 批次 + 库位定位库存行（定位口径与 ApplyWmsStockChange 一致）</summary>
    private static WmsStock? FindStock(string? materialCode, string? lotNo, string? location)
        => WmsStocks.FirstOrDefault(s =>
            string.Equals(s.MaterialCode, (materialCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.LotNo, (lotNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Location, (location ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    // ========================================================================
    // ① 废弃
    // ========================================================================

    /// <summary>废弃的禁用原因（界面按钮的启用依据，与后端校验同源）</summary>
    public static string ScrapBlockedReason(WmsStock? stock)
    {
        if (stock is null) return "请先选择一行库存批次";
        if (stock.IsScrapped) return $"该批次已于 {stock.ScrappedAt:yyyy-MM-dd HH:mm} 废弃，不可重复废弃";
        if (stock.Quantity <= 1e-9) return "该批次数量为 0，无需废弃";
        return "";
    }

    /// <summary>
    /// 废弃一个库存批次。**不可逆**：写台账 → 主表打标记 → 数量归零。
    /// </summary>
    public static (bool Ok, string Message, string ScrapNo) ScrapMaterial(
        string materialCode, string lotNo, string location,
        string reason, string @operator, string? remark = null, DateTime? at = null)
    {
        var s = FindStock(materialCode, lotNo, location);
        if (s is null) return (false, $"未找到库存批次：{materialCode} / {lotNo} / {location}。", "");

        if (s.IsScrapped)
            return (false, $"该批次已于 {s.ScrappedAt:yyyy-MM-dd HH:mm} 废弃，不可重复废弃。", "");
        if (s.Quantity <= 1e-9)
            return (false, "该批次数量为 0，无需废弃。", "");

        var r = (reason ?? "").Trim();
        if (r.Length == 0) return (false, "请填写废弃原因。", "");
        var op = (@operator ?? "").Trim();
        if (op.Length == 0) return (false, "请填写操作人。", "");

        // ① 先写台账
        var now = at ?? DateTime.Now;
        var rec = new MaterialScrapRecord
        {
            ScrapNo = $"SCRAP-{now:yyyyMMdd}-{++_scrapSeq:000}",
            MaterialCode = s.MaterialCode,
            MaterialName = s.MaterialName,
            LotNo = s.LotNo,
            Location = s.Location,
            Quantity = s.Quantity,
            OriginExpiry = s.ExpiryDate,
            Reason = r,
            Operator = op,
            ScrapAt = now,
            Remark = (remark ?? "").Trim()
        };
        MaterialScraps.Add(rec);

        // ② 再改主表：打标记 + 数量归零（物料离场，不再参与 FIFO 与可用量计算）
        s.IsScrapped = true;
        s.ScrappedAt = now;
        s.Quantity = 0;
        s.LastUpdate = now;

        return (true,
            $"批次 {s.LotNo}（{s.MaterialName} {rec.Quantity:0.##}）已废弃，台账 {rec.ScrapNo}。"
            + "该批次已退出可用库存与 FIFO 队列。",
            rec.ScrapNo);
    }

    // ========================================================================
    // ② 重新进料检
    // ========================================================================

    /// <summary>重新进料检的禁用原因</summary>
    public static string ReinspectBlockedReason(WmsStock? stock, DateTime? today = null)
    {
        if (stock is null) return "请先选择一行库存批次";
        if (stock.IsScrapped) return "该批次已废弃，不能再发起进料检";
        if (stock.Quantity <= 1e-9) return "该批次数量为 0，无可检物料";
        if (stock.ExpiryDate is null) return "该批次无有效期，不存在过期问题，无需进料检";
        var t = (today ?? DateTime.Now).Date;
        if (stock.ExpiryDate.Value.Date >= t) return $"该批次尚未过期（{stock.ExpiryDate:yyyy-MM-dd}），无需进料检";
        return "";
    }

    /// <summary>
    /// 对**已过期**批次发起重新进料检：写台账 → 更新主表效期。
    /// 新效期必须晚于今天（否则等于没检）。
    /// </summary>
    public static (bool Ok, string Message, string ReinspectNo) ReinspectMaterial(
        string materialCode, string lotNo, string location,
        DateTime newExpiry, string inspector, string result = ReinspectResults.Pass,
        string? remark = null, DateTime? at = null)
    {
        var s = FindStock(materialCode, lotNo, location);
        if (s is null) return (false, $"未找到库存批次：{materialCode} / {lotNo} / {location}。", "");

        // 四道拦截，顺序与界面一致
        if (s.IsScrapped) return (false, "该批次已废弃，不能再发起进料检。", "");
        if (s.Quantity <= 1e-9) return (false, "该批次数量为 0，无可检物料。", "");
        if (s.ExpiryDate is null) return (false, "该批次无有效期，不存在过期问题，无需进料检。", "");

        var now = at ?? DateTime.Now;
        if (s.ExpiryDate.Value.Date >= now.Date)
            return (false, $"该批次尚未过期（{s.ExpiryDate:yyyy-MM-dd}），无需进料检。", "");

        var inspectorName = (inspector ?? "").Trim();
        if (inspectorName.Length == 0) return (false, "请填写进料检人。", "");
        if (newExpiry.Date <= now.Date)
            return (false, $"新效期必须晚于今天（当前 {now:yyyy-MM-dd}，填写 {newExpiry:yyyy-MM-dd}）。", "");
        if (!ReinspectResults.All.Contains(result))
            return (false, $"进料检结论取值非法：{result}", "");

        // ① 先写台账（历史只追加）
        var rec = new MaterialReinspection
        {
            ReinspectNo = $"RI-{now:yyyyMMdd}-{++_reinspectSeq:000}",
            MaterialCode = s.MaterialCode,
            MaterialName = s.MaterialName,
            LotNo = s.LotNo,
            Location = s.Location,
            OldExpiry = s.ExpiryDate,
            NewExpiry = newExpiry.Date,
            Inspector = inspectorName,
            InspectAt = now,
            Result = result,
            Remark = (remark ?? "").Trim()
        };
        MaterialReinspections.Add(rec);

        // ② 再更新主表效期（查询与 FIFO 直接读这个值，无需改动原有逻辑）
        s.ExpiryDate = newExpiry.Date;
        s.LastUpdate = now;

        return (true,
            $"批次 {s.LotNo}（{s.MaterialName}）进料检完成：新效期 {rec.OldExpiry:yyyy-MM-dd} → {rec.NewExpiry:yyyy-MM-dd}"
            + $"（延长 {rec.ExtendedDays} 天），台账 {rec.ReinspectNo}。",
            rec.ReinspectNo);
    }

    // ========================================================================
    // ③ 台账查询（纯查询）
    // ========================================================================

    /// <summary>进料检历史：可按物料过滤，默认按时间倒序（新的在前）。</summary>
    public static List<MaterialReinspection> GetReinspections(string? materialCode = null, bool newestFirst = true)
    {
        var q = (materialCode ?? "").Trim();
        var list = MaterialReinspections
            .Where(r => q.Length == 0 || string.Equals(r.MaterialCode, q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return newestFirst
            ? list.OrderByDescending(r => r.InspectAt).ThenByDescending(r => r.ReinspectNo).ToList()
            : list.OrderBy(r => r.InspectAt).ThenBy(r => r.ReinspectNo).ToList();
    }

    /// <summary>废弃台账：可按物料过滤，默认按时间倒序。</summary>
    public static List<MaterialScrapRecord> GetScrapRecords(string? materialCode = null, bool newestFirst = true)
    {
        var q = (materialCode ?? "").Trim();
        var list = MaterialScraps
            .Where(r => q.Length == 0 || string.Equals(r.MaterialCode, q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return newestFirst
            ? list.OrderByDescending(r => r.ScrapAt).ThenByDescending(r => r.ScrapNo).ToList()
            : list.OrderBy(r => r.ScrapAt).ThenBy(r => r.ScrapNo).ToList();
    }

    /// <summary>某批次的进料检次数（界面上告诉用户"这批已经检过几次了"）</summary>
    public static int GetReinspectCount(string materialCode, string lotNo)
        => MaterialReinspections.Count(r =>
            string.Equals(r.MaterialCode, (materialCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.LotNo, (lotNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
}