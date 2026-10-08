using System;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class ServiceScopeContractTests
    {
        private sealed class RootService { }

        private sealed class ScopedService : IScoped, IDisposable
        {
            public int DisposeCount;
            public void Dispose() => DisposeCount++;
        }

        private sealed class TransientService : IDisposable
        {
            public int DisposeCount;
            public void Dispose() => DisposeCount++;
        }

        private sealed class ScopedConsumer
        {
            public ScopedConsumer(ScopedService service) { }
        }

        private interface IScoped { }

        [Fact]
        public void RootSingletonIsSharedIntoChildButChildServiceNeverLeaksBack()
        {
            using var root = new ServiceContainer();
            root.Register<RootService, RootService>();
            root.Seal();
            var child = (ServiceContainer)root.CreateScope("Account");
            child.Register<IScoped, ScopedService>(ServiceLifetime.Scoped);
            child.Seal();

            Assert.Same(root.Resolve<RootService>(), child.Resolve<RootService>());
            Assert.Same(child.Resolve<IScoped>(), child.Resolve<IScoped>());
            Assert.Throws<InvalidOperationException>(() => root.Resolve<IScoped>());
            child.Dispose();
            Assert.False(root.IsDisposed);
        }

        [Fact]
        public void ScopedAndTransientHaveDistinctLifetimeSemantics()
        {
            using var root = new ServiceContainer();
            root.Seal();
            var first = (ServiceContainer)root.CreateScope("Match-A");
            var second = (ServiceContainer)root.CreateScope("Match-B");
            first.Register<ScopedService, ScopedService>(ServiceLifetime.Scoped);
            first.RegisterFactory<TransientService>(_ => new TransientService(), ServiceLifetime.Transient);
            second.Register<ScopedService, ScopedService>(ServiceLifetime.Scoped);
            first.Seal();
            second.Seal();

            var scopedA = first.Resolve<ScopedService>();
            var scopedB = second.Resolve<ScopedService>();
            var transientA1 = first.Resolve<TransientService>();
            var transientA2 = first.Resolve<TransientService>();
            Assert.Same(scopedA, first.Resolve<ScopedService>());
            Assert.NotSame(scopedA, scopedB);
            Assert.NotSame(transientA1, transientA2);

            first.Dispose();
            Assert.Equal(1, scopedA.DisposeCount);
            Assert.Equal(1, transientA1.DisposeCount);
            Assert.Equal(1, transientA2.DisposeCount);
            Assert.Equal(0, scopedB.DisposeCount);
            second.Dispose();
            Assert.Equal(1, scopedB.DisposeCount);
        }

        [Fact]
        public void RootCannotRegisterScopedService()
        {
            using var root = new ServiceContainer();
            Assert.Contains("Root", Assert.Throws<InvalidOperationException>(() =>
                root.Register<ScopedService, ScopedService>(ServiceLifetime.Scoped)).Message);
        }

        [Fact]
        public void SingletonCannotCaptureScopedServiceInChildScope()
        {
            using var root = new ServiceContainer();
            root.Seal();
            var child = (ServiceContainer)root.CreateScope("Match");
            child.Register<ScopedService, ScopedService>(ServiceLifetime.Scoped);
            child.Register<ScopedConsumer, ScopedConsumer>(ServiceLifetime.Singleton);
            var error = Assert.Throws<InvalidOperationException>(() => child.Seal());
            Assert.Contains("生命周期越域捕获", error.Message);
            Assert.False(child.IsSealed);
            child.Dispose();
        }

        [Fact]
        public void ChildCanUseParentAfterParentSealButCannotMutateParent()
        {
            using var root = new ServiceContainer();
            root.Register<RootService, RootService>();
            root.Seal();
            var child = (ServiceContainer)root.CreateScope("UI");
            Assert.NotNull(child.Resolve<RootService>());
            Assert.Throws<InvalidOperationException>(() => child.Register<RootService, RootService>());
            child.RegisterInstance(new ScopedService(), ServiceLifetime.Scoped, ServiceOwnership.ContainerOwned);
            child.Seal();
            child.Dispose();
        }

        [Fact]
        public void ParentDisposeClosesAllChildrenAndChildDisposeIsIdempotent()
        {
            var root = new ServiceContainer();
            root.Seal();
            var child = (ServiceContainer)root.CreateScope("Account");
            child.Register<ScopedService, ScopedService>(ServiceLifetime.Scoped);
            child.Seal();
            var service = child.Resolve<ScopedService>();
            root.Dispose();
            child.Dispose();
            Assert.True(child.IsDisposed);
            Assert.Equal(1, service.DisposeCount);
        }

        [Fact]
        public void ScopePreflightResolvesParentDependencyWithoutConstructingIt()
        {
            using var root = new ServiceContainer();
            root.Register<RootService, RootService>();
            root.Seal();
            var child = (ServiceContainer)root.CreateScope("Match");
            child.Register<ScopedConsumer, ScopedConsumer>(ServiceLifetime.Scoped);
            child.Register<ScopedService, ScopedService>(ServiceLifetime.Scoped);
            child.Seal();
            Assert.False(root.TryGetInstance<RootService>(out _));
            Assert.NotNull(child.Resolve<ScopedConsumer>());
            Assert.True(root.TryGetInstance<RootService>(out _) == false);
            child.Dispose();
        }

        [Fact]
        public void TransientTickableIsRejectedBeforeItCanEnterDriverList()
        {
            using var root = new ServiceContainer();
            root.Seal();
            var child = (ServiceContainer)root.CreateScope("Match");
            child.Register<TransientTick, TransientTick>(ServiceLifetime.Transient);
            Assert.Contains("Transient", Assert.Throws<InvalidOperationException>(() => child.SetTickOrder(typeof(TransientTick))).Message);
            Assert.Contains("Transient", Assert.Throws<InvalidOperationException>(() => child.Seal()).Message);
            child.Dispose();
        }

        private sealed class TransientTick : ITickable
        {
            public void Tick(float realDelta) { }
        }
    }
}
