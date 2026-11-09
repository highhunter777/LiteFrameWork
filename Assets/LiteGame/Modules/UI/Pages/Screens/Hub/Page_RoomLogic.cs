using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>房间页：房号展示、席位名单与等待人数、房主提前开局、离房回大厅。</summary>
    public sealed class Page_RoomLogic : PageLogicBase
    {
        private const string KeyOnline = "UI.Room.PlayerState.Online";
        private const string KeyConnecting = "UI.Room.PlayerState.Connecting";

        private bool _starting;

        protected override void OnShow(IUIData data)
        {
            _starting = false;
            SetInteractable("Btn_StartMatch", true);           // 复用页复位：上次点开局后的禁钮不得残留
            string roomId = (data as RoomPageData)?.RoomId;
            bool hasRoom = !string.IsNullOrEmpty(roomId);
            SetVisible("Txt_RoomId", hasRoom);
            if (hasRoom) SetTextKey("Txt_RoomId", "UI.Room.Info", roomId);
            SetVisible("Txt_PlayerCount", false);              // 席位面到达再显（不显假数）

            SetTextKey("Txt_Leave", "Common.Leave");
            OnButtonClick("Btn_Leave", Leave);
            SetVisible("Btn_StartMatch", false);               // 缺省隐藏（非房主/未进房不显）
            SetTextKey("Txt_StartMatch", "UI.Room.StartMatch");
            OnButtonClick("Btn_StartMatch", StartMatch);

            IRoomView room = Room;
            if (room == null) return;
            room.OwnerChanged += OnOwner;
            room.SeatsChanged += OnSeats;
            room.StartMatchDenied += OnStartDenied;
            OnOwner(room.IsOwner);                              // 订阅即回放
            OnSeats(room.Seats);
        }

        private void OnOwner(bool isOwner) => SetVisible("Btn_StartMatch", isOwner);

        private void OnSeats(IReadOnlyList<RoomSeat> seats)
        {
            var set = UIListRowSet.Create();
            int count = seats?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                RoomSeat seat = seats[i];
                set.Row(seat.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Text("Txt_PlayerName", seat.Name)
                    .KeyText("Txt_PlayerState", seat.Online ? KeyOnline : KeyConnecting);
            }
            Index.SetList("SList_Players", set);
            RefreshWaiting(count);
        }

        /// <summary>等待人数：分子 = 在册席位、分母 = JoinAck 房规模；任一未就绪一律隐藏。</summary>
        private void RefreshWaiting(int seated)
        {
            IRoomView room = Room;
            int expected = room != null ? room.ExpectedPlayers : 0;
            bool show = seated > 0 && expected > 0;
            SetVisible("Txt_PlayerCount", show);
            if (show) SetTextKey("Txt_PlayerCount", "UI.Room.Waiting", seated, expected);
        }

        /// <summary>离房：只表达离房意图，页面保留到统一恢复清理完成后由 PageFlow 回大厅。</summary>
        private void Leave()
        {
            Meta?.LeaveMatch();
        }

        private void StartMatch()
        {
            if (_starting) return;
            IRoomView room = Room;
            if (room == null) return;
            _starting = true;
            SetInteractable("Btn_StartMatch", false);
            room.RequestStartMatch();                           // 成功不回调（OnStartGame 流程接管关页）
        }

        private void OnStartDenied(string reason)
        {
            _starting = false;
            SetInteractable("Btn_StartMatch", true);
            string text = Locale != null ? Locale.Format("UI.Room.StartDenied", reason) : "UI.Room.StartDenied";
            Index.ShowToast(text);
        }

        protected override void OnPageHide()
        {
            _starting = false;
            IRoomView room = Room;
            if (room == null) return;
            room.OwnerChanged -= OnOwner;
            room.SeatsChanged -= OnSeats;
            room.StartMatchDenied -= OnStartDenied;
        }
    }
}
