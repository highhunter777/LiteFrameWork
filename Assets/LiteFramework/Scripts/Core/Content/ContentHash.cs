using System;
using System.Security.Cryptography;

namespace LiteFramework
{
    /// <summary>
    /// 内容哈希（《热更与内容发布专项设计》§5"摘要分两层：文件完整性对实际原始字节计算完整 SHA-256；
    /// 语义摘要对双端解析的玩法数据按同一规范计算"）。
    ///
    /// **实现约束（2026-09-25 本工程实测）**：Unity 的 `unity-4.8-api` 参考程序集是筛选子集，
    /// 没有 .NET 6+ 的 `SHA256.HashData` / `Convert.ToHexString`；本类只用实测可用的
    /// `SHA256.Create()` + 手工 hex 转换。**不要**改成 HashData/ToHexString（编译不过）。
    ///
    /// **不做任何换行归一化**：文件完整性必须对**原始字节**计算（§5"二进制文件不做 CRLF 替换；
    /// 文本归一化仅限明确声明的文本源码身份"）。这与 buildHash 生成器（对文本源码做 CRLF→LF 归一）
    /// 是**两套口径**，不可混用——buildHash 是源码身份，本类是下载文件完整性。
    ///
    /// **实例 vs 静态**：单次散列用静态方法（内部用非分配路径）；对成百上千个候选文件循环校验时，
    /// 持有一个 <see cref="Hasher"/> 实例复用算法对象——避免每文件一次 `SHA256.Create()`
    /// 的分配与会话开销（G4 可能扫全量候选）。
    /// </summary>
    public static class ContentHash
    {
        /// <summary>SHA-256 十六进制（小写，64 字符）。输入 null 视为空字节序列。</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null) bytes = Array.Empty<byte>();
            using (var hasher = new Hasher())
                return hasher.ComputeHex(bytes);
        }

        /// <summary>SHA-256 十六进制（小写）。对字符串按其 **UTF-8 原始字节**计算（不做换行归一化）。</summary>
        public static string Sha256Hex(string text)
            => Sha256Hex(text == null ? Array.Empty<byte>() : System.Text.Encoding.UTF8.GetBytes(text));

        /// <summary>
        /// 可复用的 SHA-256 会话（批量校验用）。**非线程安全**——每个校验循环持有一个实例。
        /// 用 <c>IncrementalHash</c>：本工程实测可用，且不需要每文件重建算法对象。
        /// </summary>
        public sealed class Hasher : IDisposable
        {
            private System.Security.Cryptography.IncrementalHash _hash;

            public Hasher()
            {
                try
                {
                    _hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                        System.Security.Cryptography.HashAlgorithmName.SHA256);
                }
                catch (Exception)
                {
                    _hash = null;   // 极少数平台无 IncrementalHash：回退到静态路径（正确性优先）
                }
            }

            /// <summary>散列并返回小写 hex；可连续调用（每次自动重置）。</summary>
            public string ComputeHex(byte[] bytes)
            {
                if (bytes == null) bytes = Array.Empty<byte>();
                if (_hash == null) return StaticSha256Hex(bytes);

                _hash.AppendData(bytes);
                return ToLowerHex(_hash.GetHashAndReset());
            }

            public void Dispose() { _hash?.Dispose(); _hash = null; }
        }

        private static string StaticSha256Hex(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return ToLowerHex(sha.ComputeHash(bytes));
        }

        /// <summary>恒定时间比较两个 hex 摘要（避免按字符早退泄漏差异位置）。长度不等直接 false。</summary>
        public static bool HexEquals(string a, string b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.Length != b.Length) return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                char x = char.ToLowerInvariant(a[i]);
                char y = char.ToLowerInvariant(b[i]);
                diff |= x ^ y;
            }
            return diff == 0;
        }

        /// <summary>字节 → 小写 hex（netstandard2.1 无 Convert.ToHexString——手工转换，零依赖）。</summary>
        public static string ToLowerHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var chars = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0x0F];
            }
            return new string(chars);
        }

        /// <summary>hex → 字节；非法 hex（含奇数长度/非 hex 字符）返回 false 而不抛（候选校验的坏数据路径）。</summary>
        public static bool TryParseHex(string hex, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(hex) || (hex.Length & 1) != 0) return false;

            var result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexValue(hex[i * 2]);
                int lo = HexValue(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return false;
                result[i] = (byte)((hi << 4) | lo);
            }
            bytes = result;
            return true;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
