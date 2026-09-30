using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;
using SuperCapMES.WinForms.Utils;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分，第 1 部分）：异常登记与异常管理页（登记 / 确认 / 根因闭环 / 横展 / 履历）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Abnormal」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 异常登记 =================
    /// <summary>构建「Abnormal」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildAbnormalTab()
    {
        var page = new TabPage("异常登记");
        page.Padding = new Padding(6);

        var recentBox = MakeGridGroup("最近登记（最新 5 条，登记后实时更新；双击可查看处置）", out _abRecentGrid);
        _abRecentGrid.CellDoubleClick -= AbRecentGrid_CellDoubleClick;
        _abRecentGrid.CellDoubleClick += AbRecentGrid_CellDoubleClick;
        SetupAbRecentColumns();

        page.Controls.Add(recentBox);              // Fill
        page.Controls.Add(BuildAbnormalFormArea()); // Top（异常登记表单）

        RefreshAbnormalRecent();
        return page;
    }

    /// <summary>构建「AbnormalFormArea」界面区块。</summary>
    private Control BuildAbnormalFormArea()
    {
        var box = new GroupBox
        {
            Dock = DockStyle.Top,
            Height = 368,
            Text = "现场异常情况登记（* 为必填）",
            Padding = new Padding(10, 14, 10, 6)
        };

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 9,
            Padding = new Padding(0)
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ---- 第 1 行：发生时间 / 登记人 ----
        tbl.Controls.Add(MakeAbLabel("发生时间 *"), 0, 0);
        _dtAbOccurTime = MakeAbDateTimePicker();
        _dtAbOccurTime.Value = DateTime.Now;
        tbl.Controls.Add(_dtAbOccurTime, 1, 0);
        tbl.Controls.Add(MakeAbLabel("登记人 *"), 2, 0);
        _txtAbRegisterBy = new TextBox { Dock = DockStyle.Fill, Text = "admin", Margin = new Padding(6, 3, 2, 3) };
        tbl.Controls.Add(_txtAbRegisterBy, 3, 0);

        // ---- 第 2 行：发现人 / 后续负责人 ----
        tbl.Controls.Add(MakeAbLabel("发现人 *"), 0, 1);
        _txtAbFoundBy = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6, 3, 2, 3) };
        tbl.Controls.Add(_txtAbFoundBy, 1, 1);
        tbl.Controls.Add(MakeAbLabel("后续负责人 *"), 2, 1);
        _txtAbOwner = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6, 3, 2, 3) };
        tbl.Controls.Add(_txtAbOwner, 3, 1);

        // ---- 第 3 行：发生线体 / 发生设备 ----
        tbl.Controls.Add(MakeAbLabel("发生线体 *"), 0, 2);
        _cboAbLine = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 3, 2, 3)
        };
        _cboAbLine.Items.AddRange(LineOptions.All);
        if (_cboAbLine.Items.Count > 0) _cboAbLine.SelectedIndex = 0;
        tbl.Controls.Add(_cboAbLine, 1, 2);

        tbl.Controls.Add(MakeAbLabel("发生设备 *"), 2, 2);
        _cboAbEquipment = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 3, 2, 3)
        };
        _cboAbEquipment.DataSource = MesDatabase.Equipments.ToList();
        _cboAbEquipment.DisplayMember = "Name";
        _cboAbEquipment.ValueMember = "Code";
        if (_cboAbEquipment.Items.Count > 0) _cboAbEquipment.SelectedIndex = 0;
        tbl.Controls.Add(_cboAbEquipment, 3, 2);

        // ---- 第 4 行：异常大类 / 异常细分 ----
        tbl.Controls.Add(MakeAbLabel("异常大类 *"), 0, 3);
        _cboAbCategory = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 3, 2, 3)
        };
        _cboAbCategory.Items.AddRange(AbnormalCategories.All);
        _cboAbCategory.SelectedIndexChanged += (_, _) => ReloadAbSubTypes();
        tbl.Controls.Add(_cboAbCategory, 1, 3);

        tbl.Controls.Add(MakeAbLabel("异常细分 *"), 2, 3);
        _cboAbSubType = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 3, 2, 3)
        };
        tbl.Controls.Add(_cboAbSubType, 3, 3);

        // 大类/细分联动初始填充：先建好细分下拉再设大类默认值（触发联动刷新）
        if (_cboAbCategory.Items.Count > 0) _cboAbCategory.SelectedIndex = 0;

        // ---- 第 5 行：异常描述标题 ----
        var lblDesc = MakeAbLabel("异常描述 *");
        lblDesc.TextAlign = ContentAlignment.BottomLeft;
        lblDesc.Margin = new Padding(2, 6, 2, 2);
        tbl.Controls.Add(lblDesc, 0, 4);
        tbl.SetColumnSpan(lblDesc, 4);
        _txtAbDescription = MakeAbMultiText();
        tbl.Controls.Add(_txtAbDescription, 0, 5);
        tbl.SetColumnSpan(_txtAbDescription, 4);

        // ---- 第 7 行：临时措施标题 ----
        var lblTemp = MakeAbLabel("临时措施 *");
        lblTemp.TextAlign = ContentAlignment.BottomLeft;
        lblTemp.Margin = new Padding(2, 6, 2, 2);
        tbl.Controls.Add(lblTemp, 0, 6);
        tbl.SetColumnSpan(lblTemp, 4);
        _txtAbTempMeasure = MakeAbMultiText();
        tbl.Controls.Add(_txtAbTempMeasure, 0, 7);
        tbl.SetColumnSpan(_txtAbTempMeasure, 4);

        // ---- 第 9 行：按钮 ----
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        btnRow.Controls.Add(MakeToolbarButton("保存登记", BtnSaveAbnormal));
        btnRow.Controls.Add(MakeToolbarButton("重置", BtnResetAbnormalForm));
        btnRow.Controls.Add(new Label
        {
            Text = "提示：登记成功后将生成异常编号（AB-yyyyMMdd-xxx），状态为「待确认」，由后续负责人确认与闭环。",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(14, 9, 0, 0)
        });
        tbl.Controls.Add(btnRow, 0, 8);
        tbl.SetColumnSpan(btnRow, 4);

        box.Controls.Add(tbl);
        return box;
    }

    /// <summary>创建「AbLabel」控件/样式（统一样式的工厂方法）。</summary>
    private static Label MakeAbLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0, 0, 4, 0),
            ForeColor = Color.FromArgb(40, 50, 60)
        };

    /// <summary>创建「AbDateTimePicker」控件/样式（统一样式的工厂方法）。</summary>
    private static DateTimePicker MakeAbDateTimePicker() =>
        new()
        {
            Dock = DockStyle.Fill,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm",
            Margin = new Padding(6, 3, 2, 3)
        };

    /// <summary>创建「AbMultiText」控件/样式（统一样式的工厂方法）。</summary>
    private static TextBox MakeAbMultiText() =>
        new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Margin = new Padding(6, 1, 2, 3)
        };

    /// <summary>异常大类变化 → 联动刷新异常细分下拉；无细分项时清空（保存校验可放宽）</summary>
    private void ReloadAbSubTypes()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_cboAbSubType is null) return;
        _cboAbSubType.Items.Clear();
        var category = _cboAbCategory.SelectedItem?.ToString() ?? "";
        foreach (var sub in AbnormalSubTypes.Get(category))
            _cboAbSubType.Items.Add(sub);
        if (_cboAbSubType.Items.Count > 0) _cboAbSubType.SelectedIndex = 0;
    }

    /// <summary>处理「ResetAbnormalForm」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnResetAbnormalForm(object? sender, EventArgs e)
    {
        _dtAbOccurTime.Value = DateTime.Now;
        _txtAbFoundBy.Text = "";
        if (_cboAbLine.Items.Count > 0) _cboAbLine.SelectedIndex = 0;
        if (_cboAbEquipment.Items.Count > 0) _cboAbEquipment.SelectedIndex = 0;
        if (_cboAbCategory.Items.Count > 0) _cboAbCategory.SelectedIndex = 0;
        _txtAbDescription.Text = "";
        _txtAbTempMeasure.Text = "";
        _txtAbOwner.Text = "";
        _txtAbRegisterBy.Text = "admin";
        _txtAbFoundBy.Focus();
    }

    /// <summary>处理「SaveAbnormal」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSaveAbnormal(object? sender, EventArgs e)
    {
        var foundBy = _txtAbFoundBy.Text.Trim();
        if (string.IsNullOrEmpty(foundBy)) { Warn("请填写发现人。"); return; }

        var line = _cboAbLine.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrEmpty(line)) { Warn("请选择发生线体。"); return; }

        if (_cboAbEquipment.SelectedItem is not Equipment eq || string.IsNullOrEmpty(eq.Code))
        {
            Warn("请选择发生设备。");
            return;
        }

        var category = _cboAbCategory.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrEmpty(category)) { Warn("请选择异常大类。"); return; }

        var subType = _cboAbSubType.SelectedItem?.ToString() ?? "";
        var subTypes = AbnormalSubTypes.Get(category);
        if (subTypes.Length > 0 && string.IsNullOrEmpty(subType))
        {
            Warn("请选择异常类型细分。");
            return;
        }

        var tempMeasure = _txtAbTempMeasure.Text.Trim();
        if (string.IsNullOrEmpty(tempMeasure)) { Warn("请填写临时措施。"); return; }

        var owner = _txtAbOwner.Text.Trim();
        if (string.IsNullOrEmpty(owner)) { Warn("请填写后续负责人。"); return; }

        var description = _txtAbDescription.Text.Trim();
        if (string.IsNullOrEmpty(description)) { Warn("请填写异常描述。"); return; }

        var registerBy = _txtAbRegisterBy.Text.Trim();
        if (string.IsNullOrEmpty(registerBy)) registerBy = "admin";

        var abNo = MesDatabase.CreateAbnormal(
            _dtAbOccurTime.Value, foundBy, line, eq.Code, eq.Name,
            category, subType, description, tempMeasure, owner, registerBy);
        MessageBox.Show($"登记成功，异常编号 {abNo}。\n状态为「待确认」，将由后续负责人 {owner} 确认并闭环。",
            "登记成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        BtnResetAbnormalForm(this, EventArgs.Empty);
        RefreshAbnormalRecent();
    }

    /// <summary>刷新「AbnormalRecent」的显示。</summary>
    private void RefreshAbnormalRecent()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_abRecentGrid is null) return;
        var list = MesDatabase.AbnormalRecords
            .OrderByDescending(a => a.RegisterTime)
            .ThenByDescending(a => a.AbNo, StringComparer.Ordinal)
            .Take(5)
            .ToList();
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _abRecentGrid.DataSource = null;
        _abRecentGrid.DataSource = list;
    }

    /// <summary>设置「AbRecentColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupAbRecentColumns()
    {
        _abRecentGrid.AutoGenerateColumns = false;
        _abRecentGrid.Columns.Clear();
        _abRecentGrid.Columns.Add(MakeTextCol("AbNo", "异常编号", true, 14));
        var occur = MakeTextCol("OccurTime", "发生时间", true, 13);
        occur.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _abRecentGrid.Columns.Add(occur);
        _abRecentGrid.Columns.Add(MakeTextCol("FoundBy", "发现人", true, 7));
        _abRecentGrid.Columns.Add(MakeTextCol("Line", "线体", true, 4));
        _abRecentGrid.Columns.Add(MakeTextCol("EquipmentDesc", "设备", true, 16));
        _abRecentGrid.Columns.Add(MakeTextCol("Category", "异常大类", true, 10));
        _abRecentGrid.Columns.Add(MakeTextCol("SubType", "细分", true, 12));
        var statusCol = MakeTextCol("Status", "状态", true, 7);
        _abRecentGrid.Columns.Add(statusCol);

        // 先解除再挂接着色事件（同上，防止重复着色）
        _abRecentGrid.CellFormatting -= AbnormalStatus_CellFormatting;
        _abRecentGrid.CellFormatting += AbnormalStatus_CellFormatting;
    }

    /// <summary>双击「最近登记」行 → 打开异常详情/处置窗体（数据实时查询，关闭后刷新列表）</summary>
    private void AbRecentGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_abRecentGrid.Rows[e.RowIndex].DataBoundItem is not AbnormalRecord rec) return;
        if (string.IsNullOrEmpty(rec.AbNo)) return;
        OpenAbnormalDetail(rec.AbNo);
        RefreshAbnormalRecent();
    }

    // ================= 异常管理（查询 + 处置） =================
    /// <summary>构建「AbnormalManage」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildAbnormalManageTab()
    {
        var page = new TabPage("异常管理");
        page.Padding = new Padding(6);

        var gridGroup = MakeGridGroup("异常登记查询列表（选中一行 → 底部按钮处置；双击一行 → 查看详情/操作）", out _abGrid);
        SetupAbGridColumns();
        _abGrid.SelectionChanged += (_, _) => LoadAbnormalContext();

        page.Controls.Add(gridGroup);              // Fill
        page.Controls.Add(BuildAbnormalManageBottom());  // Bottom 动作条
        page.Controls.Add(BuildAbnormalQueryRow2());     // Top 查询行2（时间 + 按钮）
        page.Controls.Add(BuildAbnormalQueryRow1());     // Top 查询行1（编号/状态/大类/线体）

        DoQueryAbnormals();
        return page;
    }

    /// <summary>构建「AbnormalQueryRow1」界面区块。</summary>
    private Control BuildAbnormalQueryRow1()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(6, 7, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "异常编号：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _txtAbQueryNo = new TextBox { Width = 150, Margin = new Padding(4, 4, 0, 0) };
        _txtAbQueryNo.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoQueryAbnormals(); };
        bar.Controls.Add(_txtAbQueryNo);

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboAbStatusFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 92,
            Margin = new Padding(4, 4, 0, 0)
        };
        _cboAbStatusFilter.Items.Add("全部");
        _cboAbStatusFilter.Items.AddRange(AbnormalStatus.All);
        _cboAbStatusFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboAbStatusFilter);

        bar.Controls.Add(new Label { Text = "异常大类：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboAbCategoryFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 108,
            Margin = new Padding(4, 4, 0, 0)
        };
        _cboAbCategoryFilter.Items.Add("全部");
        _cboAbCategoryFilter.Items.AddRange(AbnormalCategories.All);
        _cboAbCategoryFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboAbCategoryFilter);

        bar.Controls.Add(new Label { Text = "线体：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboAbLineFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 80,
            Margin = new Padding(4, 4, 0, 0)
        };
        _cboAbLineFilter.Items.Add("全部");
        _cboAbLineFilter.Items.AddRange(LineOptions.All);
        _cboAbLineFilter.SelectedIndex = 0;
        bar.Controls.Add(_cboAbLineFilter);
        return bar;
    }

    /// <summary>构建「AbnormalQueryRow2」界面区块。</summary>
    private Control BuildAbnormalQueryRow2()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(6, 4, 4, 0),
            WrapContents = false
        };
        _chkAbUseTimeFilter = new CheckBox
        {
            Text = "按发生时间",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        _chkAbUseTimeFilter.CheckedChanged += (_, _) =>
        {
            var enabled = _chkAbUseTimeFilter.Checked;
            _dtAbFrom.Enabled = enabled;
            _dtAbTo.Enabled = enabled;
        };
        bar.Controls.Add(_chkAbUseTimeFilter);

        _dtAbFrom = MakeHoldDatePicker();
        _dtAbFrom.Value = DateTime.Today.AddDays(-30);
        bar.Controls.Add(_dtAbFrom);
        bar.Controls.Add(new Label { Text = " ~ ", AutoSize = true, Margin = new Padding(2, 8, 0, 0) });
        _dtAbTo = MakeHoldDatePicker();
        _dtAbTo.Value = DateTime.Today;
        bar.Controls.Add(_dtAbTo);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryAbnormals()));
        bar.Controls.Add(MakeToolbarButton("重置", BtnResetAbnormalQuery));

        _lblAbQueryResult = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(14, 8, 0, 0)
        };
        bar.Controls.Add(_lblAbQueryResult);
        return bar;
    }

    /// <summary>构建「AbnormalManageBottom」界面区块。</summary>
    private Control BuildAbnormalManageBottom()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8, 6, 4, 0),
            WrapContents = false
        };
        _lblAbContext = new Label
        {
            Text = "请选择一条异常记录",
            AutoSize = true,
            Margin = new Padding(2, 9, 0, 0),
            ForeColor = Color.FromArgb(40, 50, 60)
        };
        bar.Controls.Add(_lblAbContext);

        bar.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(14, 9, 0, 0) });
        _txtAbOperator = new TextBox { Text = "admin", Width = 110, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtAbOperator);

        _btnAbConfirm = MakeToolbarButton("确认登记", BtnAbConfirm);
        _btnAbConfirm.Enabled = false;
        bar.Controls.Add(_btnAbConfirm);

        _btnAbClose = MakeToolbarButton("闭环分析", BtnAbOpenDetailForClose);
        _btnAbClose.Enabled = false;
        bar.Controls.Add(_btnAbClose);

        bar.Controls.Add(MakeToolbarButton("打印异常单", BtnPrintAbnormalSlip));
        _btnAbView = MakeToolbarButton("查看详情", BtnAbOpenDetail);
        _btnAbView.Enabled = false;
        bar.Controls.Add(_btnAbView);
        return bar;
    }

    /// <summary>设置「AbGridColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupAbGridColumns()
    {
        _abGrid.AutoGenerateColumns = false;
        _abGrid.Columns.Clear();
        _abGrid.Columns.Add(MakeTextCol("AbNo", "异常编号", true, 14));
        var occur = MakeTextCol("OccurTime", "发生时间", true, 13);
        occur.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _abGrid.Columns.Add(occur);
        _abGrid.Columns.Add(MakeTextCol("FoundBy", "发现人", true, 7));
        _abGrid.Columns.Add(MakeTextCol("Line", "线体", true, 4));
        _abGrid.Columns.Add(MakeTextCol("EquipmentDesc", "设备", true, 15));
        _abGrid.Columns.Add(MakeTextCol("Category", "异常大类", true, 10));
        _abGrid.Columns.Add(MakeTextCol("SubType", "细分", true, 12));
        _abGrid.Columns.Add(MakeTextCol("Status", "状态", true, 7));
        _abGrid.Columns.Add(MakeTextCol("Owner", "后续负责人", true, 8));
        _abGrid.Columns.Add(MakeTextCol("SourceType", "来源", true, 8));
        _abGrid.Columns.Add(MakeTextCol("SourceRef", "来源单号", true, 14));

        // 先解除再挂接着色事件（同上，防止重复着色）
        _abGrid.CellFormatting -= AbnormalStatus_CellFormatting;
        _abGrid.CellFormatting += AbnormalStatus_CellFormatting;
        _abGrid.CellDoubleClick -= AbGrid_CellDoubleClick;
        _abGrid.CellDoubleClick += AbGrid_CellDoubleClick;
    }

    /// <summary>异常状态着色：待确认=橙、已确认=蓝、已闭环=绿（主网格与最近登记共用）</summary>
    private void AbnormalStatus_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name != "Status") return;

        var value = e.Value?.ToString();
        e.CellStyle.Font = value == AbnormalStatus.Pending
            ? new Font("微软雅黑", 9F, FontStyle.Bold)
            : new Font("微软雅黑", 9F);
        e.CellStyle.ForeColor = value switch
        {
            AbnormalStatus.Pending => Color.FromArgb(230, 126, 34),
            AbnormalStatus.Confirmed => Color.FromArgb(15, 76, 129),
            AbnormalStatus.Closed => Color.FromArgb(39, 174, 96),
            _ => Color.FromArgb(40, 50, 60)
        };
    }

    /// <summary>查询并加载「Abnormals」数据到界面（含网格绑定与汇总刷新）。</summary>
    private void DoQueryAbnormals()
    {
        var noText = _txtAbQueryNo.Text.Trim();
        var status = _cboAbStatusFilter.SelectedItem?.ToString();
        if (status is null or "全部") status = null;
        var category = _cboAbCategoryFilter.SelectedItem?.ToString();
        if (category is null or "全部") category = null;
        var line = _cboAbLineFilter.SelectedItem?.ToString();
        if (line is null or "全部") line = null;

        DateTime? from = null;
        DateTime? to = null;
        if (_chkAbUseTimeFilter.Checked)
        {
            from = _dtAbFrom.Value.Date;
            to = _dtAbTo.Value.Date;
        }

        var list = MesDatabase.QueryAbnormals(
            string.IsNullOrEmpty(noText) ? null : noText,
            status, category, line, from, to);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _abGrid.DataSource = null;
        _abGrid.DataSource = list;
        _lblAbQueryResult.Text = $"共查询到 {list.Count} 条异常";
        LoadAbnormalContext();
    }

    /// <summary>处理「ResetAbnormalQuery」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnResetAbnormalQuery(object? sender, EventArgs e)
    {
        _txtAbQueryNo.Text = "";
        _cboAbStatusFilter.SelectedIndex = 0;
        _cboAbCategoryFilter.SelectedIndex = 0;
        _cboAbLineFilter.SelectedIndex = 0;
        _chkAbUseTimeFilter.Checked = true;
        _dtAbFrom.Value = DateTime.Today.AddDays(-30);
        _dtAbTo.Value = DateTime.Today;
        DoQueryAbnormals();
    }

    /// <summary>在网格中选中「edAbnormal」对应的行（联动加载明细）。</summary>
    private AbnormalRecord? SelectedAbnormal()
    {
        // 未选中任何行：返回 null 交由调用方提示
        if (_abGrid.SelectedRows.Count == 0) return null;
        return _abGrid.SelectedRows[0].DataBoundItem as AbnormalRecord;
    }

    /// <summary>加载「AbnormalContext」并绑定到界面。</summary>
    private void LoadAbnormalContext()
    {
        if (_abGrid is null || _btnAbConfirm is null || _btnAbClose is null || _btnAbView is null || _lblAbContext is null)
            return;
        var rec = SelectedAbnormal();
        if (rec is null)
        {
            _lblAbContext.Text = "请选择一条异常记录";
            _btnAbConfirm.Enabled = false;
            _btnAbClose.Enabled = false;
            _btnAbView.Enabled = false;
            return;
        }
        _lblAbContext.Text = $"{rec.AbNo} · {rec.Status} · {rec.Category}（{rec.SubType}）· {rec.Line} · {rec.OccurTime:MM-dd HH:mm}";
        _btnAbConfirm.Enabled = rec.Status == AbnormalStatus.Pending;
        _btnAbClose.Enabled = rec.Status == AbnormalStatus.Confirmed;
        _btnAbView.Enabled = true;
    }

    /// <summary>确认登记：仅待确认可确认（操作人取底部输入框，默认 admin）</summary>
    private void BtnAbConfirm(object? sender, EventArgs e)
    {
        var rec = SelectedAbnormal();
        if (rec is null) { Warn("请先选择要确认的异常单。"); return; }
        if (rec.Status != AbnormalStatus.Pending)
        {
            Warn($"异常单 {rec.AbNo} 当前状态为「{rec.Status}」，无需确认登记。");
            return;
        }
        var op = _txtAbOperator.Text.Trim();
        if (string.IsNullOrEmpty(op)) op = "admin";

        if (MessageBox.Show($"确认异常单 {rec.AbNo} 的登记内容无误？\n确认后状态将变更为「已确认」，转后续负责人 {rec.Owner} 处置。",
                "确认登记", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        var (ok, message) = MesDatabase.ConfirmAbnormal(rec.AbNo, op);
        if (!ok)
        {
            Warn(message);
            return;
        }
        MessageBox.Show(message, "确认登记", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryAbnormals();
        ReselectAbnormal(rec.AbNo);
    }

    /// <summary>闭环分析：仅已确认可进入详情/闭环窗体</summary>
    private void BtnAbOpenDetailForClose(object? sender, EventArgs e)
    {
        var rec = SelectedAbnormal();
        if (rec is null) { Warn("请先选择要闭环分析的异常单。"); return; }
        if (rec.Status != AbnormalStatus.Confirmed)
        {
            Warn($"异常单 {rec.AbNo} 当前状态为「{rec.Status}」，不能闭环分析（需先确认登记）。");
            return;
        }
        OpenAbnormalDetail(rec.AbNo);
    }

    /// <summary>查看详情：任何状态均可打开；关闭后按当前查询条件刷新并重选该行</summary>
    private void BtnAbOpenDetail(object? sender, EventArgs e)
    {
        var rec = SelectedAbnormal();
        if (rec is null) { Warn("请先选择要查看的异常单。"); return; }
        OpenAbnormalDetail(rec.AbNo);
    }

    /// <summary>「AbGrid」双击单元格 → 执行跳转/穿透查看。</summary>
    private void AbGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_abGrid.Rows[e.RowIndex].DataBoundItem is not AbnormalRecord rec) return;
        if (string.IsNullOrEmpty(rec.AbNo)) return;
        OpenAbnormalDetail(rec.AbNo);
    }

    /// <summary>OpenAbnormalDetail：（说明待补充）</summary>
    private void OpenAbnormalDetail(string abNo)
    {
        using var form = new AbnormalDetailForm(abNo);
        form.ShowDialog(this);
        DoQueryAbnormals();
        ReselectAbnormal(abNo);
    }

    /// <summary>刷新后尝试重新选中指定异常编号所在行（若仍在结果中）</summary>
    private void ReselectAbnormal(string abNo)
    {
        _abGrid.ClearSelection();
        foreach (DataGridViewRow row in _abGrid.Rows)
        {
            if (row.DataBoundItem is AbnormalRecord a && a.AbNo == abNo)
            {
                row.Selected = true;
                _abGrid.CurrentCell = row.Cells[0];
                break;
            }
        }
        LoadAbnormalContext();
    }


}
