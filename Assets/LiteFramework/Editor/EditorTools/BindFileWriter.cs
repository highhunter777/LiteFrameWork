using System;
using System.IO;
using System.Text;

namespace LiteCodeGen.EditorTools
{
    /// <summary>LiteCodeGen 写盘原语：主文件只创建不覆盖；Designer 保持 write-if-changed。</summary>
    public static class BindFileWriter
    {
        public static bool WriteMainIfMissing(string assetsPath, string content)
        {
            string full = Resolve(assetsPath);
            if (File.Exists(full)) return false;
            EnsureDirectory(full);
            File.WriteAllText(full, content, new UTF8Encoding(false));
            return true;
        }

        public static bool WriteIfChanged(string assetsPath, string content)
        {
            string full = Resolve(assetsPath);
            if (File.Exists(full) && File.ReadAllText(full) == content) return false;
            EnsureDirectory(full);
            File.WriteAllText(full, content, new UTF8Encoding(false));
            return true;
        }

        private static string Resolve(string assetsPath)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), assetsPath);
        }

        private static void EnsureDirectory(string fullPath)
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }
    }

    /// <summary>绑定生成输出的目录策略：共享源集/生成数据目录禁止写入。</summary>
    public static class BindOutputPolicy
    {
        private static readonly string[] ForbiddenPrefixes =
        {
            "Assets/LiteSim/",
            "Assets/LiteNet/",
            "Assets/RoomServer/",
            "Assets/GameData/Generated/",
        };

        public static bool IsForbidden(string scriptsFolder, out string reason)
        {
            string normalized = (scriptsFolder ?? string.Empty).Replace('\\', '/').TrimEnd('/') + "/";
            foreach (string prefix in ForbiddenPrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "脚本目录落在共享源集/生成数据目录，禁止写入：" + scriptsFolder;
                    return true;
                }
            }
            reason = null;
            return false;
        }
    }
}
