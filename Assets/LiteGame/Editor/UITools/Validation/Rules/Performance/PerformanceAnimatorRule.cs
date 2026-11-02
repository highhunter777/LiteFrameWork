using System.Collections.Generic;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 性能族：UI 禁 Animator（工具链设计 §6.2「Animator」；制作规范 UI-PREFAB-03「页面根不得挂 Animator——动效走 UIFx/转场」）。
    /// 页面根挂 Animator＝Error（MUST）；其余任意位置（含模板/HUD/子树）＝Warning（动效统一走 UIFx/转场，
    /// Animator 带整链状态机与重序列化成本）。每个 Animator 节点一条问题。
    /// </summary>
    public sealed class PerformanceAnimatorRule : IUIRule
    {
        public string RuleId => UIRuleIds.PerformanceAnimator;
        public string Family => "性能";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!asset.EndsWith(".prefab", System.StringComparison.Ordinal)) continue;
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;

                bool isPageRoot = PageStructureRule.IsPageAsset(asset);
                foreach (Animator animator in prefab.GetComponentsInChildren<Animator>(true))
                {
                    bool onPageRoot = isPageRoot && animator.transform == prefab.transform;
                    string node = Binding.UIBindingRename.RelativePath(animator.transform);
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = onPageRoot ? UISeverity.Error : UISeverity.Warning,
                        Asset = asset,
                        Node = node,
                        Message = onPageRoot
                            ? "页面根挂 Animator（UI-PREFAB-03）"
                            : "UI 内使用 Animator（" + node + "）——动效统一走 UIFx/转场",
                        FixHint = "改 UIFx/转场动效（随展示作用域收口）；确需 Animator 先登记 UIRuleExceptions",
                    });
                }
            }
            return issues;
        }
    }
}
