using System;
using System.Collections.Generic;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;
using LiteGame.Modules.Config;

using LiteSync.Snapshot;
using LiteGame.Modules.Battle.SubModules.ArenaSubModule.View;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Abstractions;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Machine;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Drivers.Grip;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Profiles;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Drivers.Aim
{
    /// <summary>
    /// 角色动画驱动器（单机双根：形态裁决收进 CombatAnimMachine，本类负责事实喂入、事件路由与槽位生命周期）。
    /// </summary>
    public sealed class CharacterLocomotionDriver : IDisposable, IModuleStats, IFrameEventAnimationConsumer
    {
        private readonly SimView _view;
        private readonly IEventSubscription _subscription;
        private readonly AnimationProfile _profile;
        private readonly AnimationClipManifest _manifest;

        /// <summary>持枪偏移数据（grip.bin 装载；null = 数据未装载/损坏 → 持枪/俯仰/枪口对齐三项降级）。</summary>
        private readonly WeaponGripCatalog _gripCatalog;

        private readonly SlotAnim[] _slots = new SlotAnim[SimConfig.MaxEntities];

        /// <summary>相机俯仰读数（TPS 俯仰姿态源；null = 无姿态形态——测试/非自由视角装配）。
        /// 注入位形态（不进构造依赖面：相机是视图服务，驱动只消费 float 事实）。</summary>
        private readonly Func<float> _cameraPitchOf;

        /// <summary>准心世界点（TPS ADS 枪口对齐源；null = 无瞄准形态——测试/非自由视角装配）。</summary>
        private readonly Func<Vector3?> _aimPointOf;

        /// <summary>播放后端构造口（引擎后端由适配层实现，装配根注入）。</summary>
        private readonly IAnimationBackendFactory _backendFactory;

        /// <summary>射击窗长（秒）＝ CombatConfig.FireStanceFrames / SimConfig.TickRate（1s 单源派生）。</summary>
        private readonly float _fireHoldSeconds;

        /// <summary>快照驱动槽位的测速低通锐度（1/s）：吸收网络插值的逐帧速度抖动，
        /// 避免 Idle/Walk/Run 锁存在 0.6/0.3 阈值附近来回翻。</summary>
        private const float SnapshotSpeedSmoothSharpness = 12f;

        /// <summary>**本地**槽位测速低通的锐度（1/s）——时间常数 ≈ 1/20 = 50 ms。远端用
        /// <see cref="SnapshotSpeedSmoothSharpness"/>（12/s）；本地取更快的一档，因为起停响应
        /// 直接进玩家手感，不能像快照插值那样迟钝。噪声机理见 Tick 内注释（横移 2.5 m/s 恰压
        /// 在 Walk 锚点上，1 cm 位置修正即抖 ±0.6 m/s）。</summary>
        private const float LocalSpeedSmoothSharpness = 20f;

        /// <summary>移动**方向**的低通锐度（1/s）。帧间位移方向在低速时噪声极大：横移 2.5 m/s ≈ 4 cm/帧，
        /// 而显示位置带和解/插值的厘米级修正 ⇒ 方向噪声可达 ±20°。四向权重在槽位边界（0°/90°/180°）
        /// 对角度极敏感，未平滑时权重在两槽间跳变、模型抖（前向 Δ≈0 全落 F 槽故不抖——用户实测
        /// "按 W 不抖、按 A/S/D 抖"即此）。方向比速度更敏感，故锐度取得比速度低通更大。</summary>
        private const float MoveDirSmoothSharpness = 14f;

        /// <summary>超过该速度视为传送/复活级硬切（视图 snap），不计入动画测速。</summary>
        private const float SnapSpeedThreshold = 12f;

        /// <summary>权重容器（驱动器复用数组——零分配：播放器/后端只在提交内读取、不保留引用）。</summary>
        private readonly float[] _moveWeights = new float[3];
        private readonly float[] _aimWeights = new float[4];

        private bool _disposed;

        /// <summary>缺 catalog 的警告只打一次（槽位逐个建立时避免按实体刷屏）。</summary>
        private bool _warnedMissingGripCatalog;

        public CharacterLocomotionDriver(SimView view, AnimationProfile profile,
            AnimationClipManifest manifest = null, Func<float> cameraPitchOf = null,
            Func<Vector3?> aimPointOf = null, IAnimationBackendFactory backendFactory = null,
            WeaponGripCatalog gripCatalog = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _gripCatalog = gripCatalog;
            _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory),
                "动画后端构造口必填——适配层实现由装配根注入（Modules 不反向引用 Adapters）");
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
                || reloadDef.Channel != AnimationChannel.Overlay)
                throw new ArgumentException(
                    $"Profile 换弹语义须登记为 Overlay（上半身叠加——Override 全身接管会停掉 Base 移动，脚定住即滑步）→ {CharacterAnimationIds.Reload}", nameof(profile));
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

            // 片源覆盖校验（§4 片源载体裁决"装配期逐键 RegisterClip 入后端并经校验后才放行播放"）： Profile 两张登记表的全部绑定键都必须能在清单解析——缺键 = 清单与 Profile 不同步 （配置错误），构造期显性失败，不等到运行时逐提交静默失败。
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
            _cameraPitchOf = cameraPitchOf;
            _aimPointOf = aimPointOf;

            _subscription = _view.Events?.SubscribeSignal<FrameEvent>(OnFrameEvent);   // 静默门之后；Dispose 时摘下
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
        /// （开火/瞄准族为 AimIdle/AimMoveBlend 循环）。**换弹不在此列**——它不发起根迁移，走 Overlay 叠加层，
        /// 用 <see cref="TryGetReloadUpper"/> 读。无播放器/未开 → false。</summary>
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
        /// 读槽位当前形态与**权重**（诊断/HUD/测试）。
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

        /// <summary>读槽位**换弹上半身叠加层**是否在场（诊断/测试：换弹不再发起根迁移，
        /// 腿照常由 Base MoveBlend 走——故换弹态只能从叠加层读，不能从形态读）。
        /// 无播放器/未建上下文 → false。</summary>
        public bool TryGetReloadUpper(int slotIndex)
        {
            if (!TrySlot(slotIndex, out var s)) return false;
            return s.Ctx.ReloadSubmitted;
        }

        /// <summary>读槽位换弹叠加层句柄（测试观察"同一事实期不重提交"）。无叠加层 → false。</summary>
        public bool TryGetReloadHandle(int slotIndex, out AnimationHandle handle)
        {
            handle = default;
            if (!TrySlot(slotIndex, out var s) || !s.Ctx.ReloadSubmitted) return false;
            handle = s.Ctx.ReloadHandle;
            return true;
        }

        /// <summary>MoveBlend 槽位 {Idle,Walk,Run} 片段时长（步幅对齐输入——相位比与脚程换算）。
        /// 任一键不可解析（缺清单/缺片段引用——灰盒降级态）→ null：不干预（保持 1×），不阻断装配。</summary>
        private float[] ResolveMoveDurations(IAnimationBackend backend)
        {
            if (!_profile.TryGetBlendDefinition(CharacterAnimationIds.MoveBlend, out var moveBlend)
                || moveBlend.SlotCount != 3)
                return null;                                   // 构造期已校验在册三槽——此处防御

            var durations = new float[3];
            for (int i = 0; i < 3; i++)
                if (!backend.TryGetClipSeconds(moveBlend.Bindings[i], out durations[i]))
                    return null;
            return durations;
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

                var s = EnsureSlotFor(i, go);
                if (s == null) continue;                  // 灰盒视图：只记位置，不建播放器/状态机

                FeedFactsAndStep(i, go, s, dt);
                ApplyPoseConstraints(i, go, s, dt);
            }
        }

        /// <summary>槽位建立：视图发现面（绑定组件）→ 片源判定 → 播放器/状态机/武器约束装配。</summary>
        /// <returns>灰盒视图返回 null（仅记帧间位置基准，不建播放器/机器）。</returns>
        private SlotAnim EnsureSlotFor(int i, GameObject go)
        {
            var s = _slots[i] ?? (_slots[i] = new SlotAnim());
            if (s.Player != null) return s;

            // 发现面＝视图绑定组件（直 Clip 形态的视图入口）：无组件＝灰盒—— 裸 Animator 不构成"可动画视图"判据。
            var binding = go.GetComponentInChildren<LiteAnimator>(true);
            var animator = binding?.Animator;
            var manifest = binding != null ? binding.Manifest : null;
            if (manifest == null) manifest = _manifest;
            if (animator == null || (animator.runtimeAnimatorController == null && manifest == null))
            {
                // 灰盒视图：无绑定组件/无 Animator、或无任何片源（控制器与清单皆空）——只记位置 （保持速度判断的帧间基准），不建播放器/机器。
                RememberPosition(s, go);
                return null;
            }

            var backend = _backendFactory.Create(animator, _profile);
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

            // 上下文窄面构造：播放器 + 权重容器（驱动器复用数组，零分配）+ 窗长/换弹倍率。
            s.Ctx = new SlotAnimContext(s.Player, _moveWeights, _aimWeights, _fireHoldSeconds, reloadSpeed);
            s.Machine = CombatAnimMachine.Build(s.Ctx, ResolveMoveDurations(backend));
            s.Machine.Start(CharacterAnimId.LocomotionRoot);   // 初建提交 MoveBlend(Idle=1)——不开局 T-pose（提交/维护归移动根；无控制器时即唯一落位兜底）

            // 持枪/俯仰/枪口对齐三件套：都消费 grip.bin（数据产物单源，编译期无常量）；
            // catalog 缺失或某骨路径不存在 → 对应项显性降级（不建、不静默假装生效）。
            SlotWeaponConstraints constraints = SlotWeaponConstraints.Create(
                animator, _gripCatalog, i, go, ref _warnedMissingGripCatalog);
            s.Grip = constraints.Grip;
            s.Posture = constraints.Posture;
            s.WeaponAim = constraints.WeaponAim;

            Log.Info($"slot {i} 播放器建立：view「{go.name}」animator「{animator.name}」"
                + $"ctrl={(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")}"
                + $" manifest={(_manifest != null ? "on" : "off")} layers={animator.layerCount}"
                + $" 持枪约束={(constraints.Grip != null ? "on" : "off")}"
                + $" 窗长={_fireHoldSeconds:0.##}s 换弹倍率={reloadSpeed:0.##}×（片段「{reloadDef.Binding}」{reloadSeconds:0.###}s）", LogTag.Anim);

            return s;
        }

        /// <summary>每帧事实与状态机：测速低通/锁存 → 事实喂入 → 死亡轮询 → 机 Tick → 换弹叠加层提交。</summary>
        private void FeedFactsAndStep(int i, GameObject go, SlotAnim s, float dt)
        {
            if (s.HasPos && dt > 0f)
            {
                float measuredSpeed = MeasureSpeed(s, go.transform.position, dt, out Vector3 moveDir);
                float speed = measuredSpeed;

                // **测速低通（本地与远端统一）**：视图帧间位移是"实际速度"的瞬时估计，含 显示位置的和解/插值修正噪声。
                bool snapshotDriven = _view.HasSnapshotSource && !_view.IsLocalSlot(i);
                float sharpness = snapshotDriven ? SnapshotSpeedSmoothSharpness : LocalSpeedSmoothSharpness;
                if (measuredSpeed > SnapSpeedThreshold)
                {
                    s.SmoothedSpeed = 0f;             // 瞬移/复活硬切：本帧按 0 计（不计入步态）
                    s.HasSmoothedSpeed = true;
                }
                else if (!s.HasSmoothedSpeed)
                {
                    s.SmoothedSpeed = measuredSpeed;
                    s.HasSmoothedSpeed = true;
                }
                else
                {
                    s.SmoothedSpeed = Mathf.Lerp(s.SmoothedSpeed, measuredSpeed,
                        1f - Mathf.Exp(-sharpness * dt));
                }
                speed = s.SmoothedSpeed;

                s.MovingLatch = LocomotionBlendMath.UpdateLatch(s.MovingLatch, speed);   // 锁存公式单源

                // 方向低通（四向权重用**平滑后**的方向，不用原始帧间位移方向）：低速横移时 帧间位移方向噪声大（见 MoveDirSmoothSharpness），直接取角会让 Walk_F/R/B/L 的权重在槽位边界来回跳——表现就是"按 A/S/D 角色抖，按 W 不抖"。
                Vector3 smoothDir = moveDir;
                if (moveDir != Vector3.zero)
                {
                    if (!s.HasSmoothedDir) { s.SmoothedDir = moveDir; s.HasSmoothedDir = true; }
                    else
                    {
                        s.SmoothedDir = Vector3.Lerp(s.SmoothedDir, moveDir,
                            1f - Mathf.Exp(-MoveDirSmoothSharpness * dt));
                    }
                    float len = s.SmoothedDir.magnitude;
                    smoothDir = len > 1e-4f ? s.SmoothedDir / len : moveDir;   // 归一（插值会缩短）
                }
                else if (s.HasSmoothedDir)
                {
                    smoothDir = s.SmoothedDir;
                }

                // 事实喂入（单机单上下文——层间不引用，全部经 ctx 单点**整体替换**，无半更新态）。
                bool dead = _view.IsDead(i);
                s.Ctx.Feed(new SlotAnimFacts(
                    isAiming: _view.IsAiming(i),
                    isFireStance: _view.IsFireStance(i),   // 射击语境窗（Sim 权威——扣扳机/成功开火统一进窗）
                    isReloading: _view.IsReloading(i),  // 换弹事实（Sim 权威——武器私有面投影）
                    isDead: dead,
                    isMoving: s.MovingLatch,
                    speed: speed,
                    moveRelAngleDeg: smoothDir == Vector3.zero
                        ? 0f
                        : Vector3.SignedAngle(go.transform.rotation * Vector3.forward, smoothDir, Vector3.up)));
                if (dead && s.Machine.Current != CharacterAnimId.Dead)
                {
                    s.Ctx.ReleaseReloadUpper();      // 死亡压过一切：换弹叠加层让位（下帧前即释放，不等轮询兜底）
                    s.Machine.Request(CharacterAnimId.Dead);   // 状态轮询路径：重建/迟到者按死亡事实强制进叶
                }

                s.Machine.Tick(dt);                   // 根→叶 OnUpdate + 事务帧末应用（一帧一事务）

                // 换弹：Overlay 上半身一次性叠加（**不进战斗根**——战斗根接管会停 Base MoveBlend， 脚定住而世界位移照旧即滑步）。
                s.Ctx.UpdateReloadUpper(s.Ctx.IsReloading, s.Ctx.IsDead);

            }

        }

        /// <summary>帧末提交与约束写回：播放器采样推进 → 持枪/俯仰/枪口对齐（写序见各注释）。</summary>
        private void ApplyPoseConstraints(int i, GameObject go, SlotAnim s, float dt)
        {
            RememberPosition(s, go);
            s.Player?.Tick(dt);                        // 唯一驱动入口（§7）

            // 持枪约束：**必须在播放器 Tick 之后**——Graph.Evaluate 刚把本帧动画姿态写完， 此刻读到的武器骨局部变换就是"动画本该给的值"，约束按幅度缩放覆写它 （单一写者契约：换弹期这根骨归约束）。
            s.Grip?.Update(s.Ctx?.ReloadSubmitted ?? false);

            // 俯仰姿态（TPS）：**只给本地玩家**——相机 pitch 是本地事实，套到远端会让别人的 上半身跟着本机镜头仰俯。
            if (s.Posture != null && _cameraPitchOf != null && _view.IsLocalSlot(i))
                s.Posture.Update(
                    (s.Ctx?.IsAiming ?? false) || (s.Ctx?.IsFireStance ?? false),
                    _cameraPitchOf());

            // 枪口对齐（ADS + 腰射）：只给本地玩家；把枪口前向掰向准心世界点。
            // 写序在俯仰姿态之后（枪骨位姿已由动画/姿态定稿），退出射击语境即停写。
            bool aiming = s.Ctx?.IsAiming ?? false;
            bool firing = s.Ctx?.IsFireStance ?? false;
            if (s.WeaponAim != null && _aimPointOf != null && _view.IsLocalSlot(i)
                && (aiming || firing) && _aimPointOf() is Vector3 aimPoint)
                s.WeaponAim.Update(aiming, firing, aimPoint);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();             // 先摘订阅：此后到达的事件一律不再进入本类
            for (int i = 0; i < _slots.Length; i++) ReleaseSlot(i);
        }

        // ---- IFrameEventAnimationConsumer（§8：帧事件 → 播放请求）----。

        /// <summary>
        /// 帧事件消费（**逻辑帧边界**，由 Match 总线在静默门之后派发）：fire 事件即进 Fire 系——
        /// 语义/通道由战斗层装配期从 Profile 单源解析（构造期校验保留）。
        /// </summary>
        public void OnFrameEvent(in FrameEvent e)
        {
            if (_disposed) return;

            // 死亡不在这里处理：客户端的 Death 帧事件可能来自本地预测（已随 P0 预测隔离关闭），
            // 且事件不可撤销——死亡表现唯一读权威状态（Tick 的 IsDead 轮询）。本消费者只处理开火窗。
            if (e.Kind != FrameEventKind.Fire) return;            // 未覆盖事件类型不处理（Crit/Explosion 未预建）

            if (!_view.TryGetSlot(e.EntityId, out int slotIndex)) return;   // 主体已回收：丢弃（不补播、不猜）
            if (slotIndex < 0 || slotIndex >= _slots.Length) return;

            var s = _slots[slotIndex];
            if (s?.Player == null || s.Machine == null || !s.Machine.Started) return;   // 灰盒/未启动：无动画面

            s.Ctx.RefreshFireWindow();                            // 事件即活动：窗重置满窗（先于一切路由）

            var cur = s.Machine.Current;
            if (cur == CharacterAnimId.FireIdle || cur == CharacterAnimId.FireWalk)
                return;                                           // 已在战斗根：窗已刷（持枪循环由叶自维护）
            s.Machine.Request(s.Ctx.IsMoving ? CharacterAnimId.FireWalk : CharacterAnimId.FireIdle);
        }

        // ---- 内部：槽位生命周期与测量 ----。

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
            s.Grip = null;                               // 约束无外部持有（每帧写、无持久状态）
            s.Machine = null;                             // 机器随播放器同生共死（无独立 Dispose 面）
            s.Ctx = null;
            s.LastPos = default;
            s.HasPos = false;
            s.MovingLatch = false;
            s.SmoothedSpeed = 0f;
            s.HasSmoothedSpeed = false;
            s.SmoothedDir = default;
            s.HasSmoothedDir = false;
            _slots[i] = null;
        }

        private bool TrySlot(int slotIndex, out SlotAnim s)
        {
            s = null;
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            s = _slots[slotIndex];
            return s?.Player != null && s.Machine != null;
        }

        // ---- IModuleStats（§11 诊断；调用方复用容器，禁止每次 new）----。

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
            public IAnimationPlaybackBackend Backend;
            public WeaponGripConstraint Grip;              // 持枪约束（可选：视图未声明偏移或骨缺失时为 null）
            public AimPitchPostureDriver Posture;          // 俯仰姿态（TPS；可选：人形 Chest 映射缺失或无相机注入时为 null）
            public WeaponAimConstraint WeaponAim;          // ADS/腰射枪口对齐（可选：枪骨/Muzzle 缺失时为 null）
            public SlotAnimContext Ctx;
            public HierarchicalStageMachine<CharacterAnimId, CombatAnimReq> Machine;
            public Vector3 LastPos;
            public bool HasPos;
            public bool MovingLatch;                       // 移动事实锁存（公式单源 LocomotionBlendMath）
            public float SmoothedSpeed;                    // 快照驱动槽位的测速低通值
            public bool HasSmoothedSpeed;
            public Vector3 SmoothedDir;                    // 移动方向低通值（单位向量；四向权重用）
            public bool HasSmoothedDir;
        }
    }
}
