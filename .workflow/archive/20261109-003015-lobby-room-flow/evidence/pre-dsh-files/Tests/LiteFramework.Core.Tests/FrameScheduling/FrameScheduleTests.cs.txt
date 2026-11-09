using System;
using System.Collections.Generic;
using System.IO;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class FrameScheduleTests
    {
        private interface IAlias { }

        private sealed class TickProbe : ITickable, IAlias, IDisposable
        {
            public Action<float> OnTick { get; set; }
            public int Calls { get; private set; }
            public int DisposeCount { get; private set; }
            public void Tick(float delta) { Calls++; OnTick?.Invoke(delta); }
            public void Dispose() => DisposeCount++;
        }

        private sealed class SecondTick : ITickable
        {
            private readonly Action<float> _tick;
            public SecondTick(Action<float> tick) => _tick = tick;
            public void Tick(float delta) => _tick(delta);
        }

        [Fact]
        public void 实际执行_显式顺序_别名只执行一次_原样传递帧间隔()
        {
            using var services = new ServiceContainer();
            using var frames = new FrameSchedule(services);
            var received = new List<(string, float)>();
            var first = new TickProbe { OnTick = dt => received.Add(("first", dt)) };
            services.RegisterInstance(first);
            services.RegisterInstance<IAlias>(first);
            services.RegisterInstance(new SecondTick(dt => received.Add(("second", dt))));
            frames.SetOrder(typeof(SecondTick), typeof(IAlias), typeof(TickProbe));
            services.Seal();
            frames.Tick(0.37f);
            Assert.Equal(new[] { ("second", 0.37f), ("first", 0.37f) }, received);
            Assert.Equal(1, first.Calls);
        }

        [Fact]
        public void 校验与密封_不构造服务_解析后填入原位置()
        {
            using var services = new ServiceContainer();
            using var frames = new FrameSchedule(services);
            int constructions = 0;
            services.RegisterFactory<ITickable>(_ => { constructions++; return new TickProbe(); });
            frames.SetOrder(typeof(ITickable));
            services.Seal();
            Assert.Equal(0, constructions);
            Assert.Empty(frames.Tickables);
            var tick = services.Resolve<ITickable>();
            Assert.Same(tick, Assert.Single(frames.Tickables));
            Assert.Equal(1, constructions);
        }

        [Fact]
        public void 未编排实例注册失败_不污染登记或现有计划()
        {
            using var services = new ServiceContainer();
            using var frames = new FrameSchedule(services);
            frames.SetOrder();
            Assert.Throws<InvalidOperationException>(() => services.RegisterInstance(new TickProbe()));
            Assert.False(services.TryGetInstance<TickProbe>(out _));
            Assert.Empty(frames.Tickables);
        }

        [Fact]
        public void 瞬态工厂隐藏Tickable_发布前拒绝_容器仍回收它()
        {
            var services = new ServiceContainer();
            using var frames = new FrameSchedule(services);
            var tick = new TickProbe();
            services.RegisterFactory<object>(_ => tick, ServiceLifetime.Transient);
            services.Seal();
            Assert.Contains("Transient", Assert.Throws<InvalidOperationException>(() => services.Resolve<object>()).Message);
            Assert.False(services.TryGetInstance<object>(out _));
            Assert.Empty(frames.Tickables);
            services.Dispose();
            Assert.Equal(1, tick.DisposeCount);
            Assert.True(frames.IsDisposed);
        }

        [Fact]
        public void 释放帧计划_解除验证订阅_不接管服务资源()
        {
            using var services = new ServiceContainer();
            var frames = new FrameSchedule(services);
            var tick = new TickProbe();
            services.RegisterFactory<object>(_ => tick);
            frames.SetOrder();
            frames.Dispose();
            frames.Dispose();
            services.Seal();
            Assert.Same(tick, services.Resolve<object>());
            Assert.Equal(0, tick.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => frames.Tick(0));
            Assert.Throws<ObjectDisposedException>(() => frames.SetOrder());
            Assert.Throws<ObjectDisposedException>(() => frames.Validate());
            Assert.Empty(frames.Tickables);
        }

        [Fact]
        public void 容器释放_帧计划清引用_不继续驱动已释放对象()
        {
            var services = new ServiceContainer();
            var frames = new FrameSchedule(services);
            var tick = new TickProbe();
            services.RegisterInstance(tick, ServiceOwnership.ContainerOwned);
            services.Dispose();
            Assert.True(frames.IsDisposed);
            Assert.Empty(frames.Tickables);
            Assert.Equal(1, tick.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => frames.Tick(1));
            Assert.Throws<ObjectDisposedException>(() => new FrameSchedule(services));
        }

        [Fact]
        public void 子域帧计划_只驱动本域_父域释放清理两侧()
        {
            var root = new ServiceContainer();
            using var rootFrames = new FrameSchedule(root);
            var rootTick = new TickProbe();
            root.RegisterInstance(rootTick);
            root.Seal();
            var child = (ServiceContainer)root.CreateScope("Match");
            using var childFrames = new FrameSchedule(child);
            var childTick = new SecondTick(_ => { });
            child.RegisterInstance(childTick, ServiceLifetime.Scoped);
            child.Seal();
            Assert.Same(rootTick, Assert.Single(rootFrames.Tickables));
            Assert.Same(childTick, Assert.Single(childFrames.Tickables));
            root.Dispose();
            Assert.True(rootFrames.IsDisposed);
            Assert.True(childFrames.IsDisposed);
        }

        [Fact]
        public void 帧内解析_不使当前快照失效()
        {
            using var services = new ServiceContainer();
            using var frames = new FrameSchedule(services);
            var tick = new TickProbe { OnTick = _ => services.Resolve<object>() };
            services.RegisterInstance(tick);
            services.RegisterFactory<object>(_ => new object());
            frames.SetOrder(typeof(TickProbe));
            services.Seal();
            frames.Tick(1);
            Assert.Equal(1, tick.Calls);
            Assert.Same(tick, Assert.Single(frames.Tickables));
        }

        [Fact]
        public void 驱动异常_保持FailFast_不吞异常继续下一项()
        {
            using var services = new ServiceContainer();
            using var frames = new FrameSchedule(services);
            services.RegisterInstance(new TickProbe { OnTick = _ => throw new InvalidOperationException("tick failed") });
            bool secondCalled = false;
            services.RegisterInstance(new SecondTick(_ => secondCalled = true));
            frames.SetOrder(typeof(TickProbe), typeof(SecondTick));
            services.Seal();
            Assert.Equal("tick failed", Assert.Throws<InvalidOperationException>(() => frames.Tick(1)).Message);
            Assert.False(secondCalled);
        }

        [Fact]
        public void 稳态Tick_不产生托管分配()
        {
            using var services = new ServiceContainer();
            using var frames = new FrameSchedule(services);
            var tick = new TickProbe();
            services.RegisterInstance(tick);
            frames.SetOrder(typeof(TickProbe));
            services.Seal();
            for (int i = 0; i < 64; i++) frames.Tick(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) frames.Tick(0.016f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(320, tick.Calls);
        }

        [Fact]
        public void 生产上下文_只创建一个帧调度器_随根作用域释放()
        {
            using var root = new ClientScope("Root");
            var context = new ClientContext(root);
            FrameSchedule frames = context.Require<FrameSchedule>();
            Assert.Same(frames, context.Services.Resolve<FrameSchedule>());
            var tick = new TickProbe();
            context.Put(tick);
            frames.SetOrder(typeof(TickProbe));
            context.Services.Seal();
            frames.Tick(1);
            Assert.Equal(1, tick.Calls);
            root.Dispose();
            Assert.True(context.Services.IsDisposed);
            Assert.True(frames.IsDisposed);
        }

        [Fact]
        public void 启动失败回滚_帧调度器也释放()
        {
            var segment = new FailingSegment();
            var host = new ClientHost().AddSegment(segment);
            Assert.Throws<InvalidOperationException>(() => host.InitializeAsync().GetAwaiter().GetResult());
            Assert.NotNull(segment.Frames);
            Assert.True(segment.Frames.IsDisposed);
            Assert.Empty(segment.Frames.Tickables);
        }

        private sealed class FailingSegment : IClientSegment
        {
            public string Name => "Failing";
            public FrameSchedule Frames { get; private set; }
            public Cysharp.Threading.Tasks.UniTask InitializeAsync(SegmentContext context, System.Threading.CancellationToken ct)
            {
                Frames = context.Require<FrameSchedule>();
                throw new InvalidOperationException("init failed");
            }
            public Cysharp.Threading.Tasks.UniTask ShutdownAsync(System.Threading.CancellationToken ct)
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        [Fact]
        public void 纯容器_不再依赖帧驱动类型或API()
        {
            string root = Directory.GetCurrentDirectory();
            while (!Directory.Exists(Path.Combine(root, "Packages", "com.litegame.framework"))) root = Directory.GetParent(root).FullName;
            string directory = Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Core", "DI");
            foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                Assert.DoesNotContain("ITickable", source);
                Assert.DoesNotContain("SetTickOrder", source);
                Assert.DoesNotContain("FrameSchedule", source);
            }
        }
    }
}
