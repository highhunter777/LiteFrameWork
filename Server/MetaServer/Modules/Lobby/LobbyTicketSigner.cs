using System;
using System.Security.Cryptography;
using RoomServer.Application;

namespace MetaServer.Modules.Lobby
{
    /// <summary>
    /// Join Ticket 签发端（《Meta 服务专项设计》§6.2/§7"Join Ticket 在此签发"。）。
    /// </summary>
    public sealed class LobbyTicketSigner
    {
        private readonly string _kid;
        private readonly byte[] _secret;
        private readonly string _audience;

        /// <param name="kid">密钥标识（须与房间端 <c>--ticket-key &lt;kid&gt;:&lt;base64&gt;</c> 的 kid 一致）。</param>
        /// <param name="secret">HMAC 密钥（装配注入；≥32 字节由 MetaConfig 校验把守）。</param>
        /// <param name="audience">票据受众（须与房间端 <c>--audience</c> 一致；空 = 房间端不校验）。</param>
        public LobbyTicketSigner(string kid, byte[] secret, string audience)
        {
            _kid = kid ?? throw new ArgumentNullException(nameof(kid));
            _secret = secret ?? throw new ArgumentNullException(nameof(secret));
            _audience = audience ?? string.Empty;
        }

        /// <summary>
        /// 签发票据（§6.2 绑定项：playerId/accountId/roomId/过期时刻/nonce/buildHash/audience/mapId）。
        /// </summary>
        /// <param name="mapId">房间创建意图里的地图行键（多图批；0 = 未指定）。**只在签名覆盖内传递，
        /// <param name="expectedPlayers">房间创建意图里的期望人数（开房参数批；0 = 未指定＝按房间模板）。
        public string Issue(string playerId, string accountId, string roomId, string buildHash, int mapId,
            int expectedPlayers, string template, long nowMs, long ttlMs, out long expiresAtMs)
        {
            if (mapId < 0) throw new ArgumentOutOfRangeException(nameof(mapId), mapId, "地图行键不可为负（0 = 未指定）");
            if (expectedPlayers < 0) throw new ArgumentOutOfRangeException(nameof(expectedPlayers), expectedPlayers,
                "期望人数不可为负（0 = 未指定）");
            expiresAtMs = nowMs + ttlMs;
            var fields = new JoinTicketFormat.Fields
            {
                Kid = _kid,
                PlayerId = playerId,
                AccountId = accountId,
                RoomId = roomId,
                MatchId = string.Empty,
                Audience = _audience,
                BuildHash = buildHash,
                SimVersion = string.Empty,
                Nonce = NewNonce(),
                ExpiresAtMs = expiresAtMs,
                NotBeforeMs = 0,
                MapId = mapId,
                ExpectedPlayers = expectedPlayers,
                Template = template ?? string.Empty,
            };
            string prefix = JoinTicketFormat.BuildPrefix(_kid, fields);
            return JoinTicketFormat.AppendSignature(prefix, JoinTicketFormat.Sign(_secret, prefix));
        }

        private static string NewNonce()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }
    }
}
