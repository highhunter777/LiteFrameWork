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
                // 样例链路缺省关闭（P3"出生产构建"）——本夹具的用例就是样例端点的验证面，显式开启
                [MetaHost.ConfigSection + ":EnableSampleEndpoints"] = "true",
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
            // 409 走**领域体**（非 {code,messageKey,args} 错误体）：冲突项必须携带 expected/actual
            // **数字**供调用方修正修订号——与业务侧 409 的 MatchSettlementResponse 同类（P2 批二判据）
            Assert.Contains("revision-conflict", json);
            Assert.Contains("\"expected\":9", json);   // 请求带 9，实际为 1——两个数字都要回给调用方
            Assert.Contains("\"actual\":1", json);
        }

        [SkippableFact]
        public async Task 样例链路_默认不挂载404_显式开启且无存储则503()
        {
            const string sampleBody = "{\"OperationId\":\"op-x\",\"PlayerId\":\"p\",\"MatchId\":\"m\","
                                      + "\"SettlementType\":\"s\",\"ExpectedRevision\":0,\"Delta\":1}";

            // (a) **默认（= 生产形态）**：样例链路整体不存在——路由 404。
            //     断言的是"链路不在"，而不是"链路在但报 503"：后者会让生产暴露一个非业务面
            //     （P3"样例链路出生产构建"）。
            WebApplication offApp = MetaHost.Build(Array.Empty<string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:0",
                });
            MetaHost.WireGracefulShutdown(offApp);
            _hosts.Add(offApp);
            await offApp.StartAsync();
            using (var http = new HttpClient())
            {
                using HttpResponseMessage absent = await http.PostAsync(BaseAddress(offApp) + "/sample/settlement",
                    Body(sampleBody));
                Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            }

            // (b) 显式开启 + 未配置存储：端点存在但功能关闭——显式 503，不悄悄退回替身
            WebApplication onApp = MetaHost.Build(Array.Empty<string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:0",
                    [MetaHost.ConfigSection + ":EnableSampleEndpoints"] = "true",
                });
            MetaHost.WireGracefulShutdown(onApp);
            _hosts.Add(onApp);
            await onApp.StartAsync();

            using var client = new HttpClient();
            using HttpResponseMessage response = await client.PostAsync(BaseAddress(onApp) + "/sample/settlement",
                Body(sampleBody));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            // P2 批二：样例端点错误体并入全服单源 {code,messageKey,args}——"存储未配置"在
            // Lobby/Settlement/样例三处现在是**同一个体**（审计"同一 503 三种体"收口）
            string json = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"code\":\"profile.disabled\"", json);
            Assert.Contains("\"messageKey\":", json);
        }

        /// <summary>取宿主实际监听地址（端口 0 = 内核分配的形态）。</summary>
        private static string BaseAddress(WebApplication app)
        {
            foreach (string address in app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>().Addresses)
                return address.TrimEnd('/');
            throw new InvalidOperationException("宿主未监听任何地址");
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
            // 统一错误体（P2 批二）：稳定 code + 本地化 messageKey + 违规清单进 args
            Assert.Contains("\"code\":\"profile.invalid-request\"", json);
            Assert.Contains("\"messageKey\":", json);
            Assert.Contains("不得为空", json);
            Assert.Contains("不得为负", json);
        }
    }
}
