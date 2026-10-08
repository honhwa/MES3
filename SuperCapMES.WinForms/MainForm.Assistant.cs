using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分）：AI 助手页。
///
/// 定位：**问答式入口**，不是新的业务功能。用户用一句话问工厂现状，助手实时从 MesDatabase
/// 汇总取数回答，并把「结论 / 关键数据 / 数据依据 / 建议动作」分开呈现——数据依据必须可见，
/// 否则生产现场的问答不可追责。
///
/// 界面分三块：
///   · 中间：对话区（RichTextBox，按段渲染，可关动画）；
///   · 右侧：可解释面板（意图 / 置信度 / 识别编号 / 数据依据 / 后端标识与边界声明）；
///   · 底部：建议动作按钮（点一下按手册口径跨层跳转）+ 底部快捷提问。
///
/// 后端由 <see cref="AssistantEngine"/> 门面托管，当前挂"本地规则引擎"，将来换大模型只改门面一行。
/// </summary>
public partial class MainForm
{
    // ================= 视觉常量（与全站配色一致） =================
    private static readonly Color AsstInk = Color.FromArgb(30, 41, 59);
    private static readonly Color AsstBlue = Color.FromArgb(15, 76, 129);
    private static readonly Color AsstAccent = Color.FromArgb(30, 95, 168);
    private static readonly Color AsstGray = Color.FromArgb(110, 125, 145);
    private static readonly Color AsstGreen = Color.FromArgb(26, 127, 90);
    private static readonly Color AsstAmber = Color.FromArgb(168, 106, 0);
    private static readonly Color AsstRed = Color.FromArgb(176, 42, 42);
    private static readonly Color AsstUserBack = Color.FromArgb(226, 236, 247);
    private static readonly Color AsstPanelBack = Color.FromArgb(247, 250, 253);

    /// <summary>
    /// 渲染逐段动画开关（默认开；UI 自测时置 false 以便同步断言）。
    /// 用字段而不是属性：MainForm 派生自 Form，公开属性会触发 WinForms 设计器序列化分析器（WFO1000）。
    /// </summary>
    internal bool AnimateAssistantChat = true;

    // ================= 页面构建 =================

    /// <summary>构建「AI 助手」页签。</summary>
    private TabPage BuildAssistantTab()
    {
        var page = new TabPage("AI 助手");
        _assistantTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 120, ShowAlways = true };

        var center = BuildAssistantCenter();   // 对话区（Fill）
        var right = BuildAssistantInspector(); // 可解释面板（Right）
        var bottom = BuildAssistantActionBar();// 建议动作（Bottom）
        var top = BuildAssistantInputBar();    // 标题 + 输入 + 快捷提问（Top）

        // 停靠顺序：Fill 必须先加入（下标最小 → 最后停靠 → 占满剩余）；
        // 后加入的先停靠，因此 Top 最后加入才能拿到整行宽度。
        page.Controls.Add(center);
        page.Controls.Add(right);
        page.Controls.Add(bottom);
        page.Controls.Add(top);
        return page;
    }

    /// <summary>顶部：标题说明 + 输入行 + 快捷提问（点一下即问）。</summary>
    private Panel BuildAssistantInputBar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 128,
            BackColor = Color.FromArgb(240, 246, 252),
            Padding = new Padding(16, 8, 16, 6)
        };

        var lblTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "AI 助手 —— 用一句话问工厂现在的情况；回答里的每个数都能追到出处，也可以一键跳到对应页面处理",
            Font = new Font("微软雅黑", 10F, FontStyle.Bold),
            ForeColor = AsstBlue
        };

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };

        _txtAssistantInput = new TextBox
        {
            Width = 560,
            Font = new Font("微软雅黑", 10F),
            Margin = new Padding(0, 6, 8, 0),
            PlaceholderText = "例如：今天有什么要处理的 / 有哪些异常还没闭环 / 张伟能不能开卷绕机 / SC-A260812-L1-00001 这只电芯怎么回事"
        };
        _txtAssistantInput.KeyDown += AssistantInput_KeyDown;

        _btnAssistantSend = MakeToolbarButton("提问", BtnAssistantSend);
        _btnAssistantClear = MakeToolbarButton("清空对话", BtnAssistantClear);
        _btnAssistantClear.Width = 112;
        _btnAssistantSettings = MakeToolbarButton("后端设置", BtnAssistantSettings);
        _btnAssistantSettings.Width = 112;
        _btnAssistantExport = MakeToolbarButton("导出", BtnAssistantExport);
        _btnAssistantExport.Width = 76;

        _lblAssistantStatus = new Label
        {
            AutoSize = true,
            Margin = new Padding(8, 13, 0, 0),
            ForeColor = AsstGray,
            Font = new Font("微软雅黑", 8.5F)
        };

        row.Controls.Add(_txtAssistantInput);
        row.Controls.Add(_btnAssistantSend);
        row.Controls.Add(_btnAssistantClear);
        row.Controls.Add(_btnAssistantExport);
        row.Controls.Add(_btnAssistantSettings);
        row.Controls.Add(_lblAssistantStatus);

        var quick = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        quick.Controls.Add(new Label
        {
            Text = "常用提问：",
            AutoSize = true,
            Margin = new Padding(0, 9, 4, 0),
            ForeColor = AsstGray,
            Font = new Font("微软雅黑", 9F)
        });

        _flpAssistantQuick = quick;
        RefreshAssistantQuick(null);

        // Dock=Top 按"后加入的先停靠"排列，这里倒序加入才得到「标题 → 输入行 → 快捷行」
        panel.Controls.Add(quick);
        panel.Controls.Add(row);
        panel.Controls.Add(lblTitle);
        return panel;
    }

    /// <summary>中部：对话区。</summary>
    private Control BuildAssistantCenter()
    {
        _rtbAssistantChat = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("微软雅黑", 9.5F),
            WordWrap = true,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Padding = new Padding(14, 10, 14, 10)
        };
        return _rtbAssistantChat;
    }

    /// <summary>右侧：可解释面板（意图 / 置信度 / 识别编号 / 数据依据 / 后端与边界）。</summary>
    private Control BuildAssistantInspector()
    {
        var host = new Panel
        {
            Dock = DockStyle.Right,
            Width = 336,
            BackColor = AsstPanelBack,
            Padding = new Padding(1, 0, 0, 0)
        };

        var caption = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "  系统理解与数据依据",
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(226, 236, 247),
            ForeColor = Color.FromArgb(30, 70, 120),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };

        _rtbAssistantInspector = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = AsstPanelBack,
            Font = new Font("微软雅黑", 9F),
            WordWrap = true,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Padding = new Padding(10, 8, 8, 8)
        };

        host.Controls.Add(_rtbAssistantInspector);
        host.Controls.Add(caption);
        return host;
    }

    /// <summary>底部：建议动作按钮条（按回答动态生成）。</summary>
    private Panel BuildAssistantActionBar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 96,
            BackColor = Color.FromArgb(246, 249, 253),
            Padding = new Padding(16, 4, 16, 6)
        };

        var caption = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "建议动作（点一下跳到对应页面；跳转只切页面，不会替你执行操作）",
            ForeColor = AsstGray,
            Font = new Font("微软雅黑", 8.5F)
        };

        _flpAssistantActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = Color.FromArgb(246, 249, 253)
        };

        var placeholder = new Label
        {
            Text = "（先问一句，这里会出现可直接跳转的处理入口）",
            AutoSize = true,
            Margin = new Padding(2, 8, 0, 0),
            ForeColor = Color.FromArgb(150, 165, 185),
            Font = new Font("微软雅黑", 9F)
        };
        _flpAssistantActions.Controls.Add(placeholder);

        panel.Controls.Add(_flpAssistantActions);
        panel.Controls.Add(caption);
        return panel;
    }

    // ================= 初始化与提问 =================

    /// <summary>助手页兜底初始化（幂等）：写欢迎语 + 渲染初始可解释面板。</summary>
    private void EnsureAssistantReady()
    {
        if (_assistantLoaded) return;
        if (_rtbAssistantChat is null || _rtbAssistantChat.IsDisposed) return;
        _assistantLoaded = true;

        RenderAssistantWelcome();
        RefreshAssistantBackendLabel();
    }

    /// <summary>刷新顶部后端标签：走云端时把"数据发往哪里"也写出来，让用户随时看得见。</summary>
    private void RefreshAssistantBackendLabel()
    {
        if (_lblAssistantStatus is null) return;
        var remote = AssistantEngine.Remote;
        _lblAssistantStatus.Text = remote is not null
            ? $"后端：{AssistantEngine.BackendName}（{remote.Endpoint}）"
            : $"后端：{AssistantEngine.BackendName}";
    }

    /// <summary>回车即提问。</summary>
    private void AssistantInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        AskAssistantFromInput();
    }

    /// <summary>
    /// 刷新快捷提问区（P0-2 / P0-3）：
    ///   · 有回答 → 按**本轮意图**给出下一步该问什么（问完缺料就该推"要采购哪些"）
    ///   · 未识别 → 给"你是不是想问…"的候选，把路接上而不是直接说结束
    ///   · 无回答（初始）→ 显示固定的入门示例
    /// </summary>
    private void RefreshAssistantQuick(AssistantReply? reply)
    {
        if (_flpAssistantQuick is null) return;
        _flpAssistantQuick.SuspendLayout();
        try
        {
            _flpAssistantQuick.Controls.Clear();

            var label = new Label
            {
                Text = reply is null ? "常用提问：" : "接着可以问：",
                AutoSize = true,
                Margin = new Padding(0, 9, 4, 0),
                ForeColor = AsstGray,
                Font = new Font("微软雅黑", 9F)
            };
            _flpAssistantQuick.Controls.Add(label);

            var items = reply is null
                ? AssistantEngine.SampleQuestions.Take(6).ToList()
                : reply.Intent == AssistantIntent.Unknown || reply.IsFallback && reply.Confidence <= 0
                    ? AssistantFollowUp.GuideFromUnknown(reply)
                    : AssistantFollowUp.Suggest(reply);

            foreach (var text in items.Take(6))
            {
                var q = text;   // 闭包捕获：每条用独立副本
                var link = new LinkLabel
                {
                    Text = q,
                    AutoSize = true,
                    Margin = new Padding(0, 8, 14, 0),
                    LinkColor = AsstAccent,
                    Font = new Font("微软雅黑", 9F)
                };
                link.LinkClicked += (_, _) =>
                {
                    _txtAssistantInput.Text = q;
                    AskAssistantFromInput();
                };
                _flpAssistantQuick.Controls.Add(link);
            }
        }
        finally
        {
            _flpAssistantQuick.ResumeLayout(true);
        }
    }

    /// <summary>点「导出」：把本条问答导出成 Markdown/纯文本，供贴进汇报材料。</summary>
    private void BtnAssistantExport(object? sender, EventArgs e)
    {
        var reply = _lastAssistantReply;
        if (reply is null)
        {
            Warn("还没有问答内容可导出，先问一句。");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "导出问答记录",
            Filter = "Markdown 文件 (*.md)|*.md|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = AssistantExporter.DefaultFileName(),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = AssistantExporter.ExportToFile(reply, dlg.FileName, _assistantHistory.Turns);
        if (ok) MessageBox.Show(msg, "已导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else MessageBox.Show(msg, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>点「提问」。</summary>
    private void BtnAssistantSend(object? sender, EventArgs e) => AskAssistantFromInput();

    /// <summary>点「清空对话」：清掉对话与建议动作，保留后端与面板说明。</summary>
    private void BtnAssistantClear(object? sender, EventArgs e)
    {
        if (_assistantRenderTimer is not null) _assistantRenderTimer.Stop();
        _assistantRenderQueue.Clear();
        _lastAssistantReply = null;
        _assistantRound = 0;
        _assistantLoaded = false;
        _assistantHistory.Clear();          // 上文要一起清，否则清完对话再问还会"接住"上一轮
        RefreshAssistantQuick(null);
        _rtbAssistantChat?.Clear();
        _rtbAssistantInspector?.Clear();
        ResetAssistantActionButtons();
        EnsureAssistantReady();
        RefreshAssistantBackendLabel();
        RenderAssistantInspectorHint();
        _txtAssistantInput.Clear();
        _txtAssistantInput.Focus();
    }

    /// <summary>取输入框内容提问（供按钮、回车、快捷提问三个入口共用）。</summary>
    private void AskAssistantFromInput()
    {
        var q = _txtAssistantInput.Text.Trim();
        if (q.Length == 0)
        {
            Warn("请先输入问题。也可以点上方「常用提问」里的示例。");
            _txtAssistantInput.Focus();
            return;
        }
        _txtAssistantInput.Clear();

        // 走云端时是秒级网络调用：放到后台线程，避免把界面冻住（本地规则引擎则同步直出，无延迟）
        if (AssistantEngine.IsRemote)
        {
            _ = AskAssistantRemoteAsync(q);
            return;
        }

        AskAssistant(q);
        _txtAssistantInput.Focus();
    }

    /// <summary>云端后端专用：后台线程提问，期间禁用输入区并提示"正在问云端"。</summary>
    private async Task AskAssistantRemoteAsync(string question)
    {
        EnsureAssistantReady();
        SetAssistantBusy(true, "正在问云端大模型…");
        try
        {
            var reply = await Task.Run(() => AssistantEngine.Ask(question));
            RunOnUi(() => RenderAssistantTurn(reply));
        }
        catch (Exception ex)
        {
            // 门面本身已有兜底；这里再兜一层，保证界面绝不因网络问题崩掉
            RunOnUi(() =>
            {
                AppendAssistantSystemNote("提问失败：" + ex.Message);
                RefreshAssistantBackendLabel();
            });
        }
        finally
        {
            RunOnUi(() =>
            {
                SetAssistantBusy(false, null);
                _txtAssistantInput.Focus();
            });
        }
    }

    /// <summary>
    /// 把界面更新切回 UI 线程。
    /// await 之后的续体依赖当时的 SynchronizationContext——标准宿主（Application.Run）下它一定存在，
    /// 续体自然回到 UI 线程；但无消息循环的宿主（自测、被嵌进别的消息泵）里它可能是空的，
    /// 续体就会落在后台线程上，直接改控件会抛跨线程异常、且异常被 fire-and-forget 吞掉，
    /// 表现为"提问后一直卡在'正在问云端'"。这里统一兜一层，任何宿主下都稳。
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    private void SetAssistantBusy(bool busy, string? statusText)
    {
        if (_btnAssistantSend is not null) _btnAssistantSend.Enabled = !busy;
        if (_btnAssistantClear is not null) _btnAssistantClear.Enabled = !busy;
        if (_btnAssistantSettings is not null) _btnAssistantSettings.Enabled = !busy;
        if (_txtAssistantInput is not null) _txtAssistantInput.Enabled = !busy;
        if (statusText is not null && _lblAssistantStatus is not null) _lblAssistantStatus.Text = statusText;
    }

    /// <summary>核心提问入口（同步，供自测与本地后端使用）：算回答 → 写对话区 → 刷新面板与动作。</summary>
    internal AssistantReply AskAssistant(string question)
    {
        EnsureAssistantReady();
        var reply = AssistantEngine.Ask(question, _assistantHistory);
        RenderAssistantTurn(reply);
        return reply;
    }

    /// <summary>把一次回答渲染到界面（同步路径与异步路径共用）。</summary>
    private void RenderAssistantTurn(AssistantReply reply)
    {
        _lastAssistantReply = reply;
        _assistantRound++;

        AppendAssistantUserBubble(reply.Question);
        AppendAssistantBlocks(reply);
        RenderAssistantInspector(reply);
        UpdateAssistantActionButtons(reply);

        _lblAssistantStatus.Text = $"后端：{reply.BackendName}｜第 {_assistantRound} 轮｜意图 {reply.Intent}（{reply.ConfidenceText}）";

        // 记入上文，并按本轮意图刷新快捷提问（P0-1 / P0-2）
        _assistantHistory.Add(reply);
        RefreshAssistantQuick(reply);
    }

    /// <summary>打开后端设置：配置云端大模型（默认关闭），保存后立即切换后端并刷新界面说明。</summary>
    private void BtnAssistantSettings(object? sender, EventArgs e)
    {
        var current = AssistantLlmSettingsStore.Load();
        using var dlg = new AssistantSettingsForm(current);
        if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.Saved) return;

        var saved = AssistantLlmSettingsStore.Save(dlg.Settings);
        AssistantEngine.Backend = AssistantBackendFactory.CreateFromSettings(dlg.Settings);
        RefreshAssistantBackendLabel();
        RenderAssistantInspectorHint();

        var where = AssistantEngine.IsRemote
            ? $"已切到云端大模型：{AssistantEngine.BackendName}（{AssistantEngine.Remote!.Endpoint}）。数字仍全部取自本地仓储，返回前逐个核对。"
            : "当前仍在本地模式：未启用云端，或未填写 API Key / 未设置环境变量。全部计算在本机完成，数据不出门。";

        AppendAssistantSystemNote(where + (saved ? "" : "（注意：配置未能写入磁盘，本次运行有效）"));
    }

    // ================= 对话区渲染 =================

    private void RenderAssistantWelcome()
    {
        var rtb = _rtbAssistantChat;
        rtb.Clear();
        AsstWrite(rtb, "AI 助手\n", AsstFont(13F, true), AsstBlue);
        AsstWrite(rtb, "这里是问答入口，不替你做业务操作。你可以直接问：\n", AsstFont(9.5F), AsstInk);
        AsstWrite(rtb, "  · 现状类：「今天产量多少」「现在有多少不合格品被保留了」「设备都什么状态」\n", AsstFont(9F), Color.FromArgb(60, 78, 100));
        AsstWrite(rtb, "  · 编号类：「SC-A260812-L1-00001 这只电芯怎么回事」「WO-20260812-001 什么状态」\n", AsstFont(9F), Color.FromArgb(60, 78, 100));
        AsstWrite(rtb, "  · 人员类：「张伟能不能开卷绕机」「谁的证快到期了」\n", AsstFont(9F), Color.FromArgb(60, 78, 100));
        AsstWrite(rtb, "  · 操作类：「不合格品要去哪登记」「怎么导出追溯记录」\n", AsstFont(9F), Color.FromArgb(60, 78, 100));
        AsstWrite(rtb, "\n每条回答都会附「数据依据」，写明这个数是从哪儿算出来的。\n", AsstFont(8.5F), AsstGray);
        AsstWrite(rtb, "助手只汇总现有数据、不给质量判定结论，也不改动任何数据。\n\n", AsstFont(8.5F), AsstGray);
        AsstWrite(rtb, AsstRule(), AsstFont(8.5F), Color.FromArgb(214, 222, 232));

        RenderAssistantInspectorHint();
    }

    private static string AsstRule() => new string('─', 44) + "\n\n";

    /// <summary>用户发言：右缩进 + 浅蓝底色，做出"气泡"的视觉区分。</summary>
    private void AppendAssistantUserBubble(string question)
    {
        var rtb = _rtbAssistantChat;
        AsstWrite(rtb, "\n", AsstFont(9.5F), AsstInk);
        AsstWrite(rtb, "你：" + question + "\n", AsstFont(9.5F, true), Color.FromArgb(20, 60, 110), indent: 150, back: AsstUserBack);
    }

    /// <summary>把回答拆成若干渲染步骤：开动画时分段逐条出现，关动画时一次画完。</summary>
    private void AppendAssistantBlocks(AssistantReply r)
    {
        var blocks = BuildAssistantBlocks(r);

        if (!AnimateAssistantChat)
        {
            foreach (var b in blocks) b();
            ScrollAssistantToEnd();
            return;
        }

        _assistantRenderQueue.Clear();
        foreach (var b in blocks) _assistantRenderQueue.Enqueue(b);
        EnsureAssistantRenderTimer();
        _assistantRenderTimer!.Start();
    }

    private List<Action> BuildAssistantBlocks(AssistantReply r)
    {
        var blocks = new List<Action>();
        var rtb = _rtbAssistantChat;

        blocks.Add(() =>
        {
            AsstWrite(rtb, "助手", AsstFont(9F, true), AsstAccent);
            AsstWrite(rtb, "  " + r.Headline + "\n", AsstFont(10F, true), AsstHeadlineColor(r));
        });

        foreach (var b in r.Body)
        {
            var line = b;
            blocks.Add(() => AsstWrite(rtb, "· " + line + "\n", AsstFont(9F), AsstInk));
        }

        if (r.Facts.Count > 0)
        {
            blocks.Add(() => AsstWrite(rtb, "关键数据\n", AsstFont(9F, true), AsstBlue));
            foreach (var f in r.Facts)
            {
                var fact = f;
                blocks.Add(() =>
                {
                    AsstWrite(rtb, "  · " + fact.Label + "：" + fact.Value, AsstFont(9F), AsstInk);
                    if (!string.IsNullOrWhiteSpace(fact.Note))
                        AsstWrite(rtb, "（" + fact.Note + "）", AsstFont(8.5F), AsstGray);
                    AsstWrite(rtb, "\n", AsstFont(9F), AsstInk);
                });
            }
        }

        if (r.Evidence.Count > 0)
        {
            blocks.Add(() => AsstWrite(rtb, "数据依据\n", AsstFont(9F, true), AsstGray));
            foreach (var ev in r.Evidence)
            {
                var line = ev;
                blocks.Add(() => AsstWrite(rtb, "  · " + line + "\n", AsstFont(8.5F), Color.FromArgb(140, 155, 175)));
            }
        }

        if (r.Actions.Count > 0)
        {
            blocks.Add(() => AsstWrite(rtb, "建议动作：" + string.Join("；", r.Actions.Select(a => a.Label)) + "\n", AsstFont(9F), AsstGreen));
        }

        blocks.Add(() => AsstWrite(rtb, AsstRule(), AsstFont(8.5F), Color.FromArgb(214, 222, 232)));
        return blocks;
    }

    /// <summary>结论按语义上色：疑似问题偏暖色，正常/信息类用主色。</summary>
    private static Color AsstHeadlineColor(AssistantReply r)
    {
        if (r.IsFallback) return AsstAmber;
        if (r.Headline.Contains("不可以", StringComparison.Ordinal)) return AsstRed;
        if (r.Headline.Contains("没查到", StringComparison.Ordinal) || r.Headline.Contains("没查到", StringComparison.Ordinal)) return AsstAmber;
        if (r.Headline.Contains("超期", StringComparison.Ordinal) || r.Headline.Contains("高危", StringComparison.Ordinal)) return AsstRed;
        return AsstBlue;
    }

    private void EnsureAssistantRenderTimer()
    {
        if (_assistantRenderTimer is not null) return;
        _assistantRenderTimer = new System.Windows.Forms.Timer { Interval = 45 };
        _assistantRenderTimer.Tick += (_, _) =>
        {
            if (_assistantRenderQueue.Count == 0)
            {
                _assistantRenderTimer!.Stop();
                ScrollAssistantToEnd();
                return;
            }
            _assistantRenderQueue.Dequeue().Invoke();
            ScrollAssistantToEnd();
        };
    }

    /// <summary>把未播完的渲染步骤一次画完（UI 自测与"看结果"时用）。</summary>
    internal void DrainAssistantRender()
    {
        _assistantRenderTimer?.Stop();
        while (_assistantRenderQueue.Count > 0) _assistantRenderQueue.Dequeue().Invoke();
        ScrollAssistantToEnd();
    }

    private void ScrollAssistantToEnd()
    {
        if (_rtbAssistantChat is null || _rtbAssistantChat.IsDisposed) return;
        _rtbAssistantChat.SelectionStart = _rtbAssistantChat.TextLength;
        _rtbAssistantChat.SelectionLength = 0;
        _rtbAssistantChat.ScrollToCaret();
    }

    /// <summary>追加一段带格式文本（对话区 / 可解释面板共用；调用方自己给控件）。</summary>
    private static void AsstWrite(RichTextBox rtb, string text, Font font, Color color, int indent = 0, Color? back = null)
    {
        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionLength = 0;
        rtb.SelectionFont = font;
        rtb.SelectionColor = color;
        rtb.SelectionBackColor = back ?? rtb.BackColor;
        rtb.SelectionIndent = indent;
        rtb.AppendText(text);
    }

    private static Font AsstFont(float size, bool bold = false) =>
        new("微软雅黑", size, bold ? FontStyle.Bold : FontStyle.Regular);

    // ================= 可解释面板 =================

    /// <summary>未提问时的面板说明（讲清"这套东西是什么、边界在哪"）。</summary>
    private void RenderAssistantInspectorHint()
    {
        var rtb = _rtbAssistantInspector;
        if (rtb is null) return;
        rtb.Clear();
        AsstWrite(rtb, "这一栏是干什么的\n", AsstFont(9F, true), AsstGray);
        AsstWrite(rtb, "  回答生成后，这里会显示系统对问题的理解（意图、置信度、识别到的编号），"
                       + "以及这条回答的数据出处。目的是：任何一句话都能反查到是哪几个数算出来的。\n\n", AsstFont(8.5F), Color.FromArgb(120, 135, 155));

        AsstWrite(rtb, "当前回答由谁产生\n", AsstFont(9F, true), AsstGray);
        AsstWrite(rtb, "  " + AssistantEngine.BackendName + "\n", AsstFont(9F, true), AsstAccent);
        AsstWrite(rtb, "  " + AssistantEngine.BackendDescribe + "\n\n", AsstFont(8.5F), Color.FromArgb(120, 135, 155));

        AsstWrite(rtb, "边界声明\n", AsstFont(9F, true), AsstGray);
        AsstWrite(rtb, "  · 只做汇总与呈现，不参与合格/不合格判定；\n", AsstFont(8.5F), Color.FromArgb(150, 110, 20));
        AsstWrite(rtb, "  · 只读查询，问答不修改任何数据；\n", AsstFont(8.5F), Color.FromArgb(150, 110, 20));
        AsstWrite(rtb, "  · 跳转只切页面，不会替用户执行操作。\n", AsstFont(8.5F), Color.FromArgb(150, 110, 20));
    }

    /// <summary>渲染某次回答的可解释信息。</summary>
    private void RenderAssistantInspector(AssistantReply r)
    {
        var rtb = _rtbAssistantInspector;
        if (rtb is null) return;
        rtb.Clear();

        var ent = AssistantEntityParser.Parse(r.Question);
        var entLines = ent.Describe();

        AsstWrite(rtb, "系统理解\n", AsstFont(9F, true), AsstGray);
        AsstWrite(rtb, "  意图：" + r.Intent + "\n", AsstFont(9.5F, true), AsstAccent);
        AsstWrite(rtb, "  " + AssistantIntent.Describe(r.Intent) + "\n", AsstFont(8.5F), Color.FromArgb(120, 135, 155));
        AsstWrite(rtb, "  置信度：" + r.Confidence + "%（" + r.ConfidenceText + "）\n", AsstFont(9.5F, true), r.Confidence >= 80 ? AsstGreen : r.Confidence >= 55 ? AsstAmber : AsstGray);
        AsstWrite(rtb, "  " + r.ConfidenceExplain + "\n", AsstFont(8.5F), Color.FromArgb(120, 135, 155));

        AsstWrite(rtb, "  识别到的编号：", AsstFont(9F, true), AsstGray);
        AsstWrite(rtb, entLines.Count == 0 ? "（提问里没有业务编号）\n" : string.Join("、", entLines) + "\n", AsstFont(8.5F), entLines.Count == 0 ? Color.FromArgb(150, 165, 185) : AsstInk);

        AsstWrite(rtb, "\n数据依据\n", AsstFont(9F, true), AsstGray);
        if (r.Evidence.Count == 0)
            AsstWrite(rtb, "  （本次没有查询业务数据）\n", AsstFont(8.5F), Color.FromArgb(150, 165, 185));
        foreach (var ev in r.Evidence)
            AsstWrite(rtb, "  · " + ev + "\n", AsstFont(8.5F), Color.FromArgb(110, 128, 150));

        AsstWrite(rtb, "\n关键数据（" + r.Facts.Count + " 项）\n", AsstFont(9F, true), AsstGray);
        foreach (var f in r.Facts)
        {
            var color = f.Level switch
            {
                FactLevel.Warn => AsstRed,
                FactLevel.Watch => AsstAmber,
                FactLevel.Normal => AsstGreen,
                _ => AsstInk
            };
            AsstWrite(rtb, "  · " + f.Label + "：" + f.Value, AsstFont(8.5F), AsstInk);
            AsstWrite(rtb, "[" + f.Level + "]\n", AsstFont(8F), color);
        }

        AsstWrite(rtb, "\n本轮回答由\n  " + r.BackendName + " 生成\n", AsstFont(8.5F), Color.FromArgb(150, 165, 185));
    }

    // ================= 建议动作 =================

    private void ResetAssistantActionButtons()
    {
        if (_flpAssistantActions is null) return;
        _flpAssistantActions.Controls.Clear();
        _flpAssistantActions.Controls.Add(new Label
        {
            Text = "（先问一句，这里会出现可直接跳转的处理入口）",
            AutoSize = true,
            Margin = new Padding(2, 8, 0, 0),
            ForeColor = Color.FromArgb(150, 165, 185),
            Font = new Font("微软雅黑", 9F)
        });
    }

    private void UpdateAssistantActionButtons(AssistantReply r)
    {
        if (_flpAssistantActions is null) return;
        _flpAssistantActions.Controls.Clear();

        if (r.Actions.Count == 0)
        {
            ResetAssistantActionButtons();
            return;
        }

        foreach (var a in r.Actions)
            _flpAssistantActions.Controls.Add(MakeAssistantActionButton(a));
    }

    private Button MakeAssistantActionButton(AssistantAction action)
    {
        var b = new Button
        {
            Text = action.Label,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(4, 4, 4, 2),
            BackColor = Color.FromArgb(226, 236, 247),
            ForeColor = AsstBlue,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F)
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(180, 200, 222);
        b.Click += (_, _) => AssistantAction_Click(action);

        var tip = $"去：{action.WhereText}";
        if (!string.IsNullOrWhiteSpace(action.Hint)) tip += "\n说明：" + action.Hint;
        _assistantTip.SetToolTip(b, tip);
        b.Tag = action;
        return b;
    }

    /// <summary>点建议动作：按手册口径跨层跳转（复用 ActivatePage，保证定位一致）。</summary>
    private void AssistantAction_Click(AssistantAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetPage))
        {
            Warn("这个建议动作没有配置目标页面。");
            return;
        }

        if (string.Equals(action.TargetPage, "AI 助手", StringComparison.Ordinal))
        {
            AppendAssistantSystemNote("「" + action.Label + "」就在当前页面，无需跳转。");
            return;
        }

        var page = FindPage(action.TargetPage);
        if (page is null)
        {
            Warn($"未找到页面「{action.TargetPage}」（助手建议动作配置有误）。");
            return;
        }

        ActivatePage(page);
        var step = string.IsNullOrWhiteSpace(action.TargetButton) ? "按页面提示操作" : $"点「{action.TargetButton}」";
        var sub = string.IsNullOrWhiteSpace(action.TargetSubPage) ? "" : $" › {action.TargetSubPage}";
        _lblAssistantStatus.Text = $"已跳到「{action.TargetPage}{sub}」，{step}。";
        AppendAssistantSystemNote($"已跳转到「{action.TargetPage}{sub}」，{step}。");
    }

    /// <summary>在对话区插入一条系统提示（跳转结果等），不做气泡配色。</summary>
    private void AppendAssistantSystemNote(string text)
    {
        if (_rtbAssistantChat is null || _rtbAssistantChat.IsDisposed) return;
        AsstWrite(_rtbAssistantChat, "（系统）" + text + "\n\n", AsstFont(8.5F), AsstGray);
        ScrollAssistantToEnd();
    }
}
