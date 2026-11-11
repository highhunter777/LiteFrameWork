using System;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Platform.Ui;
using MetaWire;

namespace LiteGameNext.Features.Matchmaking
{
    /// <summary>大厅页：房间列表/刷新/建房/快速进入/关闭；所有网络动作经 MatchmakingFeature。</summary>
    public sealed class LobbyPage : PageBase
    {
        private const string Title = "大厅";

        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Matchmaking/Content/LobbyPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.Lru);

        private MatchmakingFeature _feature;

        public void Bind(MatchmakingFeature feature) => _feature = feature;

        protected override void OnShow()
        {
            SetText("Txt_Title", Title);
            OnClick("Btn_Refresh", Refresh);
            OnClick("Btn_Create", Create);
            OnClick("Btn_QuickJoin", QuickJoin);
            OnClick("Btn_Close", Close);
            RefreshView();
        }

        private void Refresh() => Run(RefreshAsync());

        private void Create() => Run(CreateAsync());

        private void QuickJoin() => Run(QuickJoinAsync());

        private void Close()
        {
            if (Handle == null) return;
            Handle.CloseAsync().Forget(ex => Log.Error("关闭大厅失败：" + ex));
        }

        private void Run(UniTask operation) => operation.Forget(ex => Log.Error("大厅操作失败：" + ex));

        private async UniTask RefreshAsync()
        {
            if (_feature == null) return;
            await _feature.RefreshAsync(CancellationToken);
            if (Handle != null && !Handle.IsClosed) RefreshView();
        }

        private async UniTask CreateAsync()
        {
            if (_feature == null) return;
            await _feature.CreateRoomAsync(CancellationToken);
            if (Handle != null && !Handle.IsClosed) RefreshView();
        }

        private async UniTask QuickJoinAsync()
        {
            if (_feature == null) return;
            await _feature.QuickJoinAsync(CancellationToken);
            if (Handle != null && !Handle.IsClosed) RefreshView();
        }

        private void RefreshView()
        {
            if (_feature == null) return;
            SetText("Txt_Status", _feature.Status);
            SetText("Txt_Rooms", DescribeRooms(_feature.Rooms));
            SetInteractable("Btn_Refresh", !_feature.IsBusy);
            SetInteractable("Btn_Create", !_feature.IsBusy);
            SetInteractable("Btn_QuickJoin", !_feature.IsBusy);
        }

        private static string DescribeRooms(System.Collections.Generic.IReadOnlyList<LobbyInstanceView> rooms)
        {
            if (rooms == null || rooms.Count == 0) return "暂无房间";
            var text = new System.Text.StringBuilder();
            for (int index = 0; index < rooms.Count; index++)
            {
                LobbyInstanceView room = rooms[index];
                if (index > 0) text.Append('\n');
                text.Append(room.InstanceId).Append("  ").Append(room.PlayerCount).Append('/')
                    .Append(room.MaxPlayers).Append("  ").Append(room.Address);
                if (room.Rooms != null && room.Rooms.Count > 0)
                    text.Append("  房号 ").Append(room.Rooms[0].RoomId);
            }

            return text.ToString();
        }
    }
}
