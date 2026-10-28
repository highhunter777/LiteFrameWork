using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 任务观测与异常边界基线（《客户端架构重构演进路线图》§4.10）：
    /// 无观测的 `.Forget()` 与业务路径的 `SafeCall` 是 M3-5 的收紧对象；
    /// M3-1 先按文件建存量基线（只减不增），防止在收紧前继续扩散。
    /// </summary>
    public sealed class TaskObservationGuardTests
    {
        private static readonly Dictionary<string, int> ForgetAllowed = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Assets/LiteClient/Runtime/Audio/AudioService.cs"] = 2,
            ["Assets/LiteClient/UI/DialogService.cs"] = 1,
            ["Assets/LiteClient/UI/FeedbackService.cs"] = 3,
            ["Assets/LiteClient/UI/Transition/TransitionStages.cs"] = 1,
            ["Assets/LiteClient/UI/UIFocusCoordinator.cs"] = 1,
            ["Assets/LiteClient/UI/UINavigationController.cs"] = 1,
            ["Assets/LiteClient/UI/UIService.cs"] = 1,
            ["Assets/LiteClient/UI/Widgets/FlyTextPool.cs"] = 1,
            ["Assets/LiteClient/UI/Widgets/UIBubble.cs"] = 1,
            ["Assets/LiteGame/App/Bootstrap/InputModalProbe.cs"] = 1,
            ["Assets/LiteGame/App/Bootstrap/MemoryLoopProbe.cs"] = 1,
            ["Assets/LiteGame/App/GameEntry.cs"] = 1,
            ["Assets/LiteGame/App/Meta/MetaBridge.cs"] = 6,
            ["Assets/LiteGame/App/Procedure/ProcedureBattle.cs"] = 2,
            ["Assets/LiteGame/App/Procedure/ProcedureError.cs"] = 1,
            ["Assets/LiteGame/App/Procedure/ProcedureLaunch.cs"] = 1,
            ["Assets/LiteGame/App/Procedure/ProcedureMain.cs"] = 1,
            ["Assets/LiteGame/App/Procedure/ProcedureMatch.cs"] = 2,
            ["Assets/LiteGame/App/Procedure/ProcedurePatch.cs"] = 1,
            ["Assets/LiteGame/App/Procedure/ProcedurePreload.cs"] = 1,
            ["Assets/LiteGame/App/Ui/PageFlow.cs"] = 2,
            ["Assets/LiteGame/Editor/DevReload.cs"] = 1,
            ["Assets/LiteGame/Editor/Gm/GmScenePage.cs"] = 4,
            ["Assets/LiteGame/Editor/Gm/GmTestModePage.cs"] = 1,
            ["Assets/LiteGame/Editor/LuaDevRefill.cs"] = 1,
            ["Assets/LiteGame/LuaBridge/Bridge.Data.cs"] = 1,
            ["Assets/LiteFramework/Scripts/Async/SharedLoadCoordinator.cs"] = 2,
            ["Assets/LiteFramework/Scripts/Unity/App/AppLifetime.cs"] = 1,
            ["Assets/LiteView/Vfx/VfxService.cs"] = 1,
        };

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

        [Fact]
        public void Forget与SafeCall_存量基线只减不增()
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
                    if (rel.EndsWith("Core/Util/SafeCall.cs", StringComparison.Ordinal)) continue;
                    int forget = Count(file, ".Forget(");
                    int safe = Count(file, "SafeCall");
                    if (forget > 0 && (!ForgetAllowed.TryGetValue(rel, out int f) || forget > f))
                        offenders.Add(rel + " .Forget=" + forget + " > " + (f));
                    if (safe > 0 && (!SafeCallAllowed.TryGetValue(rel, out int s) || safe > s))
                        offenders.Add(rel + " SafeCall=" + safe + " > " + (s));
                }
            }

            Assert.True(offenders.Count == 0,
                "任务观测/SafeCall 只能减少；新增须先收口到观测回调或端口边界:" + string.Join(" | ", offenders));
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