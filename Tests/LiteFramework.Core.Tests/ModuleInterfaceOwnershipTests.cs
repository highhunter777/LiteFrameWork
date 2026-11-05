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
    ///
    /// **例外：实现件接缝**（规范 04 §6.8 R-D）。签名里直接引用实现类型（如 `BattleContext`、
    /// `MatchSession`）的接口不是端口——它进契约包会让契约反向依赖实现，故住实现件内并在此登记。
    /// 判据：被登记的接口必须**只在实现件之间**使用；一旦被别的域消费，就必须改签名后搬回 `Abstractions/`。
    /// </summary>
    public sealed class ModuleInterfaceOwnershipTests
    {
        private static readonly Regex PublicInterface =
            new Regex(@"^\s*public\s+interface\s+\w", RegexOptions.Compiled);

        /// <summary>实现件接缝登记（签名引用实现类型、只在实现件之间使用；新增须写明为何不是端口）。</summary>
        private static readonly string[] SeamExceptions =
        {
            // 签名取 BattleContext（Battle 域实现类型）→ 契约包若收它会反向依赖实现件
            "Assets/LiteGame/Modules/Battle/IBattleDevDiagnostics.cs",
            // 返回 MatchSession（Battle/SessionSubModule 实现类型）→ 同上；消费方经 IBattleRunFactory 端口
            "Assets/LiteGame/Modules/Battle/SubModules/SessionSubModule/IMatchSessionFactory.cs",
        };

        [Fact]
        public void 模块接口必须住Abstractions()
        {
            string root = RepoRoot();
            string[] scanRoots =
            {
                Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Core"),
                Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Services"),
                Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Hosting"),
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
                    if (SeamExceptions.Contains(relative)) continue;
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
