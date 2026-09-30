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
/// MainForm 分部类（按功能域拆分，第 15 部分）：SPC 分析页与 SPC 项目管理页（控制图与能力 / 参数预警 / 规格限版本化）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Spc」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= SPC 分析 =================

    /// <summary>SPC 分析页：含「控制图与能力」「参数预警」两个子页。</summary>
    private TabPage BuildSpcTab()
    {
        var page = new TabPage("SPC 分析");
        _spcSubTabs = new TabControl { Dock = DockStyle.Fill };
        _spcSubTabs.TabPages.Add(BuildSpcChartTab());
        _spcSubTabs.TabPages.Add(BuildSpcAlertTab());
        page.Controls.Add(_spcSubTabs);
        return page;
    }

    // ================= SPC 项目管理（顶层第 13 个页签） =================

    /// <summary>
    /// SPC 项目管理页：上部项目列表（含当前版本 / 当前规格 / 下一版本），下部选中项目的规格版本列表，
    /// 支持「新建版本 / 修改待生效版本 / 删除待生效版本 / 刷新」。已生效版本不可改删（MES 可追溯）。
    /// </summary>

    // ---------- 子页1：控制图与能力 ----------
    /// <summary>构建「SpcChart」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildSpcChartTab()
    {
        var page = new TabPage("控制图与能力");
        page.Padding = new Padding(6);

        // ---- 顶部工具栏 ----
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(6, 9, 4, 0),
            WrapContents = false
        };
        toolbar.Controls.Add(new Label { Text = "监控项：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboSpcItem = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 196,
            Margin = new Padding(4, 4, 0, 0),
            DisplayMember = "DisplayText",
            ValueMember = "ItemId"
        };
        toolbar.Controls.Add(_cboSpcItem);

        toolbar.Controls.Add(new Label { Text = "设备：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboSpcEquipment = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
            Margin = new Padding(4, 4, 0, 0)
        };
        toolbar.Controls.Add(_cboSpcEquipment);

        toolbar.Controls.Add(new Label { Text = "时间范围：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboSpcRange = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 104,
            Margin = new Padding(4, 4, 0, 0)
        };
        _cboSpcRange.Items.AddRange(new object[] { "近 7 天", "近 30 天", "全部" });
        _cboSpcRange.SelectedIndex = 2;
        toolbar.Controls.Add(_cboSpcRange);

        toolbar.Controls.Add(MakeToolbarButton("刷新", (_, _) => LoadSpcChart()));
        toolbar.Controls.Add(MakeToolbarButton("生成模拟样本(30 点)", BtnSpcAppend));
        toolbar.Controls.Add(new Label
        {
            Text = "控制限口径：均值 ± 3×样本标准差(n-1)",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(16, 11, 0, 0)
        });

        // ---- 控制图 ----
        _spcChart = new SpcControlChart { Dock = DockStyle.Fill };
        var chartGroup = new GroupBox
        {
            Text = "控制图（规格限 USL/LSL：红/蓝点线；CL：绿实线；UCL/LCL：橙虚线；超限/超规格点：红色实心；判异命中点：红圈+序号）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        chartGroup.Controls.Add(_spcChart);

        // ---- 能力指标卡 ----
        var capPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            Padding = new Padding(6, 4, 6, 4),
            BackColor = Color.FromArgb(245, 248, 252)
        };
        var capGroup = new GroupBox
        {
            Text = "过程能力指标（Cpk 分级：≥1.33 良好 / 1.00~1.33 需关注 / <1.00 不足）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        capGroup.Controls.Add(capPanel);

        // ---- 样本明细 ----
        var sampleGroup = MakeGridGroup("样本明细（判定：正常 / 超控制限 / 超规格）", out _spcSampleGrid);
        SetupSpcSampleColumns();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 162));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.Controls.Add(chartGroup, 0, 0);
        root.Controls.Add(capGroup, 0, 1);
        root.Controls.Add(sampleGroup, 0, 2);

        // ---- 版本信息行（当前生效版本 + 规格 + 待生效版本）----
        var versionBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Color.FromArgb(245, 248, 252)
        };
        _lblSpcVersionInfo = new Label
        {
            Text = "版本信息：—",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Padding = new Padding(8, 0, 0, 0)
        };
        versionBar.Controls.Add(_lblSpcVersionInfo);

        page.Controls.Add(root);         // Fill
        page.Controls.Add(versionBar);   // Top（位于工具栏下方）
        page.Controls.Add(toolbar);      // Top（最外层）

        BuildSpcCapabilityCards(capPanel);

        // 数据源最后设置（避免过早触发事件）。
        // 注意：此时 TabPage 尚未挂载到顶层控件树，DataSource 绑定未物化 → ComboBox.Items.Count==0，
        // 故用控件计数守卫（与本文件既有惯例一致），实际的首次加载延迟到本页 Enter 时执行。
        _cboSpcItem.DataSource = MesDatabase.SpcItems;
        if (_cboSpcItem.Items.Count > 0) _cboSpcItem.SelectedIndex = 0;
        _cboSpcItem.SelectedIndexChanged -= SpcItem_Changed;
        _cboSpcItem.SelectedIndexChanged += SpcItem_Changed;
        _cboSpcRange.SelectedIndexChanged -= SpcRange_Changed;
        _cboSpcRange.SelectedIndexChanged += SpcRange_Changed;
        _cboSpcEquipment.SelectedIndexChanged -= SpcEquipment_Changed;
        _cboSpcEquipment.SelectedIndexChanged += SpcEquipment_Changed;

        page.Enter += (_, _) => EnsureSpcChartReady();
        EnsureSpcChartReady();
        return page;
    }

    /// <summary>确保控制图页就绪：数据绑定物化后刷新监控项下拉（含版本号）、补建设备下拉并刷新图表。</summary>
    private void EnsureSpcChartReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_cboSpcItem is null || _spcChart is null) return;
        SyncSpcItemCombo();
        if (_cboSpcEquipment is not null && _cboSpcEquipment.Items.Count == 0)
            ReloadSpcEquipmentOptions();
        LoadSpcChart();
    }

    /// <summary>
    /// 重建监控项下拉的数据绑定，使「项目名（当前版本号）」的展示随版本变化刷新。
    /// 注意：本页 TabPage 未挂载时 DataSource 绑定不物化 → Items.Count==0，故选择用控件计数守卫。
    /// </summary>
    private void SyncSpcItemCombo()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_cboSpcItem is null) return;
        var prev = SelectedSpcItem()?.ItemId;

        _spcUpdating = true;
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _cboSpcItem.DataSource = null;
        // 重设绑定字段（DataSource 被置空后 DisplayMember 可能被重置），展示「项目名（当前版本号）」
        _cboSpcItem.DisplayMember = "DisplayText";
        _cboSpcItem.ValueMember = "ItemId";
        _cboSpcItem.DataSource = MesDatabase.SpcItems;
        var idx = 0;
        if (prev is not null)
        {
            var found = MesDatabase.SpcItems.FindIndex(i => i.ItemId == prev);
            if (found >= 0) idx = found;
        }
        if (_cboSpcItem.Items.Count > 0)
            _cboSpcItem.SelectedIndex = Math.Min(idx, _cboSpcItem.Items.Count - 1);
        _spcUpdating = false;
    }

    /// <summary>构建「SpcCapabilityCards」界面区块。</summary>
    private void BuildSpcCapabilityCards(FlowLayoutPanel panel)
    {
        panel.Controls.Add(MakeSpcMetric("当前版本", out _lblSpcCapVersion));
        panel.Controls.Add(MakeSpcMetric("N 样本数", out _lblSpcCapN));
        panel.Controls.Add(MakeSpcMetric("均值 CL", out _lblSpcCapMean));
        panel.Controls.Add(MakeSpcMetric("标准差 σ", out _lblSpcCapSigma));
        panel.Controls.Add(MakeSpcMetric("Cp", out _lblSpcCapCp));
        panel.Controls.Add(MakeSpcMetric("Cpk", out _lblSpcCapCpk));
        panel.Controls.Add(MakeSpcMetric("能力判定", out _lblSpcCapGrade));
        panel.Controls.Add(MakeSpcMetric("超规格数", out _lblSpcCapOut));
        panel.Controls.Add(MakeSpcMetric("USL 规格上限", out _lblSpcCapUsl));
        panel.Controls.Add(MakeSpcMetric("LSL 规格下限", out _lblSpcCapLsl));
    }

    /// <summary>创建「SpcMetric」控件/样式（统一样式的工厂方法）。</summary>
    private static Panel MakeSpcMetric(string title, out Label value)
    {
        var p = new Panel
        {
            Size = new Size(140, 64),
            Margin = new Padding(5, 4, 5, 4),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        p.Controls.Add(new Label
        {
            Text = title,
            Location = new Point(10, 6),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("微软雅黑", 8.5F)
        });
        value = new Label
        {
            Text = "—",
            Location = new Point(10, 26),
            AutoSize = true,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 13F, FontStyle.Bold)
        };
        p.Controls.Add(value);
        return p;
    }

    /// <summary>设置「SpcSampleColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupSpcSampleColumns()
    {
        _spcSampleGrid.AutoGenerateColumns = false;
        _spcSampleGrid.Columns.Clear();
        _spcSampleGrid.Columns.Add(MakeTextCol("Seq", "序号", true, 7));
        var timeCol = MakeTextCol("SampleTime", "样本时间", true, 16);
        timeCol.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _spcSampleGrid.Columns.Add(timeCol);
        var valCol = MakeTextCol("Value", "数值", true, 11);
        valCol.DefaultCellStyle.Format = "0.###";
        _spcSampleGrid.Columns.Add(valCol);
        _spcSampleGrid.Columns.Add(MakeTextCol("Equipment", "设备", true, 14));
        _spcSampleGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 20));
        _spcSampleGrid.Columns.Add(MakeTextCol("Source", "来源", true, 8));
        var verCol = new DataGridViewTextBoxColumn
        {
            Name = "Version",
            DataPropertyName = "VersionText",
            HeaderText = "版本",
            ReadOnly = true,
            FillWeight = 8
        };
        _spcSampleGrid.Columns.Add(verCol);
        _spcSampleGrid.Columns.Add(MakeTextCol("Judge", "判定", true, 14));

        // 先解除再挂接着色事件（同上，防止重复着色）
        _spcSampleGrid.CellFormatting -= SpcSampleGrid_CellFormatting;
        _spcSampleGrid.CellFormatting += SpcSampleGrid_CellFormatting;
    }

    /// <summary>「SpcSampleGrid」网格单元格着色：按状态/结果区分颜色（差异红、异常橙、正常绿等）。</summary>
    private void SpcSampleGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        if (grid.Columns[e.ColumnIndex].Name != "Judge") return;

        var v = e.Value?.ToString();
        e.CellStyle.ForeColor = v switch
        {
            "超规格" => Color.FromArgb(192, 0, 0),
            "超控制限+超规格" => Color.FromArgb(192, 0, 0),
            "超控制限" => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(39, 174, 96)
        };
        if (v is "超规格" or "超控制限+超规格")
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
    }

    /// <summary>在网格中选中「edSpcItem」对应的行（联动加载明细）。</summary>
    private SpcItem? SelectedSpcItem() => _cboSpcItem?.SelectedItem as SpcItem;

    /// <summary>SpcItem_Changed：（说明待补充）</summary>
    private void SpcItem_Changed(object? sender, EventArgs e)
    {
        if (_spcUpdating) return;
        ReloadSpcEquipmentOptions();
        LoadSpcChart();
    }

    /// <summary>SpcRange_Changed：（说明待补充）</summary>
    private void SpcRange_Changed(object? sender, EventArgs e)
    {
        if (_spcUpdating) return;
        LoadSpcChart();
    }

    /// <summary>SpcEquipment_Changed：（说明待补充）</summary>
    private void SpcEquipment_Changed(object? sender, EventArgs e)
    {
        if (_spcUpdating) return;
        LoadSpcChart();
    }

    /// <summary>按当前监控项重建设备下拉（"全部" + 该项样本出现过的设备）。</summary>
    private void ReloadSpcEquipmentOptions()
    {
        var item = SelectedSpcItem();
        var options = new List<string> { "全部" };
        if (item is not null)
        {
            foreach (var s in MesDatabase.GetSpcSamples(item.ItemId, null, null, null))
            {
                if (!string.IsNullOrWhiteSpace(s.Equipment) && !options.Contains(s.Equipment))
                    options.Add(s.Equipment);
            }
        }

        _spcUpdating = true;
        _cboSpcEquipment.Items.Clear();
        _cboSpcEquipment.Items.AddRange(options.ToArray());
        _cboSpcEquipment.SelectedIndex = 0;
        _spcUpdating = false;
    }

    private (DateTime? From, DateTime? To) SelectedSpcRange()
    {
        var sel = _cboSpcRange.SelectedItem?.ToString();
        return sel switch
        {
            "近 7 天" => (DateTime.Today.AddDays(-7), (DateTime?)null),
            "近 30 天" => (DateTime.Today.AddDays(-30), (DateTime?)null),
            _ => (null, null)
        };
    }

    /// <summary>刷新控制图、能力指标卡、版本信息行与样本明细（按生效版本分段判定）。</summary>
    private void LoadSpcChart()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_spcChart is null || _spcSampleGrid is null) return;
        var item = SelectedSpcItem();
        if (item is null) return;

        var (from, to) = SelectedSpcRange();
        var equipment = _cboSpcEquipment.SelectedItem?.ToString();
        var samples = MesDatabase.GetSpcSamples(item.ItemId, equipment, from, to);
        var versions = MesDatabase.GetSpcItemVersions(item.ItemId);
        var res = SpcCalculator.ComputeWithVersions(samples, versions, DateTime.Now);

        _spcChart.SetData(item, samples, res);
        UpdateSpcCapability(item, res);
        UpdateSpcVersionInfo(item, res);

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _spcSampleGrid.DataSource = null;
        _spcSampleGrid.DataSource = BuildSpcSampleRows(samples, res, versions);
    }

    /// <summary>刷新控制图上方版本信息行：当前生效版本 + 规格，以及待生效版本提示。</summary>
    private void UpdateSpcVersionInfo(SpcItem item, SpcResult res)
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_lblSpcVersionInfo is null) return;

        var currentVer = MesDatabase.GetCurrentSpcVersion(item.ItemId);
        if (currentVer is null)
        {
            _lblSpcVersionInfo.Text = $"{item.ItemName}：未配置规格版本";
            return;
        }

        var spec = res.CurrentVersion == currentVer.Version
            ? $"  规格 {SpecTextOf(res.CurrentUsl, res.CurrentLsl)}"
            : "";
        var text = $"当前版本 {currentVer.VersionText}（生效 {currentVer.EffectiveFrom:yyyy-MM-dd HH:mm}）{spec}";

        var pending = MesDatabase.GetPendingSpcVersion(item.ItemId);
        if (pending is not null)
            text += $"    ｜ 下一版本 {pending.VersionText} 将于 {pending.EffectiveFrom:yyyy-MM-dd HH:mm} 生效";

        _lblSpcVersionInfo.Text = text;
    }

    /// <summary>规格文本：双边 "a ~ b"，单边 "≥ a" / "≤ b"，均无则 "—"。</summary>
    private static string SpecTextOf(double usl, double lsl)
    {
        var hasUsl = double.IsFinite(usl);
        var hasLsl = double.IsFinite(lsl);
        if (hasUsl && hasLsl) return $"{lsl:F3} ~ {usl:F3}";
        if (hasUsl) return $"≤ {usl:F3}";
        if (hasLsl) return $"≥ {lsl:F3}";
        return "—";
    }

    /// <summary>根据当前选中项更新「SpcCapability」的可用状态/提示。</summary>
    private void UpdateSpcCapability(SpcItem item, SpcResult res)
    {
        _lblSpcCapVersion.Text = res.CurrentVersionText.Length > 0 ? res.CurrentVersionText : "—";
        _lblSpcCapN.Text = res.N.ToString();
        _lblSpcCapMean.Text = SpcFmt(res.Mean);
        _lblSpcCapSigma.Text = SpcFmt(res.Sigma);
        _lblSpcCapCp.Text = SpcFmt(res.Cp);
        _lblSpcCapCpk.Text = SpcFmt(res.Cpk);
        _lblSpcCapGrade.Text = res.Capability;
        _lblSpcCapOut.Text = res.OutOfSpecCount.ToString();
        _lblSpcCapUsl.Text = double.IsFinite(res.CurrentUsl) ? res.CurrentUsl.ToString("F3") : "—";
        _lblSpcCapLsl.Text = double.IsFinite(res.CurrentLsl) ? res.CurrentLsl.ToString("F3") : "—";

        var color = res.Capability switch
        {
            SpcCapability.Good => Color.FromArgb(39, 174, 96),
            SpcCapability.Watch => Color.FromArgb(230, 126, 34),
            SpcCapability.Poor => Color.FromArgb(192, 0, 0),
            _ => Color.FromArgb(130, 138, 150)
        };
        _lblSpcCapCpk.ForeColor = color;
        _lblSpcCapGrade.ForeColor = color;
    }

    /// <summary>SpcFmt：（说明待补充）</summary>
    private static string SpcFmt(double v) => double.IsFinite(v) ? v.ToString("F3") : "—";

    /// <summary>样本明细展示行（含判定结果与生效版本，供网格绑定）。</summary>
    private sealed class SpcSampleRow
    {
        public int Seq { get; init; }
        public DateTime SampleTime { get; init; }
        public double Value { get; init; }
        public string Equipment { get; init; } = "";
        public string CellSn { get; init; } = "";
        public string Source { get; init; } = "";
        public string VersionText { get; init; } = "—";
        public string Judge { get; init; } = "";
    }

    /// <summary>List：（说明待补充）</summary>
    private static List<SpcSampleRow> BuildSpcSampleRows(
        IReadOnlyList<SpcSample> samples, SpcResult res, IReadOnlyList<SpcItemVersion> versions)
    {
        var rows = new List<SpcSampleRow>(samples.Count);
        foreach (var s in samples)
        {
            // 逐点按采样时间选择生效版本（分段判定）
            var ver = SpcCalculator.GetVersionAt(versions, s.SampleTime);
            var hasUsl = ver is not null && ver.HasUsl;
            var hasLsl = ver is not null && ver.HasLsl;
            var v = s.Value;

            var oc = double.IsFinite(res.Ucl) && (v > res.Ucl || v < res.Lcl);
            var os = (hasUsl && v > ver!.Usl) || (hasLsl && v < ver!.Lsl);
            var judge = oc && os ? "超控制限+超规格" : os ? "超规格" : oc ? "超控制限" : "正常";

            rows.Add(new SpcSampleRow
            {
                Seq = s.Seq,
                SampleTime = s.SampleTime,
                Value = v,
                Equipment = s.Equipment,
                CellSn = s.CellSn,
                Source = s.Source,
                VersionText = ver?.VersionText ?? "—",
                Judge = judge
            });
        }
        return rows;
    }

    /// <summary>「生成模拟样本」：追加 30 个模拟样本点后刷新。</summary>
    private void BtnSpcAppend(object? sender, EventArgs e)
    {
        if (_spcUpdating) return;
        var item = SelectedSpcItem();
        if (item is null)
        {
            Warn("请先选择监控项。");
            return;
        }

        var n = MesDatabase.AppendSpcSamples(item.ItemId, 30);
        LoadSpcChart();
        MessageBox.Show(
            $"已为「{item.ItemName}」追加 {n} 个模拟样本点（来源=模拟，采样时间顺延至当前）。\n"
            + "如需同步产生预警，请切换到「参数预警」页点击「重算并生成预警」。",
            "生成模拟样本", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------- 子页2：参数预警 ----------
    /// <summary>构建「SpcAlert」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildSpcAlertTab()
    {
        var page = new TabPage("参数预警");
        page.Padding = new Padding(6);

        var gridGroup = MakeGridGroup("预警列表（选中一行 → 底部处置；状态着色：待处理=橙 / 已确认=蓝 / 已关闭=绿）", out _spcAlertGrid);
        SetupSpcAlertColumns();
        // 先解除再挂接：避免重复订阅导致回调被触发多次
        _spcAlertGrid.SelectionChanged -= SpcAlertGrid_SelectionChanged;
        _spcAlertGrid.SelectionChanged += SpcAlertGrid_SelectionChanged;

        page.Controls.Add(gridGroup);                   // Fill
        page.Controls.Add(BuildSpcAlertActionBar());    // Bottom
        page.Controls.Add(BuildSpcAlertQueryRow2());    // Top2
        page.Controls.Add(BuildSpcAlertQueryRow1());    // Top1

        DoQuerySpcAlerts();
        page.Enter += (_, _) => { ReloadSpcAlertItemOptions(); DoQuerySpcAlerts(); };
        return page;
    }

    /// <summary>重建预警页监控项下拉（展示「项目名（当前版本号）」），并尽量保留原选择。</summary>
    private void ReloadSpcAlertItemOptions()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_cboSpcAlertItem is null) return;
        var prev = _cboSpcAlertItem.SelectedItem?.ToString();
        _cboSpcAlertItem.Items.Clear();
        _cboSpcAlertItem.Items.Add("全部");
        foreach (var it in MesDatabase.SpcItems) _cboSpcAlertItem.Items.Add(it.DisplayText);
        var idx = 0;
        if (!string.IsNullOrEmpty(prev))
        {
            var found = _cboSpcAlertItem.Items.IndexOf(prev);
            if (found >= 0) idx = found;
        }
        _cboSpcAlertItem.SelectedIndex = idx;
    }

    /// <summary>构建「SpcAlertQueryRow1」界面区块。</summary>
    private Control BuildSpcAlertQueryRow1()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(6, 7, 4, 0),
            WrapContents = false
        };

        bar.Controls.Add(new Label { Text = "监控项：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboSpcAlertItem = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new Padding(4, 4, 0, 0) };
        _cboSpcAlertItem.Items.Add("全部");
        foreach (var it in MesDatabase.SpcItems) _cboSpcAlertItem.Items.Add(it.DisplayText);
        _cboSpcAlertItem.SelectedIndex = 0;
        bar.Controls.Add(_cboSpcAlertItem);

        bar.Controls.Add(new Label { Text = "预警类型：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboSpcAlertType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(4, 4, 0, 0) };
        _cboSpcAlertType.Items.Add("全部");
        _cboSpcAlertType.Items.AddRange(SpcAlertType.All);
        _cboSpcAlertType.SelectedIndex = 0;
        bar.Controls.Add(_cboSpcAlertType);

        bar.Controls.Add(new Label { Text = "严重度：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboSpcAlertSeverity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80, Margin = new Padding(4, 4, 0, 0) };
        _cboSpcAlertSeverity.Items.Add("全部");
        _cboSpcAlertSeverity.Items.AddRange(SpcSeverity.All);
        _cboSpcAlertSeverity.SelectedIndex = 0;
        bar.Controls.Add(_cboSpcAlertSeverity);

        bar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
        _cboSpcAlertStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(4, 4, 0, 0) };
        _cboSpcAlertStatus.Items.Add("全部");
        _cboSpcAlertStatus.Items.AddRange(SpcAlertStatus.All);
        _cboSpcAlertStatus.SelectedIndex = 0;
        bar.Controls.Add(_cboSpcAlertStatus);
        return bar;
    }

    /// <summary>构建「SpcAlertQueryRow2」界面区块。</summary>
    private Control BuildSpcAlertQueryRow2()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(6, 4, 4, 0),
            WrapContents = false
        };

        _chkSpcAlertUseTime = new CheckBox
        {
            Text = "按样本时间",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        _chkSpcAlertUseTime.CheckedChanged += (_, _) =>
        {
            var enabled = _chkSpcAlertUseTime.Checked;
            _dtSpcAlertFrom.Enabled = enabled;
            _dtSpcAlertTo.Enabled = enabled;
        };
        bar.Controls.Add(_chkSpcAlertUseTime);

        _dtSpcAlertFrom = MakeHoldDatePicker();
        _dtSpcAlertFrom.Value = DateTime.Today.AddDays(-30);
        bar.Controls.Add(_dtSpcAlertFrom);
        bar.Controls.Add(new Label { Text = " ~ ", AutoSize = true, Margin = new Padding(2, 8, 0, 0) });
        _dtSpcAlertTo = MakeHoldDatePicker();
        _dtSpcAlertTo.Value = DateTime.Today;
        bar.Controls.Add(_dtSpcAlertTo);

        bar.Controls.Add(MakeToolbarButton("查询", (_, _) => DoQuerySpcAlerts()));
        bar.Controls.Add(MakeToolbarButton("重置", BtnResetSpcAlertQuery));

        _lblSpcAlertResult = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 8, 0, 0) };
        bar.Controls.Add(_lblSpcAlertResult);
        return bar;
    }

    /// <summary>构建「SpcAlert」操作按钮区（按钮 + 状态提示）。</summary>
    private Control BuildSpcAlertActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8, 6, 4, 0),
            WrapContents = false
        };

        bar.Controls.Add(MakeToolbarButton("重算并生成预警", BtnSpcRefreshAlerts));

        _btnSpcAlertAck = MakeToolbarButton("确认", BtnSpcAlertAck);
        _btnSpcAlertAck.Enabled = false;
        bar.Controls.Add(_btnSpcAlertAck);

        _btnSpcAlertClose = MakeToolbarButton("关闭（填写措施）", BtnSpcAlertClose);
        _btnSpcAlertClose.Enabled = false;
        bar.Controls.Add(_btnSpcAlertClose);

        _btnSpcAlertToAbnormal = MakeToolbarButton("转异常登记", BtnSpcAlertToAbnormal);
        _btnSpcAlertToAbnormal.Click -= BtnSpcAlertToAbnormal;
        _btnSpcAlertToAbnormal.Click += BtnSpcAlertToAbnormal;
        _btnSpcAlertToAbnormal.Enabled = false;
        bar.Controls.Add(_btnSpcAlertToAbnormal);

        bar.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(14, 9, 0, 0) });
        _txtSpcAlertOperator = new TextBox { Text = "admin", Width = 110, Margin = new Padding(4, 4, 0, 0) };
        bar.Controls.Add(_txtSpcAlertOperator);

        _lblSpcAlertContext = new Label
        {
            Text = "请选择一条预警",
            AutoSize = true,
            ForeColor = Color.FromArgb(40, 50, 60),
            Margin = new Padding(16, 9, 0, 0)
        };
        bar.Controls.Add(_lblSpcAlertContext);
        return bar;
    }

    /// <summary>设置「SpcAlertColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupSpcAlertColumns()
    {
        _spcAlertGrid.AutoGenerateColumns = false;
        _spcAlertGrid.Columns.Clear();

        _spcAlertGrid.Columns.Add(MakeTextCol("AlertNo", "预警号", true, 12));
        var t = MakeTextCol("SampleTime", "样本时间", true, 11);
        t.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _spcAlertGrid.Columns.Add(t);
        _spcAlertGrid.Columns.Add(MakeTextCol("ItemName", "监控项", true, 11));
        _spcAlertGrid.Columns.Add(MakeTextCol("VersionText", "版本", true, 6));
        _spcAlertGrid.Columns.Add(MakeTextCol("AlertType", "类型", true, 7));
        _spcAlertGrid.Columns.Add(MakeTextCol("RuleName", "规则", true, 16));
        _spcAlertGrid.Columns.Add(MakeTextCol("Severity", "严重度", true, 6));
        _spcAlertGrid.Columns.Add(MakeTextCol("Value", "当前值", true, 7));
        _spcAlertGrid.Columns.Add(MakeTextCol("CenterLine", "CL", true, 6));
        _spcAlertGrid.Columns.Add(MakeTextCol("Ucl", "UCL", true, 6));
        _spcAlertGrid.Columns.Add(MakeTextCol("Lcl", "LCL", true, 6));
        _spcAlertGrid.Columns.Add(MakeTextCol("Equipment", "设备", true, 10));
        _spcAlertGrid.Columns.Add(MakeTextCol("CellSn", "电芯SN", true, 13));
        _spcAlertGrid.Columns.Add(MakeTextCol("Status", "状态", true, 7));
        _spcAlertGrid.Columns.Add(MakeTextCol("Handler", "处理人", true, 7));
        var ht = MakeTextCol("HandleTime", "处理时间", true, 11);
        ht.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _spcAlertGrid.Columns.Add(ht);
        _spcAlertGrid.Columns.Add(MakeTextCol("Action", "处理措施", true, 18));
        _spcAlertGrid.Columns.Add(MakeTextCol("LinkedAbnormalNo", "关联异常单", true, 12));

        // 先解除再挂接着色事件（同上，防止重复着色）
        _spcAlertGrid.CellFormatting -= SpcAlertGrid_CellFormatting;
        _spcAlertGrid.CellFormatting += SpcAlertGrid_CellFormatting;
    }

    /// <summary>「SpcAlertGrid」网格单元格着色：按状态/结果区分颜色（差异红、异常橙、正常绿等）。</summary>
    private void SpcAlertGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        var name = grid.Columns[e.ColumnIndex].Name;

        if (name is "Value" or "CenterLine" or "Ucl" or "Lcl")
        {
            if (e.Value is double d)
            {
                e.Value = double.IsFinite(d) ? d.ToString("0.###") : "—";
                e.FormattingApplied = true;
            }
            return;
        }

        if (name == "Severity" && e.Value is string sev)
        {
            e.CellStyle.ForeColor = sev switch
            {
                SpcSeverity.High => Color.FromArgb(192, 0, 0),
                SpcSeverity.Medium => Color.FromArgb(230, 126, 34),
                _ => Color.FromArgb(130, 138, 150)
            };
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
        else if (name == "Status" && e.Value is string st)
        {
            e.CellStyle.ForeColor = st switch
            {
                SpcAlertStatus.Pending => Color.FromArgb(230, 126, 34),
                SpcAlertStatus.Acked => Color.FromArgb(15, 76, 129),
                SpcAlertStatus.Closed => Color.FromArgb(39, 174, 96),
                _ => Color.FromArgb(40, 50, 60)
            };
            e.CellStyle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
        }
    }

    /// <summary>ResolveSpcAlertItemId：（说明待补充）</summary>
    private string? ResolveSpcAlertItemId()
    {
        var name = _cboSpcAlertItem.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name) || name == "全部") return null;
        return MesDatabase.SpcItems.FirstOrDefault(i => i.DisplayText == name)?.ItemId;
    }

    /// <summary>查询并加载「SpcAlerts」数据到界面（含网格绑定与汇总刷新）。</summary>
    private void DoQuerySpcAlerts()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_spcAlertGrid is null) return;

        var itemId = ResolveSpcAlertItemId();
        var type = _cboSpcAlertType.SelectedItem?.ToString();
        if (type is null or "全部") type = null;
        var severity = _cboSpcAlertSeverity.SelectedItem?.ToString();
        if (severity is null or "全部") severity = null;
        var status = _cboSpcAlertStatus.SelectedItem?.ToString();
        if (status is null or "全部") status = null;

        DateTime? from = null, to = null;
        if (_chkSpcAlertUseTime.Checked)
        {
            from = _dtSpcAlertFrom.Value.Date;
            to = _dtSpcAlertTo.Value.Date;
        }

        var list = MesDatabase.QuerySpcAlerts(itemId, type, severity, status, from, to);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _spcAlertGrid.DataSource = null;
        _spcAlertGrid.DataSource = list;
        _lblSpcAlertResult.Text = $"共查询到 {list.Count} 条预警";
        UpdateSpcAlertContext();
    }

    /// <summary>在网格中选中「edSpcAlert」对应的行（联动加载明细）。</summary>
    private SpcAlert? SelectedSpcAlert() =>
        _spcAlertGrid.SelectedRows.Count == 0 ? null : _spcAlertGrid.SelectedRows[0].DataBoundItem as SpcAlert;

    /// <summary>「SpcAlertGrid」选中行变化 → 联动加载明细并更新按钮状态。</summary>
    private void SpcAlertGrid_SelectionChanged(object? sender, EventArgs e) => UpdateSpcAlertContext();

    /// <summary>根据当前选中项更新「SpcAlertContext」的可用状态/提示。</summary>
    private void UpdateSpcAlertContext()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_lblSpcAlertContext is null || _btnSpcAlertAck is null || _btnSpcAlertClose is null || _btnSpcAlertToAbnormal is null) return;

        var a = SelectedSpcAlert();
        if (a is null)
        {
            _lblSpcAlertContext.Text = "请选择一条预警";
            _btnSpcAlertAck.Enabled = false;
            _btnSpcAlertClose.Enabled = false;
            _btnSpcAlertToAbnormal.Enabled = false;
            return;
        }

        var sn = string.IsNullOrWhiteSpace(a.CellSn) ? "—" : a.CellSn;
        var eq = string.IsNullOrWhiteSpace(a.Equipment) ? "—" : a.Equipment;
        _lblSpcAlertContext.Text =
            $"{a.AlertNo} · {a.ItemName} · {a.AlertType}/{a.RuleName} · 设备 {eq} / SN {sn} · 值 {SpcFmt(a.Value)}";
        _btnSpcAlertAck.Enabled = a.Status == SpcAlertStatus.Pending;
        _btnSpcAlertClose.Enabled = a.Status is SpcAlertStatus.Pending or SpcAlertStatus.Acked;
        _btnSpcAlertToAbnormal.Enabled =
            (a.Status is SpcAlertStatus.Pending or SpcAlertStatus.Acked) && string.IsNullOrWhiteSpace(a.LinkedAbnormalNo);
    }

    /// <summary>处理「ResetSpcAlertQuery」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnResetSpcAlertQuery(object? sender, EventArgs e)
    {
        _cboSpcAlertItem.SelectedIndex = 0;
        _cboSpcAlertType.SelectedIndex = 0;
        _cboSpcAlertSeverity.SelectedIndex = 0;
        _cboSpcAlertStatus.SelectedIndex = 0;
        _chkSpcAlertUseTime.Checked = true;
        _dtSpcAlertFrom.Value = DateTime.Today.AddDays(-30);
        _dtSpcAlertTo.Value = DateTime.Today;
        DoQuerySpcAlerts();
    }

    /// <summary>处理「SpcRefreshAlerts」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSpcRefreshAlerts(object? sender, EventArgs e)
    {
        var op = _txtSpcAlertOperator.Text.Trim();
        if (string.IsNullOrEmpty(op)) op = "admin";
        var (added, total) = MesDatabase.RefreshSpcAlerts(null, op);
        DoQuerySpcAlerts();
        MessageBox.Show(
            $"已重算全部监控项预警：本次新增 {added} 条，当前预警总数 {total} 条。",
            "重算并生成预警", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>处理「SpcAlertAck」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSpcAlertAck(object? sender, EventArgs e)
    {
        var a = SelectedSpcAlert();
        if (a is null) return;
        var op = _txtSpcAlertOperator.Text.Trim();
        if (string.IsNullOrEmpty(op)) op = "admin";

        if (MessageBox.Show($"确认预警 {a.AlertNo}？\n确认后状态将变更为「{SpcAlertStatus.Acked}」。",
                "确认预警", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var (ok, msg) = MesDatabase.AckSpcAlert(a.AlertNo, op);
        MessageBox.Show(msg, "确认预警", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQuerySpcAlerts();
    }

    /// <summary>处理「SpcAlertClose」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSpcAlertClose(object? sender, EventArgs e)
    {
        var a = SelectedSpcAlert();
        if (a is null) return;

        using var dlg = new SpcCloseForm(a.AlertNo, $"{a.ItemName} · {a.AlertType}/{a.RuleName} · 值 {SpcFmt(a.Value)}");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var (ok, msg) = MesDatabase.CloseSpcAlert(a.AlertNo, dlg.OperatorName, dlg.HandleAction);
        MessageBox.Show(msg, "关闭预警", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        DoQuerySpcAlerts();
    }

    /// <summary>质量事件自动联动：将选中的 SPC 预警转登为异常单（工艺异常）。</summary>
    private void BtnSpcAlertToAbnormal(object? sender, EventArgs e)
    {
        var a = SelectedSpcAlert();
        if (a is null) return;
        if (a.Status is not (SpcAlertStatus.Pending or SpcAlertStatus.Acked))
        {
            Warn($"预警 {a.AlertNo} 当前状态为「{a.Status}」，仅「待处理/已确认」可转异常登记。");
            return;
        }
        if (!string.IsNullOrWhiteSpace(a.LinkedAbnormalNo))
        {
            Warn($"预警 {a.AlertNo} 已关联异常单 {a.LinkedAbnormalNo}，无需重复转单。");
            return;
        }

        var op = _txtSpcAlertOperator.Text.Trim();
        if (string.IsNullOrEmpty(op)) op = "admin";
        if (MessageBox.Show($"将预警 {a.AlertNo} 转登为异常单？\n转登后可在「异常管理」中跟踪处置。",
                "转异常登记", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var (ok, msg, abNo) = MesDatabase.CreateAbnormalFromSpcAlert(a.AlertNo, op);
        MessageBox.Show(msg, "转异常登记", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) _lblSpcAlertContext.Text = $"{a.AlertNo} 已生成异常单 {abNo}";
        DoQuerySpcAlerts();
    }


}
