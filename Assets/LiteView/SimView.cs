using System;
using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 战斗表现视图（《联机战斗演示专项设计》§3"SimView 负责槽位镜像、远端插值、本地和解衰减和事件静默"；
    /// 《状态同步专项设计》§6.2"本地玩家跟预测位置；远端使用前后快照插值；和解时本地误差衰减，
    /// 远端必要时 snap；复活/传送允许硬切"）。
    ///
    /// **只读 Sim、只写视图**——不写任何 Sim 状态（《状态同步专项设计》§1 原则 3：View → Sim 只有输入和读取）。
    /// 两类来源分别处理：
    /// - **远端**：快照按帧号进入 jitter buffer，渲染帧匀速推进（落后最新快照 <see cref="RenderDelayFrames"/>），
    ///   在前后两份快照间按**真实帧差**插值（丢包不冻结再跳；旧帧/重复帧丢弃）；
    ///   前后间距超 <see cref="SnapDistance"/> 硬切——§6.2"远端必要时 snap"，传送/复活不播成飞人；
    /// - **本地玩家**：跟预测态（0 延迟）；和解后按 <see cref="ViewTransformMath.Decay"/> 指数收敛
    ///   （帧率无关，不微抖）；超过 <see cref="SnapDistance"/> 硬切（复活/传送）。
    ///
    /// 实体视图经 <see cref="EntityViewMap"/> 按槽位索引并池化（槽位是 Sim 的稳定寻址，禁止 swap-remove）。
    /// **资源所有权在调用方**：本类只与实例化的 GameObject 打交道，prefab 由 EntityService/租约缓存持有
    /// （§8.2"租约从加载完成持有至最后一个依赖实例销毁"）。
    ///
    /// **事件面**：本类负责**静默门**的去重（回滚重放/和解会重放帧事件，重复播放 = 一次命中响两次），
    /// 去重后交 <see cref="EventSink"/>；具体表现（VFX/音效/飘字）由编排方接线——本类不调 VFX 服务，
    /// 服务间不互相调用（《动作与特效设计》§2.3）。
    /// </summary>
    public sealed class SimView : ITickable
    {
        /// <summary>视图工厂（按 prefab location 造实例；生产接 EntityService，测试接替身）。</summary>
        public delegate GameObject ViewFactory(string prefabLocation, Transform parent);

        /// <summary>视图回收（生产接 EntityService.Hide；null = 走 EntityViewMap 的自有池）。</summary>
        public delegate void ViewRecycler(GameObject view);

        /// <summary>静默门放行的帧事件回调（编排方接线：VFX/音效/飘字）。</summary>
        public delegate void FrameEventSink(in FrameEvent e);

        /// <summary>对局实体视图 prefab（模型只用 CombatGirlsCharacterPack——真角色视图由该包模型 +
        /// 该包 Rifle_Controller 构成；动画侧 CombatGirlsAnimationProfile.ViewPrefabPath 引用本常量——
        /// prefab 路径单源在此）。
        /// prefab 本体住 `Assets/ShareResource/Characters/`，**不在 `Assets/CombatGirlsCharacterPack/` 内**——该包只是它的
        /// **依赖来源**（贴图/网格/动画/Avatar 全在该包内），故收集组必须**额外覆盖 `Assets/ShareResource/Characters`**，
        /// 否则 YooAsset 拿不到它（收集按目录走）；依赖资产仍由 `Assets/CombatGirlsCharacterPack/Runtime` 收。
        /// 资源包未入库的克隆加载失败 → ProcedureBattle 回退程序化灰盒（可复现降级，勿删兜底）。</summary>
        public const string DefaultEntityPrefab = "Assets/ShareResource/Characters/Player(Rifle).prefab";

        /// <summary>快照间隔（秒）——插值窗口时长，由 <see cref="SimConfig.SnapshotHz"/> 派生。</summary>
        public static float SnapshotInterval => 1f / SimConfig.SnapshotHz;

        private readonly SimWorldState _sim;                  // 本地预测态（只读）
        private readonly EntityViewMap _views;
        private readonly ICameraService _camera;              // 相机端口（实现住 Platform.Unity 适配器——本层不认识 Cinemachine）

        // ---- 插值源（远端）----
        // 快照缓冲（jitter buffer）：按**快照帧号**推进渲染帧，而不是"收到包就重置窗口"。
        // 丢包时前后快照间隔变大，alpha 按真实帧差铺满整段（不冻结再跳）；乱序/重复包在入口丢弃。
        private const int SnapshotBufferCapacity = 8;
        private readonly List<SimWorldStateSnapshot> _snapshots = new List<SimWorldStateSnapshot>(SnapshotBufferCapacity);
        private float _renderFrame;                 // 渲染帧（tick 帧轴；= 玩家看到的"过去"）
        private bool _hasRenderFrame;

        // ---- 本地表现（预测 + 和解衰减）----
        private SimVector3 _localDisplay;
        private float _localYaw;
        private bool _hasLocalDisplay;
        private int _localLifeStart;                           // 本地当前生命起始帧（复活检测）
        private bool _hasLocalLife;
        private CharacterController _localCc;                 // 本地视图物理代理（CC.Move 收敛到衰减目标——见 PlaceLocal/EnsureLocalCc）

        /// <summary>
        /// 渲染相对最新快照的**落后帧数**（tick 帧；默认 <see cref="SimConfig.InterpFrames"/> = 66ms）。
        /// 这是一个真正的 jitter buffer：渲染帧按 <see cref="SimConfig.TickRate"/> 匀速前进，
        /// 上限 = 最新快照帧 − 本值；网络抖动/丢一包不会让远端停走或跳步。0 = 测试用的即时模式。
        /// </summary>
        public int RenderDelayFrames { get; set; } = SimConfig.InterpFrames;

        /// <summary>当前渲染帧（tick 帧轴）——延迟补偿的 `viewFrame` 单源（-1 = 尚无快照）。</summary>
        public int RenderFrame => _hasRenderFrame ? Mathf.FloorToInt(_renderFrame) : -1;

        /// <summary>缓冲中的快照数（诊断/测试）。</summary>
        public int BufferedSnapshotCount => _snapshots.Count;

        /// <summary>是否已收到过权威快照（远端是否走快照插值——动画测速平滑只对这类槽位生效）。</summary>
        public bool HasSnapshotSource => _snapshots.Count > 0;

        /// <summary>硬切距离阈值（米），两处共用：① 本地和解衰减超过它直接落位（复活/传送）；
        /// ② 远端前后两份快照的位置差超过它同样硬切（§6.2"远端必要时 snap"——否则权威侧的
        /// 传送/复活级跳变会被插值播成横穿地图的"飞人"）。≤0 = 永不硬切。</summary>
        public float SnapDistance = 3f;

        /// <summary>和解误差衰减速率（越大收敛越快；帧率无关）。</summary>
        public float ReconcileSharpness = 10f;

        /// <summary>事件静默闸：帧号 ≤ 该值的帧事件不派发（回滚重放/和解去重的帧闸）。</summary>
        public int SilenceUntilFrame { get; private set; } = -1;

        /// <summary>本地玩家实体 Id（0 = 未对齐——对齐前无本地表现，相机也不跟）。</summary>
        public long LocalEntityId { get; set; }

        /// <summary>静默门放行的事件回调（编排方接线表现；null = 只做去重统计）。</summary>
        public FrameEventSink EventSink { get; set; }

        /// <summary>本地玩家**表现**位置（相机跟随目标；缓存避免调用方每帧重算）。</summary>
        public Vector3 LocalDisplayPosition => new Vector3(_localDisplay.X, _localDisplay.Y, _localDisplay.Z);

        /// <summary>本地表现是否已初始化（首帧直接落位，不做衰减）。</summary>
        public bool HasLocalDisplay => _hasLocalDisplay;

        /// <summary>已建立的实体视图数（泄漏断言：应随活体数回基线）。</summary>
        public int ViewCount => _views.Count;

        /// <summary>取槽位视图（诊断/HUD/测试用；未建返回 false）。</summary>
        public bool TryGetView(int slotIndex, out GameObject view) => _views.TryGet(slotIndex, out view);

        /// <summary>
        /// 实体 Id → 槽位（**帧事件消费的寻址口**，§8"主体 → 实体槽位 → 该实体的播放器"）：
        /// 事件主体是 Id（跨对象复用代次稳定），而视图/播放器按槽位寻址——消费者用本方法完成那一步。
        /// 未解析（已回收 / 代次过期 / 越界）返回 false，消费者据此**丢弃事件**（不猜、不补播）。
        /// </summary>
        public bool TryGetSlot(long entityId, out int slotIndex) => _sim.TryResolve(entityId, out slotIndex);

        /// <summary>槽位是否是本地玩家（动画测速平滑只对快照驱动的远端槽位生效）。</summary>
        public bool IsLocalSlot(int slotIndex)
            => LocalEntityId != 0 && _sim.TryResolve(LocalEntityId, out int localSlot) && localSlot == slotIndex;

        /// <summary>被静默丢弃的帧事件数（诊断：回滚重放的去重命中数）。</summary>
        public int SilencedEvents { get; private set; }

        /// <summary>已派发的帧事件数（诊断）。</summary>
        public int DeliveredEvents { get; private set; }

        public SimView(SimWorldState sim, Transform root, ViewFactory factory, ViewRecycler recycler = null,
            ICameraService camera = null, Func<EntitySlot, string> prefabLocationOf = null)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _camera = camera;
            _views = new EntityViewMap(factory, recycler, root, prefabLocationOf);
        }

        // ---- 馈入点 ----

        /// <summary>
        /// 权威快照到达（<c>SnapshotReassembler</c> 重建出的完整状态）：入**帧号有序**缓冲。
        /// 渲染帧 = 最新快照帧 − <see cref="RenderDelayFrames"/>，由 <see cref="Tick"/> 匀速推进——
        /// 玩家看到的是"比最新权威晚一档"的过去，才有两份快照可插、且网络抖动不改变远端速度。
        /// 旧帧/重复帧直接丢弃（RoomClient 已在入口做同一守卫；此处防御直构/测试路径）。
        /// </summary>
        public void OnAuthoritativeSnapshot(SimWorldStateSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (_snapshots.Count > 0 && snapshot.Frame <= _snapshots[_snapshots.Count - 1].Frame) return;

            _snapshots.Add(snapshot);
            if (_snapshots.Count > SnapshotBufferCapacity) _snapshots.RemoveAt(0);
            if (!_hasRenderFrame)
            {
                _renderFrame = snapshot.Frame;   // 首个快照：先在当前帧落位，随后由缓冲延迟接管
                _hasRenderFrame = true;
            }
        }

        /// <summary>重连恢复：清空插值缓冲（旧快照属于上一条时间线），下一份快照重新起锚。</summary>
        public void ResetInterpolation()
        {
            _snapshots.Clear();
            _renderFrame = 0f;
            _hasRenderFrame = false;
        }

        /// <summary>和解发生（接 <c>RollbackSim.OnReconcile</c>）：本地表现交给衰减收敛，
        /// 只推进静默闸——重放段的事件不重播。</summary>
        public void OnReconcile(int frame) => Silence(Math.Max(SilenceUntilFrame, frame));

        /// <summary>回滚重放（接 <c>RollbackSim.OnRollback</c>）：重放段事件一律静默。</summary>
        public void OnRollback(int replayedToFrame) => Silence(Math.Max(SilenceUntilFrame, replayedToFrame));

        /// <summary>外部强制静默到某帧（重连恢复快照批量应用等场景）。</summary>
        public void Silence(int untilFrame) { if (untilFrame > SilenceUntilFrame) SilenceUntilFrame = untilFrame; }

        /// <summary>本地实体对齐（首份快照按 Slot==PlayerId 解析后调用）——重置本地表现，下一帧直接落位。</summary>
        public void AlignLocal(long entityId)
        {
            if (entityId == LocalEntityId) return;
            LocalEntityId = entityId;
            _hasLocalDisplay = false;          // 换实体/重连：重新落位（不从上一条命的位置飞过去）
            _hasLocalLife = false;             // 生命基线随实体重取（不把上一实体当作复活）
            _localCc = null;                   // CC 代理一并失效（视图重建后 EnsureLocalCc 重新解析）
        }

        // ---- 每帧驱动 ----

        /// <summary>
        /// 每渲染帧：① 视图增删 → ② 远端插值写位 → ③ 本地衰减收敛 → ④ 相机跟随。
        ///
        /// **帧事件不在这里取**：Sim 的帧事件是帧内瞬态，<see cref="FrameDriver"/> 在每个逻辑帧结束后
        /// 立即清空缓冲——渲染帧轮询要么读不到、要么重复读。事件经
        /// <see cref="OnFrameEvents"/> 在**逻辑帧边界**交付（追帧时一帧一次），由调用方接线到
        /// <c>RollbackSim.OnFrameEvents</c>（见 <c>BattleContext.AttachView</c>）。
        /// <paramref name="realDelta"/> 是真实帧间隔——表现走真实时间（不受逻辑时钟暂停影响）。
        /// </summary>
        public void Tick(float realDelta)
        {
            float dt = realDelta > 0f ? realDelta : 0f;
            AdvanceRenderFrame(dt);

            SyncViews();
            UpdateRemoteTransforms();
            UpdateLocalDisplay(dt);
            UpdateCamera(dt);
        }

        private void SyncViews()
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                bool alive = _sim.IsAlive(i);
                bool hasView = _views.TryGet(i, out _);

                if (alive && !hasView)
                {
                    ref EntitySlot slot = ref _sim.Entities[i];
                    var view = _views.Create(i, slot);
                    if (view != null)
                    {
                        Place(view.transform, in slot.Pos, slot.Yaw);
                        // **远端实体禁 CC**（CC/碰撞体归预制体配置，运行时只管语义）：
                        // 实体间碰撞 Sim 未建模（只落静态障碍）——远端实例的 CC 是实心胶囊，
                        // 会挡本地 CC 造成表现/权威分叉。本地实体在 <see cref="EnsureLocalCc"/> 再启用
                        // （首份快照对齐 LocalEntityId 之前这里可能先禁一次，对齐后恢复——竞态安全）。
                        var cc = view.GetComponent<CharacterController>();
                        if (cc != null && slot.Id != LocalEntityId) cc.enabled = false;
                    }
                }
                else if (!alive && hasView)
                {
                    _views.Release(i);         // 回收（宿主回收点——挂点特效由回收方/编排方清理）
                }
            }
        }

        private void UpdateRemoteTransforms()
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!_views.TryGet(i, out var view)) continue;

                ref EntitySlot slot = ref _sim.Entities[i];
                if (slot.Id != 0 && slot.Id == LocalEntityId) continue;   // 本地在 UpdateLocalDisplay 处理

                if (!TryInterpolate(slot.Id, out var pos, out float yaw))
                {
                    pos = slot.Pos;                                        // 缺插值源（首帧/单快照）：退回预测态位置
                    yaw = slot.Yaw;
                }
                Place(view.transform, in pos, yaw);
            }
        }

        /// <summary>
        /// 渲染帧推进：按真实时间 × tick 频率匀速前进，封顶"最新快照 − <see cref="RenderDelayFrames"/>"
        /// （保持一档缓冲）；快照停摆时停在缓冲上界，不外推。初始从首个快照帧起步——不会凭空跳。
        /// </summary>
        private void AdvanceRenderFrame(float dt)
        {
            if (!_hasRenderFrame || _snapshots.Count == 0) return;

            float newest = _snapshots[_snapshots.Count - 1].Frame;
            float oldest = _snapshots[0].Frame;
            float cap = newest - RenderDelayFrames;
            if (cap < oldest) cap = oldest;                 // 快照尚不足以撑满缓冲：先贴近现有最新

            float next = _renderFrame + dt * SimConfig.TickRate;
            if (next > cap) next = Mathf.Max(_renderFrame, cap);
            _renderFrame = next;

            // 只保留仍可能参与插值/最近历史的快照（至少保留一帧 <= 渲染帧 + 其后继）。
            while (_snapshots.Count > 2 && _snapshots[1].Frame <= _renderFrame)
                _snapshots.RemoveAt(0);
        }

        /// <summary>取渲染帧所在快照对（from.Frame ≤ RenderFrame &lt; to.Frame；末端 to = from 即停最新）。</summary>
        private bool TryGetRenderPair(out SimWorldStateSnapshot from, out SimWorldStateSnapshot to, out float alpha)
        {
            from = null;
            to = null;
            alpha = 0f;
            if (_snapshots.Count == 0) return false;

            int fromIndex = -1;
            for (int i = 0; i < _snapshots.Count; i++)
            {
                if (_snapshots[i].Frame > _renderFrame) break;
                fromIndex = i;
            }
            if (fromIndex < 0)
            {
                from = _snapshots[0];
                to = from;
                return true;
            }

            from = _snapshots[fromIndex];
            to = fromIndex + 1 < _snapshots.Count ? _snapshots[fromIndex + 1] : from;
            if (to.Frame > from.Frame)
                alpha = Mathf.Clamp01((_renderFrame - from.Frame) / (to.Frame - from.Frame));
            return true;
        }

        /// <summary>渲染帧对应的快照（离散事实读口：瞄准/开火窗/死亡/生命起始帧与画面同拍）。</summary>
        private bool TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)
        {
            snapshot = null;
            return TryGetRenderPair(out snapshot, out _, out _);
        }

        /// <summary>远端插值：渲染帧在两份快照之间按**帧差**插值（丢包时铺满真实间隔，不冻结再跳）；
        /// 快照停摆时停在最新（不外推）。</summary>
        private bool TryInterpolate(long entityId, out SimVector3 pos, out float yaw)
        {
            pos = default;
            yaw = 0f;
            if (!TryGetRenderPair(out SimWorldStateSnapshot from, out SimWorldStateSnapshot to, out float alpha))
                return false;

            bool hasFrom = TryFind(from, entityId, out var a);
            bool hasTo = TryFind(to, entityId, out var b);
            if (!hasFrom && !hasTo) return false;
            if (!hasFrom) { pos = b.Pos; yaw = b.Yaw; return true; }   // 新出现（出生/进 AOI）：直接用新快照
            if (!hasTo)   { pos = a.Pos; yaw = a.Yaw; return true; }   // 将消失（回收前一帧）：保持旧快照

            // 远端硬切（§6.2"远端必要时 snap；复活/传送允许硬切"）：前后两份快照间距超阈值 =
            // 权威侧发生过传送/复活级跳变——照常插值会把它播成横穿地图的"飞人"，直接对齐目标快照。
            if (ViewTransformMath.ShouldSnap(a.Pos, b.Pos, SnapDistance))
            {
                pos = b.Pos;
                yaw = b.Yaw;
                return true;
            }

            pos = ViewTransformMath.Lerp(a.Pos, b.Pos, alpha);
            yaw = ViewTransformMath.YawLerp(a.Yaw, b.Yaw, alpha);
            return true;
        }

        private void UpdateLocalDisplay(float dt)
        {
            if (LocalEntityId == 0) return;
            if (!_sim.TryResolve(LocalEntityId, out int slotIndex)) return;

            ref EntitySlot slot = ref _sim.Entities[slotIndex];

            // 生命更替（复活）：显式把本地视图从死亡表现里拉回来——渲染器禁用/离屏剔除残值一并清掉。
            int lifeStart = slot.LifeStartFrame;
            if (!_hasLocalLife) { _hasLocalLife = true; _localLifeStart = lifeStart; }
            else if (lifeStart != _localLifeStart)
            {
                _localLifeStart = lifeStart;
                ResetLocalViewAfterRespawn(slotIndex, lifeStart);
            }

            if (!_hasLocalDisplay)
            {
                _localDisplay = slot.Pos;              // 首帧/换实体：直接落位
                _localYaw = slot.Yaw;
                _hasLocalDisplay = true;
                _camera?.Reset();                      // 对齐/重建：下一帧相机直接落位（不从上个实体位置飞过来）
            }
            else if (ViewTransformMath.ShouldSnap(_localDisplay, slot.Pos, SnapDistance))
            {
                _localDisplay = slot.Pos;              // 复活/传送：硬切
                _localYaw = slot.Yaw;
                _camera?.Reset();                      // 相机同帧 warp——否则阻尼追赶期间角色在画面外
            }
            else
            {
                _localDisplay = ViewTransformMath.Decay(_localDisplay, slot.Pos, ReconcileSharpness, dt);
                _localYaw = ViewTransformMath.YawDecay(_localYaw, slot.Yaw, ReconcileSharpness, dt);
            }

            if (_views.TryGet(slotIndex, out var view))
                PlaceLocal(view, dt);
        }

        /// <summary>复活时强制恢复本地视图可见性：死亡/动画片段可能把渲染器或包围盒留在旧姿态，
        /// 仅靠相机 snap 不足以保证模型立刻可见。这里是表现兜底（不改 Sim/判定）。</summary>
        private void ResetLocalViewAfterRespawn(int slotIndex, int lifeStart)
        {
            if (!_views.TryGet(slotIndex, out GameObject view) || view == null) return;
            if (!view.activeSelf) view.SetActive(true);

            var renderers = view.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
                if (renderers[i] is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }
            Log.Info($"local-respawn life={lifeStart} view={view.name} renderers={renderers.Length}", LogTag.View);
        }

        /// <summary>本地视图落位：**位置走 CharacterController.Move** 收敛到
        /// 衰减目标（Unity 物理管贴地/防穿模/台阶——Sim 判定之外的场景几何不再穿透），旋转直写。
        /// CC **由预制体配置**（本类只解析消费，不建件不设参，见 <see cref="EnsureLocalCc"/>）；
        /// 预制体没配 CC（灰盒视图/测试装配）退回 <see cref="Place"/> 直落。</summary>
        private void PlaceLocal(GameObject view, float dt)
        {
            CharacterController cc = EnsureLocalCc(view);
            if (cc == null)
            {
                Place(view.transform, in _localDisplay, _localYaw);
                return;
            }

            Vector3 target = new Vector3(_localDisplay.X, _localDisplay.Y, _localDisplay.Z);
            Vector3 delta = target - view.transform.position;
            if (delta.sqrMagnitude > 0.0000001f) cc.Move(delta);   // 硬切时 delta 大——CC 一样一次 Move 到位（无墙内复活点，见出生清障）
            view.transform.rotation = Quaternion.Euler(0f, 90f - _localYaw * Mathf.Rad2Deg, 0f);   // 旋转不归物理（同 Place：90° − yaw）
        }

        /// <summary>取本地视图的物理代理（**纯消费**：CC 与碰撞体由预制体配置——
        /// 这里不建件、不设参，只解析＋为本地实例启用；远端实例在 <see cref="SyncViews"/> 建立时禁用）。
        /// 预制体没配 CC（灰盒视图/测试装配/尚未配置）→ 返回 null，<see cref="PlaceLocal"/> 退回
        /// <see cref="Place"/> 直落。
        /// **耦合提示**：CC 胶囊参数是 Sim 物理体（BodyRadius/HitscanHeight）的**烘焙单源**——
        /// 经工具 `BodyCylinderBaker` 生成 `BodyBake.g.cs` 供两端消费；改 CC 后重跑
        /// `LiteGame/烘焙身位圆柱`，EditMode 对账用例（`BodyBakeEditModeTests`）卡漂移。</summary>
        private CharacterController EnsureLocalCc(GameObject view)
        {
            if (_localCc != null && !ReferenceEquals(_localCc, null)) return _localCc;

            var cc = view.GetComponent<CharacterController>();
            if (cc != null && !cc.enabled) cc.enabled = true;   // 本地实例启用（远端默认禁——见 SyncViews）
            _localCc = cc;                                       // null 也缓存：没配 CC 的视图每帧重查成本低（一次 GetComponent）
            return cc;
        }

        private void UpdateCamera(float dt)
        {
            if (_camera == null || !_hasLocalDisplay) return;
            // **主相机只看角色本体**：
            // 焦点 = 本地表现位置（角色根）。瞄准相机（ADS）由流程经 <see cref="ICameraService.SetAiming"/>
            // 接管，跟随目标与主相机同源（相机服务侧同一焦点），不引用预制体参考点。
            // 相机构图（肩偏移/阻尼/FOV）归 vcam 场景配置，代码里没有第二处事实源。
            // 固定斜视角（无轨道旋转源——《固定斜视角射击方案专项设计》§7）：焦点朝向对主相机是
            // 空操作，仍随焦点喂给实现——供瞄准机 Composer 的角色系构图偏移（TrackedObjectOffset）使用。
            _camera.Follow(LocalDisplayPosition, FacingRotation(_localYaw), dt);   // 平滑/档位归实现（Cinemachine 由 vcam 配置表达）
        }

        /// <summary>
        /// 帧事件交付（接 <c>RollbackSim.OnFrameEvents</c>——**逻辑帧边界**，事件缓冲尚未被清空）。
        /// 静默门内的帧一律丢弃（回滚重放/和解会重放事件，重复播放 = 一次命中响两次），
        /// 放行的交 <see cref="EventSink"/> 接线表现。
        /// </summary>
        public void OnFrameEvents(SimWorldState state)
        {
            if (state == null) return;
            FrameEventBuffer events = state.Events;
            for (int i = 0; i < events.Count; i++)
            {
                FrameEvent e = events.Items[i];

                if (state.Frame <= SilenceUntilFrame)   // 静默闸：重放/和解段不重播一次性副作用
                {
                    SilencedEvents++;
                    continue;
                }

                DeliveredEvents++;
                EventSink?.Invoke(in e);
            }
        }
        /// <summary>
        /// 实体是否处于瞄准态（右键 ADS）。**只读 Sim 事实**——表现不读输入设备，本方法就是那条边界的入口：
        /// - **本地**：取预测态（预测帧保留连续位，所以与本地手感同帧）；
        /// - **远端**：取**最新权威快照**的位（离散位"取新不取插值"——位在前后快照之间的中间态无意义；
        ///   代价是远端举枪比插值姿态略早，量级 = 渲染延迟 <see cref="RenderDelayFrames"/> 帧）。
        /// 槽位越界 / 首快照未到 / 槽位已死 → false（不是瞄准态，而不是抛）。
        /// </summary>
        public bool IsAiming(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            // 本地实体必须**先按实体 Id 解析槽位**——Id 是"版本&lt;&lt;48 | 槽位"，不能当下标用
            if (LocalEntityId != 0 && _sim.TryResolve(LocalEntityId, out int localSlot) && localSlot == slotIndex)
                return (_sim.Entities[slotIndex].Flags & EntityFlags.Aiming) != 0u;

            if (!TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if ((snapshot.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) == 0u) return false;
            return (snapshot.Entities[slotIndex].Flags & EntityFlags.Aiming) != 0u;
        }

        /// <summary>
        /// 实体是否处于射击语境（开火驻留窗在场——瞄准/扣扳机/成功开火在 Sim 侧统一进窗，
        /// 判据单源 <see cref="InputSystem"/>/<see cref="ShootingSystem"/>）。取数口径同
        /// <see cref="IsAiming"/>：本地取预测态、远端取最新权威快照（窗是快照公共面，
        /// 离散窗值"取新不取插值"）；槽位越界 / 首快照未到 / 槽位不在场 → false。
        /// 表现层经此入口消费权威窗——扣扳机被武器门拦下（空仓/换弹/冷却）的帧同样进语境：
        /// 否则朝向已跟准星而动画语境缺席（无方向性的 MoveBlend 播前进走即倒滑）。
        /// </summary>
        public bool IsFireStance(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            // 本地实体必须**先按实体 Id 解析槽位**——同 IsAiming 口径（Id 不能当下标用）
            if (LocalEntityId != 0 && _sim.TryResolve(LocalEntityId, out int localSlot) && localSlot == slotIndex)
                return _sim.Entities[slotIndex].FireStanceFrames > 0;

            if (!TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if ((snapshot.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) == 0u) return false;
            return snapshot.Entities[slotIndex].FireStanceFrames > 0;
        }

        /// <summary>
        /// 实体是否已死亡（Hp ≤ 0——Sim 权威状态，**公共快照可重建**：重连/迟到加入者按状态出
        /// 死亡表现，不依赖事件回放）。取数口径同 <see cref="IsAiming"/>：本地取预测态、
        /// 远端取最新权威快照；槽位越界 / 首快照未到 / 槽位已死 → false。
        /// </summary>
        public bool IsDead(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            // 本地实体必须**先按实体 Id 解析槽位**——同 IsAiming 口径（Id 不能当下标用）
            if (LocalEntityId != 0 && _sim.TryResolve(LocalEntityId, out int localSlot) && localSlot == slotIndex)
                return _sim.Entities[slotIndex].Hp <= 0;

            if (!TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if ((snapshot.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) == 0u) return false;
            return snapshot.Entities[slotIndex].Hp <= 0;
        }

        /// <summary>
        /// 实体是否处于出生保护期（`Frame &lt; InvulnerableUntilFrame`，**端点不含**）。取数口径同
        /// <see cref="IsDead"/>：本地取预测态、远端取最新权威快照（保护期由快照携带且走粘滞窗）。
        /// 槽位越界 / 首快照未到 / 槽位已死 → false。
        /// </summary>
        public bool IsProtected(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            // 本地实体必须**先按实体 Id 解析槽位**——同 IsDead 口径（Id 不能当下标用）
            if (LocalEntityId != 0 && _sim.TryResolve(LocalEntityId, out int localSlot) && localSlot == slotIndex)
                return _sim.Frame < _sim.Entities[slotIndex].InvulnerableUntilFrame;

            if (!TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if ((snapshot.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) == 0u) return false;
            return snapshot.Frame < snapshot.Entities[slotIndex].InvulnerableUntilFrame;
        }

        /// <summary>
        /// 槽位当前**生命的起始帧**（<see cref="EntitySlot.LifeStartFrame"/>）——生命更替（复活）的唯一
        /// 可靠判据（死亡表现锁存的复位信号；本值在复活的同一帧被 RespawnSystem 改写）。
        /// 取数口径同 <see cref="IsDead"/>：本地取预测态、远端取最新权威快照（公共快照可重建——重连/
        /// 迟到加入者同路径）；槽位越界 / 首快照未到 / 尚未见到该槽位 → false（调用方应保持现状不判"新生命"）。
        /// </summary>
        public bool TryGetLifeStartFrame(int slotIndex, out int lifeStartFrame)
        {
            lifeStartFrame = 0;
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            if (LocalEntityId != 0 && _sim.TryResolve(LocalEntityId, out int localSlot) && localSlot == slotIndex)
            {
                lifeStartFrame = _sim.Entities[slotIndex].LifeStartFrame;
                return true;
            }

            if (!TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if ((snapshot.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) == 0u) return false;
            lifeStartFrame = snapshot.Entities[slotIndex].LifeStartFrame;
            return true;
        }

        /// <summary>
        /// 实体是否在换弹（Sim 权威）：读本地 Sim 运行态 <c>WeaponRuntime.State == Reloading</c>。
        /// **口径与 <see cref="IsAiming"/> 的差异**：弹药/换弹属本人私有面（不上公共快照），
        /// 本地实体取预测态（与本地握手感同帧）；远端槽位在联机下无公共字段可读——本地 Sim 里
        /// 远端武器行不被预测开火/换弹（恒 Ready），故不误报（远端换弹表现待公共位扩展，先不预建）。
        /// 槽位越界 / 槽位死（未装备）/ 槽位号越界 → false。
        /// </summary>
        public bool IsReloading(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            int sel = _sim.Entities[slotIndex].SelectedWeapon;
            if (sel < 0 || sel >= SimConfig.WeaponSlotsPerEntity) return false;
            return _sim.Weapons[slotIndex * SimConfig.WeaponSlotsPerEntity + sel].State
                == WeaponSlotState.Reloading;   // lint-allow R3（枚举判等，非浮点精度比较）
        }

        /// <summary>
        /// 槽位当前选中武器的**瞄准态最大偏转角（度）**——表现面读口（准心散布张开；判定不吃，
        /// 腰射倍率由调用方按 <see cref="CombatConfig.HipSpreadFactor"/> 自乘）。取数口径同
        /// <see cref="IsReloading"/>：本人私有面（武器行不上公共快照），读**本地预测态**的选中武器。
        /// 未装备 / 表缺行 / 槽位越界 → false（调用方按"无散布信息"降级，不猜）。
        /// </summary>
        public bool TryGetSpreadDegrees(int slotIndex, out float degrees)
        {
            degrees = 0f;
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (!WeaponSystem.IsEquipped(_sim, slotIndex, WeaponConfig.Loaded, out WeaponDef def, out int _))
                return false;
            degrees = def.Spread;
            return true;
        }

        /// <summary>
        /// 槽位当前选中武器的**定义 Id**——表现面读口（准心手感按武器取 tbcrosshair 行；判定不吃）。
        /// 取数口径同 <see cref="TryGetSpreadDegrees"/>：本人私有面（武器行不上公共快照），读**本地预测态**
        /// 的选中武器。未装备 / 槽位越界 → false（调用方按"无行"回落防御默认，不猜）。
        /// </summary>
        public bool TryGetWeaponDefId(int slotIndex, out int weaponDefId)
        {
            weaponDefId = -1;
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (!WeaponSystem.IsEquipped(_sim, slotIndex, WeaponConfig.Loaded, out WeaponDef def, out int _))
                return false;
            weaponDefId = def.Id;
            return true;
        }

        // ---- 辅助 ----

        /// <summary>摆位：位置 1:1；旋转 = <see cref="FacingRotation"/>（模型视觉前沿约定 +Z）。</summary>
        private static void Place(Transform t, in SimVector3 pos, float yaw)
        {
            t.position = new Vector3(pos.X, pos.Y, pos.Z);
            t.rotation = FacingRotation(yaw);
        }

        /// <summary>
        /// Sim Yaw（从 +X 起量）→ 视觉朝向旋转：恒差 <c>90°</c>（模型视觉前沿约定 +Z）。
        /// 相机焦点用同一份旋转——构图偏移随它变**角色系**（X=右肩、Z=前方）。
        /// </summary>
        private static Quaternion FacingRotation(float yaw)
        {
            return Quaternion.Euler(0f, 90f - yaw * Mathf.Rad2Deg, 0f);
        }

        private static bool TryFind(SimWorldStateSnapshot snapshot, long entityId, out EntitySnapshotEntry entry)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((snapshot.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                if (snapshot.Entities[i].Id != entityId) continue;
                entry = snapshot.Entities[i];
                return true;
            }
            entry = default;
            return false;
        }
    }
}
