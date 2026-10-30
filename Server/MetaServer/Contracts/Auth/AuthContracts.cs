using System.Collections.Generic;

namespace MetaServer.Contracts.Auth
{

    /// <summary>
    /// Auth 稳定错误码表（§5.2"错误码表归 Contracts 单源"）。变更 = 契约变更：
    /// 只增不改不重排（客户端按字符串字面量分支）。
    /// </summary>
    public static class AuthErrorCodes
    {
        /// <summary>请求载荷非法（缺字段/超边界/坏 JSON）。HTTP 400。</summary>
        public const string InvalidRequest = "auth.invalid-request";

        /// <summary>账号存储不可用（未配置/不可达——拒绝相应功能，§10）。HTTP 503。</summary>
        public const string StoreUnavailable = "auth.store-unavailable";

        /// <summary>Auth 功能未配置（未配签名密钥——拒绝相应功能，§10）。HTTP 503。</summary>
        public const string AuthDisabled = "auth.disabled";

        /// <summary>访问令牌无效/过期/不符（鉴权中间件随 Lobby 首个受保护端点接入时启用）。HTTP 401。</summary>
        public const string Unauthorized = "auth.unauthorized";

        /// <summary>请求过于频繁（未鉴权端点的兜底限流——游客登录每 IP 每分钟窗口）。HTTP 429。</summary>
        public const string RateLimited = "auth.rate-limited";

        /// <summary>无效请求的消息键（本地化键空间 "meta." 前缀——客户端按 key 查表）。</summary>
        public const string MessageKeyInvalidRequest = "meta.auth.invalid-request";

        /// <summary>功能不可用（存储/配置缺位）的消息键。</summary>
        public const string MessageKeyUnavailable = "meta.auth.unavailable";

        /// <summary>访问令牌无效/过期/不符的消息键（Lobby 首个受保护端点起消费）。</summary>
        public const string MessageKeyUnauthorized = "meta.auth.unauthorized";

        /// <summary>请求过于频繁的消息键。</summary>
        public const string MessageKeyRateLimited = "meta.auth.rate-limited";
    }

    /// <summary>
    /// 游客登录命令（C→S，§5.1 "Command" 语义：可被拒绝）。
    /// JSON 形状（camelCase，随 Minimal API web 默认）：<c>{"requestId":"...","deviceId":"..."}</c>。
    /// </summary>
    public sealed class GuestLoginCommand
    {
        /// <summary>请求幂等/追踪标识（1..128 UTF-8 字节；进日志与 Trace，不进 Metrics 标签）。</summary>
        public string RequestId { get; set; }

        /// <summary>设备标识（1..128 UTF-8 字节）——get-or-create 的业务键：同设备重复登录回到同一账号。
        /// **不是**安全凭据：客户端可换值（游客模型固有）；账号保护归后续正式身份/绑定（§6.1 范围外）。</summary>
        public string DeviceId { get; set; }
    }

}
