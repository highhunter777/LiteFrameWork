using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Config;
using LiteGame.Modules.Map;
using LiteSim;

namespace LiteGame
{
    /// <summary>⑰ 会话：账号会话工厂＋传输工厂端口（账号所有权归本段；对局运行归 ⑯ Battle）。
    /// 账号半边跨局复用；Match/Battle 会话与运行工厂由 <see cref="BattleSegment"/> 装配。
    /// 依赖 ⑯ Battle：客户端端口 <see cref="IBattleClientFactory"/> 归 Battle 域（会话不 `new` 客户端）。</summary>
    public sealed class SessionSegment : IClientSegment, IClientSegmentGraph
    {
        public string Name => "Session";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure", "Identity", "DevSession", "Content", "Config", "Map", "Meta", "Battle",
        };

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            IConfigService config = context.Require<IConfigService>();
            IdentityContext identity = context.Require<IdentityContext>();

            // 玩法数据身份基摘要提供者：装配期只装**提供者**（本段早于 Preload 装载配置——
            // 取值点=进房/本地服建房，恒在装载之后）；两端摘要由构造同源保证。
            Func<uint> gameplayDigestBase = () => config.GameplayDigestBase;
            IMapProvider maps = context.Require<IMapProvider>();

            // 判定参数覆写面在装配末段（组合根 <c>BindSimParams</c>）才落容器——本段只捕获**取件闭包**，
            // 求值推迟到进房时点（ProductTransportFactory 每局一次：门须在本地测试模式真的开启后才为真）。
            ServiceContainer services = context.Services;
            Func<SimParamsOverrides> simParams = () =>
                services.TryResolve(out SimParamsOverrides value) ? value : null;

            var transport = new ProductTransportFactory(
                context.Require<MetaEntryConfig>(), gameplayDigestBase, maps, context.Require<IDevSession>(),
                config, simParams);
            context.Put<ITransportFactory>(transport);

            // 客户端端口工厂（⑯ Battle 注册；装配序即依赖序）——会话域不 `new` 客户端具体件。
            IBattleClientFactory battleClients = context.Require<IBattleClientFactory>();
            // 地图委托：判定图与场景行键同键（LiteSim 类型过端口——Account 工厂不依赖 Map 域类型）。
            Func<int, SimMapData> mapsFn = id => maps.GetMap(id);
            Func<int, int> resolveMapId = BattleScenes.ResolveMapId;

            // 注册面＝服务接口（消费者经构造注入解析）：Account 半边零引擎依赖（L1 源链接直测）。
            context.Put<IMatchSessionFactory>(new MatchSessionFactory(
                context.RootScope, transport.Create, identity.Handshake,
                battleClients, mapsFn, resolveMapId,
                () =>
                {
                    IAccountDomain account = context.Get<IAccountDomain>();
                    return account != null && account.IsOpen ? account.Scope : context.RootScope;
                }));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
