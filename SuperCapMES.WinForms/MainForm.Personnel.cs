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
/// MainForm 分部类（按功能域拆分，第 18 部分）：**人员与资质管理页**。
/// 职责：人员主数据台账 / 资质证书台账 / 资质矩阵（人员 × 资质项）/ 班组绩效 / 上岗资质核查，
/// 并提供「新增人员、登记资质、吊销资质、上岗校验、导出」五项操作。
/// 核心业务价值：**无资质不能上岗操作**——<see cref="Data.MesDatabase.CheckQualification"/> 是唯一判据，
/// 本页面只做可视化与录入，不重复实现判定规则。
/// </summary>
public partial class MainForm
{
    // ================= 人员与资质管理（顶层第 23 个页签） =================

    /// <summary>构建「人员与资质」页签：汇总卡 + 筛选条 + 四个子页 + 操作条。</summary>
    private TabPage BuildPersonnelTab()
    {
        var page = new TabPage("人员与资质");

        // ---- 顶部汇总卡：在册 / 持证 / 即将到期 / 已失效 ----
        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("在册人数 (人)", Color.FromArgb(15, 76, 129), out _lblPsOnDuty));
        cards.Controls.Add(MakeCard("持证人数 (人)", Color.FromArgb(39, 174, 96), out _lblPsCertified));
        cards.Controls.Add(MakeCard("资质即将到期 (项)", Color.FromArgb(230, 126, 34), out _lblPsExpiring));
        cards.Controls.Add(MakeCard("资质已失效 (项)", Color.FromArgb(192, 0, 0), out _lblPsInvalid));

        // ---- 四个子页 ----
        var sub = new TabControl { Dock = DockStyle.Fill };
        sub.TabPages.Add(BuildPsUserTab());
        sub.TabPages.Add(BuildPsMatrixTab());
        sub.TabPages.Add(BuildPsCrewTab());
        sub.TabPages.Add(BuildPsCheckTab());

        page.Controls.Add(sub);
        page.Controls.Add(BuildPersonnelActionBar());
        page.Controls.Add(BuildPersonnelQueryBar());
        page.Controls.Add(cards);

        SetupPsColumns();
        SetupPsCheckControls();
        DoQueryPersonnel();

        page.Enter -= PersonnelTab_Enter;
        page.Enter += PersonnelTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>构建「人员台账」子页：上方人员列表，下方该人员的资质明细。/// </summary>
    private TabPage BuildPsUserTab()
    {
        var page = new TabPage("人员台账");
        page.Padding = new Padding(4);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 320,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("人员台账（选中一行 → 下方显示该人资质明细）", out _psGrid));
        split.Panel2.Controls.Add(MakeGridGroup("资质明细（红色=已失效，橙色=即将到期）", out _psCertGrid));

        page.Controls.Add(split);
        return page;
    }

    /// <summary>构建「资质矩阵」子页：行=在职人员，列=全部资质项，格值=资质等级。</summary>
    private TabPage BuildPsMatrixTab()
    {
        var page = new TabPage("资质矩阵");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup("资质矩阵（按当前部门筛选；红色=已过期/已吊销，橙色=即将到期，绿色=有效，灰=无证）", out _psMatrixGrid));
        return page;
    }

    /// <summary>构建「班组绩效」子页：班组维度的人效与资质覆盖。</summary>
    private TabPage BuildPsCrewTab()
    {
        var page = new TabPage("班组绩效");
        page.Padding = new Padding(4);
        page.Controls.Add(MakeGridGroup("班组绩效（持证率 / 工序覆盖 / 工序实绩 / 无证上岗嫌疑）", out _psCrewGrid));
        return page;
    }

    /// <summary>构建「上岗资质核查」子页：单人校验 + 实绩逐条核查。</summary>
    private TabPage BuildPsCheckTab()
    {
        var page = new TabPage("上岗资质核查");
        page.Padding = new Padding(4);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 150,
            BackColor = SystemColors.Control
        };

        // 上：单人上岗校验（人员 + 工序 → 结论）
        split.Panel1.Controls.Add(BuildPsCheckQueryBar());

        // 下：实绩逐条核查
        split.Panel2.Controls.Add(MakeGridGroup("工序实绩资质核查（红色=不通过；可勾选「只看不通过」聚焦问题记录）", out _psCheckGrid));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Padding = new Padding(6, 4, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        _chkPsOnlyFailed = new CheckBox { Text = "只看不通过", AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        _chkPsOnlyFailed.CheckedChanged += (_, _) => DoQueryPsChecks();
        bar.Controls.Add(_chkPsOnlyFailed);
        bar.Controls.Add(MakeToolbarButton("刷新核查", (_, _) => DoQueryPsChecks()));
        split.Panel2.Controls.Add(bar);

        page.Controls.Add(split);
        return page;
    }

    /// <summary>构建单人上岗校验区：人员 / 工序 / 校验按钮 / 结论。</summary>
    private Control BuildPsCheckQueryBar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 6, 6) };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false };
        flow.Controls.Add(new Label { Text = "人员：", AutoSize = true, Margin = new Padding(0, 9, 0, 0) });
        _cboPsCheckEmp = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(4, 5, 0, 0) };
        flow.Controls.Add(_cboPsCheckEmp);

        flow.Controls.Add(new Label { Text = "工序：", AutoSize = true, Margin = new Padding(14, 9, 0, 0) });
        _cboPsCheckProcess = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(4, 5, 0, 0) };
        flow.Controls.Add(_cboPsCheckProcess);

        _btnPsCheck = MakeToolbarButton("上岗校验", BtnPsCheck);
        flow.Controls.Add(_btnPsCheck);

        _lblPsCheckResult = new Label
        {
            Text = "选择人员与工序后点击「上岗校验」，或直接双击下方核查记录查看结论。",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Padding = new Padding(8, 8, 8, 8),
            ForeColor = Color.Gray,
            BackColor = Color.WhiteSmoke,
            BorderStyle = BorderStyle.FixedSingle
        };
        // 停靠规则：Fill 控件先加入（下标小）、Top 控件后加入（下标大）→ 先满足 Top，再 Fill 剩余空间
        panel.Controls.Add(_lblPsCheckResult);
        panel.Controls.Add(flow);
        return panel;
    }

    /// <summary>构建「人员与资质」筛选条：部门 / 资质状态 / 关键字。</summary>
    private Control BuildPersonnelQueryBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };

        bar.Controls.Add(new Label { Text = "部门：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboPsDept = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(4, 4, 0, 0) };
        _cboPsDept.Items.Add("全部");
        foreach (var d in MesDatabase.GetDepartments()) _cboPsDept.Items.Add(d);
        _cboPsDept.SelectedIndex = 0;

        bar.Controls.Add(new Label { Text = "资质状态：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboPsCertStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(4, 4, 0, 0) };
        _cboPsCertStatus.Items.Add("全部");
        foreach (var s in CertStatus.All) _cboPsCertStatus.Items.Add(s);
        _cboPsCertStatus.SelectedIndex = 0;

        bar.Controls.Add(new Label { Text = "关键字：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _txtPsKeyword = new TextBox { Width = 190, Margin = new Padding(4, 4, 0, 0) };

        bar.Controls.Add(_cboPsDept);
        bar.Controls.Add(_cboPsCertStatus);
        bar.Controls.Add(_txtPsKeyword);
        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQueryPersonnel()));
        bar.Controls.Add(MakeToolbarButton("重置", (_, _) =>
        {
            if (_cboPsDept.Items.Count > 0) _cboPsDept.SelectedIndex = 0;
            if (_cboPsCertStatus.Items.Count > 0) _cboPsCertStatus.SelectedIndex = 0;
            _txtPsKeyword.Text = "";
            DoQueryPersonnel();
        }));
        bar.Controls.Add(new Label
        {
            Text = "（工号 / 姓名 / 岗位 / 资质项）",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(8, 10, 0, 0)
        });
        return bar;
    }

    /// <summary>构建「人员与资质」操作条：新增 / 编辑 / 登记资质 / 吊销 / 导出。</summary>
    private Control BuildPersonnelActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6, 6, 4, 0),
            WrapContents = false,
            BackColor = Color.WhiteSmoke
        };
        bar.Controls.Add(MakeToolbarButton("登记人员", BtnPsAddUser));
        _btnPsEdit = MakeToolbarButton("编辑人员", BtnPsEditUser);
        bar.Controls.Add(_btnPsEdit);
        bar.Controls.Add(MakeToolbarButton("登记资质", BtnPsAddCert));
        _btnPsRevoke = MakeToolbarButton("吊销资质", BtnPsRevokeCert);
        bar.Controls.Add(_btnPsRevoke);
        bar.Controls.Add(MakeToolbarButton("导出人员资质", BtnPsExport));
        bar.Controls.Add(new Label
        {
            Text = "口径：证书到期前 30 天进入「即将到期」；到期或吊销后该人员不可在对应工序上岗",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        return bar;
    }

    // ---------------- 列定义 ----------------

    /// <summary>设置人员 / 资质 / 矩阵 / 班组 / 核查五张表的列定义。</summary>
    private void SetupPsColumns()
    {
        _psGrid.AutoGenerateColumns = false;
        _psGrid.Columns.Clear();
        _psGrid.Columns.Add(MakeTextCol("EmpNo", "工号", true, 9));
        _psGrid.Columns.Add(MakeTextCol("Name", "姓名", true, 9));
        _psGrid.Columns.Add(MakeTextCol("Dept", "部门", true, 9));
        _psGrid.Columns.Add(MakeTextCol("Position", "岗位", true, 15));
        _psGrid.Columns.Add(MakeTextCol("Crew", "班组", true, 8));
        _psGrid.Columns.Add(MakeTextCol("Status", "在职", true, 7));
        _psGrid.Columns.Add(MakeTextCol("HireText", "入职日期", true, 11));
        _psGrid.Columns.Add(MakeTextCol("Phone", "电话", true, 11));
        _psGrid.Columns.Add(MakeTextCol("CertText", "资质统计", true, 11));
        _psGrid.Columns.Add(MakeTextCol("CertHealth", "资质健康度", true, 10));

        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _psGrid.SelectionChanged -= PsGrid_SelectionChanged;
        _psGrid.SelectionChanged += PsGrid_SelectionChanged;
        _psGrid.CellFormatting -= PsGrid_CellFormatting;
        _psGrid.CellFormatting += PsGrid_CellFormatting;

        _psCertGrid.AutoGenerateColumns = false;
        _psCertGrid.Columns.Clear();
        _psCertGrid.Columns.Add(MakeTextCol("EmpNo", "工号", true, 9));
        _psCertGrid.Columns.Add(MakeTextCol("EmpName", "姓名", true, 9));
        _psCertGrid.Columns.Add(MakeTextCol("SkillItem", "资质项", true, 17));
        _psCertGrid.Columns.Add(MakeTextCol("Level", "等级", true, 7));
        _psCertGrid.Columns.Add(MakeTextCol("PeriodText", "有效期", true, 18));
        _psCertGrid.Columns.Add(MakeTextCol("DaysLeftText", "剩余", true, 11));
        _psCertGrid.Columns.Add(MakeTextCol("Status", "状态", true, 9));
        _psCertGrid.Columns.Add(MakeTextCol("CertNo", "证书编号", true, 12));
        _psCertGrid.Columns.Add(MakeTextCol("IssuedBy", "发证机构", true, 14));
        _psCertGrid.Columns.Add(MakeTextCol("RevokeReason", "吊销原因", true, 12));

        // 先解除再挂接着色事件（同上，防止重复着色）
        _psCertGrid.CellFormatting -= PsCertGrid_CellFormatting;
        _psCertGrid.CellFormatting += PsCertGrid_CellFormatting;

        _psMatrixGrid.AutoGenerateColumns = false;
        _psMatrixGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;   // 矩阵按单元格查看更适合
        _psMatrixGrid.Columns.Clear();

        _psCrewGrid.AutoGenerateColumns = false;
        _psCrewGrid.Columns.Clear();
        _psCrewGrid.Columns.Add(MakeTextCol("Crew", "班组", true, 10));
        _psCrewGrid.Columns.Add(MakeTextCol("Headcount", "人数", true, 8));
        _psCrewGrid.Columns.Add(MakeTextCol("CertifiedHeadcount", "持证人数", true, 10));
        _psCrewGrid.Columns.Add(MakeTextCol("CertRateText", "持证率", true, 10));
        _psCrewGrid.Columns.Add(MakeTextCol("ValidCertCount", "有效资质项", true, 11));
        _psCrewGrid.Columns.Add(MakeTextCol("ExpiringCertCount", "即将到期", true, 10));
        _psCrewGrid.Columns.Add(MakeTextCol("InvalidCertCount", "已失效", true, 9));
        _psCrewGrid.Columns.Add(MakeTextCol("CoverageText", "工序覆盖", true, 12));
        _psCrewGrid.Columns.Add(MakeTextCol("ProcessOpCount", "工序实绩", true, 10));
        _psCrewGrid.Columns.Add(MakeTextCol("UnqualifiedOpCount", "无证上岗嫌疑", true, 12));
        _psCrewGrid.Columns.Add(MakeTextCol("HealthText", "健康度", true, 11));
        _psCrewGrid.CellFormatting -= PsCrewGrid_CellFormatting;
        _psCrewGrid.CellFormatting += PsCrewGrid_CellFormatting;

        _psCheckGrid.AutoGenerateColumns = false;
        _psCheckGrid.Columns.Clear();
        _psCheckGrid.Columns.Add(MakeTextCol("ResultText", "结论", true, 7));
        _psCheckGrid.Columns.Add(MakeTextCol("TimeText", "时间", true, 9));
        _psCheckGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 15));
        _psCheckGrid.Columns.Add(MakeTextCol("WoNo", "工单", true, 9));
        _psCheckGrid.Columns.Add(MakeTextCol("ProcessName", "工序", true, 9));
        _psCheckGrid.Columns.Add(MakeTextCol("Equipment", "设备", true, 11));
        _psCheckGrid.Columns.Add(MakeTextCol("Operator", "操作人", true, 8));
        _psCheckGrid.Columns.Add(MakeTextCol("Shift", "班次", true, 7));
        _psCheckGrid.Columns.Add(MakeTextCol("RequiredSkill", "应持证", true, 13));
        _psCheckGrid.Columns.Add(MakeTextCol("CertLevelText", "持等级", true, 7));
        _psCheckGrid.Columns.Add(MakeTextCol("CertStatusText", "证状态", true, 8));
        _psCheckGrid.Columns.Add(MakeTextCol("Result", "判定说明", true, 40));
        _psCheckGrid.CellFormatting -= PsCheckGrid_CellFormatting;
        _psCheckGrid.CellFormatting += PsCheckGrid_CellFormatting;
        _psCheckGrid.CellDoubleClick -= PsCheckGrid_CellDoubleClick;
        _psCheckGrid.CellDoubleClick += PsCheckGrid_CellDoubleClick;
    }

    /// <summary>填充「单人校验」的两个下拉（在职人员 + 全部工序），在构造期一次性填好。</summary>
    private void SetupPsCheckControls()
    {
        foreach (var u in MesDatabase.Users.Where(u => u.IsActive).OrderBy(u => u.EmpNo, StringComparer.Ordinal))
            _cboPsCheckEmp.Items.Add(new PsEmpItem(u.EmpNo));
        if (_cboPsCheckEmp.Items.Count > 0) _cboPsCheckEmp.SelectedIndex = 0;

        // 工序词典合并两套命名，去重后按常见流转顺序排列
        var procs = ProcessSequence.Order
            .Concat(MesDatabase.DeviceProcessMap.Values)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var p in procs) _cboPsCheckProcess.Items.Add(p);
        if (_cboPsCheckProcess.Items.Count > 0) _cboPsCheckProcess.SelectedIndex = 0;

        if (_btnPsCheck is not null) _btnPsCheck.Enabled = _cboPsCheckEmp.Items.Count > 0 && _cboPsCheckProcess.Items.Count > 0;
    }

    // ---------------- 数据加载 ----------------

    private void PersonnelTab_Enter(object? sender, EventArgs e) => EnsurePersonnelReady();

    /// <summary>兜底初始化「人员与资质」：页签 Enter / 全局刷新时确保数据已加载（幂等）。</summary>
    private void EnsurePersonnelReady()
    {
        if (_psGrid is null) return;   // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_psGrid.Rows.Count == 0) DoQueryPersonnel();
    }

    /// <summary>加载人员与资质页：人员台账 / 资质明细 / 矩阵 / 班组绩效 / 实绩核查 + 汇总卡。</summary>
    private void DoQueryPersonnel()
    {
        if (_psGrid is null) return;   // 控件尚未创建（构造期）：直接返回

        var kw = _txtPsKeyword?.Text.Trim() ?? "";
        var dept = _cboPsDept is not null && _cboPsDept.SelectedIndex > 0 ? _cboPsDept.SelectedItem?.ToString() : null;
        var certStatus = _cboPsCertStatus is not null && _cboPsCertStatus.SelectedIndex > 0
            ? _cboPsCertStatus.SelectedItem?.ToString()
            : null;

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _psGrid.DataSource = null;
        var users = MesDatabase.QueryUsers(kw.Length == 0 ? null : kw, dept);
        // 资质状态是对「证书」的过滤：命中即保留该人员（人员行本身不带证书状态）
        if (certStatus is not null)
        {
            var hitEmpNos = MesDatabase.QueryCertificates(kw.Length == 0 ? null : kw, certStatus)
                .Select(c => c.EmpNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
            users = users.Where(u => hitEmpNos.Contains(u.EmpNo)).ToList();
        }
        _psGrid.DataSource = users;

        DoQueryPsCertOfSelected();
        DoQueryPsMatrix(dept);
        DoQueryPsCrew();
        DoQueryPsChecks();

        // 汇总卡
        var (onDuty, certified, expiring, invalid) = MesDatabase.GetPersonnelSummary();
        if (_lblPsOnDuty is not null) _lblPsOnDuty.Text = onDuty.ToString();
        if (_lblPsCertified is not null) _lblPsCertified.Text = certified.ToString();
        if (_lblPsExpiring is not null)
        {
            _lblPsExpiring.Text = expiring.ToString();
            _lblPsExpiring.ForeColor = expiring > 0 ? Color.FromArgb(255, 150, 80) : Color.White;
        }
        if (_lblPsInvalid is not null)
        {
            _lblPsInvalid.Text = invalid.ToString();
            _lblPsInvalid.ForeColor = invalid > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }

        // 选中首行（延后到此处，避免构造期 Rows 未物化时越界）
        if (_psGrid.Rows.Count > 0 && _psGrid.SelectedRows.Count == 0)
        {
            _psGrid.ClearSelection();
            _psGrid.Rows[0].Selected = true;
        }
        UpdatePsButtons();
    }

    /// <summary>加载选中人员的资质明细。</summary>
    private void DoQueryPsCertOfSelected()
    {
        if (_psCertGrid is null) return;
        var user = SelectedPsUser();
        _psCertGrid.DataSource = null;
        _psCertGrid.DataSource = user is null ? new List<SkillCertificate>() : MesDatabase.GetCertificates(user.EmpNo);
    }

    /// <summary>最近一次渲染的资质矩阵行缓存（供单元格着色回查，避免每次 CellFormatting 都重算全表）。</summary>
    private List<SkillMatrixRow>? _psMatrixCache;

    /// <summary>加载资质矩阵（手工建列：前 5 列为人员信息，其后每一资质项一列）。</summary>
    private void DoQueryPsMatrix(string? dept)
    {
        if (_psMatrixGrid is null) return;

        var (items, rows) = MesDatabase.GetSkillMatrix(dept);
        _psMatrixCache = rows;   // 缓存本次渲染的数据，供 CellFormatting 回查

        _psMatrixGrid.Columns.Clear();
        _psMatrixGrid.DataSource = null;
        _psMatrixGrid.Rows.Clear();

        _psMatrixGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "EmpNo", HeaderText = "工号", ReadOnly = true, FillWeight = 8 });
        _psMatrixGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "EmpName", HeaderText = "姓名", ReadOnly = true, FillWeight = 8 });
        _psMatrixGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Dept", HeaderText = "部门", ReadOnly = true, FillWeight = 8 });
        _psMatrixGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Position", HeaderText = "岗位", ReadOnly = true, FillWeight = 14 });
        _psMatrixGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CertTotal", HeaderText = "持证数", ReadOnly = true, FillWeight = 7 });
        foreach (var item in items)
            _psMatrixGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = item,
                HeaderText = item,
                ReadOnly = true,
                FillWeight = 9,
                ToolTipText = item
            });

        foreach (var row in rows)
        {
            var values = new List<object> { row.EmpNo, row.EmpName, row.Dept, row.Position, row.CertTotal };
            values.AddRange(row.Cells);
            _psMatrixGrid.Rows.Add(values.ToArray());
        }

        // 先解除再挂接：避免 Cells.Clear 后重复订阅导致回调被触发多次
        _psMatrixGrid.CellFormatting -= PsMatrixGrid_CellFormatting;
        _psMatrixGrid.CellFormatting += PsMatrixGrid_CellFormatting;
    }

    /// <summary>矩阵单元格着色：有效=绿、即将到期=橙、已过期/已吊销=红、无证=灰。</summary>
    private void PsMatrixGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 5) return;   // 前 5 列是人员信息，不着色
        if (_psMatrixCache is null || e.RowIndex >= _psMatrixCache.Count) return;

        var row = _psMatrixCache[e.RowIndex];
        var idx = e.ColumnIndex - 5;
        if (idx < 0 || idx >= row.CellStatuses.Length) return;

        var status = row.CellStatuses[idx];
        if (status.Length == 0) { e.CellStyle.ForeColor = Color.FromArgb(170, 170, 170); return; }

        e.CellStyle.ForeColor = status switch
        {
            CertStatus.Valid => Color.FromArgb(39, 174, 96),
            CertStatus.ExpiringSoon => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(192, 0, 0)
        };
        e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
    }

    /// <summary>加载班组绩效。</summary>
    private void DoQueryPsCrew()
    {
        if (_psCrewGrid is null) return;
        _psCrewGrid.DataSource = null;
        _psCrewGrid.DataSource = MesDatabase.GetCrewPerformance();
    }

    /// <summary>加载实绩资质核查列表。</summary>
    private void DoQueryPsChecks()
    {
        if (_psCheckGrid is null) return;
        var onlyFailed = _chkPsOnlyFailed is not null && _chkPsOnlyFailed.Checked;
        _psCheckGrid.DataSource = null;
        _psCheckGrid.DataSource = MesDatabase.GetQualificationChecks(null, onlyFailed);
    }

    // ---------------- 着色 ----------------

    /// <summary>人员台账着色：离职灰；资质健康度红/橙/灰。</summary>
    private void PsGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_psGrid.Rows[e.RowIndex].DataBoundItem is not UserRow u) return;
        var col = _psGrid.Columns[e.ColumnIndex].Name;

        if (col == "Status" && !u.IsActive)
            e.CellStyle.ForeColor = Color.FromArgb(150, 150, 150);
        if (col != "CertHealth") return;

        e.CellStyle.ForeColor = u.CertHealth switch
        {
            "未持证" or "存在无效资质" => Color.FromArgb(192, 0, 0),
            "即将到期" => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(39, 174, 96)
        };
    }

    /// <summary>资质明细着色：已吊销/已过期红；即将到期橙；有效绿。</summary>
    private void PsCertGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_psCertGrid.Rows[e.RowIndex].DataBoundItem is not SkillCertificate c) return;
        var col = _psCertGrid.Columns[e.ColumnIndex].Name;
        if (col != "Status" && col != "DaysLeftText") return;

        e.CellStyle.ForeColor = c.Status switch
        {
            CertStatus.Revoked or CertStatus.Expired => Color.FromArgb(192, 0, 0),
            CertStatus.ExpiringSoon => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(39, 174, 96)
        };
    }

    /// <summary>班组绩效着色：有无证上岗/失效资质红。</summary>
    private void PsCrewGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_psCrewGrid.Rows[e.RowIndex].DataBoundItem is not CrewPerformanceRow r) return;
        var col = _psCrewGrid.Columns[e.ColumnIndex].Name;

        if (col == "HealthText")
            e.CellStyle.ForeColor = r.HealthText == "正常" ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 0, 0);
        if (col == "UnqualifiedOpCount" && r.UnqualifiedOpCount > 0)
            e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
        if (col == "InvalidCertCount" && r.InvalidCertCount > 0)
            e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
    }

    /// <summary>核查列表着色：不通过整行浅红。</summary>
    private void PsCheckGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_psCheckGrid.Rows[e.RowIndex].DataBoundItem is not QualificationCheckRow r) return;
        if (r.Passed) return;
        if (_psCheckGrid.Columns[e.ColumnIndex].Name != "ResultText" && _psCheckGrid.Columns[e.ColumnIndex].Name != "Result") return;

        e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
        e.CellStyle.SelectionForeColor = Color.FromArgb(192, 0, 0);
    }

    // ---------------- 交互 ----------------

    private void PsGrid_SelectionChanged(object? sender, EventArgs e)
    {
        DoQueryPsCertOfSelected();   // 选中人员 → 联动刷新其资质明细
        UpdatePsButtons();
    }

    private void PsCheckGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_psCheckGrid.Rows[e.RowIndex].DataBoundItem is not QualificationCheckRow row) return;

        // 双击核查记录 → 用该记录的人/工序回填单人校验，便于复核结论
        if (_cboPsCheckEmp is not null)
            for (var i = 0; i < _cboPsCheckEmp.Items.Count; i++)
                if (((PsEmpItem)_cboPsCheckEmp.Items[i]!).EmpNo == row.Operator) { _cboPsCheckEmp.SelectedIndex = i; break; }
        if (_cboPsCheckProcess is not null)
            for (var i = 0; i < _cboPsCheckProcess.Items.Count; i++)
                if ((string)_cboPsCheckProcess.Items[i]! == row.ProcessName) { _cboPsCheckProcess.SelectedIndex = i; break; }
        RunPsCheck();
    }

    private void UpdatePsButtons()
    {
        var hasUser = SelectedPsUser() is not null;
        if (_btnPsEdit is not null) _btnPsEdit.Enabled = hasUser;
        if (_btnPsRevoke is not null) _btnPsRevoke.Enabled = SelectedPsCert() is not null;
    }

    private UserRow? SelectedPsUser()
    {
        if (_psGrid is null || _psGrid.SelectedRows.Count == 0) return null;
        return _psGrid.SelectedRows[0].DataBoundItem as UserRow;
    }

    private SkillCertificate? SelectedPsCert()
    {
        if (_psCertGrid is null || _psCertGrid.SelectedRows.Count == 0) return null;
        return _psCertGrid.SelectedRows[0].DataBoundItem as SkillCertificate;
    }

    /// <summary>登记新人员。</summary>
    private void BtnPsAddUser(object? sender, EventArgs e)
    {
        using var form = new PersonnelEditForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.AddUser(form.Profile);
        if (!ok) { Warn(msg); return; }
        MessageBox.Show(msg, "登记成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryPersonnel();
    }

    /// <summary>编辑选中人员。</summary>
    private void BtnPsEditUser(object? sender, EventArgs e)
    {
        var row = SelectedPsUser();
        if (row is null) { Warn("请先在人员台账中选择一名人员。"); return; }
        var user = MesDatabase.FindUser(row.EmpNo);
        if (user is null) { Warn("未找到该人员的原始记录。"); return; }

        using var form = new PersonnelEditForm(user);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.UpdateUser(form.Profile);
        if (!ok) { Warn(msg); return; }
        MessageBox.Show(msg, "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryPersonnel();
    }

    /// <summary>登记资质（默认对当前选中人员登记）。</summary>
    private void BtnPsAddCert(object? sender, EventArgs e)
    {
        var row = SelectedPsUser();
        var preset = row is null ? null : MesDatabase.FindUser(row.EmpNo);
        if (row is not null && preset is null) { Warn("未找到该人员的原始记录。"); return; }

        using var form = new CertificateEditForm(preset);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.AddCertificate(
            form.EmpNo, form.SkillItem, form.Level, form.IssueDate, form.ExpiryDate, form.IssuedBy);
        if (!ok) { Warn(msg); return; }
        MessageBox.Show(msg, "登记成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryPersonnel();
    }

    /// <summary>吊销选中资质（需确认）。</summary>
    private void BtnPsRevokeCert(object? sender, EventArgs e)
    {
        var cert = SelectedPsCert();
        if (cert is null) { Warn("请先在资质明细中选择一条资质。"); return; }

        var confirm = MessageBox.Show(
            $"确认吊销 {cert.EmpName}（{cert.EmpNo}）的「{cert.SkillItem}」？\n\n吊销后该人员在对应工序的实绩核查将转为「不通过」。",
            "吊销确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        var (ok, msg) = MesDatabase.RevokeCertificate(cert.EmpNo, cert.SkillItem, "不再具备资质，人工停用");
        if (!ok) { Warn(msg); return; }
        MessageBox.Show(msg, "已吊销", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DoQueryPersonnel();
    }

    /// <summary>单人上岗资质校验。</summary>
    private void BtnPsCheck(object? sender, EventArgs e) => RunPsCheck();

    /// <summary>执行单人上岗资质校验并把结论显示到结果区。</summary>
    internal void RunPsCheck()
    {
        if (_lblPsCheckResult is null) return;
        if (_cboPsCheckEmp is null || _cboPsCheckEmp.SelectedItem is null
            || _cboPsCheckProcess is null || _cboPsCheckProcess.SelectedItem is null)
        {
            _lblPsCheckResult.Text = "请先选择人员与工序。";
            _lblPsCheckResult.ForeColor = Color.Gray;
            return;
        }

        var empNo = ((PsEmpItem)_cboPsCheckEmp.SelectedItem).EmpNo;
        var process = (string)_cboPsCheckProcess.SelectedItem;
        var (ok, reason, skill, status) = MesDatabase.CheckQualification(empNo, process);

        _lblPsCheckResult.Text = $"【{(ok ? "允许上岗" : "禁止上岗")}】{reason}"
                                 + $"\n\n所需资质：{(skill.Length == 0 ? "（不设持证要求）" : skill)} ｜ 证书状态：{(status.Length == 0 ? "—" : status)}";
        _lblPsCheckResult.ForeColor = ok ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 0, 0);
    }

    /// <summary>导出人员与资质台账（含台账 / 资质 / 矩阵 / 班组 / 核查，5 个工作表）。</summary>
    private void BtnPsExport(object? sender, EventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出人员与资质台账",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"人员与资质台账_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var w = new XlsxWriter();
            BuildPersonnelWorkbook(w);
            var (sheets, rows) = w.Save(dlg.FileName);
            MessageBox.Show($"人员与资质台账已导出：\n{dlg.FileName}\n\n工作表 {sheets} 个 ｜ 数据行 {rows} 行",
                "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"导出失败：{ex.Message}");
        }
    }

    /// <summary>构造人员与资质导出工作簿（汇总 / 人员 / 资质 / 矩阵 / 班组 / 核查）。纯数据构造，便于自测。</summary>
    internal static void BuildPersonnelWorkbook(XlsxWriter w)
    {
        var (onDuty, certified, expiring, invalid) = MesDatabase.GetPersonnelSummary();

        var s0 = w.AddSheet("汇总");
        s0.SetColumns(("指标", 22), ("数值", 16));
        s0.AddRow("在册人数（人）", onDuty);
        s0.AddRow("持证人数（人）", certified);
        s0.AddRow("资质即将到期（项）", expiring);
        s0.AddRow("资质已失效（项）", invalid);
        var (checkTotal, checkPassed, checkFailed) = MesDatabase.GetQualificationCheckSummary();
        s0.AddRow("实绩核查总数（条）", checkTotal);
        s0.AddRow("核查通过（条）", checkPassed);
        s0.AddRow("核查不通过（条）", checkFailed);
        s0.AddRow("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        var s1 = w.AddSheet("人员台账");
        s1.SetColumns(("工号", 10), ("姓名", 10), ("部门", 10), ("岗位", 16), ("班组", 8),
            ("在职状态", 10), ("入职日期", 12), ("电话", 13), ("资质统计", 14), ("资质健康度", 12));
        foreach (var u in MesDatabase.QueryUsers())
            s1.AddRow(u.EmpNo, u.Name, u.Dept, u.Position, u.Crew, u.Status, u.HireText, u.Phone, u.CertText, u.CertHealth);

        var s2 = w.AddSheet("资质台账");
        s2.SetColumns(("工号", 10), ("姓名", 10), ("资质项", 18), ("等级", 8), ("发证日期", 12),
            ("有效期至", 12), ("剩余", 12), ("状态", 10), ("证书编号", 14), ("发证机构", 20), ("吊销原因", 14));
        foreach (var c in MesDatabase.QueryCertificates())
            s2.AddRow(c.EmpNo, c.EmpName, c.SkillItem, c.Level, c.IssueText, c.ExpiryText,
                c.DaysLeftText, c.Status, c.CertNo, c.IssuedBy, c.RevokeReason);

        var (items, matrix) = MesDatabase.GetSkillMatrix();
        var s3 = w.AddSheet("资质矩阵");
        var matrixCols = new List<(string Title, double Width)>
            { ("工号", 10), ("姓名", 10), ("部门", 10), ("岗位", 16), ("班组", 8) };
        matrixCols.AddRange(items.Select(i => (i, 16d)));
        s3.SetColumns(matrixCols.ToArray());
        foreach (var r in matrix)
        {
            var vals = new List<object> { r.EmpNo, r.EmpName, r.Dept, r.Position, r.Crew };
            vals.AddRange(r.Cells);
            s3.AddRow(vals.ToArray());
        }

        var s4 = w.AddSheet("班组绩效");
        s4.SetColumns(("班组", 10), ("人数", 8), ("持证人数", 10), ("持证率", 10), ("有效资质项", 12),
            ("即将到期", 10), ("已失效", 9), ("工序覆盖", 12), ("工序实绩", 10), ("无证上岗嫌疑", 12), ("健康度", 12));
        foreach (var r in MesDatabase.GetCrewPerformance())
            s4.AddRow(r.Crew, r.Headcount, r.CertifiedHeadcount, r.CertRateText, r.ValidCertCount,
                r.ExpiringCertCount, r.InvalidCertCount, r.CoverageText, r.ProcessOpCount, r.UnqualifiedOpCount, r.HealthText);

        var s5 = w.AddSheet("上岗资质核查");
        s5.SetColumns(("结论", 8), ("时间", 12), ("电芯SN", 18), ("工单", 10), ("工序", 10), ("设备", 12),
            ("操作人", 10), ("班次", 8), ("应持证", 16), ("持等级", 8), ("证状态", 10), ("判定说明", 60));
        foreach (var r in MesDatabase.GetQualificationChecks())
            s5.AddRow(r.ResultText, r.TimeText, r.CellSn, r.WoNo, r.ProcessName, r.Equipment, r.Operator,
                r.Shift, r.RequiredSkill, r.CertLevelText, r.CertStatusText, r.Result);
    }

    /// <summary>下拉项：显示「姓名（工号）」，绑定时保留工号。</summary>
    private sealed class PsEmpItem
    {
        public string EmpNo { get; }
        public PsEmpItem(string empNo) => EmpNo = empNo;
        public override string ToString()
        {
            var u = MesDatabase.FindUser(EmpNo);
            return u is null ? EmpNo : $"{u.Name}（{u.EmpNo}）";
        }
    }
}
