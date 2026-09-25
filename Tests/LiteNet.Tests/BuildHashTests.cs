using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// `BuildHash` 守卫（M10 前置）：按 `scripts/gen-build-hash.py` 的**同一规则**复算源码 hash 并与生成常量比对。
    /// 目的：**改了 Sim / 协议却忘了重跑生成器** → L1 当场红（否则握手时才发现两端不一致，代价大得多）。
    /// 规则要点（与生成器逐条对齐）：排序 Ordinal；喂入 "相对路径\0内容"；**行尾归一化 CRLF/CR → LF**（跨机器一致）。
    /// </summary>
    public sealed class BuildHashTests
    {
        /// <summary>
        /// **必须与 `scripts/gen-build-hash.py` 的 TARGETS 逐条一致**（生成器是 python、本测试是 C#，
        /// 跨语言无法共享定义，只能同步维护——本用例就是用来抓这份漂移的）。
        /// Systems/ 与 Scripts/ 平级（2026-09-19 目录收纳的最终形态），同属判定源集，漏了会让两端分叉不被校验。
        /// </summary>
        private static readonly string[] Targets =
        {
            "Assets/LiteSim/Core/Scripts",
            "Assets/LiteSim/Core/Systems",
            "Assets/LiteNet/Proto",
            "Assets/LiteNet/Protocol",
        };

        /// <summary>表数据目标（与生成器 DATA_TARGETS 对齐）：改动数值即改 hash → 旧客户端被拒进房。</summary>
        private static readonly string[] DataTargets =
        {
            "Assets/LiteGame/RawFile/Config",
            "RoomServer/Data",
        };

        private static readonly string[] DataExtensions = { ".bytes", ".json" };

        /// <summary>非玩法表（与生成器 DATA_EXCLUDE_NAMES 对齐，2026-09-25）：hash 只覆盖影响判定/协议的
        /// 表，UI 表单（层级/路径/全屏）属表现层——改它不应让全员拒绝进房（同 SkipDirs 记录的坑）。</summary>
        private static readonly string[] DataExcludeNames = { "tbuiform.bytes", "tbuiform.json" };

        private static readonly string[] SkipDirs = { "bin", "obj", ".dotnet", "__pycache__", "Editor" };
        private const string SelfName = "BuildHash.g.cs";

        [Fact]
        public void BuildHash_常量格式合法()
        {
            Assert.Equal(16, BuildHash.Value.Length);
            foreach (char c in BuildHash.Value)
                Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), "hash 应为小写 hex");
        }

        [Fact]
        public void BuildHash_与当前源码复算一致_未忘记重跑生成器()
        {
            string root = FindRepoRoot();
            var files = CollectFiles(root);
            Assert.True(files.Count > 0, "收集到的源文件为空——路径规则或仓库根定位有问题");

            string actual = Compute(root, files);
            Assert.Equal(BuildHash.Value, actual);   // 不等 → 改了 Sim/协议后请跑 scripts/gen-build-hash.py
        }

        private static List<string> CollectFiles(string root)
        {
            var files = new List<string>();
            foreach (string target in Targets)
            {
                string baseDir = Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(baseDir)) continue;
                foreach (string full in Directory.EnumerateFiles(baseDir, "*.cs", SearchOption.AllDirectories))
                {
                    if (full.EndsWith(".meta", StringComparison.Ordinal)) continue;
                    if (Path.GetFileName(full) == SelfName) continue;     // 排除自身输出（否则自指不稳）
                    if (IsUnderSkipDir(root, full)) continue;
                    files.Add(full);
                }
            }
            foreach (string target in DataTargets)
            {
                string baseDir = Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(baseDir)) continue;
                foreach (string full in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(full);
                    bool isData = false;
                    foreach (string ext in DataExtensions)
                        if (name.EndsWith(ext, StringComparison.Ordinal)) { isData = true; break; }
                    if (!isData) continue;
                    if (IsDataExcluded(name)) continue;
                    if (IsUnderSkipDir(root, full)) continue;
                    files.Add(full);
                }
            }

            files.Sort(CompareOrdinalRelative(root));
            return files;
        }

        private static bool IsUnderSkipDir(string root, string full)
        {
            string rel = Path.GetRelativePath(root, full).Replace('\\', '/');
            foreach (string dir in SkipDirs)
                if (rel.Contains("/" + dir + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>非玩法表判定（与生成器 DATA_EXCLUDE_NAMES 同一判据）。</summary>
        private static bool IsDataExcluded(string fileName)
        {
            foreach (string excluded in DataExcludeNames)
                if (string.Equals(fileName, excluded, StringComparison.Ordinal)) return true;
            return false;
        }

        private static Comparison<string> CompareOrdinalRelative(string root)
            => (a, b) => string.CompareOrdinal(
                Path.GetRelativePath(root, a).Replace('\\', '/'),
                Path.GetRelativePath(root, b).Replace('\\', '/'));

        private static string Compute(string root, List<string> files)
        {
            using var sha = SHA256.Create();
            foreach (string full in files)
            {
                string rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                sha.TransformBlock(Encoding.UTF8.GetBytes(rel), 0, Encoding.UTF8.GetByteCount(rel), null, 0);
                sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
                byte[] content = Normalize(File.ReadAllBytes(full));
                sha.TransformBlock(content, 0, content.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var sb = new StringBuilder(64);
            foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
            return sb.ToString(0, 16);
        }

        /// <summary>行尾归一化（CRLF/CR → LF）：与生成器一致，否则跨机器 clone 会算出不同 hash。</summary>
        private static byte[] Normalize(byte[] raw)
        {
            var outp = new List<byte>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == (byte)'\r')
                {
                    outp.Add((byte)'\n');
                    if (i + 1 < raw.Length && raw[i + 1] == (byte)'\n') i++;   // CRLF → 单个 LF
                }
                else outp.Add(raw[i]);
            }
            return outp.ToArray();
        }

        private static string FindRepoRoot()
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "Tests", "Tests.slnx"))
                    && Directory.Exists(Path.Combine(d.FullName, "Assets")))
                    return d.FullName;
            }
            throw new InvalidOperationException("未找到仓库根（Tests/Tests.slnx + Assets）");
        }
    }
}
