using System;
using LiteFramework.Animation;
using LiteSim;

namespace LiteView.Animation
{
    /// <summary>
    /// 槽位上下文（各态对"播放器 + 事实 + 双通道形态跟踪"的窄面——按槽位重建，不持槽数组引用；
    /// 单机单上下文）。**层间不引用**：跨层事实（IsAiming/锁存/速度/夹角）全部由驱动器喂入。
    /// </summary>
    public sealed class SlotAnimContext
    {
        public AnimationPlayer Player;

        // ---- 开火装配（驱动器构造/建槽期注入）----

        /// <summary>换弹片段播放倍率——片段时长对齐 Sim 换弹时长（`WeaponConfig.Default.ReloadFrames`
        /// / TickRate），钳制在 Profile 登记的速度区间内（越界会被播放器拒绝）；未知片段时长 ⇒ 1×。</summary>
        public float ReloadPlaybackSpeed = 1f;

        /// <summary>射击窗长（秒）＝ <see cref="CombatConfig.FireStanceFrames"/> / SimConfig.TickRate（1s 单源派生）。</summary>
        public float FireHoldSeconds;

        // ---- 输入事实（驱动器每帧喂）----

        /// <summary>瞄准中（SimView.IsAiming——Sim 权威；战斗根活跃判据之一）。</summary>
        public bool IsAiming;

        /// <summary>换弹中（SimView.IsReloading——Sim 权威，武器私有面投影）：真时由
        /// <see cref="UpdateReloadUpper"/> 提交 Overlay 上半身叠加（**不进战斗根**——移动根照常出步）；
        /// 死亡或事实清除时叠加层释放。</summary>
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

        // ---- Base 通道（移动根独占：进根建、离根收）----

        public AnimationHandle LocoHandle;

        // ---- Override 通道（战斗根叶共用："同 ID 在播即续播、异 ID 提交替换"）----

        public AnimationId BodyForm;
        public AnimationHandle BodyHandle;
        public bool BodyIsBlend;

        // ---- Overlay 通道（换弹：上半身叠加，腿继续走 Base MoveBlend）----

        /// <summary>换弹叠加层句柄（default = 未提交）；<see cref="ReloadSubmitted"/> 才是权威在播标志
        /// （句柄在自然完成后仍留作持帧占位，单看句柄判不出"还在不在换弹"）。</summary>
        public AnimationHandle ReloadHandle;

        private bool _reloadSubmitted;

        /// <summary>换弹叠加层已提交且未释放（诊断/测试读值）。</summary>
        public bool ReloadSubmitted => _reloadSubmitted;

        // ---- 态引用（驱动器窗刷新——装配期注入）----

        internal CombatRootStage Root;

        // ---- Override 形态提交（战斗根叶共用）----

        /// <summary>提交单片段形态（Override）：同 ID **仍在播** → 续播保相位（不重提交——重提交会按
        /// 第 0 帧并刷终态）；不在播/异 ID → 提交（旧形态由通道仲裁替换）。拒绝返回 false（调用方自愈重试）。</summary>
        public bool PlayBodySingle(AnimationId id, float speed = 1f)
        {
            if (BodyForm.Equals(id) && BodyHandle.IsValid
                && (!Player.TryGetState(BodyHandle, out var st) || st.IsPlaying))
                return true;
            var r = Player.Play(new AnimationRequest(id, AnimationChannel.Override, speed: speed));
            if (!r.Accepted) return false;
            BodyForm = id;
            BodyHandle = r.Handle;
            BodyIsBlend = false;
            return true;
        }

        /// <summary>提交混合形态（Override）：同 ID 在播 → **就地调权重续播**（跨态同形态——
        /// AimWalk↔FireWalk、窗尽降级，不换句柄零淡化）；否则提交。拒绝返回 false。</summary>
        public bool PlayBodyBlend(AnimationId id, float[] weights)
        {
            if (BodyForm.Equals(id) && UpdateBodyBlendWeights(weights)) return true;
            var r = Player.PlayBlend(new AnimationBlendRequest(id, AnimationChannel.Override, weights));
            if (!r.Accepted) return false;
            BodyForm = id;
            BodyHandle = r.Handle;
            BodyIsBlend = true;
            Array.Copy(weights, BodyWeightsCopy, weights.Length);
            return true;
        }

        /// <summary>Override 混合权重就地更新（句柄失效/非混合 → false）。</summary>
        public bool UpdateBodyBlendWeights(float[] weights)
        {
            if (!BodyIsBlend || !BodyHandle.IsValid) return false;
            if (!Player.UpdateBlendWeights(BodyHandle, weights)) return false;
            Array.Copy(weights, BodyWeightsCopy, weights.Length);
            return true;
        }

        // ---- 换弹：Overlay 通道一次性上半身叠加（**不进战斗根**）----

        /// <summary>
        /// 换弹事实 → 上半身叠加层的一次性提交（Overlay 通道），由驱动器每帧喂事实调用。
        ///
        /// **为什么换弹不进战斗根**：战斗根接管会把移动根的 Base MoveBlend 停掉——脚定住而世界位移照旧，
        /// 即滑步。走叠加层则 AvatarMask 只盖上半身（Root 位与双腿关闭，见 <c>OverlayMaskFactory</c>），
        /// 腿继续由 Base 通道的 MoveBlend 走。换弹因此**不参与根裁决**，也不打断开火/瞄准的 Override 形态
        /// （叠加层权重 1 天然压住上半身）。
        ///
        /// 同一事实期只提交一次（重发会按第 0 帧重播）；提交被拒（无 humanoid Mask 等能力位缺失）→
        /// 不记已提交，下帧自愈重试。
        /// </summary>
        public void UpdateReloadUpper(bool isReloading, bool isDead)
        {
            if (isDead || !isReloading)
            {
                ReleaseReloadUpper();
                return;
            }
            if (_reloadSubmitted) return;

            var r = Player.Play(new AnimationRequest(
                CharacterAnimationIds.Reload, AnimationChannel.Overlay, speed: ReloadPlaybackSpeed));
            if (!r.Accepted) return;                    // 被拒：保持"未提交"，下一渲染帧重试
            ReloadHandle = r.Handle;
            _reloadSubmitted = true;
        }

        /// <summary>释放换弹叠加层（事实清除/死亡）：权重淡出、露出下方姿态。幂等——
        /// 未提交时 no-op；已自然完成（持帧占位）时经播放器释放通道占位，等效权重淡出。</summary>
        public void ReleaseReloadUpper()
        {
            if (!_reloadSubmitted) return;
            _reloadSubmitted = false;
            if (ReloadHandle.IsValid) Player.Stop(ReloadHandle, AnimationStopReason.Cancelled);
            ReloadHandle = default;
        }
    }
}
