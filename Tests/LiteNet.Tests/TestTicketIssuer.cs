using System;
using System.Security.Cryptography;
using RoomServer.Application;

namespace LiteNet.Tests
{
    /// <summary>
    /// 隔离测试签发者（《框架先行》§6"身份签发可用隔离测试发行者，但真实网络链应经过与运行时
    /// **一致的验证器**"；Meta 专项 §6.2"由 Meta 私钥签发，RoomServer 用本地公钥验签"）。
    ///
    /// **本类只演 Meta 侧签发**，不复制线格式——组装与签名都走
    /// <see cref="JoinTicketFormat"/>（与验证器同一份形状规则）。若另写一份拼串，
    /// 签发端漂移时验证端仍然绿——那正是"测试测的不是它声称测的东西"。
    /// </summary>
    internal sealed class TestTicketIssuer
    {
        private readonly string _kid;
        private readonly byte[] _secret;

        public TestTicketIssuer(string kid, byte[] secret)
        {
            _kid = kid;
            _secret = secret;
        }

        /// <summary>与 <see cref="JoinTicketKey"/> 同源的随机密钥（测试用；生产由部署注入）。</summary>
        public static TestTicketIssuer Random(string kid)
        {
            var secret = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
            return new TestTicketIssuer(kid, secret);
        }

        public JoinTicketKey AsValidatorKey()
        {
            return new JoinTicketKey(_kid, _secret);
        }

        /// <summary>
        /// 签发票据。<paramref name="nonce"/> 缺省每次随机——**同一票据只应成功一次**
        /// （重放用例需要固定 nonce 时显式传入）。
        /// </summary>
        public string Issue(string playerId, string roomId, string buildHash,
            long nowMs, long ttlMs = 60_000, long notBeforeMs = 0,
            string accountId = null, string matchId = "m1", string audience = "test",
            string simVersion = "1", string nonce = null, string kidOverride = null)
        {
            var f = new JoinTicketFormat.Fields
            {
                Kid = _kid,
                PlayerId = playerId,
                AccountId = accountId ?? playerId,
                RoomId = roomId,
                MatchId = matchId,
                Audience = audience,
                BuildHash = buildHash,
                SimVersion = simVersion,
                Nonce = nonce ?? Guid.NewGuid().ToString("N"),
                ExpiresAtMs = nowMs + ttlMs,
                NotBeforeMs = notBeforeMs,
            };
            string prefix = JoinTicketFormat.BuildPrefix(kidOverride ?? _kid, f);
            return JoinTicketFormat.AppendSignature(prefix, JoinTicketFormat.Sign(_secret, prefix));
        }

        /// <summary>签发后在**已签名票据**上改一个字段（篡改用例：签名与内容不再匹配）。</summary>
        public string IssueThenTamper(string playerId, string roomId, string buildHash, long nowMs,
            int partIndex, string newPartValue)
        {
            string ticket = Issue(playerId, roomId, buildHash, nowMs);
            string[] parts = ticket.Split('.');
            parts[partIndex] = newPartValue;
            return string.Join(".", parts);
        }
    }
}
