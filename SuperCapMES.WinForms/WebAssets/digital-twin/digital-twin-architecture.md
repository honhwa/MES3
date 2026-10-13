# 数字孪生可视化大屏 · 架构设计说明

**交付物**：`digital-twin-dashboard.html`（单文件，零依赖，双击即开）
**日期**：2026-10-13

---

## 一、整体架构思路

### 1.1 核心设计取向：单向数据流 + 单一数据源

大屏最容易踩的坑是**数据与视图纠缠**——刷新函数里同时改数据、又操作 DOM，加一个指标就要翻三个地方。

本实现采用经典的**单向数据流（Unidirectional Data Flow）**：

```
┌──────────────┐   dispatch(action)   ┌──────────────┐   通知订阅者   ┌──────────────┐
│  交互事件     │ ──────────────────> │     Store    │ ────────────> │    View      │
│ (点击/定时)   │                      │  单一数据源   │               │  只读+重绘   │
└──────────────┘                      └──────────────┘               └──────────────┘
                                              │
                                              │ 只读查询
                                              v
                                       ┌──────────────┐
                                       │  MockSource  │  定时产出快照
                                       └──────────────┘
```

**关键约束**：
- 状态**只能**通过 `Store.dispatch()` 修改，视图层绝不直接改状态
- 视图只做「读 Store → 渲染」，不做业务判断
- 业务判断全部下沉到数据源和工具函数

**收益**（实测可验证）：
- 加一个新指标 → 只改 `MockSource._build()` 和 KPI 定义数组，**UI 逻辑一行不动**
- 改刷新逻辑 → 只改 `startTimer()`，不影响渲染
- 因为单向无环，**不会出现"渲染触发渲染"的死循环**

### 1.2 分层职责

| 层 | 职责 | 关键文件/对象 | 不允许做的事 |
|---|---|---|---|
| ① 数据源层 | 定时产出指标快照、趋势历史、告警判定 | `MockSource` / `evalThreshold` / `collectAlerts` | 不碰 DOM |
| ② 状态层 | 保存快照与配置，变更广播 | `Store` | 不碰 DOM、不算业务 |
| ③ 图表层 | Canvas 自绘，纯渲染 | `Chart` | 不读 Store、不存状态 |
| ④ 视图层 | 读 Store → 组织 DOM | `View` | 不算业务、不直接改状态 |
| ⑤ 控制层 | 事件绑定 → 转成 action | `View.bindEvents` | 不直接改状态 |

### 1.3 为什么用 Canvas 自绘而不是图表库

大屏场景三条硬要求决定了选型：

1. **离线可跑**——演示环境常无外网，CDN 挂了就白屏。原生 Canvas 无此风险
2. **零依赖单文件**——一个 html 发微信/拷 U 盘就能用，不需要 `npm install`
3. **完全可控**——告警高亮、双 Y 轴、半环仪表这类定制样式，图表库要么不支持要么得深度 hack

代价是要自己处理 **DPR 高分屏缩放**（`Chart._prep()` 里统一处理），这是 Canvas 图表最容易翻车的点。

---

## 二、页面分区布局

### 2.1 视觉分区（自上而下五层）

```
┌──────────────────────────────────────────────────────────────┐
│ ① 顶栏  品牌标识 + 实时时钟 + 数据更新时间/刷新次数            │  56px
├──────────────────────────────────────────────────────────────┤
│ ② 控制条  模块切换 | 时间范围 | 重置 | 轮播 | 折叠             │  40px
├──────────────────────────────────────────────────────────────┤
│ ③ 核心指标带  6 张 KPI 卡（每类各取核心项）                    │  72px
├────────────────────────────────────────────┬─────────────────┤
│                                            │                 │
│ ④ 主体栅格（3列×2行不对称）                  │                 │
│   ┌──────────────────┬──────────┬────────┐  │                 │
│   │                  │  网络情况  │ 设备情况 │  │  高 2 行         │
│   │   生产情况        ├──────────┴────────┤  │                 │
│   │  （占 1 列 ×2 行） │   碳排放情况       │  │                 │
│   └──────────────────┴───────────────────┘  │                 │
├────────────────────────────────────────────┴─────────────────┤
│ ⑤ 告警滚动条  全屏异常汇总 / 全绿时显示正常                    │  30px
└──────────────────────────────────────────────────────────────┘
```

### 2.2 栅格策略（不对称布局的理由）

```css
#main { grid-template-columns: 1.35fr 1fr 1fr; grid-template-rows: 1fr 1fr; }
#m-production { grid-column: 1; grid-row: 1 / span 2; }  /* 生产：左列通高 */
#m-network    { grid-column: 2; grid-row: 1; }
#m-device     { grid-column: 3; grid-row: 1; }
#m-carbon     { grid-column: 2 / span 2; grid-row: 2; }  /* 碳排放：横跨两列 */
```

**为什么生产占最大**：生产是工厂最核心的业务指标，且要同时容纳「指标卡 + 趋势图 + 产线占比环图 + 产线明细表」四种内容，需要最大空间。

**为什么碳排放横跨两列**：它有两条趋势线（排放 + 能耗，量纲完全不同，需要双 Y 轴）加一个仪表盘，横向空间比纵向更合适。

### 2.3 大屏自适应方案

不用 CSS 响应式断点，而是**设计稿等比缩放**：

```js
// 以 1920×1080 为设计基准，整体 scale 铺满任意分辨率
const scale = Math.min(innerWidth / 1920, innerHeight / 1080);
viewport.style.transform = `scale(${scale})`;   // transform-origin: top left
viewport.style.left = `${(innerWidth - 1920*scale)/2}px`;   // 居中留白
```

所有尺寸写成 `calc(14px * var(--scale))`，缩放时整体等比变化，**不会出现某块被挤扁**。这是大屏（含非 16:9 拼接屏）最稳的做法——比断点式响应式更符合"大屏就是要填满"的需求。

---

## 三、组件拆分方案

### 3.1 组件清单

| 组件 | 类型 | 职责 | 关键属性/方法 |
|---|---|---|---|
| `<TopBar>` | 静态结构 + 实时文本 | 品牌、时钟、更新时间 | `clock` / `update-info` |
| `<ControlBar>` | 交互容器 | 模块切换、时间筛选、重置、轮播、折叠 | `seg-modules` / `seg-range` |
| `<KpiCard>` | 数据驱动生成 | 单个核心指标 + 迷你趋势 + 告警角标 | `--kpi-color` / `.alarm` / `.warn` |
| `<Module>` × 4 | 栅格子面板 | 四大类指标容器 | `.hidden` / `.collapsed` |
| `<StatTile>` | 数据驱动生成 | 模块内小指标卡 + 迷你进度条 | `.stat` + `.alarm` |
| `<Chart>` | 渲染组件 | 折线/面积图、环形图、堆叠条、仪表盘 | `line()` `ring()` `stackBar()` `gauge()` |
| `<AlertTicker>` | 静态 + 动态文本 | 底部异常汇总与无缝滚动 | `#ticker-text` |
| `<MaintList>` | 列表渲染 | 维保提醒条目 | `.alert-row` + `.crit` |

### 3.2 组件解耦要点

所有组件**不直接互相调用**，只通过 Store 通信：

```js
// 点「碳排放」模块按钮 → 只发 action，不直接调 View.renderCarbon
box.onclick = (e) => Store.dispatch({ type: 'TOGGLE_MODULE', id: b.dataset.mod });

// Store 广播后，render 统一重绘（谁需要更新谁自己判断）
Store.subscribe((s, a) => this.render(s, a));
```

**加新模块只需两步**：
1. `MODULES` 数组加一项 + HTML 加一个 `<section class="panel">`
2. `View` 加一个 `renderXxx()` 方法并在 `render()` 里调用

---

## 四、状态管理方案

### 4.1 状态树

```js
state = {
  snap,                    // 当前指标快照（唯一数据源）
  range: '24h',            // 时间范围筛选
  activeModules: Set,      // 显示中的模块（可隐藏）
  collapsed: Set,          // 折叠中的模块
  carousel: false,         // 自动轮播开关
  lastUpdate: Date,        // 最近更新时间
  tickCount: 0,            // 刷新计数
}
```

### 4.2 Action 类型表（7 个）

| Action | 载荷 | 副作用范围 |
|---|---|---|
| `TICK` | `{snap}` | 更新快照/时间/计数 → **全量重绘** |
| `RANGE` | `{range}` | 改时间范围 → **重绘图表**（指标卡不变） |
| `TOGGLE_MODULE` | `{id}` | 显示/隐藏模块 |
| `COLLAPSE` | `{id}` | 折叠/展开单模块 |
| `COLLAPSE_ALL` | — | 折叠/展开全部（再点恢复） |
| `CAROUSEL` | — | 轮播开关 |
| （`RANGE` 隐含） | — | 重置由 `MockSource.reset()` 触发 `TICK` |

### 4.3 为什么用 Set 而不是数组存模块状态

模块的显示/折叠是**纯开关语义**，用 `Set` 的 `has/add/delete` 天然表达，且 `MODULES.every(m => set.has(m.id))` 一行就能判断"是否全折叠"。换成数组要做去重和索引查找，反而更绕。

### 4.4 渲染粒度策略

当前实现是 `TICK` 触发**全量重绘**（所有模块重画）。大屏 4 个模块、30KB JS 的规模下完全够用（实测刷新 <16ms）。

若后续模块增到十几个，应改为**按需重绘**：

```js
// 优化方向：render(s, action) 内按 action.type 分派
if (action.type === 'RANGE') { this.renderProduction(...); this.renderNetwork(...); return; }
if (action.type === 'TICK')    { /* 全量 */ }
```

Store 的订阅机制已天然支持这种粒度拆分，无需改动数据层。

### 4.5 确定性数据源（重要设计）

`MockSource` 用 **seeded LCG**（线性同余）伪随机，而非 `Math.random()`：

```js
function makeRng(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
}
```

**原因**：点「重置数据」时用 `Date.now() % 100000` 作新 seed，可以做到**同 seed 结果完全可复现**。这在真实项目里同样重要——写测试、复现 bug、给领导演示同一个场景，都需要数据可复现。已用断言验证（同 seed 两次 tick 结果逐位相同）。

---

## 五、交互能力实现

| 需求 | 实现 | 关键代码 |
|---|---|---|
| **模块切换** | 模块按钮组 → `TOGGLE_MODULE` → 加/删 `.hidden` | `Store.activeModules` |
| **折叠/隐藏** | 右上角箭头 → `COLLAPSE` → `.collapsed`（body 隐藏）；另有全部折叠 | `Store.collapsed` |
| **时间范围筛选** | 1h/6h/24h/7d 四档 → `RANGE` → `MockSource.series(range, key)` 重取序列 | `RANGES` 定义 points + stepMs |
| **近实时重置** | 重置 seed + 立即 `tick()` + Toast 提示 | `MockSource.reset(Date.now()%100000)` |
| **自动轮播** | `carousel=true` 时每 3 次 tick 轮换焦点模块高亮；控制条淡出（`.cinema`） | `View.rotateFocus()` |
| **异常高亮告警** | 统一 `evalThreshold()` 判定 → KPI 卡呼吸描边 + 统计卡变色 + 底部滚动条 | `THRESHOLDS` 表 |
| **实时更新时间** | 顶栏显示时分秒 + 第 N 次刷新；另有独立时钟每秒走 | `#update-info` |

### 告警判定集中化

所有阈值判断收敛到**一个函数**，避免各处写死不同标准：

```js
const THRESHOLDS = {
  yieldRate:  { warn: 97.5, crit: 96.0, dir: 'below' },  // 越低越差
  latency:    { warn: 35,   crit: 60,   dir: 'above' },  // 越高越差
  // ...
};

function evalThreshold(key, value) {
  const th = THRESHOLDS[key];
  if (!th) return 'ok';
  return th.dir === 'below'
    ? (value <= th.crit ? 'crit' : value <= th.warn ? 'warn' : 'ok')
    : (value >= th.crit ? 'crit' : value >= th.warn ? 'warn' : 'ok');
}
```

KPI 带、模块统计卡、底部滚动条**共用这一份判定**，不会出现"卡片红了但滚动条说正常"的矛盾。真实项目中 `THRESHOLDS` 应从配置中心/设备台账下发。

---

## 六、自测结果

| 项 | 结果 |
|---|---|
| JS 语法解析 | 通过（30,290 字符） |
| 顶层运行时异常 | 无 |
| 逻辑断言 | **30 / 30 PASS** |

覆盖范围：快照字段完整性、数值区间合法性、设备数自洽（运行+待机+故障 ≤ 总数）、阈值双向判定（越低/越高两类）、同 seed 可复现、告警结构合法性、四档时间范围序列长度、Store 全部 6 个 action、订阅/退订。

---

## 七、落地到生产环境的后续演进

当前是纯前端模拟，接真实系统时改三处即可：

1. **数据源替换** — `MockSource.tick()` 换成 WebSocket 推送或轮询 API，返回同样结构的快照对象；Store 与视图层**完全不用改**
2. **阈值下发** — `THRESHOLDS` 改为从配置中心接口拉取，支持不同产线不同阈值
3. **历史趋势** — `series()` 的"最近点匹配"换成真实时序数据库查询（InfluxDB / TDengine 等）

### 可选增强
- **多工厂下钻**：点击模块 → 进入该产线二级大屏
- **告警联动**：严重告警时弹出确认框，要求责任人签收（内控要求）
- **回放模式**：用历史快照做事故回溯（数字孪生的高阶能力）
- **嵌入 MES3**：WinForms 用 WebView2 控件嵌此页面，复用现有 `MesDatabase` 数据源