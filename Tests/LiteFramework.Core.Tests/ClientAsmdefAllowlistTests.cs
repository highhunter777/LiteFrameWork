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
    /// 框架侧按角色分四件（Core/Services/Adapters/Hosting，B1 切框架）：边表按件登记；
    /// Services 不得引 Adapters/Hosting，Adapters 不得引 Hosting；产品模块不得引框架 Adapters。
    /// </summary>
    public sealed class ClientAsmdefAllowlistTests
    {
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Assets/LiteCodeGen/Runtime/LiteCodeGen.Runtime.asmdef"] = Array.Empty<string>(),
            ["Assets/LiteFramework/Editor/LiteFramework.Editor.asmdef"] = new[] { "LiteCodeGen.Runtime" },
            ["Assets/LiteFramework/Runtime/Core/LiteFramework.Core.asmdef"] = Array.Empty<string>(),
            ["Assets/LiteFramework/Runtime/Services/LiteFramework.Services.asmdef"] = new[] { "LiteCodeGen.Runtime", "LiteFramework.Core", "DOTween.Modules", "UniTask", "Unity.InputSystem", "Unity.TextMeshPro" },
            ["Assets/LiteFramework/Runtime/Adapters/LiteFramework.Adapters.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "UniTask", "Unity.InputSystem", "YooAsset", "xLuaMain" },
            ["Assets/LiteFramework/Runtime/Hosting/LiteFramework.Hosting.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "UniTask" },
            ["Assets/LiteGame/Adapters/Dev/LiteGame.Adapters.Dev.asmdef"] = new[] { "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting", "LiteGame.App", "LiteGame.Bootstrap", "LiteGame.Adapters.LuaBridge", "LiteNet", "LiteSync", "LiteSim", "Luban.Tables", "RoomServer.Application.Runtime", "RoomServer.Runtime", "UniTask", "Unity.TextMeshPro", "xLuaMain", "LiteGame.Shared", "LiteGame.Modules.UI", "LiteGame.Modules.Procedure", "LiteGame.Modules.Meta", "LiteGame.Modules.Session", "LiteGame.Modules.Map", "LiteGame.Modules.Dev", "LiteGame.Battle.Abstractions" },
            ["Assets/LiteGame/Adapters/LuaBridge/LiteGame.Adapters.LuaBridge.asmdef"] = new[] { "LiteGame.Core", "LiteGame.Shared", "LiteGame.Modules.Procedure", "LiteGame.Modules.Session", "LiteGame.Modules.UI", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting", "LiteNet", "LiteSync", "Luban.Runtime", "Luban.Tables", "UniTask", "xLuaMain", "LiteGame.Modules.Meta", "LiteGame.Modules.Map", "LiteGame.Modules.Dev", "LiteGame.Battle.Abstractions" },
            ["Assets/LiteGame/Adapters/Luban/LiteGame.Adapters.Luban.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting", "LiteGame.Modules.Config", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask" },
            ["Assets/LiteGame/Adapters/Unity/LiteGame.Adapters.Unity.asmdef"] = new[] { "LiteGame.Core", "Unity.InputSystem", "Cinemachine", "LiteSim", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting" },
            ["Assets/LiteGame/App/LiteGame.App.asmdef"] = new[] { "LiteGame.Bootstrap", "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting", "LiteGame.Adapters.LuaBridge", "LiteNet", "LiteSync", "LiteSim", "Luban.Tables", "UniTask", "Unity.TextMeshPro", "LiteGame.Shared", "LiteGame.Modules.UI", "LiteGame.Modules.Procedure", "LiteGame.Modules.Meta", "LiteGame.Modules.Session", "LiteGame.Modules.Map", "LiteGame.Modules.Dev", "LiteGame.Modules.Composition", "LiteGame.Battle.Abstractions" },
            ["Assets/LiteGame/Bootstrap/LiteGame.Bootstrap.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Hosting", "LiteFramework.Services", "UniTask" },
            ["Assets/LiteGame/Core/LiteGame.Core.asmdef"] = new[] { "LiteSim" },
            ["Assets/LiteGame/Editor/Core/LiteGame.Editor.Core.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting", "Unity.TextMeshPro" },
            ["Assets/LiteGame/Editor/LiteGame.Editor.asmdef"] = new[] { "LiteGame.Adapters.Luban", "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Adapters.Unity", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting", "LiteFramework.Editor", "LiteCodeGen.Runtime", "LiteGame.App", "LiteGame.Bootstrap", "LiteGame.Adapters.Dev", "LiteGame.Editor.Core", "LiteGame.Editor.UITools", "LiteGame.Adapters.LuaBridge", "LiteNet", "LiteSync", "LiteSim", "LiteSim.Editor", "DisciplineScanner.Editor", "Luban.Tables", "Luban.Runtime", "UniTask", "Unity.TextMeshPro", "xLuaMain", "YooAsset", "YooAsset.Editor", "LiteGame.Shared", "LiteGame.Modules.UI", "LiteGame.Modules.Procedure", "LiteGame.Modules.Meta", "LiteGame.Modules.Session", "LiteGame.Modules.Map", "LiteGame.Modules.Dev" },
            ["Assets/LiteGame/Editor/UITools/LiteGame.Editor.UITools.asmdef"] = new[] { "LiteGame.Modules.Config", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Adapters", "LiteFramework.Hosting", "LiteFramework.Editor", "LiteCodeGen.Runtime", "LiteGame.Adapters.Dev", "LiteGame.Editor.Core", "Luban.Runtime", "Luban.Tables", "Unity.TextMeshPro" },
            // 战斗域契约包（端口 + 行契约；零状态、零引擎，实现件单向依赖它）。
            ["Assets/LiteGame/Modules/Battle/Abstractions/LiteGame.Battle.Abstractions.asmdef"] = new[] { "LiteGame.Core", "LiteFramework.Core", "LiteGame.Modules.Input", "LiteNet", "LiteSim", "LiteSync", "UniTask" },
            // 战斗域唯一程序集（原 Modules.View + LiteGame.Presentation 合并而来）。
            ["Assets/LiteGame/Modules/Battle/LiteGame.Modules.Battle.asmdef"] = new[] { "LiteGame.Modules.Config", "LiteGame.Modules.Settings", "LiteGame.Modules.Input", "LiteGame.Modules.Camera", "LiteGame.Core", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteNet", "LiteSync", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "Unity.TextMeshPro", "LiteGame.Shared", "LiteGame.Battle.Abstractions" },
            // 相机薄段：主相机保证 + 相机服务只接框架装配入口与注入的工厂委托。
            ["Assets/LiteGame/Modules/Camera/LiteGame.Modules.Camera.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "UniTask" },
            ["Assets/LiteGame/Modules/Config/LiteGame.Modules.Config.asmdef"] = new[] { "UniTask", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "Luban.Tables", "LiteSim", "Luban.Runtime" },
            ["Assets/LiteGame/Modules/Dev/LiteGame.Modules.Dev.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteGame.Modules.Input", "LiteNet", "LiteSync", "LiteSim", "UniTask" },
            ["Assets/LiteGame/Modules/Composition/LiteGame.Modules.Composition.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteGame.Modules.Dev", "LiteGame.Modules.Input", "LiteNet", "LiteSync", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "LiteGame.Shared", "LiteGame.Modules.Config" },
            ["Assets/LiteGame/Modules/Input/LiteGame.Modules.Input.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteSim", "UniTask" },
            ["Assets/LiteGame/Modules/Map/LiteGame.Modules.Map.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteGame.Modules.Config", "LiteNet", "LiteSync", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask" },
            ["Assets/LiteGame/Modules/Meta/LiteGame.Modules.Meta.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteGame.Modules.Battle", "LiteGame.Modules.Config", "LiteNet", "LiteSync", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "xLuaMain" },
            ["Assets/LiteGame/Modules/Procedure/LiteGame.Modules.Procedure.asmdef"] = new[] { "LiteGame.Bootstrap", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteGame.Shared", "LiteGame.Modules.Camera", "LiteGame.Modules.Config", "LiteGame.Modules.Dev", "LiteGame.Modules.Input", "LiteGame.Modules.Map", "LiteGame.Modules.Meta", "LiteGame.Modules.Session", "LiteGame.Modules.UI", "LiteNet", "LiteSync", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "xLuaMain", "LiteGame.Battle.Abstractions" },
            ["Assets/LiteGame/Modules/Session/LiteGame.Modules.Session.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteGame.Modules.Config", "LiteGame.Modules.Dev", "LiteGame.Modules.Map", "LiteGame.Modules.Meta", "LiteNet", "LiteSync", "LiteSim", "Luban.Runtime", "Luban.Tables", "UniTask", "LiteGame.Battle.Abstractions" },
            ["Assets/LiteGame/Modules/Settings/LiteGame.Modules.Settings.asmdef"] = new[] { "UniTask", "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting" },
            // 共享常量面（含生成常量 LuaKeys 与共享路径常量）：零引用，谁都可以引它、它不引任何人。
            ["Assets/LiteGame/Modules/Shared/LiteGame.Shared.asmdef"] = Array.Empty<string>(),
            ["Assets/LiteGame/Modules/UI/LiteGame.Modules.UI.asmdef"] = new[] { "LiteFramework.Core", "LiteFramework.Services", "LiteFramework.Hosting", "LiteGame.Core", "LiteGame.Modules.Config", "Luban.Runtime", "Luban.Tables", "UniTask" },
            ["Packages/com.litegame.net/LiteNet.asmdef"] = Array.Empty<string>(),
            ["Packages/com.litegame.sim/Editor/LiteSim.Editor.asmdef"] = new[] { "LiteSim" },
            ["Packages/com.litegame.sim/LiteSim.asmdef"] = Array.Empty<string>(),
            ["Packages/com.litegame.sync/LiteSync.asmdef"] = new[] { "LiteNet", "LiteSim" },
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
            "Packages/com.litegame.sim",
            "Packages/com.litegame.net",
            "Packages/com.litegame.sync",
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
                    case "Assets/LiteFramework/Runtime/Services/LiteFramework.Services.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteFramework.Adapters", offenders);   // 服务实现不得依赖第三方/平台适配器
                        ForbidPrefix(refs, pair.Key, "LiteFramework.Hosting", offenders);    // 前三件不得引用宿主
                        Forbid(refs, pair.Key, "YooAsset", offenders);
                        Forbid(refs, pair.Key, "xLuaMain", offenders);
                        break;
                    case "Assets/LiteFramework/Runtime/Adapters/LiteFramework.Adapters.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteFramework.Hosting", offenders);    // 适配器不得引用宿主
                        break;
                    case "Assets/LiteFramework/Runtime/Hosting/LiteFramework.Hosting.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        break;
                    case "Assets/LiteGame/App/LiteGame.App.asmdef":
                        Forbid(refs, pair.Key, "LiteGame.Adapters.Dev", offenders);
                        ForbidPrefix(refs, pair.Key, "RoomServer.", offenders);
                        break;
                    case "Assets/LiteGame/Bootstrap/LiteGame.Bootstrap.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);          // 进程壳不认识产品实现
                        ForbidPrefix(refs, pair.Key, "LiteFramework.Adapters", offenders);
                        break;
                    case "Assets/LiteGame/Adapters/LuaBridge/LiteGame.Adapters.LuaBridge.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.App", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.Adapters.Dev", offenders);
                        break;
                }
                // 产品模块只经**模块自持端口**谈协作：适配器（LuaBridge/Unity/Dev）都是实现侧，
                // 由装配根注入；任何 Modules/* 反向引用实现程序集即红（P1 硬边，无例外）。
                if (pair.Key.StartsWith("Assets/LiteGame/Modules/", StringComparison.Ordinal))
                {
                    ForbidPrefix(refs, pair.Key, "LiteGame.Adapters.", offenders);
                    ForbidPrefix(refs, pair.Key, "LiteFramework.Adapters", offenders);   // 框架适配器同样不得进模块
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
            // 框架 Core 的"零引擎零第三方"由程序集边保证：noEngine + 零引用（引任何第三方都会编译失败）。
            AssertNoEngineReferences(root, "Assets/LiteFramework/Runtime/Core/LiteFramework.Core.asmdef");
            using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                "Assets/LiteFramework/Runtime/Core/LiteFramework.Core.asmdef"))))
            {
                Assert.Equal(0, doc.RootElement.GetProperty("references").GetArrayLength());
            }
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteSim", "Scripts"), offenders, root);
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteSim", "Systems"), offenders, root);
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
