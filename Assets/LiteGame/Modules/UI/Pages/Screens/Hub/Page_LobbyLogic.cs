using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Map;

namespace LiteGame.Modules.UI
{
    /// <summary>大厅页：房间列表轮询、快速进入/选房/建房、战绩与设置入口、头像气泡。</summary>
    public sealed class Page_LobbyLogic : PageLogicBase
    {
        private static readonly int[] PlayerCounts = { 2, 4, 6, 8 };
        private const float RefreshIntervalSeconds = 1f;
        private const float BubbleSeconds = 3f;

        private bool _closed = true;
        private bool _entering;
        private bool _refreshPending;
        private float _refreshAccum;
        private string _rowsSignature;
        private string _statusKey;
        private bool _roomsOpen;
        private bool _createOpen;
        private float _bubble;
        private int _line;
        private IReadOnlyList<MapOption> _maps;
        private readonly List<string> _lines = new List<string>(3);

        protected override void OnShow(IUIData data)
        {
            PageServices services = Services;
            if (services == null) return;
            SetTextKey("Txt_QuickEnter", "UI.Lobby.QuickEnter");
            SetTextKey("Txt_Rooms", "UI.Lobby.RoomList");
            SetTextKey("Txt_CreateRoom", "UI.Lobby.CreateRoom");
            SetTextKey("Txt_Records", "UI.Lobby.Records");
            SetTextKey("Txt_Settings", "Common.Settings");
            SetTextKey("Txt_RoomsTitle", "UI.Lobby.RoomList");
            SetTextKey("Txt_RoomsClose", "Common.Close");
            SetTextKey("Txt_CreateTitle", "UI.Lobby.CreateRoom");
            SetTextKey("Txt_MapLabel", "UI.Lobby.Map");
            SetTextKey("Txt_PlayersLabel", "UI.Lobby.Players");
            SetTextKey("Txt_ConfirmCreate", "UI.Lobby.Create");
            SetTextKey("Txt_CancelCreate", "Common.Cancel");
            SetText("Txt_PlayerName", services.Meta.AccountId);

            _lines.Clear();
            if (Locale != null)
            {
                _lines.Add(Locale.Get("UI.Lobby.Bubble1"));
                _lines.Add(Locale.Get("UI.Lobby.Bubble2"));
                _lines.Add(Locale.Get("UI.Lobby.Bubble3"));
            }
            _line = 0;
            _bubble = 0;
            SetVisible("Cot_Bubble", false);
            SetVisible("Cot_Rooms", false);
            SetVisible("Cot_CreateParams", false);

            OnButtonClick("Btn_QuickEnter", () => Enter(string.Empty));
            OnButtonClick("Btn_Rooms", () => ShowRooms(true));
            OnButtonClick("Btn_RoomsClose", () => ShowRooms(false));
            OnButtonClick("Btn_CreateRoom", () => ShowCreate(true));
            OnButtonClick("Btn_CancelCreate", () => ShowCreate(false));
            OnButtonClick("Btn_ConfirmCreate", CreateRoom);
            OnButtonClick("Btn_Records", () => Go(PageIds.Page_Records));
            OnButtonClick("Btn_Settings", () => Go(PageIds.Page_Settings));
            OnButtonClick("Btn_Character", Tap);
            Index.OnListClick("VList_Rooms", (key, action) => Enter(key));

            SetupCreateParams(services.Maps);
            _closed = false;
            _entering = false;
            _statusKey = null;
            _rowsSignature = null;
            _refreshAccum = 0;
            RefreshRooms();
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (_bubble > 0f)
            {
                _bubble -= deltaTime;
                if (_bubble <= 0f) SetVisible("Cot_Bubble", false);
            }
            if (_closed) return;
            _refreshAccum += deltaTime;
            if (_refreshAccum < RefreshIntervalSeconds) return;
            _refreshAccum = 0f;
            RefreshRooms();
        }

        protected override void OnReveal()
        {
            if (_closed) return;
            _refreshAccum = 0f;
            RefreshRooms();
        }

        private void SetupCreateParams(IMapProvider maps)
        {
            _maps = maps.Options;
            var mapOptions = new List<string>(_maps.Count);
            for (int i = 0; i < _maps.Count; i++) mapOptions.Add(_maps[i].Name);
            Index.SetDropdownOptions("Ddl_Map", mapOptions);
            Index.SetDropdownValue("Ddl_Map", 0);

            var countOptions = new List<string>(PlayerCounts.Length);
            for (int i = 0; i < PlayerCounts.Length; i++)
            {
                int count = PlayerCounts[i];
                countOptions.Add(Locale != null ? Locale.Format("UI.Lobby.PlayersOption", count)
                                                : count.ToString(CultureInfo.InvariantCulture));
            }
            Index.SetDropdownOptions("Ddl_Players", countOptions);
            Index.SetDropdownValue("Ddl_Players", 0);
        }

        private int SelectedMapId()
        {
            int index = Index.GetDropdownValue("Ddl_Map");
            return _maps != null && index >= 0 && index < _maps.Count ? _maps[index].Id : 0;
        }

        private int SelectedPlayers()
        {
            int index = Index.GetDropdownValue("Ddl_Players");
            return index >= 0 && index < PlayerCounts.Length ? PlayerCounts[index] : 0;
        }

        private void SetStatus(string key)
        {
            if (string.Equals(_statusKey, key, StringComparison.Ordinal)) return;
            _statusKey = key;
            SetTextKey("Txt_RoomListStatus", key);
        }

        private void RefreshRooms()
        {
            if (_closed || _entering || _refreshPending) return;
            IMetaSession meta = Meta;
            if (!meta.IsConfigured)
            {
                SetStatus("UI.Login.NoServer");
                return;
            }
            _refreshPending = true;
            RefreshRoomsAsync(meta).ForgetLogged("Page_Lobby.refresh");
        }

        private async UniTask RefreshRoomsAsync(IMetaSession meta)
        {
            int generation = Form.DisplayGeneration;
            MetaResult<IReadOnlyList<MetaRoomRow>> result;
            try { result = await meta.ListRoomsAsync(Form.DisplayToken); }
            catch (OperationCanceledException) { _refreshPending = false; return; }
            catch (Exception ex)
            {
                Log.Error("房间列表查询失败:" + ex.Message, LogTag.Meta);
                result = MetaResult<IReadOnlyList<MetaRoomRow>>.Fail("Internal");
            }
            _refreshPending = false;
            if (_closed || _entering || !Form.IsDisplayCurrent(generation)) return;
            if (!result.Ok)
            {
                SetStatus("UI.Lobby.ListFailed");
                return;
            }
            int count = result.Value != null ? result.Value.Count : 0;
            SetStatus(count == 0 ? "UI.Lobby.RoomEmpty" : "UI.Lobby.Ready");
            PushRooms(result.Value);
        }

        private void PushRooms(IReadOnlyList<MetaRoomRow> rooms)
        {
            int count = rooms != null ? rooms.Count : 0;
            var set = UIListRowSet.Create();
            var signature = new StringBuilder(count * 24);
            for (int i = 0; i < count; i++)
            {
                MetaRoomRow room = rooms[i];
                string name = room.RoomId;
                if (!string.IsNullOrEmpty(room.MapName)) name = room.RoomId + " · " + room.MapName;
                set.Row(room.RoomId)
                    .Text("Txt_RoomName", name)
                    .KeyText("Txt_RoomState", "UI.Lobby.RoomState", room.Players, room.ExpectedPlayers);
                signature.Append(name).Append('/')
                    .Append(room.Players.ToString(CultureInfo.InvariantCulture)).Append('/')
                    .Append(room.ExpectedPlayers.ToString(CultureInfo.InvariantCulture)).Append('|');
            }

            string fingerprint = signature.ToString();       // 轮询同帧去重：内容不变不整表重绑
            if (string.Equals(fingerprint, _rowsSignature, StringComparison.Ordinal)) return;
            _rowsSignature = fingerprint;
            Index.SetList("VList_Rooms", set);
        }

        private void Enter(string roomId)
        {
            if (_entering) return;
            IMetaSession meta = Meta;
            if (!meta.IsConfigured)
            {
                SetStatus("UI.Login.NoServer");
                return;
            }
            _entering = true;
            SetInteractable("Btn_QuickEnter", false);
            SetStatus("UI.Lobby.Allocating");
            EnterAsync(meta, roomId ?? string.Empty).ForgetLogged("Page_Lobby.enter");
        }

        private async UniTask EnterAsync(IMetaSession meta, string roomId)
        {
            int generation = Form.DisplayGeneration;
            MetaResult<string> result;
            try
            {
                result = string.IsNullOrEmpty(roomId)
                    ? await meta.QuickEnterAsync(Form.DisplayToken)
                    : await meta.EnterRoomAsync(roomId, Form.DisplayToken);
            }
            catch (OperationCanceledException)
            {
                if (Form.IsDisplayCurrent(generation)) _entering = false;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("进房失败:" + ex.Message, LogTag.Meta);
                result = MetaResult<string>.Fail("Internal");
            }
            if (!Form.IsDisplayCurrent(generation)) return;
            _entering = false;
            SetInteractable("Btn_QuickEnter", true);
            if (result.Ok)
            {
                Replace(PageIds.Page_Room, new RoomPageData(result.Value));
                return;
            }
            SetStatus("UI.Lobby.EnterFailed");
            Index.ShowToast(Locale != null ? Locale.Get("UI.Lobby.EnterFailed") : "UI.Lobby.EnterFailed");
        }

        private void CreateRoom()
        {
            if (_entering) return;
            IMetaSession meta = Meta;
            if (!meta.IsConfigured)
            {
                SetStatus("UI.Login.NoServer");
                return;
            }
            _entering = true;
            SetInteractable("Btn_ConfirmCreate", false);
            SetStatus("UI.Lobby.Creating");
            CreateRoomAsync(meta, SelectedMapId(), SelectedPlayers()).ForgetLogged("Page_Lobby.create");
        }

        private async UniTask CreateRoomAsync(IMetaSession meta, int mapId, int expectedPlayers)
        {
            int generation = Form.DisplayGeneration;
            MetaResult<string> result;
            try { result = await meta.CreateRoomAsync(mapId, expectedPlayers, Form.DisplayToken); }
            catch (OperationCanceledException)
            {
                if (Form.IsDisplayCurrent(generation)) _entering = false;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("建房失败:" + ex.Message, LogTag.Meta);
                result = MetaResult<string>.Fail("Internal");
            }
            if (!Form.IsDisplayCurrent(generation)) return;
            _entering = false;
            SetInteractable("Btn_ConfirmCreate", true);
            if (result.Ok)
            {
                Replace(PageIds.Page_Room, new RoomPageData(result.Value));
                return;
            }
            SetStatus("UI.Lobby.CreateFailed");
            Index.ShowToast(Locale != null ? Locale.Get("UI.Lobby.CreateFailed") : "UI.Lobby.CreateFailed");
        }

        private void ShowRooms(bool open)
        {
            _roomsOpen = open;
            SetVisible("Cot_Rooms", open);
            if (!open) return;
            _createOpen = false;
            SetVisible("Cot_CreateParams", false);
            _refreshAccum = 0f;
            RefreshRooms();
        }

        private void ShowCreate(bool open)
        {
            _createOpen = open;
            SetVisible("Cot_CreateParams", open);
            if (!open) return;
            _roomsOpen = false;
            SetVisible("Cot_Rooms", false);
        }

        private void Tap()
        {
            if (_lines.Count == 0) return;
            _line = _line % _lines.Count + 1;
            _bubble = BubbleSeconds;
            SetText("Txt_Bubble", _lines[_line]);
            SetVisible("Cot_Bubble", true);
        }

        protected override void OnPageHide()
        {
            _entering = false;
            _closed = true;
            _bubble = 0f;
        }
    }
}
