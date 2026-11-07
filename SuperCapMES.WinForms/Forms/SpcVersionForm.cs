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
/// 控件宣告与静态配置见 SpcVersionForm.Designer.cs。
/// </summary>
public partial class SpcVersionForm : Form
{
    private string _itemName = "";
    private int _nextVersion;
    private bool _isEdit;
    private DateTime? _lowerBound;

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
        InitializeComponent();
        InitializeRuntime(itemName, nextVersion, usl, lsl, effectiveFrom, remark, operatorName, lowerBound, isEdit);
    }

    /// <summary>
    /// 执行期初始化：仅处理依赖建构式参数的内容（Designer 无法解析的插值字串 / 条件运算式 / 动态初值）。
    /// 原建构式内除了这些赋值外，其余控件建立 / 配置 / 加入容器均已搬至 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(
        string itemName,
        int nextVersion,
        double usl,
        double lsl,
        DateTime effectiveFrom,
        string remark,
        string operatorName,
        DateTime? lowerBound,
        bool isEdit)
    {
        _itemName = itemName;
        _nextVersion = nextVersion;
        _isEdit = isEdit;
        _lowerBound = lowerBound;

        // 以栏位值组标题（值与参数相同，行为不变；栏位在此被读取）。
        Text = _isEdit ? $"修改待生效版本 V{_nextVersion}" : $"新建规格版本 V{_nextVersion}";
        lblItemName.Text = _itemName;
        _txtUsl.Text = double.IsNaN(usl) ? "" : usl.ToString("0.###", CultureInfo.InvariantCulture);
        _txtLsl.Text = double.IsNaN(lsl) ? "" : lsl.ToString("0.###", CultureInfo.InvariantCulture);
        if (effectiveFrom != default) _dtDate.Value = effectiveFrom.Date;
        if (effectiveFrom != default) _dtTime.Value = effectiveFrom;
        _txtRemark.Text = remark ?? "";
        _txtOperator.Text = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName;

        UpdatePreview();
    }

    private void TxtUsl_TextChanged(object? sender, EventArgs e)
    {
        UpdatePreview();
    }

    private void TxtLsl_TextChanged(object? sender, EventArgs e)
    {
        UpdatePreview();
    }

    private void BtnRow_Resize(object? sender, EventArgs e)
    {
        btnOk.Location = new Point(btnRow.Width - 226, 10);
        btnCancel.Location = new Point(btnRow.Width - 114, 10);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        Save();
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

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
