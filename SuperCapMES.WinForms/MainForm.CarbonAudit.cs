using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

// ============================================================================
// MainForm 分部类：碳核查支撑（阶段五）
//
// 页面：「能源与碳」下新增一页
//   ⑦ 碳核查支撑   核算台账（快照存档）+ 排放源清单 + 报告/清单导出
//
// 核查机构要看三样东西：排放源清单、数据台账、可追溯性。
// 所以本页的核心是**把一次核算结果存档成台账快照**，再基于快照出报告——
// 屏幕上算一遍就丢的数字，核查时不认。
// ============================================================================

public partial class MainForm
{
    private static readonly Color CaInk = Color.FromArgb(30, 41, 59);
    private static readonly Color CaGray = Color.FromArgb(110, 125, 145);
    private static readonly Color CaOk = Color.FromArgb(21, 128, 61);
    private static readonly Color CaWarn = Color.FromArgb(217, 119, 6);

    // （_gridAudit / _gridAuditItem 在 MainForm.cs 声明）
    private Label _lblAuditNote = null!;

    private TabPage BuildCarbonAuditTab()
    {
        var page = new TabPage("碳核查支撑");
        page.Padding = new Padding(4);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 76, Padding = new Padding(6, 6, 4, 0), WrapContents = true
        };

        var btnSave = MakeToolbarButton("存档当前核算", BtnSaveAudit);
        btnSave.Width = 128;
        bar.Controls.Add(btnSave);

        var btnReport = MakeToolbarButton("生成报告", BtnExportReport);
        btnReport.Width = 104;
        bar.Controls.Add(btnReport);

        var btnCsv = MakeToolbarButton("导出清单", BtnExportCsv);
        btnCsv.Width = 104;
        bar.Controls.Add(btnCsv);

        bar.Controls.Add(MakeToolbarButton("刷新", (_, _) => LoadAudits()));

        bar.Controls.Add(new Label
        {
            Text = "台账只追加不改写（历史核算不可被覆盖）；报告每项可追溯到 计量点 → 读数 → 因子编号 → 排放量",
            AutoSize = true, ForeColor = CaGray, Margin = new Padding(10, 8, 0, 0)
        });

        _lblAuditNote = new Label
        {
            Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 4, 8, 4),
            ForeColor = CaWarn, Font = new Font("微软雅黑", 8.5F),
            Text = CarbonReportDisclaimers.Header
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            SplitterDistance = 220, BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup(
            "核算台账（存档快照；选中行查看其排放源清单）", out _gridAudit));
        split.Panel2.Controls.Add(MakeGridGroup(
            "该台账的排放源清单（逐项：计量点 / 活动数据 / 因子编号 / 排放量）", out _gridAuditItem));

        page.Controls.Add(split);
        page.Controls.Add(bar);
        page.Controls.Add(_lblAuditNote);

        SetupAuditColumns();
        WireAuditSelection();
        page.Enter -= CarbonAuditTab_Enter;
        page.Enter += CarbonAuditTab_Enter;
        LoadAudits();
        return page;
    }

    private void SetupAuditColumns()
    {
        _gridAudit.AutoGenerateColumns = false;
        _gridAudit.Columns.Clear();
        _gridAudit.Columns.Add(MakeTextCol("AuditNo", "台账号", true, 20));
        _gridAudit.Columns.Add(MakeTextCol("BoundaryText", "核算边界", true, 20));
        _gridAudit.Columns.Add(MakeTextCol("PeriodText", "核算期间", true, 17));
        _gridAudit.Columns.Add(MakeTextCol("TotalTText", "总排放量", true, 15));
        _gridAudit.Columns.Add(MakeTextCol("ItemCountText", "排放源", true, 9));
        _gridAudit.Columns.Add(MakeTextCol("ArchivedBy", "存档人", true, 10));
        _gridAudit.Columns.Add(MakeTextCol("ArchivedAtText", "存档时间", true, 17));
        _gridAudit.Columns.Add(MakeTextCol("Note", "存档说明", true, 34));

        _gridAuditItem.AutoGenerateColumns = false;
        _gridAuditItem.Columns.Clear();
        _gridAuditItem.Columns.Add(MakeTextCol("ScopeText", "范围", true, 22));
        _gridAuditItem.Columns.Add(MakeTextCol("MeterId", "计量点", true, 17));
        _gridAuditItem.Columns.Add(MakeTextCol("MeterName", "位置/设备", true, 20));
        _gridAuditItem.Columns.Add(MakeTextCol("TypeName", "能源", true, 8));
        _gridAuditItem.Columns.Add(MakeTextCol("ActivityText", "活动数据", true, 14));
        _gridAuditItem.Columns.Add(MakeTextCol("FactorId", "因子编号", true, 16));
        _gridAuditItem.Columns.Add(MakeTextCol("FactorText", "因子值", true, 17));
        _gridAuditItem.Columns.Add(MakeTextCol("FactorSource", "因子来源", true, 32));
        _gridAuditItem.Columns.Add(MakeTextCol("EmissionKgText", "排放量", true, 15));
        _gridAuditItem.Columns.Add(MakeTextCol("PeriodCountText", "账期数", true, 9));
    }

    private void WireAuditSelection()
    {
        void OnSel(object? s, EventArgs e) => LoadAuditItems(_gridAudit.CurrentRow?.DataBoundItem as CarbonAuditTrail);
        _gridAudit.SelectionChanged -= OnSel;
        _gridAudit.SelectionChanged += OnSel;
        LoadAuditItems(_gridAudit.CurrentRow?.DataBoundItem as CarbonAuditTrail);
    }

    private void LoadAuditItems(CarbonAuditTrail? audit)
    {
        if (_gridAuditItem is null) return;
        _gridAuditItem.DataSource = null;
        _gridAuditItem.DataSource = audit?.Snapshot.Items ?? new List<CarbonSourceItem>();

        if (_lblAuditNote is null) return;
        if (audit is null)
        {
            _lblAuditNote.Text = CarbonReportDisclaimers.Header;
            return;
        }
        var noFactor = audit.Snapshot.Items.Count(i => i.FactorId.Length == 0);
        _lblAuditNote.Text = $"台账 {audit.AuditNo}　边界 {audit.BoundaryText}　期间 {audit.PeriodText}"
            + $"　共 {audit.ItemCount} 项排放源"
            + (noFactor > 0 ? $"　⚠ {noFactor} 项无可用因子未计入总量" : "")
            + $"　{audit.Disclaimer}";
        _lblAuditNote.ForeColor = noFactor > 0 ? CaWarn : CaGray;
    }

    private void LoadAudits()
    {
        if (_gridAudit is null) return;
        var list = MesDatabase.GetCarbonAudits();
        _gridAudit.DataSource = null;
        _gridAudit.DataSource = list;
        if (_lblAuditNote is not null && list.Count == 0)
            _lblAuditNote.Text = "台账为空。先在「碳排放核算」页算一次，再回来点「存档当前核算」。"
                + "　" + CarbonReportDisclaimers.Header;
    }

    private void CarbonAuditTab_Enter(object? sender, EventArgs e)
    {
        if (_gridAudit is not null && _gridAudit.Rows.Count == 0) LoadAudits();
    }

    /// <summary>当前选中的台账。</summary>
    private CarbonAuditTrail? SelectedAudit() => _gridAudit?.CurrentRow?.DataBoundItem as CarbonAuditTrail;

    /// <summary>点「存档当前核算」：按当前核算页的选择重新算一次并存档。</summary>
    private void BtnSaveAudit(object? sender, EventArgs e)
    {
        // 复用核算页当前的选择，避免用户重复填一遍
        var sel = _cboCarbonBoundary?.SelectedItem;
        var boundary = EnergyScopes.Plant;
        var value = "";
        var text = sel?.ToString() ?? "";
        if (text.Contains("线体"))
        {
            boundary = EnergyScopes.Line;
            value = text.Split(' ').LastOrDefault() ?? "";
        }
        else if (text.Contains("设备"))
        {
            boundary = EnergyScopes.Device;
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            value = parts.Length >= 2 ? parts[1] : "";
        }
        var from = _cboCarbonPeriodFrom?.SelectedItem?.ToString() is "全部" or null ? null : _cboCarbonPeriodFrom!.SelectedItem!.ToString();
        var to = _cboCarbonPeriodTo?.SelectedItem?.ToString() is "全部" or null ? null : _cboCarbonPeriodTo!.SelectedItem!.ToString();

        var inv = MesDatabase.BuildCarbonInventory(boundary, value, from, to);
        if (inv.Items.Count == 0)
        {
            MessageBox.Show("当前条件下没有可存档的核算结果（无排放源清单）。", "无法存档",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var (ok, msg, no) = MesDatabase.SaveCarbonAudit(inv,
            $"由碳核查支撑页存档（边界 {inv.BoundaryText}，期间 {inv.PeriodText}）", CurrentWmsOperator);
        if (!ok) { MessageBox.Show(msg, "存档失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        MessageBox.Show(msg, "已存档", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadAudits();
    }

    /// <summary>点「生成报告」：导出 Markdown 报告。</summary>
    private void BtnExportReport(object? sender, EventArgs e)
    {
        var audit = SelectedAudit();
        if (audit is null) { CaWarnBox("请先在台账列表选中一条记录。"); return; }

        using var meta = new CarbonReportForm(audit);
        if (meta.ShowDialog(this) != DialogResult.OK) return;

        using var dlg = new SaveFileDialog
        {
            Title = "导出碳核查报告",
            Filter = "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
            FileName = MesDatabase.DefaultReportFileName(audit)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dlg.FileName, MesDatabase.BuildCarbonReport(audit, meta.Meta),
                new System.Text.UTF8Encoding(true));
            var size = new FileInfo(dlg.FileName).Length;
            MessageBox.Show($"已导出报告到 {dlg.FileName}（{size:N0} 字节）。", "已导出",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>点「导出清单」：导出 CSV 排放源清单。</summary>
    private void BtnExportCsv(object? sender, EventArgs e)
    {
        var audit = SelectedAudit();
        if (audit is null) { CaWarnBox("请先在台账列表选中一条记录。"); return; }

        using var dlg = new SaveFileDialog
        {
            Title = "导出排放源清单",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            FileName = $"排放源清单_{audit.AuditNo}.csv"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.ExportCarbonAuditCsv(audit, dlg.FileName);
        if (ok) MessageBox.Show(msg, "已导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else MessageBox.Show(msg, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void CaWarnBox(string m) =>
        MessageBox.Show(m, "请先选择", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}