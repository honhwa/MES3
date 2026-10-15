using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Data;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类：峰谷电费「移峰填谷」节约测算页（能源碳分类）。
/// 数据底座：月用电取自能耗台账（真实演示数据）；日负荷曲线与分时电价为演示假设，页内已标注口径。
/// </summary>
public partial class MainForm
{
    private Label _lblPvKwh = null!, _lblPvCurrent = null!, _lblPvTransfer = null!, _lblPvSaving = null!, _lblPvAnnual = null!, _lblPvNote = null!;
    private ComboBox _cboPvPct = null!;
    private DataGridView _pvGrid = null!;

    private sealed class PvPeriodRow
    {
        public string Period { get; set; } = "";
        public string Hours { get; set; } = "";
        public string Kwh { get; set; } = "";
        public string ShareText { get; set; } = "";
        public string Price { get; set; } = "";
    }

    private TabPage BuildPeakValleyTab()
    {
        var page = new TabPage("峰谷电费测算");
        page.Padding = new Padding(4);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("本月用电 (kWh)", Color.FromArgb(15, 76, 129), out _lblPvKwh));
        cards.Controls.Add(MakeCard("当前月电费 (元)", Color.FromArgb(230, 126, 34), out _lblPvCurrent));
        cards.Controls.Add(MakeCard("拟转移电量 (kWh)", Color.FromArgb(52, 152, 219), out _lblPvTransfer));
        cards.Controls.Add(MakeCard("月度节约 (元)", Color.FromArgb(39, 174, 96), out _lblPvSaving));
        cards.Controls.Add(MakeCard("年化节约 (元)", Color.FromArgb(142, 68, 173), out _lblPvAnnual));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 7, 4, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "峰段转移比例：", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cboPvPct = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, Margin = new Padding(0, 4, 10, 0) };
        _cboPvPct.Items.AddRange(new object[] { "0%", "10%", "20%", "30%", "40%", "50%" });
        _cboPvPct.SelectedIndex = 2;   // 默认 20%
        _cboPvPct.SelectedIndexChanged += (_, _) => LoadPeakValley();
        bar.Controls.Add(_cboPvPct);
        bar.Controls.Add(MakeToolbarButton("重新测算", (_, _) => LoadPeakValley()));
        _lblPvNote = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(12, 10, 0, 0) };
        bar.Controls.Add(_lblPvNote);

        var gridPanel = MakeGridGroup("四时段电量与电价（电价与时段为演示参数）", out _pvGrid);
        SetupPvColumns();

        var foot = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 60, ForeColor = Color.Gray,
            Padding = new Padding(6, 6, 6, 0),
            Text = "口径：本月用电取自能耗台账（全厂电力计量点，真实演示数据）；日负荷曲线与分时电价为演示假设（非实测、非财务决算），"
                 + "移峰填谷需结合生产工艺与设备可行性由人决策，本测算仅反映电价差的理论节约空间。"
        };

        page.Controls.Add(gridPanel);
        page.Controls.Add(bar);
        page.Controls.Add(cards);
        page.Controls.Add(foot);

        page.Enter -= PeakValleyTab_Enter;
        page.Enter += PeakValleyTab_Enter;
        LoadPeakValley();
        return page;
    }

    private void SetupPvColumns()
    {
        _pvGrid.AutoGenerateColumns = false;
        _pvGrid.Columns.Clear();
        _pvGrid.Columns.Add(MakeTextCol("Period", "时段", true, 10));
        _pvGrid.Columns.Add(MakeTextCol("Hours", "时段范围", true, 22));
        _pvGrid.Columns.Add(MakeTextCol("Kwh", "电量 (kWh)", true, 14));
        _pvGrid.Columns.Add(MakeTextCol("ShareText", "占比", true, 10));
        _pvGrid.Columns.Add(MakeTextCol("Price", "电价 (元/kWh)", true, 14));
    }

    private void PeakValleyTab_Enter(object? sender, EventArgs e) => LoadPeakValley();

    /// <summary>取数 + 重算 + 刷新（转移比例变化或进入页签时调用；构造期控件未建则跳过）。</summary>
    private void LoadPeakValley()
    {
        if (_pvGrid is null || _cboPvPct is null) return;
        int pct = _cboPvPct.SelectedIndex >= 0 ? _cboPvPct.SelectedIndex * 10 : 20;
        var a = MesDatabase.GetPeakValleyAnalysis(pct);

        if (_lblPvKwh is not null) _lblPvKwh.Text = a.MonthKwh.ToString("N0");
        if (_lblPvCurrent is not null) _lblPvCurrent.Text = a.CurrentCost.ToString("N0");
        if (_lblPvTransfer is not null) _lblPvTransfer.Text = a.TransferKwh.ToString("N0");
        if (_lblPvSaving is not null) _lblPvSaving.Text = a.Saving.ToString("N0");
        if (_lblPvAnnual is not null) _lblPvAnnual.Text = a.AnnualSaving.ToString("N0");
        if (_lblPvNote is not null)
            _lblPvNote.Text = $"账期 {a.Period} · {a.DaysInMonth} 天 · 峰段(尖峰+高峰)占比 {(a.SharpShare + a.PeakShare):0.#}% · 转移比例 {pct}%";

        var hours = new Dictionary<string, string>
        {
            ["尖峰"] = "10-12、18-20 时",
            ["高峰"] = "8-10、12-14、20-21 时",
            ["平段"] = "7-8、14-18、21-23 时",
            ["低谷"] = "23-7 时"
        };
        var rows = PeakValleyTariff.Periods.Select(p =>
        {
            var (kwh, share) = p.Name switch
            {
                "尖峰" => (a.SharpKwh, a.SharpShare),
                "高峰" => (a.PeakKwh, a.PeakShare),
                "平段" => (a.FlatKwh, a.FlatShare),
                _ => (a.ValleyKwh, a.ValleyShare)
            };
            return new PvPeriodRow
            {
                Period = p.Name,
                Hours = hours[p.Name],
                Kwh = kwh.ToString("N0"),
                ShareText = share.ToString("0.#") + "%",
                Price = p.Price.ToString("0.00")
            };
        }).ToList();
        _pvGrid.DataSource = rows;
    }
}
