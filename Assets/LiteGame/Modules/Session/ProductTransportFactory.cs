using System;
using LiteFramework;
using LiteGame.Modules.Config;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Map;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 传输工厂实现（<see cref="ITransportFactory"/>）：每局按"部署快照 + 本局规则 + 本局判定数值"产一条传输。
    ///
    /// **每局规则播种是显式步骤**（<see cref="MatchRulesSeeder.Seed"/>）：规则实例随传输同刻产出，
    /// 经 AccountSession 显式移交给对局——传输工厂只接收规则实例，不理解测试开关语义（语义在播种器）。
    /// **本局判定数值也在同一步解析**（<see cref="ResolveRoomValues"/>）：表值 → 门 → 覆写合并，
    /// 结果既是客户端预测读口（<see cref="CombatConfig.Publish"/>）也是本地服房间数值——两端逐字段同值。
    /// 开发形态（Dev 程序集在场）再套一层弱网装饰器：装饰的是"传输工厂"而不是传输本身（重连重建也走同一装饰）。
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
                    CreateTransport(testRoom, rules, deploy, gameplayDigestBase, maps, roomValues), rules);
            };
            _factory = DevHooks.Current?.WrapTransportFactory(inner) ?? inner;   // 弱网注入装饰器（开发形态）
        }

        public MatchTransport Create(bool testRoom) => _factory(testRoom);

        /// <summary>
        /// 本局判定数值（**每会话求值一次**，就在创建传输之前）：表值 → 门 → 覆写合并。
        ///
        /// 门的第二输入 = "本局走进程内本地服分支"（<c>testRoom ∧ DevBuildFacts.IsDevelopment</c>，
        /// 与 <see cref="CreateTransport"/> 的分支判据同一表达式）——联机/云端房因此恒为表值，
        /// 客户端覆写不会被带进真服。
        ///
        /// 结果发布给客户端单世界读口（`BattleContext.OnStartGame` 按它建预测世界）并随传输交给本地服；
        /// 非测试房发布的是表值（测试模式退出后下一局自然回到表值，无需额外回滚）。
        /// 覆写**不进任何摘要**：`CombatConfigDigest` 只看判定面表字节与本局规则位。
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

        /// <summary>传输裁决（产品形态）：本地服（开发装配 + 测试房）走本地传输工厂，否则走安全信封 + KCP。
        /// 信封 PSK 注入口：<paramref name="deploy"/> 带 -envelope.key 时优先（构建期/启动参数注入）；
        /// 空 = 回落 LITENET_SECURE_ENVELOPE_KEY 环境变量既有口（两道注入口不互斥——与部署侧同 key）。
        ///
        /// <paramref name="roomValues"/> = 本局判定数值（本地服建房用——与客户端发布面同一结果）；
        /// 本地分支缺失即抛：不允许本地服静默回落默认值而与客户端分叉。</summary>
        internal static LiteNet.Transport.IClientTransport CreateTransport(bool testRoom,
            LiteSim.MatchTestRules rules, MetaEntryConfig deploy = null, Func<uint> gameplayDigestBase = null,
            IMapProvider maps = null, LiteSim.CombatValues? roomValues = null)
        {
            if (testRoom && DevBuildFacts.IsDevelopment)
            {
                if (!LocalTransportRegistry.TryGet(out ILocalTransportFactory local))
                    throw new InvalidOperationException(
                        "本地测试模式缺本地传输工厂——LiteGame.Dev 未装配（开发程序集不在场/装订失败）");
                if (roomValues == null)
                    throw new InvalidOperationException(
                        "本地测试模式缺本局判定数值——进房单点须先按 SimParamsActivation 解析（表值/覆写合并）再创建传输");
                return local.Create(testRoom, rules, roomValues.Value, gameplayDigestBase, maps);
            }

            return new LiteNet.Transport.SecureEnvelopeClientTransport(
                new LiteNet.Transport.KcpTransportClient(),
                string.IsNullOrEmpty(deploy?.EnvelopeKeyBase64)
                    ? LiteNet.Transport.SecureEnvelopeOptions.FromEnvironment()   // 既有口（部署侧 env 注入）
                    : LiteNet.Transport.SecureEnvelopeOptions.FromBase64(deploy.EnvelopeKeyBase64));  // -envelope.key 优先
        }
    }
}
