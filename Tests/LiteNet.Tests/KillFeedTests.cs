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
    /// 全局击杀面（击杀提示批）：房间内核对死亡事件的记录（<see cref="KillRecordWindow"/>）、
    /// App 层 wire 映射（<see cref="KillFeedPacker"/>）、以及经 <see cref="SnapshotPipeline"/> 广播
    /// 随快照携带（客户端消费面见 EditMode 侧 <c>PublicKillFeed</c> 用例）。
    /// </summary>
    public sealed class KillFeedTests
    {
        // ---- 纯件：窗口环 ----

        [Fact]
        public void KillWindowDropsOldestWhenFullAndKeepsOrder()
        {
            var window = new KillRecordWindow(2);
            window.Add(1, 2, 10, 0);
            window.Add(3, 4, 11, 1);
            window.Add(5, 6, 12, 0);

            Assert.Equal(2, window.Count);
            Assert.Equal(3L, window.Get(0).Killer);      // 最旧（1,2）被覆盖
            Assert.Equal(4L, window.Get(0).Victim);
            Assert.Equal(11, window.Get(0).Frame);
            Assert.Equal(5L, window.Get(1).Killer);
            Assert.Equal(12, window.Get(1).Frame);
        }

        // ---- 纯件：wire 映射 ----

        [Fact]
        public void KillFeedPackerMapsWindowInOrderAndIsIdempotent()
        {
            var window = new KillRecordWindow(4);
            window.Add(1, 2, 10, 0);
            window.Add(0, 3, 11, -1);          // 自杀/环境：无归属、无武器槽

            var match = new MatchStateDelta();
            KillFeedPacker.Fill(match, window);

            Assert.Equal(2, match.RecentKills.Count);
            Assert.Equal(1L, match.RecentKills[0].Killer);
            Assert.Equal(2L, match.RecentKills[0].Victim);
            Assert.Equal(10, match.RecentKills[0].Frame);
            Assert.Equal(0, match.RecentKills[0].WeaponSlot);
            Assert.Equal(0L, match.RecentKills[1].Killer);
            Assert.Equal(11, match.RecentKills[1].Frame);
            Assert.Equal(-1, match.RecentKills[1].WeaponSlot);

            KillFeedPacker.Fill(match, window);       // 幂等：重复灌不累积
            Assert.Equal(2, match.RecentKills.Count);
        }

        // ---- 集成：死亡 → 记录 → 快照携带 ----

        [Fact]
        public void DeathIsRecordedIntoWindowAndBroadcastWithSnapshot()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(),
                RoomId = "Kill",
                Seed = 7,
                ExpectedPlayers = 2,
                Rules = new MatchRules(0, 0, 3, 2),
            });
            var seats = new Session[room.ExpectedPlayers];
            var pipeline = new SnapshotPipeline(seats, kills: room.Kills);
            var snapshots = new List<StateSnapshot>();
            pipeline.SendTo = (seat, type, msg, reliable) =>
            {
                if (type == PacketType.StateSnapshot) snapshots.Add((StateSnapshot)msg);
            };

            var outputs = new List<RoomOutput>();
            int p0 = Join(room, new Session(1, 0), seats);
            int p1 = Join(room, new Session(2, 0), seats);
            Assert.True(p0 == 0 && p1 == 1, $"席位列: {p0}/{p1}；started={room.Started} phase={room.Phase}");
            room.Execute(RoomCommand.StartEarly(0), new List<RoomOutput>());   // 房主开局（满员不再自动开局）

            room.Execute(RoomCommand.Tick(0), outputs);              // 帧 1：懒装备（两端各装备槽 0 默认武器）
            Assert.True(room.AuthSim.Frame == 1,
                $"帧未推进: frame={room.AuthSim.Frame} started={room.Started} phase={room.Phase} outputs={outputs.Count}");
            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf);   // 抽帧序 1（不播）

            room.AuthSim.TryResolve(room.EntityIdOf(0), out int killerSlot);
            room.AuthSim.Entities[killerSlot].SelectedWeapon = 1;    // 击杀帧的装备槽（回调口径：取该槽图标）
            room.AuthSim.Cmds.Write(SimCommandKind.Damage, room.EntityIdOf(1), room.EntityIdOf(0), 1000);

            room.Execute(RoomCommand.Tick(0), outputs);              // 帧 2：致死
            pipeline.BroadcastIfDue(room.AuthSim.Frame, room.AuthSim, room.Gate, room.EntityIdOf);   // 抽帧序 2（广播）

            Assert.Equal(1, room.Kills.Count);
            RoomKillRecord record = room.Kills.Get(0);
            Assert.Equal(room.EntityIdOf(0), record.Killer);
            Assert.Equal(room.EntityIdOf(1), record.Victim);
            Assert.Equal(2, record.Frame);
            Assert.Equal(1, record.WeaponSlot);

            Assert.NotEmpty(snapshots);
            StateSnapshot last = snapshots[snapshots.Count - 1];
            Assert.NotNull(last.Match);
            Assert.Single(last.Match.RecentKills);
            Assert.Equal(record.Killer, last.Match.RecentKills[0].Killer);
            Assert.Equal(record.Victim, last.Match.RecentKills[0].Victim);
            Assert.Equal(record.Frame, last.Match.RecentKills[0].Frame);
            Assert.Equal(record.WeaponSlot, last.Match.RecentKills[0].WeaponSlot);
        }

        /// <summary>
        /// 同帧存在"待回溯开火"时，前向步进的死亡事件仍须入窗：回溯判定会清事件缓冲
        /// （<c>LagCompensator.CompensateFire</c> 的 ClearFireBuffers），捕获点在环末单次会漏记
        /// 被清掉的前向死亡（同帧多射手还会互相清）——本用例钉住"回溯前 + 每位射手结算后"的捕获面。
        /// </summary>
        [Fact]
        public void DeathIsRecordedEvenWhenBackfillRunsSameFrame()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                Map = TestMaps.Greybox(),
                RoomId = "KillEdge",
                Seed = 7,
                ExpectedPlayers = 2,
                Rules = new MatchRules(0, 0, 3, 2),
            });
            var seats = new Session[room.ExpectedPlayers];
            var outputs = new List<RoomOutput>();
            int p0 = Join(room, new Session(1, 0), seats);
            int p1 = Join(room, new Session(2, 0), seats);
            Assert.True(p0 == 0 && p1 == 1, $"席位列: {p0}/{p1}");
            room.Execute(RoomCommand.StartEarly(0), new List<RoomOutput>());   // 房主开局（满员不再自动开局）

            room.Execute(RoomCommand.Tick(0), outputs);              // 帧 1：懒装备

            // 帧 2：前向直接伤害致死 p1（前向死亡事件）
            room.AuthSim.Cmds.Write(SimCommandKind.Damage, room.EntityIdOf(1), room.EntityIdOf(0), 1000);
            // 同帧 p0 还有一条待回溯开火（带视点帧）——其回溯会清事件缓冲
            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(0), out int shooterSlot));
            SimVector3 pos = room.AuthSim.Entities[shooterSlot].Pos;
            var frames = new SimInputFrame[ClientInputBatch.MaxFrames];
            frames[0] = new SimInputFrame
            {
                AimPointX = pos.X, AimPointY = pos.Y + 10f, AimPointZ = pos.Z,   // 近垂直朝天：纯脱靶，不干扰他人
                Buttons = SimInputFrame.ButtonFire,
            };
            room.Execute(RoomCommand.ClientInput(0, new ClientInputBatch
            {
                Frame = room.AuthSim.Frame + 1, AckSnapshot = room.AuthSim.Frame, ViewFrame = 30,
                Count = 1, Frames = frames,
            }), outputs);

            room.Execute(RoomCommand.Tick(0), outputs);              // 帧 2：致死 + p0 回溯开火

            Assert.Equal(1, room.Kills.Count);
            RoomKillRecord record = room.Kills.Get(0);
            Assert.Equal(room.EntityIdOf(0), record.Killer);
            Assert.Equal(room.EntityIdOf(1), record.Victim);
            Assert.Equal(2, record.Frame);
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
