using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 页面单制品守卫（路线图 §4.7）：运行时页面程序集不得直读 #uiform / 清单 / 契约。
    /// 允许面只有承载表布局的运行时常量（TableLayout）与生成器本身。
    /// </summary>
    public sealed class PageDefinitionGuardTests
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/LiteFramework/Runtime/Adapters/Luban/TableLayout.cs",
            "Assets/LiteGame/Modules/Ui/Generated/UiPageCatalog.g.cs",
        };

        [Fact]
        public void 运行时页面程序集不得直读表清单契约()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            string[] roots = { "Assets/LiteFramework/Runtime/Modules", "Assets/LiteGame/App", "Assets/LiteGame/Modules", "Assets/LiteGame/Adapters/LuaBridge" };

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
                        if (line.Contains("Tbuiform") || line.Contains("TbUIForm")
                            || line.Contains("UiPageManifest") || line.Contains("UiPageContract")
                            || line.Contains("UIContractGenerator"))
                        {
                            offenders.Add(rel + " :: " + trimmed);
                            break;
                        }
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "运行时页面程序集只能读 PageDefinition/IPageCatalog（允许面见 Allowed）:" + string.Join(" | ", offenders));
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
