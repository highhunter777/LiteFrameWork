using System;
using System.Text;
using MetaServer.Contracts.Auth;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 访问令牌线格式（<see cref="AccessTokenFormat"/>）L1：《Meta 服务专项设计》§6.1/§14 L1
    /// "票据/令牌的过期、篡改、重放、audience 不符、kid 未知与时钟回拨"矩阵——令牌侧半边。
    /// 签发/校验服务层（含 audience/purpose 判定与注入时钟）见 <see cref="AccessTokenServiceTests"/>。
    /// </summary>
    public sealed class AccessTokenFormatTests
    {
        private static readonly byte[] Key = NewKey();
        private static byte[] NewKey() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

        private static string ValidToken(byte[] key = null, string kid = "k1",
            string accountId = "a0011223344", string audience = "meta", string purpose = AccessTokenFormat.PurposeAccess,
            long iat = 1_000_000L, long exp = 2_000_000L, string jti = "0011223344556677889900aabbccddeeff")
        {
            var f = new AccessTokenFormat.Fields
            {
                Kid = kid, AccountId = accountId, Jti = jti, Audience = audience,
                Purpose = purpose, IssuedAtMs = iat, ExpiresAtMs = exp,
            };
            string prefix = AccessTokenFormat.BuildPrefix(kid, f);
            return AccessTokenFormat.AppendSignature(prefix, AccessTokenFormat.Sign(key ?? Key, prefix));
        }

        [Fact]
        public void 往返_拆段读声明与签发字段逐项一致()
        {
            string token = ValidToken();
            string[] parts = AccessTokenFormat.Split(token);
            Assert.NotNull(parts);
            Assert.Equal(AccessTokenFormat.Version, parts[0]);
            Assert.Equal("k1", AccessTokenFormat.KidOf(parts));
            AccessTokenFormat.Claims claims = AccessTokenFormat.ReadClaims(parts);
            Assert.NotNull(claims);
            Assert.Equal("a0011223344", claims.AccountId);
            Assert.Equal("0011223344556677889900aabbccddeeff", claims.Jti);
            Assert.Equal("meta", claims.Audience);
            Assert.Equal(AccessTokenFormat.PurposeAccess, claims.Purpose);
            Assert.Equal(1_000_000L, claims.IssuedAtMs);
            Assert.Equal(2_000_000L, claims.ExpiresAtMs);
        }

        [Fact]
        public void 签名覆盖全字段_任一段篡改均验签失败()
        {
            // 逐段篡改（除 sig 段——它是被换的对象；除 version——换版本直接 Malformed）：
            // kid/accountId/jti/audience/purpose/iat/exp 七个承载段各改一字节
            for (int i = 1; i <= 7; i++)
            {
                string token = ValidToken();
                string[] parts = token.Split('.');
                char c = parts[i][0];
                parts[i] = (c == 'x' ? 'y' : 'x') + parts[i].Substring(1);
                string tampered = string.Join(".", parts);
                string[] parsed = AccessTokenFormat.Split(tampered);
                Assert.NotNull(parsed);                                   // 形状仍合法
                Assert.False(AccessTokenFormat.VerifySignature(Key, parsed),
                    $"篡改段 {i} 后签名仍通过——签名覆盖面缺口");
            }
        }

        [Fact]
        public void 错误密钥验签失败_常量时间比较不抛()
        {
            string token = ValidToken();
            string[] parts = AccessTokenFormat.Split(token);
            Assert.True(AccessTokenFormat.VerifySignature(Key, parts));
            Assert.False(AccessTokenFormat.VerifySignature(NewKey(), parts));
        }

        [Fact]
        public void 段数或版本不符返回null_畸形输入不抛()
        {
            Assert.Null(AccessTokenFormat.Split(null));
            Assert.Null(AccessTokenFormat.Split(string.Empty));
            Assert.Null(AccessTokenFormat.Split("v1.a.b"));                        // 段数不足
            Assert.Null(AccessTokenFormat.Split(ValidToken() + ".x"));             // 段数超
            string[] versionBumped = ValidToken().Split('.');
            versionBumped[0] = "v2";
            Assert.Null(AccessTokenFormat.Split(string.Join(".", versionBumped))); // 版本不符
        }

        [Fact]
        public void 声明解码_非base64Url与宽松整数形式拒收()
        {
            // 自由文本段为非法 base64 长度（模 4 余 1）
            string token = ValidToken();
            string[] parts = token.Split('.');
            parts[2] = "ab!";                                                     // accountId 段
            Assert.Null(AccessTokenFormat.ReadClaims(parts));

            // iat 宽松形式（+1 / 前导空格 / 下划线分隔）——严格十进制拒收
            foreach (string bad in new[] { "+1000000", " 1000000", "1_000_000" })
            {
                string[] p = ValidToken().Split('.');
                p[6] = bad;
                Assert.True(AccessTokenFormat.ReadClaims(p) == null, $"宽松整数 {bad} 应被拒");
            }

            // 空 accountId / 空 jti → null（声明不完整不放行）
            string[] p2 = ValidToken().Split('.');
            p2[2] = string.Empty;
            Assert.Null(AccessTokenFormat.ReadClaims(p2));
        }

        [Fact]
        public void 编码_字段内不出现分隔符_往返保真()
        {
            // 自由文本含 '.' 与非 ASCII——编码后不可能出现 '.'，解码还原
            string nasty = "acct.with.dots.中文.émoji";
            string encoded = AccessTokenFormat.Encode(nasty);
            Assert.False(encoded.Contains('.'), "Base64Url 后字段内不得出现分隔符");
            Assert.Equal(nasty, AccessTokenFormat.Decode(encoded));
            Assert.Equal(string.Empty, AccessTokenFormat.Decode(string.Empty));
            Assert.Null(AccessTokenFormat.Decode(null));
        }
    }
}
