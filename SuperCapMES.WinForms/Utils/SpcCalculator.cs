using System;
using System.Collections.Generic;
using System.Linq;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Utils;

/// <summary>SPC 过程能力分级常量</summary>
public static class SpcCapability
{
    /// <summary>Cpk ≥ 1.33（绿）</summary>
    public const string Good = "良好";

    /// <summary>1.00 ≤ Cpk &lt; 1.33（橙）</summary>
    public const string Watch = "需关注";

    /// <summary>Cpk &lt; 1.00（红）</summary>
    public const string Poor = "不足";

    /// <summary>Cpk 无法计算（灰）</summary>
    public const string Unevaluable = "无法评估";
}

/// <summary>判异规则名常量（Nelson 简化版）</summary>
public static class SpcRuleNames
{
    /// <summary>规则①：单点超出控制限（任一点 &gt; UCL 或 &lt; LCL）</summary>
    public const string OutOfControl = "单点超出控制限";

    /// <summary>规则②：连续 6 点递增（或递减）</summary>
    public const string Monotonic = "连续 6 点递增（或递减）";

    /// <summary>规则③：连续 9 点在中心线同侧（严格同侧，不含恰好等于 CL）</summary>
    public const string SameSide = "连续 9 点在中心线同侧";

    /// <summary>规则④：连续 3 点中有 2 点超出 2σ（同侧）</summary>
    public const string TwoOfThree = "连续 3 点中有 2 点超出 2σ";
}

/// <summary>
/// SPC 控制图与过程能力计算结果（不可变）。
/// 口径说明：<see cref="Sigma"/> 为「样本标准差（n-1）」；控制限按 <c>CL ± 3×样本标准差</c> 估计
/// （非 I-MR 的 MR̄/d2 口径）。无法计算的相关字段为 <see cref="double.NaN"/>，由界面统一显示「—」。
/// </summary>
public sealed class SpcResult
{
    /// <summary>样本量</summary>
    public int N { get; init; }

    /// <summary>均值 CL = Σx / n</summary>
    public double Mean { get; init; } = double.NaN;

    /// <summary>样本标准差 σ = sqrt(Σ(x-mean)² / (n-1))</summary>
    public double Sigma { get; init; } = double.NaN;

    /// <summary>中心线（= 均值）</summary>
    public double Cl { get; init; } = double.NaN;

    /// <summary>控制上限 = 均值 + 3σ</summary>
    public double Ucl { get; init; } = double.NaN;

    /// <summary>控制下限 = 均值 - 3σ</summary>
    public double Lcl { get; init; } = double.NaN;

    /// <summary>规格上限（无则 NaN）</summary>
    public double Usl { get; init; } = double.NaN;

    /// <summary>规格下限（无则 NaN）</summary>
    public double Lsl { get; init; } = double.NaN;

    /// <summary>Cp = (USL-LSL)/(6σ)（仅双边规格且 σ&gt;0）</summary>
    public double Cp { get; init; } = double.NaN;

    /// <summary>Cpk 过程能力指数（σ&gt;0 且至少一侧规格存在）</summary>
    public double Cpk { get; init; } = double.NaN;

    /// <summary>能力分级：良好 / 需关注 / 不足 / 无法评估</summary>
    public string Capability { get; init; } = SpcCapability.Unevaluable;

    /// <summary>超规格点数量（Value &gt; USL 或 Value &lt; LSL；NaN 规格跳过）</summary>
    public int OutOfSpecCount { get; init; }

    /// <summary>判异命中列表：规则名 + 命中点序号集合（升序、去重）</summary>
    public List<(string RuleName, List<int> Indexes)> RuleHits { get; init; } = new();

    // ================= 版本化规格相关（由 ComputeWithVersions 填充；Compute 保持默认值） =================

    /// <summary>当前生效版本号（0 = 无版本信息）</summary>
    public int CurrentVersion { get; init; }

    /// <summary>当前生效版本文本，如 "V2"（无版本信息时为空串）</summary>
    public string CurrentVersionText { get; init; } = "";

    /// <summary>当前生效版本规格上限（无则 NaN）</summary>
    public double CurrentUsl { get; init; } = double.NaN;

    /// <summary>当前生效版本规格下限（无则 NaN）</summary>
    public double CurrentLsl { get; init; } = double.NaN;

    /// <summary>
    /// 规格分段列表：按相邻样本的生效版本变化切段，段内 From/To 取该段首末样本采样时间，
    /// Usl/Lsl 为该段生效版本的规格（无则 NaN）。用于控制图按段绘制规格线。
    /// </summary>
    public List<(int Version, DateTime From, DateTime To, double Usl, double Lsl)> SpecSegments { get; init; } = new();

    /// <summary>按版本统计的超规格点数量（仅含 &gt;0 的版本，按版本号升序）。</summary>
    public List<(int Version, int Count)> OutOfSpecByVersion { get; init; } = new();

    /// <summary>与样本一一对应的生效版本号列表（0 表示该点无版本信息）。</summary>
    public List<int> PointVersions { get; init; } = new();
}

/// <summary>
/// SPC 计算核心：纯静态、纯函数、不依赖任何仓储，便于独立验证。
/// 所有方法对空集合 / 单点 / 常量序列 / NaN 规格均安全返回，绝不抛异常。
///
/// 统一口径（务必以此为准）：
///   均值 Mean = Σx / n；
///   标准差 Sigma = sqrt(Σ(x-mean)² / (n-1))（样本标准差，n-1；n&lt;2 → NaN）；
///   控制限 UCL/LCL = Mean ± 3×Sigma（样本标准差 3σ 估计，非 I-MR 的 MR̄/d2）；
///   Cp  = (USL-LSL)/(6σ)（仅双边规格且 σ&gt;0，否则 NaN）；
///   Cpk = 双边取 min((USL-Mean)/(3σ), (Mean-LSL)/(3σ))；仅 USL 取 (USL-Mean)/(3σ)；仅 LSL 取 (Mean-LSL)/(3σ)；
///         σ≤0 或两侧规格均缺失 → NaN。
/// 判异规则命中点约定：
///   规则①→超出控制限的点下标；规则②→落入连续单调段的所有点下标；规则③→落入连续同侧段的所有点下标；
///   规则④→触发该窗口的「超出 2σ」点下标。
/// </summary>
public static class SpcCalculator
{
    /// <summary>
    /// 计算控制图统计量与过程能力，并执行 4 条简化 Nelson 判异规则。
    /// </summary>
    /// <param name="samples">样本集合（按其顺序即时间顺序）。可为 null / 空。</param>
    /// <param name="usl">规格上限（double.NaN 表示无上限）</param>
    /// <param name="lsl">规格下限（double.NaN 表示无下限）</param>
    public static SpcResult Compute(IReadOnlyList<SpcSample>? samples, double usl, double lsl)
    {
        var hasUsl = double.IsFinite(usl);
        var hasLsl = double.IsFinite(lsl);

        if (samples is null || samples.Count == 0)
        {
            return new SpcResult { N = 0, Usl = hasUsl ? usl : double.NaN, Lsl = hasLsl ? lsl : double.NaN };
        }

        var n = samples.Count;
        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = samples[i].Value;

        // ---- 均值 ----
        double sum = 0;
        for (var i = 0; i < n; i++) sum += values[i];
        var mean = sum / n;

        // ---- 样本标准差（n-1）----
        var sigma = double.NaN;
        if (n >= 2)
        {
            double ss = 0;
            for (var i = 0; i < n; i++)
            {
                var d = values[i] - mean;
                ss += d * d;
            }
            sigma = Math.Sqrt(ss / (n - 1));
        }

        // ---- 控制限 / 能力 ----
        var ucl = double.NaN;
        var lcl = double.NaN;
        var cp = double.NaN;
        var cpk = double.NaN;
        var capability = SpcCapability.Unevaluable;

        var sigmaUsable = double.IsFinite(sigma) && sigma > 0;
        if (sigmaUsable)
        {
            ucl = mean + 3.0 * sigma;
            lcl = mean - 3.0 * sigma;

            if (hasUsl && hasLsl)
                cp = (usl - lsl) / (6.0 * sigma);

            if (hasUsl || hasLsl)
            {
                var upper = hasUsl ? (usl - mean) / (3.0 * sigma) : double.PositiveInfinity;
                var lower = hasLsl ? (mean - lsl) / (3.0 * sigma) : double.PositiveInfinity;
                cpk = Math.Min(upper, lower);
            }

            capability = !double.IsFinite(cpk) ? SpcCapability.Unevaluable
                : cpk >= 1.33 ? SpcCapability.Good
                : cpk >= 1.00 ? SpcCapability.Watch
                : SpcCapability.Poor;
        }

        // ---- 超规格计数 ----
        var outOfSpec = 0;
        for (var i = 0; i < n; i++)
        {
            if (hasUsl && values[i] > usl) { outOfSpec++; continue; }
            if (hasLsl && values[i] < lsl) { outOfSpec++; }
        }

        // ---- 判异规则 ----
        var hits = new List<(string RuleName, List<int> Indexes)>();
        if (sigmaUsable || double.IsFinite(mean))
        {
            AddRuleHits(hits, values, mean, sigma, ucl, lcl, sigmaUsable);
        }

        return new SpcResult
        {
            N = n,
            Mean = mean,
            Sigma = sigma,
            Cl = mean,
            Ucl = ucl,
            Lcl = lcl,
            Usl = hasUsl ? usl : double.NaN,
            Lsl = hasLsl ? lsl : double.NaN,
            Cp = cp,
            Cpk = cpk,
            Capability = capability,
            OutOfSpecCount = outOfSpec,
            RuleHits = hits
        };
    }

    /// <summary>
    /// 版本化规格下的控制图 / 过程能力计算：
    ///   ① 每条样本按其 <see cref="SpcSample.SampleTime"/> 选择「生效版本」（<see cref="GetVersionAt"/>）→
    ///      逐点分段判定是否超规格，汇总 <see cref="SpcResult.OutOfSpecCount"/> / <see cref="SpcResult.OutOfSpecByVersion"/> /
    ///      <see cref="SpcResult.PointVersions"/>；
    ///   ② <see cref="SpcResult.SpecSegments"/> 按相邻点版本变化切段（段内 From/To 取该段首末样本时间）；
    ///   ③ 能力指标 Cp/Cpk 用「当前生效版本」（now 时刻）的规格计算（<see cref="SpcResult.CurrentVersion"/> 等）；
    ///   ④ 判异规则与 Mean/Sigma/UCL/LCL 口径完全沿用 <see cref="Compute"/>（内部复用，避免两份实现）。
    /// 无版本 / 版本为空时安全降级：CurrentVersion=0、SpecSegments 为空、按无规格处理，绝不抛异常。
    /// </summary>
    /// <param name="samples">样本集合（按其顺序即时间顺序）。可为 null / 空。</param>
    /// <param name="versions">规格版本集合（可为 null / 空）</param>
    /// <param name="now">判定「当前生效版本」的基准时刻；缺省为 <see cref="DateTime.Now"/></param>
    public static SpcResult ComputeWithVersions(
        IReadOnlyList<SpcSample>? samples,
        IReadOnlyList<SpcItemVersion>? versions,
        DateTime? now = null)
    {
        var nowVal = now ?? DateTime.Now;

        var current = GetVersionAt(versions, nowVal);
        var currentUsl = current is not null && current.HasUsl ? current.Usl : double.NaN;
        var currentLsl = current is not null && current.HasLsl ? current.Lsl : double.NaN;

        var list = samples ?? Array.Empty<SpcSample>();
        var n = list.Count;

        // 统计与判异口径沿用既有 Compute（用当前生效版本规格）
        var baseRes = Compute(list, currentUsl, currentLsl);

        // ---- 逐点选版本 + 分段超规格统计 ----
        var pointVersions = new List<int>(n);
        var byVersion = new Dictionary<int, int>();
        var outOfSpec = 0;
        for (var i = 0; i < n; i++)
        {
            var ver = GetVersionAt(versions, list[i].SampleTime);
            var vid = ver?.Version ?? 0;
            pointVersions.Add(vid);

            var v = list[i].Value;
            var over = (ver is not null && ver.HasUsl && v > ver.Usl)
                    || (ver is not null && ver.HasLsl && v < ver.Lsl);
            if (over)
            {
                outOfSpec++;
                byVersion[vid] = byVersion.TryGetValue(vid, out var c) ? c + 1 : 1;
            }
        }

        // ---- 规格分段（相邻点版本变化切段）----
        var segments = new List<(int Version, DateTime From, DateTime To, double Usl, double Lsl)>();
        if (versions is not null && versions.Count > 0 && n > 0)
        {
            var segStart = 0;
            for (var i = 1; i <= n; i++)
            {
                if (i < n && pointVersions[i] == pointVersions[segStart]) continue;

                var vid = pointVersions[segStart];
                var ver = FindVersion(versions, vid);
                segments.Add((
                    vid,
                    list[segStart].SampleTime,
                    list[i - 1].SampleTime,
                    ver is not null && ver.HasUsl ? ver.Usl : double.NaN,
                    ver is not null && ver.HasLsl ? ver.Lsl : double.NaN));
                segStart = i;
            }
        }

        var byVersionList = byVersion
            .OrderBy(kv => kv.Key)
            .Select(kv => (Version: kv.Key, Count: kv.Value))
            .ToList();

        return new SpcResult
        {
            N = baseRes.N,
            Mean = baseRes.Mean,
            Sigma = baseRes.Sigma,
            Cl = baseRes.Cl,
            Ucl = baseRes.Ucl,
            Lcl = baseRes.Lcl,
            Usl = currentUsl,
            Lsl = currentLsl,
            Cp = baseRes.Cp,
            Cpk = baseRes.Cpk,
            Capability = baseRes.Capability,
            OutOfSpecCount = outOfSpec,
            RuleHits = baseRes.RuleHits,
            CurrentVersion = current?.Version ?? 0,
            CurrentVersionText = current?.VersionText ?? "",
            CurrentUsl = currentUsl,
            CurrentLsl = currentLsl,
            SpecSegments = segments,
            OutOfSpecByVersion = byVersionList,
            PointVersions = pointVersions
        };
    }

    /// <summary>
    /// 版本选择规则（纯函数）：在 <paramref name="versions"/> 中取「EffectiveFrom ≤ moment」的版本中
    /// <b>最新</b>的一版；若 <paramref name="moment"/> 早于所有版本 → 返回<b>最早</b>版本兜底；空集合 → null。
    /// 同刻（EffectiveFrom 相同）取版本号较大者。输入无需预先排序。
    /// </summary>
    public static SpcItemVersion? GetVersionAt(IReadOnlyList<SpcItemVersion>? versions, DateTime moment)
    {
        if (versions is null || versions.Count == 0) return null;

        SpcItemVersion? best = null;      // EffectiveFrom ≤ moment 中最新的
        SpcItemVersion? earliest = null;  // 全部版本中最早的（兜底）
        foreach (var v in versions)
        {
            if (earliest is null
                || v.EffectiveFrom < earliest.EffectiveFrom
                || (v.EffectiveFrom == earliest.EffectiveFrom && v.Version < earliest.Version))
            {
                earliest = v;
            }

            if (v.EffectiveFrom <= moment)
            {
                if (best is null
                    || v.EffectiveFrom > best.EffectiveFrom
                    || (v.EffectiveFrom == best.EffectiveFrom && v.Version > best.Version))
                {
                    best = v;
                }
            }
        }
        return best ?? earliest;
    }

    /// <summary>按版本号查找版本（找不到 → null）。</summary>
    private static SpcItemVersion? FindVersion(IReadOnlyList<SpcItemVersion> versions, int version)
    {
        for (var i = 0; i < versions.Count; i++)
            if (versions[i].Version == version) return versions[i];
        return null;
    }

    /// <summary>执行 4 条判异规则并追加命中项（命中点下标升序去重）。</summary>
    private static void AddRuleHits(
        List<(string RuleName, List<int> Indexes)> hits,
        double[] v, double mean, double sigma, double ucl, double lcl, bool sigmaUsable)
    {
        var n = v.Length;

        // 规则①：单点超出控制限
        if (double.IsFinite(ucl) && double.IsFinite(lcl))
        {
            var idx = new List<int>();
            for (var i = 0; i < n; i++)
                if (v[i] > ucl || v[i] < lcl) idx.Add(i);
            if (idx.Count > 0) hits.Add((SpcRuleNames.OutOfControl, idx));
        }

        // 规则②：连续 6 点递增或递减
        if (n >= 6)
        {
            var mono = new SortedSet<int>();
            for (var s = 0; s <= n - 6; s++)
            {
                var inc = true;
                var dec = true;
                for (var k = s; k < s + 5; k++)
                {
                    if (v[k + 1] <= v[k]) inc = false;
                    if (v[k + 1] >= v[k]) dec = false;
                }
                if (!inc && !dec) continue;
                for (var k = s; k < s + 6; k++) mono.Add(k);
            }
            if (mono.Count > 0) hits.Add((SpcRuleNames.Monotonic, mono.ToList()));
        }

        // 规则③：连续 9 点在中心线同侧（严格同侧，不含等于 CL）
        if (n >= 9 && double.IsFinite(mean))
        {
            var same = new SortedSet<int>();
            var i = 0;
            while (i < n)
            {
                var side = v[i] > mean ? 1 : v[i] < mean ? -1 : 0;
                if (side == 0) { i++; continue; }
                var j = i;
                while (j < n)
                {
                    var sj = v[j] > mean ? 1 : v[j] < mean ? -1 : 0;
                    if (sj != side) break;
                    j++;
                }
                if (j - i >= 9)
                    for (var k = i; k < j; k++) same.Add(k);
                i = j;
            }
            if (same.Count > 0) hits.Add((SpcRuleNames.SameSide, same.ToList()));
        }

        // 规则④：连续 3 点中有 2 点超出 2σ（同侧）
        if (n >= 3 && sigmaUsable && double.IsFinite(mean))
        {
            var upper = mean + 2.0 * sigma;
            var lower = mean - 2.0 * sigma;
            var two = new SortedSet<int>();
            for (var s = 0; s <= n - 3; s++)
            {
                var above = new List<int>();
                var below = new List<int>();
                for (var k = s; k < s + 3; k++)
                {
                    if (v[k] > upper) above.Add(k);
                    else if (v[k] < lower) below.Add(k);
                }
                if (above.Count >= 2) foreach (var k in above) two.Add(k);
                if (below.Count >= 2) foreach (var k in below) two.Add(k);
            }
            if (two.Count > 0) hits.Add((SpcRuleNames.TwoOfThree, two.ToList()));
        }
    }

    /// <summary>样本标准差（n-1）；n&lt;2 或含非有限值 → NaN。</summary>
    public static double SampleSigma(IReadOnlyList<double>? values)
    {
        if (values is null || values.Count < 2) return double.NaN;
        double sum = 0;
        for (var i = 0; i < values.Count; i++) sum += values[i];
        var mean = sum / values.Count;
        double ss = 0;
        for (var i = 0; i < values.Count; i++)
        {
            var d = values[i] - mean;
            ss += d * d;
        }
        return Math.Sqrt(ss / (values.Count - 1));
    }
}
