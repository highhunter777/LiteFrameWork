using System;
using System.Collections.Generic;
using LiteFramework.UI;
using LiteGame.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 样式族：颜色令牌引用对账（《UI 系统总设计》§6.2；工具链设计 §6 校验核心）。
    /// </summary>
    public sealed class StyleRefRule : IUIRule
    {
        public string RuleId => UIRuleIds.StyleRef;
        public string Family => "样式";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            var assetSet = new HashSet<string>(context.AssetPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (assetSet.Count == 0) return issues;

            UITokenCatalog catalog;
            try
            {
                catalog = UITokenCatalog.Load(context.ThemeAssetPath);
            }
            catch (Exception ex)
            {
                foreach (string asset in assetSet)
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = asset,
                        Node = "-",
                        Message = "主题资产不可读，引用对账无法执行：" + ex.Message,
                        FixHint = "先跑 ui.style.tokens 生成 UITheme.asset（或修正 UIValidationContext.ThemeAssetPath）",
                    });
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

        private static void CheckPrefab(string assetPath, UITokenCatalog catalog, List<UIIssue> issues)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                foreach (UIStyleRef styleRef in root.GetComponentsInChildren<UIStyleRef>(true))
                {
                    string node = styleRef.transform.name;
                    if (UIThemeBinder.IsGraphicOwnedByWidget(styleRef))
                    {
                        issues.Add(Issue(UISeverity.Error, assetPath, node,
                            "UIStyleRef 的目标 Graphic 被自身/祖先 IThemeGraphicWriter 认领（双写者）——摘除其一",
                            "摘除 UIStyleRef（状态色由 widget token 字段声明）或摘除 writer"));
                        continue;
                    }

                    string token = styleRef.ColorToken;
                    if (string.IsNullOrEmpty(token)) continue;             // 空＝未声明合法

                    if (!catalog.TryColor(token, out Color value))
                    {
                        issues.Add(Issue(UISeverity.Error, assetPath, node,
                            "未知颜色令牌：" + token + "（不在令牌集——不猜默认色）",
                            "改令牌 ID 或在 UIStyleTokens.json 补令牌后重跑 ui.style.tokens"));
                        continue;
                    }

                    Graphic target = styleRef.GetComponent<Graphic>();
                    if (target == null)
                    {
                        issues.Add(Issue(UISeverity.Error, assetPath, node,
                            "UIStyleRef 节点没有 Graphic（无应用目标）", "把 UIStyleRef 挂到带 Image/TMP 的节点"));
                        continue;
                    }

                    if (!UIStyleResolver.Hits(target.color, value))
                        issues.Add(Issue(UISeverity.Warning, assetPath, node,
                            "声明值与令牌现值漂移：" + target.color + " ≠ " + value + "（" + token + "）",
                            "重跑 ui.style.colors-refresh（refs 段按声明收敛）"));
                }

                foreach (IThemeBound bound in root.GetComponentsInChildren<IThemeBound>(true))
                {
                    var component = bound as Component;
                    if (component == null) continue;
                    foreach (var field in UIStyleRefApplier.WidgetFields(component))
                    {
                        string token = field.Token();
                        if (string.IsNullOrEmpty(token)) continue;         // 空＝未令牌化合法（B4 存量口径）

                        if (!catalog.TryColor(token, out Color value))
                        {
                            issues.Add(Issue(UISeverity.Error, assetPath, component.transform.name,
                                "widget 状态色未知令牌：" + field.Field + " = " + token,
                                "改令牌 ID 或补令牌后重跑 ui.style.tokens"));
                            continue;
                        }

                        if (!UIStyleResolver.Hits(field.Color(), value))
                            issues.Add(Issue(UISeverity.Warning, assetPath, component.transform.name,
                                "widget 状态色漂移：" + field.Field + " 序列化值 ≠ 令牌现值（" + token + "）",
                                "重跑 ui.style.colors-refresh（迁移段按最近匹配填 token；运行期装配以令牌现值为准）"));
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static UIIssue Issue(UISeverity severity, string asset, string node, string message, string hint)
            => new UIIssue
            {
                RuleId = UIRuleIds.StyleRef,
                Severity = severity,
                Asset = asset,
                Node = node,
                Message = message,
                FixHint = hint,
            };
    }
}
