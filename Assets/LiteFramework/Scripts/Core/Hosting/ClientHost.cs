using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 客户端宿主（《商业级通用客户端框架总设计》§6.1 ClientHost）：真正的启动与关闭持有者，
    /// MonoBehaviour 侧只留引导适配器（GameEntry）与平台事件桥（AppLifetime）。
    ///
    /// 职责（§6.1 Host 责任逐条落点）：
    /// - 根取消源与模块所有权表：<see cref="_rootScope"/> + 按注册序的模块列表。
    /// - **按依赖顺序初始化**（依赖顺序 = 注册顺序，由装配方表达；不推断依赖图）；**失败只关闭已成功模块**（逆序回滚）。
    /// - **逆序关闭**；**单模块关闭失败不得阻断其余**（异常聚合上报，不抛出）。
    /// - 平台事件（Pause/Focus/LowMemory/Quit 意图）经 <see cref="SubscribePlatform"/> 转发——Host 只持回调集合，
    ///   Unity 生命周期消息由 AppLifetime 桥接进来（L1 可用假事件测试全部语义）。
    /// - 退出前刷新钩子 <see cref="AddPreShutdownFlush"/>（设置/存档/遥测/最后日志——C1 本批只留接缝，消费方后续接）。
    ///
    /// 静态状态纪律（§4 原则 8）：Host 不设静态单例；编辑器关闭 Domain Reload 的静态清理由
    /// <see cref="ResetForEditorReload"/> 承担（AppLifetime 以 RuntimeInitializeOnLoadMethod(SubsystemRegistration) 调用）。
    /// </summary>
    public sealed class ClientHost
    {
        private readonly object _gate = new object();
        private readonly List<IClientModule> _modules = new List<IClientModule>();     // 注册顺序 = 初始化顺序 = 关闭逆序
        private readonly List<(string name, Func<CancellationToken, UniTask> flush)> _flushHooks = new List<(string, Func<CancellationToken, UniTask>)>();
        private readonly List<Action<bool>> _pauseHandlers = new List<Action<bool>>();
        private readonly List<Action<bool>> _focusHandlers = new List<Action<bool>>();
        private readonly List<Action> _lowMemoryHandlers = new List<Action>();
        private readonly List<Func<UniTask>> _quitIntentHandlers = new List<Func<UniTask>>();

        private ClientScope _rootScope;
        private ClientContext _context;
        private int _initializedCount;                    // 已成功初始化的模块数（回滚/关闭的边界）
        private int _state;                                // 0=构造未启动 1=初始化中 2=运行 3=关闭中 4=已关闭
        private bool _shutdownRequested;

        /// <summary>模块初始化/关闭事件（诊断：模块名 + 阶段 + 耗时——C1 本批留字符串接缝，结构化日志归 C3）。
        /// 字段而非 event：引导适配器整钩替换（= 赋值）+ Host 内部 invoke，订阅语义由使用方自理。</summary>
        public Action<string, string> ModuleTrace;

        public int State => _state;
        public int ModuleCount { get { lock (_gate) { return _modules.Count; } } }

        /// <summary>运行期根作用域（初始化成功后可用；此前为 null）。</summary>
        public ClientScope RootScope => _rootScope;

        /// <summary>按类型读模块产物（引导完成后装配面；未初始化返回 null——调用方决定 fail-fast）。
        /// 只读透传 <see cref="ClientContext"/>：引导适配器（GameEntry）驱动容器/流程机用。</summary>
        public T Product<T>() where T : class
        {
            return _context?.Get<T>();
        }

        /// <summary>注册模块（必须在 InitializeAsync 之前；启动后拒绝——运行期动态增删模块是生命周期错误）。
        /// **注册顺序即初始化顺序、关闭逆序**——装配方用调用序表达依赖图。</summary>
        public ClientHost AddModule(IClientModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            lock (_gate)
            {
                if (_state != 0) throw new InvalidOperationException($"模块 {module.Name} 注册过晚：Host 已进入阶段 {_state}（模块集在启动后必须封闭）");
                _modules.Add(module);
            }
            return this;
        }

        /// <summary>登记退出前刷新钩子（设置保存/存档落盘/遥测冲刷/崩溃前最后日志——按登记顺序执行；
        /// 单钩子失败不阻断退出，聚合上报）。须在启动前登记。</summary>
        public ClientHost AddPreShutdownFlush(string name, Func<CancellationToken, UniTask> flush)
        {
            if (flush == null) throw new ArgumentNullException(nameof(flush));
            lock (_gate)
            {
                if (_state != 0) throw new InvalidOperationException($"刷新钩子 {name} 登记过晚：Host 已进入阶段 {_state}");
                _flushHooks.Add((name, flush));
            }
            return this;
        }

        /// <summary>订阅平台暂停/恢复（参数 = 是否暂停）。平台桥（AppLifetime）转发；订阅者异常被吞并上报，不影响其他订阅者。</summary>
        public ClientHost SubscribePause(Action<bool> handler) { lock (_gate) { _pauseHandlers.Add(handler); } return this; }

        /// <summary>订阅焦点变化（参数 = 是否获得焦点）。</summary>
        public ClientHost SubscribeFocus(Action<bool> handler) { lock (_gate) { _focusHandlers.Add(handler); } return this; }

        /// <summary>订阅低内存通知（平台桥转发；响应策略归订阅者）。</summary>
        public ClientHost SubscribeLowMemory(Action handler) { lock (_gate) { _lowMemoryHandlers.Add(handler); } return this; }

        /// <summary>订阅退出意图（如需要异步善后的宿主环境）；订阅者完成即视为同意退出。
        /// 真正的优雅关闭统一走 <see cref="ShutdownAsync"/>——Quit 桥（AppLifetime）先收集意图再触发关闭。</summary>
        public ClientHost SubscribeQuitIntent(Func<UniTask> handler) { lock (_gate) { _quitIntentHandlers.Add(handler); } return this; }

        /// <summary>
        /// 按注册顺序初始化全部模块。任一失败：**立即逆序关闭已成功模块**（含根 Scope 释放），然后抛出原异常
        /// （调用方进入确定错误态——§C1 退出条件"启动任一阶段取消或失败都能回到确定状态"的宿主侧保证）。
        /// ct 取消同理回滚（模块以 OperationCanceledException 穿透）。
        /// </summary>
        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            List<IClientModule> modules;
            lock (_gate)
            {
                if (_state != 0) throw new InvalidOperationException($"Host 状态 {_state} 不允许初始化（重复启动/已关闭）");
                _state = 1;
                modules = new List<IClientModule>(_modules);
            }

            _rootScope = new ClientScope("Root");
            _context = new ClientContext(_rootScope);

            try
            {
                for (int i = 0; i < modules.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        // C1-⑦ 关闭竞态守卫：ShutdownAsync 与初始化并发（退出打断引导）时，
                        // 初始化不再继续装配——以 OCE 走"回滚已成功模块 → 确定错误态"路径。
                        if (_shutdownRequested)
                            throw new OperationCanceledException("引导被宿主关闭打断（ShutdownAsync 与 InitializeAsync 并发）");
                    }
                    IClientModule module = modules[i];
                    ModuleTrace?.Invoke(module.Name, "init:start");
                    await module.InitializeAsync(_context, ct);
                    ModuleTrace?.Invoke(module.Name, "init:done");
                    lock (_gate) _initializedCount = i + 1;
                }

                lock (_gate) _state = 2;
            }
            catch (Exception)
            {
                // 回滚：只关闭已成功模块（逆序），再重新抛原异常。回滚自身的失败不再向上叠加（聚合丢弃——
                // 原因优先，回滚细节走 ModuleTrace）。
                await RollbackInitializedAsync();
                lock (_gate) _state = 4;
                throw;
            }
        }

        /// <summary>
        /// 优雅关闭：**先行取消根令牌**（统一取消链——级联流程/任务在途异步）→ 按登记顺序执行刷新钩子
        /// （设置/存档/遥测/最后日志）→ **逆序**关闭全部已初始化模块，最后释放根 Scope。
        /// 单模块/单钩子失败不阻断其余（异常聚合进 <see cref="ShutdownFailures"/>，不抛出）；
        /// 幂等（并发/重复调用收敛为一次）。
        /// </summary>
        public async UniTask ShutdownAsync(CancellationToken ct = default)
        {
            List<(string name, Func<CancellationToken, UniTask> flush)> flushes;
            lock (_gate)
            {
                if (_shutdownRequested) return;
                _shutdownRequested = true;
                _state = 3;
                flushes = new List<(string, Func<CancellationToken, UniTask>)>(_flushHooks);
            }

            // ① 统一取消链先行（C1-⑦）：根取消级联全部链接令牌（流程阶段 CTS/子 Scope 在途异步），
            //    使"宿主逆序关闭模块"与"流程仍持旧设施继续跑"不再竞态；资源释放仍在末尾 Dispose。
            _rootScope?.Cancel();

            // ② 刷新钩子（登记顺序）：失败聚合不阻断
            for (int i = 0; i < flushes.Count; i++)
            {
                try
                {
                    ModuleTrace?.Invoke(flushes[i].name, "flush:start");
                    await flushes[i].flush(ct);
                    ModuleTrace?.Invoke(flushes[i].name, "flush:done");
                }
                catch (Exception ex)
                {
                    lock (_gate) _shutdownFailures.Add(ex);
                }
            }

            // ③ 逆序关闭已初始化模块（未初始化者跳过——启动失败回滚后再次关闭不应触碰未初始化模块）
            List<IClientModule> modules;
            int initialized;
            lock (_gate)
            {
                modules = new List<IClientModule>(_modules);
                initialized = _initializedCount;
            }
            for (int i = initialized - 1; i >= 0; i--)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();     // 关闭路径的取消只约束"还没开始的关闭"；已开始的必须收尾
                    ModuleTrace?.Invoke(modules[i].Name, "shutdown:start");
                    await modules[i].ShutdownAsync(ct);
                    ModuleTrace?.Invoke(modules[i].Name, "shutdown:done");
                }
                catch (OperationCanceledException)
                {
                    lock (_gate) _state = 4;
                    throw;                                  // 主动放弃关闭：语义留给调用方（默认关闭不被外部 token 绑架）
                }
                catch (Exception ex)
                {
                    lock (_gate) _shutdownFailures.Add(ex); // 模块关闭失败：记录、继续其余（§6.1 红线）
                }
            }

            // ④ 根 Scope 释放（LIFO 资源逆序 + 根取消）+ 终态
            _rootScope?.Dispose();
            lock (_gate) _state = 4;
        }

        /// <summary>关闭路径聚合的异常（刷新钩子 + 模块关闭；诊断上报用，Host 不抛）。</summary>
        public IReadOnlyList<Exception> ShutdownFailures { get { lock (_gate) { return _shutdownFailures; } } }
        private readonly List<Exception> _shutdownFailures = new List<Exception>();

        /// <summary>平台桥入口：暂停/恢复。</summary>
        public void RaiseApplicationPause(bool paused)
        {
            List<Action<bool>> handlers;
            lock (_gate) handlers = new List<Action<bool>>(_pauseHandlers);
            for (int i = 0; i < handlers.Count; i++) SafeInvoke(handlers[i], h => h(paused));
        }

        /// <summary>平台桥入口：焦点变化。</summary>
        public void RaiseApplicationFocus(bool focused)
        {
            List<Action<bool>> handlers;
            lock (_gate) handlers = new List<Action<bool>>(_focusHandlers);
            for (int i = 0; i < handlers.Count; i++) SafeInvoke(handlers[i], h => h(focused));
        }

        /// <summary>平台桥入口：低内存。</summary>
        public void RaiseLowMemory()
        {
            List<Action> handlers;
            lock (_gate) handlers = new List<Action>(_lowMemoryHandlers);
            for (int i = 0; i < handlers.Count; i++) SafeInvoke(handlers[i], h => h());
        }

        /// <summary>平台桥入口：退出意图收集（订阅者串行执行；异常聚合不阻断退出流程）。</summary>
        public async UniTask RaiseQuitIntentAsync()
        {
            List<Func<UniTask>> handlers;
            lock (_gate) handlers = new List<Func<UniTask>>(_quitIntentHandlers);
            for (int i = 0; i < handlers.Count; i++)
            {
                try { await handlers[i](); }
                catch (Exception ex) { lock (_gate) _shutdownFailures.Add(ex); }
            }
        }

        /// <summary>订阅者异常吞并聚合（平台事件不因单个订阅者抛出而丢给 Unity 主循环）。</summary>
        private void SafeInvoke<T>(T handler, Action<T> invoke)
        {
            try { invoke(handler); }
            catch (Exception ex) { lock (_gate) _shutdownFailures.Add(ex); }
        }

        /// <summary>启动失败回滚：逆序关闭已成功模块 + 根 Scope；异常全部吞掉（原异常优先上抛）。</summary>
        private async UniTask RollbackInitializedAsync()
        {
            List<IClientModule> modules;
            int initialized;
            lock (_gate)
            {
                modules = new List<IClientModule>(_modules);
                initialized = _initializedCount;
            }

            for (int i = initialized - 1; i >= 0; i--)
            {
                try
                {
                    ModuleTrace?.Invoke(modules[i].Name, "rollback:shutdown");
                    await modules[i].ShutdownAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    ModuleTrace?.Invoke(modules[i].Name, "rollback:failed:" + ex.GetType().Name);
                }
            }

            _rootScope?.Dispose();
        }

        /// <summary>
        /// 编辑器静态清理（关闭 Domain Reload 场景）：AppLifetime 经 RuntimeInitializeOnLoadMethod(SubsystemRegistration)
        /// 调用。Host 自身无静态可变状态——此方法存在是为①钉死该纪律②给未来静态兼容门面一个唯一清理入口
        /// （§6.1"清理静态兼容状态"）。任何时候调用都安全。
        /// </summary>
        public static void ResetForEditorReload()
        {
            // 当前无静态状态（Host 实例由 GameEntry 持有，随场景/GO 销毁）。
            // 预留：静态门面（如未来 Log/FileSys 的编辑器态）在此统一重置。
        }
    }
}
