using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 性能族：`raycastTarget` 预算（工具链设计 §6.2「raycastTarget 过多」；总设计 §11.6「静态规则」）。
    /// </summary>
    public sealed class PerformanceRaycastTargetRule : IUIRule
    {
        public string RuleId => UIRuleIds.PerformanceRaycastTarget;
        public string Family => "性能";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!asset.EndsWith(".prefab", System.StringComparison.Ordinal)) continue;
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;

                int count = prefab.GetComponentsInChildren<Graphic>(true).Count(g => g.raycastTarget);
                if (count > UIRuleThresholds.RaycastTargetBudget)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Warning,
                        Asset = asset,
                        Message = "raycastTarget 开启数 " + count + " 超预算 " + UIRuleThresholds.RaycastTargetBudget,
                        FixHint = "非交互装饰件关 raycastTarget（纯视觉 Image/Text 不参与命中）",
                    });
                }
            }
            return issues;
        }
    }
}
