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
/// </summary>
public class AssistantSettingsForm : Form
{
    private CheckBox _chkEnabled = null!;
    private TextBox _txtBaseUrl = null!;
    private ComboBox _cboModel = null!;
    private TextBox _txtApiKey = null!;
    private CheckBox _chkShowKey = null!;
    private NumericUpDown _numTimeout = null!;
    private NumericUpDown _numTemperature = null!;
    private Button _btnTest = null!;
    private Label _lblTestResult = null!;
    private Label _lblEnvHint = null!;

    /// <summary>用户点「保存」后的配置（点取消时与传入值相同）</summary>
    public AssistantLlmSettings Settings { get; private set; }

    /// <summary>用户是否点了保存</summary>
    public bool Saved { get; private set; }

    public AssistantSettingsForm(AssistantLlmSettings current)
    {
        Settings = (current ?? new AssistantLlmSettings()).Clone();

        Text = "AI 助手 · 后端设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 428);
        Font = new Font("微软雅黑", 9F);
        BuildUi();
        LoadFrom(Settings);
    }

    // ================= 界面 =================

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 10,
            Padding = new Padding(16, 14, 16, 10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        for (int i = 0; i < 9; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // --- 第 0 行：启用开关 ---
        _chkEnabled = new CheckBox
        {
            Text = "启用云端大模型（关闭时全部在本地计算，数据不出本机）",
            AutoSize = true,
            Margin = new Padding(0, 7, 0, 0),
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9.5F, FontStyle.Bold)
        };
        _chkEnabled.CheckedChanged += (_, _) => UpdateEnabledState();
        root.Controls.Add(_chkEnabled, 0, 0);
        root.SetColumnSpan(_chkEnabled, 3);

        // --- 第 1 行：说明 ---
        var lblWhat = new Label
        {
            Text = "数字由本地仓储汇总算出，云端只负责把结果组织成人话；返回后逐个数字核对，出现未授权数字即整段丢弃。",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
            ForeColor = Color.FromArgb(110, 125, 145),
            Font = new Font("微软雅黑", 8.5F)
        };
        root.Controls.Add(lblWhat, 0, 1);
        root.SetColumnSpan(lblWhat, 3);

        // --- 第 2 行：服务地址 ---
        root.Controls.Add(MakeLabel("服务地址"), 0, 2);
        _txtBaseUrl = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 0),
            PlaceholderText = AssistantLlmSettings.DefaultBaseUrl
        };
        root.Controls.Add(_txtBaseUrl, 1, 2);
        root.SetColumnSpan(_txtBaseUrl, 2);

        // --- 第 3 行：模型 ---
        root.Controls.Add(MakeLabel("模型"), 0, 3);
        _cboModel = new ComboBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 0),
            DropDownStyle = ComboBoxStyle.DropDown,
            DropDownWidth = 280
        };
        _cboModel.Items.AddRange(AssistantLlmSettings.ModelCandidates);
        // ComboBox 用 Dock=Fill，构造期宽度还是默认值——下拉宽度必须等布局完成后再对齐输入框，
        // 否则手填长模型名时下拉比输入框还窄，展开也看不全（项目里踩过同类"构造期不物化"的坑）。
        _cboModel.SizeChanged += (_, _) =>
        {
            if (_cboModel.Width > _cboModel.DropDownWidth) _cboModel.DropDownWidth = _cboModel.Width;
        };
        root.Controls.Add(_cboModel, 1, 3);
        var lblModelHint = new Label
        {
            Text = "可手填",
            AutoSize = true,
            Margin = new Padding(0, 9, 0, 0),
            ForeColor = Color.FromArgb(140, 155, 175),
            Font = new Font("微软雅黑", 8.5F)
        };
        root.Controls.Add(lblModelHint, 2, 3);

        // --- 第 4 行：API Key ---
        root.Controls.Add(MakeLabel("API Key"), 0, 4);
        _txtApiKey = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 0),
            UseSystemPasswordChar = true,
            PlaceholderText = "sk-..."
        };
        root.Controls.Add(_txtApiKey, 1, 4);
        _chkShowKey = new CheckBox
        {
            Text = "显示",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            ForeColor = Color.FromArgb(110, 125, 145),
            Font = new Font("微软雅黑", 8.5F)
        };
        _chkShowKey.CheckedChanged += (_, _) => _txtApiKey.UseSystemPasswordChar = !_chkShowKey.Checked;
        root.Controls.Add(_chkShowKey, 2, 4);

        // --- 第 5 行：超时 ---
        root.Controls.Add(MakeLabel("超时"), 0, 5);
        _numTimeout = new NumericUpDown
        {
            Width = 82,
            Minimum = 5,
            Maximum = 120,
            DecimalPlaces = 0,
            Margin = new Padding(0, 4, 8, 0),
            TextAlign = HorizontalAlignment.Center
        };
        var timeoutRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
        timeoutRow.Controls.Add(_numTimeout);
        timeoutRow.Controls.Add(new Label { Text = "秒（超时自动退回本地规则引擎作答）", AutoSize = true, Margin = new Padding(0, 9, 0, 0), ForeColor = Color.FromArgb(110, 125, 145), Font = new Font("微软雅黑", 8.5F) });
        root.Controls.Add(timeoutRow, 1, 5);
        root.SetColumnSpan(timeoutRow, 2);

        // --- 第 6 行：温度 ---
        root.Controls.Add(MakeLabel("采样温度"), 0, 6);
        _numTemperature = new NumericUpDown
        {
            Width = 82,
            Minimum = 0,
            Maximum = 1,
            DecimalPlaces = 1,
            Increment = 0.1M,
            Margin = new Padding(0, 4, 8, 0),
            TextAlign = HorizontalAlignment.Center
        };
        var tempRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
        tempRow.Controls.Add(_numTemperature);
        tempRow.Controls.Add(new Label { Text = "（工厂问答建议 ≤0.3，越低越稳、越少发挥）", AutoSize = true, Margin = new Padding(0, 9, 0, 0), ForeColor = Color.FromArgb(110, 125, 145), Font = new Font("微软雅黑", 8.5F) });
        root.Controls.Add(tempRow, 1, 6);
        root.SetColumnSpan(tempRow, 2);

        // --- 第 7 行：环境变量提示 ---
        _lblEnvHint = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            ForeColor = Color.FromArgb(26, 127, 90),
            Font = new Font("微软雅黑", 8.5F)
        };
        root.Controls.Add(_lblEnvHint, 0, 7);
        root.SetColumnSpan(_lblEnvHint, 3);

        // --- 第 8 行：测试连通性 ---
        var testRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
        _btnTest = new Button
        {
            Text = "测试连通性",
            Size = new Size(112, 28),
            Margin = new Padding(0, 3, 10, 0),
            BackColor = Color.FromArgb(226, 236, 247),
            ForeColor = Color.FromArgb(15, 76, 129),
            FlatStyle = FlatStyle.Flat
        };
        _btnTest.FlatAppearance.BorderColor = Color.FromArgb(180, 200, 222);
        _btnTest.Click += BtnTest_Click;
        _lblTestResult = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(0, 8, 0, 0),
            ForeColor = Color.FromArgb(110, 125, 145),
            Font = new Font("微软雅黑", 8.5F)
        };
        testRow.Controls.Add(_btnTest);
        testRow.Controls.Add(_lblTestResult);
        root.Controls.Add(testRow, 0, 8);
        root.SetColumnSpan(testRow, 3);

        // --- 第 9 行：保存 / 取消 ---
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 6, 0, 0),
            WrapContents = false
        };
        var btnCancel = new Button { Text = "取消", Size = new Size(88, 30), DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        var btnOk = new Button
        {
            Text = "保存",
            Size = new Size(88, 30),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnOk.Click += (_, _) => Confirm();
        btnRow.Controls.Add(btnCancel);
        btnRow.Controls.Add(btnOk);
        root.Controls.Add(btnRow, 0, 9);
        root.SetColumnSpan(btnRow, 3);

        Controls.Add(root);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 6, 6, 0),
        ForeColor = Color.FromArgb(30, 41, 59)
    };

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
