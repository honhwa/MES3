# 数字孪生页面（WebAssets）

本目录是 MES3 桌面系统内置的**数字孪生网页**，由「数字孪生」导航分类下的入口页跳转访问。

## 页面清单

| 文件 | 说明 | 对应 MES3 模块 |
|---|---|---|
| `digital-twin-dashboard.html` | 运营大屏（生产/网络/设备/碳排放四大类指标） | 总览 › 生产看板 |
| `cockpit.html` | 经营驾驶舱（健康度总评 + 结论层 + KPI + 告警 + 趋势，管理层决策视图） | 总览 › 生产看板 |
| `production-twin.html` | 生产情况孪生（产线架构视图 + 设备实时产量/良率/稼动 + 悬停在制品详情） | 总览 › 生产看板 |
| `carbon-twin.html` | 碳排放孪生（产线碳排视图 + 悬停近一小时排放曲线 + 超阈值红色预警 + 阈值滑块） | 能源与碳 › 碳排放核算 |
| `trace-query.html` | 批次追溯查询（正向工艺链路/检验记录 + 物料批号反查 + 质量闭环，客户审厂演示） | 质量 › 质量追溯 |
| `device-twin.html` | 设备数字孪生（2.5D 等距厂房 + 产线 + 设备点位） | 设备与人员 › 设备孪生 |
| `network-twin.html` | 网络数字孪生（核心/汇聚/接入/终端四层拓扑） | 网络拓扑 |
| `digital-twin-architecture.md` | 架构设计说明（单向数据流 / 组件拆分 / 状态管理） | — |

## 访问方式

从 MES3 左侧导航 **「数字孪生」** 分类进入，默认提供两种打开方式：

1. **新标签页打开**（默认，零依赖）
   浏览器直接打开本地 HTML 文件，适合演示、投屏、双屏投放。
2. **内嵌展示**（需启用 WebView2，见下方）
   在 MES3 窗体内部嵌显示，适合一体机大屏。

两个孪生页右上角/左上角均有「← 返回运营大屏」，大屏内也可跳回设备/网络孪生页。

## 内嵌模式（WebView2）说明

内嵌需要 `Microsoft.Web.WebView2` 包。若项目未引用该包，MES3 会**自动降级为新标签页打开**并在界面上给出提示，不会报错或崩溃。

启用方式：

```powershell
dotnet add package Microsoft.Web.WebView2
```

添加后重新构建，入口页的「内嵌展示」按钮即自动可用。

> 运行时还需系统已安装 **Microsoft Edge WebView2 Runtime**
> （Win11 / Win10 21H2 及以上通常已随 Edge 预装）。

## 目录与部署

页面文件会随构建复制到输出目录：

```
bin/Debug/net10.0-windows/WebAssets/digital-twin/*.html
```

`SuperCapMES.WinForms.csproj` 中通过 `WebAssets/**` 的 `Content` + `CopyToOutputDirectory` 项保证
"复制到输出目录"。因此**直接运行 exe 或从 VS 启动都能找到页面**，不依赖源码目录。

## 修改页面后的生效方式

页面是纯静态文件，**改完直接重新构建即可**（或手动拷到 `bin/.../WebAssets/digital-twin/` 立即生效，无需重启 MES3）。

页面本身零外部依赖（不引 CDN、不引图表库），可单独双击打开用于独立演示。
