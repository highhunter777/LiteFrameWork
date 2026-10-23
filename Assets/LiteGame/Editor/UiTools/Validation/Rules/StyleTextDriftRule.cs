using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.EditorTools.UI;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 样式族：文字档对账（工具链设计 §6.2「裸字号」；§8.1 文字枚举/字号精确应用）。
    /// 复用 <see cref="UiTextStyleTool"/> 对账单源两路：<see cref="UiTextStyleTool.ReportTextDrift"/>（有文本组件的节点——
    /// 未标记/属性漂移/font 缺失）与 <see cref="UiTextStyleTool.ReportBrokenRefs"/>（带标记但无文本组件的失效标记）：
    /// 未标记（Unmarked，无 <c>UiTextStyleRef</c>＝裸字号整改清单）＝Warning；
    /// 属性漂移（Mismatch，有标记但组件属性≠角色档基线）＝Warning（报告不擅改，应用按声明收敛）；
    /// 标记失效（BrokenRef，无文本组件/TMP font 缺失）＝Error（资源引用失效）。
    /// 作用域＝上下文资产所在目录（去重），结果按上下文资产面过滤归属。
    /// </summary>
    public sealed class StyleTextDriftRule : IUiRule
    {
        public string RuleId => UiRuleIds.StyleTextDrift;
        public string Family => "样式";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            var assetSet = new HashSet<string>(context.AssetPaths ?? new string[0], System.StringComparer.Ordinal);
            if (assetSet.Count == 0) return issues;

            var scanDirs = context.AssetPaths
                .Select(Path.GetDirectoryName)
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d.Replace('\\', '/'))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();

            foreach (UiTextIssue issue in UiTextStyleTool.ReportTextDrift(scanDirs)
                .Concat(UiTextStyleTool.ReportBrokenRefs(scanDirs)))
            {
                string assetPath = (issue.Asset ?? string.Empty).Replace('\\', '/');
                if (!assetSet.Contains(assetPath)) continue;

                UiSeverity severity;
                string kind;
                switch (issue.Kind)
                {
                    case UiTextIssueKind.Unmarked:
                        severity = UiSeverity.Warning;
                        kind = "未标记（裸字号）";
                        break;
                    case UiTextIssueKind.Mismatch:
                        severity = UiSeverity.Warning;
                        kind = "标记漂移";
                        break;
                    default:
                        severity = UiSeverity.Error;
                        kind = "标记失效";
                        break;
                }
                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = severity,
                    Asset = assetPath,
                    Node = issue.Node,
                    Message = kind + "：" + issue.Detail,
                    FixHint = severity == UiSeverity.Error
                        ? "修复失效标记（补文本组件/字体引用）"
                        : "挂 UiTextStyleRef 声明角色档，或重跑样式工具按声明收敛（报告不擅改）",
                });
            }
            return issues;
        }
    }
}
