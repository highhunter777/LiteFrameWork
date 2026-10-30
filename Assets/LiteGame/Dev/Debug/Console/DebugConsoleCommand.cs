#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;

namespace LiteGame
{
    /// <summary>
    /// 控制台命令行的纯词法解析：空白切分、verb 小写；命令名与参数均不含空格，不做引号语法。
    /// 与 UI/输入后端解耦，供 EditMode 直测。
    /// </summary>
    public static class DebugConsoleCommand
    {
        public static bool TryParse(string line, out string verb, out string[] args)
        {
            verb = null;
            args = Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(line)) return false;

            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            verb = parts[0].ToLowerInvariant();
            if (parts.Length > 1)
            {
                args = new string[parts.Length - 1];
                Array.Copy(parts, 1, args, 0, args.Length);
            }
            return true;
        }
    }
}
#endif
