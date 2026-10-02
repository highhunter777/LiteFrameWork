using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteSim.Editor
{
    /// <summary>
    /// Unity 侧 IEEE 基线对账（《测试开发方案》§7.2 缺口 b；《M7 实施指导》§2.6）。
    ///
    /// 做法：用 <see cref="IeeeProbe"/>（与 .NET 侧**同一份**样本与运算）算出基线文本，
    /// 与仓库内 `Tests/LiteSim.Core.Tests/Baselines/IeeeBaseline.txt`（.NET 侧记录）**逐位比较**。
    /// 不等即说明：Unity 运行时与 .NET 的浮点路径出现了差异——确定性/预测质量的直接证据。
    ///
    /// 入口：菜单「LiteSim/对账 IEEE 基线」；CI 入口 <c>RunCli</c>（batchmode 退出码）；
    /// 另供 EditMode 用例调用 <see cref="Verify"/>（见 `Assets/LiteSim/Core/Tests/Editor`）。
    /// </summary>
    public static class IeeeBaselineChecker
    {
        public const string BaselineRelativePath = "Tests/LiteSim.Core.Tests/Baselines/IeeeBaseline.txt";
        private const string LogTag = "IeeeBaseline";
        private const int MaxReportedMismatches = 10;

        [MenuItem("LiteSim/对账 IEEE 基线")]
        public static void VerifyMenu()
        {
            var (ok, report) = Verify();
            if (ok)
            {
                Debug.Log("[IeeeBaseline] 逐位一致 ✓\n" + report);
                EditorUtility.DisplayDialog("IEEE 基线对账", "通过：Unity 侧与 .NET 基线逐位一致。", "OK");
            }
            else
            {
                Debug.LogError("[IeeeBaseline] 对账失败：\n" + report);
                EditorUtility.DisplayDialog("IEEE 基线对账", "失败：存在逐位差异，详见 Console。", "OK");
            }
        }

        /// <summary>CI 入口：一致退出码 0，否则 1。</summary>
        public static void RunCli()
        {
            var (ok, report) = Verify();
            if (ok)
            {
                Console.WriteLine("[IeeeBaseline] OK\n" + report);
                EditorApplication.Exit(0);
            }
            else
            {
                Console.Error.WriteLine("[IeeeBaseline] FAILED\n" + report);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 逐位对账。(ok, 人类可读报告)。**全部行（含 10k 步 `Chain`）均为硬判据**——
        /// `SimMath.Sqrt` 是**自研 software sqrt**（纯整数/位运算 + 正确舍入），两侧应逐位一致；
        /// 任一不一致即失败。
        /// </summary>
        public static (bool ok, string report) Verify()
        {
            string root = FindProjectRoot();
            if (root == null)
                return (false, "无法定位项目根（需要存在 Assets 与 Tests/Tests.slnx）——对账跳过，视为失败以免静默通过");

            string baselinePath = Path.Combine(root, BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(baselinePath))
                return (false, $"基线文件不存在：{BaselineRelativePath}（.NET 侧尚未记录？）");

            string[] expected = DataLines(File.ReadAllText(baselinePath));
            string[] actual = DataLines(IeeeProbe.BuildText());

            var sb = new StringBuilder();
            sb.Append("基线行数=").Append(expected.Length).Append("  本端行数=").Append(actual.Length);

            var mismatches = new List<string>();
            int common = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < common; i++)
            {
                if (string.Equals(expected[i], actual[i], StringComparison.Ordinal)) continue;
                if (mismatches.Count < MaxReportedMismatches)
                    mismatches.Add($"  行{i + 1}:\n    .NET: {expected[i]}\n    Unity: {actual[i]}");
            }
            if (expected.Length != actual.Length)
                mismatches.Add($"  行数不一致（.NET {expected.Length} / Unity {actual.Length}）——探针与基线不匹配");

            if (mismatches.Count == 0)
            {
                sb.Append("\n全部逐位一致 ✓（含 10k 步 Chain=").Append(IeeeProbe.ChainChecksum()).Append("）");
                return (true, sb.ToString());
            }

            sb.Append("\n不一致 ").Append(mismatches.Count).Append(" 处（最多显示 ").Append(MaxReportedMismatches).Append("）：");
            foreach (string m in mismatches) sb.Append('\n').Append(m);
            sb.Append("\n提示：Chain 不一致 = sqrt 等浮点路径又出现跨运行时差异（检查是否有人绕过 SimMath.Sqrt 用了 BCL）；");
            sb.Append("逐值行不一致 = 两侧基本运算路径不同。重录基线须带 [baseline] 说明。");
            return (false, sb.ToString());
        }

        /// <summary>运算链两侧值（供用例记录到日志/报告；不参与判定）。</summary>
        public static (uint expectedFromBaseline, uint unityValue, bool same) ChainComparison()
        {
            string root = FindProjectRoot();
            uint expected = 0;
            if (root != null)
            {
                string baselinePath = Path.Combine(root, BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(baselinePath))
                {
                    foreach (string line in DataLines(File.ReadAllText(baselinePath)))
                    {
                        if (!line.StartsWith("Chain ", StringComparison.Ordinal)) continue;
                        uint.TryParse(line.Substring(6).Trim(), out expected);
                    }
                }
            }
            uint unity = IeeeProbe.ChainChecksum();
            return (expected, unity, expected == unity);
        }

        /// <summary>取非注释行（表头以 '#' 开头，两侧一致，不参与逐位比较）。</summary>
        private static string[] DataLines(string text)
        {
            string[] raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var list = new List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i].TrimEnd();
                if (line.Length == 0) continue;
                if (line[0] == '#') continue;
                list.Add(line);
            }
            return list.ToArray();
        }

        /// <summary>项目根定位：与测试侧同款标记（Assets + Tests/Tests.slnx），从 dataPath 向上找。</summary>
        private static string FindProjectRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath);
            for (var cur = dir; cur != null; cur = cur.Parent)
            {
                if (Directory.Exists(Path.Combine(cur.FullName, "Assets"))
                    && File.Exists(Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return cur.FullName;
            }
            return null;
        }
    }
}
