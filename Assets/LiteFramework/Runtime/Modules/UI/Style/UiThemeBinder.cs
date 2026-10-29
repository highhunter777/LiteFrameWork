using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// 主题应用核心（P2-3 B4）：页面/HUD/未来克隆体的唯一应用入口。
    /// IThemeBound 注入 → UiStyleRef 应用（目标 Graphic 被自身/祖先 writer 认领则跳过）→ 未知令牌告警一次。
    /// </summary>
    public static class UiThemeBinder
    {
        public static void Apply(GameObject root, UiStyleResolver resolver, string label)
        {
            if (root == null || resolver == null) return;

            foreach (IThemeBound bound in root.GetComponentsInChildren<IThemeBound>(true))
                SafeCall.Invoke(() => bound.OnThemeBound(resolver), $"{label}.OnThemeBound");

            var warned = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (UiStyleRef styleRef in root.GetComponentsInChildren<UiStyleRef>(true))
            {
                if (IsGraphicOwnedByWidget(styleRef)) continue;
                Graphic target = styleRef.GetComponent<Graphic>();
                if (resolver.Apply(styleRef, target) != UiStyleApplyResult.UnknownToken) continue;
                if (warned.Add(styleRef.ColorToken ?? string.Empty))
                    Log.Warning($"未知颜色令牌 {styleRef.ColorToken}——{label}.{styleRef.name} 保持现值（校验器应报 Error）", LogTag.UI);
            }
        }

        /// <summary>目标 Graphic 是否被自身/祖先的 IThemeGraphicWriter 认领（单源判据；UIForm/applier/rule 共用）。
        /// 写权是"具体 Graphic"而非整棵子树——StateButton 只认领自身 Image、TabGroup/StarRating 只认领 Tabs/Stars。</summary>
        public static bool IsGraphicOwnedByWidget(Component component)
        {
            if (component == null) return false;
            Graphic target = component.GetComponent<Graphic>();
            if (target == null) return false;
            for (Transform node = component.transform; node != null; node = node.parent)
                foreach (IThemeGraphicWriter writer in node.GetComponents<IThemeGraphicWriter>())
                    if (writer != null && writer.Owns(target)) return true;
            return false;
        }
    }
}