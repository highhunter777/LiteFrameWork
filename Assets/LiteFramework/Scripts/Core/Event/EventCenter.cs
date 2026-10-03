using System;
using System.Collections.Generic;

namespace LiteFramework
{
    public sealed class EventCenter : IEventCenter, ITickable, IModuleStats
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        public bool StrictMode { get; set; } = true;    // 编辑器/开发构建：未订阅事件告警
#else
        public bool StrictMode { get; set; } = false;   // release：收紧
#endif

        /// <summary>队列容量上限（《事件中心专项设计》§3.2）。入队超限丢弃并计数——
        /// Tick 驱动的队列在消费不及（卡顿/漏 Tick）时有界可诊断，不无界堆积。</summary>
        public int MaxQueued { get; set; } = 256;

        /// <summary>队列溢出丢弃累计（诊断：消费不及的可见信号）。</summary>
        public long QueuedDroppedCount { get; private set; }

        /// <summary>订阅（§3.1 优先级派发：小值先派发，同优先级保持注册序）。priority 默认 0 = 既有调用零改动。</summary>
        public Action Subscribe<T>(Action<T> handler, int priority = 0) where T : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var channel = (EventChannel<T>)GetOrAddChannel<T>();
            channel.Add(handler, priority);
            return () => channel.Remove(handler);          // 闭包分配一次/订阅;订阅是生命周期边界低频操作,可接受
        }

        /// <summary>注册发布拦截器（§3.5）：Publish 管线在派发之前按注册序运行；返回注销委托（幂等）。
        /// 返回 false = 否决（该次发布丢弃、InterceptedCount 记账、池化事件否决路径同样回收）。</summary>
        public Action RegisterInterceptor(IEventInterceptor interceptor)
        {
            if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));
            _interceptors.Add(interceptor);
            return () => _interceptors.Remove(interceptor);    // 幂等（同订阅/注销语义）
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
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (!RunInterceptors(e))
            {
                RecycleIfPooled(e);                        // 否决路径同样回收——不派发、不告警、不计数发布
                return;
            }
            _published++;
            var channel = (EventChannel<T>)GetOrAddChannel<T>();
            bool dispatched = channel.Publish(e);
            if (!dispatched && StrictMode)
            {
                _strictWarnings++;
                Log.Warning($"no subscriber: {typeof(T).Name}", "Event");
            }
            RecycleIfPooled(e);                            // 无订阅者路径同样回收——否则无订阅事件 = 泄漏
        }

        /// <summary>入队，Tick 统一派发（低频便利）。class 约束下 object 装箱为零成本引用上转。
        /// delayFrames（§3.3 延迟派发，默认 1 = 现状语义）：入队后第 N 帧 Tick 派发——到期条目按入队序，
        /// 经同一派发核心（StrictMode/回收/异常隔离/优先级全适用）。UI 反馈的"下一拍"时序不必各自起计时器。</summary>
        public void PublishQueued<T>(T e, int delayFrames = 1) where T : class
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (delayFrames < 1) delayFrames = 1;            // 当帧不触发是队列语义下限

            if (_queue.Count >= MaxQueued)
            {
                QueuedDroppedCount++;
                Log.Error($"队列溢出丢弃（MaxQueued={MaxQueued}）: {typeof(T).Name}", "Event");
                RecycleIfPooled(e);                         // 丢弃路径同样回收——与"无订阅者路径同样回收"同纪律
                return;
            }

            if (!_adapters.ContainsKey(typeof(T)))
                _adapters[typeof(T)] = obj => Publish((T)obj);   // 每类型一次,恢复强类型后走同一派发核心
            _queue.Enqueue(new PendingEvent(typeof(T), e, _tickCount + delayFrames));   // class 约束:引用上转,零装箱
            if (_queue.Count > _queuedPeak) _queuedPeak = _queue.Count;
        }

        public void Tick(float realDelta)
        {
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
                if (_adapters.TryGetValue(pe.EventType, out var fire)) fire(pe.Event);
            }
        }

        private IEventChannel GetOrAddChannel<T>() where T : class
        {
            if (_channels.TryGetValue(typeof(T), out var ch)) return ch;
            var created = new EventChannel<T>();
            _channels[typeof(T)] = created;
            return created;
        }

        /// <summary>所有权移交的落点:发布即移交,派发完成后中心统一回收。channel 保持纯机制,不知道 IReference 存在。</summary>
        private void RecycleIfPooled(object e)
        {
            if (e is IReference pooled)
            {
                _recycled++;
                ReferencePool.Release(pooled);
            }
        }

        public string StatsName => "EventCenter";

        /// <summary>契约：实现负责 into.Clear() 再填入；key 为常量串零分配，value 为插值串（HUD 低频轮询可接受）。
        /// 批2 追加键（《事件中心专项设计》§4）——既有键不动，HUD 消费方兼容。</summary>
        public void Snapshot(Dictionary<string, string> into)
        {
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>通道级诊断（§4——"哪个事件被谁订阅了几次、有没有空转"；镜像 ReferencePoolInfo 形态）。
        /// Debug 三宏——HUD asmdef 本身 Development-only；每次调用分配一个列表（低频轮询可接受）。</summary>
        public IReadOnlyList<EventChannelInfo> GetChannelInfos()
        {
            var list = new List<EventChannelInfo>(_channels.Count);
            foreach (var ch in _channels.Values)
                list.Add(new EventChannelInfo(ch.EventType, ch.SubscriberCount, ch.PublishedCount, ch.DispatchErrors));
            return list;
        }
#endif

        private readonly struct PendingEvent
        {
            public readonly Type EventType;
            public readonly object Event;
            public readonly long DueTick;                    // 到期帧（Tick 计数域）
            public PendingEvent(Type eventType, object e, long dueTick) { EventType = eventType; Event = e; DueTick = dueTick; }
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
