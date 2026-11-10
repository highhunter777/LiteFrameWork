using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LiteGame.Editor.UITools.Validation;

namespace LiteGame.Editor.UITools.Reference
{
    /// <summary>一次页面逻辑调用点（名字型第一参；字面量提取——动态构造名不识别，如实登记）。</summary>
    public sealed class UIPageApiCall
    {
        /// <summary>API 名（受控名单内，如 `SetTextKey`/`OnButtonClick`）。</summary>
        public string Api;

        /// <summary>名字型第一参字面量（控件名/节点名）；非字面量调用不产出。</summary>
        public string Name;

        /// <summary>调用内全部字符串字面量（按出现序；`SetTextKey` 的 key 在 Args[1]）。</summary>
        public IReadOnlyList<string> Args;

        /// <summary>顶层实参个数（含名字参；闭包体内的参数不计）——参数数对账用。</summary>
        public int ArgCount;

        /// <summary>源码行号（1 基，报文定位用）。</summary>
        public int Line;
    }

    /// <summary>
    /// 页面逻辑源解析与扫描（《Lua 退出专项设计》§4.7）：页面逻辑＝prefab 自带 C# 组件（`Page_<Name>Logic.cs`）。
    /// </summary>
    public static class UIPageLogicScanner
    {
        /// <summary>页面逻辑件根（按页名递归查 `Page_<Name>Logic.cs`）。</summary>
        public const string LogicRoot = "Assets/LiteGame/Modules/UI/Pages";

        private static readonly Regex CSharpNameFirstCall = new Regex(
            @"\b(?<api>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*\(\s*""",
            RegexOptions.Compiled);

        /// <summary>页逻辑件的候选路径（按名递归；多个＝重复实现，由页面逻辑规则报错）。</summary>
        public static IReadOnlyList<string> ListCSharpFiles(string pageName)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(pageName)) return result;
            string root = FullPath(LogicRoot);
            if (!Directory.Exists(root)) return result;

            foreach (string file in Directory.GetFiles(root, pageName + "Logic.cs", SearchOption.AllDirectories))
                result.Add(ToProjectRelative(file));
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>C# 页源路径（未找到＝null；重复实现取排序首个，冲突由规则单独报）。</summary>
        public static string TryResolveCSharpFile(string pageName)
            => ListCSharpFiles(pageName).FirstOrDefault();

        /// <summary>页面逻辑源路径（未找到＝null）。</summary>
        public static string ResolveFile(string pageName) => TryResolveCSharpFile(pageName);

        /// <summary>枚举页面逻辑源（工程根相对）：C# 逻辑根递归 `*Logic.cs`（排序稳定）。</summary>
        public static IReadOnlyList<string> EnumerateSources(string logicRootDir)
        {
            var result = new List<string>();
            string logicRoot = FullPath(logicRootDir);
            if (Directory.Exists(logicRoot))
                foreach (string file in Directory.GetFiles(logicRoot, "*Logic.cs", SearchOption.AllDirectories))
                    result.Add(ToProjectRelative(file));

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>读源文件（工程根相对路径；不存在返回 null——调用方显式处理缺件）。</summary>
        public static string ReadSource(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath)) return null;
            string full = FullPath(projectRelativePath);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        /// <summary>提取受控名单内 API 的调用点（名字型首参必须是字符串字面量）。</summary>
        public static List<UIPageApiCall> ScanApiCalls(string source) => ScanCSharp(source);

        /// <summary>名字字面量全文匹配。</summary>
        public static bool ContainsLiteral(string source, string name)
            => !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(name)
               && (source.Contains("\"" + name + "\"") || source.Contains("'" + name + "'"));

        private static List<UIPageApiCall> ScanCSharp(string source)
        {
            var found = new List<UIPageApiCall>();
            if (string.IsNullOrEmpty(source)) return found;

            var names = new HashSet<string>(UIRuleThresholds.PageNameFirstApis, StringComparer.Ordinal);
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;   // 注释行不产出

                int searchFrom = 0;
                while (searchFrom < line.Length)
                {
                    Match match = CSharpNameFirstCall.Match(line, searchFrom);
                    if (!match.Success) break;
                    searchFrom = match.Index + match.Length;

                    string api = match.Groups["api"].Value;
                    if (!names.Contains(api)) continue;

                    int quote = match.Index + match.Length - 1;                 // 首参引号位置
                    int close = FindQuoteEnd(line, quote);
                    if (close < 0) continue;
                    string name = line.Substring(quote + 1, close - quote - 1);
                    if (string.IsNullOrEmpty(name)) continue;

                    int open = line.LastIndexOf('(', quote);
                    if (open < 0) continue;
                    int end = MatchCloseParen(line, open);
                    if (end < 0) continue;                                      // 跨行调用不产出（单行口径）

                    List<string> args = LiteralsIn(line, open, end);
                    found.Add(new UIPageApiCall
                    {
                        Api = api,
                        Name = name,
                        Args = args,
                        ArgCount = TopLevelArgCount(line, open, end),
                        Line = i + 1,
                    });
                    searchFrom = end + 1;
                }
            }
            return found;
        }

        private static int FindQuoteEnd(string line, int openQuote)
        {
            for (int i = openQuote + 1; i < line.Length; i++)
                if (line[i] == '"' && line[i - 1] != '\\') return i;
            return -1;
        }

        private static int MatchCloseParen(string line, int open)
        {
            int depth = 0;
            for (int i = open; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    int close = FindQuoteEnd(line, i);
                    if (close < 0) return -1;
                    i = close;
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static int TopLevelArgCount(string line, int open, int close)
        {
            int count = 1;
            int depth = 0;
            for (int i = open + 1; i < close; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    int quotesEnd = FindQuoteEnd(line, i);
                    if (quotesEnd < 0) return count;
                    i = quotesEnd;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0) count++;
            }
            return count;
        }

        private static List<string> LiteralsIn(string line, int open, int close)
        {
            var literals = new List<string>(4);
            for (int i = open + 1; i < close; i++)
            {
                if (line[i] != '"') continue;
                int end = FindQuoteEnd(line, i);
                if (end < 0) break;
                literals.Add(line.Substring(i + 1, end - i - 1));
                i = end;
            }
            return literals;
        }

        private static string ToProjectRelative(string fullPath)
        {
            string root = UITools.Validation.UIValidator.ProjectRoot.Replace('\\', '/');
            return fullPath.Replace('\\', '/').Replace(root + "/", string.Empty);
        }

        private static string FullPath(string projectRelativePath)
            => Path.Combine(UITools.Validation.UIValidator.ProjectRoot,
                (projectRelativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
    }
}
