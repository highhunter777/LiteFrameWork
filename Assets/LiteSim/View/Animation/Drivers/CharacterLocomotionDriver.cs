using System;
using System.Collections.Generic;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 角色移动动画驱动器（《动画模块专项设计》§7 更新次序中"Driver 解析参数与目标进度 → 提交播放/取消"
    /// 的首个消费者；§4"混合的权重由 Driver 给"的第一个真实消费者）：按视图实例的**速度与移动方向**
    /// 解析移动形态，经每实体自己的 <see cref="CharacterAnimationPlayer"/>
    /// （Profile 解析 → <see cref="AnimatorAnimationBackend"/>）提交。
    ///
    /// **形态矩阵（2026-09-27 瞄准态批）**：
    /// <code>
    /// 未瞄准   → Locomotion.MoveBlend = {Idle, Walk, Run}   速度轴 1D 混合（权重按速度连续插值）
    /// 瞄准+静止 → AimIdle（单片段）
    /// 瞄准+移动 → Locomotion.AimMoveBlend = {AimWalk_F/R/B/L}  4 向 strafe，相邻两片按夹角插值
    /// 开火驻留窗内 → 移动形态视同瞄准态（2026-09-28 裁决：腰射/跑射的臂姿语境配对；窗长为
    ///               CombatAnimMachine.FiringHoldSeconds = 2.0s 常量，纯表现语义——Sim 侧无窗），
    ///               且 AimMoveBlend 播放倍率随实际速度缩放（走 1× / 跑 2× 钳制，步频跟脚程无滑步）
    /// </code>
    /// - **速度轴改走混合器**：不再按阈值离散切片段，权重连续 ⇒ 没有档位抖动；权重经
    ///   <c>CharacterAnimationPlayer.UpdateBlendWeights</c> **就地更新**（不换句柄、不产生终态、不重建节点——
    ///   连续调参不该表现为"反复打断"）；只有**换形态**才重新提交。
    /// - **瞄准态**从 <see cref="SimView.IsAiming"/> 读（Sim 权威事实；本类仍不读输入设备）。
    /// - **限速在 Sim 侧**（`CombatConfig.AimMoveSpeed` = 走路档）——所以瞄准移动只需要 AimWalk 一套片段；
    ///   本类不做数值限速，只按实际速度解析形态。
    /// - **迟滞只用在"瞄准静止 ↔ 瞄准移动"这一处形态切换**（双阈值：起步用高阈、停下用低阈）；
    ///   其余两轴都是连续权重，不需要迟滞。
    /// - **权重数学独立成件**（<see cref="LocomotionBlendMath"/>：速度锚点常量 + 两支插值公式）——
    ///   本类只负责测速度、算夹角、提交形态与就地调权重，公式改动不牵动播放编排。
    ///
    /// **速度/方向来源**：视图 Transform 的帧间位移——本地（预测+和解衰减）与远端（快照插值）
    /// 同一来源，不读 Sim 位置；插值/衰减的速度天然平滑。
    /// **时钟**：跟随视图同一时间域（真实帧间隔——与 SimView 插值一致；世界暂停语义与视图移动
    /// 一并归 G2 表现收口，本类不单独缩放）。
    ///
    /// **灰盒降级**：无 Animator 的视图（灰盒胶囊/缺资源克隆）不建播放器——表现为无动画，
    /// 与缺角色资源的既有降级姿势一致。视图回收/复用时旧播放器随视图消失被 Dispose，
    /// 新占用者重建（Owner 代次语义由重建保证）。
    ///
    /// **帧事件（§8 表现事件）**：本类实现 <see cref="IFrameEventAnimationConsumer"/> 并**自订阅**
    /// `SimView.EventSink`（构造时挂上、<see cref="Dispose"/> 时摘下——生命周期与"视图消费者"同向，
    /// 不留"忘了接线"这类静默失效）。本批只消费 **Fire**（开火）：语义/通道经
    /// <see cref="FrameEventAnimationMap"/> 查表（不散写映射），播放落到**上半身叠加层**——
    /// **Fire 在途期间移动形态视同瞄准态**（站定腰射基础层进 AimIdle、跑射进 AimWalk——ADS 臂姿
    /// 的语境配对，2026-09-28 裁决；此前叠加在非瞄准髋上会握把错位，FullBody 方案因盖掉腿而废弃）。
    /// **移动中不提交开火叠加**（AimWalk 自带持枪步态、逐帧自洽；站姿射击片段叠上行走的骨盆必错位——
    /// 枪骨锚 root 不摆、手臂链随步态摆；包内无 AimWalk_Shoot，反馈由枪口特效承担）。
    /// **已在播则不重提**——当前 `ShootingSystem` 按住开火位即每逻辑帧产出一个 Fire 事件（射速节拍归
    /// P1 `WeaponSystem`），逐事件重提会把动作按在第 0 帧并刷 `Interrupted` 终态；接缝纪律见 §8 与
    /// <see cref="IFrameEventAnimationConsumer"/>。Hit/Death 的 FullBody 接管**不在本批**（归角色垂直切片余部）。
    ///
    /// **诊断（§11）**：实现 <see cref="IModuleStats"/>，聚合各后端的通道占用/节点数/未知绑定/截断计数
    /// （调用方复用容器，零分配）；<see cref="TryGetMotion"/> 供 HUD/测试读当前形态与权重。
    /// </summary>
    public sealed class CharacterLocomotionDriver : IDisposable, IModuleStats, IFrameEventAnimationConsumer
    {
        /// <summary>瞄准态"静止 → 移动"的进入阈值（m/s）。</summary>
        public const float AimMoveEnterMps = 0.6f;

        /// <summary>瞄准态"移动 → 静止"的退出阈值（m/s）——与进入阈值拉开即迟滞，防在单点来回切形态。</summary>
        public const float AimMoveExitMps = 0.3f;

        /// <summary>开火片段播放速度（倍率）——连发后坐节奏直接吃这里：完整播完再重起的循环从
        /// ~1.1s 压到 ~0.5s（"连发没后坐"的观感主因是一秒只踢一次），单发也更利落。
        /// Profile 区间上界即 2×。装配进战斗层 SlotAnimContext（《层次动画机设计》批B-①）。</summary>
        public const float FirePlaybackSpeed = 2f;

        private readonly SimView _view;
        private readonly AnimationProfile _profile;
        private readonly SlotAnim[] _slots = new SlotAnim[SimConfig.MaxEntities];

        /// <summary>开火语义与通道（**单一来源 = 帧事件决策表**：构造期解析并校验，不在本类散写映射）。</summary>
        private readonly AnimationId _fireId;
        private readonly AnimationChannel _fireChannel;

        /// <summary>复用权重容器（零分配：混合请求只读、不保留引用——见 <see cref="AnimationBlendRequest"/>）。</summary>
        private readonly float[] _moveWeights;
        private readonly float[] _aimWeights;

        private bool _disposed;

        public CharacterLocomotionDriver(SimView view, AnimationProfile profile = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _profile = profile ?? CombatGirlsAnimationProfile.Build();

            // 权重容器长度直接取登记定义（单一来源）；定义缺失或形状不对 = 配置错误，**构造期显性失败**，
            // 不等到运行时逐帧静默失败。
            if (!_profile.TryGetBlendDefinition(CharacterAnimationIds.MoveBlend, out var moveBlend)
                || moveBlend.SlotCount != 3)
                throw new ArgumentException(
                    $"Profile 缺 {CharacterAnimationIds.MoveBlend} 的 3 槽位混合定义（形状 {{Idle,Walk,Run}}）", nameof(profile));
            if (!_profile.TryGetBlendDefinition(CharacterAnimationIds.AimMoveBlend, out var aimBlend)
                || aimBlend.SlotCount != 4)
                throw new ArgumentException(
                    $"Profile 缺 {CharacterAnimationIds.AimMoveBlend} 的 4 槽位混合定义（形状 {{AimWalk_F,R,B,L}}）", nameof(profile));

            _moveWeights = new float[moveBlend.SlotCount];
            _aimWeights = new float[aimBlend.SlotCount];

            // 开火（帧事件路径）：映射从决策表来、Profile 必须有对应定义——配置错误在构造期显性失败，
            // 不等到第一次开火才发现"没东西可播"（与上面两条混合定义的校验同一纪律）
            if (!FrameEventAnimationMap.TryMap(FrameEventKind.Fire, out _fireId, out _fireChannel)
                || !_profile.TryGetDefinition(_fireId, out _))
                throw new ArgumentException(
                    $"Profile 缺开火语义（FrameEventAnimationMap 的 Fire 映射 → {CharacterAnimationIds.Fire}）", nameof(profile));

            _view.EventSink += OnFrameEvent;      // 自订阅（§8 接缝：接在静默门之后）；Dispose 时摘下
        }

        /// <summary>实际提交的**开火动作次数**（诊断/测试）：同段连发只计一次；上一轮播完后仍在开火才再计一次。
        /// 批B-① 起由战斗层各槽位上下文聚合（原驱动器字段的迁移位）——观测量口径不变。</summary>
        public int FireSubmits
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++) n += _slots[i]?.CombatCtx?.FireSubmits ?? 0;
                return n;
            }
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

        /// <summary>查询槽位当前动画语义（诊断/HUD/测试用；无动画视图返回 false）。</summary>
        public bool TryGetCurrent(int slotIndex, out AnimationId id)
        {
            id = default;
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            var s = _slots[slotIndex];
            if (s?.Player == null || !s.Current.IsValid) return false;
            id = s.Current;
            return true;
        }

        /// <summary>
        /// 读槽位当前形态与**权重**（诊断/HUD/测试；<paramref name="weights"/> 由调用方复用，零分配）：
        /// 返回 false = 无播放器 / 容器太小 / 当前是单片段形态（此时 <paramref name="id"/> 仍给出形态 ID）。
        /// </summary>
        public bool TryGetMotion(int slotIndex, out AnimationId id, float[] weights)
        {
            id = default;
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            var s = _slots[slotIndex];
            if (s?.Player == null || !s.Current.IsValid) return false;

            id = s.Current;
            return s.CopyWeightsTo(weights);
        }

        /// <summary>
        /// 每渲染帧推进（SimView 之后调用——视图位置先更新，本类再解析形态）：
        /// 视图增删检查 → 速度/方向测量 → 形态与权重解析 → 提交或就地调权重 → 播放器采样推进。
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
                    var animator = go.GetComponentInChildren<Animator>(true);
                    if (animator == null || animator.runtimeAnimatorController == null)
                    {
                        // 灰盒视图：无动画面——只记位置（保持速度判断的帧间基准），不建播放器。
                        // **不打日志**：这里是每帧路径，逐帧 LogWarning 会刷屏；降级事实由 §11 的
                        // IModuleStats（views 计数）与用例断言（AnimatedViews == 0）承担观测。
                        RememberPosition(s, go);
                        continue;
                    }

                    var backend = new AnimatorAnimationBackend(animator, AnimatorAnimationBackend.DefaultBlendSeconds,
                        _profile.UpperBodyMaskExclusions);   // 排除子树随 Profile（§6；纳入面由后端自动派生）
                    s.Backend = backend;
                    s.Player = new CharacterAnimationPlayer(backend, _profile);

                    // 战斗层装配（《层次动画机设计》批B-①）：fire 系规则正名进状态机——
                    // 驻留窗/移动门控/起跑终止/连发重起全在 FiringStage，驱动器只喂事实与转发事件。
                    s.CombatCtx = new SlotAnimContext
                    {
                        Player = s.Player,
                        FireId = _fireId,
                        FireChannel = _fireChannel,
                        FirePlaybackSpeed = FirePlaybackSpeed,
                    };
                    s.Combat = CombatAnimMachine.Build(s.CombatCtx);
                    s.Combat.Start(CombatAnimId.Idle);

                    LocomotionBlendMath.BuildSpeedWeights(0f, _moveWeights);
                    var first = SubmitBlend(s, CharacterAnimationIds.MoveBlend, _moveWeights);   // 初建落 Idle=1（不开局 T-pose）
                    Debug.LogWarning($"[Anim][diag] slot {i} 播放器建立：view「{go.name}」animator「{animator.name}」"
                        + $"ctrl={animator.runtimeAnimatorController.name} layers={animator.layerCount} 初建提交={first}");
                }

                if (s.HasPos && dt > 0f)
                {
                    float speed = MeasureSpeed(s, go.transform.position, dt, out Vector3 moveDir);

                    // 迟滞锁存**两分支共用刷新**：Firing 的移动门控读它——只在一个分支里更新会让
                    // 门控吃到非瞄准期冻结的陈旧值（实测：跑动停稳后腰射被误判"移动中"而不播动画）。
                    bool moving = s.AimMoving ? speed > AimMoveExitMps : speed >= AimMoveEnterMps;
                    s.AimMoving = moving;

                    // 战斗层推进：事实先行（IsMoving 喂给状态）→ 状态机 Tick（Advance + OnUpdate——
                    // 窗尽回 Idle / 起跑整态退出（B-① 原形，2026-09-30 回退驻留窗口径）的事务在此应用）
                    // → 移动层按"当前是否 Firing"合成瞄准语境。
                    s.CombatCtx.IsMoving = moving;
                    s.Combat.Tick(dt);

                    // 瞄准态来自 Sim 权威事实；**Firing 态即射击语境**（驻留窗内含——事件刷新制在
                    // FiringStage 内）。站定腰射基础层进 AimIdle、跑射进 AimWalk（2026-09-28 裁决），
                    // 叠加层手臂与基础层同语境。
                    s.IsAim = _view.IsAiming(i) || IsCombatFiring(s);

                    Submit(s, speed, in moveDir, go.transform.rotation);
                }

                RememberPosition(s, go);
                s.Player?.Tick(dt);                                    // 唯一驱动入口（§7）
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
        /// 帧事件消费（**逻辑帧边界**，由 <see cref="SimView.EventSink"/> 在静默门之后调用——回滚重放/和解
        /// 段已被挡，故"本地即时反馈与权威确认"不会各播一次，§8 合并规则）。本批只接开火：
        /// **查表 → 主体解析槽位 → 战斗层状态机**（已在 Firing 态则走态内合并口续窗/重起；否则
        /// Request 进态——进态提交与门控由 FiringStage 自持）；其余事件类型不处理。
        /// </summary>
        public void OnFrameEvent(in FrameEvent e)
        {
            if (_disposed) return;
            if (!FrameEventAnimationMap.TryMap(e.Kind, out var id, out _)) return;   // 未覆盖的事件类型不处理
            if (!id.Equals(_fireId)) return;                                        // 本批只接开火（Hit/Death 归余部）

            if (!_view.TryGetSlot(e.EntityId, out int slotIndex)) return;            // 主体已回收：丢弃（不补播、不猜）
            if (slotIndex < 0 || slotIndex >= _slots.Length) return;

            var s = _slots[slotIndex];
            if (s?.Player == null || s.Combat == null) return;                       // 灰盒视图/播放器未建：无动画面可播

            if (s.Combat.Started && s.Combat.Current == CombatAnimId.Firing)
                s.CombatCtx.Firing.OnFireEvent();        // 态内合并：续窗 + 在播吞/已完重起（不产生自转事务）
            else
                s.Combat.Request(CombatAnimId.Firing, new CombatAnimReq { IsFireEvent = true });
        }

        /// <summary>战斗层是否处于 Firing（驻留窗内含——事件刷新制）。</summary>
        private static bool IsCombatFiring(SlotAnim s)
            => s.Combat != null && s.Combat.Started && s.Combat.Current == CombatAnimId.Firing;

        // ---- 内部：形态解析与提交 ----

        /// <summary>本帧形态：同形态就地调权重，换形态才重新提交（形态矩阵见类注释）。
        /// 开火系的全部规则（驻留窗/移动门控/起跑终止/连发重起）已归战斗层状态机（批B-①）——
        /// 这里只按"瞄准语境 + 移动与否"选移动层形态。</summary>
        private void Submit(SlotAnim s, float speed, in Vector3 moveDir, Quaternion rotation)
        {
            if (s.IsAim)
            {
                // 迟滞锁存已在 Tick 统一刷新（两分支共用——见那里的注释）；这里只按"移动与否"选形态
                if (s.AimMoving)
                {
                    Vector3 facing = rotation * Vector3.forward;
                    float rel = Vector3.SignedAngle(facing, moveDir, Vector3.up);   // [-180,180]：正 = 朝向的右侧
                    LocomotionBlendMath.BuildAimWeights(rel, _aimWeights);
                    SubmitBlend(s, CharacterAnimationIds.AimMoveBlend, _aimWeights);

                    // 步频同步（2026-09-30 移动腰射批）：AimWalk 原生步频锚在走路档（AimMoveSpeed）——
                    // 不限速的移动腰射按实际速度缩放混合器倍率（走 1×、跑 2× 钳制），步频跟脚程一致
                    // ⇒ 无"走姿步频配跑速"滑步。ADS 路径 Sim 已限走路档 ⇒ 倍率恒 ≈1，天然无感。
                    // 上限 2× = Run 档速度：没有 AimRun 片段（债 #5），更快的冲刺钳在 2×。
                    s.Player.TrySetBlendSpeed(s.Handle,
                        Mathf.Clamp(speed / CombatConfig.AimMoveSpeed, 1f, 2f));
                }
                else
                {
                    SubmitSingle(s, CharacterAnimationIds.AimIdle);
                }
                return;
            }

            LocomotionBlendMath.BuildSpeedWeights(speed, _moveWeights);
            SubmitBlend(s, CharacterAnimationIds.MoveBlend, _moveWeights);
        }

        /// <summary>视图帧间位移 → 水平速度（m/s）与单位移动方向（无位移时方向为零向量）。</summary>
        private static float MeasureSpeed(SlotAnim s, Vector3 pos, float dt, out Vector3 dir)
        {
            float dx = pos.x - s.LastPos.x;
            float dz = pos.z - s.LastPos.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            dir = len > 1e-5f ? new Vector3(dx / len, 0f, dz / len) : Vector3.zero;
            return len / dt;
        }

        /// <summary>提交/更新混合形态：同 ID 且句柄仍在播 → **就地调权重**（零重建、零终态）；否则重新提交。</summary>
        private static bool SubmitBlend(SlotAnim s, AnimationId id, float[] weights)
        {
            if (s.Current.Equals(id) && s.Handle.IsValid && s.Player.UpdateBlendWeights(s.Handle, weights))
            {
                s.StoreWeights(weights);
                return true;
            }

            var result = s.Player.PlayBlend(new AnimationBlendRequest(id, AnimationChannel.Locomotion, weights));
            if (!result.Accepted) return false;            // 拒绝不推进状态（下次重试，不丢帧）

            s.Current = id;
            s.Handle = result.Handle;
            s.StoreWeights(weights);
            return true;
        }

        /// <summary>提交单片段形态（同 ID 已在播则不重复提交——重复提交会白换句柄、给旧播放收 Interrupted）。</summary>
        private static void SubmitSingle(SlotAnim s, AnimationId id)
        {
            if (s.Current.Equals(id) && s.Handle.IsValid) return;

            var result = s.Player.Play(new AnimationRequest(id, AnimationChannel.Locomotion));
            if (!result.Accepted) return;

            s.Current = id;
            s.Handle = result.Handle;
            s.IsBlendForm = false;
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
            s.Player?.Dispose();                         // 销毁序：代次失效 → 终态 → 释放后端（Graph.Destroy）
            s.Player = null;
            s.Backend = null;
            s.Combat = null;                             // 战斗层状态机随播放器同生共死（播放器销毁即失效）
            s.CombatCtx = null;
            s.Current = default;
            s.HasPos = false;
            s.IsAim = false;
            s.AimMoving = false;
            _slots[i] = null;
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
        }

        private sealed class SlotAnim
        {
            public CharacterAnimationPlayer Player;
            /// <summary>后端引用（诊断聚合用；与 Player 同生共死）。</summary>
            public AnimatorAnimationBackend Backend;
            /// <summary>战斗层状态机（批B-①：fire 系规则的承载件——随 Player 同生共死）。</summary>
            public HierarchicalStageMachine<CombatAnimId, CombatAnimReq> Combat;
            /// <summary>战斗层的槽位上下文（Player/Fire 绑定/IsMoving 事实——见 SlotAnimContext）。</summary>
            public SlotAnimContext CombatCtx;
            /// <summary>当前形态语义（单片段或混合 ID——<see cref="TryGetCurrent"/> 的读值）。</summary>
            public AnimationId Current;
            /// <summary>当前形态的句柄（就地调权重必须拿它——旧句柄会拒绝）。</summary>
            public AnimationHandle Handle;
            public Vector3 LastPos;
            public bool HasPos;
            /// <summary>瞄准态（来自 <see cref="SimView.IsAiming"/>，每帧刷新）。</summary>
            public bool IsAim;
            /// <summary>瞄准+移动的迟滞锁存（见 <see cref="AimMoveEnterMps"/>/<see cref="AimMoveExitMps"/>）。
            /// **Tick 统一刷新**——移动门控（战斗层 Firing 的提交门）以它为据，单分支刷新会让门控吃到
            /// 非瞄准期冻结的陈旧值（实测：跑动停稳后腰射被误判"移动中"而不播动画）。</summary>
            public bool AimMoving;
            /// <summary>当前是否混合形态（单片段形态没有权重）。</summary>
            public bool IsBlendForm;
            /// <summary>最近一次提交/更新的权重副本（诊断与用例的读值；长度随形态 3 或 4）。</summary>
            private float[] _weights;

            public void StoreWeights(float[] weights)
            {
                if (_weights == null || _weights.Length != weights.Length) _weights = new float[weights.Length];
                Array.Copy(weights, _weights, weights.Length);
                IsBlendForm = true;
            }

            public bool CopyWeightsTo(float[] into)
            {
                if (!IsBlendForm || _weights == null || into == null || into.Length < _weights.Length) return false;
                Array.Copy(_weights, into, _weights.Length);
                return true;
            }
        }
    }
}