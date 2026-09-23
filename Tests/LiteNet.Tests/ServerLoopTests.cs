using System.Diagnostics;
using LiteNet.Transport;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 定拍循环用例（R0 重写：《商业级通用服务端框架总设计》§5 P0-1）。
    ///
    /// **旧用例按正确契约修**（待办总览 R0 行：不为保绿保留错误行为）：旧"空载跑一秒"接受 16ms 锚点
    /// 造成的 62~63 tick——那是 62.5Hz 的**错误时间轴**，追帧补不了时间轴本身错了 4% 的根因。
    /// 现在用虚拟时钟把节拍器的数学钉死（零真实 Sleep、零墙钟依赖），真实时钟只留一条容差冒烟。
    /// </summary>
    public sealed class ServerLoopTests
    {
        private const int Port = 28891;

        // ---- 虚拟时钟（P0-1 注入：调度器不触系统时钟——L1 无真实 Sleep）----

        /// <summary>虚拟单调时钟：DelayUntil 直接推进到目标 → 每帧"准点"，节拍数学零噪声。</summary>
        private sealed class VirtualClock : IMonotonicClock
        {
            public const long Freq = 10_000_000;   // 与 Windows QPC 同量级（100ns/刻）
            private long _ts;
            public long Timestamp => _ts;
            public long Frequency => Freq;
            public void AdvanceTo(long target) { if (target > _ts) _ts = target; }
            public void AdvanceBy(long delta) => _ts += delta;
        }

        private sealed class VirtualDelay : IDelayStrategy
        {
            private readonly VirtualClock _clock;
            public VirtualDelay(VirtualClock clock) => _clock = clock;
            public void DelayUntil(IMonotonicClock clock, long targetTimestamp) => _clock.AdvanceTo(targetTimestamp);
        }

        private static ServerLoop VirtualLoop(int tickRate, out VirtualClock clock)
        {
            clock = new VirtualClock();
            return new ServerLoop(clock, new VirtualDelay(clock), pump: () => { }, tickRate: tickRate);
        }

        // ---- P0-1 验收：虚拟 10 分钟精确 36,000 tick；30/60/120Hz 参数化无累计漂移 ----

        [Theory]
        [InlineData(30, 18_000)]
        [InlineData(60, 36_000)]
        [InlineData(120, 72_000)]
        public void 虚拟十分钟_帧数精确等于理论值无漂移(int tickRate, int expectedTicks)
        {
            var loop = VirtualLoop(tickRate, out _);
            loop.Run(10 * 60 * 1000);                       // 虚拟 10 分钟

            Assert.Equal(expectedTicks, loop.Stats.Ticks);   // **精确相等**——整数有理数锚点零漂移
            Assert.Equal(0L, loop.Stats.DroppedTimeMs);     // 虚拟准点运行不应丢债
            Assert.Equal(0L, loop.Stats.CatchUpAbandoned);
        }

        [Fact]
        public void 虚拟时钟_锚点公式逐帧推进_终点刻度精确()
        {
            // 60Hz 1 秒：60 tick 后，时钟停在最后一个锚点 anchor(59) = 59×freq/60（floor 语义，确定值）
            var loop = VirtualLoop(60, out VirtualClock clock);
            loop.Run(1000);

            Assert.Equal(60, loop.Stats.Ticks);
            Assert.Equal(59 * VirtualClock.Freq / 60, clock.Timestamp);   // 终刻 = 第 59 帧锚点（第 60 帧锚点已到 end → 循环终止）
        }

        // ---- 真实时钟：60Hz 容差冒烟（替换旧"接受 62~63 tick"的错误断言）----

        [Fact]
        public void 空载实钟一秒_帧率约60Hz且无掉债()
        {
            using var host = new ServerHost(new KcpTransportServer(), new RoomConfig { Port = Port });
            host.Ops.PrintEnabled = false;
            var loop = new ServerLoop(host);

            var watch = Stopwatch.StartNew();
            loop.Run(1000);
            watch.Stop();

            ServerLoop.LoopStats stats = loop.Stats;
            // 正确契约：60Hz（容差只吸收宿主调度抖动，不再接受 62.5Hz 时间轴）
            Assert.InRange(stats.Ticks, 60 - 3, 60 + 3);
            Assert.Equal(0L, stats.DroppedTimeMs);           // 空载不该丢债
            Assert.Equal(0L, stats.CatchUpAbandoned);
            Assert.InRange(watch.ElapsedMilliseconds, 950, 1300);   // 跑满时长即返回
        }

        [Fact]
        public void 跑满时长即停_不因追赶而超出()
        {
            using var host = new ServerHost(new KcpTransportServer(), new RoomConfig { Port = Port + 1 });
            host.Ops.PrintEnabled = false;
            var loop = new ServerLoop(host);

            loop.Run(300);
            long first = loop.Stats.Ticks;

            loop.Run(300);
            long second = loop.Stats.Ticks - first;

            Assert.InRange(second, (300 * 60 / 1000) - 3, (300 * 60 / 1000) + 3);   // ~18 tick（60Hz 契约）
        }

        [Fact]
        public void 过载时_追赶有上界并记账丢债_不会无界满核()
        {
            using var host = new ServerHost(new KcpTransportServer(), new RoomConfig { Port = Port + 2 });
            host.Ops.PrintEnabled = false;
            // 单帧体 20ms（模拟过载：> 16.67ms 锚点周期，必然持续落后）。
            // 注：跨平台 CI——注入强度须大于两平台定时器粒度之和（Linux 1ms 精度下 8ms 不构成过载，M10 批④教训）。
            var loop = new ServerLoop(host, () => System.Threading.Thread.Sleep(20));

            var watch = Stopwatch.StartNew();
            loop.Run(400);
            watch.Stop();

            ServerLoop.LoopStats stats = loop.Stats;
            int maxTicksIfUnbounded = (int)(watch.ElapsedMilliseconds / 8) + 5;   // 无界追帧会把时间全填满

            Assert.True(stats.Ticks < maxTicksIfUnbounded,
                $"追赶必须有上界：ticks={stats.Ticks} 逼近无界值 {maxTicksIfUnbounded}");
            Assert.True(stats.DroppedTimeMs > 0, "过载必须记时间债（否则就是无界追帧）");
            Assert.True(stats.CatchUpAbandoned > 0);
            Assert.InRange(watch.ElapsedMilliseconds, 380, 900);   // 跑满时长即返回（不被追赶拖长）
        }

        [Fact]
        public void 虚拟时钟_慢帧过载_丢债后继续推进不无界追赶()
        {
            // 虚拟时钟下的过载：帧体把时钟推进 5 个周期 → 每轮必落后 → 有界追赶 + 丢债（无需真实 Sleep）
            var clock = new VirtualClock();
            int pumped = 0;
            var loop = new ServerLoop(clock, new VirtualDelay(clock),
                pump: () => { pumped++; clock.AdvanceBy(VirtualClock.Freq * 5 / 60); },   // 每帧耗时 5/60s
                tickRate: 60);

            loop.Run(60 * 1000);                             // 虚拟 1 分钟

            // 过载形态：丢债发生（否则就是无界追帧 = MaxCatchUp 红线失守）
            Assert.True(loop.Stats.DroppedTimeMs > 0, "持续落后必须丢债（有界追赶红线）");
            Assert.True(loop.Stats.CatchUpAbandoned > 0);
            // 速率形态：帧体耗时 5 倍周期 → 每轮最多追 MaxCatchUp+1 帧。若丢债失效（无界追赶），
            // tick 会逼近"把虚拟时间全部填满"的 ~3000；正常有界形态在 ~700 一档（每轮 6 帧 × ~116 轮）
            Assert.InRange(loop.Stats.Ticks, 60, 1500);
            Assert.True(pumped == loop.Stats.Ticks, "pump 次数与 tick 记账一致");
        }
    }
}
