using System.Collections.Generic;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// Match 状态机用例（《商业级通用服务端框架总设计》§9.1）：
    /// 迁移表合法性、非法迁移拒绝、超时策略（等待/对局时限）、全员离场收尾、
    /// Finishing 冻结摘要 + SettlementReady + 幂等关闭。
    /// 全部纯 L1（无 Socket/Sleep/文件/墙钟——时间由 Tick 命令注入）。
    /// </summary>
    public sealed class MatchStateMachineTests
    {
        // ---- 状态机本体（纯迁移表）----

        [Fact]
        public void 状态机_全链迁移合法_记录进入时刻与原因()
        {
            var m = new MatchStateMachine();
            Assert.Equal(MatchPhase.Created, m.Phase);

            Assert.True(m.TryTransition(MatchPhase.WaitingForPlayers, "first-join", 100));
            Assert.True(m.TryTransition(MatchPhase.Starting, "full", 200));
            Assert.True(m.TryTransition(MatchPhase.Running, "started", 300));
            Assert.Equal(300, m.EnteredMs);
            Assert.Equal("started", m.LastReason);

            Assert.True(m.TryTransition(MatchPhase.Finishing, "time-limit", 400));
            Assert.True(m.TryTransition(MatchPhase.Settling, "settle", 401));
            Assert.True(m.TryTransition(MatchPhase.Closed, "closed", 402));
            Assert.True(MatchStateMachine.IsTerminal(m.Phase));
        }

        [Fact]
        public void 状态机_非法迁移被拒且不改状态()
        {
            var m = new MatchStateMachine();
            Assert.False(m.TryTransition(MatchPhase.Running, "jump", 1));      // 跨阶段
            Assert.Equal(MatchPhase.Created, m.Phase);

            m.TryTransition(MatchPhase.WaitingForPlayers, "first-join", 1);
            Assert.False(m.TryTransition(MatchPhase.Running, "jump", 2));      // 跳过 Starting

            m.TryTransition(MatchPhase.Starting, "full", 3);
            m.TryTransition(MatchPhase.Running, "started", 4);
            m.TryTransition(MatchPhase.Finishing, "limit", 5);
            m.TryTransition(MatchPhase.Settling, "settle", 6);
            m.TryTransition(MatchPhase.Closed, "closed", 7);

            // 终态：任何迁移都被拒
            Assert.False(m.TryTransition(MatchPhase.Running, "revive", 8));
            Assert.Equal(MatchPhase.Closed, m.Phase);
            Assert.Equal("closed", m.LastReason);
        }

        [Fact]
        public void 状态机_任意阶段可中止()
        {
            foreach (MatchPhase stop in new[]
            {
                MatchPhase.Created, MatchPhase.WaitingForPlayers, MatchPhase.Starting,
                MatchPhase.Running, MatchPhase.Finishing, MatchPhase.Settling,
            })
            {
                var m = new MatchStateMachine();
                Walk(m, stop);
                Assert.True(m.TryTransition(MatchPhase.Aborted, "error", 999), $"{stop} 应可中止");
                Assert.True(MatchStateMachine.IsTerminal(m.Phase));
            }
        }

        private static void Walk(MatchStateMachine m, MatchPhase to)
        {
            switch (to)
            {
                case MatchPhase.Created: return;
                case MatchPhase.WaitingForPlayers: m.TryTransition(MatchPhase.WaitingForPlayers, "j", 1); return;
                case MatchPhase.Starting: Walk(m, MatchPhase.WaitingForPlayers); m.TryTransition(MatchPhase.Starting, "f", 2); return;
                case MatchPhase.Running: Walk(m, MatchPhase.Starting); m.TryTransition(MatchPhase.Running, "s", 3); return;
                case MatchPhase.Finishing: Walk(m, MatchPhase.Running); m.TryTransition(MatchPhase.Finishing, "l", 4); return;
                case MatchPhase.Settling: Walk(m, MatchPhase.Finishing); m.TryTransition(MatchPhase.Settling, "st", 5); return;
            }
        }

        // ---- 房间集成（超时/离场/冻结摘要）----

        [Fact]
        public void 等待超时_中止且不产出结算()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                RoomId = "WaitTimeout", ExpectedPlayers = 2, Seed = 1, WaitingTimeoutMs = 1000,
            });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);                 // 只进 1 人（未满）
            Assert.Equal(MatchPhase.WaitingForPlayers, room.Phase);

            room.Execute(RoomCommand.Tick(999), outputs);
            Assert.Equal(MatchPhase.WaitingForPlayers, room.Phase);      // 未到点

            room.Execute(RoomCommand.Tick(1000), outputs);
            Assert.Equal(MatchPhase.Aborted, room.Phase);
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { To: MatchPhase.Aborted, Reason: "waiting-timeout:WaitingTimeout" });
            Assert.DoesNotContain(outputs, o => o is SettlementReadyOutput);
        }

        [Fact]
        public void 对局时限_收尾迁移并冻结摘要()
        {
            var room = new RoomRuntime(new RoomConfig
            {
                RoomId = "TimeLimit", ExpectedPlayers = 2, Seed = 42, MatchTimeLimitMs = 1000,
            });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);                  // 满员开局（Running，EnteredMs=0）
            Assert.Equal(MatchPhase.Running, room.Phase);
            outputs.Clear();

            room.Execute(RoomCommand.Tick(500), outputs);
            Assert.Equal(MatchPhase.Running, room.Phase);                // 未到点，继续步进
            Assert.True(room.AuthSim.Frame > 0);

            int frameBeforeEnd = room.AuthSim.Frame;
            room.Execute(RoomCommand.Tick(1000), outputs);

            Assert.Equal(MatchPhase.Closed, room.Phase);
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { From: MatchPhase.Running, To: MatchPhase.Finishing });
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { From: MatchPhase.Finishing, To: MatchPhase.Settling });
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { From: MatchPhase.Settling, To: MatchPhase.Closed });

            SettlementReadyOutput ready = null;
            foreach (RoomOutput o in outputs)
                if (o is SettlementReadyOutput sr) ready = sr;
            Assert.NotNull(ready);
            Assert.Equal("TimeLimit", ready.Summary.MatchId);
            Assert.Equal(42L, ready.Summary.Seed);
            Assert.Equal(frameBeforeEnd, ready.Summary.FinalFrame);      // 冻结的是收尾时帧号（此后不再步进）
            Assert.Equal(ShutdownReason.TimeLimit, ready.Summary.EndReason);
            Assert.Equal(new[] { 0, 1 }, ready.Summary.SeatPlayerIds);

            // 收尾后不再步进
            outputs.Clear();
            room.Execute(RoomCommand.Tick(5000), outputs);
            Assert.Equal(frameBeforeEnd, room.AuthSim.Frame);
            Assert.Empty(outputs);
        }

        [Fact]
        public void 全员离场_收尾并清算()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "AllLeft", ExpectedPlayers = 2, Seed = 7 });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            Assert.Equal(MatchPhase.Running, room.Phase);

            room.Execute(RoomCommand.Disconnect(1), outputs);
            Assert.Equal(MatchPhase.Running, room.Phase);                // 一人掉线不停局
            Assert.Contains(outputs, o => o is MatchStateChangedOutput);  // 其实不应有迁移（Join 时的迁移除外）

            outputs.Clear();
            room.Execute(RoomCommand.Disconnect(2), outputs);
            Assert.Equal(MatchPhase.Closed, room.Phase);
            SettlementReadyOutput ready = null;
            foreach (RoomOutput o in outputs)
                if (o is SettlementReadyOutput sr) ready = sr;
            Assert.NotNull(ready);
            Assert.Equal(ShutdownReason.AllPlayersLeft, ready.Summary.EndReason);
        }

        [Fact]
        public void 未开局关闭_走中止而非结算()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "AbortEarly", ExpectedPlayers = 2, Seed = 3 });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);                  // 只进 1 人
            outputs.Clear();

            room.Execute(RoomCommand.Shutdown(ShutdownReason.Operator), outputs);
            Assert.Equal(MatchPhase.Aborted, room.Phase);
            Assert.DoesNotContain(outputs, o => o is SettlementReadyOutput);
        }

        [Fact]
        public void 终态拒绝再进房()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "ClosedJoin", ExpectedPlayers = 1, Seed = 9 });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Shutdown(ShutdownReason.Operator), outputs);
            Assert.Equal(MatchPhase.Closed, room.Phase);

            outputs.Clear();
            room.Execute(RoomCommand.Join(2), outputs);
            Assert.Contains(outputs, o => o is JoinRejectedOutput jr && jr.Reason == "房间已关闭");
        }
    }
}
