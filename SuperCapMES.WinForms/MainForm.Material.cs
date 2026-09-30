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
/// MainForm 分部类（按功能域拆分，第 9 部分）：资材追溯页（按资材查电芯 → 多选保留）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Material」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 资材追溯（电芯保留） =================
    /// <summary>构建「MaterialHold」页签：控件树 + 列定义 + 首屏数据加载。</summary>
    private TabPage BuildMaterialHoldTab()
    {
        var page = new TabPage("资材追溯");
        page.Padding = new Padding(6);

        var gridGroup = MakeGridGroup("资材关联电芯（勾选需保留的电芯后点「执行保留」，已在保留中的将自动跳过；双击行查看该电芯详情）", out _matHoldGrid);
        _matHoldGrid.ReadOnly = false;
        _matHoldGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        SetupMaterialHoldColumns();
        page.Controls.Add(gridGroup);

        page.Controls.Add(BuildMaterialHoldActions());
        page.Controls.Add(BuildMaterialHoldQuery());
        return page;
    }

    /// <summary>设置「MaterialHoldColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupMaterialHoldColumns()
    {
        _matHoldGrid.AutoGenerateColumns = false;
        _matHoldGrid.Columns.Clear();

        var chk = new DataGridViewCheckBoxColumn
        {
            Name = "Chk",
            HeaderText = "选择",
            FillWeight = 6,
            ReadOnly = false,
            ThreeState = false
        };
        _matHoldGrid.Columns.Add(chk);
        _matHoldGrid.Columns.Add(MakeTextCol("Sn", "电芯SN", true, 15));
        _matHoldGrid.Columns.Add(MakeTextCol("ProductModel", "型号", true, 10));
        _matHoldGrid.Columns.Add(MakeTextCol("Line", "线体", true, 5));
        _matHoldGrid.Columns.Add(MakeTextCol("Status", "当前状态", true, 7));
        _matHoldGrid.Columns.Add(MakeTextCol("Result", "分档", true, 6));
        _matHoldGrid.Columns.Add(MakeTextCol("ElectrodeLot", "极片批号", true, 12));
        _matHoldGrid.Columns.Add(MakeTextCol("ElectrolyteLot", "电解液批号", true, 12));
        _matHoldGrid.Columns.Add(MakeTextCol("MaterialDesc", "关联材料", true, 20));
        _matHoldGrid.Columns.Add(MakeTextCol("HoldStatus", "保留状态", true, 10));

        _matHoldGrid.CurrentCellDirtyStateChanged += MatGrid_CurrentCellDirtyStateChanged;
        _matHoldGrid.CellValueChanged += MatGrid_CellValueChanged;
        // 双击行 → 弹出该电芯单只完整详情（只读模态窗体）；先移除再挂接避免事件累积
        _matHoldGrid.CellDoubleClick -= MatHoldGrid_CellDoubleClick;
        _matHoldGrid.CellDoubleClick += MatHoldGrid_CellDoubleClick;
    }

    /// <summary>构建「MaterialHold」查询条件区（筛选控件 + 查询/重置按钮）。</summary>
    private Control BuildMaterialHoldQuery()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(6, 9, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(new Label { Text = "资材编号 / 物料名称：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });

        _txtMatKeyword = new TextBox { Width = 240, Margin = new Padding(6, 4, 0, 0) };
        _txtMatKeyword.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) BtnQueryMaterial(this, EventArgs.Empty); };
        bar.Controls.Add(_txtMatKeyword);

        bar.Controls.Add(MakeToolbarButton("查询", BtnQueryMaterial));

        _lblMatHint = new Label
        {
            Text = "示例：PC-260812-L1-001、M-ACT-CAR-01、活性炭",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 8, 0, 0)
        };
        bar.Controls.Add(_lblMatHint);

        _lblMatCount = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.FromArgb(15, 76, 129),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Margin = new Padding(16, 8, 0, 0)
        };
        bar.Controls.Add(_lblMatCount);
        return bar;
    }

    /// <summary>构建「MaterialHoldActions」界面区块。</summary>
    private Control BuildMaterialHoldActions()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(6, 5, 4, 0),
            WrapContents = false
        };
        bar.Controls.Add(MakeToolbarButton("全选", BtnSelectAllCandidates));
        bar.Controls.Add(MakeToolbarButton("取消全选", BtnClearCandidates));

        _lblMatSelected = new Label
        {
            Text = "已选 0 只（可保留 0 只）",
            AutoSize = true,
            Margin = new Padding(10, 9, 0, 0),
            ForeColor = Color.FromArgb(40, 50, 60)
        };
        bar.Controls.Add(_lblMatSelected);

        bar.Controls.Add(new Label { Text = "保留位置：", AutoSize = true, Margin = new Padding(14, 9, 0, 0) });
        _cboHoldLocation = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 108,
            Margin = new Padding(6, 4, 0, 0)
        };
        _cboHoldLocation.Items.AddRange(HoldLocations.All);
        _cboHoldLocation.SelectedItem = HoldLocations.FormationInspection;
        bar.Controls.Add(_cboHoldLocation);

        bar.Controls.Add(new Label { Text = "原因：", AutoSize = true, Margin = new Padding(14, 9, 0, 0) });
        _txtHoldReason = new TextBox { Width = 190, Margin = new Padding(6, 4, 0, 0), Text = "质量保留" };
        bar.Controls.Add(_txtHoldReason);

        bar.Controls.Add(new Label { Text = "操作人：", AutoSize = true, Margin = new Padding(10, 9, 0, 0) });
        _txtHoldOperator = new TextBox { Width = 90, Margin = new Padding(6, 4, 0, 0), Text = "admin" };
        bar.Controls.Add(_txtHoldOperator);

        _btnDoHold = MakeToolbarButton("执行保留", BtnDoHoldCandidates);
        _btnDoHold.Enabled = false;
        bar.Controls.Add(_btnDoHold);
        return bar;
    }

    /// <summary>处理「QueryMaterial」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnQueryMaterial(object? sender, EventArgs e)
    {
        var kw = _txtMatKeyword.Text.Trim();
        if (string.IsNullOrEmpty(kw))
        {
            Warn("请输入资材编号、物料编码或物料名称后再查询。");
            return;
        }
        _lastMatCandidates = MesDatabase.QueryCellsByMaterial(kw);
        FillMaterialCandidates();
    }

    /// <summary>FillMaterialCandidates：（说明待补充）</summary>
    private void FillMaterialCandidates()
    {
        _matHoldGrid.Rows.Clear();
        foreach (var c in _lastMatCandidates)
        {
            var idx = _matHoldGrid.Rows.Add();
            var row = _matHoldGrid.Rows[idx];
            row.Tag = c;
            row.Cells["Chk"].Value = false;
            row.Cells["Sn"].Value = c.Sn;
            row.Cells["ProductModel"].Value = c.ProductModel;
            row.Cells["Line"].Value = c.Line;
            row.Cells["Status"].Value = c.Status;
            row.Cells["Result"].Value = string.IsNullOrEmpty(c.Result) ? "待分档" : c.Result;
            row.Cells["ElectrodeLot"].Value = c.ElectrodeLot;
            row.Cells["ElectrolyteLot"].Value = c.ElectrolyteLot;
            row.Cells["MaterialDesc"].Value = c.MaterialDesc;

            var holdCell = row.Cells["HoldStatus"];
            if (!string.IsNullOrEmpty(c.HoldStatus))
            {
                holdCell.Value = c.HoldStatus;
                holdCell.Style.ForeColor = Color.FromArgb(192, 0, 0);
                holdCell.Style.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            }
            else
            {
                holdCell.Value = "未保留";
                holdCell.Style.ForeColor = Color.Gray;
            }
        }

        var heldCount = _lastMatCandidates.Count(c => !string.IsNullOrEmpty(c.HoldStatus));
        _lblMatCount.Text = heldCount > 0
            ? $"共命中 {_lastMatCandidates.Count} 只电芯（含 {heldCount} 只保留中）"
            : $"共命中 {_lastMatCandidates.Count} 只电芯";
        UpdateMaterialSelectionState();
    }

    /// <summary>「MatGrid」单元格脏状态变化 → 立即提交勾选/编辑（保证计数与判定实时刷新）。</summary>
    private void MatGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_matHoldGrid.CurrentCell is DataGridViewCheckBoxCell && _matHoldGrid.IsCurrentCellDirty)
            _matHoldGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    /// <summary>「MatGrid」单元格值变化 → 刷新已选计数等派生显示。</summary>
    private void MatGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_matHoldGrid.Columns[e.ColumnIndex].Name != "Chk") return;
        UpdateMaterialSelectionState();
    }

    /// <summary>根据当前选中项更新「MaterialSelectionState」的可用状态/提示。</summary>
    private void UpdateMaterialSelectionState()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_matHoldGrid is null || _btnDoHold is null || _lblMatSelected is null) return;
        var checkedCount = 0;
        var eligibleCount = 0;
        foreach (DataGridViewRow row in _matHoldGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["Chk"].Value is bool b && b)
            {
                checkedCount++;
                if (row.Tag is CellHoldCandidate cand && string.IsNullOrEmpty(cand.HoldStatus)) eligibleCount++;
            }
        }
        _lblMatSelected.Text = $"已选 {checkedCount} 只（可保留 {eligibleCount} 只）";
        _btnDoHold.Enabled = eligibleCount > 0;
    }

    /// <summary>资材追溯结果行双击 → 弹出该电芯单只完整详情（只读模态窗体，数据实时按 SN 查询）。</summary>
    private void MatHoldGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        // 双击勾选框列是勾选意图而非看详情意图，跳过
        if (e.ColumnIndex >= 0 && _matHoldGrid.Columns[e.ColumnIndex].Name == "Chk") return;
        if (_matHoldGrid.Rows[e.RowIndex].Tag is not CellHoldCandidate c) return;
        if (string.IsNullOrEmpty(c.Sn)) return;

        using var form = new CellDetailForm(c.Sn);
        form.ShowDialog(this);
    }

    /// <summary>处理「SelectAllCandidates」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnSelectAllCandidates(object? sender, EventArgs e)
    {
        foreach (DataGridViewRow row in _matHoldGrid.Rows)
        {
            if (row.IsNewRow) continue;
            row.Cells["Chk"].Value = row.Tag is not CellHoldCandidate c || string.IsNullOrEmpty(c.HoldStatus);
        }
        UpdateMaterialSelectionState();
    }

    /// <summary>处理「ClearCandidates」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnClearCandidates(object? sender, EventArgs e)
    {
        foreach (DataGridViewRow row in _matHoldGrid.Rows)
        {
            if (row.IsNewRow) continue;
            row.Cells["Chk"].Value = false;
        }
        UpdateMaterialSelectionState();
    }

    /// <summary>处理「DoHoldCandidates」按钮点击：前置校验 → 执行业务 → 刷新界面并给出结果提示。</summary>
    private void BtnDoHoldCandidates(object? sender, EventArgs e)
    {
        var selected = new List<CellHoldCandidate>();
        foreach (DataGridViewRow row in _matHoldGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells["Chk"].Value is bool b && b && row.Tag is CellHoldCandidate c)
                selected.Add(c);
        }
        if (selected.Count == 0)
        {
            Warn("请先勾选要保留的电芯。");
            return;
        }

        var location = _cboHoldLocation.SelectedItem?.ToString() ?? HoldLocations.FormationInspection;
        var reason = _txtHoldReason.Text.Trim();
        var op = _txtHoldOperator.Text.Trim();

        var mats = selected.SelectMany(c => c.HitMaterials).ToList();
        var code = JoinHoldMaterial(mats.Select(m => m.MaterialCode));
        var name = JoinHoldMaterial(mats.Select(m => m.MaterialName));
        var lots = JoinHoldMaterial(mats.Select(m => m.LotNo));
        var sns = selected.Select(c => c.Sn).ToList();

        var (ok, message, _, skipped) = MesDatabase.HoldCells(code, name, lots, location, reason, op, sns);
        if (!ok)
        {
            Warn(message);
            return;
        }

        var text = message;
        if (skipped.Count > 0)
            text += "\n\n以下电芯已在保留中，已自动跳过：\n" + string.Join("、", skipped);
        MessageBox.Show(text, "保留成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // 重新按当前关键字查询并刷新，保留状态列随之更新
        _lastMatCandidates = MesDatabase.QueryCellsByMaterial(_txtMatKeyword.Text.Trim());
        FillMaterialCandidates();
    }

    /// <summary>JoinHoldMaterial：（说明待补充）</summary>
    private static string JoinHoldMaterial(IEnumerable<string> values) =>
        string.Join("、", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));


}
