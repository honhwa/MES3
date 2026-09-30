using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// SPC 规格限版本编辑对话框：收集「上限 USL（可留空=无上限）/ 下限 LSL（可留空=无下限）/
/// 生效时间（日期 + 时分）/ 备注 / 操作人」，并实时预览规格文本。风格与 <see cref="SpcCloseForm"/> 一致。
/// 校验：至少一侧规格有效；双边时 LSL &lt; USL；（可选）生效时间须晚于给定下限。
/// 点击「保存」且校验通过后 <see cref="Form.DialogResult"/> = OK，收集值通过属性返回。
/// </summary>
public class SpcVersionForm : Form
{
    private readonly string _itemName;
    private readonly int _nextVersion;
    private readonly bool _isEdit;
    private readonly DateTime? _lowerBound;

    private TextBox _txtUsl = null!;
    private TextBox _txtLsl = null!;
    private DateTimePicker _dtDate = null!;
    private DateTimePicker _dtTime = null!;
    private TextBox _txtRemark = null!;
    private TextBox _txtOperator = null!;
    private Label _lblPreview = null!;

    /// <summary>规格上限（留空 = double.NaN 表示无上限）</summary>
    public double Usl { get; private set; } = double.NaN;

    /// <summary>规格下限（留空 = double.NaN 表示无下限）</summary>
    public double Lsl { get; private set; } = double.NaN;

    /// <summary>生效时间（日期 + 时分）</summary>
    public DateTime EffectiveFrom { get; private set; }

    /// <summary>备注</summary>
    public string RemarkText { get; private set; } = "";

    /// <summary>操作人（默认 admin）</summary>
    public string OperatorName { get; private set; } = "admin";

    /// <param name="itemName">监控项名称（标题用）</param>
    /// <param name="nextVersion">本次拟新增 / 编辑的版本号（仅用于标题展示）</param>
    /// <param name="usl">初始上限（NaN = 无上限 / 留空）</param>
    /// <param name="lsl">初始下限（NaN = 无下限 / 留空）</param>
    /// <param name="effectiveFrom">初始生效时间</param>
    /// <param name="remark">初始备注</param>
    /// <param name="operatorName">初始操作人</param>
    /// <param name="lowerBound">生效时间下限（须严格晚于该时刻；null = 无约束）</param>
    /// <param name="isEdit">true = 修改待生效版本；false = 新建版本</param>
    public SpcVersionForm(
        string itemName,
        int nextVersion,
        double usl,
        double lsl,
        DateTime effectiveFrom,
        string remark,
        string operatorName,
        DateTime? lowerBound = null,
        bool isEdit = false)
    {
        _itemName = itemName;
        _nextVersion = nextVersion;
        _isEdit = isEdit;
        _lowerBound = lowerBound;

        Text = isEdit ? $"修改待生效版本 V{nextVersion}" : $"新建规格版本 V{nextVersion}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 366);
        Font = new Font("微软雅黑", 9F);

        BuildUi(usl, lsl, effectiveFrom, remark, operatorName);
        UpdatePreview();
    }

    private void BuildUi(double usl, double lsl, DateTime effectiveFrom, string remark, string operatorName)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(16, 16, 16, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 监控项
        grid.Controls.Add(MakeLabel("监控项"), 0, 0);
        grid.Controls.Add(new Label
        {
            Text = _itemName,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        }, 1, 0);

        // 上限
        grid.Controls.Add(MakeLabel("上限 USL"), 0, 1);
        _txtUsl = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = double.IsNaN(usl) ? "" : usl.ToString("0.###", CultureInfo.InvariantCulture),
            BackColor = Color.White,
            Margin = new Padding(3, 5, 3, 5)
        };
        _txtUsl.TextChanged += (_, _) => UpdatePreview();
        grid.Controls.Add(_txtUsl, 1, 1);

        // 下限
        grid.Controls.Add(MakeLabel("下限 LSL"), 0, 2);
        _txtLsl = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = double.IsNaN(lsl) ? "" : lsl.ToString("0.###", CultureInfo.InvariantCulture),
            BackColor = Color.White,
            Margin = new Padding(3, 5, 3, 5)
        };
        _txtLsl.TextChanged += (_, _) => UpdatePreview();
        grid.Controls.Add(_txtLsl, 1, 2);

        // 生效时间（日期）
        grid.Controls.Add(MakeLabel("生效时间"), 0, 3);
        var timeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        _dtDate = new DateTimePicker
        {
            Width = 150,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Margin = new Padding(0, 4, 4, 0)
        };
        if (effectiveFrom != default) _dtDate.Value = effectiveFrom.Date;
        _dtTime = new DateTimePicker
        {
            Width = 90,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        if (effectiveFrom != default) _dtTime.Value = effectiveFrom;
        timeRow.Controls.Add(_dtDate);
        timeRow.Controls.Add(_dtTime);
        grid.Controls.Add(timeRow, 1, 3);

        // 备注
        grid.Controls.Add(MakeLabel("备注"), 0, 4);
        _txtRemark = new TextBox { Dock = DockStyle.Fill, Text = remark ?? "", BackColor = Color.White, Margin = new Padding(3, 5, 3, 5) };
        grid.Controls.Add(_txtRemark, 1, 4);

        // 操作人
        grid.Controls.Add(MakeLabel("操作人"), 0, 5);
        _txtOperator = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName,
            BackColor = Color.White,
            Margin = new Padding(3, 5, 3, 5)
        };
        grid.Controls.Add(_txtOperator, 1, 5);

        // 规格预览
        grid.Controls.Add(MakeLabel("规格预览"), 0, 6);
        _lblPreview = new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 10F, FontStyle.Bold),
            Padding = new Padding(0, 6, 0, 0)
        };
        grid.Controls.Add(_lblPreview, 1, 6);

        // 底部按钮
        var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.WhiteSmoke };
        var btnOk = new Button { Text = "保存", Width = 110, Height = 32 };
        var btnCancel = new Button { Text = "取消", Width = 100, Height = 32 };
        btnOk.Location = new Point(btnRow.Width - 226, 10);
        btnCancel.Location = new Point(btnRow.Width - 114, 10);
        btnRow.Resize += (_, _) =>
        {
            btnOk.Location = new Point(btnRow.Width - 226, 10);
            btnCancel.Location = new Point(btnRow.Width - 114, 10);
        };
        btnOk.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);

        Controls.Add(grid);
        Controls.Add(btnRow);
    }

    private static Label MakeLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60)
        };

    private void UpdatePreview()
    {
        if (_lblPreview is null) return;
        var hasUsl = TryParse(_txtUsl.Text, out var usl);
        var hasLsl = TryParse(_txtLsl.Text, out var lsl);
        _lblPreview.Text = SpecText(hasUsl, usl, hasLsl, lsl);
    }

    private static string SpecText(bool hasUsl, double usl, bool hasLsl, double lsl)
    {
        if (hasUsl && hasLsl) return $"{lsl:F3} ~ {usl:F3}";
        if (hasUsl) return $"≤ {usl:F3}（单边上限）";
        if (hasLsl) return $"≥ {lsl:F3}（单边下限）";
        return "无规格限（至少需填写一侧）";
    }

    /// <summary>解析文本框为数值；空串 → false（视为无该侧规格）。</summary>
    private static bool TryParse(string? text, out double value)
    {
        value = double.NaN;
        var s = (text ?? "").Trim();
        if (s.Length == 0) return false;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
        {
            value = d;
            return true;
        }
        return false;
    }

    private void Save()
    {
        var uslText = _txtUsl.Text.Trim();
        var lslText = _txtLsl.Text.Trim();
        var hasUsl = TryParse(uslText, out var usl);
        var hasLsl = TryParse(lslText, out var lsl);

        if (uslText.Length > 0 && !hasUsl)
        {
            Warn("上限 USL 填写格式不正确（应为一个数字，或留空表示无上限）。");
            return;
        }
        if (lslText.Length > 0 && !hasLsl)
        {
            Warn("下限 LSL 填写格式不正确（应为一个数字，或留空表示无下限）。");
            return;
        }
        if (!hasUsl && !hasLsl)
        {
            Warn("至少需要填写一侧规格限（上限 USL 或下限 LSL）。");
            return;
        }
        if (hasUsl && hasLsl && lsl >= usl)
        {
            Warn("规格下限 LSL 必须小于规格上限 USL。");
            return;
        }

        var effective = _dtDate.Value.Date + _dtTime.Value.TimeOfDay;
        if (_lowerBound.HasValue && effective <= _lowerBound.Value)
        {
            Warn($"生效时间必须严格晚于上一版本（{_lowerBound.Value:yyyy-MM-dd HH:mm}）。");
            return;
        }

        var op = _txtOperator.Text.Trim();
        if (string.IsNullOrEmpty(op))
        {
            Warn("请填写操作人。");
            return;
        }

        // 加固：生效时间已过 → 保存后该版本将「立即生效」并参与判定，保存前二次确认（默认按钮「否」）。
        // 选「否」直接返回窗体、不写库（由调用方依据 DialogResult!=OK 跳过落库，集合零变化）。
        if (effective <= DateTime.Now)
        {
            var choice = MessageBox.Show(
                "生效时间已过，保存后该版本将「立即生效」并参与判定，是否继续？",
                "确认生效时间",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes) return;
        }

        Usl = hasUsl ? usl : double.NaN;
        Lsl = hasLsl ? lsl : double.NaN;
        EffectiveFrom = effective;
        RemarkText = _txtRemark.Text.Trim();
        OperatorName = op;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
