using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 身份入口守卫（路线图 §4.11 11）：业务程序集不得直接拼 BuildHash / CombatConfigDigest。
    /// 允许面只有身份实现、配置基摘要适配器与构造函数夹具。
    /// </summary>
    public sealed class IdentityEntryGuardTests
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/LiteGame/App/Bootstrap/IdentityModule.cs",
            "Assets/LiteFramework/Runtime/Modules/Battle/GameplayMatchIdentity.cs",
            "Assets/LiteFramework/Runtime/Adapters/Luban/TableLayout.cs",
        };

        [Fact]
        public void 业务程序集不得直引身份哈希()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            string[] roots = { "Assets/LiteFramework/Runtime/Modules", "Assets/LiteGame/App", "Assets/LiteNet/Core/Client" };

            foreach (string relativeRoot in roots)
            {
                string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (Allowed.Contains(rel)) continue;
                    foreach (string line in File.ReadAllLines(file))
                    {
                        string trimmed = line.TrimStart();
                        if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                        if (line.Contains("BuildHash.Value") || line.Contains("LiteNet.BuildHash")
                            || line.Contains("CombatConfigDigest."))
                        {
                            offenders.Add(rel + " :: " + trimmed);
                            break;
                        }
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "身份哈希只能经 IdentityContext 取用（允许面见 Allowed）:" + string.Join(" | ", offenders));
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
