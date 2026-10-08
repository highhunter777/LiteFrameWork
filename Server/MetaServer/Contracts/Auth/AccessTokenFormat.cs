using System;
using System.Security.Cryptography;
using System.Text;

namespace MetaServer.Contracts.Auth
{
    /// <summary>
    /// 访问令牌的**线格式**（《Meta 服务专项设计》§6.1）。
    ///
    /// **单一来源**：签发（<c>AccessTokenService</c>）与校验（未来的鉴权中间件）都必须走本类，
    /// 形状规则只有这一份（与 RoomServer 侧 <c>JoinTicketFormat</c> 同一条纪律——两个契约各自单源，
    /// 互不引用：访问令牌是 Meta 自签自验的局内契约，Join Ticket 是 Meta→RoomServer 的跨服务接缝）。
    ///
    /// 形状：<c>v1.kid.accountId.jti.audience.purpose.iatMs.expMs.sig</c> 共 9 段。
    /// 自由文本字段走 Base64Url（去 padding、<c>+</c>→<c>-</c>、<c>/</c>→<c>_</c>）——
    /// 故字段内不可能出现分隔符 <c>.</c>；整数（iatMs/expMs）明文十进制（严格形式，拒绝 "+1"/" 1"）。
    /// 签名覆盖**除第 8 段（sig）外的全部 8 段**；HMAC-SHA256（不自创密码算法）。
    ///
    /// **对称密钥选型**：访问令牌由 Meta **自签自验**（签发方=唯一校验方，同信任域），
    /// HMAC-SHA256 满足"签名 + 过期 + audience + 用途"四项校验且密钥不出进程；
    /// RSA 非对称的价值在"签发方与校验方分离"（Join Ticket/内容签名——§6.3），令牌场景不成立。
    ///
    /// **令牌不进日志**（§11.1 禁止项）；jti 是未来吊销面的锚点（§6.1"吊销以 jti/版本号在服务端可判定"），
    /// 同样不单独写日志。
    /// </summary>
    public static class AccessTokenFormat
    {
        /// <summary>格式版本 = 令牌版本（§6.1）。不匹配即拒——旧版本不静默接受。</summary>
        public const string Version = "v1";

        /// <summary>段数（含 sig）。</summary>
        public const int PartCount = 9;

        /// <summary>访问令牌的固定用途位（§6.1"校验必须验证用途"——与未来刷新令牌区分）。</summary>
        public const string PurposeAccess = "access";

        private const int IdxVersion = 0;
        private const int IdxKid = 1;
        private const int IdxAccountId = 2;
        private const int IdxJti = 3;
        private const int IdxAudience = 4;
        private const int IdxPurpose = 5;
        private const int IdxIssuedAtMs = 6;
        private const int IdxExpiresAtMs = 7;
        private const int IdxSignature = 8;

        /// <summary>拒收分类（fail-closed；不抛异常——调用方按分类返回稳定错误码）。</summary>
        public enum Rejection : byte
        {
            None = 0,
            Missing = 1,
            Malformed = 2,
            UnknownKey = 3,
            BadSignature = 4,
            Expired = 5,
            AudienceMismatch = 6,
            PurposeMismatch = 7,
        }

        /// <summary>签发端组装的令牌字段（不含签名）。</summary>
        public sealed class Fields
        {
            public string Kid;
            public string AccountId;
            public string Jti;
            public string Audience;
            public string Purpose;
            public long IssuedAtMs;
            public long ExpiresAtMs;
        }

        /// <summary>验签通过的声明（调用方据此做绑定项比对与业务放行）。</summary>
        public sealed class Claims
        {
            public string AccountId;
            public string Jti;
            public string Audience;
            public string Purpose;
            public long IssuedAtMs;
            public long ExpiresAtMs;
        }

        /// <summary>组装被签名覆盖的前缀（前 8 段）。签发与验签**必须**共用它。</summary>
        public static string BuildPrefix(string kid, Fields f)
        {
            var sb = new StringBuilder(128);
            sb.Append(Version).Append('.')
              .Append(kid).Append('.')
              .Append(Encode(f.AccountId)).Append('.')
              .Append(Encode(f.Jti)).Append('.')
              .Append(Encode(f.Audience)).Append('.')
              .Append(Encode(f.Purpose)).Append('.')
              .Append(f.IssuedAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('.')
              .Append(f.ExpiresAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>续上签名段，得到完整令牌。</summary>
        public static string AppendSignature(string prefix, byte[] signature)
        {
            return prefix + "." + Convert.ToBase64String(signature);
        }

        /// <summary>HMAC-SHA256 签名（不自创密码算法；密钥 ≥32 字节由服务装配校验）。</summary>
        public static byte[] Sign(byte[] secret, string prefix)
        {
            using (var hmac = new HMACSHA256(secret))
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(prefix));
        }

        /// <summary>拆段；段数或版本不符返回 null（调用方转 Malformed，不抛）。</summary>
        public static string[] Split(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            string[] parts = token.Split('.');
            if (parts.Length != PartCount) return null;
            if (!string.Equals(parts[IdxVersion], Version, StringComparison.Ordinal)) return null;
            return parts;
        }

        /// <summary>常量时间验签（防时序侧信道）；签名非法 base64 直接 false（不抛）。</summary>
        public static bool VerifySignature(byte[] secret, string[] parts)
        {
            byte[] provided;
            try
            {
                provided = Convert.FromBase64String(parts[IdxSignature]);
            }
            catch (FormatException)
            {
                return false;
            }
            byte[] expected = Sign(secret, PrefixOf(parts));
            return CryptographicOperations.FixedTimeEquals(expected, provided);
        }

        /// <summary>被签名覆盖的前缀（从已拆分段重建——保证验签算的是同一串字节）。</summary>
        public static string PrefixOf(string[] parts)
        {
            var sb = new StringBuilder(128);
            for (int i = 0; i < PartCount - 1; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        /// <summary>解出声明；任一字段解码失败返回 null（调用方转 Malformed）。</summary>
        public static Claims ReadClaims(string[] parts)
        {
            string accountId = Decode(parts[IdxAccountId]);
            string jti = Decode(parts[IdxJti]);
            string audience = Decode(parts[IdxAudience]);
            string purpose = Decode(parts[IdxPurpose]);
            if (accountId == null || jti == null || audience == null || purpose == null) return null;
            if (!TryParseLong(parts[IdxIssuedAtMs], out long issuedAtMs)) return null;
            if (!TryParseLong(parts[IdxExpiresAtMs], out long expiresAtMs)) return null;
            if (accountId.Length == 0 || jti.Length == 0) return null;
            return new Claims
            {
                AccountId = accountId,
                Jti = jti,
                Audience = audience,
                Purpose = purpose,
                IssuedAtMs = issuedAtMs,
                ExpiresAtMs = expiresAtMs,
            };
        }

        /// <summary>kid 段（验签方按 kid 选密钥；未知 kid = 拒绝，不遍历兜底）。</summary>
        public static string KidOf(string[] parts) { return parts[IdxKid]; }

        /// <summary>Base64Url 编码（去 padding、<c>+</c>→<c>-</c>、<c>/</c>→<c>_</c>——故不含 <c>.</c>）。</summary>
        public static string Encode(string value)
        {
            if (value == null) value = string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Base64Url 解码；失败返回 null（不抛）。</summary>
        public static string Decode(string base64Url)
        {
            if (base64Url == null) return null;
            if (base64Url.Length == 0) return string.Empty;
            string s = base64Url.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return null;      // 长度模 4 为 1 不是合法 base64
            }
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(s));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>严格十进制整数（拒绝 "+1"、" 1"、"1_0" 等宽松形式——避免歧义编码绕过比对）。</summary>
        public static bool TryParseLong(string s, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            int start = s[0] == '-' ? 1 : 0;
            if (start >= s.Length) return false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
            }
            return long.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
