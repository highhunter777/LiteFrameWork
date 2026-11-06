using System;
using System.Text;
using System.Threading;

namespace MetaServer
{
    /// <summary>
    /// Ops 指标（《Meta 服务专项设计》§11.2 Metrics）。
    /// </summary>
    public sealed class Ops
    {
        /// <summary>进程启动时刻（UTC，UTC 口径见 §5.2"时间字段一律 UTC"）。</summary>
        public readonly DateTime StartedUtc = DateTime.UtcNow;

        private long _httpRequests;

        private long _httpRejected;

        private long _lobbyInstanceRegisters;

        private long _ticketsIssued;

        private long _roomsCreated;

        private long _matchResultsStored;

        private long _matchResultsDuplicates;

        private volatile bool _draining;

        /// <summary>drain 状态：true = 已停止接受外部写，/ready 必须报未就绪（§10 优雅关闭第 1 步）。
        /// **只读**：置位只经 <see cref="BeginDrain"/>——本类其余状态早已方法化，此项原先独留裸字段
        /// （P4 计数面封装同款口径：状态写入是事件，给它一个方法名）。</summary>
        public bool Draining
        {
            get { return _draining; }
        }

        /// <summary>进入 drain（§10 优雅关闭第 1 步；幂等——重复调用无副作用）。</summary>
        public void BeginDrain() => _draining = true;

        /// <summary>请求计数（含健康检查；并发安全）。</summary>
        public void CountRequest() => Interlocked.Increment(ref _httpRequests);

        /// <summary>拒绝计数（4xx/5xx；并发安全）。</summary>
        public void CountRejected() => Interlocked.Increment(ref _httpRejected);

        /// <summary>实例注册/心跳接受计数（§11.2 <c>instance_registered</c> 口径；并发安全）。</summary>
        public void CountInstanceRegister() => Interlocked.Increment(ref _lobbyInstanceRegisters);

        /// <summary>Join Ticket 签发计数（§11.2 <c>ticket_issue_total</c>；并发安全）。</summary>
        public void CountTicketIssued() => Interlocked.Increment(ref _ticketsIssued);

        /// <summary>创建房间出号计数（房主开局批；并发安全）。</summary>
        public void CountRoomCreated() => Interlocked.Increment(ref _roomsCreated);

        /// <summary>对局结果首次落库计数（并发安全）。</summary>
        public void CountMatchResultStore() => Interlocked.Increment(ref _matchResultsStored);

        /// <summary>对局结果重复提交命中计数（提交管道重试的"恰好一次"证据；并发安全）。</summary>
        public void CountMatchResultDuplicate() => Interlocked.Increment(ref _matchResultsDuplicates);

        /// <summary>HTTP 请求总数（读面；写入只走 <see cref="CountRequest"/>）。</summary>
        public long HttpRequests => Interlocked.Read(ref _httpRequests);

        /// <summary>被拒绝的请求数（读面；写入只走 <see cref="CountRejected"/>）。</summary>
        public long HttpRejected => Interlocked.Read(ref _httpRejected);

        /// <summary>实例注册/心跳接受总数（读面）。</summary>
        public long LobbyInstanceRegisters => Interlocked.Read(ref _lobbyInstanceRegisters);

        /// <summary>已签发 Join Ticket 总数（读面）。</summary>
        public long TicketsIssued => Interlocked.Read(ref _ticketsIssued);

        /// <summary>创建房间出号总数（读面）。</summary>
        public long RoomsCreated => Interlocked.Read(ref _roomsCreated);

        /// <summary>对局结果首次落库总数（读面）。</summary>
        public long MatchResultsStored => Interlocked.Read(ref _matchResultsStored);

        /// <summary>对局结果重复提交命中总数（读面；稳定增长 = 提交管道重试在幂等面上收敛）。</summary>
        public long MatchResultsDuplicates => Interlocked.Read(ref _matchResultsDuplicates);

        /// <summary>
        /// 指标文本出口（/metrics）。
        /// </summary>
        public string FormatMetrics()
        {
            return new StringBuilder(512)
                .Append("# TYPE meta_http_request_total counter\n")
                .Append("meta_http_request_total ").Append(HttpRequests).Append('\n')
                .Append("# TYPE meta_http_rejected_total counter\n")
                .Append("meta_http_rejected_total ").Append(HttpRejected).Append('\n')
                .Append("# TYPE meta_lobby_instance_register_total counter\n")
                .Append("meta_lobby_instance_register_total ").Append(LobbyInstanceRegisters).Append('\n')
                .Append("# TYPE meta_ticket_issue_total counter\n")
                .Append("meta_ticket_issue_total ").Append(TicketsIssued).Append('\n')
                .Append("# TYPE meta_room_create_total counter\n")
                .Append("meta_room_create_total ").Append(RoomsCreated).Append('\n')
                .Append("# TYPE meta_match_result_store_total counter\n")
                .Append("meta_match_result_store_total ").Append(MatchResultsStored).Append('\n')
                .Append("# TYPE meta_match_result_duplicate_total counter\n")
                .Append("meta_match_result_duplicate_total ").Append(MatchResultsDuplicates).Append('\n')
                .Append("# TYPE meta_draining gauge\n")
                .Append("meta_draining ").Append(Draining ? 1 : 0).Append('\n')
                .Append("# TYPE meta_uptime_seconds gauge\n")
                .Append("meta_uptime_seconds ")
                .Append((long)(DateTime.UtcNow - StartedUtc).TotalSeconds).Append('\n')
                .ToString();
        }
    }
}
