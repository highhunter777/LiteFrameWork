using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Battle
{
    /// <summary>房间等待页：显示房间/地址/票据状态；P2a 只提供连接期可观察面，离开由流程/会话收口。</summary>
    public sealed class RoomPage : PageBase
    {
        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Battle/Content/RoomPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.DestroyOnClose);

        private MatchInfo _match;

        public void Bind(MatchInfo match) => _match = match;

        protected override void OnShow()
        {
            SetText("Txt_Title", Text("UI.Room.Info", _match?.RoomId ?? string.Empty));
            if (_match == null) return;
            SetText("Txt_Room", Text("UI.Room.Info", _match.RoomId));
            SetText("Txt_Status", _match.Address);
            OnClick("Btn_Leave", Leave);
        }

        private void Leave()
        {
            if (Handle == null) return;
            Handle.CloseAsync().Forget(ex => Log.Error("关闭房间页失败：" + ex));
        }
    }
}
