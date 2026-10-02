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
        private readonly Thread _thread;
        private volatile bool _running;

        /// <summary>节拍统计（常驻线程写、外部读——用 <see cref="LoopStats"/> 聚合，避免逐字段竞态读）。</summary>
        public sealed class LoopStats
        {
            /// <summary>实际跑过的权威帧数（= Pump 次数，含补帧）。</summary>
            public long Ticks;

            /// <summary>累计丢弃的时间债（毫秒；>0 = 服务器跟不上，Ops 观测项）。</summary>
            public long DroppedTimeMs;

            /// <summary>因超过 <see cref="MaxCatchUp"/> 而放弃追帧的次数。</summary>
            public long CatchUpAbandoned;
        }

        private readonly LoopStats _stats = new LoopStats();

        /// <summary>只读快照（外部轮询用；取的是当前值，不保证与 Ticks 同一瞬间一致）。</summary>
        public LoopStats Stats => _stats;

        /// <summary>生产装配：真实时钟 + 真实等待 + 60Hz（便捷构造，host.Pump 为默认帧体）。</summary>
        public ServerLoop(ServerHost host, Action pumpOverride = null)
            : this(StopwatchClock.Instance, ThreadSleepDelay.Instance,
                  (pumpOverride ?? (host ?? throw new ArgumentNullException(nameof(host))).Pump),
                  SimTickHz)
        {
        }

        /// <summary>调度器核心装配（L1 用虚拟时钟/虚拟延迟驱动；生产勿接真实环境以外的时钟）。</summary>
        public ServerLoop(IMonotonicClock clock, IDelayStrategy delay, Action pump, int tickRate = SimTickHz)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
            _pump = pump ?? throw new ArgumentNullException(nameof(pump));
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
                _pump();
                _stats.Ticks++;              // 累计（跨多次 Run 调用/常驻全程）
                tick++;

                StepCatchUp(ref start, ref tick, running: () => true);
            }
        }

        /// <summary>常驻循环：与 <see cref="Run(long)"/> 同一套锚定/追赶/丢债规则。</summary>
        private void LoopBody()
        {
            long start = _clock.Timestamp;
            long tick = 0;
            while (_running)
            {
                _delay.DelayUntil(_clock, Anchor(start, tick));
                _pump();
                _stats.Ticks++;
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
        /// 有界追赶 + 丢债（Run 与 LoopBody 共用同一份规则——两处手抄必漂移）：
        /// 落后则连续补跑（不跳帧），上限 <see cref="MaxCatchUp"/>；超限即把锚点前移到当前时刻
        /// （丢掉的时间债记账进 <see cref="LoopStats.DroppedTimeMs"/>）。
        /// </summary>
        private void StepCatchUp(ref long start, ref long tick, Func<bool> running)
        {
            int catchUp = 0;
            while (running() && _clock.Timestamp > Anchor(start, tick) && catchUp < MaxCatchUp)
            {
                _pump();
                _stats.Ticks++;
                tick++;
                catchUp++;
            }
            if (catchUp >= MaxCatchUp)
            {
                long debt = _clock.Timestamp - Anchor(start, tick);
                if (debt > 0)
                {
                    _stats.DroppedTimeMs += debt * 1000L / _clock.Frequency;
                    _stats.CatchUpAbandoned++;
                    start += debt;                        // 锚点前移：把丢掉的债从时间轴上去掉（下一锚 = 现在）
                }
            }
        }
    }
}
