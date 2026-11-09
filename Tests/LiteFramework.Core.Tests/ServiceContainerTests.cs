using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class ServiceContainerTests
    {
        private interface IAlpha { }

        private sealed class Alpha : IAlpha, IDisposable
        {
            public int DisposeCount;
            public void Dispose() => DisposeCount++;
        }

        private sealed class Leaf : IDisposable
        {
            public int DisposeCount;
            public void Dispose() => DisposeCount++;
        }

        private sealed class ThrowingDisposer : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("dispose failed");
        }

        private sealed class EqualityDisposer : IDisposable
        {
            public int Count;
            public void Dispose() => Count++;
            public override bool Equals(object other) => other is EqualityDisposer;
            public override int GetHashCode() => 1;
        }

        private sealed class StatsProbe : IModuleStats
        {
            public string StatsName => "probe";
            public void Snapshot(Dictionary<string, string> into) => into["x"] = "1";
        }

        [Fact]
        public void 注册实例_解析同引用_TryResolve命中()
        {
            using var container = new ServiceContainer();
            var alpha = new Alpha();
            container.RegisterInstance<IAlpha>(alpha);

            Assert.Same(alpha, container.Resolve<IAlpha>());
            Assert.True(container.TryResolve<IAlpha>(out var resolved));
            Assert.Same(alpha, resolved);
        }

        [Fact]
        public void 未注册_Resolve抛_TryResolve返回false()
        {
            using var container = new ServiceContainer();

            Assert.Contains("未注册", Assert.Throws<InvalidOperationException>(() => container.Resolve<IAlpha>()).Message);
            Assert.False(container.TryResolve<IAlpha>(out _));
        }

        [Fact]
        public void 重复注册_注册期当场抛()
        {
            using var container = new ServiceContainer();
            container.RegisterInstance<IAlpha>(new Alpha());

            var error = Assert.Throws<InvalidOperationException>(() => container.RegisterInstance<IAlpha>(new Alpha()));
            Assert.Contains("重复注册", error.Message);
        }

        [Fact]
        public void 空实例与非法所有权_当场抛()
        {
            using var container = new ServiceContainer();

            Assert.Throws<ArgumentNullException>(() => container.RegisterInstance<IAlpha>(null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => container.RegisterInstance(new Alpha(), (ServiceOwnership)99));
        }

        [Fact]
        public void Seal后_注册抛_解析不受限_重复Seal抛()
        {
            using var container = new ServiceContainer();
            var alpha = new Alpha();
            container.RegisterInstance<IAlpha>(alpha);
            container.Seal();

            Assert.True(container.IsSealed);
            Assert.Same(alpha, container.Resolve<IAlpha>());
            Assert.Throws<InvalidOperationException>(() => container.RegisterInstance<IAlpha>(new Alpha()));
            Assert.Throws<InvalidOperationException>(() => container.Seal());
        }

        [Fact]
        public void Borrowed不随容器释放_ContainerOwned释放恰一次()
        {
            var borrowed = new Alpha();
            var owned = new Leaf();
            var container = new ServiceContainer();
            container.RegisterInstance<IAlpha>(borrowed);
            container.RegisterInstance(owned, ServiceOwnership.ContainerOwned);

            container.Dispose();
            container.Dispose();

            Assert.Equal(0, borrowed.DisposeCount);
            Assert.Equal(1, owned.DisposeCount);
            Assert.True(container.IsDisposed);
        }

        [Fact]
        public void 别名按引用去重_Equals不参与()
        {
            var first = new EqualityDisposer();
            var second = new EqualityDisposer();
            using var container = new ServiceContainer();
            container.RegisterInstance(first, ServiceOwnership.ContainerOwned);
            container.RegisterInstance<IDisposable>(first, ServiceOwnership.ContainerOwned);
            container.RegisterInstance<object>(second, ServiceOwnership.ContainerOwned);

            container.Dispose();

            Assert.Equal(1, first.Count);
            Assert.Equal(1, second.Count);
        }

        [Fact]
        public void 释放单项失败_继续释放并聚合诊断()
        {
            var leaf = new Leaf();
            var container = new ServiceContainer();
            container.RegisterInstance(leaf, ServiceOwnership.ContainerOwned);
            container.RegisterInstance(new ThrowingDisposer(), ServiceOwnership.ContainerOwned);

            container.Dispose();

            Assert.Equal(1, leaf.DisposeCount);
            Assert.Single(container.DisposeFailures);
            Assert.Empty(container.Stats);
        }

        [Fact]
        public void 父作用域释放容器_容器释放不反向释放作用域()
        {
            using var root = new ClientScope("Root");
            var first = new ServiceContainer(root);
            var second = new ServiceContainer(root);
            var firstLeaf = new Leaf();
            var secondLeaf = new Leaf();
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
        public void 释放后_一切注册与解析抛ObjectDisposed()
        {
            var container = new ServiceContainer();
            container.Dispose();

            Assert.Throws<ObjectDisposedException>(() => container.Resolve<IAlpha>());
            Assert.Throws<ObjectDisposedException>(() => container.TryResolve<IAlpha>(out _));
            Assert.Throws<ObjectDisposedException>(() => container.RegisterInstance(new Alpha()));
            Assert.Throws<ObjectDisposedException>(() => container.Seal());
        }

        [Fact]
        public void 统计只收已登记IModuleStats_快照清空后填()
        {
            using var container = new ServiceContainer();
            var probe = new StatsProbe();
            container.RegisterInstance(probe);

            Assert.Same(probe, Assert.Single(container.Stats));
            Assert.Equal("DI", container.StatsName);

            var snapshot = new Dictionary<string, string> { ["stale"] = "1" };
            container.Snapshot(snapshot);
            Assert.False(snapshot.ContainsKey("stale"));
            Assert.Equal("1", snapshot["注册数"]);
            Assert.Equal("否", snapshot["已密封"]);
            Assert.Equal("否", snapshot["已释放"]);
        }

        [Fact]
        public void 释放后统计清空_释放失败计数保留()
        {
            var container = new ServiceContainer();
            container.RegisterInstance(new ThrowingDisposer(), ServiceOwnership.ContainerOwned);
            container.Dispose();

            Assert.Empty(container.Stats);
            Assert.Single(container.DisposeFailures);
        }
    }
}
