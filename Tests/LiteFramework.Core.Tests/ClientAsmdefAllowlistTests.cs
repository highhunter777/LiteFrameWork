using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 客户端程序集引用边（《客户端架构重构演进路线图》§4.2/§4.11 1）：
    /// 每条边必须在允许表内；未登记的新边、反向边、跨层边一律失败。
    /// 允许表是**架构契约**——新增引用必须先改表并说明理由，不能靠“能编过”默认放行。
    /// 框架侧合并为单程序集 `LiteFramework.Runtime`（+ `LiteFramework.Editor`）后，
    /// 原 `LiteFramework.*` / `LiteFramework.{Core,Async,Unity,Abstractions}` 之间的边收敛为一条。
    /// </summary>
    public sealed class ClientAsmdefAllowlistTests
    {
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Assets/LiteGame/Adapters/Luban/LiteGame.Adapters.Luban.asmdef"] = new[] { "LiteFramework.Runtime", "LiteGame.Modules.Config", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask" },
            ["Assets/LiteGame/Modules/Config/LiteGame.Modules.Config.asmdef"] = new[] { "LiteFramework.Runtime", "Luban.Tables", "LiteSim", "Luban.Runtime", "UniTask" },
            ["Assets/Tests/UI/PlayMode/LiteGame.UI.PlayModeTests.asmdef"] = new[] { "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Modules", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Runtime", "LiteGame.App", "LiteGame.Adapters.Dev", "LiteGame.Adapters.LuaBridge", "LiteSim", "LiteTesting.Core", "LiteTesting.Runtime", "UniTask", "Unity.InputSystem", "Unity.TextMeshPro", "UnityEngine.TestRunner", "xLuaMain" },
            ["Assets/Tests/EditMode/LiteGame.EditModeTests.asmdef"] = new[] { "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Modules", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Runtime", "Unity.InputSystem", "Cinemachine", "LiteCodeGen.Runtime", "LiteFramework.Editor", "LiteGame.App", "LiteGame.Adapters.Dev", "LiteGame.Editor", "LiteGame.Editor.Core", "LiteGame.Editor.UiTools", "LiteGame.Adapters.LuaBridge", "LiteNet", "LiteSim", "LiteSim.Editor", "RoomServer.Runtime", "LiteTesting.Core", "LiteTesting.Unity.Editor", "Luban.Runtime", "Luban.Tables", "UniTask", "Unity.TextMeshPro", "UnityEditor.TestRunner", "UnityEngine.TestRunner", "xLuaMain" },
            ["Assets/LiteGame/Modules/Settings/LiteGame.Modules.Settings.asmdef"] = new[] { "LiteFramework.Runtime", "UniTask" },
            ["Assets/LiteGame/Modules/Input/LiteGame.Modules.Input.asmdef"] = new[] { "LiteFramework.Runtime", "LiteSim", "UniTask", "LiteGame.Core" },
            // 相机薄段（批 2 · A8b）：主相机保证 + 相机服务只接框架装配入口与注入的工厂委托。
            ["Assets/LiteGame/Modules/Camera/LiteGame.Modules.Camera.asmdef"] = new[] { "LiteFramework.Runtime", "UniTask" },
            ["Assets/LiteFramework/Runtime/LiteFramework.Runtime.asmdef"] = new[] { "LiteCodeGen.Runtime", "DOTween.Modules", "UniTask", "Unity.InputSystem", "Unity.TextMeshPro", "YooAsset", "xLuaMain" },
            ["Assets/LiteFramework/Editor/LiteFramework.Editor.asmdef"] = new[] { "LiteCodeGen.Runtime" },
            ["Assets/LiteGame/Core/LiteGame.Core.asmdef"] = new[] { "LiteSim" },
            ["Assets/LiteGame/Modules/LiteGame.Modules.asmdef"] = new[] { "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteFramework.Runtime", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteNet", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "Unity.TextMeshPro", "xLuaMain" },
            // 合并后战斗域唯一程序集（原 Modules.View + LiteGame.Presentation）：引用集逐条登记（批 2 · A10a）。
            ["Assets/LiteGame/Modules/Battle/LiteGame.Modules.Battle.asmdef"] = new[] { "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteFramework.Runtime", "LiteNet", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "Unity.TextMeshPro" },
            ["Assets/LiteGame/Adapters/Unity/LiteGame.Adapters.Unity.asmdef"] = new[] { "Unity.InputSystem", "Cinemachine", "LiteSim", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteFramework.Runtime" },
            ["Assets/LiteSim/LiteSim.asmdef"] = Array.Empty<string>(),
            ["Assets/LiteSim/Editor/LiteSim.Editor.asmdef"] = new[] { "LiteSim" },
            ["Assets/LiteNet/LiteNet.asmdef"] = new[] { "LiteSim" },
            ["Assets/LiteCodeGen/Runtime/LiteCodeGen.Runtime.asmdef"] = Array.Empty<string>(),
            ["Assets/LiteGame/Adapters/LuaBridge/LiteGame.Adapters.LuaBridge.asmdef"] = new[] { "LiteGame.Modules", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteFramework.Runtime", "LiteNet", "Luban.Runtime", "Luban.Tables", "UniTask", "xLuaMain" },
            ["Assets/LiteGame/App/LiteGame.App.asmdef"] = new[] { "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Modules", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Runtime", "LiteGame.Adapters.LuaBridge", "LiteNet", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "Unity.TextMeshPro", "xLuaMain" },
            ["Assets/LiteGame/Adapters/Dev/LiteGame.Adapters.Dev.asmdef"] = new[] { "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Modules", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Runtime", "LiteGame.App", "LiteGame.Adapters.LuaBridge", "LiteNet", "LiteSim", "Luban.Tables", "RoomServer.Application.Runtime", "RoomServer.Runtime", "UniTask", "Unity.TextMeshPro", "xLuaMain" },
            ["Assets/LiteGame/Editor/Core/LiteGame.Editor.Core.asmdef"] = new[] { "LiteFramework.Runtime", "Unity.TextMeshPro" },
            ["Assets/LiteGame/Editor/UiTools/LiteGame.Editor.UiTools.asmdef"] = new[] { "LiteGame.Modules.Config", "LiteFramework.Runtime", "LiteFramework.Editor", "LiteCodeGen.Runtime", "LiteGame.Adapters.Dev", "LiteGame.Editor.Core", "Luban.Runtime", "Luban.Tables", "Unity.TextMeshPro" },
            ["Assets/LiteGame/Editor/LiteGame.Editor.asmdef"] = new[] { "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Modules", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Runtime", "LiteFramework.Editor", "LiteCodeGen.Runtime", "LiteGame.App", "LiteGame.Adapters.Dev", "LiteGame.Editor.Core", "LiteGame.Editor.UiTools", "LiteGame.Adapters.LuaBridge", "LiteNet", "LiteSim", "LiteSim.Editor", "DisciplineScanner.Editor", "Luban.Tables", "Luban.Runtime", "UniTask", "Unity.TextMeshPro", "xLuaMain", "YooAsset", "YooAsset.Editor" },
        };

        [Fact]
        public void 客户端程序集引用必须落在允许边表()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            foreach (var pair in Allowed)
            {
                string path = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), "允许表登记的 asmdef 不存在：" + pair.Key);
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var item in doc.RootElement.GetProperty("references").EnumerateArray())
                {
                    string reference = item.GetString();
                    if (string.IsNullOrEmpty(reference)) continue;
                    if (!Array.Exists(pair.Value, value => string.Equals(value, reference, StringComparison.Ordinal)))
                        offenders.Add(pair.Key + " -> " + reference);
                }
            }
            Assert.True(offenders.Count == 0,
                "未登记的程序集引用边（先改允许表并说明理由）：" + string.Join(" | ", offenders));
        }

        private static readonly string[] ClientRoots =
        {
            "Assets/LiteFramework",
            "Assets/LiteGame",
            "Assets/LiteSim",
            "Assets/LiteNet",
            "Assets/LiteCodeGen",
        };

        [Fact]
        public void 客户端根下所有asmdef必须登记在允许表()
        {
            // 允许表测试只遍历已登记项——新程序集若未登记会完全逃出依赖治理。
            // 本用例反向枚举客户端根，把“漏登记”本身钉成失败。
            string root = RepoRoot();
            var known = new HashSet<string>(Allowed.Keys, StringComparer.Ordinal);
            var offenders = new List<string>();
            foreach (string clientRoot in ClientRoots)
            {
                string dir = Path.Combine(root, clientRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                foreach (string asmdef in Directory.GetFiles(dir, "*.asmdef", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, asmdef).Replace('\\', '/');
                    if (!known.Contains(rel)) offenders.Add(rel);
                }
            }

            Assert.True(offenders.Count == 0,
                "客户端根下存在未登记 asmdef（先加入允许表并说明依赖）：" + string.Join(" | ", offenders));
        }

        [Fact]
        public void 硬边_反向与跨层引用一律失败()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            foreach (var pair in Allowed)
            {
                var refs = ReadRefs(root, pair.Key);
                switch (pair.Key)
                {
                    case "Assets/LiteGame/Core/LiteGame.Core.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteFramework.", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        Forbid(refs, pair.Key, "LiteNet", offenders);
                        ForbidPrefix(refs, pair.Key, "RoomServer.", offenders);
                        break;
                    case "Assets/LiteFramework/Runtime/LiteFramework.Runtime.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        break;
                    case "Assets/LiteGame/Modules/LiteGame.Modules.asmdef":
                        // 产品模块只经**模块自持端口**谈协作：适配器（LuaBridge/Unity/Dev）与表现层都是实现侧，
                        // 由装配根注入；模块不得反向认识任何实现程序集（P1 硬边）。
                        ForbidPrefix(refs, pair.Key, "LiteGame.Adapters.", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.Dev", offenders);
                        break;
                    case "Assets/LiteGame/Modules/Battle/LiteGame.Modules.Battle.asmdef":
                        // 战斗表现复用框架动画内核（LiteGame.View.Animation）——唯一登记的适配器例外。
                        ForbidPrefixExcept(refs, pair.Key, "LiteGame.Adapters.", "LiteGame.Adapters.Unity", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.Dev", offenders);
                        break;
                    case "Assets/LiteGame/App/LiteGame.App.asmdef":
                        Forbid(refs, pair.Key, "LiteGame.Adapters.Dev", offenders);
                        ForbidPrefix(refs, pair.Key, "RoomServer.", offenders);
                        break;
                    case "Assets/LiteGame/Adapters/LuaBridge/LiteGame.Adapters.LuaBridge.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.App", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.Adapters.Dev", offenders);
                        break;
                }
                if (pair.Key.StartsWith("Assets/LiteFramework/", StringComparison.Ordinal))
                    ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
            }
            Assert.True(offenders.Count == 0, "架构硬边违规：" + string.Join(" | ", offenders));
        }

        [Fact]
        public void 框架Core与SimCore不得依赖Unity()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            AssertNoEngineReferences(root, "Assets/LiteGame/Core/LiteGame.Core.asmdef");
            ScanEngineAndThirdParty(Path.Combine(root, "Assets", "LiteFramework", "Runtime", "Core"), offenders, root);
            ScanEngineAndThirdParty(Path.Combine(root, "Assets", "LiteFramework", "Runtime", "Abstractions"), offenders, root);
            ScanEngineAndThirdParty(Path.Combine(root, "Assets", "LiteFramework", "Runtime", "Async"), offenders, root, allowUniTask: true);
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteSim", "Core"), offenders, root);
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteGame", "Core"), offenders, root);
            Assert.True(offenders.Count == 0, "Core 不得出现 UnityEngine/第三方实现依赖（注释引用不计；Editor 工具面除外）：" + string.Join(" | ", offenders));
        }

        private static readonly string[] ThirdPartyNamespaces =
        {
            "UniTask", "Cysharp", "YooAsset", "XLua", "DG.Tweening", "Cinemachine",
            "Luban", "Newtonsoft", "Sirenix", "UnityEngine", "UnityEditor",
        };

        private static void ScanEngineAndThirdParty(string directory, List<string> offenders, string root, bool allowUniTask = false)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                if (file.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar)) continue;
                foreach (string line in File.ReadAllLines(file))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!trimmed.StartsWith("using ", StringComparison.Ordinal)) continue;
                    foreach (string ns in ThirdPartyNamespaces)
                    {
                        if (allowUniTask && (ns == "UniTask" || ns == "Cysharp")) continue;
                        if (trimmed.StartsWith("using " + ns, StringComparison.Ordinal))
                            offenders.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') + " :: " + trimmed);
                    }
                }
            }
        }

        private static void ScanUnityReferences(string directory, List<string> offenders, string root)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                if (file.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar)) continue;
                foreach (string line in File.ReadAllLines(file))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (line.Contains("UnityEngine")) offenders.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }

        private static void AssertNoEngineReferences(string root, string relative)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.GetProperty("noEngineReferences").GetBoolean(),
                relative + " 必须设置 noEngineReferences=true（Core/Abstractions 零引擎依赖）");
        }

        private static HashSet<string> ReadRefs(string root, string relative)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in doc.RootElement.GetProperty("references").EnumerateArray())
            {
                string value = item.GetString();
                if (!string.IsNullOrEmpty(value)) set.Add(value);
            }
            return set;
        }

        private static void Forbid(HashSet<string> refs, string owner, string target, List<string> offenders)
        {
            if (refs.Contains(target)) offenders.Add(owner + " -> " + target);
        }

        private static void ForbidPrefixExcept(HashSet<string> refs, string key, string prefix, string allowed, List<string> offenders)
        {
            foreach (string r in refs)
            {
                if (r == null || !r.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (string.Equals(r, allowed, StringComparison.Ordinal)) continue;
                offenders.Add(key + " 反向引用实现程序集：" + r);
            }
        }

        private static void ForbidPrefix(HashSet<string> refs, string owner, string prefix, List<string> offenders)
        {
            foreach (string reference in refs)
                if (reference.StartsWith(prefix, StringComparison.Ordinal))
                    offenders.Add(owner + " -> " + reference);
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
