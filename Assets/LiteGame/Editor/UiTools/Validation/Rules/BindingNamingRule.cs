using System.Collections.Generic;
using System.Text.RegularExpressions;
using LiteGame.Editor.UiTools.Naming;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 规则：绑定命名（《UI制作规范》§9；《UI编辑器工具专项设计》§2.1.3 硬约束子集）。
    ///
    /// **资产面规则**：对 <see cref="UiValidationContext.AssetPaths"/> 里每个 prefab 的全部节点判定
    /// （递归，含未激活）。判据（§9.2/§9.4）：
    /// 1. `_` 私有件——模板内允许；**页面根（Screens/）节点禁 `_`**（§9.4"页面业务节点不得以 `_` 开头"）；
    /// 2. 其余节点名须过文法 <c>^[A-Za-z]+_[A-Za-z][A-Za-z0-9]*$</c>（§9.2）；
    /// 3. 前缀段须属词表（角色词/控件段/模板段，§9.3）——未登记段 = Warning（§1 允许登记扩展）。
    ///
    /// **豁免**：UGUI 复合控件"拥有"的子件（Slider 的 fillRect/handleRect、ScrollRect 的 viewport/content）
    /// ——它们是 UGUI 组件契约的一部分，改名会断内部引用，**不属项目命名面**（按**身份**豁免，非按名）。
    ///
    /// 本步**不**做同页同词一致性 / 编号连续性（需词频上下文）。
    /// </summary>
    public sealed class BindingNamingRule : IUiRule
    {
        public string RuleId => "ui.binding-naming";
        public string Family => "命名/绑定";

        private static readonly Regex Grammar = new Regex(UiNamingWords.GrammarPattern);

        private const string FixGrammar = "按 §9.2 命名 <前缀>_<语义>（前缀段取词表、语义 PascalCase、恰一个下划线、ASCII）";
        private const string FixPrefix = "前缀段取自 §9.3 词表（角色词/控件段/模板段）；新类别先按 §1 例外表登记";
        private const string FixPagePrivate = "页面业务节点不得以 '_' 开头；要暴露则用词表前缀/类型段（§9.4）";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            if (context == null || context.AssetPaths == null) return issues;

            foreach (string path in context.AssetPaths)
            {
                var root = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                // 页面判据按**根名**（Page_/Hud_ = 页面；Tmp_ = 模板/夹具）——不按目录
                // （Screens/ 下亦有 Tmp_ 框架夹具，其内部件 `_` 私有合法）
                bool isPage = root.name.StartsWith("Page_") || root.name.StartsWith("Hud_");
                var owned = CollectUguiOwned(root.transform);

                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (owned.Contains(t)) continue;              // UGUI 契约件豁免
                    // 模板实例内部件归模板（其 `_` 私有件在模板内合法）——本 prefab 只对**自有节点**负责
                    if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(t)
                        && !UnityEditor.PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                    string name = t.name;

                    // 根节点走"根名"判据（§9.3：Page_<语义> / Hud_<...> / Tmp_<...>）——
                    // 非常规根名（历史样例页已退役）仍按 §9.6 报 Warning 不报 Error，避免存量资产一刀切
                    if (t == root.transform)
                    {
                        if (name.StartsWith("Page_") || name.StartsWith("Hud_") || name.StartsWith("Tmp_")) continue;
                        issues.Add(Make(path, name, UiSeverity.Warning,
                            $"根名未按 §9.3（{name}）", "页面根 → Page_<语义>／模板根 → Tmp_<控件类型>（§9.6 存量页面根随页面批次整改）"));
                        continue;
                    }

                    if (name.StartsWith("_"))
                    {
                        if (isPage)
                            issues.Add(Make(path, NodePath(t), UiSeverity.Error,
                                $"页面节点以 '_' 开头（{name}）——页面业务节点禁 '_'", FixPagePrivate));
                        continue;                                  // 模板内 _ 私有件：允许
                    }

                    if (!Grammar.IsMatch(name))
                    {
                        issues.Add(Make(path, NodePath(t), UiSeverity.Error,
                            $"节点名不合 §9.2 文法（{name}）", FixGrammar));
                        continue;
                    }

                    if (UiNamingWords.TryParse(name, out string prefix, out _) && !UiNamingWords.IsKnownPrefix(prefix))
                    {
                        issues.Add(Make(path, NodePath(t), UiSeverity.Warning,
                            $"前缀段未登记词表（{prefix}）", FixPrefix));
                    }
                }
            }
            return issues;
        }

        /// <summary>UGUI 复合控件"拥有"的子件（按身份豁免——改名断内部引用）。</summary>
        private static HashSet<Transform> CollectUguiOwned(Transform root)
        {
            var owned = new HashSet<Transform>();
            foreach (var s in root.GetComponentsInChildren<Slider>(true))
            {
                if (s.fillRect != null) owned.Add(s.fillRect);
                if (s.handleRect != null) owned.Add(s.handleRect);
            }
            foreach (var sc in root.GetComponentsInChildren<ScrollRect>(true))
            {
                if (sc.viewport != null) owned.Add(sc.viewport);
                if (sc.content != null) owned.Add(sc.content);
            }
            foreach (var tg in root.GetComponentsInChildren<Toggle>(true))
            {
                if (tg.graphic != null) owned.Add(tg.graphic.transform);
                if (tg.targetGraphic != null) owned.Add(tg.targetGraphic.transform);
            }
            return owned;
        }

        private static UiIssue Make(string asset, string node, UiSeverity sev, string msg, string fix)
            => new UiIssue { RuleId = "ui.binding-naming", Severity = sev, Asset = asset, Node = node, Message = msg, FixHint = fix };

        private static string NodePath(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
