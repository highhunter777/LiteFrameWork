using System;

namespace LiteFramework
{
    /// <summary>
    /// 相对路径语法校验单源（本地持久化 / 内容清单 / 脚本路径共用）：
    /// 根、盘符、反斜杠、空段、./.. 越界段的判据不再各写一份。
    /// </summary>
    public static class SafeRelativePath
    {
        /// <summary>是否绝对开头（/ 或 \）。</summary>
        public static bool IsRooted(string p)
            => !string.IsNullOrEmpty(p) && (p[0] == '/' || p[0] == '\\');

        /// <summary>是否含盘符（第二字符为 :）。</summary>
        public static bool HasDriveLetter(string p)
            => !string.IsNullOrEmpty(p) && p.Length > 1 && p[1] == ':';

        /// <summary>是否含反斜杠（可移植路径口径禁用）。</summary>
        public static bool HasBackslash(string p)
            => p != null && p.IndexOf('\\') >= 0;

        /// <summary>段是否为 . / ..（越界段）。</summary>
        public static bool IsTraversalSegment(string segment)
            => segment == "." || segment == "..";

        /// <summary>含 .. 越界段（按 / 或 \ 分段；"a..b" 不算）。</summary>
        public static bool HasTraversalSegment(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            int start = 0;
            for (int i = 0; i <= p.Length; i++)
            {
                if (i < p.Length && p[i] != '/' && p[i] != '\\') continue;
                if (i - start == 2 && p[start] == '.' && p[start + 1] == '.') return true;
                start = i + 1;
            }
            return false;
        }

        /// <summary>可移植相对路径（文件清单/脚本路径口径）：反斜杠、根、盘符、空段、./.. 全拒。
        /// 返回 false 时 <paramref name="reason"/> 为中文原因（调用方按自身错误码映射）。</summary>
        public static bool IsPortableRelative(string p, out string reason)
        {
            if (string.IsNullOrEmpty(p)) { reason = "路径为空"; return false; }
            if (HasBackslash(p)) { reason = "路径含反斜杠"; return false; }
            if (p[0] == '/') { reason = "绝对路径"; return false; }
            if (HasDriveLetter(p)) { reason = "含盘符"; return false; }
            string[] segments = p.Split('/');
            foreach (string seg in segments)
            {
                if (seg.Length == 0) { reason = "空路径段"; return false; }
                if (IsTraversalSegment(seg)) { reason = "越界路径段"; return false; }
            }
            reason = null;
            return true;
        }
    }
}
