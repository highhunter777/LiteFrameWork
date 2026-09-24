using System;
using LiteFramework;
using LiteSim;
using UnityEngine;

namespace LiteSim.View
{
    /// <summary>
    /// 战斗表现视图（《联机战斗演示专项设计》§3"SimView 负责槽位镜像、远端插值、本地和解衰减和事件静默"；
    /// 《状态同步专项设计》§6.2"本地玩家跟预测位置；远端使用前后快照插值；和解时本地误差衰减，
    /// 远端必要时 snap；复活/传送允许硬切"；《M11实施指导》§2.4 C3）。
    ///
    /// **只读 Sim、只写视图**——不写任何 Sim 状态（《状态同步专项设计》§1 原则 3：View → Sim 只有输入和读取）。
    /// 两类来源分别处理：
    /// - **远端**：连续两份权威快照按视点帧插值（<see cref="SimWorldStateSnapshot"/> × 前后，平滑不抖）；
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

        /// <summary>默认实体 prefab（灰盒：单一胶囊；正式角色资源按选装/角色配置解析）。</summary>
        public const string DefaultEntityPrefab = "Assets/Art/Characters/BoxFighter.prefab";

        /// <summary>快照间隔（秒）——插值窗口时长，由 <see cref="SimConfig.SnapshotHz"/> 派生。</summary>
        public static float SnapshotInterval => 1f / SimConfig.SnapshotHz;

        private readonly SimWorldState _sim;                  // 本地预测态（只读）
        private readonly EntityViewMap _views;
        private readonly BattleCameraRig _camera;

        // ---- 插值源（远端）----
        private SimWorldStateSnapshot _snapFrom;
        private SimWorldStateSnapshot _snapTo;
        private float _snapElapsed;

        // ---- 本地表现（预测 + 和解衰减）----
        private SimVector3 _localDisplay;
        private float _localYaw;
        private bool _hasLocalDisplay;

        /// <summary>超过该平面距离直接硬切（复活/传送；《状态同步专项设计》§6.2）。≤0 = 永不硬切。</summary>
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

        /// <summary>被静默丢弃的帧事件数（诊断：回滚重放的去重命中数）。</summary>
        public int SilencedEvents { get; private set; }

        /// <summary>已派发的帧事件数（诊断）。</summary>
        public int DeliveredEvents { get; private set; }

        public SimView(SimWorldState sim, Transform root, ViewFactory factory, ViewRecycler recycler = null,
            BattleCameraRig camera = null, Func<EntitySlot, string> prefabLocationOf = null)
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
        }

        // ---- 每帧驱动 ----

        /// <summary>
        /// 每渲染帧：① 视图增删 → ② 远端插值写位 → ③ 本地衰减收敛 → ④ 相机跟随。
        ///
        /// **帧事件不在这里取**：Sim 的帧事件是帧内瞬态，<see cref="FrameDriver"/> 在每个逻辑帧结束后
        /// 立即清空缓冲（决策⑥）——渲染帧轮询要么读不到、要么重复读。事件经
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
                    if (view != null) Place(view.transform, in slot.Pos, slot.Yaw);
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
                Place(view.transform, in _localDisplay, _localYaw);
        }

        private void UpdateCamera(float dt)
        {
            if (_camera == null || !_hasLocalDisplay) return;
            _camera.Tick(LocalDisplayPosition, dt);
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

        // ---- 辅助 ----

        private static void Place(Transform t, in SimVector3 pos, float yaw)
        {
            t.position = new Vector3(pos.X, pos.Y, pos.Z);
            t.rotation = Quaternion.Euler(0f, -yaw * Mathf.Rad2Deg, 0f);   // Sim 的 Yaw 在 XZ 平面，绕 Y 轴取负
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
