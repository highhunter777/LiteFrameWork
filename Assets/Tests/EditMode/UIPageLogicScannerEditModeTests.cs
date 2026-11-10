using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Reference;
using LiteGame.Editor.UITools.Validation;
using LiteGame.Editor.UITools.Validation.Rules;
using NUnit.Framework;
using UnityEditor;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 页面逻辑源扫描面（《Lua 退出专项设计》§4.7）：C# 页源的调用点/事件接线提取，
    /// 以及"逻辑件 ↔ prefab 载体"对账规则。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UIPageLogicScannerEditModeTests
    {
        private const string TempDir = "Assets/TempUiPageLogicTests";

        private const string CSharpSample = @"public sealed class Page_DemoLogic : PageLogicBase
{
    protected override void OnShow(IUIData data)
    {
        SetTextKey(""Txt_Title"", ""UI.Demo.Title"");
        GetControl<UnityEngine.UI.Button>(""Btn_Go"");
        OnButtonClick(""Btn_Go"", Go);
        OnButtonClick(""Btn_Lambda"", () => { });
        OnListClick(""VList_Rows"", Enter);
        var dynamicName = ""Txt_Dynamic"";
        OnButtonClick(dynamicName, Go);
        // OnButtonClick(""Btn_Commented"", Go);
    }
}
";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
            {
                string parent = Path.GetDirectoryName(TempDir).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(TempDir));
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        [Test]
        public void CSharp调用点_字面量名提取_变量名与注释不产出()
        {
            List<UIPageApiCall> calls = UIPageLogicScanner.ScanApiCalls(CSharpSample);

            CollectionAssert.Contains(calls.Select(c => c.Api).ToList(), "SetTextKey");
            CollectionAssert.Contains(calls.Select(c => c.Api).ToList(), "GetControl");
            CollectionAssert.Contains(calls.Select(c => c.Name).ToList(), "Txt_Title");
            CollectionAssert.Contains(calls.Select(c => c.Name).ToList(), "Btn_Go");

            Assert.IsFalse(calls.Any(c => c.Name == "Txt_Dynamic"), "变量名首参不产出（只认字面量）");
            Assert.IsFalse(calls.Any(c => c.Name == "Btn_Commented"), "注释行不产出");
        }

        [Test]
        public void 实际页源_ButtonDemo可解析且唯一()
        {
            IReadOnlyList<string> files = UIPageLogicScanner.ListCSharpFiles("Page_ButtonDemo");

            Assert.AreEqual(1, files.Count, "Page_ButtonDemo 逻辑件唯一：\n" + string.Join("\n", files));
            StringAssert.EndsWith("Page_ButtonDemoLogic.cs", files[0]);
        }

        [Test]
        public void 页面逻辑规则_逻辑件存在未挂载体_迁移期报Warning()
        {
            string manifestPath = ManifestFor("Page_ButtonDemo", "Assets/NoSuchPrefab.prefab");
            var issues = new PageLogicRule().Check(ContextFor(manifestPath)).ToList();

            Assert.AreEqual(1, issues.Count, "逻辑件存在但载体缺失须报一条：\n" + string.Join("\n", issues));
            Assert.AreEqual(UIRuleIds.PageLogic, issues[0].RuleId);
            Assert.AreEqual(UISeverity.Warning, issues[0].Severity, "迁移期（RequireLogicForEveryPage=false）为 Warning");
        }

        [Test]
        public void 页面逻辑规则_模板页豁免()
        {
            string manifestPath = ManifestFor("Tmp_Dialog", "Assets/LiteGame/Modules/UI/Content/Screens/Tmp_Dialog.prefab");
            CollectionAssert.IsEmpty(new PageLogicRule().Check(ContextFor(manifestPath)).ToList());
        }

        private static UIValidationContext ContextFor(string manifestPath)
            => new UIValidationContext(UIValidator.ProjectRoot, null, new string[0], pageManifestPath: manifestPath);

        private static string ManifestFor(string pageName, string prefabPath)
        {
            var manifest = new UIPageManifest();
            var page = new UIPageManifestEntry { Name = pageName, PrefabPath = prefabPath };
            page.Forms.Add(new UIPageFormEntry { FormId = 901, Layer = 0, FullScreen = true });
            manifest.Pages.Add(page);

            string path = TempDir + "/Page_logic.manifest.json";
            File.WriteAllText(UIPageManifestPaths.FullPath(path), UIPageManifestJson.Serialize(manifest));
            return path;
        }
    }
}
