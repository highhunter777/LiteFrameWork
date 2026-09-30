using System.Net;

namespace LiteNet.Transport
{
    /// <summary>
    /// 规范化远端地址（《商业级通用服务端框架总设计》§P0-6/R2："Transport 必须提供规范化远端地址
    /// 和连接元数据后才可宣称实现"per-IP 限流））。
    ///
    /// **规范化规则**（限流键的稳定性依赖它——同一主机的不同表达必须归一到同一个键）：
    /// - IPv4-mapped IPv6（<c>::ffff:a.b.c.d</c>，双栈 socket 常见形态）归一为 IPv4 点分串；
    /// - IPv6 用 .NET 规范压缩小写形态（scope id 保留——链路本地地址跨接口不合并）；
    /// - **不含端口**：端口是每连接量，限流键不含它。
    ///
    /// 未知/空地址返回 null——调用方对 null **跳过** IP 维度限流（看不见的地址限不了，
    /// 也不得拿它当同一个桶）。
    /// </summary>
    public static class RemoteAddress
    {
        /// <summary>IPEndPoint → 规范化串（null 端点/地址返回 null）。</summary>
        public static string Normalize(IPEndPoint endpoint)
        {
            return endpoint == null ? null : Normalize(endpoint.Address);
        }

        /// <summary>IPAddress → 规范化串（null 返回 null）。</summary>
        public static string Normalize(IPAddress address)
        {
            if (address == null) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }
    }
}
