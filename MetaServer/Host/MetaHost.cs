using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Infrastructure.Persistence;
using MetaServer.Infrastructure.Persistence.Mongo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MetaServer
{
    /// <summary>
    /// Meta 宿主装配（《Meta 服务专项设计》§4.1/§4.2、服务端总设计 §12）。
    ///
    /// 本批为**宿主骨架**（§15 施工映射的 G1 行）：Generic Host、Options 校验、健康检查、
    /// 优雅关闭与 drain。**不含任何业务模块**——Auth/Lobby/Profile 归 G3，不提前实现空壳模块。
    ///
    /// 与 RoomServer 的关系：两条线共用同一套 Host/Options/`IHostedService`/Cancellation/优雅关闭
    /// 纪律（§4.1 依据 2）；`RoomServer.Host` 的 Generic Host 化归 R2，届时可反向参照本文件。
    /// </summary>
    public static class MetaHost
    {
        /// <summary>供日志与产物标识的构建身份（§P0-5）。</summary>
        public const string BuildIdentity = BuildHash.Value;

        /// <summary>配置文件中的配置节名（§10 版本化文件 / 环境变量）。</summary>
        public const string ConfigSection = "Meta";

        /// <summary>
        /// 构造宿主。**测试也从这里装配**——测试与生产共用同一装配路径，不允许测试另建一套
        /// （否则测的不是交付物）。
        ///
        /// 配置优先级（后者覆盖前者）：代码内联默认值 → `appsettings.json` → `META_` 环境变量
        /// → <paramref name="overrides"/>（命令行/测试，§10"命令行只用于本地覆盖"）。
        ///
        /// **单一来源**：配置只由 Options 管线持有一份，消费方一律经 `IOptions&lt;MetaConfig&gt;` 取用；
        /// 校验器校验的正是这同一实例。任何"另注册一个 MetaConfig 单例"的写法都会造成
        /// 两套实例、校验器验的是没人用的对象（2026-09-25 实测缺陷，见 <see cref="MetaConfig"/> 注释）。
        /// </summary>
        public static WebApplication Build(string[] args, IDictionary<string, string> overrides = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());

            // ---- 配置来源（§10）----
            string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables("META_");
            if (overrides != null) builder.Configuration.AddInMemoryCollection(overrides);

            // ---- Options：绑定 + 范围校验 + ValidateOnStart（§10）----
            builder.Services.AddSingleton<IValidateOptions<MetaConfig>, MetaConfigValidator>();
            builder.Services.AddOptions<MetaConfig>()
                .Bind(builder.Configuration.GetSection(ConfigSection))
                .ValidateOnStart();

            // ---- 入站请求体上限（§5 P0-3"包体在解析前限制长度"）----
            // 必须在解析前拒绝，故同时落 Kestrel 层与表单层。
            builder.Services.AddOptions<KestrelServerOptions>()
                .Configure<IOptions<MetaConfig>>((kestrel, meta) =>
                    kestrel.Limits.MaxRequestBodySize = meta.Value.MaxInboundBytes);
            builder.Services.AddOptions<FormOptions>()
                .Configure<IOptions<MetaConfig>>((form, meta) =>
                    form.MultipartBodyLengthLimit = meta.Value.MaxInboundBytes);

            // ---- 关闭时限（§10 优雅关闭）。默认 30s，须由配置决定而非写死。----
            builder.Services.AddOptions<HostOptions>()
                .Configure<IOptions<MetaConfig>>((host, meta) =>
                    host.ShutdownTimeout = TimeSpan.FromSeconds(meta.Value.ShutdownTimeoutSeconds));

            // ---- 观测面（§11.2）：进程级单例，计数就地累加 ----
            builder.Services.AddSingleton<Ops>();

            // ---- 持久化装配（M0-c 批二）----
            // 功能门：配置了 Mongo 连接串才注册（§10"缺真实依赖拒绝启动或拒绝相应功能"——
            // 本处取后者：功能关闭＝样例端点 503、/ready 不检存储）。**门读**用 ConfigurationBinder
            // 绑同一节，**消费值**一律经 IOptions（避免 2026-09-25 实测过的双实例失效——见 MetaConfig 注释）。
            MetaConfig boot = builder.Configuration.GetSection(ConfigSection).Get<MetaConfig>() ?? new MetaConfig();
            if (!string.IsNullOrWhiteSpace(boot.MongoConnectionString))
            {
                builder.Services.AddSingleton(sp =>
                {
                    MetaConfig meta = sp.GetRequiredService<IOptions<MetaConfig>>().Value;
                    MongoClientSettings settings =
                        MongoClientSettings.FromUrl(MongoUrl.Create(meta.MongoConnectionString));
                    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                    return (MongoDB.Driver.IMongoClient)new MongoClient(settings);
                });
                builder.Services.AddSingleton(sp => sp.GetRequiredService<MongoDB.Driver.IMongoClient>()
                    .GetDatabase(sp.GetRequiredService<IOptions<MetaConfig>>().Value.MongoDatabaseName));
                builder.Services.AddSingleton<MetaServer.Contracts.Persistence.ISettlementLedger>(sp =>
                    new MongoSettlementLedger(sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>()));
                builder.Services.AddSingleton<MetaServer.Contracts.Persistence.IOutboxStore>(sp =>
                    new MongoOutboxStore(
                        sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>(),
                        sp.GetRequiredService<IOptions<MetaConfig>>().Value.OutboxCapacity));
                builder.Services.AddSingleton<MetaServer.Contracts.Persistence.ISchemaVersionStore, MongoSchemaVersionStore>();
                builder.Services.AddSingleton<MetaServer.Contracts.Persistence.ISchemaMigrator>(sp =>
                    new SchemaMigrationRunner(
                        MongoMigrations.All(sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>()),
                        sp.GetRequiredService<MetaServer.Contracts.Persistence.ISchemaVersionStore>()));
                builder.Services.AddSingleton<SettlementSampleUseCase>();
                builder.Services.AddHostedService<MigrationStartupService>();
            }

            WebApplication app = builder.Build();

            // 监听地址由 IOptions 解析（§10：不得从另建的实例读，否则配置文件覆盖不到）
            MetaConfig effective = app.Services.GetRequiredService<IOptions<MetaConfig>>().Value;
            app.Urls.Add(effective.BindAddress);

            app.Use(CountRequestsAsync);
            MapHealthEndpoints(app);
            SampleEndpoints.Map(app);
            return app;
        }

        /// <summary>
        /// 请求计数中间件（§11.2）。按响应状态码同时记成功与拒绝：
        /// <c>HttpRejected</c> 统计 4xx/5xx（含超限、未知路由），不是永远为 0 的占位字段。
        /// 真正的 <c>{route,code}</c> 分标签出口归 R4——此处不提前引入高基数标签（§11.2 红线）。
        /// </summary>
        private static async Task CountRequestsAsync(HttpContext context, Func<Task> next)
        {
            Ops ops = context.RequestServices.GetRequiredService<Ops>();
            ops.HttpRequests++;
            try
            {
                await next();
            }
            finally
            {
                if (context.Response.StatusCode >= 400) ops.HttpRejected++;
            }
        }

        /// <summary>
        /// 健康检查（§11.3）。
        ///
        /// 语义分工是硬要求：`/live` 只证进程事件循环仍能响应；`/ready` 证配置与依赖正常**且未处于 drain**。
        /// 二者不可合并——把 drain 混进 live 会让编排器误杀进程而不是摘流量。
        ///
        /// M0-c 批二起 `/ready` 包含真实依赖：配置了存储时 ping Mongo（§10"/ready 的判定必须包含
        /// 真实依赖（Mongo 可达）"）；功能关闭（未配置连接串）时无依赖可检，仍返回 ready。
        /// </summary>
        private static void MapHealthEndpoints(WebApplication app)
        {
            app.MapGet("/live", () => Results.Text("live", "text/plain"));

            app.MapGet("/ready", async Task<IResult> (HttpContext context, Ops ops) =>
            {
                if (ops.Draining)
                {
                    return Results.Text("draining", "text/plain",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                IMongoDatabase database = context.RequestServices.GetService<IMongoDatabase>();
                if (database != null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await database.RunCommandAsync(
                            new BsonDocumentCommand<BsonDocument>(new BsonDocument("ping", 1)),
                            cancellationToken: timeout.Token);
                    }
                    catch
                    {
                        return Results.Text("store-unreachable", "text/plain",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                }

                return Results.Text("ready", "text/plain");
            });

            app.MapGet("/metrics", (Ops ops) => Results.Text(ops.FormatMetrics(), "text/plain"));
        }

        /// <summary>
        /// 优雅关闭（§10 四步）。drain 由 Host 的 ApplicationStopping 触发：
        /// readiness 先置 false，再由 Host 在 <see cref="MetaConfig.ShutdownTimeoutSeconds"/> 内
        /// 完成在途请求。
        ///
        /// 业务侧的第 2～4 步（停止接受新的外部写、刷新 Outbox、停止后台服务）随模块接入，
        /// 不在此空转占位。
        /// </summary>
        public static void WireGracefulShutdown(WebApplication app)
        {
            IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            Ops ops = app.Services.GetRequiredService<Ops>();
            ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MetaServer.Shutdown");

            lifetime.ApplicationStopping.Register(() =>
            {
                ops.Draining = true;
                logger.LogInformation("[MetaServer] drain：readiness 置 false，等待在途请求完成（时限 {Seconds}s）",
                    app.Services.GetRequiredService<IOptions<MetaConfig>>().Value.ShutdownTimeoutSeconds);
            });
        }

        /// <summary>
        /// Option 校验适配：把 <see cref="MetaConfig.Validate"/> 接到 Generic Host 的 ValidateOnStart。
        /// 启动时扫描全部注册的 <see cref="IValidateOptions{T}"/>，任一项失败即阻止启动。
        /// </summary>
        private sealed class MetaConfigValidator : IValidateOptions<MetaConfig>
        {
            public ValidateOptionsResult Validate(string name, MetaConfig options)
            {
                var errors = options.Validate();
                return errors.Count == 0
                    ? ValidateOptionsResult.Success
                    : ValidateOptionsResult.Fail(errors);
            }
        }
    }
}
