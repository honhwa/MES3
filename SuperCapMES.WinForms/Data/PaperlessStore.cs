using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

/// <summary>
/// 现场无纸化：文件 JSON 仓储。
/// 模板与提交分别落盘——模板可改、可删，历史提交内嵌模板快照，互不影响。
/// 重启不丢；无需数据库，契合 MES3 原型定位。
/// </summary>
public static class PaperlessStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义，便于人读与审计
    };

    public static string DataRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "data", "paperless");

    private static string TemplatesDir { get; } = Path.Combine(DataRoot, "templates");
    private static string SubmissionsDir { get; } = Path.Combine(DataRoot, "submissions");

    static PaperlessStore()
    {
        Directory.CreateDirectory(TemplatesDir);
        Directory.CreateDirectory(SubmissionsDir);
    }

    // ---------------- 模板 ----------------

    public static void SaveTemplate(ChecklistTemplate tpl)
    {
        File.WriteAllText(Path.Combine(TemplatesDir, tpl.Id + ".json"),
            JsonSerializer.Serialize(tpl, Json), new UTF8Encoding(false));
    }

    public static ChecklistTemplate? LoadTemplate(string id)
    {
        var p = Path.Combine(TemplatesDir, id + ".json");
        return File.Exists(p) ? JsonSerializer.Deserialize<ChecklistTemplate>(File.ReadAllText(p), Json) : null;
    }

    public static List<ChecklistTemplate> LoadAllTemplates()
    {
        return Directory.Exists(TemplatesDir)
            ? Directory.GetFiles(TemplatesDir, "*.json")
                .Select(f => JsonSerializer.Deserialize<ChecklistTemplate>(File.ReadAllText(f), Json))
                .Where(t => t is not null).Select(t => t!).ToList()
            : new List<ChecklistTemplate>();
    }

    public static void DeleteTemplate(string id)
    {
        var p = Path.Combine(TemplatesDir, id + ".json");
        if (File.Exists(p)) File.Delete(p);
        // 注意：不删 submissions —— 历史提交自带模板快照，模板删除不影响历史还原（需求硬要求）
    }

    // ---------------- 提交 ----------------

    public static void SaveSubmission(ChecklistSubmission s)
    {
        File.WriteAllText(Path.Combine(SubmissionsDir, s.Id + ".json"),
            JsonSerializer.Serialize(s, Json), new UTF8Encoding(false));
    }

    public static ChecklistSubmission? LoadSubmission(string id)
    {
        var p = Path.Combine(SubmissionsDir, id + ".json");
        return File.Exists(p) ? JsonSerializer.Deserialize<ChecklistSubmission>(File.ReadAllText(p), Json) : null;
    }

    public static List<ChecklistSubmission> LoadAllSubmissions()
    {
        if (!Directory.Exists(SubmissionsDir)) return new List<ChecklistSubmission>();
        return Directory.GetFiles(SubmissionsDir, "*.json")
            .Select(f => JsonSerializer.Deserialize<ChecklistSubmission>(File.ReadAllText(f), Json))
            .Where(s => s is not null).Select(s => s!).ToList();
    }

    // ---------------- 查询 ----------------

    public sealed class QueryFilter
    {
        public string TemplateId { get; set; } = "";       // 空 = 全部模板
        public string Submitter { get; set; } = "";        // 包含匹配
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string Keyword { get; set; } = "";           // 匹配 模板名/提交人/设备/班次/抬头值/表体上下文/填写值
    }

    public static List<ChecklistSubmission> Query(QueryFilter f)
    {
        var all = LoadAllSubmissions();
        var kw = (f.Keyword ?? "").Trim();

        return all.Where(s =>
        {
            if (!string.IsNullOrEmpty(f.TemplateId) && s.TemplateId != f.TemplateId) return false;
            if (!string.IsNullOrEmpty(f.Submitter) &&
                !(s.Submitter ?? "").Contains(f.Submitter, StringComparison.OrdinalIgnoreCase)) return false;
            if (f.From is not null && s.SubmittedAt.Date < f.From.Value.Date) return false;
            if (f.To is not null && s.SubmittedAt.Date > f.To.Value.Date) return false;
            if (kw.Length > 0)
            {
                // 关键字覆盖：模板名/提交人/设备/班次 + 抬头字段值 + 表体上下文 + 填写值
                // 上下文（点检项目、点检内容等）必须能查到，否则现场按「静电防护」找不到记录
                var context = s.TemplateSnapshot is null
                    ? ""
                    : string.Join(" ", s.TemplateSnapshot.Rows.SelectMany(r => r.Cells));
                var header = string.Join(" ", (s.HeaderValues ?? new()).Values);
                var hay = (s.TemplateName + " " + s.Submitter + " " + s.Device + " " + s.Shift + " " +
                           header + " " + context + " " +
                           string.Join(" ", s.Values.Values)).ToLowerInvariant();
                if (!hay.Contains(kw.ToLowerInvariant())) return false;
            }
            return true;
        })
        .OrderByDescending(s => s.SubmittedAt)
        .ToList();
    }
}
