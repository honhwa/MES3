using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：数字孪生入口（阶段七）
//
// 页面：「数字孪生」一级分类下新增一页
//   ⛳ 数字孪生   三个孪生网页的入口卡片 + 打开方式切换
//
// 设计要点：
//   · 网页资产放在 WebAssets/digital-twin/*.html，随构建复制到输出目录
//     （见 SuperCapMES.WinForms.csproj 的 Content + CopyToOutputDirectory）
//   · **双打开方式**：默认新标签页（零依赖，永远可用）；
//     若项目引用了 Microsoft.Web.WebView2，则额外提供「内嵌展示」
//   · 降级不报错：WebView2 包或运行时缺失时，内嵌按钮自动禁用并说明原因，
//     而不是抛异常/崩溃
//   · 返回 MES3 的路径有两层：页面内「← 返回运营大屏」回大屏；
//     大屏 ↔ MES3 则由本入口页的「← 返回 MES3」按钮与导航栏承担
// ============================================================================

public partial class MainForm
{
    // 页面资产目录（相对 exe 运行目录，与 csproj 的输出规则对应）
    private const string TwinAssetDir = "WebAssets/digital-twin";

    // 四个孪生页面：文件名 / 展示名 / 一句话说明 / 主题色
    private static readonly (string File, string Title, string Desc, string Color, string Icon)[] TwinPages =
    {
        ("digital-twin-dashboard.html", "运营大屏",
            "生产 / 网络 / 设备 / 碳排放 四大类核心指标总览，支持模块折叠、时间范围筛选与自动轮播。", "#2f81f7", "📊"),
        ("cockpit.html", "经营驾驶舱",
            "管理层决策视图：健康度总评与自动结论、8 项核心 KPI、告警风险、产量/良率/碳排/OEE 趋势。", "#2f81f7", "🧭"),
        ("showcase.html", "展厅总控",
            "展厅大屏专用：导演脚本场景列表 + 自动巡演播放 + 逐幕旁白，支持上一幕/下一幕。", "#f5a524", "🖥️"),
        ("gov-submission.html", "政府申报版",
            "梯度培育对标、CMMM 能力域自评、指标达成度与统计口径、智能化场景量化收益。", "#7f77dd", "🏛️"),
        ("production-twin.html", "生产情况孪生",
            "产线架构等距视图 + 每台设备实时产量/良率/稼动指标，悬停设备查看当前在制品详情。", "#2ecc8f", "🏭"),
        ("device-twin.html", "设备数字孪生",
            "2.5D 等距厂房布局，标注各产线全部监控设备点位，点击查看实时运行参数与告警。", "#f5a524", "⚙️"),
        ("carbon-twin.html", "碳排放孪生",
            "产线碳排等距视图，悬停设备查看近一小时排放曲线，超阈值红色预警，全厂指标 3s 刷新。", "#2ecc8f", "🌱"),
        ("network-twin.html", "网络数字孪生",
            "核心 / 汇聚 / 接入 / 终端 四层网络拓扑，展示节点在线状态与链路连通性。", "#35c6d4", "🌐"),
        ("trace-query.html", "批次追溯查询",
            "客户审厂工具：输入批次号秒级输出正向工艺链路与检验记录，点击物料批号反查投放范围。", "#7f77dd", "🔍"),
        ("quality-twin.html", "质量孪生",
            "质量追溯主线分析：全厂/产线良率、SPC 控制图、缺陷帕累托、良率热力、CAPA 闭环，客户审厂与质量申报核心视图。", "#e36fa0", "🔬"),
        ("recipe-twin.html", "工艺/配方孪生",
            "工艺参数版本管控：版本对比、关键工序参数偏移带、工序能力 CPK 矩阵、版本切换对良率影响。", "#7f9bff", "🧪"),
        ("cost-twin.html", "成本孪生",
            "经营视角成本分析：单只成本与构成、良率损失成本、成本瀑布、峰谷电费，支撑央企降本增效汇报。", "#e0a32e", "💰"),
        ("warehouse-twin.html", "仓储孪生",
            "2.5D 立体库库位状态 / 物料批次 / AGV 调度 / 出入库流水 / 工单齐套看板，仓储数字化演示视图。", "#35c6d4", "📦"),
        ("integration-twin.html", "系统集成拓扑",
            "应用层集成视图：SAP / PLC / SCADA / QMS / WMS ↔ MES 数据中台 ↔ 孪生看板 / 政府申报 / AI 助手，协议与频率一目了然。", "#2f81f7", "🔗"),
        ("guard-demo.html", "过站防呆演示台",
            "交互式工位防呆演示：五大规则判定（跳站/参数超限/版本不符/质量挂起/物料齐套）→ 放行/拦截/返工分流，可现场一键触发。", "#f4544c", "🛡️"),
        ("3d-factory.html", "3D 工厂数据流",
            "厂区等距 3D 视图：应用系统 + 网络设备点位，叠加应用层数据流（协议/频率）与网络四层拓扑，悬停/点击看详情。", "#9b6bff", "🏭"),
    };

    // 打开方式
    private static readonly string[] TwinOpenModes = { "新标签页打开", "内嵌展示（MES3 窗体内）" };
    private int _twinOpenMode;             // 0=外链 1=内嵌
    private static bool? _webView2Available;// 反射探测结果缓存（探测一次即可）

    private Label _lblTwinPath = null!;    // 页面实际路径提示
    private Label _lblTwinMode = null!;    // 当前打开方式说明
    private Panel _twinHostPanel = null!;  // 内嵌模式的宿主容器

    /// <summary>「数字孪生」入口页</summary>
    private TabPage BuildTwinTab()
    {
        var page = new TabPage("数字孪生");
        page.Padding = new Padding(8);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            // 上：工具条 / 中：四张入口卡（自动撑高）/ 下：说明
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                 // 工具条
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));              // 间距
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));             // 卡片区
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));              // 间距
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                 // 说明区

        // ---------- ① 工具条：打开方式 + 返回 ----------
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true
        };
        bar.Controls.Add(new Label { Text = "打开方式：", AutoSize = true, Margin = new Padding(2, 7, 4, 0) });

        var cboMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190,
            Margin = new Padding(0, 3, 10, 0)
        };
        cboMode.Items.AddRange(TwinOpenModes);
        cboMode.SelectedIndex = _twinOpenMode;
        cboMode.SelectedIndexChanged += (_, _) => _twinOpenMode = cboMode.SelectedIndex;
        bar.Controls.Add(cboMode);

        bar.Controls.Add(TwinButton("📂 打开页面所在目录", (_, _) => OpenAssetFolder()));
        bar.Controls.Add(TwinButton("🔄 刷新页面状态", (_, _) =>
        {
            LoadTwinCards();
            _lblTwinMode.Text = BuildModeText();
            ToastTwin($"页面状态已刷新，{TwinPages.Length} 个孪生页面可用");
        }));
        bar.Controls.Add(TwinButton("← 返回 MES3", (_, _) =>
        {
            // 从数字孪生回到 MES3 的总览页（本分类之外的第一个分类）
            var first = _tabs.TabPages.OfType<TabPage>()
                .FirstOrDefault(t => t.Text != NavTwin);
            if (first is not null) _tabs.SelectedTab = first;
            ToastTwin("已返回 MES3 主界面");
        }, accent: Color.FromArgb(21, 128, 61)));

        root.Controls.Add(bar, 0, 0);

        // ---------- ② 三张入口卡 ----------
        var cardsHost = new Panel { Dock = DockStyle.Fill };
        _twinHostPanel = cardsHost;
        root.Controls.Add(cardsHost, 0, 2);

        // ---------- ③ 说明区 ----------
        var note = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 3
        };
        _lblTwinMode = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = TwinInk, Margin = new Padding(2) };
        _lblTwinPath = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = TwinGray, Margin = new Padding(2) };
        var tip = new Label
        {
            Dock = DockStyle.Fill, AutoSize = true, ForeColor = TwinGray, Margin = new Padding(2),
            Text = "说明：孪生页面为独立静态网页（零外部依赖），也可直接双击 HTML 文件用于投屏演示；"
                 + "修改页面后重新构建即可生效，无需改动本 C# 代码。"
        };
        note.Controls.Add(_lblTwinMode, 0, 0);
        note.Controls.Add(_lblTwinPath, 0, 1);
        note.Controls.Add(tip, 0, 2);
        root.Controls.Add(note, 0, 4);

        page.Controls.Add(root);

        page.Enter -= TwinTab_Enter;
        page.Enter += TwinTab_Enter;

        LoadTwinCards();
        _lblTwinMode.Text = BuildModeText();
        _lblTwinPath.Text = "页面目录：" + ResolveTwinDir();
        return page;
    }

    private void TwinTab_Enter(object? sender, EventArgs e)
    {
        LoadTwinCards();
        _lblTwinMode.Text = BuildModeText();
        _lblTwinPath.Text = "页面目录：" + ResolveTwinDir();
    }

    // ---------- 配色（与孪生页面深色主题呼应，但不改动主界面既有配色） ----------
    private static readonly Color TwinInk = Color.FromArgb(30, 41, 59);
    private static readonly Color TwinGray = Color.FromArgb(100, 116, 139);
    private static readonly Color TwinCard = Color.FromArgb(248, 250, 252);
    private static readonly Color TwinBorder = Color.FromArgb(226, 232, 240);

    /// <summary>重建四张入口卡</summary>
    private void LoadTwinCards()
    {
        if (_twinHostPanel is null) return;
        _twinHostPanel.SuspendLayout();
        _twinHostPanel.Controls.Clear();

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            // 容器宽度不足时自动换行到下一行；容器尺寸变化（窗体缩放/分辨率变化）时
            // FlowLayoutPanel 自动重算换行位置与排列顺序。纵向超出时由 AutoScroll 提供滚动。
            WrapContents = true, AutoScroll = true
        };

        foreach (var (file, title, desc, color, icon) in TwinPages)
        {
            var exists = File.Exists(Path.Combine(ResolveTwinDir(), file));
            flow.Controls.Add(BuildTwinCard(file, title, desc, color, icon, exists));
        }
        _twinHostPanel.Controls.Add(flow);
        _twinHostPanel.ResumeLayout();
    }

    /// <summary>单张入口卡（标题 + 说明 + 两个打开按钮）</summary>
    private Control BuildTwinCard(string file, string title, string desc, string color, string icon, bool exists)
    {
        var accent = ColorTranslator.FromHtml(color);

        var card = new Panel
        {
            Width = 430,
            Height = 250,
            Margin = new Padding(0, 0, 14, 0),
            BackColor = TwinCard,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14)
        };
        card.Paint += (_, e) =>
        {
            using var b = new SolidBrush(accent);
            e.Graphics.FillRectangle(b, 0, 0, 4, card.Height);      // 左侧主题色条
        };

        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill, AutoSize = true,
            Font = new Font("微软雅黑", 13F, FontStyle.Bold),
            ForeColor = TwinInk,
            Text = $"{icon}  {title}"
        };
        var subLabel = new Label
        {
            Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 2, 0, 6),
            ForeColor = exists ? TwinGray : Color.FromArgb(200, 120, 20),
            Text = exists ? "页面就绪 · " + file : $"⚠ 未找到页面文件：{file}"
        };
        var descLabel = new Label
        {
            Dock = DockStyle.Fill, AutoSize = false,
            ForeColor = TwinGray, Font = new Font("微软雅黑", 9F),
            Text = desc
        };
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 6, 0, 0)
        };
        btnRow.Controls.Add(TwinButton("↗ 新标签页打开", (_, _) => OpenTwinPage(file), accent: accent));
        btnRow.Controls.Add(TwinButton("⊞ 内嵌展示", (_, _) => OpenTwinEmbedded(file), accent: accent));

        inner.Controls.Add(titleLabel, 0, 0);
        inner.Controls.Add(subLabel, 0, 1);
        inner.Controls.Add(descLabel, 0, 2);
        inner.Controls.Add(btnRow, 0, 3);
        card.Controls.Add(inner);
        return card;
    }

    private static Button TwinButton(string text, EventHandler onClick, Color? accent = null)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent.HasValue ? Color.White : Color.White,
            ForeColor = accent ?? TwinInk,
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = accent ?? TwinBorder;
        b.FlatAppearance.BorderSize = 1;
        b.Click += onClick;
        return b;
    }

    // ---------- 打开方式 ----------

    /// <summary>新标签页 / 外部浏览器打开（默认方式，零依赖）</summary>
    private void OpenTwinPage(string file)
    {
        var path = Path.Combine(ResolveTwinDir(), file);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"未找到孪生页面文件：\n{path}\n\n请确认已重新构建项目。",
                "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            // UseShellExecute=true 交给系统默认浏览器，等效于"新标签页打开"
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            ToastTwin($"已在浏览器中打开：{file}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开页面失败：" + ex.Message, "打开失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// 内嵌到 MES3 窗体中显示。
    /// WebView2 包/运行时不可用时**降级为外链打开**并提示，不抛异常。
    /// </summary>
    private void OpenTwinEmbedded(string file)
    {
        var path = Path.Combine(ResolveTwinDir(), file);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"未找到孪生页面文件：\n{path}", "内嵌失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!WebView2Available())
        {
            var r = MessageBox.Show(this,
                "当前项目未启用 WebView2 内嵌组件，无法在窗体内嵌显示。\n\n"
                + "启用方法：\n  dotnet add package Microsoft.Web.WebView2\n\n"
                + "是否改为在新标签页打开？",
                "内嵌不可用", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) OpenTwinPage(file);
            return;
        }

        try
        {
            // 通过反射创建 WebView2 并 dock 到宿主面板，避免硬依赖（包缺失时不会编译失败）
            var type = Type.GetType("Microsoft.Web.WebView2.WinForms.WebView2, Microsoft.Web.WebView2.WinForms")
                       ?? Type.GetType("Microsoft.Web.WebView2.WinForms.WebView2, Microsoft.Web.WebView2.Core");
            if (type is null) { OpenTwinPage(file); return; }

            _twinHostPanel.SuspendLayout();
            _twinHostPanel.Controls.Clear();

            var view = Activator.CreateInstance(type) as Control;
            if (view is null) { _twinHostPanel.ResumeLayout(); OpenTwinPage(file); return; }

            view.Dock = DockStyle.Fill;
            _twinHostPanel.Controls.Add(view);

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, Height = 34, BackColor = Color.White
            };
            top.Controls.Add(TwinButton("← 返回入口列表", (_, _) => LoadTwinCards()));
            top.Controls.Add(TwinButton("↗ 改用浏览器打开", (_, _) => OpenTwinPage(file)));
            _twinHostPanel.Controls.Add(top);
            _twinHostPanel.ResumeLayout();

            // 异步设置地址（CoreWebView2.EnsureCoreWebView2Async）
            var coreProp = view.GetType().GetProperty("CoreWebView2");
            var ensure = view.GetType().GetMethod("EnsureCoreWebView2Async");
            ensure?.Invoke(view, null);

            var navTask = Task.Run(async () =>
            {
                for (var i = 0; i < 40 && coreProp?.GetValue(view) is null; i++)
                    await Task.Delay(100);
                var core = coreProp?.GetValue(view);
                if (core is null || coreProp is null) return;
                coreProp.PropertyType.GetProperty("Source")
                    ?.SetValue(core, new Uri(path));
            });
            _ = navTask;

            ToastTwin("已内嵌显示：" + file);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "内嵌显示失败，已改为浏览器方式。\n\n" + ex.Message,
                "内嵌失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            OpenTwinPage(file);
        }
    }

    /// <summary>打开页面所在目录（便于直接双击 HTML 独立演示）</summary>
    private void OpenAssetFolder()
    {
        var dir = ResolveTwinDir();
        try
        {
            if (!Directory.Exists(dir))
            {
                MessageBox.Show(this, "页面目录不存在：" + dir, "目录未找到",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开目录失败：" + ex.Message, "打开失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------- 辅助 ----------

    /// <summary>
    /// 解析孪生页面目录：以 exe 所在目录为基准（与 csproj 的输出规则一致）。
    /// 源码目录运行（如 dotnet run）时也可通过 WebAssets 相对路径兜底找到。
    /// </summary>
    internal static string ResolveTwinDir()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "WebAssets", "digital-twin"),           // 正常：输出目录
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..",
                       "WebAssets", "digital-twin")),                      // 兜底：源码目录
        };
        foreach (var c in candidates)
            if (Directory.Exists(c)) return c;
        return candidates[0];
    }

    /// <summary>探测 WebView2 是否可用（结果缓存，只探测一次）</summary>
    private static bool WebView2Available()
    {
        if (_webView2Available.HasValue) return _webView2Available.Value;

        try
        {
            // 检查程序集能否加载（包已引用的情况）
            var asm = Assembly.Load("Microsoft.Web.WebView2.Core");
            _webView2Available = asm != null;
        }
        catch
        {
            // 再检查系统是否有 Edge WebView2 Runtime
            try
            {
                var t = Type.GetTypeFromProgID("EdgeWebView2.Application");
                _webView2Available = false;   // ProgID 不可靠，仍以包引用为准 → 未启用
            }
            catch { _webView2Available = false; }
        }
        return _webView2Available.Value;
    }

    private string BuildModeText()
    {
        var mode = _twinOpenMode == 0 ? "新标签页打开（浏览器）" : "内嵌展示（MES3 窗体）";
        var embed = WebView2Available()
            ? "内嵌组件已就绪"
            : "内嵌组件未启用（当前为外链模式；如需内嵌执行 dotnet add package Microsoft.Web.WebView2）";
        return $"当前打开方式：{mode}　|　{embed}　|　共 {TwinPages.Length} 个孪生页面";
    }

    /// <summary>页内轻提示（非模态，不阻塞操作）</summary>
    private void ToastTwin(string msg)
    {
        var tip = new Label
        {
            Text = msg,
            AutoSize = true,
            BackColor = Color.FromArgb(47, 129, 247),
            ForeColor = Color.White,
            Padding = new Padding(8, 4, 8, 4),
            Font = new Font("微软雅黑", 9F)
        };
        var host = _twinHostPanel;
        if (host is null) return;

        tip.Location = new Point(
            Math.Max(4, (host.ClientSize.Width - tip.Width) / 2), 8);
        tip.BringToFront();
        host.Controls.Add(tip);

        var timer = new System.Windows.Forms.Timer { Interval = 1800 };
        timer.Tick += (_, _) => { host.Controls.Remove(tip); tip.Dispose(); timer.Stop(); timer.Dispose(); };
        timer.Start();
    }
}
