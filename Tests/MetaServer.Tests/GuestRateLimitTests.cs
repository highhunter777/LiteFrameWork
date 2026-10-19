using System;
using LiteTesting;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 固定窗口限流器（未鉴权写面的兜底：游客登录）。纯逻辑 + 虚拟时钟——窗口边界不用 sleep、
    /// 键表容量与回收行为可直接断言。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Unit)]
    public sealed class GuestRateLimitTests
    {
        private sealed class VirtualClock
        {
            public long Ms;

            public long Now()
            {
                return Ms;
            }
        }

        [Fact]
        public void 窗口内超限_拒绝并给出剩余等待()
        {
            var clock = new VirtualClock();
            var limiter = new FixedWindowRateLimiter(permitLimit: 2, windowMs: 60_000, capacity: 8, nowMs: clock.Now);

            Assert.True(limiter.TryAcquire("ip-1", out _));
            Assert.True(limiter.TryAcquire("ip-1", out _));

            clock.Ms = 10_000;
            Assert.False(limiter.TryAcquire("ip-1", out long retryAfterMs));
            Assert.Equal(50_000, retryAfterMs);            // 窗口剩余 = 60s - 已过 10s
            Assert.Equal(1, limiter.Rejected);

            // 其他键不受影响（每源独立窗口）
            Assert.True(limiter.TryAcquire("ip-2", out _));
        }

        [Fact]
        public void 窗口滑过后_许可恢复()
        {
            var clock = new VirtualClock();
            var limiter = new FixedWindowRateLimiter(2, 60_000, 8, clock.Now);

            Assert.True(limiter.TryAcquire("ip-1", out _));
            Assert.True(limiter.TryAcquire("ip-1", out _));
            Assert.False(limiter.TryAcquire("ip-1", out _));

            clock.Ms = 60_000;                             // 窗口整拍到期
            Assert.True(limiter.TryAcquire("ip-1", out _));
            Assert.True(limiter.TryAcquire("ip-1", out _));
            Assert.False(limiter.TryAcquire("ip-1", out _));
        }

        [Fact]
        public void 键表满且无可回收键_拒绝新键_过期后可回收()
        {
            var clock = new VirtualClock();
            var limiter = new FixedWindowRateLimiter(permitLimit: 5, windowMs: 10_000, capacity: 2, nowMs: clock.Now);

            Assert.True(limiter.TryAcquire("k1", out _));
            Assert.True(limiter.TryAcquire("k2", out _));
            Assert.False(limiter.TryAcquire("k3", out _), "键表满：宁可误拒也不无界增长（fail-closed）");
            Assert.Equal(2, limiter.KeyCount);

            clock.Ms = 10_000;                             // 全部过期 → 回收后新键可入
            Assert.True(limiter.TryAcquire("k3", out _));
            Assert.Equal(1, limiter.KeyCount);
        }

        [Fact]
        public void 空键归共享桶_仍受限()
        {
            var clock = new VirtualClock();
            var limiter = new FixedWindowRateLimiter(1, 60_000, 8, clock.Now);

            Assert.True(limiter.TryAcquire(null, out _));
            Assert.False(limiter.TryAcquire(string.Empty, out _), "探测不到源地址 ⇒ 归同一共享桶");
        }

        [Theory]
        [InlineData(0, 60_000, 8)]
        [InlineData(1, 0, 8)]
        [InlineData(1, 60_000, 0)]
        public void 构造参数越界_拒绝(int permitLimit, long windowMs, int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FixedWindowRateLimiter(permitLimit, windowMs, capacity, () => 0));
        }
    }
}
