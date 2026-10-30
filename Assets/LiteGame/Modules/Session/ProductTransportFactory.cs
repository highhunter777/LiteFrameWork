using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 传输工厂实现（<see cref="ITransportFactory"/>）：每局按"部署快照 + 本局规则"产一条传输。
    ///
    /// **每局规则播种是显式步骤**（<see cref="MatchRulesSeeder.Seed"/>）：规则实例随传输同刻产出，
    /// 经 AccountSession 显式移交给对局——传输工厂只接收规则实例，不理解测试开关语义（语义在播种器）。
    /// 开发形态（Dev 程序集在场）再套一层弱网装饰器：装饰的是"传输工厂"而不是传输本身（重连重建也走同一装饰）。
    /// </summary>
    internal sealed class ProductTransportFactory : ITransportFactory
    {
        private readonly Func<bool, MatchTransport> _factory;

        internal ProductTransportFactory(MetaEntryConfig deploy, Func<uint> gameplayDigestBase,
            IMapProvider maps, IDevSession dev)
        {
            if (deploy == null) throw new ArgumentNullException(nameof(deploy));
            if (gameplayDigestBase == null) throw new ArgumentNullException(nameof(gameplayDigestBase));
            if (maps == null) throw new ArgumentNullException(nameof(maps));

            Func<bool, MatchTransport> inner = testRoom =>
            {
                LiteSim.MatchTestRules rules = MatchRulesSeeder.Seed(testRoom, dev);
                return new MatchTransport(
                    CreateTransport(testRoom, rules, deploy, gameplayDigestBase, maps), rules);
            };
            _factory = DevHooks.Current?.WrapTransportFactory(inner) ?? inner;   // 弱网注入装饰器（开发形态）
        }

        public MatchTransport Create(bool testRoom) => _factory(testRoom);

        /// <summary>传输裁决（产品形态）：本地服（开发装配 + 测试房）走本地传输工厂，否则走安全信封 + KCP。
        /// 信封 PSK 注入口：<paramref name="deploy"/> 带 -envelope.key 时优先（构建期/启动参数注入）；
        /// 空 = 回落 LITENET_SECURE_ENVELOPE_KEY 环境变量既有口（两道注入口不互斥——与部署侧同 key）。</summary>
        internal static LiteNet.Transport.IClientTransport CreateTransport(bool testRoom,
            LiteSim.MatchTestRules rules, MetaEntryConfig deploy = null, Func<uint> gameplayDigestBase = null,
            IMapProvider maps = null)
        {
            if (testRoom && DevBuildFacts.IsDevelopment)
            {
                if (!LocalTransportRegistry.TryGet(out ILocalTransportFactory local))
                    throw new InvalidOperationException(
                        "本地测试模式缺本地传输工厂——LiteGame.Dev 未装配（开发程序集不在场/装订失败）");
                return local.Create(testRoom, rules, gameplayDigestBase, maps);
            }

            return new LiteNet.Transport.SecureEnvelopeClientTransport(
                new LiteNet.Transport.KcpTransportClient(),
                string.IsNullOrEmpty(deploy?.EnvelopeKeyBase64)
                    ? LiteNet.Transport.SecureEnvelopeOptions.FromEnvironment()   // 既有口（部署侧 env 注入）
                    : LiteNet.Transport.SecureEnvelopeOptions.FromBase64(deploy.EnvelopeKeyBase64));  // -envelope.key 优先
        }
    }
}
