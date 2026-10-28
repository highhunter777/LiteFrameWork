using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 任务观测与异常边界（《客户端架构重构演进路线图》§4.10）：
    /// M3-5 起 <c>.Forget()</c> **必须带观测**（产品代码禁裸调用，统一走 <c>TaskObservation.ForgetLogged</c>）；
    /// <c>SafeCall</c> 仍按文件基线只减不增（边界收紧在后续切片）。
    /// </summary>
    public sealed class TaskObservationGuardTests
    {
        private static readonly Dictionary<string, int> SafeCallAllowed = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Assets/LiteClient/Adapters/Scripting.XLua/Lua/EventBridge.cs"] = 2,
            ["Assets/LiteClient/UI/Bind/UIBindEvents.cs"] = 4,
            ["Assets/LiteClient/UI/Bind/UIBindIndex.cs"] = 1,
            ["Assets/LiteClient/UI/Bind/UIBindListSource.cs"] = 1,
            ["Assets/LiteClient/UI/Localization/UiStaticTextBinder.cs"] = 1,
            ["Assets/LiteClient/UI/Style/UiThemeBinder.cs"] = 1,
            ["Assets/LiteClient/UI/UIForm.cs"] = 13,
            ["Assets/LiteClient/UI/Widgets/RedDotRegistry.cs"] = 1,
            ["Assets/LiteGame/App/Meta/MetaBridge.cs"] = 1,
            ["Assets/LiteGame/App/Presentation/Battle/BattleViewAssets.cs"] = 1,
            ["Assets/LiteGame/App/Room/RoomBridge.cs"] = 3,
            ["Assets/LiteFramework/Scripts/Core/Clock/GameScheduler.cs"] = 1,
            ["Assets/LiteFramework/Scripts/Core/Clock/TimelineRunner.cs"] = 1,
            ["Assets/LiteFramework/Scripts/Core/Logging/LogTag.cs"] = 2,
        };

        private static readonly string[] ProductRoots =
        {
            "Assets/LiteClient", "Assets/LiteGame", "Assets/LiteFramework", "Assets/LiteView", "Assets/LiteNet",
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
        public void SafeCall_存量基线只减不增()
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
                    int safe = Count(file, "SafeCall");
                    if (safe > 0 && (!SafeCallAllowed.TryGetValue(rel, out int s) || safe > s))
                        offenders.Add(rel + " SafeCall=" + safe + " > " + s);
                }
            }

            Assert.True(offenders.Count == 0, "SafeCall 只能减少；新增须走端口边界:" + string.Join(" | ", offenders));
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
