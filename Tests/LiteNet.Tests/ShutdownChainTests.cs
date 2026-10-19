using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteNet.Transport;
using LiteTesting;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;
using LiteSim.Tests;

namespace LiteNet.Tests
{
    /// <summary>
    /// 关闭链健壮性（服务端审计新增批）：三件事——① 后台 HTTP 客户端的停止/释放竞态
    /// （在途请求比停止等待长时，旧形态会在**存活线程**上释放停止位，线程回来后在已释放的
    /// ManualResetEventSlim 上等 → <see cref="ObjectDisposedException"/> 杀进程）；
    /// ② <see cref="ServerLoop.Stop"/> 如实返回"线程是否已退出"（旧形态丢弃 <c>Join</c> 结果，
    /// 调用方会把仍在 <c>Pump</c> 的循环当作已停而继续释放）；③ 排空完成判据的既有语义不回退
    /// （DrainComplete 改走注册表快照后仍按"所有在册房间终态"判定，见 <see cref="DrainTests"/>）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Unit)]
    public sealed class ShutdownChainTests
    {
        private static readonly byte[] Key = Filled(0x22);

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        /// <summary>阻塞到测试放行的处理器，**刻意不响应取消令牌**——复现"请求超时（5s 级）&gt;
        /// 停止等待（2s）"的真实形态：取消只在处理器尊重令牌时才有用，故停止路径必须另有兜底。</summary>
        private sealed class BlockingHandler : HttpMessageHandler
        {
            private readonly ManualResetEventSlim _entered;
            private readonly ManualResetEventSlim _release;

            public BlockingHandler(ManualResetEventSlim entered, ManualResetEventSlim release)
            {
                _entered = entered;
                _release = release;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                _entered.Set();
                _release.Wait(TimeSpan.FromSeconds(30));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"heartbeatTtlMs\":30000}", Encoding.UTF8, "application/json"),
                });
            }
        }

        private static LobbyRegistrationClient.Settings Settings()
        {
            return new LobbyRegistrationClient.Settings
            {
                Url = "http://127.0.0.1:9/lobby/instances/register",
                InstanceId = "room-1",
                InstanceKey = Key,
                AdvertiseHost = "203.0.113.7",
                HeartbeatIntervalMs = 60_000,          // 心跳只发首跳：本用例只关心停止/释放路径
                RequestTimeoutMs = 30_000,             // 远长于用例时长：排除 HttpClient 超时的干扰
            };
        }

        private static LobbyRegistrationClient.Snapshot Snapshot()
        {
            return new LobbyRegistrationClient.Snapshot
            {
                BuildHash = "hash-x",
                MaxRooms = 8,
                RoomCount = 1,
                MaxPlayers = 16,
                PlayerCount = 1,
                Port = 17777,
            };
        }

        /// <summary>
        /// 停止超时（在途请求未返回）时 Dispose 必须：**不抛**、**不释放停止位/HttpClient**，
        /// 且放行请求后线程能自行退出——旧形态在这里释放停止位，线程回到循环后的 WaitStop 抛
        /// ObjectDisposedException（后台线程未处理异常 = 测试宿主进程直接崩）。
        /// </summary>
        [Fact]
        public void 停止超时_不释放停止位_放行后在途线程安全退出()
        {
            var entered = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            var client = new LobbyRegistrationClient(Settings(), Snapshot, _ => { },
                new BlockingHandler(entered, release));

            client.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "后台线程必须已进入在途请求");

            client.Dispose();       // 停止等待 2s 到期（在途请求仍挂着）——此处不得释放停止位

            release.Set();          // 放行在途请求：线程应自行走到停止判据并退出
            bool stopped = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!stopped && watch.ElapsedMilliseconds < 10_000)
                stopped = client.Stop();

            Assert.True(stopped, "放行在途请求后，后台线程必须在有界等待内退出（不得抛未处理异常）");
        }

        /// <summary>重复 Stop/Dispose 幂等：第二次起不得因已释放的停止位/令牌抛异常。</summary>
        [Fact]
        public void 停止与释放_重复调用幂等()
        {
            var handler = new StaticHandler();
            var client = new LobbyRegistrationClient(Settings(), Snapshot, _ => { }, handler);

            Assert.True(client.Stop(), "未启动即停止：无线程可等，按已停返回");
            client.Dispose();
            client.Dispose();
            Assert.True(client.Stop());
        }

        private sealed class StaticHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
        }

        // ---- ServerLoop.Stop：返回值即真相 ----

        private static ServerHost NewHost()
        {
            var host = new ServerHost(new KcpTransportServer(),
                new RoomConfig { Map = TestMaps.Greybox(), Port = 0 }, map: TestMaps.Greybox());
            host.Ops.PrintEnabled = false;
            return host;
        }

        [Fact]
        public void 权威循环_空载时Stop返回已退出()
        {
            using var host = NewHost();
            var loop = new ServerLoop(host);

            loop.Start();
            Assert.True(loop.Stop(5_000), "空载循环必须在有界等待内退出");
        }

        [Fact]
        public void 权威循环_帧体阻塞时Stop如实返回未退出_放行后可再停()
        {
            using var host = NewHost();
            using var gate = new ManualResetEventSlim(false);
            using var entered = new ManualResetEventSlim(false);
            var loop = new ServerLoop(host, () =>
            {
                entered.Set();
                gate.Wait(TimeSpan.FromSeconds(20));
            });

            loop.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "帧体必须已进入");

            Assert.False(loop.Stop(200), "帧体阻塞 ⇒ Stop 必须如实返回未退出（调用方不得据此释放）");

            gate.Set();
            Assert.True(loop.Stop(10_000), "放行帧体后线程必须退出");
        }
    }
}
