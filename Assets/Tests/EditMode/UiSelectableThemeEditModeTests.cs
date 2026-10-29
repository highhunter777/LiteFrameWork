using LiteFramework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// `UiSelectableTheme`（P2-3 B4 / AC3）：5 枚 Selectable 状态色按令牌写入、保留 multiplier/fade、
    /// 仅 ColorTint 生效、未知令牌保持现值、null resolver 不抛。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiSelectableThemeEditModeTests
    {
        private static UiStyleResolver Resolver(params (string Id, Color Value)[] colors)
        {
            var asset = ScriptableObject.CreateInstance<UiThemeAsset>();
            var entries = new UiThemeColorEntry[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                entries[i] = new UiThemeColorEntry { Id = colors[i].Id, Value = colors[i].Value };
            asset.Colors = entries;
            UiStyleResolver resolver = UiStyleResolver.FromAsset(asset);
            Object.DestroyImmediate(asset);
            return resolver;
        }

        private static (GameObject go, Button button, UiSelectableTheme theme) Make(ColorBlock block, Selectable.Transition transition)
        {
            var go = new GameObject("selectable", typeof(RectTransform), typeof(Image), typeof(Button));
            var button = go.GetComponent<Button>();
            button.transition = transition;
            button.colors = block;
            var theme = go.AddComponent<UiSelectableTheme>();
            return (go, button, theme);
        }

        [Test]
        public void 五色写入_保留multiplier与fade()
        {
            var block = new ColorBlock
            {
                normalColor = Color.black, highlightedColor = Color.black, pressedColor = Color.black,
                selectedColor = Color.black, disabledColor = Color.black,
                colorMultiplier = 2f, fadeDuration = 0.3f,
            };
            var made = Make(block, Selectable.Transition.ColorTint);
            try
            {
                Color n = new Color(0.1f, 0.2f, 0.3f, 1f), h = new Color(0.2f, 0.3f, 0.4f, 1f);
                Color p = new Color(0.3f, 0.4f, 0.5f, 1f), s = new Color(0.4f, 0.5f, 0.6f, 1f);
                Color d = new Color(0.5f, 0.6f, 0.7f, 0.5f);
                UiStyleResolver resolver = Resolver(
                    (UiSelectableTheme.NormalToken, n), (UiSelectableTheme.HighlightedToken, h),
                    (UiSelectableTheme.PressedToken, p), (UiSelectableTheme.SelectedToken, s),
                    (UiSelectableTheme.DisabledToken, d));

                made.theme.Apply(resolver);

                ColorBlock actual = made.button.colors;
                Assert.IsTrue(UiStyleResolver.Hits(actual.normalColor, n), "normal");
                Assert.IsTrue(UiStyleResolver.Hits(actual.highlightedColor, h), "highlighted");
                Assert.IsTrue(UiStyleResolver.Hits(actual.pressedColor, p), "pressed");
                Assert.IsTrue(UiStyleResolver.Hits(actual.selectedColor, s), "selected");
                Assert.IsTrue(UiStyleResolver.Hits(actual.disabledColor, d), "disabled");
                Assert.AreEqual(2f, actual.colorMultiplier, "multiplier 保留");
                Assert.AreEqual(0.3f, actual.fadeDuration, "fade 保留");
            }
            finally { Object.DestroyImmediate(made.go); }
        }

        [Test]
        public void 未知令牌_保留该字段现值()
        {
            var block = new ColorBlock
            {
                normalColor = Color.magenta, highlightedColor = Color.magenta, pressedColor = Color.magenta,
                selectedColor = Color.magenta, disabledColor = Color.magenta,
                colorMultiplier = 1f, fadeDuration = 0.1f,
            };
            var made = Make(block, Selectable.Transition.ColorTint);
            try
            {
                Color n = new Color(0.1f, 0.2f, 0.3f, 1f);
                made.theme.Apply(Resolver((UiSelectableTheme.NormalToken, n)));
                ColorBlock actual = made.button.colors;
                Assert.IsTrue(UiStyleResolver.Hits(actual.normalColor, n), "已知令牌写入");
                Assert.AreEqual(Color.magenta, actual.highlightedColor, "未知令牌保持现值");
                Assert.AreEqual(Color.magenta, actual.disabledColor, "未知令牌保持现值");
            }
            finally { Object.DestroyImmediate(made.go); }
        }

        [Test]
        public void 非ColorTint_不写()
        {
            var block = new ColorBlock
            {
                normalColor = Color.magenta, highlightedColor = Color.magenta, pressedColor = Color.magenta,
                selectedColor = Color.magenta, disabledColor = Color.magenta,
                colorMultiplier = 1f, fadeDuration = 0.1f,
            };
            var made = Make(block, Selectable.Transition.None);
            try
            {
                made.theme.Apply(Resolver((UiSelectableTheme.NormalToken, Color.cyan)));
                Assert.AreEqual(Color.magenta, made.button.colors.normalColor, "非 ColorTint 不适用");
            }
            finally { Object.DestroyImmediate(made.go); }
        }

        [Test]
        public void Null解析器_不抛且保持现值()
        {
            var block = new ColorBlock
            {
                normalColor = Color.magenta, highlightedColor = Color.magenta, pressedColor = Color.magenta,
                selectedColor = Color.magenta, disabledColor = Color.magenta,
                colorMultiplier = 1f, fadeDuration = 0.1f,
            };
            var made = Make(block, Selectable.Transition.ColorTint);
            try
            {
                Assert.DoesNotThrow(() => made.theme.Apply(null));
                Assert.AreEqual(Color.magenta, made.button.colors.normalColor);
            }
            finally { Object.DestroyImmediate(made.go); }
        }
    }
}