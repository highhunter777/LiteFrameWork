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
    /// 纪律：数值一律经**实例**传入（技术债 #1 根治后无全局装载面可写、无还原需求），
    /// 只做"同刻绑定/派生行为"的断言。
    /// </summary>
    public sealed class RoomRuntimeConfigTests
    {
        [Fact]
        public void 配置快照_与传入实例同刻绑定()
        {
            CombatValues values = CombatValues.Default;
            FixedCombatConfig snap = FixedCombatConfig.Capture(values, WeaponTable.Default);

            Assert.Equal(values.MoveSpeed, snap.Values.MoveSpeed);
            Assert.Equal(values.Gravity, snap.Values.Gravity);
            Assert.Equal(values.HitscanRange, snap.Values.HitscanRange);
            Assert.Equal(values.BaseDamage, snap.Values.BaseDamage);
            Assert.Equal(values.DamageSpread, snap.Values.DamageSpread);
            Assert.Equal(values.EntityHp, snap.Values.EntityHp);
            Assert.Equal(CombatConfigDigest.Compute(values), snap.Digest);   // 摘要同刻绑定（§4/P0-5）
        }

        [Fact]
        public void 开局使用房间快照数值_并下发绑定的配置摘要()
        {
            var custom = new CombatValues(7.5f, -9.8f, 50f, 40, 0, 130);   // 自定义实例：证明数值经参数入房（债 #1）
            var room = new RoomRuntime(new RoomConfig { RoomId = "Cfg", ExpectedPlayers = 1, Seed = 77 }, custom);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);

            MatchStarted started = null;
            foreach (RoomOutput o in outputs)
                if (o is SignalOutput { Signal: MatchStarted ms }) started = ms;

            Assert.NotNull(started);
            Assert.Equal(CombatConfigDigest.Compute(custom), started.ConfigHash);   // 下发"本局数值身份"= 按实例计算摘要
            Assert.Equal(77L, started.Seed);

            // 出生 HP 取自房间快照（实例值直达——技术债 #1：无全局静态面）
            Assert.True(room.AuthSim.TryResolve(room.EntityIdOf(0), out int slot));
            Assert.Equal(custom.EntityHp, room.AuthSim.Entities[slot].Hp);
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
