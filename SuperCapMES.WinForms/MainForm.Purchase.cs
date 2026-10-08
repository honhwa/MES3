using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分，第 31 部分）：资材采购申请（请购单）页
///
/// 这一页存在的理由，一句话：齐套校验能告诉你"缺铝壳 2842 只"，但**然后呢**？
/// 没有这张单子，缺料就只是一个红色数字，没有出口、没有责任人、没有审批留痕。
///
/// 页面按"单据流"组织，不是按"字段"组织：
///   ① 选一张缺料工单 → ② 生成草稿（自动带出物料明细/需求量/可用量/缺口）
///   → ③ 提交 → ④ 审批（批准/驳回）→ ⑤ 下单 → ⑥ 关闭
/// 按钮的启用与文案**由单据状态决定**（PurchaseRequestActions.AllowedIn），
/// 这样流程约束写在数据层，页面只负责呈现，不会出现"按钮能点但后端拒绝"的错位。
/// </summary>
public partial class MainForm
{
    private static readonly Color PrInk = Color.FromArgb(30, 41, 59);
    private static readonly Color PrMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color PrOk = Color.FromArgb(21, 128, 61);
    private static readonly Color PrWarn = Color.FromArgb(217, 119, 6);
    private static readonly Color PrBad = Color.FromArgb(220, 38, 38);

    // ================= 页面构建 =================

    private TabPage BuildPurchaseReqTab()
    {
        var page = new TabPage("资材采购申请");

        // ---- 顶部：来源工单 + 生成 + 流程动作 ----
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(10, 9, 6, 0),
            WrapContents = false
        };
        toolbar.Controls.Add(new Label { Text = "缺料工单：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboReqWo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 360,
            Margin = new Padding(0, 4, 8, 0),
            DropDownWidth = 360
        };
        _cboReqWo.Format += (_, e) =>
        {
            if (e.ListItem is not string no) return;
            var wo = MesDatabase.WorkOrders.FirstOrDefault(w => w.WoNo == no);
            if (wo is null) return;
            var shortCount = MesDatabase.GetOutstandingShortage(wo.WoNo).Count;
            e.Value = $"{wo.WoNo} ｜ {wo.ProductModel} ｜ 待产 {Math.Max(0, wo.PlanQty - wo.FinishedQty)} 只 ｜ 缺 {shortCount} 项";
        };
        toolbar.Controls.Add(_cboReqWo);

        var btnGen = MakeToolbarButton("⚡ 按工单生成申请", BtnPurchaseGenerate);
        btnGen.Width = 152;
        toolbar.Controls.Add(btnGen);

        _btnReqSubmit = MakeToolbarButton(PurchaseRequestActions.Submit, BtnPurchaseSubmit);
        _btnReqSubmit.Width = 74;
        toolbar.Controls.Add(_btnReqSubmit);
        _btnReqApprove = MakeToolbarButton(PurchaseRequestActions.Approve, BtnPurchaseApprove);
        _btnReqApprove.Width = 74;
        toolbar.Controls.Add(_btnReqApprove);
        _btnReqReject = MakeToolbarButton(PurchaseRequestActions.Reject, BtnPurchaseReject);
        _btnReqReject.Width = 74;
        toolbar.Controls.Add(_btnReqReject);
        _btnReqOrder = MakeToolbarButton(PurchaseRequestActions.Order, BtnPurchaseOrder);
        _btnReqOrder.Width = 74;
        toolbar.Controls.Add(_btnReqOrder);
        _btnReqClose = MakeToolbarButton(PurchaseRequestActions.Close, BtnPurchaseClose);
        _btnReqClose.Width = 74;
        toolbar.Controls.Add(_btnReqClose);

        var btnRefresh = MakeToolbarButton("刷新", (_, _) => LoadPurchaseRequests());
        btnRefresh.Width = 84;
        toolbar.Controls.Add(btnRefresh);

        // ---- 第二行：筛选 ----
        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10, 3, 6, 0),
            WrapContents = false
        };
        filterBar.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        _cboReqStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Margin = new Padding(0, 2, 12, 0) };
        _cboReqStatus.Items.Add("全部");
        foreach (var s in PurchaseRequestStatus.All) _cboReqStatus.Items.Add(s);
        _cboReqStatus.SelectedIndex = 0;
        _cboReqStatus.SelectedIndexChanged += (_, _) => LoadPurchaseRequests();
        filterBar.Controls.Add(_cboReqStatus);

        filterBar.Controls.Add(new Label { Text = "搜索：", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        _txtReqSearch = new TextBox { Width = 240, Margin = new Padding(0, 2, 8, 0) };
        _txtReqSearch.TextChanged += (_, _) => LoadPurchaseRequests();
        filterBar.Controls.Add(_txtReqSearch);

        _lblReqSummary = new Label
        {
            AutoSize = true,
            ForeColor = PrMuted,
            Margin = new Padding(12, 8, 0, 0),
            Text = "共 0 张申请单"
        };
        filterBar.Controls.Add(_lblReqSummary);

        // ---- 主体：左列表 / 右明细 ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = SystemColors.Control
        };
        split.SizeChanged += (_, _) =>
        {
            var want = Math.Max(420, Math.Min(660, split.Width - 560));
            if (want > 60 && split.Width - want - split.SplitterWidth > 220)
                split.SplitterDistance = want;
        };

        var listBox = MakeGridGroup("采购申请单（选中行看下方明细；按钮按单据状态自动启用）", out _gridReqList);
        SetupPurchaseListColumns();
        _gridReqList.SelectionChanged -= PurchaseList_SelectionChanged;
        _gridReqList.SelectionChanged += PurchaseList_SelectionChanged;
        split.Panel1.Controls.Add(listBox);

        split.Panel2.Controls.Add(BuildPurchaseDetailPanel());
        SetupPurchaseLineColumns();

        page.Controls.Add(split);      // Fill
        page.Controls.Add(filterBar);  // Top
        page.Controls.Add(toolbar);    // Top
        return page;
    }

    /// <summary>右侧：单据信息 + 明细表。</summary>
    private Control BuildPurchaseDetailPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6) };

        _lblReqInfo = new Label
        {
            Dock = DockStyle.Top,
            Height = 78,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = PrInk,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 6, 10, 6),
            Text = "请选择左侧的申请单查看明细与流转记录。"
        };

        var gridGroup = MakeGridGroup(
            "采购明细（需求量 = 单位用量 × (1+损耗率%) × 投产数；可用量不含待检区与退货待处理区）",
            out _gridReqLines);
        SetupPurchaseLineColumns();

        panel.Controls.Add(gridGroup);  // Fill
        panel.Controls.Add(_lblReqInfo); // Top
        return panel;
    }

    private void SetupPurchaseListColumns()
    {
        _gridReqList.AutoGenerateColumns = false;
        _gridReqList.Columns.Clear();
        _gridReqList.Columns.Add(MakeTextCol("ReqNo", "申请单号", true, 16));
        _gridReqList.Columns.Add(MakeTextCol("WoNo", "来源工单", true, 14));
        _gridReqList.Columns.Add(MakeTextCol("ProductModel", "产品型号", true, 12));
        _gridReqList.Columns.Add(MakeTextCol("StatusText", "状态", true, 8));
        _gridReqList.Columns.Add(MakeTextCol("LineCount", "明细行", true, 7));
        _gridReqList.Columns.Add(MakeTextCol("TotalQtyText", "申请量合计", true, 12));
        _gridReqList.Columns.Add(MakeTextCol("Applicant", "申请人", true, 8));
        var ct = MakeTextCol("CreateTime", "创建时间", true, 13);
        ct.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
        _gridReqList.Columns.Add(ct);

        _gridReqList.CellFormatting -= PurchaseList_CellFormatting;
        _gridReqList.CellFormatting += PurchaseList_CellFormatting;
    }

    private void SetupPurchaseLineColumns()
    {
        _gridReqLines.AutoGenerateColumns = false;
        _gridReqLines.Columns.Clear();
        _gridReqLines.Columns.Add(MakeTextCol("MaterialType", "类型", true, 6));
        _gridReqLines.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 17));
        _gridReqLines.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 14));
        _gridReqLines.Columns.Add(MakeTextCol("FormulaText", "需求公式", true, 19));
        _gridReqLines.Columns.Add(MakeTextCol("RequirementText", "需求量", true, 9));
        _gridReqLines.Columns.Add(MakeTextCol("AvailableText", "可用量", true, 9));
        _gridReqLines.Columns.Add(MakeTextCol("ShortageText", "缺口", true, 9));
        _gridReqLines.Columns.Add(MakeTextCol("RequestQtyText", "申请量", true, 9));
        _gridReqLines.Columns.Add(MakeTextCol("SupplierText", "建议供应商", true, 12));
        _gridReqLines.Columns.Add(MakeTextCol("Remark", "备注", true, 20));

        _gridReqLines.CellFormatting -= PurchaseLine_CellFormatting;
        _gridReqLines.CellFormatting += PurchaseLine_CellFormatting;
    }

    /// <summary>单据状态着色：待审批橙、已驳回红、已批准/已下单绿、已关闭灰。</summary>
    private void PurchaseList_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_gridReqList.Rows.Count == 0 || e.RowIndex < 0) return;
        if (_gridReqList.Rows[e.RowIndex].DataBoundItem is not PurchaseRequest r) return;

        switch (r.Status)
        {
            case PurchaseRequestStatus.Submitted:
                e.CellStyle.ForeColor = PrWarn;
                e.CellStyle.BackColor = Color.FromArgb(255, 251, 235);
                break;
            case PurchaseRequestStatus.Rejected:
                e.CellStyle.ForeColor = PrBad;
                e.CellStyle.BackColor = Color.FromArgb(254, 242, 242);
                break;
            case PurchaseRequestStatus.Approved:
            case PurchaseRequestStatus.Ordered:
                e.CellStyle.ForeColor = PrOk;
                break;
            case PurchaseRequestStatus.Closed:
                e.CellStyle.ForeColor = PrMuted;
                break;
            default:
                e.CellStyle.ForeColor = PrInk;
                break;
        }
    }

    /// <summary>明细着色：缺口 > 0 红底（还缺），否则绿字。</summary>
    private void PurchaseLine_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_gridReqLines.Rows.Count == 0 || e.RowIndex < 0) return;
        if (_gridReqLines.Rows[e.RowIndex].DataBoundItem is not PurchaseRequestLine l) return;

        if (l.StillShort)
        {
            e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
            e.CellStyle.ForeColor = PrBad;
        }
        else
        {
            e.CellStyle.ForeColor = PrOk;
        }
    }

    // ================= 数据加载 =================

    private void EnsurePurchaseReady()
    {
        if (_reqLoaded) return;
        _reqLoaded = true;
        _reqTip = new ToolTip();
        _reqTip.SetToolTip(_cboReqWo, "只列出「目前有物料缺口、且还没走请购流程」的工单。");
        _reqTip.SetToolTip(_btnReqApprove, "批准后进入可下单状态；若批准量仍不足，会在提示里点名。");
        _reqTip.SetToolTip(_btnReqReject, "驳回必须填写意见，申请人据此修改后重新提交。");
        LoadPurchaseRequests();
    }

    private void LoadPurchaseRequests()
    {
        LoadSourceWorkOrders();

        var status = _cboReqStatus.SelectedItem as string ?? "全部";
        var kw = _txtReqSearch?.Text.Trim() ?? "";

        var list = MesDatabase.PurchaseRequests
            .Where(r => status == "全部" || r.Status == status)
            .Where(r => kw.Length == 0
                        || r.ReqNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || r.WoNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || r.ProductModel.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || r.Applicant.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || r.Lines.Any(l => l.MaterialName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                         || l.MaterialCode.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                         || l.Supplier.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.CreateTime)
            .ThenBy(r => r.ReqNo, StringComparer.Ordinal)
            .ToList();

        var keep = _currentRequest?.ReqNo;
        _gridReqList.DataSource = list;

        var sum = MesDatabase.GetPurchaseRequestSummary();
        _lblReqSummary.Text = $"共 {list.Count} 张（全部 {sum.Total} 张）｜ {sum.PendingText}"
                              + (sum.Rejected > 0 ? $"｜ 待处理驳回 {sum.Rejected} 张" : "");

        // 尽量保持原有选中项
        if (keep is not null)
        {
            foreach (DataGridViewRow row in _gridReqList.Rows)
            {
                if (row.DataBoundItem is PurchaseRequest p && p.ReqNo == keep)
                {
                    row.Selected = true;
                    _gridReqList.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }
        if (_gridReqList.SelectedRows.Count == 0 && _gridReqList.Rows.Count > 0)
        {
            _gridReqList.ClearSelection();
            _gridReqList.Rows[0].Selected = true;
            _gridReqList.CurrentCell = _gridReqList.Rows[0].Cells[0];
        }
        OnPurchaseSelectionChanged();
    }

    private void LoadSourceWorkOrders()
    {
        var keep = _cboReqWo.SelectedItem as string;
        _cboReqWo.Items.Clear();
        foreach (var wo in MesDatabase.GetWorkOrdersWithShortage()) _cboReqWo.Items.Add(wo.WoNo);

        if (_cboReqWo.Items.Count == 0) return;
        var idx = keep is null ? -1 : _cboReqWo.Items.IndexOf(keep);
        _cboReqWo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void PurchaseList_SelectionChanged(object? sender, EventArgs e) => OnPurchaseSelectionChanged();

    private void OnPurchaseSelectionChanged()
    {
        _currentRequest = _gridReqList.SelectedRows.Count > 0
            ? _gridReqList.SelectedRows[0].DataBoundItem as PurchaseRequest
            : null;

        if (_currentRequest is null)
        {
            _gridReqLines.DataSource = null;
            _lblReqInfo.Text = "请选择左侧的申请单查看明细与流转记录。";
            RefreshPurchaseActionButtons();
            return;
        }

        _gridReqLines.DataSource = _currentRequest.Lines;

        var r = _currentRequest;
        var sb = new System.Text.StringBuilder();
        sb.Append($"{r.ReqNo}　{r.StatusText}　{(r.AutoCreated ? "工单缺料自动生成" : "手工创建")}").Append('\n');
        sb.Append($"来源工单：{r.WoNo}");
        if (r.ErpOrderNo.Length > 0) sb.Append($"　ERP 订单：{r.ErpOrderNo}");
        sb.Append($"　型号：{r.ProductModel}").Append('\n');
        sb.Append($"申请人：{r.Applicant}　创建：{r.CreateTimeText}");
        if (r.SubmitTime is not null) sb.Append($"　提交：{r.SubmitTimeText}");
        sb.Append('\n');
        if (r.ApproveTime is not null)
            sb.Append($"审批：{r.Approver}　{r.ApproveTimeText}");
        else
            sb.Append("审批：—");
        if (r.ApprovalRemark.Length > 0) sb.Append($"　意见：{r.ApprovalRemark}");
        if (r.Remark.Length > 0) sb.Append($"\n备注：{r.Remark}");

        _lblReqInfo.Text = sb.ToString();
        RefreshPurchaseActionButtons();
    }

    /// <summary>按单据状态刷新流程按钮的启用态与文案（流程约束在数据层，这里只呈现）。</summary>
    private void RefreshPurchaseActionButtons()
    {
        var status = _currentRequest?.Status;
        void Set(Button b, string action)
        {
            var allowed = PurchaseRequestActions.CanDo(status, action);
            b.Enabled = allowed;
            b.Text = allowed ? action : ActionDisabledText(action);
        }
        Set(_btnReqSubmit, PurchaseRequestActions.Submit);
        Set(_btnReqApprove, PurchaseRequestActions.Approve);
        Set(_btnReqReject, PurchaseRequestActions.Reject);
        Set(_btnReqOrder, PurchaseRequestActions.Order);
        Set(_btnReqClose, PurchaseRequestActions.Close);
    }

    /// <summary>不可执行的动作给出原因，而不是让按钮默默灰着让人猜。</summary>
    private static string ActionDisabledText(string action) => action switch
    {
        PurchaseRequestActions.Submit => "仅草稿/驳回可提交",
        PurchaseRequestActions.Approve => "仅待审批可批准",
        PurchaseRequestActions.Reject => "仅待审批可驳回",
        PurchaseRequestActions.Order => "仅已批准可下单",
        PurchaseRequestActions.Close => "已批准/已下单可关闭",
        _ => action
    };

    // ================= 交互 =================

    private void BtnPurchaseGenerate(object? sender, EventArgs e)
    {
        var woNo = _cboReqWo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(woNo))
        {
            Warn("当前没有「有缺口且未走请购流程」的工单。\n可在「智能排产」页新建工单，或等待物料齐套。");
            return;
        }

        var (ok, msg, _) = MesDatabase.BuildRequestFromWorkOrder(woNo, "计划员", "工单缺料自动生成");
        if (!ok)
        {
            MessageBox.Show(msg, "未能生成采购申请", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(msg + "\n\n可在左侧点「提交」送审。", "已生成采购申请",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        _currentRequest = null;
        LoadPurchaseRequests();
    }

    private void BtnPurchaseSubmit(object? sender, EventArgs e) =>
        ApplyRequestAction(reqNo => MesDatabase.SubmitRequest(reqNo, "计划员"), "提交");

    private void BtnPurchaseApprove(object? sender, EventArgs e)
    {
        var r = _currentRequest;
        if (r is null) return;
        var remark = InputBox("审批意见", $"批准申请 {r.ReqNo}", "同意，按申请量采购。");
        if (remark is null) return;   // 用户取消
        ApplyRequestAction(reqNo => MesDatabase.ApproveRequest(reqNo, "采购主管", remark), "批准");
    }

    private void BtnPurchaseReject(object? sender, EventArgs e)
    {
        var r = _currentRequest;
        if (r is null) return;
        var remark = InputBox("驳回意见（必填）", $"驳回申请 {r.ReqNo}", "");
        if (remark is null) return;              // 取消
        if (remark.Trim().Length == 0)
        {
            Warn("驳回必须填写意见，否则申请人不知道要改什么。");
            return;
        }
        ApplyRequestAction(reqNo => MesDatabase.RejectRequest(reqNo, "采购主管", remark), "驳回");
    }

    private void BtnPurchaseOrder(object? sender, EventArgs e) =>
        ApplyRequestAction(reqNo => MesDatabase.OrderRequest(reqNo, "计划员"), "下单");

    private void BtnPurchaseClose(object? sender, EventArgs e) =>
        ApplyRequestAction(reqNo => MesDatabase.CloseRequest(reqNo, "计划员", "手工关闭"), "关闭");

    /// <summary>执行一次状态流转并刷新界面。所有结果都把数据层返回的原因原样呈现。</summary>
    private void ApplyRequestAction(Func<string, (bool Ok, string Message)> action, string actionName)
    {
        var r = _currentRequest;
        if (r is null)
        {
            Warn("请先选择一张采购申请单。");
            return;
        }

        var (ok, msg) = action(r.ReqNo);
        if (!ok)
        {
            MessageBox.Show(msg, $"未能{actionName}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshPurchaseActionButtons();
            return;
        }

        _currentRequest = MesDatabase.FindPurchaseRequest(r.ReqNo);
        LoadPurchaseRequests();
        Warn(msg);
    }

    /// <summary>简单的单行输入对话框（项目里没有现成的输入框窗体）。取消返回 null。</summary>
    private static string? InputBox(string title, string prompt, string initial)
    {
        using var dlg = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(430, 150),
            Font = new Font("微软雅黑", 9F)
        };
        var lbl = new Label { Text = prompt, Location = new Point(16, 14), AutoSize = true, ForeColor = Color.FromArgb(60, 70, 85) };
        var txt = new TextBox { Location = new Point(16, 40), Width = 398, Text = initial };
        var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(234, 100), Width = 88, Height = 30 };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(330, 100), Width = 84, Height = 30 };
        dlg.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        dlg.AcceptButton = btnOk;
        dlg.CancelButton = btnCancel;
        return dlg.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }
}