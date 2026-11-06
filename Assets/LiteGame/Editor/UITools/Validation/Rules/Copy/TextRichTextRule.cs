using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LiteGame.Editor.UITools.Text;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 文本族：富文本标签白名单（工具链设计 §6.2「富文本越界」；制作规范 UI-TEXT-05「作者文本只允许白名单标签」）。
    /// </summary>
    public sealed class TextRichTextRule : IUIRule
    {
        private static readonly Regex TagPattern = new Regex(@"<\s*/?\s*([a-zA-Z][a-zA-Z0-9_]*)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public string RuleId => UIRuleIds.TextRichText;
        public string Family => "文本";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            if (!UILTextEntries.TryResolve(context, out var entries, out string tableError))
            {
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Error,
                    Asset = "tbltext",
                    Message = "文案表读取失败：" + tableError,
                    FixHint = "检查 Luban 生成物与表数据（重跑 gen.bat）",
                });
                return issues;
            }
            var allowed = new HashSet<string>(UIRuleThresholds.RichTextAllowedTags,
                System.StringComparer.OrdinalIgnoreCase);

            foreach (UILTextEntry entry in entries)
            {
                var offenders = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
                Collect(entry.Zh, allowed, offenders);
                Collect(entry.En, allowed, offenders);
                if (offenders.Count == 0) continue;

                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Warning,
                    Asset = "tbltext",
                    Node = entry.Key,
                    Message = "富文本标签越白名单：" + entry.Key + "（" + string.Join("/", offenders) + "）",
                    FixHint = "作者文本只允许白名单标签（" + string.Join("/", UIRuleThresholds.RichTextAllowedTags)
                        + "）；确需越界先登记 UIRuleExceptions",
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
