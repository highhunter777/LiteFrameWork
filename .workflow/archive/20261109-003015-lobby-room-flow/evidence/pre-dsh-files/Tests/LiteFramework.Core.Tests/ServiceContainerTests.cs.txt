using System;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class ServiceContainerTests   // 实例容器，无静态状态；EnableRegistrationTrace 默认 false 不触 Log
    {
        private interface IAlpha { }
        private interface IBeta { }
        private sealed class Alpha : IAlpha
        {
            public readonly IBeta Beta;
            public Alpha(IBeta beta) => Beta = beta ?? throw new ArgumentNullException(nameof(beta));
        }
        private sealed class Beta : IBeta
        {
            public readonly IGamma Gamma;
            public Beta(IGamma gamma) => Gamma = gamma ?? throw new ArgumentNullException(nameof(gamma));
        }
        private interface IGamma { }
        private sealed class Gamma : IGamma { }

        private sealed class LoopA { public LoopA(LoopB b) { } }
        private sealed class LoopB { public LoopB(LoopA a) { } }

        private sealed class Missing { public Missing(IDep dep) { } }
        private interface IDep { }

        private sealed class Boom : IBoom { public Boom() => throw new InvalidOperationException("boom-cause"); }
        private interface IBoom { }

        [Fact]
        public void DI_链式注入_A依赖B依赖C_全通()
        {
            var c = new ServiceContainer();
            c.Register<IAlpha, Alpha>();
            c.Register<IBeta, Beta>();
            c.Register<IGamma, Gamma>();

            var alpha = c.Resolve<IAlpha>();
            Assert.IsAssignableFrom<Alpha>(alpha);
            Assert.IsAssignableFrom<Beta>(((Alpha)alpha).Beta);
        }

        [Fact]
        public void DI_单例_两次Resolve同实例()
        {
            var c = new ServiceContainer();
            c.Register<IGamma, Gamma>();
            Assert.Same(c.Resolve<IGamma>(), c.Resolve<IGamma>());
        }

        [Fact]
        public void DI_重复注册_注册期当场抛()
        {
            var c = new ServiceContainer();
            c.Register<IGamma, Gamma>();
            var ex = Assert.Throws<InvalidOperationException>(() => c.Register<IGamma, Gamma>());
            Assert.Contains("重复注册", ex.Message);
        }

        [Fact]
        public void DI_循环依赖_Resolve抛且信息含环()
        {
            var c = new ServiceContainer();
            c.Register<LoopA, LoopA>();
            c.Register<LoopB, LoopB>();
            var ex = Assert.Throws<InvalidOperationException>(() => c.Resolve<LoopA>());
            Assert.Contains("循环依赖", ex.Message);
            Assert.Contains("LoopA", ex.Message);
        }

        [Fact]
        public void DI_构造依赖未注册_Resolve当场抛()
        {
            var c = new ServiceContainer();
            c.Register<Missing, Missing>();
            var ex = Assert.Throws<InvalidOperationException>(() => c.Resolve<Missing>());
            Assert.Contains("未注册", ex.Message);
        }

        [Fact]
        public void DI_构造抛异常_Resolve报真因()
        {
            var c = new ServiceContainer();
            c.Register<IBoom, Boom>();
            var ex = Assert.Throws<InvalidOperationException>(() => c.Resolve<IBoom>());
            Assert.Contains("构造抛异常", ex.Message);
            Assert.Contains("boom-cause", ex.Message);   // 解包 TargetInvocationException，报真因
        }

        [Fact]
        public void DI_RegisterFactory_首次Resolve调用一次_单例缓存()
        {
            var c = new ServiceContainer();
            int calls = 0;
            c.RegisterFactory<IGamma>(_ => { calls++; return new Gamma(); });

            var first = c.Resolve<IGamma>();
            var second = c.Resolve<IGamma>();
            Assert.Same(first, second);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void DI_Seal后_一切注册抛_Resolve不受限()
        {
            var c = new ServiceContainer();
            c.Register<IGamma, Gamma>();
            c.Seal();

            Assert.Throws<InvalidOperationException>(() => c.Register<IGamma, Gamma>());
            Assert.Throws<InvalidOperationException>(() => c.RegisterInstance<IGamma>(new Gamma()));
            Assert.Throws<InvalidOperationException>(() => c.RegisterFactory<IGamma>(_ => new Gamma()));
            Assert.NotNull(c.Resolve<IGamma>());   // Resolve 不受限
        }

        [Fact]
        public void DI_未注册Resolve_报错信息指向装配点()
        {
            var c = new ServiceContainer();
            var ex = Assert.Throws<InvalidOperationException>(() => c.Resolve<IGamma>());
            Assert.Contains("未注册", ex.Message);
        }

        [Fact]
        public void 帧调度_注册即发现_ITickable自动入列_多接口不双Tick()
        {
            var c = new ServiceContainer();
            using var frames = new FrameSchedule(c);
            var tickable = new DualInterface();
            c.RegisterInstance<DualInterface>(tickable);
            c.RegisterInstance<IThing>(tickable);      // 同一实例注册在两个接口下

            Assert.Single(frames.Tickables);                // Contains 守卫：不双收
        }

        private interface IThing { }
        private sealed class DualInterface : IThing, ITickable
        {
            public void Tick(float realDelta) { }
        }
    }
}
