using System.Text;
using System.Text.RegularExpressions;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 回答导出（把"页面上看一眼"变成"能贴进汇报材料"）
//
// 为什么要做：这套助手的真实用途是给工厂管理者/评审看，导出 Markdown 后可以直接贴进
// 周报、评审材料、整改报告。只能截屏的话，用起来很别扭。
//
// 导出内容刻意包含**数据依据与置信度**——汇报材料里"这个数是怎么来的"比"这个数是多少"
// 更能让人信；只给结论不给出处，报告就废了。
// ============================================================================

public static class AssistantExporter
{
    /// <summary>把一次问答导出为 Markdown 文本。</summary>
    public static string BuildMarkdown(AssistantReply reply, IReadOnlyList<ChatTurn>? history = null,
        DateTime? at = null)
    {
        var sb = new StringBuilder();
        var when = at ?? DateTime.Now;

        sb.AppendLine("# MES 助手问答记录");
        sb.AppendLine();
        sb.AppendLine($"> 导出时间：{when:yyyy-MM-dd HH:mm}　|　回答来源：{reply.BackendName}"
                      + $"　|　识别意图：{reply.Intent}（匹配置信度 {reply.Confidence}%）");
        sb.AppendLine();

        sb.AppendLine("## 提问");
        sb.AppendLine();
        sb.AppendLine($"> {reply.Question}");
        sb.AppendLine();

        sb.AppendLine("## 结论");
        sb.AppendLine();
        sb.AppendLine($"**{reply.Headline}**");
        sb.AppendLine();

        if (reply.Body.Count > 0)
        {
            sb.AppendLine("## 展开说明");
            sb.AppendLine();
            foreach (var b in reply.Body) sb.AppendLine($"- {b}");
            sb.AppendLine();
        }

        if (reply.Facts.Count > 0)
        {
            sb.AppendLine("## 关键数据");
            sb.AppendLine();
            sb.AppendLine("| 项目 | 数值 | 说明 |");
            sb.AppendLine("|---|---|---|");
            foreach (var f in reply.Facts)
                sb.AppendLine($"| {Escape(f.Label)} | {Escape(f.Value)} | {Escape(f.Note)} |");
            sb.AppendLine();
        }

        if (reply.Evidence.Count > 0)
        {
            sb.AppendLine("## 数据依据");
            sb.AppendLine();
            foreach (var e in reply.Evidence) sb.AppendLine($"- {e}");
            sb.AppendLine();
        }

        if (reply.Actions.Count > 0)
        {
            sb.AppendLine("## 建议动作");
            sb.AppendLine();
            foreach (var a in reply.Actions)
                sb.AppendLine($"- {a.Label}{(a.Hint.Length > 0 ? $"　（{a.Hint}）" : "")}");
            sb.AppendLine();
        }

        if (history is { Count: > 0 })
        {
            sb.AppendLine("## 本次上文");
            sb.AppendLine();
            foreach (var t in history) sb.AppendLine($"- {t.Compact}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"由 MES 助手（{reply.BackendName}）生成；数据取自本地仓储实时计算，可复算。");
        return sb.ToString();
    }

    /// <summary>导出为纯文本（不想带 Markdown 标记时用）。</summary>
    public static string BuildPlainText(AssistantReply reply, IReadOnlyList<ChatTurn>? history = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MES 助手问答记录");
        sb.AppendLine($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}　来源：{reply.BackendName}　意图：{reply.Intent}（{reply.Confidence}%）");
        sb.AppendLine();
        sb.AppendLine($"提问：{reply.Question}");
        sb.AppendLine($"结论：{reply.Headline}");
        foreach (var b in reply.Body) sb.AppendLine($"  · {b}");
        if (reply.Facts.Count > 0)
        {
            sb.AppendLine("关键数据：");
            foreach (var f in reply.Facts) sb.AppendLine($"  · {f.Label}：{f.Value}{(f.Note.Length > 0 ? $"（{f.Note}）" : "")}");
        }
        if (reply.Evidence.Count > 0)
        {
            sb.AppendLine("数据依据：");
            foreach (var e in reply.Evidence) sb.AppendLine($"  · {e}");
        }
        if (reply.Actions.Count > 0)
        {
            sb.AppendLine("建议动作：");
            foreach (var a in reply.Actions) sb.AppendLine($"  · {a.Label}");
        }
        return sb.ToString();
    }

    /// <summary>写到文件。扩展名 .txt 自动用纯文本格式，其它一律 Markdown。</summary>
    public static (bool Ok, string Message) ExportToFile(AssistantReply reply, string path,
        IReadOnlyList<ChatTurn>? history = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return (false, "未指定导出路径。");
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var isTxt = string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);
            var content = isTxt ? BuildPlainText(reply, history) : BuildMarkdown(reply, history);
            File.WriteAllText(path, content, new UTF8Encoding(true));   // 带 BOM：Excel/记事本打开不乱码
            var size = new FileInfo(path).Length;
            return (true, $"已导出到 {path}（{size:N0} 字节，{(isTxt ? "纯文本" : "Markdown")}）");
        }
        catch (Exception ex)
        {
            return (false, "导出失败：" + ex.Message);
        }
    }

    /// <summary>默认文件名：助手问答_yyyyMMdd_HHmmss.md</summary>
    public static string DefaultFileName(DateTime? at = null)
        => $"助手问答_{(at ?? DateTime.Now):yyyyMMdd_HHmmss}.md";

    /// <summary>Markdown 表格里的竖线会破坏排版，转义掉</summary>
    private static string Escape(string s)
        => (s ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
