using System;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 移动根复合态（下层）：MoveBlend 独占 Base 通道——进根提交（按当前速度权重）、
    /// 每帧就地调权重（同形态连续，不换句柄）、离根收口（覆盖开始：先停 Base 再让战斗根开 Override）；
    /// **瞄准/换弹建立 → 进战斗根**（覆盖开始）的入口裁决也在此（与战斗根的退根裁决对称——
    /// 各根管各根的出界）。
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

            // 换弹事实 → 进战斗根 Reload 叶（覆盖开始——优先于瞄准：Sim 侧换弹期不可开火，
            // 表现同优先级 死亡>换弹>开火/瞄准；战斗根内的保持/出场由根裁决）
            if (_ctx.IsReloading)
            {
                m.Request(CharacterAnimId.Reloading);
                return;
            }

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
            // 覆盖开始：收 Base 通道——事务序（先深→浅退出、再浅→深进入）保证先停后播
            if (_ctx.LocoHandle.IsValid)
                _ctx.Player.Stop(_ctx.LocoHandle, AnimationStopReason.Cancelled);
            _ctx.LocoHandle = default;
        }

        private void SubmitMoveBlend()
        {
            LocomotionBlendMath.BuildSpeedWeights(_ctx.Speed, _ctx.MoveWeights);
            var r = _ctx.Player.PlayBlend(new AnimationBlendRequest(
                CharacterAnimationIds.MoveBlend, AnimationChannel.Base, _ctx.MoveWeights));
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
}
