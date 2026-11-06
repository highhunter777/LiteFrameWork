using System;
using System.Text;

namespace RoomServer.Application
{
    /// <summary>
    /// Base64Url 编解码的**唯一实现**（P2 单源）：去 padding、<c>+</c>→<c>-</c>、<c>/</c>→<c>_</c>。
    /// </summary>
    public static class Base64Url
    {
        /// <summary>字节序列 → base64url（无 padding）。</summary>
        public static string Encode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return Convert.ToBase64String(bytes)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>UTF-8 文本 → base64url（空/null → 空串）。</summary>
        public static string EncodeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Encode(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// base64url → 原始字节；非法（长度模 4 为 1 / 非 base64 字母表）返回 false。
        /// </summary>
        public static bool TryDecode(string base64Url, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(base64Url)) return base64Url != null;   // 空串 = 空字节序列；null = 非法

            string s = base64Url.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return false;          // 长度模 4 为 1 不是合法 base64
            }
            try
            {
                bytes = Convert.FromBase64String(s);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>base64url → UTF-8 文本；非法返回 null（不抛——调用方转 Malformed）。</summary>
        public static string DecodeText(string base64Url)
        {
            if (base64Url == null) return null;
            if (base64Url.Length == 0) return string.Empty;
            return TryDecode(base64Url, out byte[] bytes) ? Encoding.UTF8.GetString(bytes) : null;
        }
    }
}
