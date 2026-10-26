/* ==========================================================================
 * sim-core.js — MES 演示用「统一仿真内核」
 *
 * 定位：给数字孪生各页提供**同一份台账 + 同一条虚拟时间轴**，
 *       让"生产说在产的批次，仓储/质量页查得到"，且整场演示可复现。
 *
 * 与原来各页内置 mock 的区别（这是"仿真感"的来源）：
 *   · 原来：每 3 秒掷一次骰子决定设备状态 → 状态乱跳、产量忽高忽低、讲不通
 *   · 现在：设备状态由「停机窗口」与虚拟时间决定 → 产量单调累进、
 *           故障有明确起止（09:20 故障、09:47 恢复）、同一 seed 同一 t 必得同一画面
 *
 * 三条硬性约束：
 *   ① 零依赖、零外链，file:// 双击即可运行（不要求起服务器）
 *   ② 确定性：snapshot 只依赖 (seed, 虚拟分钟 t)，不依赖调用次数
 *   ③ 视图层不改：通过覆盖 MockSource.tick() 接入，现有页面渲染代码一行不动
 *
 * 时钟同步：父窗口（demo.html）用 postMessage 广播 {t, speed, paused}，
 *           各页在 file:// 下也能收到（postMessage 不受同源读 DOM 的限制）。
 * ========================================================================== */
(function (global) {
  'use strict';

  /* ---------------------------------------------------------------- 常量 */

  // 与 production-twin.html 完全一致的工厂台账设定（同一坐标、同一产线命名）
  var SHIFT_HOURS = 8;
  var SHIFT_START_MIN = 8 * 60;                 // 08:00 开班
  var SHIFT_LEN_MIN = SHIFT_HOURS * 60;         // 8 小时 = 480 分钟

  var LINES = [
    { id: 'L1', name: 'L1 卷绕线', type: '卷绕' },
    { id: 'L2', name: 'L2 涂布线', type: '涂布' },
    { id: 'L3', name: 'L3 装配线', type: '装配' },
    { id: 'L4', name: 'L4 化成线', type: '化成' }
  ];
  var ZONES = ['A区 原料预处理', 'B区 电极制造', 'C区 电芯装配', 'D区 后处理化成'];
  var LINE_ZONE = { L1: 0, L2: 1, L3: 2, L4: 3 };

  var TYPES_FOR = {
    '卷绕': ['卷绕机', '注液机', '检测仪'],
    '涂布': ['涂布机', '检测仪'],
    '装配': ['装配机', '注液机'],
    '化成': ['化成柜', '检测仪']
  };

  var DEV_TYPES = {
    '卷绕机': { icon: '🔄', stage: '卷绕', stageIdx: 2, takt: 6.8 },
    '涂布机': { icon: '🎨', stage: '涂布', stageIdx: 1, takt: 9.4 },
    '装配机': { icon: '🔧', stage: '装配', stageIdx: 3, takt: 7.2 },
    '化成柜': { icon: '🔋', stage: '化成', stageIdx: 4, takt: 22.5 },
    '注液机': { icon: '💧', stage: '注液', stageIdx: 5, takt: 6.0 },
    '检测仪': { icon: '🔍', stage: '检测', stageIdx: 6, takt: 4.2 }
  };

  var CREW_POOL = [
    ['甲班', '张伟', '李强', '王磊'],
    ['乙班', '陈晨', '刘洋', '赵敏'],
    ['丙班', '孙军', '周洁', '吴昊']
  ];
  var MODELS = ['SC-2.7V-3000F', 'SC-3.0V-1200F', 'SC-2.7V-5000F'];

  var FAULT_MSGS = ['主轴温度超限报警', '张力波动异常', '通讯中断重连', '驱动过载保护', '传感器数据跳变'];
  var IDLE_MSGS = ['计划停机', '换型调整', '等待物料', '工单间隙', '计划保养'];

  /* ------------------------------------------------------------ 确定性随机 */

  /** mulberry32：同 seed 同序列，跨浏览器一致（不依赖 Math.random） */
  function makeRng(seed) {
    var a = seed >>> 0;
    return function () {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      var t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  /** 派生一个"只跟 (seed, 序号) 有关"的稳定随机值 —— 用于逐设备属性 */
  function hash01(seed, i) {
    return makeRng((seed * 2654435761 + i * 40503) >>> 0)();
  }

  /* ------------------------------------------------------------------ 时钟 */

  var Clock = {
    seed: 1014,
    t0: SHIFT_START_MIN,       // 起始虚拟分钟
    speed: 60,                 // 60x：1 实秒 = 1 虚拟分钟（8 小时班 → 8 分钟演完）
    paused: false,
    _epoch: Date.now(),
    _frozen: null,

    /** 当前虚拟分钟（08:00 = 480） */
    now: function () {
      if (this.paused) return this._frozen == null ? this.t0 : this._frozen;
      return this.t0 + (Date.now() - this._epoch) / 1000 * this.speed / 60;
    },
    set: function (t, speed) {
      this.t0 = t;
      if (speed) this.speed = speed;
      this._epoch = Date.now();
      this._frozen = null;
    },
    pause: function () { if (!this.paused) { this._frozen = this.now(); this.paused = true; } },
    resume: function () { if (this.paused) { this.t0 = this._frozen; this._epoch = Date.now(); this.paused = false; this._frozen = null; } },
    reset: function () { this.t0 = SHIFT_START_MIN; this._epoch = Date.now(); this.paused = false; this._frozen = null; },
    /** 班次内进度 0~1 */
    progress: function () {
      return Math.max(0, Math.min(1, (this.now() - SHIFT_START_MIN) / SHIFT_LEN_MIN));
    },
    /** "HH:MM" 形式的班内时刻 */
    hhmm: function (t) {
      var m = Math.floor(t == null ? this.now() : t);
      var h = Math.floor(m / 60), mm = m % 60;
      return (h < 10 ? '0' : '') + h + ':' + (mm < 10 ? '0' : '') + mm;
    }
  };

  /* ------------------------------------------------------------------ 台账
   * 20 台设备 + 8 张工单。**这里是全厂唯一真相源**：
   * 生产页看设备在制、仓储页看批次去向、质量页看检验结果，都从这份台账派生。
   */

  function buildLedger(seed) {
    var devices = [];
    var wos = [];

    // ---- 8 张工单（先建工单池，再绑设备）----
    for (var w = 0; w < 8; w++) {
      var rw = makeRng((seed + 7001 + w * 131) >>> 0);
      var model = MODELS[Math.floor(rw() * MODELS.length)];
      var qty = 600 + Math.floor(rw() * 900);          // 600~1500 只
      wos.push({
        wo: 'WO-2026-1026-' + String(w + 1).padStart(3, '0'),
        model: model,
        qty: qty,
        lot: 'LOT-20261026-' + String.fromCharCode(65 + w),   // A~H
        due: '2026-10-2' + (6 + (w % 3)),
        priority: w < 2 ? '加急' : (w < 5 ? '正常' : '备货')
      });
    }

    // ---- 20 台设备：每条线 5 台 ----
    var wi = 0;
    LINES.forEach(function (line, li) {
      var types = TYPES_FOR[line.type];
      for (var i = 0; i < 5; i++) {
        var type = types[i % types.length];
        var tpl = DEV_TYPES[type];
        var no = line.id + '-' + String(i + 1).padStart(2, '0');
        var h = li * 5 + i;                            // 全局序号（稳定哈希用）

        var plan = 700 + Math.floor(hash01(seed, h * 3 + 1) * 800);      // 本班计划 700~1500
        var startMin = SHIFT_START_MIN + Math.floor(hash01(seed, h * 3 + 2) * 35); // 08:00~08:35 陆续开机
        var yBase = 97.6 + hash01(seed, h * 3 + 3) * 2.0;                 // 基线良率
        var crew = CREW_POOL[Math.floor(hash01(seed, h * 3 + 4) * CREW_POOL.length)];
        var wo = wos[wi++ % wos.length];

        // ---- 停机窗口：演示的"故事线"就在这里 ----
        // 设备不是每 3 秒随机跳状态，而是在班内被安排了若干明确的停机区间，
        // 于是"09:20 故障 / 09:47 恢复"可以照着讲，产量曲线也单调平滑。
        var windows = [];
        var cursor = startMin + 60 + hash01(seed, h * 5 + 11) * 60;   // 开机后 1~2h 才可能停
        var guard = 0;
        while (cursor < SHIFT_START_MIN + SHIFT_LEN_MIN && guard++ < 8) {
          var kind = hash01(seed, h * 7 + guard * 13) > 0.62 ? 'fault' : 'idle';
          var dur = kind === 'fault'
            ? 4 + hash01(seed, h * 7 + guard * 17) * 16      // 故障 4~20 分钟
            : 6 + hash01(seed, h * 7 + guard * 19) * 20;     // 计划停机 6~26 分钟
          var pool = kind === 'fault' ? FAULT_MSGS : IDLE_MSGS;
          var msg = pool[Math.floor(hash01(seed, h * 7 + guard * 23) * pool.length)];
          windows.push({ from: cursor, to: Math.min(SHIFT_START_MIN + SHIFT_LEN_MIN, cursor + dur), kind: kind, msg: msg });
          cursor += dur + 70 + hash01(seed, h * 7 + guard * 29) * 120;   // 下一段稳定运行 70~190 分钟
        }

        devices.push({
          no: no, id: 'EQ-' + no,
          name: no + ' ' + type,
          type: type, icon: tpl.icon,
          stage: tpl.stage, stageIdx: tpl.stageIdx,
          lineId: line.id, lineName: line.name,
          zone: ZONES[LINE_ZONE[line.id]],
          plan: plan, startMin: startMin,
          yBase: yBase, crew: crew, wo: wo,
          windows: windows
        });
      }
    });

    return { devices: devices, wos: wos, seed: seed };
  }

  /* ------------------------------------------------- 台账 → 时点状态（纯函数）
   * 关键：所有派生都只依赖 (dev, t)，不依赖调用次数 —— 这是可重放与跨页一致的前提。
   */

  /** 设备在 t 时刻的状态：run / idle / fault */
  function stateAt(dev, t) {
    for (var i = 0; i < dev.windows.length; i++) {
      var w = dev.windows[i];
      if (t >= w.from && t < w.to) return { status: w.kind, reason: w.msg, since: w.from };
    }
    return { status: 'run', reason: '', since: dev.startMin };
  }

  /** 设备在 t 时刻的累计有效运行分钟（扣掉已发生的停机） */
  function runMinutesAt(dev, t) {
    var run = Math.max(0, Math.min(t, SHIFT_START_MIN + SHIFT_LEN_MIN) - dev.startMin);
    var down = 0;
    for (var i = 0; i < dev.windows.length; i++) {
      var w = dev.windows[i];
      if (t <= w.from) break;
      down += Math.min(t, w.to) - w.from;
    }
    return Math.max(0, run - down);
  }

  /** 累计故障分钟（用于良率劣化） */
  function faultMinutesAt(dev, t) {
    var f = 0;
    for (var i = 0; i < dev.windows.length; i++) {
      var w = dev.windows[i];
      if (w.kind !== 'fault' || t <= w.from) continue;
      f += Math.min(t, w.to) - w.from;
    }
    return f;
  }

  /** 设备在 t 时刻的产出与良率
   *
   * 产出必须用「有效运行分钟 × 固定速率」累加，**不能用 plan × (run/total) 反算**：
   * 后者在设备停机时 run 不增而 total 在增，比例会下降，产量就会往回跳
   * （实测 t=600 时 21483 → 21738 → 21483，大屏上会看到数字倒退）。
   */
  function metricsAt(dev, t) {
    var end = Math.min(t, SHIFT_START_MIN + SHIFT_LEN_MIN);
    var span = Math.max(1, SHIFT_START_MIN + SHIFT_LEN_MIN - dev.startMin);
    var run = runMinutesAt(dev, end);
    var rate = dev.plan / span;                          // 只/分钟（全程无停机的理论速率）
    var output = Math.floor(run * rate);                 // 运行分钟单调增 → 产量单调增
    var fm = faultMinutesAt(dev, end);
    // 良率按整数分钟取样：同一分钟内稳定，跨分钟才变（否则每帧抖动，看着不真实）
    var ym = Math.floor(end);
    var y = dev.yBase - fm * 0.018 + (hash01(dev.startMin + ym * 7, dev.stageIdx) - 0.5) * 0.25;
    return {
      output: output,
      yield: Math.max(96.8, Math.min(99.8, y)),
      runHours: run / 60,
      runRatio: run / span
    };
  }

  /** 在制工单（运行中的设备才有） */
  function wipAt(dev, t, met) {
    if (stateAt(dev, t).status !== 'run') return null;
    var plan = Math.ceil(dev.plan * 0.72 / 50) * 50;
    var takt = DEV_TYPES[dev.type].takt;
    var etaMin = dev.startMin + Math.round(plan * takt / 60);
    return {
      wo: dev.wo.wo, model: dev.wo.model, batch: dev.wo.lot,
      stage: dev.stage, stageIdx: dev.stageIdx,
      plan: plan,
      done: Math.min(plan, Math.round(met.output * 0.72)),
      crew: dev.crew,
      eta: '预计 ' + Clock.hhmm(etaMin) + ' 完成',
      priority: dev.wo.priority,
      paused: false
    };
  }

  /* -------------------------------------------------------------- 快照构造 */

  var Ledger = null;

  function ensureLedger() {
    if (!Ledger || Ledger.seed !== Clock.seed) Ledger = buildLedger(Clock.seed);
    return Ledger;
  }

  /** 生产孪生页需要的快照（与 production-twin 的 snapshot() 同构） */
  function snapProduction(t) {
    var L = ensureLedger();
    var devices = L.devices.map(function (d) {
      var st = stateAt(d, t);
      var met = metricsAt(d, t);
      return {
        id: d.id, no: d.no, name: d.name, type: d.type, icon: d.icon,
        lineId: d.lineId, lineName: d.lineName, zone: d.zone,
        status: st.status, reason: st.reason, since: st.since,
        output: met.output, outputBase: d.plan,
        yield: met.yield, yieldBase: d.yBase,
        runHours: met.runHours, runBase: Math.round(met.runRatio * SHIFT_HOURS * 10) / 10,
        wip: wipAt(d, t, met)
      };
    });

    var byLine = {};
    LINES.forEach(function (l) {
      byLine[l.id] = { line: l, output: 0, run: 0, idle: 0, fault: 0, runHours: 0, yNum: 0, yDen: 0 };
    });
    devices.forEach(function (d) {
      var b = byLine[d.lineId];
      b.output += d.output;
      b.runHours += d.runHours;
      b.yNum += d.yield * d.output;
      b.yDen += d.output;
      if (d.status === 'run') b.run++;
      else if (d.status === 'idle') b.idle++;
      else b.fault++;
    });

    var alerts = devices.filter(function (d) { return d.status !== 'run'; })
      .map(function (d) {
        return {
          devId: d.id, devNo: d.no, lineName: d.lineName, zone: d.zone,
          level: d.status === 'fault' ? 'fault' : 'idle',
          msg: d.reason || '未运行',
          time: d.since
        };
      })
      .sort(function (a, b) {
        return a.level === b.level ? b.time - a.time : (a.level === 'fault' ? -1 : 1);
      });

    return {
      t: t,
      devices: devices,
      alerts: alerts,
      byLine: byLine,
      total: devices.length,
      run: devices.filter(function (d) { return d.status === 'run'; }).length,
      idle: devices.filter(function (d) { return d.status === 'idle'; }).length,
      fault: devices.filter(function (d) { return d.status === 'fault'; }).length
    };
  }

  /** 仓储视角：工单/批次的去向（供后续仓储页接入，本轮先提供数据） */
  function snapWarehouse(t) {
    var L = ensureLedger();
    return L.wos.map(function (w) {
      var dev = L.devices.filter(function (d) { return d.wo.wo === w.wo; })[0];
      var met = dev ? metricsAt(dev, t) : { output: 0 };
      var done = dev ? Math.min(w.qty, Math.round(met.output * 0.72)) : 0;
      var ratio = done / Math.max(1, w.qty);
      var stage = ratio >= 1 ? '待入库' : ratio > 0.6 ? '后段工序' : ratio > 0.15 ? '前段工序' : '待开工';
      return {
        wo: w.wo, model: w.model, lot: w.lot, qty: w.qty, due: w.due, priority: w.priority,
        done: done, progress: Math.round(ratio * 100), stage: stage,
        lineName: dev ? dev.lineName : '-'
      };
    });
  }

  /** 事件流：停机窗口的开始/结束（时间倒序，供字幕与事件面板） */
  function events(t, limit) {
    var L = ensureLedger();
    var out = [];
    L.devices.forEach(function (d) {
      d.windows.forEach(function (w) {
        if (w.from <= t) {
          out.push({
            t: w.from, level: w.kind, dev: d.no, line: d.lineName,
            msg: w.msg + '（' + (w.kind === 'fault' ? '故障' : '停机') + '开始）'
          });
        }
        if (w.to <= t) {
          out.push({
            t: w.to, level: 'ok', dev: d.no, line: d.lineName,
            msg: '恢复正常'
          });
        }
      });
    });
    out.sort(function (a, b) { return b.t - a.t; });
    return limit ? out.slice(0, limit) : out;
  }

  /** 班次级汇总（大屏顶部指标） */
  function summary(t) {
    var s = snapProduction(t);
    var L = ensureLedger();
    var out = 0, yNum = 0, yDen = 0;
    s.devices.forEach(function (d) { out += d.output; yNum += d.yield * d.output; yDen += d.output; });
    var planTotal = L.devices.reduce(function (a, d) { return a + d.plan; }, 0);
    return {
      t: t,
      clock: Clock.hhmm(t),
      progress: Clock.progress(),
      output: out,
      plan: planTotal,
      attain: planTotal > 0 ? Math.min(100, out / planTotal * 100) : 0,
      yield: yDen > 0 ? yNum / yDen : 0,
      run: s.run, idle: s.idle, fault: s.fault, total: s.total,
      faultCount: s.fault
    };
  }

  /* --------------------------------------------------- 接入现有 MockSource
   *
   * 两种页面形态，两种接法：
   *   A. 生产页：直接换掉 tick()，返回本内核的 snapshot（设备/产量/在制全部同源）
   *   B. 仓储/质量/设备页：它们有各自的 _build() 与 series()，**不重建**，
   *      只接管随机源 —— 把 _rng 在每次 tick 前重置为 f(seed, 虚拟分钟)。
   *      这样同一 (seed, t) 必得同一画面（可重放），而 _build/series/视图一行不改。
   *
   * 另外两个必须一起处理的问题（否则演示时会自相矛盾）：
   *   1. series() 原本用 Date.now() 做 X 轴 → 与虚拟时钟脱节，曲线和实时值打架。
   *      这里改成用虚拟时钟逐点重建（确定性），曲线与当前值同源。
   *   2. 各页自己造批号（LOT-yyyyMMdd-xx）→ 生产说在产的批号，仓储查不到。
   *      这里把各页的 batch/lot 统一映射到台账里的 8 张工单批次。
   */

  var PAGE_KIND = null;   // 'production' | 'warehouse' | 'quality' | 'device'

  /** 台账批次池（与生产页同源） */
  function lotPool() {
    return ensureLedger().wos.map(function (w) { return w.lot; });
  }

  /** 台账工单池（与生产页同源） */
  function woPool() {
    return ensureLedger().wos.map(function (w) { return w.wo; });
  }

  /** 把对象里所有 batch / lot 字段统一到台账批次（按稳定序号轮转，保证同 t 同值） */
  function unifyBatches(node, depth, counter) {
    if (!node || typeof node !== 'object' || depth > 6) return counter;
    if (Array.isArray(node)) {
      for (var i = 0; i < node.length; i++) counter = unifyBatches(node[i], depth + 1, counter);
      return counter;
    }
    var pool = lotPool();
    var wos = woPool();
    if ('batch' in node && typeof node.batch === 'string' && /^LOT-/.test(node.batch)) {
      node.batch = pool[counter % pool.length]; counter++;
    }
    if ('lot' in node && typeof node.lot === 'string' && /^LOT-/.test(node.lot)) {
      node.lot = pool[counter % pool.length]; counter++;
    }
    // 工单号同样统一：否则生产页说 WO-2026-1026-001，仓储页却显示 WO-2026-1015-018，
    // 现场一句"这是同一张单吗"就问穿了。
    if ('wo' in node && typeof node.wo === 'string' && /^WO-/.test(node.wo)) {
      node.wo = wos[counter % wos.length]; counter++;
    }
    for (var k in node) {
      if (k === 'batch' || k === 'lot') continue;
      var v = node[k];
      if (v && typeof v === 'object') counter = unifyBatches(v, depth + 1, counter);
    }
    return counter;
  }

  /** 用虚拟时钟重建某时刻的原始快照（确定性；不改动页面逻辑） */
  function rebuildAt(MS, tMin) {
    var saved = MS._rng;
    MS._rng = makeRng((Clock.seed ^ Math.floor(tMin)) >>> 0);
    var d = new Date(2026, 9, 26, Math.floor(tMin / 60) % 24, Math.floor(tMin % 60), 0);
    var snap;
    try {
      snap = MS._build ? MS._build(d) : (MS.snapshot ? MS.snapshot() : null);
    } finally {
      MS._rng = saved;
    }
    return snap;
  }

  /** 按页面类型接管 */
  function installMockSource(kind) {
    PAGE_KIND = kind;
    // 零视觉影响的接管标记：便于自测确认内核是否真的接管了本页
    // （dump-dom 里能看到 data-sim="warehouse"，肉眼与打印均无影响）
    try { document.documentElement.setAttribute('data-sim', kind); } catch (e) { }

    if (kind === 'production') {
      // A 类：整体换成内核快照
      MockSource.tick = function () { return snapProduction(Clock.now()); };
      MockSource.snapshot = function () { return snapProduction(Clock.now()); };
      return true;
    }

    // B 类：接管随机源 + 序列 + 批号
    var origTick = MockSource.tick;

    MockSource.tick = function () {
      // 每次 tick 前重置随机序列：让结果只依赖 (seed, 虚拟分钟)，不依赖调用次数
      MockSource._rng = makeRng((Clock.seed ^ Math.floor(Clock.now())) >>> 0);
      var snap = origTick.call(MockSource);
      unifyBatches(snap, 0, 0);
      return snap;
    };

    // 曲线 X 轴改用虚拟时钟，并逐点确定性重建（否则曲线与实时值脱节）
    if (typeof MockSource.series === 'function') {
      var ORIG_SERIES = MockSource.series;
      MockSource.series = function (rangeId, key) {
        var cfg = { '1h': { p: 12, step: 5 }, '6h': { p: 12, step: 30 }, '24h': { p: 24, step: 60 }, '7d': { p: 7, step: 1440 } }[rangeId]
                  || { p: 24, step: 60 };
        var now = Clock.now();
        // 演示只关心本班：把采样窗口压缩到 [开班, 当前]，
        // 否则 24h 范围会取到开班前的时刻 —— X 轴出现 0:00、曲线前段全是 0，看着像"没数据"。
        var span = Math.max(1, now - SHIFT_START_MIN);
        var step = Math.max(1, Math.min(cfg.step, span / Math.max(1, cfg.p - 1)));
        var out = [];
        for (var i = cfg.p - 1; i >= 0; i--) {
          var tMin = now - i * step;
          if (tMin < SHIFT_START_MIN) tMin = SHIFT_START_MIN;
          var snap = rebuildAt(MockSource, tMin);
          out.push({ t: tMin, v: snap && snap[key] != null ? snap[key] : 0, label: Clock.hhmm(tMin) });
        }
        return out;
      };
    }

    // 页面上少数"静态业务数据"也要对齐台账（否则生产页说 WO-2026-1026-001，
    // 仓储页的齐套看板却写着 WO-2026-1015-018，现场一句"这是同一张单吗"就问穿）。
    // 只能改文本、不能改结构 —— 齐套状态与备注保留（那是演示剧情）。
    try {
      if (typeof KITS !== 'undefined' && Array.isArray(KITS) && KITS.length) {
        var wosAll = ensureLedger().wos;
        for (var i = 0; i < KITS.length; i++) {
          var w = wosAll[i % wosAll.length];
          KITS[i].id = w.wo;
          KITS[i].prod = w.model;
          KITS[i].qty = w.qty;
        }
      }
    } catch (e) { }

    // 首帧对齐：B 类页面在构造期已经 tick 过一次（那时还没被接管），
    // 那一帧的数据不来自虚拟时钟。这里立刻用接管后的数据源再推一帧，避免大屏闪跳。
    try {
      if (typeof Store !== 'undefined' && Store && typeof Store.dispatch === 'function') {
        Store.dispatch({ type: 'TICK', snap: MockSource.tick() });
      }
    } catch (e) { /* Store 未就绪则交给页面自己的定时器 */ }

    return true;
  }

  /** 用本内核接管页面里的 MockSource（视图不动）。
   *  typeof MockSource 在 const 的 TDZ 内会抛错 —— 用 try 包住，页面未定义时静默跳过。 */
  function install() {
    try {
      if (typeof MockSource === 'undefined' || !MockSource) return false;
      // 依据页面自身特征判定接管方式
      var kind = 'production';
      if (typeof MockSource._build === 'function') {
        kind = (typeof MockSource.series === 'function') ? 'warehouse' : 'quality';
      } else if (typeof MockSource.snapshot === 'function' && typeof MockSource.tick !== 'function') {
        kind = 'device';
      } else if (typeof MockSource.snapshot === 'function' && !MockSource._build) {
        kind = 'device';
      }
      // 直接声明式覆盖（避免误判）：页面可通过 window.__SIM_PAGE 指定
      if (typeof global.__SIM_PAGE === 'string') kind = global.__SIM_PAGE;
      return installMockSource(kind);
    } catch (e) {
      return false;
    }
  }

  /* --------------------------------------------------------- URL & 消息 */
  // 纯浏览器 file:// 场景下，父页无法读 iframe 的 DOM（同源策略），
  // 但 postMessage 是允许的 → 用消息同步虚拟时钟，用 URL 参数做首帧对齐。

  function readUrl() {
    try {
      var q = new URLSearchParams(location.search);
      if (q.has('seed')) Clock.seed = parseInt(q.get('seed'), 10) || Clock.seed;
      if (q.has('t')) Clock.set(parseFloat(q.get('t')), parseFloat(q.get('speed') || Clock.speed));
      if (q.has('speed')) Clock.speed = parseFloat(q.get('speed')) || Clock.speed;
      if (q.get('paused') === '1') Clock.pause();
    } catch (e) { /* 老浏览器或 file:// 限制，忽略 */ }
  }

  function listenClock() {
    global.addEventListener('message', function (ev) {
      var d = ev.data;
      if (!d || d.type !== 'sim-clock') return;
      if (typeof d.seed === 'number' && d.seed !== Clock.seed) {
        Clock.seed = d.seed;
        Ledger = buildLedger(Clock.seed);
      }
      if (typeof d.t === 'number') Clock.set(d.t, d.speed || Clock.speed);
      if (d.paused === true) Clock.pause();
      else if (d.paused === false) Clock.resume();
    });
  }

  /* ------------------------------------------------------------------ 导出 */

  global.SimCore = {
    Clock: Clock,
    LINES: LINES,
    SHIFT_START_MIN: SHIFT_START_MIN,
    SHIFT_LEN_MIN: SHIFT_LEN_MIN,
    snapProduction: snapProduction,
    snapWarehouse: snapWarehouse,
    summary: summary,
    events: events,
    install: install,
    pageKind: function () { return PAGE_KIND; },
    ledger: function () { return ensureLedger(); },
    /** 自测用：直接按 (seed, t) 取快照，不依赖真实时钟 */
    snapshotAt: function (seed, t) {
      var keepSeed = Clock.seed;
      Clock.seed = seed;
      Ledger = buildLedger(seed);
      var s = snapProduction(t);
      Clock.seed = keepSeed;
      Ledger = null;
      return s;
    }
  };

  readUrl();
  listenClock();
  install();

  // 页面若在 sim-core 之后才定义 MockSource，等 DOM 就绪再补一次
  global.addEventListener('DOMContentLoaded', function () { install(); });

})(typeof window !== 'undefined' ? window : this);
