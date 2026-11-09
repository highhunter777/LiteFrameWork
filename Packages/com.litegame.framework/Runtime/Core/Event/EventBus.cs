using System;
using System.Collections.Generic;
using System.Threading;

namespace LiteFramework
{
    public sealed class EventBus : IEventBus, IMatchEventBus, IModuleStats
    {
        private readonly Dictionary<Type, IEventChannel> _channels = new Dictionary<Type, IEventChannel>(32);
        private readonly Dictionary<Type, IEventChannel> _signals = new Dictionary<Type, IEventChannel>(8);
        private readonly Queue<PendingEvent> _queue = new Queue<PendingEvent>(16);
        private readonly int _maxQueued;
        private long _published;
        private long _strictWarnings;                      // StrictMode 告警累计（没人听的事件信号）
        private long _queuedDropped;
        private int _queuedPeak;
        private long _tickCount;
        private readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        private bool _disposed;

        /// <summary>严格模式（未订阅事件告警）：装配点按 <c>DevBuildFacts.IsDevelopment</c> 传初值；
        /// 运行期可改（既有测试面），但**通道创建时刻**的快照决定该通道是否告警重复订阅。</summary>
        public bool StrictMode { get; set; }

        public bool IsDisposed => _disposed;

        public int MaxQueued => _maxQueued;

        public EventBus(bool strictMode = false, int maxQueued = QueueLimits.DefaultCapacity)
        {
            if (maxQueued < 1) throw new ArgumentOutOfRangeException(nameof(maxQueued));
            StrictMode = strictMode;
            _maxQueued = maxQueued;
        }

        /// <summary>订阅（§3.1 优先级派发：小值先派发，同优先级保持注册序）。priority 默认 0 = 既有调用零改动。</summary>
        public IEventSubscription Subscribe<T>(Action<T> handler, int priority = 0) where T : class
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var channel = (EventChannel<T>)GetOrAddChannel<T>();
            channel.Add(handler, priority);
            return new EventSubscription(this, () => channel.Remove(handler));
        }

        public void Publish<T>(T e) where T : class
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (e == null) throw new ArgumentNullException(nameof(e));
            _published++;
            var channel = (EventChannel<T>)GetOrAddChannel<T>();
            bool dispatched = channel.Publish(e);
            if (!dispatched && StrictMode)
            {
                _strictWarnings++;
                Log.Warning($"no subscriber: {typeof(T).Name}", LogTag.Event);
            }
        }

        /// <summary>订阅值型信号（独立通道表与派发循环；返回句柄释放即注销）。</summary>
        public IEventSubscription SubscribeSignal<T>(SignalHandler<T> handler, int priority = 0) where T : struct
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var channel = (SignalChannel<T>)GetOrAddSignalChannel<T>();
            channel.Add(handler, priority);
            return new EventSubscription(this, () => channel.Remove(handler));
        }

        /// <summary>派发值型信号：不排队、不告警、订阅者异常上抛。</summary>
        public void PublishSignal<T>(in T signal) where T : struct
        {
            EnsureMainThread();
            ThrowIfDisposed();
            ((SignalChannel<T>)GetOrAddSignalChannel<T>()).Publish(in signal);
        }

        /// <summary>延迟派发：到期帧由 Tick 驱动；delayFrames 最小 1。入队即移交所有权，调用方不得再改。</summary>
        public void PublishQueued<T>(T e, int delayFrames = 1) where T : class
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (delayFrames < 1) delayFrames = 1;
            if (_queue.Count >= _maxQueued)
            {
                _queuedDropped++;
                Log.Error($"事件队列溢出丢弃（MaxQueued={_maxQueued}）: {typeof(T).Name}", LogTag.Event);
                return;
            }
            _queue.Enqueue(new PendingEvent
            {
                DueTick = _tickCount + delayFrames,
                Dispatch = () => Publish(e),
            });
            if (_queue.Count > _queuedPeak) _queuedPeak = _queue.Count;
        }

        /// <summary>帧驱动：到期事件按入队序派发；本轮新入队留到下帧。</summary>
        public void Tick(float realDelta)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            _tickCount++;
            int n = _queue.Count;
            for (int i = 0; i < n; i++)
            {
                PendingEvent pending = _queue.Dequeue();
                if (pending.DueTick > _tickCount)
                {
                    _queue.Enqueue(pending);
                    continue;
                }
                try { pending.Dispatch(); }
                catch (Exception ex) { Log.Error(ex, "EventBus.Tick", LogTag.Event); }
            }
        }

        public void Dispose()
        {
            EnsureMainThread();
            if (_disposed) return;
            _disposed = true;

            _queue.Clear();
            foreach (var channel in _channels.Values) channel.Clear();
            _channels.Clear();
            foreach (var channel in _signals.Values) channel.Clear();
            _signals.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventBus));
        }

        private void EnsureMainThread()
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            if (current != _mainThreadId)
                throw new InvalidOperationException($"EventBus 只能主线程访问（构造线程 {_mainThreadId}，当前 {current}）");
        }

        private IEventChannel GetOrAddChannel<T>() where T : class
        {
            if (_channels.TryGetValue(typeof(T), out var ch)) return ch;
            var created = new EventChannel<T>(StrictMode);   // 通道创建时刻快照决定重复订阅告警（见 StrictMode 注释）
            _channels[typeof(T)] = created;
            return created;
        }

        private IEventChannel GetOrAddSignalChannel<T>() where T : struct
        {
            if (_signals.TryGetValue(typeof(T), out var ch)) return ch;
            var created = new SignalChannel<T>();
            _signals[typeof(T)] = created;
            return created;
        }

        public string StatsName => "EventBus";

        /// <summary>契约：实现负责 into.Clear() 再填入；key 为常量串零分配，value 为插值串（HUD 低频轮询可接受）。
        /// 批2 追加键（《事件中心专项设计》§4）——既有键不动，HUD 消费方兼容。</summary>
        public void Snapshot(Dictionary<string, string> into)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            into.Clear();
            int subscribers = 0;
            long dispatchErrors = 0;
            foreach (var ch in _channels.Values)
            {
                subscribers += ch.SubscriberCount;
                dispatchErrors += ch.DispatchErrors;
            }

            into["事件类型数"] = _channels.Count.ToString();
            into["订阅者总数"] = subscribers.ToString();
            into["累计发布"] = _published.ToString();
            into["DispatchErrors"] = dispatchErrors.ToString();
            into["StrictWarnings"] = _strictWarnings.ToString();
            into["QueuedPeak"] = _queuedPeak.ToString();
            into["QueuedDropped"] = _queuedDropped.ToString();
            into["DelayedCount"] = _queue.Count.ToString();

            int signalSubscribers = 0;
            foreach (var ch in _signals.Values) signalSubscribers += ch.SubscriberCount;
            into["信号类型数"] = _signals.Count.ToString();
            into["信号订阅者"] = signalSubscribers.ToString();
        }

        /// <summary>通道级诊断（§4——"哪个事件被谁订阅了几次、有没有空转"；HUD Development-only）。
        /// 低频轮询面（每次调用分配一个列表可接受）；HUD 只在开发形态消费。</summary>
        public IReadOnlyList<EventChannelInfo> GetChannelInfos()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            var list = new List<EventChannelInfo>(_channels.Count);
            foreach (var ch in _channels.Values)
                list.Add(new EventChannelInfo(ch.EventType, ch.SubscriberCount, ch.PublishedCount, ch.DispatchErrors));
            foreach (var ch in _signals.Values)
                list.Add(new EventChannelInfo(ch.EventType, ch.SubscriberCount, ch.PublishedCount, ch.DispatchErrors));
            return list;
        }

        private struct PendingEvent
        {
            public long DueTick;
            public Action Dispatch;
        }
    }


    /// <summary>订阅句柄实现：Dispose 注销；总线 Dispose 后 IsDisposed 亦为 true。</summary>
    internal sealed class EventSubscription : IEventSubscription
    {
        private EventBus _owner;
        private Action _onDispose;
        private int _disposed;

        public EventSubscription(EventBus owner, Action onDispose)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        public bool IsDisposed
            => Volatile.Read(ref _disposed) != 0 || (_owner != null && _owner.IsDisposed);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Action onDispose = _onDispose;
            _onDispose = null;
            _owner = null;
            try { onDispose?.Invoke(); }
            catch (Exception ex) { Log.Error(ex, "EventSubscription.Dispose", LogTag.Event); }
        }
    }

    /// <summary>通道级诊断快照（GetChannelInfos 消费面；HUD Development-only）。</summary>
    public readonly struct EventChannelInfo
    {
        public readonly Type EventType;
        public readonly int Subscribers;        // 订阅者数
        public readonly long Published;         // 累计派发（含无订阅者——空转可见）
        public readonly long Errors;            // 订阅者异常累计

        public EventChannelInfo(Type eventType, int subscribers, long published, long errors)
        {
            EventType = eventType; Subscribers = subscribers; Published = published; Errors = errors;
        }
    }
}
