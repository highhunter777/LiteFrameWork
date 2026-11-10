using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>平台事件订阅/派发：订阅者异常隔离并单独聚合，ClientHost 保留公开订阅门面。</summary>
    internal sealed class PlatformEventHub
    {
        private readonly object _gate = new object();
        private readonly List<Action<bool>> _pauseHandlers = new List<Action<bool>>();
        private readonly List<Action<bool>> _focusHandlers = new List<Action<bool>>();
        private readonly List<Action> _lowMemoryHandlers = new List<Action>();
        private readonly List<Exception> _failures = new List<Exception>();

        internal IReadOnlyList<Exception> Failures { get { lock (_gate) return _failures; } }

        internal void SubscribePause(Action<bool> handler) { lock (_gate) _pauseHandlers.Add(handler); }

        internal void SubscribeFocus(Action<bool> handler) { lock (_gate) _focusHandlers.Add(handler); }

        internal void SubscribeLowMemory(Action handler) { lock (_gate) _lowMemoryHandlers.Add(handler); }

        internal void RaisePause(bool paused) => Raise(_pauseHandlers, handler => handler(paused));

        internal void RaiseFocus(bool focused) => Raise(_focusHandlers, handler => handler(focused));

        internal void RaiseLowMemory() => Raise(_lowMemoryHandlers, handler => handler());

        private void Raise<T>(List<T> handlers, Action<T> invoke)
        {
            List<T> snapshot;
            lock (_gate) snapshot = new List<T>(handlers);
            for (int i = 0; i < snapshot.Count; i++)
            {
                try { invoke(snapshot[i]); }
                catch (Exception ex)
                {
                    lock (_gate) _failures.Add(ex);
                    Log.Error(ex, "ClientHost.PlatformEvent", LogTag.Bootstrap);
                }
            }
        }
    }
}
