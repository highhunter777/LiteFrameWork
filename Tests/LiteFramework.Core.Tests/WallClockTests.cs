using System;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>墙钟契约：NowMs 为 Unix 毫秒（1970 起），与 UtcNow 同源。</summary>
    public sealed class WallClockTests
    {
        [Fact]
        public void SystemWallClock_NowMs是Unix毫秒()
        {
            var clock = new SystemWallClock();
            long expected = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.InRange(clock.NowMs, expected - 5000, expected + 5000);
        }

        [Fact]
        public void SystemWallClock_NowMs与UtcNow同源()
        {
            var clock = new SystemWallClock();
            long fromUtc = (clock.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
            Assert.InRange(clock.NowMs, fromUtc - 5000, fromUtc + 5000);
        }
    }
}
