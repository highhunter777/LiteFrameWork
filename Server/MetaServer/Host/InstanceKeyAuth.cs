using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace MetaServer
{
    /// <summary>
    /// 房间侧→Meta 的服务凭据（实例密钥）比对与 Bearer 读取——《Meta 服务专项设计》§7/§8.2：
    /// Lobby 实例注册与结算提交是**同一条服务通道**的两种调用，共用一份凭据（Meta 配置的
    /// <c>LobbyInstanceKeyBase64</c>，房间端经环境变量注入）；日后若需分权再拆独立密钥。
    /// </summary>
    internal static class InstanceKeyAuth
    {
        /// <summary>实例密钥比对：Bearer 原文按 Base64 解码后**常量时间**比较（防时序侧信道）。</summary>
        public static bool Matches(HttpContext context, string expectedBase64)
        {
            string provided = ReadBearer(context);
            if (provided == null || string.IsNullOrWhiteSpace(expectedBase64)) return false;
            byte[] expected;
            byte[] actual;
            try
            {
                expected = Convert.FromBase64String(expectedBase64);
                actual = Convert.FromBase64String(provided);
            }
            catch (FormatException)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        /// <summary>读取 Bearer 凭据（缺失/坏形状返回 null——统一走鉴权失败路径，不区分对待）。</summary>
        public static string ReadBearer(HttpContext context)
        {
            string header = context.Request.Headers["Authorization"];
            const string prefix = "Bearer ";
            if (string.IsNullOrEmpty(header) || header.Length <= prefix.Length
                || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            return header.Substring(prefix.Length).Trim();
        }
    }
}
