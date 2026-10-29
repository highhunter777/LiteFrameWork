using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 开发宏边界（《客户端架构重构演进路线图》§4.8/§4.11 7）：
    /// <c>DEVELOPMENT_BUILD</c> / <c>LITEFRAMEWORK_DEBUG</c> 只允许出现在 `LiteGame.Dev`。
    ///
    /// M3-1 建立存量基线（只减不增）→ M3-6b 逐段收紧 → **6b-4 清零转纯 hard fail**：
    /// 产品程序集（LiteClient/LiteGame 非 Dev/LiteFramework/LiteView）出现任何一处即失败，无允许表。
    /// </summary>
    public sealed class DevMacroGuardTests
    {
        [Fact]
        public void 开发宏只允许出现在Dev装配层_零容忍()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            string[] roots = { "Assets/LiteClient", "Assets/LiteGame", "Assets/LiteFramework", "Assets/LiteView", "Assets/LiteBattle" };

            foreach (string relativeRoot in roots)
            {
                string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (rel.StartsWith("Assets/LiteGame/Dev/", StringComparison.Ordinal)) continue;
                    int count = CountConditionLines(file);
                    if (count > 0) offenders.Add(rel + " :: " + count);
                }
            }

            Assert.True(offenders.Count == 0,
                "开发宏只能出现在 LiteGame.Dev（M3-6b 已清零——新增即失败）:" + string.Join(" | ", offenders));
        }

        private static int CountConditionLines(string file)
        {
            int count = 0;
            foreach (string line in File.ReadAllLines(file))
            {
                string trimmed = line.TrimStart('\uFEFF').TrimStart();
                if (!trimmed.StartsWith("#if", StringComparison.Ordinal) && !trimmed.StartsWith("#elif", StringComparison.Ordinal)) continue;
                if (line.Contains("DEVELOPMENT_BUILD") || line.Contains("LITEFRAMEWORK_DEBUG")) count++;
            }
            return count;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
