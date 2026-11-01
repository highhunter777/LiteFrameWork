using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 接口自持守卫：Core 套件与 Modules 域的 `public interface` 必须住所属模块的 `Abstractions/`。
    ///
    /// 为什么需要：接口与实现同目录会让"模块只经端口交互"退化成文件约定——实现件被谁直接引用
    /// 无法从路径看出，适配器与业务件也会顺手把契约留在自己身边。先把端口位置钉死，域矩阵与
    /// asmdef 允许表才有稳定的引用目标。
    /// </summary>
    public sealed class ModuleInterfaceOwnershipTests
    {
        private static readonly Regex PublicInterface =
            new Regex(@"^\s*public\s+interface\s+\w", RegexOptions.Compiled);

        [Fact]
        public void 模块接口必须住Abstractions()
        {
            string root = RepoRoot();
            string[] scanRoots =
            {
                Path.Combine(root, "Assets", "LiteFramework", "Runtime", "Core"),
                Path.Combine(root, "Assets", "LiteFramework", "Runtime", "Modules"),
                Path.Combine(root, "Assets", "LiteGame", "Modules"),
            };

            var offenders = new List<string>();
            foreach (string scanRoot in scanRoots)
            {
                foreach (string file in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
                {
                    if (!File.ReadLines(file).Any(line => PublicInterface.IsMatch(line))) continue;
                    string relative = Relative(root, file);
                    if (relative.Contains("/Abstractions/")) continue;
                    offenders.Add(relative);
                }
            }

            Assert.True(offenders.Count == 0,
                "public interface 必须住所属模块的 Abstractions/（接口自持）:" + string.Join(" | ", offenders));
        }

        private static string Relative(string root, string path)
            => path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/');

        /// <summary>向上找含 Tests/Tests.slnx 的仓库根（与既有守卫同款定位）。</summary>
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
