using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;
using LiteSim;
using LiteSim.Tests;

namespace LiteNet.Tests
{
    /// <summary>
    /// **多图批：房间地图选择**（《地图数据专项设计》多图批）——建房路径的行键来源、校验与权威回执：
    ///
    /// - 行键来源优先级：**票据盖章值 ＞ 房间模板 `map_id` ＞ 宿主默认图**；
    /// - 模板与票据各持非 0 且不等 = 策略冲突 → **拒绝进房**（不静默取其一）；
    /// - 请求行键缺当地图（目录抛）→ 拒绝进房（fail-closed，不回落默认图）；
    /// - 缺多图目录（单图宿主）+ 请求行键 &gt;0 → 拒绝（配置缺失显式化）；
    /// - 既有房间以**房事实**为准：票据请求非 0 且不一致 → 拒绝进房；
    /// - **JoinAck.MapId = 房事实**（客户端据此选表行与场景——权威回执面）。
    ///
    /// 形态：<see cref="FakeRoomTransport"/> 逐包级（同 <see cref="OwnerStartHostTests"/> 形态，无真实 socket）。
    /// </summary>
    public sealed class RoomMapSelectionTests
    {
        private const string ConfigNoTemplateMap = @"{
            ""port"": 0, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private const string ConfigPinnedMapTwo = @"{
            ""port"": 0, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2, ""map_id"": 2 } }
        }";

        /// <summary>行键 → 判定图的地图目录假件（行键 2 = 训练图快照；未知行键抛——语义同生产目录）。</summary>
        private sealed class FakeCatalog
        {
            public readonly List<int> Requested = new List<int>();
            public int FailForMapId = -1;

            public SimMapData Resolve(int mapId)
            {
                Requested.Add(mapId);
                if (mapId == FailForMapId)
                    throw new InvalidOperationException($"tbmap 缺 map_id={mapId} 行——地图数据未生成/行键漂移");
                return mapId == 2 ? TestMaps.TrainingGround() : TestMaps.Greybox();
            }
        }

        private static (ServerHost host, FakeRoomTransport t, TestTicketIssuer issuer) NewHost(
            string config, SimMapData map = null, FakeCatalog catalog = null, bool withTicket = true)
        {
            var t = new FakeRoomTransport();
            TestTicketIssuer issuer = withTicket ? TestTicketIssuer.Random("k1") : null;
            var host = new ServerHost(t, null,
                issuer == null ? null : new HmacJoinTicketValidator(new[] { issuer.AsValidatorKey() }),
                "", RoomServerConfig.Parse(config),
                map: map ?? TestMaps.Greybox(),
                mapCatalog: catalog == null ? (Func<int, SimMapData>)null : catalog.Resolve);
            return (host, t, issuer);
        }

        private static byte[] Join(string roomId, TestTicketIssuer issuer, int mapId)
        {
            // 票据盖章：roomId 与 Join 请求同值（验签绑定项之一）；mapId = 房间创建意图里的地图行键
            string ticket = issuer.Issue("p1", roomId, ServerHost.ServerBuildHash, nowMs: 1_000,
                audience: "", mapId: mapId);
            return PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = roomId, Token = ticket, BuildHash = ServerHost.ServerBuildHash,
            });
        }

        [Fact]
        public void 票据盖章行键_新建房按它建房_JoinAck回执房事实()
        {
            var catalog = new FakeCatalog();
            var h = NewHost(ConfigNoTemplateMap, catalog: catalog);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-T2", h.issuer, mapId: 2));

            JoinAck ack = h.t.LastJoinAck(1);
            Assert.NotNull(ack);
            Assert.Equal(2, ack.MapId);                       // 权威回执 = 房事实
            Assert.Contains(2, catalog.Requested);            // 行键经目录解析（缺行 fail-closed）
            Assert.True(host.TryGetRoom("Room-T2", out RoomRuntime room));
            Assert.Equal(2, room.MapId);
            // 判定图 = 该行投影（快照夹具同值——实例各自持有，按摘要对账）
            Assert.Equal(SimMapDigest.Compute(TestMaps.TrainingGround()), SimMapDigest.Compute(room.Map));
        }

        [Fact]
        public void 模板固定行键_未请求时按模板建房()
        {
            var catalog = new FakeCatalog();
            var h = NewHost(ConfigPinnedMapTwo, catalog: catalog);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-P", h.issuer, mapId: 0));   // 票据未指定（0 = 未指定）

            Assert.Equal(2, h.t.LastJoinAck(1).MapId);             // 模板固定 2 生效
            Assert.DoesNotContain(0, catalog.Requested);           // 0 = 宿主默认图，不经目录装载
            Assert.True(host.TryGetRoom("Room-P", out RoomRuntime room));
            Assert.Equal(2, room.MapId);
        }

        [Fact]
        public void 模板与票据行键冲突_拒绝进房_不建房()
        {
            var catalog = new FakeCatalog();
            var h = NewHost(ConfigPinnedMapTwo, catalog: catalog);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-C", h.issuer, mapId: 1));   // 模板 2 vs 票据 1

            Assert.Null(h.t.LastJoinAck(1));                        // 不静默取其一
            Assert.False(host.TryGetRoom("Room-C", out _));
        }

        [Fact]
        public void 请求行键缺当地图_拒绝进房_不回落默认图()
        {
            var catalog = new FakeCatalog { FailForMapId = 7 };
            var h = NewHost(ConfigNoTemplateMap, catalog: catalog);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-X", h.issuer, mapId: 7));

            Assert.Null(h.t.LastJoinAck(1));
            Assert.False(host.TryGetRoom("Room-X", out _));
        }

        [Fact]
        public void 单图宿主_缺目录_请求行键即拒()
        {
            var h = NewHost(ConfigNoTemplateMap, catalog: null);       // 未装配多图目录
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-N", h.issuer, mapId: 2));

            Assert.Null(h.t.LastJoinAck(1));
            Assert.False(host.TryGetRoom("Room-N", out _));
        }

        [Fact]
        public void 既有房间_票据行键不一致_拒绝进房()
        {
            var catalog = new FakeCatalog();
            var h = NewHost(ConfigNoTemplateMap, catalog: catalog);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-E", h.issuer, mapId: 2));       // 首入座建房（图 2）
            Assert.Equal(2, h.t.LastJoinAck(1).MapId);

            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-E", h.issuer, mapId: 1));       // 第二人带不一致的行键

            Assert.Null(h.t.LastJoinAck(2));                            // 以房事实为准 → 拒绝（不静默换图）
            Assert.False(host.TryGetRoom("Room-E", out RoomRuntime room) && room.MapId == 1);
            Assert.Equal(2, room.MapId);
        }

        [Fact]
        public void 未标注形态_JoinAck行键为0_且无目录也能进房()
        {
            var h = NewHost(ConfigNoTemplateMap, catalog: null);         // 单图宿主 + 票据未指定行键
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-D", h.issuer, mapId: 0));

            Assert.Equal(0, h.t.LastJoinAck(1).MapId);                   // 0 = 服务器未标注（客户端按首图回落）
            Assert.True(host.TryGetRoom("Room-D", out RoomRuntime room));
            Assert.Equal(SimMapDigest.Compute(TestMaps.Greybox()), SimMapDigest.Compute(room.Map));   // 宿主默认图
        }

        // ---- 配置面（模板 map_id）----

        [Fact]
        public void 模板map_id_解析_越界拒载()
        {
            RoomConfig cfg = RoomServerConfig.Parse(ConfigPinnedMapTwo).BuildRoomConfig(null, "R");
            Assert.Equal(2, cfg.MapId);                                  // 模板固定行键落进房间配置

            Assert.Throws<System.IO.InvalidDataException>(() => RoomServerConfig.Parse(
                @"{""port"":0,""rooms"":{""t"":{""expected_players"":1,""map_id"":-1}},""default_template"":""t""}"));
        }

        [Fact]
        public void 建房_冲突与负值_显式抛()
        {
            RoomServerConfig config = RoomServerConfig.Parse(ConfigPinnedMapTwo);
            Assert.Throws<InvalidOperationException>(() => config.BuildRoomConfig(null, "R", requestedMapId: 1));
            Assert.Equal(2, config.BuildRoomConfig(null, "R", requestedMapId: 2).MapId);   // 同值不冲突
            Assert.Throws<ArgumentOutOfRangeException>(() => config.BuildRoomConfig(null, "R", requestedMapId: -1));
        }
    }
}
