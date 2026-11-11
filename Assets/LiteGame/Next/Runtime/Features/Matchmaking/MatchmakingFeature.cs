using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using MetaWire;

namespace LiteGameNext.Features.Matchmaking
{
    /// <summary>匹配结果：进房所需的最小事实；BattleFeature 只消费它，不认 LobbyPage/HTTP。</summary>
    public sealed class MatchInfo
    {
        public MatchInfo(string instanceId, string address, string roomId, string ticket, long expiresAtMs)
        {
            InstanceId = instanceId;
            Address = address;
            RoomId = roomId;
            Ticket = ticket;
            ExpiresAtMs = expiresAtMs;
        }

        public string InstanceId { get; }

        public string Address { get; }

        public string RoomId { get; }

        public string Ticket { get; }

        public long ExpiresAtMs { get; }
    }

    /// <summary>匹配功能：大厅页 + 房间列表/快速进入/建房；对外只给 MatchInfo，不交接房间操作句柄。</summary>
    public sealed class MatchmakingFeature : IDisposable
    {
        private readonly UiRuntime _ui;
        private readonly AccountFeature _account;
        private readonly MetaClient _meta;

        public MatchmakingFeature(UiRuntime ui, AccountFeature account, MetaClient meta)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        }

        public IReadOnlyList<LobbyInstanceView> Rooms { get; private set; } = Array.Empty<LobbyInstanceView>();

        public MatchInfo CurrentMatch { get; private set; }

        public string Status { get; private set; } = string.Empty;

        public bool IsBusy { get; private set; }

        /// <summary>打开大厅页并绑定；页面关闭后功能仍保留最近房间/结果，供调用方读取。</summary>
        public UniTask OpenAsync(CancellationToken cancellationToken)
            => _ui.OpenAsync<LobbyPage>(LobbyPage.Spec, page => page.Bind(this), cancellationToken);

        /// <summary>刷新公开房间列表；失败保留旧列表并写 Status。</summary>
        public async UniTask<bool> RefreshAsync(CancellationToken cancellationToken)
        {
            if (IsBusy) return false;
            IsBusy = true;
            Status = "刷新房间列表…";
            try
            {
                MetaCallResult<IReadOnlyList<LobbyInstanceView>> result =
                    await _meta.ListRoomsAsync(_account.AccessToken, cancellationToken);
                if (!result.Success)
                {
                    Status = "房间列表失败：" + result.Error.Code;
                    Log.Warning("Matchmaking 房间列表失败：" + result.Error);
                    return false;
                }

                Rooms = result.Value;
                Status = Rooms.Count == 0 ? "暂无房间，可建房或快速进入" : "房间数：" + Rooms.Count;
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>创建房间并按返回票据形成 MatchInfo。</summary>
        public async UniTask<bool> CreateRoomAsync(CancellationToken cancellationToken)
        {
            if (IsBusy) return false;
            IsBusy = true;
            Status = "建房中…";
            try
            {
                MetaCallResult<JoinTicketInfo> result = await _meta.CreateRoomAsync(
                    _account.AccessToken, string.Empty, cancellationToken: cancellationToken);
                if (!result.Success)
                {
                    Status = "建房失败：" + result.Error.Code;
                    Log.Warning("Matchmaking 建房失败：" + result.Error);
                    return false;
                }

                CurrentMatch = ToMatch(result.Value);
                Status = "已建房：" + CurrentMatch.RoomId;
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>快速进入：取第一个可用房间换票；无房时提示建房。</summary>
        public async UniTask<bool> QuickJoinAsync(CancellationToken cancellationToken)
        {
            if (IsBusy) return false;
            if (Rooms.Count == 0)
            {
                Status = "没有可进入的房间，请先建房";
                return false;
            }

            IsBusy = true;
            Status = "进入房间…";
            try
            {
                LobbyInstanceView room = Rooms[0];
                string roomId = room.Rooms != null && room.Rooms.Count > 0 ? room.Rooms[0].RoomId : string.Empty;
                MetaCallResult<JoinTicketInfo> result = await _meta.RequestJoinTicketAsync(
                    _account.AccessToken, roomId, string.Empty, cancellationToken);
                if (!result.Success)
                {
                    Status = "进房失败：" + result.Error.Code;
                    Log.Warning("Matchmaking 进房失败：" + result.Error);
                    return false;
                }

                CurrentMatch = ToMatch(result.Value);
                Status = "已进入房间：" + CurrentMatch.RoomId;
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ClearMatch() => CurrentMatch = null;

        public void Dispose()
        {
            CurrentMatch = null;
            Rooms = Array.Empty<LobbyInstanceView>();
        }

        private static MatchInfo ToMatch(JoinTicketInfo ticket)
            => new MatchInfo(ticket.InstanceId, ticket.Address, ticket.RoomId, ticket.Ticket, ticket.ExpiresAtMs);
    }
}
