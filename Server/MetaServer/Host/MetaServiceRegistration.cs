using System;
using MetaServer.Contracts.Auth;
using MetaServer.Contracts.Persistence;
using MetaServer.Infrastructure.Persistence;
using MetaServer.Infrastructure.Persistence.Mongo;
using MetaServer.Modules.Auth;
using MetaServer.Modules.Lobby;
using MetaServer.Modules.Profile;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace MetaServer
{
    /// <summary>
    /// Meta 宿主装配·**模块注册段**（《服务端宿主结构拆分专项设计》批 7A）。
    ///
    /// 功能门随块走：每个模块的"配置齐全才注册"判据与该模块的注册体同处一个方法——
    /// 宿主不再持有一张横跨 Auth/Lobby/持久化的装配清单。门读值由调用方传入
    /// （<see cref="MetaHostOptions.ReadBootConfig"/>），**消费值仍一律经 IOptions**。
    /// </summary>
    internal static class MetaServiceRegistration
    {
        /// <summary>观测面 + 三个功能模块，按依赖方向装配（观测面无依赖，恒注册）。</summary>
        internal static void AddMetaServices(IServiceCollection services, MetaConfig boot)
        {
            // ---- 观测面（§11.2）：进程级单例，计数就地累加 ----
            services.AddSingleton<Ops>();

            AddAuthServices(services, boot);
            AddLobbyServices(services, boot);
            AddPersistenceServices(services, boot);
        }

        /// <summary>Auth 首批（配置密钥才启用；真实账号存储在 Mongo 装配块内）。</summary>
        private static void AddAuthServices(IServiceCollection services, MetaConfig boot)
        {
            if (string.IsNullOrWhiteSpace(boot.AuthSigningKeyBase64)) return;

            services.AddSingleton(sp =>
            {
                MetaConfig meta = sp.GetRequiredService<IOptions<MetaConfig>>().Value;
                return new AccessTokenService(
                    Convert.FromBase64String(meta.AuthSigningKeyBase64),
                    meta.AuthKeyId,
                    meta.AuthAudience,
                    meta.AuthTokenTtlSeconds,
                    () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            });
        }

        /// <summary>Lobby（R3：实例注册 + Join Ticket 签发；配置密钥才启用，消费值同走 IOptions）。
        /// 两把密钥成对配置才启用（半段配置＝未启用，校验器负责在成对时拒绝非法值）。</summary>
        private static void AddLobbyServices(IServiceCollection services, MetaConfig boot)
        {
            bool lobbyConfigured = !string.IsNullOrWhiteSpace(boot.LobbyInstanceKeyBase64)
                && !string.IsNullOrWhiteSpace(boot.LobbyTicketKeyBase64);
            if (!lobbyConfigured) return;

            services.AddSingleton(sp =>
            {
                MetaConfig meta = sp.GetRequiredService<IOptions<MetaConfig>>().Value;
                return new InstanceRegistry(
                    meta.LobbyRegistryCapacity,
                    meta.LobbyHeartbeatTtlSeconds * 1000L,
                    () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            });
            services.AddSingleton(sp =>
            {
                MetaConfig meta = sp.GetRequiredService<IOptions<MetaConfig>>().Value;
                return new LobbyTicketSigner(
                    meta.LobbyTicketKeyId,
                    Convert.FromBase64String(meta.LobbyTicketKeyBase64),
                    meta.LobbyTicketAudience);
            });
            services.AddSingleton(sp =>
            {
                MetaConfig meta = sp.GetRequiredService<IOptions<MetaConfig>>().Value;
                return new JoinTicketIssueUseCase(
                    sp.GetRequiredService<InstanceRegistry>(),
                    sp.GetRequiredService<LobbyTicketSigner>(),
                    meta.LobbyDefaultRoomId,
                    meta.LobbyTicketTtlSeconds * 1000L,
                    () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    // 测试房口令：只走机器级环境变量（不入配置文件/仓库）。空 = 测试房功能关闭。
                    Environment.GetEnvironmentVariable("LITEGAME_GM_ROOM_KEY"));
            });
            services.AddSingleton(sp => new CreateRoomUseCase(
                sp.GetRequiredService<JoinTicketIssueUseCase>(),
                sp.GetRequiredService<InstanceRegistry>(),
                () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        /// <summary>
        /// 持久化装配（M0-c 批二）。功能门：配置了 Mongo 连接串才注册（§10"缺真实依赖拒绝启动或
        /// 拒绝相应功能"——本处取后者：功能关闭＝样例端点 503、/ready 不检存储）。
        /// </summary>
        private static void AddPersistenceServices(IServiceCollection services, MetaConfig boot)
        {
            if (string.IsNullOrWhiteSpace(boot.MongoConnectionString)) return;

            services.AddSingleton(sp =>
            {
                MetaConfig meta = sp.GetRequiredService<IOptions<MetaConfig>>().Value;
                MongoClientSettings settings =
                    MongoClientSettings.FromUrl(MongoUrl.Create(meta.MongoConnectionString));
                settings.ServerSelectionTimeout =
                    TimeSpan.FromMilliseconds(meta.MongoServerSelectionTimeoutMs);
                return (IMongoClient)new MongoClient(settings);
            });
            services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>()
                .GetDatabase(sp.GetRequiredService<IOptions<MetaConfig>>().Value.MongoDatabaseName));
            services.AddSingleton<ISettlementLedger>(sp =>
                new MongoSettlementLedger(sp.GetRequiredService<IMongoDatabase>()));
            services.AddSingleton<IAccountStore>(sp =>
                new MongoAccountStore(
                    sp.GetRequiredService<IMongoDatabase>(),
                    () => DateTime.UtcNow));
            // **Outbox/Inbox 端口「装配退役」（P3）**：`IOutboxStore` + `MongoOutboxStore` 是 G1 的
            // "只建接缝"交付物（Meta 专项 §14-G1：必要存储端口 + 迁移/幂等约束 + 故障夹具），
            // 端口契约与实现均已就绪、L1（内存替身）+ L3（真 Mongo，含重启/唯一索引）已实证——
            // 但**当前没有任何生产消费者**：Meta 的结算路径是"事务内落账 + 由 RoomServer 侧 Outbox
            // 承担重试"，Meta 自身没有外发写，因此不需要自己的 Outbox。
            // 一个注册了却没人解析的服务，会让人以为该功能已在跑（审计 P3 点名的"死装配"），
            // 故**不在此注册**。G3 接入 Meta 外发写时：此处补一行 `AddSingleton<IOutboxStore>`，
            // 并在 §10 优雅关闭第 3 步（刷新 Outbox）与 `/ready`（Outbox 未超阈）处接线。
            services.AddSingleton<ISchemaVersionStore, MongoSchemaVersionStore>();
            services.AddSingleton<ISchemaMigrator>(sp =>
                new SchemaMigrationRunner(
                    MongoMigrations.All(sp.GetRequiredService<IMongoDatabase>()),
                    sp.GetRequiredService<ISchemaVersionStore>()));
            services.AddSingleton<IMatchResultArchive>(sp =>
                new MongoMatchResultArchive(
                    sp.GetRequiredService<IMongoDatabase>(),
                    () => DateTime.UtcNow));
            services.AddSingleton(sp => new ApplyMatchResultUseCase(
                sp.GetRequiredService<ISettlementLedger>(),
                sp.GetRequiredService<IMatchResultArchive>()));
            // 样例链路（P3：出生产构建）——缺省不装配（`EnableSampleEndpoints` 缺省 false）：
            // 开启才注册用例，配合端点门控，整条链路在生产配置里不存在。
            if (boot.EnableSampleEndpoints) services.AddSingleton<SettlementSampleUseCase>();
            services.AddHostedService<MigrationStartupService>();

            if (!string.IsNullOrWhiteSpace(boot.AuthSigningKeyBase64))
            {
                services.AddSingleton(sp => new GuestLoginUseCase(
                    sp.GetRequiredService<IAccountStore>(),
                    sp.GetRequiredService<AccessTokenService>(),
                    () => DateTime.UtcNow));
            }
        }
    }
}
