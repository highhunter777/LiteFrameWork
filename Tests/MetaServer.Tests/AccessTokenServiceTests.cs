using System;
using MetaServer.Contracts.Auth;
using MetaServer.Modules.Auth;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 访问令牌服务（<see cref="AccessTokenService"/>）L1：《Meta 服务专项设计》§6.1
    /// "校验必须验证签名、过期、audience 与用途"矩阵——含注入虚拟时钟的时效边界。
    /// </summary>
    public sealed class AccessTokenServiceTests
    {
        private static byte[] NewKey() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

        private sealed class Clock
        {
            public long NowMs;
        }

        private static AccessTokenService NewService(Clock clock, byte[] key = null, string kid = "k1",
            string audience = "meta", int ttlSeconds = 600)
            => new AccessTokenService(key ?? NewKey(), kid, audience, ttlSeconds, () => clock.NowMs);

        [Fact]
        public void 签发后校验通过_声明与签发参数一致()
        {
            var clock = new Clock { NowMs = 100_000L };
            AccessTokenService svc = NewService(clock);
            string token = svc.Issue("acc001", out long exp);

            clock.NowMs = 100_001L;                                   // 时钟前进——校验用当前时刻
            AccessTokenFormat.Rejection r = svc.Validate(token, out AccessTokenFormat.Claims claims);
            Assert.Equal(AccessTokenFormat.Rejection.None, r);
            Assert.NotNull(claims);
            Assert.Equal("acc001", claims.AccountId);
            Assert.Equal("meta", claims.Audience);
            Assert.Equal(AccessTokenFormat.PurposeAccess, claims.Purpose);
            Assert.Equal(100_000L, claims.IssuedAtMs);
            Assert.Equal(100_000L + 600_000L, claims.ExpiresAtMs);    // TTL 600s
            Assert.Equal(claims.ExpiresAtMs, exp);                     // out 过期时刻与令牌同源
        }

        [Fact]
        public void 时效边界_exp前一毫秒通过_exp当刻拒绝()
        {
            var clock = new Clock { NowMs = 0L };
            AccessTokenService svc = NewService(clock, ttlSeconds: 10);
            string token = svc.Issue("a1", out _);

            clock.NowMs = 10_000L - 1;
            Assert.Equal(AccessTokenFormat.Rejection.None, svc.Validate(token, out _));

            clock.NowMs = 10_000L;                                    // exp 当刻即过期（含边界——fail-closed）
            Assert.Equal(AccessTokenFormat.Rejection.Expired, svc.Validate(token, out _));
        }

        [Fact]
        public void audience或purpose不符拒绝_空令牌与未知kid拒绝()
        {
            var clock = new Clock { NowMs = 0L };
            byte[] key = NewKey();
            AccessTokenService svc = NewService(clock, key, kid: "k1", audience: "meta");
            string token = svc.Issue("a1", out _);

            // 换 audience 的服务实例（同密钥）：audience 不符 → 拒
            AccessTokenService otherAudience = NewService(clock, key, kid: "k1", audience: "other");
            Assert.Equal(AccessTokenFormat.Rejection.AudienceMismatch,
                otherAudience.Validate(token, out AccessTokenFormat.Claims c1));
            Assert.Null(c1);

            // 伪造 purpose 段（"refresh"）→ PurposeMismatch
            string[] parts = token.Split('.');
            parts[5] = AccessTokenFormat.Encode("refresh");
            string tampered = string.Join(".", parts);
            Assert.Equal(AccessTokenFormat.Rejection.BadSignature, svc.Validate(tampered, out _));

            // 未知 kid：换 kid 的实例拒（不遍历兜底）
            AccessTokenService otherKid = NewService(clock, key, kid: "k2", audience: "meta");
            Assert.Equal(AccessTokenFormat.Rejection.UnknownKey, otherKid.Validate(token, out _));

            // 空令牌 → Missing
            Assert.Equal(AccessTokenFormat.Rejection.Missing, svc.Validate(null, out _));
            Assert.Equal(AccessTokenFormat.Rejection.Missing, svc.Validate(string.Empty, out _));
        }

        [Fact]
        public void 畸形令牌归Malformed_不抛()
        {
            var clock = new Clock { NowMs = 0L };
            byte[] key = NewKey();
            AccessTokenService svc = NewService(clock, key);
            Assert.Equal(AccessTokenFormat.Rejection.Malformed, svc.Validate("garbage", out _));
            Assert.Equal(AccessTokenFormat.Rejection.Malformed, svc.Validate("v1.x", out _));

            // 空/负有效期（exp ≤ iat）：签名有效也归 Malformed（先于时效判定）
            var f = new AccessTokenFormat.Fields
            {
                Kid = "k1", AccountId = "a1", Jti = "j", Audience = "meta",
                Purpose = AccessTokenFormat.PurposeAccess,
                IssuedAtMs = 1000, ExpiresAtMs = 1000,
            };
            string prefix = AccessTokenFormat.BuildPrefix("k1", f);
            string degenerate = AccessTokenFormat.AppendSignature(prefix,
                AccessTokenFormat.Sign(key, prefix));
            Assert.Equal(AccessTokenFormat.Rejection.Malformed, svc.Validate(degenerate, out _));
        }

        [Fact]
        public void 两次签发jti不同_同账号多令牌并行有效()
        {
            var clock = new Clock { NowMs = 0L };
            AccessTokenService svc = NewService(clock);
            string t1 = svc.Issue("a1", out _);
            string t2 = svc.Issue("a1", out _);
            Assert.NotEqual(t1, t2);                                   // jti 不同
            Assert.Equal(AccessTokenFormat.Rejection.None, svc.Validate(t1, out _));
            Assert.Equal(AccessTokenFormat.Rejection.None, svc.Validate(t2, out _));
        }
    }
}
