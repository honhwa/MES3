# 打包离线演示包：复制页面 + zip + 完整性校验
import os, shutil, zipfile, sys, hashlib

SRC = r"E:\agent\MES3\SuperCapMES.WinForms\WebAssets\digital-twin"
PKG = r"E:\agent\MES3\.artifacts\offline-package"
ZIP = r"E:\agent\MES3\.artifacts\泰州超级电容工厂-展厅演示包.zip"

pages = ["showcase.html", "cockpit.html", "production-twin.html", "device-twin.html",
         "carbon-twin.html", "network-twin.html", "trace-query.html",
         "gov-submission.html", "digital-twin-dashboard.html"]
docs = ["使用说明.md", "导演脚本.md"]

# 1) 清理旧副本（仅删本包内 html，保留说明与脚本）
for f in os.listdir(PKG):
    if f.endswith(".html"):
        os.remove(os.path.join(PKG, f))

# 2) 复制页面
copied = []
for f in pages:
    s = os.path.join(SRC, f)
    if not os.path.exists(s):
        print("MISSING", f); sys.exit(1)
    shutil.copy2(s, os.path.join(PKG, f))
    copied.append(f)

# 3) 校验副本与源码一致（MD5）
mismatch = [f for f in copied
            if hashlib.md5(open(os.path.join(SRC, f), 'rb').read()).hexdigest()
            != hashlib.md5(open(os.path.join(PKG, f), 'rb').read()).hexdigest()]
if mismatch:
    print("MISMATCH", mismatch); sys.exit(1)

# 4) 打 zip（包内相对路径）
if os.path.exists(ZIP):
    os.remove(ZIP)
with zipfile.ZipFile(ZIP, "w", zipfile.ZIP_DEFLATED) as z:
    for f in copied + docs:
        z.write(os.path.join(PKG, f), arcname="展厅演示包/" + f)

# 5) zip 完整性校验
with zipfile.ZipFile(ZIP) as z:
    bad = z.testzip()
    names = z.namelist()
    if bad:
        print("CORRUPT", bad); sys.exit(1)
    expected = ["展厅演示包/" + f for f in copied + docs]
    missing = [e for e in expected if e not in names]

print("PAGES", len(copied), "DOCS", len(docs), "MD5-OK")
print("ZIP", ZIP, "entries", len(names), "missing", missing)
print("SIZE_KB", round(os.path.getsize(ZIP) / 1024, 1))
if missing:
    sys.exit(1)
