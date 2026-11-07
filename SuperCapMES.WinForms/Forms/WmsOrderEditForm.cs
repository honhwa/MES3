using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

/// <summary>
/// WMS 新建单据窗体：
/// 顶部表头随「单据类型」联动（往来对象标签 / 物流单号 / 退货原因必填）；
/// 中部可增删明细行（物料编码下拉 → 自动带出名称/规格/单位，库位下拉）；
/// 保存时做必填校验并调用 MesDatabase.CreateWmsOrder，成功后返回新单号。
/// </summary>
public partial class WmsOrderEditForm : Form
{
    /// <summary>
    /// 联动写入抑制标志：程序化设置批次/库位单元格值时置 true，
    /// 避免 CellValueChanged 再次触发联动写入而产生递归。
    /// </summary>
    private bool _suppressLink;

    /// <summary>创建成功后返回的单据号（未成功为空）</summary>
    public string CreatedOrderNo { get; private set; } = "";

    public WmsOrderEditForm()
    {
        InitializeComponent();
        InitializeRuntime();
    }

    /// <summary>
    /// 执行期初始化：仅保留 Designer 无法解析的动态逻辑（执行期集合项目 /
    /// DataGridView 栏位与 CellStyle 物件初始设定式 / MesDatabase 主数据绑定）。
    /// 控件树建立、静态属性、容器加入与方法群事件挂接均在 WmsOrderEditForm.Designer.cs。
    /// </summary>
    private void InitializeRuntime()
    {
        // 单据类型下拉项目来自执行期集合 WmsOrderTypes.All（Designer 仅能展开字面静态项目）。
        _cboType.Items.AddRange(WmsOrderTypes.All);

        // 明细网格的栏位 / CellStyle / 主数据绑定（Designer 无法解析的段落）。
        ConfigureLineGrid();

        ApplyTypeRules();
        AddLineRow();
    }

    // ---------------- 明细网格动态设定 ----------------

    /// <summary>
    /// 明细网格的栏位、CellStyle 物件初始设定式与主数据绑定（Designer 无法解析的段落），
    /// 依迁移规格保留于执行期；网格本体静态属性、容器加入与方法群事件见 WmsOrderEditForm.Designer.cs。
    /// </summary>
    private void ConfigureLineGrid()
    {
        _lineGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 76, 129),
            ForeColor = Color.White,
            Font = new Font("微软雅黑", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter
        };
        _lineGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.Black,
            SelectionBackColor = Color.FromArgb(207, 224, 239),
            SelectionForeColor = Color.Black,
            Font = new Font("微软雅黑", 9F)
        };
        _lineGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) };

        _lineGrid.Columns.Clear();

        var matCol = new DataGridViewComboBoxColumn
        {
            Name = "MaterialCode",
            HeaderText = "物料编码",
            FillWeight = 16,
            FlatStyle = FlatStyle.Flat
        };
        matCol.DataSource = MesDatabase.WmsMaterials;
        matCol.DisplayMember = "Code";
        matCol.ValueMember = "Code";
        _lineGrid.Columns.Add(matCol);

        _lineGrid.Columns.Add(MakeTextColumn("MaterialName", "物料名称", 22));
        _lineGrid.Columns.Add(MakeTextColumn("Spec", "规格", 12));
        _lineGrid.Columns.Add(MakeTextColumn("Unit", "单位", 7));

        // 批次列：候选按「行（物料）」动态设置到单元格级 Items（列级 Items 共享，无法按行区分）。
        // 出库类只可选库存现存批次（编辑控件 DropDownList，禁止手填）；入库类可手填新批次（DropDown，可编辑）。
        _lineGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "LotNo",
            HeaderText = "批次",
            FillWeight = 13,
            FlatStyle = FlatStyle.Flat
        });

        _lineGrid.Columns.Add(MakeTextColumn("Quantity", "数量", 9));
        _lineGrid.Columns.Add(MakeTextColumn("Remark", "备注", 13));

        // 库位列：候选同样按行设置。出库类=该(物料+批次)有库存的库位；入库类=全量库位。
        _lineGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Location",
            HeaderText = "库位",
            FillWeight = 12,
            FlatStyle = FlatStyle.Flat
        });

        // 网格事件（CellValueChanged / CellBeginEdit / EditingControlShowing / DataError）
        // 已改为方法群，挂接于 WmsOrderEditForm.Designer.cs。
    }

    private void BtnAddLine(object? sender, EventArgs e) => AddLineRow();

    private void BtnDeleteLine(object? sender, EventArgs e)
    {
        if (_lineGrid.SelectedRows.Count == 0 && _lineGrid.SelectedCells.Count == 0)
        {
            Warn("请先选中要删除的明细行。");
            return;
        }
        var indexes = new HashSet<int>();
        foreach (DataGridViewRow r in _lineGrid.SelectedRows) indexes.Add(r.Index);
        foreach (DataGridViewCell c in _lineGrid.SelectedCells) indexes.Add(c.RowIndex);
        foreach (var idx in indexes.OrderByDescending(i => i))
        {
            if (idx >= 0 && idx < _lineGrid.Rows.Count && !_lineGrid.Rows[idx].IsNewRow)
                _lineGrid.Rows.RemoveAt(idx);
        }
    }

    /// <summary>追加一条空明细行：数量默认 1；库位/批次候选按当前类型初始化（入库类给默认库位建议）</summary>
    private void AddLineRow()
    {
        var idx = _lineGrid.Rows.Add();
        var row = _lineGrid.Rows[idx];
        var inbound = WmsOrderTypes.IsInbound(CurrentType());

        _suppressLink = true;
        try
        {
            row.Cells["MaterialCode"].Value = null;
            row.Cells["MaterialName"].Value = "";
            row.Cells["Spec"].Value = "";
            row.Cells["Unit"].Value = "";
            row.Cells["LotNo"].Value = null;
            row.Cells["Quantity"].Value = "1";
            row.Cells["Remark"].Value = "";
            row.Cells["Location"].Value = null;
        }
        finally { _suppressLink = false; }

        // 初始候选：无物料时，出库类批次/库位候选为空，入库类批次为空、库位为全量
        SetCellItems(row, "LotNo", new List<string>());
        SetCellItems(row, "Location", inbound ? WmsLocations.All.ToList() : new List<string>());

        _suppressLink = true;
        try
        {
            if (inbound) row.Cells["Location"].Value = DefaultLocation(CurrentType());
        }
        finally { _suppressLink = false; }

        UpdateStockHint();
    }

    private void LineGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_suppressLink) return;   // 联动写入不再触发联动，避免递归

        var colName = _lineGrid.Columns[e.ColumnIndex].Name;
        var row = _lineGrid.Rows[e.RowIndex];

        if (colName == "MaterialCode")
        {
            var code = row.Cells["MaterialCode"].Value?.ToString()?.Trim() ?? "";
            var mat = code.Length == 0 ? null : MesDatabase.FindWmsMaterial(code);

            _suppressLink = true;
            try
            {
                row.Cells["MaterialName"].Value = mat?.MaterialName ?? "";
                row.Cells["Spec"].Value = mat?.Spec ?? "";
                row.Cells["Unit"].Value = mat?.Unit ?? "";
                if (code.Length == 0)
                {
                    row.Cells["LotNo"].Value = null;
                    row.Cells["Location"].Value = null;
                }
            }
            finally { _suppressLink = false; }

            // 物料变化 → 重新带出批次/库位（单一批次自动带出，多批次带出首个；无效值清空重带）
            RefreshRowLinks(row, autoPickLot: true);
            UpdateStockHint();
            return;
        }

        if (colName == "LotNo")
        {
            var lot = row.Cells["LotNo"].Value?.ToString()?.Trim() ?? "";
            // 入库类手填的新批次不在候选中 → 追加进行候选，确保单元格正常显示
            if (lot.Length > 0 && row.Cells["LotNo"] is DataGridViewComboBoxCell lotCell
                && !lotCell.Items.Cast<object>().Any(o => string.Equals(o?.ToString(), lot, StringComparison.OrdinalIgnoreCase)))
            {
                if (WmsOrderTypes.IsInbound(CurrentType())) lotCell.Items.Add(lot);
            }

            // 批次确定后 → 刷新库位候选（出库类：该物料+批次有库存的库位；单一时自动带出）
            RefreshRowLocations(row, lot);
            UpdateStockHint();
        }
    }

    /// <summary>进入编辑前重建该单元格候选，保证候选与本行物料/批次实时一致</summary>
    private void LineGrid_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var colName = _lineGrid.Columns[e.ColumnIndex].Name;
        var row = _lineGrid.Rows[e.RowIndex];
        var code = row.Cells["MaterialCode"].Value?.ToString()?.Trim() ?? "";

        if (colName == "LotNo")
        {
            var lots = code.Length == 0 ? new List<string>() : MesDatabase.GetWmsStockLots(code);
            // 入库类已手填的新批次不在库存候选中 → 保留进行候选，避免编辑时显示丢失
            var current = row.Cells["LotNo"].Value?.ToString()?.Trim() ?? "";
            if (WmsOrderTypes.IsInbound(CurrentType()) && current.Length > 0
                && !lots.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                lots.Add(current);
            }
            SetCellItems(row, "LotNo", lots);
        }
        else if (colName == "Location")
        {
            var lot = row.Cells["LotNo"].Value?.ToString()?.Trim() ?? "";
            SetCellItems(row, "Location", BuildLocationCandidates(code, lot));
        }
    }

    /// <summary>
    /// 编辑控件显示时按「列 / 当前行所属类型」切换下拉样式：
    /// 物料编码 = DropDownList（主数据，任何单据都只能从列表选，禁止手输）；
    /// 出库类批次/库位 = DropDownList（只能选不能填）；入库类批次 = DropDown（可手填新批次）。
    /// </summary>
    private void LineGrid_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not DataGridViewComboBoxEditingControl combo) return;
        if (_lineGrid.CurrentCell is null) return;
        var colName = _lineGrid.Columns[_lineGrid.CurrentCell.ColumnIndex].Name;

        switch (colName)
        {
            case "MaterialCode":
                // 物料编码属主数据：始终禁止自由文本（列级 DataSource 已绑定主数据，无需逐行候选）
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                break;
            case "LotNo":
                combo.DropDownStyle = WmsOrderTypes.IsInbound(CurrentType())
                    ? ComboBoxStyle.DropDown
                    : ComboBoxStyle.DropDownList;
                break;
            case "Location":
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                break;
        }
    }

    // ---------------- 批次 / 库位联动 ----------------

    /// <summary>库位候选：出库类=该(物料+批次)有库存的库位；入库类=全量库位</summary>
    private List<string> BuildLocationCandidates(string materialCode, string lotNo)
    {
        if (WmsOrderTypes.IsInbound(CurrentType()))
            return WmsLocations.All.ToList();
        if (materialCode.Length == 0 || lotNo.Length == 0)
            return new List<string>();
        return MesDatabase.GetWmsStockLocations(materialCode, lotNo);
    }

    /// <summary>把候选集合写入某行某列的组合单元格级 Items（单元格级才能做到逐行不同候选）</summary>
    private static void SetCellItems(DataGridViewRow row, string columnName, List<string> items)
    {
        if (row.Cells[columnName] is not DataGridViewComboBoxCell cell) return;
        cell.Items.Clear();
        foreach (var it in items) cell.Items.Add(it);
    }

    /// <summary>
    /// 按当前类型与物料刷新某行的批次候选并自动带出，再联动刷新库位。
    /// 入库类：批次可手填（保留已填值），仅当为空且存在候选时带出首个；出库类：仅库存现存批次。
    /// </summary>
    private void RefreshRowLinks(DataGridViewRow row, bool autoPickLot)
    {
        var inbound = WmsOrderTypes.IsInbound(CurrentType());
        var code = row.Cells["MaterialCode"].Value?.ToString()?.Trim() ?? "";

        // ---- 批次候选 ----
        var lotCandidates = code.Length == 0 ? new List<string>() : MesDatabase.GetWmsStockLots(code);
        SetCellItems(row, "LotNo", lotCandidates);

        var lotCell = row.Cells["LotNo"];
        var currentLot = lotCell.Value?.ToString()?.Trim() ?? "";

        if (inbound)
        {
            // 入库：允许手填新批次，保留用户已填值；空且存在候选时带出首个
            if (currentLot.Length == 0 && autoPickLot && lotCandidates.Count > 0)
                currentLot = lotCandidates[0];
            // 手填的新批次不在候选中 → 追加进行候选，确保显示
            if (currentLot.Length > 0 && !lotCandidates.Contains(currentLot, StringComparer.OrdinalIgnoreCase))
            {
                lotCandidates.Add(currentLot);
                SetCellItems(row, "LotNo", lotCandidates);
            }
        }
        else
        {
            // 出库：必须命中库存批次；不在候选内则清空，随后带出首个
            if (currentLot.Length > 0 && !lotCandidates.Contains(currentLot, StringComparer.OrdinalIgnoreCase))
                currentLot = "";
            if (currentLot.Length == 0 && lotCandidates.Count > 0)
                currentLot = lotCandidates[0];
        }

        _suppressLink = true;
        try
        {
            lotCell.Value = currentLot.Length == 0 ? null : currentLot;
        }
        finally { _suppressLink = false; }

        // ---- 库位候选（依赖批次）----
        RefreshRowLocations(row, currentLot);
    }

    /// <summary>
    /// 按当前类型与(物料+批次)刷新某行的库位候选并自动带出。
    /// 入库类：保留用户选择，无值时给默认库位建议；出库类：仅该(物料+批次)有库存的库位。
    /// </summary>
    private void RefreshRowLocations(DataGridViewRow row, string lotNo)
    {
        var inbound = WmsOrderTypes.IsInbound(CurrentType());
        var code = row.Cells["MaterialCode"].Value?.ToString()?.Trim() ?? "";
        var locCandidates = BuildLocationCandidates(code, lotNo);
        SetCellItems(row, "Location", locCandidates);

        var locCell = row.Cells["Location"];
        var currentLoc = locCell.Value?.ToString()?.Trim() ?? "";

        if (inbound)
        {
            // 入库：保留用户已选库位；空/无效时回落到默认库位建议，再回落到首个候选
            if (currentLoc.Length == 0 || !locCandidates.Contains(currentLoc, StringComparer.OrdinalIgnoreCase))
            {
                var def = DefaultLocation(CurrentType(), MesDatabase.FindWmsMaterial(code));
                currentLoc = locCandidates.Contains(def, StringComparer.OrdinalIgnoreCase)
                    ? def
                    : (locCandidates.Count > 0 ? locCandidates[0] : "");
            }
        }
        else
        {
            // 出库：必须命中候选；不在候选内则清空，随后带出首个
            if (currentLoc.Length > 0 && !locCandidates.Contains(currentLoc, StringComparer.OrdinalIgnoreCase))
                currentLoc = "";
            if (currentLoc.Length == 0 && locCandidates.Count > 0)
                currentLoc = locCandidates[0];
        }

        _suppressLink = true;
        try
        {
            locCell.Value = currentLoc.Length == 0 ? null : currentLoc;
        }
        finally { _suppressLink = false; }
    }

    /// <summary>
    /// 刷新工具栏提示：出库类列出「有物料编码但当前无库存批次」的物料（需核对，不逐行弹窗）；
    /// 入库类给出批次/库位口径说明。
    /// </summary>
    private void UpdateStockHint()
    {
        if (WmsOrderTypes.IsInbound(CurrentType()))
        {
            _lblLocHint.ForeColor = Color.Gray;
            _lblLocHint.Text = $"默认库位建议：{DefaultLocation(CurrentType())}（入库类：批次可手填或选已有，库位可全量选择）";
            return;
        }

        var missing = new List<string>();
        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var code = row.Cells["MaterialCode"].Value?.ToString()?.Trim() ?? "";
            if (code.Length == 0) continue;
            if (MesDatabase.GetWmsStockLots(code).Count == 0) missing.Add(code);
        }

        if (missing.Count > 0)
        {
            _lblLocHint.ForeColor = Color.Firebrick;
            _lblLocHint.Text = $"⚠ 物料 {string.Join("、", missing)} 当前无库存批次，无法出库，请核对物料编码。";
        }
        else
        {
            _lblLocHint.ForeColor = Color.Gray;
            _lblLocHint.Text = "出库类：批次/库位只能选择库存中已有的项。";
        }
    }

    // ---------------- 类型联动 ----------------

    private string CurrentType() =>
        _cboType.SelectedItem?.ToString() ?? WmsOrderTypes.PurchaseInbound;

    /// <summary>类型切换时动态调整标签/必填提示与默认库位建议</summary>
    private void ApplyTypeRules()
    {
        if (_cboType.SelectedIndex < 0) _cboType.SelectedIndex = 0;
        var type = CurrentType();

        _lblPartner.Text = $"{WmsOrderTypes.PartnerLabel(type)} *";
        _lblRef.Text = WmsOrderTypes.RequiresRefNo(type) ? "物流单号 *" : "参考号";
        _lblReason.Text = IsReturnType(type) ? "退货原因 *" : "退货原因";

        // 类型切换 → 所有明细行的批次/库位候选与自动带出按新类型重建
        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            RefreshRowLinks(row, autoPickLot: true);
        }
        UpdateStockHint();
    }

    /// <summary>是否退货类单据（需填退货原因）：与仓储层 CreateWmsOrder 校验口径一致</summary>
    private static bool IsReturnType(string type) => WmsOrderTypes.RequiresReturnReason(type);

    /// <summary>按单据类型/物料类别给出库位建议：客户退货→待检区、现场退货→退货待处理区、成品→成品库、其余→原料库</summary>
    private static string DefaultLocation(string type, WmsMaterial? material = null)
    {
        if (type == WmsOrderTypes.CustomerReturn) return WmsLocations.PendingInspection;
        if (type == WmsOrderTypes.SiteReturn) return WmsLocations.ReturnPending;
        if (material?.Category == WmsCategories.Finished) return WmsLocations.FinishedB;
        return WmsLocations.RawA;
    }

    // ---------------- 保存 ----------------

    private void Save()
    {
        if (_lineGrid.IsCurrentCellInEditMode) _lineGrid.EndEdit();
        _lineGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        var type = CurrentType();
        var partner = _txtPartner.Text.Trim();
        if (partner.Length == 0) { Warn($"请填写{WmsOrderTypes.PartnerLabel(type)}。"); return; }

        var refNo = _txtRef.Text.Trim();
        if (WmsOrderTypes.RequiresRefNo(type) && refNo.Length == 0)
        {
            Warn("物流出库需填写物流单号。");
            return;
        }

        var reason = _txtReason.Text.Trim();
        if (IsReturnType(type) && reason.Length == 0)
        {
            Warn("退货类单据需填写退货原因。");
            return;
        }

        var op = _txtOperator.Text.Trim();
        if (op.Length == 0) op = "admin";

        var lines = new List<WmsOrderLine>();
        var no = 0;
        foreach (DataGridViewRow row in _lineGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var code = row.Cells["MaterialCode"].Value?.ToString()?.Trim() ?? "";
            if (code.Length == 0) continue;   // 空行忽略
            no++;

            // 物料编码必填且必须存在于主数据（新增，放在逐行校验最前）：
            // 防止手输不存在编码导致入库类过账凭空新建库存台账行（脏数据）/ 出库类报误导性“库存不足”。
            var mat = MesDatabase.FindWmsMaterial(code);
            if (mat is null)
            {
                Warn($"第 {no} 行：请选择有效的物料编码（当前为空或不在物料主数据中）。");
                return;
            }

            var qtyText = row.Cells["Quantity"].Value?.ToString()?.Trim() ?? "";
            if (!double.TryParse(qtyText, out var qty) || qty <= 0)
            {
                Warn($"第 {no} 行数量无效，请填写大于 0 的数值。");
                return;
            }

            var lot = row.Cells["LotNo"].Value?.ToString()?.Trim() ?? "";
            var locValue = row.Cells["Location"].Value?.ToString()?.Trim() ?? "";

            // 批次必填（所有非空明细行）
            if (lot.Length == 0)
            {
                Warn($"第 {no} 行：请选择/填写批次。");
                return;
            }

            // 出库类：批次必须属于该物料库存批次，库位必须该(物料,批次,库位)有库存（> 1e-9）
            if (!WmsOrderTypes.IsInbound(type))
            {
                var lotCandidates = MesDatabase.GetWmsStockLots(code);
                if (lotCandidates.Count == 0)
                {
                    Warn($"第 {no} 行：物料 {code} 当前无库存批次，无法出库，请核对物料编码。");
                    return;
                }
                if (!lotCandidates.Contains(lot, StringComparer.OrdinalIgnoreCase))
                {
                    Warn($"第 {no} 行：物料 {code} 的批次 {lot} 不在库存中，请从库存批次中选择。");
                    return;
                }
                if (locValue.Length == 0)
                {
                    Warn($"第 {no} 行：请选择物料 {code} / 批次 {lot} 有库存的库位。");
                    return;
                }
                var locCandidates = MesDatabase.GetWmsStockLocations(code, lot);
                if (!locCandidates.Contains(locValue, StringComparer.OrdinalIgnoreCase))
                {
                    Warn($"第 {no} 行：物料 {code} 的批次 {lot} 在库位 {locValue} 无库存，请重新选择。");
                    return;
                }
            }

            lines.Add(new WmsOrderLine
            {
                MaterialCode = code,
                MaterialName = row.Cells["MaterialName"].Value?.ToString()?.Trim() ?? mat?.MaterialName ?? "",
                Spec = row.Cells["Spec"].Value?.ToString()?.Trim() ?? mat?.Spec ?? "",
                Unit = row.Cells["Unit"].Value?.ToString()?.Trim() ?? mat?.Unit ?? "",
                LotNo = lot,
                Quantity = qty,
                Location = locValue.Length == 0 ? DefaultLocation(type, mat) : locValue,
                Remark = row.Cells["Remark"].Value?.ToString()?.Trim() ?? ""
            });
        }

        if (lines.Count == 0) { Warn("请至少填写 1 行明细（物料编码非空且数量 > 0）。"); return; }

        var (ok, message, orderNo) = MesDatabase.CreateWmsOrder(
            type, partner, refNo, reason, op, _txtRemark.Text.Trim(), lines);
        if (!ok)
        {
            Warn(message);
            return;
        }

        CreatedOrderNo = orderNo;
        MessageBox.Show(message, "创建成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------------- 控件工厂 ----------------

    /// <summary>明细网格文字栏位；Columns.Add 属动态段落，故本工厂保留于执行期。</summary>
    private static DataGridViewTextBoxColumn MakeTextColumn(string name, string header, int fillWeight) =>
        new()
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight
        };

    // ---------------- 由 Designer.cs 以方法群挂接的事件 ----------------

    private void CboType_SelectedIndexChanged(object? sender, EventArgs e) => ApplyTypeRules();

    private void BtnOk_Click(object? sender, EventArgs e) => Save();

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void LineGrid_DataError(object? sender, DataGridViewDataErrorEventArgs e) => e.ThrowException = false;

    /// <summary>底部面板尺寸变化时重算按钮位置（原 BuildFooter 内的 lambda）。</summary>
    private void FooterBar_Resize(object? sender, EventArgs e)
    {
        btnOk.Location = new Point(footerBar.Width - 226, 10);
        btnCancel.Location = new Point(footerBar.Width - 112, 10);
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
