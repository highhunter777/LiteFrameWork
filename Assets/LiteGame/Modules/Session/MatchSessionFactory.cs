using System;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;

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
        /// 失败就地回滚（服务域 Dispose 后重抛）。<paramref name="rules"/>＝本局规则持有件（与权威侧同实例；null = 常规房）。</summary>
        MatchSession Create(AccountSession account, LiteSim.MatchTestRules rules = null);
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
        /// <summary>会话客户端（会话域读口；<c>BattleSession</c> 经此取 StartGame 等待/地图行键/房间门面）。</summary>
        internal BattleClient BattleClient => Context.DebugBattle;
        /// <summary>本局事件总线（Match Scope 独占；Dispose 随服务域释放，清空局内订阅）。</summary>
        public IEventBus Events { get; }
        private bool _disposed;

        public MatchSession(ServiceContainer services, BattleContext context, IMatchEventBus events)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Events = events ?? throw new ArgumentNullException(nameof(events));
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
        private readonly Func<uint> _gameplayDigestBase;
        private readonly IMapProvider _maps;
        private readonly IReferencePool _pool;
        private readonly DiagnosticIdentity _diagnostics;

        /// <param name="gameplayDigestBase">本地装载产物的玩法数据身份基摘要提供者
        /// （装配点绑定 `ConfigService.GameplayDigestBase`——**惰性读**:本工厂在 Preload 之前构造，
        /// 首次取值发生在 Create（进房，恒在装载之后）；经 BattleContext 校验 `StartGame.ConfigHash`，
        /// 见该类批 E 校验点）。</param>
        /// <param name="maps">地图提供者（D9）：进房装配点经它取判定图传给 BattleContext（D22：无回退地图；
        /// 取值同刻于 Create——恒在装载之后；表缺 Map 域即抛）。</param>
        public MatchSessionFactory(IInputService input, Func<uint> gameplayDigestBase, IMapProvider maps,
            IReferencePool pool, DiagnosticIdentity diagnostics)
        {
            _input = input;
            _gameplayDigestBase = gameplayDigestBase ?? throw new ArgumentNullException(nameof(gameplayDigestBase));
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _diagnostics = diagnostics;
        }

        public MatchSession Create(AccountSession account, LiteSim.MatchTestRules rules = null)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            ServiceContainer matchServices = null;
            try
            {
                matchServices = (ServiceContainer)account.Services.CreateScope("Match");
                var matchEvents = new EventBus(_pool, strictMode: DevBuildFacts.IsDevelopment);
                matchServices.RegisterInstance<IMatchEventBus>(matchEvents, ServiceOwnership.ContainerOwned);
                matchServices.RegisterFactory<BattleContext>(
                    c =>
                    {
                        var battle = c.Resolve<BattleClient>();
                        // 多图批：本局判定图行键取 **JoinAck 权威事实**（0 = 未标注 → 首图口径，
                        // 单源裁决见 BattleScenes.ResolveMapId）——与 Match 加载的地图场景同一行键（视觉=判定同图）。
                        int mapId = BattleScenes.ResolveMapId(battle.Client.MapId);
                        uint digestBase = _gameplayDigestBase();
                        return new BattleContext(battle, account.Scope, _input,
                            _maps.GetMap(mapId),
                            gameplayDigestBase: digestBase,
                            rules: rules,             // 每局规则随会话显式注入（无静态"当前局"）
                            events: matchEvents,      // 局内事件总线随 Match Scope 释放
                            matchIdentity: new GameplayMatchIdentity(digestBase),
                            diagnosticsIdentity: _diagnostics);
                    },
                    ServiceLifetime.Scoped, typeof(BattleClient));
                matchServices.Seal();
                var context = matchServices.Resolve<BattleContext>();
                return new MatchSession(matchServices, context, matchEvents);
            }
            catch
            {
                matchServices?.Dispose();
                throw;
            }
        }
    }
}
