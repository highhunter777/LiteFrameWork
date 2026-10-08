using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// UI 壳服务：栈 / 层级组 / 状态机 / 实例化管线
    /// + 策略三件（构造注入，默认实现壳内自带——壳零策略硬编码）
    /// + 转场编排层（§1.5：状态机 + 排队 1 + 超时兜底 + 壳统一管交互门；策略只是"表现体"）
    /// + 逻辑解析器（LuaBehaviourAdapter 接线；解析失败降级 NullLogic）。
    /// 薄壳 = DI 注册的普通服务（设计方案 §1.3），装配点构造。
    /// 驱动：ITickable（GameEntry 统一喂）——仅 Active 态转发 OnUpdate（快照迭代——回调内开关界面安全）；
    /// 遮盖语义（组级批量暂停的灰盒形态）：全屏界面打开 → 更低层级组的 Active 界面批量 Covered，
    /// 关闭后按"仍开着的最高全屏"重算（多全屏叠开不误恢复）。
    /// 首次与复用合流成 <see cref="FinishOpenAsync"/> 一条管线；
    /// 打开失败回滚；Close/CloseAll 覆盖 Covered/Paused；env 重建走 <see cref="DropAllLogic"/> 清旧引用。
    /// （§4.3）：在途打开操作合流（并发 Show 共享一份工作，不返回 null）；数据冲突 Busy；
    /// 调用方 token 只取消本人、全员退出撤工作；Close 对在途加载发权威取消；失败类型化（<see cref="UIOpenException"/>）。
    ///
    /// **职责边界（单一职责）**：本类只做**实例生命周期编排**——池化/租约/LRU、开关注入转场、
    /// 打开并发合流、遮盖推导、Lua 逻辑换表。**模态栈/射线遮蔽/Back 目标**是独立的输入协调子域，
    /// 已抽出 <see cref="UIModalStack"/>（本类经 <see cref="UIModalStack.IUIFormQuery"/> 提供窄查询口，
    /// 上面的 RegisterModal/IsModalOpen/TryGetBackTarget 等是**转发面**——公共 API 不变）。
    /// 逻辑换表（<see cref="MarkLogicStale"/> 族）与遮盖（<see cref="RecomputeCovering"/>）
    /// 仍留本类：前者是实例生命周期的一环（复用前必须换表），后者与开/关管线同事务序。
    /// </summary>
    public sealed class UIService : ITickable, IModuleStats, UIModalStack.IUIFormQuery
    {
        /// <summary>语义层名（§6.2"保留 Bottom/Window/Top，可增 Modal/Loading/System"）。
        /// <c>System</c>：Toast/Loading/错误重试等统一反馈面占此层，经统一排序器分配 order——
        /// 反馈面不得自建 Canvas 或私有 sortingOrder（§7 视觉单一来源）。索引即 tbuiform.layer 列。</summary>
        public static readonly string[] GroupNames = { "Bottom", "Window", "Top", "System" };

        /// <summary>缓存实例数默认预算（§5.2：缓存有预算——Resident 也计入总预算）。</summary>
        public const int DefaultCacheBudget = 16;

        /// <summary>关闭原因（§4.4：区分用户关闭与系统清理——系统关闭不受业务"未保存拦截"阻挡、不必播离场）。</summary>
        public enum CloseReason
        {
            User,        // 用户主动（可被出栈拦截）
            Back,        // 返回（可被出栈拦截）
            Replace,     // 同组全屏替换（内部）
            ScopeExit,   // 父级 Scope 退出（系统：跳过拦截与离场）
            Reload,      // env 重建（系统）
            Shutdown,    // 宿主关闭（系统）
        }

        private readonly IUIFormCatalog _catalog;
        private readonly Dictionary<int, UIForm> _forms = new Dictionary<int, UIForm>(16);   // id → 实例（含池中）
        private readonly Dictionary<int, OpenOperation> _openOps = new Dictionary<int, OpenOperation>(4);   // 在途打开操作（并发合流）
        private readonly Dictionary<int, IUIPrefabLease> _leases = new Dictionary<int, IUIPrefabLease>(8);  // 实例租约（持到真正销毁）
        private readonly Dictionary<int, long> _lastUsed = new Dictionary<int, long>(8);     // 最近使用序号（LRU 淘汰判据）
        private readonly HashSet<int> _closing = new HashSet<int>();                         // 关闭在途（转场动画期间防并发 Close 重入）
        private readonly HashSet<int> _staleLogic = new HashSet<int>();   // 逻辑待更新（运行期增量重填，§2.3）
        private readonly List<UIForm> _tickSnapshot = new List<UIForm>(16);   // Tick 快照（§4.3 重入正确性：回调内开关界面不改枚举集合）
        private readonly UILayerGroup[] _groups;
        private readonly Transform _root;
        private readonly UITransitionRunner _transitions;                  // 转场编排层（§1.5，不注册 ITickable）
        private readonly UIModalStack _modals;                             // 模态栈/射线遮蔽/Back 目标（§6.2 独立子域）
        private readonly UIFocusCoordinator _focus;                        // 焦点协调（§6.2——保存/恢复/平台返回；U2-⑦）

        /// <summary>焦点协调者（平台返回的输入侧接线点：装配层把 <c>UINavigationController.BackAsync</c>
        /// 挂 <c>Focus.PlatformBack</c>——语义侧推导在导航器，见 <see cref="UIFocusCoordinator"/>）。</summary>
        public UIFocusCoordinator Focus => _focus;
        private readonly IPopInterceptor _pop;
        private readonly Func<UIFormInfo, IUIFormLogic> _logicResolver;
        private readonly Func<string, CancellationToken, UniTask<IUIPrefabLease>> _loadPrefab;
        private long _useCounter;
        private bool _shutdown;

        /// <summary>缓存实例数预算（池中 Recycled 计数上限；超预算淘汰最久未用的非打开实例）。</summary>
        public int CacheBudget { get; set; }

        /// <param name="replaceTransition">可选：Replace 的"两组并发"定制位；null = 壳合成 WhenAll(Close, Show)。</param>
        /// <param name="transitionMaxDuration">转场超时兜底（§1.5.4 规则④），默认 2s。</param>
        /// <param name="loadPrefab">界面 prefab 租约加载口（**必填**——返回可释放句柄；
        /// 生产绑定内容服务（<see cref="ContentPrefabLease"/>），测试/工具用 <see cref="UIPrefabLeases.Unowned"/>）。</param>
        /// <param name="cacheBudget">缓存实例数预算（默认 <see cref="DefaultCacheBudget"/>）。</param>
        public UIService(IUIFormCatalog catalog,
            ILayerStrategy layerStrategy = null,
            ITransitionStrategy transitionStrategy = null,
            IPopInterceptor popInterceptor = null,
            Func<UIFormInfo, IUIFormLogic> logicResolver = null,
            IReplaceTransition replaceTransition = null,
            float transitionMaxDuration = UITransitionRunner.DefaultMaxDuration,
            Func<string, CancellationToken, UniTask<IUIPrefabLease>> loadPrefab = null,
            int cacheBudget = DefaultCacheBudget)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _transitions = new UITransitionRunner(transitionStrategy ?? new InstantTransition(),   // 默认零动效：通用层不认识动效适配器（§5.1）；生产装配显式传 FadeSlideTransition
                                                  replaceTransition, transitionMaxDuration);
            _pop = popInterceptor ?? new DefaultPopInterceptor();
            _logicResolver = logicResolver;
            _loadPrefab = loadPrefab ?? throw new ArgumentNullException(nameof(loadPrefab),
                "UI prefab 加载口必填（租约语义——生产绑内容服务，测试绑 Unowned）");
            CacheBudget = cacheBudget > 0 ? cacheBudget : DefaultCacheBudget;

            _root = new GameObject("[UIRoot]").transform;
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);   // EditMode 用例不得走 DDOL
            _groups = new UILayerGroup[GroupNames.Length];
            for (int i = 0; i < GroupNames.Length; i++)
            {
                var node = new GameObject(GroupNames[i]).transform;
                node.SetParent(_root, false);
                _groups[i] = new UILayerGroup(GroupNames[i], (i + 1) * UILayerGroup.DepthStride, node,
                    layerStrategy ?? new DefaultLayerStrategy());
            }
            _modals = new UIModalStack(this);      // 查询口 = 本类（暴露"谁开着/某层栈顶"两个窄事实）
            _focus = new UIFocusCoordinator();    // 焦点协调（§6.2——保存/恢复/平台返回；U2-⑦）
            UiInput.EnsureEventSystem();          // 输入底座（幂等；§7 基础设施例外见 UiInput 判据）
            Log.Info("UI 壳就绪:层级组 Bottom/Window/Top（策略三件默认实现）", "UI");
        }

        /// <summary>打开界面（§4.2 统一流程：校验请求 → 获取或创建实例 → 校验初始化 → 复位视觉 →
        /// 排序/遮盖 → OnShow → 入场/替换 → 返回结果）。首次与复用**同一条管线**：复用只省略实例初始化。
        /// 幂等：已打开（含 Covered/Paused）直接返回；池中复用重跑 OnShow 但不重跑 OnInit。
        /// （§4.3）：同一界面**并发 Show 合流共享一次加载/打开**（不返回 null 表示进行中）；
        /// 在途且数据不同 → <see cref="UIOpenException"/>(Busy)；调用方 token 只取消本人等待，
        /// 全员退出才撤销工作；Close 对在途打开发权威取消。失败抛类型化异常（Reason 分类）。
        /// 数据传参收口 <see cref="IUIData"/>（禁 object）。</summary>
        public UniTask<UIForm> ShowAsync(int formId, IUIData data = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_shutdown)
                throw new UIOpenException(UIOpenFailure.Rejected, formId, "UI 已 Shutdown——不接受页面操作（§4.4 停止接入）");
            var info = _catalog.Get(formId);
            var group = GetGroup(info.Layer);
            if (group.IsFull)
                throw new UIOpenException(UIOpenFailure.Rejected, formId,
                    $"层级组[{group.Name}] 容量不足（{UILayerGroup.DepthStride}）——拒绝打开（§6.2：禁止复用正在使用的 order）");

            if (_forms.TryGetValue(formId, out var existing))
            {
                switch (existing.State)
                {
                    case UIFormState.Recycled:
                        SwapIfStale(existing);            // 换表重建（运行期增量重填）后再复用
                        EnsureLogic(existing);            // env 重建后逻辑已落空：按当前注册表重新解析
                        return ReuseAsync(existing, group, data);
                    case UIFormState.Active:
                    case UIFormState.Covered:
                    case UIFormState.Paused:
                        // 已打开即幂等返回；不暗中再次 OnShow（更新数据用显式刷新操作，§4.2）
                        Log.Warning($"UIForm[{formId}] 已打开（{existing.State}）——ShowAsync 幂等返回", "UI");
                        return UniTask.FromResult(existing);
                    default:
                        throw new InvalidOperationException(
                            $"UIForm[{formId}] 状态 {existing.State} 不允许 Show（§3.4 fail-fast）");
                }
            }

            // 在途打开操作合流：并发 Show 共享同一次加载/打开——等待者各自可取消，工作只有一份
            if (_openOps.TryGetValue(formId, out var op))
            {
                if (!ReferenceEquals(op.Data, data))
                    throw new UIOpenException(UIOpenFailure.Busy, formId,
                        $"UIForm[{formId}] 打开在途且数据不同——首请求持有数据（§4.3），更新数据走显式刷新/替换");
                Interlocked.Increment(ref op.Waiters);
                return WaitOpenAsync(op, ct);
            }

            op = new OpenOperation(formId, data);
            _openOps[formId] = op;
            Interlocked.Increment(ref op.Waiters);
            RunOpenAsync(op, info, group).Forget();       // 工作与等待分离：工作只受权威取消（WorkCts），不随单人等待走
            return WaitOpenAsync(op, ct);
        }

        /// <summary>等待一次在途打开：外部 token 只约束本人等待；取消时释放等待份额，
        /// 全员退出且工作未完成 → 撤销工作（§4.3"取消等待 ≠ 底层停止"的对称面）。</summary>
        private async UniTask<UIForm> WaitOpenAsync(OpenOperation op, CancellationToken ct)
        {
            try
            {
                return await op.Completion.Task.AttachExternalCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                if (Interlocked.Decrement(ref op.Waiters) <= 0 && !op.Completion.Task.Status.IsCompleted())
                    op.WorkCts.Cancel();                  // 最后一个等待者退出：撤销尚未提交的工作
                throw;
            }
            catch (UIOpenException)
            {
                Interlocked.Decrement(ref op.Waiters);    // 失败也释放等待份额（工作由 RunOpenAsync 收尾）
                throw;
            }
        }

        /// <summary>执行一次打开（工作侧）：加载 → 实例化挂组 → 画布就位 → 入栈 → OnInit/OnShow → 转场表现。
        /// 权威取消在各提交检查点核验（迟到结果不提交、半成品就地回滚）；成功/失败统一经 Completion 交付全部等待者。</summary>
        private async UniTaskVoid RunOpenAsync(OpenOperation op, UIFormInfo info, UILayerGroup group)
        {
            UIForm form = null;
            IUIPrefabLease lease = null;
            try
            {
                try
                {
                    lease = await _loadPrefab(info.Location, op.WorkCts.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new UIOpenException(UIOpenFailure.LoadFailed, op.FormId,
                        $"UIForm[{op.FormId}] prefab 加载失败:{info.Location}", ex);   // 类型化 + 含 location（§4.3）
                }
                op.WorkCts.Token.ThrowIfCancellationRequested();      // 迟到结果不提交（权威取消在加载完成后核）

                var root = UnityEngine.Object.Instantiate(lease.Prefab, group.Root);
                form = new UIForm(info, root);
                form.Logic = ResolveLogic(info);
                form.PrepareForShow();
                _forms[op.FormId] = form;
                _leases[op.FormId] = lease;                           // 所有权移交：租约由 UIService 持有到实例真正销毁
                lease = null;                                         // 登记成功——失败路径不就地释放
                group.Stack.Push(form);
                group.RecalculateOrders();                            // 开序即深序（统一重算）

                if (!form.EnterActiveFromLoading(op.Data))
                {
                    // 打开回滚（§4.1）：撤销登记 → 释放逻辑/Lua 引用 → 销毁对象 → 对外报失败
                    // （禁止"Active + NullLogic 伪装成功"）。租约同步释放（§4.4 所有权清理——
                    // 登记后 lease 已置 null，finally 兜底不覆盖此路径，必须就地释放）
                    _forms.Remove(op.FormId);
                    if (_leases.Remove(op.FormId, out IUIPrefabLease failedLease)) failedLease.Release();
                    group.Stack.Remove(form);
                    form.DisposeFailedOpen();
                    throw new UIOpenException(UIOpenFailure.InitFailed, op.FormId,
                        $"UIForm[{op.FormId}] 初始化失败（OnInit/OnShow 抛异常）——已回滚，对象与登记均已清理");
                }

                var opened = await FinishOpenAsync(form, group);
                op.Completion.TrySetResult(opened);
            }
            catch (OperationCanceledException)
            {
                // 权威取消（Close 对在途 / 全员退出）：半成品就地回滚，全部等待者收类型化取消
                if (form != null)
                {
                    _forms.Remove(op.FormId);
                    if (_leases.Remove(op.FormId, out IUIPrefabLease canceledLease)) canceledLease.Release();
                    group.Stack.Remove(form);
                    form.DisposeFailedOpen();
                }
                op.Completion.TrySetException(new UIOpenException(UIOpenFailure.Canceled, op.FormId,
                    $"UIForm[{op.FormId}] 打开被权威取消（Close/全部等待者退出/Scope 退出）"));
            }
            catch (Exception ex)
            {
                op.Completion.TrySetException(ex);       // 全部等待者收到同一异常（加载失败等）
            }
            finally
            {
                lease?.Release();                        // 所有权未移交成功的就地释放（加载到、但未登记）
                _openOps.Remove(op.FormId);
                op.WorkCts.Dispose();
            }
        }

        /// <summary>在途打开操作：同一界面的并发 Show 合并到这一份记录上。</summary>
        private sealed class OpenOperation
        {
            public readonly int FormId;
            public readonly IUIData Data;                 // 首个请求持有数据（§4.3：数据冲突 = Busy，不静默覆盖）
            public readonly UniTaskCompletionSource<UIForm> Completion = new UniTaskCompletionSource<UIForm>();
            public readonly CancellationTokenSource WorkCts = new CancellationTokenSource();   // 权威取消源
            public int Waiters;                           // 在途等待者数（全员退出才撤工作）

            public OpenOperation(int formId, IUIData data)
            {
                FormId = formId;
                Data = data;
            }
        }

        /// <summary>
        /// 复用打开（§4.2）：复位视觉 → 重新分配排序 → 重算遮盖 → OnShow → 入场/替换。
        /// 复用与新建两条路径必须完全一致。
        /// </summary>
        private async UniTask<UIForm> ReuseAsync(UIForm form, UILayerGroup group, IUIData data)
        {
            form.PrepareForShow();
            group.Stack.Push(form);                          // 复用同样回到组内最上层
            group.RecalculateOrders();                       // 复用后统一重排序（§6.2）
            form.EnterActiveFromRecycled(data);
            return await FinishOpenAsync(form, group);
        }

        /// <summary>打开管线的公共尾段：遮盖重算 → 推导转场模式（同组已有全屏 → Replace）→ 播表现 →
        /// 关闭被替换的旧全屏。首次与复用共用，保证"策略对新建和复用一致"（§4.2）。</summary>
        private async UniTask<UIForm> FinishOpenAsync(UIForm form, UILayerGroup group)
        {
            if (form.Info.FullScreen) RecomputeCovering();

            UIForm outgoing = null;
            var mode = TransitionMode.Push;
            if (form.Info.FullScreen)
            {
                outgoing = FindSameGroupFullScreen(group, form);
                if (outgoing != null) mode = TransitionMode.Replace;
            }
            await _transitions.PlayAsync(mode, outgoing, form);
            if (outgoing != null && outgoing.IsOpen)
                CloseFormInternal(outgoing);            // 切换：旧界面在转场收尾后关闭（转场期间被显式关掉则不动）

            _modals.RecomputeBlocking();                  // 模态射线遮蔽随打开/替换收尾重算（§6.2）
            _focus.OnFormActivated(form);                 // 打开接管：聚焦首个可交互件（转场收尾后——锁期内本就无输入）

            Log.Info($"UIForm[{form.Id}] 打开（{group.Name}@{form.Canvas.sortingOrder}，{mode}）", "UI");
            return form;
        }

        /// <summary>关闭界面（Active/Covered/Paused 均可）：出栈拦截（仅用户语义）→ 离场转场（系统原因跳过）
        /// → OnHide → 落池。全屏关闭后重算遮盖。并发安全：转场动画期间二次 Close 直接忽略。
        /// 系统原因（ScopeExit/Reload/Shutdown）：跳过出栈拦截（§4.4 系统清理不受业务"未保存拦截"无限阻挡）
        /// 且不必播离场——但 OnHide 与释放照常完成。</summary>
        public async UniTask CloseAsync(int formId, CloseReason reason = CloseReason.User)
        {
            // 关闭在途加载 = 权威取消（页面从未打开，Close 即完成——不抛"未打开"）
            if (_openOps.TryGetValue(formId, out var op))
            {
                op.WorkCts.Cancel();                      // 先取消再摘登记：新 Show 不会并入将死操作
                _openOps.Remove(formId);
                Log.Info($"UIForm[{formId}] 在途打开被 Close 取消", "UI");
                return;
            }

            if (!_forms.TryGetValue(formId, out var form))
                throw new KeyNotFoundException($"UIForm 表存在但未打开:{formId}（§3.4 fail-fast）");
            if (!form.IsOpen)
                throw new InvalidOperationException(
                    $"UIForm[{formId}] 状态 {form.State} 不允许 Close（仅 Active/Covered/Paused）");

            bool system = reason is CloseReason.ScopeExit or CloseReason.Reload or CloseReason.Shutdown;
            if (!system && !_pop.CanClose(form))
            {
                Log.Info($"UIForm[{formId}] 关闭被出栈拦截", "UI");
                return;
            }
            if (system)
            {
                // 系统清理：无离场表现，直接落库（OnHide/订阅清理/展示令牌取消照常）
                CloseFormInternal(form);
                Log.Info($"UIForm[{formId}] 系统关闭（{reason}）", "UI");
                return;
            }
            if (!_closing.Add(formId))
            {
                Log.Warning($"UIForm[{formId}] 关闭中——并发 Close 已忽略", "UI");
                return;
            }

            try
            {
                await _transitions.PlayAsync(TransitionMode.Pop, form, null);
                if (!form.IsOpen)
                {
                    Log.Info($"UIForm[{formId}] 关闭中止（转场期间状态已变为 {form.State}）", "UI");
                    return;
                }
                CloseFormInternal(form);
                Log.Info($"UIForm[{formId}] 关闭", "UI");
            }
            finally
            {
                _closing.Remove(formId);
            }
        }

        /// <summary>DevReload / Scope 退出编排用（§2.3 定案"重载后全关" + §4.4 CloseAll 从**全部打开**
        /// 集合清理）：旧 LuaTable 经适配器持有时全部失效，留旧界面 = 静默用死对象。
        /// Covered/Paused 同样是打开着的界面——只抓 Active 快照会漏关。逐界面容错（单界面失败不阻断）。
        /// 默认系统语义（ScopeExit）：跳过出栈拦截与离场表现——系统清理不被业务守卫阻挡（§4.4）。</summary>
        public async UniTask CloseAllOpen(CloseReason reason = CloseReason.ScopeExit)
        {
            var ids = new List<int>();
            foreach (var kv in _forms)
                if (kv.Value.IsOpen) ids.Add(kv.Key);
            foreach (var id in ids)
            {
                try { await CloseAsync(id, reason); }
                catch (Exception ex) { Log.Error($"CloseAllOpen[{id}]:{ex.Message}", "UI"); }
            }
        }

        /// <summary>
        /// 销毁一个缓存实例（缓存页面完整销毁——GameObject Destroy + 租约释放 + 登记摘除）。
        /// 仅允许销毁**非打开**（Recycled）实例——打开中的先 Close；在途加载先等待/取消。
        /// DestroyOnClose 策略与低内存清理走同一路径（§5.2）。
        /// </summary>
        public void Destroy(int formId)
        {
            if (!_forms.TryGetValue(formId, out var form))
                return;                                    // 幂等：不存在 = 已销毁
            if (form.IsOpen || _openOps.ContainsKey(formId))
                throw new InvalidOperationException(
                    $"UIForm[{formId}] 打开中/加载在途——先 Close/等待完成再 Destroy（§5.2 只淘汰未打开实例）");
            DestroyFormInternal(formId);
            Log.Info($"UIForm[{formId}] 缓存实例已销毁（租约释放）", "UI");
        }

        /// <summary>
        /// 优雅关闭（§4.4 ShutdownAsync）：停止接入 → 取消全部在途操作 → 全关（系统语义）→
        /// 销毁全部缓存实例与租约 → 销毁 DDoL Root。幂等；逐项异常汇总上报不中断后续清理。
        /// </summary>
        public async UniTask ShutdownAsync()
        {
            if (_shutdown) return;                         // 幂等
            _shutdown = true;                              // ① 停止接入（Show 即拒）

            // ② 取消全部在途打开（等待者收类型化 Canceled；半成品由工作侧就地回滚）
            if (_openOps.Count > 0)
            {
                var cancelling = new List<OpenOperation>(_openOps.Values);
                _openOps.Clear();
                foreach (var op in cancelling) op.WorkCts.Cancel();
            }

            // ③ 全关（系统语义：不播离场、跳过拦截——OnHide/订阅/展示令牌照常清理）
            await CloseAllOpen(CloseReason.Shutdown);

            // ④ 销毁全部缓存实例 + 释放全部租约（完整销毁路径）
            var cachedIds = new List<int>(_forms.Keys);
            foreach (var id in cachedIds) DestroyFormInternal(id);

            // ⑤ 销毁 DDoL Root
            if (_root != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(_root.gameObject);
                else UnityEngine.Object.Destroy(_root.gameObject);
#else
                UnityEngine.Object.Destroy(_root.gameObject);
#endif
            }
            Log.Info("UI Shutdown 完成：在途取消、全关、缓存销毁、租约归零、Root 销毁", "UI");
        }

        /// <summary>
        /// 逻辑全体落空（§10.2 env 重建前置）：逐个释放适配器持有的旧 Lua 引用、逻辑置空并把界面标记为
        /// "复用前需重新解析 + 补跑 OnInit"。**必须在 `LuaComponent.Shutdown()`（env.Dispose）之前调用**
        /// ——释放 Lua 引用要趁 env 还活着；不落空则池中界面下次复用会拿已 Dispose 的 LuaFunction 打进死环境。
        /// </summary>
        public void DropAllLogic()
        {
            foreach (var kv in _forms)
            {
                kv.Value.DropLogic();
                kv.Value.NeedsReinit = true;
            }
            _staleLogic.Clear();                              // 待更新标记已被"落空 + 重解析"承接
            Log.Info($"UI 逻辑已全部落空（env 重建前置）：{_forms.Count} 件待重解析", "UI");
        }

        /// <summary>手动暂停（Active → Paused；恢复走 <see cref="Resume"/>）。</summary>
        public void Pause(int formId)
        {
            var form = RequireOpen(formId);
            form.EnterPaused();
            _focus.OnFormSuspended(form);                   // 暂停页让出焦点（§6.2）
        }

        public void Resume(int formId)
        {
            var form = RequireOpen(formId);
            form.EnterActiveFromPaused();
            _focus.OnFormRevealed(form);                    // 续跑页恢复焦点（不抢占）
        }

        /// <summary>置顶（§6.2 BringToFront）：组内移到栈顶并统一重排序。
        /// 仅对打开中的界面有效（Recycled 的复用打开天然置顶）。</summary>
        public void BringToFront(int formId)
        {
            var form = RequireOpen(formId);
            var group = GetGroup(form.Info.Layer);
            if (!group.Stack.Remove(form)) return;
            group.Stack.Push(form);
            group.RecalculateOrders();
            _modals.RecomputeBlocking();                       // 模态射线遮蔽随组内序变化重算（§6.2）
        }

        /// <summary>查询打开状态（IsOpen = Active/Covered/Paused——对 Lua 语义"界面上没关"）。</summary>
        public bool IsOpen(int formId) => _forms.TryGetValue(formId, out var f) && f.IsOpen;

        // ---- 模态栈（§6.2）----
        // 模态登记/顶模态推导/射线遮蔽/Back 目标是一套自洽的输入协调子域，已独立为 UIModalStack
        // （窄查询口注入，本类不把"谁开着"的判据泄漏出去）。以下四行是**转发面**——保持
        // UIService 的既有公共 API 不变（DialogService/UINavigationController/装配点零改动）。

        /// <summary>登记模态（幂等；同 formId 重复登记无副作用）。</summary>
        public void RegisterModal(int formId) => _modals.Register(formId);

        /// <summary>取消模态登记（幂等；已打开页面不受影响——只影响后续 Back/遮蔽推导）。</summary>
        public void UnregisterModal(int formId) => _modals.Unregister(formId);

        /// <summary>当前是否有打开着的模态（输入协调者的组成输入，§6.2"输入由单一协调者综合模态栈…"）。</summary>
        public bool IsModalOpen => _modals.IsModalOpen;

        /// <summary>当前最顶模态（无模态返回 0）。</summary>
        public int TopModalId => _modals.TopModalId;

        /// <summary>
        /// 返回目标（§6.2 平台返回统一处理）：**最顶模态优先**，无模态时取最高非空层级组的栈顶。
        /// 被出栈拦截的关闭是合法确定结果——这里只给目标，拦截由 <see cref="CloseAsync"/> 走 Back 语义。
        /// </summary>
        public bool TryGetBackTarget(out int formId) => _modals.TryGetBackTarget(out formId);


        // ---- 逻辑换表：运行期增量重填（§2.3）----

        /// <summary>
        /// 全量打标"逻辑已过期"（幂等）。由重填服务在**清注册表之前**调用——先标后清，窗口内界面仍能用旧表跑完。
        /// Active 界面**不立刻换表**（§2.3：已打开的界面换代码会"一半旧一半新"），等它关闭后再换。
        /// </summary>
        public void MarkLogicStale()
        {
            foreach (var kv in _forms) _staleLogic.Add(kv.Key);
        }

        /// <summary>单界面打标（调试口用）。</summary>
        public void MarkLogicStale(int formId) => _staleLogic.Add(formId);

        /// <summary>
        /// 重填完成后调用：池中（Recycled）界面立刻换表；Active/Covered/Paused 保留标记，等 Close 时换。
        /// </summary>
        public void ApplyStaleLogic()
        {
            foreach (var kv in _forms)
                if (kv.Value.State == UIFormState.Recycled) SwapIfStale(kv.Value);
        }

        /// <summary>仍带"待更新"标记的界面数（HUD 读数：>0 = 新逻辑尚未在该界面生效）。</summary>
        public int StaleLogicCount => _staleLogic.Count;

        /// <summary>
        /// 换表（幂等）：旧适配器解绑并释放 Lua 引用 → 用当前注册表重新解析 → 池中界面标记需补跑 OnInit。
        /// 只在"已无人在用旧逻辑"时调用（Reuse 之前 / EnterClosing 之后 / ApplyStaleLogic 的池中件）。
        /// </summary>
        private void SwapIfStale(UIForm form)
        {
            if (!_staleLogic.Remove(form.Id)) return;

            form.Logic?.Release();   // 通用钩子——UI 服务不认识脚本适配器（§5.1）
            form.Logic = ResolveLogic(form.Info);
            if (form.State == UIFormState.Recycled) form.NeedsReinit = true;
        }

        // ---- 内部 ----

        // ---- UIModalStack.IUIFormQuery：模态栈的窄查询口 ----
        // 只暴露两个事实（谁仍开着 / 某层已打开栈），不把实例字典与生命周期交出去——
        // 模态推导因此可独立测试（替身实现本接口即可）。

        void UIModalStack.IUIFormQuery.CollectOpen(List<UIForm> into)
        {
            foreach (var kv in _forms)
                if (kv.Value != null && kv.Value.IsOpen) into.Add(kv.Value);
        }

        IReadOnlyList<UIForm> UIModalStack.IUIFormQuery.StackOf(int layer)
            => layer >= 0 && layer < _groups.Length ? _groups[layer].Stack.Open : null;

        int UIModalStack.IUIFormQuery.HighestLayer => _groups.Length - 1;


        private UILayerGroup GetGroup(int layer)
        {
            if (layer < 0 || layer >= _groups.Length)
                throw new ArgumentOutOfRangeException(nameof(layer),
                    $"层级越界:{layer}（语义层 0..{_groups.Length - 1}={string.Join("/", GroupNames)}，核对 tbuiform.layer 列）");
            return _groups[layer];
        }

        private UIForm RequireOpen(int formId)
        {
            if (!_forms.TryGetValue(formId, out var form) || !form.IsOpen)
                throw new InvalidOperationException($"UIForm[{formId}] 未打开（当前:{(form?.State.ToString() ?? "无")})");
            return form;
        }

        /// <summary>
        /// env 重建后的逻辑补解析（§10.2）：只有"逻辑已被 <see cref="DropAllLogic"/> 落空且标记待重解析"
        /// 才动它——C# 逻辑与非空逻辑一律不越权替换。
        /// </summary>
        private void EnsureLogic(UIForm form)
        {
            if (!form.NeedsReinit) return;
            if (!ReferenceEquals(form.Logic, NullUIFormLogic.Instance)) return;
            form.Logic = ResolveLogic(form.Info);
        }

        /// <summary>遮盖重算（幂等）：以"**仍逻辑打开**的最高全屏界面"所在组为界（含 Covered/Paused——
        /// §6.2：遮盖从仍打开且有遮盖能力的全部页面推导，不能只看 Active），**严格更低**的组全遮、其余照常；
        /// 逐界面按目标态做最小迁移（Active↔Covered 配对回调不重复触发，Paused 不被遮盖改写）。</summary>
        private void RecomputeCovering()
        {
            int coverThreshold = -1;                    // 严格低于该深度的组全遮
            foreach (var g in _groups)
                foreach (var f in g.Stack.Open)
                    if (f.IsOpen && f.Info.FullScreen)
                        coverThreshold = Math.Max(coverThreshold, g.BaseDepth);

            for (int i = _groups.Length - 1; i >= 0; i--)
            {
                var g = _groups[i];
                bool shouldCover = g.BaseDepth < coverThreshold;
                foreach (var f in g.Stack.Open)
                {
                    if (shouldCover && f.State == UIFormState.Active)
                    {
                        f.EnterCovered();
                        _focus.OnFormSuspended(f);            // 被遮页让出焦点（键盘不驱动不可交互页）
                    }
                    else if (!shouldCover && f.State == UIFormState.Covered)
                    {
                        f.EnterActiveFromCovered();
                        _focus.OnFormRevealed(f);             // 揭示页不抢占地恢复焦点
                    }
                }
            }
        }

        private IUIFormLogic ResolveLogic(UIFormInfo info)
        {
            // 空 LuaPath = 无业务逻辑的展示面（Loading/Toast/错误弹窗）：
            // **正常降级，不打日志**。若无此分支会落到下方 catch 打 Error，而测试专项 §8.2 把
            // "Console 新增 Error" 判为 UI Lane 失败——纯展示面会凭空制造门禁红灯。
            if (string.IsNullOrEmpty(info.LuaPath)) return NullUIFormLogic.Instance;

            // 逻辑解析：resolver（GameEntry 装配 LuaBehaviourAdapter ← UI 注册表）→ 失败降级 NullLogic
            // （错误语义"注册失败"，§3.4——启动期已知键由 RegistryFiller 校验兜底，此处为运行期降级）
            if (_logicResolver == null) return NullUIFormLogic.Instance;
            try
            {
                var logic = _logicResolver(info);
                return logic ?? NullUIFormLogic.Instance;
            }
            catch (Exception ex)
            {
                Log.Error($"界面逻辑解析失败[{info.LuaPath}]:{ex.Message}", "UI");
                return NullUIFormLogic.Instance;
            }
        }

        /// <summary>本组内查找"仍打开的全屏界面"（§1.5.3 Replace 推导用；排除刚入栈的新界面）。
        /// 覆盖 Active 与 Paused——同组全屏互斥对"手动暂停但没关"的页同样成立。</summary>
        private UIForm FindSameGroupFullScreen(UILayerGroup group, UIForm exclude)
        {
            var open = group.Stack.Open;
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var f = open[i];
                if (f == null || ReferenceEquals(f, exclude)) continue;
                if (f.IsOpen && f.Info.FullScreen) return f;
            }
            return null;
        }

        /// <summary>
        /// 页面关闭通知：<see cref="CloseFormInternal"/> 是全部关闭路径的单一漏斗
        /// （用户/系统/替换），打开中的页面真正离场时在此广播。消费者（弹窗服务等）据此对
        /// "被外部关闭"的在途等待收口——否则等待任务永久悬挂。
        /// 重入约束同 Tick 快照：回调内**禁止再开关界面**（先记账，出回调后再操作）。
        /// </summary>
        public event Action<int> FormClosed;

        /// <summary>
        /// 落库关闭：EnterClosing → 出栈 → 落池 → 换表 → 重算遮盖 → LRU 记账 → 预算淘汰。
        /// 由 <see cref="CloseAsync"/>（Pop 转场收尾后）与 Replace 自动关闭旧界面共用。
        /// 顺序不能反：OnHide 跑完前换表会让界面"一半旧一半新"（§2.3）。
        /// </summary>
        private void CloseFormInternal(UIForm form)
        {
            form.EnterClosing();
            _focus.OnFormClosed(form);                    // 摘保存焦点 + 清本页选择（在重算遮盖前——底层揭示恢复可接管）
            var group = GetGroup(form.Info.Layer);
            group.Stack.Remove(form);
            group.RecalculateOrders();                        // 移除后统一重算（紧缩——不复用 order 也不留洞）
            form.Recycle();
            SwapIfStale(form);
            if (form.Info.FullScreen) RecomputeCovering();
            _modals.RecomputeBlocking();                  // 模态射线遮蔽随关闭重算（关掉模态 → 下方恢复可命中）

            // per-form 缓存策略（§5.2 U2 表列）：DestroyOnClose 关即销毁（不进池）
            if (form.Info.CacheStrategy == UICacheStrategy.DestroyOnClose)
            {
                DestroyFormInternal(form.Id);
                Log.Info($"UIForm[{form.Id}] DestroyOnClose——关即销毁", "UI");
                FormClosed?.Invoke(form.Id);
                return;
            }

            _lastUsed[form.Id] = ++_useCounter;            // LRU 记账（最近使用序号）
            EvictBeyondBudget();
            FormClosed?.Invoke(form.Id);
        }

        /// <summary>缓存预算淘汰（§5.2 LRU：只淘汰**未打开、未被操作引用**的缓存实例；
        /// per-form 策略 Resident 不参与淘汰——预算满时跳过常驻页，淘汰候选仅限 LRU 策略的实例）。</summary>
        private void EvictBeyondBudget()
        {
            int cached = 0;
            foreach (var kv in _forms)
                if (kv.Value.State == UIFormState.Recycled) cached++;
            if (cached <= CacheBudget) return;

            // 候选 = 池中 LRU 策略实例（Resident 不淘汰），按最近使用序升序（最旧先淘汰）
            var candidates = new List<(int id, long used)>(cached);
            foreach (var kv in _forms)
            {
                if (kv.Value.State != UIFormState.Recycled) continue;
                if (kv.Value.Info.CacheStrategy == UICacheStrategy.Resident) continue;
                if (_lastUsed.TryGetValue(kv.Key, out long used))
                    candidates.Add((kv.Key, used));
            }
            candidates.Sort((a, b) => a.used.CompareTo(b.used));

            int toEvict = cached - CacheBudget;
            for (int i = 0; i < toEvict && i < candidates.Count; i++)
            {
                DestroyFormInternal(candidates[i].id);
                Log.Info($"UIForm[{candidates[i].id}] 缓存超出预算 {CacheBudget}——LRU 淘汰（租约释放）", "UI");
            }
        }

        /// <summary>完整销毁：摘登记 → 实例销毁（GameObject + 逻辑/Lua 引用 + 展示令牌）→ 租约释放。
        /// 终态 Disposed——不可再复用（再次 Show = 全新实例与全新租约）。</summary>
        private void DestroyFormInternal(int formId)
        {
            if (!_forms.TryGetValue(formId, out var form)) return;
            _forms.Remove(formId);
            _staleLogic.Remove(formId);
            _lastUsed.Remove(formId);
            form.DestroyInstance();
            if (_leases.TryGetValue(formId, out var lease))
            {
                _leases.Remove(formId);
                lease.Release();                           // 最后一个依赖实例已销毁——底层引用归零（§5.2）
            }
        }

        // ---- ITickable / IModuleStats ----

        public void Tick(float deltaTime)
        {
            _focus.Tick();                                // 平台返回键轮询（输入侧；语义侧在导航器 Back 链）

            // 快照迭代（§4.3 重入正确性）：OnUpdate 内发起开/关/销毁不得在枚举 _forms 时改集合；
            // 本帧新建的界面下帧起参与驱动（新开的页面同帧不需要 OnUpdate）。
            _tickSnapshot.Clear();
            _tickSnapshot.AddRange(_forms.Values);
            for (int i = 0; i < _tickSnapshot.Count; i++)
            {
                var form = _tickSnapshot[i];
                if (form != null && form.State == UIFormState.Active) form.RaiseUpdate(deltaTime);
            }

            // 转场推进放**真帧末**：既让界面 OnUpdate 里发起的请求同帧末生效，
            // 也保证收尾时同步恢复的续体（EnterClosing/Recycle）不会撞上面的字典枚举。
            _transitions.Tick(deltaTime);
        }

        public string StatsName => "UI";

        public void Snapshot(Dictionary<string, string> into)
        {
            int active = 0, covered = 0, paused = 0, recycled = 0, loading = 0;
            foreach (var form in _forms.Values)
            {
                switch (form.State)
                {
                    case UIFormState.Active: active++; break;
                    case UIFormState.Covered: covered++; break;
                    case UIFormState.Paused: paused++; break;
                    case UIFormState.Recycled: recycled++; break;
                    case UIFormState.Loading: loading++; break;
                }
            }
            into["打开"] = active.ToString();
            into["遮盖"] = covered.ToString();
            into["暂停"] = paused.ToString();
            into["池中"] = recycled.ToString();
            into["加载中"] = loading.ToString();
            into["待更新"] = StaleLogicCount.ToString();
            into["转场"] = _transitions.Phase.ToString();
            into["转场队列"] = _transitions.QueueLength.ToString();
        }
    }
}
