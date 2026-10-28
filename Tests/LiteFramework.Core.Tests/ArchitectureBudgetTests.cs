using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 复杂度预算（《客户端架构重构演进路线图》§4.11 9）：
    /// 类 >400 行、方法 >80 行为预算超限；存量按“文件::类/方法”登记，新增超限即失败。
    /// M3-1 先把预算变成可见基线；M3-7 转 hard fail（存量允许，只减不增）。
    /// </summary>
    public sealed class ArchitectureBudgetTests
    {
        private static readonly HashSet<string> AllowedClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/LiteClient/UI/UIService.cs::UIService",
            "Assets/LiteView/SimView.cs::SimView",
            "Assets/LiteGame/Dev/Projection/DebugConsole.cs::DebugConsole",
            "Assets/LiteGame/Editor/Baking/SimMapBaker.cs::SimMapBaker",
            "Assets/LiteClient/Adapters/Platform.Unity/CinemachineCameraService.cs::CinemachineCameraService",
            "Assets/LiteClient/Runtime/Battle/BattleContext.cs::BattleContext",
            "Assets/LiteGame/Editor/Gm/GmLocalChainPage.cs::GmLocalChainPage",
            "Assets/LiteView/Animation/Drivers/CharacterLocomotionDriver.cs::CharacterLocomotionDriver",
            "Assets/LiteGame/Dev/Projection/DevHUD.cs::DevHUD",
            "Assets/LiteView/Animation/Backends/AnimatorAnimationBackend.cs::AnimatorAnimationBackend",
            "Assets/LiteFramework/Scripts/Core/Fsm/HierarchicalStageMachine.cs::HierarchicalStageMachine",
            "Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchWindow.cs::UiWorkbenchWindow",
            "Assets/LiteFramework/Scripts/Async/ClientHost.cs::ClientHost",
            "Assets/LiteFramework/Scripts/Core/Animation/AnimationPlayer.cs::AnimationPlayer",
            "Assets/LiteGame/Dev/LocalServer/LocalServerTransport.cs::LocalServerTransport",
            "Assets/LiteFramework/Scripts/Core/DI/ServiceContainer.cs::ServiceContainer",
            "Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs::UiContractGenerator",
            "Assets/LiteGame/Editor/Toolchain/LiteGameToolboxWindow.cs::LiteGameToolboxWindow",
            "Assets/LiteGame/Dev/Debug/DebugKernel.cs::DebugKernel",
            "Assets/LiteGame/Dev/Projection/DebugPanelUiToolkitHost.cs::DebugPanelUiToolkitHost",
            "Assets/LiteClient/UI/Bind/UIBindIndex.cs::UIBindIndex",
        };

        private static readonly HashSet<string> AllowedMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/LiteGame/Editor/UiTools/Page/UiPageRegistration.cs::UiPageRegistration.Register",
            "Assets/LiteView/Animation/Drivers/CharacterLocomotionDriver.cs::CharacterLocomotionDriver.Tick",
            "Assets/LiteGame/App/Bootstrap/ContainerModule.cs::ContainerModule.Assemble",
            "Assets/LiteGame/Editor/UiTools/Page/UiPageManifestSync.cs::UiPageManifestSync.Sync",
            "Assets/LiteGame/Editor/Gm/GmNetDiagPage.cs::GmNetDiagPage.Draw",
            "Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs::UiPageScaffold.Scaffold",
            "Assets/LiteGame/Editor/Toolchain/ToolchainManifest.cs::ToolchainManifestLoader.Validate",
            "Assets/LiteGame/App/Procedure/ProcedureBattle.cs::ProcedureBattle.RunAsyncCore",
            "Assets/LiteGame/App/Bootstrap/InputModalProbe.cs::InputModalProbe.RunAsync",
            "Assets/LiteGame/Editor/UiTools/Validation/Rules/ContractReferenceRule.cs::ContractReferenceRule.Check",
            "Assets/LiteGame/Editor/UiTools/Preview/UiPreviewMatrixRunner.cs::UiPreviewMatrixRunner.Tick",
            "Assets/LiteGame/Dev/Projection/DebugPanelUiToolkitHost.cs::DebugPanelUiToolkitHost.AddTunableRow",
            "Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs::UiContractBatch.Diff",
            "Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs::UiContractGenerator.Generate",
            "Assets/LiteClient/Adapters/Scripting.XLua/Lua/CandidateLuaValidator.cs::CandidateLuaValidator.Validate",
            "Assets/LiteFramework/Scripts/Core/Content/CandidateContentVerifier.cs::CandidateContentVerifier.Verify",
            "Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs::UiContractBatch.Generate",
            "Assets/LiteFramework/Scripts/Async/ClientHost.cs::ClientHost.ShutdownAsync",
            "Assets/LiteGame/Editor/Style/UiTextStyleTool.cs::UiTextStyleTool.Check",
            "Assets/LiteFramework/Scripts/Async/ClientHost.cs::ClientHost.InitializeAsync",
            "Assets/LiteGame/App/Presentation/Fx/BattleDamageNumberDriver.cs::BattleDamageNumberDriver.Tick",
            "Assets/LiteGame/Editor/UiTools/Preview/UiDeviceProfileCatalog.cs::UiDeviceProfileCatalog.Validate",
            "Assets/LiteGame/Editor/Gm/GmTestModePage.cs::GmTestModePage.DrawCloudGmBlock",
            "Assets/LiteGame/Editor/Toolchain/EditorToolCatalog.cs::EditorToolCatalog.Load",
        };

        [Fact]
        public void 类与方法复杂度预算_存量只减不增()
        {
            string root = RepoRoot();
            var classOffenders = new List<string>();
            var methodOffenders = new List<string>();
            int classCount = 0;
            int methodCount = 0;
            string[] roots = { "Assets/LiteClient", "Assets/LiteGame", "Assets/LiteFramework", "Assets/LiteView" };

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
                        if (item.Span <= 400) continue;
                        classCount++;
                        string key = rel + "::" + item.Name;
                        if (!AllowedClasses.Contains(key)) classOffenders.Add(key + " (" + item.Span + " 行)");
                    }
                    foreach (var item in FindMethods(lines, classes))
                    {
                        if (item.Span <= 80) continue;
                        methodCount++;
                        string key = rel + "::" + item.Name;
                        if (!AllowedMethods.Contains(key)) methodOffenders.Add(key + " (" + item.Span + " 行)");
                    }
                }
            }

            Assert.True(classOffenders.Count == 0, "新增超 400 行类（先拆或登记预算例外）:" + string.Join(" | ", classOffenders));
            Assert.True(methodOffenders.Count == 0, "新增超 80 行方法（先拆或登记预算例外）:" + string.Join(" | ", methodOffenders));
            Assert.True(classCount == AllowedClasses.Count,
                "预算扫描发现超 400 行类 " + classCount + " 个，基线 " + AllowedClasses.Count + " 个——解析器失效或基线需随拆分更新");
            Assert.True(methodCount == AllowedMethods.Count,
                "预算扫描发现超 80 行方法 " + methodCount + " 个，基线 " + AllowedMethods.Count + " 个——解析器失效或基线需随拆分更新");
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