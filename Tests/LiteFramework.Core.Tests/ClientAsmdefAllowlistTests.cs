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
    /// 每条边必须在 arch/units.json 内；未登记的新边、反向边、跨层边一律失败。
    /// 允许表是**架构契约**——新增引用必须先改表并说明理由，不能靠“能编过”默认放行。
    /// 框架侧按角色分四件（Core/Services/Adapters/Hosting，B1 切框架）：边表按件登记；
    /// Services 不得引 Adapters/Hosting，Adapters 不得引 Hosting；产品模块不得引框架 Adapters。
    /// </summary>
    public sealed class ClientAsmdefAllowlistTests
    {
        private static Dictionary<string, string[]> LoadAllowed(string root)
        {
            ArchModel model = ArchModel.Load(root);
            var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (UnitEntry unit in model.Units)
                allowed.Add(unit.Asmdef, unit.References ?? Array.Empty<string>());
            return allowed;
        }

        [Fact]
        public void 客户端程序集引用必须落在允许边表()
        {
            string root = RepoRoot();
            var allowed = LoadAllowed(root);
            var offenders = new List<string>();
            foreach (var pair in allowed)
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
            "Packages/com.litegame.framework",
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
            var allowed = LoadAllowed(root);
            var known = new HashSet<string>(allowed.Keys, StringComparer.Ordinal);
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
            var allowed = LoadAllowed(root);
            var offenders = new List<string>();
            foreach (var pair in allowed)
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
                    case "Packages/com.litegame.framework/Runtime/Services/LiteFramework.Services.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteFramework.Adapters", offenders);   // 服务实现不得依赖第三方/平台适配器
                        ForbidPrefix(refs, pair.Key, "LiteFramework.Hosting", offenders);    // 前三件不得引用宿主
                        Forbid(refs, pair.Key, "YooAsset", offenders);
                        Forbid(refs, pair.Key, "xLuaMain", offenders);
                        break;
                    case "Packages/com.litegame.framework/Runtime/Adapters/LiteFramework.Adapters.asmdef":
                        ForbidPrefix(refs, pair.Key, "LiteGame.", offenders);
                        ForbidPrefix(refs, pair.Key, "LiteFramework.Hosting", offenders);    // 适配器不得引用宿主
                        break;
                    case "Packages/com.litegame.framework/Runtime/Hosting/LiteFramework.Hosting.asmdef":
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
                if (pair.Key.StartsWith("Packages/com.litegame.framework/", StringComparison.Ordinal))
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
            AssertNoEngineReferences(root, "Packages/com.litegame.framework/Runtime/Core/LiteFramework.Core.asmdef");
            using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                "Packages/com.litegame.framework/Runtime/Core/LiteFramework.Core.asmdef"))))
            {
                Assert.Equal(0, doc.RootElement.GetProperty("references").GetArrayLength());
            }
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteSim", "Scripts"), offenders, root);
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteSim", "Systems"), offenders, root);
            ScanUnityReferences(Path.Combine(root, "Assets", "LiteGame", "Core"), offenders, root);
            Assert.True(offenders.Count == 0, "Core 不得出现 UnityEngine/第三方实现依赖（注释引用不计；Editor 工具面除外）：" + string.Join(" | ", offenders));
        }

        [Fact]
        public void 依赖方向必须严格递减()
        {
            ArchModel model = ArchModel.Load(RepoRoot());
            var byId = new Dictionary<string, UnitEntry>(StringComparer.Ordinal);
            foreach (UnitEntry unit in model.Units) byId[unit.Id] = unit;

            var offenders = new List<string>();
            foreach (UnitEntry unit in model.Units)
                foreach (string reference in unit.References)
                    if (byId.TryGetValue(reference, out UnitEntry target) && target.Rank >= unit.Rank)
                        offenders.Add(unit.Id + "(" + unit.Rank + ") -> " + reference + "(" + target.Rank + ")");

            Assert.True(offenders.Count == 0, "程序集依赖必须指向更低 rank：" + string.Join(" | ", offenders));
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
