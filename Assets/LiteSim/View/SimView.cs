using System;
using LiteFramework;
using LiteSim;
using UnityEngine;

namespace LiteSim.View
{
    /// <summary>
    /// 战斗表现视图（《联机战斗演示专项设计》§3"SimView 负责槽位镜像、远端插值、本地和解衰减和事件静默"；
    /// 《状态同步专项设计》§6.2"本地玩家跟预测位置；远端使用前后快照插值；和解时本地误差衰减，
    /// 远端必要时 snap；复活/传送允许硬切"）。
    ///
    /// **只读 Sim、只写视图**——不写任何 Sim 状态（《状态同步专项设计》§1 原则 3：View → Sim 只有输入和读取）。
    /// 两类来源分别处理：
    /// - **远端**：连续两份权威快照按视点帧插值（<see cref="SimWorldStateSnapshot"/> × 前后，平滑不抖；
    ///   前后间距超 <see cref="SnapDistance"/> 硬切——§6.2"远端必要时 snap"，传送/复活不播成飞人）；
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
        /// 该包 Rifle_Controller 构成，见 CombatGirlsAnimationProfile.ViewPrefabPath）。
        /// prefab 本体住 `Assets/Prefab/`，**不在 `Assets/CombatGirlsCharacterPack/` 内**——该包只是它的
        /// **依赖来源**（贴图/网格/动画/Avatar 全在该包内），故收集组必须**额外覆盖 `Assets/Prefab`**，
        /// 否则 YooAsset 拿不到它（收集按目录走）；依赖资产仍由 `Assets/CombatGirlsCharacterPack/Runtime` 收。
        /// 资源包未入库的克隆加载失败 → ProcedureBattle 回退程序化灰盒（可复现降级，勿删兜底）。</summary>
        public const string DefaultEntityPrefab = "Assets/Prefab/Player(Rifle).prefab";

        /// <summary>快照间隔（秒）——插值窗口时长，由 <see cref="SimConfig.SnapshotHz"/> 派生。</summary>
        public static float SnapshotInterval => 1f / SimConfig.SnapshotHz;

        private readonly SimWorldState _sim;                  // 本地预测态（只读）
        private readonly EntityViewMap _views;
        private readonly ICameraService _camera;              // 相机端口（实现住 Platform.Unity 适配器——本层不认识 Cinemachine）

        // ---- 插值源（远端）----
        private SimWorldStateSnapshot _snapFrom;
        private SimWorldStateSnapshot _snapTo;
        private float _snapElapsed;

        // ---- 本地表现（预测 + 和解衰减）----
        private SimVector3 _localDisplay;
        private float _localYaw;
        private bool _hasLocalDisplay;
        private float _cameraYawFrozenAt = float.NaN;      // 腰射驻留窗的相机朝向锁存（NaN = 窗外，随 _localYaw）
        private CharacterController _localCc;                 // 本地视图物理代理（CC.Move 收敛到衰减目标——见 PlaceLocal/EnsureLocalCc）

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
        /// 权威快照到达（<c>SnapshotReassembler</c> 重建出的完整状态）：推进插值窗口
        /// From ← 旧 To，To ← 新快照。视点帧 = 最新快照帧 − <see cref="SimConfig.InterpFrames"/>
        /// （§3.4.1：玩家所见帧落后最新快照若干帧，才有两份快照可插）。
        /// </summary>
        public void OnAuthoritativeSnapshot(SimWorldStateSnapshot snapshot)
        {
            if (snapshot == null) return;
            _snapFrom = _snapTo;
            _snapTo = snapshot;
            _snapElapsed = 0f;                 // 新快照到达 = 插值窗口起点
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
            _snapElapsed += dt;

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

        /// <summary>远端插值：窗口内 alpha = 已过时间 / 快照间隔；快照停摆时停在最新（不外推）。</summary>
        private bool TryInterpolate(long entityId, out SimVector3 pos, out float yaw)
        {
            pos = default;
            yaw = 0f;
            if (_snapFrom == null || _snapTo == null) return false;

            float alpha = _snapElapsed / SnapshotInterval;
            if (alpha > 1f) alpha = 1f;

            if (!TryFind(_snapFrom, entityId, out var a) || !TryFind(_snapTo, entityId, out var b)) return false;

            // 远端硬切（§6.2"远端必要时 snap；复活/传送允许硬切"）：前后两份快照间距超阈值 =
            // 权威侧发生过传送/复活级跳变——照常插值会把它播成横穿地图的"飞人"，直接对齐最新快照。
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

            if (!_hasLocalDisplay)
            {
                _localDisplay = slot.Pos;              // 首帧/换实体：直接落位
                _localYaw = slot.Yaw;
                _hasLocalDisplay = true;
            }
            else if (ViewTransformMath.ShouldSnap(_localDisplay, slot.Pos, SnapDistance))
            {
                _localDisplay = slot.Pos;              // 复活/传送：硬切
                _localYaw = slot.Yaw;
            }
            else
            {
                _localDisplay = ViewTransformMath.Decay(_localDisplay, slot.Pos, ReconcileSharpness, dt);
                _localYaw = ViewTransformMath.YawDecay(_localYaw, slot.Yaw, ReconcileSharpness, dt);
            }

            if (_views.TryGet(slotIndex, out var view))
                PlaceLocal(view, dt);
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
        /// **耦合提示**：CC 胶囊参数（radius/height/center）是 Sim 身位（HitscanRadius/HitscanHeight）
        /// 的第二处事实源——改 CombatConfig 身位常量时必须同步预制体（两端不一致时命中判定与
        /// 视觉推挡会出现半径差）。</summary>
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

            // **腰射开火驻留窗（<see cref="EntitySlot.FireStanceFrames"/>，公共面——预测态即时镜像）：窗内
            // 冻结主相机朝向**（进窗时刻锁存——玩家转身不牵动相机，腰射连点画面稳定；位置照常跟随；
            // 窗尽恢复跟 Yaw，vcam 阻尼平滑回归）。ADS 段主相机被瞄准机优先级接管，冻结值无视觉影响——
            // 同窗口径不区分（瞄准帧同样置满窗）。
            bool inFireStance = _sim.TryResolve(LocalEntityId, out int localSlot)
                && _sim.Entities[localSlot].FireStanceFrames > 0;
            if (inFireStance)
            {
                if (float.IsNaN(_cameraYawFrozenAt)) _cameraYawFrozenAt = _localYaw;   // 进窗锁存
            }
            else
            {
                _cameraYawFrozenAt = float.NaN;                                        // 窗尽解冻
            }
            float cameraYaw = inFireStance ? _cameraYawFrozenAt : _localYaw;

            _camera.Follow(LocalDisplayPosition, FacingRotation(cameraYaw), dt);   // 平滑/档位归实现（Cinemachine 由 vcam 配置表达）
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
        ///   代价是远端举枪比插值姿态略早，量级 = 视点延迟 <see cref="SimConfig.InterpFrames"/> 帧）。
        /// 槽位越界 / 首快照未到 / 槽位已死 → false（不是瞄准态，而不是抛）。
        /// </summary>
        public bool IsAiming(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            // 本地实体必须**先按实体 Id 解析槽位**——Id 是"版本&lt;&lt;48 | 槽位"，不能当下标用
            if (LocalEntityId != 0 && _sim.TryResolve(LocalEntityId, out int localSlot) && localSlot == slotIndex)
                return (_sim.Entities[slotIndex].Flags & EntityFlags.Aiming) != 0u;

            if (_snapTo == null) return false;
            if ((_snapTo.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) == 0u) return false;
            return (_snapTo.Entities[slotIndex].Flags & EntityFlags.Aiming) != 0u;
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

            if (_snapTo == null) return false;
            if ((_snapTo.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) == 0u) return false;
            return _snapTo.Entities[slotIndex].Hp <= 0;
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
