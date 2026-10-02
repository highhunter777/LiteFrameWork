using System;
using System.Collections.Generic;
using Google.Protobuf;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房间广播 / E1 背压 / 重连服务 用例（《M10实施指导》§2.7/§2.8 + §3"E1 背压"/"E3 加固"组）。
    ///
    /// 改由 <see cref="RoomRuntime"/> 命令面驱动（Join/Tick/ClientInput）+ App 层
    /// <see cref="SnapshotPipeline"/> 广播（假传输——用 pipeline.SendTo 捕获出站包），
    /// 不依赖 KCP 与真实网络（那部分由 RoomServerTests 的 loopback 用例覆盖）。
    /// </summary>
    public sealed class RoomBroadcastTests
    {
        private const long SharedSeed = 12345L;

        private sealed class Capture
        {
            public readonly List<(Session session, PacketType type, IMessage msg, bool reliable)> Sent
                = new List<(Session, PacketType, IMessage, bool)>();

            public int CountOf(PacketType type) => Sent.FindAll(s => s.type == type).Count;
            public List<StateSnapshot> SnapshotsFor(Session session)
                => Sent.FindAll(s => s.type == PacketType.StateSnapshot && s.session == session)
                       .ConvertAll(s => (StateSnapshot)s.msg);
        }

        /// <summary>执行一次 Join 命令并把进房连接绑定到 App 层席位映射（App 装配的测试等价物）。</summary>
        private static int Join(RoomRuntime room, Session session, Session[] seats)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(session.ConnectionId), outputs);
            foreach (RoomOutput o in outputs)
            {
                if (o is SignalOutput { Signal: PlayerAdmitted pa })
                {
                    session.PlayerId = pa.PlayerId;
                    seats[pa.PlayerId] = session;
                    return pa.PlayerId;
                }
            }
            return -1;
        }

        /// <summary>命令面步进：Tick + 快照广播（与 ServerHost.Pump 同序）。</summary>
        private static void Step(RoomRuntime room, SnapshotPipeline pipeline, int count)
        {
            Step(room, pipeline, count, null);
        }

        /// <summary>带席位可播判定的步进（ServerHost.Pump 传"席位 == Active"的同款形态）。</summary>
        private static void Step(RoomRuntime room, SnapshotPipeline pipeline, int count, Func<int, bool> seatBroadcastable)
        {
            var outputs = new List<RoomOutput>();
            for (int i = 0; i < count; i++)
            {
                room.Execute(RoomCommand.Tick(0), outputs);
                pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf, seatBroadcastable);
            }
        }

        private static (RoomRuntime room, Capture capture, Session s1, Session s2, SnapshotPipeline pipeline) BuildStartedRoom()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "TestRoom", Seed = SharedSeed });
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats);
            var capture = new Capture();
            pipeline.SendTo = (session, type, msg, reliable) => capture.Sent.Add((session, type, msg, reliable));

            var s1 = new Session(1, 0);
            var s2 = new Session(2, 0);
            Assert.Equal(0, Join(room, s1, seats));
            Assert.Equal(1, Join(room, s2, seats));
            Assert.True(room.Started, "满员后应自动开局");
            return (room, capture, s1, s2, pipeline);
        }

        /// <summary>合成一条"第 frame 帧、玩家 p 的移动输入"（EntityId 按席位取——缺省 0 会被判失效实体）。</summary>
        private static ClientInputBatch MoveInput(RoomRuntime room, int playerId, int frame, float moveX)
        {
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame { EntityId = room.EntityIdOf(playerId), MoveX = moveX, AimX = 1f };
            return new ClientInputBatch { Frame = frame, AckSnapshot = 0, Count = 1, Frames = frames };
        }

        private static void Input(RoomRuntime room, Session session, in ClientInputBatch batch)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.ClientInput(session.PlayerId, batch), outputs);
        }

        [Fact]
        public void 快照按30Hz广播_每两逻辑帧一次()
        {
            var (room, capture, s1, s2, pipeline) = BuildStartedRoom();

            Step(room, pipeline, 10);

            // 10 逻辑帧 → 30Hz 抽帧 → 5 次广播 × 2 客户端 = 10 个快照包
            int stride = SimConfig.TickRate / SimConfig.SnapshotHz;
            Assert.Equal(10, room.AuthSim.Frame);
            Assert.Equal(10 / stride * 2, capture.CountOf(PacketType.StateSnapshot));
            Assert.True(capture.SnapshotsFor(s1).Count > 0 && capture.SnapshotsFor(s2).Count > 0);
        }

        [Fact]
        public void 首包全量_后续增量_活体数变化再全量()
        {
            var (room, capture, s1, _, pipeline) = BuildStartedRoom();

            Step(room, pipeline, 6);
            List<StateSnapshot> snapshots = capture.SnapshotsFor(s1);

            Assert.True(snapshots[0].IsFull, "首个快照必须是全量（客户端从零重建）");
            Assert.Equal(room.AuthSim.AliveCount(), snapshots[0].Slots.Count);
            for (int i = 1; i < snapshots.Count; i++) Assert.False(snapshots[i].IsFull, $"第 {i} 个快照应为增量");

            // 杀一个实体 → 含该帧的广播必须转全量（缺席无法表达"死了"）。
            // 先推进到下一广播边界的前一帧，再杀——保证"活体变化"这一帧本身就是广播帧
            capture.Sent.Clear();
            while ((room.AuthSim.Frame + 1) % (SimConfig.TickRate / SimConfig.SnapshotHz) != 0) Step(room, pipeline, 1);
            room.AuthSim.Despawn(room.EntityIdOf(1));
            Step(room, pipeline, 4);
            List<StateSnapshot> after = capture.SnapshotsFor(s1);
            Assert.True(after[0].IsFull, $"活体集合变化的那次广播必须是全量（slots={after[0].Slots.Count} full={after[0].IsFull}）");
        }

        [Fact]
        public void 掉线成员不广播_其余成员不受影响()
        {
            var (room, capture, s1, s2, pipeline) = BuildStartedRoom();
            s2.Disconnected = true;

            Step(room, pipeline, 4);

            Assert.True(capture.SnapshotsFor(s1).Count > 0, "在线成员应继续收到快照");
            Assert.Empty(capture.SnapshotsFor(s2));                  // 掉线者不占带宽（掉线不停帧，但不发）
        }

        [Fact]
        public void 广播私有面_各客户端只收到本人私有态_比赛状态全体同值()
        {
            var (room, capture, s1, s2, pipeline) = BuildStartedRoom();

            // 各玩家私有运行态不同（弹药数——只应出现在本人的快照里，业务总设计 §1 禁泄漏）
            room.AuthSim.Weapons[0 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 30;
            room.AuthSim.Weapons[1 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 8;
            room.AuthSim.Match = new MatchStateData { Phase = 1, Timer = 10800, Round = 1 };

            Step(room, pipeline, 4);

            List<StateSnapshot> a = capture.SnapshotsFor(s1);
            List<StateSnapshot> b = capture.SnapshotsFor(s2);
            Assert.True(a.Count > 0 && b.Count > 0);

            foreach (StateSnapshot snap in a)
            {
                Assert.NotNull(snap.PrivateState);
                Assert.Equal(room.EntityIdOf(0), snap.PrivateState.EntityId);
                Assert.Equal(30, snap.PrivateState.Weapons[0].MagAmmo);   // 自己的弹药
                Assert.Equal(0, snap.PrivateState.Weapons[1].MagAmmo);    // 另一武器槽空（不是别人的量）
                Assert.NotNull(snap.Match);                                // 比赛状态层随广播
                Assert.Equal(1, snap.Match.Phase);
                Assert.Equal(10800, snap.Match.Timer);
            }
            foreach (StateSnapshot snap in b)
            {
                Assert.NotNull(snap.PrivateState);
                Assert.Equal(room.EntityIdOf(1), snap.PrivateState.EntityId);
                Assert.Equal(8, snap.PrivateState.Weapons[0].MagAmmo);
            }
        }

        [Fact]
        public void 背压超限_该客户端降档抽帧_其余客户端不受拖累()
        {
            var (room, capture, s1, s2, pipeline) = BuildStartedRoom();

            // 模拟慢客户端：真实发送过 4×上限字节但从未 ACK（诚实记账播种——水位
            // 只能经验证过的 ACK 释放，直接改 SendQueueBytes/AckedBytes 会绕过 ledger）
            s1.RecordSnapshotSend(0, (int)(ProtocolConstants.BackpressureQueueLimitBytes * 4));

            Step(room, pipeline, 20);

            int s1Count = capture.SnapshotsFor(s1).Count;
            int s2Count = capture.SnapshotsFor(s2).Count;
            Assert.True(s1.BackpressureTier >= 1, $"慢客户端应已降档（tier={s1.BackpressureTier}）");
            Assert.True(pipeline.BackpressureThrottled > 0, "应有抽帧计数");
            Assert.True(s2Count > s1Count,
                $"其余客户端不应被拖累：s1={s1Count} s2={s2Count}（tier={s1.BackpressureTier}/{s2.BackpressureTier}）");
            Assert.Equal(0, s2.BackpressureTier);
        }

        [Fact]
        public void 背压恢复_水位回落后逐档恢复()
        {
            var (room, _, s1, _, pipeline) = BuildStartedRoom();
            s1.RecordSnapshotSend(0, (int)(ProtocolConstants.BackpressureQueueLimitBytes * 4));   // 慢客户端：真实发送从未确认
            Step(room, pipeline, 6);
            Assert.True(s1.BackpressureTier >= 1);

            // ack 到达 → 队列消化（AckedBytes 经 ledger 前推）
            s1.TryAcceptAck(s1.LastSentSnapshotFrame);
            int tierAfterAck = s1.BackpressureTier;

            // 恢复需要连续达标 2s（120 帧）→ 跑够时间。
            // P0 起快照分层携带比赛状态 + 本人私有面（每包约 +60B）——单次 ack 后队列会重新积压，
            // "只 ack 一次就静默恢复"的经济性不再成立；真实客户端每输入包都带 ack_snapshot（§4.5），
            // 对齐真实反馈节奏：每步 ack 最新已发送帧（重复 ack 由验证层幂等忽略）→ 水位维持低位 → 恢复可观测
            for (int i = 0; i < ProtocolConstants.RecoverHoldMillis * SimConfig.TickRate / 1000 + 5; i++)
            {
                Step(room, pipeline, 1);
                s1.TryAcceptAck(s1.LastSentSnapshotFrame);
            }

            Assert.True(s1.BackpressureTier < tierAfterAck || s1.BackpressureTier == 0,
                $"水位回落后应恢复档位（tier {tierAfterAck} → {s1.BackpressureTier}）");
        }

        [Fact]
        public void 背压档3_只裁增量帧_全量帧整帧保留()
        {
            var (room, capture, s1, s2, pipeline) = BuildStartedRoom();
            s1.BackpressureTier = 3;                       // 直置档位（升降滞回另有专测——这里只验裁剪面）

            // 双实体持续变化（裁剪/保留的可观察面：变化集恒为两槽位）
            for (int i = 0; i < 3; i++)
            {
                Input(room, s1, MoveInput(room, 0, room.AuthSim.Frame + 1, 1f));
                Input(room, s2, MoveInput(room, 1, room.AuthSim.Frame + 1, -1f));
                Step(room, pipeline, 1);
            }
            Assert.Empty(capture.SnapshotsFor(s1));        // 档 1+ 抽帧：第 1 次广播（broadcastIndex 1）被跳过

            // 请求全量恰好落在 s1 在收的广播上（broadcastIndex 2）——档 3 的 TrimFarthest 必须豁免全量帧：
            // 全量"缺席 = 死"的镜像语义只对全图成立，裁了会让档 3 客户端把视野外实体在镜像里误杀
            pipeline.RequestFullSnapshot();
            for (int i = 0; i < 4; i++)
            {
                Input(room, s1, MoveInput(room, 0, room.AuthSim.Frame + 1, 1f));
                Input(room, s2, MoveInput(room, 1, room.AuthSim.Frame + 1, -1f));
                Step(room, pipeline, 1);
            }
            List<StateSnapshot> s1Snaps = capture.SnapshotsFor(s1);
            Assert.Single(s1Snaps);                       // 此刻只收过全量（broadcastIndex 2）
            Assert.True(s1Snaps[0].IsFull, "请求的全量落在 s1 在收的广播上");
            Assert.True(s1Snaps[0].Slots.Count == room.AuthSim.AliveCount(),
                $"档 3 全量帧整帧保留（缺席判死误杀视野外实体；slots={s1Snaps[0].Slots.Count} alive={room.AuthSim.AliveCount()}）");

            for (int i = 0; i < 2; i++)                    // 后续增量（broadcastIndex 4，s1 在收）
            {
                Input(room, s1, MoveInput(room, 0, room.AuthSim.Frame + 1, 1f));
                Input(room, s2, MoveInput(room, 1, room.AuthSim.Frame + 1, -1f));
                Step(room, pipeline, 1);
            }
            List<StateSnapshot> s1After = capture.SnapshotsFor(s1);   // SnapshotsFor 是即时快照——重取
            Assert.Equal(2, s1After.Count);               // 全量 + 增量恰两包
            Assert.False(s1After[1].IsFull);
            Assert.Single(s1After[1].Slots);               // 档 3 增量仍裁最远半数（回归守卫：裁剪只让位于全量语义）
        }

        [Fact]
        public void 重连席位Restoring_抑制增量_恢复ACK后整帧全量重锚()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "TestRoom", Seed = SharedSeed });
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats);
            var capture = new Capture();
            pipeline.SendTo = (session, type, msg, reliable) => capture.Sent.Add((session, type, msg, reliable));
            var s1 = new Session(1, 0);
            var s2 = new Session(2, 0);
            Assert.Equal(0, Join(room, s1, seats));
            Assert.Equal(1, Join(room, s2, seats));

            Step(room, pipeline, 2);                                    // 建立全量基线
            Assert.True(capture.SnapshotsFor(s1).Count > 0);

            // s1 掉线 → 新连接重绑（App 等价物：新 Session 顶入席位映射）→ 席位 Restoring
            room.Execute(RoomCommand.Disconnect(s1.ConnectionId), new List<RoomOutput>());
            var fresh = new Session(9, 0);
            room.Execute(RoomCommand.Rebind(0, 9), new List<RoomOutput>());
            seats[0] = fresh;
            Assert.Equal(SeatPhase.Restoring, room.SeatOf(0).Phase);

            // 抑制期间：Restoring 席位零投递；其余成员照常收增量（且不得被 fresh 水位拖成全帧）
            capture.Sent.Clear();
            Step(room, pipeline, 6, p => room.SeatOf(p)?.Phase == SeatPhase.Active);
            Assert.Empty(capture.SnapshotsFor(fresh));
            Assert.True(capture.SnapshotsFor(s2).Count > 0, "其余成员不受抑制影响");
            foreach (StateSnapshot s in capture.SnapshotsFor(s2))
                Assert.False(s.IsFull, "抑制期间的增量帧不得被 Restoring 席位的 fresh 水位强制成全量");

            // 恢复完成 ACK → Active；宿主重锚（ServerHost 在 SeatRestored 时同款调用）
            room.Execute(RoomCommand.RestoreAck(0), new List<RoomOutput>());
            Assert.Equal(SeatPhase.Active, room.SeatOf(0).Phase);
            pipeline.RequestFullSnapshot();

            capture.Sent.Clear();
            Step(room, pipeline, 2, p => room.SeatOf(p)?.Phase == SeatPhase.Active);
            List<StateSnapshot> resumed = capture.SnapshotsFor(fresh);
            Assert.Single(resumed);
            Assert.True(resumed[0].IsFull, "恢复后首包必须整帧全量（广播链重锚，增量自此无缺口）");
        }

        [Fact]
        public void 重连票据_一次性且绑定席位()
        {
            var service = new ReconnectService();
            string ticket = service.Issue(playerId: 1, roomId: "Room-A");

            Assert.True(service.TryConsume(ticket, out int playerId, out string roomId));
            Assert.Equal(1, playerId);
            Assert.Equal("Room-A", roomId);

            // 一次性：同一张票不能再换
            Assert.False(service.TryConsume(ticket, out _, out _));
            // 伪造票拒绝
            Assert.False(service.TryConsume("forged-token", out _, out _));
            Assert.False(service.TryConsume(null, out _, out _));
        }

        [Fact]
        public void 重连响应_携带权威快照与输入历史()
        {
            var (room, _, s1, _, pipeline) = BuildStartedRoom();

            // 跑几帧并喂输入（历史才有内容）
            for (int i = 0; i < 6; i++)
            {
                Input(room, s1, MoveInput(room, 0, room.AuthSim.Frame + 1, 1f));
                Step(room, pipeline, 1);
            }

            var service = new ReconnectService();
            string ticket = service.Issue(0, room.RoomId);
            Assert.True(service.TryConsume(ticket, out int playerId, out _));
            Assert.Equal(0, playerId);

            // 客户端凭票取"权威快照 + 输入历史"（服务器侧能力，§5.6 首选路径）
            var recovered = pipeline.Differ.Build(room.AuthSim.Frame, room.AuthSim, 0,
                room.AuthSim.Entities[0].Pos, SimConfig.AoiRadius, forceFull: true);
            var mirror = new SimWorldState();
            SnapshotReassembler.Apply(recovered, mirror, out _);

            Assert.True(recovered.IsFull);
            Assert.Equal(room.AuthSim.Frame, mirror.Frame);
            Assert.Equal(room.AuthSim.AliveCount(), mirror.AliveCount());

            int historyCount = 0;
            for (int f = room.AuthSim.Frame - SimConfig.MaxInputHistory + 1; f <= room.AuthSim.Frame; f++)
                if (f > 0 && room.HistoryFor(f, out _)) historyCount++;
            Assert.True(historyCount > 0, "重连响应应带回最近若干帧的输入历史");
        }
    }
}
