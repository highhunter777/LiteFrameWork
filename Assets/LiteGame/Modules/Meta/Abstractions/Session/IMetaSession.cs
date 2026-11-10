using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>
    /// 联机业务端口（页面消费面）：登录/房间列表/进出房/战绩/离场。
    /// 回调 + 恰一次由 UniTask + CancellationToken 承接；实现住 Meta 域（MetaSession）。
    /// </summary>
    public interface IMetaSession
    {
        bool IsConfigured { get; }
        string AccountId { get; }
        string ServerUrl { get; }
        string Version { get; }

        UniTask<MetaResult<bool>> GuestLoginAsync(CancellationToken ct = default);
        UniTask<MetaResult<IReadOnlyList<MetaRoomRow>>> ListRoomsAsync(CancellationToken ct = default);
        UniTask<MetaResult<string>> QuickEnterAsync(CancellationToken ct = default);
        UniTask<MetaResult<string>> CreateRoomAsync(int mapId, int expectedPlayers, CancellationToken ct = default);
        UniTask<MetaResult<string>> EnterRoomAsync(string roomId, CancellationToken ct = default);
        UniTask<MetaResult<IReadOnlyList<MetaRecordRow>>> ListRecordsAsync(CancellationToken ct = default);
        void LeaveMatch();
    }
}
