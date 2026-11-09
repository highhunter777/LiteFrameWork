using System;
using LiteFramework;
using LiteNet.Proto;
using LiteNet.Transport;
using LiteSim;

using LiteSync.Client;
namespace LiteGame
{

    /// <summary>
    /// Match 域的所有权载体（Scope = 资源域）。
    /// </summary>
    public sealed class MatchSession : IMatchSession
    {
        public ClientScope Scope { get; }
        public ServiceContainer Services { get; }

        /// <summary>本局会话客户端端口（构造只经 <see cref="IBattleClientFactory"/>）。</summary>
        public IBattleClient Battle { get; }

        /// <summary>对局侧客户端读口（与 <see cref="Battle"/> 同实例；<see cref="IMatchRuntime"/> 显式实现）。</summary>
        IBattleClient IMatchRuntime.Client => Battle;

        /// <summary>地图提供委托（行键 → 判定图；装配根从 <c>IMapProvider</c> 投影，Battle 不认 Map 域）。</summary>
        public Func<int, SimMapData> Maps { get; }

        /// <summary>地图行键裁决委托（JoinAck 的 MapId → 表行键；装配根绑单源裁决）。</summary>
        public Func<int, int> ResolveMapId { get; }

        /// <summary>JoinAck 到达（进房阶段经端口等待；透传客户端事件）。</summary>
        public event Action<JoinAck> Joined
        {
            add => Battle.Client.OnJoinAck += value;
            remove => Battle.Client.OnJoinAck -= value;
        }

        /// <summary>推进接收泵（KCP 握手与收包；进房阶段由流程段每帧驱动）。</summary>
        public void TickIncoming() => Battle.TickIncoming();

        /// <summary>推进发送泵（KCP 发包；进房阶段由流程段每帧驱动）。</summary>
        public void TickOutgoing() => Battle.TickOutgoing();

        /// <summary>本地测试模式/本地房标记（进房入口带入；**每局规则**用——`MatchTestRules` 免死/无限子弹
        /// 的入口语义）。多图批后**不再承担地图选行**：本局地图行键以 JoinAck 权威事实为准
        /// （<see cref="ResolveMapId"/> 裁决，装配根绑单源）。</summary>
        public bool TestRoom { get; }

        /// <summary>本局规则持有件（与本地服权威侧同实例；常规房 = null）。
        /// 随会话显式移交给 Battle（`BattleDomainFactory` → `BattleContext`），不经静态登记口。</summary>
        public MatchTestRules Rules { get; }

        private bool _disposed;

        public MatchSession(ClientScope scope, ServiceContainer services, IBattleClient battle,
            Func<int, SimMapData> maps, Func<int, int> resolveMapId, bool testRoom = false,
            MatchTestRules rules = null)
        {
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            Maps = maps ?? throw new ArgumentNullException(nameof(maps));
            ResolveMapId = resolveMapId ?? throw new ArgumentNullException(nameof(resolveMapId));
            TestRoom = testRoom;
            Rules = rules;
        }

        /// <summary>建 Match 服务子域（**未 Seal**——注册由 BattleDomainFactory 完成后 Seal）。
        /// Battle 唯一的容器入口：只拿得到 Match 子域，写不到 Account/根容器。</summary>
        public ServiceContainer CreateMatchServices()
        {
            var services = new ServiceContainer();
            Scope.Register(services);                // 随 Match 作用域释放（显式 Dispose 幂等）
            return services;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Scope.Dispose();                // 资源域 LIFO：battle（登记在后）先释放，再释放服务域
        }
    }

    /// <summary>默认 Match 域工厂（根作用域 + 根服务域 + 传输/客户端/地图装配面）。</summary>
    public sealed class MatchSessionFactory : IMatchSessionFactory
    {
        private readonly ClientScope _rootScope;
        private readonly Func<bool, MatchTransport> _transportFactory;
        private readonly HandshakeIdentity _handshake;
        private readonly IBattleClientFactory _battleClients;
        private readonly Func<int, SimMapData> _maps;
        private readonly Func<int, int> _resolveMapId;
        private readonly Func<ClientScope> _parentScopeProvider;

        /// <param name="battleClients">客户端工厂端口（会话域不 `new` 具体客户端；唯一构造点归 Battle 域）。</param>
        /// <param name="maps">地图提供委托（装配根从 <c>IMapProvider</c> 投影；本工厂不依赖 Map 域类型）。</param>
        /// <param name="resolveMapId">地图行键裁决委托（装配根绑单源裁决，与场景装载同键）。</param>
        public MatchSessionFactory(ClientScope rootScope,
            Func<bool, MatchTransport> transportFactory, HandshakeIdentity handshake,
            IBattleClientFactory battleClients, Func<int, SimMapData> maps, Func<int, int> resolveMapId,
            Func<ClientScope> parentScopeProvider = null)
        {
            _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _handshake = handshake;
            _battleClients = battleClients ?? throw new ArgumentNullException(nameof(battleClients));
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _resolveMapId = resolveMapId ?? throw new ArgumentNullException(nameof(resolveMapId));
            _parentScopeProvider = parentScopeProvider;
        }

        /// <summary>端口面（流程/装配点只认它——流程段拿不到实现件）。</summary>
        IMatchSession IMatchSessionFactory.Create(string host, int port, string roomId, string token, bool testRoom)
            => Create(host, port, roomId, token, testRoom);

        /// <summary>建 Match 会话（具体返回类型供源链接测试与装配点直用；端口面见上方显式实现）。</summary>
        public MatchSession Create(string host, int port, string roomId, string token, bool testRoom)
        {
            ClientScope parentScope = _parentScopeProvider != null ? _parentScopeProvider() : _rootScope;
            ClientScope matchScope = parentScope.CreateChild("Match");
            ServiceContainer matchServices = null;
            try
            {
                matchServices = new ServiceContainer();
                // Match 作用域负责动态会话资源（取消/租约/订阅）；服务域作为其一个同步资源登记，
                // 释放顺序由登记顺序决定（LIFO：客户端先、服务域后）。
                matchScope.Register(matchServices);
                MatchTransport match = _transportFactory(testRoom);   // 每局规则随传输同刻产出（同一装配步骤）
                IBattleClient battle = matchScope.Register(_battleClients.Create(host, port, roomId, token,
                    _handshake.BuildHash, match.Transport));
                // 服务域按**端口**登记（消费面只认 IBattleClient——具体类型不出 Battle 域）。
                matchServices.RegisterInstance<IBattleClient>(battle);
                matchServices.Seal();
                return new MatchSession(matchScope, matchServices, battle,
                    maps: _maps, resolveMapId: _resolveMapId, testRoom: testRoom, rules: match.Rules);
            }
            catch
            {
                matchScope.Dispose();
                matchServices?.Dispose();
                throw;
            }
        }
    }
}
