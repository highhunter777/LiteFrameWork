using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 客户端程序集引用边（《客户端架构重构演进路线图》§4.2/§4.11 1）：
    /// 每条边必须在允许表内；未登记的新边、反向边、跨层边一律失败。
    /// 允许表是**架构契约**——新增引用必须先改表并说明理由，不能靠“能编过”默认放行。
    /// </summary>
    public sealed class ClientAsmdefAllowlistTests
    {
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Assets/LiteFramework/Scripts/Core/LiteFramework.Core.asmdef"] = Array.Empty<string>(),
            ["Assets/LiteFramework/Scripts/Async/LiteFramework.Async.asmdef"] = new[] { "LiteFramework.Core", "UniTask" },
            ["Assets/LiteFramework/Abstractions/LiteFramework.Abstractions.asmdef"] = new[] { "LiteFramework.Async", "LiteFramework.Core", "LiteSim.Core", "UniTask" },
            ["Assets/LiteFramework/Scripts/Unity/LiteFramework.Unity.asmdef"] = new[] { "LiteFramework.Async", "LiteFramework.Core", "UniTask" },
            ["Assets/LiteCodeGen/Runtime/LiteCodeGen.Runtime.asmdef"] = Array.Empty<string>(),
            ["Assets/LiteCodeGen/Editor/LiteCodeGen.Editor.asmdef"] = new[] { "LiteCodeGen.Runtime" },
            ["Assets/LiteClient/Runtime/LiteClient.Runtime.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "LiteNet", "LiteSim.Core", "UniTask" },
            ["Assets/LiteClient/UI/LiteClient.UI.asmdef"] = new[] { "DOTween.Modules", "LiteCodeGen.Runtime", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "UniTask", "Unity.InputSystem", "Unity.TextMeshPro" },
            ["Assets/LiteClient/Adapters/Content.YooAsset/LiteClient.Content.YooAsset.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteClient.Runtime", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "UniTask", "YooAsset" },
            ["Assets/LiteClient/Adapters/Platform.Unity/LiteClient.Platform.Unity.asmdef"] = new[] { "Cinemachine", "LiteFramework.Abstractions", "LiteClient.Runtime", "LiteFramework.Async", "LiteFramework.Core", "LiteSim.Core", "LiteView", "UniTask", "Unity.InputSystem" },
            ["Assets/LiteClient/Adapters/Scripting.XLua/LiteClient.Scripting.XLua.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteClient.Runtime", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "UniTask", "xLuaMain" },
            ["Assets/LiteClient/Adapters/Serialization.Luban/LiteClient.Serialization.Luban.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteFramework.Async", "LiteFramework.Core", "LiteSim.Core", "Luban.Runtime", "Luban.Tables", "UniTask" },
            ["Assets/LiteView/LiteView.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "LiteSim.Core", "UniTask" },
            ["Assets/LiteGame/LuaBridge/LiteGame.LuaBridge.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteClient.Scripting.XLua", "LiteClient.Serialization.Luban", "LiteFramework.Async", "LiteFramework.Core", "LiteClient.UI", "Luban.Runtime", "Luban.Tables", "UniTask", "xLuaMain" },
            ["Assets/LiteGame/App/LiteGame.App.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteClient.Content.YooAsset", "LiteClient.Platform.Unity", "LiteClient.Runtime", "LiteClient.Scripting.XLua", "LiteClient.Serialization.Luban", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "LiteGame.LuaBridge", "LiteClient.UI", "LiteNet", "LiteSim.Core", "LiteView", "Luban.Runtime", "Luban.Tables", "UniTask", "Unity.TextMeshPro", "xLuaMain" },
            ["Assets/LiteGame/Dev/LiteGame.Dev.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteClient.Content.YooAsset", "LiteClient.Platform.Unity", "LiteClient.Runtime", "LiteClient.Scripting.XLua", "LiteClient.Serialization.Luban", "LiteClient.UI", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "LiteGame.App", "LiteGame.LuaBridge", "LiteNet", "LiteSim.Core", "LiteView", "Luban.Tables", "RoomServer.Application.Runtime", "RoomServer.Runtime", "UniTask", "Unity.TextMeshPro", "xLuaMain" },
            ["Assets/LiteGame/Editor/LiteGame.Editor.asmdef"] = new[] { "LiteFramework.Abstractions", "LiteClient.Content.YooAsset", "LiteClient.Runtime", "LiteClient.Scripting.XLua", "LiteClient.Serialization.Luban", "LiteCodeGen.Editor", "LiteCodeGen.Runtime", "LiteFramework.Async", "LiteFramework.Core", "LiteFramework.Unity", "LiteGame.App", "LiteGame.Dev", "LiteGame.LuaBridge", "LiteClient.UI", "LiteNet", "LiteSim.Core", "LiteSim.Core.Editor", "DisciplineScanner.Editor", "LiteView", "Luban.Tables", "Luban.Runtime", "UniTask", "Unity.TextMeshPro", "xLuaMain", "YooAsset", "YooAsset.Editor" },
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
                    if (!pair.Value.Contains(reference, StringComparer.Ordinal))
                        offenders.Add(pair.Key + " -> " + reference);
                }
            }
            Assert.True(offenders.Count == 0,
                "未登记的程序集引用边（先改允许表并说明理由）：" + string.Join(" | ", offenders));
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
                    case "Assets/LiteClient/Runtime/LiteClient.Runtime.asmdef":
                        Forbid(refs, pair.Key, "LiteView", offenders);
                        break;
                    case "Assets/LiteClient/UI/LiteClient.UI.asmdef":
                        Forbid(refs, pair.Key, "LiteClient.Runtime", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        break;
                    case "Assets/LiteView/LiteView.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteClient.", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        break;
                    case "Assets/LiteGame/App/LiteGame.App.asmdef":
                        Forbid(refs, pair.Key, "LiteGame.Dev", offenders);
                        ForbidPrefix(refs, pair.Key, "RoomServer.", offenders);
                        break;
                    case "Assets/LiteGame/LuaBridge/LiteGame.LuaBridge.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.App", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteGame.Dev", offenders);
                        break;
                }
                if (pair.Key.StartsWith("Assets/LiteClient/", StringComparison.Ordinal))
                    ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
            }
            Assert.True(offenders.Count == 0, "架构硬边违规：" + string.Join(" | ", offenders));
        }

        [Fact]
        public void 框架Core与SimCore不得依赖Unity()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            AssertNoEngineReferences(root, "Assets/LiteFramework/Scripts/Core/LiteFramework.Core.asmdef");
            AssertNoEngineReferences(root, "Assets/LiteFramework/Scripts/Async/LiteFramework.Async.asmdef");
            AssertNoEngineReferences(root, "Assets/LiteFramework/Abstractions/LiteFramework.Abstractions.asmdef");
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteFramework", "Scripts", "Core"), offenders, root);
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteSim", "Core"), offenders, root);
            Assert.True(offenders.Count == 0, "Core 不得出现 UnityEngine（注释引用不计；Editor 工具面除外）：" + string.Join(" | ", offenders));
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