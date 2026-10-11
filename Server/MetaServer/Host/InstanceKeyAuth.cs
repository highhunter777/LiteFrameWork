using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace MetaServer
{
    /// <summary>
    /// 房间侧→Meta 的服务凭据（实例密钥）比对与 Bearer 读取——《Meta 服务专项设计》§7/§8.2。
    ///
    /// **凭据按权限面分离**（P1②）：
    /// - Lobby 实例注册 → <c>LobbyInstanceKeyBase64</c>（房间端 `--lobby-instance-key`）；
    /// - 结算提交 `/matches/result` → <c>SettlementInstanceKeyBase64</c>（房间端 `--settlement-instance-key`）。
    ///
    /// 两者是不同权限面（"注册实例" ≠ "写结算账本"），本类只提供比对原语，**由调用方传入对应面的
    /// 期望值**——端点不得再交叉引用另一面的 key。
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
