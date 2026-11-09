using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using MetaWire;

namespace LiteGame.Tests.EditMode
{
    /// <summary>纯 Meta 边界：登录态、取票/建房取票参数与错误、取消令牌透传；流程移交归 RoomFlowTicketTests。</summary>
    public sealed class MetaEntryEditModeTests : UnityTestBase
    {
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
            public int SeenMapId = -1;
            public int SeenExpectedPlayers = -1;
            public string SeenTemplate;
            public string SeenGmKey;
            public CancellationToken SeenTicketToken;
            public CancellationToken SeenCreateToken;
            public int TicketCalls;
            public int CreateRoomCalls;

            public UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync(string deviceId, CancellationToken ct = default)
            {
                SeenDeviceId = deviceId;
                return UniTask.FromResult(GuestLogin);
            }

            public UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string accessToken, string roomId,
                string buildHash, CancellationToken ct = default)
            {
                TicketCalls++;
                SeenRoomId = roomId;
                SeenBuildHash = buildHash;
                SeenTicketToken = ct;
                return UniTask.FromResult(JoinTicket);
            }

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(string accessToken, string buildHash, int mapId = 0,
                int expectedPlayers = 0, string template = null, string gmKey = null,
                CancellationToken ct = default)
            {
                CreateRoomCalls++;
                SeenBuildHash = buildHash;
                SeenMapId = mapId;
                SeenExpectedPlayers = expectedPlayers;
                SeenTemplate = template;
                SeenGmKey = gmKey;
                SeenCreateToken = ct;
                return UniTask.FromResult(CreateRoomResult);
            }

            public UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string accessToken,
                CancellationToken ct = default)
                => UniTask.FromResult(Rooms);

            public UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string accessToken, int limit,
                CancellationToken ct = default)
                => UniTask.FromResult(Records);
        }

        private static IdentityContext Identity() => new IdentityContext(
            new BuildIdentity(LiteNet.BuildHash.Value, "0.1.0", "WindowsPlayer", string.Empty, 5),
            new SchemaIdentity(0, 0, 0, ActivationRecord.CurrentSchemaVersion));

        private static MetaEntryService Service(FakeMetaClient client, bool configured = true)
            => new MetaEntryService(configured ? client : null, () => "device-1", "http://meta.test", Identity());

        // ---- 联机测试房批：模板/口令随部署注入 ----

        [Test]
        public void 配置测试房_建房带模板与口令_未配置则请求体不变()
        {
            var client = new FakeMetaClient();
            MetaEntryService service = Service(client);
            service.ConfigureTestRoom("sandbox", "k-1");

            service.GuestLoginAsync().GetAwaiter().GetResult();
            service.CreateRoomTicketAsync(mapId: 1, expectedPlayers: 2).GetAwaiter().GetResult();

            Assert.AreEqual("sandbox", client.SeenTemplate, "配置了模板 ⇒ 随建房请求带上");
            Assert.AreEqual("k-1", client.SeenGmKey, "口令同批带上（服务端比对，不匹配 403）");

            var plain = new FakeMetaClient();
            MetaEntryService plainService = Service(plain);
            plainService.GuestLoginAsync().GetAwaiter().GetResult();
            plainService.CreateRoomTicketAsync(mapId: 1, expectedPlayers: 2).GetAwaiter().GetResult();

            Assert.IsNull(plain.SeenTemplate, "未配置 ⇒ 不带 template（普通建房请求体与之前逐字节一致）");
            Assert.IsNull(plain.SeenGmKey);
        }

        [Test]
        public void 显式模板建房_不依赖部署注入_携带地图与口令()
        {
            var client = new FakeMetaClient();
            MetaEntryService service = Service(client);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomTicketAsync(
                mapId: 2, expectedPlayers: 0,
                template: MetaEntryService.TestRoomTemplate, gmKey: "k-2")
                .GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual("sandbox", client.SeenTemplate, "显式入口固定 sandbox");
            Assert.AreEqual("k-2", client.SeenGmKey, "口令随本次请求携带");
            Assert.AreEqual(2, client.SeenMapId, "测试房选图随建房请求盖章");
        }

        // ---- fail-closed 与登录门禁 ----

        [Test]
        public void 未配置_登录取票查询全显性失败_零客户端调用()
        {
            var client = new FakeMetaClient();
            var service = Service(client, configured: false);

            MetaCall<GuestLoginInfo> login = service.GuestLoginAsync().GetAwaiter().GetResult();
            Assert.IsFalse(login.Ok);
            Assert.AreEqual("entry.not-configured", login.Failure.Code);

            MetaCall<JoinTicketInfo> enter = service.RequestJoinTicketAsync("").GetAwaiter().GetResult();
            Assert.IsFalse(enter.Ok);
            Assert.AreEqual("entry.not-configured", enter.Failure.Code);

            MetaCall<JoinTicketInfo> create = service.CreateRoomTicketAsync().GetAwaiter().GetResult();
            Assert.IsFalse(create.Ok);
            Assert.AreEqual("entry.not-configured", create.Failure.Code);

            MetaCall<IReadOnlyList<MatchRecordInfo>> records = service.ListRecordsAsync(20).GetAwaiter().GetResult();
            Assert.IsFalse(records.Ok);

            Assert.AreEqual(0, client.TicketCalls, "fail-closed：未配置不得发任何票据请求");
            Assert.AreEqual(0, client.CreateRoomCalls, "fail-closed：未配置不得发任何建房请求");
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
        public void 未登录_取票与建房取票被拒_零票据调用()
        {
            var client = new FakeMetaClient();
            var service = Service(client);

            MetaCall<JoinTicketInfo> enter = service.RequestJoinTicketAsync("Room-A").GetAwaiter().GetResult();
            Assert.IsFalse(enter.Ok);
            Assert.AreEqual("entry.not-logged-in", enter.Failure.Code);

            MetaCall<JoinTicketInfo> create = service.CreateRoomTicketAsync().GetAwaiter().GetResult();
            Assert.IsFalse(create.Ok);
            Assert.AreEqual("entry.not-logged-in", create.Failure.Code);

            Assert.AreEqual(0, client.TicketCalls, "门禁在客户端调用之前——不浪费一次签发");
            Assert.AreEqual(0, client.CreateRoomCalls, "门禁在客户端调用之前——不浪费一次建房");
        }

        // ---- 纯票据事实（流程移交归 RoomFlowTicketTests）----

        [Test]
        public void 换票成功_返回票据事实_版本与房号原样()
        {
            var client = new FakeMetaClient();
            var service = Service(client);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.RequestJoinTicketAsync("Room-A").GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(LiteNet.BuildHash.Value, client.SeenBuildHash, "版本准入随票——两端同源常量");
            Assert.AreEqual("Room-A", client.SeenRoomId);
            Assert.AreEqual("Room-A", call.Value.RoomId, "票据房号原样透传");
            Assert.AreEqual("tkt-1", call.Value.Ticket, "票据串只返回事实");
            Assert.AreEqual(1, client.TicketCalls);
        }

        [Test]
        public void 创建房间_服务端出号_透传版本与默认参数()
        {
            var client = new FakeMetaClient();
            var service = Service(client);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomTicketAsync().GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(1, client.CreateRoomCalls, "创建房间走客户端 create-room（客户端不发明房号）");
            Assert.AreEqual(LiteNet.BuildHash.Value, client.SeenBuildHash, "版本准入随创建——两端同源常量");
            Assert.AreEqual(0, client.SeenMapId, "未指定地图行键 = 0（宿主默认图）");
            Assert.AreEqual(0, client.SeenExpectedPlayers, "未指定期望人数 = 0（按房间模板）");
            Assert.AreEqual("R-NEW001", call.Value.RoomId, "房号 = 服务端出号（响应透传）");
        }

        [Test]
        public void 创建房间_带地图行键_原样透传给客户端()
        {
            var client = new FakeMetaClient();
            var service = Service(client);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomTicketAsync(mapId: 2).GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(2, client.SeenMapId, "请求地图行键原样透传");
            Assert.AreEqual(1, client.CreateRoomCalls);
        }

        [Test]
        public void 创建房间_带期望人数_原样透传给客户端()
        {
            var client = new FakeMetaClient();
            var service = Service(client);
            service.GuestLoginAsync().GetAwaiter().GetResult();

            MetaCall<JoinTicketInfo> call = service.CreateRoomTicketAsync(mapId: 1, expectedPlayers: 4)
                .GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(1, client.SeenMapId, "请求地图行键原样透传");
            Assert.AreEqual(4, client.SeenExpectedPlayers, "请求期望人数原样透传");
            Assert.AreEqual(1, client.CreateRoomCalls);
        }

        [Test]
        public void 未登录_创建房间被拒_零create调用()
        {
            var client = new FakeMetaClient();
            var service = Service(client);

            MetaCall<JoinTicketInfo> call = service.CreateRoomTicketAsync().GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.AreEqual("entry.not-logged-in", call.Failure.Code);
            Assert.AreEqual(0, client.CreateRoomCalls, "门禁在客户端调用之前");
        }

        // ---- 取消：预取消零 HTTP、caller token 原样到达 IMetaClient ----

        [Test]
        public void 预取消_取票与建房取票均零HTTP()
        {
            var client = new FakeMetaClient();
            var service = Service(client);
            service.GuestLoginAsync().GetAwaiter().GetResult();          // 合法登录前置：先过登录门禁再验证取消
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                service.RequestJoinTicketAsync("Room-A", cts.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() =>
                service.CreateRoomTicketAsync(1, 2, cts.Token).GetAwaiter().GetResult());

            Assert.AreEqual(0, client.TicketCalls, "预取消不得发取票 HTTP");
            Assert.AreEqual(0, client.CreateRoomCalls, "预取消不得发建房 HTTP");
        }

        [Test]
        public void 取票与建房取票_传递caller的token给IMetaClient()
        {
            var client = new FakeMetaClient();
            var service = Service(client);
            service.GuestLoginAsync().GetAwaiter().GetResult();
            using var cts = new CancellationTokenSource();

            service.RequestJoinTicketAsync("Room-A", cts.Token).GetAwaiter().GetResult();
            service.CreateRoomTicketAsync(1, 2, cts.Token).GetAwaiter().GetResult();

            Assert.AreEqual(cts.Token, client.SeenTicketToken, "取票 token 必须是 caller 的原 token");
            Assert.AreEqual(cts.Token, client.SeenCreateToken, "建房 token 必须是 caller 的原 token");
            Assert.IsTrue(client.SeenTicketToken.CanBeCanceled, "可取消 token 不得被替换成 None");
        }

        // ---- 传输工厂：envelope.key 注入优先 ----

        [Test]
        public void 传输工厂_envelope注入优先_非法键fail_fast抛()
        {
            var good = new MetaEntryConfig { EnvelopeKeyBase64 = Convert.ToBase64String(new byte[32]) };
            Assert.DoesNotThrow(() => ProductTransportFactory.CreateTransport(false, null, good),
                "合法 32 字节 Base64 键可构造信封传输");

            var bad = new MetaEntryConfig { EnvelopeKeyBase64 = "not-base64!!" };
            Assert.Throws<FormatException>(() => ProductTransportFactory.CreateTransport(false, null, bad),
                "非法键当场抛（Convert.FromBase64String）——密钥配错不得静默回落环境变量");

            Assert.Throws<InvalidOperationException>(() => ProductTransportFactory.CreateTransport(true, null),
                "本地测试模式缺每局规则持有件 → 显式失败（播种步骤漏调不再静默）");
        }
    }
}
