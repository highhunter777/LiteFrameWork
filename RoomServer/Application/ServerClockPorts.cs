using System;
using System.Diagnostics;
using System.Threading;

namespace RoomServer.Application
{
    /// <summary>
    /// 单调时钟端口（《商业级通用服务端框架总设计》§5 P0-1：调度器不直接触系统时钟——
    /// L1 用虚拟时钟驱动，无真实 Sleep；生产装配 <see cref="StopwatchClock"/>）。
    /// 语义与 <see cref="System.Diagnostics.Stopwatch"/> 对齐：Timestamp 单调递增，Frequency 为每秒刻度数。
    /// </summary>
    public interface IMonotonicClock
    {
        long Timestamp { get; }
        long Frequency { get; }
    }

    /// <summary>
    /// 等待策略端口：把调度"等到 targetTimestamp"的表达与真实等待手段解耦。
    /// 生产实现 <see cref="ThreadSleepDelay"/>（粗睡 + 自旋收尾）；虚拟实现直接把时钟推进到目标（零真实等待）。
    /// </summary>
    public interface IDelayStrategy
    {
        void DelayUntil(IMonotonicClock clock, long targetTimestamp);
    }

    /// <summary>生产时钟：QPC 高精度（Windows ~10MHz / Linux ~1GHz），不受系统时间调整影响。</summary>
    public sealed class StopwatchClock : IMonotonicClock
    {
        public static readonly StopwatchClock Instance = new StopwatchClock();

        public long Timestamp => Stopwatch.GetTimestamp();
        public long Frequency => Stopwatch.Frequency;
    }

    /// <summary>生产等待：粗睡到 1ms 内再自旋收尾（Windows 定时器粒度 ~15ms，纯睡必超调）。</summary>
    public sealed class ThreadSleepDelay : IDelayStrategy
    {
        public static readonly ThreadSleepDelay Instance = new ThreadSleepDelay();

        public void DelayUntil(IMonotonicClock clock, long targetTimestamp)
        {
            long remain = targetTimestamp - clock.Timestamp;
            if (remain <= 0) return;

            long remainMs = remain * 1000L / clock.Frequency;
            if (remainMs > 1) Thread.Sleep((int)(remainMs - 1));
            while (clock.Timestamp < targetTimestamp) Thread.SpinWait(64);
        }
    }
}
