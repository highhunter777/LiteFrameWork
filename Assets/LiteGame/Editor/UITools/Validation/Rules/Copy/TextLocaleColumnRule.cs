using System.Collections.Generic;
using System.Linq;
using LiteGame.Editor.UITools.Text;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 文本族：语言列完整性（工具链设计 §6.2「语言列缺失」）。
    /// </summary>
    public sealed class TextLocaleColumnRule : IUIRule
    {
        public string RuleId => UIRuleIds.TextLocaleColumn;
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

            foreach (UILTextEntry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;
                if (string.IsNullOrWhiteSpace(entry.Zh))
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = "tbltext",
                        Node = entry.Key,
                        Message = "源语言列缺失（Zh 空）：" + entry.Key,
                        FixHint = "源语言是派生源——补齐 zh 列后重跑 gen.bat",
                    });
                }
                else if (string.IsNullOrWhiteSpace(entry.En))
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Warning,
                        Asset = "tbltext",
                        Node = entry.Key,
                        Message = "英文列缺失（En 空）：" + entry.Key,
                        FixHint = "补翻译（欠账可见，可提交——进入页面 Warning 度量）",
                    });
                }
            }
            return issues;
        }
    }
}
