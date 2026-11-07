using System;
using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// 现场异常详情/处置窗体：按异常单号实时查询展示。
/// 顶部概要（编号/状态/发生信息等），中部按状态展示操作区——
/// 待确认：核对登记内容后「确认登记」；已确认：由后续负责人填写根本原因/根本措施/横展结论后「提交闭环」；
/// 已闭环：只读展示闭环结论；底部始终展示 登记/确认/闭环 操作履历。
/// 每次动作执行后就地重取数据刷新本窗体，保证状态与履历最新。
/// </summary>
public partial class AbnormalDetailForm : Form
{
#nullable disable
    private string _abNo;
    private bool _notFound;
#nullable restore

    public AbnormalDetailForm(string abNo)
    {
        InitializeComponent();
        InitializeRuntime(abNo);
    }

    /// <summary>
    /// 执行期初始化：依赖参数的 Text、MesDatabase 查询，以及原 BuildUi() 中
    /// 无法 Designer 化的动态段落（_cboHorizon 选项绑定与事件、DataGridViewCellStyle
    /// 物件初始设定式、履历栏位建置、就地刷新）。静态控件的建立、属性与容器
    /// 加入已全部展开至 AbnormalDetailForm.Designer.cs 的 InitializeComponent()。
    /// </summary>
    private void InitializeRuntime(string abNo)
    {
        _abNo = string.IsNullOrWhiteSpace(abNo) ? "" : abNo.Trim();
        var rec = MesDatabase.FindAbnormal(_abNo);
        if (rec is null)
        {
            _notFound = true;
            Text = "异常详情";
            MessageBox.Show($"未找到异常单 {_abNo}。\n该异常单可能已被删除或编号输入有误。",
                "异常详情", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            Text = $"异常详情 - {rec.AbNo}";
        }

        // 原 BuildEditPanel()：_cboHorizon 的执行期选项绑定与 SelectedIndexChanged 挂接。
        // 依迁移规格「挂接时机影响初始状态则保留在 .cs 原顺序位置」：
        // AddRange / SelectedItem 依赖 Models 的 AbnormalHorizon 静态成员，必须在
        // 挂上事件之前完成；否则 SelectedItem 变更会触发 UpdateHorizonScopeEnabled()
        // 而改写 _txtHorizonScope.BackColor / _lblHorizonScopeTitle.ForeColor 的初值。
        // 原 lambda 已改为具名方法 CboHorizon_SelectedIndexChanged。
        _cboHorizon.Items.AddRange(AbnormalHorizon.All);
        _cboHorizon.SelectedItem = AbnormalHorizon.NotNeeded;
        _cboHorizon.SelectedIndexChanged += new System.EventHandler(this.CboHorizon_SelectedIndexChanged);

        // 原 MakeGrid()：DataGridViewCellStyle 物件初始设定式，依规格留在 .cs。
        ApplyHistoryGridStyles();

        // 原 BuildUi() 尾部，保持原相对顺序。
        SetupHistoryColumns();
        Reload();
    }

    private void CboHorizon_SelectedIndexChanged(object? sender, EventArgs e) => UpdateHorizonScopeEnabled();

    // 原 BuildBottomBar() 的 bar.Resize lambda：捕获的 bar / btnClose 已提升为栏位，
    // bar.Width - 116 为非常值运算式，保留于方法本体（挂接在 Designer.cs）。
    private void BottomBar_Resize(object? sender, EventArgs e) =>
        _btnCloseBar.Location = new Point(_bottomBar.Width - 116, 9);

    // 原 BuildBottomBar() 的 btnClose.Click lambda（挂接在 Designer.cs）。
    private void BtnCloseBar_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // 原 MakeGrid() 的三项 DataGridViewCellStyle 物件初始设定式。
    private void ApplyHistoryGridStyles()
    {
        _historyGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        _historyGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.Black,
            SelectionBackColor = Color.FromArgb(207, 224, 239),
            SelectionForeColor = Color.Black,
            Font = new Font("微软雅黑", 9F)
        };
        _historyGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_notFound) Close();
    }

    // ================= 顶部概要 =================

    private void UpdateSummary(AbnormalRecord rec)
    {
        _lblNo.Text = $"编号：{rec.AbNo}";
        _lblStatus.Text = $"状态：{rec.Status}";
        _lblOccur.Text = $"发生时间：{rec.OccurTime:yyyy-MM-dd HH:mm}";
        _lblFound.Text = $"发现人：{rec.FoundBy}";
        _lblLine.Text = $"线体：{rec.Line}";
        _lblEquip.Text = $"设备：{rec.EquipmentDesc}";
        _lblCat.Text = $"异常大类：{rec.Category}";
        _lblSub.Text = $"细分：{rec.SubType}";
        _lblOwner.Text = $"后续负责人：{rec.Owner}";
        _lblRegBy.Text = $"登记人：{rec.RegisterBy}";
        _lblRegTime.Text = $"登记时间：{rec.RegisterTime:yyyy-MM-dd HH:mm}";

        if (rec.Status == AbnormalStatus.Confirmed || rec.Status == AbnormalStatus.Closed)
        {
            _lblConfirm.Text = $"确认人/时间：{rec.ConfirmBy}  {(rec.ConfirmTime.HasValue ? rec.ConfirmTime.Value.ToString("MM-dd HH:mm") : "—")}";
        }
        else
        {
            _lblConfirm.Text = "确认人/时间：—";
        }

        var detail = string.IsNullOrWhiteSpace(rec.Description)
            ? "异常描述：—"
            : $"异常描述：{rec.Description}";
        detail += string.IsNullOrWhiteSpace(rec.TemporaryMeasure)
            ? "\r\n临时措施：—"
            : $"\r\n临时措施：{rec.TemporaryMeasure}";
        _lblDetail.Text = detail;

        var statusColor = rec.Status switch
        {
            AbnormalStatus.Pending => Color.FromArgb(230, 126, 34),
            AbnormalStatus.Confirmed => Color.FromArgb(15, 76, 129),
            AbnormalStatus.Closed => Color.FromArgb(39, 174, 96),
            _ => Color.FromArgb(40, 50, 60)
        };
        _lblStatus.ForeColor = statusColor;
        _lblStatus.Font = new Font("微软雅黑", 10F, FontStyle.Bold);
        _lblNo.ForeColor = Color.FromArgb(15, 76, 129);
        _lblNo.Font = new Font("微软雅黑", 10F, FontStyle.Bold);
    }

    // ================= 操作区刷新 =================

    private void RenderActionPanel(AbnormalRecord rec)
    {
        _pendingPanel.Visible = rec.Status == AbnormalStatus.Pending;
        _editPanel.Visible = rec.Status == AbnormalStatus.Confirmed;
        _closedPanel.Visible = rec.Status == AbnormalStatus.Closed;

        if (rec.Status == AbnormalStatus.Pending)
        {
            _actionGroup.Text = "操作：待确认 · 请核对登记内容";
            _actionGroup.Height = 160;
        }
        else if (rec.Status == AbnormalStatus.Confirmed)
        {
            _actionGroup.Text = "操作：已确认 · 后续负责人填写根因分析并闭环";
            _actionGroup.Height = 400;
        }
        else
        {
            _actionGroup.Text = "操作：已闭环 · 闭环结论只读";
            _actionGroup.Height = 330;
        }
    }

    private void UpdateHorizonScopeEnabled()
    {
        if (_cboHorizon is null || _txtHorizonScope is null || _lblHorizonScopeTitle is null) return;
        var needed = _cboHorizon.SelectedItem?.ToString() == AbnormalHorizon.Needed;
        _txtHorizonScope.Enabled = needed;
        _txtHorizonScope.BackColor = needed ? Color.White : Color.FromArgb(245, 245, 245);
        _lblHorizonScopeTitle.ForeColor = needed ? Color.FromArgb(40, 50, 60) : Color.Gray;
    }

    // ================= 履历网格 =================

    private void SetupHistoryColumns()
    {
        _historyGrid.AutoGenerateColumns = false;
        _historyGrid.Columns.Clear();
        _historyGrid.Columns.Add(MakeCol("Action", "动作", "Action", 10));
        var timeCol = MakeCol("Time", "时间", "Time", 20);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm:ss";
        _historyGrid.Columns.Add(timeCol);
        _historyGrid.Columns.Add(MakeCol("Operator", "操作人", "Operator", 12));
        _historyGrid.Columns.Add(MakeCol("Remark", "备注", "Remark", 58));
        _historyGrid.DataSource = null;
    }

    private void BindHistory(string abNo)
    {
        _historyGrid.DataSource = null;
        _historyGrid.DataSource = MesDatabase.GetAbnormalHistory(abNo);
    }

    private static DataGridViewTextBoxColumn MakeCol(string name, string header, string prop, int fillWeight) =>
        new()
        {
            Name = name,
            HeaderText = header,
            DataPropertyName = prop,
            ReadOnly = true,
            FillWeight = fillWeight
        };

    // ================= 数据加载与动作 =================

    private void Reload()
    {
        if (_notFound) return;
        var rec = MesDatabase.FindAbnormal(_abNo);
        if (rec is null)
        {
            MessageBox.Show($"异常单 {_abNo} 已不存在，请刷新列表。", "异常详情", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        UpdateSummary(rec);
        RenderActionPanel(rec);
        if (rec.Status == AbnormalStatus.Confirmed)
        {
            _txtRootCause.Text = rec.RootCause;
            _txtRootMeasure.Text = rec.RootMeasure;
            _cboHorizon.SelectedItem = rec.HorizonNeeded.Length > 0 ? rec.HorizonNeeded : AbnormalHorizon.NotNeeded;
            _txtHorizonScope.Text = rec.HorizonScope;
            _txtCloseOperator.Text = string.IsNullOrWhiteSpace(rec.Owner) ? "admin" : rec.Owner;
            UpdateHorizonScopeEnabled();
        }
        else if (rec.Status == AbnormalStatus.Closed)
        {
            _txtClosedCause.Text = string.IsNullOrWhiteSpace(rec.RootCause) ? "—" : rec.RootCause;
            _txtClosedMeasure.Text = string.IsNullOrWhiteSpace(rec.RootMeasure) ? "—" : rec.RootMeasure;
            var horizonText = $"横展：{rec.HorizonNeeded}";
            if (rec.HorizonNeeded == AbnormalHorizon.Needed)
                horizonText += string.IsNullOrWhiteSpace(rec.HorizonScope)
                    ? "\r\n范围/计划：—"
                    : $"\r\n范围/计划：{rec.HorizonScope}";
            _txtClosedHorizon.Text = horizonText;
            _lblClosedMeta.Text = $"分析人：{rec.AnalysisBy}    分析时间：{(rec.AnalysisTime.HasValue ? rec.AnalysisTime.Value.ToString("yyyy-MM-dd HH:mm") : "—")}"
                + $"    闭环时间：{(rec.ClosedTime.HasValue ? rec.ClosedTime.Value.ToString("yyyy-MM-dd HH:mm") : "—")}";
        }
        BindHistory(_abNo);
    }

    private void BtnConfirm_Click(object? sender, EventArgs e)
    {
        var op = _txtConfirmOperator.Text.Trim();
        if (string.IsNullOrEmpty(op))
        {
            Warn("请填写确认操作人。");
            return;
        }
        if (MessageBox.Show($"确认异常单 {_abNo} 的登记内容无误？\n确认后状态将变更为「已确认」。",
                "确认登记", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        var (ok, message) = MesDatabase.ConfirmAbnormal(_abNo, op);
        if (!ok)
        {
            Warn(message);
            return;
        }
        MessageBox.Show(message, "确认登记", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Reload();
    }

    private void BtnClose_Click(object? sender, EventArgs e)
    {
        var op = _txtCloseOperator.Text.Trim();
        if (string.IsNullOrEmpty(op))
        {
            Warn("请填写闭环操作人。");
            return;
        }
        var cause = _txtRootCause.Text.Trim();
        if (string.IsNullOrEmpty(cause))
        {
            Warn("请填写根本原因。");
            return;
        }
        var measure = _txtRootMeasure.Text.Trim();
        if (string.IsNullOrEmpty(measure))
        {
            Warn("请填写根本措施。");
            return;
        }
        var horizon = _cboHorizon.SelectedItem?.ToString() ?? AbnormalHorizon.NotNeeded;
        var scope = _txtHorizonScope.Text.Trim();
        if (horizon == AbnormalHorizon.Needed && string.IsNullOrEmpty(scope))
        {
            Warn("「需要」横展时，请填写横展范围/计划。");
            return;
        }
        if (MessageBox.Show($"提交异常单 {_abNo} 的闭环结论？\n提交后状态将变更为「已闭环」，不可再修改。",
                "提交闭环", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        var (ok, message) = MesDatabase.CloseAbnormal(_abNo, cause, measure, horizon, scope, op);
        if (!ok)
        {
            Warn(message);
            return;
        }
        MessageBox.Show(message, "提交闭环", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Reload();
    }

    private static void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
