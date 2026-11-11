using System;
using LiteSim;
using UnityEngine;
using LiteFramework;
using LiteGame.Feedback;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Abstractions;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Feedback;

namespace LiteGame.Modules.Battle.SubModules.CombatSubModule.Fx
{
    /// <summary>
    /// 命中特效驱动（帧事件路由 HitFeedbackDispatcher 的消费者）： **本地造成的 Hit/Crit → 受击点世界位播放 `fx_impact_enemy`**（PlayAt——打哪留哪， 不跟随实体）。
    /// </summary>
    public sealed class BattleImpactFxDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>命中特效资源名（"命名即引用"→ `Assets/LiteGame/Modules/Battle/Content/FX/fx_impact_enemy.prefab`）。</summary>
        public const string EffectName = "fx_impact_enemy";

        /// <summary>着墙特效资源名（"命名即引用"→ `Assets/LiteGame/Modules/Battle/Content/FX/fx_impact_wall.prefab`）。</summary>
        public const string WallEffectName = "fx_impact_wall";

        /// <summary>爆头档放大（观感区分；普通档 = 1）。</summary>
        private const float CritScale = 1.35f;

        private readonly IVfxService _vfx;
        private bool _disposed;

        public BattleImpactFxDriver(IVfxService vfx)
        {
            _vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
        }

        /// <summary>已播出弹着的次数（诊断面）。</summary>
        public int ImpactCount { get; private set; }

        /// <summary>已播出着墙弹着的次数（诊断面）。</summary>
        public int WallImpactCount { get; private set; }

        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed) return;

            if (ctx.Kind == FrameEventKind.ObstacleHit)                     // 着墙：本地打墙 → 障碍命中点
            {
                if (ctx.LocalRole != HitLocalRole.Caused) return;
                _vfx.PlayAt(WallEffectName, ctx.WorldPos, 1f, owner: this);        // 归属本件：离场一次收回
                WallImpactCount++;
                return;
            }

            if (ctx.Kind != FrameEventKind.Hit && ctx.Kind != FrameEventKind.Crit) return;
            if (ctx.LocalRole != HitLocalRole.Caused) return;               // 只播"我打中敌人"

            _vfx.PlayAt(EffectName, ctx.WorldPos, ctx.Kind == FrameEventKind.Crit ? CritScale : 1f,
                owner: this);
            ImpactCount++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _vfx.StopAll(this);                     // 本件播过的弹着整体收回（不等自然到期——离场计数须归零）
        }
    }
}
