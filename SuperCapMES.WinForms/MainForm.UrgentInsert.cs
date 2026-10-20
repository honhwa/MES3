using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类：紧急插单模拟页（技术方案 2.3）。
/// 选紧急订单 → 模拟「老单延后 / 拆分插空 / 转次优线」三方案 → 四维评分推荐 → 查看受影响老单。
/// </summary>
public partial class MainForm
{
    private ComboBox _cboUrgentPo = null!;
    private FlowLayoutPanel _urgentCards = null!;
    private DataGridView _gridUrgentAffected = null!;
    private Label _lblUrgentNote = null!;
    private UrgentInsertSimulation? _urgentSim;

    private TabPage BuildUrgentInsertTab()
    {
        var page = new TabPage("插单模拟");
        page.Padding = new Padding(6);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6, 8, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "紧急订单：", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        _cboUrgentPo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460, Margin = new Padding(0, 4, 10, 0), DropDownWidth = 460 };
        _cboUrgentPo.Format += (_, e) =>
        {
            if (e.ListItem is not string no) return;
            var po = MesDatabase.FindErpPo(no);
            if (po is null) return;
            e.Value = $"{po.PoNo} ｜ {po.Customer} ｜ {po.ProductModel} ｜ {po.OrderQty} 只 ｜ 交期 {po.DueDate:yyyy-MM-dd}";
        };
        bar.Controls.Add(_cboUrgentPo);
        bar.Controls.Add(MakeToolbarButton("🧩 模拟插单", (_, _) => SimulateUrgent()));
        _lblUrgentNote = new Label
        {
            AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(14, 11, 0, 0),
            Text = "选一张紧急订单，模拟三种局部重排方案并给出评分推荐。"
        };
        bar.Controls.Add(_lblUrgentNote);

        _urgentCards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 224, Padding = new Padding(6, 4, 6, 4),
            WrapContents = true, AutoScroll = true
        };

        var gridGroup = MakeGridGroup("受影响的老工单（选中方案的局部重排结果）", out _gridUrgentAffected);
        SetupUrgentColumns();

        var foot = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 48, ForeColor = Color.Gray,
            Padding = new Padding(6, 6, 6, 0),
            Text = "口径：紧急插单只做冲突局部重排（不全局打乱）；三方案为规则 What-if 模拟，评分权重 准时40% > 老单影响30% > 换型20% > 负载10%。「应用建议」为演示，不改真实排程。"
        };

        page.Controls.Add(gridGroup);    // Fill
        page.Controls.Add(_urgentCards); // Top
        page.Controls.Add(bar);          // Top（最后加 → 最上）
        page.Controls.Add(foot);         // Bottom

        page.Enter -= UrgentTab_Enter;
        page.Enter += UrgentTab_Enter;
        LoadUrgentPos();
        return page;
    }

    private void SetupUrgentColumns()
    {
        _gridUrgentAffected.AutoGenerateColumns = false;
        _gridUrgentAffected.Columns.Clear();
        _gridUrgentAffected.Columns.Add(MakeTextCol("WoNo", "工单", true, 14));
        _gridUrgentAffected.Columns.Add(MakeTextCol("ProductModel", "产品型号", true, 14));
        _gridUrgentAffected.Columns.Add(MakeTextCol("DueText", "交期", true, 8));
        _gridUrgentAffected.Columns.Add(MakeTextCol("OldFinishText", "原完工", true, 11));
        _gridUrgentAffected.Columns.Add(MakeTextCol("NewFinishText", "插单后完工", true, 11));
        _gridUrgentAffected.Columns.Add(MakeTextCol("DelayText", "交期影响", true, 11));
    }

    private void UrgentTab_Enter(object? sender, EventArgs e) => LoadUrgentPos();

    private void LoadUrgentPos()
    {
        if (_cboUrgentPo is null) return;
        var keep = _cboUrgentPo.SelectedItem as string;
        _cboUrgentPo.Items.Clear();
        foreach (var po in MesDatabase.ErpPos.Where(p => p.Status == ErpPoStatus.Pending).OrderBy(p => p.DueDate))
            _cboUrgentPo.Items.Add(po.PoNo);
        if (_cboUrgentPo.Items.Count == 0)
        {
            if (_lblUrgentNote is not null) _lblUrgentNote.Text = "当前没有待下发的 ERP 订单。";
            return;
        }
        var idx = keep is null ? 0 : Math.Max(0, _cboUrgentPo.Items.IndexOf(keep));
        _cboUrgentPo.SelectedIndex = idx;
    }

    private void SimulateUrgent()
    {
        var no = _cboUrgentPo?.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(no)) { Warn("请先选择一张紧急订单。"); return; }
        var sim = MesDatabase.SimulateUrgentInsert(no);
        if (sim is null) { Warn("订单不存在，请刷新后重试。"); return; }
        _urgentSim = sim;
        RenderUrgentCards(sim);
        var rec = sim.Recommended;
        if (rec is not null) SelectUrgentPlan(rec, null);
    }

    private void RenderUrgentCards(UrgentInsertSimulation sim)
    {
        if (_urgentCards is null) return;
        _urgentCards.Controls.Clear();
        foreach (var plan in sim.Plans)
        {
            bool isRec = plan.Id == sim.RecommendedPlanId;
            var card = new Panel
            {
                Width = 330, Height = 196, Margin = new Padding(0, 0, 12, 8),
                BackColor = isRec ? Color.FromArgb(232, 242, 255) : Color.White,
                BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10),
                Cursor = Cursors.Hand, Tag = plan
            };
            var lbl = new Label
            {
                Dock = DockStyle.Fill, AutoSize = false, Font = new Font("微软雅黑", 9F),
                ForeColor = Color.FromArgb(30, 41, 59),
                Text = $"{plan.Name}{(isRec ? "　⭐推荐" : "")}\n"
                     + $"综合得分：{plan.TotalText}\n"
                     + $"线体：{plan.Line}　紧急完工：{plan.FinishText}\n"
                     + $"{plan.OnTimeText}　{plan.AffectedText}\n"
                     + $"{plan.DelayText}　{plan.ChangeText}\n"
                     + $"产能损失：{plan.CapacityLossHours:0.#} h\n"
                     + (plan.Pros.Count > 0 ? "✔ " + plan.Pros[0] : "")
                     + (plan.Cons.Count > 0 ? "\n✘ " + plan.Cons[0] : "")
            };
            EventHandler click = (_, _) => SelectUrgentPlan(plan, card);
            card.Click += click;
            lbl.Click += click;
            card.Controls.Add(lbl);
            _urgentCards.Controls.Add(card);
        }
    }

    private void SelectUrgentPlan(UrgentPlan plan, Control? card)
    {
        if (_urgentCards is not null)
        {
            foreach (Control c in _urgentCards.Controls) c.BackColor = Color.White;
            var target = card ?? _urgentCards.Controls.Cast<Control>().FirstOrDefault(c => ReferenceEquals(c.Tag, plan));
            if (target is not null) target.BackColor = Color.FromArgb(232, 242, 255);
        }
        if (_gridUrgentAffected is not null) _gridUrgentAffected.DataSource = plan.Affected;
        if (_lblUrgentNote is not null)
            _lblUrgentNote.Text = plan.Desc
                + (plan.Pros.Count > 0 ? "　优点：" + string.Join("；", plan.Pros) : "")
                + (plan.Cons.Count > 0 ? "　代价：" + string.Join("；", plan.Cons) : "");
    }
}
