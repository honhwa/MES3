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
/// MainForm 分部类（按功能域拆分，第 7 部分）：上料防错页（扫码校验 工单-BOM-批次，防错料混料，通过与被拒均留痕）
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明，
/// 本文件只承载「Feeding」相关的页面构建、数据加载与交互逻辑。
/// </summary>
public partial class MainForm
{
    // ================= 上料防错（顶层第 19 个页签） =================

    /// <summary>上料防错页：扫码校验「工单-BOM-批次」，防错料混料；通过与被拒均留痕。</summary>
    private TabPage BuildFeedingTab()
    {
        var page = new TabPage("上料防错");

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(6, 4, 6, 4) };
        cards.Controls.Add(MakeCard("投料记录 (次)", Color.FromArgb(15, 76, 129), out _lblFeedTotal));
        cards.Controls.Add(MakeCard("校验通过 (次)", Color.FromArgb(39, 174, 96), out _lblFeedPassed));
        cards.Controls.Add(MakeCard("被拒留痕 (次)", Color.FromArgb(192, 0, 0), out _lblFeedRejected));
        cards.Controls.Add(MakeCard("BOM 投足率 (%)", Color.FromArgb(142, 68, 173), out _lblFeedBomRate));

        var scanBar = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.WhiteSmoke, Padding = new Padding(8, 6, 8, 4) };

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false };
        row1.Controls.Add(new Label { Text = "生产工单：", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        _cboFeedWo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(4, 4, 0, 0) };
        _cboFeedWo.Items.AddRange(MesDatabase.WorkOrders
            .Select(w => $"{w.WoNo} ｜ {w.ProductModel} ｜ {w.Status}").Cast<object>().ToArray());
        if (_cboFeedWo.Items.Count > 0) _cboFeedWo.SelectedIndex = 0;
        _cboFeedWo.SelectedIndexChanged += (_, _) => DoQueryFeeding();
        row1.Controls.Add(_cboFeedWo);

        row1.Controls.Add(new Label { Text = "扫码（物料|批次 或 工单|物料|批次）：", AutoSize = true, Margin = new Padding(14, 8, 0, 0) });
        _txtFeedScan = new TextBox { Width = 300, Font = new Font("Consolas", 10F), Margin = new Padding(4, 4, 0, 0) };
        _txtFeedScan.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoCheckFeeding(); };
        row1.Controls.Add(_txtFeedScan);
        row1.Controls.Add(MakeToolbarButton("校验并投料", (_, _) => DoCheckFeeding()));
        row1.Controls.Add(MakeToolbarButton("清空", (_, _) =>
        {
            _txtFeedScan.Text = "";
            if (_lblFeedResult is not null) _lblFeedResult.Text = "";
        }));

        _lblFeedResult = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Text = "等待扫码…（示例：M-ACT-CAR-01|PC-260812-L1-001）",
            Font = new Font("微软雅黑", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 50, 60),
            TextAlign = ContentAlignment.MiddleLeft
        };
        scanBar.Controls.Add(_lblFeedResult);
        scanBar.Controls.Add(row1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 230,
            BackColor = SystemColors.Control
        };
        split.Panel1.Controls.Add(MakeGridGroup("本工单 BOM 投料进度（需求量 = 单位用量 × 计划数 ×(1+损耗率)）", out _feedProgressGrid));
        split.Panel2.Controls.Add(MakeGridGroup("投料记录（通过与被拒均留痕，被拒为红色）", out _feedRecordGrid));

        page.Controls.Add(split);
        page.Controls.Add(scanBar);
        page.Controls.Add(cards);

        SetupFeedingColumns();
        DoQueryFeeding();

        page.Enter -= FeedingTab_Enter;
        page.Enter += FeedingTab_Enter;   // 构造期数据绑定未物化，进入页签时兜底加载
        return page;
    }

    /// <summary>设置「FeedingColumns」网格列定义（显式列名，供数据绑定与着色使用）。</summary>
    private void SetupFeedingColumns()
    {
        _feedProgressGrid.AutoGenerateColumns = false;
        _feedProgressGrid.Columns.Clear();
        _feedProgressGrid.Columns.Add(MakeTextCol("MaterialType", "类型", true, 7));
        _feedProgressGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 14));
        _feedProgressGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 22));
        _feedProgressGrid.Columns.Add(MakeTextCol("Spec", "规格", true, 12));
        _feedProgressGrid.Columns.Add(MakeTextCol("Unit", "单位", true, 7));
        _feedProgressGrid.Columns.Add(MakeTextCol("ProgressText", "已投 / 需求", true, 14));
        _feedProgressGrid.Columns.Add(MakeTextCol("Status", "投料状态", true, 10));
        // 先解除再挂接着色事件（同上，防止重复着色）
        _feedProgressGrid.CellFormatting -= FeedProgressGrid_CellFormatting;
        _feedProgressGrid.CellFormatting += FeedProgressGrid_CellFormatting;

        _feedRecordGrid.AutoGenerateColumns = false;
        _feedRecordGrid.Columns.Clear();
        _feedRecordGrid.Columns.Add(MakeTextCol("FeedNo", "投料单号", true, 13));
        _feedRecordGrid.Columns.Add(MakeTextCol("MaterialCode", "物料编码", true, 13));
        _feedRecordGrid.Columns.Add(MakeTextCol("MaterialName", "物料名称", true, 20));
        _feedRecordGrid.Columns.Add(MakeTextCol("LotNo", "批次", true, 14));
        _feedRecordGrid.Columns.Add(MakeTextCol("Location", "库位", true, 12));
        _feedRecordGrid.Columns.Add(MakeTextCol("Quantity", "数量", true, 8));
        _feedRecordGrid.Columns.Add(MakeTextCol("Result", "校验结果", true, 13));
        _feedRecordGrid.Columns.Add(MakeTextCol("Operator", "操作人", true, 8));
        var tCol = MakeTextCol("FeedTime", "投料时间", true, 13);
        tCol.DefaultCellStyle.Format = "MM-dd HH:mm";
        _feedRecordGrid.Columns.Add(tCol);
        // 先解除再挂接着色事件（同上，防止重复着色）
        _feedRecordGrid.CellFormatting -= FeedRecordGrid_CellFormatting;
        _feedRecordGrid.CellFormatting += FeedRecordGrid_CellFormatting;
    }

    /// <summary>投料进度着色：已投足绿 / 部分投料橙 / 未投灰。</summary>
    private void FeedProgressGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_feedProgressGrid.Rows[e.RowIndex].DataBoundItem is not FeedingProgressRow row) return;

        var col = _feedProgressGrid.Columns[e.ColumnIndex].Name;
        if (col != "Status" && col != "ProgressText") return;

        e.CellStyle.ForeColor = row.Status switch
        {
            "已投足" => Color.FromArgb(39, 174, 96),
            "部分投料" => Color.FromArgb(230, 126, 34),
            _ => Color.FromArgb(127, 140, 141)
        };
    }

    /// <summary>投料记录着色：被拒红、通过绿。</summary>
    private void FeedRecordGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_feedRecordGrid.Rows[e.RowIndex].DataBoundItem is not FeedingRecord rec) return;

        var col = _feedRecordGrid.Columns[e.ColumnIndex].Name;
        if (col != "Result") return;

        e.CellStyle.ForeColor = rec.IsPass ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 0, 0);
    }

    /// <summary>进入「FeedingTab」页签时兜底加载数据（控件已挂载、绑定已物化）。</summary>
    private void FeedingTab_Enter(object? sender, EventArgs e) => EnsureFeedingReady();

    /// <summary>兜底初始化「Feeding」：页签 Enter/全局刷新时确保数据已加载（幂等；构造期数据绑定未物化）。</summary>
    private void EnsureFeedingReady()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_feedProgressGrid is null) return;
        if (_feedProgressGrid.Rows.Count == 0) DoQueryFeeding();
    }

    /// <summary>在网格中选中「edFeedWoNo」对应的行（联动加载明细）。</summary>
    private string SelectedFeedWoNo()
    {
        var index = _cboFeedWo?.SelectedIndex ?? -1;
        return index >= 0 && index < MesDatabase.WorkOrders.Count ? MesDatabase.WorkOrders[index].WoNo : "";
    }

    /// <summary>加载上料防错页：BOM 投料进度 + 投料记录 + 汇总卡。</summary>
    private void DoQueryFeeding()
    {
        // 控件尚未创建（构造期）：直接返回，避免空引用
        if (_feedProgressGrid is null) return;
        var woNo = SelectedFeedWoNo();

        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _feedProgressGrid.DataSource = null;
        _feedProgressGrid.DataSource = woNo.Length == 0 ? null : MesDatabase.GetFeedingProgress(woNo);
        // 先清空数据源再重新绑定：强制网格重建列/行，避免残留旧数据
        _feedRecordGrid.DataSource = null;
        _feedRecordGrid.DataSource = MesDatabase.QueryFeedingRecords(woNo.Length == 0 ? null : woNo, null);

        var (total, passed, rejected, bomRate) = MesDatabase.GetFeedingSummary(woNo.Length == 0 ? null : woNo);
        if (_lblFeedTotal is not null) _lblFeedTotal.Text = total.ToString();
        if (_lblFeedPassed is not null) _lblFeedPassed.Text = passed.ToString();
        if (_lblFeedRejected is not null)
        {
            _lblFeedRejected.Text = rejected.ToString();
            _lblFeedRejected.ForeColor = rejected > 0 ? Color.FromArgb(255, 130, 130) : Color.White;
        }
        if (_lblFeedBomRate is not null) _lblFeedBomRate.Text = bomRate.ToString("0.#");
    }

    /// <summary>执行扫码校验（并投料留痕），结果以大字提示。</summary>
    private void DoCheckFeeding()
    {
        var woNo = SelectedFeedWoNo();
        if (woNo.Length == 0) { Warn("请先选择生产工单。"); return; }

        var scan = _txtFeedScan.Text.Trim();
        if (scan.Length == 0) { Warn("请扫码或输入物料/批次后再校验。"); return; }

        var outcome = MesDatabase.CheckFeeding(woNo, scan, "admin");
        if (_lblFeedResult is not null)
        {
            _lblFeedResult.Text = outcome.Message
                                  + (outcome.RequiredQty is null ? "" : $"（需求量 {outcome.RequiredQty:0.##} {outcome.Unit}）");
            _lblFeedResult.ForeColor = outcome.Ok ? Color.FromArgb(39, 174, 96) : Color.FromArgb(192, 0, 0);
        }

        DoQueryFeeding();
        _txtFeedScan.SelectAll();
        _txtFeedScan.Focus();
    }


}
