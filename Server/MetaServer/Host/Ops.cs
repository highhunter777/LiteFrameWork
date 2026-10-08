using System;
using System.Text;
using System.Threading;

namespace MetaServer
{
    /// <summary>
    /// Ops 指标（《Meta 服务专项设计》§11.2 Metrics）。
    ///
    /// 形态沿用 <c>RoomServer.Application.Ops</c>：计数由各处就地累加，本类只做**汇总与格式化**。
    /// **线程安全**：Kestrel 并发请求共用同一实例——计数走 <c>Interlocked</c>（硬化批 2026-10-03，
    /// 修复并发丢计数），/metrics 低频出口每次调用独立缓冲（并发 Format 不再互相踩踏）。
    ///
    /// §11.2 红线：标签**禁用高基数字段**（accountId/playerId/requestId 等只进日志与 Trace）。
    /// 本类因此只做进程级计数，不按调用方维度聚合。
    ///
    /// Prometheus/OpenTelemetry 出口（§11.2 末段）归 R4；本批先给低成本计数 + 文本出口，
    /// 使健康检查与指标从骨架起就可观测，而不是等到 R4 才补。
    /// </summary>
    public sealed class Ops
    {
        /// <summary>进程启动时刻（UTC，UTC 口径见 §5.2"时间字段一律 UTC"）。</summary>
        public readonly DateTime StartedUtc = DateTime.UtcNow;

        private long _httpRequests;

        private long _httpRejected;

        private long _lobbyInstanceRegisters;

        private long _ticketsIssued;

        /// <summary>drain 状态：true = 已停止接受外部写，/ready 必须报未就绪（§10 优雅关闭第 1 步）。</summary>
        public volatile bool Draining;

        /// <summary>请求计数（含健康检查；并发安全）。</summary>
        public void CountRequest() => Interlocked.Increment(ref _httpRequests);

        /// <summary>拒绝计数（4xx/5xx；并发安全）。</summary>
        public void CountRejected() => Interlocked.Increment(ref _httpRejected);

        /// <summary>实例注册/心跳接受计数（§11.2 <c>instance_registered</c> 口径；并发安全）。</summary>
        public void CountInstanceRegister() => Interlocked.Increment(ref _lobbyInstanceRegisters);

        /// <summary>Join Ticket 签发计数（§11.2 <c>ticket_issue_total</c>；并发安全）。</summary>
        public void CountTicketIssued() => Interlocked.Increment(ref _ticketsIssued);

        /// <summary>HTTP 请求总数（读面；写入只走 <see cref="CountRequest"/>）。</summary>
        public long HttpRequests => Interlocked.Read(ref _httpRequests);

        /// <summary>被拒绝的请求数（读面；写入只走 <see cref="CountRejected"/>）。</summary>
        public long HttpRejected => Interlocked.Read(ref _httpRejected);

        /// <summary>实例注册/心跳接受总数（读面）。</summary>
        public long LobbyInstanceRegisters => Interlocked.Read(ref _lobbyInstanceRegisters);

        /// <summary>已签发 Join Ticket 总数（读面）。</summary>
        public long TicketsIssued => Interlocked.Read(ref _ticketsIssued);

        /// <summary>
        /// 指标文本出口（/metrics）。
        /// 输出 Prometheus 文本格式的**最小子集**（`name value` 行），便于 R4 直接替换为正式出口
        /// 而不改变调用方。刻意不含高基数字段（§11.2）。
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
                .Append("# TYPE meta_draining gauge\n")
                .Append("meta_draining ").Append(Draining ? 1 : 0).Append('\n')
                .Append("# TYPE meta_uptime_seconds gauge\n")
                .Append("meta_uptime_seconds ")
                .Append((long)(DateTime.UtcNow - StartedUtc).TotalSeconds).Append('\n')
                .ToString();
        }
    }
}
