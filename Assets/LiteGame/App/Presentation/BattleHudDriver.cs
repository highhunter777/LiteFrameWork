using System;
using LiteSim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 对局 HUD 面板驱动（战斗 HUD 第二件：血条/弹药/技能栏——场景 <c>/Battle HUD/Panel</c>，
    /// 构建器 AgentScripts/BuildBattleHud.cs 确定性生成）：纯 View 层——每渲染帧把本地**预测态**
    /// （HP/已选武器/弹匣/换弹态）写到面板控件；**禁止回写 Sim**（HUD 归 View，数据源单向流同准心）。
    ///
    /// **数据源**（《UI框架总设计》§8.1 高频 HUD 口径——C# 直读，不跨 Lua）：
    /// - <see cref="EntitySlot.Hp"/> / <see cref="CombatConfig.EntityHp"/>（满值——表装载值）；
    /// - <see cref="EntitySlot.SelectedWeapon"/> + <see cref="WeaponRuntime"/>（弹匣/换弹态——本人私有面，
    ///   本地预测态直读）；未装备显示 <c>--</c>。
    /// - 技能栏为**静态占位**（技能系统未做——构建器生成暗态框，无数据面）。
    ///
    /// **写入纪律**（制作规范 §7 同值跳过 + §4 转场基线）：文本/填充只在**值变化**时写；
    /// 本地未对齐（StartGame 未达/首快照未对齐）整组 CanvasGroup 归零隐藏（比 SetActive 省重建）；
    /// <see cref="Dispose"/> 归零面板（离场不残留）。
    ///
    /// 测试注：预测态经构造注入（<see cref="SimWorldState"/> + 本地实体 Id 取数函数）——
    /// EditMode 确定性驱动，不依赖运行时流程。
    /// </summary>
    public sealed class BattleHudDriver : IDisposable
    {
        private readonly SimWorldState _state;
        private readonly Func<long> _localEntityId;
        private readonly CanvasGroup _panel;
        private readonly Image _hpFill;
        private readonly TextMeshProUGUI _hpText;
        private readonly TextMeshProUGUI _ammoText;
        private readonly GameObject _reloadLabel;
        private int _lastHp = int.MinValue;
        private int _lastAmmo = int.MinValue;
        private bool _lastReload;
        private bool _lastVisible = true;
        private bool _disposed;

        public BattleHudDriver(SimWorldState state, Func<long> localEntityId,
            CanvasGroup panel, Image hpFill, TextMeshProUGUI hpText,
            TextMeshProUGUI ammoText, GameObject reloadLabel)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _localEntityId = localEntityId ?? throw new ArgumentNullException(nameof(localEntityId));
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _hpFill = hpFill ?? throw new ArgumentNullException(nameof(hpFill));
            _hpText = hpText ?? throw new ArgumentNullException(nameof(hpText));
            _ammoText = ammoText ?? throw new ArgumentNullException(nameof(ammoText));
            _reloadLabel = reloadLabel;                    // null = 场景缺标签件（静默降级：不显示换弹态）
        }

        /// <summary>每渲染帧（视图状态的消费者——同拍准心/激光）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            long id = _localEntityId();
            int slot = -1;
            bool visible = id != 0 && _state.TryResolve(id, out slot);
            if (visible != _lastVisible)
            {
                _lastVisible = visible;
                _panel.alpha = visible ? 1f : 0f;          // 整组显隐（CanvasGroup——无 SetActive 重建）
            }
            if (!visible) return;

            ref EntitySlot e = ref _state.Entities[slot];

            if (e.Hp != _lastHp)
            {
                _lastHp = e.Hp;
                _hpText.SetText("{0}", e.Hp);
                float max = CombatConfig.EntityHp;
                _hpFill.fillAmount = max > 0 ? e.Hp / max : 0f;
            }

            int sel = e.SelectedWeapon;
            if (sel < 0 || sel >= SimConfig.WeaponSlotsPerEntity)
            {
                if (_lastAmmo != -1)
                {
                    _lastAmmo = -1;
                    _ammoText.SetText("--");               // 未装备（懒装备前/测试形态）
                }
                SetReload(false);
                return;
            }

            ref WeaponRuntime w = ref _state.Weapons[slot * SimConfig.WeaponSlotsPerEntity + sel];
            if (w.MagAmmo != _lastAmmo)
            {
                _lastAmmo = w.MagAmmo;
                _ammoText.SetText("{0}", w.MagAmmo);
            }
            SetReload(w.State == WeaponSlotState.Reloading);
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
