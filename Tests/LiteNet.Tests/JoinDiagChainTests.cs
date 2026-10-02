using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LiteFramework;
using LiteNet;
using LiteNet.Diagnostics;
using LiteNet.Transport;
using LiteTesting;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// **全链诊断样例**（《框架先行建设与业务接入专项设计》§8 准入项「可诊断」：
    /// "一次注入失败可关联 Build/内容、事务、会话/房间并定位阶段"——证据类型：受控日志/错误码/诊断产物）。
    ///
    /// 形态：真 <see cref="ServerHost"/> + 真 KCP 客户端（装置同 <see cref="MultiRoomTransportTests"/>），
    /// **一次注入** = 客户端携错误 buildHash 进房（版本红线）。断言四件事：
    /// ① **阶段定位**：服务端记录 stage=<see cref="DiagStage.Build"/>、code=<see cref="DiagCode.JoinRejectedBuildHash"/>；
    /// ② **两端关联**：客户端进房记录与①②**同键**（<see cref="DiagTrace.JoinKey"/> 单源构造）；
    /// ③ **内容/事务关联**：客户端记录 detail 里带真实 <see cref="ActivationTransactionStore"/> 的事务 ID，
    ///    与事务面记录（<see cref="DiagCode.ActivationFailed"/>）互相可指认；
    /// ④ **诊断产物**：环境变量 <c>LITETEST_ARTIFACTS</c> 存在时把全量记录落盘（一行一条，稳定格式）。
    ///
    /// 边界（如实）：内容/事务面的**生产接线点**是组合根（客户端为内容模块/PatchRunner；本样例即组合根），
    /// Core 只暴露 <see cref="IDiagRecordSink"/> 端口——生产装配随 L2 核证批补充。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class JoinDiagChainTests : IDisposable
    {
        private const int Port = 28891;
        private const string Config = @"{
            ""port"": 28891, ""max_rooms"": 2, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private readonly ServerHost _host;
        private readonly List<KcpTransportClient> _transports = new List<KcpTransportClient>();

        public JoinDiagChainTests()
        {
            _host = new ServerHost(new KcpTransportServer(), null, null, null, RoomServerConfig.Parse(Config));
            DiagTrace.ResetForTesting();
        }

        public void Dispose()
        {
            foreach (var t in _transports) t.Dispose();
            _host.Dispose();
        }

        [Fact]
        public void 一次注入失败_版本红线_两端同键_阶段可定位_事务可关联()
        {
            // ---- 内容/事务面：一次真实的激活事务失败（组合根接线，见类注释边界）----
            var store = new ActivationTransactionStore(new MemoryActivationIO(), () => 1000)
            {
                TransactionIdFactory = () => "txn-diag-1",
                DiagSink = new DiagSinkAdapter(),
            };
            store.BeginCandidate("rel-7");
            store.MarkPendingActivation();
            store.RecordFailure("健康检查失败：入口页面未打开");

            // ---- 会话/房间面：真客户端携错误 buildHash 进房（本样例的**唯一注入**）----
            var transport = ConnectClient();
            var client = new RoomClient(transport)
            {
                ContentContext = $"release=rel-7;gen=3;txn={store.Current.TransactionId}",
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

            // ③ 内容/事务关联：两条记录互相可指认（客户端 detail 里的 txn == 事务记录键里的 txn）
            var txn = Find(DiagCode.ActivationFailed, "txn-diag-1");
            Assert.Equal(DiagStage.Txn, txn.Stage);
            Assert.Contains("release=rel-7", txn.Key);
            Assert.Contains("txn=txn-diag-1", txn.Key);
            Assert.Equal("健康检查失败：入口页面未打开", txn.Detail);
            Assert.Contains(store.Current.TransactionId, attempt.Detail);

            // 会话面事实：未获应答即断开 → 客户端侧记录（与①②同键）
            var noAck = Find(DiagCode.JoinNoAckDisconnected, "deadbeef-deadbeef");
            Assert.Equal(serverReject.Key, noAck.Key);
            Assert.Equal(-1, client.PlayerId);   // 未进房（未获 JoinAck）

            // ④ 诊断产物：有产物目录就落盘（一行一条；环境未设时跳过——本地/CI 均不假绿）
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

        [Fact]
        public void 内容激活事务中断恢复_记录带事务ID与恢复结论()
        {
            var store = new ActivationTransactionStore(new MemoryActivationIO(), () => 1000)
            {
                TransactionIdFactory = () => "txn-recover-1",
                DiagSink = new DiagSinkAdapter(),
            };
            store.BeginCandidate("rel-8");
            store.MarkPendingActivation();

            store.RecoverOnStartup();      // 模拟"待激活态被中断后重启"的恢复决策

            var recovered = Find(DiagCode.ActivationRecovered, "txn-recover-1");
            Assert.Equal(DiagStage.Txn, recovered.Stage);
            Assert.Contains("release=rel-8", recovered.Key);
            Assert.Contains("txn=txn-recover-1", recovered.Key);
            Assert.Contains("rel-8", recovered.Detail);
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
        /// 在全局诊断环里找**本样例注入**的记录：<see cref="DiagTrace"/> 是进程级静态环（容量 64），
        /// 其它真实传输用例类并行运行时也会写同码记录（各 xunit 类并行）——因此必须按**注入标记**
        /// （错误的 buildHash / 本样例的 txn）过滤，否则会取到别的用例的记录（跨类串读）。
        /// </summary>
        private static DiagEvent Find(string code, string marker)
            => DiagTrace.Recent.First(e => e.Code == code
                && ((e.Key != null && e.Key.Contains(marker)) || (e.Detail != null && e.Detail.Contains(marker))));

        /// <summary>组合根接线：Core 诊断端口 → 结构化记录面（生产形态由宿主/Bootstrap 装同款适配）。</summary>
        private sealed class DiagSinkAdapter : IDiagRecordSink
        {
            public void Record(string stage, string code, string key, string detail)
                => DiagTrace.Emit(stage, code, key, detail);
        }

        /// <summary>内存激活记录（真存储由 Unity 侧 FileSys/JSON 适配；本样例只验诊断链）。</summary>
        private sealed class MemoryActivationIO : IActivationRecordIO
        {
            private ActivationRecord _record;
            public ActivationRecord TryLoad() => _record;
            public void Save(ActivationRecord record) => _record = record;
        }
    }
}