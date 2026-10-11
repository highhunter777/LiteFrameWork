using System;
using LiteFramework.Animation;
using LiteSim;

namespace LiteView.Animation
{
    /// <summary>
    /// 驱动器每帧喂入的跨层事实（不可变值——**唯一写入入口** <see cref="SlotAnimContext.Feed"/>）。
    /// 事实整体替换（非逐项赋值），故不存在"喂一半"的中间态；读取方为各 Stage
    /// （层间不引用：跨层事实全部经此单点传递）。
    /// </summary>
    public readonly struct SlotAnimFacts
    {
        /// <summary>瞄准中（SimView.IsAiming——Sim 权威；战斗根活跃判据之一）。</summary>
        public readonly bool IsAiming;

        /// <summary>换弹中（SimView.IsReloading——Sim 权威，武器私有面投影）：真时由
        /// <see cref="SlotAnimContext.UpdateReloadUpper"/> 提交 Overlay 上半身叠加（**不进战斗根**）。</summary>
        public readonly bool IsReloading;

        /// <summary>死亡事实（SimView.IsDead——Hp≤0 快照可重建；战斗根死亡裁决判据。
        /// **生命更替（复活）即复位**：驱动器按 LifeStartFrame 变化清锁存——单条生命内不可逆）。</summary>
        public readonly bool IsDead;

        /// <summary>移动事实锁存（迟滞公式单源 <see cref="LocomotionBlendMath.UpdateLatch"/>）。</summary>
        public readonly bool IsMoving;

        /// <summary>视图帧间位移速度（m/s）。</summary>
        public readonly float Speed;

        /// <summary>移动方向 vs 朝向的夹角（度；正 = 朝向右侧——AimWalk 四向权重）。</summary>
        public readonly float MoveRelAngleDeg;

        public SlotAnimFacts(bool isAiming = false, bool isReloading = false, bool isDead = false,
            bool isMoving = false, float speed = 0f, float moveRelAngleDeg = 0f)
        {
            IsAiming = isAiming;
            IsReloading = isReloading;
            IsDead = isDead;
            IsMoving = isMoving;
            Speed = speed;
            MoveRelAngleDeg = moveRelAngleDeg;
        }
    }

    /// <summary>
    /// 槽位上下文（各态对"播放器 + 事实 + 双通道形态跟踪 + 射击窗"的窄面——按槽位新建，不持槽数组引用；
    /// 单机单上下文）。**层间不引用**：跨层事实由驱动器经 <see cref="Feed"/> 整体喂入（只读读口
    /// <see cref="Facts"/>）；**不反向引用宿主**（窗状态归本类，根阶段只调本类的窗三态入口）。
    ///
    /// **属主**（本类不设 public 可变字段）：
    /// - 播放器/权重容器/窗长/换弹倍率 = 构造期注入（驱动器持有，本类只持引用）；
    /// - 事实（<see cref="Facts"/>）= 驱动器每帧唯一写者；
    /// - 通道状态（<see cref="LocoHandle"/>/<see cref="BodyForm"/>/<see cref="BodyHandle"/>/
    ///   <see cref="BodyIsBlend"/>/<see cref="ReloadHandle"/>）= 状态机（各根/叶）写、外部只读；
    /// - 射击窗（<see cref="FireWindow"/>）= 裁决与推进都在战斗根，本类只提供三态入口。
    /// </summary>
    public sealed class SlotAnimContext
    {
        /// <summary>播放器（构造期注入；本类不拥有其生命周期）。</summary>
        public AnimationPlayer Player { get; }

        /// <summary>射击窗长（秒）＝ <see cref="CombatConfig.FireStanceFrames"/> / SimConfig.TickRate（1s 单源派生）。</summary>
        public float FireHoldSeconds { get; }

        /// <summary>换弹片段播放倍率——片段时长对齐 Sim 换弹时长（<c>WeaponConfig.Default.ReloadFrames</c>
        /// / TickRate），钳制在 Profile 登记的速度区间内（越界会被播放器拒绝）；未知片段时长 ⇒ 1×。</summary>
        public float ReloadPlaybackSpeed { get; }

        /// <summary>MoveBlend 槽位权重容器（序 {Idle, Walk, Run}；驱动器复用数组，零分配）。</summary>
        internal float[] MoveWeights { get; }

        /// <summary>AimMoveBlend 槽位权重容器（序 {F, R, B, L}；同上）。</summary>
        internal float[] AimWeights { get; }

        /// <summary>最近一次提交/更新的权重副本（诊断读回——<c>TryGetMotion</c> 的读值）。</summary>
        internal readonly float[] LocoWeightsCopy = new float[3];
        internal readonly float[] BodyWeightsCopy = new float[4];

        private SlotAnimFacts _facts;

        /// <summary>事实唯一写入口（驱动器每帧调；整体替换，不提供逐项写口）。</summary>
        public void Feed(in SlotAnimFacts facts) => _facts = facts;

        /// <summary>死亡锁存（不可逆）：死亡事件沿在帧中即时置位——压过一切已挂起路由（last-wins
        /// 语义下"后发者赢"必须让死亡先落位）。下帧 <see cref="Feed"/> 由驱动器 DeadSeen 锁存带来
        /// 同一事实，两路同值不冲突。</summary>
        internal void LatchDead()
            => _facts = new SlotAnimFacts(_facts.IsAiming, _facts.IsReloading, isDead: true,
                _facts.IsMoving, _facts.Speed, _facts.MoveRelAngleDeg);

        /// <summary>瞄准中（读口——见 <see cref="SlotAnimFacts.IsAiming"/>）。</summary>
        public bool IsAiming => _facts.IsAiming;

        /// <summary>换弹中（读口——见 <see cref="SlotAnimFacts.IsReloading"/>）。</summary>
        public bool IsReloading => _facts.IsReloading;

        /// <summary>死亡事实（读口——见 <see cref="SlotAnimFacts.IsDead"/>）。</summary>
        public bool IsDead => _facts.IsDead;

        /// <summary>移动事实锁存（读口——见 <see cref="SlotAnimFacts.IsMoving"/>）。</summary>
        public bool IsMoving => _facts.IsMoving;

        /// <summary>视图帧间位移速度 m/s（读口——见 <see cref="SlotAnimFacts.Speed"/>）。</summary>
        public float Speed => _facts.Speed;

        /// <summary>移动方向 vs 朝向夹角（读口——见 <see cref="SlotAnimFacts.MoveRelAngleDeg"/>）。</summary>
        public float MoveRelAngleDeg => _facts.MoveRelAngleDeg;

        // ---- 射击窗（战斗根裁决单点；本类只存状态 + 提供三态入口）----

        /// <summary>射击窗剩余（秒）——0 = 窗不在（诊断/测试读值）。</summary>
        public float FireWindow { get; private set; }

        /// <summary>事件刷新＝重置满窗（上限即窗长，不累加）。</summary>
        internal void RefreshFireWindow() => FireWindow = FireHoldSeconds;

        /// <summary>按帧递减（钳制到 0——窗不欠账）。</summary>
        internal void AdvanceFireWindow(float elapsedSeconds)
            => FireWindow = Math.Max(0f, FireWindow - elapsedSeconds);

        /// <summary>窗清零（退根/离场收口）。</summary>
        internal void ClearFireWindow() => FireWindow = 0f;

        // ---- 通道状态（状态机写、外部只读）----

        /// <summary>Base 通道句柄（移动根独占：进根建、离根收）。</summary>
        internal AnimationHandle LocoHandle { get; set; }

        /// <summary>Override 通道当前形态 id（战斗根叶共用："同 ID 在播即续播、异 ID 提交替换"）。</summary>
        public AnimationId BodyForm { get; internal set; }

        /// <summary>Override 通道当前句柄。</summary>
        public AnimationHandle BodyHandle { get; internal set; }

        /// <summary>Override 当前形态是否为混合形态（诊断/权重就地更新判据）。</summary>
        public bool BodyIsBlend { get; internal set; }

        /// <summary>换弹叠加层句柄（default = 未提交）；<see cref="ReloadSubmitted"/> 才是权威在播标志
        /// （句柄在自然完成后仍留作持帧占位，单看句柄判不出"还在不在换弹"）。</summary>
        public AnimationHandle ReloadHandle { get; private set; }

        private bool _reloadSubmitted;

        /// <summary>换弹叠加层已提交且未释放（诊断/测试读值）。</summary>
        public bool ReloadSubmitted => _reloadSubmitted;

        public SlotAnimContext(AnimationPlayer player, float[] moveWeights, float[] aimWeights,
            float fireHoldSeconds, float reloadPlaybackSpeed = 1f)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            MoveWeights = moveWeights ?? throw new ArgumentNullException(nameof(moveWeights));
            AimWeights = aimWeights ?? throw new ArgumentNullException(nameof(aimWeights));
            if (moveWeights.Length < 3) throw new ArgumentException("MoveBlend 权重容器至少 3 槽", nameof(moveWeights));
            if (aimWeights.Length < 4) throw new ArgumentException("AimMoveBlend 权重容器至少 4 槽", nameof(aimWeights));
            FireHoldSeconds = fireHoldSeconds;
            ReloadPlaybackSpeed = reloadPlaybackSpeed;
        }

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
