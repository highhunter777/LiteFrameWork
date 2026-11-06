using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MetaServer.Contracts.Auth;
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
        /// 构造宿主。
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
            UseGuestLoginRateLimit(app);          // 未鉴权写面的兜底限流（游客登录）——在端点之前
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
        /// 游客登录限流（未鉴权写面的兜底）：**每源 IP 每分钟**许可数走配置 （<see cref="MetaConfig.GuestLoginPerMinute"/>；0 = 关闭）。
        /// </summary>
        private static void UseGuestLoginRateLimit(WebApplication app)
        {
            MetaConfig effective = app.Services.GetRequiredService<IOptions<MetaConfig>>().Value;
            if (effective.GuestLoginPerMinute <= 0) return;      // 0 = 关闭（显式配置）

            var limiter = new FixedWindowRateLimiter(effective.GuestLoginPerMinute, windowMs: 60_000,
                capacity: GuestRateLimitKeyCapacity, nowMs: () => Environment.TickCount64);

            app.Use(async (context, next) =>
            {
                if (HttpMethods.IsPost(context.Request.Method)
                    && string.Equals(context.Request.Path.Value, AuthEndpoints.GuestLoginPath, StringComparison.Ordinal))
                {
                    string key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    if (!limiter.TryAcquire(key, out long retryAfterMs))
                    {
                        context.Response.Headers.RetryAfter =
                            ((retryAfterMs + 999) / 1000).ToString(CultureInfo.InvariantCulture);
                        await MetaResults.WriteAsync(context,
                            MetaResults.Error(AuthErrorCodes.RateLimited, AuthErrorCodes.MessageKeyRateLimited),
                            StatusCodes.Status429TooManyRequests);
                        return;
                    }
                }
                await next();
            });
        }

        /// <summary>限流键表容量（防"源地址可枚举 ⇒ 键表当内存放大器"；超出且无可回收键时拒新键）。</summary>
        private const int GuestRateLimitKeyCapacity = 4096;

        /// <summary>
        /// 请求计数中间件（§11.2）。
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
        /// 优雅关闭（§10 四步）。
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
        /// 命令行覆盖解析（§10"命令行只用于本地覆盖"）。
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
