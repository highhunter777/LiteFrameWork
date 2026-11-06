using LiteSync.Input;
using LiteSync.Shared;
using LiteSync.Snapshot;
using LiteSim;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 宿主信令的 proto 单源（信号/输出 → <c>JoinAck</c>/<c>StartGame</c>/<c>MatchEnded</c>）： 真服宿主（<c>ServerHost.ApplySignal/Appl。）。
    /// </summary>
    public static class HandshakePacker
    {
        /// <summary>席位就位 → JoinAck（Members 为当前全员快照；SnapshotHz 是房级参数，TickRate 是两端共读的协议常量）。</summary>
        public static Proto.JoinAck BuildJoinAck(PlayerAdmitted admitted, string reconnectToken,
            int reconnectWindowSeconds, int mapId, int expectedPlayers, int snapshotHz)
        {
            if (snapshotHz < ProtocolConstants.MinSnapshotHz || snapshotHz > ProtocolConstants.MaxSnapshotHz)
                throw new System.ArgumentOutOfRangeException(nameof(snapshotHz), snapshotHz,
                    $"快照频率越界（允许 {ProtocolConstants.MinSnapshotHz}..{ProtocolConstants.MaxSnapshotHz}）");
            var ack = new Proto.JoinAck
            {
                PlayerId = admitted.PlayerId,
                ReconnectToken = reconnectToken,
                ReconnectWindowSeconds = reconnectWindowSeconds,
                SnapshotHz = snapshotHz,
                TickRate = SimConfig.TickRate,
                IsOwner = admitted.IsOwner,           // 开房裁决：开房者=首入座者——UI"开始对局"按钮归属
                MapId = mapId,                        // 多图批：本房地图行键（0 = 未标注）
                ExpectedPlayers = expectedPlayers,    // 开房参数批：房规模权威值（客户端预测定容/房间页分母）
            };
            ack.Members.Add(admitted.Members);
            return ack;
        }

        /// <summary>对局开始 → StartGame（全字段源自信号——两形态零差异面；MatchId 空值归一为空串）。</summary>
        public static Proto.StartGame BuildStartGame(MatchStarted started)
        {
            return new Proto.StartGame
            {
                Seed = started.Seed,
                ConfigHash = started.ConfigHash,
                Frame = started.Frame,
                MatchId = started.MatchId ?? "",
                Match = SnapshotCodec.PackMatch(started.MatchState),
            };
        }

        /// <summary>
        /// 结算就绪 → <c>MatchEnded</c>（**带结果**——对局中收尾的唯一终局信令）： 真服（<c>ServerHost.SendMatchResult</c>）与本地服（<c>LocalServerTrans。）。
        /// </summary>
        public static Proto.MatchEnded BuildMatchEnded(MatchResultSummary summary)
        {
            var ended = new Proto.MatchEnded
            {
                MatchId = summary.MatchId ?? "",
                FinalFrame = summary.FinalFrame,
                EndReason = (int)summary.EndReason,
                GameplayEndReason = (int)summary.GameplayEndReason,
                WinnerEntityId = summary.WinnerEntityId,
            };
            foreach (PlayerMatchResult result in summary.Players)
                ended.Players.Add(new Proto.PlayerMatchResult
                {
                    PlayerId = result.PlayerId,
                    EntityId = result.EntityId,
                    Kills = result.Kills,
                    Deaths = result.Deaths,
                });
            return ended;
        }

        /// <summary>
        /// 终态中止 → <c>MatchEnded</c>（**无结果可结算**——Players 空、EndReason 随因、玩法终因恒 None）： 等待期/未开局收尾（等待超时/运维关闭/排空）的散场信令。
        /// </summary>
        public static Proto.MatchEnded BuildAbortedMatchEnded(RoomAbortedOutput aborted)
        {
            return new Proto.MatchEnded
            {
                MatchId = aborted.MatchId ?? "",
                FinalFrame = aborted.Frame,
                EndReason = (int)aborted.Reason,
                GameplayEndReason = (int)MatchEndReason.None,
                WinnerEntityId = 0L,
            };
        }
    }
}
