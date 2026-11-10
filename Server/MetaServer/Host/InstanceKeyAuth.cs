using System;
using System.Security.Cryptography;
using MetaServer.Contracts.Auth;
using MetaServer.Modules.Auth;
using Microsoft.AspNetCore.Http;

namespace MetaServer
{
    /// <summary>
    /// 鉴权薄层：实例密钥比对、Bearer 读取、玩家访问令牌鉴权（失败即 401）——《Meta 服务专项设计》§7/§8.2。
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

        /// <summary>玩家访问令牌鉴权单点：读 Bearer → 校验；失败返 401 结果（调用方直接 return），成功返 <c>null</c>。</summary>
        /// <param name="claims">仅成功（返回 <c>null</c>）时非 null；失败时为 null。</param>
        /// <remarks>拒绝分类进 args（非敏感）：稳定错误码 + 分类便于对数；令牌原文绝不回显。</remarks>
        public static IResult TryValidateToken(HttpContext context, AccessTokenService tokens,
            out AccessTokenFormat.Claims claims)
        {
            AccessTokenFormat.Rejection rejection = tokens.Validate(ReadBearer(context), out claims);
            if (rejection == AccessTokenFormat.Rejection.None) return null;
            return MetaResults.Error(StatusCodes.Status401Unauthorized,
                AuthErrorCodes.Unauthorized, AuthErrorCodes.MessageKeyUnauthorized, rejection.ToString());
        }
    }
}
