using System.Collections.Generic;

namespace RoomServer.Application
{
    /// <summary>
    /// 重连一次性票据（《服务端架构设计》§10-E3：《M10实施指导》决策 12）：
    /// Join 成功时签发 → 客户端断线后凭票换"权威快照 + 输入历史"（§5.6 首选路径）。
    /// **一次性**：消费即作废（重放同一张票拿不到第二次）；**有时效**：超过 <see cref="TicketTtlMs"/> 作废。
    /// 票据绑定 (playerId, roomId)——换票即恢复原席位，不重开新号。
    ///
    /// R1（§9.2"票据表…必须有容量上限和周期清理"）：
    /// - **时钟注入**：原先直读 <c>Environment.TickCount64</c>（Runtime/Application 禁系统时钟）——
    ///   改从注入的 <see cref="IMonotonicClock"/> 派生毫秒（L1 用虚拟时钟，无墙钟依赖）；
    /// - **容量上限**：条目超过 <see cref="Capacity"/> 时淘汰最老条目（票据本是短期凭证，
    ///   淘汰语义 = 该客户端需重新 Join，不影响在途合法重连的近期票据）；
    /// - <see cref="PurgeExpired"/> 由宿主周期调用。
    ///
    /// 安全边界（R2 前不得公网）：票据仍是**可预测串**（非 CSPRNG），仅作原型能力——
    /// 签名票据/熵源改造归 R2（《服务端通用框架总设计》§5 P0-6）。
    /// </summary>
    public sealed class ReconnectService
    {
        /// <summary>票据有效期（毫秒）：5 分钟（《联机Demo设计》断线宽容窗口）。</summary>
        public const long TicketTtlMs = 5 * 60 * 1000;

        /// <summary>票据表容量上限（默认 256——房间席位规模下远超在途票据数，超限淘汰最老）。</summary>
        public const int DefaultCapacity = 256;

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
        private long _serial;

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

            string token = "rc" + (++_serial).ToString("x") + "-" + playerId.ToString("x");
            _tickets[token] = new Ticket { PlayerId = playerId, RoomId = roomId, ExpireAtMs = NowMs() + TicketTtlMs };
            _issuedOrder.Enqueue(token);
            return token;
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
