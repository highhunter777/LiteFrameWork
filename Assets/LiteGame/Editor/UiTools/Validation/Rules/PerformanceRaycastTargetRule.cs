using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 性能族：`raycastTarget` 预算（工具链设计 §6.2「raycastTarget 过多」；总设计 §11.6「静态规则」）。
    /// 单 prefab 开启数超过 <see cref="UiRuleThresholds.RaycastTargetBudget"/>＝一条 Warning（SHOULD 级；
    /// 非交互装饰件应关 raycastTarget——预算随真实面校准，不在规则写死）。
    /// </summary>
    public sealed class PerformanceRaycastTargetRule : IUiRule
    {
        public string RuleId => UiRuleIds.PerformanceRaycastTarget;
        public string Family => "性能";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!asset.EndsWith(".prefab", System.StringComparison.Ordinal)) continue;
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;

                int count = prefab.GetComponentsInChildren<Graphic>(true).Count(g => g.raycastTarget);
                if (count > UiRuleThresholds.RaycastTargetBudget)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Warning,
                        Asset = asset,
                        Message = "raycastTarget 开启数 " + count + " 超预算 " + UiRuleThresholds.RaycastTargetBudget,
                        FixHint = "非交互装饰件关 raycastTarget（纯视觉 Image/Text 不参与命中）",
                    });
                }
            }
            return issues;
        }
    }
}
