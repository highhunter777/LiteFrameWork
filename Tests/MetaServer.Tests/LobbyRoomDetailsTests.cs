using System.Collections.Generic;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Lobby;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 房间明细投影用例（房间列表批）：注册带明细 → 投影直映；未上报（null）保持兼容语义；
    /// 越界上报（明细数超 maxRooms / phase 越界 / roomId 无效）装载期拒并回显字段名。
    /// phase 闸与内核 MatchPhase 枚举同源（源链接共编）：非终态值域 Created..Settling 接受，
    /// 终态（Closed/Aborted）与枚举外值拒绝——内核新增相位时本用例随枚举更新。
    /// </summary>
    public sealed class LobbyRoomDetailsTests
    {
        private static InstanceRegisterCommand Base(string instanceId, int maxRooms = 8)
            => new InstanceRegisterCommand
            {
                InstanceId = instanceId,
                BuildHash = "hash-1",
                Address = "10.0.0.1:17777",
                MaxRooms = maxRooms,
                RoomCount = 0,
                MaxPlayers = 64,
                PlayerCount = 0,
                Draining = false,
            };

        [Fact]
        public void 注册带房间明细_投影直映_换对象更新生效()
        {
            var registry = new InstanceRegistry(8, 60_000, () => 0);

            var first = Base("inst-1");
            first.RoomCount = 2;
            first.Rooms = new List<RoomReport>
            {
                new RoomReport { RoomId = "Room-A", Phase = 1, Players = 2, ExpectedPlayers = 4 },
                new RoomReport { RoomId = "Room-B", Phase = 3, Players = 8, ExpectedPlayers = 8 },
            };
            Assert.Equal(InstanceRegistry.RegisterResult.Accepted, registry.Register(first, out _));

            var rooms = Assert.Single(registry.ProjectRooms());
            Assert.NotNull(rooms.Rooms);
            Assert.Equal(2, rooms.Rooms.Count);
            Assert.Equal("Room-A", rooms.Rooms[0].RoomId);
            Assert.Equal(1, rooms.Rooms[0].Phase);                 // WaitingForPlayers = 等待中可进
            Assert.Equal(2, rooms.Rooms[0].Players);
            Assert.Equal("Room-B", rooms.Rooms[1].RoomId);
            Assert.Equal(8, rooms.Rooms[1].ExpectedPlayers);

            // 同实例再注册（心跳）——明细更新以换对象生效
            var heartbeat = Base("inst-1");
            heartbeat.RoomCount = 1;
            heartbeat.Rooms = new List<RoomReport>
            {
                new RoomReport { RoomId = "Room-A", Phase = 3, Players = 4, ExpectedPlayers = 4 },
            };
            registry.Register(heartbeat, out _);
            rooms = Assert.Single(registry.ProjectRooms());
            var detail = Assert.Single(rooms.Rooms);
            Assert.Equal(3, detail.Phase);
        }

        [Fact]
        public void 未上报明细_投影为空_不误报()
        {
            var registry = new InstanceRegistry(8, 60_000, () => 0);
            Assert.Equal(InstanceRegistry.RegisterResult.Accepted,
                registry.Register(Base("inst-legacy"), out _));    // 旧实例：不带 rooms 字段

            var rooms = Assert.Single(registry.ProjectRooms());
            Assert.Null(rooms.Rooms);                                // 未上报语义：null（区别于"上报了空表"）
        }

        [Fact]
        public void 越界上报_拒绝并回显字段名()
        {
            var registry = new InstanceRegistry(8, 60_000, () => 0);

            var over = Base("inst-2", maxRooms: 2);
            over.Rooms = new List<RoomReport>
            {
                new RoomReport { RoomId = "A", Phase = 1 },
                new RoomReport { RoomId = "B", Phase = 1 },
                new RoomReport { RoomId = "C", Phase = 1 },         // 明细数 > maxRooms
            };
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(over, out string reason));
            Assert.Equal("rooms", reason);

            var badPhase = Base("inst-3");
            badPhase.Rooms = new List<RoomReport> { new RoomReport { RoomId = "A", Phase = 9 } };   // 枚举外值（MatchPhase 值域 0..7）
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(badPhase, out reason));
            Assert.Equal("rooms[0].phase", reason);

            // 终态相位（6 = MatchPhase.Closed / 7 = Aborted）：契约"终态不报"——房间侧读面已过滤，
            // Meta 侧为防御面仍拒。**相位值用字面量**：MatchPhase 双程序集共编同名（源链接），
            // 测试不得直接命名（CS0433，先例见本工程 csproj 注释）；生产侧单源绑定在
            // InstanceRegistry 编译期（引枚举派生），不依赖本用例。
            var terminal = Base("inst-terminal");
            terminal.Rooms = new List<RoomReport> { new RoomReport { RoomId = "A", Phase = 6 } };
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(terminal, out reason));
            Assert.Equal("rooms[0].phase", reason);

            terminal.Rooms = new List<RoomReport> { new RoomReport { RoomId = "A", Phase = 7 } };
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(terminal, out reason));
            Assert.Equal("rooms[0].phase", reason);

            // 非终态上界（5 = Settling）仍接受——上界由内核枚举派生，不是手写字面量
            var settling = Base("inst-settling");
            settling.Rooms = new List<RoomReport> { new RoomReport { RoomId = "A", Phase = 5 } };
            Assert.Equal(InstanceRegistry.RegisterResult.Accepted, registry.Register(settling, out _));

            var badRoomId = Base("inst-4");
            badRoomId.Rooms = new List<RoomReport> { new RoomReport { RoomId = "", Phase = 1 } };
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(badRoomId, out reason));
            Assert.Equal("rooms[0].roomId", reason);
        }
    }
}
