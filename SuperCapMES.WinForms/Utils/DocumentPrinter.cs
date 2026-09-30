using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace SuperCapMES.WinForms.Utils;

/// <summary>
/// 打印模型：单据标题 / 副标题（单号）/ 表头键值 / 明细表 / 条码文本 / 页脚。
/// 由各页面的「打印」按钮构造，交给 <see cref="DocumentPrinter"/> 预览或打印。
/// </summary>
public sealed class PrintModel
{
    /// <summary>单据类型（如「返工单」「工单流转卡」）</summary>
    public string Title { get; init; } = "";

    /// <summary>副标题（通常为单号）</summary>
    public string SubTitle { get; init; } = "";

    /// <summary>表头键值对（两列布局）</summary>
    public List<(string Label, string Value)> Header { get; } = new();

    /// <summary>明细列头</summary>
    public List<string> DetailHeaders { get; } = new();

    /// <summary>明细行（每行与列头对应）</summary>
    public List<List<string>> DetailRows { get; } = new();

    /// <summary>条码内容（空则不绘制）</summary>
    public string BarcodeText { get; init; } = "";

    /// <summary>页脚信息（打印时间 / 操作人）</summary>
    public string Footer { get; init; } = "";

    /// <summary>页眉公司行</summary>
    public string CompanyLine { get; init; } = "北京绿发中科锂电容科技有限公司 · 泰州工厂 MES";
}

/// <summary>
/// 通用单据打印器（**零依赖**：System.Drawing.Printing）。
/// 支持：打印预览（含打印按钮）、直接打印、明细表自动分页（页眉/页脚每页重复）、右上角 Code128 条码 + 明文单号。
/// </summary>
public static class DocumentPrinter
{
    /// <summary>每页明细行数（A4 纵向估算）</summary>
    public const int RowsPerPage = 22;

    /// <summary>计算总页数（纯函数，便于自测）</summary>
    public static int PageCount(int detailRowCount)
    {
        if (detailRowCount <= 0) return 1;
        return Math.Max(1, (int)Math.Ceiling(detailRowCount / (double)RowsPerPage));
    }

    /// <summary>弹出打印预览（用户可在预览窗口内打印）</summary>
    public static void Preview(IWin32Window? owner, PrintModel model)
    {
        var doc = BuildDocument(model);
        using var dlg = new PrintPreviewDialog
        {
            Document = doc,
            Width = 1020,
            Height = 740,
            StartPosition = FormStartPosition.CenterParent,
            Text = $"{model.Title} 打印预览 - {model.SubTitle}"
        };
        dlg.ShowDialog(owner);
        doc.Dispose();
    }

    /// <summary>直接打印到默认打印机</summary>
    public static void Print(PrintModel model)
    {
        var doc = BuildDocument(model);
        doc.Print();
        doc.Dispose();
    }

    private static PrintDocument BuildDocument(PrintModel model)
    {
        var pageIndex = 0;
        var pageCount = PageCount(model.DetailRows.Count);
        var doc = new PrintDocument { DocumentName = $"{model.Title} {model.SubTitle}" };
        doc.PrintPage += (_, e) =>
        {
            if (e.Graphics is null) return;
            RenderPage(e.Graphics, model, pageIndex, pageCount, e.MarginBounds);
            pageIndex++;
            e.HasMorePages = pageIndex < pageCount;
        };
        return doc;
    }

    /// <summary>绘制单页内容（纯绘制，便于在预览与打印间复用）</summary>
    public static void RenderPage(Graphics g, PrintModel model, int pageIndex, int pageCount, Rectangle bounds)
    {
        using var titleFont = new Font("微软雅黑", 16F, FontStyle.Bold);
        using var subFont = new Font("微软雅黑", 11F);
        using var textFont = new Font("微软雅黑", 9F);
        using var boldFont = new Font("微软雅黑", 9F, FontStyle.Bold);
        using var footerFont = new Font("微软雅黑", 8F);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        using var right = new StringFormat { Alignment = StringAlignment.Far };

        var top = bounds.Top;

        // 页眉：公司行 + 标题 + 副标题
        g.DrawString(model.CompanyLine, footerFont, Brushes.Gray,
            new RectangleF(bounds.Left, top, bounds.Width, 16), center);
        top += 20;
        g.DrawString(model.Title, titleFont, Brushes.Black,
            new RectangleF(bounds.Left, top, bounds.Width, 30), center);
        top += 34;
        if (!string.IsNullOrWhiteSpace(model.SubTitle))
        {
            g.DrawString(model.SubTitle, subFont, Brushes.Black,
                new RectangleF(bounds.Left, top, bounds.Width, 22), center);
            top += 26;
        }

        // 右上角条码 + 明文
        if (!string.IsNullOrWhiteSpace(model.BarcodeText))
        {
            var barRect = new Rectangle(bounds.Right - 200, bounds.Top + 40, 190, 48);
            BarcodeCode128.Draw(g, model.BarcodeText, barRect, Color.Black);
            g.DrawString(model.BarcodeText, textFont, Brushes.Black,
                new RectangleF(barRect.Left, barRect.Bottom + 2, barRect.Width, 16), center);
        }

        // 表头键值（两列）
        var colWidth = bounds.Width / 2;
        for (var i = 0; i < model.Header.Count; i++)
        {
            var x = bounds.Left + (i % 2) * colWidth;
            var y = top + (i / 2) * 20;
            g.DrawString(model.Header[i].Label + "：", boldFont, Brushes.Black, x, y);
            g.DrawString(model.Header[i].Value, textFont, Brushes.Black, x + 78, y);
        }
        top += ((model.Header.Count + 1) / 2) * 20 + 8;

        // 明细表
        if (model.DetailHeaders.Count > 0)
        {
            var colW = bounds.Width / model.DetailHeaders.Count;
            g.FillRectangle(Brushes.LightSteelBlue, bounds.Left, top, bounds.Width, 22);
            g.DrawRectangle(Pens.Gray, bounds.Left, top, bounds.Width, 22);
            for (var c = 0; c < model.DetailHeaders.Count; c++)
                g.DrawString(model.DetailHeaders[c], boldFont, Brushes.Black, bounds.Left + c * colW + 4, top + 4);
            top += 22;

            var start = pageIndex * RowsPerPage;
            var end = Math.Min(start + RowsPerPage, model.DetailRows.Count);
            for (var r = start; r < end; r++)
            {
                var row = model.DetailRows[r];
                if ((r - start) % 2 == 1)
                    g.FillRectangle(Brushes.WhiteSmoke, bounds.Left, top, bounds.Width, 20);

                for (var c = 0; c < model.DetailHeaders.Count; c++)
                {
                    var value = c < row.Count ? row[c] : "";
                    g.DrawString(value, textFont, Brushes.Black,
                        new RectangleF(bounds.Left + c * colW + 4, top + 3, colW - 8, 18));
                }
                top += 20;
                if (top > bounds.Bottom - 40) break;
            }

            if (model.DetailRows.Count == 0)
                g.DrawString("（无明细）", textFont, Brushes.Gray, bounds.Left + 6, top + 4);
        }

        // 页脚
        var footer = string.IsNullOrWhiteSpace(model.Footer)
            ? $"第 {pageIndex + 1} / {pageCount} 页"
            : $"{model.Footer}    第 {pageIndex + 1} / {pageCount} 页";
        g.DrawString(footer, footerFont, Brushes.Gray,
            new RectangleF(bounds.Left, bounds.Bottom - 18, bounds.Width, 16), right);
    }
}
