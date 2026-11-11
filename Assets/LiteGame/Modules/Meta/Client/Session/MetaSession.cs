using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using MetaWire;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Meta.Abstractions.Account;
using LiteGame.Modules.Meta.Abstractions.Room;
using LiteGame.Modules.Meta.Abstractions.Session;
using LiteGame.Modules.Meta.Client.Account;

namespace LiteGame.Modules.Meta.Client.Session
{
    /// <summary>联机业务端口实现（Meta 域自持）：登录事实/账户令牌面/流程面三组路由；root 稳定单例。</summary>
    public sealed class MetaSession : IMetaSession
    {
        /// <summary>战绩查询条数（服务端上限 100 裁剪）。</summary>
        public const int RecordsLimit = 20;

        private const string NotLoggedInCode = "entry.not-logged-in";

        private readonly IAccountDomain _domain;
        private readonly MetaLoginService _login;
        private readonly IRoomFlow _rooms;
        private readonly Func<cfg.Tables> _tables;

        public MetaSession(IAccountDomain domain, MetaLoginService login, IRoomFlow rooms,
            Func<cfg.Tables> tables = null)
        {
            _domain = domain ?? throw new ArgumentNullException(nameof(domain));
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
            _tables = tables;
        }

        public bool IsConfigured => _login.IsConfigured;
        public string AccountId => _domain.AccountId ?? string.Empty;
        public string ServerUrl => _login.ServerUrl ?? string.Empty;
        public string Version => _login.BuildHash ?? string.Empty;

        public async UniTask<MetaResult<bool>> GuestLoginAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<bool>.Fail("entry.not-configured");
            MetaCall<GuestLoginInfo> call = await _login.GuestLoginAsync();
            ct.ThrowIfCancellationRequested();
            if (!call.Ok) return MetaResult<bool>.Fail(Reason(call.Failure));
            _domain.Open(call.Value.AccountId, call.Value.AccessToken);
            return MetaResult<bool>.Success(true);
        }

        public async UniTask<MetaResult<IReadOnlyList<MetaRoomRow>>> ListRoomsAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<IReadOnlyList<MetaRoomRow>>.Fail("entry.not-configured");
            IMetaAccountSession account = RequireAccount();
            if (account == null) return MetaResult<IReadOnlyList<MetaRoomRow>>.Fail(NotLoggedInCode);
            MetaCall<IReadOnlyList<LobbyInstanceView>> call = await account.ListRoomsAsync();
            ct.ThrowIfCancellationRequested();
            if (!call.Ok) return MetaResult<IReadOnlyList<MetaRoomRow>>.Fail(Reason(call.Failure));

            var rows = new List<MetaRoomRow>();
            if (call.Value != null)
            {
                for (int i = 0; i < call.Value.Count; i++)
                {
                    LobbyInstanceView instance = call.Value[i];
                    if (instance.Rooms == null) continue;
                    for (int j = 0; j < instance.Rooms.Count; j++)
                    {
                        LobbyRoomEntry room = instance.Rooms[j];
                        rows.Add(new MetaRoomRow(room.RoomId, room.Players, room.ExpectedPlayers,
                            room.MapId, MapDisplayName(room.MapId)));
                    }
                }
            }
            return MetaResult<IReadOnlyList<MetaRoomRow>>.Success(rows);
        }

        public async UniTask<MetaResult<string>> QuickEnterAsync(CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<string>.Fail("entry.not-configured");
            if (RequireAccount() == null) return MetaResult<string>.Fail(NotLoggedInCode);
            MetaCall<JoinTicketInfo> call = await _rooms.EnterRoomAsync("", ct);
            return call.Ok ? MetaResult<string>.Success(call.Value.RoomId) : MetaResult<string>.Fail(Reason(call.Failure));
        }

        public async UniTask<MetaResult<string>> CreateRoomAsync(int mapId, int expectedPlayers,
            CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<string>.Fail("entry.not-configured");
            if (RequireAccount() == null) return MetaResult<string>.Fail(NotLoggedInCode);
            MetaCall<JoinTicketInfo> call = await _rooms.CreateRoomAsync(mapId, expectedPlayers, ct);
            return call.Ok ? MetaResult<string>.Success(call.Value.RoomId) : MetaResult<string>.Fail(Reason(call.Failure));
        }

        public async UniTask<MetaResult<string>> EnterRoomAsync(string roomId, CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<string>.Fail("entry.not-configured");
            if (RequireAccount() == null) return MetaResult<string>.Fail(NotLoggedInCode);
            MetaCall<JoinTicketInfo> call = await _rooms.EnterRoomAsync(roomId ?? string.Empty, ct);
            return call.Ok ? MetaResult<string>.Success(call.Value.RoomId) : MetaResult<string>.Fail(Reason(call.Failure));
        }

        public async UniTask<MetaResult<IReadOnlyList<MetaRecordRow>>> ListRecordsAsync(
            CancellationToken ct = default)
        {
            if (!IsConfigured) return MetaResult<IReadOnlyList<MetaRecordRow>>.Fail("entry.not-configured");
            IMetaAccountSession account = RequireAccount();
            if (account == null) return MetaResult<IReadOnlyList<MetaRecordRow>>.Fail(NotLoggedInCode);
            MetaCall<IReadOnlyList<MatchRecordInfo>> call = await account.ListRecordsAsync(RecordsLimit);
            ct.ThrowIfCancellationRequested();
            if (!call.Ok) return MetaResult<IReadOnlyList<MetaRecordRow>>.Fail(Reason(call.Failure));

            var rows = new List<MetaRecordRow>();
            if (call.Value != null)
            {
                for (int i = 0; i < call.Value.Count; i++)
                {
                    MatchRecordInfo record = call.Value[i];
                    rows.Add(new MetaRecordRow(record.MatchId, record.Kills, record.Deaths,
                        ((LiteSim.MatchEndReason)record.GameplayEndReason).ToString(),
                        FormatTime(record.FinishedAtMs)));
                }
            }
            return MetaResult<IReadOnlyList<MetaRecordRow>>.Success(rows);
        }

        public void LeaveMatch() => _rooms.LeaveMatch();

        private IMetaAccountSession RequireAccount() => _domain.IsOpen ? _domain.Account : null;

        private string MapDisplayName(int mapId)
        {
            if (mapId <= 0 || _tables == null) return string.Empty;
            cfg.map row = _tables().Tbmap?.GetOrDefault(mapId);
            return row?.Name ?? string.Empty;
        }

        private static string FormatTime(long finishedAtMs)
            => finishedAtMs <= 0
                ? string.Empty
                : DateTimeOffset.FromUnixTimeMilliseconds(finishedAtMs).ToLocalTime()
                    .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

        private static string Reason(MetaFailure failure)
        {
            if (failure == null) return "Internal";
            switch (failure.Kind)
            {
                case MetaFailureKind.Network: return "Network";
                case MetaFailureKind.InvalidResponse: return "InvalidResponse";
                default:
                    return string.IsNullOrEmpty(failure.Code) ? "HTTP" + failure.HttpStatus : failure.Code;
            }
        }
    }
}
