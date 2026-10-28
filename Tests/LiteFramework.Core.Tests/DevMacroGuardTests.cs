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
        // M3-6b 3b：开发面已整件迁入 LiteGame.Dev——产品程序集只剩**一处**平台适配件例外。
        // 任何其它文件出现 DEVELOPMENT_BUILD/LITEFRAMEWORK_DEBUG 即时失败（hard fail）。
        private static readonly Dictionary<string, int> Allowed = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // 平台层输入后端：R12 禁 App/LiteGame.Dev 直接 import InputSystem，故热键件必须留在
            // LiteClient.Platform.Unity（release 由宏剥离）。6b-4 收尾时评估改为自绘 Hotkey 端口。
            ["Assets/LiteClient/Adapters/Platform.Unity/Dev/DevHotkey.cs"] = 1,
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