using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// /metrics 零依赖出口用例（R4 第一批：《服务端总设计》§11.2 "出口 /metrics 走 Prometheus"——
    /// RoomServer 侧低成本文本出口，同 MetaServer Ops 先例）。钉住：
    /// ①FormatMetrics 与周期行计数**同源对账**（读面同名，肉眼可核）；
    /// ②只出进程级累计计数 + 房间数 gauge（高基数字段红线不破）；
    /// ③metrics_port 配置三态（缺省 0=关闭 / 显式 / 越界拒）；
    /// ④HTTP 出口语义（GET /metrics 200、其余 404、Dispose 后不可达）。
    /// </summary>
    public sealed class RoomMetricsTests
    {
        [Fact]
        public void FormatMetrics_零计数_全部指标行格式合法且不缺项()
        {
            string text = new Ops().FormatMetrics();

            string[] valueLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("#", StringComparison.Ordinal)).ToArray();
            // 每行 "roomserver_<name> <数字>"——Prometheus 文本最小子集（负值合法：mismatch_last_frame 缺省 -1 = 从未上报）
            Assert.All(valueLines, l => Assert.Matches(new Regex(@"^roomserver_\w+ -?\d+$"), l));
            Assert.Contains(valueLines, l => l.StartsWith("roomserver_rooms ", StringComparison.Ordinal));
            Assert.Contains(valueLines, l => l.StartsWith("roomserver_input_packets_total ", StringComparison.Ordinal));
            Assert.Contains(valueLines, l => l.StartsWith("roomserver_ticket_rejected_total ", StringComparison.Ordinal));
            Assert.Contains(valueLines, l => l.StartsWith("roomserver_settlements_outbox_failed_total ", StringComparison.Ordinal));
            Assert.Contains(valueLines, l => l.StartsWith("roomserver_ack_rejected_evicted_total ", StringComparison.Ordinal));
            // 指标全集 ≥ 30 行（将来静默丢指标在此断——对照 Ops 读面逐项列齐）
            Assert.True(valueLines.Length >= 30, $"值行数 {valueLines.Length} < 30——指标清单疑似缺项");
        }

        [Fact]
        public void FormatMetrics_计数与周期行同源()
        {
            var ops = new Ops();
            ops.NoteReject();
            ops.NoteTicketRejection(JoinTicketRejection.Expired);
            ops.NoteSessionsCleaned(3);

            string text = ops.FormatMetrics();

            Assert.Contains("roomserver_rejects_total 1", text);
            Assert.Contains("roomserver_ticket_rejected_total 1", text);
            Assert.Contains("roomserver_ticket_rejected_expired_total 1", text);   // 总数与分类一次前推
            Assert.Contains("roomserver_sessions_cleaned_total 3", text);
        }

        [Fact]
        public void FormatMetrics_房间数gauge_未绑定恒0_绑定后随源()
        {
            var ops = new Ops();
            Assert.Contains("roomserver_rooms 0", ops.FormatMetrics());

            int rooms = 2;
            ops.BindRoomsGauge(() => rooms);
            Assert.Contains("roomserver_rooms 2", ops.FormatMetrics());

            rooms = 5;                                                             // 每次出口现取——不是快照
            Assert.Contains("roomserver_rooms 5", ops.FormatMetrics());
        }

        [Fact]
        public void MetricsPort配置_缺省关闭_显式生效_越界装载期拒()
        {
            const string baseRooms = "\"rooms\":{\"default\":{\"expected_players\":2}}";

            Assert.Equal(0, RoomServerConfig.Parse($"{{\"port\":17777,\"max_rooms\":1,{baseRooms}}}").MetricsPort);

            var cfg = RoomServerConfig.Parse($"{{\"port\":17777,\"max_rooms\":1,\"metrics_port\":9101,{baseRooms}}}");
            Assert.Equal(9101, cfg.MetricsPort);
            Assert.Contains("metricsPort=9101", cfg.Describe());                    // 启动日志可见

            Assert.Throws<InvalidDataException>(
                () => RoomServerConfig.Parse($"{{\"port\":17777,\"max_rooms\":1,\"metrics_port\":70000,{baseRooms}}}"));
            Assert.Throws<InvalidDataException>(
                () => RoomServerConfig.Parse($"{{\"port\":17777,\"max_rooms\":1,\"metrics_port\":\"x\",{baseRooms}}}"));
        }

        [Fact]
        public async Task Metrics出口_GET文本_非metrics路径404_关闭后不可达()
        {
            int port = GetFreePort();
            var ops = new Ops();
            ops.NoteReject();
            using (var listener = new MetricsHttpListener(() => ops.FormatMetrics()))
            {
                listener.Start(port, "localhost");                                 // 测试绑定 localhost——避开非管理员 urlacl 依赖
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

                using var ok = await http.GetAsync($"http://localhost:{port}/metrics");
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                string body = await ok.Content.ReadAsStringAsync();
                Assert.Contains("roomserver_rejects_total 1", body);

                using var miss = await http.GetAsync($"http://localhost:{port}/other");   // 只认 /metrics——其余 404
                Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);
            }

            using var gone = new HttpClient { Timeout = TimeSpan.FromSeconds(2) }; // Dispose 后出口不可达
            await Assert.ThrowsAnyAsync<Exception>(() => gone.GetAsync($"http://localhost:{port}/metrics"));
        }

        private static int GetFreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
