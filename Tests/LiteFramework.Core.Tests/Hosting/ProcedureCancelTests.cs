using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 统一取消链用例（C1-⑦：《商业级通用客户端框架总设计》§4 原则 4"所有跨帧异步必须绑定 CancellationToken"
    /// + §6.1 根取消源 + §C1 退出条件"启动任一阶段取消或失败都能回到确定状态"）：
    /// - 流程阶段 CTS 链接宿主根令牌：根取消级联在途阶段；离场取消语义不变；根已取消时新进阶段立即观察到。
    /// - ClientHost 关闭先行取消根令牌（模块关闭前）；ShutdownAsync 与 InitializeAsync 并发时初始化中止并回滚。
    /// 全部纯逻辑（fake 模块 + UniTaskCompletionSource 定向续延），无真实时钟/Sleep。
    /// </summary>
    public sealed class ProcedureCancelTests
    {
        private enum Id { A, B, C }

        private readonly struct Req { }

        /// <summary>记录 RunAsync 收到的取消令牌的阶段（测试观察口）。</summary>
        private sealed class RecordingStage : ProcedureStageBase<Id, Req>
        {
            public CancellationToken LastCt;

            public RecordingStage(CancellationToken rootToken = default) : base(rootToken) { }

            protected override void RunAsync(IStageHost<Id, Req> m, in Req req, CancellationToken ct)
                => LastCt = ct;                                  // 同步记录即可（无需推进）
        }

        private static void Pump(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            awaiter.GetResult();
        }

        // ---- 流程阶段 CTS × 根令牌 ----

        [Fact]
        public void 阶段_无根令牌_离场取消语义保持不变()
        {
            var a = new RecordingStage();
            var b = new RecordingStage();
            var m = new StageMachine<Id, Req>("t", (Id.A, a), (Id.B, b));
            m.Start(Id.A);

            Assert.False(a.LastCt.IsCancellationRequested);
            m.Request(Id.B);
            m.Tick(0.016f);                                      // 帧末迁移：A.OnLeave → 取消 A 的阶段 CTS

            Assert.True(a.LastCt.IsCancellationRequested);        // 离场即取消（原语义不回退）
            Assert.False(b.LastCt.IsCancellationRequested);       // 新阶段令牌独立
        }

        [Fact]
        public void 阶段_链接根令牌_根取消级联到在途阶段()
        {
            var rootCts = new CancellationTokenSource();
            var a = new RecordingStage(rootCts.Token);
            var m = new StageMachine<Id, Req>("t", (Id.A, a));
            m.Start(Id.A);

            Assert.False(a.LastCt.IsCancellationRequested);
            rootCts.Cancel();                                     // 宿主关闭（ShutdownAsync 先行 Cancel 根）

            Assert.True(a.LastCt.IsCancellationRequested);        // 在途阶段立即观察到（统一取消链）
        }

        [Fact]
        public void 阶段_根令牌已取消_新进阶段立即观察到取消()
        {
            var rootCts = new CancellationTokenSource();
            rootCts.Cancel();
            var a = new RecordingStage(rootCts.Token);
            var m = new StageMachine<Id, Req>("t", (Id.A, a));
            m.Start(Id.A);                                        // 链接 CTS 在根已取消后创建

            Assert.True(a.LastCt.IsCancellationRequested);        // 阶段 OnEnter 即见取消（不接受"迟到才取消"）
        }

        [Fact]
        public void 阶段_离场释放链接CTS_不释放根令牌源()
        {
            var rootCts = new CancellationTokenSource();
            var a = new RecordingStage(rootCts.Token);
            var b = new RecordingStage(rootCts.Token);
            var m = new StageMachine<Id, Req>("t", (Id.A, a), (Id.B, b));
            m.Start(Id.A);

            m.Request(Id.B);
            m.Tick(0.016f);                                       // A 离场（取消 + Dispose 自身链接 CTS）

            Assert.True(a.LastCt.IsCancellationRequested);
            Assert.False(b.LastCt.IsCancellationRequested);        // 根未取消：B 的阶段令牌仍健康
            Assert.False(rootCts.IsCancellationRequested);         // 阶段释放不影响根（所有权单向）
            rootCts.Dispose();
        }

        // ---- ClientHost 关闭序列 ----

        private sealed class TracerModule : IClientModule
        {
            private readonly List<string> _trace;
            public string Name { get; }
            public TracerModule(string name, List<string> trace) { Name = name; _trace = trace; }
            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _trace.Add("init:" + Name);
                return UniTask.CompletedTask;
            }
            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _trace.Add("shutdown:" + Name);
                return UniTask.CompletedTask;
            }
        }

        [Fact]
        public void 宿主关闭_根令牌先于模块关闭被取消()
        {
            var trace = new List<string>();
            ClientHost host = null;
            var probe = new ProbeModule("A", trace, () =>
                trace.Add("rootCancelled:" + host.RootScope.Token.IsCancellationRequested));
            host = new ClientHost();
            host.AddModule(probe);

            Pump(host.InitializeAsync());
            Assert.False(host.RootScope.Token.IsCancellationRequested);   // 运行期根令牌健康

            Pump(host.ShutdownAsync());

            // 探针在自身 ShutdownAsync 内回读根状态：True = 根取消先于首个模块关闭执行（关闭序列验证）
            Assert.Contains("rootCancelled:True", trace);
            Assert.True(host.RootScope.IsDisposed);
        }

        /// <summary>关闭期探针模块：ShutdownAsync 时回读根令牌状态（验证关闭序列的取消先于模块关闭）。</summary>
        private sealed class ProbeModule : IClientModule
        {
            private readonly List<string> _trace;
            private readonly Action _probe;
            public string Name { get; }
            public ProbeModule(string name, List<string> trace, Action probe)
            {
                Name = name; _trace = trace; _probe = probe;
            }
            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _trace.Add("init:" + Name);
                return UniTask.CompletedTask;
            }
            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _probe();
                _trace.Add("shutdown:" + Name);
                return UniTask.CompletedTask;
            }
        }

        [Fact]
        public void 关闭竞态_初始化在途时宿主关闭_装配中止并回滚_未初始化模块不再触碰()
        {
            var trace = new List<string>();
            var pending = new UniTaskCompletionSource();          // B 的初始化由测试定向续延
            var host = new ClientHost();
            host.AddModule(new TracerModule("A", trace));
            host.AddModule(new PendingModule("B", trace, pending));
            host.AddModule(new TracerModule("C", trace));         // 竞态守卫的目标：C 必须不再初始化

            var initTask = host.InitializeAsync();                // A 完成、B 挂起
            Assert.Equal(new[] { "init:A", "init:B" }, trace.ToArray());

            Pump(host.ShutdownAsync());                           // 退出打断引导：根取消 → 已成功者(A)关闭 → 根释放

            pending.TrySetResult();                                // B 的初始化此刻完成 → 守卫触发 OCE → 回滚
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(initTask));

            Assert.DoesNotContain("init:C", trace);                // 竞态守卫：装配中止，C 未被初始化
            Assert.Contains("shutdown:B", trace);                 // B 已完成初始化 → 回滚路径关闭它
            Assert.Equal(4, host.State);                          // 确定关闭终态
        }

        /// <summary>初始化挂起在 UniTaskCompletionSource 上的模块（定向续延——不依赖真实时钟/线程调度）。</summary>
        private sealed class PendingModule : IClientModule
        {
            private readonly List<string> _trace;
            private readonly UniTaskCompletionSource _pending;
            public string Name { get; }
            public PendingModule(string name, List<string> trace, UniTaskCompletionSource pending)
            {
                Name = name; _trace = trace; _pending = pending;
            }
            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _trace.Add("init:" + Name);
                return _pending.Task;
            }
            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _trace.Add("shutdown:" + Name);
                return UniTask.CompletedTask;
            }
        }
    }
}
