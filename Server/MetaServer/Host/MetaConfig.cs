using System;
using System.Collections.Generic;
using MongoDB.Driver;

namespace MetaServer
{
    /// <summary>
    /// Meta 宿主装配参数（《Meta 服务专项设计》§10 配置与启动、§13 预算与容量）。
    ///
    /// **必须是属性而非字段**（与 <c>RoomServer.Runtime.RoomConfig</c> 的字段形态不同，这是有意的）：
    /// `ConfigurationBinder` 只绑定属性，字段形态会让 §10 的"版本化文件 / 环境变量"配置源
    /// **静默不生效**（2026-09-25 实测：字段形态从配置文件读到值后仍是内联默认值）。
    ///
    /// **每项都有范围校验**（§10"均有范围校验"），拒绝值直接导致启动失败返回非零退出码，
    /// 不得带默认错配置继续运行。
    ///
    /// M0-c 批二（2026-09-30）起承载持久化参数（Mongo 连接/库名/Outbox 容量）；
    /// Redis/限流阈值等仍归 G3/R4，接入时在此追加，不另建第二套配置类。
    /// </summary>
    public sealed class MetaConfig
    {
        // ---- 监听（§4.1 Kestrel；测试用 http://127.0.0.1:0 取临时端口）----

        /// <summary>监听地址。生产必须为 HTTPS 端点（§12"外部接口一律 TLS"）——
        /// http 绑定只允许回环地址；非回环 http 需显式置 <see cref="AllowNonLoopbackHttp"/>（受信内网豁免）。</summary>
        public string BindAddress { get; set; } = "http://127.0.0.1:5000";

        /// <summary>
        /// 非回环 http 绑定豁免（默认 false = 拒绝）。https 绑定不受影响；
        /// 仅当部署形态确为"受信内网 + 终端 TLS 在上游"时才显式开启——
        /// 把"生产忘了配 TLS"从静默事故变成启动期显式裁决（硬化批 2026-10-03 门禁化）。
        /// </summary>
        public bool AllowNonLoopbackHttp { get; set; }

        // ---- 请求边界（对齐《服务端总设计》§5 P0-3"包体在解析前限制长度"）----

        /// <summary>入站请求体上限（字节）。落在 Kestrel 的 MaxRequestBodySize，解析前即拒绝。</summary>
        public int MaxInboundBytes { get; set; } = 65536;

        // ---- 优雅关闭（§10 优雅关闭四步）----

        /// <summary>关闭时限（秒）：Host 在该期限内完成在途请求与后台服务停止，超时强制结束。</summary>
        public int ShutdownTimeoutSeconds { get; set; } = 15;

        /// <summary>Mongo 集群选择超时（毫秒）——驱动在此期限内选不到可用节点即失败。
        /// 不可达存储必须快速失败（fail-closed 拒启/503），超时值可配以适配内网拓扑。</summary>
        public int MongoServerSelectionTimeoutMs { get; set; } = 5000;

        /// <summary>/ready 存储 ping 超时（毫秒）：健康检查必须 bounded，不得拖死探针响应。</summary>
        public int ReadyPingTimeoutMs { get; set; } = 2000;

        // ---- 持久化（M0-c 批二，2026-09-30；§9.1/§10/§11.2）----

        /// <summary>
        /// Mongo 连接串。**空 = 持久化功能关闭**（拒绝相应功能而非拒绝启动——样例命令 503、
        /// /ready 不检存储）；非空时必须为合法 mongodb/mongodb+srv URI 且配套库名。
        /// 生产部署必须配置：启用后启动迁移失败（含 Mongo 不可达）即拒绝启动（fail-closed）。
        /// </summary>
        public string MongoConnectionString { get; set; } = "";

        /// <summary>数据库名（配置了连接串时必填）。</summary>
        public string MongoDatabaseName { get; set; } = "";

        /// <summary>持久 Outbox 容量上界（§11.2"任何队列必须有显式容量"；满则入队显式拒绝）。</summary>
        public int OutboxCapacity { get; set; } = 1024;

        public static MetaConfig Default() => new MetaConfig();

        /// <summary>
        /// 范围校验（§10）。返回全部违规项——一次报告所有问题，不逐条反复试错启动。
        /// 空列表 = 通过。**非法配置必须阻止启动**，不得降级为默认值。
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(BindAddress))
            {
                errors.Add("BindAddress 不得为空");
            }
            else if (!Uri.TryCreate(BindAddress, UriKind.Absolute, out Uri parsed))
            {
                errors.Add("BindAddress 不是合法绝对 URI：" + BindAddress);
            }
            else if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add("BindAddress 只支持 http/https，实际：" + parsed.Scheme);
            }
            else if (parsed.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(parsed) && !AllowNonLoopbackHttp)
            {
                // §12"外部接口一律 TLS"的门禁化：非回环 http 默认拒绝——
                // 把"生产忘配 TLS"从静默事故变成启动期显式裁决（豁免需显式置 AllowNonLoopbackHttp）
                errors.Add("BindAddress 为 http 且非回环地址（" + parsed.Host + "）——外部接口必须 TLS；" +
                           "确需非回环 http（受信内网）请显式置 AllowNonLoopbackHttp=true");
            }

            // 1MB 上限与 §5 P0-3 的"解析前限制长度"一致；低于 1KB 会挡住正常登录载荷
            if (MaxInboundBytes < 1024 || MaxInboundBytes > 1024 * 1024)
            {
                errors.Add("MaxInboundBytes 必须在 1024..1048576，实际：" + MaxInboundBytes);
            }

            if (ShutdownTimeoutSeconds < 1 || ShutdownTimeoutSeconds > 300)
            {
                errors.Add("ShutdownTimeoutSeconds 必须在 1..300，实际：" + ShutdownTimeoutSeconds);
            }

            if (MongoServerSelectionTimeoutMs < 100 || MongoServerSelectionTimeoutMs > 60000)
            {
                errors.Add("MongoServerSelectionTimeoutMs 必须在 100..60000，实际：" + MongoServerSelectionTimeoutMs);
            }

            if (ReadyPingTimeoutMs < 100 || ReadyPingTimeoutMs > 10000)
            {
                errors.Add("ReadyPingTimeoutMs 必须在 100..10000，实际：" + ReadyPingTimeoutMs);
            }

            // ---- 持久化（M0-c 批二）：功能门 + 形状校验 ----
            // 驱动 3.x 无 MongoUrl.TryCreate——用 Create 的异常面判合法性（配置校验场景，
            // 任何解析失败都归为"非法 URI"）。**错误信息不回显原串**——连接串可能含凭据，
            // 回显会经 ValidateOnStart 的异常面泄入 stderr/日志（硬化批 2026-10-03）。
            bool storageConfigured = !string.IsNullOrWhiteSpace(MongoConnectionString);
            if (storageConfigured && !IsParsableMongoUri(MongoConnectionString))
            {
                errors.Add("MongoConnectionString 必须是合法 mongodb/mongodb+srv URI（长度 " +
                           MongoConnectionString.Length + "；原串可能含凭据，不回显）");
            }
            if (storageConfigured && string.IsNullOrWhiteSpace(MongoDatabaseName))
            {
                errors.Add("配置了 MongoConnectionString 时 MongoDatabaseName 不得为空");
            }
            if (!storageConfigured && !string.IsNullOrWhiteSpace(MongoDatabaseName))
            {
                errors.Add("未配置 MongoConnectionString 时 MongoDatabaseName 应为空（没有存储可指向）");
            }
            if (OutboxCapacity < 1 || OutboxCapacity > 1024 * 1024)
            {
                errors.Add("OutboxCapacity 必须在 1..1048576，实际：" + OutboxCapacity);
            }

            return errors;
        }

        private static bool IsParsableMongoUri(string value)
        {
            try
            {
                MongoUrl.Create(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>回环宿主判定（127.0.0.0/8、::1、localhost）——http 门禁用。</summary>
        private static bool IsLoopbackHost(Uri bind)
        {
            if (string.Equals(bind.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return System.Net.IPAddress.TryParse(bind.Host, out System.Net.IPAddress ip)
                   && System.Net.IPAddress.IsLoopback(ip);
        }
    }
}
