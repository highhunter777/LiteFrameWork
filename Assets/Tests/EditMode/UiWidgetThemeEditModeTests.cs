using LiteClient.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 三 widget 状态色令牌化（P2-3 B2 / AC6-②，D3 用户裁决）：StateButton/TabGroup/StarRating 实现
    /// IThemeBound；注入后按 token 取令牌现值，未注入/未令牌化/未知令牌走序列化 fallback（行为不漂）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiWidgetThemeEditModeTests
    {
        private static UiStyleResolver RealResolver()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UiThemeAsset>(
                LiteGame.EditorTools.UI.UiThemePaths.DefaultAssetPath);
            return UiStyleResolver.FromAsset(asset);
        }

        [Test]
        public void 三widget实现IThemeBound()
        {
            Assert.IsTrue(typeof(IThemeBound).IsAssignableFrom(typeof(StateButton)), "StateButton");
            Assert.IsTrue(typeof(IThemeBound).IsAssignableFrom(typeof(TabGroup)), "TabGroup");
            Assert.IsTrue(typeof(IThemeBound).IsAssignableFrom(typeof(StarRating)), "StarRating");
        }

        [Test]
        public void StateButton_注入后常态取令牌值()
        {
            var go = new GameObject("btn", typeof(Image));
            try
            {
                var image = go.GetComponent<Image>();
                var button = go.AddComponent<StateButton>();
                button.NormalColor = Color.magenta;                    // 序列化 fallback 刻意错开
                button.NormalColorToken = "color.state.interact.normal";
                UiStyleResolver resolver = RealResolver();

                button.OnThemeBound(resolver);
                resolver.TryColor("color.state.interact.normal", out Color expected);
                Assert.AreEqual(expected, image.color, "注入后常态＝令牌现值");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void StateButton_选中态取选中令牌值()
        {
            var go = new GameObject("btn", typeof(Image));
            try
            {
                var image = go.GetComponent<Image>();
                var button = go.AddComponent<StateButton>();
                button.SelectedColor = Color.magenta;
                button.SelectedColorToken = "color.state.interact.selected";
                button.Interactable = true;

                button.OnThemeBound(RealResolver());
                button.Selected = true;

                RealResolver().TryColor("color.state.interact.selected", out Color expected);
                Assert.AreEqual(expected, image.color, "选中态＝令牌现值");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void StateButton_未注入保持序列化fallback()
        {
            var go = new GameObject("btn", typeof(Image));
            try
            {
                var image = go.GetComponent<Image>();
                var button = go.AddComponent<StateButton>();
                button.NormalColor = Color.cyan;
                button.SelectedColor = Color.magenta;
                button.NormalColorToken = "color.state.interact.normal";   // 有声明但未注入

                button.Selected = true;                    // 主动触发刷新（EditMode 不跑 Awake）
                Assert.AreEqual(Color.magenta, image.color, "未注入（编辑器域/测试）＝fallback，行为不漂");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void StateButton_未知令牌走fallback()
        {
            var go = new GameObject("btn", typeof(Image));
            try
            {
                var image = go.GetComponent<Image>();
                var button = go.AddComponent<StateButton>();
                button.NormalColor = Color.cyan;
                button.NormalColorToken = "color.nope";

                button.OnThemeBound(RealResolver());

                Assert.AreEqual(Color.cyan, image.color, "未知令牌不猜色＝fallback");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TabGroup_选中与常态取令牌值()
        {
            var root = new GameObject("tabs");
            try
            {
                var tab0 = new GameObject("Tab0", typeof(Image), typeof(Button));
                tab0.transform.SetParent(root.transform, false);
                var tab1 = new GameObject("Tab1", typeof(Image), typeof(Button));
                tab1.transform.SetParent(root.transform, false);
                var group = root.AddComponent<TabGroup>();
                group.Tabs.Add(tab0.GetComponent<Button>());
                group.Tabs.Add(tab1.GetComponent<Button>());
                group.SelectedColor = Color.magenta;
                group.NormalColor = Color.green;
                group.SelectedColorToken = "color.state.interact.selected";
                group.NormalColorToken = "color.state.nav.normal";
                UiStyleResolver resolver = RealResolver();

                group.OnThemeBound(resolver);
                group.Select(0);

                resolver.TryColor("color.state.interact.selected", out Color selected);
                resolver.TryColor("color.state.nav.normal", out Color normal);
                Assert.AreEqual(selected, tab0.GetComponent<Image>().color, "选中页签＝选中令牌");
                Assert.AreEqual(normal, tab1.GetComponent<Image>().color, "未选中页签＝常态令牌");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void StarRating_注入后点亮熄灭取令牌值()
        {
            var root = new GameObject("stars");
            try
            {
                var star0 = new GameObject("S0", typeof(Image));
                star0.transform.SetParent(root.transform, false);
                var star1 = new GameObject("S1", typeof(Image));
                star1.transform.SetParent(root.transform, false);
                var rating = root.AddComponent<StarRating>();
                rating.Stars = new[] { star0.GetComponent<Image>(), star1.GetComponent<Image>() };
                rating.OnColor = Color.magenta;
                rating.OffColor = Color.green;
                rating.OnColorToken = "color.state.star.on";
                rating.OffColorToken = "color.state.star.off";
                UiStyleResolver resolver = RealResolver();

                rating.OnThemeBound(resolver);
                rating.Set(1);

                resolver.TryColor("color.state.star.on", out Color on);
                resolver.TryColor("color.state.star.off", out Color off);
                Assert.AreEqual(on, star0.GetComponent<Image>().color, "点亮星＝on 令牌");
                Assert.AreEqual(off, star1.GetComponent<Image>().color, "熄灭星＝off 令牌");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
