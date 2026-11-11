using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using MetaWire;
using NUnit.Framework;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Meta.Abstractions.Account;
using LiteGame.Modules.Meta.Abstractions.Room;
using LiteGame.Modules.Meta.Client.Account;

namespace LiteGame.Tests.EditMode
{
    /// <summary>流程取票边界：真实 ProcedureFlow + StageMachine 观察 Match 排队与精确入场参数。</summary>
    public sealed class RoomFlowTicketTests
    {
        private sealed class TicketMetaClient : IMetaClient
        {
            public MetaCall<GuestLoginInfo> GuestLogin = MetaCall<GuestLoginInfo>.OkResult(
                new GuestLoginInfo { AccountId = "acc-1", AccessToken = "tok-1", ExpiresAtMs = 1 });
            public MetaCall<JoinTicketInfo> JoinTicket = MetaCall<JoinTicketInfo>.OkResult(
                new JoinTicketInfo { InstanceId = "ins-1", Address = "203.0.113.7:17777", RoomId = "Room-A", Ticket = "tkt-1", ExpiresAtMs = 2 });
            public MetaCall<JoinTicketInfo> CreateRoomResult = MetaCall<JoinTicketInfo>.OkResult(
                new JoinTicketInfo { InstanceId = "ins-2", Address = "203.0.113.7:17778", RoomId = "R-NEW001", Ticket = "tkt-2", ExpiresAtMs = 3 });

            public string SeenBuildHash;
            public string SeenRoomId;
            public int SeenMapId = -1;
            public int SeenExpectedPlayers = -1;
            public string SeenTemplate;
            public string SeenGmKey;
            public int TicketCalls;
            public int CreateRoomCalls;

            public UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync(string deviceId, CancellationToken ct = default)
                => UniTask.FromResult(GuestLogin);

            public UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string accessToken, string roomId,
                string buildHash, CancellationToken ct = default)
            {
                TicketCalls++;
                SeenRoomId = roomId;
                SeenBuildHash = buildHash;
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
                return UniTask.FromResult(CreateRoomResult);
            }

            public UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string accessToken,
                CancellationToken ct = default)
                => UniTask.FromResult(default(MetaCall<IReadOnlyList<LobbyInstanceView>>));

            public UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string accessToken, int limit,
                CancellationToken ct = default)
                => UniTask.FromResult(default(MetaCall<IReadOnlyList<MatchRecordInfo>>));
        }

        private sealed class ProbeStage : IStage<ProcedureId, ProcedureArgs>
        {
            public int Enters;
            public ProcedureArgs LastArgs;

            public void OnInit(IStageHost<ProcedureId, ProcedureArgs> m) { }

            public void OnEnter(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req)
            {
                Enters++;
                LastArgs = req;
            }

            public void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds) { }
            public void OnLeave(IStageHost<ProcedureId, ProcedureArgs> m) { }
        }

        private sealed class TicketFlowRig : IDisposable
        {
            public readonly TicketMetaClient Client = new();
            public readonly ClientScope RootScope = new ClientScope("Root");
            public readonly ServiceContainer RootServices = new ServiceContainer();
            public readonly ProbeStage Main = new();
            public readonly ProbeStage Match = new();
            public readonly AccountDomain Domain;
            public readonly AccountTicketSource Tickets;
            public readonly StageMachine<ProcedureId, ProcedureArgs> Machine;
            public readonly ProcedureFlow Flow;

            public TicketFlowRig(bool configured = true)
            {
                IdentityContext identity = Identity();
                var login = new MetaLoginService(configured ? Client : null, () => "device-1", "http://meta.test",
                    identity);
                RootServices.RegisterInstance(identity);
                RootServices.RegisterInstance<IMetaClient>(Client);
                RootServices.RegisterInstance<IMetaConfigFacts>(login);
                Domain = RoomFlowRig.NewDomain(RootScope, RootServices, null);
                RootServices.RegisterInstance<IAccountDomain>(Domain);
                RootServices.Seal();
                Tickets = new AccountTicketSource(Domain);

                Machine = new StageMachine<ProcedureId, ProcedureArgs>("RoomFlowTicketTest",
                    (ProcedureId.Main, Main),
                    (ProcedureId.Match, Match));
                Machine.Start(ProcedureId.Main);
                Flow = new ProcedureFlow(Machine, Tickets, new RoomFlowLifetime());
            }

            public void Login()
            {
                Domain.Open("acc-1", "tok-1");
                Assert.IsTrue(Domain.IsOpen, "登录后账户域必须开启（前置）");
            }

            public void Dispose()
            {
                Flow.Dispose();
                if (Machine.Started) Machine.Reset();
                RootScope.Dispose();
                RootServices.Dispose();
            }
        }

        private static IdentityContext Identity() => new IdentityContext(
            new BuildIdentity(LiteNet.BuildHash.Value, "0.1.0", "WindowsPlayer", string.Empty, 5),
            new SchemaIdentity(0, 0, 0, ActivationRecord.CurrentSchemaVersion));

        [Test]
        public void 换票成功_地址解析并精确移交Match()
        {
            using var rig = new TicketFlowRig();
            rig.Login();

            MetaCall<JoinTicketInfo> call = rig.Flow.EnterRoomAsync("Room-A").GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(1, rig.Client.TicketCalls);
            Assert.AreEqual(LiteNet.BuildHash.Value, rig.Client.SeenBuildHash, "版本准入随票——两端同源常量");
            Assert.AreEqual("Room-A", rig.Client.SeenRoomId);

            Assert.IsTrue(rig.Machine.HasPending, "进房 = Match 排队");
            Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId);
            rig.Machine.Advance();

            Assert.AreEqual(1, rig.Match.Enters, "Match 实际入场一次");
            MetaJoin join = rig.Match.LastArgs.MetaJoin;
            Assert.AreEqual("203.0.113.7", join.Host);
            Assert.AreEqual(17777, join.Port);
            Assert.AreEqual("Room-A", join.RoomId);
            Assert.AreEqual("tkt-1", join.Ticket);
        }

        [Test]
        public void 创建房间_服务端出号_精确移交Match()
        {
            using var rig = new TicketFlowRig();
            rig.Login();

            MetaCall<JoinTicketInfo> call = rig.Flow.CreateRoomAsync(2, 4, "sandbox", "k-2")
                .GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(1, rig.Client.CreateRoomCalls, "创建房间走客户端 create-room（客户端不发明房号）");
            Assert.AreEqual(LiteNet.BuildHash.Value, rig.Client.SeenBuildHash);
            Assert.AreEqual(2, rig.Client.SeenMapId, "请求地图行键原样透传");
            Assert.AreEqual(4, rig.Client.SeenExpectedPlayers, "请求期望人数原样透传");
            Assert.AreEqual("sandbox", rig.Client.SeenTemplate, "显式入口模板原样透传");
            Assert.AreEqual("k-2", rig.Client.SeenGmKey, "口令随本次请求携带");
            Assert.AreEqual("R-NEW001", call.Value.RoomId, "房号 = 服务端出号（响应透传）");

            Assert.IsTrue(rig.Machine.HasPending, "创建成功 = Match 排队");
            Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId);
            rig.Machine.Advance();

            MetaJoin join = rig.Match.LastArgs.MetaJoin;
            Assert.AreEqual("203.0.113.7", join.Host);
            Assert.AreEqual(17778, join.Port);
            Assert.AreEqual("R-NEW001", join.RoomId);
            Assert.AreEqual("tkt-2", join.Ticket);
        }

        [Test]
        public void 票据地址不可解析_确定性失败_零Match排队()
        {
            using var rig = new TicketFlowRig();
            rig.Client.JoinTicket = MetaCall<JoinTicketInfo>.OkResult(new JoinTicketInfo
                { InstanceId = "ins-1", Address = "no-port", RoomId = "Room-A", Ticket = "tkt-1", ExpiresAtMs = 2 });
            rig.Login();

            MetaCall<JoinTicketInfo> call = rig.Flow.EnterRoomAsync("Room-A").GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.AreEqual(MetaFailureKind.InvalidResponse, call.Failure.Kind, "部署侧配错 = 确定性失败（重试无效）");
            Assert.IsFalse(rig.Machine.HasPending, "不可解析不得排队 Match——避免连到错误端点");
            Assert.AreEqual(0, rig.Match.Enters);
        }

        [Test]
        public void 建房票据地址不可解析_确定性失败_零Match排队()
        {
            using var rig = new TicketFlowRig();
            rig.Client.CreateRoomResult = MetaCall<JoinTicketInfo>.OkResult(new JoinTicketInfo
                { InstanceId = "ins-2", Address = "no-port", RoomId = "R-NEW001", Ticket = "tkt-2", ExpiresAtMs = 3 });
            rig.Login();

            MetaCall<JoinTicketInfo> call = rig.Flow.CreateRoomAsync().GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.AreEqual(MetaFailureKind.InvalidResponse, call.Failure.Kind);
            Assert.IsFalse(rig.Machine.HasPending, "不可解析不得排队 Match——避免连到错误端点");
            Assert.AreEqual(0, rig.Match.Enters);
        }

        [Test]
        public void 未配置_取票失败_零Match排队()
        {
            using var rig = new TicketFlowRig(configured: false);

            MetaCall<JoinTicketInfo> call = rig.Flow.EnterRoomAsync("").GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.AreEqual("entry.not-configured", call.Failure.Code);
            Assert.IsFalse(rig.Machine.HasPending, "未配置不得排队 Match");
        }

        [Test]
        public void 流程构造_缺必要端口_当场fail_fast()
        {
            var root = new ClientScope("Root");
            var services = new ServiceContainer();
            var tickets = new AccountTicketSource(RoomFlowRig.NewDomain(root, services, null));
            var machine = new StageMachine<ProcedureId, ProcedureArgs>("RoomFlowTicketCtor",
                (ProcedureId.Main, new ProbeStage()),
                (ProcedureId.Match, new ProbeStage()));

            Assert.Throws<ArgumentNullException>(() => new ProcedureFlow(null, tickets, new RoomFlowLifetime()),
                "缺阶段机 = 装配缺口，构造即失败");
            Assert.Throws<ArgumentNullException>(() => new ProcedureFlow(machine, null, new RoomFlowLifetime()),
                "缺票据源 = 装配缺口，构造即失败");
            Assert.Throws<ArgumentNullException>(() => new ProcedureFlow(machine, tickets, null),
                "缺生命周期 = 装配缺口，构造即失败");
            Assert.DoesNotThrow(() => new ProcedureFlow(machine, tickets, new RoomFlowLifetime()).Dispose(),
                "三端口齐备可构造");

            root.Dispose();
            services.Dispose();
        }
    }
}
