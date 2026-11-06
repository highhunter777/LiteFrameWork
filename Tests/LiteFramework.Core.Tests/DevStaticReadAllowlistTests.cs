using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// I5/I6 守卫（《客户端架构重构演进路线图》§3.4）：<c>TestModeRuntime</c> 直读**只减不增**。
    /// </summary>
    public sealed class DevStaticReadAllowlistTests
    {
        /// <summary>存量直读基线（只减不增；新增文件必须改经 <c>IDevSession</c>）。</summary>
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/LiteGame/Editor/Baking/HeadHitLineTuner.cs",
        };

        [Fact]
        public void TestModeRuntime直读仅限基线文件()
        {
            string root = RepoRoot();
            string[] roots =
            {
                Path.Combine(root, "Packages", "com.litegame.framework"),
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
