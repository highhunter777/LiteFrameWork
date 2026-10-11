using System;
using System.Threading;

namespace RoomServer.Application
{
    public sealed class ServerLoop
    {
        /// <summary>逻辑帧率默认值（与 <c>SimConfig.TickRate</c> 同源约定；ServerLoop 不引 LiteSim，故本地复述）。</summary>
        public const int SimTickHz = 60;

        /// <summary>单轮最多补帧数（防"落后 → 补跑 → 更落后"的雪崩；超出即丢弃时间债）。</summary>
        public const int MaxCatchUp = 5;

        /// <summary>目标帧率（构造参数；生产恒 60，参数化只为调度器通用性验收：30/60/120Hz 均无漂移）。</summary>
        public int TickRate { get; }

        private readonly IMonotonicClock _clock;
        private readonly IDelayStrategy _delay;
        private readonly Action _pump;
        private readonly Action<Exception> _onPumpError;
        private readonly Thread _thread;
        private volatile bool _running;

        /// <summary>帧体异常上报节流步长（首败必报，其后每 N 次报一次——坏配置这类"每帧必抛"不得刷屏）。</summary>
        private const long PumpErrorReportEvery = 100;

        /// <summary>节拍统计（由循环线程写、外部读）。P4 计数面收口：状态私有、只读属性 + 写入方法——
        /// 外部（Ops 行/启动日志/用例）一律读，不碰字段；<c>PumpFailure</c> 返回累计次数供节流上报用。</summary>
        public sealed class LoopStats
        {
            private long _ticks;
            private long _droppedTimeMs;
            private long _catchUpAbandoned;
            private long _pumpFailures;
            private string _lastPumpError;

            /// <summary>实际跑过的权威帧数（= 帧体调用次数，含补帧与**抛错的帧**）。</summary>
            public long Ticks => _ticks;

            /// <summary>累计丢弃的时间债（毫秒；>0 = 服务器跟不上，Ops 观测项）。</summary>
            public long DroppedTimeMs => _droppedTimeMs;

            /// <summary>因超过 <see cref="ServerLoop.MaxCatchUp"/> 而放弃追帧的次数。</summary>
            public long CatchUpAbandoned => _catchUpAbandoned;

            /// <summary>帧体抛异常被吸收的次数（P0-2：>0 = 权威循环已吞掉异常，必须查 <see cref="LastPumpError"/>）。</summary>
            public long PumpFailures => _pumpFailures;

            /// <summary>最近一次被吸收的帧体异常描述（<c>类型: 消息</c>；null = 从未失败）。</summary>
            public string LastPumpError => _lastPumpError;

            /// <summary>记一帧（含被吸收异常的帧——记账口径与 <see cref="Ticks"/> 同源）。</summary>
            public void NoteTick() => _ticks++;

            /// <summary>记一次"放弃追帧 + 丢债"（时间债与次数是同一事件，必须一起记）。</summary>
            public void NoteCatchUpAbandoned(long droppedTimeMs)
            {
                _droppedTimeMs += droppedTimeMs;
                _catchUpAbandoned++;
            }

            /// <summary>记一次被吸收的帧体异常（返回累计次数，供上报节流判断）。</summary>
            public long NotePumpFailure(string description)
            {
                _lastPumpError = description;
                return ++_pumpFailures;
            }
        }

        private readonly LoopStats _stats = new LoopStats();

        /// <summary>只读快照（外部轮询用；取的是当前值，不保证与 Ticks 同一瞬间一致）。</summary>
        public LoopStats Stats => _stats;

        /// <summary>生产装配：真实时钟 + 真实等待 + 60Hz（便捷构造，host.Pump 为默认帧体）。</summary>
        /// <param name="onPumpError">帧体异常上报口（宿主注入日志；见 <see cref="PumpOnce"/>）。</param>
        public ServerLoop(ServerHost host, Action pumpOverride = null, Action<Exception> onPumpError = null)
            : this(StopwatchClock.Instance, ThreadSleepDelay.Instance,
                  (pumpOverride ?? (host ?? throw new ArgumentNullException(nameof(host))).Pump),
                  SimTickHz,
                  onPumpError)
        {
        }

        /// <summary>调度器核心装配（L1 用虚拟时钟/虚拟延迟驱动；生产勿接真实环境以外的时钟）。</summary>
        public ServerLoop(IMonotonicClock clock, IDelayStrategy delay, Action pump, int tickRate = SimTickHz,
            Action<Exception> onPumpError = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
            _pump = pump ?? throw new ArgumentNullException(nameof(pump));
            _onPumpError = onPumpError;
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "帧率必须为正");
            TickRate = tickRate;
            _thread = new Thread(LoopBody) { IsBackground = true, Name = "RoomServer.Loop" };
        }

        /// <summary>第 n 帧的目标时间戳（整数有理数：start + floor(n × freq / tickRate)——P0-1 的核心公式）。
        /// n 由 LoopBody 的溢出重置防御约束（n × freq 不会逼近 long 上限）。</summary>
        private long Anchor(long start, long n) => start + n * _clock.Frequency / TickRate;

        public void Start() { _running = true; _thread.Start(); }
        public void Stop() { _running = false; _thread.Join(2000); }

        /// <summary>跑满 <paramref name="durationMs"/> 毫秒（按时钟频率换算）后返回（验收脚本/用例用）。</summary>
        public void Run(long durationMs)
        {
            long start = _clock.Timestamp;
            long end = start + durationMs * _clock.Frequency / 1000L;
            long tick = 0;
            while (true)
            {
                long next = Anchor(start, tick);
                if (next >= end) break;
                _delay.DelayUntil(_clock, next);
                PumpOnce();                  // 帧体异常一律吸收（Run 与 LoopBody 同一纪律）
                tick++;

                StepCatchUp(ref start, ref tick, running: () => true);
            }
        }

        /// <summary>常驻循环：与 <see cref="Run(long)"/> 同一套锚定/追赶/丢债规则 + 帧体异常吸收
        /// （<see cref="PumpOnce"/>——线程不得因帧体异常死亡）。</summary>
        private void LoopBody()
        {
            long start = _clock.Timestamp;
            long tick = 0;
            while (_running)
            {
                _delay.DelayUntil(_clock, Anchor(start, tick));
                PumpOnce();
                tick++;

                StepCatchUp(ref start, ref tick, running: () => _running);
                if (tick > long.MaxValue / 2 / _clock.Frequency)   // n×freq 逼近溢出前重置时间轴（数百年量级，纯防御）
                {
                    start = _clock.Timestamp;
                    tick = 0;
                }
            }
        }

        /// <summary>
        /// 跑一帧权威帧体：**异常一律吸收**（P0-2），绝不让一根异常杀死循环线程——
        /// 本线程是 <c>IsBackground</c>，线程死亡 = 进程变僵尸：既不推进对局、也不退出、继续占资源，
        /// 且没有任何日志说明发生了什么。对照 <c>RoomWorkerPool</c> 的 worker 异常隔离——同系统同一纪律。
        ///
        /// 记账口径：<see cref="LoopStats.Ticks"/> 数**帧体调用次数**（含抛错的帧），与
        /// <see cref="StepCatchUp"/> 的追帧计数同源；失败另计 <see cref="LoopStats.PumpFailures"/>。
        /// 上报节流：首败必报，其后每 <see cref="PumpErrorReportEvery"/> 次报一次（"每帧必抛"的坏配置
        /// 不得刷屏，但也不能静默——Opus/Ops 行读 <see cref="LoopStats.PumpFailures"/> 即知）。
        /// </summary>
        private void PumpOnce()
        {
            try
            {
                _pump();
            }
            catch (Exception ex)
            {
                long failures = _stats.NotePumpFailure(ex.GetType().Name + ": " + ex.Message);
                if (failures == 1 || failures % PumpErrorReportEvery == 0) _onPumpError?.Invoke(ex);
            }
            _stats.NoteTick();
        }

        /// <summary>
        /// 有界追赶 + 丢债（Run 与 LoopBody 共用同一份规则——两处手抄必漂移）：
        /// 落后则连续补跑（不跳帧），上限 <see cref="MaxCatchUp"/>；超限即把锚点前移到当前时刻
        /// （丢掉的时间债记账进 <see cref="LoopStats.DroppedTimeMs"/>）。
        /// </summary>
        private void StepCatchUp(ref long start, ref long tick, Func<bool> running)
        {
            int catchUp = 0;
            while (running() && _clock.Timestamp > Anchor(start, tick) && catchUp < MaxCatchUp)
            {
                PumpOnce();
                tick++;
                catchUp++;
            }
            if (catchUp >= MaxCatchUp)
            {
                long debt = _clock.Timestamp - Anchor(start, tick);
                if (debt > 0)
                {
                    _stats.NoteCatchUpAbandoned(debt * 1000L / _clock.Frequency);
                    start += debt;                        // 锚点前移：把丢掉的债从时间轴上去掉（下一锚 = 现在）
                }
            }
        }
    }
}
