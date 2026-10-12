using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 结算 Outbox 文件语义 L3（《商业级通用服务端框架总设计》§11.3"本地持久 Outbox"、
    /// §12 优雅关闭第 4 步"刷新 Outbox 到持久介质"）。真实临时文件——重启恢复＝介质比进程活得久。
    ///
    /// **边界**：此处证明的是"冻结事实不因进程退出而丢、重启可重放"；提交管道（后台服务重试发送
    /// Profile.Apply）由后续环节承担。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class SettlementOutboxFileTests
    {
        private static string TempJournal()
        {
            return Path.Combine(Path.GetTempPath(), "litegame_outbox_" + Guid.NewGuid().ToString("N"),
                "settlements.journal");
        }

        private static MatchResultSummary Summary(string matchId, long seed = 42, int frame = 999)
        {
            return new MatchResultSummary(matchId, seed, frame, ShutdownReason.TimeLimit, new[] { 1, 2 });
        }

        private static void Cleanup(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        /// <summary>Outbox 流仍打开时的共享读（句柄 share=Read 只容 Read 访问，File.ReadAllLines 的 share 不兼容）。</summary>
        private static string[] ReadJournalLines(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var lines = new List<string>();
            string line;
            while ((line = reader.ReadLine()) != null) lines.Add(line);
            return lines.ToArray();
        }

        [Fact]
        public void 首次入盒_逐条落盘_文件可读()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-1")));
                }

                Assert.True(File.Exists(path));
                string content = File.ReadAllText(path);
                Assert.Contains("m-1", content);
                Assert.Contains("\"FinalFrame\":999", content);
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 重启恢复_新实例续接_待提交不丢()
        {
            string path = TempJournal();
            try
            {
                using (var first = FileSettlementOutbox.Open(path, 16))
                {
                    first.Enqueue(Summary("m-1", seed: 7, frame: 100));
                    first.Enqueue(Summary("m-2", seed: 8, frame: 200));
                }

                // "重启"：新实例、同一文件
                using (var restarted = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(2, restarted.Count);
                    IReadOnlyList<PendingSettlement> pending = restarted.ListPending();
                    Assert.Equal("m-1", pending[0].MatchId);
                    Assert.Equal(7, pending[0].Summary.Seed);
                    Assert.Equal(100, pending[0].Summary.FinalFrame);
                    Assert.Equal("m-2", pending[1].MatchId);

                    // 追加新条目接在既有内容之后（续接不覆盖）
                    Assert.Equal(SettlementOutboxResult.Appended, restarted.Enqueue(Summary("m-3")));
                    Assert.Equal(3, restarted.Count);
                }

                using (var third = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(3, third.Count);   // 三次"进程"共写 3 条，全部可重放
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 同matchId重复入盒_幂等不重写()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-1", seed: 7)));
                    Assert.Equal(SettlementOutboxResult.Duplicate, outbox.Enqueue(Summary("m-1", seed: 999)));
                    Assert.Equal(1, outbox.Count);
                }

                // 重启后判重集合从日志重建——跨进程幂等
                using (var restarted = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(SettlementOutboxResult.Duplicate, restarted.Enqueue(Summary("m-1", seed: 999)));
                    Assert.Equal(1, restarted.Count);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 容量满_显式拒绝()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 2))
                {
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-1")));
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-2")));
                    Assert.Equal(SettlementOutboxResult.RejectedFull, outbox.Enqueue(Summary("m-3")));
                    Assert.Equal(2, outbox.Count);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 崩溃半行_装载期跳过计数_重启不炸()
        {
            string path = TempJournal();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // 手工造一份"崩溃残留"：一条完好 + 一条半行 + 一条完好
                File.WriteAllLines(path, new[]
                {
                    "{\"MatchId\":\"m-1\",\"Seed\":7,\"FinalFrame\":10,\"EndReason\":1,\"SeatPlayerIds\":[1,2]}",
                    "{\"MatchId\":\"m-2\",\"Seed\":8,\"Fin",      // 崩溃半行（写盘中断形态）
                    "{\"MatchId\":\"m-3\",\"Seed\":9,\"FinalFrame\":30,\"EndReason\":1,\"SeatPlayerIds\":[1,2]}",
                });

                using (var outbox = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(2, outbox.Count);
                    Assert.Equal(1, outbox.SkippedCorruptLines);
                    Assert.Equal("m-1", outbox.ListPending()[0].MatchId);
                    Assert.Equal("m-3", outbox.ListPending()[1].MatchId);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void Flush幂等_多次调用不抛()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 16))
                {
                    outbox.Enqueue(Summary("m-1"));
                    outbox.Flush();
                    outbox.Flush();
                    outbox.Flush();
                    Assert.Equal(1, outbox.Count);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        // ---- 完成语义（§11.3"Outbox 标记完成"存储半步；容量按待提交计）----

        [Fact]
        public void 标记完成_释放容量_退出待提交面()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 2))
                {
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-1")));
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-2")));
                    Assert.Equal(SettlementOutboxResult.RejectedFull, outbox.Enqueue(Summary("m-3")));

                    Assert.True(outbox.TryMarkCompleted("m-1"));
                    Assert.Equal(1, outbox.Count);                  // 容量按待提交计——标记即释放
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-3")));

                    Assert.False(outbox.TryMarkCompleted("m-x"));   // 未知 matchId
                    Assert.False(outbox.TryMarkCompleted("m-1"));   // 已完成（幂等，不写盘）
                    Assert.Equal(2, outbox.Count);

                    IReadOnlyList<PendingSettlement> pending = outbox.ListPending();
                    Assert.Equal(2, pending.Count);
                    Assert.Equal("m-2", pending[0].MatchId);
                    Assert.Equal("m-3", pending[1].MatchId);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 重启恢复_完成态不复活()
        {
            string path = TempJournal();
            try
            {
                using (var first = FileSettlementOutbox.Open(path, 16))
                {
                    first.Enqueue(Summary("m-1"));
                    first.Enqueue(Summary("m-2"));
                    Assert.True(first.TryMarkCompleted("m-1"));
                }

                using (var restarted = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(1, restarted.Count);               // 完成标记行重放 → 不回到待提交面
                    Assert.Equal("m-2", restarted.ListPending()[0].MatchId);
                    Assert.Equal(SettlementOutboxResult.Duplicate,
                        restarted.Enqueue(Summary("m-1")));         // 未压实前判重仍含已完成
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void Flush压实_日志收敛为待提交_容量随之可用()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 16))
                {
                    outbox.Enqueue(Summary("m-1"));
                    outbox.Enqueue(Summary("m-2"));
                    outbox.Enqueue(Summary("m-3"));
                    Assert.True(outbox.TryMarkCompleted("m-1"));
                    Assert.True(outbox.TryMarkCompleted("m-2"));

                    outbox.Flush();                                  // 收口即压实
                    Assert.Equal(1, outbox.Compactions);
                    Assert.Equal(1, outbox.Count);
                    Assert.Equal(1, ReadJournalLines(path).Length); // 日志只剩待提交行（m-3）
                }

                using (var reopened = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(1, reopened.Count);
                    Assert.Equal("m-3", reopened.ListPending()[0].MatchId);

                    // 压实后判重集合收敛：已完成 matchId 重入盒作为新待提交接受——
                    // 重复提交不重复发奖由 Meta 台账唯一索引兜底（§11.2）
                    Assert.Equal(SettlementOutboxResult.Appended, reopened.Enqueue(Summary("m-1")));
                    Assert.Equal(2, reopened.Count);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 旧格式日志_无Kind行_按结算行重放()
        {
            string path = TempJournal();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new[]
                {
                    "{\"MatchId\":\"m-1\",\"Seed\":7,\"FinalFrame\":10,\"EndReason\":1,\"SeatPlayerIds\":[1,2]}",
                    "{\"MatchId\":\"m-2\",\"Seed\":8,\"FinalFrame\":20,\"EndReason\":1,\"SeatPlayerIds\":[1,2]}",
                });

                using (var outbox = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(2, outbox.Count);                  // 既有日志兼容：缺 Kind = 结算行
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-3")));
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 连续标记达阈值_自动压实_判重集合收敛()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 2048))
                {
                    for (int i = 0; i < 1024; i++)
                    {
                        Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-" + i)));
                        Assert.True(outbox.TryMarkCompleted("m-" + i));
                    }

                    Assert.True(outbox.Compactions >= 1);           // 达自动压实阈值即重写（长跑不无界）
                    Assert.Equal(0, outbox.Count);
                    Assert.Equal(0, ReadJournalLines(path).Length);

                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(Summary("m-final")));
                    Assert.Equal(1, outbox.Count);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        /// <summary>
        /// P0-1 回归：**入盒（权威循环线程）与标记/取待提交（提交后台线程）并发**——
        /// 全类曾零锁裸并发（<c>_entries/_seen/_completed/_stream</c>），是服务端唯一会造成真实介质损坏的缺陷。
        /// 断言面：① 无异常（裸并发下 <c>List</c>/<c>HashSet</c> 结构损坏或枚举中改动必抛）；
        /// ② 每条入盒都被接纳（无莫名 Duplicate/Failed）；③ 全部标记后待提交面归零；
        /// ④ **日志行无坏行**（并发追加若无临界区会出现交错半行——重启装载即报坏行）。
        /// </summary>
        [Fact]
        public void 并发入盒与标记_无结构损坏_账目自洽_日志无坏行()
        {
            string path = TempJournal();
            const int producers = 2, perProducer = 200, markers = 2;
            var failures = new List<Exception>();
            var results = new List<SettlementOutboxResult>();
            int finishedProducers = 0;
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 4096))
                {
                    var threads = new List<Thread>();
                    for (int p = 0; p < producers; p++)
                    {
                        int producer = p;
                        threads.Add(new Thread(() =>
                        {
                            try
                            {
                                for (int i = 0; i < perProducer; i++)
                                {
                                    SettlementOutboxResult r = outbox.Enqueue(Summary("m-" + producer + "-" + i));
                                    lock (results) results.Add(r);
                                }
                            }
                            catch (Exception ex) { lock (failures) failures.Add(ex); }
                            finally { Interlocked.Increment(ref finishedProducers); }
                        }));
                    }

                    // 提交后台线程形态（×2）：**全程与入盒重叠**地取快照 → 按 id 标记 → 收口/诊断读。
                    // 读写并发正是隐患暴露面：裸并发下 List<T>/HashSet<T> 的"枚举中改动"必抛或静默损坏。
                    for (int m = 0; m < markers; m++)
                    {
                        threads.Add(new Thread(() =>
                        {
                            try
                            {
                                long spins = 0;
                                while (Volatile.Read(ref finishedProducers) < producers)
                                {
                                    IReadOnlyList<PendingSettlement> pending = outbox.ListPending();
                                    if (pending.Count > 0) outbox.TryMarkCompleted(pending[0].MatchId);
                                    if ((++spins & 15) == 0) outbox.Flush();   // 收口路径（可与入盒并发）
                                    _ = outbox.Count;                          // 诊断读（Ops 行同款）
                                }
                            }
                            catch (Exception ex) { lock (failures) failures.Add(ex); }
                        }));
                    }

                    foreach (Thread t in threads) t.Start();
                    foreach (Thread t in threads) t.Join();

                    Assert.Empty(failures);                       // ① 无结构损坏/索引越界/枚举中改动类异常
                    Assert.Equal(producers * perProducer, results.Count);
                    Assert.All(results, r => Assert.Equal(SettlementOutboxResult.Appended, r));   // ② 全部接纳

                    // 收口：把剩余待提交标记完（提交方排空形态），待提交面必须精确归零
                    for (IReadOnlyList<PendingSettlement> left = outbox.ListPending(); left.Count > 0;
                         left = outbox.ListPending())
                    {
                        for (int i = 0; i < left.Count; i++) Assert.True(outbox.TryMarkCompleted(left[i].MatchId));
                    }
                    outbox.Flush();
                    Assert.Equal(0, outbox.Count);                // ③ 账目自洽（无幽灵/丢失条目）
                    Assert.Equal(producers * perProducer, results.Count);
                }

                // ④ 重放面：并发追加后的日志必须行行可解析（无交错半行）
                using (var reopened = FileSettlementOutbox.Open(path, 4096))
                {
                    Assert.Equal(0, reopened.SkippedCorruptLines);
                    Assert.Equal(0, reopened.Count);               // 全部已完成 → 无待提交（压实后日志为空）
                }
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void 弃置_标记行落盘_重开不复活_让位后续()
        {
            string path = TempJournal();
            try
            {
                using (var outbox = FileSettlementOutbox.Open(path, 16))
                {
                    outbox.Enqueue(Summary("m-1"));
                    outbox.Enqueue(Summary("m-2"));

                    Assert.True(outbox.TryAbandon("m-1", "HTTP 400"));
                    Assert.Equal(1, outbox.Abandoned);
                    Assert.Equal(1, outbox.Count);                     // 弃置即退出待提交面（容量释放）
                    Assert.Equal("m-2", outbox.ListPending()[0].MatchId);
                    Assert.False(outbox.TryAbandon("m-1", "HTTP 400")); // 已标记（幂等，不写盘）
                    Assert.False(outbox.TryAbandon("m-x", "HTTP 400")); // 未知 matchId

                    string[] lines = ReadJournalLines(path);
                    Assert.Contains(lines, l => l.Contains("\"Kind\":\"x\"") && l.Contains("m-1")
                        && l.Contains("HTTP 400"));
                }

                // "重启"：弃置由 "x" 标记行重建——不复活、不重投
                using (var restarted = FileSettlementOutbox.Open(path, 16))
                {
                    Assert.Equal(1, restarted.Abandoned);
                    Assert.Equal(1, restarted.Count);
                    Assert.Equal("m-2", restarted.ListPending()[0].MatchId);
                }
            }
            finally
            {
                Cleanup(path);
            }
        }
    }

    /// <summary>
    /// 结算 Outbox 宿主接线 L1（替身盒验证接线语义；文件语义归上组 L3）：
    /// SettlementReady → Enqueue 四态计数、未装配现状不变、满拒/失败不炸权威循环。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Unit)]
    public sealed class SettlementOutboxHostTests
    {
        private const string Config = @"{
            ""port"": 46011, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private static byte[] Join(string roomId) => PacketCodec.Encode(PacketType.Join,
            new JoinRequest { RoomId = roomId, Token = "tok", BuildHash = ServerHost.ServerBuildHash });

        /// <summary>记录型替身盒：可注入固定返回（满拒/失败/幂等）。</summary>
        private sealed class RecordingOutbox : ISettlementOutbox
        {
            public readonly List<MatchResultSummary> Entries = new List<MatchResultSummary>();
            public string[] LastSeatAccountIds;
            public SettlementOutboxResult NextResult = SettlementOutboxResult.Appended;
            public int FlushCalls;

            public int Count => Entries.Count;

            public SettlementOutboxResult Enqueue(MatchResultSummary summary, string[] seatAccountIds = null)
            {
                LastSeatAccountIds = seatAccountIds;
                if (NextResult == SettlementOutboxResult.Appended) Entries.Add(summary);
                return NextResult;
            }

            public IReadOnlyList<PendingSettlement> ListPending()
            {
                var list = new List<PendingSettlement>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++) list.Add(new PendingSettlement(Entries[i], null));
                return list;
            }

            public bool TryMarkCompleted(string matchId) => false;
            public bool TryAbandon(string matchId, string reason) => false;

            public void Flush() => FlushCalls++;
        }

        /// <summary>满员开局 → 排空超时收尾（截止时刻已过 → 首个 Pump 强制 Finish）→ SettlementReady。</summary>
        private static void RunToSettlement(ServerHost host, FakeRoomTransport t)
        {
            t.RaiseConnected(1);
            t.RaiseConnected(2);
            t.RaiseData(1, Join("Room-A"));
            t.RaiseData(2, Join("Room-A"));
            host.TryGetRoom("Room-A", out RoomRuntime room);
            Assert.True(room.Started);

            host.BeginDrain(0);   // 截止时刻 0 ≤ 当前单调毫秒 → 首个 Pump 即 DrainTimeout 收尾
            host.Pump();
            Assert.True(room.Closed, "排空超时应强制收尾到终态");
        }

        private static (ServerHost host, FakeRoomTransport t, RecordingOutbox outbox) NewHostWithOutbox()
        {
            var outbox = new RecordingOutbox();
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            return (new ServerHost(t, null, null, null, cfg, outbox), t, outbox);
        }

        [Fact]
        public void 装配Outbox后_结算就绪即入盒_计数与收口()
        {
            var h = NewHostWithOutbox();
            using var host = h.host;
            h.host.Ops.PrintEnabled = false;

            RunToSettlement(host, h.t);

            Assert.Equal(1, host.Ops.SettlementsReady);
            Assert.Equal(1, host.Ops.SettlementsJournaled);
            Assert.Single(h.outbox.Entries);
            Assert.Equal(h.t.LastStartGame(1).MatchId, h.outbox.Entries[0].MatchId);
            Assert.Equal(ShutdownReason.DrainTimeout, h.outbox.Entries[0].EndReason);
            Assert.Equal(1, host.SettlementOutboxPending);

            // §12 第 4 步收口：FlushSettlementOutbox 幂等，转发到盒
            host.FlushSettlementOutbox();
            host.FlushSettlementOutbox();
            Assert.Equal(2, h.outbox.FlushCalls);
        }

        [Fact]
        public void 入盒幂等命中_计数不丢()
        {
            var h = NewHostWithOutbox();
            using var host = h.host;
            host.Ops.PrintEnabled = false;
            h.outbox.NextResult = SettlementOutboxResult.Duplicate;

            RunToSettlement(host, h.t);

            Assert.Equal(1, host.Ops.SettlementsReady);
            Assert.Equal(1, host.Ops.SettlementsOutboxDuplicates);
            Assert.Equal(0, host.Ops.SettlementsJournaled);
            Assert.Equal(0, host.SettlementOutboxPending);   // 替身未收录——幂等命中不新增
        }

        [Fact]
        public void 入盒满拒_不炸权威循环_房间照常收尾()
        {
            var h = NewHostWithOutbox();
            using var host = h.host;
            host.Ops.PrintEnabled = false;
            h.outbox.NextResult = SettlementOutboxResult.RejectedFull;

            RunToSettlement(host, h.t);   // 若 Enqueue 异常传播会在此炸——不抛即通过

            Assert.Equal(1, host.Ops.SettlementsOutboxRejected);
            Assert.Equal(0, host.Ops.SettlementsJournaled);
        }

        [Fact]
        public void 入盒写失败_不炸权威循环_计数可见()
        {
            var h = NewHostWithOutbox();
            using var host = h.host;
            host.Ops.PrintEnabled = false;
            h.outbox.NextResult = SettlementOutboxResult.Failed;

            RunToSettlement(host, h.t);

            Assert.Equal(1, host.Ops.SettlementsOutboxFailed);
            Assert.Equal(0, host.Ops.SettlementsJournaled);
        }

        [Fact]
        public void 未装配Outbox_现状不变_只计数不入盒()
        {
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, null, null, null, cfg);   // 无 outbox
            host.Ops.PrintEnabled = false;

            RunToSettlement(host, t);

            Assert.Equal(1, host.Ops.SettlementsReady);
            Assert.Equal(0, host.Ops.SettlementsJournaled);
            Assert.Equal(0, host.SettlementOutboxPending);
            host.FlushSettlementOutbox();   // 未装配＝无操作，不抛
        }
    }
}
