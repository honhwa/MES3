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
public class AbnormalDetailForm : Form
{
    private readonly string _abNo;
    private bool _notFound;

    // 概要标签
    private Label _lblNo = null!;
    private Label _lblStatus = null!;
    private Label _lblOccur = null!;
    private Label _lblFound = null!;
    private Label _lblLine = null!;
    private Label _lblEquip = null!;
    private Label _lblCat = null!;
    private Label _lblSub = null!;
    private Label _lblOwner = null!;
    private Label _lblRegBy = null!;
    private Label _lblRegTime = null!;
    private Label _lblConfirm = null!;
    private Label _lblDetail = null!;

    // 中部操作区容器与三个状态面板
    private GroupBox _actionGroup = null!;
    private Panel _pendingPanel = null!;
    private Panel _editPanel = null!;
    private Panel _closedPanel = null!;

    // 待确认面板控件
    private TextBox _txtConfirmOperator = null!;

    // 已确认（闭环编辑）面板控件
    private TextBox _txtRootCause = null!;
    private TextBox _txtRootMeasure = null!;
    private ComboBox _cboHorizon = null!;
    private TextBox _txtHorizonScope = null!;
    private Label _lblHorizonScopeTitle = null!;
    private TextBox _txtCloseOperator = null!;

    // 已闭环只读面板控件
    private TextBox _txtClosedCause = null!;
    private TextBox _txtClosedMeasure = null!;
    private TextBox _txtClosedHorizon = null!;
    private Label _lblClosedMeta = null!;

    private DataGridView _historyGrid = null!;

    public AbnormalDetailForm(string abNo)
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

        Font = new Font("微软雅黑", 9F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 800);
        MinimumSize = new Size(1000, 700);

        BuildUi();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_notFound) Close();
    }

    private void BuildUi()
    {
        var summary = BuildSummaryPanel();
        _actionGroup = BuildActionGroup();
        var historyGroup = MakeGridGroup("操作履历（登记 / 确认 / 闭环）", out _historyGrid);
        var bottom = BuildBottomBar();

        // Dock 布局：后添加的控件先贴边；先 fill，再 Top（后加的在上），最后 Bottom
        Controls.Add(historyGroup);
        Controls.Add(_actionGroup);
        Controls.Add(summary);
        Controls.Add(bottom);

        SetupHistoryColumns();
        Reload();
    }

    // ================= 顶部概要 =================

    private Control BuildSummaryPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 196,
            BackColor = Color.White,
            Padding = new Padding(12, 8, 12, 6)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblNo = MakeInfoLabel();
        _lblStatus = MakeInfoLabel();
        _lblOccur = MakeInfoLabel();
        _lblFound = MakeInfoLabel();
        _lblLine = MakeInfoLabel();
        _lblEquip = MakeInfoLabel();
        _lblCat = MakeInfoLabel();
        _lblSub = MakeInfoLabel();
        _lblOwner = MakeInfoLabel();
        _lblRegBy = MakeInfoLabel();
        _lblRegTime = MakeInfoLabel();
        _lblConfirm = MakeInfoLabel();
        _lblDetail = MakeInfoLabel();
        _lblDetail.AutoSize = false;
        _lblDetail.TextAlign = ContentAlignment.TopLeft;
        _lblDetail.Dock = DockStyle.Fill;

        grid.Controls.Add(_lblNo, 0, 0);
        grid.Controls.Add(_lblStatus, 1, 0);
        grid.Controls.Add(_lblOccur, 2, 0);
        grid.Controls.Add(_lblFound, 3, 0);

        grid.Controls.Add(_lblLine, 0, 1);
        grid.Controls.Add(_lblEquip, 1, 1);
        grid.Controls.Add(_lblCat, 2, 1);
        grid.Controls.Add(_lblSub, 3, 1);

        grid.Controls.Add(_lblOwner, 0, 2);
        grid.Controls.Add(_lblRegBy, 1, 2);
        grid.Controls.Add(_lblRegTime, 2, 2);
        grid.Controls.Add(_lblConfirm, 3, 2);

        grid.Controls.Add(_lblDetail, 0, 3);
        grid.SetColumnSpan(_lblDetail, 4);

        panel.Controls.Add(grid);
        return panel;
    }

    private static Label MakeInfoLabel() =>
        new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            AutoEllipsis = true,
            Margin = new Padding(2, 1, 8, 1)
        };

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

    // ================= 中部操作区 =================

    private GroupBox BuildActionGroup()
    {
        var box = new GroupBox
        {
            Dock = DockStyle.Top,
            Height = 390,
            Padding = new Padding(10, 16, 10, 6)
        };

        _pendingPanel = BuildPendingPanel();
        _editPanel = BuildEditPanel();
        _closedPanel = BuildClosedPanel();

        box.Controls.Add(_pendingPanel);
        box.Controls.Add(_editPanel);
        box.Controls.Add(_closedPanel);
        return box;
    }

    private static Panel MakeActionPanel()
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(6, 2, 6, 2)
        };
        return p;
    }

    // ---- 待确认面板 ----
    private Panel BuildPendingPanel()
    {
        var panel = MakeActionPanel();
        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 6, 8, 4)
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "请核对上述登记内容（发生信息/描述/临时措施）无误后点击「确认登记」，"
                 + "系统将把异常单状态变更为「已确认」并转后续负责人处理。",
            ForeColor = Color.FromArgb(230, 126, 34),
            Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        tbl.Controls.Add(hint, 0, 0);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        flow.Controls.Add(new Label { Text = "确认操作人：", AutoSize = true, Margin = new Padding(0, 9, 0, 0) });
        _txtConfirmOperator = new TextBox { Text = "admin", Width = 180, Margin = new Padding(6, 4, 0, 0) };
        flow.Controls.Add(_txtConfirmOperator);
        var btnConfirm = MakeActionButton("确认登记", BtnConfirm_Click);
        flow.Controls.Add(btnConfirm);
        tbl.Controls.Add(flow, 0, 1);

        panel.Controls.Add(tbl);
        return panel;
    }

    // ---- 已确认面板（闭环编辑） ----
    private Panel BuildEditPanel()
    {
        var panel = MakeActionPanel();
        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(8, 4, 8, 4)
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        tbl.Controls.Add(MakeFieldHint("请作为后续负责人补充根本原因与根本措施；若其他线体需要横展，请填写横展范围/计划。"), 0, 0);

        tbl.Controls.Add(MakeFieldTitle("根本原因 *"), 0, 1);
        _txtRootCause = MakeMultiText();
        tbl.Controls.Add(_txtRootCause, 0, 2);

        tbl.Controls.Add(MakeFieldTitle("根本措施 *"), 0, 3);
        _txtRootMeasure = MakeMultiText();
        tbl.Controls.Add(_txtRootMeasure, 0, 4);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        flow.Controls.Add(new Label { Text = "其他线体横展：", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
        _cboHorizon = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110,
            Margin = new Padding(6, 5, 0, 0)
        };
        _cboHorizon.Items.AddRange(AbnormalHorizon.All);
        _cboHorizon.SelectedItem = AbnormalHorizon.NotNeeded;
        _cboHorizon.SelectedIndexChanged += (_, _) => UpdateHorizonScopeEnabled();
        flow.Controls.Add(_cboHorizon);
        flow.Controls.Add(new Label { Text = "    闭环操作人：", AutoSize = true, Margin = new Padding(6, 10, 0, 0) });
        _txtCloseOperator = new TextBox { Text = "admin", Width = 160, Margin = new Padding(6, 5, 0, 0) };
        flow.Controls.Add(_txtCloseOperator);
        tbl.Controls.Add(flow, 0, 5);

        _lblHorizonScopeTitle = MakeFieldTitle("横展范围 / 横展计划 *（仅「需要」时填写）");
        tbl.Controls.Add(_lblHorizonScopeTitle, 0, 6);
        _txtHorizonScope = MakeMultiText();
        tbl.Controls.Add(_txtHorizonScope, 0, 7);
        _txtHorizonScope.Enabled = false;

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var btnClose = MakeActionButton("提交闭环", BtnClose_Click);
        btnFlow.Controls.Add(btnClose);
        btnFlow.Controls.Add(new Label
        {
            Text = "提交后状态变更为「已闭环」，不可再修改",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        tbl.Controls.Add(btnFlow, 0, 8);

        panel.Controls.Add(tbl);
        return panel;
    }

    private void UpdateHorizonScopeEnabled()
    {
        if (_cboHorizon is null || _txtHorizonScope is null || _lblHorizonScopeTitle is null) return;
        var needed = _cboHorizon.SelectedItem?.ToString() == AbnormalHorizon.Needed;
        _txtHorizonScope.Enabled = needed;
        _txtHorizonScope.BackColor = needed ? Color.White : Color.FromArgb(245, 245, 245);
        _lblHorizonScopeTitle.ForeColor = needed ? Color.FromArgb(40, 50, 60) : Color.Gray;
    }

    // ---- 已闭环面板（只读） ----
    private Panel BuildClosedPanel()
    {
        var panel = MakeActionPanel();
        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(8, 4, 8, 4)
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        tbl.Controls.Add(MakeFieldHint("该异常已完成闭环分析，闭环结论如下（只读，不可再编辑）。"), 0, 0);

        tbl.Controls.Add(MakeFieldTitle("根本原因"), 0, 1);
        _txtClosedCause = MakeReadonlyText();
        tbl.Controls.Add(_txtClosedCause, 0, 2);

        tbl.Controls.Add(MakeFieldTitle("根本措施"), 0, 3);
        _txtClosedMeasure = MakeReadonlyText();
        tbl.Controls.Add(_txtClosedMeasure, 0, 4);

        tbl.Controls.Add(MakeFieldTitle("横展结论"), 0, 5);
        _txtClosedHorizon = MakeReadonlyText();
        tbl.Controls.Add(_txtClosedHorizon, 0, 6);

        _lblClosedMeta = MakeFieldHint("");
        _lblClosedMeta.TextAlign = ContentAlignment.MiddleLeft;
        tbl.Controls.Add(_lblClosedMeta, 0, 7);

        panel.Controls.Add(tbl);
        return panel;
    }

    private static Label MakeFieldTitle(string text) =>
        new()
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Color.FromArgb(40, 50, 60),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 2)
        };

    private static Label MakeFieldHint(string text) =>
        new()
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
            AutoEllipsis = true
        };

    private static TextBox MakeMultiText() =>
        new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White
        };

    private static TextBox MakeReadonlyText() =>
        new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            ForeColor = Color.Black
        };

    private static Button MakeActionButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Width = 120,
            Height = 34,
            Margin = new Padding(10, 4, 0, 0),
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        b.Click += onClick;
        return b;
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

    // ================= 底部按钮条 =================

    private Control BuildBottomBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.WhiteSmoke
        };
        var btnClose = new Button
        {
            Text = "关闭",
            Width = 100,
            Height = 32,
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };
        btnClose.Location = new Point(bar.Width - 116, 9);
        bar.Resize += (_, _) => btnClose.Location = new Point(bar.Width - 116, 9);
        btnClose.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        bar.Controls.Add(btnClose);
        return bar;
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

    private static DataGridView MakeGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(15, 76, 129),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.Black,
                SelectionBackColor = Color.FromArgb(207, 224, 239),
                SelectionForeColor = Color.Black,
                Font = new Font("微软雅黑", 9F)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) }
        };
        return g;
    }

    private static Control MakeGridGroup(string title, out DataGridView grid)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6, 12, 6, 6) };
        grid = MakeGrid();
        box.Controls.Add(grid);
        return box;
    }

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
