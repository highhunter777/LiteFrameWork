using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StjKind = System.Text.Json.JsonValueKind;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// friend 允许集守卫（T2 / AC1）：9 个产品模块的 InternalsVisibleTo 必须逐条登记在 arch/units.json。
    /// 作用域＝Modules/&lt;模块&gt;/AssemblyInfo.cs（与 asmdef 同目录），不递归子单元、不含 Editor/App/LiteNet。
    /// friends 存程序集名（与 InternalsVisibleTo 实参逐字一致），不是单元 id。
    /// </summary>
    public sealed class ModuleFriendAllowlistTests
    {
        /// <summary>作用域：9 个带 friend 表的产品模块（其余模块与子单元不在本批口径）。</summary>
        internal static readonly string[] ScopeModules =
        {
            "Battle", "Composition", "Config", "Dev", "Map", "Meta", "Procedure", "Session", "UI",
        };

        /// <summary>9 份 AssemblyInfo 的 friend 条数基线（AC1/T8：只减不增）。</summary>
        internal const int BaselineFriendCount = 55;

        [Fact]
        public void 模型必须为每个模块登记friends字段()
        {
            string root = RepoRoot();
            var errors = new List<string>();
            Dictionary<string, List<string>> model = FriendAllowlistRules.LoadModelFriends(root, errors);

            var missing = ScopeModules.Where(module => !model.ContainsKey(module)).ToList();

            Assert.True(errors.Count == 0,
                "arch/units.json 的 friend 允许集不完整（缺字段/类型错即失败）：" + string.Join(" | ", errors));
            Assert.True(missing.Count == 0,
                "以下模块在 arch/units.json 的 units[].friends 缺字段（需按各自 AssemblyInfo 逐条登记，9 模块当前 55 条）："
                + string.Join(", ", missing));
        }

        [Fact]
        public void 模块friend必须与模型逐条一致()
        {
            string root = RepoRoot();
            var errors = new List<string>();
            Dictionary<string, List<string>> model = FriendAllowlistRules.LoadModelFriends(root, errors);
            var offenders = new List<string>(errors);
            int declaredTotal = 0;

            foreach (string module in ScopeModules)
            {
                string file = Path.Combine(root, "Assets", "LiteGame", "Modules", module, "AssemblyInfo.cs");
                Assert.True(File.Exists(file), "缺 friend 表文件：" + Relative(root, file));
                List<string> declared = FriendAllowlistRules.ParseInternalsVisibleTo(File.ReadAllText(file));
                declaredTotal += declared.Count;

                if (!model.TryGetValue(module, out List<string> modelFriends))
                {
                    offenders.Add(module + " 模块在 arch/units.json 缺 friends 字段");
                    continue;
                }

                offenders.AddRange(FriendAllowlistRules.CompareFriends(module, modelFriends, declared));
            }

            Assert.True(declaredTotal == BaselineFriendCount,
                "9 个模块 AssemblyInfo 的 friend 条数基线是 " + BaselineFriendCount + "，实际 " + declaredTotal
                + "（少一条＝漏解析/漏文件，多一条＝新增 friend 未先改基线）；逐文件核对 Battle/Composition/Config/Dev/Map/Meta/Procedure/Session/UI");
            Assert.True(offenders.Count == 0,
                "模块 friend 与 arch/units.json 不一致（未登记即红；friends 写程序集名，不是单元 id）："
                + string.Join(" | ", offenders));
        }

        [Fact]
        public void 作用域只含9个模块根AssemblyInfo()
        {
            string root = RepoRoot();
            string modulesRoot = Path.Combine(root, "Assets", "LiteGame", "Modules");
            IReadOnlyList<string> files = FriendAllowlistRules.CollectModuleAssemblyInfoFiles(modulesRoot);

            Assert.Equal(ScopeModules.Length, files.Count);
            string[] discovered = files
                .Select(file => Path.GetFileName(Path.GetDirectoryName(file)))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] expected = ScopeModules.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, discovered);

            foreach (string file in files)
                Assert.True(file.StartsWith(modulesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    "friend 作用域只能落在 Assets/LiteGame/Modules 下：" + Relative(root, file));

            // 子单元/编辑器/外部程序集的 AssemblyInfo 真实存在，但不得被递归吞入本批作用域。
            var collected = new HashSet<string>(files.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            string[] outsideScope =
            {
                "Assets/LiteGame/App/AssemblyInfo.cs",
                "Assets/LiteGame/Editor/UITools/AssemblyInfo.cs",
                "Packages/com.litegame.net/Adapters/kcp2k/kcp/AssemblyInfo.cs",
                "Packages/com.litegame.sim/Properties/AssemblyInfo.cs",
            };
            foreach (string relative in outsideScope)
            {
                string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) continue;
                Assert.False(collected.Contains(Path.GetFullPath(full)),
                    "friend 作用域不得越界/递归纳入：" + relative + "（本批只守 9 个 Modules/* 根）");
            }
        }

        [Fact]
        public void 未登记friend的合成负例必须判违规()
        {
            const string assemblyInfo = @"
using System.Runtime.CompilerServices;
// [assembly: InternalsVisibleTo(""LiteGame.GhostLineComment"")]
/* [assembly: InternalsVisibleTo(""LiteGame.GhostBlockComment"")] */
[assembly: InternalsVisibleTo(""LiteGame.App"")]
[assembly: InternalsVisibleTo(""LiteGame.Modules.Session"", AllInternalsVisible = true)]
[assembly: InternalsVisibleTo(""LiteGame.App"")]
";
            List<string> declared = FriendAllowlistRules.ParseInternalsVisibleTo(assemblyInfo);
            Assert.Equal(new[] { "LiteGame.App", "LiteGame.Modules.Session", "LiteGame.App" }, declared.ToArray());

            List<string> offenders = FriendAllowlistRules.CompareFriends("Synthetic",
                new[] { "LiteGame.App" }, declared);
            Assert.Contains(offenders, offender => offender.Contains("LiteGame.Modules.Session"));
            Assert.Contains(offenders, offender => offender.Contains("重复"));
            Assert.DoesNotContain(offenders, offender => offender.Contains("Ghost"));

            List<string> expired = FriendAllowlistRules.CompareFriends("Synthetic",
                new[] { "LiteGame.App", "LiteGame.Stale" }, new[] { "LiteGame.App" });
            Assert.Contains(expired, offender => offender.Contains("LiteGame.Stale"));

            Assert.Empty(FriendAllowlistRules.CompareFriends("Synthetic",
                new[] { "LiteGame.App", "LiteGame.Editor" }, new[] { "LiteGame.Editor", "LiteGame.App" }));
        }

        [Fact]
        public void 合成子目录AssemblyInfo不得递归纳入()
        {
            string temp = Path.Combine(Path.GetTempPath(), "litegame-friend-scope-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(temp, "ModuleA", "SubModule"));
                File.WriteAllText(Path.Combine(temp, "ModuleA", "AssemblyInfo.cs"),
                    "[assembly: InternalsVisibleTo(\"Outer\")]");
                File.WriteAllText(Path.Combine(temp, "ModuleA", "SubModule", "AssemblyInfo.cs"),
                    "[assembly: InternalsVisibleTo(\"Nested\")]");

                IReadOnlyList<string> files = FriendAllowlistRules.CollectModuleAssemblyInfoFiles(temp);
                Assert.Single(files);
                List<string> friends = FriendAllowlistRules.ParseInternalsVisibleTo(File.ReadAllText(files[0]));
                Assert.Equal(new[] { "Outer" }, friends.ToArray());
            }
            finally
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }

        private static string Relative(string root, string path)
            => Path.GetRelativePath(root, path).Replace('\\', '/');

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

    /// <summary>
    /// friend 表解析/比对纯函数：供守卫与合成负例共用；不读写生产树（目录函数只读传入根）。
    /// </summary>
    internal static class FriendAllowlistRules
    {
        private static readonly Regex InternalsVisibleToPattern =
            new Regex("InternalsVisibleTo\\s*\\(\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant);

        /// <summary>解析 AssemblyInfo 文本的 InternalsVisibleTo 程序集名（注释内不算；保留顺序与重复）。</summary>
        internal static List<string> ParseInternalsVisibleTo(string text)
        {
            string code = StripComments(text ?? string.Empty);
            var names = new List<string>();
            foreach (Match match in InternalsVisibleToPattern.Matches(code))
                names.Add(match.Groups[1].Value.Trim());
            return names;
        }

        /// <summary>模型 declarations 与 AssemblyInfo 逐条比对；返回违规描述（空 = 一致）。</summary>
        internal static List<string> CompareFriends(string module, IEnumerable<string> model, IEnumerable<string> declared)
        {
            var offenders = new List<string>();
            string label = module + " 模块";
            if (model == null)
            {
                offenders.Add(label + " 在 arch/units.json 缺 friends 字段");
                return offenders;
            }

            List<string> modelList = model.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            List<string> declaredList = declared == null
                ? new List<string>()
                : declared.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();

            foreach (string duplicate in Duplicates(modelList))
                offenders.Add(label + " 模型重复登记 friend：" + duplicate);
            foreach (string duplicate in Duplicates(declaredList))
                offenders.Add(label + " AssemblyInfo 重复登记 friend：" + duplicate);

            var modelSet = new HashSet<string>(modelList, StringComparer.Ordinal);
            var declaredSet = new HashSet<string>(declaredList, StringComparer.Ordinal);

            foreach (string friend in declaredSet.Where(name => !modelSet.Contains(name)).OrderBy(name => name, StringComparer.Ordinal))
                offenders.Add(label + " 未登记 friend：" + friend + "（先写进 arch/units.json 的 units[].friends 再放行）");
            foreach (string friend in modelSet.Where(name => !declaredSet.Contains(name)).OrderBy(name => name, StringComparer.Ordinal))
                offenders.Add(label + " 模型登记了不存在的 friend：" + friend + "（允许表过期，删除该条）");

            return offenders;
        }

        /// <summary>收集 Modules/&lt;模块&gt;/AssemblyInfo.cs（只认模块根，不递归子目录）。</summary>
        internal static IReadOnlyList<string> CollectModuleAssemblyInfoFiles(string modulesRoot)
        {
            var files = new List<string>();
            if (!Directory.Exists(modulesRoot)) return files;
            foreach (string moduleDir in Directory.GetDirectories(modulesRoot)
                .OrderBy(dir => Path.GetFileName(dir), StringComparer.Ordinal))
            {
                string file = Path.Combine(moduleDir, "AssemblyInfo.cs");
                if (File.Exists(file)) files.Add(file);
            }
            return files;
        }

        /// <summary>读取 9 个模块在 units[].friends 的登记；缺字段/类型错进 errors。</summary>
        internal static Dictionary<string, List<string>> LoadModelFriends(string root, List<string> errors)
        {
            string path = Path.Combine(root, "arch", "units.json");
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement units = document.RootElement.GetProperty("units");
                foreach (JsonElement unit in units.EnumerateArray())
                {
                    string module = ScopeModuleOf(unit);
                    if (module == null) continue;

                    if (!unit.TryGetProperty("friends", out JsonElement friends) || friends.ValueKind != StjKind.Array)
                    {
                        errors.Add("arch/units.json 单元 " + UnitId(unit) + " 缺 friends 数组（模块 " + module + "）");
                        continue;
                    }

                    var entries = new List<string>();
                    foreach (JsonElement entry in friends.EnumerateArray())
                    {
                        if (entry.ValueKind != StjKind.String)
                        {
                            errors.Add("arch/units.json 单元 " + UnitId(unit) + " 的 friends 含非字符串条目（模块 " + module + "）");
                            continue;
                        }
                        entries.Add(entry.GetString());
                    }
                    result[module] = entries;
                }
            }
            return result;
        }

        private static string ScopeModuleOf(JsonElement unit)
        {
            string unitPath = unit.TryGetProperty("path", out JsonElement path) ? path.GetString() : null;
            if (string.IsNullOrEmpty(unitPath)) return null;
            unitPath = unitPath.Replace('\\', '/').TrimEnd('/');

            const string prefix = "Assets/LiteGame/Modules/";
            if (!unitPath.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string module = unitPath.Substring(prefix.Length);
            if (module.IndexOf('/') >= 0) return null; // 子单元（如 Battle/Abstractions）不纳入
            return ModuleFriendAllowlistTests.ScopeModules.Contains(module, StringComparer.Ordinal) ? module : null;
        }

        private static string UnitId(JsonElement unit)
            => unit.TryGetProperty("id", out JsonElement id) ? id.GetString() : "(无 id)";

        private static List<string> Duplicates(IEnumerable<string> values)
            => values.GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

        /// <summary>去注释但保留字符串字面量（IVT 实参不能被剥掉）；注释里的 friend 不算数。</summary>
        private static string StripComments(string text)
        {
            var builder = new StringBuilder(text.Length);
            int index = 0;
            while (index < text.Length)
            {
                char current = text[index];
                if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    while (index < text.Length && text[index] != '\n') index++;
                }
                else if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/')) index++;
                    index = Math.Min(index + 2, text.Length);
                }
                else if (current == '"')
                {
                    builder.Append(current);
                    index++;
                    while (index < text.Length && text[index] != '"')
                    {
                        if (text[index] == '\\' && index + 1 < text.Length)
                        {
                            builder.Append(text[index]);
                            index++;
                        }
                        builder.Append(text[index]);
                        index++;
                    }
                    if (index < text.Length)
                    {
                        builder.Append(text[index]);
                        index++;
                    }
                }
                else
                {
                    builder.Append(current);
                    index++;
                }
            }
            return builder.ToString();
        }
    }
}
