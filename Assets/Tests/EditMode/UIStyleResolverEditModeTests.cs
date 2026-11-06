using System.Linq;
using LiteFramework.UI;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UIThemePaths = LiteGame.EditorTools.UI.UIThemePaths;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 令牌解析核心单源（P2-3 B2 / AC1）：`UIStyleResolver`（LiteFramework.UI）从生成物 SO 读令牌； 编辑器 `UITokenCatalog` 与其同源。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UIStyleResolverEditModeTests
    {
        private static UIThemeAsset LoadAsset()
            => AssetDatabase.LoadAssetAtPath<UIThemeAsset>(UIThemePaths.DefaultAssetPath);

        private static Color32 Hex(string hex)
        {
            Assert.IsTrue(UIThemeColorHex.TryParse(hex, out Color32 value), hex + " 应是合法 #RRGGBBAA");
            return value;
        }

        [Test]
        public void FromAsset_真实资产_颜色六十九枚含十三枚状态色()
        {
            UIStyleResolver resolver = UIStyleResolver.FromAsset(LoadAsset());
            Assert.IsNotNull(resolver);
            Assert.AreEqual(69, resolver.ColorCount, "12 既有 + 8 B2 状态 + 44 精确 + 5 Selectable（B4 补集）");
        }

        [Test]
        public void FromAsset_真实资产_文字五枚()
        {
            Assert.AreEqual(5, UIStyleResolver.FromAsset(LoadAsset()).TypographyCount);
        }

        [Test]
        public void TryColor_已知令牌返回真实值_未知返回假()
        {
            UIStyleResolver resolver = UIStyleResolver.FromAsset(LoadAsset());
            Assert.IsTrue(resolver.TryColor("color.bg.screen", out Color value), "已知 ID 应命中");
            Assert.AreEqual((Color)Hex("#1F1F29F2"), value, "bg.screen 的冻结值");
            Assert.IsFalse(resolver.TryColor("color.nope", out _), "未知 ID 不猜默认色");
        }

        [Test]
        public void TryTypography_已知令牌返回条目_未知返回假()
        {
            UIStyleResolver resolver = UIStyleResolver.FromAsset(LoadAsset());
            Assert.IsTrue(resolver.TryTypography("font.title", out UIThemeTypographyEntry title), "font.title 应命中");
            Assert.AreEqual(24f, title.FontSize, 1e-3f, "标题档字号");
            Assert.IsFalse(resolver.TryTypography("font.nope", out _), "未知 ID 返回假");
        }

        [Test]
        public void Apply_按声明写值并返回Applied()
        {
            var go = new GameObject("ref", typeof(Image));
            try
            {
                var image = go.GetComponent<Image>();
                var styleRef = go.AddComponent<UIStyleRef>();
                styleRef.ColorToken = "color.brand.primary";
                UIStyleResolver resolver = UIStyleResolver.FromAsset(LoadAsset());

                Assert.AreEqual(UIStyleApplyResult.Applied, resolver.Apply(styleRef, image));
                Assert.AreEqual((Color)Hex("#E6BF40FF"), image.color, "应写入令牌现值");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Apply_空声明跳过_未知令牌不猜色()
        {
            var go = new GameObject("ref", typeof(Image));
            try
            {
                var image = go.GetComponent<Image>();
                image.color = Color.magenta;
                var styleRef = go.AddComponent<UIStyleRef>();
                UIStyleResolver resolver = UIStyleResolver.FromAsset(LoadAsset());

                Assert.AreEqual(UIStyleApplyResult.NoToken, resolver.Apply(styleRef, image), "空声明＝跳过");
                Assert.AreEqual(Color.magenta, image.color, "空声明不改值");

                styleRef.ColorToken = "color.nope";
                Assert.AreEqual(UIStyleApplyResult.UnknownToken, resolver.Apply(styleRef, image), "未知令牌结果");
                Assert.AreEqual(Color.magenta, image.color, "未知令牌不猜默认色");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Apply_无Graphic返回NoTarget()
        {
            var go = new GameObject("ref");
            try
            {
                var styleRef = go.AddComponent<UIStyleRef>();
                styleRef.ColorToken = "color.bg.screen";
                Assert.AreEqual(UIStyleApplyResult.NoTarget,
                    UIStyleResolver.FromAsset(LoadAsset()).Apply(styleRef, null));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 与UiTokenCatalog同源一致()
        {
            UIThemeAsset asset = LoadAsset();
            UIStyleResolver resolver = UIStyleResolver.FromAsset(asset);
            UITokenCatalog catalog = UITokenCatalog.FromAsset(asset);
            foreach ((string _, string tokenId) in UIStyle.LegacyTokenIds)
            {
                Assert.IsTrue(catalog.TryColor(tokenId, out Color catalogValue), tokenId + " catalog 应命中");
                Assert.IsTrue(resolver.TryColor(tokenId, out Color resolverValue), tokenId + " resolver 应命中");
                Assert.AreEqual(catalogValue, resolverValue, tokenId + " 两读面同源");
            }
        }

        [Test]
        public void FromAsset_重复ID后写覆盖_空ID跳过()
        {
            var asset = ScriptableObject.CreateInstance<UIThemeAsset>();
            try
            {
                asset.Colors = new[]
                {
                    new UIThemeColorEntry { Id = "color.test.dup", Value = Hex("#000000FF") },
                    new UIThemeColorEntry { Id = "color.test.dup", Value = Hex("#FFFFFFFF") },
                    new UIThemeColorEntry { Id = "", Value = Hex("#123456FF") },
                    new UIThemeColorEntry { Id = "color.test.ok", Value = Hex("#00FF00FF") },
                };
                UIStyleResolver resolver = UIStyleResolver.FromAsset(asset);
                Assert.AreEqual(2, resolver.ColorCount, "重复合并为一、空 ID 跳过");
                Assert.IsTrue(resolver.TryColor("color.test.dup", out Color dup));
                Assert.AreEqual((Color)Hex("#FFFFFFFF"), dup, "重复 ID 后写覆盖（P2-3a 口径）");
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void HitEpsilon_单源阈值驻Resolver()
        {
            Assert.AreEqual(1e-4f, UIStyleResolver.HitEpsilon, "命中阈值＝1e-4（工具/规则/阈值表引用本常量）");
        }

        [Test]
        public void Resolve_已知取令牌_空与未知与空解析器回落()
        {
            UIStyleResolver resolver = UIStyleResolver.FromAsset(LoadAsset());
            Assert.IsTrue(resolver.TryColor("color.brand.primary", out Color primary));
            Assert.AreEqual(primary, resolver.Resolve("color.brand.primary", Color.magenta), "已知令牌取现值");
            Assert.AreEqual(Color.magenta, resolver.Resolve(null, Color.magenta), "空 token＝fallback");
            Assert.AreEqual(Color.magenta, resolver.Resolve(string.Empty, Color.magenta), "空串 token＝fallback");
            Assert.AreEqual(Color.magenta, resolver.Resolve("color.nope", Color.magenta), "未知 token＝fallback");
        }
    }
}
