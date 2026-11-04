using System;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 装配段形态守卫：装配段必须"就地"住进所属单位目录（`Modules/&lt;单位&gt;/&lt;单位&gt;Segment.cs`）。
    ///
    /// 为什么需要：装配段曾在 `App/Bootstrap/Modules/`、功能域又在 `Modules/`——两处同名"模块"
    /// 导致"模块内实现只与自己有关"无法成为可校验的不变量；`ContainerModule.Assemble` 曾经 152 行，
    /// 是"装配根冒充功能模块"的直接证据。
    ///
    /// 判据：
    /// - `App/Bootstrap/Modules/` 不存在；
    /// - `Modules/**` 至少 18 个 `*Segment.cs`；
    /// - `Modules/**` 下不得出现 `*Composition.cs` / `*Module.cs`（后缀唯一，见规范 09 §2）。
    /// </summary>
    public sealed class SegmentBudgetTests
    {
        /// <summary>装配段数量下限（18 段全部就地）。</summary>
        private const int MinimumSegments = 18;

        [Fact]
        public void 模块目录不得残留于Bootstrap()
        {
            string root = RepoRoot();
            string legacy = Path.Combine(root, "Assets", "LiteGame", "App", "Bootstrap", "Modules");

            Assert.False(Directory.Exists(legacy),
                "装配段必须就地住进 Modules/<单位>/<单位>Segment.cs；`" + Relative(root, legacy) + "` 不应存在");
        }

        [Fact]
        public void 装配段必须就地且后缀唯一()
        {
            string root = RepoRoot();
            string modules = Path.Combine(root, "Assets", "LiteGame", "Modules");
            Assert.True(Directory.Exists(modules), "Modules 目录缺失：" + modules);

            var segments = Directory
                .GetFiles(modules, "*Segment.cs", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            Assert.True(segments.Length >= MinimumSegments,
                "`Modules/**` 下装配段不足 " + MinimumSegments + " 个（实测 " + segments.Length + "）："
                + string.Join(", ", segments.Select(p => Relative(root, p))));

            var strays = Directory
                .GetFiles(modules, "*.cs", SearchOption.AllDirectories)
                .Where(p =>
                {
                    string name = Path.GetFileName(p);
                    return name.EndsWith("Composition.cs", StringComparison.Ordinal)
                        || name.EndsWith("Module.cs", StringComparison.Ordinal);
                })
                .Select(p => Relative(root, p))
                .ToArray();

            Assert.True(strays.Length == 0,
                "`Modules/**` 下装配段后缀必须唯一（`<单位>Segment.cs`），不得再有 `*Composition.cs` / `*Module.cs`："
                + string.Join(", ", strays));
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
