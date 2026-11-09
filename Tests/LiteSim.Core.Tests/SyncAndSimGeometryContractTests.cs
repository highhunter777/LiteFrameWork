using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 同步层拆分与判定参数化（批 7 · LiteSim 参数装载）的红测：UT-04~UT-12。
    /// </summary>
    public sealed class SyncAndSimGeometryContractTests
    {
        /// <summary>Ut-08 覆盖的旧几何成员（13 个常量 + MuzzleOrigin 出口）。</summary>
        private static readonly string[] OldGeometryMembers =
        {
            "HitscanRadius", "BodyRadius", "HitscanHeight", "HeadHitLine", "HeadshotRadius",
            "MuzzleOffsetForward", "MuzzleOffsetRight", "MuzzleOffsetHeight",
            "HeadshotDamageShift", "HipSpreadFactor", "CorpseFrames", "HardMaxSpeed", "FaceTurnRadPerSec",
        };

        // ---- UT-04：LiteNet 不得再反向依赖判定内核 ----

        [Fact]
        public void UT04_LiteNet_NoUsingLiteSim()
        {
            string dir = RepoPath("Packages/com.litegame.net");
            Assert.True(Directory.Exists(dir), "缺少 Packages/com.litegame.net 目录");

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(file)) continue;
                string code = StripCommentsAndStrings(File.ReadAllText(file));
                if (Regex.IsMatch(code, @"(?m)^\s*using\s+LiteSim\b", RegexOptions.CultureInvariant))
                    offenders.Add(Relative(file));
            }

            Assert.True(offenders.Count == 0,
                "LiteNet 仍反向 using LiteSim（应迁 LiteSync，B2）：" + string.Join(" | ", offenders));
        }

        // ---- UT-05：CombatValues 新增 Geometry 字段与 9 参构造 ----

        [Fact]
        public void UT05_CombatValues_HasGeometryFieldAndNineParamCtor()
        {
            FieldInfo geometry = typeof(CombatValues).GetField("Geometry", BindingFlags.Public | BindingFlags.Instance);
            Assert.True(geometry != null, "CombatValues 缺少 public Geometry 字段（B9）");
            Assert.Equal("CombatGeometry", geometry.FieldType.Name);

            bool hasNineParamCtor = false;
            foreach (ConstructorInfo ctor in typeof(CombatValues).GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length != 9) continue;
                foreach (ParameterInfo parameter in parameters)
                {
                    if (parameter.ParameterType.Name != "CombatGeometry" && parameter.ParameterType.Name != "CombatGeometry&") continue;
                    hasNineParamCtor = true;
                    break;
                }
                if (hasNineParamCtor) break;
            }

            Assert.True(hasNineParamCtor, "CombatValues 缺少 9 参构造（第 9 参类型应为 CombatGeometry，B9）");
        }

        // ---- UT-06：发布闸门——未 Publish 读 Loaded 必须抛 ----

        [Fact]
        public void UT06_CombatConfig_Loaded_ThrowsBeforePublish()
        {
            ResetCombatConfigForTest();
            try
            {
                Assert.Throws<InvalidOperationException>(() => { _ = CombatConfig.Loaded; });

                CombatValues published = CombatValues.Default;
                CombatConfig.Publish(published);
                Assert.Equal(published.MoveSpeed, CombatConfig.Loaded.MoveSpeed);
            }
            finally
            {
                ResetCombatConfigForTest();
            }
        }

        // ---- UT-07：CombatConfig.Geometry 非判定投影存在 ----

        [Fact]
        public void UT07_CombatConfig_GeometryPropertyExists()
        {
            PropertyInfo property = typeof(CombatConfig).GetProperty("Geometry",
                BindingFlags.Public | BindingFlags.Static);
            Assert.True(property != null, "CombatConfig 缺少 public static Geometry 投影（B9/B10）");
            Assert.Equal("CombatGeometry", property.PropertyType.Name);
        }

        // ---- UT-08：旧几何常量与 MuzzleOrigin 必须从 CombatConfig 退役 ----

        [Fact]
        public void UT08_CombatConfig_OldGeometryConstantsRemoved()
        {
            var offenders = new List<string>();
            foreach (string name in OldGeometryMembers)
            {
                if (typeof(CombatConfig).GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance) != null)
                    offenders.Add(name);
            }
            if (typeof(CombatConfig).GetMethod("MuzzleOrigin",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance) != null)
                offenders.Add("MuzzleOrigin");

            Assert.True(offenders.Count == 0,
                "CombatConfig 仍有旧几何成员（应全部迁入 CombatGeometry，B10）：" + string.Join(" | ", offenders));
        }

        // ---- UT-09：MatchTestRules 只删几何覆写，保留非几何 dev 覆写 ----

        [Fact]
        public void UT09_MatchTestRules_GeometryOverrideRemoved()
        {
            Type type = typeof(MatchTestRules);
            var geometryOffenders = new List<string>();

            if (type.GetField("HeadHitLineOverride", BindingFlags.Public | BindingFlags.Instance) != null)
                geometryOffenders.Add("HeadHitLineOverride");
            if (type.GetField("HeadshotRadiusOverride", BindingFlags.Public | BindingFlags.Instance) != null)
                geometryOffenders.Add("HeadshotRadiusOverride");
            if (type.GetMethod("ResolveHeadHitLine", BindingFlags.Public | BindingFlags.Static) != null)
                geometryOffenders.Add("ResolveHeadHitLine");
            if (type.GetMethod("ResolveHeadshotRadius", BindingFlags.Public | BindingFlags.Static) != null)
                geometryOffenders.Add("ResolveHeadshotRadius");

            Assert.True(geometryOffenders.Count == 0,
                "MatchTestRules 仍有几何覆写面（B10/B19）：" + string.Join(" | ", geometryOffenders));

            Assert.True(type.GetField("DamageOverride", BindingFlags.Public | BindingFlags.Instance) != null,
                "MatchTestRules.DamageOverride 必须保留（非几何 dev 覆写，B19）");
            Assert.True(type.GetField("RpmOverride", BindingFlags.Public | BindingFlags.Instance) != null,
                "MatchTestRules.RpmOverride 必须保留（非几何 dev 覆写，B19）");
        }

        // ---- UT-10：TableLayout.Files 接入 Combat/tbcombatgeom ----

        [Fact]
        public void UT10_TableLayout_ContainsCombatGeom()
        {
            string text = ReadRepoText("Assets/LiteGame/Modules/Config/Table/TableLayout.cs");
            Assert.Contains("Combat/tbcombatgeom", text);
        }

        // ---- UT-11：SimConfigMapper.BuildCombatValues 接收 tbcombatgeom ----

        [Fact]
        public void UT11_SimConfigMapper_HasCombatGeomParameter()
        {
            string text = ReadRepoText("Assets/LiteGame/Modules/Config/Table/SimConfigMapper.cs");
            Match signature = Regex.Match(text,
                @"public\s+static\s+CombatValues\s+BuildCombatValues\s*\((?<params>[^)]*)\)",
                RegexOptions.CultureInvariant);
            Assert.True(signature.Success, "SimConfigMapper 缺少 BuildCombatValues 声明");
            string parameters = signature.Groups["params"].Value;
            Assert.Contains("combatgeom", parameters.ToLowerInvariant());
        }

        // ---- UT-12：判定面零静态几何读 ----

        [Fact]
        public void UT12_GeometryStaticReadGuard_NoOldNames()
        {
            string pattern =
                @"\bCombatConfig\s*\.\s*(?:" + string.Join("|", OldGeometryMembers) + @")\b";
            var offenders = new List<string>();

            foreach (string relativeRoot in new[] { "Packages/com.litegame.sim/Scripts", "Packages/com.litegame.sim/Systems" })
            {
                string dir = RepoPath(relativeRoot);
                Assert.True(Directory.Exists(dir), "缺少判定面目录：" + relativeRoot);
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (IsBuildArtifact(file)) continue;
                    string code = StripCommentsAndStrings(File.ReadAllText(file));
                    foreach (Match match in Regex.Matches(code, pattern, RegexOptions.CultureInvariant))
                        offenders.Add(Relative(file) + " :: " + match.Value);
                }
            }

            Assert.True(offenders.Count == 0,
                "判定面仍有 CombatConfig.<几何名> 静态读（B10）：" + string.Join(" | ", offenders));
        }

        [Fact]
        public void UT21_CombatConfig_PublishGateAndGeometryFallback()
        {
            ResetCombatConfigForTest();
            try
            {
                Assert.False(CombatConfig.HasPublished);
                Assert.False(CombatConfig.TryGetLoaded(out _));
                Assert.Equal(CombatValues.Default.Geometry.BodyRadius, CombatConfig.Geometry.BodyRadius);

                var values = new CombatValues(5f, -9.8f, 100f, 25, 1, 100, 2.5f, 60, CombatGeometry.Default);
                CombatConfig.Publish(values);
                Assert.True(CombatConfig.HasPublished);
                Assert.True(CombatConfig.TryGetLoaded(out CombatValues loaded));
                Assert.Equal(values.MoveSpeed, loaded.MoveSpeed);
                Assert.Equal(values.Geometry.HitscanRadius, CombatConfig.Geometry.HitscanRadius);
            }
            finally
            {
                ResetCombatConfigForTest();
            }
        }

        // ---- 辅助 ----

        private static void ResetCombatConfigForTest()
        {
            // 施工后应为 internal static ResetForTests()（InternalsVisibleTo）；基线没有 → 反射为 null，跳过。
            MethodInfo reset = typeof(CombatConfig).GetMethod("ResetForTests",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            reset?.Invoke(null, null);
        }

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
        /// 去注释/去字符串字面量——守卫按**代码标识符**扫描（B10 明确排除注释与字符串），
        /// 保留换行以便报告位置；不改变行数。
        /// </summary>
        private static string StripCommentsAndStrings(string text)
        {
            var sb = new StringBuilder(text.Length);
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
