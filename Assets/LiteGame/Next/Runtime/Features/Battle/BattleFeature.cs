using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Battle
{
    /// <summary>战斗边界：RoomPage + 一局会话；唯一出口 BattleResult，不把连接/世界/视图泄漏给流程。</summary>
    public sealed class BattleFeature : IDisposable
    {
        private readonly UiRuntime _ui;
        private readonly Func<MatchInfo, CancellationToken, UniTask<BattleResult>> _session;

        public BattleFeature(UiRuntime ui,
            Func<MatchInfo, CancellationToken, UniTask<BattleResult>> session)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>跑一局：打开房间页 → 会话 → 必然关闭房间页；取消原样传播，页面仍清理。</summary>
        public async UniTask<BattleResult> RunAsync(MatchInfo match, CancellationToken cancellationToken)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            PageHandle room = await _ui.OpenAsync<RoomPage>(
                RoomPage.Spec, page => page.Bind(match), cancellationToken);
            try
            {
                BattleResult result = await _session(match, cancellationToken);
                Log.Info("对局结束：" + result.Outcome + " reason=" + result.Reason);
                return result;
            }
            finally
            {
                if (!room.IsClosed) await _ui.CloseAsync(room);
            }
        }

        public void Dispose()
        {
        }
    }
}
