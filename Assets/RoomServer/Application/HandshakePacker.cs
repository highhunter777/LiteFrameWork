using LiteNet.Protocol;
using LiteSim;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 进房/开局回执的 proto 单源（信号 → <c>JoinAck</c>/<c>StartGame</c>）：
    /// 真服宿主（<c>ServerHost.ApplySignal</c>）与离线本地服（<c>LocalServerTransport</c>）共用本件——
    /// 回执字段面此前两处手抄，已真实漂移过一族（本地服缺 <c>IsOwner</c>/<c>ExpectedPlayers</c>/
    /// <c>MatchId</c>/<c>Match</c>，其中 ExpectedPlayers 是客户端定容与房间页等待人数的权威值）。
    /// 与输入面单源（<c>InputPacker</c>）对称：字段面收进一份后，此类漂移结构性消除。
    ///
    /// **凭据类差异经参数注入**（不进本件）：真服的重连票据由 ReconnectService 签发、窗口 = 票据 TTL；
    /// 本地服无重连语义，用占位串与 0 秒窗口。房事实（mapId/expectedPlayers）同样由调用方给——
    /// 两者都是"宿主知道的房间身份"，不是信号自身携带的字段。
    /// </summary>
    public static class HandshakePacker
    {
        /// <summary>席位就位 → JoinAck（Members 为当前全员快照；SnapshotHz/TickRate 是两端共读的协议常量）。</summary>
        public static Proto.JoinAck BuildJoinAck(PlayerAdmitted admitted, string reconnectToken,
            int reconnectWindowSeconds, int mapId, int expectedPlayers)
        {
            var ack = new Proto.JoinAck
            {
                PlayerId = admitted.PlayerId,
                ReconnectToken = reconnectToken,
                ReconnectWindowSeconds = reconnectWindowSeconds,
                SnapshotHz = SimConfig.SnapshotHz,
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
    }
}
