using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LiteFramework
{
    public sealed class MainThreadDispatcher : IMainThreadDispatcher, IModuleStats
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly int _maxActionsPerTick;   // 0 = 不限；默认上限防止后台洪泛拖住一帧

        public MainThreadDispatcher(int maxActionsPerTick = 1024)
        {
            if (maxActionsPerTick < 0) throw new ArgumentOutOfRangeException(nameof(maxActionsPerTick));
            _maxActionsPerTick = maxActionsPerTick;
        }

        public int MaxActionsPerTick => _maxActionsPerTick;

        public void Post(Action action)
            => _queue.Enqueue(action ?? throw new ArgumentNullException(nameof(action)));

        // 由 GameEntry 驱动 → Tick 必然主线程,消费与 Log 皆线程安全。
        public void Tick(float realDelta)
        {
            int processed = 0;
            while ((_maxActionsPerTick == 0 || processed < _maxActionsPerTick)
                   && _queue.TryDequeue(out var action))
            {
                processed++;
                try { action(); }
                catch (Exception ex) { Log.Error(ex, null, LogTag.MainThreadDispatcher); }  // 单回调失败不炸队列
            }
        }

        public string StatsName => "MainThreadDispatcher";

        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["队列长度"] = _queue.Count.ToString();
            into["本帧上限"] = _maxActionsPerTick == 0 ? "不限" : _maxActionsPerTick.ToString();
        }
    }
}
