namespace SuperCapMES.WinForms.Models;

/// <summary>
/// SPC 监控项「规格限版本」。每个监控项可维护一串版本（V1、V2、V3…），每版含：
///   Usl / Lsl（double.NaN 表示该侧无规格限）、生效时间 <see cref="EffectiveFrom"/>（含）之后到达的样本按本版本判定、
///   可选目标值、创建人 / 创建时间与备注。
/// 判定真值以版本表（<c>MesDatabase.SpcItemVersions</c>）为准，<see cref="SpcItem.Usl"/>/<see cref="SpcItem.Lsl"/>
/// 仅作为初始 / 兼容字段保留。
/// 版本治理规则（MES 可追溯）：已生效版本不可修改 / 删除，只能新建版本；待生效版本（EffectiveFrom &gt; Now）可改可删。
/// </summary>
public class SpcItemVersion
{
    /// <summary>所属监控项编号（形如 SPC-P01）</summary>
    public string ItemId { get; set; } = "";

    /// <summary>版本号（同一监控项内自增，从 1 开始）</summary>
    public int Version { get; set; }

    /// <summary>规格上限（double.NaN 表示无上限）</summary>
    public double Usl { get; set; } = double.NaN;

    /// <summary>规格下限（double.NaN 表示无下限）</summary>
    public double Lsl { get; set; } = double.NaN;

    /// <summary>目标值（double.NaN 表示未设定）</summary>
    public double TargetValue { get; set; } = double.NaN;

    /// <summary>开始使用时间：样本采样时间 ≥ 该时刻时按本版本判定</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>创建人</summary>
    public string CreatedBy { get; set; } = "";

    /// <summary>创建时间</summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>备注（如调整原因）</summary>
    public string Remark { get; set; } = "";

    /// <summary>是否有上限规格</summary>
    public bool HasUsl => !double.IsNaN(Usl);

    /// <summary>是否有下限规格</summary>
    public bool HasLsl => !double.IsNaN(Lsl);

    /// <summary>版本展示文本，如 "V2"</summary>
    public string VersionText => $"V{Version}";

    /// <summary>规格限展示文本（不含单位），如 "1.550 ~ 2.050" / "≥ 95.000" / "≤ 25.000" / "无规格限"</summary>
    public string SpecText
    {
        get
        {
            if (HasUsl && HasLsl) return $"{Lsl:F3} ~ {Usl:F3}";
            if (HasUsl) return $"≤ {Usl:F3}";
            if (HasLsl) return $"≥ {Lsl:F3}";
            return "无规格限";
        }
    }

    /// <summary>相对给定时刻是否已生效（EffectiveFrom ≤ now）。</summary>
    public bool IsEffective(DateTime now) => EffectiveFrom <= now;

    /// <summary>状态文本：已生效 / 待生效。</summary>
    public string StatusText(DateTime now) => IsEffective(now) ? "已生效" : "待生效";
}
