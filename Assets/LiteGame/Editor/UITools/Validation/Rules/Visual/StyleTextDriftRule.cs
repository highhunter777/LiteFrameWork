using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.EditorTools.UI;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 样式族：文字档对账（工具链设计 §6.2「裸字号」；§8.1 文字枚举/字号精确应用）。
    /// </summary>
    public sealed class StyleTextDriftRule : IUIRule
    {
        public string RuleId => UIRuleIds.StyleTextDrift;
        public string Family => "样式";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            var assetSet = new HashSet<string>(context.AssetPaths ?? new string[0], System.StringComparer.Ordinal);
            if (assetSet.Count == 0) return issues;

            var scanDirs = context.AssetPaths
                .Select(Path.GetDirectoryName)
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d.Replace('\\', '/'))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();

            foreach (UITextIssue issue in UITextStyleTool.ReportTextDrift(scanDirs)
                .Concat(UITextStyleTool.ReportBrokenRefs(scanDirs)))
            {
                string assetPath = (issue.Asset ?? string.Empty).Replace('\\', '/');
                if (!assetSet.Contains(assetPath)) continue;

                UISeverity severity;
                string kind;
                string fixHint;
                switch (issue.Kind)
                {
                    case UITextIssueKind.Unmarked:
                        severity = UISeverity.Warning;
                        kind = "未标记（裸字号）";
                        fixHint = "挂 UITextStyleRef 声明角色档，或重跑样式工具按声明收敛（报告不擅改）";
                        break;
                    case UITextIssueKind.Mismatch:
                        severity = UISeverity.Warning;
                        kind = "标记漂移";
                        fixHint = "重跑样式工具按声明收敛，或核对角色档（报告不擅改）";
                        break;
                    case UITextIssueKind.OverrideMisuse:
                        severity = UISeverity.Error;
                        kind = "实例覆盖滥用";
                        fixHint = "UseInstanceOverrides 仅限嵌套 prefab 实例；直挂节点请移除该标记";
                        break;
                    default:
                        severity = UISeverity.Error;
                        kind = "标记失效";
                        fixHint = "修复失效标记（补文本组件/字体引用）";
                        break;
                }
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = severity,
                    Asset = assetPath,
                    Node = issue.Node,
                    Message = kind + "：" + issue.Detail,
                    FixHint = fixHint,
                });
            }
            return issues;
        }
    }
}
