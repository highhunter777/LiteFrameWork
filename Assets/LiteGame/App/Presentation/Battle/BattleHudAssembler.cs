using System;
using LiteFramework;
using LiteFramework;
using LiteSim;
using LiteView;
using UnityEngine;
using LiteBattle;

namespace LiteGame
{
    /// <summary>
    /// 对局 **HUD 族装配器**：准心 / 血条弹药面板 / 局内排行榜 / 全局击杀提示 / 网络质量 / 死亡复活面板，
    /// 以及 HUD 根件销毁与菜单钮监听挂摘。件缺失一律静默降级（只警告）——HUD 是表现增益，不当作对局失败。
    /// 驱动实例归本件；根件（ScreenSpace Canvas 实例）也由本件销毁，租约归视图作用域。
    /// </summary>
    internal sealed class BattleHudAssembler
    {
        private readonly CrosshairFeelProfile _crosshairFeel;   // 准心手感投影（tbcrosshair——可空 = 测试装配回落 Default）

        private BattleCrosshairDriver _crosshair;
        private BattleReadModel _predictedModel;              // HUD/击杀提示共用（预测态读源）
        private BattleHudDriver _hud;
        private BattleRankboardDriver _rankboard;
        private BattleKillFeedDriver _killFeed;
        private BattleNetworkHudDriver _netStats;
        private BattleRespawnHudDriver _respawnHud;
        private BattleHudView _hudView;
        private GameObject _hudInstance;
        private UnityEngine.Events.UnityAction _menuClick;

        public BattleHudAssembler(CrosshairFeelProfile crosshairFeel)
            => _crosshairFeel = crosshairFeel;

        public BattleCrosshairDriver Crosshair => _crosshair;

        /// <summary>建 HUD 族并接线（须在视图建立之后；HUD 件自身缺失时逐项静默降级）。</summary>
        public void Attach(BattleContext context, SimView view, IInputService input,
            BattleHudView hudView, GameObject hudInstance, UnityEngine.Events.UnityAction menuClick,
            HitFeedbackDispatcher dispatcher, Func<SimWorldState> publicFactState)
        {
            _hudView = hudView;
            _hudInstance = hudInstance;
            _menuClick = menuClick;

            AttachCrosshair(view, input);
            AttachHud(context, view);
            AttachRankboard(publicFactState, view);
            AttachKillFeed(context, view);
            AttachNetworkHud(context);
            dispatcher?.Register(_crosshair);                 // 准心：本地 Fire → 连发张开/后坐（null 拒收 = 缺件降级）
        }

        /// <summary>每渲染帧：准心 → HUD → 排行榜 → 击杀提示 → 网络质量 → 阵亡 HUD（同帧顺序固定）。</summary>
        public void Tick(float elapseSeconds)
        {
            _crosshair?.Tick(elapseSeconds);          // 位置/形态/光标 + 连发张开/后坐回正（真实帧时长）
            _hud?.Tick();                             // 血条/弹药（预测态值变化写）
            _rankboard?.Tick();                       // 击杀分布变化才重排/写（镜像公共面）
            _killFeed?.Tick();                        // 待展示队列 → 行（驻留到时回收）
            _netStats?.Tick();                        // 探针 RTT/丢包（值变才写，离线隐藏）
            _respawnHud?.Tick();                      // 阵亡倒计时/归属/保护角标（值变化才写）
        }

        public void DisposeRankboard()
        {
            _rankboard?.Dispose();                    // 格归位——同属视图消费者
            _rankboard = null;
        }

        public void DisposeKillFeed()
        {
            _killFeed?.Dispose();                     // 行归位——同属视图消费者
            _killFeed = null;
        }

        public void DisposeNetworkHud()
        {
            _netStats?.Dispose();                     // 归位——同属视图消费者
            _netStats = null;
        }

        public void DisposePanel()
        {
            _hud?.Dispose();                          // 面板归零——视图消费者，先于准心拆
            _hud = null;
        }

        public void DisposeRespawnHud()
        {
            _respawnHud?.Dispose();                   // 帧事件消费者——先于分发器摘
            _respawnHud = null;
        }

        public void DisposeCrosshair()
        {
            _crosshair?.Dispose();                    // 还系统光标——同属视图消费者
            _crosshair = null;
        }

        /// <summary>HUD 根件销毁 + 菜单钮监听摘除（在 HUD 驱动之后、视图根之前调用）。</summary>
        public void DisposeRoot()
        {
            if (_hudInstance != null)                 // ScreenSpace Canvas——随离场销毁
            {
                if (_hudView != null && _hudView.SettingsButton != null && _menuClick != null)
                    _hudView.SettingsButton.onClick.RemoveListener(_menuClick);   // 菜单钮监听随拆（存实例移除）
                _menuClick = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(_hudInstance);
                else UnityEngine.Object.DestroyImmediate(_hudInstance);
                _hudInstance = null;
            }
            _hudView = null;
        }

        /// <summary>
        /// 挂对局准心（HUD prefab 内十字件——构建器确定性生成）：<see cref="BattleHudView"/> 缺失时静默降级。
        /// 准心画布无 GraphicRaycaster 且 CanvasGroup 不拦射线——**永不参与输入**；上下文门被拦时由驱动自己藏准心。
        /// </summary>
        private void AttachCrosshair(SimView view, IInputService input)
        {
            if (_hudView == null)
            {
                Log.Warning("HUD prefab 缺失/未实例化——准心静默降级", LogTag.Battle);
                return;
            }

            _crosshair = new BattleCrosshairDriver(view, input, (RectTransform)_hudView.transform, _hudView.CrosshairRoot,
                hip: _hudView.HipForm,
                ads: _hudView.AdsForm,
                feel: _crosshairFeel);                             // 手感投影（null = 测试装配回落 Default）——位置恒屏心（TPS）
            Log.Info("crosshair-attached", LogTag.Battle);
        }

        /// <summary>挂 HUD 面板（血条/弹药——技能栏静态占位）：驱动读本地**预测态**，值变化才写、未对齐整组隐藏。</summary>
        private void AttachHud(BattleContext context, SimView view)
        {
            if (_hudView == null)
            {
                Log.Warning("HUD prefab 缺失/未实例化——HUD 面板静默降级", LogTag.Battle);
                return;
            }

            _predictedModel = new BattleReadModel(() => context.Sim.State,
                () => view != null ? view.LocalEntityId : 0L);
            _hud = new BattleHudDriver(_predictedModel,
                _hudView.PanelGroup, _hudView.HpBar, _hudView.AmmoText,
                _hudView.ReloadLabel,                               // 缺标签件=驱动内静默降级（不显示换弹态）
                _hudView.ReserveAmmoText, _hudView.SlashSeparator); // 剩余备弹读出（缺件=静默降级：只显当前弹匣）
            if (_hudView.SettingsButton != null && _menuClick != null)
                _hudView.SettingsButton.onClick.AddListener(_menuClick);   // 对局菜单钮 → 流程层开设置页（退场链）
            Log.Info("hud-attached", LogTag.Battle);
        }

        /// <summary>挂局内排行榜（FFA "比分"＝击杀）：读公共事实（权威镜像优先）；格件不齐 → 静默降级。</summary>
        private void AttachRankboard(Func<SimWorldState> publicFactState, SimView view)
        {
            if (_hudView == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (!_hudView.HasRankboard)
            {
                Log.Warning("HUD prefab 排行榜格不齐（格根/名字/击杀/高亮）×8——排行榜静默降级", LogTag.Battle);
                return;
            }
            var rankModel = new BattleReadModel(publicFactState,
                () => view != null ? view.LocalEntityId : 0L);
            _rankboard = new BattleRankboardDriver(rankModel,
                () => view != null ? view.LocalEntityId : 0L,
                _hudView.RankCells, _hudView.RankNames, _hudView.RankKills, _hudView.RankHighlights,
                _hudView.TeamScoreText);                   // 团队比分预留节点（缺 = 驱动内跳过）
            Log.Info("rankboard-attached", LogTag.Battle);
        }

        /// <summary>击杀提示读模型：优先复用 HUD 的预测态实例（同一读源、少一份缓冲）。</summary>
        private BattleReadModel KillFeedModel(BattleContext context, SimView view)
            => _predictedModel ?? new BattleReadModel(() => context.Sim.State,
                () => view != null ? view.LocalEntityId : 0L);

        /// <summary>挂全局击杀提示：消费会话公共战斗事件面的击杀队列；行件不齐 → 静默降级。</summary>
        private void AttachKillFeed(BattleContext context, SimView view)
        {
            if (_hudView == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (!_hudView.HasKillFeed)
            {
                Log.Warning("HUD prefab 击杀提示行件不齐（行四组）×4——击杀提示静默降级", LogTag.Battle);
                return;
            }
            _killFeed = new BattleKillFeedDriver(KillFeedModel(context, view),
                context.CombatFeed,
                _hudView.WeaponIcons,
                _hudView.KillFeedRows, _hudView.KillFeedKillers, _hudView.KillFeedWeapons, _hudView.KillFeedVictims);
            Log.Info("kill-feed-attached", LogTag.Battle);
        }

        /// <summary>挂网络质量显示（会话探针 RTT/丢包千分比）；件缺失 → 静默降级。</summary>
        private void AttachNetworkHud(BattleContext context)
        {
            if (_hudView == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (_hudView.NetStatsRoot == null || _hudView.NetStatsText == null)
            {
                Log.Warning("HUD prefab 网络质量件缺失——网络质量显示静默降级", LogTag.Battle);
                return;
            }
            _netStats = new BattleNetworkHudDriver(() => context.NetworkQuality,
                _hudView.NetStatsRoot.gameObject, _hudView.NetStatsText);
            Log.Info("net-stats-attached", LogTag.Battle);
        }

        /// <summary>挂死亡/复活 HUD（中央倒计时 + 归属 + 保护角标）；件不齐 → 静默降级。
        /// 调用时点保持在激光/保护壳之后（与原接线序一致）。</summary>
        public void AttachRespawnHud(Func<SimWorldState> publicFactState, Func<long> localEntityId,
            Func<bool> isLocalDead, HitFeedbackDispatcher dispatcher)
        {
            if (_hudView == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (!_hudView.HasRespawnHud)
            {
                Log.Warning("HUD prefab 死亡/复活件不齐（中央面板 3 件）——死亡等待面板静默降级", LogTag.Battle);
                return;
            }
            _respawnHud = new BattleRespawnHudDriver(
                new BattleReadModel(publicFactState, localEntityId, isLocalDead),
                _hudView.RespawnRoot, _hudView.RespawnTitle, _hudView.RespawnCountdown,
                _hudView.ProtectionRoot);
            dispatcher?.Register(_respawnHud);
            Log.Info("respawn-hud-attached", LogTag.Battle);
        }
    }
}
