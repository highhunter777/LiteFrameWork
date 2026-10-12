using System.Collections.Generic;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Lobby;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 房间明细投影用例（房间列表批）：注册带明细 → 投影直映；未上报（null）保持兼容语义；
    /// 越界上报（明细数超 maxRooms / phase 越界 / roomId 无效）装载期拒并回显字段名。
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
            badPhase.Rooms = new List<RoomReport> { new RoomReport { RoomId = "A", Phase = 9 } };   // MatchPhase 只有 0..5
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(badPhase, out reason));
            Assert.Equal("rooms[0].phase", reason);

            var badRoomId = Base("inst-4");
            badRoomId.Rooms = new List<RoomReport> { new RoomReport { RoomId = "", Phase = 1 } };
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(badRoomId, out reason));
            Assert.Equal("rooms[0].roomId", reason);
        }
    }
}
