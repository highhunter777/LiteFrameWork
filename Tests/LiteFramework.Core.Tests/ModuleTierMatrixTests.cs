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
    /// </summary>
    public sealed class ModuleTierMatrixTests
    {
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
            string manifestPath = Path.Combine(root, "scripts", "content", "module-tiers.json");
            Assert.True(File.Exists(manifestPath), "缺 scripts/content/module-tiers.json");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), options);
            Assert.NotNull(manifest);
            Assert.Equal(1, manifest.Version);
            Assert.NotNull(manifest.Tiers);
            Assert.Equal(new[] { "base", "data", "script" }, manifest.Tiers.OrderBy(x => x, StringComparer.Ordinal).ToArray());

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

            string settingPath = Path.Combine(root, "Assets", "BundleCollectorSetting.asset");
            Assert.True(File.Exists(settingPath), "缺 Assets/BundleCollectorSetting.asset");
            string setting = File.ReadAllText(settingPath);
            var actualGroups = Regex.Matches(setting, @"- GroupName:\s*([A-Za-z0-9_]+)")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
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
