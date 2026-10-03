using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // StrictMode 用例断言 Log.Recent（全局静态）
    public sealed class EventCenterTests
    {
        private sealed class ProbeEvent { }
        private sealed class UnheardEvent { }

        [Fact]
        public void EventCenter_订阅派发收到_注销后再派发不收到()
        {
            var events = new EventCenter();
            var received = 0;
            var unsub = events.Subscribe<ProbeEvent>(_ => received++);

            events.Publish(new ProbeEvent());
            Assert.Equal(1, received);

            unsub();
            events.Publish(new ProbeEvent());
            Assert.Equal(1, received);   // 注销后不再收到
        }

        [Fact]
        public void EventCenter_注销委托重复执行_幂等不抛()
        {
            var events = new EventCenter();
            var unsub = events.Subscribe<ProbeEvent>(_ => { });
            unsub();
            unsub();                     // 幂等：EventChannel.Remove no-op
        }

        [Fact]
        public void EventCenter_派发中改集_本轮快照不崩_下轮生效()
        {
            var events = new EventCenter();
            var secondReceived = 0;

            events.Subscribe<ProbeEvent>(_ =>
            {
                // 派发中订阅：本轮快照不含它，下轮生效
                events.Subscribe<ProbeEvent>(_ => secondReceived++);
            });

            events.Publish(new ProbeEvent());
            Assert.Equal(0, secondReceived);
            events.Publish(new ProbeEvent());
            Assert.Equal(1, secondReceived);
        }

        [Fact]
        public void EventCenter_PublishQueued_当帧不触发_Tick后触发()
        {
            var events = new EventCenter();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);

            events.PublishQueued(new ProbeEvent());
            Assert.Equal(0, received);   // 队列路径不经 Publish 直派

            events.Tick(0.016f);
            Assert.Equal(1, received);
        }

        [Fact]
        public void EventCenter_StrictMode_未订阅派发_Recent出现Warning()
        {
            var events = new EventCenter { StrictMode = true };
            events.Publish(new UnheardEvent());
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Warning, last.Level);
            Assert.Contains(nameof(UnheardEvent), last.Message);
        }

        [Fact]
        public void EventCenter_热路径稳态零GC()
        {
            var events = new EventCenter();
            events.Subscribe<ProbeEvent>(_ => { });
            events.Publish(new ProbeEvent());          // 热身：通道/快照缓冲就位

            var batch = new ProbeEvent[1000];          // 事件对象预分配在测量外——测的是派发路径
            for (int i = 0; i < batch.Length; i++) batch[i] = new ProbeEvent();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < batch.Length; i++) events.Publish(batch[i]);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0, after - before);
        }

        [Fact]
        public void EventCenter_池化事件_无订阅者路径同样回收()
        {
            var events = new EventCenter { StrictMode = false };
            var e = ReferencePool.Acquire<PooledProbe>();
            events.Publish(e);                          // 无订阅者：发布即移交，中心统一回收
            Assert.Same(e, ReferencePool.Acquire<PooledProbe>());   // 已回池 → 再取是同一实例
        }

        // ---- 批1（《事件中心专项设计》§3.1/§3.2/§3.3/§3.4）----

        [Fact]
        public void EventCenter_优先级_小值先派发_同优先级保持注册序()
        {
            var events = new EventCenter();
            var order = new List<int>();
            events.Subscribe<ProbeEvent>(_ => order.Add(1), priority: 2);
            events.Subscribe<ProbeEvent>(_ => order.Add(2), priority: 0);
            events.Subscribe<ProbeEvent>(_ => order.Add(3), priority: 1);
            events.Subscribe<ProbeEvent>(_ => order.Add(4), priority: 0);

            events.Publish(new ProbeEvent());

            Assert.Equal(new[] { 2, 4, 3, 1 }, order);   // p0 先（2/4 按注册序），再 p1，最后 p2
        }

        [Fact]
        public void EventCenter_重复订阅_Debug告警不抛_双计次单注销余一()
        {
            var events = new EventCenter();
            var received = 0;
            Action<ProbeEvent> handler = _ => received++;
            events.Subscribe(handler);
            events.Subscribe(handler);                   // 重复：Debug 告警（多实例复用同一委托合法）

            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Warning, last.Level);
            Assert.Contains("重复订阅", last.Message);

            events.Publish(new ProbeEvent());
            Assert.Equal(2, received);                  // 两次订阅 = 派发两次（告警不改变注册行为）

            var unsub = events.Subscribe(handler);       // 第三次订阅取一个注销委托
            unsub();
            events.Publish(new ProbeEvent());
            Assert.Equal(4, received);                   // 三条订阅注销一条 → 余两条仍派发
        }

        [Fact]
        public void EventCenter_有界队列_溢出丢弃计数_池化事件丢弃路径回收()
        {
            var events = new EventCenter { MaxQueued = 2, StrictMode = false };
            var e1 = ReferencePool.Acquire<PooledProbe>();
            var e2 = ReferencePool.Acquire<PooledProbe>();
            var e3 = ReferencePool.Acquire<PooledProbe>();

            events.PublishQueued(e1);
            events.PublishQueued(e2);
            events.PublishQueued(e3);                    // 溢出：丢弃并回收（不因溢出产生池泄漏）

            Assert.Equal(1, events.QueuedDroppedCount);
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Error, last.Level);
            Assert.Contains("溢出", last.Message);
            Assert.Same(e3, ReferencePool.Acquire<PooledProbe>());   // 被丢弃的池化事件同样回收——再取是同一实例
        }

        [Fact]
        public void EventCenter_延迟派发_到期帧触发_入队序保持_与当帧队列先后分明()
        {
            var events = new EventCenter();
            var order = new List<string>();
            events.Subscribe<TaggedProbe>(e => order.Add(e.Tag));

            events.PublishQueued(new TaggedProbe { Tag = "a" }, delayFrames: 2);
            events.PublishQueued(new TaggedProbe { Tag = "b" }, delayFrames: 2);
            events.PublishQueued(new TaggedProbe { Tag = "c" });            // 默认 1 = 现状语义

            events.Tick(0.016f);
            Assert.Equal(new[] { "c" }, order);          // 当帧队列先走；a/b 延迟在途未触发
            events.Tick(0.016f);
            Assert.Equal(new[] { "c", "a", "b" }, order); // 到期帧按入队序派发
        }

        private sealed class TaggedProbe
        {
            public string Tag;
        }

        // ---- 批2（《事件中心专项设计》§4：统计与诊断面）----

        [Fact]
        public void EventCenter_统计_Snapshot追加键与健康度计数()
        {
            var events = new EventCenter { StrictMode = true };
            var into = new Dictionary<string, string>();

            events.Publish(new UnheardEvent());                     // Strict 告警 +1
            events.Subscribe<ProbeEvent>(_ => throw new InvalidOperationException("订阅者故障注入"));
            events.Publish(new ProbeEvent());                        // 异常隔离 + DispatchErrors +1
            events.PublishQueued(new ProbeEvent(), delayFrames: 2);  // 延迟在途 +1
            events.PublishQueued(new ProbeEvent());                  // 峰值 2

            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("1", into["StrictWarnings"]);
            Assert.Equal("1", into["DispatchErrors"]);
            Assert.Equal("2", into["DelayedCount"]);                 // 延迟 + 当帧队列件此刻均"未到期在途"
            Assert.Equal("2", into["QueuedPeak"]);
            Assert.Equal("0", into["QueuedDropped"]);

            events.Tick(0.016f);                                     // 当帧队列派发（异常再 +1）
            events.Tick(0.016f);                                     // 延迟到期派发（异常再 +1）
            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("0", into["DelayedCount"]);                // 延迟在途随到期清零
            Assert.Equal("3", into["DispatchErrors"]);              // 直发 1 + 当帧队列 1 + 延迟到期 1（队列路径同走派发核心）
        }

        [Fact]
        public void EventCenter_统计_池化事件回收计数_含订阅者路径()
        {
            var events = new EventCenter { StrictMode = false };
            var e = ReferencePool.Acquire<PooledProbe>();
            events.Subscribe<PooledProbe>(_ => { });
            events.Publish(e);                                       // 派发完同样回收（所有权移交）

            var into = new Dictionary<string, string>();
            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("1", into["Recycled"]);
        }

        [Fact]
        public void EventCenter_诊断_通道级信息_订阅数_派发累计_空转与异常可见()
        {
            var events = new EventCenter { StrictMode = false };
            events.Subscribe<ProbeEvent>(_ => { });
            events.Subscribe<ProbeEvent>(_ => { });
            events.Subscribe<ProbeEvent>(_ => throw new InvalidOperationException("订阅者故障注入"));

            events.Publish(new ProbeEvent());                        // 2 正常 + 1 异常
            events.Publish(new UnheardEvent());                      // 空转：无订阅者仍计入派发
            events.Publish(new ProbeEvent());

            var probe = events.GetChannelInfos();
            var probeInfo = probe.First(i => i.EventType == typeof(ProbeEvent));
            var unheardInfo = probe.First(i => i.EventType == typeof(UnheardEvent));

            Assert.Equal(3, probeInfo.Subscribers);
            Assert.Equal(2, probeInfo.Published);
            Assert.Equal(2, probeInfo.Errors);                      // 两次派发各炸一次
            Assert.Equal(0, unheardInfo.Subscribers);
            Assert.Equal(1, unheardInfo.Published);                  // "有没有空转"可见
        }

        // ---- 批3（§3.5 发布拦截链）----

        private sealed class PassAll : IEventInterceptor
        {
            public int Calls;
            public bool Intercept(Type eventType, object e) { Calls++; return true; }
        }

        private sealed class Veto<TEvent> : IEventInterceptor where TEvent : class
        {
            public int Calls;
            public bool Intercept(Type eventType, object e) { Calls++; return eventType != typeof(TEvent); }
        }

        private sealed class Recorder : IEventInterceptor
        {
            private readonly string _tag;
            private readonly List<string> _log;
            public Recorder(string tag, List<string> log) { _tag = tag; _log = log; }
            public bool Intercept(Type eventType, object e) { _log.Add(_tag); return true; }
        }

        private sealed class Throwing : IEventInterceptor
        {
            public bool Intercept(Type eventType, object e) => throw new InvalidOperationException("拦截器故障注入");
        }

        [Fact]
        public void EventCenter_拦截_观测放行_派发正常()
        {
            var events = new EventCenter();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);
            var pass = new PassAll();
            events.RegisterInterceptor(pass);

            events.Publish(new ProbeEvent());
            Assert.Equal(1, received);                     // 放行不改变派发
            Assert.Equal(1, pass.Calls);
        }

        [Fact]
        public void EventCenter_拦截_否决丢弃_不派发_否决路径池化回收()
        {
            var events = new EventCenter();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);
            var veto = new Veto<ProbeEvent>();
            events.RegisterInterceptor(veto);

            events.Publish(new ProbeEvent());              // 否决：订阅者存在也不派发
            Assert.Equal(0, received);
            Assert.Equal(1, veto.Calls);

            var pooledVeto = new Veto<PooledProbe>();
            events.RegisterInterceptor(pooledVeto);
            var pooled = ReferencePool.Acquire<PooledProbe>();
            events.Publish(pooled);                        // 否决 + 池化事件回收（不因否决泄漏）
            Assert.Same(pooled, ReferencePool.Acquire<PooledProbe>());
        }

        [Fact]
        public void EventCenter_拦截_注册序运行_注销幂等()
        {
            var events = new EventCenter();
            var order = new List<string>();
            var unsubA = events.RegisterInterceptor(new Recorder("A", order));
            events.RegisterInterceptor(new Recorder("B", order));

            events.Publish(new ProbeEvent());
            Assert.Equal(new[] { "A", "B" }, order);       // 先注册先执行

            unsubA();
            unsubA();                                      // 幂等：二次注销 no-op
            events.Publish(new ProbeEvent());
            Assert.Equal(new[] { "A", "B", "B" }, order);  // A 已移除，B 仍在
        }

        [Fact]
        public void EventCenter_拦截_异常隔离_fail_open_不阻断派发()
        {
            var events = new EventCenter();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);
            events.RegisterInterceptor(new Throwing());    // 拦截器作者代码炸了
            var pass = new PassAll();
            events.RegisterInterceptor(pass);              // 后续拦截器照常运行

            events.Publish(new ProbeEvent());
            Assert.Equal(1, pass.Calls);                   // 抛异常的跳过，管线继续
            Assert.Equal(1, received);                     // fail-open：观测性缺陷不阻断业务流
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Error, last.Level);
        }

        [Fact]
        public void EventCenter_拦截_否决计数进Snapshot()
        {
            var events = new EventCenter();
            events.RegisterInterceptor(new Veto<ProbeEvent>());
            events.Publish(new ProbeEvent());
            events.Publish(new ProbeEvent());

            var into = new Dictionary<string, string>();
            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("2", into["Intercepted"]);        // 否决可观察（健康度信号）
        }

        [Fact]
        public void EventCenter_拦截_队列路径同样适用_零特例()
        {
            var events = new EventCenter();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);
            events.RegisterInterceptor(new Veto<ProbeEvent>());

            events.PublishQueued(new ProbeEvent());         // 入队不拦（拦截在派发核心内）
            events.Tick(0.016f);                            // Tick 派发 → 经 Publish 核 → 被否决
            Assert.Equal(0, received);
            var into = new Dictionary<string, string>();
            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("1", into["Intercepted"]);        // 队列/延迟路径拦截全适用（§3.5：同一核心零特例）
        }


        private sealed class PooledProbe : IReference
        {
            public void Clear() { }
        }
    }
}
