using LiteFramework.UI;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// `UiSelectableThemeApplier`（P2-3 B4 / AC3）：只给直挂 ColorTint Selectable 补组件；嵌套实例跳过由源模板继承；
    /// 幂等；不写任何 ColorBlock 序列化值。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiSelectableThemeApplierEditModeTests
    {
        private const string TempDir = "Assets/TempUiSelectableThemeTests";
        private static readonly string[] Dirs = { TempDir };

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "TempUiSelectableThemeTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        private static string SavePrefab(string name, System.Action<GameObject> build)
        {
            var root = new GameObject(name, typeof(RectTransform));
            build(root);
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        private static void AddButton(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent.transform, false);
        }

        [Test]
        public void EnsureAll_直挂补组件_嵌套跳过_幂等()
        {
            string sourcePath = SavePrefab("Src_Button", root =>
            {
                root.AddComponent<Image>();
                root.AddComponent<Button>();
            });

            SavePrefab("Page_NestedAndDirect", root =>
            {
                var nested = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), root.transform);
                nested.name = "Nested_Button";
                AddButton(root, "Direct_Button");
            });

            UiSelectableThemeApplier.EnsureReport report = UiSelectableThemeApplier.EnsureAll(Dirs);

            Assert.AreEqual(2, report.Added, "源模板 + 直挂各 1 个：" + report.Added);
            Assert.AreEqual(1, report.Inherited, "嵌套实例跳过计数");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath).GetComponent<UiSelectableTheme>());

            var page = AssetDatabase.LoadAssetAtPath<GameObject>(TempDir + "/Page_NestedAndDirect.prefab");
            Assert.IsNotNull(page.transform.Find("Direct_Button").GetComponent<UiSelectableTheme>(), "直挂应补组件");
            var nestedTheme = page.transform.Find("Nested_Button").GetComponent<UiSelectableTheme>();
            Assert.IsNotNull(nestedTheme, "嵌套实例应继承源模板组件");
            Assert.IsFalse(PrefabUtility.IsAddedComponentOverride(nestedTheme), "不得在嵌套实例制造新增组件 override");

            UiSelectableThemeApplier.EnsureReport second = UiSelectableThemeApplier.EnsureAll(Dirs);
            Assert.AreEqual(0, second.Added, "幂等：二次新增 0");
        }

        [Test]
        public void EnsureAll_不改ColorBlock序列化值()
        {
            string path = SavePrefab("Page_ColorKeep", root =>
            {
                root.AddComponent<Image>();
                var button = root.AddComponent<Button>();
                var block = button.colors;
                block.normalColor = Color.magenta;
                block.disabledColor = new Color(0.1f, 0.2f, 0.3f, 0.5f);
                button.colors = block;
            });

            UiSelectableThemeApplier.EnsureAll(Dirs);

            var button2 = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Button>();
            Assert.AreEqual(Color.magenta, button2.colors.normalColor, "只加组件，不改颜色");
            Assert.AreEqual(0.5f, button2.colors.disabledColor.a, "alpha 不变");
        }

        [Test]
        public void EnsureAll_非ColorTint跳过()
        {
            SavePrefab("Page_NoTint", root =>
            {
                root.AddComponent<Image>();
                var button = root.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
            });

            UiSelectableThemeApplier.EnsureReport report = UiSelectableThemeApplier.EnsureAll(Dirs);

            Assert.AreEqual(0, report.Added);
            Assert.AreEqual(1, report.Skipped, "非 ColorTint 不适用");
        }
    }
}