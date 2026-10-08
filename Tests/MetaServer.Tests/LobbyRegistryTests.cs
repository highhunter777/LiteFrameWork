using System;
using System.Collections.Generic;
using LiteTesting;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Lobby;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// Lobby 实例注册表（《Meta 服务专项设计》§7 实例注册/容量驱动分配/drain 协同；§13 有界要求）。
    ///
    /// L1：纯逻辑 + 虚拟时钟——心跳超时不用 sleep，时钟推进即刻可断言。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Unit)]
    public sealed class LobbyRegistryTests
    {
        private sealed class VirtualClock
        {
            public long Ms;

            public long Now()
            {
                return Ms;
            }
        }

        private static InstanceRegisterCommand Command(string instanceId, string buildHash = "hash-a",
            string address = "10.0.0.1:7777", int maxRooms = 8, int roomCount = 0,
            int maxPlayers = 64, int playerCount = 0, bool draining = false)
        {
            return new InstanceRegisterCommand
            {
                InstanceId = instanceId,
                BuildHash = buildHash,
                Address = address,
                MaxRooms = maxRooms,
                RoomCount = roomCount,
                MaxPlayers = maxPlayers,
                PlayerCount = playerCount,
                Draining = draining,
            };
        }

        private static InstanceRegistry NewRegistry(VirtualClock clock, int capacity = 8, long ttlMs = 30_000)
        {
            return new InstanceRegistry(capacity, ttlMs, clock.Now);
        }

        [Fact]
        public void 注册_心跳幂等_同实例更新字段不新增条目()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock);

            Assert.Equal(InstanceRegistry.RegisterResult.Accepted, registry.Register(Command("r1"), out _));
            clock.Ms = 5_000;
            Assert.Equal(InstanceRegistry.RegisterResult.Accepted,
                registry.Register(Command("r1", buildHash: "hash-b", playerCount: 3), out _));

            Assert.Equal(1, registry.Count);
            Assert.True(registry.TryAllocate(null, out InstanceRegistry.Entry entry, out _));
            Assert.Equal("hash-b", entry.BuildHash);          // 更新生效（换对象语义）
            Assert.Equal(3, entry.PlayerCount);
            Assert.Equal(5_000, entry.LastSeenMs);            // 心跳刷新时刻
        }

        [Fact]
        public void 心跳超时_移出可分配集合并计数()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock, ttlMs: 30_000);
            registry.Register(Command("r1"), out _);

            clock.Ms = 30_000;                                 // 边界内（严格大于才算超时）
            Assert.True(registry.TryAllocate(null, out _, out _));
            Assert.Equal(0, registry.SweptExpired);

            clock.Ms = 30_001;
            Assert.False(registry.TryAllocate(null, out _, out _));
            Assert.Equal(1, registry.SweptExpired);
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void 心跳过后重新注册_恢复可分配()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock, ttlMs: 1_000);
            registry.Register(Command("r1"), out _);

            clock.Ms = 2_000;                                  // 超时被清
            Assert.False(registry.TryAllocate(null, out _, out _));

            registry.Register(Command("r1"), out _);           // 实例心跳恢复 → 重新登记
            Assert.True(registry.TryAllocate(null, out _, out _));
        }

        [Fact]
        public void 表满_拒新实例_既有实例心跳不受影响()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock, capacity: 1);
            Assert.Equal(InstanceRegistry.RegisterResult.Accepted, registry.Register(Command("a"), out _));

            Assert.Equal(InstanceRegistry.RegisterResult.RegistryFull, registry.Register(Command("b"), out string reason));
            Assert.Equal("registry-full", reason);

            // 既有实例的周期心跳不因满拒而失败（淘汰旧条目会静默丢掉存活实例的资格）
            Assert.Equal(InstanceRegistry.RegisterResult.Accepted, registry.Register(Command("a"), out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void 字段越界_实例标识为空拒(string instanceId)
        {
            var registry = NewRegistry(new VirtualClock());
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields,
                registry.Register(Command(instanceId), out string reason));
            Assert.Equal("instanceId", reason);
        }

        [Fact]
        public void 字段越界_超长标识与坏地址与计数越界拒()
        {
            var registry = NewRegistry(new VirtualClock());

            Assert.Equal("instanceId",
                Reject(registry, Command(new string('x', 65))));
            Assert.Equal("buildHash",
                Reject(registry, Command("r1", buildHash: "")));
            Assert.Equal("address",
                Reject(registry, Command("r1", address: "no-port")));
            Assert.Equal("address",
                Reject(registry, Command("r1", address: "host:0")));
            Assert.Equal("address",
                Reject(registry, Command("r1", address: "host:70000")));
            Assert.Equal("maxRooms",
                Reject(registry, Command("r1", maxRooms: 0)));
            Assert.Equal("roomCount",
                Reject(registry, Command("r1", maxRooms: 2, roomCount: 3)));
            Assert.Equal("playerCount",
                Reject(registry, Command("r1", maxPlayers: 4, playerCount: 5)));
        }

        [Fact]
        public void 字段越界_null载荷拒()
        {
            var registry = NewRegistry(new VirtualClock());
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, registry.Register(null, out string reason));
            Assert.Equal("payload-missing", reason);
        }

        private static string Reject(InstanceRegistry registry, InstanceRegisterCommand command)
        {
            InstanceRegistry.RegisterResult result = registry.Register(command, out string reason);
            Assert.Equal(InstanceRegistry.RegisterResult.InvalidFields, result);
            return reason;
        }

        [Fact]
        public void 分配_容量驱动_占用少者优先()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock);
            registry.Register(Command("busy", playerCount: 5), out _);
            registry.Register(Command("idle", playerCount: 1), out _);

            Assert.True(registry.TryAllocate(null, out InstanceRegistry.Entry entry, out _));
            Assert.Equal("idle", entry.InstanceId);
        }

        [Fact]
        public void 分配_占用相同_比房间占用_再比Ordinal()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock);
            registry.Register(Command("b", roomCount: 2), out _);
            registry.Register(Command("a", roomCount: 1), out _);

            Assert.True(registry.TryAllocate(null, out InstanceRegistry.Entry entry, out _));
            Assert.Equal("a", entry.InstanceId);               // 房间占用少者优先

            registry.Register(Command("a", roomCount: 1, playerCount: 1), out _);
            registry.Register(Command("b", roomCount: 1, playerCount: 1), out _);
            registry.Register(Command("c", roomCount: 1, playerCount: 1), out _);
            Assert.True(registry.TryAllocate(null, out entry, out _));
            Assert.Equal("a", entry.InstanceId);               // 全同 → Ordinal 序（确定性）
        }

        [Fact]
        public void 分配_跳过排空与满员实例()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock);
            registry.Register(Command("draining", draining: true), out _);
            registry.Register(Command("full", maxPlayers: 2, playerCount: 2), out _);

            Assert.False(registry.TryAllocate(null, out _, out bool versionConflict));   // 无可用实例
            Assert.False(versionConflict);

            registry.Register(Command("ok", playerCount: 1), out _);
            Assert.True(registry.TryAllocate(null, out InstanceRegistry.Entry entry, out _));
            Assert.Equal("ok", entry.InstanceId);
        }

        [Fact]
        public void 分配_版本过滤_并区分版本冲突与无容量()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock);
            registry.Register(Command("old", buildHash: "hash-old"), out _);

            // 声明新版本：仅存在其它版本的可用实例 → 版本冲突（与"无容量"处置不同）
            Assert.False(registry.TryAllocate("hash-new", out _, out bool conflict));
            Assert.True(conflict);

            // 无实例可分配（全部排空）→ 无容量，不是版本冲突
            registry.Register(Command("old", buildHash: "hash-old", draining: true), out _);
            Assert.False(registry.TryAllocate("hash-new", out _, out conflict));
            Assert.False(conflict);

            // 版本匹配走正常分配
            registry.Register(Command("new", buildHash: "hash-new", draining: false), out _);
            Assert.True(registry.TryAllocate("hash-new", out InstanceRegistry.Entry entry, out _));
            Assert.Equal("new", entry.InstanceId);
        }

        [Fact]
        public void 投影_按实例序输出且新鲜度随注册表时钟()
        {
            var clock = new VirtualClock();
            var registry = NewRegistry(clock);
            registry.Register(Command("b", buildHash: "h2"), out _);
            registry.Register(Command("a", buildHash: "h1"), out _);

            clock.Ms = 4_000;
            List<LobbyRoomView> rooms = registry.ProjectRooms();

            Assert.Equal(2, rooms.Count);
            Assert.Equal("a", rooms[0].InstanceId);            // Ordinal 序
            Assert.Equal("b", rooms[1].InstanceId);
            Assert.Equal(4_000, rooms[0].LastSeenAgeMs);       // 年龄用注册表时钟（时钟单源）
            Assert.Equal("h1", rooms[0].BuildHash);
        }
    }
}
