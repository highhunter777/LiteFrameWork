using RoomServer.Application;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 分层限流用例（《商业级通用服务端框架总设计》§595"限流可按 IP/账号/Session 生效"、
    /// §343"限流桶必须有容量上限和周期清理"）。纯 L1：全部显式传虚拟毫秒，不碰系统时钟
    /// （与 <see cref="ReconnectService"/> 用例同一纪律）。
    /// </summary>
    public sealed class RateLimiterTests
    {
        private static RateLimitSettings Lanes(RateLimitSettings.Lane lane,
            int buckets = 8192, long idleTtlMs = 120_000)
        {
            return new RateLimitSettings(lane, lane, lane, lane, buckets, idleTtlMs);
        }

        [Fact]
        public void 突发耗尽后拒绝_按速率随时间恢复()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(2, 1)));   // 2 突发 + 1/s

            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 0));
            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 0));
            Assert.False(limiter.TryAcquireIpConnect("1.1.1.1", 0));      // 突发耗尽 → 拒绝
            Assert.Equal(1, limiter.RejectedIpConnect);

            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 1000));    // +1s → +1 枚
            Assert.False(limiter.TryAcquireIpConnect("1.1.1.1", 1000));   // 只补了这一枚
            Assert.Equal(2, limiter.RejectedIpConnect);
        }

        [Fact]
        public void 空闲再久_令牌也封顶在突发容量()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(2, 1000)));

            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 0));
            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 0));

            // 空闲 60s 按速率应补 6 万枚——封顶 = 突发容量 2（防"憋久了爆发一次"绕过限速）
            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 60_000));
            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 60_000));
            Assert.False(limiter.TryAcquireIpConnect("1.1.1.1", 60_000));
        }

        [Fact]
        public void 时间回退不补令牌()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(1, 1)));

            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 10_000));
            Assert.False(limiter.TryAcquireIpConnect("1.1.1.1", 5_000));    // 回退：不补
            Assert.False(limiter.TryAcquireIpConnect("1.1.1.1", 10_000));   // 回到锚点：同样不补
            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 11_000));    // 单调前推 +1s → +1 枚
        }

        [Fact]
        public void 键表满_拒绝新键并计数_旧键不受影响()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(4, 0), buckets: 2));

            Assert.True(limiter.TryAcquireIpConnect("a", 0));
            Assert.True(limiter.TryAcquireIpConnect("b", 0));
            Assert.False(limiter.TryAcquireIpConnect("c", 0));       // 表满：拒绝新键，不淘汰旧桶

            Assert.Equal(1, limiter.RejectedTableFull);
            Assert.Equal(1, limiter.RejectedIpConnect);              // 表满是"被拒"的子原因，两处都记
            Assert.Equal(2, limiter.BucketCount);                    // 拒绝不建桶

            Assert.True(limiter.TryAcquireIpConnect("a", 0));        // 旧键仍有余额，不受表满影响
        }

        [Fact]
        public void 各lane独立_同一键不共享桶()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(1, 0)));

            Assert.True(limiter.TryAcquireIpConnect("1.1.1.1", 0));
            Assert.False(limiter.TryAcquireIpConnect("1.1.1.1", 0));    // 连接 lane 耗尽
            Assert.True(limiter.TryAcquireIpEntry("1.1.1.1", 0));       // 入场 lane 独立
            Assert.True(limiter.TryAcquireAccountEntry("acct-1", 0));
            Assert.True(limiter.TryAcquireSessionPacket(1, 0));
            Assert.False(limiter.TryAcquireSessionPacket(1, 0));        // Session lane 独立（int 键）
            Assert.Equal(1, limiter.RejectedSessionPackets);
        }

        [Fact]
        public void 空键放行_看不见的地址限不了()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(1, 0)));

            Assert.True(limiter.TryAcquireIpConnect(null, 0));
            Assert.True(limiter.TryAcquireIpConnect("", 0));
            Assert.True(limiter.TryAcquireIpEntry(null, 0));
            Assert.True(limiter.TryAcquireAccountEntry(null, 0));
            Assert.Equal(0, limiter.BucketCount);                    // 空键不建桶
        }

        [Fact]
        public void 周期清理_按空闲时长回收桶并计数()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(2, 0), idleTtlMs: 1000));

            Assert.True(limiter.TryAcquireIpConnect("a", 10_000));
            Assert.True(limiter.TryAcquireSessionPacket(7, 10_000));
            Assert.Equal(2, limiter.BucketCount);

            Assert.Equal(0, limiter.PurgeIdle(10_500));              // 未到 TTL：不动
            Assert.Equal(2, limiter.BucketCount);

            Assert.Equal(2, limiter.PurgeIdle(11_001));              // 到达 TTL：全部回收
            Assert.Equal(0, limiter.BucketCount);
            Assert.Equal(2, limiter.BucketsPurged);
        }

        [Fact]
        public void 清理后重建桶_配额复位()
        {
            var limiter = new RateLimiter(Lanes(new RateLimitSettings.Lane(1, 0), idleTtlMs: 1000));

            Assert.True(limiter.TryAcquireIpConnect("a", 0));
            Assert.False(limiter.TryAcquireIpConnect("a", 0));

            limiter.PurgeIdle(2000);                                  // 空闲超 TTL → 回收旧桶

            Assert.True(limiter.TryAcquireIpConnect("a", 2000));      // 重建：满额
        }
    }
}
