using System;
using System.Collections;
using System.Collections.Generic;

namespace LiteFramework
{

    /// <summary>
    /// 强类型事件通道。派发语义：
    /// ① 本轮收到 = 派发开始时刻的订阅集快照；订阅/注销立即生效于下一次派发；
    /// ② 嵌套派发各层独立快照——嵌套不影响外层正在遍历的缓冲；
    /// ③ 快照缓冲复用增长式分配稳态零 GC，分配只在订阅数超过历史峰值时发生一次。
    /// ④ 优先级派发（《事件中心专项设计》§3.1）：小值先派发，同优先级保持注册序（稳定排序）——
    ///    订阅表按 (priority asc, 注册序 asc) 有序插入维护；订阅是生命周期边界低频操作，
    ///    O(n) 插入可接受，派发热路径（快照复制/遍历）形态不变。
    /// </summary>
    internal sealed class EventChannel<T> : IEventChannel where T : class
    {
        private static readonly Action<T>[] Empty = new Action<T>[0];

        private readonly struct Sub
        {
            public readonly Action<T> Handler;
            public readonly int Priority;
            public Sub(Action<T> handler, int priority) { Handler = handler; Priority = priority; }
        }

        private readonly List<Sub> _subs = new List<Sub>(4);
        private Action<T>[] _snapshot = Empty;
        private int _depth;                                  // 嵌套派发深度
        private long _published;                              // 累计派发（含无订阅者——"空转"诊断信号）
        private long _errors;                                 // 订阅者异常累计（健康度：抓订阅者里的坏代码）

        public int SubscriberCount => _subs.Count;
        public Type EventType => typeof(T);
        public long PublishedCount => _published;
        public long DispatchErrors => _errors;

        /// <summary>有序插入：插到首个"优先级严格更大"的条目之前——同优先级保持注册序（稳定排序）。
        /// Debug 三宏下同一委托重复订阅 → Log.Warning（不抛——多实例订阅者复用同一静态方法是合法形态，§3.4）。</summary>
        public void Add(Action<T> handler, int priority)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            for (int i = 0; i < _subs.Count; i++)
                if (_subs[i].Handler == handler)
                    Log.Warning($"重复订阅（多实例复用同一委托合法，但通常是遗忘注销的信号）: {typeof(T).Name}", "Event");
#endif
            int insertAt = _subs.Count;
            for (int i = 0; i < _subs.Count; i++)
            {
                if (_subs[i].Priority > priority) { insertAt = i; break; }
            }
            _subs.Insert(insertAt, new Sub(handler, priority));
        }

        /// <summary>不存在的 handler = no-op。与 SubscriptionBag 的"违例必炸"相反: 事件注销时机与帧时序交叠, 迟到无害。</summary>
        public void Remove(Action<T> handler)
        {
            for (int i = 0; i < _subs.Count; i++)
                if (_subs[i].Handler == handler) { _subs.RemoveAt(i); return; }
        }

        /// <summary>返回是否派发给了订阅者（StrictMode 判定用）。</summary>
        public bool Publish(T e)
        {
            _published++;
            int n = _subs.Count;
            if (n == 0) return false;

            Action<T>[] buf;
            if (_depth == 0)
            {
                if (_snapshot.Length < n) _snapshot = new Action<T>[n];   // 增长式复用
                buf = _snapshot;
            }
            else
            {
                buf = new Action<T>[n];   // 嵌套不借用外层缓冲（否则内外互相干扰;嵌套罕见,临时分配可接受）
            }
            for (int i = 0; i < n; i++) buf[i] = _subs[i].Handler;                // 快照

            _depth++;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    try { buf[i](e); }
                    catch (Exception ex) { _errors++; Log.Error(ex, $"Event.{typeof(T).Name}"); }  // 单订阅者不炸派发链
                }
            }
            finally { _depth--; }
            return true;
        }
    }

}
