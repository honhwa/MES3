using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// SPC 控制图自绘面板（GDI+ 手绘，零外部依赖）。
/// 绘制要素：网格与 Y 轴刻度、规格限 USL/LSL（红/蓝点线并标注）、中心线 CL（绿实线）、
/// 控制限 UCL/LCL（橙虚线）、数据折线（蓝）+ 数据点；超控制限/超规格点为红色实心；
/// 判异命中点加红色圆环并标注序号；X 轴显示首/中/末采样时间；顶部标注 N/CL/UCL/LCL。
/// 样本不足（&lt;2 点）时中央显示灰色提示，绝不抛异常。
/// </summary>
public sealed class SpcControlChart : Panel
{
    private IReadOnlyList<SpcSample> _samples = Array.Empty<SpcSample>();
    private SpcResult? _result;
    private SpcItem? _item;

    public SpcControlChart()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
    }

    /// <summary>设置绘图数据并触发重绘。</summary>
    public void SetData(SpcItem? item, IReadOnlyList<SpcSample>? samples, SpcResult? result)
    {
        _item = item;
        _samples = samples ?? Array.Empty<SpcSample>();
        _result = result;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        if (rect.Width < 180 || rect.Height < 120) return;

        using var titleFont = new Font("微软雅黑", 10F, FontStyle.Bold);
        using var labelFont = new Font("微软雅黑", 8F);
        using var annFont = new Font("微软雅黑", 8F, FontStyle.Bold);
        using var hintFont = new Font("微软雅黑", 10F);
        using var titleBrush = new SolidBrush(Color.FromArgb(15, 76, 129));
        using var labelBrush = new SolidBrush(Color.FromArgb(90, 100, 115));
        using var hintBrush = new SolidBrush(Color.FromArgb(150, 158, 170));

        var res = _result;
        if (_samples.Count < 2 || res is null || !double.IsFinite(res.Mean))
        {
            const string hint = "暂无足够样本（需 ≥2 点）";
            var size = g.MeasureString(hint, hintFont);
            g.DrawString(hint, hintFont, hintBrush,
                (rect.Width - size.Width) / 2f, (rect.Height - size.Height) / 2f);
            return;
        }

        const int padL = 68;
        const int padR = 26;
        const int padT = 48;
        const int padB = 36;
        var plot = new Rectangle(
            padL, padT,
            Math.Max(10, rect.Width - padL - padR),
            Math.Max(10, rect.Height - padT - padB));

        // ---- Y 轴范围（纳入规格限与控制限，留 12% 余量）----
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        void Include(double v)
        {
            if (!double.IsFinite(v)) return;
            if (v < yMin) yMin = v;
            if (v > yMax) yMax = v;
        }
        foreach (var s in _samples) Include(s.Value);
        Include(res.Ucl); Include(res.Lcl); Include(res.Usl); Include(res.Lsl);
        foreach (var seg in res.SpecSegments)
        {
            Include(seg.Usl);
            Include(seg.Lsl);
        }
        if (!double.IsFinite(yMin) || !double.IsFinite(yMax))
        {
            yMin = 0; yMax = 1;
        }
        var range = yMax - yMin;
        if (range <= 0) range = Math.Max(1, Math.Abs(yMax) * 0.4);
        var pad = range * 0.12;
        yMin -= pad; yMax += pad;
        if (yMax <= yMin) yMax = yMin + 1;

        float Y2Pix(double v) => (float)(plot.Bottom - (v - yMin) / (yMax - yMin) * plot.Height);
        float X2Pix(int i) => plot.Left + plot.Width * (float)i / (_samples.Count - 1);

        // ---- 标题 ----
        var itemName = _item?.ItemName ?? "控制图";
        var unit = string.IsNullOrWhiteSpace(_item?.Unit) ? "" : $"（{_item!.Unit}）";
        g.DrawString($"{itemName} 控制图{unit}", titleFont, titleBrush, padL - 4, 6);

        // ---- 顶部统计标注 ----
        var info = $"N={_samples.Count}   CL={F(res.Cl)}   UCL={F(res.Ucl)}   LCL={F(res.Lcl)}";
        g.DrawString(info, annFont, labelBrush, padL - 4, 27);

        // ---- 网格 + Y 轴刻度 ----
        using var gridPen = new Pen(Color.FromArgb(228, 233, 240), 1);
        using var axisPen = new Pen(Color.FromArgb(120, 130, 145), 1);
        const int gridLines = 4;
        for (var i = 0; i <= gridLines; i++)
        {
            var y = plot.Bottom - (int)(plot.Height * i / (double)gridLines);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var val = yMin + (yMax - yMin) * i / gridLines;
            g.DrawString(ValText(val, range), labelFont, labelBrush, plot.Left - 62f, y - 8f);
        }
        g.DrawRectangle(axisPen, plot);

        // ---- 规格限（版本化：单版本退化为整条点线；多版本按段绘制并标注版本号）----
        var specLookup = new Dictionary<int, (double Usl, double Lsl)>();
        foreach (var seg in res.SpecSegments)
            specLookup[seg.Version] = (seg.Usl, seg.Lsl);

        // 逐点是否超规格（优先按该点生效版本；无版本信息时回退整条规格）
        bool OverSpec(int i)
        {
            var v = _samples[i].Value;
            if (specLookup.Count > 0 && res.PointVersions.Count == _samples.Count
                && specLookup.TryGetValue(res.PointVersions[i], out var sp))
            {
                return (double.IsFinite(sp.Usl) && v > sp.Usl) || (double.IsFinite(sp.Lsl) && v < sp.Lsl);
            }
            return (double.IsFinite(res.Usl) && v > res.Usl) || (double.IsFinite(res.Lsl) && v < res.Lsl);
        }

        if (res.SpecSegments.Count > 1)
        {
            DrawSpecSegments(g, plot, res, specLookup, labelFont, Y2Pix, X2Pix, yMin, yMax);
        }
        else
        {
            using var uslPen = new Pen(Color.FromArgb(192, 0, 0), 1.4f) { DashStyle = DashStyle.Dot };
            using var lslPen = new Pen(Color.FromArgb(15, 76, 129), 1.4f) { DashStyle = DashStyle.Dot };
            using var uslBrush = new SolidBrush(Color.FromArgb(192, 0, 0));
            using var lslBrush = new SolidBrush(Color.FromArgb(15, 76, 129));
            var verTag = res.CurrentVersionText.Length > 0 ? $" ({res.CurrentVersionText})" : "";
            if (double.IsFinite(res.Usl) && res.Usl >= yMin && res.Usl <= yMax)
            {
                var y = Y2Pix(res.Usl);
                g.DrawLine(uslPen, plot.Left, y, plot.Right, y);
                g.DrawString($"USL {F(res.Usl)}{verTag}", labelFont, uslBrush, plot.Right - 118f, (float)y - 15f);
            }
            if (double.IsFinite(res.Lsl) && res.Lsl >= yMin && res.Lsl <= yMax)
            {
                var y = Y2Pix(res.Lsl);
                g.DrawLine(lslPen, plot.Left, y, plot.Right, y);
                g.DrawString($"LSL {F(res.Lsl)}{verTag}", labelFont, lslBrush, plot.Right - 118f, (float)y + 2f);
            }
        }

        // ---- 中心线 / 控制限 ----
        using var clPen = new Pen(Color.FromArgb(39, 174, 96), 1.8f);
        using var ctrlPen = new Pen(Color.FromArgb(230, 126, 34), 1.4f) { DashStyle = DashStyle.Dash };
        using var ctrlBrush = new SolidBrush(Color.FromArgb(230, 126, 34));
        using var clBrush = new SolidBrush(Color.FromArgb(39, 174, 96));
        if (double.IsFinite(res.Ucl))
        {
            var y = Y2Pix(res.Ucl);
            g.DrawLine(ctrlPen, plot.Left, y, plot.Right, y);
            g.DrawString($"UCL {F(res.Ucl)}", labelFont, ctrlBrush, plot.Left + 4f, (float)y - 15f);
        }
        if (double.IsFinite(res.Lcl))
        {
            var y = Y2Pix(res.Lcl);
            g.DrawLine(ctrlPen, plot.Left, y, plot.Right, y);
            g.DrawString($"LCL {F(res.Lcl)}", labelFont, ctrlBrush, plot.Left + 4f, (float)y + 2f);
        }
        {
            var y = Y2Pix(res.Cl);
            g.DrawLine(clPen, plot.Left, y, plot.Right, y);
            g.DrawString($"CL {F(res.Cl)}", labelFont, clBrush, plot.Right - 60f, (float)y - 15f);
        }

        // ---- 数据折线 + 数据点 ----
        var pts = new PointF[_samples.Count];
        for (var i = 0; i < _samples.Count; i++)
            pts[i] = new PointF(X2Pix(i), (float)Y2Pix(_samples[i].Value));

        using var linePen = new Pen(Color.FromArgb(41, 128, 185), 1.8f);
        g.DrawLines(linePen, pts);

        using var dotBrush = new SolidBrush(Color.FromArgb(41, 128, 185));
        using var outBrush = new SolidBrush(Color.FromArgb(192, 0, 0));
        using var hitPen = new Pen(Color.FromArgb(220, 20, 60), 1.8f);

        var hitIndexes = new HashSet<int>();
        foreach (var (_, indexes) in res.RuleHits)
            foreach (var k in indexes) hitIndexes.Add(k);

        for (var i = 0; i < _samples.Count; i++)
        {
            var v = _samples[i].Value;
            var outOfControl = double.IsFinite(res.Ucl) && (v > res.Ucl || v < res.Lcl);
            var outOfSpec = OverSpec(i);

            if (outOfControl || outOfSpec)
                g.FillEllipse(outBrush, pts[i].X - 3.5f, pts[i].Y - 3.5f, 7f, 7f);
            else
                g.FillEllipse(dotBrush, pts[i].X - 2.5f, pts[i].Y - 2.5f, 5f, 5f);
        }

        // ---- 判异命中点：红圈 + 序号 ----
        using var hitBrush = new SolidBrush(Color.FromArgb(220, 20, 60));
        foreach (var i in hitIndexes.OrderBy(x => x))
        {
            if (i < 0 || i >= pts.Length) continue;
            g.DrawEllipse(hitPen, pts[i].X - 6f, pts[i].Y - 6f, 12f, 12f);
            if (_samples.Count <= 30)
                g.DrawString((i + 1).ToString(), annFont, hitBrush, pts[i].X - 4f, pts[i].Y - 22f);
        }

        // ---- X 轴时间标签（首/中/末）----
        var mid = _samples.Count / 2;
        DrawTimeLabel(g, labelFont, labelBrush, plot, pts[0].X, _samples[0].SampleTime, true, false);
        if (mid != 0 && mid != _samples.Count - 1)
            DrawTimeLabel(g, labelFont, labelBrush, plot, pts[mid].X, _samples[mid].SampleTime, false, false);
        DrawTimeLabel(g, labelFont, labelBrush, plot, pts[^1].X, _samples[^1].SampleTime, false, true);
    }

    /// <summary>版本配色（V1…V5 循环）：用于多版本分段规格线及版本号标注。</summary>
    private static readonly Color[] SpecVersionColors =
    {
        Color.FromArgb(192, 0, 0),     // V1 红
        Color.FromArgb(15, 76, 129),   // V2 蓝
        Color.FromArgb(39, 174, 96),   // V3 绿
        Color.FromArgb(142, 68, 173),  // V4 紫
        Color.FromArgb(211, 84, 0)     // V5 深橙
    };

    private static Color SpecColor(int version) =>
        version <= 0
            ? Color.FromArgb(120, 130, 145)
            : SpecVersionColors[(version - 1) % SpecVersionColors.Length];

    /// <summary>
    /// 按「样本逐点生效版本」分段绘制规格线（USL/LSL），不同版本用不同颜色，并在段顶部中点标注版本号。
    /// 段内左/右像素取该段首末样本的 X 坐标；单点段向左/右略微延展以免不可见。绝不抛异常。
    /// </summary>
    private static void DrawSpecSegments(
        Graphics g,
        Rectangle plot,
        SpcResult res,
        Dictionary<int, (double Usl, double Lsl)> specLookup,
        Font labelFont,
        Func<double, float> y2pix,
        Func<int, float> x2pix,
        double yMin,
        double yMax)
    {
        var pv = res.PointVersions;
        if (pv is null || pv.Count == 0) return;

        var n = pv.Count;
        var segStart = 0;
        for (var i = 1; i <= n; i++)
        {
            if (i < n && pv[i] == pv[segStart]) continue;

            var vid = pv[segStart];
            if (specLookup.TryGetValue(vid, out var spec))
            {
                var color = SpecColor(vid);
                var left = x2pix(segStart);
                var right = i - 1 > segStart ? x2pix(i - 1) : x2pix(Math.Min(segStart + 1, n - 1));
                if (right <= left) right = left + 6f;

                using var pen = new Pen(color, 1.5f) { DashStyle = DashStyle.Dot };
                using var brush = new SolidBrush(color);

                if (double.IsFinite(spec.Usl) && spec.Usl >= yMin && spec.Usl <= yMax)
                {
                    var y = y2pix(spec.Usl);
                    g.DrawLine(pen, left, y, right, y);
                }
                if (double.IsFinite(spec.Lsl) && spec.Lsl >= yMin && spec.Lsl <= yMax)
                {
                    var y = y2pix(spec.Lsl);
                    g.DrawLine(pen, left, y, right, y);
                }

                if (vid > 0)
                {
                    var verText = $"V{vid}";
                    var mid = (left + right) / 2f;
                    var size = g.MeasureString(verText, labelFont);
                    g.DrawString(verText, labelFont, brush, mid - size.Width / 2f, plot.Top + 4f);
                }
            }
            segStart = i;
        }
    }

    private static void DrawTimeLabel(
        Graphics g, Font font, Brush brush, Rectangle plot, float x, DateTime time, bool leftAlign, bool rightAlign)
    {
        var text = time.ToString("MM-dd HH:mm");
        var size = g.MeasureString(text, font);
        var tx = rightAlign ? x - size.Width : leftAlign ? x : x - size.Width / 2f;
        g.DrawString(text, font, brush, tx, plot.Bottom + 8f);
    }

    private static string F(double v) => double.IsFinite(v) ? v.ToString("F3") : "—";

    private static string ValText(double v, double range) =>
        range < 1 ? v.ToString("F3") : range < 10 ? v.ToString("F2") : v.ToString("F1");
}
