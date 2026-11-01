using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 域边界守卫（批 2 起生效）：业务域必须**竖向自持**——`Modules/Battle/` 收拢战斗的判定/表现/UI，
    /// 不存在横向的 `Modules/View`、`Modules/Presentation`；业务端口住 `Modules/Battle/Abstractions/`，
    /// 业务域内子目录按**职责**命名（禁 `View`/`Presentation`/`Domain`/`Logic`）。
    ///
    /// 为什么需要：这三个横向目录把本属战斗的东西（HUD/Fx/音频/Rig/Look/SimView/动画）收在共享层，
    /// 业务系统无法自持自己的 UI 与表现；目录名一旦回潮，"新增业务域 = 目录 + 段 + Abstractions"就会失效。
    /// </summary>
    public sealed class DomainBoundaryTests
    {
        /// <summary>业务域子目录里禁止出现的技术分层名（`Abstractions/` 是唯一保留的技术名）。</summary>
        private static readonly string[] ForbiddenDirectoryNames = { "View", "Presentation", "Domain", "Logic" };

        /// <summary>8 个共享能力域（每个域一份命名空间；批 5 · A25）。</summary>
        private static readonly string[] SharedDomains =
        {
            "Composition", "Ui", "Input", "Settings", "Config", "Camera", "Map", "Dev",
        };

        /// <summary>批 2 后战斗域 `Abstractions/` 必须包含的业务端口。</summary>
        private static readonly string[] BattleAbstractions =
        {
            "IBattleView.cs",
            "IBattleReadModel.cs",
            "IMatchIdentity.cs",
            "IBattleViewGroup.cs",
            "IBattleViewGroupFactory.cs",
            "IBattleDevDiagnostics.cs",
        };

        [Fact]
        public void 横向表现域不得存在()
        {
            string root = RepoRoot();
            var offenders = new List<string>();

            foreach (string name in new[] { "View", "Presentation" })
            {
                string path = Path.Combine(root, "Assets", "LiteGame", "Modules", name);
                if (Directory.Exists(path)) offenders.Add(Relative(root, path));
            }

            Assert.True(offenders.Count == 0,
                "横向 UI/表现域必须并入业务域（战斗表现归 Modules/Battle）：" + string.Join(", ", offenders));
        }

        [Fact]
        public void 业务端口不得留在Core()
        {
            string root = RepoRoot();
            string coreBattle = Path.Combine(root, "Assets", "LiteGame", "Core", "Battle");

            Assert.False(Directory.Exists(coreBattle),
                "Core 只放基础设施与工具；业务端口须下沉 Modules/Battle/Abstractions（实测仍在：" + Relative(root, coreBattle) + "）");
        }

        [Fact]
        public void 战斗域Abstractions必须齐备()
        {
            string root = RepoRoot();
            string abstractions = Path.Combine(root, "Assets", "LiteGame", "Modules", "Battle", "Abstractions");

            var missing = BattleAbstractions
                .Where(name => !File.Exists(Path.Combine(abstractions, name)))
                .ToArray();

            Assert.True(missing.Length == 0,
                "`Modules/Battle/Abstractions/` 缺端口：" + string.Join(", ", missing));
        }

        [Fact]
        public void 业务域子目录必须按职责命名()
        {
            string root = RepoRoot();
            string modules = Path.Combine(root, "Assets", "LiteGame", "Modules");
            if (!Directory.Exists(modules)) return;

            var offenders = new List<string>();
            foreach (string domain in Directory.GetDirectories(modules))
            {
                foreach (string sub in Directory.GetDirectories(domain))
                {
                    string name = Path.GetFileName(sub);
                    if (ForbiddenDirectoryNames.Contains(name, StringComparer.Ordinal))
                        offenders.Add(Relative(root, sub));
                }
            }

            Assert.True(offenders.Count == 0,
                "业务域子目录须表达职责（Match/Arena/Characters/Combat/Aim/Hud/Settlement/Setup/Commands）；"
                + "禁 View/Presentation/Domain/Logic：" + string.Join(", ", offenders));
        }

        [Fact]
        public void 共享域文件必须使用各自命名空间()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            string modules = Path.Combine(root, "Assets", "LiteGame", "Modules");

            foreach (string domain in SharedDomains)
            {
                string directory = Path.Combine(modules, domain);
                if (!Directory.Exists(directory))
                {
                    offenders.Add("缺共享域目录:" + Relative(root, directory));
                    continue;
                }

                string expected = "LiteGame.Modules." + domain;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    // AssemblyInfo.cs 是程序集属性文件（\[assembly: ...\] 必须写在命名空间外）——不是域代码，跳过。
                    if (string.Equals(Path.GetFileName(file), "AssemblyInfo.cs", StringComparison.Ordinal)) continue;
                    string actual = FirstNamespace(file);
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                        offenders.Add(Relative(root, file) + " → " + (actual ?? "(无)") + "，期望 " + expected);
                }
            }

            Assert.True(offenders.Count == 0,
                "8 个共享域必须各自使用 LiteGame.Modules.<域> 命名空间（批 5 · A25）:" + string.Join(" | ", offenders));
        }

        /// <summary>
        /// `LiteGame.Core` 准入章程：**只放跨域契约 + 纯计算**，按类别分目录。
        /// 常量/资源标识归共享常量面 `LiteGame.Shared`（那里只有生成物与路径常量），不放这里——
        /// Core 一旦"什么都收"，就会退化成杂物抽屉，而它是 fan-in 最高的产品程序集。
        /// </summary>
        private static readonly string[] CoreDirectories = { "Dev", "Feedback", "Spatial" };

        [Fact]
        public void Core目录必须落在准入章程内()
        {
            string root = RepoRoot();
            string core = Path.Combine(root, "Assets", "LiteGame", "Core");
            Assert.True(Directory.Exists(core), "缺 LiteGame/Core 目录");

            var offenders = Directory.GetDirectories(core)
                .Select(Path.GetFileName)
                .Where(name => !CoreDirectories.Contains(name, StringComparer.Ordinal))
                .ToArray();

            Assert.True(offenders.Length == 0,
                "LiteGame.Core 只放跨域契约/纯计算（登记在 CoreDirectories）；常量归 LiteGame.Shared。未登记目录："
                + string.Join(", ", offenders));
        }

        private static string FirstNamespace(string file)
        {
            foreach (string line in File.ReadLines(file))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("namespace ", StringComparison.Ordinal)) continue;
                string rest = trimmed.Substring("namespace ".Length).Trim();
                int end = rest.IndexOfAny(new[] { ' ', '{', ';' });
                return end >= 0 ? rest.Substring(0, end) : rest;
            }
            return null;
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
