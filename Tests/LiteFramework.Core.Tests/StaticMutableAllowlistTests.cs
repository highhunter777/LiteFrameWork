using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// I1 守卫（路线图 §3.4/§4.11 6）：`public static` 可变字段**只减不增**。
    /// M0 建立基线（脚本生成，勿手改排序）；新字段一律改构造注入/实例状态，
    /// 确需例外先在设计文档登记再从基线加行。
    /// </summary>
    public sealed class StaticMutableAllowlistTests
    {
        /// <summary>存量基线（路径 :: 字段名）。</summary>
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/LiteGame/Dev/Debug/Session/DevStartupConfig.cs :: DirectToTrainingEnabled",
            "Assets/LiteGame/App/Presentation/Battle/Look/LookTuning.cs :: AdsScaleDevOverride",
            "Assets/LiteGame/App/Presentation/Battle/Look/LookTuning.cs :: SensitivityDevOverride",
            "Assets/LiteGame/App/Presentation/Hud/CrosshairFeel.cs :: BurstCapDevOverride",
            "Assets/LiteGame/App/Presentation/Hud/CrosshairFeel.cs :: BurstGrowthDevOverride",
            "Assets/LiteGame/App/Presentation/Hud/CrosshairFeel.cs :: BurstRecoverDevOverride",
            "Assets/LiteGame/App/Presentation/Hud/CrosshairFeel.cs :: KickDevOverride",
            "Assets/LiteGame/App/Presentation/Hud/CrosshairFeel.cs :: KickRecoverDevOverride",
            "Assets/LiteGame/App/Presentation/Hud/CrosshairFeel.cs :: StaticMaxDevOverride",
            "Assets/LiteGame/Editor/GenConfig/GenConfig.cs :: CSharpCallLua",
            "Assets/LiteGame/Modules/View/Animation/Drivers/GripTuning.cs :: TorsoWeightDevOverride",
        };

        [Fact]
        public void 静态可变字段仅限基线()
        {
            string root = RepoRoot();
            string[] roots = { "Assets/LiteFramework/Runtime/Modules", "Assets/LiteGame", "Assets/LiteFramework", "Assets/LiteSim" };
            var rx = new Regex(@"^\s*public\s+static\s+(?!readonly\b)(?!const\b)(?!class\b)(?!partial\b)(?!void\b)(?!event\b)[\w<>,\[\]\.]+\s+([A-Za-z_]\w*)\s*(=[^=>]|;)");
            var offenders = new List<string>();
            foreach (string dir in roots)
            {
                string full = Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(full)) continue;
                foreach (string file in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    foreach (string line in File.ReadAllLines(file))
                    {
                        string t = line.TrimStart();
                        if (t.StartsWith("//") || t.StartsWith("///")) continue;
                        var m = rx.Match(line);
                        if (!m.Success) continue;
                        string key = rel + " :: " + m.Groups[1].Value;
                        if (!Allowed.Contains(key)) offenders.Add(key);
                    }
                }
            }
            Assert.True(offenders.Count == 0,
                "新增 public static 可变字段（改构造注入/实例状态；确需例外先登记）：" + string.Join(" | ", offenders));
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
