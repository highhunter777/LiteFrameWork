using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame.Modules.UI
{
    /// <summary>Meta 调用结果：失败给稳定 reason 文本（页面据此选文案键，不匹配原文）。</summary>
    public readonly struct MetaResult<T>
    {
        public readonly bool Ok;
        public readonly string Reason;
        public readonly T Value;

        private MetaResult(bool ok, T value, string reason)
        {
            Ok = ok;
            Value = value;
            Reason = reason;
        }

        public static MetaResult<T> Success(T value) => new MetaResult<T>(true, value, null);
        public static MetaResult<T> Fail(string reason) => new MetaResult<T>(false, default, reason ?? "Internal");
    }

    /// <summary>大厅房间行（页面列表投影）。</summary>
    public sealed class MetaRoomRow
    {
        public readonly string RoomId;
        public readonly int Players;
        public readonly int ExpectedPlayers;
        public readonly int MapId;
        public readonly string MapName;

        public MetaRoomRow(string roomId, int players, int expectedPlayers, int mapId, string mapName)
        {
            RoomId = roomId ?? string.Empty;
            Players = players;
            ExpectedPlayers = expectedPlayers;
            MapId = mapId;
            MapName = mapName ?? string.Empty;
        }
    }

    /// <summary>战绩行（最近 N 局；时间已按本地时区格式化）。</summary>
    public sealed class MetaRecordRow
    {
        public readonly string MatchId;
        public readonly int Kills;
        public readonly int Deaths;
        public readonly string Reason;
        public readonly string TimeText;

        public MetaRecordRow(string matchId, int kills, int deaths, string reason, string timeText)
        {
            MatchId = matchId ?? string.Empty;
            Kills = kills;
            Deaths = deaths;
            Reason = reason ?? string.Empty;
            TimeText = timeText ?? string.Empty;
        }
    }

    /// <summary>
    /// 联机业务端口（页面消费面）：登录/房间列表/进出房/战绩/离场。
    /// 回调 + 恰一次由 UniTask + CancellationToken 承接；实现住组合根（App 的 MetaSessionFacade，转当前账户会话）。
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
