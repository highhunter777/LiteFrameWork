using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteFramework.UI;
using LiteGame.Editor.UITools.Validation;
using LiteGame.Editor.UITools.Validation.Rules;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UIThemePaths = LiteGame.EditorTools.UI.UIThemePaths;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 样式值表退役（P2-3a）：颜色/文字值只从 `UIThemeAsset` 读，生产读口不再持有字面量； 对账判定与阈值保持不变；`ui.style.token-drift` 对"JSON 改了没重生成"报 Error。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UIStyleRetirementEditModeTests
    {
        private const string TempDir = "Assets/TempUiStyleRetirementTests";
        private const string JsonPath = TempDir + "/tokens.json";
        private const string AssetPath = TempDir + "/UITheme.asset";

        private const string Doc =
            "{\"schemaVersion\":\"2.0\"," +
            "\"meta\":{\"name\":\"UiTheme\",\"version\":\"1.0.0\",\"theme\":\"Default\"}," +
            "\"groups\":{" +
            "\"color\":{\"tokens\":{\"color.bg.screen\":{\"value\":\"#1F1F29F2\"}," +
            "\"color.bg.deep\":{\"value\":\"#14141AE6\"}," +
            "\"color.bg.item\":{\"value\":\"#2E2E38FF\"}," +
            "\"color.brand.primary\":{\"value\":\"#E6BF40FF\"}," +
            "\"color.brand.accent\":{\"value\":\"#4CB2F2FF\"}," +
            "\"color.state.warn\":{\"value\":\"#E64033FF\"}," +
            "\"color.state.success\":{\"value\":\"#66D966FF\"}," +
            "\"color.text.primary\":{\"value\":\"#EBEBF0FF\"}," +
            "\"color.text.dim\":{\"value\":\"#9999A6FF\"}," +
            "\"color.text.bright\":{\"value\":\"#FFFFFFFF\"}," +
            "\"color.effect.raycast\":{\"value\":\"#FFFFFF03\"}," +
            "\"color.effect.mark\":{\"value\":\"#FFD94C40\"}}}," +
            "\"typography\":{\"tokens\":{" +
            "\"font.title\":{\"fontFamily\":\"NotoSansSC SDF\",\"fontSize\":24,\"lineHeight\":1.0,\"weight\":\"Regular\"}," +
            "\"font.body\":{\"fontFamily\":\"NotoSansSC SDF\",\"fontSize\":20,\"lineHeight\":1.0,\"weight\":\"Regular\"}," +
            "\"font.caption\":{\"fontFamily\":\"NotoSansSC SDF\",\"fontSize\":14,\"lineHeight\":1.0,\"weight\":\"Regular\"}," +
            "\"font.label\":{\"fontFamily\":\"NotoSansSC SDF\",\"fontSize\":20,\"lineHeight\":1.0,\"weight\":\"Regular\"}," +
            "\"font.emphasis\":{\"fontFamily\":\"NotoSansSC SDF\",\"fontSize\":20,\"lineHeight\":1.0,\"weight\":\"Bold\"}}}," +
            "\"spacing\":{\"tokens\":{}},\"radius\":{\"tokens\":{}},\"elevation\":{\"tokens\":{}}," +
            "\"motion\":{\"tokens\":{}},\"icon\":{\"tokens\":{}}}}";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempDir);
            File.WriteAllText(UIThemePaths.FullPath(JsonPath), Doc);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
            Assert.IsFalse(AssetDatabase.IsValidFolder(TempDir), "临时资产目录必须清理干净（含 .meta）");
            Assert.IsFalse(Directory.Exists(UIThemePaths.FullPath(TempDir)), "临时目录不应残留");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string MakePrefab(string name, Color color)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var image = root.AddComponent<Image>();
            image.color = color;
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        // ---- 颜色 ----

        [Test]
        public void 颜色_生产读口不再持有颜色字面量()
        {
            string source = File.ReadAllText(
                Path.Combine(UIThemePaths.ProjectRoot, "Assets/LiteGame/Editor/Core/Style/UIStyle.cs"));

            StringAssert.DoesNotContain("new Color(", source, "颜色值只允许出现在令牌 JSON（红线 5）");
            StringAssert.DoesNotContain("0.12f", source, "旧色值应已退役");
        }

        // ---- 文字 ----

        [Test]
        public void 文字_五档字号与字重来自令牌()
        {
            UITextStyleDef[] all = UITextStyle.All;
            Assert.AreEqual(5, all.Length, "初始五角色");
            CollectionAssert.AreEqual(new[] { 24f, 20f, 14f, 20f, 20f },
                all.OrderBy(def => (int)def.Role).Select(def => def.FontSize).ToArray(), "字号来自 font.* 令牌");
            Assert.IsTrue(all.Single(def => def.Role == UITextRole.Emphasis).Bold, "强调档来自 weight=Bold");
            Assert.AreEqual(1, all.Count(def => def.Bold), "唯一加粗档");
        }

        [Test]
        public void 文字_生产读口不再持有字号字面量()
        {
            string source = File.ReadAllText(
                Path.Combine(UIThemePaths.ProjectRoot, "Assets/LiteGame/Editor/Core/Style/UITextStyle.cs"));

            StringAssert.DoesNotContain("24f", source, "字号只允许来自令牌");
            StringAssert.DoesNotContain("14f", source, "字号只允许来自令牌");
        }

        // ---- 读面失败语义 ----

        [Test]
        public void 目录_资产缺失时显式失败()
        {
            Assert.Throws<FileNotFoundException>(() => UITokenCatalog.Load(TempDir + "/missing.asset"),
                "缺资产必须显式失败，不返回空集冒充成功");
        }

        // ---- 对账语义不变 ----

        [Test]
        public void 工具_颜色对账命中与不命中判定不变()
        {
            UITokenCatalog catalog = UITokenCatalog.Load();
            Assert.IsTrue(catalog.TryColor("color.brand.primary", out Color primary));

            string hit = MakePrefab("Page_Hit", primary);
            string miss = MakePrefab("Page_Miss", new Color(0.123f, 0.456f, 0.789f, 1f));

            List<(string asset, string node, Color color)> drifts = UIStyleTool.ReportDrift(new[] { TempDir });

            Assert.AreEqual(1, drifts.Count, "只有不命中项被报告");
            StringAssert.Contains(miss, drifts[0].asset);
            Assert.IsFalse(drifts[0].asset.EndsWith(hit), "命中项不得被报告");
        }

        // ---- 令牌漂移规则 ----

        [Test]
        public void 规则_令牌源改动未重生成报Error_重生成后消失()
        {
            string realJson = UIThemePaths.FullPath(UIThemePaths.DefaultJsonPath);
            string realAsset = UIThemePaths.FullPath(UIThemePaths.DefaultAssetPath);
            byte[] jsonBefore = File.ReadAllBytes(realJson);
            byte[] assetBefore = File.ReadAllBytes(realAsset);

            UIThemeGenerationResult generated = UIThemeAssetGenerator.Generate(JsonPath, AssetPath);
            Assert.IsTrue(generated.Success, generated.Summary());

            var context = new UIValidationContext(UIThemePaths.ProjectRoot,
                themeJsonPath: JsonPath, themeAssetPath: AssetPath);

            Assert.IsEmpty(new StyleTokenDriftRule().Check(context).ToList(), "一致时不应报问题");

            File.WriteAllText(UIThemePaths.FullPath(JsonPath), Doc.Replace("#1F1F29F2", "#1F1F29F3"));
            List<UIIssue> issues = new StyleTokenDriftRule().Check(context).ToList();

            Assert.AreEqual(1, issues.Count, "改 JSON 未重生成应报一条");
            Assert.AreEqual(UIRuleIds.StyleTokenDrift, issues[0].RuleId);
            Assert.AreEqual(UISeverity.Error, issues[0].Severity, "令牌过期/手改属 Error");
            StringAssert.Contains("过期", issues[0].Message ?? string.Empty, "文案应直说 SO 过期或手改");
            StringAssert.Contains("ui.style.tokens", issues[0].FixHint ?? string.Empty, "应提示重生成命令");

            File.WriteAllText(UIThemePaths.FullPath(JsonPath), Doc);
            Assert.IsTrue(UIThemeAssetGenerator.Generate(JsonPath, AssetPath).Success);
            Assert.IsEmpty(new StyleTokenDriftRule().Check(context).ToList(), "重生成后问题应消失");

            CollectionAssert.AreEqual(jsonBefore, File.ReadAllBytes(realJson), "真实令牌源不得被夹具触碰");
            CollectionAssert.AreEqual(assetBefore, File.ReadAllBytes(realAsset), "真实主题资产不得被夹具触碰");
        }
    }
}
