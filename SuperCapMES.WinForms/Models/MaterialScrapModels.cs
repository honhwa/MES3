namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 物料废弃 + 重新进料检 —— 模型层
//
// 两条路互斥：过期物料只能「废弃」或「重新进料检」二选一。
//   废弃      ：**不可逆终态**，记录原因/操作人/时间，废弃后不再出现在待处理列表
//   重新进料检：可重复多次，每次留一条历史；主表只更新 ExpiryDate，历史另表存
//
// 为什么废弃台账与进料检台账独立成表、而不是往 WmsStock 上堆字段：
//   WmsStock 是"当前状态"（会被过账不断改写），台账是"历史事实"（只追加不改写）。
//   两者混在一张表里，历史会被后续过账冲掉，追溯链就断了。
// ============================================================================

/// <summary>废弃原因</summary>
public static class ScrapReasons
{
    public const string Expired = "过期报废";
    public const string IncomingBad = "来料不良";
    public const string ProcessCancel = "工艺取消";
    public const string Other = "其他";
    public static readonly string[] All = { Expired, IncomingBad, ProcessCancel, Other };
}

/// <summary>进料检结论</summary>
public static class ReinspectResults
{
    public const string Pass = "合格";
    public const string Fail = "不合格";
    public static readonly string[] All = { Pass, Fail };
}

/// <summary>废弃台账（一条记录 = 一次废弃动作，只追加不改写）</summary>
public class MaterialScrapRecord
{
    public string ScrapNo { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>废弃时的数量</summary>
    public double Quantity { get; set; }

    /// <summary>废弃前的效期（可能已过期，也可能只是提前报废）</summary>
    public DateTime? OriginExpiry { get; set; }

    public string Reason { get; set; } = "";
    public string Operator { get; set; } = "";
    public DateTime ScrapAt { get; set; } = DateTime.Now;
    public string Remark { get; set; } = "";

    public string OriginExpiryText => OriginExpiry?.ToString("yyyy-MM-dd") ?? "无有效期";
    public string ScrapAtText => ScrapAt.ToString("yyyy-MM-dd HH:mm");
    public string QtyText => $"{Quantity:0.##}";
}

/// <summary>重新进料检记录（一条记录 = 一次进料检动作，多次累积成历史）</summary>
public class MaterialReinspection
{
    public string ReinspectNo { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string LotNo { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>进料检前的效期</summary>
    public DateTime? OldExpiry { get; set; }

    /// <summary>进料检后的新效期（必须晚于今天）</summary>
    public DateTime NewExpiry { get; set; }

    public string Inspector { get; set; } = "";
    public DateTime InspectAt { get; set; } = DateTime.Now;
    public string Result { get; set; } = ReinspectResults.Pass;
    public string Remark { get; set; } = "";

    public string OldExpiryText => OldExpiry?.ToString("yyyy-MM-dd") ?? "—";
    public string NewExpiryText => NewExpiry.ToString("yyyy-MM-dd");
    public string InspectAtText => InspectAt.ToString("yyyy-MM-dd HH:mm");

    /// <summary>延长的天数（用于展示"这次进料检争取了多少时间"）</summary>
    public int ExtendedDays => OldExpiry is null ? 0 : (int)(NewExpiry.Date - OldExpiry.Value.Date).TotalDays;

    public string ExtendedText => OldExpiry is null
        ? "—"
        : ExtendedDays > 0 ? $"+{ExtendedDays} 天" : ExtendedDays < 0 ? $"{ExtendedDays} 天" : "不变";
}