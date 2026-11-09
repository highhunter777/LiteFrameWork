using System;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>两条通道（class 事实 / struct 信号）共用的语义契约；差异项单列。</summary>
    [Collection("CoreStatic")]
    public sealed class ChannelConformanceTests
    {
        private sealed class ProbeEvent { public int Value; }

        private struct ProbeSignal { public int Value; }

        private sealed class Probe
        {
            private readonly EventBus _bus;
            private readonly bool _isSignal;

            public Probe(bool isSignal) { _bus = new EventBus(); _isSignal = isSignal; }

            public IEventSubscription Subscribe(Action<int> onValue, int priority = 0)
                => _isSignal
                    ? _bus.SubscribeSignal<ProbeSignal>((in ProbeSignal s) => onValue(s.Value), priority)
                    : _bus.Subscribe<ProbeEvent>(e => onValue(e.Value), priority);

            public void Publish(int value)
            {
                if (_isSignal) _bus.PublishSignal(new ProbeSignal { Value = value });
                else _bus.Publish(new ProbeEvent { Value = value });
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void 通道_订阅派发收到_注销后不再收到(bool isSignal)
        {
            var probe = new Probe(isSignal);
            int received = 0;
            var sub = probe.Subscribe(v => received++);

            probe.Publish(1);
            Assert.Equal(1, received);

            sub.Dispose();
            probe.Publish(2);
            Assert.Equal(1, received);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void 通道_注销重复执行_幂等不抛(bool isSignal)
        {
            var probe = new Probe(isSignal);
            var sub = probe.Subscribe(_ => { });
            sub.Dispose();
            sub.Dispose();
            Assert.True(sub.IsDisposed);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void 通道_同优先级_按注册序派发(bool isSignal)
        {
            var probe = new Probe(isSignal);
            var order = new System.Collections.Generic.List<int>();
            probe.Subscribe(_ => order.Add(1));
            probe.Subscribe(_ => order.Add(2));
            probe.Subscribe(_ => order.Add(3));

            probe.Publish(0);
            Assert.Equal(new[] { 1, 2, 3 }, order);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void 通道_优先级小值先派发(bool isSignal)
        {
            var probe = new Probe(isSignal);
            var order = new System.Collections.Generic.List<int>();
            probe.Subscribe(_ => order.Add(2), priority: 2);
            probe.Subscribe(_ => order.Add(0), priority: 0);
            probe.Subscribe(_ => order.Add(1), priority: 1);

            probe.Publish(0);
            Assert.Equal(new[] { 0, 1, 2 }, order);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void 通道_重复订阅同一委托_收两次(bool isSignal)
        {
            var probe = new Probe(isSignal);
            int received = 0;
            Action<int> handler = _ => received++;
            probe.Subscribe(handler);
            probe.Subscribe(handler);

            probe.Publish(0);
            Assert.Equal(2, received);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void 通道_无订阅者发布_不抛(bool isSignal)
        {
            var probe = new Probe(isSignal);
            probe.Publish(7);
        }

        [Fact]
        public void 事实通道_订阅者异常被吞并_后续订阅者仍收到()
        {
            var bus = new EventBus();
            int after = 0;
            bus.Subscribe<ProbeEvent>(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe<ProbeEvent>(_ => after++);

            bus.Publish(new ProbeEvent());
            Assert.Equal(1, after);
        }

        [Fact]
        public void 信号通道_订阅者异常上抛_后续订阅者不再收到()
        {
            var bus = new EventBus();
            int after = 0;
            bus.SubscribeSignal<ProbeSignal>((in ProbeSignal _) => throw new InvalidOperationException("boom"));
            bus.SubscribeSignal<ProbeSignal>((in ProbeSignal _) => after++);

            Assert.Throws<InvalidOperationException>(() => bus.PublishSignal(new ProbeSignal()));
            Assert.Equal(0, after);
        }

        [Fact]
        public void 通道_派发中注销自己_当轮快照不崩()
        {
            var bus = new EventBus();
            int calls = 0;
            IEventSubscription sub = null;
            sub = bus.SubscribeSignal<ProbeSignal>((in ProbeSignal _) => { calls++; sub.Dispose(); });

            bus.PublishSignal(new ProbeSignal());
            bus.PublishSignal(new ProbeSignal());
            Assert.Equal(1, calls);
        }
    }
}
