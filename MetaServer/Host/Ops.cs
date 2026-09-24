using System;
using System.Text;

namespace MetaServer
{
    /// <summary>
    /// Ops 指标（《Meta 服务专项设计》§11.2 Metrics）。
    ///
    /// 形态沿用 <c>RoomServer.Application.Ops</c>：计数由各处就地累加，本类只做**汇总与格式化**
    /// （StringBuilder 复用，零分配热路径）。定位是运维观测，不是精确计量器。
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

        /// <summary>HTTP 请求总数（含健康检查）。</summary>
        public long HttpRequests;

        /// <summary>被拒绝的请求数（超上限/非法）。</summary>
        public long HttpRejected;

        /// <summary>drain 状态：true = 已停止接受外部写，/ready 必须报未就绪（§10 优雅关闭第 1 步）。</summary>
        public volatile bool Draining;

        private readonly StringBuilder _sb = new StringBuilder(256);

        /// <summary>
        /// 指标文本出口（/metrics）。
        /// 输出 Prometheus 文本格式的**最小子集**（`name value` 行），便于 R4 直接替换为正式出口
        /// 而不改变调用方。刻意不含高基数字段（§11.2）。
        /// </summary>
        public string FormatMetrics()
        {
            _sb.Clear();
            _sb.Append("# TYPE meta_http_request_total counter\n")
               .Append("meta_http_request_total ").Append(HttpRequests).Append('\n')
               .Append("# TYPE meta_http_rejected_total counter\n")
               .Append("meta_http_rejected_total ").Append(HttpRejected).Append('\n')
               .Append("# TYPE meta_draining gauge\n")
               .Append("meta_draining ").Append(Draining ? 1 : 0).Append('\n')
               .Append("# TYPE meta_uptime_seconds gauge\n")
               .Append("meta_uptime_seconds ")
               .Append((long)(DateTime.UtcNow - StartedUtc).TotalSeconds).Append('\n');
            return _sb.ToString();
        }
    }
}
