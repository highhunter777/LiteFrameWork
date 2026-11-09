using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface IWallClock
    {
        DateTime UtcNow { get; }     // 真实时间（UTC）
        long NowMs { get; }          // Unix 毫秒（跨端比对 / 持久化用）
    }
}
