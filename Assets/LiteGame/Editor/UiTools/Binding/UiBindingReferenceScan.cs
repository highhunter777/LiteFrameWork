using System.Collections.Generic;
using System.IO;
using LiteGame.Editor.UiTools.Validation;

namespace LiteGame.Editor.UiTools.Binding
{
    /// <summary>一处字符串字面量引用（改名联动提示的最小单元）。</summary>
    public sealed class UiRefHit
    {
        public string File;     // 工程根相对路径
        public int Line;       // 1 起
        public string Text;     // 命中行（去首尾空白）
    }

    /// <summary>
    /// 引用点扫描（《UI编辑器工具专项设计》§2.1.2"改名时列出 Lua/C# 字符串字面量引用点辅助同步"）。
    ///
    /// 只提示、不改写——Lua 页面与 C# 受控 API 的引用同步由作者按清单同批完成（§9.2"改名联动"
    /// 的联动面是作者责任；工具的责任是让清单可见，不静默替作者改代码）。
    /// 判据＝行内出现 <c>"名字"</c> 引号形态（字符串字面量），不做语义分析。
    /// </summary>
    public static class UiBindingReferenceScan
    {
        /// <summary>
        /// 默认扫描根：Lua 页面 + UI 运行时 + 流程装配 + 测试夹具——§9.2 联动清单的现实消费面。
        /// </summary>
        public static readonly string[] DefaultScanRoots =
        {
            "Assets/LiteGame/Lua",
            "Assets/LiteGame/UI",
            "Assets/LiteGame/App",
            "Assets/Tests",
        };

        /// <summary>扫一个名字的字符串字面量引用点（roots 为空时用默认扫描根）。</summary>
        public static List<UiRefHit> Scan(string literal, IReadOnlyList<string> roots = null)
        {
            var hits = new List<UiRefHit>();
            if (string.IsNullOrEmpty(literal)) return hits;
            if (roots == null || roots.Count == 0) roots = DefaultScanRoots;

            string projectRoot = UiValidator.ProjectRoot;
            string quoted = "\"" + literal + "\"";
            var seen = new HashSet<string>();
            foreach (var root in roots)
            {
                var fullRoot = Path.Combine(projectRoot, root.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(fullRoot)) continue;
                foreach (var file in EnumerateSources(fullRoot))
                {
                    string rel = file.Substring(projectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                    if (!seen.Add(rel)) continue;
                    int lineNo = 0;
                    foreach (var line in File.ReadLines(file))
                    {
                        lineNo++;
                        if (line.Contains(quoted))
                            hits.Add(new UiRefHit { File = rel, Line = lineNo, Text = line.Trim() });
                        if (hits.Count >= MaxHits) return hits;
                    }
                }
            }
            hits.Sort((a, b) => string.CompareOrdinal(a.File, b.File) != 0
                ? string.CompareOrdinal(a.File, b.File)
                : a.Line.CompareTo(b.Line));
            return hits;
        }

        /// <summary>命中上限（交互提示面按清单用，不鼓励全库扫成报告）。</summary>
        public const int MaxHits = 200;

        private static IEnumerable<string> EnumerateSources(string dir)
        {
            foreach (var cs in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)) yield return cs;
            foreach (var lua in Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories)) yield return lua;
        }
    }
}
