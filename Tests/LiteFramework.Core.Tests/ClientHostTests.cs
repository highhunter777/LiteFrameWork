using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 客户端生命周期用例（《商业级通用客户端框架总设计》§6.1/§6.2 + G1 退出条件"启动中断进确定错误态"）：
    /// 模块按序初始化；失败只回滚已成功者；逆序关闭且单模块失败不阻断；取消传播；Scope LIFO 与隔离。
    /// 全部纯逻辑（fake 模块 + 手工推进），无真实时钟/Sleep。
    /// </summary>
    public sealed class ClientHostTests
    {
        /// <summary>记录调用轨迹的 fake 模块：init/shutdown 顺序与失败注入点可控。</summary>
        private sealed class FakeModule : IClientModule
        {
            private readonly List<string> _trace;
            private readonly Exception _initFailure;
            private readonly Exception _shutdownFailure;
            private readonly int _delayFrames;                 // 模拟有界异步（驱动续延推进）

            public string Name { get; }
            public int InitCount;
            public int ShutdownCount;

            public FakeModule(string name, List<string> trace, Exception initFailure = null,
                Exception shutdownFailure = null, int delayFrames = 0)
            {
                Name = name;
                _trace = trace;
                _initFailure = initFailure;
                _shutdownFailure = shutdownFailure;
                _delayFrames = delayFrames;
            }

            public async UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _trace.Add("init:" + Name);
                for (int i = 0; i < _delayFrames; i++)
                {
                    await UniTask.Yield();
                    ct.ThrowIfCancellationRequested();
                }
                InitCount++;
                if (_initFailure != null) throw _initFailure;
            }

            public async UniTask ShutdownAsync(CancellationToken ct)
            {
                _trace.Add("shutdown:" + Name);
                for (int i = 0; i < _delayFrames; i++)
                {
                    await UniTask.Yield();
                    ct.ThrowIfCancellationRequested();
                }
                ShutdownCount++;
                if (_shutdownFailure != null) throw _shutdownFailure;
            }
        }

        /// <summary>同步泵：把 UniTask 驱动到完成（测试内无 PlayerLoop——Host 契约要求关闭路径可被显式驱动）。</summary>
        private static void Pump(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            awaiter.GetResult();
        }

        private static ClientHost Host(params IClientModule[] modules)
        {
            var host = new ClientHost();
            foreach (var m in modules) host.AddModule(m);
            return host;
        }

        [Fact]
        public void 初始化_按注册顺序执行_成功后进入运行态()
        {
            var trace = new List<string>();
            var host = Host(new FakeModule("A", trace), new FakeModule("B", trace), new FakeModule("C", trace));

            Pump(host.InitializeAsync());

            Assert.Equal(new[] { "init:A", "init:B", "init:C" }, trace.ToArray());
            Assert.Equal(2, host.State);                       // 运行态
            Assert.NotNull(host.RootScope);
        }

        [Fact]
        public void 初始化失败_只逆序关闭已成功模块_后续不再初始化_原异常上抛()
        {
            var trace = new List<string>();
            var boom = new InvalidOperationException("B 失败");
            var host = Host(
                new FakeModule("A", trace),
                new FakeModule("B", trace, initFailure: boom),
                new FakeModule("C", trace));

            var ex = Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));
            Assert.Same(boom, ex);                             // 原异常上抛（回滚不吞不换装）

            Assert.Equal(new[] { "init:A", "init:B", "shutdown:A" }, trace.ToArray());   // 只回滚已成功者
            Assert.Equal(4, host.State);                       // 确定关闭终态
            Assert.NotNull(host.RootScope);                    // Scope 引用保留（已释放）
            Assert.True(host.RootScope.IsDisposed);
        }

        [Fact]
        public void 关闭_逆序执行_单模块失败不阻断其余_异常聚合()
        {
            var trace = new List<string>();
            var bad = new FakeModule("B", trace, shutdownFailure: new InvalidOperationException("B 关闭炸"));
            var host = Host(new FakeModule("A", trace), bad, new FakeModule("C", trace));

            Pump(host.InitializeAsync());
            Pump(host.ShutdownAsync());

            Assert.Equal(new[] { "init:A", "init:B", "init:C", "shutdown:C", "shutdown:B", "shutdown:A" }, trace.ToArray());
            Assert.Equal(1, bad.ShutdownCount);                // B 抛异常后自身关闭流程仍完成
            Assert.Single(host.ShutdownFailures);              // 聚合上报（不抛出）
            Assert.Equal(4, host.State);
        }

        [Fact]
        public void 关闭幂等_重复调用收敛为一次()
        {
            var trace = new List<string>();
            var host = Host(new FakeModule("A", trace));

            Pump(host.InitializeAsync());
            Pump(host.ShutdownAsync());
            Pump(host.ShutdownAsync());                        // 重复关闭
            Pump(host.ShutdownAsync());

            Assert.Equal(new[] { "init:A", "shutdown:A" }, trace.ToArray());
        }

        [Fact]
        public void 取消传播_模块初始化收到ct取消即穿透回滚()
        {
            var trace = new List<string>();
            var cts = new CancellationTokenSource();
            var host = Host(new FakeModule("A", trace), new CancellingModule(trace, cts), new FakeModule("C", trace));

            // B 初始化时取消根令牌 → OperationCanceledException 穿透 → A 回滚 → 原取消上抛
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(host.InitializeAsync(cts.Token)));
            Assert.Equal(new[] { "init:A", "init:B", "shutdown:A" }, trace.ToArray());
        }

        private sealed class CancellingModule : IClientModule
        {
            private readonly List<string> _trace;
            private readonly CancellationTokenSource _cts;
            public string Name => "B";

            public CancellingModule(List<string> trace, CancellationTokenSource cts) { _trace = trace; _cts = cts; }

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _trace.Add("init:" + Name);
                _cts.Cancel();                                 // 模拟上游取消（父级令牌失效）
                return UniTask.FromException(new OperationCanceledException(ct));
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _trace.Add("shutdown:" + Name);
                return UniTask.CompletedTask;
            }
        }

        [Fact]
        public void 退出前刷新钩子_按登记顺序执行_失败聚合不阻断()
        {
            var trace = new List<string>();
            var host = Host(new FakeModule("A", trace));
            host.AddPreShutdownFlush("settings", ct => { trace.Add("flush:settings"); return UniTask.CompletedTask; });
            host.AddPreShutdownFlush("telemetry", ct => { trace.Add("flush:telemetry"); throw new InvalidOperationException("遥测炸"); });
            host.AddPreShutdownFlush("lastlog", ct => { trace.Add("flush:lastlog"); return UniTask.CompletedTask; });

            Pump(host.InitializeAsync());
            Pump(host.ShutdownAsync());

            Assert.Equal(new[] { "flush:settings", "flush:telemetry", "flush:lastlog" }, trace.GetRange(1, 3).ToArray());
            Assert.Single(host.ShutdownFailures);
        }

        // ---- ClientScope ----

        [Fact]
        public void 作用域_LIFO释放_单项异常隔离_幂等()
        {
            var trace = new List<string>();
            var scope = new ClientScope("test");

            scope.Register(new Tracer(trace, "first"));
            scope.Register(new Tracer(trace, "second"));
            scope.Register(new BoomDisposer());

            scope.Dispose();                                    // LIFO：boom → second → first；boom 异常不阻断
            Assert.Equal(new[] { "release:second", "release:first" }, trace.ToArray());
            Assert.Single(scope.DisposeFailures);
            Assert.True(scope.IsDisposed);
            Assert.Equal(0, scope.OwnedCount);

            scope.Dispose();                                    // 幂等
            Assert.Equal(2, trace.Count);                       // 不重复释放
        }

        [Fact]
        public void 作用域_登记进已死作用域_就地释放并显性失败()
        {
            var trace = new List<string>();
            var scope = new ClientScope("test");
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.Register(new Tracer(trace, "late")));
            Assert.Equal(new[] { "release:late" }, trace.ToArray());   // 已就地释放（不给泄漏假象）
        }

        [Fact]
        public void 作用域_父子取消链接_父取消级联子孙()
        {
            var root = new ClientScope("root");
            var child = new ClientScope("account", root);
            var grand = new ClientScope("match", child);

            Assert.False(child.Token.IsCancellationRequested);
            root.Cancel();

            Assert.True(child.Token.IsCancellationRequested);   // 父取消级联
            Assert.True(grand.Token.IsCancellationRequested);   // 孙级联
            Assert.False(child.IsDisposed);                     // 取消 ≠ 释放（资源仍要正常走 LIFO 释放路径）
            child.Dispose();
            grand.Dispose();
            root.Dispose();
        }

        [Fact]
        public void 作用域_外部令牌链接()
        {
            var cts = new CancellationTokenSource();
            var scope = new ClientScope("with-external", externalCancellationToken: cts.Token);
            Assert.False(scope.Token.IsCancellationRequested);
            cts.Cancel();
            Assert.True(scope.Token.IsCancellationRequested);
            scope.Dispose();
        }

        // ---- 平台事件 ----

        [Fact]
        public void 平台事件_转发全部订阅者_订阅者异常不阻断()
        {
            var host = new ClientHost();
            int pauseCalls = 0, focusCalls = 0, lowMemoryCalls = 0;
            bool lastPause = true;

            host.SubscribePause(v => { pauseCalls++; lastPause = v; });
            host.SubscribePause(_ => throw new InvalidOperationException("订阅者炸"));
            host.SubscribeFocus(_ => focusCalls++);
            host.SubscribeLowMemory(() => lowMemoryCalls++);

            host.RaiseApplicationPause(false);
            host.RaiseApplicationFocus(false);
            host.RaiseLowMemory();

            Assert.Equal(1, pauseCalls);
            Assert.False(lastPause);
            Assert.Equal(1, focusCalls);
            Assert.Equal(1, lowMemoryCalls);
        }

        [Fact]
        public void 运行期注册模块_拒绝_模块集启动后封闭()
        {
            var host = Host(new FakeModule("A", new List<string>()));
            Pump(host.InitializeAsync());

            Assert.Throws<InvalidOperationException>(() => host.AddModule(new FakeModule("B", new List<string>())));
        }

        [Fact]
        public void 静态重置_任何时候可调用()
        {
            ClientHost.ResetForEditorReload();                 // 关闭 Domain Reload 场景的静态清理入口（不抛即可）
        }

        // ---- 辅助 ----

        private sealed class Tracer : IDisposable
        {
            private readonly List<string> _trace;
            private readonly string _name;
            public Tracer(List<string> trace, string name) { _trace = trace; _name = name; }
            public void Dispose() => _trace.Add("release:" + _name);
        }

        private sealed class BoomDisposer : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("boom");
        }
    }
}
