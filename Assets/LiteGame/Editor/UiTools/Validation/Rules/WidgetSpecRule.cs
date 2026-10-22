using System;
using System.Collections.Generic;
using System.IO;
using LiteClient.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 契约族：控件模板必须声明 `WidgetSpec`（《UI 编辑器工具链专项设计》§6.2「契约」+ §8.4）。
    /// 缺声明＝Error（P1-2 把「缺 spec」记为 Warning 只为让清单先能生成；校验面按设计应为 Error）。
    /// 按**文件名前缀** `Tmp_` 识别控件模板（目录不限）。
    /// </summary>
    public sealed class WidgetSpecRule : IUiRule
    {
        public string RuleId => UiRuleIds.WidgetSpec;
        public string Family => "契约";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!IsWidgetAsset(asset)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Message = "控件模板 prefab 无法读取",
                        FixHint = "确认 prefab 未损坏、路径在版本库中",
                    });
                    continue;
                }
                if (prefab.GetComponent<WidgetSpec>() != null) continue;

                issues.Add(new UiIssue
                {
                    RuleId = RuleId,
                    Severity = UiSeverity.Error,
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
