using System;
using LiteClient;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// Match 域会话工厂（《客户端依赖注入专项设计》§6）：从 Account 会话建立局内服务域
    /// （<see cref="ServiceContainer"/> 子域）与 <see cref="BattleContext"/>（**类型化工厂**——
    /// 进房参数/输入服务经工厂声明依赖进域，流程不再裸 new）。
    ///
    /// **Unity 面**（BattleContext 依赖 SimView——本件只被 Unity 编译；.NET 侧验收归 DI-4）。
    /// Account 半边（<see cref="AccountSessionFactory"/>）零引擎依赖、L1 源链接直测。
    /// </summary>
    public interface IMatchSessionFactory
    {
        /// <summary>建 Match 会话：服务子域 + BattleContext（工厂产物，容器承担其释放）。
        /// 失败就地回滚（服务域 Dispose 后重抛）。</summary>
        MatchSession Create(AccountSession account);
    }

    /// <summary>
    /// Match 域的所有权载体（Services = 服务域；Context = 对局上下文）。
    /// <see cref="Dispose"/> 释放服务域——工厂产物（BattleContext，Scoped/容器拥有）随域 LIFO 释放，
    /// 其内部 Match 资源域（订阅退订）由 BattleContext.Dispose 承担；Account 域由持有方
    /// （Battle 流程 finally）随后释放。
    /// </summary>
    public sealed class MatchSession : IDisposable
    {
        public ServiceContainer Services { get; }
        public BattleContext Context { get; }
        private bool _disposed;

        public MatchSession(ServiceContainer services, BattleContext context)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Services.Dispose();
        }
    }

    /// <summary>默认 Match 域工厂（输入服务为根域常驻——跨对局复用，随域声明进工厂依赖）。</summary>
    public sealed class MatchSessionFactory : IMatchSessionFactory
    {
        private readonly IInputService _input;

        public MatchSessionFactory(IInputService input)
        {
            _input = input;
        }

        public MatchSession Create(AccountSession account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            ServiceContainer matchServices = null;
            try
            {
                matchServices = (ServiceContainer)account.Services.CreateScope("Match");
                matchServices.RegisterFactory<BattleContext>(
                    c =>
                    {
                        var battle = c.Resolve<BattleClient>();
                        return new BattleContext(battle, account.Scope, _input);
                    },
                    ServiceLifetime.Scoped, typeof(BattleClient));
                matchServices.Seal();
                var context = matchServices.Resolve<BattleContext>();
                return new MatchSession(matchServices, context);
            }
            catch
            {
                matchServices?.Dispose();
                throw;
            }
        }
    }
}
