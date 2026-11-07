"""WinForms .Designer.cs 静态检查：确认 InitializeComponent() 只含 Designer 可解析陈述。

用法：python .artifacts/check_designer.py [目录...]
预设检查 SuperCapMES.WinForms/Forms 与 SuperCapMES.WinForms 根目录下的 *.Designer.cs。
exit 0 = 全部通过；exit 1 = 有违规。
"""
from __future__ import annotations
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DIRS = [ROOT / "SuperCapMES.WinForms" / "Forms", ROOT / "SuperCapMES.WinForms"]

FORBIDDEN = re.compile(
    r"\bforeach\b|\bfor\s*\(|\bwhile\s*\(|\bif\s*\(|\bswitch\s*\(|\btry\b|\bcatch\b"
    r"|\breturn\b|\bthrow\b|\bvar\b|=>|\$\"|\?\.|\?\?|null!|\bMesDatabase\b"
    r"|\bDateTime\.|\bEnumerable\b|\.Cast<|\.Select\(|\.Where\(|\.ToList\("
)

# 允许的陈述形状（每行一条，去掉注解与字串内容后再比对）
ALLOWED = re.compile(
    r"^\s*this\.[\w.]+\s*=\s*new .*;\s*$"                       # new / 属性赋值
    r"|^\s*this\.[\w.]+\s*=\s*[^;]+;\s*$"                         # 一般赋值
    r"|^\s*this\.[\w.]*Controls\.Add\([^;]*\);\s*$"
    r"|^\s*this\.[\w.]+\.Items\.AddRange\([^;]*\);\s*$"
    r"|^\s*this\.[\w.]+\.Items\.Add\([^;]*\);\s*$"
    r"|^\s*this\.[\w.]+\.TabPages\.Add\([^;]*\);\s*$"
    r"|^\s*this\.[\w.]+\.(ColumnStyles|RowStyles)\.Add\([^;]*\);\s*$"
    r"|^\s*this\.[\w.]+\.Set(Column|Row)Span\([^;]*\);\s*$"
    r"|^\s*this\.[\w.]*(SuspendLayout|PerformLayout)\(\);\s*$"
    r"|^\s*this\.[\w.]*ResumeLayout\((false|true)?\);\s*$"
    r"|^\s*this\.[\w.]+\s*\+=\s*new [\w.]+\(this\.[\w.]+\);\s*$"
    r"|^\s*this\.[\w.]+\s*-=\s*new [\w.]+\(this\.[\w.]+\);\s*$"
)


def strip_comments_and_strings(text: str) -> str:
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            # 逐字/@ 或一般字串：整段以空白取代
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                j += 1
            out.append(" " * (j - i))
            i = j
        elif text.startswith("//", i):
            j = text.find("\n", i)
            if j < 0:
                j = n
            out.append(" " * (j - i))
            i = j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("".join(ch if ch == "\n" else " " for ch in text[i:j]))
            i = j
        else:
            out.append(c)
            i += 1
    return "".join(out)


def extract_body(text: str, signature: str) -> str | None:
    m = re.search(re.escape(signature) + r"\s*\{", text)
    if not m:
        return None
    start = m.end()
    depth = 1
    clean = strip_comments_and_strings(text)
    i = start
    while i < len(clean) and depth:
        if clean[i] == "{":
            depth += 1
        elif clean[i] == "}":
            depth -= 1
        i += 1
    return text[start:i - 1] if depth == 0 else None


def check(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8-sig")
    errors: list[str] = []
    if text.startswith("\ufeff"):
        errors.append("含 BOM")
    if "#nullable disable" not in text:
        errors.append("缺 #nullable disable")
    if "protected override void Dispose(bool disposing)" not in text:
        errors.append("缺 Dispose(bool) 覆写")
    if re.search(r"^using ", text, re.M):
        errors.append("含 using（Designer.cs 应使用完整限定名）")

    body = extract_body(text, "private void InitializeComponent()")
    if body is None:
        errors.append("找不到 / 无法解析 InitializeComponent()")
        return errors

    clean = strip_comments_and_strings(body)
    # 以分号切分陈述（允许多行，如 ToolStripItem[] 阵列初始设定式）
    buf, start_no = [], 1
    for no, raw in enumerate(clean.splitlines(), 1):
        line = raw.strip()
        if not line:
            continue
        if not buf:
            start_no = no
        buf.append(line)
        if line.endswith(";"):
            stmt = " ".join(buf)
            buf = []
            if stmt in ("{", "}"):
                continue
            if FORBIDDEN.search(stmt):
                errors.append(f"InitializeComponent L{start_no}: 禁用语法 -> {stmt[:110]}")
            elif not ALLOWED.match(stmt):
                errors.append(f"InitializeComponent L{start_no}: 非白名单陈述 -> {stmt[:110]}")
    if buf:
        errors.append(f"InitializeComponent L{start_no}: 未以分号结束 -> {' '.join(buf)[:110]}")

    # 颜色必须是 VS canonical
    for no, line in enumerate(text.splitlines(), 1):
        if "Color.FromArgb(" in line and "((int)(((byte)" not in line:
            errors.append(f"L{no}: Color.FromArgb 非 VS canonical 形式")

    # 栏位宣告不得带 readonly / null! / 初始值
    for no, line in enumerate(text.splitlines(), 1):
        m = re.match(r"^\s*private\s+(?!System\.ComponentModel\.IContainer)([\w.<>?]+)\s+([\w]+)\s*(=.*)?;\s*$", line)
        if m and m.group(3):
            errors.append(f"L{no}: 控件栏位带初始值 -> {line.strip()[:110]}")
        if m and "readonly" in line:
            errors.append(f"L{no}: 控件栏位 readonly -> {line.strip()[:110]}")
    return errors


def main() -> int:
    dirs = [Path(p) for p in sys.argv[1:]] if len(sys.argv) > 1 else DEFAULT_DIRS
    files = sorted({f for d in dirs for f in d.glob("*.Designer.cs")})
    if not files:
        print("找不到任何 .Designer.cs")
        return 1
    failed = 0
    for f in files:
        errs = check(f)
        status = "OK" if not errs else f"FAIL ({len(errs)})"
        print(f"[{status}] {f.relative_to(ROOT)}")
        for e in errs:
            print(f"    - {e}")
        if errs:
            failed += 1
    print(f"\n共 {len(files)} 份 Designer.cs，通过 {len(files) - failed}，失败 {failed}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
