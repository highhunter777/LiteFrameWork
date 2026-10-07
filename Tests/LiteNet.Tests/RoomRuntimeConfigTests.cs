using System.Collections.Generic;
using LiteSim;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房间级配置快照与 seed 派生用例（《商业级通用服务端框架总设计》§4/P0-5 + §8.1 纯化）：
    /// - 房间创建时**固定**不可变玩法配置并绑定规范化摘要（此后进程改全局不影响本房间的值消费）；
    /// - seed=0 时由 Tick 注入的单调时间派生（Runtime 不再读系统墙钟——纯化红线）。
    ///
    /// 纪律：不写全局 <see cref="CombatConfig"/> 装载状态（会与其他用例竞态），
    /// 只做"同刻绑定/派生行为"的断言。
    /// </summary>
    public sealed class RoomRuntimeConfigTests
    {
        [Fact]
        public void 配置快照_与全局装载值同刻绑定()
        {
            FixedCombatConfig snap = FixedCombatConfig.Capture();

            Assert.Equal(CombatConfig.MoveSpeed, snap.MoveSpeed);
            Assert.Equal(CombatConfig.Gravity, snap.Gravity);
            Assert.Equal(CombatConfig.HitscanRange, snap.HitscanRange);
            Assert.Equal(CombatConfig.BaseDamage, snap.BaseDamage);
            Assert.Equal(CombatConfig.DamageSpread, snap.DamageSpread);
            Assert.Equal(CombatConfig.EntityHp, snap.EntityHp);
            Assert.Equal(CombatConfigDigest.Compute(), snap.Digest);   // 摘要同刻绑定（§4/P0-5）
        }

        [Fact]
        public void 开局使用房间快照数值_并下发绑定的配置摘要()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "Cfg", ExpectedPlayers = 1, Seed = 77 });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);

            MatchStarted started = null;
            foreach (RoomOutput o in outputs)
                if (o is SignalOutput { Signal: MatchStarted ms }) started = ms;

            Assert.NotNull(started);
            Assert.Equal(room.FixedConfig.Digest, started.ConfigHash);   // 下发"本局数值身份"= 创建时绑定摘要
            Assert.Equal(77L, started.Seed);

            // 出生 HP 取自房间快照（而非运行中可能被改的全局静态）
            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(0), out int slot));
            Assert.Equal(room.FixedConfig.EntityHp, room.AuthSim.Entities[slot].Hp);
        }

        [Fact]
        public void seed为零_由Tick时间派生_不读系统墙钟()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "Seed", ExpectedPlayers = 1, Seed = 0 });
            var outputs = new List<RoomOutput>();

            room.Execute(RoomCommand.Tick(123456), outputs);         // App 注入的单调毫秒（虚拟时钟形态）
            room.Execute(RoomCommand.Join(1), outputs);              // 满员自动开局

            Assert.True(room.Started);
            Assert.NotEqual(0L, room.Seed);                          // 派生出非零种子（无需 DateTime.Now）

            // 同刻同输入派生同值（确定性——不依赖墙钟）
            var room2 = new RoomRuntime(new RoomConfig { RoomId = "Seed", ExpectedPlayers = 1, Seed = 0 });
            var outputs2 = new List<RoomOutput>();
            room2.Execute(RoomCommand.Tick(123456), outputs2);
            room2.Execute(RoomCommand.Join(1), outputs2);
            Assert.Equal(room.Seed, room2.Seed);
        }

        [Fact]
        public void 关闭命令_收尾迁移且幂等停步()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "Shut", ExpectedPlayers = 1, Seed = 5 });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);            // 满员自动开局（Running）
            outputs.Clear();

            room.Execute(RoomCommand.Shutdown(ShutdownReason.Operator), outputs);

            Assert.Equal(MatchPhase.Closed, room.Phase);
            Assert.True(room.Closed);
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { From: MatchPhase.Running, To: MatchPhase.Finishing });
            Assert.Contains(outputs, o => o is SettlementReadyOutput sr && sr.Summary.EndReason == ShutdownReason.Operator);
            Assert.Contains(outputs, o => o is MatchStateChangedOutput { From: MatchPhase.Settling, To: MatchPhase.Closed });

            int frame = room.AuthSim.Frame;
            outputs.Clear();
            room.Execute(RoomCommand.Shutdown(ShutdownReason.Operator), outputs);   // 幂等：终态只记事件不重迁移
            Assert.Single(outputs);
            Assert.Equal("重复关闭（忽略）", ((MatchStateChangedOutput)outputs[0]).Reason);

            outputs.Clear();
            room.Execute(RoomCommand.Tick(1000), outputs);                          // 关闭后不再步进
            Assert.Equal(frame, room.AuthSim.Frame);
            Assert.Empty(outputs);
        }
    }
}
