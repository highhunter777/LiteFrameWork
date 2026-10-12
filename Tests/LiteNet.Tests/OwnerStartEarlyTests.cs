using System.Collections.Generic;
using System.Linq;
using LiteSim;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房主提前开局用例（开房裁决：**开房者 = 首个入座者**，owner 位随 JoinAck 下发；
    /// 未满员可开局——世界只生成已入座实体，缺席席位沿用空输入；对局中拒加入〔此前中途加入
    /// 会拿到无实体的空席位〕）。Runtime 直测（纯逻辑）；宿主路由/协议面归 ServerHost 集成用例。
    /// </summary>
    public sealed class OwnerStartEarlyTests
    {
        private static List<RoomOutput> Run(RoomRuntime room, in RoomCommand cmd)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(cmd, outputs);
            return outputs;
        }

        private static MatchPhase Phase(RoomRuntime room)
        {
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Tick(0), outputs);                  // 推一帧不改变相位，读 Phase（属性直读即可）
            return room.Phase;
        }

        [Fact]
        public void 首入座者为房主_随就位信号下发()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "R", ExpectedPlayers = 4, Seed = 7 });

            var first = Run(room, RoomCommand.Join(101));               // [0]=迁移事件，就位信号在 OfType 里
            var second = Run(room, RoomCommand.Join(202));

            var firstAdmit = (PlayerAdmitted)first.OfType<SignalOutput>().Single().Signal;
            var secondAdmit = (PlayerAdmitted)second.OfType<SignalOutput>().Single().Signal;
            Assert.True(firstAdmit.IsOwner);                             // 开房者 = 首入座者
            Assert.False(secondAdmit.IsOwner);
            Assert.Equal(MatchPhase.WaitingForPlayers, Phase(room));     // 未满不开
        }

        [Fact]
        public void 房主提前开局_未满员即开_只生成已入座实体()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
            Run(room, RoomCommand.Join(101));
            Run(room, RoomCommand.Join(202));

            var outputs = Run(room, RoomCommand.StartEarly(0));

            Assert.Equal(MatchPhase.Running, Phase(room));
            Assert.Contains(outputs, o => o is SignalOutput s && s.Signal is MatchStarted);   // 开局广播
            // 已入座 2 人各持实体；未入座 2 席位无实体（EntityId 恒 0）
            Assert.NotEqual(0L, room.EntityIdOf(0));
            Assert.NotEqual(0L, room.EntityIdOf(1));
            Assert.Equal(0L, room.EntityIdOf(2));
            Assert.Equal(0L, room.EntityIdOf(3));
            // 满员开局照旧（回归钉子）：满员自动路径不受 owner 开局影响
            var full = new RoomRuntime(new RoomConfig { RoomId = "F", ExpectedPlayers = 2, Seed = 7 });
            Run(full, RoomCommand.Join(1));
            var fullOut = Run(full, RoomCommand.Join(2));
            Assert.Contains(fullOut, o => o is SignalOutput s && s.Signal is MatchStarted);
            Assert.Equal(MatchPhase.Running, Phase(full));
        }

        [Fact]
        public void 非房主开局被拒_带原因输出()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
            Run(room, RoomCommand.Join(101));
            Run(room, RoomCommand.Join(202));

            var outputs = Run(room, RoomCommand.StartEarly(1));          // 第二入座者非房主

            Assert.Equal(MatchPhase.WaitingForPlayers, Phase(room));     // 没开
            var rejected = outputs.OfType<StartRejectedOutput>().Single();
            Assert.Equal(1, rejected.PlayerId);
            Assert.Contains("房主", rejected.Reason);
        }

        [Fact]
        public void 开局后重复请求幂等忽略_无效席位拒绝()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "R", ExpectedPlayers = 2, Seed = 7 });
            Run(room, RoomCommand.Join(101));
            Run(room, RoomCommand.StartEarly(0));

            var again = Run(room, RoomCommand.StartEarly(0));             // Running 后重复请求：幂等忽略（无输出无抛）
            Assert.Empty(again);
            Assert.Equal(MatchPhase.Running, Phase(room));

            var ghost = Run(room, RoomCommand.StartEarly(99));             // 无效席位在 Running 外同样忽略
            Assert.Empty(ghost);

            var waiting = new RoomRuntime(new RoomConfig { RoomId = "W", ExpectedPlayers = 4, Seed = 7 });
            Run(waiting, RoomCommand.Join(1));                             // WaitingForPlayers 前置（Created 期无人可反馈）
            var invalid = Run(waiting, RoomCommand.StartEarly(5));        // WaitingForPlayers 下无效席位 → 拒绝输出
            Assert.IsType<StartRejectedOutput>(Assert.Single(invalid));
        }

        [Fact]
        public void 对局中拒加入_不再发放无实体空席位()
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
            Run(room, RoomCommand.Join(101));
            Run(room, RoomCommand.StartEarly(0));                          // 房主开 4 人房（只 1 人在座）

            var late = Run(room, RoomCommand.Join(303));                    // 中途加入 → 拒绝

            var rejected = late.OfType<JoinRejectedOutput>().Single();
            Assert.Contains("对局进行中", rejected.Reason);
            Assert.Equal(1, room.NextPlayerId);                           // 未发放席位
            Assert.Equal(0L, room.EntityIdOf(1));                          // 无实体席位被开出去

            // 房间关闭语义照旧（回归钉子）
            var closed = new RoomRuntime(new RoomConfig { RoomId = "C", ExpectedPlayers = 2, Seed = 7 });
            Run(closed, RoomCommand.Join(1));
            Run(closed, RoomCommand.Shutdown(ShutdownReason.Operator));
            var closedOut = Run(closed, RoomCommand.Join(9));
            Assert.Contains(closedOut, o => o is JoinRejectedOutput j && j.Reason == "房间已关闭");
        }
    }
}
