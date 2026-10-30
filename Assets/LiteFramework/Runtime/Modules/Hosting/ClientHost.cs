using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 客户端宿主：真正的启动与关闭持有者，
    /// MonoBehaviour 侧只留引导适配器（GameEntry）与平台事件桥（AppLifetime）。
    ///
    /// 职责：
    /// - 根取消源与模块所有权表：<see cref="_rootScope"/> + 按注册序的模块列表。
    /// - **按依赖顺序初始化**（依赖顺序 = 注册顺序，由装配方表达；不推断依赖图）；**失败只关闭已成功模块**（逆序回滚）。
    /// - **逆序关闭**；**单模块关闭失败不得阻断其余**（异常聚合上报，不抛出）。
    /// - 平台事件（Pause/Focus/LowMemory/Quit 意图）经订阅方法转发——Host 只持回调集合，
    ///   Unity 生命周期消息由 AppLifetime 桥接进来（L1 可用假事件测试全部语义）。
    /// - 退出前刷新钩子 <see cref="AddPreShutdownFlush"/>（设置/存档/遥测/最后日志）。
    ///
    /// 静态状态纪律：Host 不设静态单例；编辑器关闭 Domain Reload 的静态清理由
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
        private UniTaskCompletionSource _shutdownSignal;   // 并发关闭的完成信号：第二调用等待首次收尾，而不是立即返回
        private bool _scopeFailuresReported;
        private readonly List<(IClientModuleHandle handle, ModuleScope scope)> _handles = new List<(IClientModuleHandle, ModuleScope)>();   // 已成功模块的关闭句柄 + 资源作用域（逆序关闭/释放）

        /// <summary>模块初始化/关闭事件（诊断：模块名 + 阶段）。
        /// 字段而非 event：引导适配器整钩替换（= 赋值）+ Host 内部 invoke，订阅语义由使用方自理。</summary>
        public Action<string, string> ModuleTrace;

        public int State => _state;
        public int ModuleCount { get { lock (_gate) { return _modules.Count; } } }

        /// <summary>运行期根作用域（初始化成功后可用；未初始化时为 null）。</summary>
        public ClientScope RootScope => _rootScope;

        /// <summary>装配面是否已密封（路线图 §4.3）：初始化成功或启动回滚结束后为 true，
        /// 运行期任何新增登记都被拒绝。诊断/用例读口。</summary>
        public bool IsAssemblySealed => _context != null && _context.IsAssemblySealed;

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
        /// （调用方进入确定错误态：启动任一阶段取消或失败都能回到确定状态）。
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

            // 启动前图校验（路线图 M1）：重复名/缺失依赖/依赖环一律 fail-fast——早于任何副作用。
            // 校验失败 = 启动终止：进入终态（4），否则 Host 会停在"初始化中"且无法重试。
            try
            {
                ValidateModuleGraph(modules);
            }
            catch
            {
                lock (_gate)
                {
                    _shutdownRequested = true;
                    _state = 4;
                }
                throw;
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
                        // 关闭竞态守卫：ShutdownAsync 与初始化并发（退出打断引导）时，
                        // 初始化停止继续装配——以 OCE 走"回滚已成功模块 → 确定错误态"路径。
                        if (_shutdownRequested)
                            throw new OperationCanceledException("引导被宿主关闭打断（ShutdownAsync 与 InitializeAsync 并发）");
                    }
                    IClientModule module = modules[i];
                    ModuleTrace?.Invoke(module.Name, "init:start");
                    // 每个模块独立资源作用域（路线图 §4.3）：失败时本模块 scope 就地释放，不留给根作用域。
                    var moduleScope = new ModuleScope(_rootScope, module.Name);
                    // 装配窗口只覆盖本模块初始化：返回/抛出/取消/异步泄漏都在 finally 关窗。
                    var moduleContext = new ModuleContext(_context, module.Name, moduleScope);
                    try
                    {
                        await module.InitializeAsync(moduleContext, ct);
                    }
                    catch
                    {
                        moduleScope.Dispose();                 // 失败模块先释放自己的登记资源
                        throw;
                    }
                    finally
                    {
                        moduleContext.Close();
                    }
                    ModuleTrace?.Invoke(module.Name, "init:done");
                    lock (_gate)
                    {
                        _handles.Add((new ModuleHandle(module), moduleScope));   // 成功即产出关闭句柄（M1：初始化成功与可关闭绑定）
                        _initializedCount = i + 1;
                    }
                }

                _context.SealAssembly();                       // 装配完成：运行期不再新增注册
                if (!_context.Services.IsSealed) _context.Services.Seal();
                lock (_gate) _state = 2;
            }
            catch (Exception)
            {
                // 回滚：只关闭已成功模块（逆序），再重新抛原异常。回滚自身的失败不向上叠加（聚合丢弃——
                // 原因优先，回滚细节走 ModuleTrace）。
                _context?.SealAssembly();                      // 失败/取消后同样是"装配已结束"，迟到的 Put 一律拒绝
                await RollbackInitializedAsync();
                lock (_gate) 
                    _state = 4;
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
            List<(string name, Func<CancellationToken, UniTask> flush)> flushes = null;
            UniTaskCompletionSource waitSignal = null;
            lock (_gate)
            {
                if (_shutdownRequested)
                {
                    waitSignal = _shutdownSignal;
                    if (waitSignal == null) return;          // 已强制收尾（ForceReleaseRoot）——没有可等待的在途关闭
                }
                else
                {
                    _shutdownRequested = true;
                    _state = 3;
                    _shutdownSignal = new UniTaskCompletionSource();
                    flushes = new List<(string, Func<CancellationToken, UniTask>)>(_flushHooks);
                }
            }

            if (waitSignal != null)
            {
                // 并发/重复调用：等待首次关闭真正结束（含取消路径的 finally），而不是立即返回。
                await waitSignal.Task;
                return;
            }

            // ① 统一取消链先行：根取消级联全部链接令牌（流程阶段 CTS/子 Scope 在途异步），
            //    消除"宿主逆序关闭模块"与"流程仍持旧设施继续跑"的竞态；资源释放仍在末尾 Dispose。
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
            List<(IClientModuleHandle handle, ModuleScope scope)> handles;
            lock (_gate) handles = new List<(IClientModuleHandle, ModuleScope)>(_handles);
            try
            {
                for (int i = handles.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();     // 关闭路径的取消只约束"还没开始的关闭"；已开始的必须收尾
                        await handles[i].handle.ShutdownAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;                                  // 主动放弃关闭：语义留给调用方（作用域仍由 finally 释放）
                    }
                    catch (Exception ex)
                    {
                        lock (_gate) _shutdownFailures.Add(ex); // 模块关闭失败：记录、继续其余
                    }
                    finally
                    {
                        // 模块资源作用域随模块关闭释放（先句柄后资源；取消/失败路径同样释放）
                        ReleaseModuleScope(handles[i]);
                    }
                }
            }
            finally
            {
                // ④ 取消/异常路径兜底：未走到的模块 scope 也必须释放（Dispose 幂等；IsDisposed 防重复记账）。
                for (int i = handles.Count - 1; i >= 0; i--) ReleaseModuleScope(handles[i]);

                // ⑤ 根 Scope 释放（LIFO 资源逆序 + 根取消）+ 终态——**任何提前退出路径都必须释放**
                // （Roadmap §4.3：取消令牌只约束等待上限，不得跳过根 Scope 释放）。
                DisposeRoot();
                UniTaskCompletionSource signal;
                lock (_gate) { _state = 4; signal = _shutdownSignal; }
                signal?.TrySetResult();
            }
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

        /// <summary>
        /// 模块图校验（路线图 M1）：模块名重复、依赖缺失、依赖环——全部 fail-fast，且早于任何副作用。
        /// **只校验已声明依赖**（实现 <see cref="IClientModuleGraph"/> 的模块）；未声明者按注册序初始化。
        /// 依赖按**模块名**寻址（同一模块类可有多个实例，名字才是唯一身份）。
        /// </summary>
        private static void ValidateModuleGraph(List<IClientModule> modules)
        {
            var byName = new Dictionary<string, IClientModule>(StringComparer.Ordinal);
            for (int i = 0; i < modules.Count; i++)
            {
                IClientModule m = modules[i];
                if (byName.ContainsKey(m.Name))
                    throw new InvalidOperationException($"模块名重复：{m.Name}（模块集必须唯一）");
                byName.Add(m.Name, m);
            }

            for (int i = 0; i < modules.Count; i++)
            {
                if (!(modules[i] is IClientModuleGraph graph) || graph.DependsOn == null) continue;
                for (int d = 0; d < graph.DependsOn.Count; d++)
                {
                    string dep = graph.DependsOn[d];
                    if (string.IsNullOrEmpty(dep) || !byName.ContainsKey(dep))
                        throw new InvalidOperationException(
                            $"模块 {modules[i].Name} 依赖缺失：{(string.IsNullOrEmpty(dep) ? "<null>" : dep)} 未注册（注册序/装配错误）");
                }
            }

            // 环检测（DFS 三色；只走已声明边）
            var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 0=未访问 1=在栈 2=完成
            for (int i = 0; i < modules.Count; i++)
                VisitModule(modules[i].Name, byName, state);
        }

        private static void VisitModule(string name, Dictionary<string, IClientModule> byName, Dictionary<string, int> state)
        {
            if (state.TryGetValue(name, out int s))
            {
                if (s == 1) throw new InvalidOperationException($"模块依赖成环：{name}");
                if (s == 2) return;
            }
            state[name] = 1;
            if (byName.TryGetValue(name, out IClientModule module)
                && module is IClientModuleGraph graph && graph.DependsOn != null)
            {
                for (int i = 0; i < graph.DependsOn.Count; i++)
                {
                    string dep = graph.DependsOn[i];
                    if (!string.IsNullOrEmpty(dep)) VisitModule(dep, byName, state);
                }
            }
            state[name] = 2;
        }

        /// <summary>
        /// 强制同步释放根 Scope（退出/销毁路径的确定性兜底）：异步关闭未完成时也必须释放根资源。
        /// 幂等（`DisposeRoot` 内部有重复上报守卫）；只在主线程调用。
        /// </summary>
        public void ForceReleaseRoot()
        {
            UniTaskCompletionSource signal;
            lock (_gate)
            {
                _state = 4;
                _shutdownRequested = true;      // 迟到 ShutdownAsync 不再重复跑关闭链
                signal = _shutdownSignal;
            }
            DisposeRoot();
            signal?.TrySetResult();              // 唤醒并发等待者（若有）
        }

        /// <summary>释放一个模块的资源作用域（幂等；只在该 scope 首次释放时聚合其失败，避免兜底路径重复记账）。</summary>
        private void ReleaseModuleScope((IClientModuleHandle handle, ModuleScope scope) entry)
        {
            if (entry.scope.IsDisposed) return;
            entry.scope.Dispose();
            lock (_gate) _shutdownFailures.AddRange(entry.scope.DisposeFailures);
        }

        /// <summary>启动失败回滚：逆序关闭已成功模块 + 释放各自作用域 + 根 Scope；异常全部吞掉（原异常优先上抛）。</summary>
        private async UniTask RollbackInitializedAsync()
        {
            List<(IClientModuleHandle handle, ModuleScope scope)> handles;
            lock (_gate) handles = new List<(IClientModuleHandle, ModuleScope)>(_handles);

            for (int i = handles.Count - 1; i >= 0; i--)
            {
                try
                {
                    ModuleTrace?.Invoke(handles[i].handle.GetType().Name, "rollback:shutdown");
                    await handles[i].handle.ShutdownAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    ModuleTrace?.Invoke(handles[i].handle.GetType().Name, "rollback:failed:" + ex.GetType().Name);
                }
                finally
                {
                    handles[i].scope.Dispose();                // 模块资源随回滚释放（顺序：先句柄后资源）
                }
            }

            DisposeRoot();
        }

        /// <summary>模块关闭句柄（初始化成功后创建；把"成功"与"可关闭"绑成一个对象）。</summary>
        private sealed class ModuleHandle : IClientModuleHandle
        {
            private readonly IClientModule _module;
            public ModuleHandle(IClientModule module) => _module = module;

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                // 诊断轨迹保留模块名（句柄本身不带 Name）。
                return _module.ShutdownAsync(ct);
            }

            public override string ToString() => "ModuleHandle(" + _module.Name + ")";
        }

        /// <summary>资源域与服务域的同步释放异常统一进入宿主诊断；重复关闭不重复上报。</summary>
        private void DisposeRoot()
        {
            _rootScope?.Dispose();
            lock (_gate)
            {
                if (_scopeFailuresReported) return;
                _scopeFailuresReported = true;
                if (_rootScope != null) _shutdownFailures.AddRange(_rootScope.DisposeFailures);
                if (_context != null) _shutdownFailures.AddRange(_context.Services.DisposeFailures);
            }
        }

        /// <summary>
        /// 编辑器静态清理（关闭 Domain Reload 场景）：AppLifetime 经 RuntimeInitializeOnLoadMethod(SubsystemRegistration)
        /// 调用。Host 自身无静态可变状态——此方法存在是为①钉死该纪律②给静态兼容门面一个唯一清理入口。
        /// 任何时候调用都安全。
        /// </summary>
        public static void ResetForEditorReload()
        {
            // 当前无静态状态（Host 实例由 GameEntry 持有，随场景/GO 销毁）。
            // 预留：静态门面（如未来 Log/FileSystem 的编辑器态）在此统一重置。
        }
    }
}
