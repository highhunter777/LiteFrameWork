using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// B2 契约可见性守卫：跨模块类型只认 Abstractions/（共享契约在 LiteGame.Core、共享常量在 Modules/Shared）。
    /// </summary>
    public sealed class CrossModuleContractLocationTests
    {
        /// <summary>当前例外：跨模块可见但并非端口（实现件/部署件/回退件）。只减不增——新增必须迁 Abstractions/Core。</summary>
        private static readonly Dictionary<string, string> Allowed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AnimationProfileConfig"] = "Config 表层配置单源（Battle 读行；随 Config 域收口再评估）",
            ["BattleScenes"] = "Map 场景事实件（Procedure/Session 消费；LiteSync 依赖，不能进 Core）",
            ["DeployBake"] = "Config 部署烙入（Composition/Meta 消费；UnityEngine 依赖）",
            ["DevHooks"] = "Dev 开发装配钩子（Composition/Session 消费；开发面例外）",
            ["IBattleDevDiagnostics"] = "Battle 开发诊断端口（Dev 消费；住 Battle 根，开发面例外）",
            ["LTextCatalog"] = "Config 文案目录（UI 消费；表装载件）",
            ["MapTableProjection"] = "Config 地图表投影（Map 消费；表装载件）",
            ["ProductSettings"] = "设置门面单源（UI 设置页直接读口；设计 §4.4 明确保留，P2 后评估注入化）",
            ["MetaEntryConfig"] = "Meta 部署配置（Session 消费；联机部署件）",
            ["NullDevSession"] = "Dev 空实现回退件（Composition/Procedure 消费；发布形态占位）",
            ["NullLocalTestHooks"] = "Dev 空实现回退件（Session 消费；发布形态占位）",
            ["SimParamsActivation"] = "Config 判定参数激活面（Session 消费；进房单点）",
            ["SimParamsGate"] = "Config 判定参数门（Session 消费；进房单点）",
            ["SimParamsOverrides"] = "Config 判定参数覆写（Session 消费；调参面）",
            ["TableLayout"] = "Config 表清单/领域布局单源（Map 消费；表装载件）",
        };

        private static readonly Regex TypeDeclaration = new Regex(
            @"^ {0,4}(?:(?:public|internal|protected|private|static|sealed|abstract|partial|readonly|ref|unsafe|new)\s+)*(?:class|interface|struct|enum|record)\s+([A-Za-z_]\w*)",
            RegexOptions.Multiline);

        [Fact]
        public void 跨模块类型必须住Abstractions或Core()
        {
            string root = RepoRoot();
            string modulesRoot = Path.Combine(root, "Assets", "LiteGame", "Modules");
            Assert.True(Directory.Exists(modulesRoot), "缺 Assets/LiteGame/Modules");

            string[] modules = Directory.GetDirectories(modulesRoot)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var definitions = new Dictionary<string, (string Module, string Relative)>(StringComparer.Ordinal);
            foreach (string module in modules)
            {
                foreach (string file in Directory.GetFiles(Path.Combine(modulesRoot, module), "*.cs", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFileName(file), "AssemblyInfo.cs", StringComparison.Ordinal)) continue;
                    string code = StripComments(File.ReadAllText(file));
                    foreach (Match match in TypeDeclaration.Matches(code))
                        definitions.TryAdd(match.Groups[1].Value, (module, Relative(root, file)));
                }
            }

            var moduleText = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string module in modules)
            {
                var pieces = new List<string>();
                foreach (string file in Directory.GetFiles(Path.Combine(modulesRoot, module), "*.cs", SearchOption.AllDirectories))
                    pieces.Add(StripComments(File.ReadAllText(file)));
                moduleText[module] = string.Join("\n", pieces);
            }

            var offenders = new List<string>();
            var crossModule = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in definitions)
            {
                string type = pair.Key;
                var consumers = modules
                    .Where(module => !string.Equals(module, pair.Value.Module, StringComparison.Ordinal)
                        && Regex.IsMatch(moduleText[module], @"\b" + Regex.Escape(type) + @"\b"))
                    .ToArray();
                if (consumers.Length == 0) continue;
                crossModule.Add(type);

                string relative = pair.Value.Relative.Replace('\\', '/');
                if (relative.Contains("/Abstractions/", StringComparison.Ordinal)
                    || relative.Contains("/Shared/", StringComparison.Ordinal)
                    || relative.Contains("/Generated/", StringComparison.Ordinal))
                    continue;
                if (Allowed.ContainsKey(type)) continue;

                offenders.Add($"{relative} → {type}（消费：{string.Join(",", consumers)}）");
            }

            // 允许表只减不增：登记项已不再是跨模块类型 = 必须删除该行（防先登记后漂移）
            foreach (string stale in Allowed.Keys.Where(type => !crossModule.Contains(type)).OrderBy(x => x, StringComparer.Ordinal))
                offenders.Add("allowlist 过期（已非跨模块类型，删除该行）：" + stale);

            Assert.True(offenders.Count == 0,
                "跨模块类型必须住 Modules/<域>/Abstractions/ 或 LiteGame.Core（共享常量除外）；"
                + "新增实现件例外须登记 allowlist 并说明去处：" + string.Join(" | ", offenders));
        }

        /// <summary>
        /// B2 同步面：Tests/** 的 &lt;Compile Include&gt; 源链接逐条核销——目标文件必须仍然存在 （端口迁移/目录搬动后死链会让 L1 在“找不到源文件”处静默换路径或构建失败）。
        /// </summary>
        [Fact]
        public void 测试源码链接必须指向现存文件()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            foreach (string csproj in Directory.GetFiles(Path.Combine(root, "Tests"), "*.csproj", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(csproj);
                foreach (Match match in Regex.Matches(text, @"<Compile Include=""([^""]+)"""))
                {
                    string include = match.Groups[1].Value;
                    if (include.IndexOf("..", StringComparison.Ordinal) < 0) continue;
                    string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, include.Replace('\\', Path.DirectorySeparatorChar)));
                    if (include.IndexOf('*') >= 0 || include.IndexOf('?') >= 0)
                    {
                        string dir = Path.GetDirectoryName(full);
                        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                            offenders.Add(Path.GetFileName(csproj) + " -> " + include + "（目录不存在）");
                        continue;
                    }
                    if (!File.Exists(full))
                        offenders.Add(Path.GetFileName(csproj) + " -> " + include);
                }
            }

            Assert.True(offenders.Count == 0, "测试源码链接存在死链（迁移后未同步 csproj）：" + string.Join(" | ", offenders));
        }

        private static string StripComments(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(text, @"//.*$", string.Empty, RegexOptions.Multiline);
        }

        private static string Relative(string root, string path)
            => Path.GetRelativePath(root, path).Replace('\\', '/');

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
