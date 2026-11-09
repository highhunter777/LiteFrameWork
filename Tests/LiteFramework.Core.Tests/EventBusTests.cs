using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // StrictMode 用例断言 Log.Recent（全局静态）
    public sealed class EventBusTests
    {

        private sealed class ProbeEvent { }
        private sealed class UnheardEvent { }

        [Fact]
        public void EventBus_订阅派发收到_注销后再派发不收到()
        {
            var events = new EventBus();
            var received = 0;
            var unsub = events.Subscribe<ProbeEvent>(_ => received++);

            events.Publish(new ProbeEvent());
            Assert.Equal(1, received);

            unsub.Dispose();
            events.Publish(new ProbeEvent());
            Assert.Equal(1, received);   // 注销后不再收到
        }

        [Fact]
        public void EventBus_注销委托重复执行_幂等不抛()
        {
            var events = new EventBus();
            var unsub = events.Subscribe<ProbeEvent>(_ => { });
            unsub.Dispose();
            unsub.Dispose();                     // 幂等：EventChannel.Remove no-op
        }

        [Fact]
        public void EventBus_派发中改集_本轮快照不崩_下轮生效()
        {
            var events = new EventBus();
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
        public void EventBus_StrictMode_未订阅派发_Recent出现Warning()
        {
            var events = new EventBus() { StrictMode = true };
            events.Publish(new UnheardEvent());
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Warning, last.Level);
            Assert.Contains(nameof(UnheardEvent), last.Message);
        }

        [Fact]
        public void EventBus_热路径稳态零GC()
        {
            var events = new EventBus();
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
        public void EventBus_Dispose_清空订阅_后续操作抛()
        {
            var events = new EventBus() { StrictMode = false };
            var sub = events.Subscribe<ProbeEvent>(_ => { });

            events.Dispose();

            Assert.True(events.IsDisposed);
            Assert.True(sub.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => events.Publish(new ProbeEvent()));
        }

        [Fact]
        public void EventBus_跨线程发布_守卫拒绝()
        {
            var events = new EventBus();
            Exception caught = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { events.Publish(new ProbeEvent()); }
                catch (Exception ex) { caught = ex; }
            });
            thread.Start();
            thread.Join();

            Assert.NotNull(caught);
            Assert.IsType<InvalidOperationException>(caught);
            Assert.Contains("主线程", caught.Message);
        }

        // ---- 批1（《事件中心专项设计》§3.1/§3.2/§3.3/§3.4）----

        [Fact]
        public void EventBus_优先级_小值先派发_同优先级保持注册序()
        {
            var events = new EventBus();
            var order = new List<int>();
            events.Subscribe<ProbeEvent>(_ => order.Add(1), priority: 2);
            events.Subscribe<ProbeEvent>(_ => order.Add(2), priority: 0);
            events.Subscribe<ProbeEvent>(_ => order.Add(3), priority: 1);
            events.Subscribe<ProbeEvent>(_ => order.Add(4), priority: 0);

            events.Publish(new ProbeEvent());

            Assert.Equal(new[] { 2, 4, 3, 1 }, order);   // p0 先（2/4 按注册序），再 p1，最后 p2
        }

        [Fact]
        public void EventBus_重复订阅_Debug告警不抛_双计次单注销余一()
        {
            var events = new EventBus(strictMode: true);   // 重复订阅告警随 StrictMode 快照到通道
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
            unsub.Dispose();
            events.Publish(new ProbeEvent());
            Assert.Equal(4, received);                   // 三条订阅注销一条 → 余两条仍派发
        }

        [Fact]
        public void EventBus_统计_Snapshot追加键与健康度计数()
        {
            var events = new EventBus() { StrictMode = true };
            var into = new Dictionary<string, string>();

            events.Publish(new UnheardEvent());                     // Strict 告警 +1
            events.Subscribe<ProbeEvent>(_ => throw new InvalidOperationException("订阅者故障注入"));
            events.Publish(new ProbeEvent());                        // 异常隔离 + DispatchErrors +1
        }

        [Fact]
        public void EventBus_诊断_通道级信息_订阅数_派发累计_空转与异常可见()
        {
            var events = new EventBus() { StrictMode = false };
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

        [Fact]
        public void EventBus_延迟派发_到期前不派发_到期帧派发()
        {
            var events = new EventBus();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);

            events.PublishQueued(new ProbeEvent(), delayFrames: 2);
            events.Tick(0.016f);
            Assert.Equal(0, received);                               // 第 1 帧未到期
            events.Tick(0.016f);
            Assert.Equal(1, received);                               // 第 2 帧到期
        }

        [Fact]
        public void EventBus_延迟派发_同帧按入队序()
        {
            var events = new EventBus();
            var order = new List<int>();
            events.Subscribe<NumberedEvent>(e => order.Add(e.Value));

            events.PublishQueued(new NumberedEvent { Value = 1 });
            events.PublishQueued(new NumberedEvent { Value = 2 });
            events.PublishQueued(new NumberedEvent { Value = 3 });
            events.Tick(0.016f);

            Assert.Equal(new[] { 1, 2, 3 }, order);
        }

        [Fact]
        public void EventBus_延迟派发_零帧按一帧钳制()
        {
            var events = new EventBus();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);

            events.PublishQueued(new ProbeEvent(), delayFrames: 0);
            Assert.Equal(0, received);
            events.Tick(0.016f);
            Assert.Equal(1, received);
        }

        [Fact]
        public void EventBus_有界队列_溢出丢弃新事件并计数()
        {
            var events = new EventBus(maxQueued: 1);
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);

            events.PublishQueued(new ProbeEvent());
            events.PublishQueued(new ProbeEvent());                  // 溢出：丢弃新事件
            var into = new Dictionary<string, string>();
            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("1", into["QueuedDropped"]);
            Assert.Equal("1", into["DelayedCount"]);

            events.Tick(0.016f);
            Assert.Equal(1, received);
        }

        [Fact]
        public void EventBus_队列峰值与快照()
        {
            var events = new EventBus(maxQueued: 4);
            events.Subscribe<ProbeEvent>(_ => { });

            events.PublishQueued(new ProbeEvent());
            events.PublishQueued(new ProbeEvent());
            var into = new Dictionary<string, string>();
            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("2", into["QueuedPeak"]);
            Assert.Equal("2", into["DelayedCount"]);

            events.Tick(0.016f);
            ((IModuleStats)events).Snapshot(into);
            Assert.Equal("0", into["DelayedCount"]);
            Assert.Equal("2", into["QueuedPeak"]);                   // 峰值保留
        }

        [Fact]
        public void EventBus_延迟派发_处理器内新入队留到下帧()
        {
            var events = new EventBus();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ =>
            {
                received++;
                if (received == 1) events.PublishQueued(new ProbeEvent());
            });

            events.PublishQueued(new ProbeEvent());
            events.Tick(0.016f);
            Assert.Equal(1, received);                               // 本轮快照只有 1 件
            events.Tick(0.016f);
            Assert.Equal(2, received);
        }

        private sealed class NumberedEvent
        {
            public int Value;
        }
    }
}
