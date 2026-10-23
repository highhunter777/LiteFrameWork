using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 性能族：布局重建深度（工具链设计 §6.2「LayoutGroup/ContentSizeFitter 过深」）。
    /// 同轴连续嵌套的 LayoutGroup/ContentSizeFitter 链深度超过 <see cref="UiRuleThresholds.LayoutDepthLimit"/>
    /// ＝一条 Warning（嵌套布局链是重建成本的主要来源；阈值随真实面校准）。
    /// 链深按节点向上累计：同节点 LayoutGroup+ContentSizeFitter 记一层。
    /// </summary>
    public sealed class PerformanceLayoutDepthRule : IUiRule
    {
        public string RuleId => UiRuleIds.PerformanceLayoutDepth;
        public string Family => "性能";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!asset.EndsWith(".prefab", System.StringComparison.Ordinal)) continue;
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;

                int deepest = 0;
                string deepestNode = null;
                foreach (Transform node in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (!HasLayout(node)) continue;
                    int depth = DepthOf(node);
                    if (depth > deepest)
                    {
                        deepest = depth;
                        deepestNode = Binding.UiBindingRename.RelativePath(node);
                    }
                }
                if (deepest > UiRuleThresholds.LayoutDepthLimit)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Warning,
                        Asset = asset,
                        Node = deepestNode,
                        Message = "布局链深度 " + deepest + " 超上限 " + UiRuleThresholds.LayoutDepthLimit
                            + "（" + deepestNode + "）",
                        FixHint = "拆平嵌套容器（合并 LayoutGroup/改固定尺寸中间层）",
                    });
                }
            }
            return issues;
        }

        private static bool HasLayout(Transform node)
        {
            return node.GetComponent<LayoutGroup>() != null || node.GetComponent<ContentSizeFitter>() != null;
        }

        private static int DepthOf(Transform node)
        {
            int depth = 0;
            for (Transform t = node; t != null && t is RectTransform && HasLayout(t); t = t.parent)
                depth++;
            return depth;
        }
    }
}
