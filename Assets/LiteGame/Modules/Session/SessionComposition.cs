using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Config;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;

namespace LiteGame
{
    /// <summary>⑰ 会话：账号/对局会话工厂（会话所有权归本段）＋ 传输工厂端口。
    /// 会话是"跨对局常驻"的所有者：进房/离场只换对局半边，账号半边跨局复用。</summary>
    public sealed class SessionComposition : IClientModule, IClientModuleGraph
    {
        public string Name => "Session";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure", "Identity", "DevSession", "Content", "Config", "Map", "Input", "Meta",
        };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            IConfigService config = context.Require<IConfigService>();
            IdentityContext identity = context.Require<IdentityContext>();

            // 玩法数据身份基摘要提供者：装配期只装**提供者**（本段早于 Preload 装载配置——
            // 取值点=进房/本地服建房，恒在装载之后）；两端摘要由构造同源保证。
            Func<uint> gameplayDigestBase = () => config.GameplayDigestBase;
            IMapProvider maps = context.Require<IMapProvider>();

            var transport = new ProductTransportFactory(
                context.Require<MetaEntryConfig>(), gameplayDigestBase, maps, context.Require<IDevSession>());
            context.Put<ITransportFactory>(transport);

            // 注册面＝服务接口（消费者经构造注入解析）：Account 半边零引擎依赖（L1 源链接直测）。
            context.Put<IAccountSessionFactory>(new AccountSessionFactory(
                context.RootScope, context.Services, transport.Create, identity.Handshake));
            var matchSessions = new MatchSessionFactory(
                context.Require<IInputService>(), gameplayDigestBase, maps,
                context.Require<IReferencePool>(), identity.Diagnostics);
            context.Put<IMatchSessionFactory>(matchSessions);
            // 流程段只拿对局运行端口（不构造 Match/Battle 会话）。
            context.Put<IBattleRunFactory>(new BattleRunFactory(matchSessions, context.Require<IDevSession>()));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
