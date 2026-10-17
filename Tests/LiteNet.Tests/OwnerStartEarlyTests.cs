using System.Collections.Generic;
using System.Linq;
using LiteSim;
using LiteSim.Tests;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房主开局用例（开房裁决：**开房者 = 首个入座者**，owner 位随 JoinAck 下发；
    /// **开局唯一入口 = 房主发起**〔满员自动开局已取消——满员也停在等待〕——
    /// 世界只生成已入座实体，缺席席位沿用空输入；对局中拒加入〔此前中途加入
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
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 4, Seed = 7 });

            var first = Run(room, RoomCommand.Join(101));               // [0]=迁移事件，就位信号在 OfType 里
            var second = Run(room, RoomCommand.Join(202));

            var firstAdmit = (PlayerAdmitted)first.OfType<SignalOutput>().Select(s => s.Signal).OfType<PlayerAdmitted>().Single();
            var secondAdmit = (PlayerAdmitted)second.OfType<SignalOutput>().Select(s => s.Signal).OfType<PlayerAdmitted>().Single();
            Assert.True(firstAdmit.IsOwner);                             // 开房者 = 首入座者
            Assert.False(secondAdmit.IsOwner);
            Assert.Equal(MatchPhase.WaitingForPlayers, Phase(room));     // 未满不开
        }

        [Fact]
        public void 房主提前开局_未满员即开_只生成已入座实体()
        {
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
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
            // 满员不自动开局（用户裁决；回归钉子）：人齐仍停在 WaitingForPlayers，须房主发起
            var full = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "F", ExpectedPlayers = 2, Seed = 7 });
            Run(full, RoomCommand.Join(1));
            var fullOut = Run(full, RoomCommand.Join(2));
            Assert.DoesNotContain(fullOut, o => o is SignalOutput s && s.Signal is MatchStarted);
            Assert.Equal(MatchPhase.WaitingForPlayers, Phase(full));
            var fullStart = Run(full, RoomCommand.StartEarly(0));
            Assert.Contains(fullStart, o => o is SignalOutput s && s.Signal is MatchStarted);
            Assert.Equal(MatchPhase.Running, Phase(full));
        }

        [Fact]
        public void 非房主开局被拒_带原因输出()
        {
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
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
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 2, Seed = 7 });
            Run(room, RoomCommand.Join(101));
            Run(room, RoomCommand.StartEarly(0));

            var again = Run(room, RoomCommand.StartEarly(0));             // Running 后重复请求：幂等忽略（无输出无抛）
            Assert.Empty(again);
            Assert.Equal(MatchPhase.Running, Phase(room));

            var ghost = Run(room, RoomCommand.StartEarly(99));             // 无效席位在 Running 外同样忽略
            Assert.Empty(ghost);

            var waiting = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "W", ExpectedPlayers = 4, Seed = 7 });
            Run(waiting, RoomCommand.Join(1));                             // WaitingForPlayers 前置（Created 期无人可反馈）
            var invalid = Run(waiting, RoomCommand.StartEarly(5));        // WaitingForPlayers 下无效席位 → 拒绝输出
            Assert.IsType<StartRejectedOutput>(Assert.Single(invalid));
        }

        [Fact]
        public void 房主离席_owner位转最低在座席位_新房主可开局()
        {
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
            Run(room, RoomCommand.Join(101));                              // p0 = owner
            Run(room, RoomCommand.Join(202));
            Run(room, RoomCommand.Join(303));

            var outputs = Run(room, RoomCommand.Disconnect(101));          // 房主退房

            var changed = outputs.OfType<SignalOutput>().Select(s => s.Signal).OfType<OwnerChanged>().Single();
            Assert.Equal(1, changed.PlayerId);                            // 转给最低 playerId 的其余席位
            Assert.Equal(MatchPhase.WaitingForPlayers, Phase(room));

            var start = Run(room, RoomCommand.StartEarly(1));              // 新房主开局成功
            Assert.Contains(start, o => o is SignalOutput s && s.Signal is MatchStarted);
        }

        [Fact]
        public void 旧房主重连不夺回_非owner离席不触发迁移()
        {
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
            Run(room, RoomCommand.Join(101));                              // p0 = owner
            Run(room, RoomCommand.Join(202));
            Run(room, RoomCommand.Join(303));                              // 驻留席位：房主离席后房间不因全员离场收尾

            var nonOwner = Run(room, RoomCommand.Disconnect(202));          // 非 owner 离席：无迁移广播（席位释放）
            Assert.Empty(nonOwner.OfType<SignalOutput>().Where(s => s.Signal is OwnerChanged));

            var ownerLeft = Run(room, RoomCommand.Disconnect(101));         // 房主离席 → 席位释放 + owner 迁移
            var changed = ownerLeft.OfType<SignalOutput>().Select(s => s.Signal).OfType<OwnerChanged>().Single();
            Assert.Equal(2, changed.PlayerId);                              // p1 已释放（未开局离场即让位）——owner 转在线的 p2

            // 旧房主重新进房：空位复用拿回 0 号位——但**不凭号位二次称主**（owner 位已在 p2）
            var rejoined = Run(room, RoomCommand.Join(909));
            var pa = rejoined.OfType<SignalOutput>().Select(s => s.Signal).OfType<PlayerAdmitted>().Single();
            Assert.Equal(0, pa.PlayerId);                                   // 最低空位复用（释放的 0 号位）
            Assert.False(pa.IsOwner);

            var retry = Run(room, RoomCommand.StartEarly(0));               // 旧房主夺不回：被拒
            Assert.Contains(retry, o => o is StartRejectedOutput);
            Assert.Equal(MatchPhase.WaitingForPlayers, Phase(room));

            var newOwner = Run(room, RoomCommand.StartEarly(2));            // 房主位在 p2
            Assert.Contains(newOwner, o => o is SignalOutput s && s.Signal is MatchStarted);
        }

        [Fact]
        public void 独房主离席保持owner_对局中全断线仍转最低保留席位()
        {
            var solo = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "S", ExpectedPlayers = 2, Seed = 7 });
            Run(solo, RoomCommand.Join(101));
            var out1 = Run(solo, RoomCommand.Disconnect(101));               // 只剩房主一人：保持原位（无人可转）
            Assert.Empty(out1.OfType<SignalOutput>().Where(s => s.Signal is OwnerChanged));
            Assert.Equal(MatchPhase.Aborted, Phase(solo));                   // 且全员离场 → 未开局房收尾（不留永驻空房）

            // 未开局离场即释放（空位复用）——"全断线也转"只在对局中有意义（断线席位**保留**可重连，
            // 重连者接管 owner 防"回来的人都不是房主"死局）；未开局路径下离场者已让位、无主可迁。
            var multi = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "M", ExpectedPlayers = 4, Seed = 7 });
            Run(multi, RoomCommand.Join(101));                               // p0=owner
            Run(multi, RoomCommand.Join(202));
            Run(multi, RoomCommand.StartEarly(0));                           // 开局：此后离场席位保留（重连窗口）
            Run(multi, RoomCommand.Disconnect(202));                         // p1 对局中断线（席位保留）
            var out2 = Run(multi, RoomCommand.Disconnect(101));              // 房主再断（其余席位全为保留的断线席位）
            var changed = out2.OfType<SignalOutput>().Select(s => s.Signal).OfType<OwnerChanged>().Single();
            Assert.Equal(1, changed.PlayerId);                              // 仍转给最低其余席位（迁移先于收尾）
            Assert.Equal(MatchPhase.Closed, Phase(multi));                   // 随后全员离场 → 结算收尾
        }

        [Fact]
        public void 对局中房主离席_owner优先转在线席位_断线保留席位不优先()
        {
            // 对局中断线席位**保留**（重连窗口内可重绑）——owner 位若转给断线席位，重连后归属混乱；
            // 在线席位（Active/Restoring）优先。未开局路径无此形态（离场即释放、无断线保留席位）。
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 3, Seed = 7 });
            Run(room, RoomCommand.Join(101));                              // p0 = owner
            Run(room, RoomCommand.Join(202));                              // p1
            Run(room, RoomCommand.Join(303));                              // p2
            Run(room, RoomCommand.StartEarly(0));                          // 开局

            Run(room, RoomCommand.Disconnect(202));                        // p1 对局中断线（席位保留，可重连）

            var outputs = Run(room, RoomCommand.Disconnect(101));          // 房主断线 → owner 位迁移
            var changed = outputs.OfType<SignalOutput>().Select(s => s.Signal).OfType<OwnerChanged>().Single();
            Assert.Equal(2, changed.PlayerId);                             // 转给在线 p2，不是最低号但断线保留的 p1
            Assert.Equal(MatchPhase.Running, Phase(room));                  // 对局继续（p2 在座不收尾）
        }

        [Fact]
        public void 未开局离场释放席位_空位可复用_号位不再等价房主()
        {
            // 方案A（用户裁决）：未开局席位不可恢复（App 重连要求已开局），保留只占容量——
            // 坐满后走一人即恒答"房间已满"直到全员散尽。释放后空位按最低号复用。
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 2, Seed = 7 });
            Run(room, RoomCommand.Join(101));                              // p0 = owner
            Run(room, RoomCommand.Join(202));                              // p1：满员（2/2）

            var full = Run(room, RoomCommand.Join(303));                    // 满员拒加入
            Assert.Contains(full, o => o is JoinRejectedOutput j && j.Reason == "房间已满");

            var released = Run(room, RoomCommand.Disconnect(101));          // 未开局离场 → 释放（owner 先迁移）
            var changed = released.OfType<SignalOutput>().Select(s => s.Signal).OfType<OwnerChanged>().Single();
            Assert.Equal(1, changed.PlayerId);                              // owner 转给在座的 p1
            Assert.Null(room.SeatOf(0));                                    // 席位已释放
            var seats = released.OfType<SignalOutput>().Select(s => s.Signal).OfType<SeatsChanged>().Single();
            Assert.Single(seats.Seats);                                     // 名单缩容：释放者从列表消失
            Assert.Equal(1, room.AllocatedSeats);                           // 在册席位 = 1（高水位 NextPlayerId 仍为 2）

            var rejoined = Run(room, RoomCommand.Join(303));                // 空位复用：拿回 0 号位
            var pa = rejoined.OfType<SignalOutput>().Select(s => s.Signal).OfType<PlayerAdmitted>().Single();
            Assert.Equal(0, pa.PlayerId);                                   // 最低空位优先
            Assert.False(pa.IsOwner);                                       // 不凭号位二次称主（owner 在 p1）
            Assert.Equal(MatchPhase.WaitingForPlayers, Phase(room));         // 房间仍活（未全员离场不收尾）

            var start = Run(room, RoomCommand.StartEarly(1));               // 在座的 owner 可开局
            Assert.Contains(start, o => o is SignalOutput s && s.Signal is MatchStarted);
            Assert.Equal(MatchPhase.Running, Phase(room));
        }

        [Fact]
        public void 对局中拒加入_不再发放无实体空席位()
        {
            var room = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "R", ExpectedPlayers = 4, Seed = 7 });
            Run(room, RoomCommand.Join(101));
            Run(room, RoomCommand.StartEarly(0));                          // 房主开 4 人房（只 1 人在座）

            var late = Run(room, RoomCommand.Join(303));                    // 中途加入 → 拒绝

            var rejected = late.OfType<JoinRejectedOutput>().Single();
            Assert.Contains("对局进行中", rejected.Reason);
            Assert.Equal(1, room.NextPlayerId);                           // 未发放席位
            Assert.Equal(0L, room.EntityIdOf(1));                          // 无实体席位被开出去

            // 房间关闭语义照旧（回归钉子）
            var closed = new RoomRuntime(new RoomConfig { Map = TestMaps.Greybox(), RoomId = "C", ExpectedPlayers = 2, Seed = 7 });
            Run(closed, RoomCommand.Join(1));
            Run(closed, RoomCommand.Shutdown(ShutdownReason.Operator));
            var closedOut = Run(closed, RoomCommand.Join(9));
            Assert.Contains(closedOut, o => o is JoinRejectedOutput j && j.Reason == "房间已关闭");
        }
    }
}
