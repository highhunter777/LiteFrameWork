using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public sealed class SystemWallClock : IWallClock
    {   // Core 默认实现，DateTime.UtcNow 直通
        public DateTime UtcNow => DateTime.UtcNow;
        public long NowMs => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
    }
}
