using System.IO;
using System.Linq;
using LiteFramework.UI;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UiThemePaths = LiteGame.EditorTools.UI.UiThemePaths;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 主题资产生成器（《UI 编辑器工具链专项设计》§8.1/§8.3）：确定性、幂等、负例零写盘、路径守卫，
    /// 并把"手改资产→重生成覆盖"钉成行为。夹具只用临时目录 `Assets/TempUiThemeTests`，结束即删。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiThemeAssetGeneratorEditModeTests
    {
        private const string TempDir = "Assets/TempUiThemeTests";
        private const string JsonPath = TempDir + "/tokens.json";
        private const string AssetPath = TempDir + "/UiTheme.asset";

        private const string Json =
            "{\"schemaVersion\":\"2.0\"," +
            "\"meta\":{\"name\":\"UiTheme\",\"version\":\"1.0.0\",\"theme\":\"Default\"}," +
            "\"groups\":{" +
            "\"color\":{\"tokens\":{\"color.bg.screen\":{\"value\":\"#1F1F29F2\"},\"color.brand.primary\":{\"value\":\"#E6BF40FF\"}}}," +
            "\"typography\":{\"tokens\":{\"font.title\":{\"fontFamily\":\"NotoSansSC SDF\",\"fontSize\":24,\"lineHeight\":1.0,\"weight\":\"Regular\"}}}," +
            "\"spacing\":{\"tokens\":{}},\"radius\":{\"tokens\":{}},\"elevation\":{\"tokens\":{}}," +
            "\"motion\":{\"tokens\":{}},\"icon\":{\"tokens\":{}}}}";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempDir);
            File.WriteAllText(UiThemePaths.FullPath(JsonPath), Json);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
            Assert.IsFalse(AssetDatabase.IsValidFolder(TempDir), "临时资产目录必须清理干净（含 .meta）");
            Assert.IsFalse(Directory.Exists(UiThemePaths.FullPath(TempDir)), "临时目录不应残留");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [Test]
        public void 生成_首次生成资产并按组计数读回()
        {
            UiThemeGenerationResult result = UiThemeAssetGenerator.Generate(JsonPath, AssetPath);

            Assert.IsTrue(result.Success, result.Summary());
            Assert.IsTrue(result.Changed, "首次生成应报告变更");
            Assert.AreEqual(2, result.ColorCount);
            Assert.AreEqual(1, result.TypographyCount);
            Assert.AreEqual(0, result.SpacingCount);
            Assert.IsNotEmpty(result.SourceHash, "应带 sourceHash");

            var asset = AssetDatabase.LoadAssetAtPath<UiThemeAsset>(AssetPath);
            Assert.IsNotNull(asset, "资产应已落盘");
            Assert.AreEqual("Default", asset.Theme);
            Assert.AreEqual("1.0.0", asset.Version);
            Assert.AreEqual(result.SourceHash, asset.SourceHash, "资产指纹与报告一致");
            Assert.AreEqual(2, asset.Colors.Length);
            Assert.AreEqual((byte)31, asset.Colors[0].Value.r, "颜色按 8bit 原样读回");
        }

        [Test]
        public void 生成_二次生成零变更且资产字节不变()
        {
            Assert.IsTrue(UiThemeAssetGenerator.Generate(JsonPath, AssetPath).Success);
            byte[] before = File.ReadAllBytes(UiThemePaths.FullPath(AssetPath));

            UiThemeGenerationResult second = UiThemeAssetGenerator.Generate(JsonPath, AssetPath);

            Assert.IsTrue(second.Success, second.Summary());
            Assert.IsFalse(second.Changed, "同源再生成不应报告变更");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(UiThemePaths.FullPath(AssetPath)), "资产必须逐字节不变");
        }

        [Test]
        public void 生成_非法令牌源零写盘()
        {
            File.WriteAllText(UiThemePaths.FullPath(JsonPath),
                Json.Replace("#1F1F29F2", "#GGGGGGGG"));

            UiThemeGenerationResult result = UiThemeAssetGenerator.Generate(JsonPath, AssetPath);

            Assert.IsFalse(result.Success, "非法源不应成功");
            Assert.IsNotEmpty(result.Errors, "应列出问题");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UiThemeAsset>(AssetPath), "失败时不得创建资产");
        }

        [Test]
        public void 生成_资产路径不在Assets下被拒绝()
        {
            UiThemeGenerationResult result = UiThemeAssetGenerator.Generate(JsonPath, "Temp/UiTheme.asset");

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("Assets")), "错误应说明路径约束：" + result.Summary());
        }

        [Test]
        public void 生成_资产后缀非asset被拒绝()
        {
            UiThemeGenerationResult result = UiThemeAssetGenerator.Generate(JsonPath, TempDir + "/UiTheme.txt");

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Any(error => error.Contains(".asset")), "错误应说明后缀约束：" + result.Summary());
        }

        [Test]
        public void 生成_路径穿越被拒绝()
        {
            UiThemeGenerationResult result = UiThemeAssetGenerator.Generate(
                JsonPath, TempDir + "/../../UiTheme.asset");

            Assert.IsFalse(result.Success, "归一后越出 Assets/ 的路径必须拒绝");
            Assert.IsTrue(result.Errors.Any(error => error.Contains("Assets")), "错误应说明路径约束：" + result.Summary());
        }

        [Test]
        public void 生成_令牌源缺失报错()
        {
            UiThemeGenerationResult result = UiThemeAssetGenerator.Generate(TempDir + "/missing.json", AssetPath);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("missing.json")), "错误应带缺失路径：" + result.Summary());
        }

        [Test]
        public void 生成_手改资产后重生成覆盖并报告变更()
        {
            Assert.IsTrue(UiThemeAssetGenerator.Generate(JsonPath, AssetPath).Success);
            var asset = AssetDatabase.LoadAssetAtPath<UiThemeAsset>(AssetPath);
            asset.Colors[0].Value = new UnityEngine.Color32(1, 2, 3, 4);
            UnityEditor.EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            UiThemeGenerationResult result = UiThemeAssetGenerator.Generate(JsonPath, AssetPath);

            Assert.IsTrue(result.Success, result.Summary());
            Assert.IsTrue(result.Changed, "手改后重生成应报告变更");
            var reloaded = AssetDatabase.LoadAssetAtPath<UiThemeAsset>(AssetPath);
            Assert.AreEqual((byte)31, reloaded.Colors[0].Value.r, "值应被令牌源覆盖回来");
        }
    }
}
