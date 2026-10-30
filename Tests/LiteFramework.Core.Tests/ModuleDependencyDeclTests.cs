using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 模块依赖声明守卫：`IClientModuleGraph.DependsOn` 必须覆盖该模块实际消费（Require/Get）的产物面。
    ///
    /// 为什么需要：`DependsOn` 过去只是"注册序的可读副本"——漏写没有任何后果，因为 Host 只按注册序初始化。
    /// 后果是**隐性反向依赖**（后段模块的产物被前段模块在运行期 Require）在启动期查不出来，
    /// 只在首次真正执行到那段代码时才有行为（实例：UiServiceModule 的 ConfirmOf 懒取 FeedbackService）。
    ///
    /// 判据（静态，不编译模块实现）：模块 A 在 InitializeAsync 里 Require/Get 了类型 T，
    /// 而 T 由模块 B 产出（`Put` 或 `RegisterInstance`，与 ClientContext 同一容器来源），则 B 必须在 A 的 DependsOn 里。
    /// 解析不出产物类型的登记一律失败（不让"写个新形态就悄悄逃出治理"）。
    ///
    /// 允许表语义：`ExternalProducts` = 不由任何模块产出、而由框架装配面（ClientContext/ClientHost）提供的类型；
    /// `Baseline` = 已知欠账，修好后必须删除对应行（否则本用例失败——欠账不许沉淀）。
    /// </summary>
    public sealed class ModuleDependencyDeclTests
    {
        /// <summary>由框架装配面（而非某个模块）提供的产物：模块 Require 它们不算跨模块依赖。</summary>
        private static readonly HashSet<string> ExternalProducts = new HashSet<string>(StringComparer.Ordinal)
        {
            "ServiceContainer",
            "IServiceContainer",
            "ClientScope",
        };

        /// <summary>已知欠账（"消费方 -> 产出方"）：每条对应一处 DependsOn 缺失，修复后必须删除本行。
        /// **当前为空集**——最后一笔（"UiService ← Container"）已随确认缝端口化解：
        /// UI 段产出 <see cref="LiteFramework.UI.ConfirmDialogRouter"/>（可后绑定的 <c>IConfirmDialog</c>），
        /// 装配根在后段把 <c>FeedbackService.ConfirmAsync</c> 绑上去；UI 段不再 Require&lt;FeedbackService&gt;。</summary>
        private static readonly SortedSet<string> Baseline = new SortedSet<string>(StringComparer.Ordinal);

        [Fact]
        public void 模块DependsOn必须覆盖Require面()
        {
            string root = RepoRoot();
            var modules = LoadModules(root);
            var producers = BuildProducerIndex(modules);

            var offenders = new List<string>();
            var violatedPairs = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var module in modules)
            {
                foreach (string required in module.Required)
                {
                    if (!producers.TryGetValue(required, out var producer)) continue;   // 外部产物：由下面的完整性用例兜
                    if (string.Equals(producer.ClassName, module.ClassName, StringComparison.Ordinal)) continue;

                    string pair = module.ClassName + " <- " + producer.ClassName;
                    if (module.DeclaredDeps.Contains(producer.ClassName))
                    {
                        violatedPairs.Remove(pair);
                        continue;
                    }

                    violatedPairs.Add(pair);
                    if (Baseline.Contains(pair)) continue;
                    offenders.Add(pair + "（" + required + "：" + module.ClassName + " 未声明依赖 " + producer.ClassName + "）");
                }
            }

            foreach (string stale in Baseline)
                if (!violatedPairs.Contains(stale))
                    offenders.Add("欠账已消（请从 Baseline 删除）：" + stale);

            Assert.True(offenders.Count == 0,
                "模块 DependsOn 未覆盖 Require 面（依赖声明必须与实际消费一致）：" + string.Join(" | ", offenders));
        }

        [Fact]
        public void 模块产物与依赖声明必须可静态解析()
        {
            string root = RepoRoot();
            var modules = LoadModules(root);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in modules) known.Add(module.ClassName);

            var offenders = new List<string>();
            var producers = new Dictionary<string, ModuleNode>(StringComparer.Ordinal);

            foreach (var module in modules)
            {
                foreach (string unresolved in module.UnresolvedPuts)
                    offenders.Add(unresolved);

                foreach (string dep in module.DeclaredDeps)
                    if (!known.Contains(dep))
                        offenders.Add(module.ClassName + " 声明了不存在的模块：" + dep);

                foreach (string produced in module.Produced)
                {
                    if (producers.TryGetValue(produced, out var existing))
                    {
                        if (!string.Equals(existing.ClassName, module.ClassName, StringComparison.Ordinal))
                            offenders.Add("产物 " + produced + " 被两个模块登记：" + existing.ClassName + " / " + module.ClassName);
                        continue;
                    }
                    producers.Add(produced, module);
                }
            }

            // 反向：Require 到既无模块产出、也不在外部产物表的类型——依赖者无从声明，必须显式登记。
            foreach (var module in modules)
                foreach (string required in module.Required)
                    if (!producers.ContainsKey(required) && !ExternalProducts.Contains(required))
                        offenders.Add(module.ClassName + " Require 了无产出方的产物：" + required
                                      + "（若由框架装配面提供，请登记 ExternalProducts）");

            Assert.True(offenders.Count == 0,
                "模块产物/依赖面无法静态解析（新增登记形态请让本守卫可识别，或登记例外并说明）："
                + string.Join(" | ", offenders));
        }

        /// <summary>装载装配段并解析其依赖/产物/消费面：`Modules/**` 下的 `*Composition.cs` + 过渡期仍住 App/ 的两段。</summary>
        private static List<ModuleNode> LoadModules(string root)
        {
            string modulesDir = Path.Combine(root, "Assets", "LiteGame", "Modules");
            Assert.True(Directory.Exists(modulesDir), "模块目录缺失：Assets/LiteGame/Modules（模块搬动后须同步本守卫）");

            var files = new List<string>(Directory.GetFiles(modulesDir, "*Composition.cs", SearchOption.AllDirectories));
            // 过渡期：组合根两段（Ui 待 IUiLogicFactory 端口后归位）
            foreach (string name in new[] { "ContainerModule.cs", "UiServiceModule.cs" })
            {
                string path = Path.Combine(root, "Assets", "LiteGame", "App", name);
                if (File.Exists(path)) files.Add(path);
            }

            Assert.True(files.Count > 0, "未找到任何装配段（Modules/**/*Composition.cs ∪ App/{Container,Ui}Module.cs）");

            var modules = new List<ModuleNode>(files.Count);
            foreach (string file in files) modules.Add(Parse(file));
            return modules;
        }

        private static ModuleNode Parse(string path)
        {
            string code = StripComments(File.ReadAllText(path));
            // 模块身份＝模块名（`public string Name => "X";`），与 `DependsOn` 的声明口径一致；
            // 文件名只作回退（`XModule.cs` / `XComposition.cs` 去掉后缀）。
            var module = new ModuleNode { ClassName = ParseModuleName(code, Path.GetFileNameWithoutExtension(path)) };

            // ① 依赖声明：DependsOn => new[] { "Content", "Clocks" };
            Match declared = Regex.Match(code, @"DependsOn\s*=>\s*(.*?);", RegexOptions.Singleline);
            if (declared.Success)
            {
                foreach (Match item in Regex.Matches(declared.Groups[1].Value, "\"([^\"]+)\""))
                    module.DeclaredDeps.Add(item.Groups[1].Value);
            }

            // ② 消费面：context.Require<T>() / context.Get<T>()（含 lambda/局部函数内的懒取）
            foreach (Match item in Regex.Matches(code, @"context\.(?:Require|Get)\s*<\s*([^<>;]+?)\s*>"))
                module.Required.Add(LastSegment(item.Groups[1].Value));

            // ③ 产物面：Put / RegisterInstance（与 ClientContext 同一容器的两种登记形态）
            foreach (Match item in Regex.Matches(code, @"context\.Put\s*<\s*([\w.]+)\s*>\s*\("))
                module.Produced.Add(LastSegment(item.Groups[1].Value));
            foreach (Match item in Regex.Matches(code, @"context\.Put\s*\(\s*new\s+([\w.]+)"))
                module.Produced.Add(LastSegment(item.Groups[1].Value));
            foreach (Match item in Regex.Matches(code, @"_services\.RegisterInstance\s*<\s*([\w.]+)\s*>"))
                module.Produced.Add(LastSegment(item.Groups[1].Value));
            foreach (Match item in Regex.Matches(code, @"_services\.RegisterInstance\s*\(\s*new\s+([\w.]+)"))
                module.Produced.Add(LastSegment(item.Groups[1].Value));

            // ④ 实参形态（Put(变量) / Put(方法调用)）：先建"标识符 -> 类型"表再解析
            var identifierTypes = BuildIdentifierTypes(code);
            foreach (Match item in Regex.Matches(code, @"context\.Put\s*\(\s*([A-Za-z_]\w*)\s*\)"))
                Resolve(module, item.Groups[1].Value, identifierTypes, "Put");
            foreach (Match item in Regex.Matches(code, @"context\.Put\s*\(\s*([A-Za-z_]\w*)\s*\("))
                Resolve(module, item.Groups[1].Value, identifierTypes, "Put");
            foreach (Match item in Regex.Matches(code, @"_services\.RegisterInstance\s*\(\s*([A-Za-z_]\w*)\s*[,)]"))
                Resolve(module, item.Groups[1].Value, identifierTypes, "RegisterInstance");

            return module;
        }

        /// <summary>标识符 → 类型：字段声明、`= new X(...)` 赋值、局部方法返回值（`Put(EnsureCamera())` 这类）。</summary>
        private static Dictionary<string, string> BuildIdentifierTypes(string code)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Match item in Regex.Matches(code,
                         @"(?m)^\s*(?:private|internal|public|protected)\s+(?:(?:static|readonly|sealed|const|volatile)\s+)*([\w.]+(?:<[^;()]*>)?)\s+([A-Za-z_]\w*)\s*;"))
                map[item.Groups[2].Value] = LastSegment(item.Groups[1].Value);

            foreach (Match item in Regex.Matches(code, @"\b([A-Za-z_]\w*)\s*=\s*new\s+[\w.]*?([A-Za-z_]\w*)"))
                map[item.Groups[1].Value] = item.Groups[2].Value;

            foreach (Match item in Regex.Matches(code, @"\bAddComponent\s*<\s*([\w.]+)\s*>"))
                map.TryAdd("__AddComponent_" + item.Groups[1].Value, LastSegment(item.Groups[1].Value));

            var methodReturns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match item in Regex.Matches(code,
                         @"(?m)^\s*(?:private|internal|public|protected)\s+(?:(?:static|async|sealed|override|virtual)\s+)*([\w.]+(?:<[^()]*>)?)\s+([A-Za-z_]\w*)\s*\("))
                methodReturns[item.Groups[2].Value] = LastSegment(item.Groups[1].Value);

            // Put(局部方法()) 形态：被调方法的返回类型即产物类型
            foreach (var pair in methodReturns) map.TryAdd(pair.Key, pair.Value);

            foreach (Match item in Regex.Matches(code, @"\b([A-Za-z_]\w*)\s*=\s*([A-Za-z_]\w*)\s*\("))
            {
                if (methodReturns.TryGetValue(item.Groups[2].Value, out string returns))
                    map[item.Groups[1].Value] = returns;
            }

            return map;
        }

        private static void Resolve(ModuleNode module, string identifier, Dictionary<string, string> identifierTypes,
            string surface)
        {
            if (identifierTypes.TryGetValue(identifier, out string type))
            {
                module.Produced.Add(type);
                return;
            }

            module.UnresolvedPuts.Add(module.ClassName + " :: " + surface + "(" + identifier
                                      + ") 无法解析产物类型（请写成 Put<T>(...) 或让守卫可识别）");
        }

        private static Dictionary<string, ModuleNode> BuildProducerIndex(List<ModuleNode> modules)
        {
            var producers = new Dictionary<string, ModuleNode>(StringComparer.Ordinal);
            foreach (var module in modules)
                foreach (string type in module.Produced)
                    if (!producers.ContainsKey(type)) producers.Add(type, module);
            return producers;
        }

        /// <summary>
        /// 模块身份：优先取 `public string Name => "X";`（与 <c>ClientHost</c> 的按名校验同源），
        /// 回退到文件名去后缀（`InputModule` / `InputComposition` → `Input`）。
        /// </summary>
        private static string ParseModuleName(string code, string fileStem)
        {
            Match name = Regex.Match(code, @"public\s+string\s+Name\s*=>\s*""([^""]+)""");
            if (name.Success) return name.Groups[1].Value;

            if (fileStem.EndsWith("Module", StringComparison.Ordinal))
                return fileStem.Substring(0, fileStem.Length - "Module".Length);
            if (fileStem.EndsWith("Composition", StringComparison.Ordinal))
                return fileStem.Substring(0, fileStem.Length - "Composition".Length);
            return fileStem;
        }

        private static string LastSegment(string typeName)
        {
            int generic = typeName.IndexOf('<');
            if (generic > 0) typeName = typeName.Substring(0, generic);
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot >= 0) typeName = typeName.Substring(lastDot + 1);
            return typeName.Trim();
        }

        /// <summary>剥离注释（保留行号与字符串字面量）：注释里出现的 `Require&lt;T&gt;` 不算依赖。</summary>
        private static string StripComments(string code)
        {
            var builder = new StringBuilder(code.Length);
            bool lineComment = false, blockComment = false, inString = false, verbatim = false, inChar = false;

            for (int i = 0; i < code.Length; i++)
            {
                char current = code[i];
                char next = i + 1 < code.Length ? code[i + 1] : '\0';

                if (lineComment)
                {
                    if (current == '\n') { lineComment = false; builder.Append(current); }
                    else builder.Append(' ');
                    continue;
                }
                if (blockComment)
                {
                    if (current == '*' && next == '/') { blockComment = false; builder.Append("  "); i++; }
                    else builder.Append(current == '\n' ? '\n' : ' ');
                    continue;
                }
                if (inString)
                {
                    if (verbatim)
                    {
                        if (current == '"' && next == '"') { builder.Append("\"\""); i++; continue; }
                        if (current == '"') { inString = false; verbatim = false; }
                        builder.Append(current);
                        continue;
                    }
                    if (current == '\\' && next != '\0') { builder.Append(current).Append(next); i++; continue; }
                    if (current == '"') inString = false;
                    builder.Append(current);
                    continue;
                }
                if (inChar)
                {
                    if (current == '\\' && next != '\0') { builder.Append(current).Append(next); i++; continue; }
                    if (current == '\'') inChar = false;
                    builder.Append(current);
                    continue;
                }

                if (current == '/' && next == '/') { lineComment = true; builder.Append("  "); i++; continue; }
                if (current == '/' && next == '*') { blockComment = true; builder.Append("  "); i++; continue; }
                if (current == '@' && next == '"') { inString = true; verbatim = true; builder.Append("@\""); i++; continue; }
                if (current == '"') { inString = true; builder.Append(current); continue; }
                if (current == '\'') { inChar = true; builder.Append(current); continue; }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private sealed class ModuleNode
        {
            public string ClassName;
            public readonly HashSet<string> DeclaredDeps = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Produced = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Required = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<string> UnresolvedPuts = new List<string>();
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
