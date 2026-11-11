using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MetaWire;
using LiteGame.Modules.Meta.Abstractions.Room;

namespace LiteGame.Modules.Meta.Abstractions.Account
{
    /// <summary>每登录账户的 Meta 会话（令牌面）：房间/取票/建房/战绩；不暴露令牌。</summary>
    public interface IMetaAccountSession
    {
        string AccountId { get; }
        bool IsLoggedIn { get; }

        UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync();
        UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string roomId, CancellationToken ct = default);
        UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId = 0, int expectedPlayers = 0,
            CancellationToken ct = default);
        UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId, int expectedPlayers,
            string template, string gmKey, CancellationToken ct = default);
        UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecordsAsync(int limit);
        void Logout(string reason);
    }
}
