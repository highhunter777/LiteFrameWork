using System;
using LiteFramework;
using LiteFramework;
using LiteSim;
using UnityEngine;
using LiteBattle;

namespace LiteView
{
    /// <summary>
    /// 战斗表现视图门面：槽位镜像、远端插值、本地和解衰减、事件静默。
    ///
    /// **只读 Sim、只写视图**——不写任何 Sim 状态。协作件分工：
    /// <see cref="SnapshotInterpolationBuffer"/>（jitter buffer/渲染帧/远端插值）、
    /// <see cref="LocalReconciler"/>（本地表现与硬切）、
    /// <see cref="FrameEventSilenceGate"/>（回滚/和解事件去重）、
    /// <see cref="SimViewFacts"/>（表现层事实读口）、
    /// <see cref="EntityViewBinder"/>（槽位视图生命周期与远端落位）。
    /// </summary>
    public sealed class SimView : ITickable, IBattleView
    {
        /// <summary>视图工厂（按 prefab location 造实例；生产接 EntityService，测试接替身）。</summary>
        public delegate GameObject ViewFactory(string prefabLocation, Transform parent);

        /// <summary>视图回收（生产接 EntityService.Hide；null = 走 EntityViewMap 的自有池）。</summary>
        public delegate void ViewRecycler(GameObject view);

        /// <summary>静默门放行的帧事件回调（编排方接线：VFX/音效/飘字）。</summary>
        public delegate void FrameEventSink(in FrameEvent e);

        /// <summary>对局实体视图 prefab（prefab 本体住 `Assets/ShareResource/Characters/`，收集组须覆盖）。</summary>
        public const string DefaultEntityPrefab = "Assets/ShareResource/Characters/Player(Rifle).prefab";

        /// <summary>快照间隔（秒）——插值窗口时长，由 <see cref="SimConfig.SnapshotHz"/> 派生。</summary>
        public static float SnapshotInterval => 1f / SimConfig.SnapshotHz;

        private readonly SimWorldState _sim;
        private readonly ICameraService _camera;
        private readonly EntityViewMap _views;
        private readonly EntityViewBinder _binder;
        private readonly SnapshotInterpolationBuffer _snapshots;
        private readonly FrameEventSilenceGate _silence;
        private readonly LocalReconciler _local;
        private readonly SimViewFacts _facts;
        private long _localEntityId;

        /// <summary>渲染相对最新快照的落后帧数（tick 帧；0 = 测试用即时模式）。</summary>
        public int RenderDelayFrames
        {
            get => _snapshots.RenderDelayFrames;
            set => _snapshots.RenderDelayFrames = value;
        }

        /// <summary>当前渲染帧（tick 帧轴）——延迟补偿的 `viewFrame` 单源（-1 = 尚无快照）。</summary>
        public int RenderFrame => _snapshots.RenderFrame;

        /// <summary>缓冲中的快照数（诊断/测试）。</summary>
        public int BufferedSnapshotCount => _snapshots.Count;

        /// <summary>是否已收到过权威快照（远端是否走快照插值——动画测速平滑只对这类槽位生效）。</summary>
        public bool HasSnapshotSource => _snapshots.HasSource;

        /// <summary>硬切距离阈值（米）：本地和解/远端前后快照位置差超过它直接落位；≤0 = 永不硬切。</summary>
        public float SnapDistance = 3f;

        /// <summary>和解误差衰减速率（越大收敛越快；帧率无关）。</summary>
        public float ReconcileSharpness = 10f;

        /// <summary>事件静默闸：帧号 ≤ 该值的帧事件不派发（回滚重放/和解去重的帧闸）。</summary>
        public int SilenceUntilFrame => _silence.SilenceUntilFrame;

        /// <summary>本地玩家实体 Id（0 = 未对齐——对齐前无本地表现，相机也不跟）。</summary>
        public long LocalEntityId
        {
            get => _localEntityId;
            set
            {
                if (value == _localEntityId) return;
                _localEntityId = value;
                _local.Align();
            }
        }

        /// <summary>本地复活序号（每次生命更替 +1）；换实体不清零。</summary>
        public int LocalRespawnSerial => _local.RespawnSerial;

        /// <summary>本地视图关 CC 直写（瞬移落位）的次数（诊断/测试读数）。</summary>
        public int LocalTeleportCount => _local.TeleportCount;

        /// <summary>静默门放行的事件回调（编排方接线表现；null = 只做去重统计）。</summary>
        public FrameEventSink EventSink { get; set; }

        /// <summary>本地玩家表现位置（相机跟随目标）。</summary>
        public Vector3 LocalDisplayPosition => new Vector3(_local.Display.X, _local.Display.Y, _local.Display.Z);

        /// <summary>本地表现是否已初始化（首帧直接落位，不做衰减）。</summary>
        public bool HasLocalDisplay => _local.HasDisplay;

        /// <summary>已建立的实体视图数（泄漏断言：应随活体数回基线）。</summary>
        public int ViewCount => _binder.Count;

        /// <summary>取槽位视图（诊断/HUD/测试用；未建返回 false）。</summary>
        public bool TryGetView(int slotIndex, out GameObject view) => _binder.TryGet(slotIndex, out view);

        /// <summary>实体 Id → 槽位（帧事件消费的寻址口）；未解析返回 false。</summary>
        public bool TryGetSlot(long entityId, out int slotIndex) => _sim.TryResolve(entityId, out slotIndex);

        /// <summary>槽位是否是本地玩家（动画测速平滑只对快照驱动的远端槽位生效）。</summary>
        public bool IsLocalSlot(int slotIndex) => _facts.IsLocalSlot(slotIndex);

        /// <summary>被静默丢弃的帧事件数（诊断）。</summary>
        public int SilencedEvents => _silence.SilencedEvents;

        /// <summary>已派发的帧事件数（诊断）。</summary>
        public int DeliveredEvents => _silence.DeliveredEvents;

        public SimView(SimWorldState sim, Transform root, ViewFactory factory, ViewRecycler recycler = null,
            ICameraService camera = null, Func<EntitySlot, string> prefabLocationOf = null)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _camera = camera;
            _views = new EntityViewMap(factory, recycler, root, prefabLocationOf);
            _binder = new EntityViewBinder(_views);
            _snapshots = new SnapshotInterpolationBuffer();
            _silence = new FrameEventSilenceGate();
            _local = new LocalReconciler();
            _facts = new SimViewFacts(_sim, _snapshots, () => _localEntityId, () => SnapDistance);
        }

        /// <summary>权威快照到达：入帧号有序缓冲；旧帧/重复帧丢弃。</summary>
        public void OnAuthoritativeSnapshot(SimWorldStateSnapshot snapshot) => _snapshots.OnSnapshot(snapshot);

        /// <summary>重连恢复：清空插值缓冲（旧快照属于上一条时间线），下一份快照重新起锚。</summary>
        public void ResetInterpolation() => _snapshots.Reset();

        /// <summary>和解发生：本地表现交给衰减收敛，只推进静默闸。</summary>
        public void OnReconcile(int frame) => _silence.OnReconcile(frame);

        /// <summary>回滚重放：重放段事件一律静默。</summary>
        public void OnRollback(int replayedToFrame) => _silence.OnRollback(replayedToFrame);

        /// <summary>外部强制静默到某帧。</summary>
        public void Silence(int untilFrame) => _silence.Silence(untilFrame);

        /// <summary>本地实体对齐——重置本地表现，下一帧直接落位。</summary>
        public void AlignLocal(long entityId) => LocalEntityId = entityId;

        /// <summary>每渲染帧：视图增删 → 远端插值写位 → 本地衰减收敛 → 相机跟随。</summary>
        public void Tick(float realDelta)
        {
            float dt = realDelta > 0f ? realDelta : 0f;
            _snapshots.Advance(dt);

            _binder.Sync(_sim, _localEntityId);
            _binder.UpdateRemote(_sim, _localEntityId, _snapshots, SnapDistance);
            LocalStep step = _local.Step(_sim, _localEntityId, _views, SnapDistance, ReconcileSharpness, dt);
            if (step.CameraReset) _camera?.Reset();
            UpdateCamera(dt);
        }

        private void UpdateCamera(float dt)
        {
            if (_camera == null || !_local.HasDisplay) return;
            // 焦点 = 本地表现位置；相机构图/阻尼归 vcam 场景配置，代码不持第二处事实源。
            _camera.Follow(LocalDisplayPosition, SimViewMath.FacingRotation(_local.Yaw), dt);
        }

        /// <summary>帧事件交付（逻辑帧边界）：静默门内的帧丢弃，放行的交 <see cref="EventSink"/>。</summary>
        public void OnFrameEvents(SimWorldState state) => _silence.Deliver(state, EventSink);

        public bool IsAiming(int slotIndex) => _facts.IsAiming(slotIndex);

        public bool IsFireStance(int slotIndex) => _facts.IsFireStance(slotIndex);

        public bool IsDead(int slotIndex) => _facts.IsDead(slotIndex);

        public bool IsProtected(int slotIndex) => _facts.IsProtected(slotIndex);

        public bool TryGetLifeStartFrame(int slotIndex, out int lifeStartFrame)
            => _facts.TryGetLifeStartFrame(slotIndex, out lifeStartFrame);

        public bool TryGetRenderPosition(int slotIndex, out SimVector3 position)
            => _facts.TryGetRenderPosition(slotIndex, out position);

        public bool IsReloading(int slotIndex) => _facts.IsReloading(slotIndex);

        public bool TryGetSpreadDegrees(int slotIndex, out float degrees)
            => _facts.TryGetSpreadDegrees(slotIndex, out degrees);

        public bool TryGetWeaponDefId(int slotIndex, out int weaponDefId)
            => _facts.TryGetWeaponDefId(slotIndex, out weaponDefId);
    }
}
