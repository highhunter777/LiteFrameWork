using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Battle
{
    /// <summary>战斗边界：RoomPage + HUD + 一局会话；唯一出口 BattleResult，不泄漏连接/世界/视图。</summary>
    public sealed class BattleFeature : IDisposable
    {
        private readonly UiRuntime _ui;
        private readonly Func<MatchInfo, BattleHudModel, CancellationToken, UniTask<BattleResult>> _session;

        public BattleFeature(UiRuntime ui,
            Func<MatchInfo, BattleHudModel, CancellationToken, UniTask<BattleResult>> session)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>跑一局：房间页 + HUD → 会话 → 必然关闭两个页面；取消原样传播，页面仍清理。</summary>
        public async UniTask<BattleResult> RunAsync(MatchInfo match, CancellationToken cancellationToken)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var hud = new BattleHudModel();
            PageHandle room = await _ui.OpenAsync<RoomPage>(
                RoomPage.Spec, page => page.Bind(match), cancellationToken);
            PageHandle hudPage = await _ui.OpenAsync<BattleHudView>(
                BattleHudView.Spec, page => page.Bind(hud), cancellationToken);
            try
            {
                BattleResult result = await _session(match, hud, cancellationToken);
                Log.Info("对局结束：" + result.Outcome + " reason=" + result.Reason);
                return result;
            }
            finally
            {
                if (!hudPage.IsClosed) await _ui.CloseAsync(hudPage);
                if (!room.IsClosed) await _ui.CloseAsync(room);
            }
        }

        public void Dispose()
        {
        }
    }
}
