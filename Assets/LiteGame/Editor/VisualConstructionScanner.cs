using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LiteGame.Editor
{
    /// <summary>
    /// 运行时视觉构建扫描器（《UI框架总设计》§7"视觉单一来源"；《UI制作规范》§8 资产静态检查）。
    ///
    /// 规则：用户可见的 UI 视觉一律来自 `Assets/UI/Screens|Widgets` 的模板；运行时代码不得构建
    /// 视觉结构——不 `new GameObject` 造 Canvas/Graphic/Text/Button，不 `AddComponent` 挂视觉组件。
    ///
    /// **层容器节点不在此限**：`UIService` 建 `[UIRoot]` 与各语义层节点是**层级骨架不是视觉**
    /// （§7 原文），这些调用不带视觉组件参数，故按"是否构造视觉组件"判定而非按 `new GameObject` 判定。
    ///
    /// **唯一例外**：引导期错误界面（`ProcedureError`）——资源系统不可用时的兜底，判据见 §7：
    /// 仅限初始化不变量路径 + 绑冒烟标记 + 不得被常规运行路径复用。例外在
    /// <see cref="AllowedFiles"/> 显式登记，新增例外必须同时改设计文档与其判据。
    ///
    /// 纯函数式：输入源码文本 → 违规列表，便于用例喂合成负例（§4.2"违规负例"）。
    /// </summary>
    public static class VisualConstructionScanner
    {
        /// <summary>视觉/交互组件类型名——构造它们即构建视觉或交互结构。</summary>
        public static readonly string[] VisualComponentTypes =
        {
            // Graphic
            "Image", "RawImage", "Text", "TextMeshProUGUI", "TMP_Text", "TMP_InputField", "InputField",
            // 复合控件
            "Button", "Toggle", "Slider", "Dropdown", "ScrollRect", "LayoutGroup", "ContentSizeFitter",
            // 画布与交互结构
            "Canvas", "CanvasGroup", "CanvasScaler", "GraphicRaycaster", "RectMask2D", "Mask",
            "EventSystem", "StandaloneInputModule", "InputSystemUIInputModule",
        };

        /// <summary>登记例外（相对工程根的路径，正斜杠）。新增须同步设计文档 §7 判据。</summary>
        public static readonly string[] AllowedFiles =
        {
            // 引导期错误界面：内置资源不可用时仍要能显示（§7）；绑冒烟标记 BootstrapError
            "Assets/LiteGame/Runtime/Main/Procedure/ProcedureError.cs",
        };

        public sealed class Violation
        {
            public string File;
            public int Line;
            public string Snippet;
            public string Reason;

            public override string ToString() => $"{File}:{Line} {Reason} — {Snippet}";
        }

        /// <summary>扫描单个源文件（纯函数：不含 IO 与资产访问）。</summary>
        public static List<Violation> ScanSource(string file, string source)
        {
            var found = new List<Violation>();
            if (string.IsNullOrEmpty(source)) return found;
            if (IsAllowed(file)) return found;
            if (IsComment(file)) return found;                 // 注释自身不判违规（文档/说明常见）

            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripCommentsAndStrings(lines[i]);
                if (line.Length == 0) continue;

                foreach (string type in VisualComponentTypes)
                {
                    if (MatchesComponentConstruction(line, type))
                    {
                        found.Add(new Violation
                        {
                            File = file,
                            Line = i + 1,
                            Snippet = lines[i].Trim(),
                            Reason = $"运行时构建视觉/交互结构（{type}）——视觉须取模板（§7）",
                        });
                        break;                                  // 一行只报一次
                    }
                }
            }
            return found;
        }

        /// <summary>单遍剥离行注释与字符串字面量（正则扫描器只看**代码**，不看文本）。
        /// 顺序敏感：`//` 出现在字符串内不算注释（如 URL），故一趟处理两者。</summary>
        private static string StripCommentsAndStrings(string line)
        {
            var sb = new System.Text.StringBuilder(line.Length);
            bool inString = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inString)
                {
                    if (c == '\\' && i + 1 < line.Length) { i++; continue; }   // 转义：连字符跳过
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;   // 行注释 → 行尾
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>构造判定：typeof(T) 参数、AddComponent&lt;T&gt;、GetOrAddComponent&lt;T&gt; 等。</summary>
        private static bool MatchesComponentConstruction(string line, string type)
        {
            // new GameObject(..., typeof(T), ...)
            if (Regex.IsMatch(line, $@"typeof\s*\(\s*{Regex.Escape(type)}\s*\)")) return true;

            // AddComponent<T>() / GetOrAddComponent<T>() / RequireComponent 之类
            if (Regex.IsMatch(line, $@"\b\w*AddComponent\s*<\s*{Regex.Escape(type)}\s*>")) return true;

            return false;
        }

        /// <summary>该文件是否登记为例外。</summary>
        public static bool IsAllowed(string file)
        {
            string norm = Normalize(file);
            foreach (string allowed in AllowedFiles)
                if (norm.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsComment(string file)
            => Normalize(file).EndsWith("VisualConstructionScanner.cs", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string path) => (path ?? "").Replace('\\', '/');

        /// <summary>扫描目录树下的 .cs（跳过 Editor 程序集——生产约束针对运行时）。</summary>
        public static List<Violation> ScanDirectory(string projectRoot, params string[] relativeDirs)
        {
            var all = new List<Violation>();
            foreach (string rel in relativeDirs)
            {
                string dir = Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;

                foreach (string path in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string norm = Normalize(path);
                    if (norm.Contains("/Editor/")) continue;          // 编辑器工具不在约束面
                    if (norm.Contains("/.dotnet/") || norm.Contains("/obj/")) continue;
                    all.AddRange(ScanSource(norm.Substring(Normalize(projectRoot).Length + 1), File.ReadAllText(path)));
                }
            }
            return all;
        }
    }
}
