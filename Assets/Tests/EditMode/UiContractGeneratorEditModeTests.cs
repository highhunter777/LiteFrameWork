using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteClient.UI;
using LiteCodeGen;
using LiteGame.Editor.UiTools.Contracts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-1 页面契约生成：从 prefab 提取 controls/text/styles/templates，遵守
    /// Clone_ 不暴露、嵌套 BindRoot 独立、模板内部件不展开、无时间戳/稳定排序的确定性约束。
    /// </summary>
    public sealed class UiContractGeneratorEditModeTests
    {
        private const string TempDir = "Assets/ShareResource/UI/Screens/TempUiContractTests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets/ShareResource/UI/Screens", "TempUiContractTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.DeleteAsset(TempDir);
        }

        private static UiPageMetadata Meta(int formId = 901)
            => new UiPageMetadata
            {
                FormId = formId,
                LuaPath = "UI.Page_Test",
                Layer = 1,
                FullScreen = true,
                CacheStrategy = "Lru",
            };

        private static GameObject Save(GameObject root, string fileName)
        {
            string path = TempDir + "/" + fileName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static GameObject Template()
        {
            var root = new GameObject("Tmp_Row", typeof(RectTransform));
            var inside = new GameObject("Button_Inner", typeof(RectTransform), typeof(Button));
            inside.transform.SetParent(root.transform, false);
            inside.AddComponent<BindNode>().BindName = "Button_Inner";
            return Save(root, "Tmp_Row");
        }

        private static GameObject Page(GameObject template)
        {
            var root = new GameObject("Page_Test", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

            var start = new GameObject("Button_Start", typeof(RectTransform), typeof(Button));
            start.transform.SetParent(root.transform, false);
            start.AddComponent<BindNode>().BindName = "Button_Start";

            var title = new GameObject("Txt_Title", typeof(RectTransform), typeof(Text));
            title.transform.SetParent(root.transform, false);
            title.AddComponent<BindNode>().BindName = "Txt_Title";
            title.AddComponent<LTextKeyRef>().Key = "UI.Test.Title";
            title.AddComponent<UiTextStyleRef>().Role = UiTextRole.Title;

            var clone = new GameObject("Clone_Item", typeof(RectTransform));
            clone.transform.SetParent(root.transform, false);
            var cloneButton = new GameObject("Button_Clone", typeof(RectTransform), typeof(Button));
            cloneButton.transform.SetParent(clone.transform, false);
            cloneButton.AddComponent<BindNode>().BindName = "Button_Clone";
            var cloneTemplate = (GameObject)PrefabUtility.InstantiatePrefab(template);
            cloneTemplate.transform.SetParent(clone.transform, false);

            var nestedRoot = new GameObject("GenRoot", typeof(RectTransform));
            nestedRoot.transform.SetParent(root.transform, false);
            nestedRoot.AddComponent<BindRoot>().ScriptName = "GenRoot";
            var nestedButton = new GameObject("Button_Gen", typeof(RectTransform), typeof(Button));
            nestedButton.transform.SetParent(nestedRoot.transform, false);
            nestedButton.AddComponent<BindNode>().BindName = "Button_Gen";
            var nestedTemplate = (GameObject)PrefabUtility.InstantiatePrefab(template);
            nestedTemplate.transform.SetParent(nestedRoot.transform, false);

            var templateInstance = (GameObject)PrefabUtility.InstantiatePrefab(template);
            templateInstance.transform.SetParent(root.transform, false);
            templateInstance.AddComponent<BindNode>().BindName = "List_Rows";

            return Save(root, "Page_Test");
        }

        [Test]
        public void 契约生成_提取四段_并过滤模板内部与克隆与嵌套根()
        {
            GameObject template = Template();
            GameObject page = Page(template);

            var result = UiContractGenerator.Generate(AssetDatabase.GetAssetPath(page), Meta());
            Assert.IsTrue(result.Ok, result.ErrorText);
            UiPageContract c = result.Contract;

            Assert.AreEqual("2.0", c.SchemaVersion);
            Assert.AreEqual("Page_Test", c.Page.Name);
            Assert.AreEqual(901, c.Page.FormId);
            Assert.AreEqual("UI.Page_Test", c.Page.LuaPath);
            Assert.AreEqual("Lru", c.Page.CacheStrategy);

            CollectionAssert.AreEquivalent(
                new[] { "Button_Start", "List_Rows", "Txt_Title" },
                c.Controls.Select(x => x.BindName).ToArray(),
                "Clone_/嵌套 BindRoot/模板内部件都不得成为页面 controls");

            UiContractControl start = c.Controls.Single(x => x.BindName == "Button_Start");
            Assert.AreEqual("Command", start.Kind);
            Assert.IsTrue(start.Required);
            Assert.AreEqual("UnityEngine.UI.Button", start.Type);

            UiContractControl titleControl = c.Controls.Single(x => x.BindName == "Txt_Title");
            Assert.AreEqual("None", titleControl.Kind);
            Assert.IsFalse(titleControl.Required);

            Assert.AreEqual(1, c.Text.Count);
            Assert.AreEqual("UI.Test.Title", c.Text[0].Key);
            Assert.AreEqual(0, c.Text[0].Args);

            Assert.AreEqual(1, c.Styles.Count);
            Assert.AreEqual("Title", c.Styles[0].Role);

            Assert.AreEqual(1, c.Templates.Count, "Clone_/嵌套 BindRoot 下的模板实例不属于本页接口面");
            Assert.AreEqual("Tmp_Row", c.Templates[0].Template);
            Assert.AreEqual("Tmp_Row", c.Templates[0].NodePath);
            Assert.AreEqual("List_Rows", c.Controls.Single(x => x.BindName == "List_Rows").BindName);
            Assert.IsFalse(c.Controls.Any(x => x.BindName == "Button_Inner"), "模板内部件不得展开进 controls");

            StringAssert.StartsWith("sha256:", c.SourceHash);
            Assert.AreEqual(64 + "sha256:".Length, c.SourceHash.Length);
        }

        [Test]
        public void 契约生成_确定性_无时间戳_重复生成零diff()
        {
            GameObject template = Template();
            GameObject page = Page(template);
            string path = AssetDatabase.GetAssetPath(page);

            var a = UiContractGenerator.Generate(path, Meta());
            var b = UiContractGenerator.Generate(path, Meta());
            Assert.IsTrue(a.Ok, a.ErrorText);
            Assert.IsTrue(b.Ok, b.ErrorText);
            Assert.AreEqual(UiContractJson.Serialize(a.Contract), UiContractJson.Serialize(b.Contract));
            StringAssert.DoesNotContain("generatedAt", UiContractJson.Serialize(a.Contract));
        }

        [Test]
        public void sourceHash_对绑定面敏感_对页面元数据不敏感()
        {
            GameObject template = Template();
            GameObject page = Page(template);
            string path = AssetDatabase.GetAssetPath(page);

            var a = UiContractGenerator.Generate(path, Meta(901));
            var b = UiContractGenerator.Generate(path, Meta(902));
            Assert.IsTrue(a.Ok, a.ErrorText);
            Assert.IsTrue(b.Ok, b.ErrorText);
            Assert.AreEqual(a.Contract.SourceHash, b.Contract.SourceHash, "sourceHash 只描述 prefab 接口面，不含 formId 等元数据");

            var bind = page.GetComponentsInChildren<BindNode>(true).Single(x => x.BindName == "Button_Start");
            bind.BindName = "Button_StartChanged";
            PrefabUtility.SavePrefabAsset(page);
            var changed = UiContractGenerator.Generate(path, Meta());
            Assert.IsTrue(changed.Ok, changed.ErrorText);
            Assert.AreNotEqual(a.Contract.SourceHash, changed.Contract.SourceHash, "BindName 变化必须改变 sourceHash");
        }

        [Test]
        public void 契约写盘_读回一致_负例不写盘()
        {
            GameObject template = Template();
            GameObject page = Page(template);
            string path = AssetDatabase.GetAssetPath(page);
            string output = TempDir + "/Page_Test.contract.json";

            var ok = UiContractGenerator.GenerateToDisk(path, Meta(), output);
            Assert.IsTrue(ok.Ok, ok.ErrorText);
            Assert.IsTrue(File.Exists(Absolute(output)), "成功生成必须写盘");

            UiPageContract loaded = UiContractStore.Load(output);
            Assert.AreEqual("Page_Test", loaded.Page.Name);
            Assert.AreEqual(ok.Contract.SourceHash, loaded.SourceHash);

            var badRoot = new GameObject("Page_BadWrite", typeof(RectTransform));
            var a = new GameObject("Button_A", typeof(RectTransform), typeof(Button));
            a.transform.SetParent(badRoot.transform, false);
            a.AddComponent<BindNode>().BindName = "Button_Same";
            var b = new GameObject("Button_B", typeof(RectTransform), typeof(Button));
            b.transform.SetParent(badRoot.transform, false);
            b.AddComponent<BindNode>().BindName = "Button_Same";
            GameObject bad = Save(badRoot, "Page_BadWrite");
            string badOutput = TempDir + "/Page_BadWrite.contract.json";

            var badResult = UiContractGenerator.GenerateToDisk(AssetDatabase.GetAssetPath(bad), Meta(), badOutput);
            Assert.IsFalse(badResult.Ok);
            Assert.IsFalse(File.Exists(Absolute(badOutput)), "负例不得写盘");
        }

        [Test]
        public void 真实页面_按tbuiform行生成_契约可用()
        {
            var result = UiContractGenerator.GenerateRegistered("Assets/ShareResource/UI/Screens/Page_Login.prefab");
            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.AreEqual(11, result.Contract.Page.FormId);
            Assert.AreEqual("UI.Page_Login", result.Contract.Page.LuaPath);
            Assert.GreaterOrEqual(result.Contract.Controls.Count, 1);
            StringAssert.StartsWith("sha256:", result.Contract.SourceHash);
        }

        [Test]
        public void 真实页面_模板依赖被记录()
        {
            var result = UiContractGenerator.GenerateRegistered("Assets/ShareResource/UI/Screens/Page_Lobby.prefab");
            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.GreaterOrEqual(result.Contract.Templates.Count, 1, "Page_Lobby 使用了 Tmp_* 模板");
            Assert.IsTrue(result.Contract.Templates.All(x => x.Template.StartsWith("Tmp_")),
                "templates[] 只记录 Tmp_* 模板");
        }

        [Test]
        public void 重复BindName_生成失败并列出错误()
        {
            var root = new GameObject("Page_Duplicate", typeof(RectTransform));
            var a = new GameObject("Button_A", typeof(RectTransform), typeof(Button));
            a.transform.SetParent(root.transform, false);
            a.AddComponent<BindNode>().BindName = "Button_Same";
            var b = new GameObject("Button_B", typeof(RectTransform), typeof(Button));
            b.transform.SetParent(root.transform, false);
            b.AddComponent<BindNode>().BindName = "Button_Same";
            GameObject page = Save(root, "Page_Duplicate");

            var result = UiContractGenerator.Generate(AssetDatabase.GetAssetPath(page), Meta());
            Assert.IsFalse(result.Ok);
            Assert.That(result.ErrorText, Does.Contain("重复"));
        }

        [Test]
        public void 静态key为空_生成失败()
        {
            var root = new GameObject("Page_BadKey", typeof(RectTransform));
            var title = new GameObject("Txt_Title", typeof(RectTransform), typeof(Text));
            title.transform.SetParent(root.transform, false);
            title.AddComponent<LTextKeyRef>().Key = string.Empty;
            GameObject page = Save(root, "Page_BadKey");

            var result = UiContractGenerator.Generate(AssetDatabase.GetAssetPath(page), Meta());
            Assert.IsFalse(result.Ok);
            Assert.That(result.ErrorText, Does.Contain("Key"));
        }

        [Test]
        public void 类型不可解析_生成失败()
        {
            var root = new GameObject("Page_BadType", typeof(RectTransform));
            var button = new GameObject("Button_Bad", typeof(RectTransform), typeof(Button));
            button.transform.SetParent(root.transform, false);
            var bind = button.AddComponent<BindNode>();
            bind.BindName = "Button_Bad";
            bind.CustomTypeName = "No.Such.Type";
            GameObject page = Save(root, "Page_BadType");

            var result = UiContractGenerator.Generate(AssetDatabase.GetAssetPath(page), Meta());
            Assert.IsFalse(result.Ok);
            Assert.That(result.ErrorText, Does.Contain("类型不可解析"));
        }

        [Test]
        public void 输出路径不在Assets下_生成失败且不写盘()
        {
            GameObject template = Template();
            GameObject page = Page(template);

            var result = UiContractGenerator.GenerateToDisk(
                AssetDatabase.GetAssetPath(page), Meta(), "Temp/Page_Test.contract.json");
            Assert.IsFalse(result.Ok);
            Assert.That(result.ErrorText, Does.Contain("Assets/"));
        }

        private static string Absolute(string assetPath)
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
