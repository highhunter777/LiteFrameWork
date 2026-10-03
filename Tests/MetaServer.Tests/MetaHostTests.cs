using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>把宿主跑在临时端口上，用完即停。<see cref="BaseAddress"/> 取内核分配的真实地址。</summary>
    public sealed class MetaHostFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;

        public string BaseAddress { get; }

        private MetaHostFixture(WebApplication app, string baseAddress)
        {
            _app = app;
            BaseAddress = baseAddress;
        }

        public static async Task<MetaHostFixture> StartAsync(Action<Dictionary<string, string>> overrides = null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 0 = 临时端口，避免测试端口冲突
                [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:0",
            };
            overrides?.Invoke(map);

            WebApplication app = MetaHost.Build(Array.Empty<string>(), map);
            MetaHost.WireGracefulShutdown(app);
            await app.StartAsync();

            var server = app.Services.GetRequiredService<IServer>();
            string address = string.Empty;
            foreach (string a in server.Features.Get<IServerAddressesFeature>().Addresses)
            {
                address = a;
                break;
            }

            return new MetaHostFixture(app, address.TrimEnd('/'));
        }

        public Ops Ops => _app.Services.GetRequiredService<Ops>();

        public MetaConfig Config => _app.Services.GetRequiredService<IOptions<MetaConfig>>().Value;

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// 宿主骨架端到端（《Meta 服务专项设计》§11.3 健康检查、§10 优雅关闭、§4.1 依据 5 测试技法）。
    ///
    /// L3：真实 Kestrel + 真实回环 HTTP。
    /// **不用 `WebApplicationFactory`/`TestServer`**——两者都需 `PackageReference`，
    /// 与 §4.1"不引入 NuGet Web 依赖"冲突；此处以临时端口 + `IServerAddressesFeature`
    /// 取回真实端口，再用 `HttpClient` 打真实回环，全程零 NuGet 且测的是真实监听栈。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MetaHostTests
    {
        [Fact]
        public async Task 健康端点_存活与就绪分别可用()
        {
            await using var host = await MetaHostFixture.StartAsync();
            using var http = new HttpClient();

            HttpResponseMessage live = await http.GetAsync(host.BaseAddress + "/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal("live", await live.Content.ReadAsStringAsync());

            HttpResponseMessage ready = await http.GetAsync(host.BaseAddress + "/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal("ready", await ready.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task drain后_存活仍200_就绪转503()
        {
            await using var host = await MetaHostFixture.StartAsync();
            using var http = new HttpClient();

            // drain 前就绪
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(host.BaseAddress + "/ready")).StatusCode);

            host.Ops.Draining = true;

            // §11.3 语义分工：live 只证进程事件循环可响应，**不因 drain 失败**（否则编排器会误杀而非摘流量）
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(host.BaseAddress + "/live")).StatusCode);
            // ready 必须转 503，让编排器摘掉流量
            HttpResponseMessage ready = await http.GetAsync(host.BaseAddress + "/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal("draining", await ready.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task 指标出口_暴露计数且不含高基数字段()
        {
            await using var host = await MetaHostFixture.StartAsync();
            using var http = new HttpClient();

            await http.GetAsync(host.BaseAddress + "/live");
            await http.GetAsync(host.BaseAddress + "/live");

            string metrics = await http.GetStringAsync(host.BaseAddress + "/metrics");

            Assert.Contains("meta_http_request_total", metrics);
            Assert.Contains("meta_draining", metrics);
            Assert.Contains("meta_uptime_seconds", metrics);

            // §11.2：标签禁用高基数字段。文本出口连这些词都不应出现。
            foreach (string highCardinality in new[] { "accountId", "playerId", "requestId", "matchId", "connectionId" })
            {
                Assert.DoesNotContain(highCardinality, metrics);
            }
        }

        [Fact]
        public async Task 请求计数_随请求增长且拒绝数按状态码记账()
        {
            await using var host = await MetaHostFixture.StartAsync();
            using var http = new HttpClient();

            long beforeReq = host.Ops.HttpRequests;
            long beforeRej = host.Ops.HttpRejected;

            await http.GetAsync(host.BaseAddress + "/live");
            await http.GetAsync(host.BaseAddress + "/ready");

            Assert.True(host.Ops.HttpRequests >= beforeReq + 2,
                $"请求计数未增长：before={beforeReq} after={host.Ops.HttpRequests}");

            // 4xx/5xx 必须进 HttpRejected，而不是留一个永远为 0 的占位字段
            await http.GetAsync(host.BaseAddress + "/nope");           // 404
            Assert.True(host.Ops.HttpRejected >= beforeRej + 1,
                $"拒绝计数未增长：before={beforeRej} after={host.Ops.HttpRejected}");
        }

        [Fact]
        public async Task 未知路由_返回404_不崩溃宿主()
        {
            await using var host = await MetaHostFixture.StartAsync();
            using var http = new HttpClient();

            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(host.BaseAddress + "/nope")).StatusCode);

            // 之后宿主仍正常服务
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(host.BaseAddress + "/live")).StatusCode);
        }

        [Fact]
        public async Task 多实例_临时端口互不冲突()
        {
            await using var a = await MetaHostFixture.StartAsync();
            await using var b = await MetaHostFixture.StartAsync();

            Assert.NotEqual(a.BaseAddress, b.BaseAddress);

            using var http = new HttpClient();
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(a.BaseAddress + "/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(b.BaseAddress + "/live")).StatusCode);
        }
    }

    /// <summary>
    /// 纯逻辑面：CLI 覆盖解析门禁与 Ops 并发计数（不起监听）。
    /// </summary>
    public sealed class MetaCliAndOpsTests
    {
        [Fact]
        public void 参数解析_合法覆盖_落入配置节()
        {
            IReadOnlyDictionary<string, string> overrides =
                MetaHost.ParseCliOverrides(new[] { "--bind", "http://127.0.0.1:6000" }, out string error);

            Assert.Null(error);
            Assert.Equal("http://127.0.0.1:6000", overrides["Meta:BindAddress"]);
        }

        [Theory]
        [InlineData(new[] { "--bnd", "x" }, "未知参数")]
        [InlineData(new[] { "--bind" }, "缺少值")]
        public void 参数解析_未知参数或缺值_报错不静默(string[] args, string expectedFragment)
        {
            MetaHost.ParseCliOverrides(args, out string error);

            Assert.NotNull(error);
            Assert.Contains(expectedFragment, error);
        }

        [Fact]
        public void Ops计数_并发不丢()
        {
            var ops = new Ops();
            const int threads = 8;
            const int perThread = 10_000;

            var barrier = new Barrier(threads);
            var tasks = new System.Threading.Tasks.Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = System.Threading.Tasks.Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < perThread; i++)
                    {
                        ops.CountRequest();
                        if (i % 10 == 0) ops.CountRejected();
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);

            Assert.Equal(threads * perThread, ops.HttpRequests);
            Assert.Equal(threads * perThread / 10, ops.HttpRejected);
        }
    }
}
