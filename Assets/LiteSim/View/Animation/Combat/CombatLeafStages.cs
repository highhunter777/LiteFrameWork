using System;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteSim.View.Animation
{
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
}
