using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Config;
using LiteGame.Modules.UI;
using LiteTesting.Unity;
using MetaWire;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// Meta 账户域边界（方案 C / AC2）：登录开域、登出/软硬关闭、重登恰一次、门面切换、401×4 单点重入、
    /// 令牌不外泄；另保 13 例纯 Meta 边界（取票/建房取票参数与错误、取消令牌透传、部署事实）。
    /// </summary>
    public sealed class MetaEntryEditModeTests : UnityTestBase
    {
        private const string AccountId = "acc-1";
        private const string AccessToken = "tok-1";
        private const string DeviceId = "device-1";
        private const string ServerUrl = "http://meta.test";
        private const string NotLoggedInCode = "entry.not-logged-in";

        private sealed class FakeMetaClient : IMetaClient
        {
            public MetaCall<GuestLoginInfo> GuestLogin = MetaCall<GuestLoginInfo>.OkResult(
                new GuestLoginInfo { AccountId = AccountId, AccessToken = AccessToken, ExpiresAtMs = 1 });
            public MetaCall<JoinTicketInfo> JoinTicket = Ticket("Room-A", "ins-1", "203.0.113.7:17777", "tkt-1");
            public MetaCall<JoinTicketInfo> CreateRoomResult = Ticket("R-NEW001", "ins-2", "203.0.113.7:17778", "tkt-2");
            public MetaCall<IReadOnlyList<LobbyInstanceView>> Rooms =
                MetaCall<IReadOnlyList<LobbyInstanceView>>.OkResult(new LobbyInstanceView[0]);
            public MetaCall<IReadOnlyList<MatchRecordInfo>> Records =
                MetaCall<IReadOnlyList<MatchRecordInfo>>.OkResult(new MatchRecordInfo[0]);

            /// <summary>true = 所有令牌调用返回 401（<see cref="MetaFailure.IsAuthLost"/> 判据）。</summary>
            public bool AuthLost;

            public string SeenDeviceId;
            public string SeenRoomId;
            public string SeenBuildHash;
            public string SeenAccessToken;
            public int SeenMapId = -1;
            public int SeenExpectedPlayers = -1;
            public string SeenTemplate;
            public string SeenGmKey;
            public CancellationToken SeenTicketToken;
            public CancellationToken SeenCreateToken;
            public int TicketCalls;
            public int CreateRoomCalls;
            public int ListRoomsCalls;
            public int ListRecordsCalls;

            public int TokenCalls => TicketCalls + CreateRoomCalls + ListRoomsCalls + ListRecordsCalls;

            public static MetaCall<JoinTicketInfo> Ticket(string roomId, string instanceId, string address,
                string ticket)
                => MetaCall<JoinTicketInfo>.OkResult(new JoinTicketInfo
                {
                    InstanceId = instanceId,
                    Address = address,
                    RoomId = roomId,
                    Ticket = ticket,
                    ExpiresAtMs = 1,
                });

            public UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync(string deviceId, CancellationToken ct = default)
            {
                SeenDeviceId = deviceId;
                return UniTask.FromResult(GuestLogin);
            }

            public UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string accessToken, string roomId,
                string buildHash, CancellationToken ct = default)
            {
                TicketCalls++;
                SeenAccessToken = accessToken;
                SeenRoomId = roomId;
                SeenBuildHash = buildHash;
                SeenTicketToken = ct;
                return UniTask.FromResult(AuthLost ? AuthLostCall<JoinTicketInfo>() : JoinTicket);
            }

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(string accessToken, string buildHash, int mapId = 0,
                int expectedPlayers = 0, string template = null, string gmKey = null,
                CancellationToken ct = default)
            {
                CreateRoomCalls++;
                SeenAccessToken = accessToken;
                SeenBuildHash = buildHash;
                SeenMapId = mapId;
                SeenExpectedPlayers = expectedPlayers;
                SeenTemplate = template;
                SeenGmKey = gmKey;
                SeenCreateToken = ct;
                return UniTask.FromResult(AuthLost ? AuthLostCall<JoinTicketInfo>() : CreateRoomResult);
            }

            public UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string accessToken,
                CancellationToken ct = default)
            {
                ListRoomsCalls++;
                SeenAccessToken = accessToken;
                return UniTask.FromResult(AuthLost ? AuthLostCall<IReadOnlyList<LobbyInstanceView>>() : Rooms);
            }

            public UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string accessToken, int limit,
                CancellationToken ct = default)
            {
                ListRecordsCalls++;
                SeenAccessToken = accessToken;
                return UniTask.FromResult(AuthLost ? AuthLostCall<IReadOnlyList<MatchRecordInfo>>() : Records);
            }

            private static MetaCall<T> AuthLostCall<T>()
                => MetaCall<T>.Failed(MetaFailure.Rejected(MetaErrorCodes.HttpUnauthorized,
                    MetaErrorCodes.Unauthorized, "令牌失效"));
        }

        private sealed class FakeAccountReentry : IAccountReentry
        {
            public readonly List<string> Reasons = new List<string>();

            public int Calls => Reasons.Count;

            public UniTask<bool> ReturnToLoginAsync(string reason, CancellationToken ct = default)
            {
                Reasons.Add(reason);
                return UniTask.FromResult(true);
            }
        }

        private sealed class FakeRoomFlow : IRoomFlow
        {
            public int LeaveAsyncCalls;

            public UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(string roomId, CancellationToken ct = default)
                => UniTask.FromResult(FakeMetaClient.Ticket(roomId, "ins-flow", "203.0.113.7:17777", "tkt-flow"));

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId = 0, int expectedPlayers = 0,
                CancellationToken ct = default)
                => UniTask.FromResult(FakeMetaClient.Ticket("Room-Create", "ins-flow", "203.0.113.7:17777", "tkt-flow"));

            public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId, int expectedPlayers,
                string template, string gmKey, CancellationToken ct = default)
                => UniTask.FromResult(FakeMetaClient.Ticket("Room-Create", "ins-flow", "203.0.113.7:17777", "tkt-flow"));

            public bool LeaveMatch() => true;

            public UniTask<bool> LeaveMatchAsync(CancellationToken ct = default)
            {
                LeaveAsyncCalls++;
                return UniTask.FromResult(true);
            }
        }

        private sealed class CapturingSink : ILogSink
        {
            public readonly List<string> Messages = new List<string>();

            public void Write(in LogEntry entry) => Messages.Add(entry.Message ?? string.Empty);
        }

        private sealed class DisposeProbe : IDisposable
        {
            public int DisposeCount;

            public void Dispose() => DisposeCount++;
        }

        /// <summary>真实装配：RootScope + 根服务域 + AccountDomain + MetaLoginService + 门面 + 票据源。</summary>
        private sealed class AccountRig : IDisposable
        {
            public readonly ClientScope RootScope = new ClientScope("Root");
            public readonly ServiceContainer RootServices = new ServiceContainer();
            public readonly FakeMetaClient Client;
            public readonly FakeAccountReentry Reentry = new();
            public readonly FakeRoomFlow Rooms = new();
            public readonly IdentityContext Identity;
            public readonly MetaLoginService Login;
            public readonly AccountDomain Domain;
            public readonly AccountTicketSource Tickets;
            public readonly MetaSession Facade;
            public readonly bool ReentryInjected;

            public AccountRig(FakeMetaClient client = null, bool configured = true)
            {
                Client = client ?? new FakeMetaClient();
                Identity = IdentityOf();
                Login = new MetaLoginService(configured ? Client : null, () => DeviceId, ServerUrl, Identity);

                RootServices.RegisterInstance<IdentityContext>(Identity);
                RootServices.RegisterInstance<IMetaClient>(Client);
                RootServices.RegisterInstance<IAccountReentry>(Reentry);
                RootServices.RegisterInstance<IMetaConfigFacts>(Login);

                Domain = NewDomain(RootScope, RootServices, Reentry, out bool injected);
                ReentryInjected = injected;
                RootServices.RegisterInstance<IAccountDomain>(Domain);
                RootServices.Seal();
                RootScope.Register(Domain);

                Tickets = new AccountTicketSource(Domain);
                Facade = new MetaSession(Domain, Login, Rooms);
            }

            public bool Login2() => Facade.GuestLoginAsync().GetAwaiter().GetResult().Ok;

            public IMetaAccountSession RequireAccount()
            {
                IMetaAccountSession account = Domain.Account;
                Assert.IsNotNull(account,
                    "登录后账户会话必须可解析（AccountDomain.Open 建 MetaAccountSession 并登记 Account 服务域）");
                return account;
            }

            public void Dispose()
            {
                RootScope.Dispose();
                RootServices.Dispose();
            }
        }

        private static IdentityContext IdentityOf() => new IdentityContext(
            new BuildIdentity(LiteNet.BuildHash.Value, "0.1.0", "WindowsPlayer", string.Empty, 5),
            new SchemaIdentity(0, 0, 0, ActivationRecord.CurrentSchemaVersion));

        /// <summary>建域：优先注入 <see cref="IAccountReentry"/>（401 单点）；无该形参时退回最小构造。</summary>
        private static AccountDomain NewDomain(ClientScope rootScope, ServiceContainer rootServices,
            IAccountReentry reentry, out bool reentryInjected)
        {
            var ports = new Dictionary<Type, object>
            {
                [typeof(ClientScope)] = rootScope,
                [typeof(ServiceContainer)] = rootServices,
                [typeof(IAccountReentry)] = reentry,
            };

            ConstructorInfo best = null;
            object[] bestArgs = null;
            foreach (ConstructorInfo ctor in typeof(AccountDomain).GetConstructors())
            {
                ParameterInfo[] ps = ctor.GetParameters();
                var args = new object[ps.Length];
                bool usable = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!ports.TryGetValue(ps[i].ParameterType, out object arg))
                    {
                        if (!ps[i].HasDefaultValue)
                        {
                            usable = false;
                            break;
                        }
                        args[i] = ps[i].DefaultValue;
                        continue;
                    }
                    args[i] = arg;
                }
                if (!usable) continue;
                if (best == null || ps.Length > best.GetParameters().Length)
                {
                    best = ctor;
                    bestArgs = args;
                }
            }

            Assert.IsNotNull(best, "AccountDomain 缺少可用构造（需要 ClientScope + ServiceContainer；401 需 IAccountReentry）");
            reentryInjected = best.GetParameters().Any(p => p.ParameterType == typeof(IAccountReentry));
            return (AccountDomain)best.Invoke(bestArgs);
        }

        /// <summary>建每账户会话：按形参名/类型装配（T4 可增参，助手不因签名微调而编译失败）。</summary>
        private static MetaAccountSession NewAccountSession(FakeMetaClient client, IdentityContext identity,
            string accountId, string accessToken, string template = null, string gmKey = null,
            IAccountReentry reentry = null)
        {
            ConstructorInfo ctor = typeof(MetaAccountSession).GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length).First();
            ParameterInfo[] ps = ctor.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                string name = ps[i].Name ?? string.Empty;
                if (ps[i].ParameterType == typeof(IMetaClient)) args[i] = client;
                else if (ps[i].ParameterType == typeof(IdentityContext)) args[i] = identity;
                else if (ps[i].ParameterType == typeof(IAccountReentry)) args[i] = reentry;
                else if (name.IndexOf("account", StringComparison.OrdinalIgnoreCase) >= 0) args[i] = accountId;
                else if (name.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0) args[i] = accessToken;
                else if (name.IndexOf("template", StringComparison.OrdinalIgnoreCase) >= 0) args[i] = template;
                else if (name.IndexOf("gm", StringComparison.OrdinalIgnoreCase) >= 0) args[i] = gmKey;
                else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                else Assert.Fail("MetaAccountSession 构造形参未识别：" + name);
            }
            return (MetaAccountSession)ctor.Invoke(args);
        }

        private sealed class EmptySegment : IClientSegment
        {
            public EmptySegment(string name) => Name = name;

            public string Name { get; }

            public UniTask InitializeAsync(SegmentContext context, CancellationToken ct) => UniTask.CompletedTask;

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        private sealed class ProductSegment<T> : IClientSegment where T : class
        {
            public ProductSegment(string name, T product)
            {
                Name = name;
                Product = product;
            }

            public string Name { get; }
            public T Product { get; }

            public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
            {
                context.Put(Product);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        // ---- 13 例装配重写：部署事实 + 每账户会话双对象 ----

        [Test]
        public void 配置测试房_建房带模板与口令_未配置则请求体不变()
        {
            var client = new FakeMetaClient();
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken,
                MetaLoginService.TestRoomTemplate, "k-1");

            session.CreateRoomTicketAsync(mapId: 1, expectedPlayers: 2).GetAwaiter().GetResult();

            Assert.AreEqual(MetaLoginService.TestRoomTemplate, client.SeenTemplate, "配置了模板 ⇒ 随建房请求带上");
            Assert.AreEqual("k-1", client.SeenGmKey, "口令同批带上（服务端比对，不匹配 403）");

            var plain = new FakeMetaClient();
            MetaAccountSession plainSession = NewAccountSession(plain, IdentityOf(), AccountId, AccessToken);
            plainSession.CreateRoomTicketAsync(mapId: 1, expectedPlayers: 2).GetAwaiter().GetResult();

            Assert.IsNull(plain.SeenTemplate, "未配置 ⇒ 不带 template（普通建房请求体与之前逐字节一致）");
            Assert.IsNull(plain.SeenGmKey);
        }

        [Test]
        public void 显式模板建房_不依赖部署注入_携带地图与口令()
        {
            var client = new FakeMetaClient();
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken);

            MetaCall<JoinTicketInfo> call = session.CreateRoomTicketAsync(
                mapId: 2, expectedPlayers: 0,
                template: MetaLoginService.TestRoomTemplate, gmKey: "k-2").GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(MetaLoginService.TestRoomTemplate, client.SeenTemplate, "显式入口固定 sandbox");
            Assert.AreEqual("k-2", client.SeenGmKey, "口令随本次请求携带");
            Assert.AreEqual(2, client.SeenMapId, "测试房选图随建房请求盖章");
        }

        [Test]
        public void 未配置_登录取票查询全显性失败_零客户端调用()
        {
            using var rig = new AccountRig(new FakeMetaClient(), configured: false);

            Assert.IsFalse(rig.Login2(), "未配置：登录显性失败");
            Assert.AreEqual("entry.not-configured", rig.Login.GuestLoginAsync().GetAwaiter().GetResult().Failure.Code);

            MetaCall<JoinTicketInfo> enter = rig.Tickets.RequestJoinTicketAsync("").GetAwaiter().GetResult();
            Assert.IsFalse(enter.Ok);
            Assert.AreEqual("entry.not-configured", enter.Failure.Code, "失败优先级：NotConfigured > NotLoggedIn");

            MetaCall<JoinTicketInfo> create = rig.Tickets.CreateRoomTicketAsync().GetAwaiter().GetResult();
            Assert.IsFalse(create.Ok);
            Assert.AreEqual("entry.not-configured", create.Failure.Code);

            MetaResult<IReadOnlyList<MetaRecordRow>> records = rig.Facade.ListRecordsAsync().GetAwaiter().GetResult();
            Assert.IsFalse(records.Ok, "未配置：战绩查询同样显性失败");
            Assert.AreEqual("entry.not-configured", records.Reason, "失败优先级：NotConfigured > NotLoggedIn");

            Assert.AreEqual(0, rig.Client.TicketCalls, "fail-closed：未配置不得发任何票据请求");
            Assert.AreEqual(0, rig.Client.CreateRoomCalls, "fail-closed：未配置不得发任何建房请求");
            Assert.AreEqual(0, rig.Client.ListRoomsCalls + rig.Client.ListRecordsCalls,
                "fail-closed：未配置不得发任何查询请求");
        }

        [Test]
        public void 登录成功_缓存账号_设备标识直达客户端()
        {
            using var rig = new AccountRig();

            Assert.IsTrue(rig.Login2(), "登录成功");
            Assert.AreEqual(AccountId, rig.Facade.AccountId, "登录态缓存——页面据此显示");
            Assert.AreEqual(DeviceId, rig.Client.SeenDeviceId, "设备标识经注入直达客户端（get-or-create 幂等键）");
        }

        [Test]
        public void 未登录_取票与建房取票被拒_零票据调用()
        {
            var client = new FakeMetaClient();
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), null, null);

            MetaCall<JoinTicketInfo> enter = session.RequestJoinTicketAsync("Room-A").GetAwaiter().GetResult();
            Assert.IsFalse(enter.Ok);
            Assert.AreEqual(NotLoggedInCode, enter.Failure.Code);

            MetaCall<JoinTicketInfo> create = session.CreateRoomTicketAsync().GetAwaiter().GetResult();
            Assert.IsFalse(create.Ok);
            Assert.AreEqual(NotLoggedInCode, create.Failure.Code);

            Assert.AreEqual(0, client.TicketCalls, "门禁在客户端调用之前——不浪费一次签发");
            Assert.AreEqual(0, client.CreateRoomCalls, "门禁在客户端调用之前——不浪费一次建房");
        }

        [Test]
        public void 换票成功_返回票据事实_版本与房号原样()
        {
            var client = new FakeMetaClient();
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken);

            MetaCall<JoinTicketInfo> call = session.RequestJoinTicketAsync("Room-A").GetAwaiter().GetResult();

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
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken);

            MetaCall<JoinTicketInfo> call = session.CreateRoomTicketAsync().GetAwaiter().GetResult();

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
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken);

            MetaCall<JoinTicketInfo> call = session.CreateRoomTicketAsync(mapId: 2).GetAwaiter().GetResult();

            Assert.IsTrue(call.Ok);
            Assert.AreEqual(2, client.SeenMapId, "请求地图行键原样透传");
            Assert.AreEqual(1, client.CreateRoomCalls);
        }

        [Test]
        public void 创建房间_带期望人数_原样透传给客户端()
        {
            var client = new FakeMetaClient();
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken);

            MetaCall<JoinTicketInfo> call = session.CreateRoomTicketAsync(mapId: 1, expectedPlayers: 4)
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
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), null, null);

            MetaCall<JoinTicketInfo> call = session.CreateRoomTicketAsync().GetAwaiter().GetResult();

            Assert.IsFalse(call.Ok);
            Assert.AreEqual(NotLoggedInCode, call.Failure.Code);
            Assert.AreEqual(0, client.CreateRoomCalls, "门禁在客户端调用之前");
        }

        [Test]
        public void 预取消_取票与建房取票均零HTTP()
        {
            var client = new FakeMetaClient();
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                session.RequestJoinTicketAsync("Room-A", cts.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() =>
                session.CreateRoomTicketAsync(1, 2, cts.Token).GetAwaiter().GetResult());

            Assert.AreEqual(0, client.TicketCalls, "预取消不得发取票 HTTP");
            Assert.AreEqual(0, client.CreateRoomCalls, "预取消不得发建房 HTTP");
        }

        [Test]
        public void 取票与建房取票_传递caller的token给IMetaClient()
        {
            var client = new FakeMetaClient();
            MetaAccountSession session = NewAccountSession(client, IdentityOf(), AccountId, AccessToken);
            using var cts = new CancellationTokenSource();

            session.RequestJoinTicketAsync("Room-A", cts.Token).GetAwaiter().GetResult();
            session.CreateRoomTicketAsync(1, 2, cts.Token).GetAwaiter().GetResult();

            Assert.AreEqual(cts.Token, client.SeenTicketToken, "取票 token 必须是 caller 的原 token");
            Assert.AreEqual(cts.Token, client.SeenCreateToken, "建房 token 必须是 caller 的原 token");
            Assert.IsTrue(client.SeenTicketToken.CanBeCanceled, "可取消 token 不得被替换成 None");
        }

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

        // ---- 账户域生命周期（真实 RootScope / 根容器 / AccountDomain / 门面）----

        [Test]
        public void 登录开域_账户服务域可解析会话()
        {
            using var rig = new AccountRig();

            Assert.IsTrue(rig.Login2(), "登录成功（前置）");

            Assert.IsTrue(rig.Domain.IsOpen, "登录成功必须开启账户域");
            Assert.AreEqual(AccountId, rig.Domain.AccountId, "账户域持 AccountId");
            Assert.IsNotNull(rig.Domain.Scope, "账户域必须有独立资源域（ClientScope）");
            Assert.AreEqual("Account", rig.Domain.Scope.Name, "账户域作用域名 = Account");
            Assert.IsFalse(rig.Domain.Scope.IsDisposed, "开启期不得释放");

            IMetaAccountSession account = null;
            Assert.DoesNotThrow(() => account = rig.Domain.Services.Resolve<IMetaAccountSession>(),
                "Account 服务域必须登记 IMetaAccountSession（Meta 本地接口）");
            Assert.IsNotNull(account, "账户会话非空");
            Assert.AreSame(account, rig.Domain.Account, "服务域解析与域属性同一实例");
            Assert.IsTrue(account.IsLoggedIn, "每账户会话持令牌");
        }

        [Test]
        public void 登出关域_旧scope释放_服务域清空()
        {
            using var rig = new AccountRig();
            Assert.IsTrue(rig.Login2(), "登录成功（前置）");
            ClientScope scope = rig.Domain.Scope;
            ServiceContainer services = rig.Domain.Services;
            Assert.IsNotNull(scope, "登录后账户域作用域非空（前置）");
            Assert.IsNotNull(services, "登录后账户服务域非空（前置）");

            rig.Domain.Close(AccountCloseReason.Logout);

            Assert.IsFalse(rig.Domain.IsOpen, "登出必须关闭账户域");
            Assert.IsTrue(scope.IsDisposed, "旧资源域必须释放");
            Assert.AreEqual(0, scope.OwnedCount, "旧资源域登记项清零");
            Assert.IsTrue(services.IsDisposed, "Account 服务域随域释放");
            Assert.IsTrue(rig.Domain.Scope == null || rig.Domain.Scope.IsDisposed, "关闭后不暴露活作用域");
            Assert.IsTrue(rig.Domain.Account == null || !rig.Domain.Account.IsLoggedIn, "关闭后不暴露可用会话");
        }

        [Test]
        public void 重登_旧域释放恰一次_新scope不等()
        {
            using var rig = new AccountRig();
            Assert.IsTrue(rig.Login2(), "首次登录（前置）");
            ClientScope first = rig.Domain.Scope;
            Assert.IsNotNull(first, "首次登录后作用域非空（前置）");
            var probe = new DisposeProbe();
            first.Register(probe);

            Assert.IsTrue(rig.Login2(), "重登（前置）");

            Assert.AreEqual(1, probe.DisposeCount, "旧域资源恰好释放一次（不重复释放）");
            Assert.IsTrue(first.IsDisposed, "旧域已释放");
            Assert.IsNotNull(rig.Domain.Scope, "新域已开启");
            Assert.AreNotSame(first, rig.Domain.Scope, "重登必须换新作用域（不复用死域）");
            Assert.IsFalse(rig.Domain.Scope.IsDisposed);
            Assert.AreEqual(AccountId, rig.Domain.AccountId);
        }

        [Test]
        public void Revoke后仍可Close_Close幂等()
        {
            using var rig = new AccountRig();
            Assert.IsTrue(rig.Login2(), "登录成功（前置）");
            ClientScope scope = rig.Domain.Scope;
            Assert.IsNotNull(scope, "登录后作用域非空（前置）");
            var probe = new DisposeProbe();
            scope.Register(probe);

            rig.Domain.Revoke(AccountCloseReason.AuthLost);

            Assert.IsFalse(rig.Domain.IsOpen, "软关闭立即标记未登录（拒绝新请求）");
            Assert.IsFalse(scope.IsDisposed, "软关闭不得释放账户域（硬释放延后到 Match 清理完成）");
            Assert.AreEqual(0, probe.DisposeCount, "软关闭不得释放域内资源");

            rig.Domain.Close(AccountCloseReason.Logout);
            Assert.IsTrue(scope.IsDisposed, "软关闭后仍可硬释放");
            Assert.AreEqual(1, probe.DisposeCount);

            Assert.DoesNotThrow(() => rig.Domain.Close(AccountCloseReason.Logout), "Close 幂等");
            Assert.DoesNotThrow(() => rig.Domain.Revoke(AccountCloseReason.Shutdown), "Revoke 幂等");
            Assert.AreEqual(1, probe.DisposeCount, "重复关闭不得重复释放");
        }

        [Test]
        public void Open失败_不留半开域_可重登()
        {
            using var rig = new AccountRig();

            Assert.Throws<ArgumentNullException>(() => rig.Domain.Open(null, AccessToken),
                "缺 AccountId = 程序员错误，构造即失败");

            Assert.IsFalse(rig.Domain.IsOpen, "Open 失败不得留下半开域");
            Assert.IsTrue(rig.Domain.Scope == null || rig.Domain.Scope.IsDisposed, "失败不留活作用域");
            Assert.IsTrue(rig.Domain.Services == null || rig.Domain.Services.IsDisposed, "失败不留活服务域");

            Assert.IsTrue(rig.Login2(), "失败后仍可重新登录");
            Assert.IsTrue(rig.Domain.IsOpen);
        }

        [Test]
        public void 门面切换_登录读新会话_登出读NotLoggedIn_旧会话不可读()
        {
            using var rig = new AccountRig();
            Assert.IsTrue(rig.Login2(), "登录成功（前置）");
            IMetaAccountSession old = rig.RequireAccount();
            MetaSession facade = rig.Facade;

            Assert.AreEqual(AccountId, facade.AccountId, "门面读当前账户");
            Assert.IsTrue(facade.ListRoomsAsync().GetAwaiter().GetResult().Ok, "登录后房间列表可读");

            rig.Domain.Close(AccountCloseReason.Logout);

            Assert.IsTrue(string.IsNullOrEmpty(facade.AccountId), "登出后门面读不到旧账号");
            MetaResult<IReadOnlyList<MetaRoomRow>> rooms = facade.ListRoomsAsync().GetAwaiter().GetResult();
            Assert.IsFalse(rooms.Ok, "登出后门面读 NotLoggedIn");
            Assert.AreEqual(NotLoggedInCode, rooms.Reason, "失败码单源（不复制字符串）");
            Assert.AreSame(facade, rig.Facade, "页面持同一门面（不重建）");

            Assert.IsFalse(old.IsLoggedIn, "旧会话已失效（不可再读）");
            int callsBefore = rig.Client.ListRoomsCalls;
            MetaCall<IReadOnlyList<LobbyInstanceView>> stale = old.ListRoomsAsync().GetAwaiter().GetResult();
            Assert.IsFalse(stale.Ok, "旧会话调用必须被拒");
            Assert.AreEqual(NotLoggedInCode, stale.Failure.Code);
            Assert.AreEqual(callsBefore, rig.Client.ListRoomsCalls, "旧会话失效后不得再发令牌请求");
        }

        [Test]
        public void 令牌不进日志_公开端口无令牌成员()
        {
            var sink = new CapturingSink();
            Log.SetSink(sink);
            try
            {
                using var rig = new AccountRig();
                Assert.IsTrue(rig.Login2(), "登录成功（前置）");
                rig.Facade.ListRoomsAsync().GetAwaiter().GetResult();
                rig.RequireAccount().RequestJoinTicketAsync("Room-A").GetAwaiter().GetResult();
            }
            finally
            {
                Log.SetSink(null);
            }

            Assert.IsFalse(sink.Messages.Any(m => m.IndexOf(AccessToken, StringComparison.Ordinal) >= 0),
                "令牌不得进日志出口（Log.SetSink）");

            foreach (Type port in new[] { typeof(IAccountDomain), typeof(IMetaAccountSession), typeof(IMetaSession) })
            {
                foreach (MemberInfo member in port.GetMembers(BindingFlags.Public | BindingFlags.Instance))
                {
                    string name = member.Name.ToLowerInvariant();
                    Assert.IsFalse(name.Contains("token") || name.Contains("secret") || name.Contains("password"),
                        port.Name + " 公开成员不得暴露令牌面：" + member.Name);
                }
            }
        }

        [UnityTest]
        public System.Collections.IEnumerator 四调用点401_各调度一次_不自拆()
        {
            using var rig = new AccountRig();
            MetaAccountSession session = NewAccountSession(rig.Client, rig.Identity, AccountId, AccessToken,
                reentry: rig.Reentry);
            rig.Client.AuthLost = true;

            UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> t1 = session.ListRoomsAsync();
            UniTask<MetaCall<JoinTicketInfo>> t2 = session.RequestJoinTicketAsync("Room-A");
            UniTask<MetaCall<JoinTicketInfo>> t3 = session.CreateRoomTicketAsync();
            UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> t4 = session.ListRecordsAsync(20);

            for (int i = 0; i < 240; i++)
            {
                if (!t1.Status.Equals(UniTaskStatus.Pending) && !t2.Status.Equals(UniTaskStatus.Pending)
                    && !t3.Status.Equals(UniTaskStatus.Pending) && !t4.Status.Equals(UniTaskStatus.Pending)
                    && rig.Reentry.Calls >= 4)
                {
                    break;
                }
                yield return null;
            }

            Assert.AreNotEqual(UniTaskStatus.Pending, t1.Status, "ListRooms 401 未收口");
            Assert.AreNotEqual(UniTaskStatus.Pending, t2.Status, "取票 401 未收口");
            Assert.AreNotEqual(UniTaskStatus.Pending, t3.Status, "建房 401 未收口");
            Assert.AreNotEqual(UniTaskStatus.Pending, t4.Status, "战绩 401 未收口");

            MetaCall<IReadOnlyList<LobbyInstanceView>> rooms = t1.GetAwaiter().GetResult();
            MetaCall<JoinTicketInfo> ticket = t2.GetAwaiter().GetResult();
            MetaCall<JoinTicketInfo> created = t3.GetAwaiter().GetResult();
            MetaCall<IReadOnlyList<MatchRecordInfo>> records = t4.GetAwaiter().GetResult();

            Assert.AreEqual(4, rig.Reentry.Calls, "四个令牌调用点各调度一次账号重入（不自拆、不递归）");
            Assert.AreEqual(4, rig.Client.TokenCalls, "每调用点一次令牌请求（不重放）");
            Assert.IsFalse(rooms.Ok, "ListRooms 401 返回失败结果");
            Assert.IsFalse(ticket.Ok, "取票 401 返回失败结果");
            Assert.IsFalse(created.Ok, "建房 401 返回失败结果");
            Assert.IsFalse(records.Ok, "战绩 401 返回失败结果");
            Assert.AreEqual(MetaErrorCodes.Unauthorized, rooms.Failure.Code, "401 失败载荷保持 auth.unauthorized 判据");
        }

        [Test]
        public void 真实MetaSegment装配_根容器登记账户域_ForceReleaseRoot释放()
        {
            var host = new ClientHost();
            host.AddSegment(new EmptySegment("PlatformInfrastructure"));
            host.AddSegment(new ProductSegment<IdentityContext>("Identity", IdentityOf()));
            host.AddSegment(new MetaSegment(new DeployBake(_ => null)));

            Assert.DoesNotThrow(() => host.InitializeAsync().GetAwaiter().GetResult(), "MetaSegment 初始化不得抛");
            try
            {
                Assert.IsNotNull(host.Product<IRoomTicketSource>(), "票据源门面必须由 MetaSegment 登记（流程只持稳定门面）");
                Assert.IsNotNull(host.Product<IMetaConfigFacts>(), "部署事实端口必须由 MetaSegment 登记");

                IAccountDomain domain = host.Product<IAccountDomain>();
                Assert.IsNotNull(domain, "MetaSegment 必须在根容器登记 IAccountDomain（AC8 消费者/释放者）");

                domain.Open(AccountId, AccessToken);
                Assert.IsTrue(domain.IsOpen, "登录开域（前置）");

                host.ForceReleaseRoot();
                Assert.IsFalse(domain.IsOpen, "ForceReleaseRoot 必须释放账户域（RootScope 兜底）");
            }
            finally
            {
                host.ForceReleaseRoot();
            }
        }
    }
}
