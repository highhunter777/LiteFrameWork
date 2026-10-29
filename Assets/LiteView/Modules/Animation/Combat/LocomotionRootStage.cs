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
    /// **瞄准建立 → 进战斗根**（覆盖开始）的入口裁决也在此（与战斗根的退根裁决对称——
    /// 各根管各根的出界）。**换弹不进此裁决**（走 Overlay 上半身叠加，Base 持续出步——停即滑步）。
    /// </summary>
    internal sealed class LocomotionRootStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        /// <summary>MoveBlend 三槽片段时长（步幅对齐输入；null = 不做换算——清单缺件/测试直构）。</summary>
        private readonly float[] _moveDurations;
        private readonly float[] _moveSpeeds = new float[3];

        internal LocomotionRootStage(SlotAnimContext ctx, float[] moveDurations = null)
        {
            _ctx = ctx;
            _moveDurations = moveDurations;
        }

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

            // 权威射击窗在场（扣扳机/成功开火——Sim 统一置窗）→ 进战斗根 Fire 系（按锁存选叶）：
            // 扣扳机被武器门拦下（空仓/换弹/冷却）的帧同进语境——朝向已跟准星（InputSystem 判据
            // 同一条），此处不进根即"面朝反向播前进走"的倒滑（MoveBlend 无方向性）。
            // 窗续期与退根由战斗根单点裁决（权威窗是保持条件而非充值源——Sim 窗尽即离场）。
            if (_ctx.IsFireStance)
            {
                m.Request(_ctx.IsMoving ? CharacterAnimId.FireWalk : CharacterAnimId.FireIdle);
                return;
            }

            // 速度轴权重就地维护（同形态连续——不换句柄、无终态）
            LocomotionBlendMath.BuildSpeedWeights(_ctx.Speed, _ctx.MoveWeights);
            if (_ctx.LocoHandle.IsValid && _ctx.Player.UpdateBlendWeights(_ctx.LocoHandle, _ctx.MoveWeights))
            {
                Array.Copy(_ctx.MoveWeights, _ctx.LocoWeightsCopy, _ctx.MoveWeights.Length);
                ApplyStrideRates();
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
            ApplyStrideRates();                               // 新句柄当帧即落对齐速率（不等下一帧）
        }

        /// <summary>步幅对齐：按当前速度/权重写逐输入净速率（相位锁定 + 脚程换算——数学见
        /// <see cref="LocomotionBlendMath.BuildMoveSpeeds"/>）。时长缺省（清单缺件）→ 不干预（保持 1× 现状）；
        /// 写入被拒（句柄换代/非混合节点）→ 下帧自愈重试。</summary>
        private void ApplyStrideRates()
        {
            if (_moveDurations == null) return;
            LocomotionBlendMath.BuildMoveSpeeds(_ctx.Speed, _ctx.MoveWeights, _moveDurations, _moveSpeeds);
            _ctx.Player.TrySetBlendInputSpeeds(_ctx.LocoHandle, _moveSpeeds);
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
                    Mathf.Clamp(ctx.Speed / CombatConfig.AimMoveSpeed,
                        LocomotionBlendMath.AimStepRateMin, LocomotionBlendMath.AimStepRateMax));
        }
    }
}
