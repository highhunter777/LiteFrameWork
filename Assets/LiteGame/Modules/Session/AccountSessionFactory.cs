using System;
using LiteFramework;
using LiteFramework;
using LiteNet.Proto;
using LiteNet.Transport;

namespace LiteGame
{
    /// <summary>
    /// Account 域会话工厂（《客户端依赖注入专项设计》§6 服务作用域）：把动态进房参数变成可移交的
    /// <see cref="AccountSession"/>——**类型化工厂**（不用裸 new + 手工拼作用域散落进流程）。
    /// 传输选择经构造注入的委托由**产品装配根**决定（本地测试模式/真 KCP 的裁决归
    /// <see cref="ProcedureMatch.CreateTransport"/>——本类不认识测试开关语义）。
    ///
    /// **零引擎依赖**（ClientScope/ServiceContainer/BattleClient/IClientTransport 皆 L1 可编）：
    /// 本件被 .NET 测试工程**源链接编译**——工厂行为在 L1 直测（离场/失败/再进房无残留），
    /// 不靠 Unity 才能验证。
    /// </summary>
    public interface IAccountSessionFactory
    {
        /// <summary>建 Account 会话：Account Scope（资源域）+ Account 服务域 + BattleClient（已发 Join）。
        /// 失败就地回滚（作用域与服务域 Dispose 后重抛）。</summary>
        AccountSession Create(string host, int port, string roomId, string token, bool testRoom);
    }

    /// <summary>
    /// Account 域的所有权载体（Scope = 资源域；Services = 服务域；Battle = 会话客户端）。
    /// 移交给 Battle 流程后由其收尾：<see cref="Dispose"/> 释放 Account Scope（资源域 LIFO——
    /// BattleClient 先于服务域释放）；服务域已作为同步资源登记进 Scope，随 Scope 一并释放。
    /// </summary>
    public sealed class AccountSession : IMatchJoin, IDisposable
    {
        public ClientScope Scope { get; }
        public ServiceContainer Services { get; }
        public BattleClient Battle { get; }

        /// <summary>JoinAck 到达（进房阶段经端口等待；透传 BattleClient 客户端事件）。</summary>
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
        /// （`RoomClient.MapId` → `BattleScenes.ResolveMapId`）。</summary>
        public bool TestRoom { get; }

        /// <summary>本局规则持有件（与本地服权威侧同实例；常规房 = null）。
        /// 随会话显式移交给 Battle（`MatchSessionFactory` → `BattleContext`），不经静态登记口。</summary>
        public LiteSim.MatchTestRules Rules { get; }

        private bool _disposed;

        public AccountSession(ClientScope scope, ServiceContainer services, BattleClient battle, bool testRoom = false,
            LiteSim.MatchTestRules rules = null)
        {
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            TestRoom = testRoom;
            Rules = rules;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Scope.Dispose();                // 资源域 LIFO：battle（登记在后）先释放，再释放服务域
        }
    }

    /// <summary>默认 Account 域工厂（根作用域 + 根服务域 + 传输委托）。</summary>
    public sealed class AccountSessionFactory : IAccountSessionFactory
    {
        private readonly ClientScope _rootScope;
        private readonly ServiceContainer _rootServices;
        private readonly Func<bool, MatchTransport> _transportFactory;
        private readonly HandshakeIdentity _handshake;

        public AccountSessionFactory(ClientScope rootScope, ServiceContainer rootServices,
            Func<bool, MatchTransport> transportFactory, HandshakeIdentity handshake)
        {
            _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
            _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _handshake = handshake;
        }

        public AccountSession Create(string host, int port, string roomId, string token, bool testRoom)
        {
            ClientScope accountScope = _rootScope.CreateChild("Account");
            ServiceContainer accountServices = null;
            try
            {
                accountServices = (ServiceContainer)_rootServices.CreateScope("Account");
                // AccountScope 负责动态会话资源（取消/租约/订阅）；服务域作为其一个同步资源登记，
                // 释放顺序由登记顺序决定（LIFO：BattleClient 先、服务域后）。
                accountScope.Register(accountServices);
                MatchTransport match = _transportFactory(testRoom);   // 每局规则随传输同刻产出（同一装配步骤）
                var battle = accountScope.Register(new BattleClient(host, port, roomId, token, _handshake.BuildHash,
                    transport: match.Transport));
                accountServices.RegisterInstance(battle, ServiceLifetime.Scoped, ServiceOwnership.Borrowed);
                accountServices.Seal();
                return new AccountSession(accountScope, accountServices, battle, testRoom, match.Rules);
            }
            catch
            {
                accountScope.Dispose();
                accountServices?.Dispose();
                throw;
            }
        }
    }
}
