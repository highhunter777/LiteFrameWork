using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 复杂度预算硬门禁（《客户端架构重构演进路线图》§4.11 9）：
    /// 类 >400 行、方法 >80 行为预算超限。**M3-7 7d 起**：
    /// ① 新增超限即失败；② 存量按"文件::类/方法 → 行数上限"冻结，**涨一行即失败**；
    /// ③ 降到阈值内必须从冻结表删除（否则"清账不可见"）。
    /// 例外只有一条且带理由：`UIService`（用户裁决"只留开关管线"，不拆开/关管线换行数）。
    /// Dev/Editor 程序集同样在冻结表内（不进发布包，但同样只减不增）。
    /// </summary>
    public sealed class ArchitectureBudgetTests
    {
        // M3-7 7d：存量基线转"冻结上限"——键 = 文件::类，值 = 当前行数上限（只许降，涨一行即失败）。
        // 范围含 Dev/Editor：它们不进发布包，但同样只减不增；UIService 为用户裁决例外（只留开关管线，不拆）。
        private static readonly Dictionary<string, int> ClassCaps = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Assets/LiteGame/Adapters/Unity/Cinemachine/CinemachineCameraService.cs::CinemachineCameraService"] = 592,
            ["Assets/LiteGame/Modules/Battle/BattleContext.cs::BattleContext"] = 491,
            ["Assets/LiteFramework/Runtime/Modules/UI/Bind/UIBindIndex.cs::UIBindIndex"] = 402,
            ["Assets/LiteFramework/Runtime/Modules/UI/UIService.cs::UIService"] = 650,
            ["Assets/LiteFramework/Runtime/Modules/Hosting/ClientHost.cs::ClientHost"] = 454,
            ["Assets/LiteFramework/Runtime/Modules/Animation/AnimationPlayer.cs::AnimationPlayer"] = 447,
            ["Assets/LiteFramework/Runtime/Core/DI/ServiceContainer.cs::ServiceContainer"] = 429,
            ["Assets/LiteFramework/Runtime/Core/Fsm/HierarchicalStageMachine.cs::HierarchicalStageMachine"] = 508,
            ["Assets/LiteGame/Adapters/Dev/Debug/Kernel/DebugKernel.cs::DebugKernel"] = 404,
            ["Assets/LiteGame/Adapters/Dev/Projection/DebugConsole.cs::DebugConsole"] = 660,
            ["Assets/LiteGame/Adapters/Dev/Projection/DebugPanelUiToolkitHost.cs::DebugPanelUiToolkitHost"] = 403,
            ["Assets/LiteGame/Adapters/Dev/Projection/DevHUD.cs::DevHUD"] = 522,
            ["Assets/LiteGame/Editor/Baking/SimMapBaker.cs::SimMapBaker"] = 646,
            ["Assets/LiteGame/Editor/Gm/GmLocalChainPage.cs::GmLocalChainPage"] = 527,
            ["Assets/LiteGame/Editor/Toolchain/Window/LiteGameToolboxWindow.cs::LiteGameToolboxWindow"] = 409,
            ["Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs::UiContractGenerator"] = 424,
            ["Assets/LiteGame/Editor/UiTools/Workbench/Window/UiWorkbenchWindow.cs::UiWorkbenchWindow"] = 457,
            ["Assets/LiteFramework/Runtime/Adapters/Unity/AnimationBackends/AnimatorAnimationBackend.cs::AnimatorAnimationBackend"] = 505,
            ["Assets/LiteGame/Modules/Battle/Characters/Drivers/CharacterLocomotionDriver.cs::CharacterLocomotionDriver"] = 524,
        };

        /// <summary>方法行数冻结上限（同 7d 口径；超 80 行的方法只许降）。</summary>
        private static readonly Dictionary<string, int> MethodCaps = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Assets/LiteFramework/Runtime/Adapters/XLua/Lua/CandidateLuaValidator.cs::CandidateLuaValidator.Validate"] = 87,
            ["Assets/LiteFramework/Runtime/Modules/Content/Abstractions/CandidateContentVerifier.cs::CandidateContentVerifier.Verify"] = 87,
            ["Assets/LiteFramework/Editor/EditorTools/BindGenerator.cs::BindGenerator.DoGenerate"] = 130,
            ["Assets/LiteFramework/Editor/Inspectors/BindNodeInspector.cs::BindNodeInspector.OnInspectorGUI"] = 99,
            ["Assets/LiteGame/App/ContainerModule.cs::ContainerModule.Assemble"] = 152,
            ["Assets/LiteGame/App/Bootstrap/Probes/InputModalProbe.cs::InputModalProbe.RunAsync"] = 103,
            ["Assets/LiteGame/Modules/Battle/Combat/BattleDamageNumberDriver.cs::BattleDamageNumberDriver.Tick"] = 82,
            ["Assets/LiteGame/Modules/Procedure/Stages/ProcedureBattle.cs::ProcedureBattle.RunAsyncCore"] = 109,
            ["Assets/LiteGame/Adapters/Dev/Projection/DebugPanelUiToolkitHost.cs::DebugPanelUiToolkitHost.AddTunableRow"] = 91,
            ["Assets/LiteGame/Editor/Gm/GmNetDiagPage.cs::GmNetDiagPage.Draw"] = 130,
            ["Assets/LiteGame/Editor/Gm/GmTestModePage.cs::GmTestModePage.DrawCloudGmBlock"] = 81,
            ["Assets/LiteGame/Editor/Core/Style/UiTextStyleTool.cs::UiTextStyleTool.Check"] = 83,
            ["Assets/LiteGame/Editor/Toolchain/Registry/EditorToolCatalog.cs::EditorToolCatalog.Load"] = 81,
            ["Assets/LiteGame/Editor/Core/ToolchainManifest.cs::ToolchainManifestLoader.Validate"] = 110,
            ["Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs::UiContractBatch.Diff"] = 90,
            ["Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs::UiContractBatch.Generate"] = 86,
            ["Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs::UiContractGenerator.Generate"] = 87,
            ["Assets/LiteGame/Editor/UiTools/Page/Manifest/UiPageManifestSync.cs::UiPageManifestSync.Sync"] = 144,
            ["Assets/LiteGame/Editor/UiTools/Page/Registration/UiPageRegistration.cs::UiPageRegistration.Register"] = 196,
            ["Assets/LiteGame/Editor/UiTools/Page/Scaffold/UiPageScaffold.cs::UiPageScaffold.Scaffold"] = 116,
            ["Assets/LiteGame/Editor/UiTools/Preview/UiDeviceProfileCatalog.cs::UiDeviceProfileCatalog.Validate"] = 81,
            ["Assets/LiteGame/Editor/UiTools/Preview/UiPreviewMatrixRunner.cs::UiPreviewMatrixRunner.Tick"] = 91,
            ["Assets/LiteGame/Editor/UiTools/Validation/Rules/Structure/ContractReferenceRule.cs::ContractReferenceRule.Check"] = 100,
            ["Assets/LiteGame/Modules/Battle/Characters/Drivers/CharacterLocomotionDriver.cs::CharacterLocomotionDriver.Tick"] = 177,
        };

        [Fact]
        public void 类与方法复杂度预算_存量只减不增()
        {
            string root = RepoRoot();
            var classOffenders = new List<string>();
            var methodOffenders = new List<string>();
            var classActual = new Dictionary<string, int>(StringComparer.Ordinal);
            var methodActual = new Dictionary<string, int>(StringComparer.Ordinal);
            string[] roots = { "Assets/LiteFramework/Runtime/Modules", "Assets/LiteGame", "Assets/LiteFramework" };

            foreach (string relativeRoot in roots)
            {
                string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    string[] lines = File.ReadAllLines(file);
                    var classes = FindClasses(lines);
                    foreach (var item in classes)
                    {
                        string key = rel + "::" + item.Name;
                        if (item.Span <= 400) continue;
                        classActual[key] = item.Span;              // 同名块（嵌套/重复声明）取后者；与基线键一一对应
                        if (!ClassCaps.TryGetValue(key, out int cap))
                            classOffenders.Add(key + " 新增超限 (" + item.Span + " 行)");
                        else if (item.Span > cap)
                            classOffenders.Add(key + " 增长 " + cap + " → " + item.Span);
                    }
                    foreach (var item in FindMethods(lines, classes))
                    {
                        string key = rel + "::" + item.Name;
                        if (item.Span <= 80) continue;
                        methodActual[key] = item.Span;
                        if (!MethodCaps.TryGetValue(key, out int cap))
                            methodOffenders.Add(key + " 新增超限 (" + item.Span + " 行)");
                        else if (item.Span > cap)
                            methodOffenders.Add(key + " 增长 " + cap + " → " + item.Span);
                    }
                }
            }

            // 收紧方向：拆完的件必须从冻结表删除（否则"清账不可见"）；上限只许降。
            var classStale = ClassCaps.Where(p => !classActual.ContainsKey(p.Key)).Select(p => p.Key).ToList();
            var methodStale = MethodCaps.Where(p => !methodActual.ContainsKey(p.Key)).Select(p => p.Key).ToList();

            Assert.True(classOffenders.Count == 0, "类预算硬门禁（新增/增长一律失败）:" + string.Join(" | ", classOffenders));
            Assert.True(methodOffenders.Count == 0, "方法预算硬门禁（新增/增长一律失败）:" + string.Join(" | ", methodOffenders));
            Assert.True(classStale.Count == 0, "类已降到 ≤400 行（或解析失效）——请从冻结表删除:" + string.Join(" | ", classStale));
            Assert.True(methodStale.Count == 0, "方法已降到 ≤80 行（或解析失效）——请从冻结表删除:" + string.Join(" | ", methodStale));
        }

        private sealed class Block
        {
            public string Name;
            public int Start;
            public int End;
            public int Span => End - Start + 1;
        }

        private static List<Block> FindClasses(string[] lines)
        {
            var result = new List<Block>();
            var regex = new Regex(@"\b(class|struct)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);
            for (int i = 0; i < lines.Length; i++)
            {
                Match match = regex.Match(StripCode(lines[i]));
                if (!match.Success) continue;
                int end = FindBlockEnd(lines, i);
                if (end < 0) continue;
                result.Add(new Block { Name = match.Groups[2].Value, Start = i, End = end });
            }
            return result;
        }

        private static List<Block> FindMethods(string[] lines, List<Block> classes)
        {
            var result = new List<Block>();
            var regex = new Regex(
                @"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|new|unsafe|extern|partial)\s+)+(?:(?:[A-Za-z_][\w<>,\[\]\.\?]*)\s+)?([A-Za-z_]\w*)\s*\(",
                RegexOptions.Compiled);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripCode(lines[i]);
                if (!code.Contains("(")) continue;
                Match match = regex.Match(code);
                if (!match.Success) continue;
                string name = match.Groups[1].Value;
                if (IsKeyword(name) || code.Contains("=>")) continue;
                int braceLine = FindBraceLine(lines, i);
                if (braceLine < 0) continue;
                int end = FindBlockEnd(lines, braceLine);
                if (end < 0) continue;
                Block owner = classes.Where(c => c.Start <= i && i <= c.End).OrderByDescending(c => c.Start).FirstOrDefault();
                string fullName = (owner == null ? "?" : owner.Name) + "." + name;
                result.Add(new Block { Name = fullName, Start = i, End = end });
            }
            return result;
        }

        private static int FindBraceLine(string[] lines, int start)
        {
            for (int i = start; i < lines.Length && i < start + 6; i++)
            {
                string code = StripCode(lines[i]);
                if (code.Contains("{")) return i;
                if (code.Contains(";")) return -1;
            }
            return -1;
        }

        private static int FindBlockEnd(string[] lines, int start)
        {
            int depth = 0;
            bool started = false;
            for (int i = start; i < lines.Length; i++)
            {
                foreach (char ch in StripCode(lines[i]))
                {
                    if (ch == '{') { depth++; started = true; }
                    else if (ch == '}')
                    {
                        depth--;
                        if (started && depth == 0) return i;
                    }
                }
            }
            return -1;
        }

        private static string StripCode(string line)
        {
            string code = Regex.Replace(line, @"//.*$", "");
            code = Regex.Replace(code, @"/\*.*?\*/", "");
            code = Regex.Replace(code, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            code = Regex.Replace(code, @"'(?:\\.|[^'\\])*'", "''");
            return code;
        }

        private static bool IsKeyword(string name)
        {
            switch (name)
            {
                case "if": case "for": case "foreach": case "while": case "switch":
                case "catch": case "using": case "lock": case "return": case "new":
                case "typeof": case "sizeof": case "nameof": case "base": case "this":
                    return true;
                default:
                    return false;
            }
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