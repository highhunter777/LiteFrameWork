using System;
using LiteClient;
using LiteSim;
using LiteView;
using UnityEngine;
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>
    /// 远端开火火光驱动（帧事件之外的**公共事件面**消费者）：把 <see cref="PublicCombatFeed"/> 的开火队列
    /// 逐条落到开火者的武器挂点——播放 `fx_muzzle_flash`（与本地火光同件同参数，缩放取
    /// <see cref="BattleMuzzleFlashDriver.EffectScale"/> 单源）。
    ///
    /// - **数据源**：独立事件批（`PacketType.CombatEvents`）携带的房间级事件窗口——权威侧记录、
    ///   30Hz 全窗口重发自带丢包收敛；本地开火**不消费本队列**（本地走帧事件即时播，避免双闪）；
    /// - **落点**：开火者视图的 `Weapon_Rifle/Muzzle` 挂点（<see cref="WeaponMounts"/> 单源——
    ///   与本地火光/激光同解析件）；视图缺位/灰盒无挂点/实体已回收 → 静默退化；
    /// - **静默降级**：feed/vfx 缺失形态空转（装配缺口在各自接线处已警告）。
    /// </summary>
    public sealed class BattleRemoteMuzzleFlashDriver : IDisposable
    {
        private readonly SimView _view;
        private readonly PublicCombatFeed _feed;
        private readonly IVFXService _vfx;
        private readonly Transform[] _mountCache;        // 按槽的挂点缓存（视图回收/重建 → 重解析）
        private readonly GameObject[] _mountOwner;       // 挂点缓存键：所属视图实例
        private bool _disposed;

        public BattleRemoteMuzzleFlashDriver(SimView view, PublicCombatFeed feed, IVFXService vfx)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _feed = feed;                                 // null = 空转（静默降级）
            _vfx = vfx;                                   // null = 空转（静默降级）
            _mountCache = new Transform[SimConfig.MaxEntities];
            _mountOwner = new GameObject[SimConfig.MaxEntities];
        }

        /// <summary>已播出远端火光的次数（诊断面——"别人开了几枪就该闪几下"）。</summary>
        public int RemoteFlashCount { get; private set; }

        /// <summary>每渲染帧（视图状态的消费者——同面板/击杀提示/网络质量同拍）。</summary>
        public void Tick()
        {
            if (_disposed || _feed == null || _vfx == null) return;

            while (_feed.FireCount > 0)
            {
                if (!_feed.TryDequeueFire(out PublicCombatFeed.FireEntry e)) break;
                if (e.Shooter == 0L || e.Shooter == _view.LocalEntityId) continue;   // 本地开火即时播——不双闪
                if (!_view.TryGetSlot(e.Shooter, out int slot)) continue;            // 实体已离场/未对齐
                Transform mount = ResolveMount(slot);
                if (mount == null) continue;                                         // 无视图/灰盒无挂点 → 诚实退化

                _vfx.Play(BattleMuzzleFlashDriver.EffectName, mount, follow: true, BattleMuzzleFlashDriver.EffectScale);
                RemoteFlashCount++;
            }
        }

        /// <summary>远端开火者的武器火光挂点（按槽缓存——视图回收/重建后重解析）。</summary>
        private Transform ResolveMount(int slot)
        {
            if (!_view.TryGetView(slot, out GameObject view) || view == null) return null;
            if (_mountCache[slot] != null && _mountOwner[slot] == view) return _mountCache[slot];

            _mountOwner[slot] = view;
            _mountCache[slot] = WeaponMounts.FindMuzzle(view.transform);
            return _mountCache[slot];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _mountCache.Length; i++)
            {
                _mountCache[i] = null;
                _mountOwner[i] = null;
            }
        }
    }
}
