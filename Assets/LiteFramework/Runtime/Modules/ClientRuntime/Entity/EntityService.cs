using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteClient
{
    /// <summary>实体句柄：Hide/查询按句柄走（不裸露 GameObject 之外的生命周期控制）。</summary>
    public sealed class EntityHandle
    {
        public int Id { get; }
        public string Location { get; }
        public GameObject GameObject { get; internal set; }

        /// <summary>
        /// 实体级作用域（《商业级通用客户端框架总设计》§6.2 实体域；《框架先行》§5 接缝 1）：
        /// **谁创建谁取消**——实体随宿主（场景/对局/展示）退出而回收，其上的异步与定时器的取消源就是这里。
        /// 与下方订阅袋同址：取消令牌、资源登记、事件订阅三项都随实体回收一并收口，不留裸根任务。
        /// </summary>
        public ClientScope Scope { get; internal set; }

        private SubscriptionBag _subs;
        private bool _released;

        /// <summary>
        /// 实体级订阅袋（C# 对象的事件订阅生命周期归属）：**随实体回收自动清零**（HideInternal 统一 Dispose）。
        /// 用法：<c>handle.Subscriptions.Add(events.Subscribe&lt;XxxEvent&gt;(OnXxx));</c>
        /// 骨架/玩法系统为某实体订阅事件时一律挂这里，不要裸订阅——否则实体回收后通道仍持回调（泄漏 +
        /// 池化复用后回调打进新占用者）。句柄不复用（Reserve 每次递增），Dispose 后误用会当场抛 ObjectDisposedException。
        /// **归还期护栏**：已回收句柄上取袋子 = 往已回收实体塞订阅（必然泄漏）→ Debug 三宏下当场抛，release 记错误。
        /// </summary>
        public SubscriptionBag Subscriptions
        {
            get
            {
                if (_released)
                {
                    const string msg = "已回收实体的句柄不得再订阅事件（订阅袋不会再被释放 → 必然泄漏）";
                    if (DevBuildFacts.IsDevelopment) throw new InvalidOperationException($"Entity[{Id}]: {msg}");
                    Log.Error($"Entity[{Id}]: {msg}", LogTag.Entity);
                }
                return _subs ??= new SubscriptionBag();
            }
        }

        /// <summary>回收前清零（由 EntityService 在池回收前调用；幂等）。</summary>
        internal void DisposeSubscriptions()
        {
            _released = true;                                  // 先置位：Dispose 委托执行期间的重入订阅当场被拦
            _subs?.Dispose();
            _subs = null;
        }

        internal EntityHandle(int id, string location, GameObject go)
        {
            Id = id;
            Location = location;
            GameObject = go;
        }
    }

    /// <summary>
    /// 实体服务：句柄制实体管理叠在通用 <see cref="GameObjectPool"/> 之上（池化内核归通用池，
    /// 本类只留实体语义——Reserve/竞态表/句柄制）。实体 = 视觉表现件，逻辑回 C# 玩法系统——**不转发 Lua**。
    /// 竞态语义：加载在途收到 Hide → 记入竞态表 → 加载完成后取消显示（直接出返回 null）。
    /// 生命周期回调 = IPoolLifecycle（通用池驱动）。
    /// </summary>
    public sealed class EntityService : IModuleStats
    {
        private readonly GameObjectPool _pool;
        private readonly Dictionary<int, EntityHandle> _active = new Dictionary<int, EntityHandle>(16);
        private readonly HashSet<int> _inFlight = new HashSet<int>();           // 加载在途
        private readonly HashSet<int> _releaseOnLoad = new HashSet<int>();      // 加载竞态表
        private readonly Func<string, CancellationToken, UniTask<GameObject>> _loadPrefab;   // 加载口（装配点绑 PrefabLeaseCache——租约持有）
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();   // 宿主关闭级联（在途加载链接它）
        private int _nextHandle = 1;

        /// <summary>显式宿主作用域（装配点注入；null = 每个实体自建独立作用域，仅靠 <see cref="Hide"/> 回收）。</summary>
        public ClientScope HostScope { get; set; }

        /// <summary>是否已关闭（<see cref="Shutdown"/> 后不再接受新实体）。</summary>
        public bool IsShutdown { get; private set; }

        /// <summary>重复归还计数（诊断：对未知/已回收句柄的 Hide——多次归还是纪律问题，要可见）。</summary>
        public int DuplicateReturns { get; private set; }

        /// <summary>池中闲置实例数（诊断/泄漏断言：反复循环后应回到预期基线，不无界增长）。</summary>
        public int PooledTotal => _pool.PooledTotal;

        /// <summary>当前活体实体数（诊断/基线断言）。</summary>
        public int ActiveCount => _active.Count;

        /// <summary>加载在途数（诊断：关闭后应为 0）。</summary>
        public int InFlightCount => _inFlight.Count;

        /// <param name="loadPrefab">prefab 加载口（装配点绑 <c>PrefabLeaseCache</c>——实例池常驻期间持租约）。
        /// **必注入**——本类不回落静态门面，避免实体服务（Runtime）反向依赖 YooAsset 适配器。</param>
        /// <param name="poolRoot">池根容器（null = 自建 <c>[GameObjectPool]</c> 根并 DontDestroyOnLoad）。
        /// 测试/Player 注入受控容器，避免自建根跨用例驻留（同 VfxService 的池根注入形态）。</param>
        public EntityService(Func<string, CancellationToken, UniTask<GameObject>> loadPrefab, Transform poolRoot = null)
        {
            _loadPrefab = loadPrefab ?? throw new ArgumentNullException(nameof(loadPrefab));
            _pool = new GameObjectPool(poolRoot);
        }

        /// <summary>
        /// 关闭释放面（宿主关闭）：取消在途加载（链接取消令牌）、回收全部活体、清空竞态表、排空实例池。
        /// 幂等。
        /// </summary>
        public void Shutdown()
        {
            if (IsShutdown) return;
            IsShutdown = true;

            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }   // 取消在途加载

            var handles = new List<int>(_active.Keys);
            for (int i = 0; i < handles.Count; i++)
                if (_active.ContainsKey(handles[i]))          // 回收期间可能已带走其他句柄——跳过快照里已消失的
                    HideInternal(handles[i]);

            _releaseOnLoad.Clear();
            _inFlight.Clear();
            _pool.Clear();                                                     // 排空驻留实例（池根随之销毁）

            _lifetime.Dispose();
            Log.Info($"实体服务已关闭（回收 {handles.Count} 个活体，池已排空）", LogTag.Entity);
        }

        /// <summary>宿主关闭级联令牌（在途加载链接它——关闭即取消，不靠等待）。</summary>
        public CancellationToken LifetimeToken => _lifetime.Token;

        /// <summary>实体作用域（注入宿主作用域则建子作用域——宿主退出级联取消实体上的全部工作；
        /// 未注入则各自独立，仅靠回收点 Dispose）。</summary>
        private ClientScope NewEntityScope(int handleId)
        {
            string name = $"Entity[{handleId}]";
            return HostScope != null ? HostScope.CreateChild(name) : new ClientScope(name);
        }

        /// <summary>预占句柄（竞态场景用：先 Reserve → ShowAsync(id) → 任意时刻 Hide(id)）。</summary>
        public int Reserve() => _nextHandle++;

        /// <summary>显示实体（简单路径：内部 Reserve）。</summary>
        public UniTask<EntityHandle> ShowAsync(string location, Transform parent = null, CancellationToken ct = default)
            => ShowAsync(Reserve(), location, parent, ct);

        /// <summary>显示实体（指定句柄）：池命中直取（零加载）；未命中加载 → 竞态表命中则取消（返回 null）。
        /// 关闭后拒绝新实体（显性失败，同 SceneService 口径）；在途加载链接生命周期令牌——
        /// 关闭即真取消，不依赖调用方传令牌；加载完成收尾复查（关闭 → 不实例化；宿主已退出 → 显性失败）。</summary>
        public async UniTask<EntityHandle> ShowAsync(int handleId, string location, Transform parent = null, CancellationToken ct = default)
        {
            if (IsShutdown)
                throw new ObjectDisposedException(nameof(EntityService), $"实体服务已关闭——不再接受新实体（句柄 {handleId}）");
            if (HostScope != null && HostScope.IsDisposed)
                throw new ObjectDisposedException(nameof(HostScope), "宿主作用域已释放——实体不得再显示");

            if (_active.ContainsKey(handleId))
                throw new InvalidOperationException($"实体句柄 {handleId} 已在使用（先 Hide 再 Show，§3.4 fail-fast）");

            // 池命中：零加载直取（生命周期 OnSpawn 由通用池驱动）
            if (_pool.TryGet(location, out var pooled, parent))
            {
                var pooledHandle = new EntityHandle(handleId, location, pooled) { Scope = NewEntityScope(handleId) };
                _active[handleId] = pooledHandle;
                Log.Info($"实体[{handleId}] 复用（{location}，池中 {_pool.PooledTotal}）", LogTag.Entity);
                return pooledHandle;
            }

            // 未命中：加载在途登记（竞态窗口开启）；加载口同时挂调用方取消与服务生命周期——关闭即真取消
            _inFlight.Add(handleId);
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            try
            {
                var prefab = await _loadPrefab(location, linked.Token);
                _inFlight.Remove(handleId);
                if (IsShutdown)
                {
                    // 加载完成时服务已关闭：loader 可能不观察取消令牌——按竞态同语义取消显示，
                    // 不实例化（否则幽灵实体复活在已关闭的壳上，同 VfxService 收尾复查口径）
                    Log.Info($"实体[{handleId}] 加载完成时服务已关闭——取消显示", LogTag.Entity);
                    return null;
                }
                if (_releaseOnLoad.Remove(handleId))
                {
                                    // 加载期间被 Hide（竞态表命中）——不实例化不显示，直接取消
                    Log.Info($"实体[{handleId}] 加载期间已被 Hide——取消显示（竞态表）", LogTag.Entity);
                    return null;
                }
                if (HostScope != null && HostScope.IsDisposed)
                    throw new ObjectDisposedException(nameof(HostScope),
                        $"实体[{handleId}] 加载完成时宿主作用域已退出——显性失败不静默（§6 样例③「加载后 Owner 已退出」）");
                linked.Token.ThrowIfCancellationRequested();   // 调用方取消且 loader 未观察令牌 → 类型化取消

                // 首建：经通用池建桶（打 PooledInstance 标记，后续走复用）——OnSpawn 由池 Get 驱动
                //（与池命中路径一致，各恰一次；此处不得再手动调用——§5.1 双 OnSpawn 缺陷已修）
                var go = _pool.Get(location, () => UnityEngine.Object.Instantiate(prefab, parent), parent);
                var handle = new EntityHandle(handleId, location, go) { Scope = NewEntityScope(handleId) };
                _active[handleId] = handle;
                Log.Info($"实体[{handleId}] 显示（{location}，池中 {_pool.PooledTotal}）", LogTag.Entity);
                return handle;
            }
            finally
            {
                linked.Dispose();
                _inFlight.Remove(handleId);                // 幂等兜底（正常路径上方已移除）
            }
        }

        /// <summary>隐藏实体：回收活体。加载在途 → 竞态表；未知句柄 = 幂等 no-op + 计数。</summary>
        public void Hide(int handleId)
        {
            if (_active.TryGetValue(handleId, out _))
            {
                HideInternal(handleId);
                return;
            }
            if (_inFlight.Contains(handleId))
            {
                _releaseOnLoad.Add(handleId);              // 加载竞态表：完成后立即取消
                return;
            }

            // 重复归还/未知句柄：**幂等 no-op**（重复归还不是错误状态，但也绝不该静默——
            // 计数留痕，供诊断提示调用方纪律问题）。不抛异常：清理路径抛异常会让关停链断在半路。
            DuplicateReturns++;
        }

        /// <summary>回收（内部）：订阅清零 → 作用域收口 → 池回收。</summary>
        private void HideInternal(int handleId)
        {
            var handle = _active[handleId];
            handle.DisposeSubscriptions();                 // 订阅清零在池回收之前：OnRecycle 期间已无事件可打进来
            handle.Scope?.Dispose();                       // 实体作用域收口（取消令牌 + 登记资源）——先于池回收
            handle.Scope = null;
            _pool.Release(handle.GameObject);
            _active.Remove(handleId);
            Log.Info($"实体[{handleId}] 回收（连锁含子件）", LogTag.Entity);
        }

        public string StatsName => "Entity";

        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();                                    // 契约：实现 Clear + 填（HUD 不清，全实现统一）
            into["活跃"] = _active.Count.ToString();
            into["加载中"] = _inFlight.Count.ToString();
            into["池中"] = _pool.PooledTotal.ToString();
            into["竞态表"] = _releaseOnLoad.Count.ToString();
        }
    }
}
