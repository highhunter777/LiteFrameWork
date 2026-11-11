using System;
using LiteFramework;
using LiteFramework.Services.UnityObjects;
using LiteFramework.UI;
using LiteSim;
using UnityEngine;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Input;
using LiteGame.Modules.Battle.SubModules.ArenaSubModule.View;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Feedback;
using LiteGame.Modules.Battle.SubModules.HudSubModule.Abstractions;
using LiteGame.Modules.Battle.SubModules.HudSubModule.Crosshair;
using LiteGame.Modules.Battle.SubModules.HudSubModule.Drivers;

namespace LiteGame.Modules.Battle.SubModules.HudSubModule.Model
{
    /// <summary>
    /// 对局 **HUD 族装配器**：准心 / 血条弹药面板 / 局内排行榜 / 全局击杀提示 / 网络质量 / 死亡复活面板， 以及 HUD 根件销毁与菜单钮监听挂摘。
    /// </summary>
    internal sealed class BattleHudAssembler : HudAssemblerBase<IBattleHudView>
    {
        private readonly CrosshairFeelProfile _crosshairFeel;   // 准心手感投影（tbcrosshair——可空 = 测试装配回落 Default）
        private readonly BattleStrategyProfile _strategy;      // 策略表投影（排行榜全序）

        private BattleCrosshairDriver _crosshair;
        private BattleHudDriver _hud;
        private BattleRankboardDriver _rankboard;
        private BattleKillFeedDriver _killFeed;
        private BattleNetworkHudDriver _netStats;
        private BattleRespawnHudDriver _respawnHud;
        private BattleHudModel _model;
        private GameObject _hudInstance;
        private UnityEngine.Events.UnityAction _menuClick;

        public BattleHudAssembler(CrosshairFeelProfile crosshairFeel,
            BattleStrategyProfile? strategy = null)
        {
            _crosshairFeel = crosshairFeel;
            _strategy = strategy ?? BattleStrategyProfile.Default;   // 缺省 = 历史全序（缺表回落）
        }

        public BattleCrosshairDriver Crosshair => _crosshair;

        /// <summary>HUD 根件（ScreenSpace Canvas 实例）。**仅测试/诊断用**：冒烟用例判"根件在场且在层"。</summary>
        internal GameObject Root => _hudInstance;

        /// <summary>建 HUD 族并接线（须在视图建立之后；HUD 件自身缺失时逐项静默降级）。</summary>
        public void Attach(SimView view, IInputService input,
            IBattleHudView hudView, GameObject hudInstance, UnityEngine.Events.UnityAction menuClick,
            HitFeedbackDispatcher dispatcher, BattleHudModel model)
        {
            Bind(hudView);
            _hudInstance = hudInstance;
            _menuClick = menuClick;
            _model = model ?? throw new ArgumentNullException(nameof(model));

            AttachCrosshair(view, input);
            AttachHud();
            AttachRankboard();
            AttachKillFeed();
            AttachNetworkHud();
            dispatcher?.Register(_crosshair);                 // 准心：本地 Fire → 连发张开/后坐（null 拒收 = 缺件降级）
        }

        public void DisposeRankboard()
        {
            _rankboard?.Dispose();                    // 格归位——同属视图消费者
            if (_rankboard != null) Remove(_rankboard);
            _rankboard = null;
        }

        public void DisposeKillFeed()
        {
            _killFeed?.Dispose();                     // 行归位——同属视图消费者
            if (_killFeed != null) Remove(_killFeed);
            _killFeed = null;
        }

        public void DisposeNetworkHud()
        {
            _netStats?.Dispose();                     // 归位——同属视图消费者
            if (_netStats != null) Remove(_netStats);
            _netStats = null;
        }

        public void DisposePanel()
        {
            _hud?.Dispose();                          // 面板归零——视图消费者，先于准心拆
            if (_hud != null) Remove(_hud);
            _hud = null;
        }

        public void DisposeRespawnHud()
        {
            _respawnHud?.Dispose();                   // 帧事件消费者——先于分发器摘
            if (_respawnHud != null) Remove(_respawnHud);
            _respawnHud = null;
        }

        public void DisposeCrosshair()
        {
            _crosshair?.Dispose();                    // 还系统光标——同属视图消费者
            if (_crosshair != null) Remove(_crosshair);
            _crosshair = null;
        }

        /// <summary>HUD 根件销毁 + 菜单钮监听摘除（在 HUD 驱动之后、视图根之前调用）。</summary>
        public void DisposeRoot()
        {
            if (_hudInstance != null)                 // ScreenSpace Canvas——随离场销毁
            {
                if (View != null && View.SettingsButton != null && _menuClick != null)
                    View.SettingsButton.onClick.RemoveListener(_menuClick);   // 菜单钮监听随拆（存实例移除）
                _menuClick = null;
                UnityObjects.Destroy(_hudInstance);
                _hudInstance = null;
            }
            Unbind();
        }

        /// <summary>
        /// 挂对局准心（HUD prefab 内十字件——构建器确定性生成）：<see cref="BattleHudView"/> 缺失时静默降级。
        /// 准心画布无 GraphicRaycaster 且 CanvasGroup 不拦射线——**永不参与输入**；上下文门被拦时由驱动自己藏准心。
        /// </summary>
        private void AttachCrosshair(SimView view, IInputService input)
        {
            if (View == null)
            {
                Log.Warning("HUD prefab 缺失/未实例化——准心静默降级", LogTag.Battle);
                return;
            }

            _crosshair = new BattleCrosshairDriver(view, input, (RectTransform)View.RootTransform, View.CrosshairRoot,
                hip: View.HipForm,
                ads: View.AdsForm,
                feel: _crosshairFeel);                             // 手感投影（null = 测试装配回落 Default）——位置恒屏心（TPS）
            Add(_crosshair);
            Log.Info("crosshair-attached", LogTag.Battle);
        }

        /// <summary>挂 HUD 面板（血条/弹药——技能栏静态占位）：驱动读本地**预测态**，值变化才写、未对齐整组隐藏。</summary>
        private void AttachHud()
        {
            if (View == null)
            {
                Log.Warning("HUD prefab 缺失/未实例化——HUD 面板静默降级", LogTag.Battle);
                return;
            }

            _hud = new BattleHudDriver(_model.Predicted,
                View.PanelGroup, View.HpBar, View.AmmoText,
                View.ReloadLabel,                               // 缺标签件=驱动内静默降级（不显示换弹态）
                View.ReserveAmmoText, View.SlashSeparator); // 剩余备弹读出（缺件=静默降级：只显当前弹匣）
            if (View.SettingsButton != null && _menuClick != null)
                View.SettingsButton.onClick.AddListener(_menuClick);   // 对局菜单钮 → 流程层开设置页（退场链）
            Add(_hud);
            Log.Info("hud-attached", LogTag.Battle);
        }

        /// <summary>挂局内排行榜（FFA "比分"＝击杀）：读公共事实（权威镜像优先）；格件不齐 → 静默降级。</summary>
        private void AttachRankboard()
        {
            if (View == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (!View.HasRankboard)
            {
                Log.Warning("HUD prefab 排行榜格不齐（格根/名字/击杀/高亮）×8——排行榜静默降级", LogTag.Battle);
                return;
            }
            _rankboard = new BattleRankboardDriver(_model.Public,
                _model.LocalEntityId,
                View.RankCells, View.RankNames, View.RankKills, View.RankHighlights,
                View.TeamScoreText,                    // 团队比分预留节点（缺 = 驱动内跳过）
                _strategy.Rank);                           // 策略表全序（缺表 = 击杀降序→EntityId 升序）
            Add(_rankboard);
            Log.Info("rankboard-attached", LogTag.Battle);
        }

        /// <summary>挂全局击杀提示：消费会话公共战斗事件面的击杀队列；行件不齐 → 静默降级。</summary>
        private void AttachKillFeed()
        {
            if (View == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (!View.HasKillFeed)
            {
                Log.Warning("HUD prefab 击杀提示行件不齐（行四组）×4——击杀提示静默降级", LogTag.Battle);
                return;
            }
            _killFeed = new BattleKillFeedDriver(_model.Predicted,
                _model.Feed,
                View.WeaponIcons,
                View.KillFeedRows, View.KillFeedKillers, View.KillFeedWeapons, View.KillFeedVictims);
            Add(_killFeed);
            Log.Info("kill-feed-attached", LogTag.Battle);
        }

        /// <summary>挂网络质量显示（会话探针 RTT/丢包千分比）；件缺失 → 静默降级。</summary>
        private void AttachNetworkHud()
        {
            if (View == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (View.NetStatsRoot == null || View.NetStatsText == null)
            {
                Log.Warning("HUD prefab 网络质量件缺失——网络质量显示静默降级", LogTag.Battle);
                return;
            }
            _netStats = new BattleNetworkHudDriver(_model.Quality,
                View.NetStatsRoot.gameObject, View.NetStatsText);
            Add(_netStats);
            Log.Info("net-stats-attached", LogTag.Battle);
        }

        /// <summary>挂死亡/复活 HUD（中央倒计时 + 归属 + 保护角标）；件不齐 → 静默降级。
        /// 调用时点保持在激光/保护壳之后（与原接线序一致）。</summary>
        public void AttachRespawnHud(HitFeedbackDispatcher dispatcher)
        {
            if (View == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (!View.HasRespawnHud)
            {
                Log.Warning("HUD prefab 死亡/复活件不齐（中央面板 3 件）——死亡等待面板静默降级", LogTag.Battle);
                return;
            }
            _respawnHud = new BattleRespawnHudDriver(
                _model.Respawn,
                View.RespawnRoot, View.RespawnTitle, View.RespawnCountdown,
                View.ProtectionRoot);
            dispatcher?.Register(_respawnHud);
            Add(_respawnHud);
            Log.Info("respawn-hud-attached", LogTag.Battle);
        }
    }
}
