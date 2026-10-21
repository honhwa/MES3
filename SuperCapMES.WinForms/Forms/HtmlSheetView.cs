using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 内嵌 HTML 渲染宿主：把 SheetHtmlRenderer 产出的自包含 HTML 显示出来，并支持回读单元格里填的值。
///
/// 三个 WebView2 的坑都在这里一次性处理掉（都是实测踩出来的）：
/// ① 非当前 TabPage 里的控件没有有效句柄，构造期初始化必失败（报 CoreWebView2 is uninitialized）
///    → 改成第一次可见时初始化；
/// ② EnsureCoreWebView2Async 的 continuation 可能落到线程池，而 CoreWebView2 只允许 UI 线程访问
///    → 收尾统一用 BeginInvoke 切回（不依赖 SynchronizationContext，它在自动化宿主里可能为 null）；
/// ③ 不要为了"共享环境"预先 await CoreWebView2Environment.CreateAsync —— 那一步本身会丢上下文，
///    反而让后续 EnsureCoreWebView2Async 在线程池上跑（实测踩过）。用默认环境即可。
///
/// 降级：运行时缺失/初始化失败只在页面上显示可读说明，绝不抛异常打断页面构造。
/// </summary>
public sealed class HtmlSheetView : UserControl
{
    private readonly WebView2? _view;
    private readonly Label _lblFallback;
    private string _pendingHtml = "";
    private bool _domReady;
    private bool _initStarted;

    /// <summary>核心是否就绪</summary>
    public bool IsReady { get; private set; }
    /// <summary>降级/失败原因</summary>
    public string? LastError { get; private set; }
    /// <summary>CoreWebView2 是否已真正可用（排查用：IsReady 为真但取不到内容时看这个）</summary>
    public bool HasCore => _view?.CoreWebView2 is not null;

    public HtmlSheetView()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        _lblFallback = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Visible = false,
            Text = "正在初始化 WebView2…",
        };

        try
        {
            _view = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_view);
            // 构造期不初始化：非当前 TabPage 的控件没有有效句柄
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ShowFallback("⚠ 无法创建 WebView2 组件：" + ex.Message);
        }

        Controls.Add(_lblFallback);
        _lblFallback.BringToFront();
    }

    /// <summary>首次可见时自动初始化（TabPage 场景下唯一可靠的时机）。</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _ = EnsureInitializedAsync();
    }

    /// <summary>确保已初始化（幂等；页面与自测都可主动调用）。</summary>
    public Task EnsureInitializedAsync()
    {
        if (_initStarted) return Task.CompletedTask;
        _initStarted = true;

        // 统一从消息泵回调里发起（等价于"用户点了这一页"），避免在任意 await 链中间调 WebView2 初始化
        if (IsHandleCreated) BeginInvoke(new Action(() => { _ = RunInitAsync(); }));
        else _ = RunInitAsync();
        return Task.CompletedTask;
    }

    private async Task RunInitAsync()
    {
        try
        {
            await _view!.EnsureCoreWebView2Async();
            // 收尾必须回 UI 线程；用 BeginInvoke 而不是 SynchronizationContext（后者在自测宿主里可能为 null）
            if (IsHandleCreated) BeginInvoke(new Action(FinishInit));
            else FinishInit();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            if (IsHandleCreated)
                BeginInvoke(new Action(() => ShowFallback(
                    "⚠ WebView2 运行时不可用，无法在原样视图中显示。\n" +
                    "请安装 Microsoft Edge WebView2 Runtime 后重试。\n\n" + ex.Message)));
            else ShowFallback("⚠ WebView2 初始化失败：" + ex.Message);
        }
    }

    /// <summary>核心就绪后的收尾（UI 线程）。</summary>
    private void FinishInit()
    {
        try
        {
            var s = _view!.CoreWebView2.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            // 导航完成才代表"真的画出来了"：截图要等这个信号，否则拿到空白
            _view.CoreWebView2.NavigationCompleted += (_, __) => _domReady = true;
            IsReady = true;
            LastError = null;
            _lblFallback.Visible = false;
            if (_pendingHtml.Length > 0) _ = NavigatePendingAsync();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ShowFallback("⚠ WebView2 初始化收尾失败：" + ex.Message);
        }
    }

    private void ShowFallback(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => ShowFallback(text))); return; }
        _lblFallback.Text = text;
        _lblFallback.Visible = true;
        _lblFallback.BringToFront();
    }

    /// <summary>显示 HTML（多次调用以最后一次为准）；未初始化时先存着，就绪后自动导航。</summary>
    public void ShowHtml(string html)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => ShowHtml(html))); return; }
        _pendingHtml = html ?? "";
        _domReady = false;
        if (!IsReady) return;
        _ = NavigatePendingAsync();
    }

    private Task NavigatePendingAsync()
    {
        if (_view?.CoreWebView2 is null || !IsReady) return Task.CompletedTask;
        try
        {
            _view.NavigateToString(_pendingHtml);   // 单页几百 KB 以内都够用
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ShowFallback("⚠ 渲染失败：" + ex.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>在页面里执行一段脚本（自测驱动填值、将来也可做联动）。</summary>
    public async Task<string?> RunScriptAsync(string script)
    {
        if (_view?.CoreWebView2 is null || !IsReady) return null;
        try { return await _view.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>回读所有可填单元格的值（JSON 文本，key = r{row}c{col}）。</summary>
    public async Task<string?> CollectValuesJsonAsync()
    {
        if (_view?.CoreWebView2 is null || !IsReady) return null;
        try
        {
            var r = await _view.CoreWebView2.ExecuteScriptAsync("window.__collect ? window.__collect() : '{}'");
            return Unwrap(r);
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>截取当前渲染结果为 PNG（会先等 DOM 加载完成）。</summary>
    public async Task<byte[]?> CapturePngAsync(int waitRenderMs = 8000)
    {
        if (_view?.CoreWebView2 is null || !IsReady) { LastError = "WebView2 未就绪"; return null; }
        var end = DateTime.Now.AddMilliseconds(waitRenderMs);
        while (!_domReady && DateTime.Now < end) await Task.Delay(60);
        try
        {
            using var ms = new MemoryStream();
            await _view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex) { LastError = "截图失败：" + ex.Message; return null; }
    }

    /// <summary>等待初始化就绪（自测用）。</summary>
    public async Task<bool> WaitReadyAsync(int timeoutMs = 15000)
    {
        if (!_initStarted) _ = EnsureInitializedAsync();
        var end = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < end)
        {
            if (IsReady) return true;
            if (LastError is not null) return false;
            await Task.Delay(80);
        }
        return IsReady;
    }

    private static string Unwrap(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "{}";
        var s = raw.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            try
            {
                var inner = System.Text.Json.JsonSerializer.Deserialize<string>(s);
                return string.IsNullOrEmpty(inner) ? "{}" : inner!;
            }
            catch { /* 落到原样返回 */ }
        }
        return s;
    }
}
