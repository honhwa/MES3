using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms;

/// <summary>
/// MainForm 分部类（按功能域拆分）：使用手册页。
/// 功能：用户用大白话描述"我要干什么"，手册返回「去哪个页面 / 点哪个按钮 / 按什么路径走」，
///       并提供一键跳转到目标页面。搜索为纯本地打分排序（见 Models/ManualModels.cs）。
/// 说明：所有分部文件共同构成同一个 <see cref="MainForm"/> 类，字段与控件在 MainForm.cs 中统一声明。
/// </summary>
public partial class MainForm
{
    // ================= 页面构建 =================

    /// <summary>构建「使用手册」页签：搜索区（上）+ 结果列表与详情（中）+ 跳转动作（下）。</summary>
    private TabPage BuildManualTab()
    {
        var page = new TabPage("使用手册");

        var split = BuildManualSplit();      // 中间：左结果列表 / 右详情
        var top = BuildManualSearchBar();    // 顶部：说明 + 搜索框 + 快捷示例
        var bottom = BuildManualActionBar(); // 底部：跳转按钮

        // 停靠顺序：Fill 必须最先加入（下标最小）才能正确占满剩余空间
        page.Controls.Add(split);
        page.Controls.Add(bottom);
        page.Controls.Add(top);
        return page;
    }

    /// <summary>构建顶部搜索区：标题说明 + 搜索框 + 查找/清空 + 快捷示例。</summary>
    private Panel BuildManualSearchBar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 116,
            BackColor = Color.FromArgb(240, 246, 252),
            Padding = new Padding(16, 10, 16, 8)
        };

        var lblTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "使用手册 —— 不知道某个功能在哪？用大白话描述你要做的事，这里告诉你「去哪、点什么、怎么走」",
            Font = new Font("微软雅黑", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 76, 129)
        };

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };

        var lblInput = new Label
        {
            Text = "我要：",
            AutoSize = true,
            Margin = new Padding(0, 9, 4, 0),
            Font = new Font("微软雅黑", 9.5F)
        };

        _txtManualQuery = new TextBox
        {
            Width = 460,
            Height = 28,
            Font = new Font("微软雅黑", 10F),
            Margin = new Padding(0, 5, 8, 0),
            PlaceholderText = "例如：不合格怎么办 / 改工单数量 / 客户投诉了 / 怎么导出 / 谁有资格开设备"
        };
        // 回车即搜索：比"填完再找按钮"更符合直觉
        _txtManualQuery.KeyDown += ManualQuery_KeyDown;

        _btnManualSearch = MakeToolbarButton("查找", BtnManualSearch);
        _btnManualClear = MakeToolbarButton("清空", BtnManualClear);

        row.Controls.Add(lblInput);
        row.Controls.Add(_txtManualQuery);
        row.Controls.Add(_btnManualSearch);
        row.Controls.Add(_btnManualClear);

        var quick = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        quick.Controls.Add(new Label
        {
            Text = "常用提问：",
            AutoSize = true,
            Margin = new Padding(0, 9, 4, 0),
            ForeColor = Color.FromArgb(90, 105, 125)
        });

        // 快捷示例：把最高频的几类提问做成一点即搜的链接，降低"不知道该搜什么"的门槛
        foreach (var sample in new[] { "不合格怎么办", "客户投诉了", "改工单数量", "怎么看库存", "谁有资格上岗", "怎么导出" })
        {
            var link = new LinkLabel
            {
                Text = sample,
                AutoSize = true,
                Margin = new Padding(0, 8, 14, 0),
                LinkColor = Color.FromArgb(30, 95, 168),
                Font = new Font("微软雅黑", 9F)
            };
            var text = sample;   // 闭包捕获：每次循环用独立副本
            link.LinkClicked += (_, _) =>
            {
                _txtManualQuery.Text = text;
                DoManualSearch();
            };
            quick.Controls.Add(link);
        }

        // Dock=Top 按"后加入的先停靠"排列，因此这里要倒序加入才能得到「标题→搜索行→快捷行」的视觉顺序
        panel.Controls.Add(quick);
        panel.Controls.Add(row);
        panel.Controls.Add(lblTitle);
        return panel;
    }

    /// <summary>构建中部：左侧结果列表 + 右侧操作详情。</summary>
    private SplitContainer BuildManualSplit()
    {
        // 注意：SplitterDistance / Panel1MinSize / Panel2MinSize 三个值互相约束，
        // 构造期容器的 Width 还是默认值（约 150），此时设置会直接抛
        // ArgumentOutOfRangeException —— 编译期查不出来，是 UI 启动冒烟抓到的。
        // 所以这里只做基础配置，等容器拿到真实宽度后在 SizeChanged 里一次性分配。
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6
        };

        bool laidOut = false;
        split.SizeChanged += (_, _) =>
        {
            if (laidOut) return;

            const int minLeft = 300, minRight = 320;
            int need = minLeft + minRight + split.SplitterWidth;
            if (split.Width <= need) return;   // 还太窄，等下一次尺寸变化

            int maxDist = split.Width - minRight - split.SplitterWidth;
            int dist = Math.Max(minLeft, (int)(split.Width * 0.42));
            if (dist > maxDist) dist = maxDist;
            if (dist < minLeft) return;

            split.Panel1MinSize = minLeft;
            split.Panel2MinSize = minRight;
            split.SplitterDistance = dist;     // 此时宽度足够，赋值合法
            laidOut = true;
        };

        // ---- 左：结果列表 ----
        _manualResultGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,      // 手工建列：列名与顺序完全可控
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        SetupManualResultColumns();
        _manualResultGrid.SelectionChanged += ManualResult_SelectionChanged;
        _manualResultGrid.CellDoubleClick += ManualResult_CellDoubleClick;   // 双击 = 跳到目标页面

        var lblListCaption = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "  检索结果（得分从高到低，双击可直接跳到该页面）",
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(226, 236, 247),
            ForeColor = Color.FromArgb(30, 70, 120),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };

        _lblManualHint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(90, 105, 125),
            Padding = new Padding(4, 0, 0, 0)
        };

        split.Panel1.Controls.Add(_manualResultGrid);
        split.Panel1.Controls.Add(_lblManualHint);
        split.Panel1.Controls.Add(lblListCaption);

        // ---- 右：详情 ----
        _rtbManualDetail = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("微软雅黑", 9.5F),
            WordWrap = true,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Padding = new Padding(12)
        };

        var lblDetailCaption = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "  操作说明（去哪 / 点什么 / 怎么走）",
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(226, 236, 247),
            ForeColor = Color.FromArgb(30, 70, 120),
            Font = new Font("微软雅黑", 9F, FontStyle.Bold)
        };

        split.Panel2.Controls.Add(_rtbManualDetail);
        split.Panel2.Controls.Add(lblDetailCaption);

        return split;
    }

    /// <summary>构建底部动作条：一键跳转到目标页面。</summary>
    private Panel BuildManualActionBar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(246, 249, 253),
            Padding = new Padding(16, 7, 16, 7)
        };

        _btnManualGo = MakeToolbarButton("一键跳转到该页面", BtnManualGo);
        _btnManualGo.Width = 170;
        _btnManualGo.Dock = DockStyle.Left;

        var lblTip = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  提示：跳转只会切换页面，不会替你执行操作；到了目标页面按右侧「操作路径」逐步点即可。",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(110, 125, 145),
            Font = new Font("微软雅黑", 8.5F)
        };

        panel.Controls.Add(lblTip);
        panel.Controls.Add(_btnManualGo);
        return panel;
    }

    /// <summary>
    /// 定义结果列表的列。
    /// 注意：AutoGenerateColumns=false 时，手工列必须同时给 Name 与 DataPropertyName——
    /// 只给 Name 不给 DataPropertyName，绑定后整列会是空白（UI 冒烟抓到的坑）。
    /// </summary>
    private void SetupManualResultColumns()
    {
        _manualResultGrid.Columns.Add(MakeManualCol("Level", "匹配", "Level", 8));
        _manualResultGrid.Columns.Add(MakeManualCol("Title", "可以做什么", "Title", 30));
        _manualResultGrid.Columns.Add(MakeManualCol("Where", "去哪", "Where", 26));
        _manualResultGrid.Columns.Add(MakeManualCol("Why", "为什么推荐这条", "Why", 36));
    }

    /// <summary>手写列工厂：Name / HeaderText / DataPropertyName 三个属性必须一起给，缺一列就空白。</summary>
    private static DataGridViewTextBoxColumn MakeManualCol(string name, string header, string prop, int fillWeight) =>
        new()
        {
            Name = name,
            HeaderText = header,
            DataPropertyName = prop,
            ReadOnly = true,
            FillWeight = fillWeight
        };

    // ================= 数据加载 =================

    /// <summary>
    /// 手册页兜底初始化（幂等）：首次进入时载入全部条目，便于用户先浏览再搜索。
    /// 由 RefreshAll 与页签 Enter 双路调用。
    /// </summary>
    private void EnsureManualReady()
    {
        if (_manualLoaded) return;
        if (_manualResultGrid is null || _manualResultGrid.Columns.Count == 0) return;  // 控件未就绪
        _manualLoaded = true;
        DoManualSearch();   // 空查询 = 浏览全部
    }

    /// <summary>执行手册检索并刷新列表与详情。</summary>
    private void DoManualSearch()
    {
        var query = _txtManualQuery.Text;
        _manualHits = ManualSearch.Search(query);

        // 用行对象承载展示字段，避免为绑定再建一次投影类型
        var rows = _manualHits.Select(h => new ManualRow
        {
            Level = $"{new string('★', h.Stars)}",
            Title = h.Entry.Title,
            Where = h.Entry.WhereText,
            Why = h.Reason
        }).ToList();

        _manualResultGrid.DataSource = rows;

        if (_lblManualHint is not null)
        {
            _lblManualHint.Text = string.IsNullOrWhiteSpace(query)
                ? $"  共 {_manualHits.Count} 条操作可查（输入关键词可快速定位）"
                : $"  命中 {_manualHits.Count} 条" + (_manualHits.Count == 0 ? "：换个更短的关键词试试，例如「异常」「工单」「导出」「导出报表」" : "");
        }

        // 默认选中首条：延后到数据绑定之后（控件未挂载时 Rows 不物化）
        if (_manualResultGrid.Rows.Count > 0)
        {
            _manualResultGrid.Rows[0].Selected = true;
            _manualResultGrid.CurrentCell = _manualResultGrid.Rows[0].Cells["Title"];
        }
        else
        {
            RenderManualPlaceholder();
        }

        UpdateManualButtons();
    }

    /// <summary>取当前选中的手册条目（无选中返回 null）。</summary>
    private ManualEntry? SelectedManualEntry()
    {
        if (_manualResultGrid.CurrentRow is null) return null;
        int idx = _manualResultGrid.CurrentRow.Index;
        return idx >= 0 && idx < _manualHits.Count ? _manualHits[idx].Entry : null;
    }

    // ================= 详情渲染 =================

    /// <summary>把条目渲染到右侧详情面板：去哪 / 点什么 / 操作路径 / 提示 / 相关操作。</summary>
    private void RenderManualDetail(ManualEntry e)
    {
        if (_rtbManualDetail is null) return;

        var fTitle = new Font("微软雅黑", 13F, FontStyle.Bold);
        var fHead = new Font("微软雅黑", 9F, FontStyle.Bold);
        var fBody = new Font("微软雅黑", 9.5F);
        var fSmall = new Font("微软雅黑", 8.5F);

        var cInk = Color.FromArgb(30, 41, 59);
        var cBlue = Color.FromArgb(15, 76, 129);
        var cGray = Color.FromArgb(110, 125, 145);
        var cGreen = Color.FromArgb(26, 127, 90);

        _rtbManualDetail.Clear();

        RtbWrite($" {e.Title}\n", fTitle, cBlue);
        RtbWrite($"{e.Summary}\n\n", fBody, cInk);

        RtbWrite("去哪\n", fHead, cGray);
        RtbWrite($"    {e.Category}  ›  {e.TargetPage}"
            + (string.IsNullOrWhiteSpace(e.TargetSubPage) ? "" : $"  ›  {e.TargetSubPage}") + "\n", fBody, cBlue);

        RtbWrite("点什么\n", fHead, cGray);
        RtbWrite($"    {e.ButtonText}\n", fBody, e.TargetButton.Length > 0 ? cGreen : cGray);

        RtbWrite("\n操作路径\n", fHead, cGray);
        for (int i = 0; i < e.Steps.Length; i++)
            RtbWrite($"    {i + 1}. {e.Steps[i]}\n", fBody, cInk);

        if (!string.IsNullOrWhiteSpace(e.Tip))
        {
            RtbWrite("\n提示\n", fHead, cGray);
            RtbWrite($"    {e.Tip}\n", fSmall, Color.FromArgb(168, 106, 0));
        }

        var related = e.SeeAlso.Select(ManualSearch.ById).Where(x => x is not null).ToList();
        if (related.Count > 0)
        {
            RtbWrite("\n相关操作\n", fHead, cGray);
            foreach (var r in related)
                RtbWrite($"    · {r!.Title}（{r.WhereText}）\n", fSmall, cGray);
        }
    }

    /// <summary>无选中项时的占位说明。</summary>
    private void RenderManualPlaceholder()
    {
        if (_rtbManualDetail is null) return;
        _rtbManualDetail.Clear();
        RtbWrite("没有匹配到操作\n\n", new Font("微软雅黑", 12F, FontStyle.Bold), Color.FromArgb(168, 106, 0));
        RtbWrite("可以这样试：\n", new Font("微软雅黑", 9.5F, FontStyle.Bold), Color.FromArgb(30, 41, 59));
        RtbWrite("  · 用更短的词，例如「异常」「工单」「库存」「导出」\n", new Font("微软雅黑", 9.5F), Color.FromArgb(60, 78, 100));
        RtbWrite("  · 直接说业务动作，例如「返工」「客诉」「点检」「资质」\n", new Font("微软雅黑", 9.5F), Color.FromArgb(60, 78, 100));
        RtbWrite("  · 点上方「常用提问」里的示例，看系统是怎么理解的\n", new Font("微软雅黑", 9.5F), Color.FromArgb(60, 78, 100));
        RtbWrite("\n也可清空搜索框后直接浏览全部操作条目。", new Font("微软雅黑", 9F), Color.FromArgb(110, 125, 145));
    }

    /// <summary>向详情面板追加一段带格式的文本。</summary>
    private void RtbWrite(string text, Font font, Color color)
    {
        _rtbManualDetail.SelectionFont = font;
        _rtbManualDetail.SelectionColor = color;
        _rtbManualDetail.AppendText(text);
    }

    /// <summary>刷新按钮可用性：无选中项时禁用跳转。</summary>
    private void UpdateManualButtons()
    {
        if (_btnManualGo is null) return;
        var e = SelectedManualEntry();
        _btnManualGo.Enabled = e is not null && !string.IsNullOrWhiteSpace(e.TargetPage);
    }

    // ================= 事件处理 =================

    /// <summary>搜索框回车 → 触发检索。</summary>
    private void ManualQuery_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;   // 吞掉回车音
        DoManualSearch();
    }

    /// <summary>点「查找」。</summary>
    private void BtnManualSearch(object? sender, EventArgs e) => DoManualSearch();

    /// <summary>点「清空」：清空关键词并回到全量浏览。</summary>
    private void BtnManualClear(object? sender, EventArgs e)
    {
        _txtManualQuery.Text = "";
        DoManualSearch();
        _txtManualQuery.Focus();
    }

    /// <summary>结果列表选中变化 → 右侧详情跟随刷新。</summary>
    private void ManualResult_SelectionChanged(object? sender, EventArgs e)
    {
        var entry = SelectedManualEntry();
        if (entry is not null) RenderManualDetail(entry);
        UpdateManualButtons();
    }

    /// <summary>双击结果行 → 直接跳到目标页面。</summary>
    private void ManualResult_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        BtnManualGo(sender, EventArgs.Empty);
    }

    /// <summary>一键跳转到目标页面（跨层定位由 ActivatePage 统一处理）。</summary>
    private void BtnManualGo(object? sender, EventArgs e)
    {
        var entry = SelectedManualEntry();
        if (entry is null) return;

        if (string.Equals(entry.TargetPage, "使用手册", StringComparison.Ordinal))
        {
            MessageBox.Show("这条说明本身就在当前页面，无需跳转。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var page = FindPage(entry.TargetPage);
        if (page is null)
        {
            Warn($"未找到页面「{entry.TargetPage}」，请检查手册条目配置。");
            return;
        }

        ActivatePage(page);
        _lblManualHint.Text = $"  已跳转到「{entry.Category} › {entry.TargetPage}」，请在该页面点「{entry.ButtonText}」继续操作。";
    }

    // ================= 列表行模型 =================

    /// <summary>结果列表的展示行（DataGridView 绑定用，属性名与列 Name 对应）。</summary>
    public sealed class ManualRow
    {
        public string Level { get; init; } = "";
        public string Title { get; init; } = "";
        public string Where { get; init; } = "";
        public string Why { get; init; } = "";
    }
}
