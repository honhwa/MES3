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
/// MainForm 分部类（按功能域拆分，第 2 部分）：工序实绩页（设备筛选 / 模拟上传 / 检验项填写与自动判定 / 保存检验）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Actuals」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 设备工序实绩 =================
    /// <summary>构建「Actuals」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildActualsTab()
    {
        var page = new TabPage("工序实绩");

        // 顶部筛选行
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10, 8, 6, 0) };
        toolbar.Controls.Add(new Label { Text = "设备：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });

        _cboActualDevice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
        _cboActualDevice.DataSource = MesDatabase.Equipments
            .Where(e => MesDatabase.DeviceProcessMap.ContainsKey(e.Code)).ToList();
        _cboActualDevice.DisplayMember = "Name";
        _cboActualDevice.ValueMember = "Code";
        _cboActualDevice.SelectedIndexChanged += (_, _) => RefreshActuals();
        toolbar.Controls.Add(_cboActualDevice);
        toolbar.Controls.Add(MakeToolbarButton("模拟设备上传", BtnUploadActual));
        toolbar.Controls.Add(MakeToolbarButton("刷新", (_, _) => RefreshActuals()));

        _lblActualStatus = new Label { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 9, 0, 0) };
        toolbar.Controls.Add(_lblActualStatus);

        // 上下分栏：上方实绩表 / 下方检验填写
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            BackColor = SystemColors.Control
        };

        var actualBox = MakeGridGroup("设备上传的工序实绩（每个电芯的生产信息，选中行 → 下方填写检验）", out _actualGrid);
        split.Panel1.Controls.Add(actualBox);
        _actualGrid.SelectionChanged += (_, _) => LoadInspectionForCurrent();

        var inspectBox = new GroupBox { Text = "检验项目填写", Dock = DockStyle.Fill, Padding = new Padding(8, 14, 8, 8) };
        var iroot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        iroot.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        iroot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        iroot.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        _lblInspectTarget = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(15, 76, 129), TextAlign = ContentAlignment.MiddleLeft };
        _inspectGrid = MakeGrid();
        _inspectGrid.ReadOnly = false;
        // 实测值编辑结束 → 按标准值自动判定结果
        _inspectGrid.CellEndEdit += (_, e) => AutoJudgeInspection(e.RowIndex);
        // 结果列"不合格"的行整行标红（先解除再挂接，防止重复订阅；BindInspectionGrid 频繁重建列不影响实例级事件）
        _inspectGrid.CellFormatting -= InspectGrid_CellFormatting;
        _inspectGrid.CellFormatting += InspectGrid_CellFormatting;

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        btnRow.Controls.Add(MakeToolbarButton("载入检验项目", BtnLoadInspect));
        btnRow.Controls.Add(MakeToolbarButton("保存检验", BtnSaveInspect));

        iroot.Controls.Add(_lblInspectTarget, 0, 0);
        iroot.Controls.Add(_inspectGrid, 0, 1);
        iroot.Controls.Add(btnRow, 0, 2);
        inspectBox.Controls.Add(iroot);
        split.Panel2.Controls.Add(inspectBox);

        page.Controls.Add(split);
        page.Controls.Add(toolbar);
        return page;
    }

    /// <summary>GetSelectedDevice：（说明待补充）</summary>
    private string GetSelectedDevice() => _cboActualDevice?.SelectedValue?.ToString() ?? "";

    /// <summary>刷新「Actuals」的显示。</summary>
    private void RefreshActuals()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_cboActualDevice is null || _actualGrid is null) return;
        var code = GetSelectedDevice();
        if (string.IsNullOrEmpty(code)) { _actualGrid.DataSource = null; return; }

        var list = MesDatabase.GetActualsByEquipment(code);
        BindGrid(_actualGrid, list, ActualHeaders);
        if (_lblActualStatus is not null)
        {
            var proc = MesDatabase.DeviceProcessMap.TryGetValue(code, out var p) ? p : "";
            _lblActualStatus.Text = $"{code} · {proc} · 实绩 {list.Count} 条";
        }
    }

    /// <summary>处理「UploadActual」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnUploadActual(object? sender, EventArgs e)
    {
        var code = GetSelectedDevice();
        if (string.IsNullOrEmpty(code)) { Warn("请选择设备。"); return; }
        var n = MesDatabase.SimulateDeviceUpload(code);
        MessageBox.Show($"已模拟设备 {code} 上传 {n} 条工序实绩。",
            "设备上传", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshActuals();
    }

    /// <summary>加载「InspectionForCurrent」并绑定到界面。</summary>
    private void LoadInspectionForCurrent()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_actualGrid is null || _inspectGrid is null || _lblInspectTarget is null) return;
        if (_actualGrid.SelectedRows.Count == 0 || _actualGrid.SelectedRows[0].DataBoundItem is not ProcessActualRecord act)
        {
            _lblInspectTarget.Text = "请在上方选中一条工序实绩";
            // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
            _inspectGrid.DataSource = null;
            return;
        }
        var saved = MesDatabase.CountInspections(act.CellSn, act.Equipment, act.ProcessName);
        _lblInspectTarget.Text = $"检验对象：{act.CellSn} · {act.ProcessName} · {act.Equipment}（已保存 {saved} 条）";
        BindInspectionGrid(MesDatabase.GetInspectionRows(act.CellSn, act.Equipment, act.ProcessName));
    }

    /// <summary>处理「LoadInspect」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnLoadInspect(object? sender, EventArgs e)
    {
        if (_actualGrid.SelectedRows.Count == 0) { Warn("请先在实绩表中选中一行。"); return; }
        LoadInspectionForCurrent();
    }

    /// <summary>实测值编辑结束后，对照标准值自动判定合格/不合格</summary>
    private void AutoJudgeInspection(int rowIndex)
    {
        if (rowIndex < 0 || _inspectGrid.Rows[rowIndex].IsNewRow) return;

        var row = _inspectGrid.Rows[rowIndex];
        var std = row.Cells["StandardValue"].Value?.ToString() ?? "";
        var measured = row.Cells["MeasuredValue"].Value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(measured)) return; // 未填实测值不判定

        var ok = InspectionJudge.TryJudge(std, measured);
        if (ok is null) return; // 非数值标准（如"无褶皱"）或无法解析，保留手动结果

        var result = ok.Value ? "合格" : "不合格";
        var resultCell = row.Cells["Result"];
        if (resultCell.Value?.ToString() != result)
        {
            resultCell.Value = result;
            // 结果值变化后立即整行重绘，保证"不合格"行的红底提示即时可见（纯视觉优化，不改变判定逻辑）
            _inspectGrid.InvalidateRow(rowIndex);
        }
    }

    /// <summary>检验网格：结果列为"不合格"的行整行标红；合格/空值保持默认样式。</summary>
    private void InspectGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (sender is not DataGridView grid) return;
        var resultCol = grid.Columns["Result"];
        if (resultCol is null) return; // 列尚未重建完成时不做处理

        var row = grid.Rows[e.RowIndex];
        if (row.IsNewRow) return;
        if (row.Cells[resultCol.Index].Value?.ToString() != "不合格") return;

        // 仅命中不合格行：整行浅红底 + 深红字；选中态用红系保证可读
        e.CellStyle.ForeColor = Color.FromArgb(192, 0, 0);
        e.CellStyle.BackColor = Color.FromArgb(255, 235, 235);
        e.CellStyle.SelectionForeColor = Color.FromArgb(192, 0, 0);
        e.CellStyle.SelectionBackColor = Color.FromArgb(255, 200, 200);
    }

    /// <summary>处理「SaveInspect」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSaveInspect(object? sender, EventArgs e)
    {
        if (_actualGrid.SelectedRows.Count == 0 || _actualGrid.SelectedRows[0].DataBoundItem is not ProcessActualRecord act)
        {
            Warn("请先在实绩表中选中一行。");
            return;
        }

        // 先提交当前编辑中的单元格：触发 CellEndEdit→AutoJudge 自动判定，并让最新实测值/结果落入行数据
        if (_inspectGrid.IsCurrentCellInEditMode)
            _inspectGrid.EndEdit();
        _inspectGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        // 首件检验门禁：工单未通过首检放行时提示（默认「否」，符合"换型/开班首件必检"要求）
        if (!MesDatabase.IsFaiPassed(act.WoNo))
        {
            var latest = MesDatabase.GetLatestFai(act.WoNo);
            var stateText = latest is null ? "尚未做首件检验" : $"最近首检 {latest.FaiNo} 状态为「{latest.Status}」";
            var go = MessageBox.Show(
                $"工单 {act.WoNo} 尚未通过首件检验放行（{stateText}）。\n是否仍要保存本次检验实绩？\n（建议先到「首件检验」页完成首检并放行）",
                "首件检验门禁",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (go != DialogResult.Yes) return;   // 默认「否」→ 不保存
        }

        // 存在明确"不合格"结果时先二次确认（合格/空值不触发；默认按钮为「否」，防止误按回车直接保存）
        var badCount = _inspectGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Count(r => r.Cells["Result"].Value?.ToString() == "不合格");
        if (badCount > 0)
        {
            var ask = MessageBox.Show(
                $"存在 {badCount} 项检验结果不合格，是否仍要保存？",
                "检验结果提醒",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (ask != DialogResult.Yes) return; // 选「否」→ 不保存，直接返回
        }

        var rows = new List<InspectionRow>();
        foreach (DataGridViewRow r in _inspectGrid.Rows)
        {
            if (r.IsNewRow) continue;
            rows.Add(new InspectionRow
            {
                ItemName = r.Cells["ItemName"].Value?.ToString() ?? "",
                StandardValue = r.Cells["StandardValue"].Value?.ToString() ?? "",
                MeasuredValue = r.Cells["MeasuredValue"].Value?.ToString() ?? "",
                Result = r.Cells["Result"].Value?.ToString() ?? "合格",
                Inspector = r.Cells["Inspector"].Value?.ToString() ?? ""
            });
        }
        if (rows.Count == 0) { Warn("没有可保存的检验项目。"); return; }

        var n = MesDatabase.SaveInspections(act.CellSn, act.Equipment, act.ProcessName, rows);
        var savedMsg = $"已保存检验记录 {n} 条。";

        // 质量事件自动联动：本次保存含不合格项 → 自动为该电芯创建保留单（不合格/保留单联闭环）
        var badItems = rows
            .Where(r => r.Result == "不合格")
            .Select(r => r.ItemName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
        if (badItems.Count > 0)
        {
            var head = string.Join("、", badItems.Take(3));
            var tail = badItems.Count > 3 ? "等" : "";
            var reason = $"检验不合格自动保留：{act.ProcessName} {head}{tail}";
            var op = string.IsNullOrWhiteSpace(act.Operator) ? "检验系统" : act.Operator;
            var (ok, msg) = MesDatabase.HoldSingleNonconformingCell(act.CellSn, act.Equipment, act.ProcessName, reason, op);
            var holdMatch = System.Text.RegularExpressions.Regex.Match(msg, @"保留单\s+(\S+)");
            if (ok && holdMatch.Success)
                savedMsg += $"\n已自动创建保留单 {holdMatch.Groups[1].Value}（位置：待检区）";
            else if (msg.Contains("已在保留中"))
                savedMsg += "\n该电芯已在保留中，未重复保留";
            else
                savedMsg += $"\n自动保留失败：{msg}";
        }

        MessageBox.Show(savedMsg, "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadInspectionForCurrent();
    }

    /// <summary>BindInspectionGrid：（说明待补充）</summary>
    private void BindInspectionGrid(List<InspectionRow> rows)
    {
        _inspectGrid.AutoGenerateColumns = false;
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _inspectGrid.DataSource = null;
        _inspectGrid.Columns.Clear();

        _inspectGrid.Columns.Add(MakeTextCol("ItemName", "检验项目", true, 30));
        _inspectGrid.Columns.Add(MakeTextCol("StandardValue", "标准值", true, 22));
        _inspectGrid.Columns.Add(MakeTextCol("MeasuredValue", "实测值", false, 22));

        var resultCol = new DataGridViewComboBoxColumn { Name = "Result", HeaderText = "结果", DataPropertyName = "Result", FillWeight = 14 };
        resultCol.Items.Add("合格");
        resultCol.Items.Add("不合格");
        _inspectGrid.Columns.Add(resultCol);

        _inspectGrid.Columns.Add(MakeTextCol("Inspector", "检验员", false, 12));
        _inspectGrid.DataSource = rows;
    }

    /// <summary>创建「TextCol」控件/样式（统一样式的工厂方法）。</summary>
    private static DataGridViewTextBoxColumn MakeTextCol(string prop, string header, bool readOnly, int fillWeight) =>
        new()
        {
            Name = prop,
            DataPropertyName = prop,
            HeaderText = header,
            ReadOnly = readOnly,
            FillWeight = fillWeight
        };


}
