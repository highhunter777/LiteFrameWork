using System;

namespace LiteNet
{
    /// <summary>
    /// 房间会话事实（RoomClient 的行内件，M2 拆分）：席位/房主/地图/期望人数、StartGame 载荷缓存、
    /// 席位实时面与终局结果的**唯一持有者**。只做"协议事实进、只读事实出"，不碰传输/事件派发；
    /// 读写侧 Clone 防御（<see cref="MatchResult"/>/<see cref="Seats"/>）在此收口。
    /// </summary>
    internal sealed class RoomSession
    {
        private Proto.MatchEnded _matchResult;
        private Proto.RoomSeats _seats;
        private string _activeMatchId;

        /// <summary>本机席位（JoinAck 定值；-1 = 未入房）。</summary>
        public int PlayerId { get; private set; } = -1;

        /// <summary>本机是否房主（JoinAck 初值 + OwnerChanged 事实重算）。</summary>
        public bool IsOwner { get; private set; }

        /// <summary>本房地图行键（0 = 未标注）。</summary>
        public int MapId { get; private set; }

        /// <summary>本房期望人数（0 = 未标注）。</summary>
        public int ExpectedPlayers { get; private set; }

        /// <summary>最近一次 StartGame 载荷（晚订阅者补发用；未到达 = null）。</summary>
        public Proto.StartGame StartGame { get; private set; }

        /// <summary>最近一次终局结果（Clone 防御；未到达 = null）。</summary>
        public Proto.MatchEnded MatchResult => _matchResult?.Clone();

        /// <summary>席位实时面最新快照（Clone 防御；未收到前为 null）。</summary>
        public Proto.RoomSeats Seats => _seats?.Clone();

        /// <summary>本局活动对局号（StartGame 定值；空 = 未开局）。</summary>
        public string ActiveMatchId => _activeMatchId;

        /// <summary>是否已有终局结果。</summary>
        public bool HasMatchResult => _matchResult != null;

        /// <summary>新一次 Join：清本局结果与开局载荷。</summary>
        public void ResetOnJoin()
        {
            _matchResult = null;
            _activeMatchId = null;
            StartGame = null;
        }

        /// <summary>JoinAck：席位/房主/地图/期望人数。</summary>
        public void ApplyJoinAck(Proto.JoinAck ack)
        {
            PlayerId = ack.PlayerId;
            IsOwner = ack.IsOwner;
            MapId = ack.MapId;
            ExpectedPlayers = ack.ExpectedPlayers;
        }

        /// <summary>StartGame：缓存载荷与活动对局号（为空时回落进房房间号）。</summary>
        public void ApplyStartGame(Proto.StartGame start, string fallbackMatchId)
        {
            StartGame = start;
            _activeMatchId = string.IsNullOrEmpty(start.MatchId) ? fallbackMatchId : start.MatchId;
        }

        /// <summary>房主迁移：按协议事实重算（迁移后旧主失位/新主到位）。</summary>
        public void ApplyOwnerChanged(long ownerPlayerId) => IsOwner = ownerPlayerId == PlayerId;

        /// <summary>席位实时面：入缓 Clone（读侧再 Clone——同 MatchResult 防御）。</summary>
        public void ApplySeats(Proto.RoomSeats seats) => _seats = seats.Clone();

        /// <summary>
        /// 接受终局（Reliable 且本局匹配）：未开局（StartGame 未达）也接受——等待期房间被服务器中止
        /// （等待超时/运维关闭/排空）时经 MatchEnded 通知散场；此时判据退为"本次进房已有应答"
        /// （JoinAck 已回且非新一次 Join 的未决窗口）。返回 false = 丢弃（旧局/重复/未决窗口）。
        /// </summary>
        public bool TryAcceptMatchEnded(Proto.MatchEnded ended, bool hasStartGame, bool joinAttemptPending)
        {
            if (_matchResult != null) return false;
            if (hasStartGame ? ended.MatchId != _activeMatchId : (joinAttemptPending || PlayerId < 0)) return false;
            _matchResult = ended;
            return true;
        }
    }
}
