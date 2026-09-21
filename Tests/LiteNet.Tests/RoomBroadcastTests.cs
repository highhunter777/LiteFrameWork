using System;
using System.Collections.Generic;
using Google.Protobuf;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房间广播 / E1 背压 / 重连服务 用例（《M10实施指导》§2.7/§2.8 + §3"E1 背压"/"E3 加固"组）。
    ///
    /// 形态：直接驱动 <see cref="Room"/>（假传输——用 <c>Room.SendTo</c> 捕获出站包），
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

        private static (Room room, Capture capture, Session s1, Session s2) BuildStartedRoom()
        {
            var room = new Room(new RoomConfig { RoomId = "TestRoom" });
            var capture = new Capture();
            room.SendTo = (session, type, msg, reliable) => capture.Sent.Add((session, type, msg, reliable));
            room.Broadcaster.SendTo = room.SendTo;   // 广播面拆分后共用同一捕获（快照经 Broadcaster 发出）

            var s1 = new Session(1, 0);
            var s2 = new Session(2, 0);
            Assert.Equal(0, room.AssignPlayerId(s1));
            Assert.Equal(1, room.AssignPlayerId(s2));
            room.Start(SharedSeed);
            return (room, capture, s1, s2);
        }

        /// <summary>合成一条"第 frame 帧、玩家 p 的移动输入"（EntityId 必填——缺省 0 会被判失效实体）。</summary>
        private static InputMessage MoveInput(Room room, int playerId, int frame, float moveX)
        {
            long entityId = room.EntityIdOf(playerId);
            return new InputMessage
            {
                Frame = frame,
                AckSnapshot = 0,
                Frames = { new InputFrame { EntityId = entityId, MoveX = moveX, AimX = 1f } },
            };
        }

        [Fact]
        public void 快照按30Hz广播_每两逻辑帧一次()
        {
            var (room, capture, s1, s2) = BuildStartedRoom();

            for (int i = 0; i < 10; i++) room.StepFrame();

            // 10 逻辑帧 → 30Hz 抽帧 → 5 次广播 × 2 客户端 = 10 个快照包
            int stride = SimConfig.TickRate / SimConfig.SnapshotHz;
            Assert.Equal(10, room.AuthSim.Frame);
            Assert.Equal(10 / stride * 2, capture.CountOf(PacketType.StateSnapshot));
            Assert.True(capture.SnapshotsFor(s1).Count > 0 && capture.SnapshotsFor(s2).Count > 0);
        }

        [Fact]
        public void 首包全量_后续增量_活体数变化再全量()
        {
            var (room, capture, s1, _) = BuildStartedRoom();

            for (int i = 0; i < 6; i++) room.StepFrame();
            List<StateSnapshot> snapshots = capture.SnapshotsFor(s1);

            Assert.True(snapshots[0].IsFull, "首个快照必须是全量（客户端从零重建）");
            Assert.Equal(room.AuthSim.AliveCount(), snapshots[0].Slots.Count);
            for (int i = 1; i < snapshots.Count; i++) Assert.False(snapshots[i].IsFull, $"第 {i} 个快照应为增量");

            // 杀一个实体 → 含该帧的广播必须转全量（缺席无法表达"死了"）。
            // 先推进到下一广播边界的前一帧，再杀——保证"活体变化"这一帧本身就是广播帧
            capture.Sent.Clear();
            while ((room.AuthSim.Frame + 1) % (SimConfig.TickRate / SimConfig.SnapshotHz) != 0) room.StepFrame();
            room.AuthSim.Despawn(room.EntityIdOf(1));
            for (int i = 0; i < 4; i++) room.StepFrame();
            List<StateSnapshot> after = capture.SnapshotsFor(s1);
            Assert.True(after[0].IsFull, $"活体集合变化的那次广播必须是全量（slots={after[0].Slots.Count} full={after[0].IsFull}）");
        }

        [Fact]
        public void 掉线成员不广播_其余成员不受影响()
        {
            var (room, capture, s1, s2) = BuildStartedRoom();
            s2.Disconnected = true;

            for (int i = 0; i < 4; i++) room.StepFrame();

            Assert.True(capture.SnapshotsFor(s1).Count > 0, "在线成员应继续收到快照");
            Assert.Empty(capture.SnapshotsFor(s2));                  // 掉线者不占带宽（掉线不停帧，但不发）
        }

        [Fact]
        public void 广播私有面_各客户端只收到本人私有态_比赛状态全体同值()
        {
            var (room, capture, s1, s2) = BuildStartedRoom();

            // 各玩家私有运行态不同（弹药数——只应出现在本人的快照里，业务总设计 §1 禁泄漏）
            room.AuthSim.Weapons[0 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 30;
            room.AuthSim.Weapons[1 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 8;
            room.AuthSim.Match = new MatchStateData { Phase = 1, Timer = 10800, Round = 1 };

            for (int i = 0; i < 4; i++) room.StepFrame();

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
            var (room, capture, s1, s2) = BuildStartedRoom();

            s1.SendQueueBytes = ProtocolConstants.BackpressureQueueLimitBytes * 4;   // s1 堆积（模拟慢客户端）
            s1.AckedBytes = 0;

            for (int i = 0; i < 20; i++) room.StepFrame();

            int s1Count = capture.SnapshotsFor(s1).Count;
            int s2Count = capture.SnapshotsFor(s2).Count;
            Assert.True(s1.BackpressureTier >= 1, $"慢客户端应已降档（tier={s1.BackpressureTier}）");
            Assert.True(room.BackpressureThrottled > 0, "应有抽帧计数");
            Assert.True(s2Count > s1Count,
                $"其余客户端不应被拖累：s1={s1Count} s2={s2Count}（tier={s1.BackpressureTier}/{s2.BackpressureTier}）");
            Assert.Equal(0, s2.BackpressureTier);
        }

        [Fact]
        public void 背压恢复_水位回落后逐档恢复()
        {
            var (room, _, s1, _) = BuildStartedRoom();
            s1.SendQueueBytes = ProtocolConstants.BackpressureQueueLimitBytes * 4;
            for (int i = 0; i < 6; i++) room.StepFrame();
            Assert.True(s1.BackpressureTier >= 1);

            // ack 到达 → 队列消化（AckedBytes 追上）
            room.OnClientAck(s1, room.AuthSim.Frame);
            int tierAfterAck = s1.BackpressureTier;

            // 恢复需要连续达标 2s（120 帧）→ 跑够时间。
            // P0 起快照分层携带比赛状态 + 本人私有面（每包约 +60B）——单次 ack 后队列会重新积压，
            // "只 ack 一次就静默恢复"的经济性不再成立；真实客户端每输入包都带 ack_snapshot（§4.5），
            // 这里对齐真实反馈节奏：每步 ack（水位维持 ≤ 阈值 40%）→ 恢复判据才可稳定观测
            for (int i = 0; i < ProtocolConstants.RecoverHoldMillis * SimConfig.TickRate / 1000 + 5; i++)
            {
                room.StepFrame();
                room.OnClientAck(s1, room.AuthSim.Frame);
            }

            Assert.True(s1.BackpressureTier < tierAfterAck || s1.BackpressureTier == 0,
                $"水位回落后应恢复档位（tier {tierAfterAck} → {s1.BackpressureTier}）");
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
            var (room, capture, _, _) = BuildStartedRoom();

            // 跑几帧并喂输入（历史才有内容）
            for (int i = 0; i < 6; i++)
            {
                room.OnInput(GetSession(room, 0), MoveInput(room, 0, room.AuthSim.Frame + 1, 1f));
                room.StepFrame();
            }

            var service = new ReconnectService();
            string ticket = service.Issue(0, room.RoomId);
            Assert.True(service.TryConsume(ticket, out int playerId, out _));
            Assert.Equal(0, playerId);

            // 客户端凭票取"权威快照 + 输入历史"（服务器侧能力，§5.6 首选路径）
            var recovered = room.Differ.Build(room.AuthSim.Frame, room.AuthSim, 0,
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
            _ = capture;
        }

        private static Session GetSession(Room room, int playerId)
        {
            Assert.True(room.TryGetMember(playerId, out Session session));
            return session;
        }
    }
}
