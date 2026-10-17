using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MetaServer
{
    /// <summary>
    /// Meta 宿主装配（《Meta 服务专项设计》§4.1/§4.2、服务端总设计 §12）。
    ///
    /// 本宿主只做**编排**：选项接线段归 <see cref="MetaHostOptions"/>、模块注册归
    /// <see cref="MetaServiceRegistration"/>；本文件保留公共装配入口、中间件与端点挂载序、
    /// 优雅关闭与命令行解析（《服务端宿主结构拆分专项设计》批 7A）。
    ///
    /// 与 RoomServer 的关系：两条线共用同一套 Host/Options/`IHostedService`/Cancellation/优雅关闭
    /// 纪律（§4.1 依据 2）；`RoomServer.Host` 的 Generic Host 化归 R2，届时可反向参照本文件。
    /// </summary>
    public static class MetaHost
    {
        /// <summary>供日志与产物标识的构建身份（§P0-5）= 本工程源集摘要，**生成物**
        /// （`Host/BuildHash.g.cs`，由 `scripts/codegen/gen-build-hash.py` 产出——P1④ 取代旧手写占位）。
        /// 与两端握手红线的 <c>LiteNet.BuildHash</c> 不是一回事：本值不参与任何准入判定。</summary>
        public const string BuildIdentity = BuildHash.Value;

        /// <summary>配置文件中的配置节名（§10 版本化文件 / 环境变量）。</summary>
        public const string ConfigSection = "Meta";

        /// <summary>
        /// 构造宿主。**测试也从这里装配**——测试与生产共用同一装配路径，不允许测试另建一套
        /// （否则测的不是交付物）。
        ///
        /// 配置优先级（后者覆盖前者）：代码内联默认值 → `appsettings.json` → `META_` 环境变量
        /// → <paramref name="overrides"/>（命令行/测试，§10"命令行只用于本地覆盖"）；
        /// 来源与 Options 接线在 <see cref="MetaHostOptions"/>，模块注册在 <see cref="MetaServiceRegistration"/>。
        /// </summary>
        public static WebApplication Build(string[] args, IReadOnlyDictionary<string, string> overrides = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
            MetaHostOptions.Configure(builder, overrides);

            // 功能门只决定是否注册真实存储/业务服务（消费值仍从 IOptions 读取），
            // 故门读在装配前取一次并传给注册段。
            MetaConfig boot = MetaHostOptions.ReadBootConfig(builder.Configuration);
            MetaServiceRegistration.AddMetaServices(builder.Services, boot);

            WebApplication app = builder.Build();

            // 监听地址由 IOptions 解析（§10：不得从另建的实例读，否则配置文件覆盖不到）
            MetaHostOptions.ApplyListenAddress(app);

            app.Use(CountRequestsAsync);
            // 挂载形态统一（P2 批二）：健康端点与业务端点同走 XxxEndpoints.Map——
            // 宿主只留挂载序，不再有"私有方法挂载的一类端点"。
            HealthEndpoints.Map(app);
            // 样例链路默认不挂载（P3：出生产构建）——未显式开启时该路径**不存在**（404），
            // 不留"能打进来但必然 503"的误导面（与用例注册同一门：boot 门读 + 同一配置节）。
            if (boot.EnableSampleEndpoints) SampleEndpoints.Map(app);
            AuthEndpoints.Map(app);
            LobbyEndpoints.Map(app);
            SettlementEndpoints.Map(app);
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
            ops.CountRequest();
            try
            {
                await next();
            }
            finally
            {
                if (context.Response.StatusCode >= 400) ops.CountRejected();
            }
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
                ops.BeginDrain();
                logger.LogInformation("[MetaServer] drain：readiness 置 false，等待在途请求完成（时限 {Seconds}s）",
                    app.Services.GetRequiredService<IOptions<MetaConfig>>().Value.ShutdownTimeoutSeconds);
            });
        }

        /// <summary>
        /// 命令行覆盖解析（§10"命令行只用于本地覆盖"）。**未知参数/缺值 = 配置错误**（error 非空，
        /// 退出码 2）——静默忽略拼错的覆盖项会让"以为改了配置"变成假象，与 fail-closed 口径矛盾
        /// （硬化批 2026-10-03）。独立成静态方法供 L1 直测。
        /// </summary>
        public static IReadOnlyDictionary<string, string> ParseCliOverrides(string[] args, out string error)
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--bind":
                        if (i + 1 >= args.Length)
                        {
                            error = "--bind 缺少值（用法：--bind <url>）";
                            return overrides;
                        }
                        overrides[ConfigSection + ":BindAddress"] = args[++i];
                        break;
                    default:
                        error = "未知参数：" + args[i] + "（支持：--bind <url>）";
                        return overrides;
                }
            }
            return overrides;
        }
    }
}
