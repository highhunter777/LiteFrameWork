using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 接口落点守卫：产品侧 `public interface` 不得住技术层目录名（规范 02 §1.12）；
    /// 框架侧 `Core`/`Services`/`Hosting` 是设计内技术层（规范 02 §1.14），接口仍住模块自己的 `Abstractions/`。
    /// </summary>
    public sealed class ModuleInterfaceOwnershipTests
    {
        private static readonly Regex PublicInterface =
            new Regex(@"^\s*public\s+interface\s+\w", RegexOptions.Compiled);

        /// <summary>产品侧禁用的技术层目录名（规范 02 §1.12；`Core`/`Common`/`Utils` 只由 AR1 报告，不并入本测试）。</summary>
        private static readonly string[] ForbiddenProductDirectories =
        {
            "Abstractions", "SubModules", "Impl", "Interfaces", "Services",
        };

        private const string ProductRelativeRoot = "Assets/LiteGame/Modules/";
        private const string Ar1BaselineRelative = "arch/namespace-path-baseline.json";

        /// <summary>实现件接缝登记（签名引用实现类型、只在实现件之间使用；新增须写明为何不是端口）。</summary>
        private static readonly string[] SeamExceptions =
        {
            // 签名取 BattleContext（Battle 域实现类型）→ 契约包若收它会反向依赖实现件
            "Assets/LiteGame/Modules/Battle/IBattleDevDiagnostics.cs",
            // 同上：签名取 BattleContext，只能留在模块实现程序集
            "Assets/LiteGame/Modules/Battle/IBattleViewAttachment.cs",
            // 返回 BattleDomain（Battle/SessionSubModule 实现类型）→ 同上；消费方经 IBattleRunFactory 端口
            "Assets/LiteGame/Modules/Battle/SubModules/SessionSubModule/IBattleDomainFactory.cs",
        };

        [Fact]
        public void 模块接口不得住产品侧技术层目录()
        {
            string root = RepoRoot();
            var offenders = new List<string>();

            // 框架侧四层目录是设计内技术层（AGENTS 红线 16 / 规范 02 §1.14），不受产品侧禁名约束，只保留原意。
            string[] frameworkRoots =
            {
                Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Core"),
                Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Services"),
                Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Hosting"),
            };
            foreach (string scanRoot in frameworkRoots) offenders.AddRange(FrameworkOffenders(root, scanRoot));

            HashSet<string> ratcheted = Ar1LegacyProductDirectories(root);
            string productRoot = Path.Combine(root, "Assets", "LiteGame", "Modules");
            foreach (string file in Directory.EnumerateFiles(productRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (!DeclaresPublicInterface(file)) continue;
                string relative = Relative(root, file);
                string banned = UnratchetedProductDirectory(relative, ratcheted);
                if (banned == null) continue;
                if (SeamExceptions.Contains(relative)) continue;
                offenders.Add(relative + "（技术层目录 " + banned + "）");
            }

            Assert.True(offenders.Count == 0,
                "产品侧 public interface 不得住技术层目录名（规范 02 §1.12：Abstractions/SubModules/Impl/Interfaces/Services）；"
                + "框架侧仍须住所属模块的 Abstractions/。违规：" + string.Join(" | ", offenders));
        }

        /// <summary>框架侧：接口必须住所属模块目录树的 `Abstractions/`（框架保留技术层，不在产品侧禁令内）。</summary>
        private static IEnumerable<string> FrameworkOffenders(string root, string scanRoot)
        {
            foreach (string file in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (!DeclaresPublicInterface(file)) continue;
                string relative = Relative(root, file);
                if (relative.Contains("/Abstractions/")) continue;
                if (SeamExceptions.Contains(relative)) continue;
                yield return relative;
            }
        }

        private static bool DeclaresPublicInterface(string file)
            => File.ReadLines(file).Any(line => PublicInterface.IsMatch(line));

        /// <summary>最外层未被棘轮覆盖的产品侧技术层目录；全被覆盖或未命中返回 null。</summary>
        private static string UnratchetedProductDirectory(string relative, HashSet<string> ratcheted)
        {
            if (!relative.StartsWith(ProductRelativeRoot, StringComparison.Ordinal)) return null;
            string[] segments = relative.Substring(ProductRelativeRoot.Length).Split('/');
            string path = ProductRelativeRoot;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                path += segments[i];
                if (ForbiddenProductDirectories.Contains(segments[i], StringComparer.Ordinal) && !ratcheted.Contains(path))
                    return path;
                path += "/";
            }
            return null;
        }

        /// <summary>存量技术层目录＝AR1 基线 `dir-name` 项（单一来源，只减不增）；新增目录不在其中即红。</summary>
        private static HashSet<string> Ar1LegacyProductDirectories(string root)
        {
            string path = Path.Combine(root, Ar1BaselineRelative);
            Assert.True(File.Exists(path), "缺 AR1 基线（产品侧技术层目录存量的唯一来源）：" + Ar1BaselineRelative);
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var ratcheted = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement item in document.RootElement.GetProperty("items").EnumerateArray())
                {
                    if (!string.Equals(item.GetProperty("kind").GetString(), "dir-name", StringComparison.Ordinal)) continue;
                    string directory = item.GetProperty("file").GetString();
                    if (directory != null && directory.StartsWith(ProductRelativeRoot, StringComparison.Ordinal))
                        ratcheted.Add(directory);
                }
                return ratcheted;
            }
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
