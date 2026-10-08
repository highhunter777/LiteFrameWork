using System.Collections.Generic;

namespace MetaServer.Contracts.Auth
{
    /// <summary>
    /// 统一错误形状（§5.2）：<c>{code, messageKey, args}</c>——<c>code</c> 是**稳定机器可读**标识，
    /// 客户端按 code 分支不解析自由文本；<c>messageKey</c> 交本地化；<c>args</c> 只放非敏感参数。
    /// 取代样例端点的简化 <c>SampleError</c>（后者归 M0-c 样例专用，不扩展）。
    /// </summary>
    public sealed class MetaError
    {
        /// <summary>稳定机器可读错误码（AuthErrorCodes 常量表单源）。</summary>
        public string Code { get; set; }

        /// <summary>本地化消息键（客户端侧查表展示，不解析自由文本）。</summary>
        public string MessageKey { get; set; }

        /// <summary>非敏感参数（如字段名/上限值；**绝不**放令牌、字段原文或凭据）。</summary>
        public IReadOnlyList<string> Args { get; set; }
    }

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

        /// <summary>无效请求的消息键（本地化键空间 "meta." 前缀——客户端按 key 查表）。</summary>
        public const string MessageKeyInvalidRequest = "meta.auth.invalid-request";

        /// <summary>功能不可用（存储/配置缺位）的消息键。</summary>
        public const string MessageKeyUnavailable = "meta.auth.unavailable";

        /// <summary>访问令牌无效/过期/不符的消息键（Lobby 首个受保护端点起消费）。</summary>
        public const string MessageKeyUnauthorized = "meta.auth.unauthorized";
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

    /// <summary>
    /// 游客登录成功响应（S→C 载荷）。JSON 形状（camelCase）：
    /// <c>{"accountId":"...","accessToken":"...","tokenType":"Bearer","expiresAtMs":123}</c>。
    /// </summary>
    public sealed class GuestLoginResponse
    {
        public string AccountId { get; set; }

        /// <summary>访问令牌（v1 线格式；客户端对其不可解析不可验证——只透传）。</summary>
        public string AccessToken { get; set; }

        /// <summary>令牌类型（透传形态固定 "Bearer"；Authorization: Bearer &lt;token&gt;）。</summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>过期时刻（epoch 毫秒，UTC——§5.2"时间字段一律 UTC"；客户端只用于提前刷新，不作判据）。</summary>
        public long ExpiresAtMs { get; set; }
    }
}
