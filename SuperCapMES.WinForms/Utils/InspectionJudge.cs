using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SuperCapMES.WinForms.Utils;

/// <summary>
/// 检验自动判定：解析标准值（如 "1.5~2.0 N"、"≥95 F"、"≤25 mΩ"、"±0.05 mm"、"2.70±0.05 V"），
/// 将实测值与标准范围比较，返回判定结果。无法解析（如 "无褶皱" 等非数值标准）时返回 null，不做自动判定。
/// </summary>
public static class InspectionJudge
{
    /// <summary>
    /// 判定实测值是否合格。
    /// </summary>
    /// <returns>true=合格 / false=不合格 / null=无法自动判定</returns>
    public static bool? TryJudge(string standard, string measured)
    {
        var rule = ParseStandard(standard);
        if (rule is null) return null;

        var mv = ExtractFirstNumber(measured);
        if (mv is null) return null;

        var (min, max) = rule.Value;
        const double eps = 1e-9; // 容差：恰好等于标准边界视为合格（避免浮点精度误判）
        if (min.HasValue && mv.Value < min.Value - eps) return false;
        if (max.HasValue && mv.Value > max.Value + eps) return false;
        return true;
    }

    /// <summary>
    /// 解析标准值为 [Min, Max] 区间。
    /// </summary>
    public static (double? Min, double? Max)? ParseStandard(string? standard)
    {
        if (string.IsNullOrWhiteSpace(standard)) return null;
        var s = standard.Trim();

        // 范围：a~b / a～b
        if (s.Contains('~') || s.Contains('～'))
        {
            var nums = ExtractNumbers(s);
            if (nums.Count >= 2) return (nums[0], nums[1]);
            return null;
        }

        // 下限：≥a / >=a / >a
        if (s.Contains('≥') || s.Contains(">=") || s.StartsWith(">"))
        {
            var n = ExtractFirstNumber(s);
            return n.HasValue ? (n.Value, (double?)null) : null;
        }

        // 上限：≤a / <=a / <a
        if (s.Contains('≤') || s.Contains("<=") || s.StartsWith("<"))
        {
            var n = ExtractFirstNumber(s);
            return n.HasValue ? ((double?)null, n.Value) : null;
        }

        // 偏差：c±d（如 2.70±0.05）或 ±d（如 ±0.05）
        if (s.Contains('±'))
        {
            var nums = ExtractNumbers(s);
            if (nums.Count == 0) return null;
            var idx = s.IndexOf('±');
            var hasCenter = idx > 0 && char.IsDigit(s[idx - 1]);
            if (hasCenter && nums.Count >= 2)
                return (nums[0] - nums[1], nums[0] + nums[1]);
            return (-nums[0], nums[0]);
        }

        return null;
    }

    private static List<double> ExtractNumbers(string s)
    {
        var list = new List<double>();
        foreach (Match m in Regex.Matches(s, @"-?\d+(\.\d+)?"))
        {
            if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                list.Add(v);
        }
        return list;
    }

    private static double? ExtractFirstNumber(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"-?\d+(\.\d+)?");
        if (!m.Success) return null;
        return double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
