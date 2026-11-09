using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>值型信号通道：同步派发、异常上抛中断、同优先级注册序。</summary>
    internal sealed class SignalChannel<T> : IEventChannel where T : struct
    {
        private readonly struct Sub
        {
            public readonly SignalHandler<T> Handler;
            public readonly int Priority;
            public Sub(SignalHandler<T> handler, int priority) { Handler = handler; Priority = priority; }
        }

        private readonly List<Sub> _subs = new List<Sub>(4);
        private SignalHandler<T>[] _snapshot = Array.Empty<SignalHandler<T>>();
        private long _published;
        private int _depth;

        public int SubscriberCount => _subs.Count;
        public Type EventType => typeof(T);
        public long PublishedCount => _published;
        public long DispatchErrors { get; private set; }

        /// <summary>有序插入：插到首个“优先级严格更大”的条目之前（同优先级保持注册序）。</summary>
        public void Add(SignalHandler<T> handler, int priority)
        {
            int insertAt = _subs.Count;
            for (int i = 0; i < _subs.Count; i++)
                if (_subs[i].Priority > priority) { insertAt = i; break; }
            _subs.Insert(insertAt, new Sub(handler, priority));
        }

        /// <summary>不存在的 handler = no-op（注销时机与帧时序交叠，迟到无害）。</summary>
        public void Remove(SignalHandler<T> handler)
        {
            for (int i = 0; i < _subs.Count; i++)
                if (_subs[i].Handler == handler) { _subs.RemoveAt(i); return; }
        }

        /// <summary>派发：外层复用快照缓冲，嵌套派发临时分配（避免内外互相干扰）。</summary>
        public void Publish(in T signal)
        {
            _published++;
            int n = _subs.Count;
            if (n == 0) return;

            SignalHandler<T>[] buf = _depth == 0 ? Buffer(n) : new SignalHandler<T>[n];
            for (int i = 0; i < n; i++) buf[i] = _subs[i].Handler;

            _depth++;
            try
            {
                for (int i = 0; i < n; i++) buf[i](in signal);   // 上抛：首个异常即中断派发链
            }
            catch { DispatchErrors++; throw; }
            finally { _depth--; }
        }

        public void Clear()
        {
            _subs.Clear();
            _snapshot = Array.Empty<SignalHandler<T>>();
            _depth = 0;
        }

        private SignalHandler<T>[] Buffer(int n)
        {
            if (_snapshot.Length < n) _snapshot = new SignalHandler<T>[n];
            return _snapshot;
        }
    }
}
