using System.Net;

namespace LiteNet.Transport
{
    /// <summary>
    /// 规范化远端地址（《商业级通用服务端框架总设计》§P0-6："Transport 必须提供规范化远端地址 和连接元数据后才可宣称实现"per-IP 限流））。
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
