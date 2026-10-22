using System;
using System.Collections.Generic;
using System.IO;
using LiteClient.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 结构族：页面根契约（《UI 编辑器工具链专项设计》§6.2「结构」+《UI 工作台》§3.2 根契约）：
    /// `Page_*` prefab 根必须有 Canvas / CanvasScaler / GraphicRaycaster / CanvasGroup，子树必须有
    /// `SafeAreaReceiver`。按**文件名前缀**识别页面（不限目录，便于临时/迁移资产）；
    /// HUD 与 UIMain 的根契约尚未在设计冻结，v1.0 不覆盖（见审计表）。
    /// </summary>
    public sealed class PageStructureRule : IUiRule
    {
        private static readonly string[] RequiredRootComponents =
        {
            "Canvas", "CanvasScaler", "GraphicRaycaster", "CanvasGroup",
        };

        public string RuleId => UiRuleIds.PageStructure;
        public string Family => "结构";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            foreach (string asset in context.AssetPaths)
            {
                if (!IsPageAsset(asset)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Message = "页面 prefab 无法读取",
                        FixHint = "确认 prefab 未损坏、路径在版本库中",
                    });
                    continue;
                }

                var missing = new List<string>();
                if (prefab.GetComponent<Canvas>() == null) missing.Add(RequiredRootComponents[0]);
                if (prefab.GetComponent<CanvasScaler>() == null) missing.Add(RequiredRootComponents[1]);
                if (prefab.GetComponent<GraphicRaycaster>() == null) missing.Add(RequiredRootComponents[2]);
                if (prefab.GetComponent<CanvasGroup>() == null) missing.Add(RequiredRootComponents[3]);
                if (missing.Count > 0)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Message = "页面根缺 " + string.Join("/", missing),
                        FixHint = "按《UI 工作台》§3.2 根契约补齐（Canvas + CanvasScaler 1920×1080 Match 0.5 + GraphicRaycaster + CanvasGroup）",
                    });
                }

                if (prefab.GetComponentsInChildren<SafeAreaReceiver>(true).Length == 0)
                {
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Message = "页面缺 SafeAreaReceiver（安全区未适配）",
                        FixHint = "在内容容器挂 SafeAreaReceiver（根契约要求，见《UI 工作台》§3.2）",
                    });
                }
            }
            return issues;
        }

        /// <summary>页面候选：`Page_*.prefab`（按文件名前缀，目录不限）。</summary>
        public static bool IsPageAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.Ordinal))
                return false;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            return name != null && name.StartsWith("Page_", StringComparison.Ordinal);
        }
    }
}
