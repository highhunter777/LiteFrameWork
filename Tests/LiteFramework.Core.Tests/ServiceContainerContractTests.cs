using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class ServiceContainerContractTests
    {
        private sealed class Probe
        {
            public int Constructions;
            public readonly List<string> Releases = new();
        }

        private interface ILeaf { }
        private sealed class Leaf : ILeaf, IDisposable
        {
            private readonly Probe _probe;
            public int DisposeCount;
            public Leaf(Probe probe) { _probe = probe; probe.Constructions++; }
            public void Dispose() { DisposeCount++; _probe.Releases.Add("leaf"); }
        }

        private sealed class Consumer : IDisposable
        {
            public readonly Leaf Leaf;
            private readonly Probe _probe;
            public Consumer(Leaf leaf, Probe probe) { Leaf = leaf; _probe = probe; probe.Constructions++; }
            public void Dispose() => _probe.Releases.Add("consumer");
        }

        private sealed class BrokenConsumer
        {
            public BrokenConsumer(Leaf leaf) => throw new InvalidOperationException("constructor failed");
        }

        private sealed class LoopA { public LoopA(LoopB dependency) { } }
        private sealed class LoopB { public LoopB(LoopA dependency) { } }
        private abstract class AbstractService { }
        private sealed class PrivateService { private PrivateService() { } }
        private sealed class AmbiguousService
        {
            public AmbiguousService(Leaf leaf) { }
            public AmbiguousService(Probe probe) { }
        }

        private sealed class EqualityDisposer : IDisposable
        {
            public int Count;
            public void Dispose() => Count++;
            public override bool Equals(object other) => other is EqualityDisposer;
            public override int GetHashCode() => 1;
        }

        private sealed class ThrowingDisposer : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("dispose failed");
        }

        private sealed class TickLeaf : ITickable { public void Tick(float delta) { } }
        private sealed class TickConsumer : ITickable
        {
            public TickConsumer(TickLeaf dependency) { }
            public void Tick(float delta) { }
        }

        private sealed class UnknownTick : ITickable, IDisposable
        {
            public int DisposeCount;
            public void Tick(float delta) { }
            public void Dispose() => DisposeCount++;
        }

        [Fact]
        public void PreflightAndSealDoNotRunConstructorsOrFactories()
        {
            using var container = new ServiceContainer();
            var probe = new Probe();
            int factoryCalls = 0;
            container.RegisterInstance(probe);
            container.Register<Consumer, Consumer>();
            container.Register<Leaf, Leaf>();
            container.RegisterFactory<ILeaf>(c => { factoryCalls++; return c.Resolve<Leaf>(); }, typeof(Leaf));
            container.Validate();
            container.Seal();
            Assert.Equal(0, probe.Constructions);
            Assert.Equal(0, factoryCalls);
            Assert.False(container.TryGetInstance<Consumer>(out _));
        }

        [Fact]
        public void PreflightReportsFullMissingDependencyChain()
        {
            using var container = new ServiceContainer();
            container.Register<Consumer, Consumer>();
            container.Register<Leaf, Leaf>();
            var error = Assert.Throws<InvalidOperationException>(() => container.Validate());
            Assert.Contains("Consumer → Leaf → Probe", error.Message);
        }

        [Fact]
        public void PreflightFindsCyclesAcrossFactoriesAndConstructors()
        {
            using var container = new ServiceContainer();
            container.Register<LoopA, LoopA>();
            container.RegisterFactory<LoopB>(c => new LoopB(c.Resolve<LoopA>()), typeof(LoopA));
            var error = Assert.Throws<InvalidOperationException>(() => container.Seal());
            Assert.Contains("LoopA → LoopB → LoopA", error.Message);
            Assert.False(container.IsSealed);
        }

        [Fact]
        public void PreflightRejectsAbstractPrivateAndAmbiguousConstructors()
        {
            using var abstractContainer = new ServiceContainer();
            abstractContainer.Register<AbstractService, AbstractService>();
            Assert.Contains("不能构造", Assert.Throws<InvalidOperationException>(() => abstractContainer.Validate()).Message);
            using var privateContainer = new ServiceContainer();
            privateContainer.Register<PrivateService, PrivateService>();
            Assert.Contains("无公共构造", Assert.Throws<InvalidOperationException>(() => privateContainer.Validate()).Message);
            using var ambiguousContainer = new ServiceContainer();
            ambiguousContainer.Register<AmbiguousService, AmbiguousService>();
            Assert.Contains("歧义", Assert.Throws<InvalidOperationException>(() => ambiguousContainer.Validate()).Message);
        }

        [Fact]
        public void FailedSealAllowsMissingRegistrationToBeRepaired()
        {
            using var container = new ServiceContainer();
            container.Register<Leaf, Leaf>();
            Assert.Throws<InvalidOperationException>(() => container.Seal());
            container.RegisterInstance(new Probe());
            container.Seal();
            Assert.NotNull(container.Resolve<Leaf>());
        }

        [Fact]
        public void FactoryDependenciesAreDeclaredAndDefensivelyCopied()
        {
            using var container = new ServiceContainer();
            var dependencies = new[] { typeof(Leaf) };
            container.RegisterInstance(new Probe());
            container.Register<Leaf, Leaf>();
            container.RegisterFactory<ILeaf>(c => c.Resolve<Leaf>(), dependencies);
            dependencies[0] = typeof(LoopA);
            container.Seal();
            Assert.Same(container.Resolve<Leaf>(), container.Resolve<ILeaf>());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void FactoryCannotHideDependenciesThroughResolutionOrInstanceQueries(int mode)
        {
            using var container = new ServiceContainer();
            container.RegisterInstance<ILeaf>(new Leaf(new Probe()));
            container.RegisterFactory<object>(c =>
            {
                if (mode == 0) return c.Resolve<ILeaf>();
                if (mode == 1) { c.TryResolve<ILeaf>(out var resolved); return resolved; }
                c.TryGetInstance<ILeaf>(out var existing);
                return existing;
            });
            container.Seal();
            Assert.Contains("未声明", Assert.Throws<InvalidOperationException>(() => container.Resolve<object>()).Message);
        }

        [Fact]
        public void FactoryNullIsRejectedWithoutCachingSuccess()
        {
            using var container = new ServiceContainer();
            int calls = 0;
            container.RegisterFactory<ILeaf>(_ => { calls++; return null; });
            Assert.Throws<InvalidOperationException>(() => container.Resolve<ILeaf>());
            Assert.Throws<InvalidOperationException>(() => container.Resolve<ILeaf>());
            Assert.Equal(2, calls);
        }

        [Fact]
        public void FactoryFailureDoesNotLeaveCircularResolutionState()
        {
            using var container = new ServiceContainer();
            int calls = 0;
            var leaf = new Leaf(new Probe());
            container.RegisterFactory<ILeaf>(_ =>
            {
                if (++calls < 2) throw new InvalidOperationException("factory failed");
                return leaf;
            });
            Assert.Throws<InvalidOperationException>(() => container.Resolve<ILeaf>());
            Assert.Same(leaf, container.Resolve<ILeaf>());
        }

        [Fact]
        public void BorrowedInstanceRemainsOwnedByItsModule()
        {
            var leaf = new Leaf(new Probe());
            var container = new ServiceContainer();
            container.RegisterInstance(leaf);
            container.Dispose();
            Assert.Equal(0, leaf.DisposeCount);
            leaf.Dispose();
            Assert.Equal(1, leaf.DisposeCount);
        }

        [Fact]
        public void ConstructorOwnedGraphReleasesConsumersBeforeDependencies()
        {
            var container = new ServiceContainer();
            var probe = new Probe();
            container.RegisterInstance(probe);
            container.Register<Consumer, Consumer>();
            container.Register<Leaf, Leaf>();
            container.Resolve<Consumer>();
            container.Dispose();
            container.Dispose();
            Assert.Equal(new[] { "consumer", "leaf" }, probe.Releases);
        }

        [Fact]
        public void FailedConsumerConstructionStillReleasesConstructedDependencies()
        {
            using var container = new ServiceContainer();
            var probe = new Probe();
            container.RegisterInstance(probe);
            container.Register<Leaf, Leaf>();
            container.Register<BrokenConsumer, BrokenConsumer>();
            Assert.Throws<InvalidOperationException>(() => container.Resolve<BrokenConsumer>());
            container.Dispose();
            Assert.Equal(new[] { "leaf" }, probe.Releases);
        }

        [Fact]
        public void OwnershipAndAliasesUseReferenceIdentityRatherThanEquals()
        {
            var first = new EqualityDisposer();
            var second = new EqualityDisposer();
            using var container = new ServiceContainer();
            container.RegisterInstance(first, ServiceOwnership.ContainerOwned);
            container.RegisterInstance<IDisposable>(second, ServiceOwnership.ContainerOwned);
            container.RegisterInstance<object>(first, ServiceOwnership.ContainerOwned);
            container.Dispose();
            Assert.Equal(1, first.Count);
            Assert.Equal(1, second.Count);
        }

        [Fact]
        public void FactoryAliasesOfConstructedServiceDoNotReleaseTwice()
        {
            using var container = new ServiceContainer();
            container.RegisterInstance(new Probe());
            container.Register<Leaf, Leaf>();
            container.RegisterFactory<ILeaf>(c => c.Resolve<Leaf>(), typeof(Leaf));
            var leaf = container.Resolve<ILeaf>() as Leaf;
            container.Dispose();
            Assert.Equal(1, leaf.DisposeCount);
        }

        [Fact]
        public void DisposeContinuesAfterFailureAndExposesDiagnostics()
        {
            using var container = new ServiceContainer();
            var leaf = new Leaf(new Probe());
            container.RegisterInstance(leaf, ServiceOwnership.ContainerOwned);
            container.RegisterInstance(new ThrowingDisposer(), ServiceOwnership.ContainerOwned);
            container.Dispose();
            Assert.Equal(1, leaf.DisposeCount);
            Assert.Single(container.DisposeFailures);
            Assert.Empty(container.Tickables);
            Assert.Empty(container.Stats);
        }

        [Fact]
        public void ParentScopeReleasesServicesAndChildDisposeDoesNotReleaseParent()
        {
            using var root = new ClientScope("Root");
            var first = new ServiceContainer(root);
            var second = new ServiceContainer(root);
            var firstLeaf = new Leaf(new Probe());
            var secondLeaf = new Leaf(new Probe());
            first.RegisterInstance(firstLeaf, ServiceOwnership.ContainerOwned);
            second.RegisterInstance(secondLeaf, ServiceOwnership.ContainerOwned);
            first.Dispose();
            Assert.False(root.IsDisposed);
            Assert.Equal(0, secondLeaf.DisposeCount);
            root.Dispose();
            Assert.Equal(1, firstLeaf.DisposeCount);
            Assert.Equal(1, secondLeaf.DisposeCount);
        }

        [Fact]
        public void DisposedContainerRejectsAllAssemblyAndResolutionOperations()
        {
            var container = new ServiceContainer();
            container.Dispose();
            Assert.Throws<ObjectDisposedException>(() => container.Resolve<Probe>());
            Assert.Throws<ObjectDisposedException>(() => container.TryResolve<Probe>(out _));
            Assert.Throws<ObjectDisposedException>(() => container.TryGetInstance<Probe>(out _));
            Assert.Throws<ObjectDisposedException>(() => container.Register<Probe, Probe>());
            Assert.Throws<ObjectDisposedException>(() => container.RegisterInstance(new Probe()));
            Assert.Throws<ObjectDisposedException>(() => container.RegisterFactory<Probe>(_ => new Probe()));
            Assert.Throws<ObjectDisposedException>(() => container.SetTickOrder());
            Assert.Throws<ObjectDisposedException>(() => container.Validate());
            Assert.Throws<ObjectDisposedException>(() => container.Seal());
        }

        [Fact]
        public void DisposingUnusedRegistrationDoesNotConstructIt()
        {
            var probe = new Probe();
            using var container = new ServiceContainer();
            container.RegisterInstance(probe);
            container.Register<Leaf, Leaf>();
            container.Dispose();
            Assert.Equal(0, probe.Constructions);
            Assert.Empty(probe.Releases);
        }

        [Fact]
        public void ExplicitTickPlanSurvivesReverseConstructionOrder()
        {
            using var container = new ServiceContainer();
            container.Register<TickConsumer, TickConsumer>();
            container.Register<TickLeaf, TickLeaf>();
            var plan = new[] { typeof(TickConsumer), typeof(TickLeaf) };
            container.SetTickOrder(plan);
            plan[0] = typeof(Probe);
            container.Seal();
            container.Resolve<TickLeaf>();
            var consumer = container.Resolve<TickConsumer>();
            Assert.Same(consumer, container.Tickables[0]);
            Assert.IsType<TickLeaf>(container.Tickables[1]);
        }

        [Fact]
        public void AutomaticTickOrderFollowsRegistrationRatherThanDependencyResolution()
        {
            using var container = new ServiceContainer();
            container.Register<TickConsumer, TickConsumer>();
            container.Register<TickLeaf, TickLeaf>();
            container.Resolve<TickConsumer>();
            Assert.IsType<TickConsumer>(container.Tickables[0]);
            Assert.IsType<TickLeaf>(container.Tickables[1]);
        }

        [Fact]
        public void TickPlanAcceptsOneAliasAndDeduplicatesTwoAliases()
        {
            using var container = new ServiceContainer();
            var tick = new TickLeaf();
            container.RegisterInstance(tick);
            container.RegisterInstance<ITickable>(tick);
            container.SetTickOrder(typeof(ITickable));
            Assert.Single(container.Tickables);
            container.SetTickOrder(typeof(ITickable), typeof(TickLeaf));
            Assert.Single(container.Tickables);
        }

        [Fact]
        public void InvalidTickPlanDoesNotReplaceValidPlan()
        {
            using var container = new ServiceContainer();
            container.RegisterInstance(new TickLeaf());
            container.RegisterInstance(new Probe());
            container.SetTickOrder(typeof(TickLeaf));
            Assert.Throws<InvalidOperationException>(() => container.SetTickOrder(typeof(TickLeaf), typeof(TickLeaf)));
            Assert.Throws<InvalidOperationException>(() => container.SetTickOrder(typeof(LoopA)));
            Assert.Throws<InvalidOperationException>(() => container.SetTickOrder(typeof(Probe)));
            Assert.Throws<InvalidOperationException>(() => container.SetTickOrder());
            Assert.Single(container.Tickables);
            container.Seal();
            Assert.Throws<InvalidOperationException>(() => container.SetTickOrder(typeof(TickLeaf)));
        }

        [Fact]
        public void UnexpectedFactoryTickableIsRejectedAndOwnedForRollback()
        {
            using var container = new ServiceContainer();
            var tick = new UnknownTick();
            container.RegisterFactory<object>(_ => tick);
            container.SetTickOrder();
            container.Seal();
            Assert.Contains("未编排", Assert.Throws<InvalidOperationException>(() => container.Resolve<object>()).Message);
            Assert.False(container.TryGetInstance<object>(out _));
            container.Dispose();
            Assert.Equal(1, tick.DisposeCount);
        }

        [Fact]
        public void LazyResolutionDoesNotInvalidateCurrentFrameEnumeration()
        {
            using var container = new ServiceContainer();
            container.RegisterInstance(new TickLeaf());
            container.Register<Probe, Probe>();
            container.SetTickOrder(typeof(TickLeaf));
            container.Seal();
            int visited = 0;
            foreach (ITickable tick in container.Tickables)
            {
                container.Resolve<Probe>();
                tick.Tick(0);
                visited++;
            }
            Assert.Equal(1, visited);
        }

        [Fact]
        public void FactoryThatDisposesContainerCannotPublishNewInstance()
        {
            var container = new ServiceContainer();
            var leaf = new Leaf(new Probe());
            container.RegisterFactory<ILeaf>(_ => { container.Dispose(); return leaf; });
            Assert.Throws<ObjectDisposedException>(() => container.Resolve<ILeaf>());
            Assert.Equal(1, leaf.DisposeCount);
        }
    }
}
