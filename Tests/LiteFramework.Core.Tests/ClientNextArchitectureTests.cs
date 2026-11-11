using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StjValueKind = System.Text.Json.JsonValueKind;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// Next 子树静态契约（G2/G3、AC2）：入口/程序集/功能边界/旧 token；根路径取自 arch/client-roots.json。
    /// </summary>
    public sealed class ClientNextArchitectureTests
    {
        private const string RootsModel = "arch/client-roots.json";
        private const string BootNextScene = "Assets/Scenes/BootNext.unity";

        private static readonly string[] LegacyTokens =
        {
            "PageIds",
            "UIPageCatalog",
            "UIBindIndex",
            "BindGenerator",
            "IClientSegment",
            "ServiceContainer",
            "PageServices",
            "ProcedureId",
        };

        private static readonly string[] LegacyRootReferences = { "Assets/LiteGameNext" };

        private static readonly string[] BannedFeatureDirectories = { "Abstractions", "SubModules", "Services", "Impl" };

        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".json", ".prefab", ".unity", ".asset", ".mat",
            ".controller", ".txt", ".xml", ".uss", ".uxml", ".md",
        };

        [Fact]
        public void ClientRoots模型必须自洽()
        {
            ClientRoots roots = LoadClientRoots();
            Assert.StartsWith(roots.FinalRoot + "/", roots.NextRoot);
            Assert.StartsWith(roots.NextRoot + "/", roots.NextRuntime);
            Assert.StartsWith(roots.NextRoot + "/", roots.NextBootstrap);
            Assert.NotEqual(roots.NextRoot, roots.FrameworkNextRoot);
            Assert.NotEqual(roots.FinalRoot, roots.NextRoot);
        }

        [Theory]
        [InlineData("nextBootstrap", "GameEntry.cs")]
        [InlineData("nextRuntime", "App/ClientApp.cs")]
        [InlineData("nextRuntime", "Platform/Ui/UiRuntime.cs")]
        public void Next骨架必需文件必须存在(string rootField, string relativePath)
        {
            string root = RepoRoot();
            string required = LoadClientRoots().Required(rootField) + "/" + relativePath;
            Assert.True(File.Exists(ToFull(root, required)),
                "P0 骨架必需文件缺失：" + required + "（T2/T3 交付：BootNext → GameEntry → ClientApp → UiRuntime）");
        }

        [Fact]
        public void BootNext场景必须存在()
        {
            Assert.True(File.Exists(ToFull(RepoRoot(), BootNextScene)),
                "新启动场景缺失：" + BootNextScene + "（T2 交付；旧 Boot.unity 保位可回滚）");
        }

        [Fact]
        public void Next子树必须有AOT与hot程序集且AOT不引用hot()
        {
            string root = RepoRoot();
            ClientRoots roots = LoadClientRoots();
            string nextRoot = ToFull(root, roots.NextRoot);
            Assert.True(Directory.Exists(nextRoot), "Next 子树不存在：" + roots.NextRoot + "（原地 strangler 尚未落地）");

            List<AsmdefEntry> assemblies = new List<AsmdefEntry>();
            foreach (string scanRoot in new[] { roots.NextRoot, roots.FrameworkNextRoot })
            {
                string directory = ToFull(root, scanRoot);
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.asmdef", SearchOption.AllDirectories))
                    assemblies.Add(ReadAsmdef(root, file));
            }

            Assert.True(assemblies.Count > 0,
                "Next 子树没有任何 asmdef（需要 AOT 框架件 + AOT 引导件 + hot 应用件分件）："
                + roots.NextRoot + " / " + roots.FrameworkNextRoot + " 实际 " + Describe(assemblies));

            var aotFramework = assemblies.Where(entry => IsAotFramework(entry.Name)).ToList();
            var aotBootstrap = assemblies.Where(entry => IsAotBootstrap(entry.Name)).ToList();
            var hotApp = assemblies.Where(entry => IsHotApp(entry.Name)).ToList();
            var problems = new List<string>();
            RequireSingle(problems, "AOT 框架件（LiteFrameworkNext 或等价命名）", aotFramework);
            RequireSingle(problems, "AOT 引导件（LiteGameNext.Bootstrap 或等价命名）", aotBootstrap);
            RequireSingle(problems, "hot 应用件（LiteGameNext 或等价命名）", hotApp);

            foreach (AsmdefEntry entry in aotBootstrap)
                if (!entry.RelativePath.StartsWith(roots.NextBootstrap + "/", StringComparison.Ordinal))
                    problems.Add("AOT 引导件不在 " + roots.NextBootstrap + "/ 下：" + entry.RelativePath);
            foreach (AsmdefEntry entry in hotApp)
                if (!entry.RelativePath.StartsWith(roots.NextRuntime + "/", StringComparison.Ordinal))
                    problems.Add("hot 应用件不在 " + roots.NextRuntime + "/ 下：" + entry.RelativePath);

            var hotNames = new HashSet<string>(hotApp.Select(entry => entry.Name), StringComparer.Ordinal);
            foreach (AsmdefEntry aot in aotFramework.Concat(aotBootstrap))
                foreach (string reference in aot.References)
                    if (hotNames.Contains(reference))
                        problems.Add("AOT 引用 hot（G1 唯一硬边界）：" + aot.Name + " -> " + reference);

            Assert.True(problems.Count == 0,
                "AOT/hot 程序集契约不成立（设计 §4.1）：" + string.Join(" | ", problems) + "；实际：" + Describe(assemblies));
        }

        [Fact]
        public void Next功能目录必须内聚且不含技术层子目录()
        {
            string root = RepoRoot();
            ClientRoots roots = LoadClientRoots();
            string featuresRoot = ToFull(root, roots.NextRuntime + "/Features");
            Assert.True(Directory.Exists(featuresRoot),
                "功能根不存在：" + roots.NextRuntime + "/Features（T3 起创建 Account 等功能目录）");

            string[] features = Directory.GetDirectories(featuresRoot);
            Assert.True(features.Length > 0, "Features 下没有任何功能目录：" + featuresRoot);

            var problems = new List<string>();
            if (!Array.Exists(features, dir => string.Equals(Path.GetFileName(dir), "Account", StringComparison.Ordinal)))
                problems.Add("缺少 Account 功能目录（任务口径：登录页归 AccountFeature）");

            foreach (string feature in features)
            {
                string name = Path.GetFileName(feature);
                foreach (string nested in Directory.GetDirectories(feature, "*", SearchOption.AllDirectories))
                {
                    string nestedName = Path.GetFileName(nested);
                    if (Array.Exists(BannedFeatureDirectories, banned => string.Equals(banned, nestedName, StringComparison.OrdinalIgnoreCase)))
                        problems.Add("功能 " + name + " 内含技术层目录 " + Path.GetRelativePath(featuresRoot, nested).Replace('\\', '/'));
                }

                if (!File.Exists(Path.Combine(feature, name + "Feature.cs")))
                    problems.Add("功能 " + name + " 缺唯一公开入口 " + name + "Feature.cs");
            }

            Assert.True(problems.Count == 0,
                "功能目录边界违规（G2/§7：一功能一目录、入口唯一、禁技术层目录）：" + string.Join(" | ", problems));
        }

        [Fact]
        public void Next文本不得出现旧机制token()
        {
            List<string> hits = ScanNextText(LegacyTokens);
            Assert.True(hits.Count == 0,
                "Next 子树出现旧机制 token（G3/AC2 要求 0 处）：" + string.Join(" | ", hits.Take(20)) + Summarize(hits));
        }

        [Fact]
        public void Next文本不得引用旧根Assets_LiteGameNext()
        {
            List<string> hits = ScanNextText(LegacyRootReferences);
            Assert.True(hits.Count == 0,
                "Next 子树引用了被否决的旧根 Assets/LiteGameNext（A5：原地 strangler，不建独立根）："
                + string.Join(" | ", hits.Take(20)) + Summarize(hits));
        }

        // ---- 根路径单源（arch/client-roots.json） ----

        private sealed class ClientRoots
        {
            public string NextRoot;
            public string NextRuntime;
            public string NextBootstrap;
            public string FrameworkNextRoot;
            public string FinalRoot;

            public string Required(string field)
            {
                switch (field)
                {
                    case "nextRoot": return NextRoot;
                    case "nextRuntime": return NextRuntime;
                    case "nextBootstrap": return NextBootstrap;
                    case "frameworkNextRoot": return FrameworkNextRoot;
                    case "finalRoot": return FinalRoot;
                    default: throw new ArgumentOutOfRangeException(nameof(field), field, "client-roots.json 不认识的字段");
                }
            }
        }

        private static ClientRoots LoadClientRoots()
        {
            string path = ToFull(RepoRoot(), RootsModel);
            Assert.True(File.Exists(path),
                RootsModel + " 未落地：客户端重写的根路径必须单源取自该模型，测试不得硬编码 Next 子树路径（缺：" + path + "）");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonElement model = document.RootElement;
            return new ClientRoots
            {
                NextRoot = ReadRootField(model, "nextRoot"),
                NextRuntime = ReadRootField(model, "nextRuntime"),
                NextBootstrap = ReadRootField(model, "nextBootstrap"),
                FrameworkNextRoot = ReadRootField(model, "frameworkNextRoot"),
                FinalRoot = ReadRootField(model, "finalRoot"),
            };
        }

        private static string ReadRootField(JsonElement model, string field)
        {
            bool present = model.TryGetProperty(field, out JsonElement element) && element.ValueKind == StjValueKind.String;
            Assert.True(present, RootsModel + " 缺字符串字段：" + field);
            string value = (element.GetString() ?? string.Empty).Replace('\\', '/').Trim('/');
            Assert.False(value.Length == 0, RootsModel + " 字段为空：" + field);
            return value;
        }

        private static string ToFull(string root, string relative)
        {
            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        // ---- asmdef 解析 ----

        private sealed class AsmdefEntry
        {
            public string Name;
            public string RelativePath;
            public string[] References;
        }

        private static void RequireSingle(List<string> problems, string what, List<AsmdefEntry> found)
        {
            if (found.Count == 0) problems.Add("缺少 " + what);
            else if (found.Count > 1) problems.Add(what + " 应唯一，实际 " + found.Count + " 个：" + string.Join(",", found.Select(entry => entry.Name)));
        }

        private static bool IsEditorAssembly(string name)
        {
            return name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDevAssembly(string name)
        {
            return name.IndexOf(".Dev", StringComparison.OrdinalIgnoreCase) >= 0
                || name.EndsWith("Dev", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAotFramework(string name)
        {
            return !IsEditorAssembly(name) && !IsDevAssembly(name)
                && name.IndexOf("Framework", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("Next", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAotBootstrap(string name)
        {
            return !IsEditorAssembly(name) && !IsDevAssembly(name)
                && name.IndexOf("Bootstrap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHotApp(string name)
        {
            return !IsEditorAssembly(name) && !IsDevAssembly(name)
                && !IsAotFramework(name) && !IsAotBootstrap(name)
                && name.IndexOf("Next", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static AsmdefEntry ReadAsmdef(string root, string file)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonElement model = document.RootElement;
            string name = model.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == StjValueKind.String
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            var references = new List<string>();
            if (model.TryGetProperty("references", out JsonElement referencesElement)
                && referencesElement.ValueKind == StjValueKind.Array)
                foreach (JsonElement item in referencesElement.EnumerateArray())
                    if (item.ValueKind == StjValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                        references.Add(item.GetString());
            return new AsmdefEntry
            {
                Name = name,
                RelativePath = RelativePath(root, file),
                References = references.ToArray(),
            };
        }

        private static string Describe(List<AsmdefEntry> assemblies)
        {
            return assemblies.Count == 0
                ? "（无）"
                : string.Join(",", assemblies.Select(entry => entry.Name + "@" + entry.RelativePath));
        }

        private static string Summarize(List<string> hits)
        {
            return hits.Count > 20 ? " …共 " + hits.Count + " 处" : string.Empty;
        }

        private static string RelativePath(string root, string file)
        {
            return Path.GetRelativePath(root, file).Replace('\\', '/');
        }

        // ---- 文本扫描 ----

        private static List<string> ScanNextText(string[] needles)
        {
            string root = RepoRoot();
            ClientRoots roots = LoadClientRoots();
            string nextRoot = ToFull(root, roots.NextRoot);
            Assert.True(Directory.Exists(nextRoot), "Next 子树不存在，静态扫描无对象：" + roots.NextRoot);

            var hits = new List<string>();
            int scanned = 0;
            foreach (string file in TextFiles(nextRoot))
            {
                scanned++;
                string relative = RelativePath(root, file);
                string[] lines = SanitizedLines(file);
                for (int index = 0; index < lines.Length; index++)
                {
                    string line = lines[index].Replace('\\', '/');
                    foreach (string needle in needles)
                        if (line.IndexOf(needle, StringComparison.Ordinal) >= 0)
                            hits.Add(relative + ":" + (index + 1) + " :: " + needle);
                }
            }

            Assert.True(scanned > 0, "Next 子树没有任何可扫描文本文件（骨架未落地）：" + roots.NextRoot);
            return hits;
        }

        private static IEnumerable<string> TextFiles(string directory)
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                if (TextExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
        }

        // 契约 token 只按“代码引用”判定：C# 注释里引用废除清单不算违规（与 PageDefinitionGuardTests 同口径）。
        private static string[] SanitizedLines(string file)
        {
            string[] lines = File.ReadAllLines(file);
            if (!string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase)) return lines;

            var sanitized = new string[lines.Length];
            bool inBlockComment = false;
            for (int index = 0; index < lines.Length; index++)
            {
                string text = lines[index];
                if (inBlockComment)
                {
                    int blockEnd = text.IndexOf("*/", StringComparison.Ordinal);
                    if (blockEnd < 0)
                    {
                        sanitized[index] = string.Empty;
                        continue;
                    }
                    text = text.Substring(blockEnd + 2);
                    inBlockComment = false;
                }

                while (true)
                {
                    int blockStart = text.IndexOf("/*", StringComparison.Ordinal);
                    int lineStart = text.IndexOf("//", StringComparison.Ordinal);
                    if (lineStart >= 0 && (blockStart < 0 || lineStart < blockStart))
                    {
                        text = text.Substring(0, lineStart);
                        break;
                    }
                    if (blockStart < 0) break;
                    int blockEnd = text.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
                    if (blockEnd < 0)
                    {
                        text = text.Substring(0, blockStart);
                        inBlockComment = true;
                        break;
                    }
                    text = text.Substring(0, blockStart) + text.Substring(blockEnd + 2);
                }

                sanitized[index] = text;
            }

            return sanitized;
        }

        // 从测试输出目录向上找仓库根：同时存在 AGENTS.md 与 Assets/ 即命中（不硬编码本机绝对路径）。
        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Assets")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "无法从测试输出目录向上定位仓库根（需同时存在 AGENTS.md 与 Assets/）：" + AppContext.BaseDirectory);
        }
    }
}
