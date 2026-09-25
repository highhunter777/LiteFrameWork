using System;
using System.Collections.Generic;
using Google.Protobuf;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using LiteTesting;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 跨房间过载隔离（《商业级通用服务端框架总设计》§514"**多房间并发时一个慢客户端或过载房间
    /// 不拖累其他房间**"；§593 弱网行"其他房间不受单慢连接拖累"；§560"退出条件：并发多房间、
    /// 慢客户端、过载房间和 drain 测试通过"）。
    ///
    /// 与 <see cref="RoomBroadcastTests"/>「背压超限_该客户端降档抽帧_其余客户端不受拖累」的分工：
    /// 那条钉的是**房间内**（同房间不同客户端）；本组钉的是**跨房间**——A 房被慢客户端压垮时，
    /// B 房的推帧与广播频率必须不受影响。两件事的失效模式不同：房间内靠 per-Session 记账隔开，
    /// 跨房间靠 per-Room 管线 + 逐房间推帧隔开（若哪天有人把管线或帧推进改成全局共享，本组会红）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class CrossRoomOverloadTests
    {
        private const long SharedSeed = 987L;

        private sealed class Capture
        {
            public readonly List<(Session session, PacketType type, IMessage msg)> Sent
                = new List<(Session, PacketType, IMessage)>();

            public List<StateSnapshot> SnapshotsFor(Session session)
                => Sent.FindAll(s => s.type == PacketType.StateSnapshot && s.session == session)
                       .ConvertAll(s => (StateSnapshot)s.msg);
        }

        private sealed class RoomFixture
        {
            public RoomRuntime Room;
            public SnapshotPipeline Pipeline;
            public Session[] Seats;
            public Capture Capture;
            public Session Slow;      // 该房间的慢客户端（从不 ACK）
            public Session Healthy;   // 该房间的正常客户端

            /// <summary>
            /// 本房两个席位都按正常节奏反馈（ack 最新已发帧，对齐真实 §4.5 输入包携带 ack）。
            /// **不 ack 的席位会被背压判定为慢客户端**（水位只经验证过的 ACK 释放）——
            /// 用例必须显式表达"谁在正常反馈"，不能靠默认值蒙混。
            /// </summary>
            public void AckAll()
            {
                Slow.TryAcceptAck(Slow.LastSentSnapshotFrame);
                Healthy.TryAcceptAck(Healthy.LastSentSnapshotFrame);
            }

            /// <summary>只让指定席位反馈（其余保持慢客户端身份）。</summary>
            public void Ack(Session session) => session.TryAcceptAck(session.LastSentSnapshotFrame);
        }

        private static int Join(RoomRuntime room, Session session, Session[] seats)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(session.ConnectionId), outputs);
            foreach (RoomOutput o in outputs)
                if (o is SignalOutput { Signal: PlayerAdmitted pa })
                {
                    session.PlayerId = pa.PlayerId;
                    seats[pa.PlayerId] = session;
                    return pa.PlayerId;
                }
            return -1;
        }

        /// <summary>建一个已开局的 2 人房（房号独立 → 独立 Runtime 与独立快照管线）。</summary>
        private static RoomFixture BuildStartedRoom(string roomId, int connBase)
        {
            var f = new RoomFixture
            {
                Room = new RoomRuntime(new RoomConfig { RoomId = roomId, Seed = SharedSeed }),
            };
            f.Seats = new Session[f.Room.ExpectedPlayers];
            f.Pipeline = new SnapshotPipeline(f.Seats);
            f.Capture = new Capture();
            f.Pipeline.SendTo = (session, type, msg, reliable) => f.Capture.Sent.Add((session, type, msg));

            f.Slow = new Session(connBase, 0);
            f.Healthy = new Session(connBase + 1, 0);
            Assert.Equal(0, Join(f.Room, f.Slow, f.Seats));
            Assert.Equal(1, Join(f.Room, f.Healthy, f.Seats));
            Assert.True(f.Room.Started, $"{roomId} 应满员开局");
            return f;
        }

        /// <summary>
        /// 命令面步进。两房各自用自己的 Runtime 与管线——与 ServerHost.Pump 的逐房间循环同序。
        ///
        /// <paramref name="ackA"/>/<paramref name="ackB"/> 声明该房是否按正常节奏反馈。
        /// **必须显式声明**：不 ack 的席位会被背压判定为慢客户端（水位只经验证过的 ACK 释放），
        /// 所以"没写 ack"等价于"这一房全员慢客户端"——那是另一个测试场景，不能靠默认值蒙混。
        /// </summary>
        private static void StepPair(RoomFixture a, RoomFixture b, int count, bool ackA = false, bool ackB = false)
        {
            var outputs = new List<RoomOutput>();
            for (int i = 0; i < count; i++)
            {
                a.Room.Execute(RoomCommand.Tick(0), outputs);
                if (ackA) a.AckAll();
                a.Pipeline.BroadcastIfDue(a.Room.AuthSim.Frame, a.Room.AuthSim, a.Room.Gate, a.Room.EntityIdOf, null);

                b.Room.Execute(RoomCommand.Tick(0), outputs);
                if (ackB) b.AckAll();
                b.Pipeline.BroadcastIfDue(b.Room.AuthSim.Frame, b.Room.AuthSim, b.Room.Gate, b.Room.EntityIdOf, null);
            }
        }

        [Fact]
        public void 过载房间_不拖累另一房间的广播频率()
        {
            RoomFixture overloaded = BuildStartedRoom("Room-A", 10);
            RoomFixture healthy = BuildStartedRoom("Room-B", 20);

            // 制造过载：A 房的**两个**席位都是慢客户端（整房被压）——比单慢客户端更强的情形
            overloaded.Slow.RecordSnapshotSend(0, (int)(ProtocolConstants.BackpressureQueueLimitBytes * 4));
            overloaded.Healthy.RecordSnapshotSend(0, (int)(ProtocolConstants.BackpressureQueueLimitBytes * 4));

            int bHealthyBefore = healthy.Capture.SnapshotsFor(healthy.Healthy).Count;
            StepPair(overloaded, healthy, 20, ackA: false, ackB: true);   // A 全员慢；B 正常反馈

            // A 房确实被压垮（两席位都降档、有抽帧记账）
            Assert.True(overloaded.Slow.BackpressureTier >= 1, "A 房慢客户端应降档");
            Assert.True(overloaded.Healthy.BackpressureTier >= 1, "A 房另一席位同样被压");
            Assert.True(overloaded.Pipeline.BackpressureThrottled > 0, "A 房应有抽帧计数");

            // B 房**完全不受影响**：两个席位都未降档、无抽帧
            Assert.Equal(0, healthy.Slow.BackpressureTier);
            Assert.Equal(0, healthy.Healthy.BackpressureTier);
            Assert.Equal(0, healthy.Pipeline.BackpressureThrottled);

            // 广播频率：B 房正常客户端应拿到比 A 房任一席位更多的快照
            int aSlow = overloaded.Capture.SnapshotsFor(overloaded.Slow).Count;
            int bHealthy = healthy.Capture.SnapshotsFor(healthy.Healthy).Count;
            Assert.True(bHealthy > bHealthyBefore, "B 房应持续广播");
            Assert.True(bHealthy > aSlow,
                $"B 房不应被 A 房拖累：A慢={aSlow} B正常={bHealthy}");
        }

        [Fact]
        public void 过载房间_不拖累另一房间的推帧()
        {
            RoomFixture overloaded = BuildStartedRoom("Room-A", 30);
            RoomFixture healthy = BuildStartedRoom("Room-B", 40);
            overloaded.Slow.RecordSnapshotSend(0, (int)(ProtocolConstants.BackpressureQueueLimitBytes * 4));

            StepPair(overloaded, healthy, 30, ackA: false, ackB: true);

            // 两房帧号必须同步推进——背压只降**广播**，绝不降**权威步**
            Assert.Equal(overloaded.Room.AuthSim.Frame, healthy.Room.AuthSim.Frame);
            Assert.True(healthy.Room.AuthSim.Frame >= 30, "B 房应正常推帧");
        }

        [Fact]
        public void 两房间管线各自记账_水位互不串用()
        {
            // 若哪天有人把 SnapshotPipeline 改成跨房间共享，本用例会红。
            // 注意 B 房席位的水位**不是 0**——B 房自己也在正常广播，自然累积已发字节；
            // "不共享"的正确判据是"B 的水位只反映 B 自己的发送量"，故用**量级差**而非零值。
            RoomFixture a = BuildStartedRoom("Room-A", 50);
            RoomFixture b = BuildStartedRoom("Room-B", 60);

            a.Slow.RecordSnapshotSend(0, (int)(ProtocolConstants.BackpressureQueueLimitBytes * 8));
            long aSeeded = a.Slow.SendQueueBytes;
            StepPair(a, b, 10, ackA: false, ackB: true);

            Assert.True(a.Slow.SendQueueBytes > aSeeded, "A 房水位应在播种之上继续增长");
            Assert.True(a.Slow.BackpressureTier >= 1, "A 房应降档");
            Assert.Equal(0, b.Slow.BackpressureTier);                       // B 房未被波及
            Assert.True(b.Slow.SendQueueBytes < ProtocolConstants.BackpressureQueueLimitBytes,
                $"B 房水位应只反映自身正常发送（{b.Slow.SendQueueBytes} < {ProtocolConstants.BackpressureQueueLimitBytes}）");
            Assert.True(a.Slow.SendQueueBytes > b.Slow.SendQueueBytes * 10, "两房水位量级应完全不同");
            Assert.NotSame(a.Pipeline, b.Pipeline);
            Assert.NotSame(a.Slow, b.Slow);
        }

        [Fact]
        public void 过载恢复_只影响本房间()
        {
            RoomFixture a = BuildStartedRoom("Room-A", 70);
            RoomFixture b = BuildStartedRoom("Room-B", 80);
            a.Slow.RecordSnapshotSend(0, (int)(ProtocolConstants.BackpressureQueueLimitBytes * 4));

            StepPair(a, b, 6, ackA: false, ackB: true);   // A 慢；B 正常反馈
            Assert.True(a.Slow.BackpressureTier >= 1);

            // A 房慢客户端开始正常 ACK → 水位回落 → 逐档恢复。
            // **恢复是逐档的、每档要连续达标 2s**（`UpdateBackpressureTier` 每次只降一档），
            // 故跑够 tier 档数 × 2s。这与 RoomBroadcastTests「背压恢复」同一口径。
            int tiers = a.Slow.BackpressureTier;
            var outputs = new List<RoomOutput>();
            int frames = (int)(tiers * ProtocolConstants.RecoverHoldMillis * SimConfig.TickRate / 1000) + 10;
            for (int i = 0; i < frames; i++)
            {
                a.Room.Execute(RoomCommand.Tick(0), outputs);
                a.Slow.TryAcceptAck(a.Slow.LastSentSnapshotFrame);
                a.Pipeline.BroadcastIfDue(a.Room.AuthSim.Frame, a.Room.AuthSim, a.Room.Gate, a.Room.EntityIdOf, null);
                b.Room.Execute(RoomCommand.Tick(0), outputs);
                b.AckAll();
                b.Pipeline.BroadcastIfDue(b.Room.AuthSim.Frame, b.Room.AuthSim, b.Room.Gate, b.Room.EntityIdOf, null);
            }

            Assert.True(a.Slow.BackpressureTier == 0,
                $"A 房应逐档恢复到 0（起始 {tiers} 档，跑了 {frames} 帧，实际 tier={a.Slow.BackpressureTier}）");
            Assert.Equal(0, b.Slow.BackpressureTier);              // B 房自始至终未被卷入
            Assert.Equal(0, b.Pipeline.BackpressureThrottled);
        }
    }
}
