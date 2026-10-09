using System;
using System.Collections.Generic;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 角色动画驱动器（《层次动画机设计》——**单机双根**：形态裁决全部收进
    /// <see cref="CombatAnimMachine"/>，本类只负责事实喂入、事件路由、槽位生命周期与诊断聚合）：
    /// - **速度/方向来源**：视图 Transform 的帧间位移——本地（预测+和解衰减）与远端（快照插值）同一来源，
    ///   不读 Sim 内部；插值/衰减的速度天然平滑；
    /// - **IsAiming**：<see cref="SimView.IsAiming"/>（Sim 权威）；**IsReloading**：<see cref="SimView.IsReloading"/>
    ///   （Sim 权威——武器账本私有面）；**IsDead**：<see cref="SimView.IsDead"/>；
    /// - **锁存**：迟滞公式单源 <see cref="LocomotionBlendMath.UpdateLatch"/>（进 0.6 / 出 0.3 m/s），
    ///   驱动器每帧算、作为事实喂状态机（机内无迟滞散字段）；
    /// - **开火窗长**：单源 `CombatConfig.FireStanceFrames / SimConfig.TickRate`（1s——窗与开火表现解耦）；
    ///   **换弹倍率**：装配期按片段时长 / Sim `ReloadFrames` 求商（钳制在 Profile 区间——动画收势与
    ///   弹药回国同帧）；
    /// - **帧事件**：本类实现 <see cref="IFrameEventAnimationConsumer"/> 并**自订阅**
    ///   `SimView.EventSink`（构造时挂上、<see cref="Dispose"/> 时摘下）；Fire 事件只刷新射击窗
    ///   （站姿由 Fire 族持 AimIdle 循环，无片段可重播），首次进入按锁存路由进 FireIdle/FireWalk；
    /// - **片源（去 AC 主路径）**：<see cref="AnimationClipManifest"/>（键→Clip 显式引用的纯配置）经
    ///   <c>AnimatorAnimationBackend.RegisterManifest</c> 装配期逐键登记——**控制器可选**：有控制器时
    ///   其片段索引只是便利源（清单后注册，同名键以清单为权威）；无控制器（直 Clip 模型）由清单独立供片；
    ///   两皆缺 = 无片源，视同灰盒跳过；
    /// - **发现面＝<see cref="LiteAnimator"/>**：视图挂绑定组件（完整动画配置面：清单/可选控制器/
    ///   Avatar/RootMotion/UpdateMode/Culling）才是可动画视图（组件 RequireComponent 自动补
    ///   Animator 并持有全部配置）——无组件即灰盒，裸 Animator 不构成判据；
    /// - **灰盒降级**：无绑定组件、或无任何片源（控制器与清单皆空）的视图不建播放器/状态机
    ///   （与缺角色资源既有姿势一致）；视图回收时旧播放器/机器随槽位消失，新占用者重建（Owner 代次
    ///   语义由重建保证）。
    /// </summary>
    public sealed class CharacterLocomotionDriver : IDisposable, IModuleStats, IFrameEventAnimationConsumer
    {
        private readonly SimView _view;
        private readonly AnimationProfile _profile;
        private readonly AnimationClipManifest _manifest;
        private readonly SlotAnim[] _slots = new SlotAnim[SimConfig.MaxEntities];

        /// <summary>射击窗长（秒）＝ CombatConfig.FireStanceFrames / SimConfig.TickRate（1s 单源派生）。</summary>
        private readonly float _fireHoldSeconds;

        /// <summary>权重容器（驱动器复用数组——零分配：播放器/后端只在提交内读取、不保留引用）。</summary>
        private readonly float[] _moveWeights = new float[3];
        private readonly float[] _aimWeights = new float[4];

        private bool _disposed;

        public CharacterLocomotionDriver(SimView view, AnimationProfile profile,
            AnimationClipManifest manifest = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile),
                "Profile 必填——单源在 tbanimationprofile 表：运行时经读口 AnimationProfileConfig.Rows + FromRows(模型族) 装配");
            _manifest = manifest;

            // 形状校验（配置错误构造期显性失败，不等到运行时逐帧静默失败）：
            // MoveBlend 三槽走 Base；开火/瞄准站姿/瞄准移动三形走 Override（战斗动作全身接管）
            if (!_profile.TryGetBlendDefinition(CharacterAnimationIds.MoveBlend, out var moveBlend)
                || moveBlend.SlotCount != 3
                || moveBlend.Channel != AnimationChannel.Base)
                throw new ArgumentException(
                    $"Profile 缺 {CharacterAnimationIds.MoveBlend} 的 3 槽位 Base 混合定义（形状 {{Idle,Walk,Run}}）", nameof(profile));
            if (!_profile.TryGetBlendDefinition(CharacterAnimationIds.AimMoveBlend, out var aimBlend)
                || aimBlend.SlotCount != 4
                || aimBlend.Channel != AnimationChannel.Override)
                throw new ArgumentException(
                    $"Profile 缺 {CharacterAnimationIds.AimMoveBlend} 的 4 槽位 Override 混合定义（形状 {{AimWalk_F,R,B,L}}）", nameof(profile));
            if (!_profile.TryGetDefinition(CharacterAnimationIds.Reload, out var reloadDef)
                || reloadDef.Channel != AnimationChannel.Override)
                throw new ArgumentException(
                    $"Profile 换弹语义须登记为 Override（全身接管）→ {CharacterAnimationIds.Reload}", nameof(profile));
            if (reloadDef.Loop)
                throw new ArgumentException(
                    $"Profile 换弹语义须为一次性（播完持末帧至事实清除）→ {CharacterAnimationIds.Reload}", nameof(profile));
            if (!_profile.TryGetDefinition(CharacterAnimationIds.AimIdle, out var aimIdleDef)
                || aimIdleDef.Channel != AnimationChannel.Override)
                throw new ArgumentException(
                    $"Profile 瞄准站姿须登记为 Override → {CharacterAnimationIds.AimIdle}", nameof(profile));
            if (aimIdleDef.Loop == false)
                throw new ArgumentException(
                    $"Profile 瞄准站姿须为循环（开火窗与瞄准态持同一循环）→ {CharacterAnimationIds.AimIdle}", nameof(profile));

            // 片源覆盖校验（§4 片源载体裁决"装配期逐键 RegisterClip 入后端并经校验后才放行播放"）：
            // Profile 两张登记表的全部绑定键都必须能在清单解析——缺键 = 清单与 Profile 不同步
            // （配置错误），构造期显性失败，不等到运行时逐提交静默失败。片段引用缺失（null）不在此判：
            // 那是资源缺失态（缺包克隆），由槽位登记计数观测、不阻止装配。
            if (manifest != null)
            {
                var missing = new List<string>();
                foreach (var def in _profile.Definitions)
                    if (!manifest.ContainsKey(def.Binding)) missing.Add($"{def.Id}:{def.Binding}");
                foreach (var blend in _profile.BlendDefinitions)
                    for (int slot = 0; slot < blend.SlotCount; slot++)
                        if (!manifest.ContainsKey(blend.Bindings[slot])) missing.Add($"{blend.Id}:槽{slot}:{blend.Bindings[slot]}");
                if (missing.Count > 0)
                    throw new ArgumentException(
                        $"片段清单缺 {missing.Count} 个 Profile 绑定键（{string.Join(", ", missing.ToArray())}）"
                        + "——清单须与 Profile 同步（键单源在 Profile）", nameof(manifest));
            }

            _fireHoldSeconds = (float)CombatConfig.FireStanceFrames / SimConfig.TickRate;   // 60/60 = 1s（单源派生）

            _view.EventSink += OnFrameEvent;      // 自订阅（§8 接缝：接在静默门之后）；Dispose 时摘下
        }

        /// <summary>当前有动画播放器的实体视图数（诊断/测试）。</summary>
        public int AnimatedViews
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++) if (_slots[i]?.Player != null) n++;
                return n;
            }
        }

        /// <summary>清单条目未能登记的计数（聚合全部在役后端——片段引用缺失是缺包克隆的资源态观测；
        /// 恒 0 = 片源健康；后端换代重建后计数随旧后端回收，观测口径为当前在役面）。</summary>
        public int MissingManifestClips
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++) n += _slots[i]?.Backend?.MissingManifestClips ?? 0;
                return n;
            }
        }

        /// <summary>读槽位当前**形态**语义（诊断/HUD/测试）：移动根活跃 = MoveBlend；战斗根活跃 = Override 当前形态
        /// （开火/瞄准族为 AimIdle/AimMoveBlend 循环，换弹为 Reload——"窗内保持 clip"）。无播放器/未开 → false。</summary>
        public bool TryGetCurrent(int slotIndex, out AnimationId id)
        {
            id = default;
            if (!TrySlot(slotIndex, out var s) || !s.Machine.Started) return false;

            var cur = s.Machine.Current;
            if (cur == CharacterAnimId.Idle || cur == CharacterAnimId.Moving)
            {
                id = CharacterAnimationIds.MoveBlend;             // 移动根恒 MoveBlend（同形态换权重）
                return true;
            }
            if (!s.Ctx.BodyForm.IsValid) return false;            // 战斗根：Override 当前形态
            id = s.Ctx.BodyForm;
            return true;
        }

        /// <summary>
        /// 读槽位当前形态与**权重**（诊断/HUD/测试；<paramref name="weights"/> 由调用方复用，零分配）：
        /// 混合形态（MoveBlend 3 权重 / AimMoveBlend 4 权重）给出拷贝并返回 true；单片段形态返回 false 但
        /// <paramref name="id"/> 仍给出形态。
        /// </summary>
        public bool TryGetMotion(int slotIndex, out AnimationId id, float[] weights)
        {
            if (!TryGetCurrent(slotIndex, out id) || !TrySlot(slotIndex, out var s)) return false;

            if (id.Equals(CharacterAnimationIds.MoveBlend))
            {
                if (weights.Length < 3) return false;
                Array.Copy(s.Ctx.LocoWeightsCopy, weights, 3);
                return true;
            }
            if (id.Equals(CharacterAnimationIds.AimMoveBlend))
            {
                if (weights.Length < 4) return false;
                Array.Copy(s.Ctx.BodyWeightsCopy, weights, 4);
                return true;
            }
            return false;                                         // 单片段形态没有权重
        }

        /// <summary>读槽位当前**状态**（§11 诊断面 HSM 直读——测试/断言用：Fire*/Aim*/Idle/Moving）。
        /// 无播放器/机器未启动 → false。</summary>
        public bool TryGetAnimState(int slotIndex, out CharacterAnimId state)
        {
            state = default;
            if (!TrySlot(slotIndex, out var s) || !s.Machine.Started) return false;
            state = s.Machine.Current;
            return true;
        }

        /// <summary>读槽位 Override 当前形态句柄（测试观察"同形态续播不换句柄"——跨态续播的直证面）。</summary>
        public bool TryGetFormHandle(int slotIndex, out AnimationHandle handle)
        {
            handle = default;
            if (!TrySlot(slotIndex, out var s) || !s.Machine.Started || !s.Ctx.BodyHandle.IsValid) return false;
            handle = s.Ctx.BodyHandle;
            return true;
        }

        /// <summary>
        /// 每渲染帧推进（SimView 之后调用——视图位置先更新，本类再喂事实推状态机）：视图增删检查 →
        /// 速度/方向测量 → 锁存与事实喂入 → 机.Tick（根→叶 OnUpdate + 事务帧末应用）→ 播放器采样推进。
        /// </summary>
        public void Tick(float realDelta)
        {
            if (_disposed) return;
            float dt = realDelta > 0f ? realDelta : 0f;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_view.TryGetView(i, out var go))
                {
                    ReleaseSlot(i);                       // 视图已回收（死亡/离场）：播放器随视图消失收口
                    continue;
                }

                var s = _slots[i] ?? (_slots[i] = new SlotAnim());
                if (s.Player == null)
                {
                    // 发现面＝视图绑定组件（直 Clip 形态的视图入口）：无组件＝灰盒——
                    // 裸 Animator 不构成"可动画视图"判据；组件含 RequireComponent，Animator 不可被单独剥除。
                    // 片源优先取组件声明的清单（视图自含），装配传入清单作回退——两者同资产时等价。
                    var binding = go.GetComponentInChildren<LiteAnimator>(true);
                    var animator = binding?.Animator;
                    var manifest = binding != null ? binding.Manifest : null;
                    if (manifest == null) manifest = _manifest;
                    if (animator == null || (animator.runtimeAnimatorController == null && manifest == null))
                    {
                        // 灰盒视图：无绑定组件/无 Animator、或无任何片源（控制器与清单皆空）——只记位置
                        // （保持速度判断的帧间基准），不建播放器/机器。不打日志：每帧路径不得刷屏；
                        // 降级事实由 IModuleStats（views 计数）与用例承担观测。
                        RememberPosition(s, go);
                        continue;
                    }

                    var backend = new AnimatorAnimationBackend(animator,
                        AnimatorAnimationBackend.DefaultBlendSeconds, _profile.OverlayMaskExclusions);
                    backend.RegisterManifest(manifest);   // 装配期逐键登记（后注册＝清单为权威；引用缺失后端计数）
                    s.Backend = backend;
                    s.Player = new AnimationPlayer(backend, _profile);

                    // 换弹倍率：片段时长 / Sim 换弹时长（WeaponConfig.Default.ReloadFrames / TickRate）——
                    // 动画收势与弹药回国同帧；钳制在 Profile 登记区间（越界会被播放器拒绝）；未知片段时长 ⇒ 1×
                    _profile.TryGetDefinition(CharacterAnimationIds.Reload, out var reloadDef);   // 构造期已校验在册
                    backend.TryGetClipSeconds(reloadDef.Binding, out float reloadSeconds);
                    float reloadTarget = (float)WeaponConfig.Default.ReloadFrames / SimConfig.TickRate;
                    float reloadSpeed = reloadSeconds > 0f && reloadTarget > 0f
                        ? Mathf.Clamp(reloadSeconds / reloadTarget, reloadDef.MinSpeed, reloadDef.MaxSpeed)
                        : 1f;

                    s.Ctx = new SlotAnimContext
                    {
                        Player = s.Player,
                        MoveWeights = _moveWeights,               // 驱动器复用数组（零分配）
                        AimWeights = _aimWeights,
                        ReloadPlaybackSpeed = reloadSpeed,
                        FireHoldSeconds = _fireHoldSeconds,
                    };
                    s.Machine = CombatAnimMachine.Build(s.Ctx);
                    s.Machine.Start(CharacterAnimId.LocomotionRoot);   // 初建提交 MoveBlend(Idle=1)——不开局 T-pose（提交/维护归移动根；无控制器时即唯一落位兜底）
                    Debug.LogWarning($"[Anim][diag] slot {i} 播放器建立：view「{go.name}」animator「{animator.name}」"
                        + $"ctrl={(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")}"
                        + $" manifest={(_manifest != null ? "on" : "off")} layers={animator.layerCount}"
                        + $" 窗长={_fireHoldSeconds:0.##}s 换弹倍率={reloadSpeed:0.##}×（片段「{reloadDef.Binding}」{reloadSeconds:0.###}s）");
                }

                if (s.HasPos && dt > 0f)
                {
                    float speed = MeasureSpeed(s, go.transform.position, dt, out Vector3 moveDir);
                    s.MovingLatch = LocomotionBlendMath.UpdateLatch(s.MovingLatch, speed);   // 锁存公式单源

                    // 事实喂入（单机单上下文——层间不引用，全部经 ctx 单点）
                    if (_view.IsDead(i)) s.DeadSeen = true;   // 死亡不可逆锁存（快照状态——重连/迟到者同路径）
                    s.Ctx.IsDead = s.DeadSeen;
                    if (s.DeadSeen && s.Machine.Current != CharacterAnimId.Dead)
                        s.Machine.Request(CharacterAnimId.Dead);   // 状态轮询路径：重建/迟到者按死亡事实强制进叶
                    s.Ctx.IsAiming = _view.IsAiming(i);
                    s.Ctx.IsReloading = _view.IsReloading(i);  // 换弹事实（Sim 权威——武器私有面投影）
                    s.Ctx.IsMoving = s.MovingLatch;
                    s.Ctx.Speed = speed;
                    s.Ctx.MoveRelAngleDeg = moveDir == Vector3.zero
                        ? 0f
                        : Vector3.SignedAngle(go.transform.rotation * Vector3.forward, moveDir, Vector3.up);

                    s.Machine.Tick(dt);                   // 根→叶 OnUpdate + 事务帧末应用（一帧一事务）
                }

                RememberPosition(s, go);
                s.Player?.Tick(dt);                        // 唯一驱动入口（§7）
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _view.EventSink -= OnFrameEvent;      // 先摘订阅：此后到达的事件一律不再进入本类
            for (int i = 0; i < _slots.Length; i++) ReleaseSlot(i);
        }

        // ---- IFrameEventAnimationConsumer（§8：帧事件 → 播放请求）----

        /// <summary>
        /// 帧事件消费（**逻辑帧边界**，由 <see cref="SimView.EventSink"/> 在静默门之后调用）：
        /// fire 事件即进 Fire 系——语义/通道由战斗层装配期从 Profile 单源解析
        /// （构造期校验保留）；Hit/Evade 待接入（未预建消费者）。
        /// 路由：窗先行（根合并口刷新＝重置满窗）→ Fire 族/换弹族态内只续窗（站姿循环由叶自维护，
        /// 无片段可重播）→ 否则按锁存 Request 进 Fire 叶（站定 FireIdle / 移动 FireWalk）。
        /// </summary>
        public void OnFrameEvent(in FrameEvent e)
        {
            if (_disposed) return;

            // Death：死亡不可逆锁存 + 立即进死亡叶（**压过一切挂起路由**——last-wins；
            // 迟到/重连无事件者由状态轮询路径补进：IsDead 事实 → 根裁决强制迁移）
            if (e.Kind == FrameEventKind.Death)
            {
                if (!_view.TryGetSlot(e.EntityId, out int deadSlot)) return;
                if (deadSlot < 0 || deadSlot >= _slots.Length) return;
                var d = _slots[deadSlot];
                if (d?.Machine == null || !d.Machine.Started || d.Ctx == null) return;   // 灰盒/未启动：无动画面
                d.DeadSeen = true;
                d.Ctx.IsDead = true;
                if (d.Machine.Current != CharacterAnimId.Dead)
                    d.Machine.Request(CharacterAnimId.Dead);
                return;
            }

            if (e.Kind != FrameEventKind.Fire) return;            // 未覆盖事件类型不处理（Crit/Explosion 未预建）

            if (!_view.TryGetSlot(e.EntityId, out int slotIndex)) return;   // 主体已回收：丢弃（不补播、不猜）
            if (slotIndex < 0 || slotIndex >= _slots.Length) return;

            var s = _slots[slotIndex];
            if (s?.Player == null || s.Machine == null || !s.Machine.Started) return;   // 灰盒/未启动：无动画面

            s.Ctx.Root.RefreshWindow();                           // 事件即活动：窗重置满窗（先于一切路由）

            var cur = s.Machine.Current;
            if (cur == CharacterAnimId.FireIdle || cur == CharacterAnimId.FireWalk
                || cur == CharacterAnimId.Reloading)
                return;                                           // 已在战斗根：窗已刷（持枪循环由叶自维护；换弹不可打断）
            s.Machine.Request(s.Ctx.IsMoving ? CharacterAnimId.FireWalk : CharacterAnimId.FireIdle);
        }

        // ---- 内部：槽位生命周期与测量 ----

        /// <summary>视图帧间位移 → 水平速度（m/s）与单位移动方向（无位移时方向为零向量）。</summary>
        private static float MeasureSpeed(SlotAnim s, Vector3 pos, float dt, out Vector3 dir)
        {
            float dx = pos.x - s.LastPos.x;
            float dz = pos.z - s.LastPos.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            dir = len > 1e-5f ? new Vector3(dx / len, 0f, dz / len) : Vector3.zero;
            return len / dt;
        }

        private static void RememberPosition(SlotAnim s, GameObject go)
        {
            s.LastPos = go.transform.position;
            s.HasPos = true;
        }

        private void ReleaseSlot(int i)
        {
            var s = _slots[i];
            if (s == null) return;
            s.Player?.Dispose();                          // 销毁序：代次失效 → 终态 → 释放后端（Graph.Destroy）
            s.Player = null;
            s.Backend = null;
            s.Machine = null;                             // 机器随播放器同生共死（无独立 Dispose 面）
            s.Ctx = null;
            s.LastPos = default;
            s.HasPos = false;
            s.MovingLatch = false;
            _slots[i] = null;
        }

        private bool TrySlot(int slotIndex, out SlotAnim s)
        {
            s = null;
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            s = _slots[slotIndex];
            return s?.Player != null && s.Machine != null;
        }

        // ---- IModuleStats（§11 诊断；调用方复用容器，禁止每次 new）----

        string IModuleStats.StatsName => "Animation";

        void IModuleStats.Snapshot(Dictionary<string, string> into)
        {
            into.Clear();

            int channels = 0, nodes = 0, nodesMax = 0, unknown = 0, truncated = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                var b = _slots[i]?.Backend;
                if (b == null) continue;
                channels += b.ActiveChannels;
                nodes += b.PlayableCount;
                if (b.PlayableCount > nodesMax) nodesMax = b.PlayableCount;
                unknown += b.UnknownBindings;
                truncated += b.TruncatedBlends;
            }

            into["views"] = AnimatedViews.ToString();
            into["channels"] = channels.ToString();
            into["nodes"] = nodes.ToString();
            into["nodesMax"] = nodesMax.ToString();       // 单后端峰值：节点稳定性（§12）的观测值
            into["unknownBindings"] = unknown.ToString();
            into["truncatedBlends"] = truncated.ToString();
            into["missingManifestClips"] = MissingManifestClips.ToString();
        }

        /// <summary>槽位内运行时（驱动器私有）：播放器/后端/机器/事实——随视图同生共死。</summary>
        private sealed class SlotAnim
        {
            public AnimationPlayer Player;
            public AnimatorAnimationBackend Backend;
            public SlotAnimContext Ctx;
            public HierarchicalStageMachine<CharacterAnimId, CombatAnimReq> Machine;
            public Vector3 LastPos;
            public bool HasPos;
            public bool MovingLatch;                       // 移动事实锁存（公式单源 LocomotionBlendMath）
            public bool DeadSeen;                          // 死亡不可逆锁存（事件沿/快照状态——置位后恒真）
        }
    }
}
