using System.Linq;
using LiteFramework.UI;
using LiteFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// `UIThemeBinder`（P2-3 B4 / AC3/AC6）：refs 应用、IThemeBound 注入、IThemeGraphicWriter 认领目标跳过、
    /// UISelectableTheme 节点上的基色 ref 可应用、未知令牌告警一次；真 HUD prefab 注入可观察。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UIThemeBinderEditModeTests
    {
        private static UIStyleResolver Resolver(params (string Id, Color Value)[] colors)
        {
            var asset = ScriptableObject.CreateInstance<UIThemeAsset>();
            asset.Colors = colors.Select(c => new UIThemeColorEntry { Id = c.Id, Value = c.Value }).ToArray();
            UIStyleResolver resolver = UIStyleResolver.FromAsset(asset);
            Object.DestroyImmediate(asset);
            return resolver;
        }

        private static GameObject ImageNode(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.GetComponent<Image>().color = color;
            return go;
        }

        [Test]
        public void Apply_refs按声明写令牌现值()
        {
            var root = new GameObject("Root");
            try
            {
                var node = ImageNode("Icon", Color.magenta);
                node.transform.SetParent(root.transform, false);
                node.AddComponent<UIStyleRef>().ColorToken = "color.brand.primary";
                UIThemeBinder.Apply(root, Resolver(("color.brand.primary", Color.cyan)), "test");
                Assert.AreEqual(Color.cyan, node.GetComponent<Image>().color);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_IThemeBound注入_StateButton取令牌()
        {
            var root = new GameObject("Root");
            try
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                var button = node.AddComponent<StateButton>();
                button.NormalColor = Color.magenta;
                button.NormalColorToken = "color.state.interact.normal";
                UIThemeBinder.Apply(root, Resolver(("color.state.interact.normal", Color.cyan)), "test");
                Assert.AreEqual(Color.cyan, node.GetComponent<Image>().color);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_writer认领的tab目标跳过ref()
        {
            var root = new GameObject("Root");
            try
            {
                var tabs = root.AddComponent<TabGroup>();
                var tab = new GameObject("Tab0", typeof(RectTransform), typeof(Image), typeof(Button));
                tab.transform.SetParent(root.transform, false);
                tabs.Tabs.Add(tab.GetComponent<Button>());
                tab.GetComponent<Image>().color = Color.magenta;
                tab.AddComponent<UIStyleRef>().ColorToken = "color.text.bright";

                UIThemeBinder.Apply(root, Resolver(("color.text.bright", Color.cyan)), "test");

                Assert.AreEqual(Color.magenta, tab.GetComponent<Image>().color,
                    "TabGroup 认领 Tabs 的 Image——ref 不得越权");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_writer不拥有的子标签ref仍应用()
        {
            var root = new GameObject("Root");
            try
            {
                var buttonGo = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                buttonGo.transform.SetParent(root.transform, false);
                buttonGo.AddComponent<StateButton>();                 // writer 只拥有自身 Image
                var label = ImageNode("Label", Color.magenta);
                label.transform.SetParent(buttonGo.transform, false);
                label.AddComponent<UIStyleRef>().ColorToken = "color.text.bright";

                UIThemeBinder.Apply(root, Resolver(("color.text.bright", Color.cyan)), "test");

                Assert.AreEqual(Color.cyan, label.GetComponent<Image>().color,
                    "writer 只认领自身/列表 Graphic，不认领整棵子树");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_SelectableTheme节点上的基色ref仍可应用()
        {
            var root = new GameObject("Root");
            try
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
                node.transform.SetParent(root.transform, false);
                node.AddComponent<UISelectableTheme>();
                node.GetComponent<Image>().color = Color.magenta;
                node.AddComponent<UIStyleRef>().ColorToken = "color.bg.screen";

                UIThemeBinder.Apply(root, Resolver(
                    ("color.bg.screen", Color.cyan),
                    (UISelectableTheme.NormalToken, Color.white)), "test");

                Assert.AreEqual(Color.cyan, node.GetComponent<Image>().color,
                    "UISelectableTheme 只写 tint，不是 Graphic writer——基色 ref 可应用");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_未知令牌只告警一次()
        {
            var root = new GameObject("Root");
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var node = ImageNode("Icon" + i, Color.magenta);
                    node.transform.SetParent(root.transform, false);
                    node.AddComponent<UIStyleRef>().ColorToken = "color.nope";
                }
                Log.ResetForTesting();
                UIThemeBinder.Apply(root, Resolver(("color.bg.screen", Color.cyan)), "test-label");
                int warnings = Log.Recent.Count(e => e.Level == LogLevel.Warning
                    && (e.Message ?? string.Empty).Contains("color.nope"));
                Assert.AreEqual(1, warnings, "同一未知令牌按 label 只告警一次");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_真HUD_ButtonSettings变色_Slider不抛()
        {
            const string hudPath = "Assets/LiteGame/Modules/Battle/Content/Hud/Hud_Battle.prefab";
            GameObject hud = PrefabUtility.LoadPrefabContents(hudPath);
            try
            {
                Transform button = hud.transform.Find("Cot_Settings/Button_Settings");
                Assert.IsNotNull(button, "HUD 设置按钮应在");
                var theme = button.GetComponent<UISelectableTheme>();
                Assert.IsNotNull(theme, "HUD 设置按钮应挂 UISelectableTheme（迁移后）");

                Color normal = new Color(0.12f, 0.34f, 0.56f, 1f);
                UIThemeBinder.Apply(hud, Resolver(
                    (UISelectableTheme.NormalToken, normal),
                    (UISelectableTheme.HighlightedToken, Color.white),
                    (UISelectableTheme.PressedToken, Color.gray),
                    (UISelectableTheme.SelectedToken, Color.white),
                    (UISelectableTheme.DisabledToken, new Color(0.5f, 0.5f, 0.5f, 0.5f))), "HUD");

                Assert.IsTrue(UIStyleResolver.Hits(button.GetComponent<Selectable>().colors.normalColor, normal),
                    "真 HUD 按钮状态色被注入（8bit 量化内）");
                Transform slider = hud.transform.Find("Cot_Panel/Cot_Vitals/HpBar_Player/_Bar/_Slider");
                Assert.IsNotNull(slider, "HUD 血条 Slider 应在");
                Assert.DoesNotThrow(() => UIThemeBinder.Apply(slider.gameObject,
                    Resolver((UISelectableTheme.NormalToken, normal)), "HUD-Slider"),
                    "targetGraphic=null 的 Slider 不得抛");
            }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
        }
    }
}