using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LiteGame.Editor.UiTools.Text;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 文本族：富文本标签白名单（工具链设计 §6.2「富文本越界」；制作规范 UI-TEXT-05「作者文本只允许白名单标签」）。
    /// 检查 tbltext 全部语言列值里的 <c>&lt;tag…&gt;</c> 标签（大小写不敏感），标签不在
    /// <see cref="UiRuleThresholds.RichTextAllowedTags"/> 内＝Warning（SHOULD 级——可见即整改或例外）。
    /// 表读取失败显式报一条 Error；种子注入同 <see cref="TextStaticKeyRule"/>。
    /// </summary>
    public sealed class TextRichTextRule : IUiRule
    {
        private static readonly Regex TagPattern = new Regex(@"<\s*/?\s*([a-zA-Z][a-zA-Z0-9_]*)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public string RuleId => UiRuleIds.TextRichText;
        public string Family => "文本";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            if (!UiLTextEntries.TryResolve(context, out var entries, out string tableError))
            {
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
                    Asset = "tbltext",
                    Message = "文案表读取失败：" + tableError,
                    FixHint = "检查 Luban 生成物与表数据（重跑 gen.bat）",
                });
                return issues;
            }
            var allowed = new HashSet<string>(UiRuleThresholds.RichTextAllowedTags,
                System.StringComparer.OrdinalIgnoreCase);

            foreach (UiLTextEntry entry in entries)
            {
                var offenders = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
                Collect(entry.Zh, allowed, offenders);
                Collect(entry.En, allowed, offenders);
                if (offenders.Count == 0) continue;

                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Warning,
                    Asset = "tbltext",
                    Node = entry.Key,
                    Message = "富文本标签越白名单：" + entry.Key + "（" + string.Join("/", offenders) + "）",
                    FixHint = "作者文本只允许白名单标签（" + string.Join("/", UiRuleThresholds.RichTextAllowedTags)
                        + "）；确需越界先登记 UiRuleExceptions",
                });
            }
            return issues;
        }

        private static void Collect(string value, HashSet<string> allowed, SortedSet<string> offenders)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (Match match in TagPattern.Matches(value))
            {
                string tag = match.Groups[1].Value;
                if (!allowed.Contains(tag)) offenders.Add(tag);
            }
        }
    }
}
