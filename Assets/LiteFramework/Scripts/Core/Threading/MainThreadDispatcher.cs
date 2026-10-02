using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LiteFramework
{
    public sealed class MainThreadDispatcher : IMainThreadDispatcher, IModuleStats
    {
        private readonly ConcurrentQueue<Action> _queue = new();

        public void Post(Action action)
            => _queue.Enqueue(action ?? throw new ArgumentNullException(nameof(action)));

        // 由 GameEntry 驱动 → Tick 必然主线程,消费与 Log 皆线程安全。
        // 执行中再 Post(嵌套)→ 同帧继续消费;无限嵌套 = 业务 bug,框架不防(与事件环同款)。
        public void Tick(float realDelta)
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Log.Error(ex, "MainThreadDispatcher"); }  // 单回调失败不炸队列
            }
        }

        public string StatsName => "MainThreadDispatcher";

        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["队列长度"] = _queue.Count.ToString();
        }
    }
}
