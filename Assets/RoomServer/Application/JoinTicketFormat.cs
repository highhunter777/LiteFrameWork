using System;
using System.Security.Cryptography;
using System.Text;

namespace RoomServer.Application
{
    /// <summary>
    /// Join 票据的**线格式**（《服务端总设计》§P0-6 六项绑定 + §7"建议最小接口"）。
    ///
    /// **单一来源**：签发端与验证端都必须走本类，形状规则（字段序、编码、被签名覆盖的范围）
    /// 只有这一份。测试用隔离签发器亦调本类——**不得另写一份拼串**，否则签发端漂移时
    /// 验证端仍是绿的（协议单源红线同一条纪律）。
    ///
    /// 形状：<c>v3.kid.playerId.accountId.roomId.matchId.audience.buildHash.simVersion.nonce.expMs.nbfMs.mapId.expectedPlayers.sig</c>
    /// 共 15 段。自由文本字段走 Base64Url（去 padding、<c>+</c>→<c>-</c>、<c>/</c>→<c>_</c>），
    /// 故字段内不可能出现分隔符 <c>.</c>；整数（expMs/nbfMs/mapId/expectedPlayers）明文十进制。
    /// 签名覆盖**除末段（sig）外的全部 14 段**。
    ///
    /// **v2（多图批）**：相对 v1 追加 <c>mapId</c> 段（房间创建意图里的地图行键；0 = 未指定）。
    /// **v3（开房参数批）**：相对 v2 追加 <c>expectedPlayers</c> 段（房间创建意图里的期望人数；
    /// 0 = 未指定＝按房间模板）。两段都在签名覆盖内——创建意图与其余绑定项同等防篡改。
    /// 旧版票据**不静默接受**（版本段不符即拒）。
    /// </summary>
    public static class JoinTicketFormat
    {
        /// <summary>格式版本。不匹配即拒——旧格式不静默接受。
        /// v2 = 追加 mapId 段（多图批）；v3 = 追加 expectedPlayers 段（开房参数批）。</summary>
        public const string Version = "v3";

        /// <summary>段数（含 sig）。</summary>
        public const int PartCount = 15;

        private const int IdxVersion = 0;
        private const int IdxKid = 1;
        private const int IdxPlayerId = 2;
        private const int IdxAccountId = 3;
        private const int IdxRoomId = 4;
        private const int IdxMatchId = 5;
        private const int IdxAudience = 6;
        private const int IdxBuildHash = 7;
        private const int IdxSimVersion = 8;
        private const int IdxNonce = 9;
        private const int IdxExpiresAtMs = 10;
        private const int IdxNotBeforeMs = 11;
        private const int IdxMapId = 12;
        private const int IdxExpectedPlayers = 13;
        private const int IdxSignature = 14;

        /// <summary>签发端组装的票据字段（不含签名）。</summary>
        public sealed class Fields
        {
            public string Kid;
            public string PlayerId;
            public string AccountId;
            public string RoomId;
            public string MatchId;
            public string Audience;
            public string BuildHash;
            public string SimVersion;
            public string Nonce;
            public long ExpiresAtMs;
            public long NotBeforeMs;

            /// <summary>房间创建意图里的地图行键（0 = 未指定——房间服按模板/宿主默认图建房）。</summary>
            public int MapId;

            /// <summary>房间创建意图里的**期望人数**（0 = 未指定——房间服按模板 `expected_players`；
            /// 正值只作用于**新建房**，房间端按内核容量校验，越界拒建房）。</summary>
            public int ExpectedPlayers;
        }

        /// <summary>组装被签名覆盖的前缀（前 13 段）。签发与验签**必须**共用它。</summary>
        public static string BuildPrefix(string kid, Fields f)
        {
            var sb = new StringBuilder(160);
            sb.Append(Version).Append('.')
              .Append(kid).Append('.')
              .Append(Encode(f.PlayerId)).Append('.')
              .Append(Encode(f.AccountId)).Append('.')
              .Append(Encode(f.RoomId)).Append('.')
              .Append(Encode(f.MatchId)).Append('.')
              .Append(Encode(f.Audience)).Append('.')
              .Append(Encode(f.BuildHash)).Append('.')
              .Append(Encode(f.SimVersion)).Append('.')
              .Append(Encode(f.Nonce)).Append('.')
              .Append(f.ExpiresAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('.')
              .Append(f.NotBeforeMs.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('.')
              .Append(f.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('.')
              .Append(f.ExpectedPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>续上签名段，得到完整票据。</summary>
        public static string AppendSignature(string prefix, byte[] signature)
        {
            return prefix + "." + Convert.ToBase64String(signature);
        }

        /// <summary>拆段；段数或版本不符返回 null（调用方转 Malformed，不抛）。</summary>
        public static string[] Split(string ticket)
        {
            if (string.IsNullOrEmpty(ticket)) return null;
            string[] parts = ticket.Split('.');
            if (parts.Length != PartCount) return null;
            if (!string.Equals(parts[IdxVersion], Version, StringComparison.Ordinal)) return null;
            return parts;
        }

        public static string KidOf(string[] parts) { return parts[IdxKid]; }
        public static string SignatureOf(string[] parts) { return parts[IdxSignature]; }

        /// <summary>被签名覆盖的前缀（从已拆分的段重建——保证验签算的是同一串字节）。</summary>
        public static string PrefixOf(string[] parts)
        {
            var sb = new StringBuilder(160);
            for (int i = 0; i < PartCount - 1; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        /// <summary>解出字段；任一字段解码失败返回 null（调用方转 Malformed）。</summary>
        public static Fields ReadFields(string[] parts)
        {
            var f = new Fields
            {
                Kid = parts[IdxKid],
                PlayerId = Decode(parts[IdxPlayerId]),
                AccountId = Decode(parts[IdxAccountId]),
                RoomId = Decode(parts[IdxRoomId]),
                MatchId = Decode(parts[IdxMatchId]),
                Audience = Decode(parts[IdxAudience]),
                BuildHash = Decode(parts[IdxBuildHash]),
                SimVersion = Decode(parts[IdxSimVersion]),
                Nonce = Decode(parts[IdxNonce]),
            };
            if (f.PlayerId == null || f.AccountId == null || f.RoomId == null || f.MatchId == null
                || f.Audience == null || f.BuildHash == null || f.SimVersion == null || f.Nonce == null)
                return null;
            if (!TryParseLong(parts[IdxExpiresAtMs], out f.ExpiresAtMs)) return null;
            if (!TryParseLong(parts[IdxNotBeforeMs], out f.NotBeforeMs)) return null;
            // 地图行键：非负十进制 int（越界/负值 = 编码非法，按 Malformed 拒——不静默取 0）
            if (!TryParseLong(parts[IdxMapId], out long mapId)) return null;
            if (mapId < 0 || mapId > int.MaxValue) return null;
            f.MapId = (int)mapId;
            // 期望人数：同上口径（0 = 未指定；上界归房间端按内核容量校验——本层只管编码合法）
            if (!TryParseLong(parts[IdxExpectedPlayers], out long players)) return null;
            if (players < 0 || players > int.MaxValue) return null;
            f.ExpectedPlayers = (int)players;
            return f;
        }

        /// <summary>Base64Url 编码（去 padding、<c>+</c>→<c>-</c>、<c>/</c>→<c>_</c>——故不含 <c>.</c>）。
        /// **实现单源 <see cref="Base64Url"/>**（P2：与访问令牌线格式共一份）。</summary>
        public static string Encode(string value) => Base64Url.EncodeText(value);

        /// <summary>Base64Url 解码；失败返回 null（不抛）。实现单源 <see cref="Base64Url"/>。</summary>
        public static string Decode(string base64Url) => Base64Url.DecodeText(base64Url);

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

        /// <summary>HMAC-SHA256 签名（**不自创密码算法**——§P0-6 末条）。</summary>
        public static byte[] Sign(byte[] secret, string prefix)
        {
            using (var hmac = new HMACSHA256(secret))
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(prefix));
        }
    }
}
