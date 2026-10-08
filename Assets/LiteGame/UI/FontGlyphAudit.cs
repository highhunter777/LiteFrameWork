using System;
using System.Collections.Generic;
using System.Text;
using TMPro;

namespace LiteGame.UI
{
    /// <summary>
    /// 字体族缺字审计（《UI框架总设计》§9"字体作为字体族和 fallback 链配置，检查缺字"；
    /// 制作规范 §7"按所支持语言检查缺字"）。
    ///
    /// 覆盖口径 = 主字体 + fallback 链**逐级**查找（与 TMP 运行时同序：主字体未命中才问下一级）；
    /// 字体族配置单源 = TMP Settings（默认字体 + fallback 链——"字体族与 fallback 受统一配置管理"），
    /// 本工具只做只读审计，不持有第二份字体配置。
    ///
    /// 用途：文本表落库前/候选校验期检查全量 key 的字形覆盖；发布前缺字可诊断。
    /// 控制字符（换行/制表）不计缺字——它们由排版层消费，不是字形。
    /// </summary>
    public static class FontGlyphAudit
    {
        /// <summary>逐字符审计结果：文本 → 缺字列表（去重保序）。空列表 = 全覆盖。</summary>
        public sealed class Result
        {
            public string Text;
            public readonly List<char> Missing = new List<char>(4);
            public bool Covered => Missing.Count == 0;
        }

        /// <summary>审计一组文本（每条独立出结果；条目顺序与输入一致）。
        /// 文本表（tbtext）落库后由装载侧平铺 key×locale 全量文本进本入口——审计不依赖表装载时机。</summary>
        public static List<Result> Audit(IEnumerable<string> texts, TMP_FontAsset main,
            IReadOnlyList<TMP_FontAsset> fallbacks)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));

            var results = new List<Result>();
            foreach (string text in texts)
            {
                var r = new Result { Text = text ?? string.Empty };
                if (!string.IsNullOrEmpty(text))
                {
                    var seen = new HashSet<char>();
                    foreach (char c in text)
                    {
                        if (IsControl(c)) continue;
                        if (!seen.Add(c)) continue;                     // 去重（缺字报告按字符不重复）
                        if (!FamilyHas(main, fallbacks, c)) r.Missing.Add(c);
                    }
                }
                results.Add(r);
            }
            return results;
        }

        /// <summary>格式化报告（诊断输出："key/locale: 缺 [字符]"逐行；空 = 全覆盖一行）。</summary>
        public static string Report(IReadOnlyList<Result> results)
        {
            var sb = new StringBuilder();
            int missing = 0;
            foreach (var r in results)
            {
                if (r.Covered) continue;
                missing++;
                sb.Append(r.Text).Append(": 缺 [");
                sb.Append(string.Join(" ", r.Missing));
                sb.AppendLine("]");
            }
            if (missing == 0) sb.AppendLine("字体族缺字审计: 全覆盖");
            else sb.Insert(0, $"字体族缺字审计: {missing} 条文本缺字{Environment.NewLine}");
            return sb.ToString();
        }

        /// <summary>单字符是否被字体族覆盖（主字体 → fallback 逐级；与 TMP 运行时查找序一致）。
        /// 3 参 HasCharacter(unicode, searchFallbacks, tryAddCharacter)：本字体直查、不触发 Dynamic 补图集。</summary>
        public static bool FamilyHas(TMP_FontAsset main, IReadOnlyList<TMP_FontAsset> fallbacks, char c)
        {
            if (main != null && main.HasCharacter(c, false, false))
                return true;
            if (fallbacks == null) return false;
            for (int i = 0; i < fallbacks.Count; i++)
            {
                var f = fallbacks[i];
                if (f != null && f.HasCharacter(c, false, false))
                    return true;
            }
            return false;
        }

        /// <summary>排版控制字符（不占字形——由 TMP/排版层处理，不是缺字）。</summary>
        private static bool IsControl(char c)
            => c == '\n' || c == '\r' || c == '\t' || char.IsControl(c);
    }
}
