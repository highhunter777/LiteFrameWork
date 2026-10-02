using System;
using System.Collections;
using System.Collections.Generic;

namespace LiteFramework
{

    /// <summary>
    /// 强类型事件通道。派发语义：
    /// ① 本轮收到 = 派发开始时刻的订阅集;订阅/注销立即生效于下一次派发;
    /// ② 嵌套派发各层独立快照——内层派发不影响外层正在遍历的缓冲;
    /// ③ 快照缓冲复用增长式分配:稳态零 GC,分配只在订阅数超过历史峰值时发生一次。
    /// </summary>
    internal sealed class EventChannel<T> : IEventChannel where T : class
    {
        private static readonly Action<T>[] Empty = new Action<T>[0];
        private readonly List<Action<T>> _subs = new List<Action<T>>(4);
        private Action<T>[] _snapshot = Empty;
        private int _depth;                                  // 嵌套派发深度

        public int SubscriberCount => _subs.Count;

        public void Add(Action<T> handler) => _subs.Add(handler);

        /// <summary>不存在的 handler = no-op。与 SubscriptionBag 的"违例必炸"相反:事件注销时机与帧时序交织,迟到无害。</summary>
        public void Remove(Action<T> handler) => _subs.Remove(handler);

        /// <summary>返回是否派发给了订阅者(StrictMode 判定用)。</summary>
        public bool Publish(T e)
        {
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
                buf = new Action<T>[n];   // 嵌套不复用外层缓冲,否则内外互踩;嵌套罕见,临时分配可接受
            }
            for (int i = 0; i < n; i++) buf[i] = _subs[i];                // 快照

            _depth++;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    try { buf[i](e); }
                    catch (Exception ex) { Log.Error(ex, $"Event.{typeof(T).Name}"); }  // 单订阅者不炸派发链
                }
            }
            finally { _depth--; }
            return true;
        }
    }

}
