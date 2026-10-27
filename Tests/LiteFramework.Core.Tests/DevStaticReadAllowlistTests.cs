using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// I5/I6 守卫（《客户端架构重构演进路线图》§3.4）：<c>TestModeRuntime</c> 直读**只减不增**。
    ///
    /// M0 允许存量直读（基线表），但新文件一律失败——经过 <c>IDevSession</c> 端口才是默认路径。
    /// 按**文件**做基线（而非逐站点）是刻意的：存量点位分散在 14 个文件里，逐站点表会随改写漂移，
    /// 文件级基线足够拦住"新代码又去摸静态"。
    /// </summary>
    public sealed class DevStaticReadAllowlistTests
    {
        /// <summary>存量直读基线（只减不增；新增文件必须改经 <c>IDevSession</c>）。</summary>
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/LiteGame/App/Debug/DebugKernel.cs",
            "Assets/LiteGame/App/Debug/DevSession.cs",          // 端口适配器（唯一合法转发点）
            "Assets/LiteGame/App/Debug/DevStartupConfig.cs",
            "Assets/LiteGame/App/Debug/FeatureDebugTunables.cs",
            "Assets/LiteGame/App/Debug/TestModeOptions.cs",
            "Assets/LiteGame/App/Debug/WeaponDebugTunables.cs",
            "Assets/LiteGame/App/Presentation/Battle/BattleHeadshotDebugDrawer.cs",
            "Assets/LiteGame/App/Presentation/Battle/BattleViewRig.cs",
            "Assets/LiteGame/App/Presentation/Fx/BattleDamageNumberDriver.cs",
            "Assets/LiteGame/App/Presentation/Fx/LaserSightPolicy.cs",
            "Assets/LiteGame/App/Procedure/MatchRulesSeeder.cs",
            "Assets/LiteGame/App/Procedure/ProcedureBattle.cs",
            "Assets/LiteGame/App/Procedure/ProcedureMain.cs",
            "Assets/LiteGame/App/Procedure/ProcedureMatch.cs",
            "Assets/LiteGame/Editor/Baking/HeadHitLineTuner.cs",
        };

        [Fact]
        public void TestModeRuntime直读仅限基线文件()
        {
            string root = RepoRoot();
            string[] roots =
            {
                Path.Combine(root, "Assets", "LiteClient"),
                Path.Combine(root, "Assets", "LiteGame", "App"),
                Path.Combine(root, "Assets", "LiteGame", "Editor"),
            };

            var offenders = new List<string>();
            foreach (string dir in roots)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (!File.ReadAllText(file).Contains("TestModeRuntime.")) continue;
                    string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (!Allowed.Contains(rel)) offenders.Add(rel);
                }
            }

            Assert.True(offenders.Count == 0,
                "新增的 TestModeRuntime 直读必须改经 IDevSession（或登记进基线并说明归属）："
                + string.Join(" | ", offenders));
        }

        /// <summary>向上找含 Tests/Tests.slnx 的仓库根（与 DisciplineScannerTests 同款定位）。</summary>
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
