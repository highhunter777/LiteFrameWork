using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LiteSim;
using LiteTesting;
using MetaServer.Modules.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace MetaServer.IntegrationTests
{
    /// <summary>
    /// 结算链真实端到端 L3（《上云测试专项设计》§6 第 4 条："结果在 Meta 可查（按账号）；
    /// 同一结果重复提交 100 次只落一条"）：真 Kestrel + 真 Mongo 测试容器 + 真迁移（v4）。
    ///
    /// 覆盖：提交幂等（归档 _id 裁判 + 账本双维度）／按账号查询／鉴权与形状；
    /// 以及**房间侧提交管道接真 Meta**的闭环（Outbox → SettlementSubmitService → Meta → Mongo，
    /// 重启续投不丢不重——重投由 Meta 幂等收敛为一条）。
    /// </summary>
    [Collection("Mongo")]
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class SettlementL3Tests : IAsyncLifetime
    {
        private static readonly byte[] InstanceKey = Filled(0x44);
        private static readonly byte[] AuthKey = Filled(0x33);
        private static readonly byte[] LobbyTicketKey = Filled(0x55);

        private readonly MongoFixture _fixture;
        private readonly List<WebApplication> _hosts = new List<WebApplication>();

        public SettlementL3Tests(MongoFixture fixture)
        {
            _fixture = fixture;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            foreach (WebApplication app in _hosts)
            {
                try
                {
                    await app.StopAsync();
                }
                catch
                {
                    // 收尾失败不掩盖断言结果
                }
                finally
                {
                    await app.DisposeAsync();
                }
            }
            _hosts.Clear();
        }

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        private void SkipIfUnavailable()
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");
        }

        private async Task<(WebApplication App, string BaseAddress)> StartHostAsync(string databaseName)
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:0",
                [MetaHost.ConfigSection + ":MongoConnectionString"] = _fixture.Uri,
                [MetaHost.ConfigSection + ":MongoDatabaseName"] = databaseName,
                [MetaHost.ConfigSection + ":AuthSigningKeyBase64"] = Convert.ToBase64String(AuthKey),
                [MetaHost.ConfigSection + ":LobbyInstanceKeyBase64"] = Convert.ToBase64String(InstanceKey),
                // Lobby 两密钥成对（半段配置拒启）——结算提交与 Lobby 注册共用实例密钥，
                // 部署形态两者同开；此处补齐使宿主通过配置门。
                [MetaHost.ConfigSection + ":LobbyTicketKeyBase64"] = Convert.ToBase64String(LobbyTicketKey),
            };

            WebApplication app = MetaHost.Build(Array.Empty<string>(), overrides);
            MetaHost.WireGracefulShutdown(app);
            _hosts.Add(app);
            try
            {
                await app.StartAsync();
            }
            catch
            {
                _hosts.Remove(app);
                await app.DisposeAsync();
                throw;
            }

            var server = app.Services.GetRequiredService<IServer>();
            foreach (string address in server.Features.Get<IServerAddressesFeature>().Addresses)
            {
                return (app, address.TrimEnd('/'));
            }
            throw new InvalidOperationException("未取得监听地址");
        }

        private static HttpRequestMessage Authorized(HttpMethod method, string url, string bearer, string body = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            if (body != null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return request;
        }

        private static string Submission(string matchId, int finalFrame = 500,
            params (string PlayerId, string AccountId, int Kills, int Deaths)[] players)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"requestId\":\"r-").Append(matchId)
              .Append("\",\"matchId\":\"").Append(matchId)
              .Append("\",\"seed\":7,\"finalFrame\":").Append(finalFrame)
              .Append(",\"endReason\":1,\"gameplayEndReason\":2,\"winnerEntityId\":999,\"players\":[");
            for (int i = 0; i < players.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"playerId\":\"").Append(players[i].PlayerId)
                  .Append("\",\"accountId\":\"").Append(players[i].AccountId)
                  .Append("\",\"kills\":").Append(players[i].Kills)
                  .Append(",\"deaths\":").Append(players[i].Deaths)
                  .Append(",\"delta\":0}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        [SkippableFact]
        public async Task 提交_同一局一百次_只落一条_归档幂等()
        {
            SkipIfUnavailable();
            (WebApplication app, string baseAddress) =
                await StartHostAsync(_fixture.CreateDatabaseName());
            string token = app.Services.GetRequiredService<AccessTokenService>().Issue("acc-idem-1", out _);
            string key = Convert.ToBase64String(InstanceKey);
            string matchId = "m-idem-" + Guid.NewGuid().ToString("N");
            string body = Submission(matchId, players: new[] { ("p0", "acc-idem-1", 5, 2), ("p1", "acc-idem-2", 1, 5) });

            using var http = new HttpClient();
            for (int i = 0; i < 100; i++)
            {
                HttpResponseMessage response = await http.SendAsync(
                    Authorized(HttpMethod.Post, baseAddress + "/matches/result", key, body));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                JsonElement json = await ReadJsonAsync(response);
                Assert.Equal(matchId, json.GetProperty("matchId").GetString());
                Assert.Equal(i > 0, json.GetProperty("duplicate").GetBoolean());   // 首次 false，其后全部 duplicate
            }

            // 查询面：只一条（第 100 次的请求以同一 token 查询——账号维度）
            HttpResponseMessage query = await http.SendAsync(
                Authorized(HttpMethod.Get, baseAddress + "/matches", token));
            Assert.Equal(HttpStatusCode.OK, query.StatusCode);
            JsonElement queryJson = await ReadJsonAsync(query);
            JsonElement matches = queryJson.GetProperty("matches");
            int mine = 0;
            for (int i = 0; i < matches.GetArrayLength(); i++)
                if (matches[i].GetProperty("matchId").GetString() == matchId) mine++;
            Assert.Equal(1, mine);

            Ops ops = app.Services.GetRequiredService<Ops>();
            Assert.Equal(1, ops.MatchResultsStored);
            Assert.Equal(99, ops.MatchResultsDuplicates);
        }

        [SkippableFact]
        public async Task 查询_按账号_完成时刻倒序_限长生效()
        {
            SkipIfUnavailable();
            (WebApplication app, string baseAddress) =
                await StartHostAsync(_fixture.CreateDatabaseName());
            string token = app.Services.GetRequiredService<AccessTokenService>().Issue("acc-q-1", out _);
            string otherToken = app.Services.GetRequiredService<AccessTokenService>().Issue("acc-q-other", out _);
            string key = Convert.ToBase64String(InstanceKey);

            using var http = new HttpClient();
            foreach ((string matchId, int kills) in new[] { ("m-q-1", 3), ("m-q-2", 9) })
            {
                HttpResponseMessage response = await http.SendAsync(Authorized(HttpMethod.Post,
                    baseAddress + "/matches/result", key,
                    Submission(matchId, players: new[] { ("p0", "acc-q-1", kills, 1), ("p1", "acc-q-other", 0, 2) })));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await Task.Delay(20);   // 完成时刻拉开（存储端时钟赋值，毫秒级）
            }

            // 我的最近 1 局 = 后提交的 m-q-2（倒序），字段同源
            JsonElement newest = await ReadJsonAsync(await http.SendAsync(
                Authorized(HttpMethod.Get, baseAddress + "/matches?limit=1", token)));
            JsonElement first = newest.GetProperty("matches")[0];
            Assert.Equal(1, newest.GetProperty("matches").GetArrayLength());
            Assert.Equal("m-q-2", first.GetProperty("matchId").GetString());
            Assert.Equal(9, first.GetProperty("kills").GetInt32());
            Assert.Equal(1, first.GetProperty("deaths").GetInt32());
            Assert.Equal(1, first.GetProperty("endReason").GetInt32());
            Assert.Equal(2, first.GetProperty("gameplayEndReason").GetInt32());
            Assert.True(first.GetProperty("finishedAtMs").GetInt64() > 0);

            // 我的全量 = 两局；他人的视角只看到自己的
            JsonElement mine = await ReadJsonAsync(await http.SendAsync(
                Authorized(HttpMethod.Get, baseAddress + "/matches", token)));
            Assert.Equal(2, mine.GetProperty("matches").GetArrayLength());

            JsonElement other = await ReadJsonAsync(await http.SendAsync(
                Authorized(HttpMethod.Get, baseAddress + "/matches", otherToken)));
            Assert.Equal(2, other.GetProperty("matches").GetArrayLength());   // 他在两局都有条目（kills=0）
            Assert.Equal(0, other.GetProperty("matches")[0].GetProperty("kills").GetInt32());
        }

        [SkippableFact]
        public async Task 鉴权与形状_稳定错误码()
        {
            SkipIfUnavailable();
            (WebApplication app, string baseAddress) =
                await StartHostAsync(_fixture.CreateDatabaseName());
            string token = app.Services.GetRequiredService<AccessTokenService>().Issue("acc-a-1", out _);

            using var http = new HttpClient();

            // 提交：错实例密钥 → 401；坏载荷（players 空）→ 400 一次报全
            HttpResponseMessage wrongKey = await http.SendAsync(Authorized(HttpMethod.Post,
                baseAddress + "/matches/result", Convert.ToBase64String(Filled(0x99)),
                Submission("m-a-1", players: new[] { ("p0", "acc-a-1", 1, 0) })));
            Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
            Assert.Contains("profile.instance-unauthorized", await wrongKey.Content.ReadAsStringAsync());

            string key = Convert.ToBase64String(InstanceKey);
            HttpResponseMessage emptyPlayers = await http.SendAsync(Authorized(HttpMethod.Post,
                baseAddress + "/matches/result", key,
                "{\"requestId\":\"r\",\"matchId\":\"m-a-2\",\"players\":[]}"));
            Assert.Equal(HttpStatusCode.BadRequest, emptyPlayers.StatusCode);
            Assert.Contains("profile.invalid-request", await emptyPlayers.Content.ReadAsStringAsync());

            HttpResponseMessage missingAccount = await http.SendAsync(Authorized(HttpMethod.Post,
                baseAddress + "/matches/result", key,
                "{\"requestId\":\"r\",\"matchId\":\"m-a-3\",\"players\":[{\"playerId\":\"p0\",\"kills\":0,\"deaths\":0}]}"));
            Assert.Equal(HttpStatusCode.BadRequest, missingAccount.StatusCode);
            Assert.Contains("accountId", await missingAccount.Content.ReadAsStringAsync());

            // 查询：无令牌 → 401；坏 limit → 400
            HttpResponseMessage noToken = await http.GetAsync(baseAddress + "/matches");
            Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
            Assert.Contains("auth.unauthorized", await noToken.Content.ReadAsStringAsync());

            HttpResponseMessage badLimit = await http.SendAsync(Authorized(HttpMethod.Get,
                baseAddress + "/matches?limit=0", token));
            Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
        }

        [SkippableFact]
        public async Task 闭环_房间Outbox管道接真Meta_重启续投_重投出一份()
        {
            SkipIfUnavailable();
            (WebApplication app, string baseAddress) =
                await StartHostAsync(_fixture.CreateDatabaseName());
            string token = app.Services.GetRequiredService<AccessTokenService>().Issue("acc-loop-1", out _);

            string dir = Path.Combine(Path.GetTempPath(), "litegame_loop_" + Guid.NewGuid().ToString("N"));
            string journal = Path.Combine(dir, "settlements.jsonl");
            string matchId = "m-loop-" + Guid.NewGuid().ToString("N");
            var summary = new MatchResultSummary(matchId, seed: 11L, finalFrame: 321, ShutdownReason.KillLimit,
                new[] { 0, 1 }, winnerEntityId: 42L, MatchEndReason.KillLimit,
                new[] { new PlayerMatchResult(0, 42L, 7, 1), new PlayerMatchResult(1, 43L, 1, 7) });
            var serviceSettings = new SettlementSubmitService.Settings
            {
                Url = baseAddress + "/matches/result",
                InstanceKey = InstanceKey,
                IntervalMs = 5_000,
                MaxBackoffMs = 60_000,
                RequestTimeoutMs = 5_000,
            };

            try
            {
                // 第一段：Outbox 入盒（带席位账号映射）→ 管道提交（真 HTTP）→ 标记完成
                using (var outbox = FileSettlementOutbox.Open(journal, 16))
                using (var service = new SettlementSubmitService(serviceSettings, outbox, _ => { }))
                {
                    Assert.Equal(SettlementOutboxResult.Appended,
                        outbox.Enqueue(summary, new[] { "acc-loop-1", "acc-loop-2" }));
                    Assert.Equal(SettlementSubmitStep.Submitted, service.TrySubmitNext(out string error));
                    Assert.Null(error);
                    Assert.Equal(0, outbox.Count);
                    outbox.Flush();   // 排空第 4 步收口：完成标记压实（判重集合收敛为待提交面）
                }

                // "重启"：日志重放——已标记条目不再待提交（不重投）
                using (var restarted = FileSettlementOutbox.Open(journal, 16))
                {
                    Assert.Equal(0, restarted.Count);

                    // 响应丢失的重投形态（Meta 已收但标记未及持久）：同局再入盒 → 管道再提交 →
                    // Meta 幂等命中 duplicate → 仍标记完成；存储面永远只有一份
                    Assert.Equal(SettlementOutboxResult.Appended,
                        restarted.Enqueue(summary, new[] { "acc-loop-1", "acc-loop-2" }));
                    using var service = new SettlementSubmitService(serviceSettings, restarted, _ => { });
                    Assert.Equal(SettlementSubmitStep.Submitted, service.TrySubmitNext(out _));
                    Assert.Equal(0, restarted.Count);
                }

                // 查询面核对：一份、字段来自首次落库
                using var http = new HttpClient();
                JsonElement matches = await ReadJsonAsync(await http.SendAsync(
                    Authorized(HttpMethod.Get, baseAddress + "/matches", token)));
                int count = 0;
                int kills = -1;
                for (int i = 0; i < matches.GetProperty("matches").GetArrayLength(); i++)
                {
                    JsonElement item = matches.GetProperty("matches")[i];
                    if (item.GetProperty("matchId").GetString() != matchId) continue;
                    count++;
                    kills = item.GetProperty("kills").GetInt32();
                }
                Assert.Equal(1, count);
                Assert.Equal(7, kills);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
