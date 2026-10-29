using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>
    /// uGUI Selectable 状态色令牌接缝（P2-3 B4 / D3）：5 个状态色令牌常量，装配期经 <see cref="IThemeBound"/>
    /// 注入后一次性写 <see cref="Selectable.colors"/>（保留 colorMultiplier/fadeDuration）；仅 ColorTint 生效。
    /// 未注入/未知令牌保持 prefab 现值；只写状态 tint，不写 Graphic 基色（可与 `UiStyleRef` 共存）。
    /// 不 RequireComponent：无 Selectable 由 `ui.style.selectable` 报 Error（可测的显式判据，避免自动补组件掩盖配置问题）。
    /// </summary>
    [AddComponentMenu("LiteGame/UI/UiSelectableTheme Selectable 状态色")]
    [DisallowMultipleComponent]
    public sealed class UiSelectableTheme : MonoBehaviour, IThemeBound
    {
        /// <summary>Selectable 状态令牌前缀（单源；widget 迁移过滤用）。</summary>
        public const string TokenPrefix = "color.state.selectable.";

        public const string NormalToken = TokenPrefix + "normal";
        public const string HighlightedToken = TokenPrefix + "highlighted";
        public const string PressedToken = TokenPrefix + "pressed";
        public const string SelectedToken = TokenPrefix + "selected";
        public const string DisabledToken = TokenPrefix + "disabled";

        /// <summary>主题注入（IThemeBound）：持解析器并刷新状态色；未注入保持现值。</summary>
        public void OnThemeBound(UiStyleResolver resolver) => Apply(resolver);

        /// <summary>按令牌刷新 Selectable.colors；5 色逐项解析，未知令牌保持该字段现值。</summary>
        public void Apply(UiStyleResolver resolver)
        {
            if (resolver == null) return;
            Selectable selectable = GetComponent<Selectable>();
            if (selectable == null || selectable.transition != Selectable.Transition.ColorTint) return;

            ColorBlock block = selectable.colors;
            bool changed = false;
            changed |= TryApply(resolver, NormalToken, block.normalColor, value => block.normalColor = value);
            changed |= TryApply(resolver, HighlightedToken, block.highlightedColor, value => block.highlightedColor = value);
            changed |= TryApply(resolver, PressedToken, block.pressedColor, value => block.pressedColor = value);
            changed |= TryApply(resolver, SelectedToken, block.selectedColor, value => block.selectedColor = value);
            changed |= TryApply(resolver, DisabledToken, block.disabledColor, value => block.disabledColor = value);
            if (changed) selectable.colors = block;                 // 一次写回；multiplier/fade 原样
        }

        private bool TryApply(UiStyleResolver resolver, string token, Color current, System.Action<Color> write)
        {
            if (!resolver.TryColor(token, out Color value))
            {
                WarnOnce(token);
                return false;
            }
            if (UiStyleResolver.Hits(current, value)) return false;  // 现值已在令牌阈值内——不改写
            write(value);
            return true;
        }

        private void WarnOnce(string token)
        {
            if (_warned.Add(token))
                Log.Warning($"未知 Selectable 状态令牌 {token}——{name} 保持现值（校验器应报 Error）", LogTag.UI);
        }

        private readonly System.Collections.Generic.HashSet<string> _warned =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
    }
}