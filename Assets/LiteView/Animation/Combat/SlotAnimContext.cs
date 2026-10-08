using System;
using LiteFramework.Animation;
using LiteSim;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 槽位上下文（各态对"播放器 + 事实 + 双通道形态跟踪"的窄面——按槽位重建，不持槽数组引用；
    /// 单机单上下文）。**层间不引用**：跨层事实（IsAiming/锁存/速度/夹角）全部由驱动器喂入。
    /// </summary>
    public sealed class SlotAnimContext
    {
        public CharacterAnimationPlayer Player;

        // ---- 开火装配（驱动器构造/建槽期注入）----

        /// <summary>换弹片段播放倍率——片段时长对齐 Sim 换弹时长（`WeaponConfig.Default.ReloadFrames`
        /// / TickRate），钳制在 Profile 登记的速度区间内（越界会被播放器拒绝）；未知片段时长 ⇒ 1×。</summary>
        public float ReloadPlaybackSpeed = 1f;

        /// <summary>射击窗长（秒）＝ <see cref="CombatConfig.FireStanceFrames"/> / SimConfig.TickRate（1s 单源派生）。</summary>
        public float FireHoldSeconds;

        // ---- 输入事实（驱动器每帧喂）----

        /// <summary>瞄准中（SimView.IsAiming——Sim 权威；战斗根活跃判据之一）。</summary>
        public bool IsAiming;

        /// <summary>换弹中（SimView.IsReloading——Sim 权威，武器私有面投影）：真时战斗根恒驻
        /// Reload 叶（开火/瞄准事实让位）；假时同帧按窗/瞄准事实选叶。</summary>
        public bool IsReloading;

        /// <summary>死亡事实（SimView.IsDead——Hp≤0 快照可重建；战斗根死亡裁决判据，
        /// 不可逆：置位后恒驻死亡叶，窗/退根/Aim 系全被守卫）。</summary>
        public bool IsDead;

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

        // ---- 态引用（驱动器窗刷新——装配期注入）----

        internal CombatRootStage Root;

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
}
