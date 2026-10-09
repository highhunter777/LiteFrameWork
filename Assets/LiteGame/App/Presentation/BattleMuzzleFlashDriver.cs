using System;
using LiteSim;
using LiteView;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 枪口火光驱动（《动作与特效专项设计》表现件；帧事件路由 <see cref="HitFeedbackDispatcher"/>
    /// 的消费者）：**本地开火 Fire 帧事件 → 武器挂点（`Weapon_Rifle/Muzzle`）跟随播放
    /// `fx_muzzle_flash`**（<c>follow=true</c> 挂点跟随——开火动画的枪姿态自动带走火光的位姿）。
    ///
    /// **只吃 Fire**：Hit/Crit 归命中特效（<see cref="BattleImpactFxDriver"/>）、死亡/技能归各自批。
    /// **静默门**由上游 <c>SimView.EventSink</c> 保证（回滚重放/和解段事件被丢弃——火光不重复喷）；
    /// 回滚重放期间的武器节拍由 Sim 判定，Fire 事件频率 = 真实开火频率（含武器门拦截段不产事件）。
    ///
    /// **远端开火火光待公共开火信号**：Fire 帧事件只来自本地预测世界（远端实体本地无输入、不开火）；
    /// 公开面现有的开火驻留窗（`FireStanceFrames`）**瞄准也会置窗**、无法区分开火——远端火光归
    /// CombatEvent 批（《状态同步专项设计》：可靠战斗事件留 P1）。**灰盒无挂载点 → 静默退化**（不造
    /// 第二视觉源）。**表现参数（资源名/缩放系数）为本地常量，禁入 `CombatConfig`**（不搅 digest）。
    /// </summary>
    public sealed class BattleMuzzleFlashDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>火光资源名（VfxCatalog"命名即引用"→ `Assets/FX/fx_muzzle_flash.prefab`）。</summary>
        public const string EffectName = "fx_muzzle_flash";

        /// <summary>观感缩放（KriptoFX 件按整枪尺度作者化——单点常量，美术可调；换件归构建器）。</summary>
        private const float EffectScale = 1f;

        private readonly SimView _view;
        private readonly IVFXService _vfx;

        private GameObject _mountView;      // 挂载点缓存键：所属视图实例（视图回收/重建 → 重解析）
        private Transform _mount;
        private bool _disposed;

        public BattleMuzzleFlashDriver(SimView view, IVFXService vfx)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
        }

        /// <summary>已播出火光的开火次数（诊断面——"开了几枪就该闪几下"）。</summary>
        public int FlashCount { get; private set; }

        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed) return;
            if (ctx.Kind != FrameEventKind.Fire) return;
            if (ctx.Slot < 0 || !_view.TryGetView(ctx.Slot, out var view) || view == null) return;

            Transform mount = ResolveMount(view);
            if (mount == null) return;                                  // 灰盒无武器挂载点 → 诚实退化

            _vfx.Play(EffectName, mount, follow: true, EffectScale);
            FlashCount++;
        }

        /// <summary>武器激光挂载点（按视图实例缓存——视图回收/重建后重解析；与激光同源解析件）。</summary>
        private Transform ResolveMount(GameObject view)
        {
            if (_mount != null && _mountView == view) return _mount;

            _mountView = view;
            _mount = WeaponMounts.FindMuzzle(view.transform);
            return _mount;
        }

        public void Dispose()
        {
            _disposed = true;
            _mount = null;
            _mountView = null;
        }
    }
}
