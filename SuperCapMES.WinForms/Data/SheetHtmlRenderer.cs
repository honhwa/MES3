using System.Globalization;
using System.Text;
using SuperCapMES.WinForms.Models;

namespace SuperCapMES.WinForms.Data;

/// <summary>HTML 渲染参数。</summary>
public sealed class HtmlRenderOptions
{
    /// <summary>可填写模式：可填列渲染单元格内嵌控件</summary>
    public bool Editable { get; set; }
    /// <summary>语义模板（提供"哪列可填、什么控件、候选项"）；可填模式必填</summary>
    public ChecklistTemplate? Template { get; set; }
    /// <summary>回显值：key = r{row}c{col}（与 ChecklistSubmission.Values 同一套键）</summary>
    public Dictionary<string, string>? Values { get; set; }
    /// <summary>页面上方标题（导出电子档时用）</summary>
    public string Title { get; set; } = "";
    /// <summary>标题下方一行说明（提交人/时间等）</summary>
    public string Subtitle { get; set; } = "";
    /// <summary>缩放（1.0 = 原尺寸）</summary>
    public double Zoom { get; set; } = 1.0;
}

/// <summary>
/// 把 StyledSheet 渲染成自包含 HTML：合并单元格、列宽、行高、字体、字号、粗斜体、
/// 字色、填充、对齐、自动换行、四边边框、常用数字格式全部还原。
///
/// 三条纪律：
/// ① **零外链**（不引 CDN / 外部字体），离线可用，与数字孪生页同一口径；
/// ② 单元格内容**全部 HTML 转义**（导入的是任意文件，否则可注入脚本）；
/// ③ 只读渲染与可填写渲染**同一套骨架**，保证"填写页"和"归档件"长得一样。
/// </summary>
public static class SheetHtmlRenderer
{
    // 必须用单引号：td 的 style="..." 用双引号包裹，字体名里再出现双引号会把整个属性截断
    private const string FontStack = "'Noto Sans CJK SC','Microsoft YaHei','微软雅黑',sans-serif";

    /// <summary>
    /// 当前工作表的标题：优先取该表首行的原文（原表的标题就在首行），
    /// 拿不到再退回工作表名 / 模板标题。
    /// 必须按表取——否则切到第 2、3 张时页面大标题还是第 1 张的，用户会以为没切换成功。
    /// </summary>
    public static string SheetTitle(StyledSheet sheet, string templateTitle)
    {
        var first = sheet.RowCount > 0 ? sheet.Cell(0, 0).Trim() : "";
        if (first.Length >= 4) return first;
        if (!string.IsNullOrWhiteSpace(sheet.SheetName)) return sheet.SheetName;
        return templateTitle;
    }

    public static string Render(StyledSheet sheet, HtmlRenderOptions opt)
    {
        int rows = sheet.RowCount, cols = sheet.ColCount;
        var sb = new StringBuilder(64 * 1024);

        sb.Append("<!doctype html><html lang=\"zh\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(Esc(string.IsNullOrWhiteSpace(opt.Title) ? sheet.SheetName : opt.Title)).Append("</title>");
        sb.Append("<style>").Append(BaseCss(opt.Zoom)).Append("</style></head><body>");

        // 标题只进 <title>：原表首行本身就是标题（合并单元格、居中），
        // 再额外画一行 div 会与原件版式不一致（参照物里没有这一行）。
        if (opt.Subtitle.Length > 0)
            sb.Append("<div class=\"sub\">").Append(Esc(opt.Subtitle)).Append("</div>");

        if (rows == 0 || cols == 0)
        {
            sb.Append("<div class=\"empty\">该工作表没有可渲染的内容。</div></body></html>");
            return sb.ToString();
        }

        // 合并区域：左上角记 span，其余格标记为被覆盖
        var spans = new Dictionary<(int R, int C), (int RS, int CS)>();
        var covered = new bool[rows, cols];
        foreach (var m in sheet.Merges)
        {
            if (m.R1 < 0 || m.C1 < 0 || m.R2 >= rows || m.C2 >= cols) continue;
            if (m.R1 == m.R2 && m.C1 == m.C2) continue;
            spans[(m.R1, m.C1)] = (m.R2 - m.R1 + 1, m.C2 - m.C1 + 1);
            for (int r = m.R1; r <= m.R2; r++)
                for (int c = m.C1; c <= m.C2; c++)
                    if (r != m.R1 || c != m.C1) covered[r, c] = true;
        }

        double totalW = 0;
        for (int c = 0; c < cols; c++) totalW += c < sheet.ColWidthPx.Count ? sheet.ColWidthPx[c] : 64;

        sb.Append("<div class=\"doc\"><table class=\"sheet\" style=\"width:").Append(F(totalW)).Append("px\">");
        sb.Append("<colgroup>");
        for (int c = 0; c < cols; c++)
            sb.Append("<col style=\"width:").Append(F(c < sheet.ColWidthPx.Count ? sheet.ColWidthPx[c] : 64)).Append("px\">");
        sb.Append("</colgroup><tbody>");

        for (int r = 0; r < rows; r++)
        {
            var h = r < sheet.RowHeightPx.Count ? sheet.RowHeightPx[r] : 20;
            sb.Append("<tr style=\"height:").Append(F(h)).Append("px\">");

            for (int c = 0; c < cols; c++)
            {
                if (covered[r, c]) continue;          // 被合并覆盖的格不输出
                spans.TryGetValue((r, c), out var sp);
                int rs = sp.RS <= 0 ? 1 : sp.RS, cspan = sp.CS <= 0 ? 1 : sp.CS;

                var style = sheet.StyleAt(r, c);
                var raw = sheet.Cell(r, c);
                var text = FormatValue(raw, style);
                // 列语义优先取本表自带的（多工作表时每张表各有各的列定义），
                // 回退到语义模板（单表旧数据）。这样"第 2、3 张表"也能各自正确地出控件。
                var meta = ResolveMeta(sheet, opt, c);
                // 只在**表体行**范围内允许填写：抬头行/表头行/表尾行即使列下标撞上也绝不套控件
                bool inBody = InBody(sheet, opt, r);
                bool fillable = opt.Editable && inBody && meta is not null && meta.Fillable;
                var key = $"r{r}c{c}";

                sb.Append("<td");
                if (rs > 1) sb.Append(" rowspan=\"").Append(rs).Append('"');
                if (cspan > 1) sb.Append(" colspan=\"").Append(cspan).Append('"');
                sb.Append(" style=\"").Append(Css(style, fillable)).Append('"');
                if (fillable) sb.Append(" class=\"fc\"");
                sb.Append('>');

                if (fillable)
                {
                    sb.Append(RenderInput(meta!, key, opt.Values));
                }
                else
                {
                    // 只读模式下也要回显填写值：归档件就是靠这条把值画回原表
                    var shown = inBody && opt.Values is not null && opt.Values.TryGetValue(key, out var vv) && vv.Length > 0
                        ? vv
                        : text;
                    if (shown.StartsWith("data:image", StringComparison.Ordinal))
                        sb.Append("<img class=\"sigimg ro\" alt=\"\" src=\"").Append(Esc(shown)).Append("\">");
                    else if (shown.Length > 0)
                        sb.Append("<span class=\"v\">").Append(Esc(shown)).Append("</span>");
                }

                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></div>");
        if (opt.Editable) sb.Append(SigPadHtml());
        sb.Append(CollectScript());
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ---------------- 可填控件 ----------------

    /// <summary>取第 c 列的渲染语义：本表自带优先，其次语义模板（兼容单表旧数据）。</summary>
    private static SheetColumnMeta? ResolveMeta(StyledSheet sheet, HtmlRenderOptions opt, int c)
    {
        if (sheet.Columns.Count > 0)
            return c < sheet.Columns.Count ? sheet.Columns[c] : null;
        if (opt.Template is not null && c < opt.Template.Columns.Count)
        {
            var t = opt.Template.Columns[c];
            return new SheetColumnMeta
            {
                Header = t.Header,
                Kind = t.Kind,
                Fillable = t.Fillable,
                Required = t.Required,
                Options = t.Options.ToList(),
            };
        }
        return null;
    }

    /// <summary>第 r 行是否属于表体（可填控件只允许出现在表体行）。</summary>
    private static bool InBody(StyledSheet sheet, HtmlRenderOptions opt, int r)
    {
        if (sheet.BodyRowCount > 0)
            return r >= sheet.BodyStartRow && r < sheet.BodyStartRow + sheet.BodyRowCount;
        if (opt.Template is not null)
            return r >= opt.Template.BodyStartRow && r < opt.Template.BodyStartRow + opt.Template.Rows.Count;
        return false;
    }

    private static string RenderInput(SheetColumnMeta col, string key, Dictionary<string, string>? values)
    {
        var cur = values is not null && values.TryGetValue(key, out var v) ? v : "";
        var k = Esc(key);

        return col.Kind switch
        {
            FieldKind.Select => BuildSelect(col, key, k, cur),
            FieldKind.Checkbox => $"<input type=\"checkbox\" data-key=\"{k}\"{(cur == "是" ? " checked" : "")}>",
            FieldKind.Multiline => $"<textarea data-key=\"{k}\" rows=\"2\">{Esc(cur)}</textarea>",
            FieldKind.Number => $"<input type=\"number\" step=\"any\" data-key=\"{k}\" value=\"{Esc(cur)}\">",
            // 签名列：格子里放一个手写入口，值以 PNG dataURL 存在隐藏域里，
            // 这样它和普通输入走完全相同的收集/归档/回显链路（不需要额外的存储通道）。
            FieldKind.Signature => BuildSignature(k, cur),
            _ => $"<input type=\"text\" data-key=\"{k}\" value=\"{Esc(cur)}\">",
        };
    }

    private static string BuildSignature(string escKey, string cur)
    {
        var hasImg = cur.StartsWith("data:image", StringComparison.Ordinal);
        var sb = new StringBuilder();
        sb.Append("<input type=\"hidden\" data-key=\"").Append(escKey).Append("\" value=\"")
          .Append(hasImg ? Esc(cur) : "").Append("\">");
        sb.Append("<div class=\"sigwrap\" data-sig=\"").Append(escKey).Append("\" title=\"点击手写签名\">");
        sb.Append("<img class=\"sigimg\" alt=\"\"")
          .Append(hasImg ? " src=\"" + Esc(cur) + "\"" : " hidden")
          .Append(">");
        sb.Append("<span class=\"sighint\"").Append(hasImg ? " hidden" : "").Append(">✍ 点击手写</span>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildSelect(SheetColumnMeta col, string key, string escKey, string cur)
    {
        var sb = new StringBuilder();
        sb.Append("<select data-key=\"").Append(escKey).Append("\">");
        sb.Append("<option value=\"\"").Append(cur.Length == 0 ? " selected" : "").Append("></option>");
        foreach (var o in col.Options)
        {
            sb.Append("<option value=\"").Append(Esc(o)).Append('"');
            if (o == cur) sb.Append(" selected");
            sb.Append('>').Append(Esc(o)).Append("</option>");
        }
        // 回显值不在候选集里（历史数据/汇总行遗留）→ 单独给一个已选项，避免"值凭空消失"
        if (cur.Length > 0 && !col.Options.Contains(cur))
            sb.Append("<option value=\"").Append(Esc(cur)).Append("\" selected>").Append(Esc(cur)).Append("</option>");
        sb.Append("</select>");
        return sb.ToString();
    }

    // ---------------- 样式 ----------------

    private static string Css(CellStyle s, bool fillable)
    {
        var sb = new StringBuilder();
        // 字体名同样清洗引号，避免再次截断 style 属性
        var fam = s.FontName.Length > 0
            ? $"'{s.FontName.Replace("'", "").Replace("\"", "")}',{FontStack}"
            : FontStack;
        sb.Append("font-family:").Append(fam).Append(';');
        if (Math.Abs(s.FontSizePt - 11) > 0.01)
            sb.Append("font-size:").Append(F(s.FontSizePt)).Append("pt;");
        if (s.Bold) sb.Append("font-weight:600;");
        if (s.Italic) sb.Append("font-style:italic;");
        if (s.Underline) sb.Append("text-decoration:underline;");
        if (s.FontColor.Length > 0) sb.Append("color:").Append(s.FontColor).Append(';');
        if (s.FillColor.Length > 0) sb.Append("background:").Append(s.FillColor).Append(';');
        if (s.HAlign.Length > 0) sb.Append("text-align:").Append(s.HAlign).Append(';');
        if (s.VAlign.Length > 0) sb.Append("vertical-align:").Append(s.VAlign).Append(';');
        if (s.Wrap) sb.Append("white-space:pre-wrap;word-break:break-word;");
        if (s.Borders.Top.Length > 0) sb.Append("border-top:").Append(s.Borders.Top).Append(';');
        if (s.Borders.Right.Length > 0) sb.Append("border-right:").Append(s.Borders.Right).Append(';');
        if (s.Borders.Bottom.Length > 0) sb.Append("border-bottom:").Append(s.Borders.Bottom).Append(';');
        if (s.Borders.Left.Length > 0) sb.Append("border-left:").Append(s.Borders.Left).Append(';');
        if (fillable) sb.Append("padding:0;");
        return sb.ToString();
    }

    private static string BaseCss(double zoom)
    {
        var z = Math.Abs(zoom - 1.0) < 0.001 ? "" : $"zoom:{F(zoom)};";
        return
            "html,body{margin:0;padding:0;background:#f4f4f4}" +
            "body{" + z + "font-family:" + FontStack + ";font-size:11pt;color:#000}" +
            ".doc{padding:16px;background:#f4f4f4}" +
            ".sub{font-size:9pt;color:#666;padding:10px 16px 6px;text-align:center;background:#f4f4f4}" +
            ".empty{padding:24px;color:#888}" +
            // 关键：collapse 让相邻细线合并成一条，与 Excel 观感一致
            "table.sheet{border-collapse:collapse;table-layout:fixed;background:#fff}" +
            // 不用 overflow:hidden：长文本宁可撑高也不该被裁断（列宽算偏时尤其明显）
            "table.sheet td{border:0;padding:2px 4px;" +
            "  font-family:inherit;font-size:inherit;line-height:1.25;vertical-align:middle}" +
            "table.sheet td .v{display:block}" +
            "table.sheet td.fc{padding:0}" +
            "table.sheet td.fc input,table.sheet td.fc select,table.sheet td.fc textarea{" +
            "  width:100%;height:100%;box-sizing:border-box;border:0;outline:0;background:transparent;" +
            "  font-family:inherit;font-size:inherit;color:inherit;padding:2px 4px;text-align:inherit}" +
            "table.sheet td.fc input:focus,table.sheet td.fc select:focus,table.sheet td.fc textarea:focus{" +
            "  background:#fffbe6;box-shadow:inset 0 0 0 2px #f0a020}" +
            "table.sheet td.fc textarea{resize:vertical}" +
            // 签名格：整格可点，图按格缩放
            "table.sheet td.fc .sigwrap{width:100%;height:100%;min-height:22px;display:flex;" +
            "  align-items:center;justify-content:center;cursor:crosshair}" +
            "table.sheet td.fc .sigimg{max-width:100%;max-height:100%;object-fit:contain}" +
            "table.sheet td .sigimg.ro{max-width:100%;max-height:100%;object-fit:contain;display:block}" +
            "table.sheet td.fc .sighint{color:#bbb;font-size:8pt;user-select:none;white-space:nowrap}" +
            // 手写面板
            "#sigpad{position:fixed;left:0;top:0;right:0;bottom:0;background:rgba(0,0,0,.45);" +
            "  display:flex;align-items:center;justify-content:center;z-index:999}" +
            "#sigpad[hidden]{display:none}" +
            "#sigpad .panel{background:#fff;border-radius:8px;padding:14px 16px 12px;" +
            "  box-shadow:0 10px 34px rgba(0,0,0,.35)}" +
            "#sigpad .panel h3{margin:0 0 10px;font-size:12pt;font-weight:600}" +
            "#sigcanvas{background:#fff;border:1px dashed #bbb;border-radius:4px;display:block;" +
            "  touch-action:none;cursor:crosshair}" +
            "#sigpad .row{margin-top:10px;display:flex;gap:8px;justify-content:flex-end}" +
            "#sigpad .row button{font-size:10pt;padding:5px 16px;border:1px solid #ccc;" +
            "  background:#f7f7f7;border-radius:4px;cursor:pointer}" +
            "#sigpad .row button.ok{background:#2F5597;color:#fff;border-color:#2F5597}" +
            "@media print{html,body,.doc,.sub{background:#fff}" +
            "  .doc{padding:0}table.sheet{page-break-inside:auto}" +
            "  table.sheet tr{page-break-inside:avoid}@page{size:A4 landscape;margin:8mm}}";
    }

    // ---------------- 数字格式 ----------------

    /// <summary>
    /// 按 numFmt 还原显示值：不映射的话日期会显示成「46345」、百分比变「0.55」——这是保真度最容易翻车的一处。
    /// 只覆盖点检表常用的日期/百分比/千分位/小数位，其余原样输出。
    /// </summary>
    private static string FormatValue(string raw, CellStyle st)
    {
        if (raw.Length == 0) return "";
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return raw;

        var code = st.NumFmtCode;
        bool isDate = st.NumFmtId is >= 14 and <= 22 or >= 45 and <= 47
                      || (code.Length > 0 && LooksLikeDateFormat(code));

        if (isDate && num > 0)
        {
            try
            {
                var dt = DateTime.FromOADate(num);
                bool hasTime = code.Contains('h') || code.Contains('H');
                return hasTime ? dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                               : dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch { return raw; }
        }

        if (st.NumFmtId == 9 || (code.Contains('%') && !code.Contains("0.00%"))) return (num * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        if (st.NumFmtId == 10 || code.Contains("0.00%")) return (num * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        if (st.NumFmtId is 3 or 4) return num.ToString("#,##0", CultureInfo.InvariantCulture);
        if (st.NumFmtId == 2) return num.ToString("0.00", CultureInfo.InvariantCulture);
        return raw;
    }

    private static bool LooksLikeDateFormat(string code)
    {
        var c = code.ToLowerInvariant();
        if (c.Contains('%') || c.Contains('#') || c.Contains("0.0")) return false;
        return c.Contains('y') || c.Contains('d') || c.Contains("m月") || c.Contains('年');
    }

    // ---------------- 脚本（取值回传） ----------------

    private static string CollectScript() =>
        "<script>" +
        "window.__collect=function(){var o={};" +
        "document.querySelectorAll('[data-key]').forEach(function(el){var k=el.getAttribute('data-key');" +
        "o[k]=el.type==='checkbox'?(el.checked?'是':'否'):(el.value||'').trim();});" +
        "return JSON.stringify(o);};" +
        "window.__missing=function(){var a=[];" +
        "document.querySelectorAll('[data-key][data-req=\"1\"]').forEach(function(el){" +
        "var v=el.type==='checkbox'?(el.checked?'是':''):(el.value||'').trim();if(!v)a.push(el.getAttribute('data-key'));});" +
        "return JSON.stringify(a);};" +
        SigPadJs() +
        "</script>";

    /// <summary>手写面板脚本：点签名格打开 → 画 → 确定则导出 PNG dataURL 写回隐藏域。</summary>
    private static string SigPadJs() =>
        "var __sigKey=null,__sigCtx=null,__sigDrawing=false,__sigDpr=1;" +
        "function __sigOpen(key){" +
        "  var pad=document.getElementById('sigpad');if(!pad)return;" +
        "  __sigKey=key;pad.hidden=false;" +
        "  var cv=document.getElementById('sigcanvas');" +
        "  var dpr=window.devicePixelRatio||1;__sigDpr=dpr;" +
        "  var cssW=Math.min(560,Math.max(320,window.innerWidth-160));" +
        "  var cssH=Math.round(cssW*0.38);" +
        "  cv.style.width=cssW+'px';cv.style.height=cssH+'px';" +
        "  cv.width=Math.round(cssW*dpr);cv.height=Math.round(cssH*dpr);" +
        "  __sigCtx=cv.getContext('2d');__sigCtx.setTransform(dpr,0,0,dpr,0,0);" +
        "  __sigCtx.fillStyle='#fff';__sigCtx.fillRect(0,0,cssW,cssH);" +
        "  __sigCtx.strokeStyle='#111';__sigCtx.lineWidth=2.2;__sigCtx.lineCap='round';__sigCtx.lineJoin='round';" +
        "  var inp=document.querySelector('[data-key=\"'+key+'\"]');" +
        "  if(inp&&inp.value&&inp.value.indexOf('data:image')===0){" +
        "    var img=new Image();img.onload=function(){__sigCtx.drawImage(img,0,0,cssW,cssH);};img.src=inp.value;}" +
        "}" +
        "function __sigClose(){var pad=document.getElementById('sigpad');if(pad)pad.hidden=true;__sigKey=null;}" +
        "function __sigPos(ev){" +
        "  var cv=document.getElementById('sigcanvas');var r=cv.getBoundingClientRect();" +
        "  var p=(ev.touches&&ev.touches[0])||ev;" +
        "  return {x:p.clientX-r.left,y:p.clientY-r.top};}" +
        "function __sigStart(ev){ev.preventDefault();__sigDrawing=true;var q=__sigPos(ev);" +
        "  __sigCtx.beginPath();__sigCtx.moveTo(q.x,q.y);}" +
        "function __sigMove(ev){if(!__sigDrawing)return;ev.preventDefault();var q=__sigPos(ev);" +
        "  __sigCtx.lineTo(q.x,q.y);__sigCtx.stroke();}" +
        "function __sigEnd(ev){__sigDrawing=false;}" +
        "function __sigApply(){" +
        "  if(!__sigKey)return;var cv=document.getElementById('sigcanvas');" +
        "  var url=cv.toDataURL('image/png');" +
        "  var inp=document.querySelector('[data-key=\"'+__sigKey+'\"]');" +
        "  if(inp)inp.value=url;" +
        "  var wrap=document.querySelector('[data-sig=\"'+__sigKey+'\"]');" +
        "  if(wrap){var im=wrap.querySelector('.sigimg');var hi=wrap.querySelector('.sighint');" +
        "    im.src=url;im.hidden=false;hi.hidden=true;}" +
        "  __sigClose();}" +
        "document.addEventListener('click',function(ev){" +
        "  var t=ev.target;" +
        "  var w=t.closest?t.closest('[data-sig]'):null;" +
        "  if(w){__sigOpen(w.getAttribute('data-sig'));return;}" +
        "  var b=t.closest?t.closest('#sigpad button'):null;" +
        "  if(!b)return;" +
        "  var a=b.getAttribute('data-act');" +
        "  if(a==='ok')__sigApply();" +
        "  else if(a==='clear'){var cv=document.getElementById('sigcanvas');" +
        "    __sigCtx.clearRect(0,0,cv.width,cv.height);__sigCtx.fillStyle='#fff';" +
        "    __sigCtx.fillRect(0,0,cv.width,__sigCtx.canvas.height);}" +
        "  else __sigClose();" +
        "});" +
        "(function(){var cv=document.getElementById('sigcanvas');if(!cv)return;" +
        "  cv.addEventListener('mousedown',__sigStart);cv.addEventListener('mousemove',__sigMove);" +
        "  window.addEventListener('mouseup',__sigEnd);" +
        "  cv.addEventListener('touchstart',__sigStart,{passive:false});" +
        "  cv.addEventListener('touchmove',__sigMove,{passive:false});" +
        "  cv.addEventListener('touchend',__sigEnd);})();";

    /// <summary>手写面板容器（只有可填模式才输出）。</summary>
    private static string SigPadHtml() =>
        "<div id=\"sigpad\" hidden><div class=\"panel\"><h3>手写签名</h3>" +
        "<canvas id=\"sigcanvas\" width=\"560\" height=\"210\"></canvas>" +
        "<div class=\"row\"><button data-act=\"clear\">重写</button>" +
        "<button data-act=\"cancel\">取消</button>" +
        "<button class=\"ok\" data-act=\"ok\">确定</button></div></div></div>";

    // ---------------- 工具 ----------------

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>HTML 转义：导入的是任意文件，单元格文本必须全量转义。</summary>
    private static string Esc(string s) => (s ?? "")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");
}
