using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using LiteSim.Tests;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 公共开火面（远端开火火光）：房间内核对前向开火事件的记录（<see cref="FireRecordWindow"/>）、
    /// App 层 wire 映射（<see cref="FireSignalPacker"/>）、以及经 <see cref="SnapshotPipeline"/> 广播
    /// 随快照携带（客户端消费面见 EditMode 侧 <c>PublicFireFeed</c> 用例）。
    /// </summary>
    public sealed class FireSignalTests
    {
        // ---- 纯件：窗口环 ----

        [Fact]
        public void FireWindowDropsOldestWhenFullAndKeepsOrder()
        {
            var window = new FireRecordWindow(2);
            window.Add(1, 10);
            window.Add(2, 11);
            window.Add(3, 12);

            Assert.Equal(2, window.Count);
            Assert.Equal(2L, window.Get(0).Shooter);     // 最旧（1,10）被覆盖 → 头部件为 (2,11)
            Assert.Equal(11, window.Get(0).Frame);
            Assert.Equal(3L, window.Get(1).Shooter);
            Assert.Equal(12, window.Get(1).Frame);
        }

        // ---- 纯件：wire 映射 ----

        [Fact]
        public void FireSignalPackerMapsWindowInOrderAndIsIdempotent()
        {
            var window = new FireRecordWindow(4);
            window.Add(7, 21);
            window.Add(8, 22);

            var match = new MatchStateDelta();
            FireSignalPacker.Fill(match, window);

            Assert.Equal(2, match.RecentFires.Count);
            Assert.Equal(7L, match.RecentFires[0].Shooter);
            Assert.Equal(21, match.RecentFires[0].Frame);
            Assert.Equal(8L, match.RecentFires[1].Shooter);
            Assert.Equal(22, match.RecentFires[1].Frame);

            FireSignalPacker.Fill(match, window);       // 幂等：重复灌不累积
            Assert.Equal(2, match.RecentFires.Count);
        }

        // ---- 集成：开火 → 记录 → 快照携带（**双射手同帧待回溯**——前向捕获点须在回溯清缓冲之前） ----

        [Fact]
        public void ForwardFireIsRecordedAndBroadcastEvenWhenBackfillRuns()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(),
                RoomId = "Fire",
                Seed = 7,
                ExpectedPlayers = 2,
                Rules = new MatchRules(0, 0, 3, 2),
            });
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats, kills: room.Kills, fires: room.Fires);
            var snapshots = new List<StateSnapshot>();
            pipeline.SendTo = (seat, type, msg, reliable) =>
            {
                if (type == PacketType.StateSnapshot) snapshots.Add((StateSnapshot)msg);
            };

            var outputs = new List<RoomOutput>();
            int p0 = Join(room, new Session(1, 0), seats);
            int p1 = Join(room, new Session(2, 0), seats);
            Assert.True(p0 == 0 && p1 == 1, $"席位列: {p0}/{p1}");

            room.Execute(RoomCommand.Tick(0), outputs);              // 帧 1：懒装备
            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf);   // 抽帧序 1（不播）

            Fire(room, outputs, 0);
            Fire(room, outputs, 1);                                  // 两名射手同帧开火（均带视点帧 → 双回溯在场）

            room.Execute(RoomCommand.Tick(0), outputs);              // 帧 2：两人前向开火各产 Fire 事件
            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf);   // 抽帧序 2（广播）

            Assert.Equal(2, room.Fires.Count);                       // 回溯清缓冲后两条都在（前向捕获点）
            RoomFireRecord first = room.Fires.Get(0);
            RoomFireRecord second = room.Fires.Get(1);
            Assert.Equal(room.EntityIdOf(0), first.Shooter);
            Assert.Equal(room.EntityIdOf(1), second.Shooter);
            Assert.Equal(2, first.Frame);
            Assert.Equal(2, second.Frame);

            Assert.NotEmpty(snapshots);
            StateSnapshot last = snapshots[snapshots.Count - 1];
            Assert.NotNull(last.Match);
            Assert.Equal(2, last.Match.RecentFires.Count);
            Assert.Equal(first.Shooter, last.Match.RecentFires[0].Shooter);
            Assert.Equal(second.Shooter, last.Match.RecentFires[1].Shooter);
        }

        /// <summary>提交一名射手的开火输入（带视点帧 → 走待回溯路径；近垂直朝天 = 纯脱靶）。</summary>
        private static void Fire(RoomRuntime room, List<RoomOutput> outputs, int playerId)
        {
            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(playerId), out int slot));
            SimVector3 pos = room.AuthSim.Entities[slot].Pos;
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame
            {
                AimPointX = pos.X, AimPointY = pos.Y + 10f, AimPointZ = pos.Z,
                Buttons = SimInputFrame.ButtonFire,
            };
            room.Execute(RoomCommand.ClientInput(playerId, new ClientInputBatch
            {
                Frame = room.AuthSim.Frame + 1, AckSnapshot = room.AuthSim.Frame, ViewFrame = 30,
                Count = 1, Frames = frames,
            }), outputs);
        }

        /// <summary>执行一次 Join 命令并把席位绑定到 App 层映射（App 装配的测试等价物）。</summary>
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
