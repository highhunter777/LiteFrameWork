using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using LiteTesting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MetaServer.IntegrationTests
{
    /// <summary>
    /// 宿主装配接真存储的端到端 L3（Meta 专项 §14 L3"真实 HTTP + Mongo 测试容器"、
    /// 框架先行 §6 末条"生产配置缺真实依赖时拒绝启动或拒绝相应功能，不能悄悄退回 fake"）。
    /// 走 <see cref="MetaHost.Build"/>——测试与生产共用同一装配路径。
    /// </summary>
    [Collection("Mongo")]
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MetaHostPersistenceL3Tests : IAsyncLifetime
    {
        private readonly MongoFixture _fixture;
        private readonly List<WebApplication> _hosts = new List<WebApplication>();

        public MetaHostPersistenceL3Tests(MongoFixture fixture)
        {
            _fixture = fixture;
        }

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

        public Task InitializeAsync() => Task.CompletedTask;

        private void SkipIfUnavailable()
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");
        }

        private async Task<string> StartHostAsync(
            string databaseName, string bind, string connectionString = null)
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":BindAddress"] = bind,
                [MetaHost.ConfigSection + ":MongoConnectionString"] =
                    connectionString ?? "mongodb://127.0.0.1:27017",
                [MetaHost.ConfigSection + ":MongoDatabaseName"] = databaseName,
            };

            WebApplication app = MetaHost.Build(Array.Empty<string>(), overrides);
            MetaHost.WireGracefulShutdown(app);
            _hosts.Add(app);   // 先登记再启动——启动抛出也能在本用例收尾清理
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
                return address.TrimEnd('/');
            }
            throw new InvalidOperationException("未取得监听地址");
        }

        private static HttpContent Body(string json) =>
            new StringContent(json, Encoding.UTF8, "application/json");

        [SkippableFact]
        public async Task 装配真存储_ready就绪()
        {
            SkipIfUnavailable();
            string database = _fixture.CreateDatabaseName();
            string baseAddress = await StartHostAsync(database, "http://127.0.0.1:0");

            using var http = new HttpClient();
            using HttpResponseMessage ready = await http.GetAsync(baseAddress + "/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal("ready", await ready.Content.ReadAsStringAsync());
        }

        [SkippableFact]
        public async Task 样例端点_真实全链_首次接受_重复返回首次()
        {
            SkipIfUnavailable();
            string database = _fixture.CreateDatabaseName();
            string baseAddress = await StartHostAsync(database, "http://127.0.0.1:0");
            string body = "{\"OperationId\":\"op-e2e-1\",\"PlayerId\":\"p1\",\"MatchId\":\"m1\","
                          + "\"SettlementType\":\"settle\",\"ExpectedRevision\":0,\"Delta\":10}";

            using var http = new HttpClient();
            using HttpResponseMessage first = await http.PostAsync(
                baseAddress + "/sample/settlement", Body(body));
            using HttpResponseMessage duplicate = await http.PostAsync(
                baseAddress + "/sample/settlement", Body(body));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            string firstJson = await first.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"accepted\"", firstJson);
            Assert.Contains("\"appliedRevision\":1", firstJson);
            Assert.Contains("\"balanceAfter\":10", firstJson);

            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            string duplicateJson = await duplicate.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"duplicate\"", duplicateJson);
            Assert.Contains("\"balanceAfter\":10", duplicateJson);   // 首次快照，非重算
        }

        [SkippableFact]
        public async Task 样例端点_修订冲突_409携带实际修订号()
        {
            SkipIfUnavailable();
            string database = _fixture.CreateDatabaseName();
            string baseAddress = await StartHostAsync(database, "http://127.0.0.1:0");
            string firstBody = "{\"OperationId\":\"op-1\",\"PlayerId\":\"p1\",\"MatchId\":\"m1\","
                               + "\"SettlementType\":\"settle\",\"ExpectedRevision\":0,\"Delta\":5}";
            string conflictBody = "{\"OperationId\":\"op-2\",\"PlayerId\":\"p1\",\"MatchId\":\"m2\","
                                  + "\"SettlementType\":\"settle\",\"ExpectedRevision\":9,\"Delta\":5}";

            using var http = new HttpClient();
            using HttpResponseMessage first = await http.PostAsync(
                baseAddress + "/sample/settlement", Body(firstBody));
            using HttpResponseMessage conflict = await http.PostAsync(
                baseAddress + "/sample/settlement", Body(conflictBody));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            string json = await conflict.Content.ReadAsStringAsync();
            Assert.Contains("revision-conflict", json);
            Assert.Contains("\"actual\":1", json);
        }

        [SkippableFact]
        public async Task 未配置存储_样例端点503_拒绝相应功能()
        {
            // 功能关闭：不注册持久化服务——端点显式 503，不悄悄退回替身
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:0",
            };
            WebApplication app = MetaHost.Build(Array.Empty<string>(), overrides);
            MetaHost.WireGracefulShutdown(app);
            _hosts.Add(app);
            await app.StartAsync();

            var server = app.Services.GetRequiredService<IServer>();
            string baseAddress = null;
            foreach (string address in server.Features.Get<IServerAddressesFeature>().Addresses)
            {
                baseAddress = address.TrimEnd('/');
                break;
            }

            using var http = new HttpClient();
            using HttpResponseMessage response = await http.PostAsync(
                baseAddress + "/sample/settlement",
                Body("{\"OperationId\":\"op-x\",\"PlayerId\":\"p\",\"MatchId\":\"m\","
                     + "\"SettlementType\":\"s\",\"ExpectedRevision\":0,\"Delta\":1}"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("store-not-configured", await response.Content.ReadAsStringAsync());
        }

        [SkippableFact]
        public async Task Mongo不可达_启动迁移失败_宿主拒绝启动()
        {
            SkipIfUnavailable();
            // fail-closed（§9.1）：配置了存储但不可达 → 启动迁移失败 → StartAsync 抛出。
            // 用一个必然无人监听的端口
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await StartHostAsync(
                    _fixture.CreateDatabaseName(),
                    "http://127.0.0.1:0",
                    connectionString: "mongodb://127.0.0.1:59999");
            });
        }

        [SkippableFact]
        public async Task 非法命令_400携带违规清单()
        {
            SkipIfUnavailable();
            string database = _fixture.CreateDatabaseName();
            string baseAddress = await StartHostAsync(database, "http://127.0.0.1:0");

            using var http = new HttpClient();
            using HttpResponseMessage response = await http.PostAsync(
                baseAddress + "/sample/settlement",
                Body("{\"OperationId\":\" \",\"PlayerId\":\"p\",\"MatchId\":\"m\","
                     + "\"SettlementType\":\"s\",\"ExpectedRevision\":-1,\"Delta\":1}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            Assert.Contains("invalid-command", json);
            Assert.Contains("不得为空", json);
            Assert.Contains("不得为负", json);
        }
    }
}
