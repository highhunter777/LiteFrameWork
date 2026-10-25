using System.Linq;
using LiteClient.UI;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UiThemePaths = LiteGame.EditorTools.UI.UiThemePaths;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 令牌解析核心单源（P2-3 B2 / AC1）：`UiStyleResolver`（LiteClient.UI）从生成物 SO 读令牌；
    /// 编辑器 `UiTokenCatalog` 与其同源；命中阈值单源驻 resolver。
    /// 《UI 系统总设计》§6.1「运行时通过 UiStyleResolver 解析，不读 JSON」。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiStyleResolverEditModeTests
    {
        private static UiThemeAsset LoadAsset()
            => AssetDatabase.LoadAssetAtPath<UiThemeAsset>(UiThemePaths.DefaultAssetPath);

        private static Color32 Hex(string hex)
        {
            Assert.IsTrue(UiThemeColorHex.TryParse(hex, out Color32 value), hex + " 应是合法 #RRGGBBAA");
            return value;
        }

        [Test]
        public void FromAsset_真实资产_颜色二十枚含八枚状态色()
        {
            UiStyleResolver resolver = UiStyleResolver.FromAsset(LoadAsset());
            Assert.IsNotNull(resolver);
            Assert.AreEqual(20, resolver.ColorCount, "12 既有 + 8 状态色（B2 补集）");
        }

        [Test]
        public void FromAsset_真实资产_文字五枚()
        {
            Assert.AreEqual(5, UiStyleResolver.FromAsset(LoadAsset()).TypographyCount);
        }

        [Test]
        public void TryColor_已知令牌返回真实值_未知返回假()
        {
            UiStyleResolver resolver = UiStyleResolver.FromAsset(LoadAsset());
            Assert.IsTrue(resolver.TryColor("color.bg.screen", out Color value), "已知 ID 应命中");
            Assert.AreEqual((Color)Hex("#1F1F29F2"), value, "bg.screen 的冻结值");
            Assert.IsFalse(resolver.TryColor("color.nope", out _), "未知 ID 不猜默认色");
        }

        [Test]
        public void TryTypography_已知令牌返回条目_未知返回假()
        {
            UiStyleResolver resolver = UiStyleResolver.FromAsset(LoadAsset());
            Assert.IsTrue(resolver.TryTypography("font.title", out UiThemeTypographyEntry title), "font.title 应命中");
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
                var styleRef = go.AddComponent<UiStyleRef>();
                styleRef.ColorToken = "color.brand.primary";
                UiStyleResolver resolver = UiStyleResolver.FromAsset(LoadAsset());

                Assert.AreEqual(UiStyleApplyResult.Applied, resolver.Apply(styleRef, image));
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
                var styleRef = go.AddComponent<UiStyleRef>();
                UiStyleResolver resolver = UiStyleResolver.FromAsset(LoadAsset());

                Assert.AreEqual(UiStyleApplyResult.NoToken, resolver.Apply(styleRef, image), "空声明＝跳过");
                Assert.AreEqual(Color.magenta, image.color, "空声明不改值");

                styleRef.ColorToken = "color.nope";
                Assert.AreEqual(UiStyleApplyResult.UnknownToken, resolver.Apply(styleRef, image), "未知令牌结果");
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
                var styleRef = go.AddComponent<UiStyleRef>();
                styleRef.ColorToken = "color.bg.screen";
                Assert.AreEqual(UiStyleApplyResult.NoTarget,
                    UiStyleResolver.FromAsset(LoadAsset()).Apply(styleRef, null));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 与UiTokenCatalog同源一致()
        {
            UiThemeAsset asset = LoadAsset();
            UiStyleResolver resolver = UiStyleResolver.FromAsset(asset);
            UiTokenCatalog catalog = UiTokenCatalog.FromAsset(asset);
            foreach ((string _, string tokenId) in UiStyle.LegacyTokenIds)
            {
                Assert.IsTrue(catalog.TryColor(tokenId, out Color catalogValue), tokenId + " catalog 应命中");
                Assert.IsTrue(resolver.TryColor(tokenId, out Color resolverValue), tokenId + " resolver 应命中");
                Assert.AreEqual(catalogValue, resolverValue, tokenId + " 两读面同源");
            }
        }

        [Test]
        public void FromAsset_重复ID后写覆盖_空ID跳过()
        {
            var asset = ScriptableObject.CreateInstance<UiThemeAsset>();
            try
            {
                asset.Colors = new[]
                {
                    new UiThemeColorEntry { Id = "color.test.dup", Value = Hex("#000000FF") },
                    new UiThemeColorEntry { Id = "color.test.dup", Value = Hex("#FFFFFFFF") },
                    new UiThemeColorEntry { Id = "", Value = Hex("#123456FF") },
                    new UiThemeColorEntry { Id = "color.test.ok", Value = Hex("#00FF00FF") },
                };
                UiStyleResolver resolver = UiStyleResolver.FromAsset(asset);
                Assert.AreEqual(2, resolver.ColorCount, "重复合并为一、空 ID 跳过");
                Assert.IsTrue(resolver.TryColor("color.test.dup", out Color dup));
                Assert.AreEqual((Color)Hex("#FFFFFFFF"), dup, "重复 ID 后写覆盖（P2-3a 口径）");
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void HitEpsilon_单源阈值驻Resolver()
        {
            Assert.AreEqual(1e-4f, UiStyleResolver.HitEpsilon, "命中阈值＝1e-4（工具/规则/阈值表引用本常量）");
        }
    }
}
