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

            public async UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
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

            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
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

        // ---- M1：模块依赖图（重复/缺失/环）与关闭链全路径释放 ----

        /// <summary>带依赖声明的 fake 模块（M1 DAG 校验用）。</summary>
        private sealed class GraphModule : IClientModule, IClientModuleGraph
        {
            public string Name { get; }
            public IReadOnlyList<string> DependsOn { get; }
            public ClientScope CapturedRoot { get; private set; }

            public GraphModule(string name, params string[] deps)
            {
                Name = name;
                DependsOn = deps ?? Array.Empty<string>();
            }

            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
            {
                CapturedRoot = context.RootScope;
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        [Fact]
        public void 模块图_重复模块名拒绝()
        {
            var host = Host(new GraphModule("A"), new GraphModule("A"));
            Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));
        }

        [Fact]
        public void 模块图_依赖缺失拒绝()
        {
            var host = Host(new GraphModule("A"), new GraphModule("B", "GraphModule2NotRegistered"));
            Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));
            Assert.Equal(4, host.State);   // 图校验失败 = 终态，不得停在"初始化中"
        }

        [Fact]
        public void 模块图_依赖成环拒绝()
        {
            // 环只能在**不同类型**之间形成；用两个不同 fake 类型互相依赖。
            var host = new ClientHost();
            host.AddModule(new CycleA());
            host.AddModule(new CycleB());
            Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));
        }

        [Fact]
        public void 关闭_取消令牌不跳过根Scope释放()
        {
            var module = new GraphModule("A");
            var host = Host(module);
            Pump(host.InitializeAsync());
            Assert.NotNull(module.CapturedRoot);
            Assert.False(module.CapturedRoot.IsDisposed);

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => Pump(host.ShutdownAsync(cts.Token)));
            }

            Assert.True(module.CapturedRoot.IsDisposed,
                "关闭被取消也必须释放根 Scope（路线图 §4.3：令牌只约束等待上限）");
        }

        private sealed class GraphModule2NotRegistered : IClientModule
        {
            public string Name => "not-registered";
            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        // ---- M1：装配窗口与装配面密封（路线图 §4.3） ----

        private sealed class SealedProbe { }

        /// <summary>捕获本模块装配窗口的模块（用于验证窗口关闭/跨模块拒绝）。</summary>
        private sealed class CaptureModule : IClientModule
        {
            private readonly Action<ModuleContext> _capture;
            public CaptureModule(Action<ModuleContext> capture) => _capture = capture;
            public string Name => "capture";
            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
            {
                _capture(context);
                return UniTask.CompletedTask;
            }
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        /// <summary>在初始化中执行外部动作的模块（跨模块登记用例）。</summary>
        private sealed class ActionModule : IClientModule
        {
            private readonly Action _action;
            private readonly string _name;
            public ActionModule(string name, Action action) { _name = name; _action = action; }
            public string Name => _name;
            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
            {
                _action();
                return UniTask.CompletedTask;
            }
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        private sealed class FailingModule : IClientModule
        {
            public string Name => "failing";
            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
                => throw new InvalidOperationException("init failed");
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        [Fact]
        public void 装配窗口_模块初始化返回后关闭_迟到登记拒绝()
        {
            ModuleContext captured = null;
            var host = Host(new CaptureModule(context => captured = context));

            Pump(host.InitializeAsync());

            Assert.NotNull(captured);
            Assert.True(host.IsAssemblySealed);
            var ex = Assert.Throws<InvalidOperationException>(() => captured.Put(new SealedProbe()));
            Assert.Contains("装配窗口已关闭", ex.Message);
        }

        [Fact]
        public void 装配窗口_不得跨模块登记()
        {
            ModuleContext firstWindow = null;
            var host = Host(
                new CaptureModule(context => firstWindow = context),
                new ActionModule("late-put", () => firstWindow.Put(new SealedProbe())));

            var ex = Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));
            Assert.Contains("装配窗口已关闭", ex.Message);
        }

        [Fact]
        public void 装配失败回滚_装配面同样密封_迟到登记拒绝()
        {
            ModuleContext captured = null;
            var host = Host(new CaptureModule(context => captured = context), new FailingModule());

            Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));
            Assert.True(host.IsAssemblySealed);
            Assert.Throws<InvalidOperationException>(() => captured.Put(new SealedProbe()));
        }

        // ---- 组合根（ComposeRoot：全部模块成功后、密封前的跨域装订） ----

        [Fact]
        public void 组合根_全部模块成功后执行_窗口内可登记_密封后迟到登记拒绝()
        {
            var trace = new List<string>();
            ModuleContext composeWindow = null;
            var host = Host(new FakeModule("A", trace), new FakeModule("B", trace));
            host.ComposeRoot(context =>
            {
                trace.Add("compose");
                composeWindow = context;
                context.Put(new SealedProbe());                 // 窗口内登记合法（与模块同一语义）
                return UniTask.CompletedTask;
            });

            Pump(host.InitializeAsync());

            Assert.Equal(new[] { "init:A", "init:B", "compose" }, trace.ToArray());   // 模块全成功后才装订
            Assert.NotNull(composeWindow);
            Assert.True(host.IsAssemblySealed);
            var ex = Assert.Throws<InvalidOperationException>(() => composeWindow.Put(new SealedProbe()));
            Assert.Contains("装配窗口已关闭", ex.Message);       // 与模块同一窗口纪律
        }

        [Fact]
        public void 组合根失败_回滚模块并释放组合根作用域_原异常上抛()
        {
            var trace = new List<string>();
            var released = new List<string>();
            var boom = new InvalidOperationException("compose 失败");
            var host = Host(new FakeModule("A", trace));
            host.ComposeRoot(context =>
            {
                trace.Add("compose");
                context.Scope.Register(new Tracer(released, "compose-resource"));
                throw boom;
            });

            var ex = Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));

            Assert.Same(boom, ex);                              // 原异常上抛
            Assert.Contains("shutdown:A", trace);               // 已成功模块照常逆序回滚
            Assert.Equal(new[] { "release:compose-resource" }, released.ToArray());   // 组合根登记资源就地回收
            Assert.Equal(4, host.State);
        }

        [Fact]
        public void 组合根取消_回滚模块_不进入运行态()
        {
            var trace = new List<string>();
            using var cts = new CancellationTokenSource();
            var host = Host(new FakeModule("A", trace));
            host.ComposeRoot(async context =>
            {
                trace.Add("compose");
                await UniTask.Yield();
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            });

            Assert.ThrowsAny<OperationCanceledException>(() => Pump(host.InitializeAsync(cts.Token)));
            Assert.Contains("shutdown:A", trace);
            Assert.Equal(4, host.State);
            Assert.NotEqual(2, host.State);
        }

        [Fact]
        public void 关闭_组合根作用域先于模块释放()
        {
            var released = new List<string>();
            var host = Host(new ScopedModule("module", released));
            host.ComposeRoot(context =>
            {
                context.Scope.Register(new Tracer(released, "compose"));
                return UniTask.CompletedTask;
            });

            Pump(host.InitializeAsync());
            Pump(host.ShutdownAsync());

            // 组合根作用域先释放（最后装订 = 逆序第一），随后才是模块句柄关闭与其作用域
            Assert.Equal(new[] { "init:module", "release:compose", "shutdown:module", "release:scope:module" }, released.ToArray());
        }

        // ---- M2：逐模块资源作用域（路线图 §4.3） ----

        /// <summary>把资源登记进本模块作用域的模块：验证释放时机与顺序。</summary>
        private sealed class ScopedModule : IClientModule
        {
            private readonly List<string> _trace;
            private readonly string _name;
            private readonly bool _fail;

            public ScopedModule(string name, List<string> trace, bool fail = false)
            {
                _name = name;
                _trace = trace;
                _fail = fail;
            }

            public string Name => _name;

            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
            {
                _trace.Add("init:" + _name);
                context.Scope.Register(new Tracer(_trace, "scope:" + _name));
                if (_fail) throw new InvalidOperationException("init failed: " + _name);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _trace.Add("shutdown:" + _name);
                return UniTask.CompletedTask;
            }
        }

        [Fact]
        public void 模块作用域_失败模块先释放自己的资源_再回滚已成功模块()
        {
            var trace = new List<string>();
            var host = Host(new ScopedModule("A", trace), new ScopedModule("B", trace, fail: true));

            Assert.Throws<InvalidOperationException>(() => Pump(host.InitializeAsync()));

            Assert.Equal(new[] { "init:A", "init:B", "release:scope:B", "shutdown:A", "release:scope:A" }, trace.ToArray());
        }

        [Fact]
        public void 模块作用域_关闭时随模块逆序释放_先句柄后资源()
        {
            var trace = new List<string>();
            var host = Host(new ScopedModule("A", trace), new ScopedModule("B", trace));

            Pump(host.InitializeAsync());
            trace.Clear();
            Pump(host.ShutdownAsync());

            Assert.Equal(new[] { "shutdown:B", "release:scope:B", "shutdown:A", "release:scope:A" }, trace.ToArray());
        }

        [Fact]
        public void 模块作用域_关闭被取消也释放()
        {
            var trace = new List<string>();
            var host = Host(new ScopedModule("A", trace));

            Pump(host.InitializeAsync());
            trace.Clear();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => Pump(host.ShutdownAsync(cts.Token)));
            }

            Assert.Contains("release:scope:A", trace);
        }

        [Fact]
        public void 模块作用域_关闭被取消时多模块也全部释放()
        {
            var trace = new List<string>();
            var host = Host(new ScopedModule("A", trace), new ScopedModule("B", trace));

            Pump(host.InitializeAsync());
            trace.Clear();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => Pump(host.ShutdownAsync(cts.Token)));
            }

            Assert.Contains("release:scope:A", trace);
            Assert.Contains("release:scope:B", trace);
        }

        [Fact]
        public async System.Threading.Tasks.Task 关闭_并发第二调用等待首次完成()
        {
            var gate = new UniTaskCompletionSource();
            var host = Host(new GatedModule(gate));
            await host.InitializeAsync();

            UniTask first = host.ShutdownAsync();
            UniTask second = host.ShutdownAsync();
            Assert.False(second.Status.IsCompleted(), "第二调用必须等待首次关闭，而不是立即返回");

            gate.TrySetResult();
            await first;
            await second;
            Assert.Equal(4, host.State);
        }

        private sealed class GatedModule : IClientModule
        {
            private readonly UniTaskCompletionSource _gate;
            public GatedModule(UniTaskCompletionSource gate) => _gate = gate;
            public string Name => "gated";
            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask ShutdownAsync(CancellationToken ct) => _gate.Task;
        }

        private sealed class CycleA : IClientModule, IClientModuleGraph
        {
            public string Name => "CycleA";
            public IReadOnlyList<string> DependsOn => new[] { "CycleB" };
            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        private sealed class CycleB : IClientModule, IClientModuleGraph
        {
            public string Name => "CycleB";
            public IReadOnlyList<string> DependsOn => new[] { "CycleA" };
            public UniTask InitializeAsync(ModuleContext context, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }
    }
}
