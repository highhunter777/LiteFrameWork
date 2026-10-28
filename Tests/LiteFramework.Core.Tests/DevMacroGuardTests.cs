using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 开发宏边界（《客户端架构重构演进路线图》§4.8/§4.11 7）：
    /// `DEVELOPMENT_BUILD` / `LITEFRAMEWORK_DEBUG` 只允许出现在 `LiteGame.Dev`。
    /// M3-1 先按文件建存量基线（只减不增）；M3-6b 清零后转 hard fail。
    /// </summary>
    public sealed class DevMacroGuardTests
    {
        private static readonly Dictionary<string, int> Allowed = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs"] = 1,
            ["Assets/LiteClient/Runtime/Battle/BattleContext.cs"] = 2,
            ["Assets/LiteGame/App/Battle/BattleSession.cs"] = 2,
            ["Assets/LiteGame/App/Bootstrap/ContainerModule.cs"] = 4,
            ["Assets/LiteGame/App/Bootstrap/DevSessionModule.cs"] = 1,
            ["Assets/LiteGame/App/Bootstrap/UiServiceModule.cs"] = 1,
            ["Assets/LiteGame/App/Commands/Gm/GmSceneCommands.cs"] = 1,
            ["Assets/LiteGame/App/Debug/BattleDiagnostics.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugConsoleCommand.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugKernel.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugPerfSampler.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugRuntimeTuner.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugStatsDiscovery.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugTunable.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugTunableDiscovery.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DebugTunableText.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DevSession.cs"] = 1,
            ["Assets/LiteGame/App/Debug/DevStartupConfig.cs"] = 1,
            ["Assets/LiteGame/App/Debug/FeatureDebugTunables.cs"] = 1,
            ["Assets/LiteGame/App/Debug/GmClockCommands.cs"] = 1,
            ["Assets/LiteGame/App/Debug/NetDebug/NetworkDebug.cs"] = 1,
            ["Assets/LiteGame/App/Debug/NetDebug/NetworkDebugCommands.cs"] = 1,
            ["Assets/LiteGame/App/Debug/TestModeOptions.cs"] = 1,
            ["Assets/LiteGame/App/Debug/TestModeRuntime.cs"] = 1,
            ["Assets/LiteGame/App/Debug/WeakNet/NetHistory.cs"] = 1,
            ["Assets/LiteGame/App/Debug/WeakNet/WeakNetCommands.cs"] = 1,
            ["Assets/LiteGame/App/Debug/WeakNet/WeakNetPresets.cs"] = 1,
            ["Assets/LiteGame/App/Debug/WeakNet/WeakNetRuntime.cs"] = 1,
            ["Assets/LiteGame/App/Debug/WeakNet/WeakNetSnapshot.cs"] = 1,
            ["Assets/LiteGame/App/Debug/WeakNet/WeakNetWiring.cs"] = 1,
            ["Assets/LiteGame/App/Debug/WeaponDebugTunables.cs"] = 1,
            ["Assets/LiteGame/App/GameEntry.cs"] = 2,
            ["Assets/LiteGame/App/Presentation/Battle/AutoHeadshotRig.cs"] = 1,
            ["Assets/LiteGame/App/Presentation/Battle/BattleHeadshotDebugDrawer.cs"] = 1,
            ["Assets/LiteGame/App/Presentation/Battle/BattleViewRig.cs"] = 4,
            ["Assets/LiteGame/App/Presentation/Battle/LookTuning.cs"] = 1,
            ["Assets/LiteGame/App/Presentation/Hud/CrosshairFeel.cs"] = 1,
            ["Assets/LiteGame/App/Procedure/ProcedureMatch.cs"] = 2,
            ["Assets/LiteGame/LuaBridge/LuaBehaviourAdapter.cs"] = 1,
            ["Assets/LiteView/Animation/Drivers/GripTuning.cs"] = 1,
        };

        [Fact]
        public void 开发宏只允许出现在Dev装配层_存量只减不增()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            string[] roots = { "Assets/LiteClient", "Assets/LiteGame", "Assets/LiteFramework", "Assets/LiteView" };

            foreach (string relativeRoot in roots)
            {
                string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (rel.StartsWith("Assets/LiteGame/Dev/", StringComparison.Ordinal)) continue;
                    int count = CountConditionLines(file);
                    if (count == 0) continue;
                    if (!Allowed.TryGetValue(rel, out int allowed) || count > allowed)
                        offenders.Add(rel + " :: " + count + " > 基线 " + (Allowed.TryGetValue(rel, out int value) ? value : 0));
                }
            }

            Assert.True(offenders.Count == 0,
                "开发宏只能出现在 LiteGame.Dev；新增/增加即失败（M3-6b 清零）:" + string.Join(" | ", offenders));

            // 基线诚实性：条目一旦清零/减少，必须同步收紧基线——否则"清零进度"不可见（只减不增的另一面）
            var stale = new List<string>();
            foreach (var pair in Allowed)
            {
                string path = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                int actual = File.Exists(path) ? CountConditionLines(path) : 0;
                if (actual < pair.Value) stale.Add(pair.Key + " :: " + pair.Value + " -> " + actual);
            }
            Assert.True(stale.Count == 0, "基线条目已减少/清零——请从基线删除:" + string.Join(" | ", stale));
        }

        private static int CountConditionLines(string file)
        {
            int count = 0;
            foreach (string line in File.ReadAllLines(file))
            {
                string trimmed = line.TrimStart('\uFEFF').TrimStart();
                if (!trimmed.StartsWith("#if", StringComparison.Ordinal) && !trimmed.StartsWith("#elif", StringComparison.Ordinal)) continue;
                if (line.Contains("DEVELOPMENT_BUILD") || line.Contains("LITEFRAMEWORK_DEBUG")) count++;
            }
            return count;
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