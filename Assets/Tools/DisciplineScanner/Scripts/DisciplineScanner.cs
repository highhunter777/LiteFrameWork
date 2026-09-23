using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Tools.DisciplineScan
{
    /// <summary>纪律规则编号（《测试开发方案》§6.4、《M7 实施指导》§2.5）。</summary>
    public enum LintRule
    {
        /// <summary>R1 禁超越函数（Math/MathF 的 Sin/Cos/Tan/Atan2/Exp/Log/Pow…）——一律走 SimTrig 查表。</summary>
        R1Transcendental = 1,

        /// <summary>R2 禁 FMA 显式写法（FusedMultiplyAdd）。</summary>
        R2Fma = 2,

        /// <summary>R3 禁 float 精度比较（==/!=），一律用 SimMath.NearlyEqual。</summary>
        R3FloatEquality = 3,

        /// <summary>R4 禁确定性容器/遍历（LINQ、不稳定排序）；M8 起生效。</summary>
        R4DeterminismContainer = 4,

        /// <summary>R5 禁裸 UNITY_EDITOR：条件编译须三宏并集（UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG）。</summary>
        R5BareUnityEditor = 5,

        /// <summary>R6 禁原生 Unity 协程：业务 Unity 层只用可取消 UniTask（IEnumerator / StartCoroutine / yield return）。</summary>
        R6NativeCoroutine = 6,

        /// <summary>R7 非法 .meta GUID：guid 必须是 32 位 hex（64 位 base64 会被 Unity 拒绝导入 → 资源/类型静默消失）。</summary>
        R7InvalidMetaGuid = 7,

        /// <summary>R8 禁 Resources.Load/LoadAsync（`Assets/LiteGame`）：破「资源唯一入口」——统一走资源服务/收集组。</summary>
        R8ResourcesLoad = 8,

        /// <summary>R9 禁 Mod 相关类型（`Assets/LiteSim`）：守《模组系统设计》红线 M1「模组永不进 Sim」（判定必须在权威内）。</summary>
        R9ModInSim = 9,

        /// <summary>R10 禁 INetworkService（`Shell/UI`）：薄壳/UI 不得直发业务包（网络契约归框架层，业务数据经事件/桥过）。</summary>
        R10ShellSendsBusinessPacket = 10,

        /// <summary>R11 RoomServer/Runtime 纯化（《商业级通用服务端框架总设计》§8.1 禁止项）：
        /// 禁 Transport/Socket/kcp、系统墙钟/Stopwatch/Sleep、Console、文件 IO、proto/LiteNet 引用与全局随机。</summary>
        R11RuntimePurity = 11,
    }

    /// <summary>一条纪律违规。</summary>
    public struct LintViolation
    {
        public string File;
        public int Line;
        public LintRule Rule;
        public string Code;

        public override string ToString()
        {
            return File + ":" + Line + ":" + DisciplineScanner.RuleId(Rule) + ":" + Code;
        }
    }

    /// <summary>
    /// 纪律扫描**唯一引擎**（零依赖纯 C#）：Editor 菜单与 dotnet 测试共用，避免规则多处各写一遍。
    ///
    /// 规则匹配前先剔除注释（字符串字面量感知）——注释里提到禁用 API 不算违规。
    /// 豁免：行内出现 <c>lint-allow</c> 时该行全部规则豁免；<c>lint-allow R3</c> 只豁免 R3。
    /// 另：与常量 0 / null / default 的字面量比较自动豁免 R3。
    /// </summary>
    public static class DisciplineScanner
    {
        private static readonly Regex R1Regex = new Regex(
            @"\bMathF?\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Exp|Log|Log10|Pow)\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex R2Regex = new Regex(@"\bFusedMultiplyAdd\b", RegexOptions.Compiled);

        private static readonly Regex R3Regex = new Regex(
            @"(?<left>[A-Za-z_][A-Za-z0-9_\.]*|[0-9][A-Za-z0-9_\.]*)\s*(?<op>==|!=)\s*(?<right>[A-Za-z_][A-Za-z0-9_\.]*|[0-9][A-Za-z0-9_\.]*)",
            RegexOptions.Compiled);

        private static readonly Regex R4Regex = new Regex(
            @"\busing\s+System\.Linq\b|\.(OrderBy|OrderByDescending|GroupBy|ToDictionary|Where|Select)\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex R5DirectiveRegex = new Regex(@"^\s*#\s*(?:if|elif)\b", RegexOptions.Compiled);

        private static readonly Regex R6Regex = new Regex(
            @"\bIEnumerator\b|\b(?:StartCoroutine|StopCoroutine|StopAllCoroutines)\b|\byield\s+return\b",
            RegexOptions.Compiled);

        /// <summary>R7：.meta 的 guid 行必须是 32 位 hex（2026-09-15 事故：64 位 base64 被 Unity 拒收）。</summary>
        private static readonly Regex MetaGuidValueRegex = new Regex(@"^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

        /// <summary>R8：资源唯一入口——禁 Resources.Load / LoadAsync（LoadAll 等未列，按《测试开发方案》§7.6 口径）。
        /// 容忍泛型实参与空白：<c>Resources.Load&lt;GameObject&gt;("x")</c> / <c>Resources . Load (…)</c> 均命中。</summary>
        private static readonly Regex R8Regex = new Regex(
            @"\bResources\s*\.\s*(?:Load|LoadAsync)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.Compiled);

        /// <summary>
        /// R9：Mod 类标识符。只认「Mod/Mods 独立词」「Mod+大写驼峰」「IMod+大写」——
        /// Mode/Model/Modify/Modules（Mod+小写）不误伤；IModXxx 因 I 与 M 间非词边界需显式列出。
        /// </summary>
        private static readonly Regex R9Regex = new Regex(
            @"\b(?:Mod|Mods)(?![A-Za-z])|\bMod[A-Z]\w*|\bIMod[A-Z]\w*", RegexOptions.Compiled);

        /// <summary>R10：Shell/UI 不得直发业务包——禁 INetworkService 契约。</summary>
        private static readonly Regex R10Regex = new Regex(@"\bINetworkService\b", RegexOptions.Compiled);

        /// <summary>
        /// R11：RoomServer/Runtime 纯化（§8.1 禁止项——Runtime 只持确定性房间状态，时间经 RoomCommand.Tick 注入、
        /// 输入经 ClientInputBatch 纯数据、输出经 RoomOutput 纯事件）。命中任一即违规：
        /// Console / DateTime.Now / Environment.TickCount / Stopwatch / Thread.Sleep / System.Net / Socket / Kcp* /
        /// IRoomTransport / using LiteNet 或 LiteNet. 限定名（proto 单源在 LiteNet）/ Google.Protobuf /
        /// System.IO / File.* / new Random()（确定性随机走 Sim 的 RngState）。注释剔除后匹配（本文件命中示例在字符串里）。
        /// </summary>
        private static readonly Regex R11Regex = new Regex(
            @"\bConsole\s*\.\s*(?:Write|WriteLine|WriteAsync|WriteLineAsync|Error|Out|In|Read|ReadLine|ReadKey)" +
            @"|\bDateTime\s*\.\s*(?:Now|UtcNow)" +
            @"|\bEnvironment\s*\.\s*TickCount" +
            @"|\bStopwatch\b" +
            @"|\bThread\s*\.\s*Sleep\b" +
            @"|\bSystem\s*\.\s*Net\b" +
            @"|\bSocket\b" +
            @"|\bKcp\w*" +
            @"|\bIRoomTransport\b" +
            @"|\busing\s+LiteNet\b" +
            @"|\bLiteNet\s*\." +
            @"|\bGoogle\s*\.\s*Protobuf\b" +
            @"|\bSystem\s*\.\s*IO\b" +
            @"|\bFile\s*\.\s*(?:Open|Read|Write|Delete|Exists|Create|Append|Move|Copy)" +
            @"|\bnew\s+Random\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex NumericLiteral = new Regex(
            @"^[-+]?[0-9]+(\.[0-9]+)?[fFuUlLdDmM]*$", RegexOptions.Compiled);

        /// <summary>全部规则（用于 lint-allow 解析）。</summary>
        public static readonly LintRule[] AllRules =
        {
            LintRule.R1Transcendental,
            LintRule.R2Fma,
            LintRule.R3FloatEquality,
            LintRule.R4DeterminismContainer,
            LintRule.R5BareUnityEditor,
            LintRule.R6NativeCoroutine,
            LintRule.R7InvalidMetaGuid,
            LintRule.R8ResourcesLoad,
            LintRule.R9ModInSim,
            LintRule.R10ShellSendsBusinessPacket,
            LintRule.R11RuntimePurity,
        };

        public static string RuleId(LintRule rule)
        {
            switch (rule)
            {
                case LintRule.R1Transcendental: return "R1";
                case LintRule.R2Fma: return "R2";
                case LintRule.R3FloatEquality: return "R3";
                case LintRule.R4DeterminismContainer: return "R4";
                case LintRule.R5BareUnityEditor: return "R5";
                case LintRule.R6NativeCoroutine: return "R6";
                case LintRule.R7InvalidMetaGuid: return "R7";
                case LintRule.R8ResourcesLoad: return "R8";
                case LintRule.R9ModInSim: return "R9";
                case LintRule.R10ShellSendsBusinessPacket: return "R10";
                case LintRule.R11RuntimePurity: return "R11";
                default: return "R?";
            }
        }

        /// <summary>
        /// 默认排除：Editor 目录下的工具码、生成物 SimTrigTables.cs、以及引擎自身的规则定义文件
        /// （规则里含 <c>FusedMultiplyAdd</c> / <c>Math.</c> 等字面串，会被自己误报）。
        /// </summary>
        public static bool IsExcluded(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('\\', '/');
            if (p.EndsWith("/SimTrigTables.cs", StringComparison.Ordinal)) return true;
            if (p.EndsWith("/DisciplineScanner.cs", StringComparison.Ordinal)) return true;
            if (p.Contains("/Editor/") || p.EndsWith("/Editor", StringComparison.Ordinal)) return true;
            if (p.Contains("/Vendor/")) return true; // 第三方 vendored 源码（kcp2k 等）——非自研代码不入纪律扫描（M10 批①）
            return false;
        }

        /// <summary>扫描一段源码文本（只跑 <paramref name="rules"/> 指定的规则）。</summary>
        public static List<LintViolation> ScanText(string fileName, string text, LintRule[] rules)
        {
            var result = new List<LintViolation>();
            if (text == null || rules == null) return result;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool inBlockComment = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string code = StripComments(raw, ref inBlockComment);
                int lineNo = i + 1;

                for (int r = 0; r < rules.Length; r++)
                {
                    if (!IsExempt(raw, rules[r]) && Matches(rules[r], code, raw))
                        result.Add(Make(fileName, lineNo, rules[r], raw));
                }
            }
            return result;
        }

        /// <summary>扫描一个源根目录（相对 <paramref name="projectRoot"/>）下的全部 *.cs。
        /// <paramref name="excludeRoots"/> 内的子根被跳过（相对路径前缀匹配，用于同一大根下的异域子目录）。</summary>
        public static List<LintViolation> ScanRoot(string projectRoot, string relativeRoot, LintRule[] rules,
            string[] excludeRoots = null)
        {
            var result = new List<LintViolation>();
            string root = Path.Combine(projectRoot, relativeRoot);
            if (!Directory.Exists(root)) return result;

            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal); // 固定顺序 → 输出稳定
            for (int i = 0; i < files.Length; i++)
            {
                string normalized = files[i].Replace('\\', '/');
                if (IsExcluded(normalized)) continue;
                string display = RelativeDisplay(projectRoot, normalized);
                if (IsUnderExcludedRoot(display, excludeRoots)) continue;
                result.AddRange(ScanText(display, File.ReadAllText(files[i]), rules));
            }
            return result;
        }

        /// <summary>相对路径是否落在任一排除子根之下（前缀匹配 + 路径边界，防 "View" 误匹配 "ViewXxx"）。</summary>
        private static bool IsUnderExcludedRoot(string relativePath, string[] excludeRoots)
        {
            if (excludeRoots == null || excludeRoots.Length == 0) return false;
            for (int i = 0; i < excludeRoots.Length; i++)
            {
                string r = excludeRoots[i];
                if (string.IsNullOrEmpty(r)) continue;
                r = r.Replace('\\', '/').TrimEnd('/');
                if (relativePath.StartsWith(r, StringComparison.Ordinal)
                    && (relativePath.Length == r.Length || relativePath[r.Length] == '/'))
                    return true;
            }
            return false;
        }

        /// <summary>按 <see cref="ScanTargets.Default"/> 扫描全部目标（含 <see cref="ScanTargets.MetaRoots"/> 的 .meta），返回「目标 → 违规」。</summary>
        public static List<KeyValuePair<string, LintViolation>> ScanDefault(string projectRoot)
        {
            var result = new List<KeyValuePair<string, LintViolation>>();
            ScanTarget[] targets = ScanTargets.Default;
            for (int t = 0; t < targets.Length; t++)
            {
                List<LintViolation> hits = ScanRoot(projectRoot, targets[t].Root, targets[t].Rules, targets[t].ExcludeRoots);
                for (int i = 0; i < hits.Count; i++)
                    result.Add(new KeyValuePair<string, LintViolation>(targets[t].Root, hits[i]));
            }

            string[] metaRoots = ScanTargets.MetaRoots;
            for (int m = 0; m < metaRoots.Length; m++)
            {
                List<LintViolation> hits = ScanMetas(projectRoot, metaRoots[m]);
                for (int i = 0; i < hits.Count; i++)
                    result.Add(new KeyValuePair<string, LintViolation>(metaRoots[m], hits[i]));
            }
            return result;
        }

        /// <summary>
        /// 扫描一段 .meta 文本（R7）：guid 行必须是 32 位小写 hex；缺 guid 行同样违规。
        /// 与 <see cref="ScanText"/> 的差异：meta 不是 C#，不做注释剔除/豁免，只认第一个 guid 行。
        /// </summary>
        public static List<LintViolation> ScanMetaText(string fileName, string text)
        {
            var result = new List<LintViolation>();
            if (text == null) return result;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!line.StartsWith("guid:", StringComparison.Ordinal)) continue;
                string value = line.Substring(5).Trim();
                if (!MetaGuidValueRegex.IsMatch(value))
                {
                    string shown = value.Length == 0 ? "(guid 值为空)" : value;
                    result.Add(new LintViolation
                    {
                        File = fileName,
                        Line = i + 1,
                        Rule = LintRule.R7InvalidMetaGuid,
                        Code = shown,
                    });
                }
                return result; // meta 规范只含一个 guid 行
            }

            result.Add(new LintViolation
            {
                File = fileName,
                Line = 1,
                Rule = LintRule.R7InvalidMetaGuid,
                Code = "(缺少 guid 行)",
            });
            return result;
        }

        /// <summary>扫描一个根目录（相对 <paramref name="projectRoot"/>）下的全部 *.meta（R7）。</summary>
        public static List<LintViolation> ScanMetas(string projectRoot, string relativeRoot)
        {
            var result = new List<LintViolation>();
            string root = Path.Combine(projectRoot, relativeRoot);
            if (!Directory.Exists(root)) return result;

            string[] files = Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal); // 固定顺序 → 输出稳定
            for (int i = 0; i < files.Length; i++)
            {
                string normalized = files[i].Replace('\\', '/');
                string display = RelativeDisplay(projectRoot, normalized);
                result.AddRange(ScanMetaText(display, File.ReadAllText(files[i])));
            }
            return result;
        }

        // ---- 内部 ----

        private static bool Matches(LintRule rule, string code, string raw)
        {
            switch (rule)
            {
                case LintRule.R1Transcendental: return R1Regex.IsMatch(code);
                case LintRule.R2Fma: return R2Regex.IsMatch(code);
                case LintRule.R3FloatEquality: return HasR3Violation(code);
                case LintRule.R4DeterminismContainer: return R4Regex.IsMatch(code);
                case LintRule.R5BareUnityEditor: return HasR5Violation(code);
                case LintRule.R6NativeCoroutine: return R6Regex.IsMatch(code);
                case LintRule.R8ResourcesLoad: return R8Regex.IsMatch(code);
                case LintRule.R9ModInSim: return R9Regex.IsMatch(code);
                case LintRule.R10ShellSendsBusinessPacket: return R10Regex.IsMatch(code);
                case LintRule.R11RuntimePurity: return R11Regex.IsMatch(code);
                default: return false;
            }
        }

        private static LintViolation Make(string file, int lineNo, LintRule rule, string raw)
        {
            return new LintViolation { File = file, Line = lineNo, Rule = rule, Code = raw.Trim() };
        }

        /// <summary>行内含 lint-allow：无规则号 → 全部豁免；有规则号 → 只豁免所列。</summary>
        private static bool IsExempt(string line, LintRule rule)
        {
            int idx = line.IndexOf("lint-allow", StringComparison.Ordinal);
            if (idx < 0) return false;

            string tail = line.Substring(idx + "lint-allow".Length);
            bool anyRuleNamed = false;
            for (int i = 0; i < AllRules.Length; i++)
            {
                if (tail.Contains(RuleId(AllRules[i]))) anyRuleNamed = true;
            }
            if (!anyRuleNamed) return true;
            return tail.Contains(RuleId(rule));
        }

        /// <summary>R3：==/!= 两侧含变量即为违规；与常量 0 / null / default 的比较豁免。</summary>
        private static bool HasR3Violation(string code)
        {
            MatchCollection matches = R3Regex.Matches(code);
            for (int i = 0; i < matches.Count; i++)
            {
                string left = matches[i].Groups["left"].Value;
                string right = matches[i].Groups["right"].Value;
                if (IsZeroOrNullLiteral(left) || IsZeroOrNullLiteral(right)) continue;
                return true;
            }
            return false;
        }

        private static bool IsZeroOrNullLiteral(string token)
        {
            if (token == "null" || token == "default" || token == "true" || token == "false") return true;
            return NumericLiteral.IsMatch(token);
        }

        /// <summary>R5：条件编译指令行含 UNITY_EDITOR 却缺 LITEFRAMEWORK_DEBUG。</summary>
        private static bool HasR5Violation(string code)
        {
            if (!R5DirectiveRegex.IsMatch(code)) return false;
            if (code.IndexOf("UNITY_EDITOR", StringComparison.Ordinal) < 0) return false;
            if (code.IndexOf("LITEFRAMEWORK_DEBUG", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        /// <summary>去掉行注释与块注释（字符串字面量感知）。</summary>
        private static string StripComments(string line, ref bool inBlock)
        {
            var sb = new StringBuilder(line.Length);
            bool inString = false;
            bool inChar = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inBlock)
                {
                    if (c == '*' && i + 1 < line.Length && line[i + 1] == '/') { inBlock = false; i++; }
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < line.Length) { sb.Append(line[i + 1]); i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < line.Length) { sb.Append(line[i + 1]); i++; continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '"') { inString = true; sb.Append(c); continue; }
                if (c == '\'') { inChar = true; sb.Append(c); continue; }
                if (c == '/' && i + 1 < line.Length)
                {
                    if (line[i + 1] == '/') break; // 行注释 → 丢弃余下
                    if (line[i + 1] == '*') { inBlock = true; i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string RelativeDisplay(string projectRoot, string normalizedAbsolute)
        {
            string root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            if (normalizedAbsolute.StartsWith(root, StringComparison.Ordinal))
                return normalizedAbsolute.Substring(root.Length);
            return normalizedAbsolute;
        }
    }

    /// <summary>项目根定位：标记必须是**被 git 跟踪**的路径（Assets + Tests/Tests.slnx）。</summary>
    public static class ProjectLocator
    {
        public static string FindProjectRoot()
        {
            var candidates = new[]
            {
                new DirectoryInfo(Directory.GetCurrentDirectory()),
                new DirectoryInfo(AppContext.BaseDirectory),
            };

            for (int c = 0; c < candidates.Length; c++)
            {
                for (DirectoryInfo current = candidates[c]; current != null; current = current.Parent)
                {
                    // 不能用 ProjectSettings/ —— 它不在版本库里，干净检出（clone / worktree / CI）都没有。
                    if (Directory.Exists(Path.Combine(current.FullName, "Assets"))
                        && File.Exists(Path.Combine(current.FullName, "Tests", "Tests.slnx")))
                        return current.FullName;
                }
            }

            throw new DirectoryNotFoundException("无法定位项目根目录（需要同时存在 Assets 与 Tests/Tests.slnx）。");
        }
    }
}
