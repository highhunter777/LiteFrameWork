using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace RoomServer.Application
{
    /// <summary>
    /// HMAC-SHA256 Join 票据验证器（《服务端总设计》§P0-6、§7"建议最小接口"；《框架先行》§5-4）。
    ///
    /// **为什么是共享密钥而非非对称**：§P0-6 允许"RoomServer 使用**本地公钥或共享验证器**验签"。
    /// 框架期 Meta 与 RoomServer 同信任域，HMAC-SHA256 即满足"热路径不查库 + fail-closed"。
    /// **换成非对称验签只替换本类**，<see cref="IJoinTicketValidator"/> 与消费者不变。
    ///
    /// **本类纪律**（对应 <see cref="IJoinTicketValidator"/> 的契约）：
    /// - 时钟**只**取 <see cref="JoinContext.NowMs"/>，绝不读系统时钟（否则 L1 造不出过期/回拨用例）；
    /// - <see cref="Validate"/> **不抛异常**——任何解析失败/未预期异常一律落到拒绝分类（fail-closed）；
    /// - nonce 窗口**有界**且可 purge（§20 完成定义第 4 条）；
    /// - **本类不写日志**（票据原文绝不进日志）。
    ///
    /// **线程安全**：nonce 窗口加锁（服务端多连接并发验证）。
    /// </summary>
    public sealed class HmacJoinTicketValidator : IJoinTicketValidator
    {
        /// <summary>nonce 一次性消费窗口默认容量（条目数）。</summary>
        public const int DefaultNonceWindow = 4096;

        /// <summary>票据字符串字节上限（P0-3 解析前限制长度：超限不解析直接拒）。</summary>
        public const int DefaultMaxTokenBytes = 2048;

        private readonly Dictionary<string, JoinTicketKey> _keysByKid =
            new Dictionary<string, JoinTicketKey>(StringComparer.Ordinal);
        private readonly int _nonceWindow;
        private readonly int _maxTokenBytes;

        // nonce 一次性窗口：nonce → 票据过期时刻。"不重放"优先于"有界"，故本表仅由 Purge() 清理；
        // 表满时**拒绝新票据**而不是淘汰旧条目——淘汰会重新打开已用票据的重放窗口（安全 > 可用）。
        private readonly Dictionary<string, long> _seenNonces = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly object _nonceLock = new object();

        private long _noncesPurged;
        private long _rejectedWindowFull;

        public HmacJoinTicketValidator(IEnumerable<JoinTicketKey> keys, int nonceWindow = DefaultNonceWindow,
            int maxTokenBytes = DefaultMaxTokenBytes)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            foreach (JoinTicketKey k in keys)
            {
                if (k == null) continue;
                _keysByKid[k.Kid] = k;      // 同 kid 后者覆盖：轮换时以最新装配为准
            }
            if (_keysByKid.Count == 0) throw new ArgumentException("至少需要一个验签密钥", nameof(keys));

            if (nonceWindow <= 0) throw new ArgumentException("nonceWindow 必须 > 0（0 = 重放保护失效）", nameof(nonceWindow));
            _nonceWindow = nonceWindow;
            _maxTokenBytes = maxTokenBytes > 0 ? maxTokenBytes : DefaultMaxTokenBytes;
        }

        /// <summary>在册 nonce 数（诊断）。</summary>
        public int NonceWindowCount
        {
            get { lock (_nonceLock) { return _seenNonces.Count; } }
        }

        /// <summary>被清理的过期 nonce 数。</summary>
        public long NoncesPurged
        {
            get { lock (_nonceLock) { return _noncesPurged; } }
        }

        /// <summary>因窗口满而被拒的票据数（&gt;0 = 宿主未及时 Purge，重放保护在牺牲可用性）。</summary>
        public long RejectedWindowFull
        {
            get { lock (_nonceLock) { return _rejectedWindowFull; } }
        }

        /// <summary>
        /// 验签 + 绑定项比对 + 重放判定（fail-closed）。任何一步失败即返回对应拒绝分类，
        /// **不降级为警告**（Meta §6.2）。成功时 nonce 已入一次性窗口。
        /// </summary>
        public JoinPrincipal Validate(string ticket, JoinContext context)
        {
            try
            {
                return ValidateCore(ticket, context);
            }
            catch (Exception)
            {
                // fail-closed：任何未预期异常都不得放行。不把异常内容带出去（票据字段可能在其中）。
                return JoinPrincipal.Rejected(JoinTicketRejection.Malformed);
            }
        }

        private JoinPrincipal ValidateCore(string ticket, JoinContext context)
        {
            if (string.IsNullOrEmpty(ticket)) return JoinPrincipal.Rejected(JoinTicketRejection.Missing);
            if (context == null) return JoinPrincipal.Rejected(JoinTicketRejection.Malformed);
            if (Encoding.UTF8.GetByteCount(ticket) > _maxTokenBytes)
                return JoinPrincipal.Rejected(JoinTicketRejection.Malformed);

            string[] parts = JoinTicketFormat.Split(ticket);
            if (parts == null) return JoinPrincipal.Rejected(JoinTicketRejection.Malformed);

            // ① 未知 kid = 拒绝（不遍历其它密钥兜底——那会让轮换期间的旧密钥变成旁路）
            if (!_keysByKid.TryGetValue(JoinTicketFormat.KidOf(parts), out JoinTicketKey key))
                return JoinPrincipal.Rejected(JoinTicketRejection.UnknownKey);

            // ② 验签（覆盖除 sig 外全部字段：改任何一项都失效）
            if (!VerifySignature(key, parts))
                return JoinPrincipal.Rejected(JoinTicketRejection.BadSignature);

            // ③ 解字段（解码失败 → Malformed，不抛）
            JoinTicketFormat.Fields f = JoinTicketFormat.ReadFields(parts);
            if (f == null) return JoinPrincipal.Rejected(JoinTicketRejection.Malformed);

            // ④ 时效：**只用服务端时钟**（客户端时间只用于提前刷新，不作判据——Meta §6.2）
            if (f.ExpiresAtMs <= f.NotBeforeMs)
                return JoinPrincipal.Rejected(JoinTicketRejection.Malformed);     // 空/负有效期：先于时序判定
            if (context.NowMs < f.NotBeforeMs)
                return JoinPrincipal.Rejected(JoinTicketRejection.NotYetValid);   // 未来生效 = 时钟回拨/伪造
            if (context.NowMs >= f.ExpiresAtMs)
                return JoinPrincipal.Rejected(JoinTicketRejection.Expired);

            // ⑤ 绑定项比对（逐项给分类便于诊断；不回显字段值）
            if (!string.IsNullOrEmpty(context.Audience)
                && !string.Equals(f.Audience, context.Audience, StringComparison.Ordinal))
                return JoinPrincipal.Rejected(JoinTicketRejection.AudienceMismatch);
            if (!string.Equals(f.RoomId, context.RoomId, StringComparison.Ordinal))
                return JoinPrincipal.Rejected(JoinTicketRejection.RoomMismatch);
            if (!string.IsNullOrEmpty(context.BuildHash)
                && !string.Equals(f.BuildHash, context.BuildHash, StringComparison.Ordinal))
                return JoinPrincipal.Rejected(JoinTicketRejection.BuildHashMismatch);
            if (!string.IsNullOrEmpty(context.Version)
                && !string.Equals(f.SimVersion, context.Version, StringComparison.Ordinal))
                return JoinPrincipal.Rejected(JoinTicketRejection.VersionMismatch);
            if (f.PlayerId.Length == 0 || f.AccountId.Length == 0 || f.Nonce.Length == 0)
                return JoinPrincipal.Rejected(JoinTicketRejection.Malformed);

            // ⑥ 重放：nonce 一次性消费（**最后一步**——前面全过才占窗口，避免无效票据耗尽窗口）
            if (!ConsumeNonce(f.Nonce, f.ExpiresAtMs, context.NowMs))
                return JoinPrincipal.Rejected(JoinTicketRejection.Replayed);

            return new JoinPrincipal
            {
                PlayerId = f.PlayerId,
                AccountId = f.AccountId,
                RoomId = f.RoomId,
                MatchId = f.MatchId,
                Audience = f.Audience,
                Nonce = f.Nonce,
                ExpiresAtMs = f.ExpiresAtMs,
                Rejection = JoinTicketRejection.None,
            };
        }

        /// <summary>常量时间比较（防时序侧信道）；签名非法 base64 直接 false（不抛）。</summary>
        private static bool VerifySignature(JoinTicketKey key, string[] parts)
        {
            byte[] provided;
            try
            {
                provided = Convert.FromBase64String(JoinTicketFormat.SignatureOf(parts));
            }
            catch (FormatException)
            {
                return false;
            }
            byte[] expected = JoinTicketFormat.Sign(key.Secret, JoinTicketFormat.PrefixOf(parts));
            return CryptographicOperations.FixedTimeEquals(expected, provided);
        }

        /// <summary>nonce 一次性消费：true = 首次（已登记）；false = 重放或窗口满。</summary>
        private bool ConsumeNonce(string nonce, long expiresAtMs, long nowMs)
        {
            lock (_nonceLock)
            {
                PurgeExpiredLocked(nowMs);

                if (_seenNonces.ContainsKey(nonce)) return false;      // 重放

                if (_seenNonces.Count >= _nonceWindow)
                {
                    // 窗口满：**拒绝新票据**，不淘汰旧条目——淘汰等于重开已用票据的重放窗口。
                    // 宿主应周期 PurgeExpiredNonces；本计数 > 0 说明 TTL 与窗口容量不匹配。
                    _rejectedWindowFull++;
                    return false;
                }

                _seenNonces[nonce] = expiresAtMs;
                return true;
            }
        }

        /// <summary>周期清理过期 nonce（宿主可调）。计数并入 <see cref="NoncesPurged"/>。</summary>
        public int PurgeExpiredNonces(long nowMs)
        {
            lock (_nonceLock)
            {
                return PurgeExpiredLocked(nowMs);
            }
        }

        private int PurgeExpiredLocked(long nowMs)
        {
            // nonce 表按插入序无序遍历即可（表规模由窗口上限约束）；过期条目占比通常很低。
            List<string> dead = null;
            foreach (KeyValuePair<string, long> kv in _seenNonces)
            {
                if (nowMs < kv.Value) continue;
                if (dead == null) dead = new List<string>();
                dead.Add(kv.Key);
            }
            if (dead == null) return 0;

            for (int i = 0; i < dead.Count; i++) _seenNonces.Remove(dead[i]);
            _noncesPurged += dead.Count;
            return dead.Count;
        }
    }
}
