using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                    throw new InvalidOperationException($"Entity[{Id}]: {msg}");
#else
                    Log.Error($"Entity[{Id}]: {msg}", "Entity");
#endif
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
    /// 实体壳（M4 §2.8，手册步骤 6）：句柄制实体管理叠在通用 <see cref="GameObjectPool"/> 之上
    /// （2026-09-13 提炼：池化内核归 Unity 层通用池，本类只留实体语义——Reserve/竞态表/句柄制）。
    /// 实体 = 视觉表现件，逻辑回 C# 玩法系统（设计方案 §4.1）——**不转发 Lua**。
    /// 竞态语义（GF EntitiesToReleaseOnLoad 同款）：加载在途收到 Hide → 记入竞态表 →
    /// 加载完成后取消显示（直接出返回 null）。生命周期回调 = IPoolLifecycle（通用池驱动）。
    /// </summary>
    public sealed class EntityService : IModuleStats
    {
        private readonly GameObjectPool _pool = new GameObjectPool();
        private readonly Dictionary<int, EntityHandle> _active = new Dictionary<int, EntityHandle>(16);
        private readonly HashSet<int> _inFlight = new HashSet<int>();           // 加载在途
        private readonly HashSet<int> _releaseOnLoad = new HashSet<int>();      // 加载竞态表
        private readonly Dictionary<int, int> _parentOf = new Dictionary<int, int>(8);                        // 挂接：child → parent
        private readonly Dictionary<int, List<int>> _attachments = new Dictionary<int, List<int>>(8);         // 挂接：parent → [child]（容器可枚举）
        private readonly Func<string, CancellationToken, UniTask<GameObject>> _loadPrefab;   // 加载口（G1：绑 PrefabLeaseCache——租约持有；缺省静态门面为迁移期兼容）
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
        /// **必注入**：原先的 `null → AssetService` 静态回退已删（《客户端总设计》§5.1 逻辑边界）——
        /// 它让实体服务（Runtime）反向依赖 YooAsset 适配器。全部调用点本就传了 loader（生产/测试皆然）。</param>
        public EntityService(Func<string, CancellationToken, UniTask<GameObject>> loadPrefab)
        {
            _loadPrefab = loadPrefab ?? throw new ArgumentNullException(nameof(loadPrefab));
        }

        /// <summary>
        /// 关闭释放面（宿主关闭）：取消在途加载（链接取消令牌）、回收全部活体、清空竞态表、排空实例池。
        /// 幂等。收回调链会连锁收子件——与正常回收同一路径（不留第二套语义）。
        /// </summary>
        public void Shutdown()
        {
            if (IsShutdown) return;
            IsShutdown = true;

            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }   // 取消在途加载

            var handles = new List<int>(_active.Keys);
            for (int i = 0; i < handles.Count; i++)
                if (_active.ContainsKey(handles[i]))          // 级联回收可能已带走子件——跳过快照里已消失的
                    HideInternal(handles[i]);

            _releaseOnLoad.Clear();
            _inFlight.Clear();
            _attachments.Clear();
            _parentOf.Clear();
            _pool.Clear();                                                     // 排空驻留实例（池根随之销毁）

            _lifetime.Dispose();
            Log.Info($"实体壳已关闭（回收 {handles.Count} 个活体，池已排空）", "Entity");
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

        /// <summary>显示实体（指定句柄）：池命中直取（零加载）；未命中加载 → 竞态表命中则取消（返回 null）。</summary>
        public async UniTask<EntityHandle> ShowAsync(int handleId, string location, Transform parent = null, CancellationToken ct = default)
        {
            if (_active.ContainsKey(handleId))
                throw new InvalidOperationException($"实体句柄 {handleId} 已在使用（先 Hide 再 Show，§3.4 fail-fast）");

            // 池命中：零加载直取（生命周期 OnSpawn 由通用池驱动）
            if (_pool.TryGet(location, out var pooled, parent))
            {
                var pooledHandle = new EntityHandle(handleId, location, pooled) { Scope = NewEntityScope(handleId) };
                _active[handleId] = pooledHandle;
                Log.Info($"实体[{handleId}] 复用（{location}，池中 {_pool.PooledTotal}）", "Entity");
                return pooledHandle;
            }

            // 未命中：加载在途登记（竞态窗口开启）
            _inFlight.Add(handleId);
            try
            {
                var prefab = await _loadPrefab(location, ct);
                _inFlight.Remove(handleId);
                if (_releaseOnLoad.Remove(handleId))
                {
                                    // 加载期间被 Hide（竞态表命中）——不实例化不显示，直接取消
                    Log.Info($"实体[{handleId}] 加载期间已被 Hide——取消显示（竞态表）", "Entity");
                    return null;
                }

                // 首建：经通用池建桶（打 PooledInstance 标记，后续走复用）
                var go = _pool.Get(location, () => UnityEngine.Object.Instantiate(prefab, parent), parent);
                var handle = new EntityHandle(handleId, location, go) { Scope = NewEntityScope(handleId) };
                _active[handleId] = handle;
                handle.GameObject.GetComponent<IPoolLifecycle>()?.OnSpawn();
                Log.Info($"实体[{handleId}] 显示（{location}，池中 {_pool.PooledTotal}）", "Entity");
                return handle;
            }
            finally
            {
                _inFlight.Remove(handleId);                // 幂等兜底（正常路径上方已移除）
            }
        }

        /// <summary>
        /// 挂接（Attach/Detach 行为契约，M4 实施记录 2026-09-13 三语义定案）：
        /// child 挂到 parent 的命名锚点（锚点 = parent 实体内空 Transform，美术摆位；null = parent 根）。
        /// ① 宿主回收连锁收子件（Hide(parent) → 子件递归 Detach + Hide，归各自池——挂接件泄漏机制上不可发生）；
        /// ② Detach 与 Hide 分离（Detach = 脱离挂接保持显示；回收另走 Hide）；
        /// ③ 允许挂接链（武器→手→角色），容器只记直接父子；**不提供树遍历/子树查询 API**（查询归玩法——
        ///    Transform 原生可查；壳内递归仅为回收连锁的安全网，不对外暴露）。
        /// 自挂/成环抛（防回收递归死循环）；child 已挂他处 = 隐式脱离转移。
        /// </summary>
        public void Attach(int childHandle, int parentHandle, string anchor = null, bool keepWorld = false)
        {
            if (childHandle == parentHandle)
                throw new InvalidOperationException($"实体 {childHandle} 不可挂接自身");
            if (!_active.TryGetValue(childHandle, out var child))
                throw new KeyNotFoundException($"挂接 child 未找到:{childHandle}");
            if (!_active.TryGetValue(parentHandle, out var parent))
                throw new KeyNotFoundException($"挂接 parent 未找到:{parentHandle}");

            for (var up = parentHandle; _parentOf.TryGetValue(up, out var grand); up = grand)
                if (grand == childHandle)
                    throw new InvalidOperationException(
                        $"挂接成环:{childHandle} 是 {parentHandle} 的祖先——回收连锁会死循环（§3.4 fail-fast）");

            Transform anchorT = parent.GameObject.transform;
            if (!string.IsNullOrEmpty(anchor))
            {
                anchorT = anchorT.Find(anchor);
                if (anchorT == null)
                    throw new InvalidOperationException($"挂接锚点不存在:{anchor}（核对 prefab 挂点节点）");
            }

            if (_parentOf.Remove(childHandle, out var old))
                if (_attachments.TryGetValue(old, out var oldList)) oldList.Remove(childHandle);

            child.GameObject.transform.SetParent(anchorT, keepWorld);
            if (!keepWorld)
            {
                child.GameObject.transform.localPosition = Vector3.zero;
                child.GameObject.transform.localRotation = Quaternion.identity;
            }

            _parentOf[childHandle] = parentHandle;
            if (!_attachments.TryGetValue(parentHandle, out var list))
                _attachments[parentHandle] = list = new List<int>(4);
            list.Add(childHandle);
            Log.Info($"实体[{childHandle}] 挂接至 [{parentHandle}]{(anchor != null ? "@" + anchor : "")}", "Entity");
        }

        /// <summary>脱离挂接（幂等）：脱离父级、保留世界位姿、保持显示——回收另走 Hide。</summary>
        public void Detach(int childHandle)
        {
            if (!_parentOf.Remove(childHandle, out var parent)) return;    // 未挂接：幂等
            if (_attachments.TryGetValue(parent, out var list)) list.Remove(childHandle);
            if (_active.TryGetValue(childHandle, out var child))
                child.GameObject.transform.SetParent(null, true);          // 世界位姿保留
            Log.Info($"实体[{childHandle}] 已脱离挂接", "Entity");
        }

        /// <summary>容器可枚举：宿主的直接挂件（只读快照）。</summary>
        public IReadOnlyList<int> GetAttachments(int parentHandle)
            => _attachments.TryGetValue(parentHandle, out var list)
                ? (IReadOnlyList<int>)list.ToArray()
                : Array.Empty<int>();

        /// <summary>隐藏实体：连锁收子件 → 脱离父容器 → 回收。加载在途 → 竞态表；未知句柄 = 幂等 no-op + 计数。</summary>
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

        /// <summary>回收连锁（内部）：① 递归收子件（子件的子件递归；容器随递归变动，拷贝遍历）
        /// → ② 脱离父容器登记 → ③ 池回收自身（OnRecycle + 隐藏 + 归桶）。
        /// 顺序：子件先收（父 OnRecycle 执行时身上已空）、回收传播为生命周期安全网而非树操作。</summary>
        private void HideInternal(int handleId)
        {
            if (_attachments.TryGetValue(handleId, out var children))
            {
                _attachments.Remove(handleId);
                var copy = new List<int>(children);
                foreach (var childId in copy)
                {
                    _parentOf.Remove(childId);
                    HideInternal(childId);
                }
            }
            if (_parentOf.TryGetValue(handleId, out var parentHandle))
            {
                _parentOf.Remove(handleId);
                if (_attachments.TryGetValue(parentHandle, out var list)) list.Remove(handleId);
            }
            var handle = _active[handleId];
            handle.DisposeSubscriptions();                 // 订阅清零在池回收之前：OnRecycle 期间已无事件可打进来
            handle.Scope?.Dispose();                       // 实体作用域收口（取消令牌 + 登记资源）——先于池回收
            handle.Scope = null;
            _pool.Release(handle.GameObject);
            _active.Remove(handleId);
            Log.Info($"实体[{handleId}] 回收（连锁含子件）", "Entity");
        }

        public string StatsName => "Entity";

        public void Snapshot(Dictionary<string, string> into)
        {
            into["活跃"] = _active.Count.ToString();
            into["加载中"] = _inFlight.Count.ToString();
            into["池中"] = _pool.PooledTotal.ToString();
            into["竞态表"] = _releaseOnLoad.Count.ToString();
            into["挂接"] = _parentOf.Count.ToString();
        }
    }
}
