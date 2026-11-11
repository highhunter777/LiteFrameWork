using System;
using LiteSim;
using UnityEngine;
using LiteFramework;
using LiteGame.Modules.Battle.SubModules.ArenaSubModule.View;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Abstractions;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Feedback;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Weapon;

namespace LiteGame.Modules.Battle.SubModules.CombatSubModule.Fx
{
    /// <summary>
    /// 枪口火光驱动（《动作与特效专项设计》表现件）。
    /// </summary>
    public sealed class BattleMuzzleFlashDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>火光资源名（VfxCatalog"命名即引用"→ `Assets/LiteGame/Modules/Battle/Content/FX/fx_muzzle_flash.prefab`）。</summary>
        public const string EffectName = "fx_muzzle_flash";

        /// <summary>观感缩放（KriptoFX 件按整枪尺度作者化——单点常量，美术可调；换件归构建器）。
        /// 本地/远端火光共用同一缩放单源（<see cref="BattleRemoteMuzzleFlashDriver"/> 引用）。</summary>
        public const float EffectScale = 1f;

        private readonly SimView _view;
        private readonly IVfxService _vfx;

        private GameObject _mountView;      // 挂载点缓存键：所属视图实例（视图回收/重建 → 重解析）
        private Transform _mount;
        private bool _disposed;

        public BattleMuzzleFlashDriver(SimView view, IVfxService vfx)
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

            _vfx.Play(EffectName, mount, follow: true, EffectScale, owner: this);   // 归属本件：离场一次收回
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
            if (_disposed) return;
            _disposed = true;
            _vfx.StopAll(this);                     // 本件播过的火光整体收回（不等自然到期——离场计数须归零）
            _mount = null;
            _mountView = null;
        }
    }
}
