using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Battle.World;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Platform.Ui;
using UnityEngine;

namespace LiteGameNext.Features.Battle
{
    /// <summary>战斗边界：房间页 + HUD + 世界视图 + 一局会话；唯一出口 BattleResult。</summary>
    public sealed class BattleFeature : IDisposable
    {
        private readonly UiRuntime _ui;
        private readonly Func<MatchInfo, BattleHudModel, BattleWorldView, CancellationToken, UniTask<BattleResult>> _session;
        private readonly Func<MatchInfo, BattleHudModel, BattleWorldView, CancellationToken,
            System.Action<string, UnityEngine.Vector3, bool>, UniTask<BattleResult>> _sessionWithFeedback;
        private readonly Func<BattleWorldView> _worldFactory;
        private readonly Func<int, LiteGameNext.Platform.Config.CrosshairFeel> _crosshairOf;
        private BattleWorldView _activeWorld;

        /// <summary>逐帧驱动活动对局的世界视图（远端插值）；无活动对局或世界已销毁为 no-op。</summary>
        public void Tick(float deltaTime)
        {
            // Unity 的 fake-null：对象已 Destroy 时 `?.` 不成立，必须显式比较。
            if (_activeWorld == null) return;
            _activeWorld.Tick(deltaTime);
        }

        public BattleFeature(UiRuntime ui,
            Func<MatchInfo, BattleHudModel, BattleWorldView, CancellationToken, UniTask<BattleResult>> session,
            Func<BattleWorldView> worldFactory = null,
            Func<int, LiteGameNext.Platform.Config.CrosshairFeel> crosshairOf = null)
            : this(ui, session, worldFactory, crosshairOf, null)
        {
        }

        /// <summary>带伤害反馈回调的构造（HUD 飘字用）；反馈为 null 时退回 4 参委托。</summary>
        public BattleFeature(UiRuntime ui,
            Func<MatchInfo, BattleHudModel, BattleWorldView, CancellationToken, UniTask<BattleResult>> session,
            Func<BattleWorldView> worldFactory,
            Func<int, LiteGameNext.Platform.Config.CrosshairFeel> crosshairOf,
            Func<MatchInfo, BattleHudModel, BattleWorldView, CancellationToken,
                System.Action<string, UnityEngine.Vector3, bool>, UniTask<BattleResult>> sessionWithFeedback)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _worldFactory = worldFactory;
            _crosshairOf = crosshairOf;
            _sessionWithFeedback = sessionWithFeedback;
        }

        /// <summary>跑一局：房间页/HUD/世界 → 会话 → 必然清理页面与世界；取消仍清理。</summary>
        public async UniTask<BattleResult> RunAsync(MatchInfo match, CancellationToken cancellationToken)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var hud = new BattleHudModel();
            if (_crosshairOf != null) hud.SetCrosshairFeel(_crosshairOf(0));
            BattleWorldView world = _worldFactory?.Invoke();
            _activeWorld = world;
            PageHandle room = await _ui.OpenAsync<RoomPage>(
                RoomPage.Spec, page => page.Bind(match), cancellationToken);
            PageHandle hudPage = await _ui.OpenAsync<BattleHudView>(
                BattleHudView.Spec, page => page.Bind(hud), cancellationToken);
            try
            {
                    var hudView = hudPage.Page as BattleHudView;
                BattleResult result = _sessionWithFeedback != null
                    ? await _sessionWithFeedback(match, hud, world, cancellationToken,
                        (text, pos, crit) => hudView?.PushDamage(text, pos, crit))
                    : await _session(match, hud, world, cancellationToken);
                Log.Info("对局结束：" + result.Outcome + " reason=" + result.Reason);
                return result;
            }
            finally
            {
                if (!hudPage.IsClosed) await _ui.CloseAsync(hudPage);
                if (!room.IsClosed) await _ui.CloseAsync(room);
                if (world != null) UnityEngine.Object.Destroy(world.gameObject);
                _activeWorld = null;
            }
        }

        public void Dispose()
        {
        }
    }
}
