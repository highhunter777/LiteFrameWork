using System.Collections.Generic;

using LiteSync.Client;
namespace RoomServer.Application
{
    /// <summary>
    /// 限流参（配置装载产物；<see cref="Default"/> 为缺省——容量/速率都可调，不写死为事实）。
    /// </summary>
    public sealed class RateLimitSettings
    {
        /// <summary>单条 lane 的令牌桶参数。</summary>
        public readonly struct Lane
        {
            /// <summary>突发容量（&gt;=1）；同时也是桶内令牌上限。</summary>
            public readonly int Burst;

            /// <summary>稳态补充速率（枚/秒）。</summary>
            public readonly double RefillPerSec;

            public Lane(int burst, double refillPerSec)
            {
                Burst = burst;
                RefillPerSec = refillPerSec;
            }
        }

        public readonly Lane IpConnect;
        public readonly Lane IpEntry;
        public readonly Lane AccountEntry;
        public readonly Lane SessionPackets;

        /// <summary>每条 lane 的键表容量上限（条）。</summary>
        public readonly int Buckets;

        /// <summary>空闲桶回收阈值（毫秒）：超过该时长未活动的桶由 <see cref="RateLimiter.PurgeIdle"/> 清理。</summary>
        public readonly long IdleTtlMs;

        public RateLimitSettings(Lane ipConnect, Lane ipEntry, Lane accountEntry, Lane sessionPackets,
            int buckets, long idleTtlMs)
        {
            IpConnect = ipConnect;
            IpEntry = ipEntry;
            AccountEntry = accountEntry;
            SessionPackets = sessionPackets;
            Buckets = buckets;
            IdleTtlMs = idleTtlMs;
        }

        /// <summary>
        /// 缺省参（原型/兼容形态用；生产应经配置显式给值——§520/§600 不把估算值写死为事实）。
        /// </summary>
        public static readonly RateLimitSettings Default = new RateLimitSettings(
            new Lane(32, 4), new Lane(32, 8), new Lane(16, 2), new Lane(256, 128),
            buckets: 8192, idleTtlMs: 120_000);
    }

    /// <summary>
    /// 分层限流（《商业级通用服务端框架总设计》§P0-6"远端地址限流"、 §595 Beta 门槛"限流可**按 IP/账号/Session** 生效"、§343"限流桶必须有**容量上限和周期清理**"）。
    /// </summary>
    public sealed class RateLimiter
    {
        private sealed class Bucket
        {
            public double Tokens;
            public long LastRefillMs;
            public long LastSeenMs;
        }

        private readonly RateLimitSettings _settings;
        private readonly object _gate = new object();

        private readonly Dictionary<string, Bucket> _ipConnect = new Dictionary<string, Bucket>();
        private readonly Dictionary<string, Bucket> _ipEntry = new Dictionary<string, Bucket>();
        private readonly Dictionary<string, Bucket> _accountEntry = new Dictionary<string, Bucket>();
        private readonly Dictionary<int, Bucket> _sessionPackets = new Dictionary<int, Bucket>();

        // ---- 计数面（P4 同款收口）：私有 + 只读属性，写点全在本类的锁内 ---- 这六个数此前是 public 可变字段，而写入还以 `ref` 传进 TryAcquire——等于把计数器的 写权限同时交给"任何拿到 limiter 的代码"。

        private long _rejectedIpConnect;
        private long _rejectedIpEntry;
        private long _rejectedAccountEntry;
        private long _rejectedSessionPackets;
        private long _rejectedTableFull;
        private long _bucketsPurged;

        /// <summary>IP 维度·连接被限数（宿主拒绝的 OnConnected）。</summary>
        public long RejectedIpConnect => _rejectedIpConnect;

        /// <summary>IP 维度·入场被限数（Join / Reconnect 尝试）。</summary>
        public long RejectedIpEntry => _rejectedIpEntry;

        /// <summary>账号维度·入场被限数。</summary>
        public long RejectedAccountEntry => _rejectedAccountEntry;

        /// <summary>Session 维度·入包被限数（丢弃，不回应）。</summary>
        public long RejectedSessionPackets => _rejectedSessionPackets;

        /// <summary>键表满导致的新键拒绝数（任一 lane 的子原因，同时计入对应 lane 计数；&gt;0 = 容量与来源规模不匹配）。</summary>
        public long RejectedTableFull => _rejectedTableFull;

        /// <summary>累计被清理的空闲桶数。</summary>
        public long BucketsPurged => _bucketsPurged;

        public RateLimiter(RateLimitSettings settings)
        {
            _settings = settings ?? RateLimitSettings.Default;
        }

        /// <summary>IP 维度·连接：OnConnected 时消耗。null/空地址放行（看不见的地址限不了，见端口契约）。</summary>
        public bool TryAcquireIpConnect(string address, long nowMs)
            => string.IsNullOrEmpty(address)
                || TryAcquire(_ipConnect, address, _settings.IpConnect, nowMs, ref _rejectedIpConnect);

        /// <summary>IP 维度·入场：Join / Reconnect 尝试时消耗。null/空地址放行。</summary>
        public bool TryAcquireIpEntry(string address, long nowMs)
            => string.IsNullOrEmpty(address)
                || TryAcquire(_ipEntry, address, _settings.IpEntry, nowMs, ref _rejectedIpEntry);

        /// <summary>账号维度·入场：仅验签通过后的 Join 调用（验签前无 AccountId）。null/空放行。</summary>
        public bool TryAcquireAccountEntry(string accountId, long nowMs)
            => string.IsNullOrEmpty(accountId)
                || TryAcquire(_accountEntry, accountId, _settings.AccountEntry, nowMs, ref _rejectedAccountEntry);

        /// <summary>Session 维度·包速率：每入包消耗（键 = connectionId，int 键零分配）。</summary>
        public bool TryAcquireSessionPacket(int connectionId, long nowMs)
            => TryAcquire(_sessionPackets, connectionId, _settings.SessionPackets, nowMs, ref _rejectedSessionPackets);

        /// <summary>当前在册桶总数（诊断；各 lane 求和）。</summary>
        public int BucketCount
        {
            get
            {
                lock (_gate)
                    return _ipConnect.Count + _ipEntry.Count + _accountEntry.Count + _sessionPackets.Count;
            }
        }

        /// <summary>周期清理空闲桶（宿主每 <c>SessionCleanupIntervalTicks</c> 调一次）。返回移除数。</summary>
        public int PurgeIdle(long nowMs)
        {
            lock (_gate)
            {
                int removed = PurgeTable(_ipConnect, nowMs) + PurgeTable(_ipEntry, nowMs)
                    + PurgeTable(_accountEntry, nowMs) + PurgeTable(_sessionPackets, nowMs);
                _bucketsPurged += removed;
                return removed;
            }
        }

        private bool TryAcquire<TKey>(Dictionary<TKey, Bucket> table, TKey key,
            in RateLimitSettings.Lane lane, long nowMs, ref long rejected)
        {
            lock (_gate)
            {
                if (!table.TryGetValue(key, out Bucket bucket))
                {
                    if (table.Count >= _settings.Buckets)
                    {
                        // 表满：拒绝新键，不淘汰旧桶（安全 > 可用；淘汰 = 替攻击者清欠账）。
                        _rejectedTableFull++;
                        rejected++;
                        return false;
                    }
                    bucket = new Bucket { Tokens = lane.Burst, LastRefillMs = nowMs, LastSeenMs = nowMs };
                    table.Add(key, bucket);
                }
                else
                {
                    bucket.LastSeenMs = nowMs;
                }

                if (nowMs > bucket.LastRefillMs)
                {
                    double refill = (nowMs - bucket.LastRefillMs) * lane.RefillPerSec / 1000.0;
                    double tokens = bucket.Tokens + refill;
                    bucket.Tokens = tokens > lane.Burst ? lane.Burst : tokens;
                    bucket.LastRefillMs = nowMs;
                }

                if (bucket.Tokens >= 1.0)
                {
                    bucket.Tokens -= 1.0;
                    return true;
                }
                rejected++;
                return false;
            }
        }

        private int PurgeTable<TKey>(Dictionary<TKey, Bucket> table, long nowMs)
        {
            List<TKey> dead = null;
            foreach (KeyValuePair<TKey, Bucket> kv in table)
            {
                if (nowMs - kv.Value.LastSeenMs < _settings.IdleTtlMs) continue;
                if (dead == null) dead = new List<TKey>();
                dead.Add(kv.Key);
            }
            if (dead == null) return 0;

            for (int i = 0; i < dead.Count; i++) table.Remove(dead[i]);
            return dead.Count;
        }
    }
}
