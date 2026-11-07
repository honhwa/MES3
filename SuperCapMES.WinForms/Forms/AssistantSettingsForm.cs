using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// AI 助手后端设置：配置云端大模型（OpenAI 兼容接口，DeepSeek 可直接用）。
///
/// 设计上有三条刻意的克制：
///   ① **默认关闭**——不打开开关，系统行为与纯本地完全一致，不会有人"被"把数据发出去；
///   ② **Key 不落项目目录**——存到 <c>%APPDATA%\SuperCapMES\assistant-llm.json</c>，也支持用环境变量
///      <c>DEEPSEEK_API_KEY</c> 覆盖（环境变量优先，更适合交付给现场）；
///   ③ **发送内容写清楚**——界面直接告诉用户"发出去的是什么、不发什么"，而不是藏在代码里。
/// 控件宣告与静态配置见 AssistantSettingsForm.Designer.cs。
/// </summary>
public partial class AssistantSettingsForm : Form
{
    /// <summary>用户点「保存」后的配置（点取消时与传入值相同）</summary>
    public AssistantLlmSettings Settings { get; private set; }

    /// <summary>用户是否点了保存</summary>
    public bool Saved { get; private set; }

    public AssistantSettingsForm(AssistantLlmSettings current)
    {
        Settings = (current ?? new AssistantLlmSettings()).Clone();
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：处理依赖传入参数 / 静态类别值 / 两层属性的内容，
    /// 以及原本建构式在 UI 建立后呼叫的 LoadFrom(Settings)。
    /// 其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime()
    {
        // 原 BuildUi：PlaceholderText / Items 取静态类别值，Designer 无法解析，原样保留。
        _txtBaseUrl.PlaceholderText = AssistantLlmSettings.DefaultBaseUrl;
        _cboModel.Items.AddRange(AssistantLlmSettings.ModelCandidates);

        // 原 BuildUi：FlatAppearance 子属性（两层属性设定），Designer 白名单未含，原样保留。
        _btnTest.FlatAppearance.BorderColor = Color.FromArgb(180, 200, 222);

        LoadFrom(Settings);
    }

    private void ChkEnabled_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateEnabledState();
    }

    private void CboModel_SizeChanged(object? sender, EventArgs e)
    {
        if (_cboModel.Width > _cboModel.DropDownWidth) _cboModel.DropDownWidth = _cboModel.Width;
    }

    private void ChkShowKey_CheckedChanged(object? sender, EventArgs e)
    {
        _txtApiKey.UseSystemPasswordChar = !_chkShowKey.Checked;
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        Confirm();
    }

    // ================= 读写 =================

    private void LoadFrom(AssistantLlmSettings s)
    {
        _chkEnabled.Checked = s.Enabled;
        _txtBaseUrl.Text = s.BaseUrl;
        _cboModel.Text = s.Model;
        _txtApiKey.Text = s.ApiKey;
        _numTimeout.Value = Math.Clamp(s.TimeoutSeconds, 5, 120);
        _numTemperature.Value = Math.Clamp((decimal)s.Temperature, 0M, 1M);

        var env = Environment.GetEnvironmentVariable(AssistantLlmSettings.ApiKeyEnvVar);
        _lblEnvHint.Text = string.IsNullOrWhiteSpace(env)
            ? $"提示：也可以不在这里填 Key，改用环境变量 {AssistantLlmSettings.ApiKeyEnvVar}（更适合交付现场，Key 不落盘）。"
            : $"已检测到环境变量 {AssistantLlmSettings.ApiKeyEnvVar}（已设置），它将覆盖这里填写的 Key。";

        UpdateEnabledState();
    }

    private void ApplyToSettings()
    {
        Settings.Enabled = _chkEnabled.Checked;
        Settings.BaseUrl = _txtBaseUrl.Text.Trim().Length == 0 ? AssistantLlmSettings.DefaultBaseUrl : _txtBaseUrl.Text.Trim();
        Settings.Model = _cboModel.Text.Trim().Length == 0 ? AssistantLlmSettings.DefaultModel : _cboModel.Text.Trim();
        Settings.ApiKey = _txtApiKey.Text.Trim();
        Settings.TimeoutSeconds = (int)_numTimeout.Value;
        Settings.Temperature = (double)_numTemperature.Value;
    }

    /// <summary>未启用时把下面的输入项灰掉，避免"以为填了就生效"。</summary>
    private void UpdateEnabledState()
    {
        var on = _chkEnabled.Checked;
        _txtBaseUrl.Enabled = on;
        _cboModel.Enabled = on;
        _txtApiKey.Enabled = on;
        _chkShowKey.Enabled = on;
        _numTimeout.Enabled = on;
        _numTemperature.Enabled = on;
        _btnTest.Enabled = on;
        if (!on) _lblTestResult.Text = "";
    }

    private void Confirm()
    {
        ApplyToSettings();

        if (Settings.Enabled && Settings.ResolveApiKey().Length == 0)
        {
            var r = MessageBox.Show(
                "已勾选启用，但既没有填 API Key，也没有设置环境变量 " + AssistantLlmSettings.ApiKeyEnvVar + "。\n\n"
                + "这样仍会退回本地规则引擎作答。要现在保存吗？",
                "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        if (Settings.Enabled && !Uri.TryCreate(Settings.BaseUrl, UriKind.Absolute, out _))
        {
            MessageBox.Show("服务地址不是合法的 URL，请检查（例如 " + AssistantLlmSettings.DefaultBaseUrl + "）。",
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtBaseUrl.Focus();
            return;
        }

        Saved = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ================= 连通性自测 =================

    private async void BtnTest_Click(object? sender, EventArgs e)
    {
        ApplyToSettings();
        if (!Settings.CanCall)
        {
            _lblTestResult.ForeColor = Color.FromArgb(168, 106, 0);
            _lblTestResult.Text = "请先勾选启用并填写 API Key（或设置环境变量）。";
            return;
        }

        _btnTest.Enabled = false;
        _lblTestResult.ForeColor = Color.FromArgb(110, 125, 145);
        _lblTestResult.Text = "正在连接 " + Settings.BaseUrl + " …";

        var cfg = Settings.Clone();
        try
        {
            var (ok, msg) = await Task.Run(() => LlmAssistant.TestConnection(cfg));
            _lblTestResult.ForeColor = ok ? Color.FromArgb(26, 127, 90) : Color.FromArgb(176, 42, 42);
            _lblTestResult.Text = (ok ? "✓ " : "✗ ") + msg;
        }
        catch (Exception ex)
        {
            _lblTestResult.ForeColor = Color.FromArgb(176, 42, 42);
            _lblTestResult.Text = "✗ 测试失败：" + ex.Message;
        }
        finally
        {
            _btnTest.Enabled = _chkEnabled.Checked;
        }
    }
}
