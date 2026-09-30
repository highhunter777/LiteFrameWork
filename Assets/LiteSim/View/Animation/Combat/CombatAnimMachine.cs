using System;
using LiteFramework;
using LiteFramework.Animation;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 战斗层状态 id（《层次动画机设计》§1，2026-09-29 批B-①）。
    /// 批B-① 只落 Idle/Firing——Reloading/Hit/Death 随消费者（G2）接入再启用，
    /// 不预建无消费者的状态（§8 纪律同款）。
    /// </summary>
    public enum CombatAnimId
    {
        /// <summary>待机（无战斗动作在途——移动层独占基础形态）。</summary>
        Idle = 0,

        /// <summary>开火中（Fire 驻留窗内；窗内持有 UpperBody 后坐叠加）。</summary>
        Firing = 1,
    }

    /// <summary>
    /// 战斗层迁移请求载荷（TReq——层级机 Request 的 payload）。
    /// 无载荷迁移用无参 <c>Request(id)</c> 重载。
    /// </summary>
    public struct CombatAnimReq
    {
        /// <summary>本次迁移是否由 Fire 帧事件触发（诊断/对拍观测；行为判据在状态内自持）。</summary>
        public bool IsFireEvent;
    }

    /// <summary>
    /// 战斗层状态机装配（《层次动画机设计》§2——旧驱动器布尔规则的正名，不是重设计）：
    /// - Idle→Firing：Fire 事件（驱动器转发 Request / 态内 OnFireEvent 合并）；
    /// - Firing 驻留（事件刷新制判窗；**窗长 = 开火态时间 = 开火动画时间**——开火片段时长 ÷ 播放倍率，
    ///   由驱动器在槽位装配期从**片段资产**解析注入 <see cref="SlotAnimContext.FireHoldSeconds"/>
    ///   （2026-09-30 四次裁决：不再写 2.0s 常量 / 不再与 Sim 同源）；持续射击不断续窗，
    ///   静默满窗才回 Idle——窗尽即本轮片段播完）；
    /// - Firing 内新事件且上一轮已完 → 重起一轮（2× 速，原连发重起策略）；
    /// - 移动层事实（IsMoving）由驱动器每帧喂 ctx：**移动中不提交后坐**（原移动门控——AimWalk
    ///   持枪自洽）；**起跑只停站姿后坐叠加、不退出 Firing 态**——Firing 是"移动形态视同瞄准态"
    ///   配对的载体，退出会让移动腰射失去持枪臂姿（2026-09-30 步频同步批恢复；Sim 侧已无窗，
    ///   步频滑步由驱动器的倍率缩放消解）；
    /// - 窗尽回 Idle（叠加随离场淡出；窗尽帧**先判窗再自检**，不残留一帧 Firing——见 OnUpdate）。
    /// </summary>
    public static class CombatAnimMachine
    {
        /// <summary>装配一台战斗层状态机（批B-① 无复合态=平面退化形态；批B-② 移动层入树时长出第二根）。</summary>
        public static HierarchicalStageMachine<CombatAnimId, CombatAnimReq> Build(SlotAnimContext ctx)
        {
            return new HierarchicalStageMachine<CombatAnimId, CombatAnimReq>(
                "CombatAnim",
                new (CombatAnimId, IStage<CombatAnimId, CombatAnimReq>)[]
                {
                    (CombatAnimId.Idle, new IdleStage()),
                    (CombatAnimId.Firing, new FiringStage(ctx)),
                },
                composites: null);
        }
    }

    /// <summary>
    /// 槽位上下文（战斗层各态对"该槽位播放器 + 事实"的窄面——按槽位重建，不持槽数组引用）。
    /// <see cref="IsMoving"/> 是**移动层事实**的跨层喂入（层间不直接引用——见设计 §2 事件表）。
    /// </summary>
    public sealed class SlotAnimContext
    {
        /// <summary>该槽位的动画播放器（驱动器建播放器时注入；灰盒无播放器 = 不建战斗层）。</summary>
        public CharacterAnimationPlayer Player;

        /// <summary>Fire 语义绑定与通道（Profile 单源——驱动器构造期校验后注入）。</summary>
        public AnimationId FireId;
        public AnimationChannel FireChannel;

        /// <summary>开火片段播放速度（2×——后坐节奏，驱动器常量装配注入）。</summary>
        public float FirePlaybackSpeed;

        /// <summary>开火态时间（秒）——**= 开火动画时间**：开火片段时长 ÷ <see cref="FirePlaybackSpeed"/>
        /// （驱动器槽位装配期从**片段资产**解析注入——时长不在 Profile/驱动里写死；解析失败 = 0 →
        /// 进态即退，与"没东西可播"一致，不猜兜底时长）。Firing 驻留窗以它为长（事件刷新制）。</summary>
        public float FireHoldSeconds;

        /// <summary>移动层事实（驱动器每帧更新）：true = 移动中——不提交站姿后坐、已播即离场。</summary>
        public bool IsMoving;

        /// <summary>开火提交计数（诊断/测试——原驱动器 FireSubmits 的迁移位）。</summary>
        public int FireSubmits;

        /// <summary>Firing 态句柄（驱动器转发事件用：态内合并，不产生自转事务）。</summary>
        internal FiringStage Firing;
    }

    /// <summary>待机态：无行为（移动层独占基础形态）；Fire 事件由驱动器转发为 Request(Firing)。</summary>
    internal sealed class IdleStage : IStage<CombatAnimId, CombatAnimReq>
    {
        public void OnInit(IStageHost<CombatAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CombatAnimId, CombatAnimReq> m, in CombatAnimReq req) { }
        public void OnUpdate(IStageHost<CombatAnimId, CombatAnimReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<CombatAnimId, CombatAnimReq> m) { }
    }

    /// <summary>
    /// 开火态：驻留窗（**窗长 = 开火动画时间**，ctx 注入）内持有 UpperBody 后坐叠加。进态即提交一轮
    /// （移动中只开窗不提交——AimWalk 持枪自洽，后坐留给站定）；窗内来新事件且上一轮已完 → 重起一轮（连发）；
    /// 移动开始 → **只停站姿后坐叠加、不退出本态**（Firing 是移动腰射"持枪步态"配对的载体——
    /// 腿部 AimWalk 由驱动器按实际速度缩放步频，见 CharacterLocomotionDriver 步频同步）；窗尽 → 回 Idle。
    /// </summary>
    internal sealed class FiringStage : IStage<CombatAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;
        private AnimationHandle _handle;
        private float _windowElapsed;      // 驻留剩余（秒）——**事件刷新制**（窗长 = 开火态时间 = 开火动画
                                           // 时间：持续射击时窗不断续期，只有静默满窗——即本轮片段播完——
                                           // 才出；不用 StageTime——那是"进态后绝对时长"，持续射击会周期性闪回 Idle 一帧）

        internal FiringStage(SlotAnimContext ctx)
        {
            _ctx = ctx;
            ctx.Firing = this;                 // 句柄注入：驱动器转发事件走态内合并（自转会重入停句柄）
        }

        public void OnInit(IStageHost<CombatAnimId, CombatAnimReq> m) { }

        public void OnEnter(IStageHost<CombatAnimId, CombatAnimReq> m, in CombatAnimReq req)
        {
            _windowElapsed = _ctx.FireHoldSeconds;      // 窗长 = 开火动画时间（装配期从片段资产派生注入）
            // 进态即一轮后坐（首发与静默后的事件重入）；移动中只开窗不提交（AimWalk 持枪自洽）
            if (!_ctx.IsMoving) SubmitFire();
        }

        public void OnUpdate(IStageHost<CombatAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            // 起跑终止（2026-09-30 步频同步批恢复"不退态"）：移动开始 → 只停**站姿后坐叠加**（上半身
            // 让位给 AimWalk 持枪步态），**不退出 Firing 态**——Firing 是"移动形态视同瞄准态"配对的
            // 载体（驱动器 IsCombatFiring → IsAim 合成）；腿部的步频滑步由驱动器倍率缩放消解。
            // 窗保持（事件刷新制继续），窗尽自然回 Idle。
            if (_ctx.IsMoving && _handle.IsValid)
            {
                _ctx.Player.Stop(_handle, AnimationStopReason.Cancelled);
                _handle = default;
            }

            // 驻留窗尽（事件刷新制：静默满窗才出；窗长 = 开火动画时间 ⇒ **窗尽即本轮片段播完**）
            // → 回 Idle（叠加随状态离场停止——引擎层淡出；句柄若仍在播由 OnLeave 停）
            _windowElapsed -= elapseSeconds;
            if (_windowElapsed <= 0f)
            {
                m.Request(CombatAnimId.Idle);
                return;
            }

            // 在途自检：上一轮播完清句柄（下一个事件经 OnFireEvent 重起）
            if (_handle.IsValid && _ctx.Player.TryGetState(_handle, out var st) && !st.IsPlaying)
                _handle = default;
        }

        public void OnLeave(IStageHost<CombatAnimId, CombatAnimReq> m)
        {
            if (_handle.IsValid)
            {
                _ctx.Player.Stop(_handle, AnimationStopReason.Cancelled);
                _handle = default;
            }
        }

        /// <summary>事件驱动口（驱动器转发 Fire 事件；Firing 态内的合并口）：
        /// 先续窗（窗跟"最近开火活动"，移动中事件同样续——原驻留窗语义）；
        /// 在播则吞（同段连发不重提——重提会按在第 0 帧并刷终态），已完或未提交则重起一轮；
        /// 移动中不叠站姿后坐（窗已开，等站定的下一个事件）。</summary>
        internal void OnFireEvent()
        {
            _windowElapsed = _ctx.FireHoldSeconds;                  // 事件即活动：窗续期（先于一切门控；窗长=开火动画时间）
            if (_handle.IsValid && _ctx.Player.TryGetState(_handle, out var st) && st.IsPlaying) return;
            if (_ctx.IsMoving) return;
            SubmitFire();
        }

        private void SubmitFire()
        {
            var result = _ctx.Player.Play(new AnimationRequest(_ctx.FireId, _ctx.FireChannel,
                speed: _ctx.FirePlaybackSpeed));
            if (result.Accepted)
            {
                _ctx.FireSubmits++;
                _handle = result.Handle;
            }
            else
            {
                _handle = default;      // 拒绝（配置/能力面）不推进：下一个事件重试
            }
        }
    }
}
