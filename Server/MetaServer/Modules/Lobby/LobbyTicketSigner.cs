using System;
using System.Security.Cryptography;
using RoomServer.Application;

namespace MetaServer.Modules.Lobby
{
    /// <summary>
    /// Join Ticket 签发端（《Meta 服务专项设计》§6.2/§7"Join Ticket 在此签发"；
    /// 《上云测试专项设计》§2"Meta 以 <c>JoinTicketFormat.BuildPrefix/AppendSignature</c> 签发，
    /// 房间服按同格式同 key 验签，物理上不会漂"）。
    ///
    /// - **形状单源**：组装/签名走 <see cref="JoinTicketFormat"/>——该文件以源链接共编进本工程
    ///   （见 MetaServer.csproj），与房间端验证器是**同一份**形状规则。**不得**在本工程另写拼串。
    /// - **HMAC-SHA256 共享密钥**：框架期 Meta 与 RoomServer 同信任域（房间端
    ///   <c>HmacJoinTicketValidator</c> 的选型注释同口径）；换非对称只替换两端本类/验证器。
    /// - **令牌与票据不写日志**（§11.1 禁止项——本类不持日志面，注释钉住约定）。
    /// - **MatchId/SimVersion 置空**：对局由房间创建时自带独立 MatchId（此字段房间端不过滤）；
    ///   Sim 版本分离未启用（房间端 JoinContext.Version 为空 = 不校验）——不为"占位"编造值。
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
        /// 签发票据（§6.2 绑定项：playerId/accountId/roomId/过期时刻/nonce/buildHash/audience）。
        /// nonce = CSPRNG 128 bit（房间端一次性消费窗口按它去重——重放必拒）。
        /// </summary>
        public string Issue(string playerId, string accountId, string roomId, string buildHash,
            long nowMs, long ttlMs, out long expiresAtMs)
        {
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
