using System.Drawing;

namespace SuperCapMES.WinForms.Utils;

/// <summary>
/// Code128B 条码生成（**零依赖**：内置标准 patterns 表）。
/// Code128B 覆盖 ASCII 32~126，可编码电芯 SN、单据号等（字母/数字/连字符均可）。
/// 说明：编码表为 Code128 标准 patterns（0~106）；Stop 为 7 元素。
/// 打印件同时输出**明文单号**，即使扫码环境异常也可人工识别。
/// </summary>
public static class BarcodeCode128
{
    /// <summary>Code128 标准宽度模式（0~106；Stop=106 为 7 个元素）</summary>
    private static readonly string[] Patterns =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112"
    };

    /// <summary>Code128B 起止代码：StartB = 104、Stop = 106</summary>
    private const int StartB = 104;
    private const int Stop = 106;

    /// <summary>
    /// 计算 Code128 校验位（mod 103）：起始符 StartB(104) 权重 1，其后每个数据字符权重为其位置（从 1 开始）。
    /// 含非 ASCII 32~126 字符时返回 -1（便于自测独立核验）。
    /// </summary>
    public static int ComputeCheckCode(string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        foreach (var ch in text)
            if (ch < 32 || ch > 126) return -1;

        var sum = StartB;
        for (var i = 0; i < text.Length; i++)
            sum += (text[i] - 32) * (i + 1);
        return sum % 103;
    }

    /// <summary>
    /// 编码为条空序列（true = 条 / false = 空，不含静区）。
    /// 含非 ASCII 32~126 字符时返回 null。
    /// </summary>
    public static List<bool>? Encode(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var ch in text)
            if (ch < 32 || ch > 126) return null;

        var codes = new List<int> { StartB };
        var sum = StartB;
        for (var i = 0; i < text.Length; i++)
        {
            var code = text[i] - 32;          // Code128B：字符值 = ASCII - 32
            codes.Add(code);
            sum += code * (i + 1);            // 校验位权重从 1 开始
        }
        codes.Add(sum % 103);                 // 校验位（mod 103）
        codes.Add(Stop);

        var bars = new List<bool>();
        foreach (var code in codes)
        {
            var pattern = Patterns[code];
            var isBar = true;
            foreach (var ch in pattern)
            {
                var width = ch - '0';
                for (var i = 0; i < width; i++) bars.Add(isBar);
                isBar = !isBar;               // 条空交替
            }
        }
        return bars;
    }

    /// <summary>条码总模块宽度（不含静区）</summary>
    public static int ModuleCount(string text)
    {
        var bars = Encode(text);
        return bars?.Count ?? 0;
    }

    /// <summary>绘制条码到指定区域（自动按高度缩放，宽度不足时缩小模块宽度）。返回是否绘制成功。</summary>
    public static bool Draw(Graphics g, string text, Rectangle bounds, Color? color = null, int quietZone = 8)
    {
        var bars = Encode(text);
        if (bars is null || bars.Count == 0) return false;

        var usableWidth = bounds.Width - quietZone * 2;
        if (usableWidth <= 0) return false;

        var moduleWidth = Math.Max(1, usableWidth / bars.Count);
        var totalWidth = moduleWidth * bars.Count;
        var x = bounds.Left + Math.Max(quietZone, (bounds.Width - totalWidth) / 2);
        var brush = new SolidBrush(color ?? Color.Black);

        var current = bars[0];
        var runLength = 0;
        foreach (var bar in bars)
        {
            if (bar == current) { runLength++; continue; }
            if (current) g.FillRectangle(brush, x, bounds.Top, runLength * moduleWidth, bounds.Height);
            x += runLength * moduleWidth;
            current = bar;
            runLength = 1;
        }
        if (current) g.FillRectangle(brush, x, bounds.Top, runLength * moduleWidth, bounds.Height);

        brush.Dispose();
        return true;
    }

    /// <summary>生成条码位图（供窗体或文档嵌入）。</summary>
    public static Bitmap? Render(string text, int height = 60, int moduleWidth = 2, int quietZone = 8)
    {
        var bars = Encode(text);
        if (bars is null || bars.Count == 0) return null;

        var width = bars.Count * moduleWidth + quietZone * 2;
        var bmp = new Bitmap(Math.Max(width, 1), Math.Max(height, 1));
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        Draw(g, text, new Rectangle(0, 0, bmp.Width, bmp.Height), Color.Black, quietZone);
        return bmp;
    }
}
