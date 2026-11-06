using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.EditorTools.UI;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 样式族：颜色 token 对账（工具链设计 §6.2「裸颜色」；§8.1「漂移报告区分引用缺失/解析值不符/裸值」）。
    /// </summary>
    public sealed class StyleColorDriftRule : IUIRule
    {
        public string RuleId => UIRuleIds.StyleColorDrift;
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

            foreach ((string asset, string node, Color color) drift in UIStyleTool.ReportDrift(scanDirs))
            {
                string assetPath = (drift.asset ?? string.Empty).Replace('\\', '/');
                if (!assetSet.Contains(assetPath)) continue;
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Warning,
                    Asset = assetPath,
                    Node = drift.node,
                    Message = "颜色不命中任何 token：" + drift.color + "（" + drift.node + "——手调/漂移，对账暴露）",
                    FixHint = "收敛到 UiStyle token（改 token 值重跑样式工具刷新）或维持手调并知悉质量度量",
                });
            }
            return issues;
        }
    }
}
