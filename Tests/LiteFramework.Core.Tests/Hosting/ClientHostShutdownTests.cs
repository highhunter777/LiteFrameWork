using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]
    public sealed class ClientHostShutdownTests
    {
        private sealed class FlagResource : IDisposable
        {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        private sealed class ScopeModule : IClientSegment
        {
            public string Name { get; }
            public FlagResource Resource { get; } = new FlagResource();
            public int ShutdownCount;

            public ScopeModule(string name) => Name = name;

            public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
            {
                context.Scope.Register(Resource);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                ShutdownCount++;
                return UniTask.CompletedTask;
            }
        }

        private static void Pump(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            awaiter.GetResult();
        }

        [Fact]
        public void 强制释放_释放组合根模块与根Scope_不调用模块关闭()
        {
            var host = new ClientHost();
            var module = new ScopeModule("A");
            var compose = new FlagResource();
            var root = new FlagResource();
            host.AddSegment(module);
            host.ComposeRoot(context =>
            {
                context.Scope.Register(compose);
                return UniTask.CompletedTask;
            });

            Pump(host.InitializeAsync());
            host.RootScope.Register(root);
            host.ForceReleaseRoot();

            Assert.True(module.Resource.Disposed, "模块 Scope 资源必须释放");
            Assert.True(compose.Disposed, "组合根 Scope 资源必须释放");
            Assert.True(root.Disposed, "根 Scope 资源必须释放");
            Assert.Equal(0, module.ShutdownCount);              // 强制路径不跑优雅关闭
            Assert.Equal(HostState.Closed, host.State);
            Assert.Empty(host.ShutdownFailures);
        }

        [Fact]
        public void 强制释放后_再调Shutdown_立即返回不重复()
        {
            var host = new ClientHost();
            host.AddSegment(new ScopeModule("A"));
            Pump(host.InitializeAsync());
            host.ForceReleaseRoot();

            Pump(host.ShutdownAsync());                         // 已强制收尾：等待已完成的信号
            Assert.Equal(HostState.Closed, host.State);
        }

        [Fact]
        public void 平台订阅者异常_进PlatformFailures_不污染ShutdownFailures()
        {
            var host = new ClientHost();
            host.SubscribePause(_ => throw new InvalidOperationException("平台订阅者故障"));

            host.RaiseApplicationPause(true);

            Assert.Single(host.PlatformFailures);
            Assert.Empty(host.ShutdownFailures);
        }
    }
}
