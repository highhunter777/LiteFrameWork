using System;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 角色动画层次机的阶段 id（**TId 枚举一统**：
    /// 双复合根 + 全部叶）。消费者到位时在此扩 Hit/Evade（同战斗根）。
    /// </summary>
    public enum CharacterAnimId
    {
        /// <summary>战斗根（复合态①——上层）：射击窗持有者 + 退根/降级**单点裁决**者。</summary>
        CombatRoot = 0,

        /// <summary>移动根（复合态②——下层）：默认移动，MoveBlend 独占 Locomotion 通道。</summary>
        LocomotionRoot = 1,

        /// <summary>开火·静止：持 AimIdle 循环（开火不播专用片段——反馈归枪口特效）。</summary>
        FireIdle = 2,

        /// <summary>开火·移动：AimMoveBlend 四向循环（同持枪站姿族）。</summary>
        FireWalk = 3,

        /// <summary>瞄准·静止。</summary>
        AimIdle = 4,

        /// <summary>瞄准·移动：4 向 strafe + 步频倍率。</summary>
        AimWalk = 5,

        /// <summary>移动根·速度≈0（同形态 MoveBlend，态界只服务诊断/事件路由）。</summary>
        Idle = 6,

        /// <summary>移动根·速度轴 1D 混合 {Idle,Walk,Run}。</summary>
        Moving = 7,

        /// <summary>死亡叶（终态）：进态即播 Death（一次性·非循环）——完成后不重发/不退根/不降权
        /// （playable 停在末帧 = 帧锁定；根裁决按 IsDead 恒驻本叶）。</summary>
        Dead = 8,

        /// <summary>换弹叶：Sim 换弹事实（<c>WeaponRuntime.State == Reloading</c>）驱动——
        /// 进态播一次性 Reload（倍率对齐 Sim 时长）、播完持末帧；事实清除由根裁决换叶。</summary>
        Reloading = 9,
    }

    /// <summary>
    /// 迁移请求载荷（**当前无载荷**：开火态与瞄准态同持 AimIdle 家族，路由不再区分进态成因；
    /// 保留结构体作为层次机的 TReq 形参占位——需要"进态成因"的叶出现时在此扩字段）。
    /// </summary>
    public struct CombatAnimReq
    {
    }

    /// <summary>
    /// 单机双根角色动画层次机装配（《层次动画机设计》§1/§2）：
    /// - **互斥覆盖**：战斗根（上层）任一态激活 = 覆盖移动根（框架多根 = 互斥平级根，跨根 = 全退全进）；
    /// - **战斗层不可被移动层打断**：退根只有两条路——Fire*：窗尽 ∧ !IsAiming；Aim*：!IsAiming（单点裁决）；
    ///   移动事实只驱动层内 idle↔walk 轴（两族）与退根叶选择；换弹/死亡事实由根裁决**打断**（优先级路由）；
    /// - **射击窗**：窗长 = <see cref="CombatConfig.FireStanceFrames"/>/TickRate（1s 独立常量），
    ///   事件刷新＝重置满窗；**窗内保持 clip**——Fire 族持 AimIdle 家族循环（开火不播专用片段，
    ///   见 <see cref="CombatGirlsAnimationProfile"/>）；窗尽 = 保持 clip 的终点（同形态次态续播保相位、
    ///   异形态提交替换）；
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
                    (CharacterAnimId.Dead, new DeadStage(ctx)),
                    (CharacterAnimId.Reloading, new ReloadStage(ctx)),
                },
                composites: new CompositeSpec<CharacterAnimId>[]
                {
                    // 初始子态仅形式性存在（战斗根进入恒由事件/ADS/换弹显式 Request 叶）；无历史——
                    // 窗尽降级与 ADS 进入都按事实选叶，不复活旧子页（不引入历史语义）
                    new CompositeSpec<CharacterAnimId>(CharacterAnimId.CombatRoot, CharacterAnimId.AimIdle,
                        HistoryMode.None,
                        CharacterAnimId.FireIdle, CharacterAnimId.FireWalk,
                        CharacterAnimId.AimIdle, CharacterAnimId.AimWalk, CharacterAnimId.Dead,
                        CharacterAnimId.Reloading),
                    new CompositeSpec<CharacterAnimId>(CharacterAnimId.LocomotionRoot, CharacterAnimId.Idle,
                        HistoryMode.None,
                        CharacterAnimId.Idle, CharacterAnimId.Moving),
                });
        }
    }
}
