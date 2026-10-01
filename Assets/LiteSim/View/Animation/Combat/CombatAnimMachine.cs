using System;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 角色动画层次机的阶段 id（《层次动画机设计》v0.5 五次重设计/v0.6 六次裁决——**TId 枚举一统**：
    /// 双复合根 + 全部叶）。批C 消费者到位时在此扩 Reloading/Hit/Death/Evade（同战斗根）。
    /// </summary>
    public enum CharacterAnimId
    {
        /// <summary>战斗根（复合态①——上层）：射击窗持有者 + 退根/降级**单点裁决**者。</summary>
        CombatRoot = 0,

        /// <summary>移动根（复合态②——下层）：默认移动，MoveBlend 独占 Locomotion 通道。</summary>
        LocomotionRoot = 1,

        /// <summary>开火·静止：站姿射击片段按事件重播 / 播完持 AimIdle 循环填窗。</summary>
        FireIdle = 2,

        /// <summary>开火·移动：AimMoveBlend 四向循环（不播站姿射击——债 #5）。</summary>
        FireWalk = 3,

        /// <summary>瞄准·静止。</summary>
        AimIdle = 4,

        /// <summary>瞄准·移动：4 向 strafe + 步频倍率。</summary>
        AimWalk = 5,

        /// <summary>移动根·速度≈0（同形态 MoveBlend，态界只服务诊断/事件路由）。</summary>
        Idle = 6,

        /// <summary>移动根·速度轴 1D 混合 {Idle,Walk,Run}。</summary>
        Moving = 7,
    }

    /// <summary>
    /// 迁移请求载荷：<see cref="IsFireEvent"/> = 本次迁移由开火判定触发——FireIdle 据此区分
    /// "播一轮射击片段"（事件进态）与"持枪站姿"（停步进态/重入不重播）。
    /// </summary>
    public struct CombatAnimReq
    {
        /// <summary>进态成因是开火事件（驱动器事件路由置位；层内迁移走无参 Request = false）。</summary>
        public bool IsFireEvent;
    }

    /// <summary>
    /// 单机双根角色动画层次机装配（《层次动画机设计》§1/§2——v0.5/v0.6）：
    /// - **互斥覆盖**：战斗根（上层）任一态激活 = 覆盖移动根（框架多根 = 互斥平级根，跨根 = 全退全进）；
    /// - **战斗层不可被移动层打断**：退根只有两条路——Fire*：窗尽 ∧ !IsAiming；Aim*：!IsAiming（单点裁决）；
    ///   移动事实只驱动层内 idle↔walk 轴（两族）与退根叶选择；
    /// - **射击窗（六次裁决）**：窗长 = <see cref="CombatConfig.FireStanceFrames"/>/TickRate（1s 独立常量），
    ///   事件刷新＝重置满窗；**窗内保持 clip**——FireIdle 射击片段播完持 AimIdle 循环、FireWalk 即
    ///   AimMoveBlend 循环；窗尽 = 保持 clip 的终点（同形态次态续播保相位、异形态提交替换）；
    /// - **通道接管**：各根自管本根通道（移动根收/建 Locomotion、战斗根收 FullBody）——事务序
    ///   （先深→浅退出、再浅→深进入）结构性保证先停旧通道再开新通道。
    /// </summary>
    public static class CombatAnimMachine
    {
        /// <summary>装配一台单机双根机（每槽一台，随播放器同生共死——Owner 代次由重建保证）。</summary>
        public static HierarchicalStageMachine<CharacterAnimId, CombatAnimReq> Build(SlotAnimContext ctx)
        {
            return new HierarchicalStageMachine<CharacterAnimId, CombatAnimReq>(
                "CharacterAnim",
                new (CharacterAnimId, IStage<CharacterAnimId, CombatAnimReq>)[]
                {
                    (CharacterAnimId.CombatRoot, new CombatRootStage(ctx)),
                    (CharacterAnimId.LocomotionRoot, new LocomotionRootStage(ctx)),
                    (CharacterAnimId.FireIdle, new FireIdleStage(ctx)),
                    (CharacterAnimId.FireWalk, new FireWalkStage(ctx)),
                    (CharacterAnimId.AimIdle, new AimIdleStage(ctx)),
                    (CharacterAnimId.AimWalk, new AimWalkStage(ctx)),
                    (CharacterAnimId.Idle, new IdleStage(ctx)),
                    (CharacterAnimId.Moving, new MovingStage(ctx)),
                },
                composites: new CompositeSpec<CharacterAnimId>[]
                {
                    // 初始子态仅形式性存在（战斗根进入恒由事件/ADS 显式 Request 叶）；无历史——
                    // 窗尽降级与 ADS 进入都按事实选叶，不复活旧子页（v0.5 裁决：不引入历史语义）
                    new CompositeSpec<CharacterAnimId>(CharacterAnimId.CombatRoot, CharacterAnimId.AimIdle,
                        HistoryMode.None,
                        CharacterAnimId.FireIdle, CharacterAnimId.FireWalk,
                        CharacterAnimId.AimIdle, CharacterAnimId.AimWalk),
                    new CompositeSpec<CharacterAnimId>(CharacterAnimId.LocomotionRoot, CharacterAnimId.Idle,
                        HistoryMode.None,
                        CharacterAnimId.Idle, CharacterAnimId.Moving),
                });
        }
    }

    /// <summary>
    /// 槽位上下文（各态对"播放器 + 事实 + 双通道形态跟踪"的窄面——按槽位重建，不持槽数组引用；
    /// v0.5 起单机单上下文）。**层间不引用**：跨层事实（IsAiming/锁存/速度/夹角）全部由驱动器喂入。
    /// </summary>
    public sealed class SlotAnimContext
    {
        public CharacterAnimationPlayer Player;

        // ---- 开火装配（驱动器构造/建槽期注入）----

        /// <summary>开火片段播放速度（2×——后坐节奏；窗长 ≥ 片段播放时长的不变式由它参与）。</summary>
        public float FirePlaybackSpeed;

        /// <summary>射击窗长（秒）＝ <see cref="CombatConfig.FireStanceFrames"/> / SimConfig.TickRate（1s 单源派生）。</summary>
        public float FireHoldSeconds;

        // ---- 输入事实（驱动器每帧喂）----

        /// <summary>瞄准中（SimView.IsAiming——Sim 权威；战斗根活跃判据之一）。</summary>
        public bool IsAiming;

        /// <summary>移动事实锁存（迟滞公式单源 <see cref="LocomotionBlendMath.UpdateLatch"/>）。</summary>
        public bool IsMoving;

        /// <summary>视图帧间位移速度（m/s）。</summary>
        public float Speed;

        /// <summary>移动方向 vs 朝向的夹角（度；正 = 朝向右侧——AimWalk 四向权重）。</summary>
        public float MoveRelAngleDeg;

        // ---- 权重容器（驱动器复用数组注入——零分配路径；播放器只在提交内读取不保留引用）----

        /// <summary>MoveBlend 槽位权重（序 {Idle, Walk, Run}）。</summary>
        public float[] MoveWeights;

        /// <summary>AimMoveBlend 槽位权重（序 {F, R, B, L}）。</summary>
        public float[] AimWeights;

        // ---- 诊断读回（最近一次提交/更新的权重副本——TryGetMotion 的读值）----

        public readonly float[] LocoWeightsCopy = new float[3];
        public readonly float[] BodyWeightsCopy = new float[4];

        // ---- Locomotion 通道（移动根独占：进根建、离根收）----

        public AnimationHandle LocoHandle;

        // ---- FullBody 通道（战斗根叶共用："同 ID 在播即续播、异 ID 提交替换"）----

        public AnimationId BodyForm;
        public AnimationHandle BodyHandle;
        public bool BodyIsBlend;

        // ---- 观测 ----

        /// <summary>实际提交的**开火动作**次数（诊断/测试——同段连发只计一次，播完仍在开火才再计）。</summary>
        public int FireSubmits;

        // ---- 态引用（驱动器事件路由/窗刷新——装配期注入）----

        internal CombatRootStage Root;
        internal FireIdleStage FireIdleRef;

        // ---- FullBody 形态提交（战斗根叶共用）----

        /// <summary>提交单片段形态（FullBody）：同 ID **仍在播** → 续播保相位（不重提交——重提交会按
        /// 第 0 帧并刷终态）；不在播/异 ID → 提交（旧形态由通道仲裁替换）。拒绝返回 false（调用方自愈重试）。</summary>
        public bool PlayBodySingle(AnimationId id, float speed = 1f)
        {
            if (BodyForm.Equals(id) && BodyHandle.IsValid
                && (!Player.TryGetState(BodyHandle, out var st) || st.IsPlaying))
                return true;
            var r = Player.Play(new AnimationRequest(id, AnimationChannel.FullBody, speed: speed));
            if (!r.Accepted) return false;
            BodyForm = id;
            BodyHandle = r.Handle;
            BodyIsBlend = false;
            return true;
        }

        /// <summary>提交混合形态（FullBody）：同 ID 在播 → **就地调权重续播**（跨态同形态——
        /// AimWalk↔FireWalk、窗尽降级，不换句柄零淡化）；否则提交。拒绝返回 false。</summary>
        public bool PlayBodyBlend(AnimationId id, float[] weights)
        {
            if (BodyForm.Equals(id) && UpdateBodyBlendWeights(weights)) return true;
            var r = Player.PlayBlend(new AnimationBlendRequest(id, AnimationChannel.FullBody, weights));
            if (!r.Accepted) return false;
            BodyForm = id;
            BodyHandle = r.Handle;
            BodyIsBlend = true;
            Array.Copy(weights, BodyWeightsCopy, weights.Length);
            return true;
        }

        /// <summary>FullBody 混合权重就地更新（句柄失效/非混合 → false）。</summary>
        public bool UpdateBodyBlendWeights(float[] weights)
        {
            if (!BodyIsBlend || !BodyHandle.IsValid) return false;
            if (!Player.UpdateBlendWeights(BodyHandle, weights)) return false;
            Array.Copy(weights, BodyWeightsCopy, weights.Length);
            return true;
        }
    }

    /// <summary>叶/根共用的语境让位检查：已有挂起请求（事件路由进 Fire 系 / 根的退根降级）时
    /// 本帧不再发自己的请求——last-wins 语义下"后来的赢"，让位保证已挂的更高级裁决不被覆盖。</summary>
    internal static class StageGate
    {
        internal static bool Pending(IStageHost<CharacterAnimId, CombatAnimReq> m)
            => m is HierarchicalStageMachine<CharacterAnimId, CombatAnimReq> hsm && hsm.HasPending;
    }

    /// <summary>
    /// 战斗根复合态（上层）：射击窗持有者 + 退根/降级**单点裁决**（v0.5/v0.6 §2——窗递减与
    /// "窗尽 → 去哪"收在此处，叶只发层内轴请求且先让位）。
    /// </summary>
    internal sealed class CombatRootStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;
        private float _window;

        internal CombatRootStage(SlotAnimContext ctx)
        {
            _ctx = ctx;
            ctx.Root = this;
        }

        /// <summary>射击窗剩余（秒）——测试/诊断读值（0 = 窗不在）。</summary>
        internal float Window => _window;

        /// <summary>事件刷新＝重置满窗（上限即窗长，不累加）——驱动器在事件路由前调用（窗先于一切门控）。</summary>
        internal void RefreshWindow() => _window = _ctx.FireHoldSeconds;

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req) { }

        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;                 // 事件路由已挂（进 Fire 系优先）——本帧不裁决

            // 批次E（八次裁决）：`IsAiming` 在场即充值窗（Sim 侧瞄准帧同步置窗——两层同源同长；
            // 长按 ADS 的松开尾巴与点按瞄准的间隙尾巴同一语义：窗内不回移动层）
            if (_ctx.IsAiming) _window = _ctx.FireHoldSeconds;
            else if (_window > 0f) _window = Mathf.Max(0f, _window - elapseSeconds);

            // 退根/降级单点裁决（批次E 统一——**全族同一条退根路**：窗尽 ∧ !IsAiming；
            // 移动事实永不触发退根，只选叶）：
            //   窗尽 ∧ IsAiming → Fire* 降级 Aim 叶（同形态续播保相位；Aim 系无需请求——已在 Aim 叶）
            //   窗尽 ∧ !IsAiming → 移动根叶（窗尽才离开——窗内"不能回移动层"的六次裁决语义）
            if (_window <= 0f)
            {
                if (_ctx.IsAiming)
                {
                    if (m.Current == CharacterAnimId.FireIdle || m.Current == CharacterAnimId.FireWalk)
                        m.Request(_ctx.IsMoving ? CharacterAnimId.AimWalk : CharacterAnimId.AimIdle);
                    // Aim 系 + IsAiming：已在瞄准叶——无动作
                }
                else
                {
                    m.Request(_ctx.IsMoving ? CharacterAnimId.Moving : CharacterAnimId.Idle);
                }
            }
        }

        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m)
        {
            // 退根收口：FullBody 通道所有权终止（次根同帧提交替换/淡出——事务序先退后进）；
            // 窗语义随离场清零（再进战斗根必经事件刷新或 ADS 建立）
            if (_ctx.BodyHandle.IsValid)
                _ctx.Player.Stop(_ctx.BodyHandle, AnimationStopReason.Cancelled);
            _ctx.BodyForm = default;
            _ctx.BodyHandle = default;
            _ctx.BodyIsBlend = false;
            _window = 0f;
        }
    }

    /// <summary>
    /// 移动根复合态（下层）：MoveBlend 独占 Locomotion 通道——进根提交（按当前速度权重）、
    /// 每帧就地调权重（同形态连续，不换句柄）、离根收口（覆盖开始：先停 Locomotion 再让战斗根开 FullBody）；
    /// **瞄准建立 → 进战斗根**（覆盖开始）的入口裁决也在此（与战斗根的退根裁决对称——各根管各根的出界）。
    /// </summary>
    internal sealed class LocomotionRootStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal LocomotionRootStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }

        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
        {
            // 覆盖解除/初建：重提交 MoveBlend（初建 Speed=0 → Idle=1，不开局 T-pose）
            SubmitMoveBlend();
        }

        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;                 // 事件路由已挂（进 Fire 系优先于同帧 ADS 建立）

            // 瞄准建立 → 进战斗根（覆盖开始——按锁存选叶；fire 事件路径由驱动器直接 Request，不经此）
            if (_ctx.IsAiming)
            {
                m.Request(_ctx.IsMoving ? CharacterAnimId.AimWalk : CharacterAnimId.AimIdle);
                return;
            }

            // 速度轴权重就地维护（同形态连续——不换句柄、无终态）
            LocomotionBlendMath.BuildSpeedWeights(_ctx.Speed, _ctx.MoveWeights);
            if (_ctx.LocoHandle.IsValid && _ctx.Player.UpdateBlendWeights(_ctx.LocoHandle, _ctx.MoveWeights))
            {
                Array.Copy(_ctx.MoveWeights, _ctx.LocoWeightsCopy, _ctx.MoveWeights.Length);
                return;
            }
            SubmitMoveBlend();                                // 句柄不在（初建被拒/异常终态）→ 自愈重提交
        }

        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m)
        {
            // 覆盖开始：收 Locomotion 通道——事务序（先深→浅退出、再浅→深进入）保证先停后播
            if (_ctx.LocoHandle.IsValid)
                _ctx.Player.Stop(_ctx.LocoHandle, AnimationStopReason.Cancelled);
            _ctx.LocoHandle = default;
        }

        private void SubmitMoveBlend()
        {
            LocomotionBlendMath.BuildSpeedWeights(_ctx.Speed, _ctx.MoveWeights);
            var r = _ctx.Player.PlayBlend(new AnimationBlendRequest(
                CharacterAnimationIds.MoveBlend, AnimationChannel.Locomotion, _ctx.MoveWeights));
            if (!r.Accepted) return;                          // 拒绝不推进——OnUpdate 自愈重试
            _ctx.LocoHandle = r.Handle;
            Array.Copy(_ctx.MoveWeights, _ctx.LocoWeightsCopy, _ctx.MoveWeights.Length);
        }
    }

    /// <summary>AimWalk/FireWalk 共用的四向形态提交与逐帧维护（同形态不同语境——权重/倍率同数学）。</summary>
    internal static class AimWalkForm
    {
        /// <summary>提交/续播 AimMoveBlend（进态用——含同形态续播保相位）。</summary>
        internal static void Submit(SlotAnimContext ctx)
        {
            LocomotionBlendMath.BuildAimWeights(ctx.MoveRelAngleDeg, ctx.AimWeights);
            ctx.PlayBodyBlend(CharacterAnimationIds.AimMoveBlend, ctx.AimWeights);
        }

        /// <summary>逐帧维护：四向权重就地更新（句柄死自愈重提交）+ 步频倍率跟脚程（限速下恒≈1）。</summary>
        internal static void Maintain(SlotAnimContext ctx)
        {
            LocomotionBlendMath.BuildAimWeights(ctx.MoveRelAngleDeg, ctx.AimWeights);
            if (!ctx.UpdateBodyBlendWeights(ctx.AimWeights))
                Submit(ctx);
            if (ctx.BodyHandle.IsValid)
                ctx.Player.TrySetBlendSpeed(ctx.BodyHandle,
                    Mathf.Clamp(ctx.Speed / CombatConfig.AimMoveSpeed, 1f, 2f));
        }
    }

    /// <summary>
    /// 开火·静止（六次裁决"窗内保持 clip"）：Fire 事件进态 → 播一轮站姿射击（2× 单次）；
    /// **片段播完 ∧ 窗在 → 态内切持枪站姿（AimIdle 循环）填窗，不迁移**；窗内新事件 → 重播片段
    /// ＋窗已重置（在播吞——重提会按第 0 帧并刷终态）；起跑 → 迁 FireWalk（片段由 AimMoveBlend
    /// 提交替换、**窗不清**——限速语境保持）。
    /// </summary>
    internal sealed class FireIdleStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;
        private AnimationHandle _fireClip;                     // 本轮射击片段（播完清空 → 转持枪循环）

        internal FireIdleStage(SlotAnimContext ctx)
        {
            _ctx = ctx;
            ctx.FireIdleRef = this;
        }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }

        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
        {
            // 开火事件进态 → 播一轮；停步进态（FireWalk→FireIdle）/其他 → 持枪站姿不重播
            if (req.IsFireEvent) SubmitFireClip();
            else _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle);
        }

        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;                  // 根已挂退根/降级——叶放弃层内请求

            if (_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.FireWalk);           // 起跑：clip 由次态提交替换、窗不清
                return;
            }

            if (_fireClip.IsValid)
            {
                // 本轮片段播完 ∧ 窗在 → 态内切持枪循环（窗长 > 片段时长的填窗段——v0.6 核心）
                if (_ctx.Player.TryGetState(_fireClip, out var st) && !st.IsPlaying)
                {
                    _fireClip = default;
                    _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle);
                }
            }
            else
            {
                // 持枪循环自愈（幂等）：在播即续播、句柄死即重提交
                _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle);
            }
        }

        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        // 离场不无条件 Stop：窗尽退根/降级由根收口（同形态续播/异形态提交替换）；起跑由次态提交替换

        /// <summary>事件合并口（驱动器在 FireIdle 态内转发；窗已由驱动器 RefreshWindow 先行刷新）：
        /// 在播**吞**（重提会按第 0 帧并刷终态）；已完/未提交 ∧ 站定 → 重起一轮（连发）；
        /// 移动中不播站姿片段（等迁 FireWalk——站姿片段不可盖步态）。</summary>
        internal void OnFireEvent()
        {
            if (_fireClip.IsValid && _ctx.Player.TryGetState(_fireClip, out var st) && st.IsPlaying) return;
            if (_ctx.IsMoving) return;
            SubmitFireClip();
        }

        private void SubmitFireClip()
        {
            var r = _ctx.Player.Play(new AnimationRequest(
                CharacterAnimationIds.Fire, AnimationChannel.FullBody, speed: _ctx.FirePlaybackSpeed));
            _fireClip = r.Accepted ? r.Handle : default;        // 拒绝不推进——下一个事件重试
            if (!r.Accepted) return;
            _ctx.FireSubmits++;
            _ctx.BodyForm = CharacterAnimationIds.Fire;
            _ctx.BodyHandle = r.Handle;
            _ctx.BodyIsBlend = false;
        }
    }

    /// <summary>
    /// 开火·移动（"firewalk 也一样"——同窗规则）：AimMoveBlend 四向**循环**即其保持 clip
    /// （不播站姿射击——无移动射击资产，债 #5，反馈由枪口特效承担）；停步 → 迁 FireIdle 持站姿不重播。
    /// </summary>
    internal sealed class FireWalkStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal FireWalkStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
            => AimWalkForm.Submit(_ctx);                        // 含同形态续播（AimWalk 开火 → 就地续播不重提交）
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (!_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.FireIdle);            // 停步：持枪站姿（窗保持）
                return;
            }
            AimWalkForm.Maintain(_ctx);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }

    /// <summary>瞄准·静止：AimIdle 单片段循环（FullBody 覆盖；窗无涉——活跃判据只 IsAiming）。</summary>
    internal sealed class AimIdleStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal AimIdleStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
            => _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle);
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.AimWalk);            // 迟滞进入（锁存置位）
                return;
            }
            _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle); // 在播续播/死了自愈（幂等）
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }

    /// <summary>瞄准·移动：AimMoveBlend 四向混合 + 步频倍率（限速走路档 ⇒ 倍率恒≈1）。</summary>
    internal sealed class AimWalkStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal AimWalkStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
            => AimWalkForm.Submit(_ctx);
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (!_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.AimIdle);            // 迟滞退出（锁存清零）
                return;
            }
            AimWalkForm.Maintain(_ctx);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }

    /// <summary>移动根·静止叶（形态归移动根——本叶只做迟滞轴迁移）。</summary>
    internal sealed class IdleStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal IdleStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req) { }
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (_ctx.IsMoving) m.Request(CharacterAnimId.Moving);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }

    /// <summary>移动根·移动叶（同上——Idle↔Moving 同形态 MoveBlend，跨越不重提交）。</summary>
    internal sealed class MovingStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal MovingStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req) { }
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (!_ctx.IsMoving) m.Request(CharacterAnimId.Idle);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }
}
