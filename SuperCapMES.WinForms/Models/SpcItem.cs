namespace SuperCapMES.WinForms.Models;

/// <summary>SPC 监控项类别常量：工序参数（现场工艺值）/ 测试数据（电性能值）</summary>
public static class SpcCategory
{
    /// <summary>工序参数（来源：ProcessRecord 的 ParamName/ParamValue）</summary>
    public const string ProcessParam = "工序参数";

    /// <summary>测试数据（来源：TestRecord 的电性能字段）</summary>
    public const string TestData = "测试数据";

    /// <summary>全部类别（顺序即下拉展示顺序）</summary>
    public static readonly string[] All = { ProcessParam, TestData };
}

/// <summary>
/// SPC 监控项定义（一个监控项对应一条质量特性）。
/// 规格限来源说明：Usl / Lsl 取自「检验标准」——工序项与测试项分别对应检验模板中的标准值
/// （如 "1.5~2.0" → Lsl=1.5/Usl=2.0；"≥95" → 单边下限 95；"≤25" → 单边上限 25）。
/// 演示用注液量按本次口径取 4.80~5.20 g。Usl / Lsl 为 <see cref="double.NaN"/> 表示该侧无规格限。
///
/// 【重要】引入「规格限版本化」后，判定真值以版本表（<c>MesDatabase.SpcItemVersions</c>，
/// 见 <see cref="SpcItemVersion"/>）为准；本类的 <see cref="Usl"/>/<see cref="Lsl"/> 仅作为
/// 初始 / 兼容字段保留（用作 V1 种子值），界面展示统一由版本表驱动的展示字段提供。
/// </summary>
public class SpcItem
{
    /// <summary>监控项编号，形如 SPC-P01</summary>
    public string ItemId { get; set; } = "";

    /// <summary>监控项名称，如「卷绕张力」</summary>
    public string ItemName { get; set; } = "";

    /// <summary>类别：工序参数 / 测试数据</summary>
    public string Category { get; set; } = SpcCategory.ProcessParam;

    /// <summary>数据来源键：工序项形如 "卷绕:卷绕张力"；测试项为字段名如 "CapacityF"</summary>
    public string SourceKey { get; set; } = "";

    /// <summary>量纲单位，如 N / g / F / mΩ / %</summary>
    public string Unit { get; set; } = "";

    /// <summary>规格上限（double.NaN 表示无上限）</summary>
    public double Usl { get; set; } = double.NaN;

    /// <summary>规格下限（double.NaN 表示无下限）</summary>
    public double Lsl { get; set; } = double.NaN;

    /// <summary>设备/工序适用范围（空 = 全部设备）</summary>
    public string DeviceScope { get; set; } = "";

    /// <summary>备注（如规格限来源说明）</summary>
    public string Remark { get; set; } = "";

    /// <summary>是否有上限规格</summary>
    public bool HasUsl => !double.IsNaN(Usl);

    /// <summary>是否有下限规格</summary>
    public bool HasLsl => !double.IsNaN(Lsl);

    /// <summary>规格限展示文本，如 "1.500 ~ 2.000 N" / "≥ 95 F" / "≤ 25 mΩ"</summary>
    public string SpecText
    {
        get
        {
            var unit = string.IsNullOrWhiteSpace(Unit) ? "" : $" {Unit}";
            if (HasUsl && HasLsl) return $"{Lsl:F3} ~ {Usl:F3}{unit}";
            if (HasUsl) return $"≤ {Usl:F3}{unit}";
            if (HasLsl) return $"≥ {Lsl:F3}{unit}";
            return "无规格限";
        }
    }

    // ================= 版本化规格展示字段（由仓储 SyncSpcItemVersionInfo 填充） =================

    /// <summary>当前生效版本号（0 = 未标注 / 无版本）</summary>
    public int CurrentVersion { get; set; }

    /// <summary>当前生效版本文本，如 "V2"（无版本时为空串）</summary>
    public string CurrentVersionText { get; set; } = "";

    /// <summary>当前生效版本规格文本，如 "1.550 ~ 2.050"（无版本时为空串）</summary>
    public string CurrentSpecText { get; set; } = "";

    /// <summary>待生效版本描述，如 "V3（2026-09-16 00:00 生效）"（无待生效版本时为空串）</summary>
    public string PendingVersionText { get; set; } = "";

    /// <summary>样本数（该监控项当前已登记的样本点数量）</summary>
    public int SampleCount { get; set; }

    /// <summary>
    /// 下拉 / 列表展示文本：有当前版本时形如「卷绕张力（V1）」，否则回退为项目名称。
    /// 供 SPC 分析页监控项下拉显示版本号。
    /// </summary>
    public string DisplayText => CurrentVersionText.Length > 0 ? $"{ItemName}（{CurrentVersionText}）" : ItemName;
}
