using System.Collections.Generic;
using LiteNet.Protocol;
using LiteSim;
using LiteSim.Tests;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 权威命中面（P1）：同一 (shooter,target,frame) 的霰弹多弹丸聚合为一条（伤害合计、Crit 合并），
    /// wire 映射字段完整——客户端据此驱动命中/伤害表现，不再依赖本地预测。
    /// </summary>
    public sealed class HitFeedTests
    {
        [Fact]
        public void 命中共键聚合_伤害合计与Crit合并()
        {
            var window = new HitRecordWindow();
            window.AddOrAccumulate(11, 22, 7, 10, new LiteSim.SimVector3(1f, 2f, 3f), crit: false);
            window.AddOrAccumulate(11, 22, 7, 12, new LiteSim.SimVector3(1f, 2f, 3f), crit: true);

            Assert.Equal(1, window.Count);
            RoomHitRecord r = window.Get(0);
            Assert.Equal(22, r.Value);
            Assert.True(r.Crit);
            Assert.Equal(11, r.Shooter);
            Assert.Equal(22, r.Target);
            Assert.Equal(7, r.Frame);
        }

        [Fact]
        public void 命中窗口映射到快照命中面()
        {
            var window = new HitRecordWindow();
            window.AddOrAccumulate(11, 22, 7, 30, new LiteSim.SimVector3(1f, 2f, 3f), crit: false);

            var match = new Proto.MatchStateDelta();
            HitFeedPacker.Fill(match, window);

            Assert.Single(match.RecentHits);
            Proto.HitRecord r = match.RecentHits[0];
            Assert.Equal(11, r.Shooter);
            Assert.Equal(22, r.Target);
            Assert.Equal(7, r.Frame);
            Assert.Equal(30, r.Value);
            Assert.Equal(1f, r.PosX);
            Assert.Equal(2f, r.PosY);
            Assert.Equal(3f, r.PosZ);
        }

        [Fact]
        public void 房间回溯命中_进入命中窗口并随快照下发()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(),
                RoomId = "Hit",
                Seed = 7,
                ExpectedPlayers = 2,
                Rules = new MatchRules(0, 0, 3, 2),
            }, weapons: TestWeapons.NoSpread());
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats, hits: room.Hits);
            var snapshots = new List<Proto.StateSnapshot>();
            pipeline.SendTo = (seat, type, msg, reliable) =>
            {
                if (type == PacketType.StateSnapshot) snapshots.Add((Proto.StateSnapshot)msg);
            };

            var outputs = new List<RoomOutput>();
            Join(room, new Session(1, 0), seats);
            Join(room, new Session(2, 0), seats);
            room.Execute(RoomCommand.StartEarly(0), new List<RoomOutput>());
            room.Execute(RoomCommand.Tick(0), outputs);                 // 帧 1：懒装备

            room.AuthSim.TryResolve(room.EntityIdOf(0), out int shooterSlot);
            room.AuthSim.TryResolve(room.EntityIdOf(1), out int targetSlot);
            SimVector3 target = room.AuthSim.Entities[targetSlot].Pos;
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame
            {
                AimPointX = target.X, AimPointY = target.Y + 1f, AimPointZ = target.Z,
                Buttons = SimInputFrame.ButtonFire,
            };
            room.Execute(RoomCommand.ClientInput(0, new ClientInputBatch
            {
                Frame = room.AuthSim.Frame + 1,
                AckSnapshot = room.AuthSim.Frame,
                ViewFrame = room.AuthSim.Frame,
                Count = 1,
                Frames = frames,
            }), outputs);
            room.Execute(RoomCommand.Tick(0), outputs);                 // 帧 2：前向 Fire + 回溯命中
            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf);
            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf); // 抽帧到广播

            Assert.True(room.Hits.Count > 0, "回溯命中应进入权威命中窗口");
            RoomHitRecord hit = room.Hits.Get(room.Hits.Count - 1);
            Assert.Equal(room.EntityIdOf(0), hit.Shooter);
            Assert.Equal(room.EntityIdOf(1), hit.Target);
            Assert.True(hit.Value > 0);

            Assert.NotEmpty(snapshots);
            Proto.StateSnapshot last = snapshots[snapshots.Count - 1];
            Assert.NotNull(last.Match);
            Assert.NotEmpty(last.Match.RecentHits);
            Assert.Equal(hit.Target, last.Match.RecentHits[0].Target);
        }

        private static int Join(RoomRuntime room, Session session, Session[] seats)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(session.ConnectionId), outputs);
            foreach (RoomOutput o in outputs)
            {
                if (o is SignalOutput signal && signal.Signal is PlayerAdmitted admitted)
                {
                    session.PlayerId = admitted.PlayerId;
                    seats[admitted.PlayerId] = session;
                    return admitted.PlayerId;
                }
            }
            return -1;
        }
    }
}
