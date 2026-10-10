using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteSim;
using LiteTesting;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 结算提交管道（《上云测试专项设计》§2）：Outbox → Meta 幂等端点 → 完成标记/重试/重启续投。
    ///
    /// L1：伪造 <see cref="HttpMessageHandler"/> 断言**发送载荷与完成语义**（真 HTTP 闭环见
    /// MetaServer 集成用例）；Outbox 用真实文件实装（临时日志）——提交管道的进度语义全在
    /// 日志里，替身会掩盖这层。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class SettlementSubmitServiceTests
    {
        private static readonly byte[] Key = Filled(0x44);

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public int Calls;
            public string LastBody;
            public string LastAuth;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Calls++;
                LastAuth = request.Headers.TryGetValues("Authorization", out IEnumerable<string> values)
                    ? string.Join(",", values)
                    : null;
                LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return _respond(request);
            }
        }

        private static SettlementSubmitService.Settings Settings(int intervalMs = 10_000)
        {
            return new SettlementSubmitService.Settings
            {
                Url = "http://127.0.0.1:9/matches/result",
                InstanceKey = Key,
                IntervalMs = intervalMs,
                MaxBackoffMs = 60_000,
                RequestTimeoutMs = 2_000,
            };
        }

        private static HttpResponseMessage Ok(string body = "{\"matchId\":\"m\",\"duplicate\":false,\"finishedAtMs\":1}")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

        private static MatchResultSummary Summary(string matchId = "m-1")
        {
            return new MatchResultSummary(matchId, seed: 7L, finalFrame: 500, ShutdownReason.KillLimit,
                new[] { 0, 1 }, winnerEntityId: 999L, MatchEndReason.KillLimit,
                new[]
                {
                    new PlayerMatchResult(0, 999L, 4, 1),
                    new PlayerMatchResult(1, 1000L, 1, 4),
                });
        }

        private static string TempJournal()
        {
            string dir = Path.Combine(Path.GetTempPath(), "litegame_submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settlements.jsonl");
        }

        private static void Cleanup(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Fact]
        public void 空闲_无待提交_返回Idle_不发请求()
        {
            string path = TempJournal();
            try
            {
                var handler = new FakeHandler(_ => Ok());
                using var outbox = FileSettlementOutbox.Open(path, 8);
                using var service = new SettlementSubmitService(Settings(), outbox, _ => { }, handler);

                Assert.Equal(SettlementSubmitStep.Idle, service.TrySubmitNext(out string error));
                Assert.Null(error);
                Assert.Equal(0, handler.Calls);
                Assert.Equal(0, service.Submitted);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void 提交成功_200_标记完成_载荷字段与账号映射正确()
        {
            string path = TempJournal();
            try
            {
                var handler = new FakeHandler(_ => Ok());
                using var outbox = FileSettlementOutbox.Open(path, 8);
                using var service = new SettlementSubmitService(Settings(), outbox, _ => { }, handler);

                Assert.Equal(SettlementOutboxResult.Appended,
                    outbox.Enqueue(Summary("m-42"), new[] { "acc-0", "acc-1" }));

                Assert.Equal(SettlementSubmitStep.Submitted, service.TrySubmitNext(out string error));
                Assert.Null(error);
                Assert.Equal(0, outbox.Count);              // 已标记完成
                Assert.Equal(1, service.Submitted);
                Assert.Equal("Bearer " + Convert.ToBase64String(Key), handler.LastAuth);

                using var payload = JsonDocument.Parse(handler.LastBody);
                Assert.Equal("submit:m-42", payload.RootElement.GetProperty("requestId").GetString());
                Assert.Equal("m-42", payload.RootElement.GetProperty("matchId").GetString());
                Assert.Equal(7, payload.RootElement.GetProperty("seed").GetInt64());
                Assert.Equal(500, payload.RootElement.GetProperty("finalFrame").GetInt32());
                Assert.Equal((int)ShutdownReason.KillLimit, payload.RootElement.GetProperty("endReason").GetInt32());
                Assert.Equal((int)MatchEndReason.KillLimit, payload.RootElement.GetProperty("gameplayEndReason").GetInt32());
                Assert.Equal(999L, payload.RootElement.GetProperty("winnerEntityId").GetInt64());

                JsonElement players = payload.RootElement.GetProperty("players");
                Assert.Equal(2, players.GetArrayLength());
                Assert.Equal("0", players[0].GetProperty("playerId").GetString());
                Assert.Equal("acc-0", players[0].GetProperty("accountId").GetString());
                Assert.Equal(4, players[0].GetProperty("kills").GetInt32());
                Assert.Equal(1, players[0].GetProperty("deaths").GetInt32());
                Assert.Equal(0, players[0].GetProperty("delta").GetInt64());      // 无经济占位
                Assert.Equal(-1, players[0].GetProperty("expectedRevision").GetInt64());  // 追加模式
                Assert.Equal("acc-1", players[1].GetProperty("accountId").GetString());
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void 旧行无账号_提交时账号为空串_如实送达()
        {
            string path = TempJournal();
            try
            {
                var handler = new FakeHandler(_ => Ok());
                using var outbox = FileSettlementOutbox.Open(path, 8);
                using var service = new SettlementSubmitService(Settings(), outbox, _ => { }, handler);

                Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-old")));   // 无账号映射

                Assert.Equal(SettlementSubmitStep.Submitted, service.TrySubmitNext(out _));

                using var payload = JsonDocument.Parse(handler.LastBody);
                Assert.Equal("", payload.RootElement.GetProperty("players")[0].GetProperty("accountId").GetString());
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void 提交失败_HTTP500_保持待提交_恢复后续投()
        {
            string path = TempJournal();
            try
            {
                HttpStatusCode status = HttpStatusCode.InternalServerError;
                var handler = new FakeHandler(_ => new HttpResponseMessage(status));
                using var outbox = FileSettlementOutbox.Open(path, 8);
                using var service = new SettlementSubmitService(Settings(), outbox, _ => { }, handler);
                outbox.Enqueue(Summary("m-1"), new[] { "acc-0", "acc-1" });

                Assert.Equal(SettlementSubmitStep.Failed, service.TrySubmitNext(out string error));
                Assert.Equal("HTTP 500", error);
                Assert.Equal(1, outbox.Count);              // 条目保持待提交（不丢）
                Assert.Equal(1, service.Failed);
                Assert.Equal(0, service.Submitted);

                status = HttpStatusCode.OK;                 // Meta 恢复
                Assert.Equal(SettlementSubmitStep.Submitted, service.TrySubmitNext(out _));
                Assert.Equal(0, outbox.Count);
                Assert.Equal(1, service.Submitted);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void 提交失败_传输异常_保持待提交_不抛()
        {
            string path = TempJournal();
            try
            {
                var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
                using var outbox = FileSettlementOutbox.Open(path, 8);
                using var service = new SettlementSubmitService(Settings(), outbox, _ => { }, handler);
                outbox.Enqueue(Summary("m-1"), new[] { "acc-0", "acc-1" });

                Assert.Equal(SettlementSubmitStep.Failed, service.TrySubmitNext(out string error));
                Assert.Contains("HttpRequestException", error);
                Assert.Equal(1, outbox.Count);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void 重启续投_重启后从未标记面继续_不丢不重()
        {
            string path = TempJournal();
            try
            {
                var handler = new FakeHandler(_ => Ok());
                // 第一次"进程"：入盒两条，只提交掉第一条（第二条留待重启续投）
                using (var first = FileSettlementOutbox.Open(path, 8))
                using (var service = new SettlementSubmitService(Settings(), first, _ => { }, handler))
                {
                    first.Enqueue(Summary("m-1"), new[] { "acc-0", "acc-1" });
                    first.Enqueue(Summary("m-2"), new[] { "acc-0", "acc-1" });
                    Assert.Equal(SettlementSubmitStep.Submitted, service.TrySubmitNext(out _));
                }

                // "重启"：新 Outbox 实例（重放日志）+ 新服务——待提交面只剩 m-2
                using (var restarted = FileSettlementOutbox.Open(path, 8))
                using (var service = new SettlementSubmitService(Settings(), restarted, _ => { }, handler))
                {
                    Assert.Equal(1, restarted.Count);
                    Assert.Equal("m-2", restarted.ListPending()[0].MatchId);

                    Assert.Equal(SettlementSubmitStep.Submitted, service.TrySubmitNext(out _));
                    Assert.Equal(0, restarted.Count);
                }

                using (var third = FileSettlementOutbox.Open(path, 8))
                {
                    Assert.Equal(0, third.Count);           // 重启面收敛：不重投已标记条目
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void 退避_指数增长并封顶()
        {
            Assert.Equal(1000, SettlementSubmitService.BackoffMs(1, 1000, 60000));   // 首次 = 基础间隔
            Assert.Equal(2000, SettlementSubmitService.BackoffMs(2, 1000, 60000));
            Assert.Equal(4000, SettlementSubmitService.BackoffMs(3, 1000, 60000));
            Assert.Equal(60000, SettlementSubmitService.BackoffMs(10, 1000, 60000)); // 1000<<9 越顶
            Assert.Equal(60000, SettlementSubmitService.BackoffMs(999, 1000, 60000));
        }

        [Fact]
        public void 构造_坏URL或缺密钥_拒绝()
        {
            SettlementSubmitService.Settings settings = Settings();
            settings.Url = "not-a-url";
            Assert.ThrowsAny<Exception>(() =>
                new SettlementSubmitService(settings, new NullOutbox(), _ => { }, new FakeHandler(_ => Ok())));

            settings = Settings();
            settings.InstanceKey = null;
            Assert.ThrowsAny<Exception>(() =>
                new SettlementSubmitService(settings, new NullOutbox(), _ => { }, new FakeHandler(_ => Ok())));
        }

        /// <summary>构造期拒绝用例的占位盒（不会被触碰）。</summary>
        private sealed class NullOutbox : ISettlementOutbox
        {
            public SettlementOutboxResult Enqueue(MatchResultSummary summary, string[] seatAccountIds = null)
                => SettlementOutboxResult.Failed;
            public bool TryMarkCompleted(string matchId) => false;
            public int Count => 0;
            public IReadOnlyList<PendingSettlement> ListPending() => Array.Empty<PendingSettlement>();
            public void Flush() { }
        }
    }
}
