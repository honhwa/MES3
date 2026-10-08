using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分，第 29 部分）：智能排产页
///
/// 这一页要做的事，用一句话讲清：
///   选一张待下发的 ERP 订单 → 从**历史工单**里统计出良率与节拍，推出「投产多少、哪条线、几号开工几号完工」
///   → 同时按 BOM 展开物料需求，逐项比对库存，缺料就把缺口和处置建议摆出来。
///
/// 为什么把「推理依据」做成一个必须看得见的区域：
///   排产结论是要拿去开工的，必须可审计。「为什么建议投产 6071 只而不是 6000 只」
///   要能一路追到具体的历史工单号与公式——所以依据不是折叠在 tooltip 里，而是常驻列表。
/// </summary>
public partial class MainForm
{
    private static readonly Color SchInk = Color.FromArgb(30, 41, 59);
    private static readonly Color SchMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color SchOk = Color.FromArgb(21, 128, 61);
    private static readonly Color SchWarn = Color.FromArgb(217, 119, 6);
    private static readonly Color SchBad = Color.FromArgb(220, 38, 38);

    // ================= 页面构建 =================

    /// <summary>构建「智能排产」页签：PO 选择 → 排产建议 + 推理依据 + 齐套校验。</summary>
    private TabPage BuildSchedulingTab()
    {
        var page = new TabPage("智能排产");

        // ---- 顶部：选订单 + 动作 ----
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(10, 9, 6, 0),
            WrapContents = false
        };
        toolbar.Controls.Add(new Label { Text = "ERP 订单：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });

        _cboSchedulePo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 470,
            Margin = new Padding(0, 4, 10, 0),
            DropDownWidth = 470   // 订单描述较长，展开宽度与框宽一致，避免看不全
        };
        _cboSchedulePo.Format += (_, e) =>
        {
            if (e.ListItem is not string no) return;
            var po = MesDatabase.FindErpPo(no);
            if (po is null) return;
            e.Value = $"{po.PoNo} ｜ {po.Customer} ｜ {po.ProductModel} ｜ {po.OrderQty} 只 ｜ 交期 {po.DueDate:yyyy-MM-dd}";
        };
        _cboSchedulePo.SelectedIndexChanged += (_, _) =>
        {
            if (_scheduleLoaded) GenerateSchedule();
        };
        toolbar.Controls.Add(_cboSchedulePo);

        _btnScheduleGen = MakeToolbarButton("⚡ 生成排产建议", BtnScheduleGenerate);
        _btnScheduleGen.Width = 142;
        toolbar.Controls.Add(_btnScheduleGen);

        _btnScheduleCreate = MakeToolbarButton("✅ 按建议生成工单", BtnScheduleCreateWorkOrder);
        _btnScheduleCreate.Width = 158;
        _btnScheduleCreate.Enabled = false;   // 没有建议前不给点，避免误生成
        toolbar.Controls.Add(_btnScheduleCreate);

        _btnScheduleAi = MakeToolbarButton("🤖 AI 推演", BtnScheduleAiAdvise);
        _btnScheduleAi.Width = 108;
        toolbar.Controls.Add(_btnScheduleAi);

        var btnRefresh = MakeToolbarButton("刷新", (_, _) => LoadSchedulingPos());
        btnRefresh.Width = 84;
        toolbar.Controls.Add(btnRefresh);

        // ---- 结论条 ----
        var conclusion = new Panel
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = Color.White,
            Padding = new Padding(14, 6, 14, 6)
        };
        _lblScheduleHeadline = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            AutoSize = false,
            Font = new Font("微软雅黑", 11.5F, FontStyle.Bold),
            ForeColor = SchInk,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "请选择一张待下发的 ERP 订单，点「生成排产建议」。"
        };
        _lblScheduleRisk = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = SchMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "口径：可用库存不含待检区与退货待处理区；1 个生产日按 16h 有效产能折算（未含周末与节假日）。"
        };
        _lblScheduleResource = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = SchMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "设备占用：生成建议后自动校验设备/工装。"
        };
        _lblScheduleAi = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = SchMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = ""
        };
        conclusion.Controls.Add(_lblScheduleAi);      // Bottom
        conclusion.Controls.Add(_lblScheduleResource);// Bottom
        conclusion.Controls.Add(_lblScheduleRisk);    // Fill
        conclusion.Controls.Add(_lblScheduleHeadline);// Top

        // ---- 主体：左=结论与依据，右=齐套校验 ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = SystemColors.Control
        };
        // 构造期不设 SplitterDistance：容器宽度还是默认值，设了会与最小尺寸冲突直接抛异常。
        // 改到 SizeChanged 里按实际宽度设置（带钳制）。
        split.SizeChanged += (_, _) =>
        {
            // 左栏留到 620 是为了让四张卡排成 2×2 而不是 1×4（1×4 会把下面的依据区压得太扁）
            var want = Math.Max(340, Math.Min(620, split.Width - 520));
            if (want > 60 && split.Width - want - split.SplitterWidth > 200)
                split.SplitterDistance = want;
        };

        split.Panel1.Controls.Add(BuildScheduleConclusionPanel());

        // 右侧改成两个子页：物料齐套 / 资产占用（同一套口径，两个维度分开看）
        var resTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("微软雅黑", 9F) };
        var matPage = new TabPage("资材占用（跨工单汇总）");
        matPage.Padding = new Padding(4);
        matPage.Controls.Add(MakeGridGroup(
            "资材占用（主体）：占用 = 单位用量 × (1+损耗率%) × 剩余待产量，跨工单无条件累加；可用量不含待检区与退货待处理区"
            + "（物料是消耗品，时间窗错开也要备足，故不按时间窗判冲突）",
            out _gridScheduleStock));
        SetupScheduleStockColumns();

        var assetPage = new TabPage("设备占用");
        assetPage.Padding = new Padding(4);
        assetPage.Controls.Add(MakeGridGroup(
            "设备占用（辅助）：设备/工装是可复用资源，按并发容量与时间窗判冲突，并给出可选方案",
            out _gridScheduleAsset));
        SetupScheduleAssetColumns();

        resTabs.TabPages.Add(matPage);
        resTabs.TabPages.Add(assetPage);
        split.Panel2.Controls.Add(resTabs);

        page.Controls.Add(split);         // Fill
        page.Controls.Add(conclusion);    // Top
        page.Controls.Add(toolbar);       // Top
        return page;
    }

    /// <summary>左侧面板：4 张结论卡 + 齐套汇总 + 推理依据列表。</summary>
    private Control BuildScheduleConclusionPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 4, 4) };

        var cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            // 按内容自动增高：容器窄时 4 张卡会排成 1 列 4 行，写死高度会把后两张裁掉
            // （这个坑与"构造期不物化"同源——尺寸只能等布局算完，不能提前假定）
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        cards.Controls.Add(MakeCard("推荐线体", SchInk, out _lblScheduleLine));
        cards.Controls.Add(MakeCard("投产数量", SchInk, out _lblScheduleInputQty));
        cards.Controls.Add(MakeCard("预计完工", SchInk, out _lblScheduleFinish));
        cards.Controls.Add(MakeCard("交期余量", SchInk, out _lblScheduleSlack));

        _lblScheduleStock = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            AutoSize = false,
            Font = new Font("微软雅黑", 9F),
            ForeColor = SchMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "齐套校验：生成建议后自动展开 BOM 逐项比对。"
        };

        var evidenceBox = new GroupBox
        {
            Text = "推理依据（逐条可追问：样本 / 公式 / 口径）",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 12, 6, 6)
        };
        _lstScheduleEvidence = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 9F),
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        evidenceBox.Controls.Add(_lstScheduleEvidence);

        panel.Controls.Add(evidenceBox);      // Fill 必须最先加
        panel.Controls.Add(_lblScheduleStock); // Top
        panel.Controls.Add(cards);             // Top
        return panel;
    }

    /// <summary>齐套校验表列定义（显式列名，供绑定与着色）。</summary>
    private void SetupScheduleStockColumns()
    {
        _gridScheduleStock.AutoGenerateColumns = false;
        _gridScheduleStock.Columns.Clear();
        _gridScheduleStock.Columns.Add(MakeTextCol("MaterialType", "类型", true, 6));
        _gridScheduleStock.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 16));
        _gridScheduleStock.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 13));
        _gridScheduleStock.Columns.Add(MakeTextCol("TotalText", "占用总量", true, 11));
        _gridScheduleStock.Columns.Add(MakeTextCol("WoCountText", "工单数", true, 8));
        _gridScheduleStock.Columns.Add(MakeTextCol("AvailableText", "可用库存", true, 10));
        _gridScheduleStock.Columns.Add(MakeTextCol("InTransitText", "在途采购", true, 10));
        _gridScheduleStock.Columns.Add(MakeTextCol("ShortageText", "需采购", true, 10));
        _gridScheduleStock.Columns.Add(MakeTextCol("StatusText", "状态", true, 8));
        _gridScheduleStock.Columns.Add(MakeTextCol("DetailsText", "占用明细（工单 / 数量 / 起止时间）", true, 40));
        _gridScheduleStock.Columns.Add(MakeTextCol("CrowdingText", "时段挤兑提示", true, 28));

        _gridScheduleStock.CellFormatting -= ScheduleStock_CellFormatting;
        _gridScheduleStock.CellFormatting += ScheduleStock_CellFormatting;
    }

    /// <summary>资产占用表列定义。</summary>
    private void SetupScheduleAssetColumns()
    {
        _gridScheduleAsset.AutoGenerateColumns = false;
        _gridScheduleAsset.Columns.Clear();
        _gridScheduleAsset.Columns.Add(MakeTextCol("Process", "工序", true, 7));
        _gridScheduleAsset.Columns.Add(MakeTextCol("AssetName", "资产", true, 15));
        _gridScheduleAsset.Columns.Add(MakeTextCol("AssetCode", "编码", true, 13));
        _gridScheduleAsset.Columns.Add(MakeTextCol("NeedText", "本单需求", true, 9));
        _gridScheduleAsset.Columns.Add(MakeTextCol("AvailableText", "可用余量", true, 9));
        _gridScheduleAsset.Columns.Add(MakeTextCol("StatusText", "状态", true, 9));
        _gridScheduleAsset.Columns.Add(MakeTextCol("ConflictText", "冲突占用明细", true, 30));
        _gridScheduleAsset.Columns.Add(MakeTextCol("OptionText", "可选方案", true, 34));

        _gridScheduleAsset.CellFormatting -= ScheduleAsset_CellFormatting;
        _gridScheduleAsset.CellFormatting += ScheduleAsset_CellFormatting;
    }

    private void ScheduleAsset_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_gridScheduleAsset.Rows.Count == 0 || e.RowIndex < 0) return;
        if (_gridScheduleAsset.Rows[e.RowIndex].DataBoundItem is not AssetCheckLine l) return;
        switch (l.Status)
        {
            case AssetCheckStatus.Conflict:
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = SchBad;
                break;
            case AssetCheckStatus.Insufficient:
                e.CellStyle.BackColor = Color.FromArgb(255, 247, 237);
                e.CellStyle.ForeColor = SchWarn;
                break;
            default:
                e.CellStyle.ForeColor = SchOk;
                break;
        }
    }

    /// <summary>齐套状态着色：短缺红底、紧张黄底、满足绿字。</summary>
    private void ScheduleStock_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_gridScheduleStock.Rows.Count == 0 || e.RowIndex < 0) return;
        if (_gridScheduleStock.Rows[e.RowIndex].DataBoundItem is not MaterialOccupancyRow r) return;

        if (r.NetShortage > 1e-9)
        {
            e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
            e.CellStyle.ForeColor = SchBad;
        }
        else if (r.Status == ShortageStatus.Tight)
            e.CellStyle.ForeColor = SchWarn;
        else
            e.CellStyle.ForeColor = SchOk;
    }

    // ================= 数据加载 =================

    /// <summary>排产页首次加载守卫（幂等；与 AI 助手页同一套约定）。</summary>
    private void EnsureScheduleReady()
    {
        if (_scheduleLoaded) return;
        _scheduleLoaded = true;
        _scheduleTip = new ToolTip();
        _scheduleTip.SetToolTip(_cboSchedulePo, "只列出「待下发」的 ERP 订单；已生成过工单的订单不再出现。");
        _scheduleTip.SetToolTip(_btnScheduleCreate, "按建议生成工单（工单号 = WO-订单号）。缺料时会先弹预警并要你确认。");
        LoadSchedulingPos();
    }

    /// <summary>载入待下发 PO 列表（已下发的不再重复排产）。</summary>
    private void LoadSchedulingPos()
    {
        var keep = _cboSchedulePo.SelectedItem as string;
        _cboSchedulePo.Items.Clear();

        var pending = MesDatabase.ErpPos
            .Where(p => p.Status == ErpPoStatus.Pending)
            .OrderBy(p => p.DueDate)
            .ToList();
        foreach (var po in pending) _cboSchedulePo.Items.Add(po.PoNo);

        _btnScheduleCreate.Enabled = false;
        _currentSchedule = null;

        if (_cboSchedulePo.Items.Count == 0)
        {
            _lblScheduleHeadline.Text = "当前没有待下发的 ERP 订单。可到「工单管理 › ERP 下发」查看已下发的订单。";
            _lblScheduleRisk.Text = "口径：可用库存不含待检区与退货待处理区；1 个生产日按 16h 有效产能折算。";
            _lblScheduleRisk.ForeColor = SchMuted;
            _gridScheduleStock.DataSource = null;
            _lstScheduleEvidence.Items.Clear();
            ResetScheduleCards();
            return;
        }

        // 恢复原选中项；没有就选交期最近的那张（计划员通常先看最紧的）
        var idx = keep is null ? -1 : _cboSchedulePo.Items.IndexOf(keep);
        _cboSchedulePo.SelectedIndex = idx >= 0 ? idx : 0;
        if (idx < 0) GenerateSchedule();
    }

    private void ResetScheduleCards()
    {
        _lblScheduleLine.Text = "—";
        _lblScheduleInputQty.Text = "—";
        _lblScheduleFinish.Text = "—";
        _lblScheduleSlack.Text = "—";
        _lblScheduleLine.ForeColor = SchInk;
        _lblScheduleInputQty.ForeColor = SchInk;
        _lblScheduleFinish.ForeColor = SchInk;
        _lblScheduleSlack.ForeColor = SchInk;
    }

    /// <summary>生成当前选中 PO 的排产建议并渲染。</summary>
    private void GenerateSchedule()
    {
        var poNo = _cboSchedulePo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(poNo)) return;

        var rec = MesDatabase.RecommendSchedule(poNo);
        if (rec is null)
        {
            _lblScheduleHeadline.Text = $"ERP 中不存在订单 {poNo}，请刷新后重试。";
            _btnScheduleCreate.Enabled = false;
            _currentSchedule = null;
            return;
        }

        _currentSchedule = rec;

        _lblScheduleHeadline.Text = rec.Headline;

        // 风险等级必须以标签形式直接给出，不能只靠"比交期晚 3 天"这种描述让用户自己推断
        var riskTag = rec.Risk switch
        {
            ScheduleRisk.Late => "【来不及】",
            ScheduleRisk.Tight => "【偏紧】",
            _ => "【正常】"
        };
        _lblScheduleRisk.Text = riskTag + rec.RiskText;
        _lblScheduleRisk.ForeColor = rec.Risk switch
        {
            ScheduleRisk.Late => SchBad,
            ScheduleRisk.Tight => SchWarn,
            _ => SchOk
        };

        _lblScheduleLine.Text = rec.RecommendedLine;
        _lblScheduleInputQty.Text = rec.InputQty.ToString();
        _lblScheduleFinish.Text = rec.PlannedFinish.ToString("MM-dd HH:mm");
        _lblScheduleSlack.Text = rec.SlackDays >= 0 ? $"+{rec.SlackDays:0.#} 天" : $"{rec.SlackDays:0.#} 天";
        _lblScheduleSlack.ForeColor = rec.Risk switch
        {
            ScheduleRisk.Late => SchBad,
            ScheduleRisk.Tight => SchWarn,
            _ => SchOk
        };

        // 齐套汇总：缺料时直接点名，不藏在表格里等人自己找
        var mo = rec.MaterialOccupancy;
        _lblScheduleStock.Text = mo.Aggregated.Count == 0
            ? "资材占用：生成建议后按「工单 + BOM」自动计算并跨工单汇总。"
            : mo.AllSatisfied
                ? $"资材占用：{mo.Lines.Count} 项占用 / {mo.Aggregated.Count} 种物料，跨工单累加后全部覆盖，可正常开工。"
                  + (mo.CrowdingCount > 0 ? $"（{mo.CrowdingCount} 项时段挤兑，不影响总量）" : "")
                : $"资材占用：{mo.ShortageCount} 种需采购 → {PurchaseBriefOf(rec)}。建议先补料。";
        _lblScheduleStock.ForeColor = mo.Aggregated.Count == 0 || mo.AllSatisfied ? SchOk : SchBad;

        _gridScheduleStock.DataSource = rec.MaterialOccupancy.Aggregated;

        // 资产占用：齐套不等于能开工——设备被别的工单占着，照样排不了
        var res = rec.ResourceCheck;
        _gridScheduleAsset.DataSource = res?.AssetLines;
        if (res is null || res.AssetLines.Count == 0)
        {
            _lblScheduleResource.Text = "设备占用：该型号未匹配到需占用的设备/工装。";
            _lblScheduleResource.ForeColor = SchMuted;
        }
        else if (res.AssetOk)
        {
            _lblScheduleResource.Text = $"设备占用：✓ {res.AssetLines.Count} 项设备/工装在计划时段均有余量。";
            _lblScheduleResource.ForeColor = SchOk;
        }
        else
        {
            var names = string.Join("、", res.AssetLines
                .Where(l => l.Status != AssetCheckStatus.Ok)
                .Select(l => $"{l.AssetName}({(l.Status == AssetCheckStatus.Conflict ? "冲突" : "余量不足")})")
                .Take(3));
            _lblScheduleResource.Text = $"设备占用：⚠ {res.ConflictCount} 项时间冲突 / {res.InsufficientCount} 项余量不足 —— {names}。见「设备占用」页可选方案。";
            _lblScheduleResource.ForeColor = SchBad;
        }

        _lblScheduleAi.Text = "";   // 换订单时清掉上一张的 AI 推演结果

        _lstScheduleEvidence.BeginUpdate();
        try
        {
            _lstScheduleEvidence.Items.Clear();
            foreach (var e in rec.Evidence) _lstScheduleEvidence.Items.Add(e);
            if (_lstScheduleEvidence.Items.Count > 0) _lstScheduleEvidence.SelectedIndex = 0;
        }
        finally
        {
            _lstScheduleEvidence.EndUpdate();
        }

        // 已下发的订单不给生成入口（防重复建单）
        _btnScheduleCreate.Enabled = !rec.StockCheck.NoBom || rec.MaterialReady;
        _btnScheduleCreate.Text = rec.MaterialReady ? "✅ 按建议生成工单" : "⚠ 缺料仍要建单";
    }

    // ================= 交互 =================

    /// <summary>资材占用的采购摘要（跨工单汇总后需采购的物料，3 条封顶）</summary>
    private static string PurchaseBriefOf(ScheduleRecommendation rec)
    {
        var list = rec.MaterialOccupancy.Aggregated
            .Where(r => r.NetShortage > 1e-9)
            .OrderByDescending(r => r.NetShortage)
            .ToList();
        if (list.Count == 0) return "无";
        return string.Join("、", list.Take(3).Select(r => $"{r.MaterialName} 需购 {r.ShortageText}"))
               + (list.Count > 3 ? $" 等 {list.Count} 种" : "");
    }

    private void BtnScheduleGenerate(object? sender, EventArgs e) => GenerateSchedule();

    /// <summary>
    /// AI 推演：把**规则已算好的**结论 + 依据交给云端模型，请它讲成主管能直接决策的话。
    /// 数字仍全部来自规则；模型若编了素材外的数字，返回会被整段丢弃并自动降级（结果区标明来源）。
    /// </summary>
    private async void BtnScheduleAiAdvise(object? sender, EventArgs e)
    {
        var rec = _currentSchedule;
        if (rec is null) { Warn("请先生成排产建议。"); return; }

        _btnScheduleAi.Enabled = false;
        _btnScheduleAi.Text = "推演中…";
        _lblScheduleAi.Text = "正在请求云端大模型…（数字仍以规则计算为准）";
        try
        {
            LlmCallResult<ScheduleAdviseResult> r;
            try { r = await Task.Run(() => AiTasks.AdviseSchedule(rec)); }
            catch (Exception ex) { r = new LlmCallResult<ScheduleAdviseResult> { Ok = false, Source = LlmSources.Rule, Reason = ex.Message }; }

            if (!r.Ok || r.Value is null)
            {
                _lblScheduleAi.Text = r.TraceText;
                _lblScheduleAi.ForeColor = SchWarn;
                return;
            }

            var v = r.Value;
            _lblScheduleAi.Text = $"{r.TraceText}｜{v.Conclusion}";
            _lblScheduleAi.ForeColor = SchOk;

            // 推演结果追加进「推理依据」列表：与规则依据放在一起，方便对照"哪些是算的、哪些是模型讲的"
            _lstScheduleEvidence.BeginUpdate();
            try
            {
                _lstScheduleEvidence.Items.Add($"—— 以下为 AI 推演（{r.Source}，置信度 {r.Confidence:P0}）——");
                _lstScheduleEvidence.Items.Add("【结论】" + v.Conclusion);
                foreach (var a in v.Actions) _lstScheduleEvidence.Items.Add("【建议】" + a);
                foreach (var k in v.Risks) _lstScheduleEvidence.Items.Add("【风险】" + k);
            }
            finally
            {
                _lstScheduleEvidence.EndUpdate();
            }
        }
        finally
        {
            _btnScheduleAi.Enabled = true;
            _btnScheduleAi.Text = "🤖 AI 推演";
        }
    }

    /// <summary>
    /// 按排产建议生成工单。
    /// 缺料 / 交期风险都会先弹窗确认——**不静默拦截也不静默放行**：
    /// 拦截太死会挡住紧急插单，放行不提示等于纵然缺料开工，两者都不可接受，所以给一次明确选择。
    /// </summary>
    private void BtnScheduleCreateWorkOrder(object? sender, EventArgs e)
    {
        var rec = _currentSchedule;
        if (rec is null)
        {
            Warn("请先生成排产建议。");
            return;
        }

        var po = MesDatabase.FindErpPo(rec.PoNo);
        if (po is null || po.Status == ErpPoStatus.Pushed)
        {
            Warn($"订单 {rec.PoNo} 已下发或不存在，请刷新列表后再试。");
            return;
        }

        var force = false;
        var blocks = new List<string>();
        if (rec.Risk == ScheduleRisk.Late) blocks.Add($"· 交期风险：{rec.RiskText}");
        if (!rec.StockCheck.NoBom && !rec.MaterialReady)
            blocks.Add($"· 物料不齐套：缺 {rec.StockCheck.ShortageBrief}\n  缺料开工会造成生产中断或半成品积压。");
        if (rec.ResourceCheck is { AssetLines.Count: > 0 } rc && !rc.AssetOk)
        {
            foreach (var l in rc.AssetLines.Where(x => x.Status != AssetCheckStatus.Ok).Take(3))
            {
                var t = $"· 资产占用{l.StatusText}：{l.AssetName}（{l.AssetCode}／{l.Process}）"
                        + (l.Conflicts.Count > 0
                            ? "，已被 " + string.Join("、", l.Conflicts.Select(c => $"工单 {c.WoNo}（{c.WindowText}）")) + " 占用"
                            : $"，可用余量 {l.AvailableText} 不足本单所需 {l.NeedText}");
                var opts = l.Options.Where(o => o.Feasible).Select(o => o.Text).ToList();
                if (opts.Count > 0) t += "\n  可选方案：" + string.Join("；", opts.Take(2));
                blocks.Add(t);
            }
        }

        if (blocks.Count > 0)
        {
            var askMsg = $"订单 {rec.PoNo} 存在以下情况：\n\n{string.Join("\n\n", blocks)}\n\n仍要按此建议创建工单吗？";
            if (MessageBox.Show(askMsg, "创建工单前确认", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                _lblScheduleRisk.Text = "已取消创建：" + askMsg.Replace("\n", " ");
                _lblScheduleRisk.ForeColor = SchMuted;
                return;
            }
            force = true;
        }

        var (ok, msg, woNo) = MesDatabase.CreateWorkOrderFromRecommendation(
            rec, rec.RecommendedLine, "计划员", force);

        if (!ok)
        {
            MessageBox.Show(msg, "未能创建工单", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(msg, "已生成工单", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // 生成后该 PO 已下发，从待下发列表移除并禁用按钮，避免重复建单
        _btnScheduleCreate.Enabled = false;
        LoadSchedulingPos();
        RefreshAll();
        var wo = MesDatabase.WorkOrders.FirstOrDefault(w => w.WoNo == woNo);
        if (wo is not null)
        {
            GoTab("工单管理");
            SelectRow(_woGrid, wo);   // 选中新建的工单，行李箱里立刻能看到
        }
    }
}
