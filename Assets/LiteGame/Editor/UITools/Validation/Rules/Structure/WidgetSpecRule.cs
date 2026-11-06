using System;
using System.Collections.Generic;
using System.IO;
using LiteFramework.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 契约族：控件模板必须声明 `WidgetSpec`（《UI 编辑器工具链专项设计》§6.2「契约」+ §8.4）。
    /// </summary>
    public sealed class WidgetSpecRule : IUIRule
    {
        public string RuleId => UIRuleIds.WidgetSpec;
        public string Family => "契约";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!IsWidgetAsset(asset)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = asset,
                        Message = "控件模板 prefab 无法读取",
                        FixHint = "确认 prefab 未损坏、路径在版本库中",
                    });
                    continue;
                }
                if (prefab.GetComponent<WidgetSpec>() != null) continue;

                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Error,
                    Asset = asset,
                    Message = "控件模板缺 WidgetSpec（能力声明）",
                    FixHint = "在模板根挂 WidgetSpec，填 Owner/Summary/状态/事件/尺寸行为后重跑控件清单（§8.4）",
                });
            }
            return issues;
        }

        /// <summary>
        /// 控件模板候选：`Tmp_*.prefab` 且不在 `Screens/` 下（`Screens/Tmp_*` 是页面基线/系统面，不属于控件库模板）。
        /// </summary>
        public static bool IsWidgetAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.Ordinal))
                return false;
            if (assetPath.Replace('\\', '/').Contains("/Screens/")) return false;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            return name != null && name.StartsWith("Tmp_", StringComparison.Ordinal);
        }
    }
}
