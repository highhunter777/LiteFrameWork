using System;
using LiteClient;
using LiteFramework;
using LiteNet.Transport;

namespace LiteGame
{
    /// <summary>
    /// Account 域会话工厂（《客户端依赖注入专项设计》§6 服务作用域）：把动态进房参数变成可移交的
    /// <see cref="AccountSession"/>——**类型化工厂**（不用裸 new + 手工拼作用域散落进流程）。
    /// 传输选择经构造注入的委托由**产品装配根**决定（测试房/真 KCP 的裁决归
    /// <see cref="ProcedureMatch.CreateTransportForFactory"/>——本类不认识测试开关语义）。
    ///
    /// **零引擎依赖**（ClientScope/ServiceContainer/BattleClient/IClientTransport 皆 L1 可编）：
    /// 本件被 .NET 测试工程**源链接编译**——工厂行为在 L1 直测（离场/失败/再进房无残留），
    /// 不靠 Unity 才能验证。
    /// </summary>
    public interface IAccountSessionFactory
    {
        /// <summary>建 Account 会话：Account Scope（资源域）+ Account 服务域 + BattleClient（已发 Join）。
        /// 失败就地回滚（作用域与服务域 Dispose 后重抛）。</summary>
        AccountSession Create(string host, int port, string roomId, string token, string buildHash, bool testRoom);
    }

    /// <summary>
    /// Account 域的所有权载体（Scope = 资源域；Services = 服务域；Battle = 会话客户端）。
    /// 移交给 Battle 流程后由其收尾：<see cref="Dispose"/> 释放 Account Scope（资源域 LIFO——
    /// BattleClient 先于服务域释放）；服务域已作为同步资源登记进 Scope，随 Scope 一并释放。
    /// </summary>
    public sealed class AccountSession : IDisposable
    {
        public ClientScope Scope { get; }
        public ServiceContainer Services { get; }
        public BattleClient Battle { get; }
        private bool _disposed;

        public AccountSession(ClientScope scope, ServiceContainer services, BattleClient battle)
        {
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
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
        private readonly Func<bool, IClientTransport> _transportFactory;

        public AccountSessionFactory(ClientScope rootScope, ServiceContainer rootServices,
            Func<bool, IClientTransport> transportFactory)
        {
            _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
            _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        }

        public AccountSession Create(string host, int port, string roomId, string token, string buildHash, bool testRoom)
        {
            ClientScope accountScope = _rootScope.CreateChild("Account");
            ServiceContainer accountServices = null;
            try
            {
                accountServices = (ServiceContainer)_rootServices.CreateScope("Account");
                // AccountScope 负责动态会话资源（取消/租约/订阅）；服务域作为其一个同步资源登记，
                // 释放顺序由登记顺序决定（LIFO：BattleClient 先、服务域后）。
                accountScope.Register(accountServices);
                var battle = accountScope.Register(new BattleClient(host, port, roomId, token, buildHash,
                    transport: _transportFactory(testRoom)));
                accountServices.RegisterInstance(battle, ServiceLifetime.Scoped, ServiceOwnership.Borrowed);
                accountServices.Seal();
                return new AccountSession(accountScope, accountServices, battle);
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
