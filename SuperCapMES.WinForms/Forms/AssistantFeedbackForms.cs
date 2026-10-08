using System.Drawing;
using System.Windows.Forms;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Forms;

// ============================================================================
// 反馈相关的两个小窗体
//   FeedbackForm     ：点 👎 后追问"哪里不对"——一条没有说明的差评没法指导任何改进
//   FeedbackStatsForm：按意图看差评率 + 可执行改进建议，可导出 Markdown
// ============================================================================

/// <summary>负反馈原因采集（点 👎 时弹出）。</summary>
public class FeedbackForm : Form
{
    private readonly TextBox _txt = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("微软雅黑", 9F)
    };

    /// <summary>用户填的原因</summary>
    public string Comment => _txt.Text.Trim();

    public FeedbackForm(AssistantReply reply)
    {
        Text = "这条回答哪里不对？";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 330);
        MaximizeBox = false;
        Font = new Font("微软雅黑", 9F);

        var head = new Label
        {
            Dock = DockStyle.Top,
            Height = 74,
            Padding = new Padding(14, 10, 14, 6),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 65, 85),
            Text = $"你的问题：{reply.Question}\n" +
                   $"系统理解为「{reply.Intent}」（置信度 {reply.Confidence}%），结论：{reply.Headline}\n\n" +
                   "下面写一句就行（可留空）——只有说明了原因，这条反馈才能指导改进。"
        };

        var quick = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 74,
            Padding = new Padding(14, 2, 10, 2),
            WrapContents = true
        };
        foreach (var t in new[] { "没说到我想问的", "数字不对", "结论看不懂", "答非所问" })
        {
            var tag = t;
            var l = new LinkLabel
            {
                Text = tag,
                AutoSize = true,
                Margin = new Padding(0, 4, 10, 0),
                LinkColor = Color.FromArgb(30, 95, 168),
                Font = new Font("微软雅黑", 8.5F)
            };
            l.Click += (_, _) => { if (_txt.Text.Trim().Length > 0) _txt.Text += "；"; _txt.Text += tag; _txt.Focus(); };
            quick.Controls.Add(l);
        }

        var btnOk = new Button { Text = "提交", Width = 90, Height = 30, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 4, 12, 0)
        };
        btnRow.Controls.Add(btnCancel);
        btnRow.Controls.Add(btnOk);

        Controls.Add(_txt);
        Controls.Add(quick);
        Controls.Add(head);
        Controls.Add(btnRow);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}

/// <summary>反馈统计：差评率按意图排序 + 可执行改进建议 + 导出。</summary>
public class FeedbackStatsForm : Form
{
    public FeedbackStatsForm(FeedbackSummary s)
    {
        Text = "AI 助手反馈统计";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 520);
        Font = new Font("微软雅黑", 9F);

        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("微软雅黑", 9F),
            Text = BuildText(s)
        };

        var btnExport = new Button { Text = "导出 Markdown", Width = 130, Height = 30 };
        var btnClose = new Button { Text = "关闭", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
        btnExport.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "导出反馈统计",
                Filter = "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
                FileName = $"助手反馈统计_{DateTime.Now:yyyyMMdd_HHmm}.md"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dlg.FileName, AssistantFeedbackStore.ExportMarkdown(),
                    new System.Text.UTF8Encoding(true));
                MessageBox.Show($"已导出到 {dlg.FileName}", "已导出",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 5, 12, 0),
            BackColor = Color.WhiteSmoke
        };
        btnRow.Controls.Add(btnClose);
        btnRow.Controls.Add(btnExport);

        Controls.Add(rtb);
        Controls.Add(btnRow);
        CancelButton = btnClose;
    }

    private static string BuildText(FeedbackSummary s)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("反馈统计　——　用途是反哺：找出「哪类问法答不好」，据此补关键词、补手册条目");
        sb.AppendLine();
        sb.AppendLine($"共 {s.Total} 条｜有帮助 {s.Good}｜没答到点 {s.Bad}｜差评率 {s.BadRate:P0}｜含文字意见 {s.WithComment} 条");
        sb.AppendLine();
        sb.AppendLine("【改进建议】");
        foreach (var h in s.ImprovementHints()) sb.AppendLine("· " + h);

        if (s.ByIntent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【分意图明细】（按差评率降序）");
            foreach (var i in s.ByIntent.OrderByDescending(x => x.BadRate).ThenByDescending(x => x.Total))
                sb.AppendLine($"· {i.Intent,-8}　有帮助 {i.Good}／没答到点 {i.Bad}　差评率 {i.BadRateText}"
                              + $"　{(i.EnoughSamples ? "（样本够，可下结论）" : "（样本不足 3 条，暂不下结论）")}");
        }

        var comments = AssistantFeedbackStore.All.Where(f => !string.IsNullOrWhiteSpace(f.Comment)).ToList();
        if (comments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【具体意见】");
            foreach (var f in comments)
                sb.AppendLine($"· 「{f.Question}」→ {f.Comment}（{f.Intent}）");
        }
        return sb.ToString();
    }
}
