using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LiteNet;
using LiteNet.Diagnostics;
using LiteNet.Transport;
using LiteTesting;
using RoomServer;
using Xunit;
using LiteSim.Tests;

using LiteSync.Client;
namespace LiteNet.Tests
{
    /// <summary>
    /// 真实网络诊断样例：版本拒绝与客户端连接事件按关联键串联，并保留调用方内容上下文。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class JoinDiagChainTests : IDisposable
    {
        private int Port => _host.BoundPort;   // 动态端口：宿主绑定后回读（Start(0) 由系统分配）
        private const string Config = @"{
            ""port"": 0, ""max_rooms"": 2, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private readonly ServerHost _host;
        private readonly List<KcpTransportClient> _transports = new List<KcpTransportClient>();

        public JoinDiagChainTests()
        {
            _host = new ServerHost(new KcpTransportServer(), null, null, null, RoomServerConfig.Parse(Config), map: TestMaps.Greybox());
            DiagTrace.ResetForTesting();
        }

        public void Dispose()
        {
            foreach (var t in _transports) t.Dispose();
            _host.Dispose();
        }

        [Fact]
        public void 一次注入失败_版本红线_两端同键_阶段与上下文可定位()
        {
            // ---- 会话/房间面：真客户端携错误 buildHash 进房（本样例的**唯一注入**）----
            var transport = ConnectClient();
            var client = new RoomClient(transport)
            {
                ContentContext = "release=rel-7;gen=3;txn=txn-diag-1",
            };
            client.SendJoin("two", "harness", "deadbeef-deadbeef");

            Assert.True(WaitFor(() => DiagTrace.Recent.Any(e => e.Code == DiagCode.JoinRejectedBuildHash)),
                "服务器应在 5s 内留下版本红线拒绝记录");

            client.Disconnect();
            Pump(200);

            // ① 阶段定位：Build 段（版本红线），detail 带两端哈希对照
            var serverReject = Find(DiagCode.JoinRejectedBuildHash, "deadbeef-deadbeef");
            Assert.Equal(DiagStage.Build, serverReject.Stage);
            Assert.Contains(ServerHost.ServerBuildHash, serverReject.Detail);
            Assert.Contains("deadbeef-deadbeef", serverReject.Detail);

            // ② 两端关联：客户端进房记录与服务器拒绝记录**同键**
            var attempt = Find(DiagCode.JoinAttempt, "deadbeef-deadbeef");
            Assert.Equal(DiagStage.Session, attempt.Stage);
            Assert.Equal(serverReject.Key, attempt.Key);

            Assert.Contains(client.ContentContext, attempt.Detail);

            // 会话面事实：未获应答即断开 → 客户端侧记录（与①②同键）
            var noAck = Find(DiagCode.JoinNoAckDisconnected, "deadbeef-deadbeef");
            Assert.Equal(serverReject.Key, noAck.Key);
            Assert.Equal(-1, client.PlayerId);   // 未进房（未获 JoinAck）

            // ③ 诊断产物：有产物目录就落盘（一行一条；环境未设时跳过——本地/CI 均不假绿）
            string artifacts = Environment.GetEnvironmentVariable("LITETEST_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                string path = Path.Combine(artifacts, "diag-trace-join-chain.txt");
                File.WriteAllText(path, DiagTrace.Format());
                Assert.True(File.Exists(path), "诊断产物应已落盘");
                Assert.Contains(DiagCode.JoinRejectedBuildHash, File.ReadAllText(path));
            }
        }

        // ---- 装置（同 MultiRoomTransportTests）----

        private KcpTransportClient ConnectClient()
        {
            var client = new KcpTransportClient();
            _transports.Add(client);
            var connected = new ManualResetEventSlim(false);
            client.OnConnected += () => connected.Set();
            client.Connect("127.0.0.1", Port);
            var watch = Stopwatch.StartNew();
            while (!connected.Wait(10) && watch.ElapsedMilliseconds < 5000) PumpOne();
            Assert.True(client.Connected, "客户端 5s 内未完成握手");
            return client;
        }

        private void PumpOne()
        {
            _host.Pump();
            foreach (var t in _transports) { t.TickIncoming(); t.TickOutgoing(); }
            Thread.Sleep(10);
        }

        private void Pump(int ms)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms) PumpOne();
        }

        private bool WaitFor(Func<bool> predicate, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return true;
                PumpOne();
            }
            return predicate();
        }

        /// <summary>
        /// 在全局诊断环里找**本样例注入**的记录：<see cref="DiagTrace"/> 是进程级静态环（容量 64）。
        /// </summary>
        private static DiagEvent Find(string code, string marker)
            => DiagTrace.Recent.First(e => e.Code == code
                && ((e.Key != null && e.Key.Contains(marker)) || (e.Detail != null && e.Detail.Contains(marker))));

    }
}