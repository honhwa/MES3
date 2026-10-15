using System.Net.Http;
using System.Text;
using System.Text.Json;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Models;

// ============================================================================
// 大模型 HTTP 通道（共用）
//
// 抽出来是因为有两类调用形态：问答（AssistantLlm）与结构化任务（LlmService）。
// 两者共用同一套配置、同一套错误分类、同一套降级策略——**不能各写一份**，
// 否则一处修了"超时识别"另一处没修，问题会以很难查的形式冒出来。
//
// 一个已知坑（2026-10-08 踩过）：.NET 中自定义 HttpMessageHandler 不支持同步 Send，
// 请求根本发不出去（实测 CallCount=0），必须用 SendAsync + GetAwaiter().GetResult()。
// ============================================================================

public static class LlmHttp
{
    private static readonly HttpClient Shared = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan   // 超时交给每次请求的 CancellationToken
    };

    /// <summary>发一次对话请求，返回模型正文（失败抛异常，由调用方统一降级）。</summary>
    public static string Send(AssistantLlmSettings cfg, string system, string user, HttpClient? http = null, int? maxTokens = null)
    {
        var url = cfg.BaseUrl.TrimEnd('/') + "/chat/completions";
        var payload = new
        {
            model = cfg.Model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = cfg.Temperature,
            max_tokens = maxTokens ?? cfg.MaxTokens,
            stream = false
        };

        // 不转义中文：体积更小、抓包日志可读（HTTP 头已声明 UTF-8）
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(cfg.TimeoutSeconds));
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.ResolveApiKey());

        using var resp = (http ?? Shared).SendAsync(req, cts.Token).GetAwaiter().GetResult();
        string body;
        using (var reader = new StreamReader(resp.Content.ReadAsStream(cts.Token), Encoding.UTF8))
            body = reader.ReadToEnd();

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}：{Shorten(ExtractApiError(body), 200)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices)
            || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var msg)
            || !msg.TryGetProperty("content", out var content))
            throw new InvalidOperationException("响应结构不符合 OpenAI 兼容格式（缺少 choices[0].message.content）");

        return content.GetString() ?? "";
    }

    /// <summary>连通性自测：发一条最小请求。</summary>
    public static (bool Ok, string Message) TestConnection(AssistantLlmSettings cfg, HttpClient? http = null)
    {
        if (!cfg.CanCall) return (false, "尚未启用或未填写 API Key。");
        try
        {
            var text = Send(cfg, "你是连通性测试端点。", "请只回复六个字：连接正常。", http);
            return string.IsNullOrWhiteSpace(text)
                ? (false, "服务有响应但内容为空。")
                : (true, $"连接正常，模型回显：{Shorten(text.Trim(), 80)}（模型 {cfg.Model}）");
        }
        catch (Exception ex)
        {
            return (false, ShortError(ex));
        }
    }

    /// <summary>异常归类成人话（界面与降级原因都用它）。</summary>
    public static string ShortError(Exception ex) => ex switch
    {
        TaskCanceledException or OperationCanceledException => "请求超时（可调大超时秒数或检查网络）",
        HttpRequestException hre => "网络不可达：" + Shorten(hre.Message, 160),
        _ => ex.GetType().Name + "：" + Shorten(ex.Message, 180)
    };

    public static string Shorten(string? s, int max)
    {
        var t = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static string ExtractApiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var m))
                return m.GetString() ?? body;
        }
        catch { /* 不是 JSON 就原样返回 */ }
        return body;
    }
}
