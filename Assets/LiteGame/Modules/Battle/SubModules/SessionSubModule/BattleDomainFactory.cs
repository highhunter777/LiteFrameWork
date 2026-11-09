using System;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteGame.Modules.Input;

namespace LiteGame
{

    /// <summary>
    /// Match 域的所有权载体（Services = 服务域）。
    /// </summary>
    public sealed class BattleDomain : IDisposable
    {
        public ServiceContainer Services { get; }
        public BattleContext Context { get; }
        /// <summary>会话客户端端口（Battle 域读口；<c>BattleSession</c> 经此取 StartGame 等待/地图行键/房间门面）。</summary>
        internal IBattleClient BattleClient => Context.DebugBattle;
        /// <summary>本局事件总线（Match Scope 独占；Dispose 随服务域释放，清空局内订阅）。</summary>
        public IEventBus Events { get; }
        private bool _disposed;

        public BattleDomain(ServiceContainer services, BattleContext context, IMatchEventBus events)
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

    /// <summary>默认 Match 域工厂（输入服务为根域常驻——跨对局复用，随域声明进工厂依赖）。
    /// 对局材料只经 <see cref="IMatchRuntime"/> 端口取：Match 子域
    /// （<see cref="IMatchRuntime.CreateMatchServices"/>）、客户端端口（<see cref="IMatchRuntime.Client"/>）、
    /// 本局规则与地图委托——不认会话/地图域实现件。</summary>
    public sealed class BattleDomainFactory : IBattleDomainFactory
    {
        private readonly IInputService _input;
        private readonly Func<uint> _gameplayDigestBase;
        private readonly DiagnosticIdentity _diagnostics;

        /// <param name="gameplayDigestBase">本地装载产物的玩法数据身份基摘要提供者
        /// （装配点绑定 `ConfigService.GameplayDigestBase`——**惰性读**:本工厂在 Preload 之前构造，
        /// 首次取值发生在 Create（进房，恒在装载之后）；经 BattleContext 校验 `StartGame.ConfigHash`，
        /// 见该类批 E 校验点）。</param>
        public BattleDomainFactory(IInputService input, Func<uint> gameplayDigestBase,
            DiagnosticIdentity diagnostics)
        {
            _input = input;
            _gameplayDigestBase = gameplayDigestBase ?? throw new ArgumentNullException(nameof(gameplayDigestBase));
            _diagnostics = diagnostics;
        }

        public BattleDomain Create(IMatchRuntime account, LiteSim.MatchTestRules rules = null)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            ServiceContainer matchServices = null;
            try
            {
                // Match 子域由 Account 会话建（未 Seal——本工厂完成登记后 Seal）。
                matchServices = account.CreateMatchServices();
                var matchEvents = new EventBus(strictMode: DevBuildFacts.IsDevelopment);
                matchServices.RegisterInstance<IMatchEventBus>(matchEvents, ServiceOwnership.ContainerOwned);
                IBattleClient battle = account.Client;
                // 多图批：本局判定图行键取 **JoinAck 权威事实**（0 = 未标注 → 首图口径，
                // 单源裁决经 IMatchRuntime.ResolveMapId）——与 Match 加载的地图场景同一行键（视觉=判定同图）。
                int mapId = account.ResolveMapId(battle.Client.MapId);
                uint digestBase = _gameplayDigestBase();
                var context = new BattleContext(battle, account.Scope, _input,
                    account.Maps(mapId),      // 判定图经端口委托取（Battle 不认 Map 域类型）
                    gameplayDigestBase: digestBase,
                    rules: rules,             // 每局规则随会话显式注入（无静态"当前局"）
                    events: matchEvents,      // 局内事件总线随 Match Scope 释放
                    matchIdentity: new GameplayMatchIdentity(digestBase),
                    diagnosticsIdentity: _diagnostics);
                matchServices.RegisterInstance<BattleContext>(context, ServiceOwnership.ContainerOwned);
                matchServices.Seal();
                return new BattleDomain(matchServices, context, matchEvents);
            }
            catch
            {
                matchServices?.Dispose();
                throw;
            }
        }
    }
}
