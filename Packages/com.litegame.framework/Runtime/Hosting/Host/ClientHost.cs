using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
namespace LiteFramework
{
    /// <summary>客户端宿主：真正的启动与关闭持有者， MonoBehaviour 侧只留引导适配器（GameEntry）与平台事件桥（AppLifetime）。</summary>
    public sealed class ClientHost
    {
        private readonly object _gate = new object();
        private readonly SegmentRuntime _segments = new SegmentRuntime();
        private readonly PlatformEventHub _platform = new PlatformEventHub();
        private readonly List<(string name, Func<CancellationToken, UniTask> flush)> _flushHooks = new List<(string, Func<CancellationToken, UniTask>)>();
        private ClientScope _rootScope;
        private ClientContext _context;
        private ComposeRootRunner _composeRoot;              // 组合根执行器（全部模块成功后、密封前一次）
        private HostState _state;                            // Created/Initializing/Running/ShuttingDown/Closed
        private readonly ShutdownCoordinator _shutdown;

        public ClientHost()
        {
            _shutdown = new ShutdownCoordinator(
                () => _rootScope,
                _segments.SnapshotHandles,
                SnapshotFlushHooks,
                () => _composeRoot,
                () => _context,
                () => _segmentTrace,
                SetState);
        }

        /// <summary>模块初始化/关闭事件（诊断：模块名 + 阶段）。经 <see cref="SetSegmentTrace"/> 整钩设置， 订阅语义由使用方自理。</summary>
        public Action<string, string> SegmentTrace => _segmentTrace;

        private Action<string, string> _segmentTrace;

        /// <summary>整钩替换诊断回调（引导适配器装配期设置）。</summary>
        public ClientHost SetSegmentTrace(Action<string, string> trace)
        {
            _segmentTrace = trace;
            return this;
        }

        public HostState State => _state;
        public int SegmentCount => _segments.Count;

        /// <summary>装配报告（初始化成功后有值；失败/取消路径为 null）。</summary>
        public AssemblyReport AssemblyReport { get; private set; }

        /// <summary>运行期根作用域（初始化成功后可用；未初始化时为 null）。</summary>
        public ClientScope RootScope => _rootScope;

        /// <summary>装配面是否已密封（路线图 §4.3）：初始化成功或启动回滚结束后为 true， 运行期任何新增登记都被拒绝。诊断/用例读口。</summary>
        public bool IsAssemblySealed => _context != null && _context.IsAssemblySealed;

        /// <summary>按类型读模块产物（引导完成后装配面；未初始化返回 null——调用方决定 fail-fast）。 只读透传 <see cref="ClientContext"/>：引导适配器（GameEntry）驱动容器/流程机用。</summary>
        public T Product<T>() where T : class
        {
            return _context?.Get<T>();
        }

        /// <summary>注册模块（必须在 InitializeAsync 之前；启动后拒绝——运行期动态增删模块是生命周期错误）。 **依赖由 <see cref="IClientSegmentGraph.DependsOn"/> 声明决定，注册序只作同层稳定序**。</summary>
        public ClientHost AddSegment(IClientSegment module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            lock (_gate)
            {
                if (_state != HostState.Created) throw new InvalidOperationException($"模块 {module.Name} 注册过晚：Host 已进入阶段 {_state}（模块集在启动后必须封闭）");
                _segments.Add(module);
            }
            return this;
        }

        /// <summary>注册组合根回调（<c>AppComposition.ComposeAfter</c>）：**全部模块初始化成功后、装配面密封前** 调用一次。回调拿到**打开的装配窗口**（<c>Put</c> 合法；返回/抛出/取消后立即关闭）与自己的资源作用域 （关闭时先于模块释放；失败与模块失败同路径回滚）。跨域装订只能落这里——段内不许跨域连线。 **必须早于启动登记**（与 <see cref="AddSegment"/> 同口径）。</summary>
        public ClientHost ComposeRoot(Func<SegmentContext, UniTask> compose)
        {
            if (compose == null) throw new ArgumentNullException(nameof(compose));
            lock (_gate)
            {
                if (_state != HostState.Created) throw new InvalidOperationException($"组合根登记过晚：Host 已进入阶段 {_state}（回调集在启动后必须封闭）");
                _composeRoot = new ComposeRootRunner(compose);
            }
            return this;
        }

        /// <summary>登记退出前刷新钩子（设置保存/存档落盘/遥测冲刷/崩溃前最后日志——按登记顺序执行； 单钩子失败不阻断退出，聚合上报）。须在启动前登记。</summary>
        public ClientHost AddPreShutdownFlush(string name, Func<CancellationToken, UniTask> flush)
        {
            if (flush == null) throw new ArgumentNullException(nameof(flush));
            lock (_gate)
            {
                if (_state != HostState.Created) throw new InvalidOperationException($"刷新钩子 {name} 登记过晚：Host 已进入阶段 {_state}");
                _flushHooks.Add((name, flush));
            }
            return this;
        }

        /// <summary>订阅平台暂停/恢复（参数 = 是否暂停）。平台桥（AppLifetime）转发；订阅者异常被吞并上报，不影响其他订阅者。</summary>
        public ClientHost SubscribePause(Action<bool> handler) { _platform.SubscribePause(handler); return this; }

        /// <summary>订阅焦点变化（参数 = 是否获得焦点）。</summary>
        public ClientHost SubscribeFocus(Action<bool> handler) { _platform.SubscribeFocus(handler); return this; }

        /// <summary>订阅低内存通知（平台桥转发；响应策略归订阅者）。</summary>
        public ClientHost SubscribeLowMemory(Action handler) { _platform.SubscribeLowMemory(handler); return this; }

        /// <summary>按 DependsOn 拓扑序初始化全部模块（同层按注册序稳定）。</summary>
        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            lock (_gate)
            {
                if (_state != HostState.Created) throw new InvalidOperationException($"Host 状态 {_state} 不允许初始化（重复启动/已关闭）");
                _state = HostState.Initializing;
            }

            try
            {
                _segments.Prepare();                            // 图校验早于任何副作用
            }
            catch
            {
                _shutdown.MarkClosed();
                throw;
            }

            _rootScope = new ClientScope("Root");
            _context = new ClientContext(_rootScope);

            try
            {
                await _segments.InitializeAsync(_context, _rootScope, SegmentTrace, ct,
                    () => _shutdown.IsShutdownRequested);
                if (_composeRoot != null)                       // 组合根：全部模块成功后、密封前的跨域装订
                    await _composeRoot.RunAsync(_context, _rootScope, SegmentTrace, ct);

                _context.SealAssembly();                        // 装配完成：运行期不再新增注册
                if (!_context.Services.IsSealed) _context.Services.Seal();
                AssemblyReport = _context.BuildReport(_segments.OrderedNames);
                SetState(HostState.Running);
            }
            catch (Exception)
            {
                _context?.SealAssembly();                       // 失败/取消后同样是"装配已结束"
                _shutdown.ReleaseComposeScope();
                await _segments.RollbackAsync(SegmentTrace, _shutdown.AddFailure);
                _shutdown.DisposeRoot();
                _shutdown.MarkClosed();
                throw;
            }
        }

        /// <summary>优雅关闭：**先行取消根令牌**（统一取消链——级联流程/任务在途异步）→ 按登记顺序执行刷新钩子 （设置/存档/遥测/最后日志）→ **逆序**关闭全部已初始化模块，最后释放根 Scope。</summary>
        public UniTask ShutdownAsync(CancellationToken ct = default) => _shutdown.ShutdownAsync(ct);

        /// <summary>关闭路径聚合的异常（刷新钩子 + 模块关闭；诊断上报用，Host 不抛）。</summary>
        public IReadOnlyList<Exception> ShutdownFailures => _shutdown.Failures;

        /// <summary>平台事件订阅者异常（与关闭失败分离；诊断用）。</summary>
        public IReadOnlyList<Exception> PlatformFailures => _platform.Failures;

        /// <summary>平台桥入口：暂停/恢复。</summary>
        public void RaiseApplicationPause(bool paused) => _platform.RaisePause(paused);

        /// <summary>平台桥入口：焦点变化。</summary>
        public void RaiseApplicationFocus(bool focused) => _platform.RaiseFocus(focused);

        /// <summary>平台桥入口：低内存。</summary>
        public void RaiseLowMemory() => _platform.RaiseLowMemory();

        /// <summary>强制同步释放（退出/销毁路径的确定性兜底）：释放组合根、模块与根 Scope；不调用模块 Shutdown。</summary>
        public void ForceReleaseRoot() => _shutdown.ForceRelease();

        private void SetState(HostState state)
        {
            lock (_gate) _state = state;
        }

        private IReadOnlyList<(string name, Func<CancellationToken, UniTask> flush)> SnapshotFlushHooks()
        {
            lock (_gate) return new List<(string, Func<CancellationToken, UniTask>)>(_flushHooks);
        }

        /// <summary>编辑器静态清理（关闭 Domain Reload 场景）：AppLifetime 经 RuntimeInitializeOnLoadMethod(SubsystemRegistration) 调用。</summary>
        public static void ResetForEditorReload()
        {
            // 当前无静态状态（Host 实例由 GameEntry 持有，随场景/GO 销毁）。
            // 预留：静态门面（如未来 Log/FileSystem 的编辑器态）在此统一重置。
        }
    }
}
