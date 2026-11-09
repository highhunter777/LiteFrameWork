using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;
using LiteNet.Proto;
using LiteSim;
using LiteSync.Client;
using MetaWire;

namespace LiteGame.Tests.EditMode
{
    /// <summary>可控 Meta 客户端：票据响应由测试完成点决定，忽略取消（复现晚到成功）。</summary>
    internal sealed class ControlledMetaClient : IMetaClient
    {
        public bool GateTickets;
        public int TicketCalls;
        public int CreateRoomCalls;
        public string LastRoomId;
        /// <summary>非 null 时取票原样返回（失败或畸形载荷）；null 走既有成功/闸门语义。</summary>
        public MetaCall<JoinTicketInfo> TicketResponse;

        private readonly List<UniTaskCompletionSource<MetaCall<JoinTicketInfo>>> _pending = new();

        public MetaCall<GuestLoginInfo> LoginResult = MetaCall<GuestLoginInfo>.OkResult(
            new GuestLoginInfo { AccountId = "acc-roomflow", AccessToken = "tok-roomflow", ExpiresAtMs = 1 });

        public UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync(string deviceId, CancellationToken ct = default)
            => UniTask.FromResult(LoginResult);

        public UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string accessToken, string roomId,
            string buildHash, CancellationToken ct = default)
        {
            TicketCalls++;
            LastRoomId = roomId;
            if (TicketResponse != null) return UniTask.FromResult(TicketResponse);
            if (!GateTickets) return UniTask.FromResult(Ticket(roomId));
            var gate = new UniTaskCompletionSource<MetaCall<JoinTicketInfo>>();
            _pending.Add(gate);
            return gate.Task;
        }

        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(string accessToken, string buildHash, int mapId = 0,
            int expectedPlayers = 0, string template = null, string gmKey = null, CancellationToken ct = default)
        {
            CreateRoomCalls++;
            return UniTask.FromResult(Ticket("Room-Create"));
        }

        public UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string accessToken,
            CancellationToken ct = default)
            => UniTask.FromResult(MetaCall<IReadOnlyList<LobbyInstanceView>>.OkResult(new LobbyInstanceView[0]));

        public UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string accessToken, int limit,
            CancellationToken ct = default)
            => UniTask.FromResult(MetaCall<IReadOnlyList<MatchRecordInfo>>.OkResult(new MatchRecordInfo[0]));

        public int PendingTickets => _pending.Count;

        public void CompleteTicket(int index, string roomId = null)
            => _pending[index].TrySetResult(Ticket(roomId));

        public void CompleteAll(string roomId = null)
        {
            for (int i = 0; i < _pending.Count; i++) _pending[i].TrySetResult(Ticket(roomId));
        }

        public static MetaCall<JoinTicketInfo> Ticket(string roomId)
            => MetaCall<JoinTicketInfo>.OkResult(new JoinTicketInfo
            {
                InstanceId = "ins-roomflow",
                Address = "203.0.113.7:17777",
                RoomId = string.IsNullOrEmpty(roomId) ? "Room-Quick" : roomId,
                Ticket = "tkt-roomflow",
                ExpiresAtMs = 1,
            });
    }

    /// <summary>可控账号会话：JoinAck 与 Dispose 计数是流程所有权的观察面；作用域/服务域按生产同构。</summary>
    internal sealed class ControlledMatchSession : IMatchSession
    {
        public int DisposeCount;
        public int JoinAckWaiters;

        public ControlledMatchSession(ClientScope scope = null, ServiceContainer services = null)
        {
            Scope = scope;
            Services = services;
        }

        public ClientScope Scope { get; }
        public ServiceContainer Services { get; }
        public ServiceContainer CreateMatchServices() => throw new NotSupportedException();
        public IBattleClient Client => null;
        public MatchTestRules Rules => null;
        public Func<int, SimMapData> Maps => _ => null;
        public Func<int, int> ResolveMapId => id => id;

        public event Action<JoinAck> Joined;

        public void TickIncoming() { }
        public void TickOutgoing() { }

        public void EmitJoined()
            => Joined?.Invoke(new JoinAck { PlayerId = 1, ReconnectToken = "tok-roomflow" });

        public void Dispose()
        {
            DisposeCount++;
            Scope?.Dispose();
        }
    }

    /// <summary>账号会话工厂：记录建连次数与参数；Match 域作用域/服务域按生产同构（交付即 Seal）。</summary>
    internal sealed class ControlledMatchSessionFactory : IMatchSessionFactory
    {
        private readonly ClientScope _rootScope;
        private readonly ServiceContainer _rootServices;

        public ControlledMatchSessionFactory(ClientScope rootScope = null, ServiceContainer rootServices = null)
        {
            _rootScope = rootScope;
            _rootServices = rootServices;
        }

        public int CreateCount;
        public int CreateAttempts;
        /// <summary>非 null 时下一次 Create 抛出并自清（模拟建连失败一次）。</summary>
        public Exception CreateFailure;
        public string LastHost;
        public int LastPort;
        public string LastRoomId;
        public ControlledMatchSession Last;

        public IMatchSession Create(string host, int port, string roomId, string token, bool testRoom)
        {
            CreateAttempts++;
            if (CreateFailure != null)
            {
                Exception failure = CreateFailure;
                CreateFailure = null;
                throw failure;
            }
            CreateCount++;
            LastHost = host;
            LastPort = port;
            LastRoomId = roomId;
            ClientScope scope = _rootScope != null ? _rootScope.CreateChild("Match") : new ClientScope("Match");
            ServiceContainer services = _rootServices != null
                ? (ServiceContainer)_rootServices.CreateScope("Match")
                : new ServiceContainer();
            scope.Register(services);
            services.Seal();
            Last = new ControlledMatchSession(scope, services);
            return Last;
        }
    }

    /// <summary>账号重入端口替身：只记调度次数（401 单点调用面）。</summary>
    internal sealed class ControlledAccountReentry : IAccountReentry
    {
        public int Calls;
        public string LastReason;

        public UniTask<bool> ReturnToLoginAsync(string reason, CancellationToken ct = default)
        {
            Calls++;
            LastReason = reason;
            return UniTask.FromResult(true);
        }
    }

    internal sealed class ControlledBattleCommands : IBattleCommands
    {
        public void RequestTeleportToAim() { }
    }

    internal sealed class ControlledViewGroup : IBattleViewGroup
    {
        public int LoadCount;
        public int DisposeCount;

        public UniTask LoadAsync(CancellationToken ct)
        {
            LoadCount++;
            return UniTask.CompletedTask;
        }

        public void Tick(float elapseSeconds) { }
        public void TickPreSample() { }
        public void Dispose() => DisposeCount++;
    }

    internal sealed class ControlledViewGroupFactory : IBattleViewGroupFactory
    {
        public IBattleViewGroup LastCreated { get; private set; }

        public IBattleViewGroup Create(ClientScope account, Action<SimVector3> teleportDispatch)
            => LastCreated = new ControlledViewGroup();
    }

    internal sealed class ControlledRoomSessionBridge : IRoomSessionBridge
    {
        public int BindCount;
        public int UnbindCount;

        public void BindSession(IRoomSessionFace session) => BindCount++;
        public void UnbindSession() => UnbindCount++;
    }

    /// <summary>可控对局运行端口：StartGame 等待用完成源，Dispose 计数可断言。</summary>
    internal sealed class ControlledBattleRun : IBattleRun
    {
        public int DisposeCount;
        public int DetachViewCount;

        public int MapId => 1;
        public bool HasStarted => true;
        public bool HasSimulation => true;
        public BattleRunOutcome? Outcome => null;
        public LiteNet.Proto.MatchEnded MatchResult => null;
        public int PlayerId => -1;
        public long ReconcileCount => 0;
        public MatchTestRules Rules => null;
        public IBattleViewGroup View => null;
        public IRoomSessionFace RoomSession => null;

        public event Action<BattleRunOutcome> Ended { add { } remove { } }

        public UniTask<bool> WaitStartedOrEndedAsync(CancellationToken ct)
            => UniTask.FromResult(true);

        public void AttachView(IBattleViewGroup view) { }
        public void ActivateView(Action openBattleMenu = null) { }
        public void DetachView() => DetachViewCount++;
        public void Tick(float deltaSeconds, IInputService input) { }
        public void Leave() { }
        public void Dispose() => DisposeCount++;
    }

    internal sealed class ControlledBattleRunFactory : IBattleRunFactory
    {
        public int CreateCount;
        public ControlledBattleRun Last;

        public IBattleRun Create(IMatchRuntime account)
        {
            CreateCount++;
            Last = new ControlledBattleRun();
            return Last;
        }
    }

    internal sealed class ControlledMapProvider : IMapProvider
    {
        public const string MapLocation = "Assets/Scenes/RoomFlowTestMap.unity";

        public SimMapData GetMap(int mapId) => throw new NotSupportedException();
        public string ScenePath(int mapId) => MapLocation;
        public IReadOnlyList<string> ScenePaths() => new[] { MapLocation };
    }

    /// <summary>可控场景服务：记录本次加载持有的场景；仅其卸载挂起，表达“必要卸载未结束”。</summary>
    internal sealed class ControlledSceneService : ISceneService
    {
        private readonly UniTaskCompletionSource _unloadGate = new();
        private readonly HashSet<string> _loaded = new();
        private readonly HashSet<string> _owned = new();

        public int LoadCalls;
        public int UnloadCalls;
        public int IgnoredUnloadCalls;
        public string LastUnload;

        public bool IsLoaded(string location) => _loaded.Contains(location);
        public string SingleSceneName => null;
        public int AdditiveCount => _loaded.Count;
        public IReadOnlyCollection<string> AdditiveLocations => new List<string>(_loaded);
        public float Progress => 0f;

        public UniTask LoadSingleAsync(string location, CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask UnloadSingleAsync(CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask LoadAdditiveAsync(string location, CancellationToken ct = default)
        {
            LoadCalls++;
            _loaded.Add(location);
            _owned.Add(location);
            return UniTask.CompletedTask;
        }

        public UniTask UnloadAdditiveAsync(string location, CancellationToken ct = default)
        {
            if (!_owned.Contains(location))
            {
                IgnoredUnloadCalls++;
                return UniTask.CompletedTask;
            }
            UnloadCalls++;
            LastUnload = location;
            _owned.Remove(location);
            return _unloadGate.Task;
        }

        public bool UnloadCompleted => _unloadGate.Task.Status == UniTaskStatus.Succeeded;

        public void CompleteUnload() => _unloadGate.TrySetResult();

        public void ReleaseAll() { }
    }

    internal sealed class RecordingLoginSurface : ILoginSurface
    {
        public int ShowLoginCount;
        public string LastReason;
        public bool IsLoginOpen => ShowLoginCount > 0;

        public void ShowLogin(string reason)
        {
            ShowLoginCount++;
            LastReason = reason;
        }
    }
}
