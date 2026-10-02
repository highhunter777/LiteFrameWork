using System.Collections.Generic;
using System.Security.Cryptography;

namespace RoomServer.Application
{
    /// <summary>
    /// 重连一次性票据（《服务端架构设计》§10-E3：《M10实施指导》决策 12）：
    /// Join 成功时签发 → 客户端断线后凭票换"权威快照 + 输入历史"（§5.6 首选路径）。
    /// **一次性**：消费即作废（重放同一张票拿不到第二次）；**有时效**：超过 <see cref="TicketTtlMs"/> 作废。
    /// 票据绑定 (playerId, roomId)——换票即恢复原席位，不重开新号。
    ///
    /// **容量与清理**（§9.2"票据表…必须有容量上限和周期清理"）：
    /// - **时钟注入**：从注入的 <see cref="IMonotonicClock"/> 派生毫秒（Runtime/Application 禁系统时钟；
    ///   L1 用虚拟时钟，无墙钟依赖）；
    /// - **容量上限**：条目超过 <see cref="Capacity"/> 时淘汰最老条目（票据本是短期凭证，
    ///   淘汰语义 = 该客户端需重新 Join，不影响在途合法重连的近期票据）；
    /// - <see cref="PurgeExpired"/> 由宿主周期调用。
    ///
    /// 安全边界：票据是 **CSPRNG 生成的不透明串**（<see cref="TokenEntropyBytes"/>×8 = 128 bit 熵，
    /// <see cref="RandomNumberGenerator"/>，§P0-6"至少 128 bit 熵"+"不得自创密码算法"）。
    /// **仍未达公开部署标准**的是信道侧：业务载荷无机密性/完整性/重放保护（**安全信封**，专项另立）
    /// ——在信封完成前本服务不得公网暴露。
    /// </summary>
    public sealed class ReconnectService
    {
        /// <summary>票据有效期（毫秒）：5 分钟（《联机Demo设计》断线宽容窗口）。</summary>
        public const long TicketTtlMs = 5 * 60 * 1000;

        /// <summary>票据表容量上限（默认 256——房间席位规模下远超在途票据数，超限淘汰最老）。</summary>
        public const int DefaultCapacity = 256;

        /// <summary>票据熵字节数：16 B = 128 bit（§P0-6"至少 128 bit 熵"），base64url 后 22 字符。</summary>
        public const int TokenEntropyBytes = 16;

        private struct Ticket
        {
            public int PlayerId;
            public string RoomId;
            public long ExpireAtMs;
        }

        private readonly Dictionary<string, Ticket> _tickets = new Dictionary<string, Ticket>();
        private readonly Queue<string> _issuedOrder = new Queue<string>();   // 签发序（淘汰最老用）
        private readonly IMonotonicClock _clock;
        private readonly int _capacity;

        public ReconnectService(int capacity = DefaultCapacity, IMonotonicClock clock = null)
        {
            _capacity = capacity > 0 ? capacity : 1;
            _clock = clock ?? StopwatchClock.Instance;
        }

        /// <summary>注入时钟的单调毫秒（无墙钟——L1 虚拟时钟可用）。</summary>
        private long NowMs() => _clock.Timestamp * 1000L / _clock.Frequency;

        /// <summary>签发一次性票据（Join 成功时调用）；超容量先淘汰最老条目。</summary>
        public string Issue(int playerId, string roomId)
        {
            while (_issuedOrder.Count >= _capacity)
            {
                string oldest = _issuedOrder.Dequeue();
                _tickets.Remove(oldest);                                    // 淘汰最老（票据是短期凭证）
            }

            string token = NewToken();
            _tickets[token] = new Ticket { PlayerId = playerId, RoomId = roomId, ExpireAtMs = NowMs() + TicketTtlMs };
            _issuedOrder.Enqueue(token);
            return token;
        }

        /// <summary>
        /// CSPRNG 不透明票据：<see cref="TokenEntropyBytes"/> 字节随机数 → base64url（去 padding，无分隔符）。
        /// 不携带任何可推导信息（票据原文不进日志）。
        /// </summary>
        private static string NewToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(TokenEntropyBytes);
            return System.Convert.ToBase64String(bytes)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>消费票据（一次性 + 时效）：成功则移除并返回席位；失败不改状态。</summary>
        public bool TryConsume(string token, out int playerId, out string roomId)
        {
            playerId = -1;
            roomId = null;
            if (string.IsNullOrEmpty(token)) return false;
            if (!_tickets.TryGetValue(token, out Ticket ticket)) return false;

            _tickets.Remove(token);                       // 一次性：无论是否过期，用过即作废
            if (NowMs() > ticket.ExpireAtMs) return false;

            playerId = ticket.PlayerId;
            roomId = ticket.RoomId;
            return true;
        }

        /// <summary>
        /// 只查看票据绑定，不消费一次性票据。Mailbox admission 在控制 lane 满载时
        /// 需要先定位目标房间，避免为了路由而吞掉仍可重试的重连票据。
        /// </summary>
        public bool TryPeek(string token, out int playerId, out string roomId)
        {
            playerId = -1;
            roomId = null;
            if (string.IsNullOrEmpty(token)) return false;
            if (!_tickets.TryGetValue(token, out Ticket ticket)) return false;
            if (NowMs() > ticket.ExpireAtMs) return false;
            playerId = ticket.PlayerId;
            roomId = ticket.RoomId;
            return true;
        }

        /// <summary>清理过期票据（宿主周期调用；不清理只是内存缓慢增长，功能不受影响）。</summary>
        public int PurgeExpired()
        {
            long now = NowMs();
            var dead = new List<string>();
            foreach (var kv in _tickets)
                if (now > kv.Value.ExpireAtMs) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) _tickets.Remove(dead[i]);
            return dead.Count;
        }

        /// <summary>当前在册票据数（容量观测用）。</summary>
        public int Count => _tickets.Count;
    }
}
