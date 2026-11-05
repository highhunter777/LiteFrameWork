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
    /// B3 模块发行登记守卫：scripts/content/module-tiers.json 是「模块 → tier（base/script/data）→ 收集组」
    /// 与「改什么 → 发什么」矩阵的单源；本用例把它拉进 L1（CI），防止登记与真实模块/收集组漂移。
    /// B4-4c 追加：收集组路径必须落在归属模块的 <c>Content/</c> 下（顶层例外逐条给依据），
    /// 并校验收集路径真实存在——Sound 曾漂在 <c>Assets/Sound</c> 而登记归属 Shared，此类漂移由本用例拦住。
    /// </summary>
    public sealed class ModuleTierMatrixTests
    {
        /// <summary>收集组路径的登记例外：组 → （归属模块，依据）。模块自有资源必须落 `<模块>/Content/`，例外逐条说明。</summary>
        private static readonly Dictionary<string, (string Owner, string Reason)> CollectPathExceptions =
            new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["Scenes"] = ("LiteGame.Modules.Map", "2026-10-11 裁决：流程编排资产维持 Assets/Scenes 顶层"),
                ["Config"] = ("LiteGame.Modules.Config", "2026-10-11 裁决：表产物维持 Assets/GameData/Config 顶层"),
                ["Lua"] = ("LiteGame.Modules.Procedure", "资源收集目录专项设计 §5：L2 样板（代码即热更资源，目录即收集组）"),
            };

        private sealed class Manifest
        {
            public int Version { get; set; }
            public string[] Tiers { get; set; }
            public ModuleEntry[] Modules { get; set; }
            public ChangeEntry[] ChangeMatrix { get; set; }
        }

        private sealed class ModuleEntry
        {
            public string Id { get; set; }
            public string Path { get; set; }
            public string[] Tiers { get; set; }
            public string[] CollectGroups { get; set; }
            public string Ship { get; set; }
        }

        private sealed class ChangeEntry
        {
            public string Change { get; set; }
            public string[] Paths { get; set; }
            public string[] Ship { get; set; }
        }

        [Fact]
        public void 模块发行登记_层级与收集组覆盖()
        {
            string root = RepoRoot();
            Manifest manifest = LoadManifest(root);

            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var registeredPaths = new HashSet<string>(StringComparer.Ordinal);
            var coveredGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleEntry module in manifest.Modules ?? Array.Empty<ModuleEntry>())
            {
                if (string.IsNullOrWhiteSpace(module.Id)) { errors.Add("模块缺 id"); continue; }
                if (!ids.Add(module.Id)) errors.Add("模块 id 重复：" + module.Id);
                if (!Directory.Exists(Path.Combine(root, module.Path))) errors.Add("模块路径不存在：" + module.Id + " -> " + module.Path);
                registeredPaths.Add(module.Path.Replace('\\', '/'));

                if (module.Tiers == null || module.Tiers.Length == 0) errors.Add("模块缺 tier：" + module.Id);
                foreach (string tier in module.Tiers ?? Array.Empty<string>())
                {
                    if (!manifest.Tiers.Contains(tier, StringComparer.Ordinal))
                        errors.Add("未知 tier：" + module.Id + " -> " + tier);
                }

                foreach (string group in module.CollectGroups ?? Array.Empty<string>())
                {
                    coveredGroups.Add(group);
                }
            }

            var actualGroups = ReadCollectPaths(root).Keys.ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(actualGroups);

            foreach (string group in actualGroups)
                if (!coveredGroups.Contains(group)) errors.Add("收集组未登记到任何模块：" + group);
            foreach (string group in coveredGroups)
                if (!actualGroups.Contains(group)) errors.Add("登记了不存在的收集组：" + group);

            string modulesRoot = Path.Combine(root, "Assets", "LiteGame", "Modules");
            foreach (string dir in Directory.GetDirectories(modulesRoot))
            {
                string rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
                if (!registeredPaths.Contains(rel)) errors.Add("模块目录未登记：" + rel);
            }

            Assert.NotNull(manifest.ChangeMatrix);
            Assert.NotEmpty(manifest.ChangeMatrix);
            foreach (ChangeEntry change in manifest.ChangeMatrix)
            {
                if (string.IsNullOrWhiteSpace(change.Change)) errors.Add("矩阵行缺 change");
                if (change.Paths == null || change.Paths.Length == 0) errors.Add("矩阵行缺 paths：" + change.Change);
                foreach (string ship in change.Ship ?? Array.Empty<string>())
                    if (!manifest.Tiers.Contains(ship, StringComparer.Ordinal))
                        errors.Add("矩阵行 ship 不是 tier：" + change.Change + " -> " + ship);
            }

            Assert.True(errors.Count == 0, string.Join(" | ", errors));
        }

        /// <summary>收集组路径 ↔ 归属模块对齐：模块自有组必须落在 `<模块>/Content/` 下；例外表逐条给依据且归属一致。</summary>
        [Fact]
        public void 模块发行登记_收集组路径与模块归属对齐()
        {
            string root = RepoRoot();
            Manifest manifest = LoadManifest(root);
            Dictionary<string, List<string>> paths = ReadCollectPaths(root);

            var errors = new List<string>();
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedExceptions = new HashSet<string>(StringComparer.Ordinal);

            foreach (ModuleEntry module in manifest.Modules ?? Array.Empty<ModuleEntry>())
            {
                foreach (string group in module.CollectGroups ?? Array.Empty<string>())
                {
                    if (owner.ContainsKey(group))
                        errors.Add("收集组重复归属：" + group + " -> " + owner[group] + " / " + module.Id);
                    else
                        owner[group] = module.Id;

                    string expectedPrefix = module.Path.TrimEnd('/') + "/Content/";
                    foreach (string path in paths.TryGetValue(group, out List<string> list) ? list : new List<string>())
                    {
                        if (path.StartsWith(expectedPrefix, StringComparison.Ordinal)) continue;

                        if (CollectPathExceptions.TryGetValue(group, out (string Owner, string Reason) exception) == false)
                        {
                            errors.Add(module.Id + " 的收集组 " + group + " 路径不在 " + expectedPrefix + " 下：" + path);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(exception.Reason))
                            errors.Add("例外表缺依据：" + group);
                        if (exception.Owner != module.Id)
                            errors.Add("例外表归属不符：" + group + " -> " + exception.Owner + "（登记归属 " + module.Id + "）");
                        usedExceptions.Add(group);
                    }
                }
            }

            foreach (string group in paths.Keys)
                if (!owner.ContainsKey(group)) errors.Add("收集组无模块归属：" + group);
            foreach (string group in CollectPathExceptions.Keys)
            {
                if (!paths.ContainsKey(group)) errors.Add("例外表登记了不存在的收集组：" + group);
                else if (!usedExceptions.Contains(group)) errors.Add("例外表残留（该组已按模块 Content 路径对齐）：" + group);
            }

            foreach (KeyValuePair<string, List<string>> pair in paths)
                foreach (string path in pair.Value)
                    if (!Directory.Exists(Path.Combine(root, path)))
                        errors.Add("收集组路径不存在：" + pair.Key + " -> " + path);

            Assert.True(errors.Count == 0, string.Join(" | ", errors));
        }

        private static Manifest LoadManifest(string root)
        {
            string manifestPath = Path.Combine(root, "scripts", "content", "module-tiers.json");
            Assert.True(File.Exists(manifestPath), "缺 scripts/content/module-tiers.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), options);
            Assert.NotNull(manifest);
            Assert.Equal(1, manifest.Version);
            Assert.NotNull(manifest.Tiers);
            Assert.Equal(new[] { "base", "data", "script" }, manifest.Tiers.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            return manifest;
        }

        /// <summary>从收集器配置解析 组名 → 收集路径（配置是 Unity 产出的 YAML；只读文本，不引 Unity）。</summary>
        private static Dictionary<string, List<string>> ReadCollectPaths(string root)
        {
            string settingPath = Path.Combine(root, "Assets", "BundleCollectorSetting.asset");
            Assert.True(File.Exists(settingPath), "缺 Assets/BundleCollectorSetting.asset");

            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string current = null;
            foreach (string raw in File.ReadAllLines(settingPath))
            {
                string line = raw.Trim();
                Match groupMatch = Regex.Match(line, @"^- GroupName:\s*([A-Za-z0-9_]+)");
                if (groupMatch.Success)
                {
                    current = groupMatch.Groups[1].Value;
                    if (!result.ContainsKey(current)) result[current] = new List<string>();
                    continue;
                }

                Match pathMatch = Regex.Match(line, @"^- CollectPath:\s*(\S+)");
                if (pathMatch.Success && current != null) result[current].Add(pathMatch.Groups[1].Value);
            }

            return result;
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
