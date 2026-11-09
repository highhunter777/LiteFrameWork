using System;
using LiteFramework;
using LiteSim;
using LiteFramework.UI;
using TMPro;
using UnityEngine;
using LiteFramework;
using LiteGame;

namespace LiteGame
{
    /// <summary>
    /// 对局 HUD 面板驱动（战斗 HUD 第二件：血条/弹药/技能栏——prefab Hud_Battle， 血条走 HpBar 控件模板 Tmp_HpBar）。
    /// </summary>
    public sealed class BattleHudDriver : IDisposable, IHudDriver
    {
        /// <summary>未装备（懒装备前/测试形态）时的弹匣占位（备弹读出随它一起收起）。</summary>
        public const string UnarmedAmmoPlaceholder = "--";

        private readonly IBattleReadModel _readModel;
        private readonly CanvasGroup _panel;
        private readonly HpBar _hpBar;
        private readonly TextMeshProUGUI _ammoText;
        private readonly GameObject _reloadLabel;
        private readonly TextMeshProUGUI _reserveAmmoText;         // 可缺（缺件静默降级：不显示备弹读出）
        private readonly GameObject _slashSeparator;              // 可缺（同上；与备弹件同显隐）
        private int _lastHp = int.MinValue;
        private int _lastAmmo = int.MinValue;
        private int _lastReserve = int.MinValue;
        private bool _lastReload;
        private bool _lastVisible = true;
        private bool _disposed;

        public BattleHudDriver(IBattleReadModel readModel,
            CanvasGroup panel, HpBar hpBar, TextMeshProUGUI ammoText, GameObject reloadLabel,
            TextMeshProUGUI reserveAmmoText = null, GameObject slashSeparator = null)
        {
            _readModel = readModel ?? throw new ArgumentNullException(nameof(readModel));
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _hpBar = hpBar ?? throw new ArgumentNullException(nameof(hpBar));
            _ammoText = ammoText ?? throw new ArgumentNullException(nameof(ammoText));
            _reloadLabel = reloadLabel;                    // null = 场景缺标签件（静默降级：不显示换弹态）
            _reserveAmmoText = reserveAmmoText;            // null = 缺备弹件（静默降级：只显当前弹匣）
            _slashSeparator = slashSeparator;              // null = 缺分隔符件（同上）
        }

        void IHudDriver.Tick(float deltaSeconds) => Tick();

        /// <summary>每渲染帧（视图状态的消费者——同拍准心/激光）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            bool visible = _readModel.TryGetLocalPlayer(out BattleLocalPlayerFacts local);
            if (visible != _lastVisible)
            {
                _lastVisible = visible;
                _panel.alpha = visible ? 1f : 0f;          // 整组显隐（CanvasGroup——无 SetActive 重建）
            }
            if (!visible) return;

            if (local.Hp != _lastHp)
            {
                _lastHp = local.Hp;
                _hpBar.Set(local.Hp, CombatConfig.EntityHp);   // HpBar 控件 API（前条瞬时 + 后条延迟滑落）
            }

            if (local.WeaponSlot < 0)
            {
                if (_lastAmmo != -1)
                {
                    _lastAmmo = -1;
                    _ammoText.SetText(UnarmedAmmoPlaceholder);   // 未装备（懒装备前/测试形态）
                }
                SetReserveSuffix(false);                   // 备弹读出随 "--" 一起收起（不显示陈旧备弹）
                SetReload(false);
                return;
            }

            if (local.MagAmmo != _lastAmmo)
            {
                _lastAmmo = local.MagAmmo;
                _ammoText.SetText("{0}", local.MagAmmo);
            }
            UpdateReserve(local.ReserveAmmo);
            SetReload(local.Reloading);
        }

        /// <summary>剩余备弹读出（本人私有面 <see cref="WeaponRuntime.ReserveAmmo"/>——换弹从备弹扣、
        /// 随消耗递减）：值变化才写，装备态显示「/ 备弹」。</summary>
        private void UpdateReserve(int reserve)
        {
            if (reserve != _lastReserve)
            {
                _lastReserve = reserve;
                if (_reserveAmmoText != null) _reserveAmmoText.SetText("{0}", reserve);
            }
            SetReserveSuffix(true);
        }

        /// <summary>备弹读出（备弹件 ＋ 分隔符）整组显隐——两件同开同关（缺件静默跳过）。</summary>
        private void SetReserveSuffix(bool on)
        {
            if (_reserveAmmoText != null && _reserveAmmoText.gameObject.activeSelf != on)
                _reserveAmmoText.gameObject.SetActive(on);
            if (_slashSeparator != null && _slashSeparator.activeSelf != on)
                _slashSeparator.SetActive(on);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _panel.alpha = 0f;                             // 离场归零（不残留半透明面板）
        }

        private void SetReload(bool reloading)
        {
            if (_reloadLabel == null || reloading == _lastReload) return;
            _lastReload = reloading;
            _reloadLabel.SetActive(reloading);
        }
    }
}
