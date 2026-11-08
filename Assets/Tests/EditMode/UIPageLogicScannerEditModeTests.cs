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
    /// 页面逻辑源扫描面（《Lua 退出专项设计》§4.7）：C# 页源的调用点/事件接线提取、
    /// 按扩展名分派的双源解析，以及"逻辑件 ↔ prefab 载体"对账规则。
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
        OnButton(""Btn_Go"", Go);
        OnButton(""Btn_Lambda"", () => { });
        OnListClick(""VList_Rows"", Enter);
        var dynamicName = ""Txt_Dynamic"";
        OnButton(dynamicName, Go);
        // OnButton(""Btn_Commented"", Go);
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
            List<UIPageApiCall> calls = UIPageLogicScanner.ScanCSharpApiCalls(CSharpSample);

            CollectionAssert.Contains(calls.Select(c => c.Api).ToList(), "SetTextKey");
            CollectionAssert.Contains(calls.Select(c => c.Api).ToList(), "GetControl");
            CollectionAssert.Contains(calls.Select(c => c.Name).ToList(), "Txt_Title");
            CollectionAssert.Contains(calls.Select(c => c.Name).ToList(), "Btn_Go");

            Assert.IsFalse(calls.Any(c => c.Name == "Txt_Dynamic"), "变量名首参不产出（只认字面量）");
            Assert.IsFalse(calls.Any(c => c.Name == "Btn_Commented"), "注释行不产出");
        }

        [Test]
        public void CSharp事件接线_方法组与lambda解析()
        {
            List<UIPageEventWiring> wiring = UIPageLogicScanner.ScanCSharpEventWiring(CSharpSample);

            UIPageEventWiring go = wiring.Single(w => w.Name == "Btn_Go" && w.Api == "OnButton");
            Assert.AreEqual("Go", go.Handler, "方法组取末段");
            Assert.AreEqual("onClick", go.Event);

            UIPageEventWiring lambda = wiring.Single(w => w.Name == "Btn_Lambda");
            Assert.AreEqual("anonymous", lambda.Handler, "lambda 记 anonymous");

            UIPageEventWiring list = wiring.Single(w => w.Name == "VList_Rows");
            Assert.AreEqual("onListClick", list.Event, "列表接线映射到 onListClick");
        }

        [Test]
        public void 双源分派_按扩展名走对应语法()
        {
            Assert.IsTrue(UIPageLogicScanner.IsCSharp("Assets/LiteGame/Modules/UI/Logic/Screens/Demo/Page_ButtonDemoLogic.cs"));
            Assert.IsFalse(UIPageLogicScanner.IsCSharp("Assets/LiteGame/Lua/UI/Page_Login.lua"));

            List<UIPageApiCall> luaCalls = UIPageLogicScanner.ScanApiCalls(
                "Assets/LiteGame/Lua/UI/Page_Login.lua", "self.ui:SetVisible(\"Txt_Status\", false)");
            Assert.AreEqual(1, luaCalls.Count, "Lua 源经分发器仍按 Lua 语法解析");
            Assert.AreEqual("Txt_Status", luaCalls[0].Name);
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
            var page = new UIPageManifestEntry { Name = pageName, PrefabPath = prefabPath, ContractPresent = false };
            page.Forms.Add(new UIPageFormEntry { FormId = 901, Layer = 0, FullScreen = true });
            manifest.Pages.Add(page);

            string path = TempDir + "/Page_logic.manifest.json";
            File.WriteAllText(UIPageManifestPaths.FullPath(path), UIPageManifestJson.Serialize(manifest));
            return path;
        }
    }
}
