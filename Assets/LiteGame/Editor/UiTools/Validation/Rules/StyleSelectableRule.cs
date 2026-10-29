using System;
using System.Collections.Generic;
using LiteFramework.UI;
using LiteGame.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 样式族：uGUI Selectable 状态色接缝（《UI 系统总设计》§6.2/§6.5，P2-3 B4）。判据：
    /// ① ColorTint Selectable 缺 `UiSelectableTheme`＝Warning；② 组件节点无 Selectable＝Error；
    /// ③ 5 枚状态令牌任一未知＝Error；④ ColorBlock 当前值与令牌现值不命中＝Warning；
    /// ⑤ 非 ColorTint（None/SpriteSwap/Animation）不适用，跳过。
    /// </summary>
    public sealed class StyleSelectableRule : IUiRule
    {
        public string RuleId => UiRuleIds.StyleSelectable;
        public string Family => "样式";

        private static readonly (string Label, string Token)[] Slots =
        {
            ("normal", UiSelectableTheme.NormalToken),
            ("highlighted", UiSelectableTheme.HighlightedToken),
            ("pressed", UiSelectableTheme.PressedToken),
            ("selected", UiSelectableTheme.SelectedToken),
            ("disabled", UiSelectableTheme.DisabledToken),
        };

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            var assetSet = new HashSet<string>(context.AssetPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (assetSet.Count == 0) return issues;

            UiTokenCatalog catalog;
            try
            {
                catalog = UiTokenCatalog.Load(context.ThemeAssetPath);
            }
            catch (Exception ex)
            {
                foreach (string asset in assetSet)
                    issues.Add(Issue(UiSeverity.Error, asset, "-",
                        "主题资产不可读，Selectable 状态色对账无法执行：" + ex.Message,
                        "先跑 ui.style.tokens 生成 UiTheme.asset（或修正 UiValidationContext.ThemeAssetPath）"));
                return issues;
            }

            foreach (string asset in context.AssetPaths)
            {
                string assetPath = (asset ?? string.Empty).Replace('\\', '/');
                if (!assetSet.Contains(assetPath)) continue;
                if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                CheckPrefab(assetPath, catalog, issues);
            }
            return issues;
        }

        private static void CheckPrefab(string assetPath, UiTokenCatalog catalog, List<UiIssue> issues)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                // ② 组件节点必须真有 Selectable（异常 prefab/被删组件的负例）
                foreach (UiSelectableTheme theme in root.GetComponentsInChildren<UiSelectableTheme>(true))
                    if (!PrefabUtility.IsPartOfPrefabInstance(theme) && theme.GetComponent<Selectable>() == null)
                        issues.Add(Issue(UiSeverity.Error, assetPath, theme.transform.name,
                            "UiSelectableTheme 节点没有 Selectable（无注入目标）",
                            "摘除组件或补 Selectable（组件不再 RequireComponent，缺 Selectable 由此规则显式报 Error）"));

                foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
                {
                    // 嵌套实例由源模板统一检查（与 UiSelectableThemeApplier 同判据）：避免同一继承 ColorBlock 在多份实例重复报。
                    if (PrefabUtility.IsPartOfPrefabInstance(selectable)) continue;
                    if (selectable.transition != Selectable.Transition.ColorTint) continue;   // ⑤ 不适用

                    var theme = selectable.GetComponent<UiSelectableTheme>();
                    if (theme == null)
                    {
                        issues.Add(Issue(UiSeverity.Warning, assetPath, selectable.transform.name,
                            "ColorTint Selectable 缺 UiSelectableTheme（状态色未接主题）",
                            "跑 ui.selectable-theme 迁移件补组件，或改用非 ColorTint"));
                        continue;
                    }

                    ColorBlock block = selectable.colors;
                    Color[] values = { block.normalColor, block.highlightedColor, block.pressedColor, block.selectedColor, block.disabledColor };
                    for (int i = 0; i < Slots.Length; i++)
                    {
                        if (!catalog.TryColor(Slots[i].Token, out Color tokenValue))
                        {
                            issues.Add(Issue(UiSeverity.Error, assetPath, selectable.transform.name,
                                "Selectable 状态色未知令牌：" + Slots[i].Label + " = " + Slots[i].Token,
                                "改令牌 ID 或补令牌后重跑 ui.style.tokens"));
                            continue;
                        }
                        if (!UiStyleResolver.Hits(values[i], tokenValue))
                            issues.Add(Issue(UiSeverity.Warning, assetPath, selectable.transform.name,
                                "Selectable 状态色漂移：" + Slots[i].Label + " 序列化值 ≠ 令牌现值（" + Slots[i].Token + "）",
                                "跑 ui.selectable-theme 迁移件（只补组件）后由主题注入收敛；或人工核对"));
                    }
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static UiIssue Issue(UiSeverity severity, string asset, string node, string message, string hint)
            => new UiIssue
            {
                RuleId = UiRuleIds.StyleSelectable,
                Severity = severity,
                Asset = asset,
                Node = node,
                Message = message,
                FixHint = hint,
            };
    }
}