using System;
using LiteFramework;
using LiteGame.Modules.Config;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Map;
using LiteNet.Transport;
using LiteSim;
using LiteGame.Modules.Meta.Config;
using LiteGame.Modules.Dev.Abstractions;

namespace LiteGame
{
    /// <summary>
    /// 传输工厂实现（ITransportFactory）：每局按"部署快照 + 本局规则 + 本局判定数值"产一条传输。
    /// </summary>
    internal sealed class ProductTransportFactory : ITransportFactory
    {
        private readonly Func<bool, MatchTransport> _factory;

        /// <param name="config">表装载面（本局判定数值的表值来源——<see cref="IConfigService.TableCombatValues"/>）。</param>
        /// <param name="simParams">判定参数覆写面的取件闭包（覆写实例在装配末段落容器，取件推迟到进房时点）。</param>
        internal ProductTransportFactory(MetaEntryConfig deploy, Func<uint> gameplayDigestBase,
            IMapProvider maps, IDevSession dev, IConfigService config, Func<SimParamsOverrides> simParams)
        {
            if (deploy == null) throw new ArgumentNullException(nameof(deploy));
            if (gameplayDigestBase == null) throw new ArgumentNullException(nameof(gameplayDigestBase));
            if (maps == null) throw new ArgumentNullException(nameof(maps));
            if (config == null) throw new ArgumentNullException(nameof(config));

            Func<bool, MatchTransport> inner = testRoom =>
            {
                LiteSim.MatchTestRules rules = MatchRulesSeeder.Seed(testRoom, dev);
                CombatValues roomValues = ResolveRoomValues(testRoom, dev, config, simParams);
                return new MatchTransport(
                    CreateTransport(testRoom, rules, deploy, gameplayDigestBase, maps, roomValues,
                        config.Strategy.Rules), rules);
            };
            // 弱网注入装饰器（开发形态）：只包传输本身，本局规则原样带过（开发端口不认识会话域类型）。
            _factory = testRoom =>
            {
                MatchTransport match = inner(testRoom);
                IDevAssembly dev = DevHooks.Current;
                if (dev == null || match.Transport == null) return match;
                IClientTransport wrapped = dev.WrapTransport(testRoom, match.Transport);
                return ReferenceEquals(wrapped, match.Transport)
                    ? match
                    : new MatchTransport(wrapped, match.Rules);
            };
        }

        public MatchTransport Create(bool testRoom) => _factory(testRoom);

        /// <summary>
        /// 本局判定数值（**每会话求值一次**，就在创建传输之前）：表值 → 门 → 覆写合并。
        /// </summary>
        private static CombatValues ResolveRoomValues(bool testRoom, IDevSession dev, IConfigService config,
            Func<SimParamsOverrides> simParams)
        {
            CombatValues table = config.TableCombatValues;
            bool localTestRoom = testRoom && DevBuildFacts.IsDevelopment;
            bool testModeActive = dev != null && dev.Active;
            bool applyOverrides = SimParamsGate.Evaluate(testModeActive, localTestRoom);
            CombatValues active = SimParamsActivation.Resolve(applyOverrides, table, simParams?.Invoke());
            CombatConfig.Publish(active);
            return active;
        }

        /// <summary>
        /// 传输裁决（产品形态）：本地服（开发装配 + 测试房）走本地传输工厂，否则走安全信封 + KCP。
        /// </summary>
        internal static LiteNet.Transport.IClientTransport CreateTransport(bool testRoom,
            LiteSim.MatchTestRules rules, MetaEntryConfig deploy = null, Func<uint> gameplayDigestBase = null,
            IMapProvider maps = null, LiteSim.CombatValues? roomValues = null,
            LiteSim.MatchRules? roomRules = null)
        {
            if (testRoom && DevBuildFacts.IsDevelopment)
            {
                ILocalTransportFactory local = DevHooks.Current?.LocalTransport;
                if (local == null)
                    throw new InvalidOperationException(
                        "本地测试模式缺本地传输工厂——LiteGame.Dev 未装配（DevHooks 未绑定）");
                if (roomValues == null)
                    throw new InvalidOperationException(
                        "本地测试模式缺本局判定数值——进房单点须先按 SimParamsActivation 解析（表值/覆写合并）再创建传输");
                return local.Create(testRoom, rules, roomValues.Value, gameplayDigestBase, maps, roomRules);
            }

            return new LiteNet.Transport.SecureEnvelopeClientTransport(
                new LiteNet.Transport.KcpTransportClient(),
                string.IsNullOrEmpty(deploy?.EnvelopeKeyBase64)
                    ? LiteNet.Transport.SecureEnvelopeOptions.FromEnvironment()   // 既有口（部署侧 env 注入）
                    : LiteNet.Transport.SecureEnvelopeOptions.FromBase64(deploy.EnvelopeKeyBase64));  // -envelope.key 优先
        }
    }
}
