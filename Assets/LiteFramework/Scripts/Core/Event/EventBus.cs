using System;
using System.Collections.Generic;
using System.Threading;

namespace LiteFramework
{
    public sealed class EventBus : IEventBus, IMatchEventBus, ITickable, IModuleStats
    {
        private readonly Dictionary<Type, IEventChannel> _channels = new Dictionary<Type, IEventChannel>(32);
        private readonly Queue<PendingEvent> _queue = new Queue<PendingEvent>(16);
        private readonly Dictionary<Type, Action<object>> _adapters = new Dictionary<Type, Action<object>>(16);
        private long _published;
        private long _tickCount;                           // 帧计数（延迟派发的到期基准，Tick 自增）
        private int _queuedPeak;                            // 队列峰值（消费不及的留存信号）
        private long _recycled;                            // 池化事件回收累计
        private long _strictWarnings;                      // StrictMode 告警累计（没人听的事件信号）
        private long _intercepted;                         // 拦截否决累计（§3.5——被拦截管掉的事件信号）
        private readonly List<IEventInterceptor> _interceptors = new List<IEventInterceptor>(2);   // 注册序运行
        private readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        private bool _disposed;

        /// <summary>严格模式（未订阅事件告警）：装配点按 <c>DevBuildFacts.IsDevelopment</c> 传初值；
        /// 运行期可改（既有测试面），但**通道创建时刻**的快照决定该通道是否告警重复订阅。</summary>
        public bool StrictMode { get; set; }

        /// <summary>队列容量上限（《事件中心专项设计》§3.2）。入队超限丢弃并计数——
        /// Tick 驱动的队列在消费不及（卡顿/漏 Tick）时有界可诊断，不无界堆积。
        /// 构造注入（缺省 = <see cref="QueueLimits.DefaultCapacity"/>），运行期不可改。</summary>
        public int MaxQueued { get; }
        public bool IsDisposed => _disposed;
        private readonly IReferencePool _pool;

        public EventBus(IReferencePool pool, int maxQueued = QueueLimits.DefaultCapacity, bool strictMode = false)
        {
            if (maxQueued < 1) throw new ArgumentOutOfRangeException(nameof(maxQueued));
            MaxQueued = maxQueued;
            StrictMode = strictMode;
            _pool = pool ?? throw new System.ArgumentNullException(nameof(pool), "IReferencePool 未注入");
        }

        /// <summary>队列溢出丢弃累计（诊断：消费不及的可见信号）。</summary>
        public long QueuedDroppedCount { get; private set; }

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

        /// <summary>注册发布拦截器（§3.5）：Publish 管线在派发之前按注册序运行；返回注销委托（幂等）。
        /// 返回 false = 否决（该次发布丢弃、InterceptedCount 记账、池化事件否决路径同样回收）。</summary>
        public IDisposable RegisterInterceptor(IEventInterceptor interceptor)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));
            _interceptors.Add(interceptor);
            return new EventSubscription(this, () => _interceptors.Remove(interceptor));
        }

        /// <summary>拦截管线（§3.5）：全部放行返回 true；任一否决返回 false。
        /// 拦截器异常 = 记日志跳过继续（fail-open——观测性缺陷不阻断业务流）。零拦截器 = 一次计数判断。</summary>
        private bool RunInterceptors<T>(T e) where T : class
        {
            if (_interceptors.Count == 0) return true;
            for (int i = 0; i < _interceptors.Count; i++)
            {
                bool pass;
                try { pass = _interceptors[i].Intercept(typeof(T), e); }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Event.Interceptor.{_interceptors[i].GetType().Name}");
                    continue;
                }
                if (!pass)
                {
                    _intercepted++;
                    return false;
                }
            }
            return true;
        }

        public void Publish<T>(T e) where T : class
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (!RunInterceptors(e))
            {
                return;
            }
            _published++;
            var channel = (EventChannel<T>)GetOrAddChannel<T>();
            bool dispatched = channel.Publish(e);
            if (!dispatched && StrictMode)
            {
                _strictWarnings++;
                Log.Warning($"no subscriber: {typeof(T).Name}", LogTag.Event);
            }
        }

        /// <summary>显式池化发布：派发完成后由中心回收（仅 IReference 事件）。普通事件请用 Publish。</summary>
        public void PublishPooled<T>(T e) where T : class, IReference
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (e == null) throw new ArgumentNullException(nameof(e));
            try { Publish(e); }
            finally { RecycleIfPooled(e); }
        }

        /// <summary>入队，Tick 统一派发（低频便利）。class 约束下 object 装箱为零成本引用上转。
        /// delayFrames（§3.3 延迟派发，默认 1 = 现状语义）：入队后第 N 帧 Tick 派发——到期条目按入队序，
        /// 经同一派发核心（StrictMode/回收/异常隔离/优先级全适用）。UI 反馈的"下一拍"时序不必各自起计时器。</summary>
        public void PublishQueued<T>(T e, int delayFrames = 1) where T : class
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (delayFrames < 1) delayFrames = 1;            // 当帧不触发是队列语义下限

            if (_queue.Count >= MaxQueued)
            {
                QueuedDroppedCount++;
                Log.Error($"队列溢出丢弃（MaxQueued={MaxQueued}）: {typeof(T).Name}", LogTag.Event);
                RecycleIfPooled(e);                         // 队列丢弃路径回收（队列已接管池化事件）
                return;
            }

            if (!_adapters.ContainsKey(typeof(T)))
                _adapters[typeof(T)] = obj => Publish((T)obj);   // 每类型一次,恢复强类型后走同一派发核心
            _queue.Enqueue(new PendingEvent(typeof(T), e, _tickCount + delayFrames));   // class 约束:引用上转,零装箱
            if (_queue.Count > _queuedPeak) _queuedPeak = _queue.Count;
        }

        public void Tick(float realDelta)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            _tickCount++;
            int n = _queue.Count;                          // 快照量:派发期间新入队留到下帧,防互相触发同帧死循环
            for (int i = 0; i < n; i++)
            {
                PendingEvent pe = _queue.Dequeue();
                if (pe.DueTick > _tickCount)
                {
                    _queue.Enqueue(pe);                    // 未到期（延迟在途）:留队到未来帧——不属于"当帧快照"级联防护语义
                    continue;
                }
                if (_adapters.TryGetValue(pe.EventType, out var fire))
                {
                    fire(pe.Event);
                    RecycleIfPooled(pe.Event);              // 队列接管池化事件：派发完成统一回收
                }
            }
        }

        public void Dispose()
        {
            EnsureMainThread();
            if (_disposed) return;
            _disposed = true;

            foreach (var pe in _queue) RecycleIfPooled(pe.Event);   // 队列接管中的池化事件在关闭时归还
            _queue.Clear();
            _adapters.Clear();
            foreach (var channel in _channels.Values) channel.Clear();
            _channels.Clear();
            _interceptors.Clear();
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

        /// <summary>池化事件回收点：仅 PublishPooled 与队列路径调用；Publish 不接管所有权。channel 保持纯机制。</summary>
        private void RecycleIfPooled(object e)
        {
            if (e is IReference pooled)
            {
                _recycled++;
                _pool.Release(pooled);
            }
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
            int delayed = 0;
            foreach (var ch in _channels.Values)
            {
                subscribers += ch.SubscriberCount;
                dispatchErrors += ch.DispatchErrors;
            }
            foreach (var pe in _queue)
                if (pe.DueTick > _tickCount) delayed++;    // HUD 低频轮询，队列扫描可接受

            into["事件类型数"] = _channels.Count.ToString();
            into["订阅者总数"] = subscribers.ToString();
            into["队列长度"] = _queue.Count.ToString();
            into["累计发布"] = _published.ToString();
            into["QueuedPeak"] = _queuedPeak.ToString();
            into["QueuedDropped"] = QueuedDroppedCount.ToString();
            into["DispatchErrors"] = dispatchErrors.ToString();
            into["Recycled"] = _recycled.ToString();
            into["StrictWarnings"] = _strictWarnings.ToString();
            into["DelayedCount"] = delayed.ToString();
            into["Intercepted"] = _intercepted.ToString();
        }

        /// <summary>通道级诊断（§4——"哪个事件被谁订阅了几次、有没有空转"；镜像 ReferencePoolInfo 形态）。
        /// 低频轮询面（每次调用分配一个列表可接受）；HUD 只在开发形态消费。</summary>
        public IReadOnlyList<EventChannelInfo> GetChannelInfos()
        {
            var list = new List<EventChannelInfo>(_channels.Count);
            foreach (var ch in _channels.Values)
                list.Add(new EventChannelInfo(ch.EventType, ch.SubscriberCount, ch.PublishedCount, ch.DispatchErrors));
            return list;
        }

        private readonly struct PendingEvent
        {
            public readonly Type EventType;
            public readonly object Event;
            public readonly long DueTick;                    // 到期帧（Tick 计数域）
            public PendingEvent(Type eventType, object e, long dueTick) { EventType = eventType; Event = e; DueTick = dueTick; }
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

    /// <summary>通道级诊断快照（GetChannelInfos 消费面；HUD Development-only——同 ReferencePoolInfo 先例）。</summary>
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
