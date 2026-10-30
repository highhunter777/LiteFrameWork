using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 布局/适配族：CanvasScaler 基线（工具链设计 §6.2「CanvasScaler 不符」；制作规范 UI-LAYOUT-01/§3.1）。
    /// `Page_*` 根必须 <c>ScaleWithScreenSize</c> + 参考 1920×1080 + Match 0.5（偏离＝Error，一条汇总）；
    /// 页面子树出现第二套 CanvasScaler＝Error（「页面不得自带第二套缩放——子 Canvas 继承」）。
    /// 组件缺失归结构族（ui.page-structure），本规则只辖参数与重复面；Canvas 根 RectTransform 受驱动不检查锚点。
    /// </summary>
    public sealed class LayoutScalerRule : IUiRule
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float Match = 0.5f;

        public string RuleId => UiRuleIds.LayoutScaler;
        public string Family => "布局/适配";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!PageStructureRule.IsPageAsset(asset)) continue;
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;

                var deviations = new List<string>();
                CanvasScaler scaler = prefab.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                        deviations.Add("模式=" + scaler.uiScaleMode + "（须 ScaleWithScreenSize）");
                    if (scaler.referenceResolution != new Vector2(ReferenceWidth, ReferenceHeight))
                        deviations.Add("参考分辨率=" + scaler.referenceResolution + "（须 " + ReferenceWidth + "×" + ReferenceHeight + "）");
                    if (System.Math.Abs(scaler.matchWidthOrHeight - Match) > 0.001f)
                        deviations.Add("Match=" + scaler.matchWidthOrHeight.ToString("0.##") + "（须 " + Match + "）");
                }
                if (deviations.Count > 0)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Message = "根 CanvasScaler 偏离基线：" + string.Join("；", deviations),
                        FixHint = "按 UI-LAYOUT-01 统一（ScaleWithScreenSize 1920×1080 Match 0.5）",
                    });
                }

                foreach (CanvasScaler nested in prefab.GetComponentsInChildren<CanvasScaler>(true))
                {
                    if (nested.transform == prefab.transform) continue;
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Node = Binding.UiBindingRename.RelativePath(nested.transform),
                        Message = "页面自带第二套缩放器（" + Binding.UiBindingRename.RelativePath(nested.transform)
                            + "）——子 Canvas 应继承根缩放（UI-LAYOUT-01）",
                        FixHint = "移除子 CanvasScaler；子画布按需只调 overrideSorting/排序带",
                    });
                }
            }
            return issues;
        }
    }
}
