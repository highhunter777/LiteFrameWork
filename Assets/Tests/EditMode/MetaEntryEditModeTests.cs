using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using XLua;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 批C-③ 联机入口编排链（《上云测试专项设计》§2 客户端闭环）：
    /// ① fail-closed——未配置 Meta 基址时登录/进房/查询显性失败（不连任何地址、不发 Request）；
    /// ② 登录态缓存与门禁（未登录不得换票）；
    /// ③ 换票成功 = 票据地址解析 + Request(Match, MetaJoin) 精确移交（走现有 Join 管道）；
    /// ④ 票据地址不可解析 = 确定性失败、零 Request；
    /// ⑤ Bridge.meta 门面回调契约（恰好一次、ok 首参、失败 reason 不含原文、行形状手工展开）；
    /// ⑥ 传输工厂 -envelope.key 注入优先（非法键 fail-fast 抛——密钥配错当场暴露，不静默回落）。
    /// </summary>
    public sealed class MetaEntryEditModeTests : UnityTestBase
    {
        // ---- 替身 ----

        /// <summary>IMetaClient 替身：脚本化结果 + 记录请求参数（deviceId/roomId/buildHash 对数）。</summary>
        private sealed class FakeMetaClient : IMetaClient
        {
            public MetaCall<GuestLoginInfo> GuestLogin = MetaCall<GuestLoginInfo>.OkResult(
                new GuestLoginInfo { AccountId = "acc-1", AccessToken = "tok-1", ExpiresAtMs = 1 });
            public MetaCall<JoinTicketInfo> JoinTicket = MetaCall<JoinTicketInfo>.OkResult(
                new JoinTicketInfo { InstanceId = "ins-1", Address = "203.0.113.7:17777", RoomId = "Room-A", Ticket = "tkt-1", ExpiresAtMs = 2 });
            public MetaCall<JoinTicketInfo> CreateRoomResult = MetaCall<JoinTicketInfo>.OkResult(
                new JoinTicketInfo { InstanceId = "ins-2", Address = "203.0.113.7:17778", RoomId = "R-NEW001", Ticket = "tkt-2", ExpiresAtMs = 3 });
            public MetaCall<IReadOnlyList<LobbyInstanceView>> Rooms;
            public MetaCall<IReadOnlyList<MatchRecordInfo>> Records;

            public string SeenDeviceId;
            public string SeenRoomId;
            public string SeenBuildHash;
            public int SeenMapId = -1;                                   // 多图批：create-room 的请求地图行键（-1 = 未调用）
            public int SeenExpectedPlayers = -1;                         // 开房参数批：create-room 的请求人数（-1 = 未调用）
            public int TicketCalls;
            public int CreateRoomCalls;

            public UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync(string deviceId, System.Threading.CancellationToken ct = default)
            {
                SeenDeviceId = deviceId;
                return UniTask.FromResult(GuestLogin);
            }

            public UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string accessToken, string roomId,
                string buildHash, System.Threading.CancellationToken ct = default)
            {
                TicketCalls++;
                SeenRoomId = roomId;
                SeenBuildHash = buildHash;
                return UniTask.FromResult(JoinTicket);
            }

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(string accessToken, string buildHash, int mapId = 0,
                int expectedPlayers = 0, System.Threading.CancellationToken ct = default)
            {
                CreateRoomCalls++;
                SeenBuildHash = buildHash;
                SeenMapId = mapId;
                SeenExpectedPlayers = expectedPlayers;
                return UniTask.FromResult(CreateRoomResult);
            }

            public UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string accessToken,
                System.Threading.CancellationToken ct = default)
                => UniTask.FromResult(Rooms);

            public UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string accessToken, int limit,
                System.Threading.CancellationToken ct = default)
                => UniTask.FromResult(Records);
        }

        /// <summary>IStageHost 替身：捕获 Request（进房移交的断言面）。</summary>
        private sealed class FakeStageHost : IStageHost<ProcedureId, ProcedureArgs>
        {
            public (ProcedureId id, ProcedureArgs args)? LastRequest;
            public bool Started => true;
            public ProcedureId Current => ProcedureId.Main;
            public float StageTime => 0f;
            public int StageFrames => 0;

            public bool Request(ProcedureId nextId, in ProcedureArgs req)
            {
                LastRequest = (nextId, req);
                return true;
            }

            public bool Request(ProcedureId nextId) => Request(nextId, default);
        }

        private static MetaEntryService Service(FakeMetaClient client, FakeStageHost machine = null,
            bool configured = true)
        {
            var service = new MetaEntryService(configured ? client : null, () => "device-1", "http://meta.test");
            if (machine != null) service.BindMachine(machine);
            return service;
        }

        // ---- fail-closed 与登录门禁 ----

        [Test]
        public void 未配置_登录进房查询全显性失败_零Request()
        {
            var machine = new FakeStageHost();
            var service = Service(null, machine, configured: false);

            MetaCall<GuestLoginInfo> login = service.GuestLoginAsync().GetAwaiter().GetResult();
            Assert.IsFalse(login.Ok);
            Assert.AreEqual("entry.not-configured", login.Failure.Code);

            MetaCall<JoinTicketInfo> enter = service.EnterRoomAsync("").GetAwaiter().GetResult();
            Assert.IsFalse(enter.Ok);
            Assert.AreEqual("entry.not-configured", enter.Failure.Code);

            MetaCall<IReadOnlyList<MatchRecordInfo>> records = service.ListRecordsAsync(20).GetAwaiter().GetResult();
            Assert.IsFalse(records.Ok);

            Assert.IsNull(machine.LastRequest, "fail-closed：未配置不得发任何流程请求");
        }

        [Test]
        public void 登录成功_缓存账号_设备标识直达客户端()
        {
            var client = new FakeMetaClient();
            var service = Service(client);

            MetaCall<GuestLoginInfo> call = service.GuestLoginAsync().GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual("acc-1", service.AccountId, "登录态缓存——页面据此显示");
            Assert.AreEqual("device-1", client.SeenDeviceId, "设备标识经注入直达客户端（get-or-create 幂等键）");
        }

        [Test]
        public void 未登录_换票与查询被拒_零票据调用()
        {
            var client = new FakeMetaClient();
            var service = Service(client, new FakeStageHost());

            MetaCall<JoinTicketInfo> enter = service.EnterRoomAsync("Room-A").GetAwaiter().GetResult();
            Assert.IsFalse(enter.Ok);
            Assert.AreEqual("entry.not-logged-in", enter.Failure.Code);
            Assert.AreEqual(0, client.TicketCalls, "门禁在客户端调用之前——不浪费一次签发");
        }

        // ---- 换票 → 现有 Join 管道移交 ----

        [Test]
        public void 换票成功_地址解析并精确移交Match()
        {
            var client = new FakeMetaClient();
            var machine = new FakeStageHost();
            var service = Service(client, machine);
            service.GuestLoginAsync().GetAwaiter().GetResult();          // 先登录（缓存令牌）

            MetaCall<JoinTicketInfo> call = service.EnterRoomAsync("Room-A").GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(LiteNet.BuildHash.Value, client.SeenBuildHash, "版本准入随票——两端同源常量");
            Assert.AreEqual("Room-A", client.SeenRoomId);

            Assert.IsNotNull(machine.LastRequest, "进房 = Request(Match)——走现有 Join 管道");
            Assert.AreEqual(ProcedureId.Match, machine.LastRequest.Value.id);
            MetaJoin join = machine.LastRequest.Value.args.MetaJoin;
            Assert.AreEqual("203.0.113.7", join.Host);
            Assert.AreEqual(17777, join.Port);
            Assert.AreEqual("Room-A", join.RoomId);
            Assert.AreEqual("tkt-1", join.Ticket);
        }

        [Test]
        public void 票据地址不可解析_确定性失败_零Request()
        {
            var client = new FakeMetaClient
            {
                JoinTicket = MetaCall<JoinTicketInfo>.OkResult(new JoinTicketInfo
                    { InstanceId = "ins-1", Address = "no-port", RoomId = "Room-A", Ticket = "tkt-1", ExpiresAtMs = 2 }),
            };
            var machine = new FakeStageHost();
            var service = Service(client, machine);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.EnterRoomAsync("Room-A").GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.AreEqual(MetaFailureKind.InvalidResponse, call.Failure.Kind, "部署侧配错 = 确定性失败（重试无效）");
            Assert.IsNull(machine.LastRequest, "不可解析不得发 Request——避免连到错误端点");
        }

        [Test]
        public void 流程机未绑定_换票显性失败()
        {
            var client = new FakeMetaClient();
            var service = Service(client);                               // 不 BindMachine
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.EnterRoomAsync("").GetAwaiter().GetResult();
            Assert.IsFalse(call.Ok);
            Assert.AreEqual(MetaFailureKind.InvalidResponse, call.Failure.Kind);
            StringAssert.Contains("BindMachine", call.Failure.Detail, "失败点名装配面缺口");
        }

        // ---- 创建房间（服务端出号 → 与进房同一管道）----

        [Test]
        public void 创建房间_服务端出号_精确移交Match_与进房同管道()
        {
            var client = new FakeMetaClient();
            var machine = new FakeStageHost();
            var service = Service(client, machine);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomAsync().GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(1, client.CreateRoomCalls, "创建房间走客户端 create-room（客户端不发明房号）");
            Assert.AreEqual(LiteNet.BuildHash.Value, client.SeenBuildHash, "版本准入随创建——两端同源常量");
            Assert.AreEqual(0, client.SeenMapId, "未指定地图行键 = 0（宿主默认图；多图批）");
            Assert.AreEqual(0, client.SeenExpectedPlayers, "未指定期望人数 = 0（按房间模板；开房参数批）");
            Assert.AreEqual("R-NEW001", call.Value.RoomId, "房号 = 服务端出号（响应透传）");

            Assert.IsNotNull(machine.LastRequest, "创建房间成功 = Request(Match)——与换票进房同管道");
            Assert.AreEqual(ProcedureId.Match, machine.LastRequest.Value.id);
            MetaJoin join = machine.LastRequest.Value.args.MetaJoin;
            Assert.AreEqual("203.0.113.7", join.Host);
            Assert.AreEqual(17778, join.Port);
            Assert.AreEqual("R-NEW001", join.RoomId);
            Assert.AreEqual("tkt-2", join.Ticket);
        }

        [Test]
        public void 创建房间_带地图行键_原样透传给客户端()
        {
            // 多图批：create-room 的 mapId 是"房间创建意图"的客户端侧入口——必须原样到达客户端
            // （盖章与校验归 Meta/房间端：票据段 + 表行）
            var client = new FakeMetaClient();
            var machine = new FakeStageHost();
            var service = Service(client, machine);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomAsync(mapId: 2).GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(2, client.SeenMapId, "请求地图行键原样透传");
            Assert.AreEqual(ProcedureId.Match, machine.LastRequest.Value.id, "与既有创建管道同链");
        }

        [Test]
        public void 创建房间_带期望人数_原样透传给客户端()
        {
            // 开房参数批：create-room 的 expectedPlayers 同 mapId 口径——客户端侧入口原样到达客户端
            // （盖章与上界校验归 Meta/房间端：票据段 + 内核席位容量）
            var client = new FakeMetaClient();
            var machine = new FakeStageHost();
            var service = Service(client, machine);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomAsync(mapId: 1, expectedPlayers: 4).GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(1, client.SeenMapId, "请求地图行键原样透传");
            Assert.AreEqual(4, client.SeenExpectedPlayers, "请求期望人数原样透传");
            Assert.AreEqual(ProcedureId.Match, machine.LastRequest.Value.id, "与既有创建管道同链");
        }

        [Test]
        public void 创建房间_地址不可解析_确定性失败_零Request()
        {
            var client = new FakeMetaClient
            {
                CreateRoomResult = MetaCall<JoinTicketInfo>.OkResult(new JoinTicketInfo
                    { InstanceId = "ins-2", Address = "no-port", RoomId = "R-NEW001", Ticket = "tkt-2", ExpiresAtMs = 3 }),
            };
            var machine = new FakeStageHost();
            var service = Service(client, machine);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomAsync().GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.IsNull(machine.LastRequest, "地址不可解析不得发 Request——避免连到错误端点");
        }

        [Test]
        public void 未登录_创建房间被拒_零create调用()
        {
            var client = new FakeMetaClient();
            var service = Service(client, new FakeStageHost());

            MetaCall<JoinTicketInfo> call = service.CreateRoomAsync().GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.AreEqual("entry.not-logged-in", call.Failure.Code);
            Assert.AreEqual(0, client.CreateRoomCalls, "门禁在客户端调用之前");
        }

        [Test]
        public void 门面_CreateRoom回调_成功带新房号()
        {
            using (var env = new LuaEnv())
            {
                ProductBridges.BindGlobals(env);
                var client = new FakeMetaClient();
                var service = Service(client, new FakeStageHost());
                MetaBridge.Bind(service);
                service.GuestLoginAsync().GetAwaiter().GetResult();      // 令牌就位

                env.DoString(@"
                    create_ok, create_room = nil, nil
                    Bridge.meta.CreateRoom(2, 4, function(ok, roomId) create_ok = ok; create_room = roomId end)");

                Assert.IsTrue(env.Global.Get<bool>("create_ok"));
                Assert.AreEqual("R-NEW001", env.Global.Get<string>("create_room"), "成功载荷 = 新房号（房间页展示用）");
                Assert.AreEqual(1, client.CreateRoomCalls);
                Assert.AreEqual(2, client.SeenMapId, "首参 = 请求地图行键（多图批）");
                Assert.AreEqual(4, client.SeenExpectedPlayers, "次参 = 请求期望人数（开房参数批）");
            }
        }

        // ---- Bridge.meta 门面（真 LuaEnv；替身同步完成 → 回调同步可断言） ----

        [Test]
        public void 门面_GuestLogin回调契约_成功带账号_失败带稳定码()
        {
            using (var env = new LuaEnv())
            {
                ProductBridges.BindGlobals(env);
                MetaBridge.Bind(Service(new FakeMetaClient()));
                env.DoString(@"
                    result_ok, result_val = nil, nil
                    Bridge.meta.GuestLogin(function(ok, v) result_ok = ok; result_val = v end)");

                Assert.IsTrue(env.Global.Get<bool>("result_ok"));
                Assert.AreEqual("acc-1", env.Global.Get<string>("result_val"));

                // 未配置失败路径：ok=false + reason（稳定码——不含原文/堆栈）
                MetaBridge.Bind(Service(null, configured: false));
                env.DoString(@"
                    fail_ok, fail_val = nil, nil
                    Bridge.meta.GuestLogin(function(ok, v) fail_ok = ok; fail_val = v end)");
                Assert.IsFalse(env.Global.Get<bool>("fail_ok"));
                Assert.AreEqual("entry.not-configured", env.Global.Get<string>("fail_val"));
            }
        }

        [Test]
        public void 门面_ListRooms行形状手工展开()
        {
            var client = new FakeMetaClient
            {
                Rooms = MetaCall<IReadOnlyList<LobbyInstanceView>>.OkResult(new[]
                {
                    new LobbyInstanceView
                    {
                        InstanceId = "ins-1", BuildHash = "bh", Address = "h:1", PlayerCount = 2, Draining = false,
                        Rooms = new[] { new LobbyRoomEntry { RoomId = "Room-A", Phase = 1, Players = 2, ExpectedPlayers = 8 } },
                    },
                }),
            };
            using (var env = new LuaEnv())
            {
                ProductBridges.BindGlobals(env);
                var service = Service(client);
                MetaBridge.Bind(service);
                service.GuestLoginAsync().GetAwaiter().GetResult();      // 令牌就位
                env.DoString(@"
                    rows_ok, rows_val = nil, nil
                    Bridge.meta.ListRooms(function(ok, rows)
                        rows_ok = ok
                        rows_val = tostring(rows[1].roomId) .. ':' .. tostring(rows[1].players) .. '/' .. tostring(rows[1].expectedPlayers)
                    end)");

                Assert.IsTrue(env.Global.Get<bool>("rows_ok"));
                Assert.AreEqual("Room-A:2/8", env.Global.Get<string>("rows_val"));
            }
        }

        // ---- 传输工厂：envelope.key 注入优先 ----

        [Test]
        public void 传输工厂_envelope注入优先_非法键fail_fast抛()
        {
            var good = new MetaEntryConfig { EnvelopeKeyBase64 = Convert.ToBase64String(new byte[32]) };
            Assert.DoesNotThrow(() => ProcedureMatch.CreateTransportForFactory(false, good),
                "合法 32 字节 Base64 键可构造信封传输");

            var bad = new MetaEntryConfig { EnvelopeKeyBase64 = "not-base64!!" };
            Assert.Throws<FormatException>(() => ProcedureMatch.CreateTransportForFactory(false, bad),
                "非法键当场抛（Convert.FromBase64String）——密钥配错不得静默回落环境变量");
        }
    }
}
