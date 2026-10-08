using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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

        // ---- Auth（R3 首批：游客登录 + 访问令牌）----

        /// <summary>Auth 访问令牌 HMAC 密钥（Base64；空 = Auth 功能关闭，不把假密钥带入生产）。</summary>
        public string AuthSigningKeyBase64 { get; set; } = "";

        /// <summary>访问令牌密钥标识；轮换期间由签发/校验双方按 kid 选择密钥。</summary>
        public string AuthKeyId { get; set; } = "k1";

        /// <summary>访问令牌固定受众。</summary>
        public string AuthAudience { get; set; } = "meta";

        /// <summary>访问令牌有效期（秒）。</summary>
        public int AuthTokenTtlSeconds { get; set; } = 600;

        // ---- Lobby（R3：实例注册 + Join Ticket 签发；§6.2/§7）----

        /// <summary>
        /// Lobby 实例注册凭据（Base64；**空 = Lobby 功能关闭**）。房间端以同值
        /// 作 Bearer 调用注册端点（常量时间比较）；本值只作凭据，不解码为其它用途。
        /// </summary>
        public string LobbyInstanceKeyBase64 { get; set; } = "";

        /// <summary>
        /// Join Ticket HMAC 密钥（Base64；空 = Lobby 功能关闭）。**须与房间端
        /// `--ticket-key &lt;kid&gt;:&lt;base64&gt;` 的密钥一致**——Meta 签发、房间验签同 key。
        /// </summary>
        public string LobbyTicketKeyBase64 { get; set; } = "";

        /// <summary>票据密钥标识（须与房间端 `--ticket-key` 的 kid 一致；轮换期间两端同改）。</summary>
        public string LobbyTicketKeyId { get; set; } = "l1";

        /// <summary>票据受众（须与房间端 `--audience` 一致；空 = 房间端不校验）。</summary>
        public string LobbyTicketAudience { get; set; } = "lobby";

        /// <summary>票据有效期（秒）——短期（§6.2"有效期短期"）。</summary>
        public int LobbyTicketTtlSeconds { get; set; } = 300;

        /// <summary>心跳存活时限（秒）：超时未心跳即从可分配集合移除（回告实例，两端口径同源）。</summary>
        public int LobbyHeartbeatTtlSeconds { get; set; } = 30;

        /// <summary>注册表容量上限（§13"任何队列/缓存…必须有显式容量"；满拒新实例）。</summary>
        public int LobbyRegistryCapacity { get; set; } = 64;

        /// <summary>默认房间号（请求未带 roomId 时使用；空 = 必须由请求给出）。</summary>
        public string LobbyDefaultRoomId { get; set; } = "";

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

            bool authConfigured = !string.IsNullOrWhiteSpace(AuthSigningKeyBase64);
            if (authConfigured)
            {
                try
                {
                    byte[] secret = Convert.FromBase64String(AuthSigningKeyBase64);
                    if (secret.Length < 32)
                        errors.Add("AuthSigningKeyBase64 解码后至少需要 32 字节");
                }
                catch (FormatException)
                {
                    errors.Add("AuthSigningKeyBase64 必须是合法 Base64（原值不回显）");
                }
            }

            if (string.IsNullOrWhiteSpace(AuthKeyId) || AuthKeyId.Length > 64 || AuthKeyId.Contains('.'))
                errors.Add("AuthKeyId 必须为 1..64 个字符且不得包含 '.'");
            if (string.IsNullOrWhiteSpace(AuthAudience) || AuthAudience.Length > 128)
                errors.Add("AuthAudience 必须为 1..128 个字符");
            if (AuthTokenTtlSeconds < 60 || AuthTokenTtlSeconds > 604800)
                errors.Add("AuthTokenTtlSeconds 必须在 60..604800，实际：" + AuthTokenTtlSeconds);

            // ---- Lobby（R3）：半段配置 = 配置错误（宁可不启动，也不带"以为开了"的 Lobby 运行）----
            bool lobbyConfigured = !string.IsNullOrWhiteSpace(LobbyInstanceKeyBase64)
                || !string.IsNullOrWhiteSpace(LobbyTicketKeyBase64);
            if (lobbyConfigured)
            {
                if (!IsUsableKey(LobbyInstanceKeyBase64))
                    errors.Add("LobbyInstanceKeyBase64 必须是合法 Base64 且解码后至少 32 字节（原值不回显）");
                if (!IsUsableKey(LobbyTicketKeyBase64))
                    errors.Add("LobbyTicketKeyBase64 必须是合法 Base64 且解码后至少 32 字节（原值不回显）");
                if (string.IsNullOrWhiteSpace(AuthSigningKeyBase64))
                    errors.Add("配置 Lobby 时必须同时配置 AuthSigningKeyBase64——签发端点以访问令牌鉴权，缺 Auth 时 Lobby 不可用");
                if (string.IsNullOrEmpty(LobbyTicketKeyId) || LobbyTicketKeyId.Length > 32
                    || !LobbyTicketKeyIdPattern.IsMatch(LobbyTicketKeyId))
                    errors.Add("LobbyTicketKeyId 只允许 [A-Za-z0-9_-]{1,32}（不得含票据分隔符 '.'）");
                if (LobbyTicketAudience == null || LobbyTicketAudience.Length > 128)
                    errors.Add("LobbyTicketAudience 不得为 null 且不得超过 128 个字符");
                if (LobbyTicketTtlSeconds < 30 || LobbyTicketTtlSeconds > 3600)
                    errors.Add("LobbyTicketTtlSeconds 必须在 30..3600，实际：" + LobbyTicketTtlSeconds);
                if (LobbyHeartbeatTtlSeconds < 1 || LobbyHeartbeatTtlSeconds > 3600)
                    errors.Add("LobbyHeartbeatTtlSeconds 必须在 1..3600，实际：" + LobbyHeartbeatTtlSeconds);
                if (LobbyRegistryCapacity < 1 || LobbyRegistryCapacity > 4096)
                    errors.Add("LobbyRegistryCapacity 必须在 1..4096，实际：" + LobbyRegistryCapacity);
                if (!string.IsNullOrEmpty(LobbyDefaultRoomId)
                    && Encoding.UTF8.GetByteCount(LobbyDefaultRoomId) > 64)
                    errors.Add("LobbyDefaultRoomId 不得超过 64 UTF-8 字节（与房间端房间号上限同口径）");
            }

            return errors;
        }

        /// <summary>Key 形状与 Auth 同口径：Base64 可解码且 ≥32 字节（原值不回显）。</summary>
        private static bool IsUsableKey(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return false;
            try
            {
                return Convert.FromBase64String(base64).Length >= 32;
            }
            catch (FormatException)
            {
                return false;
            }
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

        /// <summary>票据 kid 形状（与房间端 JoinTicketKey 的同款约束——含分隔符会让票据拆段错位）。</summary>
        private static readonly Regex LobbyTicketKeyIdPattern =
            new Regex(@"^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);
    }
}
