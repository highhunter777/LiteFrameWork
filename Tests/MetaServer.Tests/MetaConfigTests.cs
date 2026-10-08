using System;
using System.Collections.Generic;
using LiteTesting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 配置范围校验与 Options 装配（《Meta 服务专项设计》§10）。
    ///
    /// L1：纯逻辑 + DI 装配，不起监听。
    ///
    /// 本文件钉住配置校验在启动路径上的拦截（见 <see cref="非法配置_ValidateOnStart拒绝启动"/>）。
    /// </summary>
    public sealed class MetaConfigTests
    {
        [Fact]
        [Trait(TestTrait.Category, TestCategory.Unit)]
        public void 默认配置_通过校验()
        {
            Assert.Empty(MetaConfig.Default().Validate());
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 非法监听地址_被拒_且一次报全()
        {
            var config = MetaConfig.Default();
            config.BindAddress = "not-a-uri";
            config.MaxInboundBytes = 10;             // 同时越下限
            config.ShutdownTimeoutSeconds = 0;       // 同时越下限

            IReadOnlyList<string> errors = config.Validate();

            // 一次给出全部违规项，而不是逐条反复试错启动
            Assert.Equal(3, errors.Count);
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData("ftp://127.0.0.1:5000")]        // 非 http/https 方案
        [InlineData("")]
        [InlineData("   ")]
        public void 未支持的监听方案_被拒(string bind)
        {
            var config = MetaConfig.Default();
            config.BindAddress = bind;
            Assert.NotEmpty(config.Validate());
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(1023)]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1024 * 1024 + 1)]
        public void 入站上限越界_被拒(int bytes)
        {
            var config = MetaConfig.Default();
            config.MaxInboundBytes = bytes;
            Assert.NotEmpty(config.Validate());
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(1024)]
        [InlineData(65536)]
        [InlineData(1024 * 1024)]
        public void 入站上限边界内_通过(int bytes)
        {
            var config = MetaConfig.Default();
            config.MaxInboundBytes = bytes;
            Assert.Empty(config.Validate());
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(0)]
        [InlineData(301)]
        public void 关闭时限越界_被拒(int seconds)
        {
            var config = MetaConfig.Default();
            config.ShutdownTimeoutSeconds = seconds;
            Assert.NotEmpty(config.Validate());
        }

        // ---- 超时可配 / TLS 门禁 / 连接串脱敏 ----

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(99)]
        [InlineData(60001)]
        public void Mongo选择超时越界_被拒(int ms)
        {
            var config = MetaConfig.Default();
            config.MongoServerSelectionTimeoutMs = ms;
            Assert.NotEmpty(config.Validate());
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(99)]
        [InlineData(10001)]
        public void ReadyPing超时越界_被拒(int ms)
        {
            var config = MetaConfig.Default();
            config.ReadyPingTimeoutMs = ms;
            Assert.NotEmpty(config.Validate());
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData("http://127.0.0.1:5000")]
        [InlineData("http://localhost:5000")]
        [InlineData("http://[::1]:5000")]
        public void 回环http绑定_通过(string bind)
        {
            var config = MetaConfig.Default();
            config.BindAddress = bind;
            Assert.Empty(config.Validate());
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 非回环http绑定_默认拒绝_显式豁免后通过()
        {
            var config = MetaConfig.Default();
            config.BindAddress = "http://0.0.0.0:5000";
            Assert.NotEmpty(config.Validate());       // §12"外部接口一律 TLS"门禁化：默认拒绝

            config.AllowNonLoopbackHttp = true;       // 受信内网显式豁免
            Assert.Empty(config.Validate());
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 非回环https绑定_不受http门禁影响()
        {
            var config = MetaConfig.Default();
            config.BindAddress = "https://0.0.0.0:5000";
            Assert.Empty(config.Validate());
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 非法Mongo连接串_错误不回显原串()
        {
            var config = MetaConfig.Default();
            config.MongoConnectionString = "mongodb://user:secret@host:notaport/db";
            config.MongoDatabaseName = "meta";

            IReadOnlyList<string> errors = config.Validate();

            Assert.NotEmpty(errors);
            foreach (string error in errors)
            {
                // 连接串可能含凭据——校验错误必须脱敏（否则泄入 stderr/日志）
                Assert.DoesNotContain("secret", error);
                Assert.DoesNotContain("user:", error);
            }
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void Auth密钥长度与编码_非法拒绝_原值不回显()
        {
            var config = MetaConfig.Default();
            config.AuthSigningKeyBase64 = "not-base64";
            Assert.Contains(config.Validate(), error => error.Contains("AuthSigningKeyBase64"));
            Assert.DoesNotContain("not-base64", string.Join(";", config.Validate()));

            config.AuthSigningKeyBase64 = Convert.ToBase64String(new byte[31]);
            Assert.Contains(config.Validate(), error => error.Contains("32 字节"));

            config = MetaConfig.Default();
            config.AuthKeyId = "k.1";
            Assert.Contains(config.Validate(), error => error.Contains("AuthKeyId"));
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(59)]
        [InlineData(604801)]
        public void Auth令牌有效期越界_被拒(int ttl)
        {
            var config = MetaConfig.Default();
            config.AuthTokenTtlSeconds = ttl;
            Assert.Contains(config.Validate(), error => error.Contains("AuthTokenTtlSeconds"));
        }

        // ---- Lobby 配置（R3：实例注册 + Join Ticket 签发）----

        private static MetaConfig LobbyReady()
        {
            var config = MetaConfig.Default();
            config.AuthSigningKeyBase64 = Convert.ToBase64String(new byte[32]);
            config.LobbyInstanceKeyBase64 = Convert.ToBase64String(new byte[32]);
            config.LobbyTicketKeyBase64 = Convert.ToBase64String(new byte[32]);
            return config;
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 默认配置_Lobby未启用_通过校验()
        {
            Assert.Empty(MetaConfig.Default().Validate());      // 缺省 = 功能关闭，不是半段配置
            Assert.Empty(LobbyReady().Validate());
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void Lobby半段配置_拒绝启动()
        {
            var config = MetaConfig.Default();
            config.LobbyInstanceKeyBase64 = Convert.ToBase64String(new byte[32]);   // 只有一半
            Assert.Contains(config.Validate(), error => error.Contains("LobbyTicketKeyBase64"));

            config = MetaConfig.Default();
            config.LobbyTicketKeyBase64 = Convert.ToBase64String(new byte[32]);
            Assert.Contains(config.Validate(), error => error.Contains("LobbyInstanceKeyBase64"));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void Lobby配置_缺Auth令牌密钥_拒绝启动()
        {
            var config = LobbyReady();
            config.AuthSigningKeyBase64 = "";                   // 签发端点以访问令牌鉴权，缺 Auth 不可用
            Assert.Contains(config.Validate(), error => error.Contains("AuthSigningKeyBase64"));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void Lobby密钥_非法Base64或过短_拒绝_原值不回显()
        {
            var config = LobbyReady();
            config.LobbyInstanceKeyBase64 = "not-base64";
            IReadOnlyList<string> errors = config.Validate();
            Assert.Contains(errors, error => error.Contains("LobbyInstanceKeyBase64"));
            Assert.DoesNotContain("not-base64", string.Join(";", errors));

            config = LobbyReady();
            config.LobbyTicketKeyBase64 = Convert.ToBase64String(new byte[31]);
            Assert.Contains(config.Validate(), error => error.Contains("32 字节"));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void Lobby票据kid_形状约束_拒绝含分隔符()
        {
            var config = LobbyReady();
            config.LobbyTicketKeyId = "l.1";
            Assert.Contains(config.Validate(), error => error.Contains("LobbyTicketKeyId"));

            config = LobbyReady();
            config.LobbyTicketKeyId = new string('k', 33);
            Assert.Contains(config.Validate(), error => error.Contains("LobbyTicketKeyId"));
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(29)]
        [InlineData(3601)]
        public void Lobby票据有效期越界_被拒(int ttl)
        {
            var config = LobbyReady();
            config.LobbyTicketTtlSeconds = ttl;
            Assert.Contains(config.Validate(), error => error.Contains("LobbyTicketTtlSeconds"));
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(0)]
        [InlineData(3601)]
        public void Lobby心跳时限越界_被拒(int ttl)
        {
            var config = LobbyReady();
            config.LobbyHeartbeatTtlSeconds = ttl;
            Assert.Contains(config.Validate(), error => error.Contains("LobbyHeartbeatTtlSeconds"));
        }

        [Theory]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        [InlineData(0)]
        [InlineData(4097)]
        public void Lobby注册表容量越界_被拒(int capacity)
        {
            var config = LobbyReady();
            config.LobbyRegistryCapacity = capacity;
            Assert.Contains(config.Validate(), error => error.Contains("LobbyRegistryCapacity"));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void Lobby默认房间号_超字节上限_拒绝()
        {
            var config = LobbyReady();
            config.LobbyDefaultRoomId = new string('c', 65);
            Assert.Contains(config.Validate(), error => error.Contains("LobbyDefaultRoomId"));
        }

        // ---- 以下三项为配置校验的回归钉 ----

        /// <summary>
        /// §12"启动失败必须返回非零退出码，不能带默认错配置继续运行"。
        ///
        /// 此用例确保非法配置**在启动路径上**被拦下（校验对象必须与实际使用实例一致）。
        /// </summary>
        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async System.Threading.Tasks.Task 非法配置_拒绝启动()
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:0",  // 万一没拦住也别占端口
                [MetaHost.ConfigSection + ":MaxInboundBytes"] = "10",               // 越下限
            };

            // 校验可能在 Build 或 StartAsync 任一阶段触发（Host 构造 ConsoleLifetime
            // 时解析 IOptions<HostOptions> 即带出 MetaConfig 校验）。两个阶段都算"启动路径被拦住"。
            WebApplication app = null;
            try
            {
                OptionsValidationException ex = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
                {
                    app = MetaHost.Build(Array.Empty<string>(), overrides);
                    await app.StartAsync();
                });
                Assert.Contains(ex.Failures, f => f.Contains("MaxInboundBytes"));
            }
            finally
            {
                if (app != null) await app.DisposeAsync();
            }
        }

        /// <summary>
        /// §10 配置来源必须真正生效。
        ///
        /// 配置必须经 `IOptions&lt;MetaConfig&gt;` 读——那才是实际生效的那一份。
        /// </summary>
        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 覆盖项_经IOptions生效()
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":BindAddress"] = "http://127.0.0.1:18080",
                [MetaHost.ConfigSection + ":MaxInboundBytes"] = "2048",
                [MetaHost.ConfigSection + ":ShutdownTimeoutSeconds"] = "42",
            };

            WebApplication app = MetaHost.Build(Array.Empty<string>(), overrides);
            try
            {
                MetaConfig effective = app.Services.GetRequiredService<IOptions<MetaConfig>>().Value;

                Assert.Equal("http://127.0.0.1:18080", effective.BindAddress);
                Assert.Equal(2048, effective.MaxInboundBytes);
                Assert.Equal(42, effective.ShutdownTimeoutSeconds);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// §5 P0-3"包体在解析前限制长度"：入站上限必须真的落到 Kestrel，而不只是配置对象里的一个数。
        /// </summary>
        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 入站上限_落到Kestrel限制()
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":MaxInboundBytes"] = "2048",
            };

            WebApplication app = MetaHost.Build(Array.Empty<string>(), overrides);
            try
            {
                KestrelServerOptions kestrel = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
                Assert.Equal(2048, kestrel.Limits.MaxRequestBodySize);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>§10 关闭时限须由配置决定，不得写死默认 30s。</summary>
        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void 关闭时限_落到HostOptions()
        {
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetaHost.ConfigSection + ":ShutdownTimeoutSeconds"] = "42",
            };

            WebApplication app = MetaHost.Build(Array.Empty<string>(), overrides);
            try
            {
                HostOptions host = app.Services.GetRequiredService<IOptions<HostOptions>>().Value;
                Assert.Equal(TimeSpan.FromSeconds(42), host.ShutdownTimeout);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }
}
