using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 同步层拆分与判定参数化（批 6 LiteSync 去耦 + 批 7 尾段参数面）的红测：UT-01~UT-03、UT-13~UT-20。
    /// </summary>
    public sealed class SyncAndSimContractTests
    {
        // ---- UT-01：LiteSync 程序集落地（目录 + asmdef + 双轨 csproj） ----

        [Fact]
        public void UT01_SharedDirsAndAsmdefExist()
        {
            string dir = RepoPath("Packages/com.litegame.sync");
            Assert.True(Directory.Exists(dir), "缺少 Packages/com.litegame.sync/（LiteSync 同步协议层程序集，B1）");

            string asmdef = Path.Combine(dir, "LiteSync.asmdef");
            Assert.True(File.Exists(asmdef), "缺少 Packages/com.litegame.sync/LiteSync.asmdef（B1）");
            Assert.True(File.Exists(Path.Combine(dir, "LiteSync.csproj")),
                "缺少 Packages/com.litegame.sync/LiteSync.csproj（双轨 csproj，B1）");

            using var doc = JsonDocument.Parse(File.ReadAllText(asmdef));
            var references = new List<string>();
            foreach (var item in doc.RootElement.GetProperty("references").EnumerateArray())
                references.Add(item.GetString());
            Assert.Contains("LiteNet", references);
            Assert.Contains("LiteSim", references);

            Assert.True(doc.RootElement.GetProperty("noEngineReferences").GetBoolean(),
                "LiteSync.asmdef 必须设置 noEngineReferences=true（B1）");

            var precompiled = new List<string>();
            foreach (var item in doc.RootElement.GetProperty("precompiledReferences").EnumerateArray())
                precompiled.Add(item.GetString());
            Assert.Contains("Google.Protobuf.dll", precompiled);
            Assert.True(doc.RootElement.GetProperty("overrideReferences").GetBoolean(),
                "LiteSync.asmdef 必须设置 overrideReferences=true（B1）");
        }

        // ---- UT-02：BuildHash 共享源集增 LiteSync 共享子目录、排除 Client ----

        [Fact]
        public void UT02_SharedTargets_ContainLiteSyncSharedNotClient()
        {
            string text = ReadRepoText("scripts/codegen/gen-build-hash.py");
            int start = text.IndexOf("SHARED_TARGETS = [", StringComparison.Ordinal);
            Assert.True(start >= 0, "gen-build-hash.py 缺少 SHARED_TARGETS 清单");
            int end = text.IndexOf("\n]", start, StringComparison.Ordinal);
            Assert.True(end > start, "无法解析 SHARED_TARGETS 清单结尾");
            string block = text.Substring(start, end - start);

            var entries = new List<string>();
            foreach (Match match in Regex.Matches(block, "'(?<path>[^']+)'", RegexOptions.CultureInvariant))
                entries.Add(match.Groups["path"].Value);

            foreach (string required in new[]
                     {
                         "Packages/com.litegame.sync/Shared", "Packages/com.litegame.sync/Snapshot",
                         "Packages/com.litegame.sync/Input", "Packages/com.litegame.sync/Events",
                     })
            {
                Assert.True(entries.Contains(required),
                    "SHARED_TARGETS 缺 " + required + "（B4）");
            }

            var offenders = new List<string>();
            foreach (string entry in entries)
                if (entry.StartsWith("Packages/com.litegame.sync/Client", StringComparison.Ordinal))
                    offenders.Add(entry);
            Assert.True(offenders.Count == 0,
                "SHARED_TARGETS 不得包含 LiteSync/Client（客户端专属改动不应迫使服务端重建，B4）："
                + string.Join(" | ", offenders));
        }

        // ---- UT-03：客户端 asmdef 允许表登记 LiteSync 并删除 LiteNet -> LiteSim ----

        [Fact]
        public void UT03_ClientAsmdefAllowlist_ContainsLiteSync()
        {
            ArchModel model = ArchModel.Load(RepoRoot());

            UnitEntry sync = model.Unit("LiteSync");
            Assert.NotNull(sync);
            Assert.Equal("Packages/com.litegame.sync/LiteSync.asmdef", sync.Asmdef);

            UnitEntry net = model.Unit("LiteNet");
            Assert.NotNull(net);
            Assert.DoesNotContain("LiteSim", net.References);
        }

        // ---- UT-13：五个旧烘焙 .g.cs 全部退役 ----

        [Fact]
        public void UT13_OldBakeArtifactsRemoved()
        {
            string assets = RepoPath("Assets");
            Assert.True(Directory.Exists(assets), "缺少 Assets 目录");

            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(assets, "*.g.cs", SearchOption.AllDirectories))
                if (!IsBuildArtifact(file))
                    existing.Add(Path.GetFileName(file));

            var offenders = new List<string>();
            foreach (string retired in new[]
                     {
                         "BodyBake.g.cs", "HeadBake.g.cs", "MuzzleBake.g.cs",
                         "RootMotionData.g.cs", "GripBake.g.cs",
                     })
            {
                if (existing.Contains(retired)) offenders.Add(retired);
            }

            Assert.True(offenders.Count == 0,
                "旧烘焙 .g.cs 仍存在（应退役改产表行/.bin，B12/B16）：" + string.Join(" | ", offenders));
        }

        // ---- UT-14：rootmotion/grip 数据产物与 CC 夹具落地 ----

        [Fact]
        public void UT14_RootMotionAndGripBinariesExist()
        {
            var missing = new List<string>();
            foreach (string relative in new[]
                     {
                         "Assets/GameData/Config/runtime/rootmotion.bin",
                         "Assets/GameData/Config/runtime/grip.bin",
                         "Tests/EditMode/Fixtures/BodyCc.json",
                     })
            {
                if (!File.Exists(RepoPath(relative))) missing.Add(relative);
            }

            Assert.True(missing.Count == 0,
                "缺少烘焙数据产物/CC 夹具（B12/B16-B18）：" + string.Join(" | ", missing));
        }

        // ---- UT-15：BakeTargets.WriteGenerated 死路径删除 ----

        [Fact]
        public void UT15_WriteGeneratedRemoved()
        {
            string text = ReadRepoText("Assets/LiteGame/Editor/Baking/Targets/BakeTargets.cs");
            Assert.DoesNotContain("WriteGenerated", text);
        }

        // ---- UT-16：SimView.SnapshotInterval 改实例字段、不再静态读 SimConfig ----

        [Fact]
        public void UT16_SimView_SnapshotIntervalIsInstance()
        {
            string text = ReadRepoText("Assets/LiteGame/Modules/Battle/SubModules/ArenaSubModule/View/SimView.cs");
            Match declaration = Regex.Match(text,
                @"(?m)^[ \t]*(?<decl>(?:public|internal|private|protected)[^\r\n;=]*\bSnapshotInterval\b)",
                RegexOptions.CultureInvariant);
            Assert.True(declaration.Success, "SimView 缺少 SnapshotInterval 声明");
            string decl = declaration.Groups["decl"].Value;
            Assert.DoesNotContain("static", decl);
            Assert.Contains("float", decl);

            string code = StripCommentsAndStrings(text);
            Assert.DoesNotContain("SimConfig.SnapshotHz", code);
        }

        // ---- UT-17：AoiFilter 去静态派生缓存、改实例 AOI 设置 ----

        [Fact]
        public void UT17_AoiFilter_NoStaticReadonlyCache()
        {
            string path = FindFileNamed("AoiFilter.cs");
            Assert.True(path != null, "缺少 AoiFilter.cs");

            string code = StripCommentsAndStrings(File.ReadAllText(path));
            Assert.DoesNotContain("static readonly", code);
            foreach (string old in new[]
                     {
                         "SimConfig.AoiCellSize", "SimConfig.AoiGridExtentMeters", "SimConfig.AoiRadius",
                     })
            {
                Assert.DoesNotContain(old, code);
            }
        }

        // ---- UT-18：AoiSettings 落 LiteSync/Shared ----

        [Fact]
        public void UT18_AoiSettings_InLiteSyncShared()
        {
            const string relative = "Packages/com.litegame.sync/Shared/AoiSettings.cs";
            string path = RepoPath(relative);
            Assert.True(File.Exists(path), "缺少 " + relative + "（B15）");

            string text = File.ReadAllText(path);
            Assert.Contains("class AoiSettings", text);
            Assert.Contains("ThrottleRadius", text);
        }

        // ---- UT-19：ProtocolConstants.ThrottleAoiRadius 删除（节流档由 AoiSettings 派生） ----

        [Fact]
        public void UT19_ProtocolConstants_ThrottleAoiRadiusRemoved()
        {
            List<string> files = FindFilesNamed("ProtocolConstants.cs");
            Assert.True(files.Count > 0, "缺少 ProtocolConstants.cs");

            var offenders = new List<string>();
            foreach (string file in files)
            {
                string code = StripCommentsAndStrings(File.ReadAllText(file));
                if (code.Contains("ThrottleAoiRadius")) offenders.Add(Relative(file));
            }

            Assert.True(offenders.Count == 0,
                "ProtocolConstants 仍保留 ThrottleAoiRadius（B15）：" + string.Join(" | ", offenders));
        }

        // ---- UT-20：SimParams 门/覆写/调参/导出四件落地 ----

        [Fact]
        public void UT20_SimParamsGateAndOverridesExist()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string area in new[] { "Assets", "Server" })
            {
                string dir = RepoPath(area);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                    if (!IsBuildArtifact(file))
                        names.Add(Path.GetFileName(file));
            }

            var missing = new List<string>();
            foreach (string required in new[]
                     {
                         "SimParamsGate.cs", "SimParamsOverrides.cs",
                         "SimParamsDebugTunables.cs", "SimParamsExporter.cs",
                     })
            {
                if (!names.Contains(required)) missing.Add(required);
            }

            Assert.True(missing.Count == 0,
                "缺少 SimParams 参数面文件（B19-B21）：" + string.Join(" | ", missing));
        }

        // ---- 辅助 ----

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        private static string RepoPath(string relative)
            => Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

        private static string ReadRepoText(string relative)
        {
            string path = RepoPath(relative);
            Assert.True(File.Exists(path), "缺少文件：" + relative);
            return File.ReadAllText(path);
        }

        private static string Relative(string absolute)
            => Path.GetRelativePath(RepoRoot(), absolute).Replace('\\', '/');

        private static string FindFileNamed(string fileName)
        {
            List<string> files = FindFilesNamed(fileName);
            return files.Count == 0 ? null : files[0];
        }

        private static List<string> FindFilesNamed(string fileName)
        {
            // 共享源（LiteSim/LiteNet/LiteSync）已搬进 Packages/com.litegame.*——守卫面必须跟着走，
            // 否则扫描落空会让"已删除的符号/缓存"类断言变成假绿。
            string[] roots =
            {
                RepoPath("Assets"),
                RepoPath("Packages/com.litegame.sim"),
                RepoPath("Packages/com.litegame.net"),
                RepoPath("Packages/com.litegame.sync"),
            };
            var result = new List<string>();
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
                    if (!IsBuildArtifact(file))
                        result.Add(file);
            }
            return result;
        }

        /// <summary>排除构建产物目录（obj/bin/.dotnet 等）——它们不是源集，也不是守卫面。</summary>
        private static bool IsBuildArtifact(string path)
        {
            foreach (string segment in path.Replace('\\', '/').Split('/'))
            {
                if (segment == "obj" || segment == "bin" || segment == ".dotnet"
                    || segment == "Library" || segment == "Temp" || segment == "Logs")
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 去注释/去字符串字面量——文本守卫按**代码标识符**判定（注释里的旧名/说明不算违规）。
        /// 保留换行以便未来报告位置；不改变行数。
        /// </summary>
        private static string StripCommentsAndStrings(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            bool inChar = false;
            bool verbatim = false;
            bool escaped = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n') { inLineComment = false; sb.Append(c); }
                    else sb.Append(' ');
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/') { inBlockComment = false; sb.Append("  "); i++; }
                    else sb.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }

                if (inString)
                {
                    if (verbatim)
                    {
                        if (c == '"' && next == '"') { sb.Append("  "); i++; }
                        else if (c == '"') { inString = false; sb.Append(c); }
                        else sb.Append(c == '\n' ? '\n' : ' ');
                    }
                    else if (escaped) { escaped = false; sb.Append(' '); }
                    else if (c == '\\') { escaped = true; sb.Append(' '); }
                    else if (c == '"') { inString = false; sb.Append(c); }
                    else sb.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }

                if (inChar)
                {
                    if (escaped) { escaped = false; sb.Append(' '); }
                    else if (c == '\\') { escaped = true; sb.Append(' '); }
                    else if (c == '\'') { inChar = false; sb.Append(c); }
                    else sb.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }

                if (c == '/' && next == '/') { inLineComment = true; sb.Append("  "); i++; continue; }
                if (c == '/' && next == '*') { inBlockComment = true; sb.Append("  "); i++; continue; }
                if (c == '"')
                {
                    inString = true;
                    verbatim = i > 0 && text[i - 1] == '@';
                    sb.Append(c);
                    continue;
                }
                if (c == '\'') { inChar = true; sb.Append(c); continue; }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
