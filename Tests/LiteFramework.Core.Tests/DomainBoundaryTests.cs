using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            "Composition", "UI", "Input", "Settings", "Config", "Camera", "Map", "Dev",
        };

        /// <summary>批 2 后战斗域 `Abstractions/` 必须包含的业务端口。</summary>
        private static readonly string[] BattleAbstractions =
        {
            "IBattleView.cs",
            "IBattleReadModel.cs",
            "IMatchIdentity.cs",
            "IBattleViewGroup.cs",
            "IBattleViewGroupFactory.cs",
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

        /// <summary>
        /// 战斗包图守卫：叶子子模块不得依赖装配包 `Setup/`——那会形成包级回边。
        /// 历史：`Combat/BattleFeedbackAssembler` 持 `Setup/BattleViewAssets`，与 `Setup → Combat` 构成 2 环；
        /// 2026-11-03 把 `BattleViewAssets` 下移域根破环，本条守卫防再生。
        /// </summary>
        [Fact]
        public void 战斗叶子子模块不得引用装配包()
        {
            string root = RepoRoot();
            string battle = Path.Combine(root, "Assets", "LiteGame", "Modules", "Battle");
            string setup = Path.Combine(battle, "Setup");
            if (!Directory.Exists(setup)) return;

            HashSet<string> setupTypes = TopLevelTypeNames(setup);
            string[] leaves = { "Aim", "Arena", "Characters", "Combat", "Commands", "Hud", "Session", "Settlement" };

            var offenders = new List<string>();
            foreach (string leaf in leaves)
            {
                string dir = Path.Combine(battle, leaf);
                if (!Directory.Exists(dir)) continue;

                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string code = StripComments(File.ReadAllText(file));
                    foreach (string type in setupTypes)
                    {
                        if (Regex.IsMatch(code, @"\b" + Regex.Escape(type) + @"\b"))
                            offenders.Add(Relative(root, file) + " → " + type);
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "叶子子模块不得依赖装配包 Setup（会形成包级回边，按 R-D 把共用件下移域根）："
                + string.Join("; ", offenders));
        }

        /// <summary>
        /// 共享子模块（`Arena/`）不得依赖叶子子模块：它被 4 个包消费，只应认识域根与 `Abstractions/`。
        /// </summary>
        [Fact]
        public void 共享子模块不得引用叶子包()
        {
            string root = RepoRoot();
            string battle = Path.Combine(root, "Assets", "LiteGame", "Modules", "Battle");
            string shared = Path.Combine(battle, "Arena");
            if (!Directory.Exists(shared)) return;

            string[] leaves = { "Aim", "Characters", "Combat", "Commands", "Hud", "Session", "Settlement", "Setup" };
            var leafTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string leaf in leaves)
            {
                string dir = Path.Combine(battle, leaf);
                if (Directory.Exists(dir)) foreach (string t in TopLevelTypeNames(dir)) leafTypes.Add(t);
            }

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(shared, "*.cs", SearchOption.AllDirectories))
            {
                string code = StripComments(File.ReadAllText(file));
                foreach (string type in leafTypes)
                {
                    if (Regex.IsMatch(code, @"\b" + Regex.Escape(type) + @"\b"))
                        offenders.Add(Relative(root, file) + " → " + type);
                }
            }

            Assert.True(offenders.Count == 0,
                "共享子模块 Arena 只许依赖域根与 Abstractions；引用叶子包会制造回边：" + string.Join("; ", offenders));
        }

        private static HashSet<string> TopLevelTypeNames(string dir)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var pattern = new Regex(@"\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*(?:class|interface|struct|enum|record)\s+(\w+)");
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in pattern.Matches(StripComments(File.ReadAllText(file))))
                    names.Add(m.Groups[1].Value);
            return names;
        }

        /// <summary>去掉块注释、行注释与字符串字面量（注释里提到类型名不算依赖）。</summary>
        private static string StripComments(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    while (i < n && text[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    i += 2;
                }
                else if (c == '"')
                {
                    i++;
                    while (i < n && text[i] != '"')
                    {
                        if (text[i] == '\\') i++;
                        i++;
                    }
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
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
