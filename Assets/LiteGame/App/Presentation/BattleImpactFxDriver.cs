using System;
using LiteSim;
using LiteView;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 敌人命中特效驱动（帧事件路由 <see cref="HitFeedbackDispatcher"/> 的消费者）：
    /// **本地造成的 Hit/Crit → 受击点世界位播放 `fx_impact_enemy`**（<c>PlayAt</c>——打哪留哪，
    /// 不跟随实体；爆头 Crit 放大 <see cref="CritScale"/>）。
    ///
    /// **过滤**：只吃本地方向的命中（`Caused`）——"敌人命中特效"= 我打中敌人；承受反馈
    /// （`Received`）与旁观（`Bystander`）不播（不做全屏噪音）。**位置 = 事件命中点**（弹道命中部位，
    /// 与伤害飘字同源——`ShootingSystem` 写入的 hitPos/judgeY）。**静默门**由上游
    /// <c>SimView.EventSink</c> 保证（回滚重放不重复弹着）。**朝向不贴合表面**（事件无法线——
    /// 表面分流/贴花随 ObstacleHit 批）。**表现参数为本地常量，禁入 `CombatConfig`**。
    /// </summary>
    public sealed class BattleImpactFxDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>命中特效资源名（"命名即引用"→ `Assets/FX/fx_impact_enemy.prefab`）。</summary>
        public const string EffectName = "fx_impact_enemy";

        /// <summary>爆头档放大（观感区分；普通档 = 1）。</summary>
        private const float CritScale = 1.35f;

        private readonly IVFXService _vfx;
        private bool _disposed;

        public BattleImpactFxDriver(IVFXService vfx)
        {
            _vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
        }

        /// <summary>已播出弹着的次数（诊断面）。</summary>
        public int ImpactCount { get; private set; }

        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed) return;
            if (ctx.Kind != FrameEventKind.Hit && ctx.Kind != FrameEventKind.Crit) return;
            if (ctx.LocalRole != HitLocalRole.Caused) return;               // 只播"我打中敌人"

            _vfx.PlayAt(EffectName, ctx.WorldPos, ctx.Kind == FrameEventKind.Crit ? CritScale : 1f);
            ImpactCount++;
        }

        public void Dispose() => _disposed = true;
    }
}
