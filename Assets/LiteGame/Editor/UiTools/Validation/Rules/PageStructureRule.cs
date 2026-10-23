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
    /// 结构族：根契约（《UI 编辑器工具链专项设计》§6.2「结构」+《UI 制作规范》§3.1/§3.3 根契约——按制作规范
    /// 对齐实况口径的裁决版）：三类根各有 MUST 面——
    /// `Page_*`：Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup 四件套 + 子树 SafeAreaReceiver；
    /// `Hud_*`：Canvas/CanvasScaler/CanvasGroup（GraphicRaycaster 按需——默认根无输入面，交互角组以子 Canvas 开射线）；
    /// `Tmp_*`（Screens 系统面）：Canvas/CanvasGroup（**禁** CanvasScaler——继承根缩放；GraphicRaycaster 按需）。
    /// Tmp_*（Widgets 控件模板）归 <see cref="WidgetSpecRule"/>/词表域，本规则不辖。
    /// 按文件名前缀识别（目录不限，便于临时/迁移资产）；系统面 Tmp_ 按 Screens 路径段判定。
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
                if (!asset.EndsWith(".prefab", StringComparison.Ordinal)) continue;
                string name = Path.GetFileNameWithoutExtension(asset);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (prefab == null) continue;

                if (IsPageAsset(asset)) CheckPageRoot(issues, asset, prefab);
                else if (IsHudAsset(asset)) CheckHudRoot(issues, asset, prefab);
                else if (IsScreensSystemAsset(asset)) CheckSystemRoot(issues, asset, prefab);
            }
            return issues;
        }

        /// <summary>页面候选：`Page_*.prefab`（按文件名前缀，目录不限）。</summary>
        public static bool IsPageAsset(string assetPath)
        {
            string name = FileNameOf(assetPath);
            return name != null && name.StartsWith("Page_", StringComparison.Ordinal);
        }

        /// <summary>HUD 候选：`Hud_*.prefab`（按文件名前缀，目录不限）。</summary>
        public static bool IsHudAsset(string assetPath)
        {
            string name = FileNameOf(assetPath);
            return name != null && name.StartsWith("Hud_", StringComparison.Ordinal);
        }

        /// <summary>系统面候选：Screens 目录下的 `Tmp_*.prefab`（Widgets 控件模板不辖）。</summary>
        public static bool IsScreensSystemAsset(string assetPath)
        {
            string name = FileNameOf(assetPath);
            return name != null && name.StartsWith("Tmp_", StringComparison.Ordinal)
                && (assetPath ?? string.Empty).Replace('\\', '/').Contains("/Screens/");
        }

        private static void CheckPageRoot(List<UiIssue> issues, string asset, GameObject prefab)
        {
            var missing = new List<string>();
            if (prefab.GetComponent<Canvas>() == null) missing.Add(RequiredRootComponents[0]);
            if (prefab.GetComponent<CanvasScaler>() == null) missing.Add(RequiredRootComponents[1]);
            if (prefab.GetComponent<GraphicRaycaster>() == null) missing.Add(RequiredRootComponents[2]);
            if (prefab.GetComponent<CanvasGroup>() == null) missing.Add(RequiredRootComponents[3]);
            if (missing.Count > 0)
            {
                issues.Add(new UiIssue
                {
                    RuleId = UiRuleIds.PageStructure,
                    Severity = UiSeverity.Error,
                    Asset = asset,
                    Message = "页面根缺 " + string.Join("/", missing),
                    FixHint = "按《UI 制作规范》§3.1 根契约补齐（Canvas + CanvasScaler 1920×1080 Match 0.5 + GraphicRaycaster + CanvasGroup）",
                });
            }

            if (prefab.GetComponentsInChildren<SafeAreaReceiver>(true).Length == 0)
            {
                issues.Add(new UiIssue
                {
                    RuleId = UiRuleIds.PageStructure,
                    Severity = UiSeverity.Error,
                    Asset = asset,
                    Message = "页面缺 SafeAreaReceiver（安全区未适配）",
                    FixHint = "在内容容器挂 SafeAreaReceiver（根契约要求，见《UI 制作规范》§3.1）",
                });
            }
        }

        private static void CheckHudRoot(List<UiIssue> issues, string asset, GameObject prefab)
        {
            var missing = new List<string>();
            if (prefab.GetComponent<Canvas>() == null) missing.Add("Canvas");
            if (prefab.GetComponent<CanvasScaler>() == null) missing.Add("CanvasScaler");
            if (prefab.GetComponent<CanvasGroup>() == null) missing.Add("CanvasGroup");
            if (missing.Count > 0)
            {
                issues.Add(new UiIssue
                {
                    RuleId = UiRuleIds.PageStructure,
                    Severity = UiSeverity.Error,
                    Asset = asset,
                    Message = "HUD 根缺 " + string.Join("/", missing),
                    FixHint = "按《UI 制作规范》§3.3 HUD 根契约补齐（Canvas + CanvasScaler + CanvasGroup；射线按需——子 Canvas 模式）",
                });
            }
        }

        private static void CheckSystemRoot(List<UiIssue> issues, string asset, GameObject prefab)
        {
            var missing = new List<string>();
            if (prefab.GetComponent<Canvas>() == null) missing.Add("Canvas");
            if (prefab.GetComponent<CanvasGroup>() == null) missing.Add("CanvasGroup");
            if (missing.Count > 0)
            {
                issues.Add(new UiIssue
                {
                    RuleId = UiRuleIds.PageStructure,
                    Severity = UiSeverity.Error,
                    Asset = asset,
                    Message = "系统面根缺 " + string.Join("/", missing),
                    FixHint = "按《UI 制作规范》§3.1 系统面根契约补齐（Canvas + CanvasGroup；射线按需）",
                });
            }

            if (prefab.GetComponent<CanvasScaler>() != null)
            {
                issues.Add(new UiIssue
                {
                    RuleId = UiRuleIds.PageStructure,
                    Severity = UiSeverity.Error,
                    Asset = asset,
                    Message = "系统面根自带 CanvasScaler——继承根缩放（UI-LAYOUT-01 同口径）",
                    FixHint = "移除 CanvasScaler（嵌套 Canvas 继承 UIMain 根缩放）",
                });
            }
        }

        private static string FileNameOf(string assetPath)
            => string.IsNullOrEmpty(assetPath) ? null : Path.GetFileNameWithoutExtension(assetPath);
    }
}
