using System;
using System.Collections.Generic;
using LiteClient.UI;
using LiteGame.EditorTools.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 样式族：颜色令牌引用对账（《UI 系统总设计》§6.2；工具链设计 §6 校验核心）。五判据：
    /// ① UiStyleRef 声明的 ID 不在令牌集＝Error（「令牌不存在」）；② 声明值与令牌现值漂移＝Warning
    /// （阈值单源 <see cref="UiStyleResolver.HitEpsilon"/>）；③ 节点无 Graphic＝Error；
    /// ④ widget 状态 token 三态（未知＝Error/漂移＝Warning/空＝合法——B4 存量口径）；
    /// ⑤ 同 GameObject 上 UiStyleRef＋IThemeBound 并存＝Error（双写者——widget 是自身 Graphic 的唯一写者）。
    /// </summary>
    public sealed class StyleRefRule : IUiRule
    {
        public string RuleId => UiRuleIds.StyleRef;
        public string Family => "样式";

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
                    issues.Add(new UiIssue
                    {
                        RuleId = RuleId,
                        Severity = UiSeverity.Error,
                        Asset = asset,
                        Node = "-",
                        Message = "主题资产不可读，引用对账无法执行：" + ex.Message,
                        FixHint = "先跑 ui.style.tokens 生成 UiTheme.asset（或修正 UiValidationContext.ThemeAssetPath）",
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

        private static void CheckPrefab(string assetPath, UiTokenCatalog catalog, List<UiIssue> issues)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                foreach (UiStyleRef styleRef in root.GetComponentsInChildren<UiStyleRef>(true))
                {
                    string node = styleRef.transform.name;
                    if (UiThemeBinder.IsGraphicOwnedByWidget(styleRef))
                    {
                        issues.Add(Issue(UiSeverity.Error, assetPath, node,
                            "UiStyleRef 的目标 Graphic 被自身/祖先 IThemeGraphicWriter 认领（双写者）——摘除其一",
                            "摘除 UiStyleRef（状态色由 widget token 字段声明）或摘除 writer"));
                        continue;
                    }

                    string token = styleRef.ColorToken;
                    if (string.IsNullOrEmpty(token)) continue;             // 空＝未声明合法

                    if (!catalog.TryColor(token, out Color value))
                    {
                        issues.Add(Issue(UiSeverity.Error, assetPath, node,
                            "未知颜色令牌：" + token + "（不在令牌集——不猜默认色）",
                            "改令牌 ID 或在 UiStyleTokens.json 补令牌后重跑 ui.style.tokens"));
                        continue;
                    }

                    Graphic target = styleRef.GetComponent<Graphic>();
                    if (target == null)
                    {
                        issues.Add(Issue(UiSeverity.Error, assetPath, node,
                            "UiStyleRef 节点没有 Graphic（无应用目标）", "把 UiStyleRef 挂到带 Image/TMP 的节点"));
                        continue;
                    }

                    if (!UiStyleResolver.Hits(target.color, value))
                        issues.Add(Issue(UiSeverity.Warning, assetPath, node,
                            "声明值与令牌现值漂移：" + target.color + " ≠ " + value + "（" + token + "）",
                            "重跑 ui.style.colors-refresh（refs 段按声明收敛）"));
                }

                foreach (IThemeBound bound in root.GetComponentsInChildren<IThemeBound>(true))
                {
                    var component = bound as Component;
                    if (component == null) continue;
                    foreach (var field in UiStyleRefApplier.WidgetFields(component))
                    {
                        string token = field.Token();
                        if (string.IsNullOrEmpty(token)) continue;         // 空＝未令牌化合法（B4 存量口径）

                        if (!catalog.TryColor(token, out Color value))
                        {
                            issues.Add(Issue(UiSeverity.Error, assetPath, component.transform.name,
                                "widget 状态色未知令牌：" + field.Field + " = " + token,
                                "改令牌 ID 或补令牌后重跑 ui.style.tokens"));
                            continue;
                        }

                        if (!UiStyleResolver.Hits(field.Color(), value))
                            issues.Add(Issue(UiSeverity.Warning, assetPath, component.transform.name,
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

        private static UiIssue Issue(UiSeverity severity, string asset, string node, string message, string hint)
            => new UiIssue
            {
                RuleId = UiRuleIds.StyleRef,
                Severity = severity,
                Asset = asset,
                Node = node,
                Message = message,
                FixHint = hint,
            };
    }
}
