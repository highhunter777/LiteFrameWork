using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// M1-3 守卫：日志环形缓冲的**并发写 + 并发枚举**不得撕裂。
    /// 读侧来自 Unity 的 <c>logMessageReceivedThreaded</c> 回调链（任意线程），写侧在主线程；
    /// 修复前 `_ring/_start/_count` 无锁，枚举期间滚出条目会读到错位/越界。
    /// </summary>
    public sealed class LogRingConcurrencyTests
    {
        [Fact]
        public void 环形缓冲_并发写与枚举_不抛且计数有界()
        {
            const int writes = 20000;
            const int reads = 2000;
            int torn = 0;

            Task writer = Task.Run(() =>
            {
                for (int i = 0; i < writes; i++) Log.Info("ring-" + i);
            });

            Task reader = Task.Run(() =>
            {
                for (int r = 0; r < reads; r++)
                {
                    int count = Log.Recent.Count;
                    if (count < 0) Interlocked.Increment(ref torn);
                    int seen = 0;
                    foreach (LogEntry e in Log.Recent)
                    {
                        if (e.Message == null) Interlocked.Increment(ref torn);   // 滚出条目被置 default 时读到 null = 撕裂
                        seen++;
                    }
                    if (seen != count) Interlocked.Increment(ref torn);            // 枚举期间计数漂移未走快照 = 撕裂
                }
            });

            Task.WaitAll(writer, reader);

            Assert.Equal(0, Volatile.Read(ref torn));
            Assert.InRange(Log.Recent.Count, 0, 4096);
        }
    }
}
