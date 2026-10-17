using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MetaServer
{
    /// <summary>
    /// Meta 宿主装配·**选项接线段**（《服务端宿主结构拆分专项设计》批 7A）。
    ///
    /// 只持有三件事：日志出口、配置来源与其优先级、Options 管线（绑定＋范围校验＋ValidateOnStart）
    /// 及由配置决定的上限与时限。模块注册归 <see cref="MetaServiceRegistration"/>，
    /// 两端挂载序与运行时行为留 <see cref="MetaHost"/>。
    ///
    /// **单一来源不变**：配置只由 Options 管线持有一份，消费方一律经 <see cref="IOptions{TOptions}"/> 取用；
    /// 本类只搬运既有接线，不新建第二份配置实例（2026-09-25 实测缺陷的防线见 <see cref="MetaConfig"/> 注释）。
    /// </summary>
    internal static class MetaHostOptions
    {
        /// <summary>环境变量配置源的前缀（§10 优先级第三级：<c>META_</c> 环境变量）。</summary>
        internal const string EnvironmentVariablePrefix = "META_";

        /// <summary>版本化配置文件基名（随产物落在基目录）。</summary>
        private const string SettingsFileName = "appsettings.json";

        /// <summary>
        /// 日志 + 配置来源 + Options 管线一次装完（<see cref="MetaHost.Build"/> 的第一步，顺序即语义）。
        /// </summary>
        internal static void Configure(WebApplicationBuilder builder, IReadOnlyDictionary<string, string> overrides)
        {
            ConfigureLogging(builder);
            AddConfigurationSources(builder, overrides);
            BindOptions(builder);
        }

        /// <summary>
        /// 日志出口＝控制台。Generic Host 在 Windows 上可能自动装配 EventLog provider，非管理员进程
        /// 写入默认的 ".NET Runtime" source 会在宿主启动失败时再次抛异常，把真正的迁移/依赖失败包装成
        /// AggregateException，破坏 fail-closed 的稳定错误语义；Meta 的结构化日志出口当前是控制台
        /// （正式 OTel/Prometheus 归 R4），因此明确移除 EventLog，保证日志故障不遮蔽原始启动异常。
        /// </summary>
        private static void ConfigureLogging(WebApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
        }

        /// <summary>
        /// 配置来源（§10），后者覆盖前者：代码内联默认值 → <c>appsettings.json</c> → <c>META_</c>
        /// 环境变量 → <paramref name="overrides"/>（命令行/测试，"命令行只用于本地覆盖"）。
        /// </summary>
        private static void AddConfigurationSources(WebApplicationBuilder builder, IReadOnlyDictionary<string, string> overrides)
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
            builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables(EnvironmentVariablePrefix);
            if (overrides != null) builder.Configuration.AddInMemoryCollection(overrides);
        }

        /// <summary>
        /// Options：绑定 + 范围校验 + ValidateOnStart（§10），并落由配置决定的三处宿主上限/时限——
        /// 入站请求体上限须在解析前生效，故同时落 Kestrel 层与表单层（§5 P0-3）。
        /// </summary>
        private static void BindOptions(WebApplicationBuilder builder)
        {
            builder.Services.AddSingleton<IValidateOptions<MetaConfig>, MetaConfigValidator>();
            builder.Services.AddOptions<MetaConfig>()
                .Bind(builder.Configuration.GetSection(MetaHost.ConfigSection))
                .ValidateOnStart();

            builder.Services.AddOptions<KestrelServerOptions>()
                .Configure<IOptions<MetaConfig>>((kestrel, meta) =>
                    kestrel.Limits.MaxRequestBodySize = meta.Value.MaxInboundBytes);
            builder.Services.AddOptions<FormOptions>()
                .Configure<IOptions<MetaConfig>>((form, meta) =>
                    form.MultipartBodyLengthLimit = meta.Value.MaxInboundBytes);

            // 关闭时限（§10 优雅关闭）。默认 30s，须由配置决定而非写死。
            builder.Services.AddOptions<HostOptions>()
                .Configure<IOptions<MetaConfig>>((host, meta) =>
                    host.ShutdownTimeout = TimeSpan.FromSeconds(meta.Value.ShutdownTimeoutSeconds));
        }

        /// <summary>
        /// 功能门读（绑定同一配置节的**普通实例**）：只用于决定是否注册真实存储/业务服务；
        /// 消费值仍一律经 <see cref="IOptions{TOptions}"/> 读取。
        /// </summary>
        internal static MetaConfig ReadBootConfig(IConfiguration configuration)
        {
            return configuration.GetSection(MetaHost.ConfigSection).Get<MetaConfig>() ?? new MetaConfig();
        }

        /// <summary>
        /// 监听地址落位——由 <see cref="IOptions{TOptions}"/> 解析（§10：不得从另建实例读，
        /// 否则配置文件覆盖不到）。
        /// </summary>
        internal static void ApplyListenAddress(WebApplication app)
        {
            MetaConfig effective = app.Services.GetRequiredService<IOptions<MetaConfig>>().Value;
            app.Urls.Add(effective.BindAddress);
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
