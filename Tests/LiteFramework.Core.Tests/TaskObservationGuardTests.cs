using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 任务观测与异常边界（《客户端架构重构演进路线图》§4.10）：
    /// M3-5 起 <c>.Forget()</c> **必须带观测**（产品代码禁裸调用，统一走 <c>TaskObservation.ForgetLogged</c>）；
    /// <c>SafeCall</c> 只允许外部回调边界文件（清单外即红）。
    /// </summary>
    public sealed class TaskObservationGuardTests
    {
        /// <summary>
        /// SafeCall 的合法边界（§4.10：只允许外部回调边界——Lua 回调、Unity/框架消息、UI 逻辑回调、
        /// 调度器回调、表现降级）。**文件即边界**：不在表里的文件出现 SafeCall 即红；表内计数只减不增。
        /// 业务路径不得用 catch-all 吞可恢复错误。
        /// </summary>
        private static readonly Dictionary<string, int> SafeCallBoundaries = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // Lua 回调边界（页面/桥脚本回调不得炸派发链）
            ["Assets/LiteFramework/Runtime/Adapters/XLua/Lua/EventBridge.cs"] = 2,
            ["Assets/LiteGame/Modules/Meta/MetaBridge.cs"] = 1,
            ["Assets/LiteGame/Modules/Room/RoomBridge.cs"] = 3,
            // UI 逻辑/控件回调边界（页面生命周期、控件事件、主题/静态文案绑定、红点规则）
            ["Assets/LiteFramework/Runtime/Modules/UI/UIForm.cs"] = 13,
            ["Assets/LiteFramework/Runtime/Modules/UI/Bind/UIBindEvents.cs"] = 4,
            ["Assets/LiteFramework/Runtime/Modules/UI/Bind/UIBindIndex.cs"] = 1,
            ["Assets/LiteFramework/Runtime/Modules/UI/Bind/UIBindListSource.cs"] = 1,
            ["Assets/LiteFramework/Runtime/Modules/UI/Localization/UiStaticTextBinder.cs"] = 1,
            ["Assets/LiteFramework/Runtime/Modules/UI/Style/UiThemeBinder.cs"] = 1,
            ["Assets/LiteFramework/Runtime/Modules/UI/Widgets/RedDotRegistry.cs"] = 1,
            // 调度器/Timeline 用户回调边界（单个回调抛异常不得中断派发）
            ["Assets/LiteFramework/Runtime/Core/Clock/GameScheduler.cs"] = 1,
            ["Assets/LiteFramework/Runtime/Core/Clock/TimelineRunner.cs"] = 1,
            // 表现降级边界（HUD 主题注入失败保持烘焙值，不阻断对局）
            ["Assets/LiteGame/Modules/Presentation/Battle/Rig/BattleViewAssets.cs"] = 1,
        };

        private static readonly string[] ProductRoots =
        {
            "Assets/LiteFramework/Runtime/Modules", "Assets/LiteGame", "Assets/LiteFramework", "Assets/LiteNet",
        };

        /// <summary>产品代码禁裸 <c>.Forget()</c>：无回调 = 异常静默；改用 <c>ForgetLogged(op)</c>。</summary>
        [Fact]
        public void Forget_必须带观测回调()
        {
            string root = RepoRoot();
            var offenders = new List<string>();

            foreach (string relativeRoot in ProductRoots)
            {
                string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    string[] lines = File.ReadAllLines(file);
                    for (int number = 0; number < lines.Length; number++)
                    {
                        string line = lines[number];
                        if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                        int index = line.IndexOf(".Forget(", StringComparison.Ordinal);
                        if (index < 0) continue;
                        int argsStart = index + ".Forget(".Length;
                        int depth = 1;
                        int cursor = argsStart;
                        while (cursor < line.Length && depth > 0)
                        {
                            if (line[cursor] == '(') depth++;
                            else if (line[cursor] == ')') depth--;
                            cursor++;
                        }
                        string args = line.Substring(argsStart, Math.Max(0, cursor - 1 - argsStart)).Trim();
                        if (args.Length == 0)
                            offenders.Add(rel + ":" + (number + 1));
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "根任务必须带观测（TaskObservation.ForgetLogged 或 .Forget(ex => ...)）:" + string.Join(" | ", offenders));
        }

        [Fact]
        public void SafeCall_只允许外部边界文件()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            foreach (string relativeRoot in ProductRoots)
            {
                string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (rel.EndsWith("Core/Util/SafeCall.cs", StringComparison.Ordinal)) continue;
                    int safe = Count(file, "SafeCall.");
                    if (safe == 0) continue;
                    if (!SafeCallBoundaries.TryGetValue(rel, out int allowed))
                        offenders.Add(rel + " SafeCall=" + safe + "（未登记为边界文件）");
                    else if (safe > allowed)
                        offenders.Add(rel + " SafeCall=" + safe + " > " + allowed);
                }
            }

            Assert.True(offenders.Count == 0, "SafeCall 只允许外部边界文件（清单外即红；边界内只减不增）:" + string.Join(" | ", offenders));
        }

        private static int Count(string file, string symbol)
        {
            int count = 0;
            foreach (string line in File.ReadAllLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.Contains(symbol)) count++;
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
