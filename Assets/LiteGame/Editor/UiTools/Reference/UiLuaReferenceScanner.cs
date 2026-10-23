using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Validation;

namespace LiteGame.Editor.UiTools.Reference
{
    /// <summary>一次 Lua 受控 API 调用点（名字型第一参；字面量提取——动态构造名不识别，如实登记）。</summary>
    public sealed class UiLuaApiCall
    {
        /// <summary>API 名（受控名单内，如 <c>SetTextKey</c>）。</summary>
        public string Api;

        /// <summary>名字型第一参字面量（控件名/节点名）；非字面量调用不产出。</summary>
        public string Name;

        /// <summary>调用内全部字符串字面量（按出现序；<c>SetTextKey</c> 的 key 在 Args[1]）。</summary>
        public IReadOnlyList<string> Args;

        /// <summary>顶层实参个数（含名字参；闭包体内的参数不计）——参数数对账用。</summary>
        public int ArgCount;

        /// <summary>源码行号（1 基，报文定位用）。</summary>
        public int Line;
    }

    /// <summary>一次事件接线（On* 族）：契约 events[] 的对账数据。</summary>
    public sealed class UiLuaEventWiring
    {
        /// <summary>接线 API 名（如 <c>OnButton</c>）。</summary>
        public string Api;

        /// <summary>契约事件名（与受控 API 同文，如 <c>onClick</c>；映射单源＝UiRuleThresholds.LuaEventApis）。</summary>
        public string Event;

        /// <summary>控件名（bindName）。</summary>
        public string Name;

        /// <summary>handler 尽力解析：匿名闭包内首个 <c>self:方法(</c> 的方法名；解析不到记 <c>anonymous</c>。</summary>
        public string Handler;

        /// <summary>源码行号（1 基）。</summary>
        public int Line;
    }

    /// <summary>
    /// Lua 引用对账数据面（《UI 编辑器工具链专项设计》§5.2「契约只做对账提示」+ §6.2 契约族「controls 越界/未消费、文本双写」）：
    /// 从页面 Lua 源提取受控 API 调用点与名字字面量，供契约生成（events[]）与契约校验（未消费/越界/双写）共用一份提取结果。
    ///
    /// 提取口径：**字面量与可见证据**——只识别字符串字面量形态（单/双引号）与**单行**调用（真实页面接线均为单行）；
    /// 括号配对跳过字符串内容，闭包内的括号按深度配对不误入顶层参数。动态拼接名不产出、不臆造。
    /// bindName 消费判定用「全文字面量匹配」以覆盖变量中转（如 <c>_Begin("Button_X",…)</c> → <c>SetInteractable(btn,…)</c>）。
    /// </summary>
    public static class UiLuaReferenceScanner
    {
        private static readonly HashSet<string> NameFirstApiSet =
            new HashSet<string>(UiRuleThresholds.LuaNameFirstApis, StringComparer.Ordinal);

        /// <summary>提取受控名单内 API 的调用点（<c>self.ui:方法(…)</c> 形态，名字型第一参须为字面量）。</summary>
        public static List<UiLuaApiCall> ScanApiCalls(string source)
        {
            var calls = new List<UiLuaApiCall>();
            if (string.IsNullOrEmpty(source)) return calls;
            var lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                ScanLine(lines[i], i + 1, calls, api => NameFirstApiSet.Contains(api));
            return calls;
        }

        /// <summary>提取事件接线点（On* 族）并尽力解析 handler。</summary>
        public static List<UiLuaEventWiring> ScanEventWiring(string source)
        {
            var wiring = new List<UiLuaEventWiring>();
            if (string.IsNullOrEmpty(source)) return wiring;
            var map = UiRuleThresholds.LuaEventApis.ToDictionary(p => p.Api, p => p.Event, StringComparer.Ordinal);
            var lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                var buffer = new List<UiLuaApiCall>();
                ScanLine(lines[i], lineNumber, buffer, api => map.ContainsKey(api));
                foreach (var call in buffer)
                {
                    wiring.Add(new UiLuaEventWiring
                    {
                        Api = call.Api,
                        Event = map[call.Api],
                        Name = call.Name,
                        Handler = ResolveHandler(lines[i]) ?? "anonymous",
                        Line = lineNumber,
                    });
                }
            }
            return wiring;
        }

        /// <summary>名字字面量全文匹配（消费判定；引用计数语义＝出现即消费）。</summary>
        public static bool ContainsLiteral(string source, string name)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(name)) return false;
            return ContainsQuoted(source, name, '"') || ContainsQuoted(source, name, '\'');
        }

        /// <summary>页面 luaPath → Lua 文件路径（工程根相对；<c>UI.Page_X</c> → 起页 Lua 目录 <c>Page_X.lua</c>；不在页面目录形态返回 null）。</summary>
        public static string TryResolveLuaFile(string luaPath)
        {
            if (string.IsNullOrEmpty(luaPath) || !luaPath.StartsWith("UI.", StringComparison.Ordinal)) return null;
            string remainder = luaPath.Substring(3).Trim();
            if (remainder.Length == 0 || remainder.Contains(".")) return null;      // 只认 UI.<单段> 页面形态
            return UiPageScaffold.LuaDir + "/" + remainder + ".lua";
        }

        /// <summary>读源文件（工程根相对路径；不存在返回 null——调用方显式处理缺件）。</summary>
        public static string ReadSource(string projectRelativePath)
        {
            string full = Path.Combine(Validation.UiValidator.ProjectRoot,
                (projectRelativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        // ---------------- 实现 ----------------

        /// <summary>单行扫描：找 <c>self.ui:Api(</c>，配对括号取调用段，顶层参数解析。</summary>
        private static void ScanLine(string line, int lineNumber, List<UiLuaApiCall> sink, Func<string, bool> apiFilter)
        {
            int searchFrom = 0;
            while (true)
            {
                int hit = line.IndexOf("self.ui:", searchFrom, StringComparison.Ordinal);
                if (hit < 0) return;
                searchFrom = hit + 1;

                int cursor = hit + "self.ui:".Length;
                int nameEnd = cursor;
                while (nameEnd < line.Length && (char.IsLetterOrDigit(line[nameEnd]) || line[nameEnd] == '_')) nameEnd++;
                if (nameEnd == cursor) continue;
                string api = line.Substring(cursor, nameEnd - cursor);
                if (!apiFilter(api)) continue;

                int open = line.IndexOf('(', nameEnd);
                if (open < 0) continue;
                int close = MatchCloseParen(line, open);
                if (close < 0) continue;                    // 跨行调用不产出（单行口径）
                searchFrom = close + 1;

                var args = SplitTopLevelArgs(line, open, close);
                if (args.Count == 0 || !IsStringLiteral(args[0])) continue;
                string name = Unquote(args[0]);

                sink.Add(new UiLuaApiCall
                {
                    Api = api,
                    Name = name,
                    Args = args.Where(IsStringLiteral).Select(Unquote).ToList(),
                    ArgCount = args.Count,
                    Line = lineNumber,
                });
            }
        }

        /// <summary>handler 尽力解析：该行内首个 <c>self:方法(</c> 的方法名（找不到返回 null）。</summary>
        private static string ResolveHandler(string line)
        {
            int hit = line.IndexOf("self:", StringComparison.Ordinal);
            if (hit < 0) return null;
            int start = hit + "self:".Length;
            int end = start;
            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_')) end++;
            if (end == start) return null;
            return line.Substring(start, end - start);
        }

        /// <summary>配对右括号：跳过字符串内容；闭包内括号按深度计入（Lua 闭包无花括号干扰）。</summary>
        private static int MatchCloseParen(string line, int open)
        {
            int depth = 0;
            for (int i = open; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' || c == '\'')
                {
                    int close = FindQuoteEnd(line, i, c);
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

        /// <summary>顶层实参切分（深度 0 的逗号；字符串内容不切）。</summary>
        private static List<string> SplitTopLevelArgs(string line, int open, int close)
        {
            var args = new List<string>();
            var current = new StringBuilder();
            int depth = 0;
            for (int i = open + 1; i < close; i++)
            {
                char c = line[i];
                if (c == '"' || c == '\'')
                {
                    int end = FindQuoteEnd(line, i, c);
                    if (end < 0) return args;
                    current.Append(line.Substring(i, end - i + 1));
                    i = end;
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (c == ',' && depth == 0)
                {
                    args.Add(current.ToString().Trim());
                    current.Clear();
                }
                else current.Append(c);
            }
            if (current.Length > 0) args.Add(current.ToString().Trim());
            return args;
        }

        private static int FindQuoteEnd(string line, int start, char quote)
        {
            for (int i = start + 1; i < line.Length; i++)
                if (line[i] == quote && (i == 0 || line[i - 1] != '\\')) return i;
            return -1;
        }

        private static bool IsStringLiteral(string token)
        {
            return token != null && token.Length >= 2
                && ((token[0] == '"' && token[token.Length - 1] == '"')
                    || (token[0] == '\'' && token[token.Length - 1] == '\''));
        }

        private static string Unquote(string token) => token.Substring(1, token.Length - 2);

        private static bool ContainsQuoted(string source, string name, char quote)
        {
            return source.IndexOf(quote + name + quote, StringComparison.Ordinal) >= 0;
        }
    }
}
