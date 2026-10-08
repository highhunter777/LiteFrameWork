using System;
using System.Security.Cryptography;
using MetaServer.Contracts.Auth;

namespace MetaServer.Modules.Auth
{
    /// <summary>
    /// 访问令牌服务（《Meta 服务专项设计》§6.1）：签发与校验的单点。
    ///
    /// - **签发**：绑定 accountId + jti + audience + purpose + iat/exp（时钟注入——**禁读系统时钟**，
    ///   L1 才造得出过期/临界用例；§6.1"过期判定以服务端时钟为准"）。
    /// - **校验（fail-closed，不抛）**：kid 未知 / 签名不符 / 过期 / audience 或 purpose 不符 →
    ///   对应拒收分类；任一项失败即拒绝，不降级为警告（§6.2 同款纪律）。
    /// - 密钥经装配注入（配置 base64 解码；≥32 字节由 MetaConfig 校验把守）。
    /// - **令牌与 jti 不写日志**（§11.1 禁止项——本类不持日志面，注释钉住约定）。
    ///
    /// 校验消费方（鉴权中间件）随 Lobby 首个受保护端点接入（§10.2"未实装不声明"——不预建空中间件）。
    /// </summary>
    public sealed class AccessTokenService
    {
        private readonly byte[] _secret;
        private readonly string _kid;
        private readonly string _audience;
        private readonly long _ttlMs;
        private readonly Func<long> _nowMs;

        /// <param name="signingSecret">HMAC 密钥（≥32 字节；装配层校验后传入——本类不再兜底判空长度）。</param>
        /// <param name="keyId">密钥标识（轮换期间新旧 kid 并存——§6.3 同款语义）。</param>
        /// <param name="audience">固定受众（Meta 自验，装配根定值）。</param>
        /// <param name="ttlSeconds">有效期（秒，60..604800——配置校验把守）。</param>
        /// <param name="nowMs">单调毫秒时钟（服务端权威；测试注入虚拟时钟）。</param>
        public AccessTokenService(byte[] signingSecret, string keyId, string audience,
            int ttlSeconds, Func<long> nowMs)
        {
            _secret = signingSecret ?? throw new ArgumentNullException(nameof(signingSecret));
            _kid = keyId ?? throw new ArgumentNullException(nameof(keyId));
            _audience = audience ?? throw new ArgumentNullException(nameof(audience));
            if (ttlSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(ttlSeconds));
            _ttlMs = ttlSeconds * 1000L;
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        }

        /// <summary>签发访问令牌（jti = CSPRNG 16 字节——未来吊销面锚点，§6.1）。
        /// <paramref name="expiresAtMs"/> 与令牌内 exp 同刻产出（调用方响应载荷与令牌同源）。</summary>
        public string Issue(string accountId, out long expiresAtMs)
        {
            if (string.IsNullOrEmpty(accountId))
                throw new ArgumentException("accountId 不得为空", nameof(accountId));

            long now = _nowMs();
            var f = new AccessTokenFormat.Fields
            {
                Kid = _kid,
                AccountId = accountId,
                Jti = NewJti(),
                Audience = _audience,
                Purpose = AccessTokenFormat.PurposeAccess,
                IssuedAtMs = now,
                ExpiresAtMs = now + _ttlMs,
            };
            expiresAtMs = f.ExpiresAtMs;
            string prefix = AccessTokenFormat.BuildPrefix(_kid, f);
            return AccessTokenFormat.AppendSignature(prefix, AccessTokenFormat.Sign(_secret, prefix));
        }

        /// <summary>
        /// 校验（fail-closed，不抛）：kid → 签名（常量时间）→ 声明 → 时效 → audience → purpose。
        /// 通过返回声明；任一失败返回拒收分类（<paramref name="claims"/> 为 null）。
        /// </summary>
        public AccessTokenFormat.Rejection Validate(string token, out AccessTokenFormat.Claims claims)
        {
            claims = null;
            try
            {
                if (string.IsNullOrEmpty(token)) return AccessTokenFormat.Rejection.Missing;

                string[] parts = AccessTokenFormat.Split(token);
                if (parts == null) return AccessTokenFormat.Rejection.Malformed;

                if (!string.Equals(AccessTokenFormat.KidOf(parts), _kid, StringComparison.Ordinal))
                    return AccessTokenFormat.Rejection.UnknownKey;      // 未知/换 kid：拒绝，不遍历兜底

                if (!AccessTokenFormat.VerifySignature(_secret, parts))
                    return AccessTokenFormat.Rejection.BadSignature;

                AccessTokenFormat.Claims parsed = AccessTokenFormat.ReadClaims(parts);
                if (parsed == null) return AccessTokenFormat.Rejection.Malformed;

                if (parsed.ExpiresAtMs <= parsed.IssuedAtMs)
                    return AccessTokenFormat.Rejection.Malformed;         // 空/负有效期：先于时效判定
                if (_nowMs() >= parsed.ExpiresAtMs)
                    return AccessTokenFormat.Rejection.Expired;

                if (!string.Equals(parsed.Audience, _audience, StringComparison.Ordinal))
                    return AccessTokenFormat.Rejection.AudienceMismatch;
                if (!string.Equals(parsed.Purpose, AccessTokenFormat.PurposeAccess, StringComparison.Ordinal))
                    return AccessTokenFormat.Rejection.PurposeMismatch;  // 混用刷新令牌等其它用途位

                claims = parsed;
                return AccessTokenFormat.Rejection.None;
            }
            catch (Exception)
            {
                // fail-closed：任何未预期异常都归 Malformed，不放行（票据侧同款纪律）
                claims = null;
                return AccessTokenFormat.Rejection.Malformed;
            }
        }

        private static string NewJti()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(16);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
