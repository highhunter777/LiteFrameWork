using System;

namespace LiteFramework
{
    public sealed class SystemWallClock : IWallClock
    {   // Core 默认实现，DateTime.UtcNow 直通
        public DateTime UtcNow => DateTime.UtcNow;

        /// <summary>Unix 毫秒（1970-01-01T00:00:00Z 起；与 <see cref="IWallClock"/> 契约一致）。</summary>
        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
