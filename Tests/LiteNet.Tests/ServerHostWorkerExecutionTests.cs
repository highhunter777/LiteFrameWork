using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// Worker 执行形态（《商业级通用服务端框架总设计》§8.2"hash(roomId) % workerCount 决定房间归属、
    /// 一个 Worker 顺序驱动多个 RoomActor、每个 RoomActor 仍是单线程"）：
    /// 房间命令与快照广播在归属 Worker 上执行，产出经 Mailbox Outbound lane 回传宿主应用/发送；
    /// 宿主只做 Transport IO、准入与回传应用。
    ///
    /// 与 <see cref="ServerHostMailboxRoutingTests"/>（宿主 owner 消费）的分工：那组钉宿主单线程消费语义；
    /// 本组钉"执行 owner 换成 Worker"后的等价语义与回传边界。
    /// </summary>
    public sealed class ServerHostWorkerExecutionTests
    {
        private const string TwoRoomConfig = @"{
            ""port"": 45301, ""max_rooms"": 2, ""worker_count"": 1, ""mailbox_capacity"": 256,
            ""audience"": """", ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 }, ""one"": { ""expected_players"": 1 } }
        }";

        private static (ServerHost host, FakeRoomTransport t) NewHost(string json = TwoRoomConfig,
            ISettlementOutbox outbox = null)
        {
            var cfg = RoomServerConfig.Parse(json);
            var t = new FakeRoomTransport();
            // 生产装配形态：路由 + 延迟排空 + Worker 执行。
            var host = new ServerHost(t, null, null, null, cfg, outbox, null,
                mailboxRouting: true, drainMailboxesImmediately: false, workerExecution: true);
            host.Ops.PrintEnabled = false;
            return (host, t);
        }

        private static byte[] Join(string roomId) => PacketCodec.Encode(PacketType.Join,
            new JoinRequest { RoomId = roomId, Token = "tok", BuildHash = ServerHost.ServerBuildHash });

        /// <summary>
        /// 冗余窗输入（真实客户端形态）：Frames[0] 对应 <paramref name="baseFrame"/>，向后连续
        /// <paramref name="count"/> 帧同值。闸门按"服务器当前帧+1"在窗内取帧——
        /// 单帧包在"读帧号与执行之间有推进"时会因基帧不在窗内被整条判 OutOfRange，
        /// 冗余窗正是协议对竞态的处置方式。
        /// </summary>
        private static byte[] InputWindow(int baseFrame, int count, float moveX)
        {
            var message = new InputMessage { Frame = baseFrame, AckSnapshot = -1, ViewFrame = 0 };
            for (int i = 0; i < count; i++)
                message.Frames.Add(new InputFrame { EntityId = 0, MoveX = moveX, AimX = 1f, AimZ = 0f });
            return PacketCodec.Encode(PacketType.Input, message);
        }

        /// <summary>驱动宿主直到条件成立（Pump + 让出 CPU 给 Worker）。</summary>
        private static void PumpUntil(ServerHost host, Func<bool> predicate, string message, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                host.Pump();
                if (predicate()) return;
                Thread.Sleep(1);
            }
            Assert.True(predicate(), message);
        }

        /// <summary>
        /// 只等待（不 Pump）：用于断言 Worker 侧已入盒命令的执行效果。
        /// 不 Pump 就不再投递新 Tick——否则服务器帧会以测试循环的速度飞跑，
        /// 按"当前帧+1"构造的输入在执行时必然已被判过期（帧号纪律，非缺陷）。
        /// </summary>
        private static void WaitWorker(Func<bool> predicate, string message, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return;
                Thread.Sleep(1);
            }
            Assert.True(predicate(), message);
        }

        private static void PumpFor(ServerHost host, int ms)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms)
            {
                host.Pump();
                Thread.Sleep(1);
            }
        }

        [Fact]
        public void Worker执行形态_前置冲突_装载期显式拒绝()
        {
            // 没有 Mailbox 路由 → Worker 拿不到命令
            Assert.Throws<ArgumentException>(() => new ServerHost(new FakeRoomTransport(),
                new RoomConfig { Port = 45311, RoomId = "W", ExpectedPlayers = 2 },
                workerPool: new RoomWorkerPool(1, 8), workerExecution: true));

            // 立即排空（宿主同栈执行）与"执行 owner 是 Worker"直接冲突
            Assert.Throws<ArgumentException>(() => new ServerHost(new FakeRoomTransport(),
                new RoomConfig { Port = 45312, RoomId = "W", ExpectedPlayers = 2 },
                workerPool: new RoomWorkerPool(1, 8), mailboxRouting: true, workerExecution: true));

            // 合法组合：路由 + 延迟排空 + 显式池
            var pool = new RoomWorkerPool(1, 8);
            using var host = new ServerHost(new FakeRoomTransport(),
                new RoomConfig { Port = 45313, RoomId = "W", ExpectedPlayers = 2 },
                workerPool: pool, mailboxRouting: true, drainMailboxesImmediately: false, workerExecution: true);
            Assert.True(host.WorkerExecutionEnabled);
            Assert.True(host.WorkerPoolStarted);
        }

        [Fact]
        public void Worker执行_进房开局走回传_输入推进闸门_快照由回传发出()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-W"));
            h.t.RaiseData(2, Join("Room-W"));

            // 执行 owner 是 Worker：Join 不在回调栈内完成，JoinAck 必须等宿主应用回传后才有。
            Assert.Null(h.t.LastJoinAck(1));

            PumpUntil(host, () => h.t.LastJoinAck(1) != null && h.t.LastJoinAck(2) != null, "两端应收到 JoinAck");
            PumpUntil(host, () => host.TryGetRoom("Room-W", out RoomRuntime room) && room.Started, "满员应由 Worker 开局");
            host.TryGetRoom("Room-W", out RoomRuntime started);
            PumpUntil(host, () => h.t.LastStartGame(1) != null && h.t.LastStartGame(2) != null, "两端应收到 StartGame");

            // 输入：Worker 执行进闸门（宿主不再执行 Runtime 命令）。
            // 冗余窗：基帧 = 当前帧+4（容忍窗内）——不 Pump 的等待期间服务器至多再推进一帧；
            // 闸门要求"包基帧 ≥ 执行时服务器帧+1"（否则整条按已消费帧拒绝，帧号纪律非缺陷）。
            long acceptedBefore = started.Gate.AcceptedCount;
            h.t.RaiseData(1, InputWindow(started.AuthSim.Frame + 4, 4, 0.5f));
            WaitWorker(() => started.Gate.AcceptedCount > acceptedBefore,
                $"输入应由 Worker 执行进闸门（stale={host.MailboxStale} frame={started.AuthSim.Frame}）");
            Assert.Equal(1, host.Ops.InputPackets);

            // 广播：Worker 构建快照 → Outbound 回传 → 宿主编码发送。
            PumpUntil(host, () => h.t.CountOf(1, PacketType.StateSnapshot) > 0
                && h.t.CountOf(2, PacketType.StateSnapshot) > 0, "两席位都应收到经回传发出的快照");
        }

        [Fact]
        public void Worker执行_两Worker并行_两房各自独立推进()
        {
            // 选两个落在不同 Worker 的房号——保证"并行"是事实而非同一 Worker 串行。
            var probe = new RoomWorkerPool(2, 64);
            string roomA = null, roomB = null;
            for (int i = 0; i < 32 && roomB == null; i++)
            {
                string candidate = "Room-" + i;
                if (roomA == null)
                {
                    roomA = candidate;
                    continue;
                }
                if (probe.GetWorkerIndex(candidate) != probe.GetWorkerIndex(roomA)) roomB = candidate;
            }
            probe.Dispose();
            Assert.NotNull(roomB);

            var json = TwoRoomConfig.Replace(@"""worker_count"": 1", @"""worker_count"": 2");
            var h = NewHost(json);
            using var host = h.host;
            Assert.Equal(2, host.WorkerPool.WorkerCount);
            Assert.NotEqual(host.WorkerPool.GetWorkerIndex(roomA), host.WorkerPool.GetWorkerIndex(roomB));

            h.t.RaiseConnected(1);
            h.t.RaiseConnected(2);
            h.t.RaiseConnected(3);
            h.t.RaiseConnected(4);
            h.t.RaiseData(1, Join(roomA));
            h.t.RaiseData(2, Join(roomA));   // A 房满员
            h.t.RaiseData(3, Join(roomB));
            h.t.RaiseData(4, Join(roomB));   // B 房满员

            PumpUntil(host, () => host.TryGetRoom(roomA, out RoomRuntime a) && a.Started
                && host.TryGetRoom(roomB, out RoomRuntime b) && b.Started, "两房应各自开局");
            // 等四份 JoinAck 都回传落定（会话绑定房间）——真实客户端据此才开始发输入。
            PumpUntil(host, () => h.t.LastJoinAck(1) != null && h.t.LastJoinAck(2) != null
                && h.t.LastJoinAck(3) != null && h.t.LastJoinAck(4) != null, "四端应收到 JoinAck");
            host.TryGetRoom(roomA, out RoomRuntime roomA1);
            host.TryGetRoom(roomB, out RoomRuntime roomB1);
            Assert.NotSame(roomA1, roomB1);
            Assert.NotSame(roomA1.AuthSim, roomB1.AuthSim);

            h.t.RaiseData(1, InputWindow(roomA1.AuthSim.Frame + 4, 4, 1f));
            h.t.RaiseData(3, InputWindow(roomB1.AuthSim.Frame + 4, 4, -1f));

            WaitWorker(() => roomA1.Gate.AcceptedCount > 0 && roomB1.Gate.AcceptedCount > 0,
                "两房输入都应被各自 Worker 执行");
            PumpUntil(host, () => h.t.CountOf(1, PacketType.StateSnapshot) > 0
                && h.t.CountOf(3, PacketType.StateSnapshot) > 0, "两房快照都应到达各自客户端");
            Assert.Equal(2, host.Ops.InputPackets);
        }

        [Fact]
        public void Worker执行_重连经回传落定_旧连接失效且席位恢复()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-R"));
            h.t.RaiseData(2, Join("Room-R"));
            PumpUntil(host, () => h.t.LastJoinAck(1) != null && h.t.LastJoinAck(2) != null, "两端应收到 JoinAck");
            string token = h.t.LastJoinAck(1).ReconnectToken;

            h.t.RaiseDisconnected(1);
            h.t.RaiseConnected(3);
            h.t.RaiseData(3, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = token }));

            PumpUntil(host, () => h.t.Last(3, PacketType.ReconnectResponse) is ReconnectResponse r && r.Ok,
                "重连应由 Worker 校验并经回传响应");

            host.TryGetRoom("Room-R", out RoomRuntime room);
            Assert.Equal(3, room.SeatOf(0).ConnectionId);                  // Runtime 侧已换绑
            Assert.Equal(SeatPhase.Restoring, room.SeatOf(0).Phase);        // 恢复完成 ACK 前抑制广播
            Assert.True(host.Sessions.TryGet(1, out Session old) && old.Disconnected, "旧连接应失效");
            Assert.True(host.Sessions.TryGet(3, out Session rebound));
            Assert.Equal(0, rebound.PlayerId);                              // 宿主应用 SeatRebound 后映射
            Assert.Equal("Room-R", rebound.RoomId);
            Assert.Equal(1, host.Ops.ReconnectsServed);

            h.t.RaiseData(3, PacketCodec.Encode(PacketType.RestoreComplete, new RestoreComplete()));
            PumpUntil(host, () => room.SeatOf(0).Phase == SeatPhase.Active, "恢复完成后席位应回 Active");
            // SeatRestored 是回传输出：宿主应用后才计 Ops（Worker 先置相位，两者间有一个回传窗口）。
            PumpUntil(host, () => host.Ops.RestoresCompleted == 1, "SeatRestored 回传应由宿主应用");
        }

        [Fact]
        public void Worker执行_排空超时_结算入盒且终态房间销毁()
        {
            var outbox = new RecordingOutbox();
            var h = NewHost(TwoRoomConfig, outbox);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-D"));
            h.t.RaiseData(2, Join("Room-D"));
            PumpUntil(host, () => host.TryGetRoom("Room-D", out RoomRuntime room) && room.Started, "满员应由 Worker 开局");

            host.BeginDrain(0);   // 截止时刻已过 → 下一个 Pump 投递 Shutdown，由 Worker 收尾
            PumpUntil(host, () => host.DrainComplete, "排空应在时限内收敛", 8000);
            // 销毁以"房间静默（Worker 无在途/无待驱动、三 lane 空）"为前提——先收口回传再销毁。
            PumpUntil(host, () => host.RoomCount == 0, "终态房间应销毁", 8000);

            Assert.Equal(1, host.Ops.RoomsDrainTimedOut);
            Assert.Equal(1, host.Ops.SettlementsReady);
            Assert.Equal(1, host.Ops.SettlementsJournaled);   // 销毁前输出已应用（结算事实不丢）
            Assert.Single(outbox.Entries);
            Assert.Equal("Room-D", outbox.Entries[0].MatchId);
            Assert.Equal(0, host.RoomCount);                  // 终态房间已销毁（§42）
            Assert.Equal(1, host.Ops.RoomsDestroyed);
        }

        [Fact]
        public void Worker执行_Dispose先停Worker再收口_在途结算入盒()
        {
            var outbox = new RecordingOutbox();
            var h = NewHost(TwoRoomConfig, outbox);
            var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseConnected(2);
            h.t.RaiseData(1, Join("Room-X"));
            h.t.RaiseData(2, Join("Room-X"));
            PumpUntil(host, () => host.TryGetRoom("Room-X", out RoomRuntime room) && room.Started, "满员应由 Worker 开局");
            // 真实客户端在收到 JoinAck 后才会有后续动作：等宿主把两份回传都应用完，
            // 否则会话尚未绑定房间（RoomId 为空），Leave 会按"未进房"丢弃。
            PumpUntil(host, () => h.t.LastJoinAck(1) != null && h.t.LastJoinAck(2) != null, "两端应收到 JoinAck");

            // 全员离场命令刚入盒即释放：Dispose 必须先让 Worker 把已接受的命令执行完
            //（收尾 → SettlementReady 回传），再由宿主收口应用——已接受的工作不能丢。
            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));
            h.t.RaiseData(2, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));
            host.Dispose();

            Assert.True(host.WorkerPoolStopped);
            Assert.True(h.t.Disposed);
            Assert.Single(outbox.Entries);
            Assert.Equal("Room-X", outbox.Entries[0].MatchId);
            Assert.Equal(ShutdownReason.AllPlayersLeft, outbox.Entries[0].EndReason);
        }

        private sealed class RecordingOutbox : ISettlementOutbox
        {
            public readonly List<MatchResultSummary> Entries = new List<MatchResultSummary>();

            public int Count => Entries.Count;

            public SettlementOutboxResult Enqueue(MatchResultSummary summary)
            {
                Entries.Add(summary);
                return SettlementOutboxResult.Appended;
            }

            public IReadOnlyList<MatchResultSummary> ListPending() => Entries;

            public bool TryMarkCompleted(string matchId) => false;

            public void Flush() { }
        }
    }
}