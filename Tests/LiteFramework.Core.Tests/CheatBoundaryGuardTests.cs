using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 信任边界守卫（《GM 与调试工具分层设计》§1.1）：
    /// 开发作弊/调试实现只允许住 `LiteGame.Dev`；产品程序集只能经 `IDevSession`/`ILocalTestHooks` 端口。
    /// 代码级直读（含 `TestModeRuntime.*`/`DebugKernel.*`/`SimTestRules.*`）一律失败。
    /// </summary>
    public sealed class CheatBoundaryGuardTests
    {
        private static readonly string[] ProductRoots =
        {
            "Assets/LiteFramework/Runtime/Modules",
            "Assets/LiteGame/App",
            "Assets/LiteGame/Adapters/LuaBridge",
        };

        private static readonly string[] Forbidden =
        {
            "TestModeRuntime",
            "DebugKernel",
            "SimTestRules",
            "TestModeOptions",
            "FeatureDebugTunables",
            "WeaponDebugTunables",
        };

        [Fact]
        public void 产品程序集不得直读开发作弊源()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            foreach (string productRoot in ProductRoots)
            {
                string dir = Path.Combine(root, productRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string code = StripCommentAndStrings(lines[i]);
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        foreach (string token in Forbidden)
                        {
                            if (Regex.IsMatch(code, @"\b" + token + @"\b"))
                                offenders.Add(rel + ":" + (i + 1) + " :: " + token);
                        }
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "产品程序集直读开发作弊源（改经 IDevSession/ILocalTestHooks 端口）："
                + string.Join(" | ", offenders));
        }

        [Fact]
        public void 开发作弊实现必须住LiteGameDev()
        {
            string root = RepoRoot();
            Assert.True(File.Exists(Path.Combine(root, "Assets/LiteGame/Adapters/Dev/Debug/Session/TestModeRuntime.cs")));
            Assert.True(File.Exists(Path.Combine(root, "Assets/LiteGame/Adapters/Dev/Debug/Kernel/DebugKernel.cs")));
            Assert.False(File.Exists(Path.Combine(root, "Assets/LiteGame/App/Debug/TestModeRuntime.cs")));
            Assert.True(File.Exists(Path.Combine(root, "Assets/LiteGame/Core/Dev/ILocalTestHooks.cs")));
        }

        private static string StripCommentAndStrings(string line)
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment);
            return Regex.Replace(line, "\"[^\"]*\"", "\"\"");
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
