using System.Collections.Generic;
using System.Linq;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.EditorTools.UI;
using LiteGame.Editor.UiTools.Validation.Rules;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// `ui.style.selectable`（P2-3 B4 / AC7）：缺组件 Warning/无 Selectable Error/未知令牌 Error/
    /// ColorBlock 漂移 Warning/非 ColorTint 跳过；真实面由 UiValidationEditModeTests 钉 0。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiStyleSelectableRuleEditModeTests
    {
        private const string TempDir = "Assets/TempUiStyleSelectableTests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "TempUiStyleSelectableTests");
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

        private static Button AddButton(GameObject go)
        {
            go.AddComponent<Image>();
            return go.AddComponent<Button>();
        }

        private static List<UiIssue> Check(string asset, string themeAssetPath = null)
            => new StyleSelectableRule().Check(
                new UiValidationContext(UiValidator.ProjectRoot, null, new[] { asset }, themeAssetPath: themeAssetPath)).ToList();

        [Test]
        public void 正例_ColorTint有组件且值命中_零问题()
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            string asset = SavePrefab("sel_ok", root =>
            {
                var button = AddButton(root);
                var block = button.colors;
                catalog.TryColor(UiSelectableTheme.NormalToken, out Color normal);
                catalog.TryColor(UiSelectableTheme.HighlightedToken, out Color highlighted);
                catalog.TryColor(UiSelectableTheme.PressedToken, out Color pressed);
                catalog.TryColor(UiSelectableTheme.SelectedToken, out Color selected);
                catalog.TryColor(UiSelectableTheme.DisabledToken, out Color disabled);
                block.normalColor = normal;
                block.highlightedColor = highlighted;
                block.pressedColor = pressed;
                block.selectedColor = selected;
                block.disabledColor = disabled;
                button.colors = block;
                root.AddComponent<UiSelectableTheme>();
            });

            Assert.IsEmpty(Check(asset), "正例零问题");
        }

        [Test]
        public void 无Selectable_Error()
        {
            string asset = SavePrefab("sel_no_selectable", root =>
            {
                var button = AddButton(root);
                root.AddComponent<UiSelectableTheme>();
                Object.DestroyImmediate(button, true);      // 人为移除 Selectable（组件不再 RequireComponent，规则应显式报错）
            });

            List<UiIssue> issues = Check(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Error),
                "UiSelectableTheme 节点无 Selectable 应 Error：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 缺组件_Warning()
        {
            string asset = SavePrefab("sel_missing", root => AddButton(root));
            List<UiIssue> issues = Check(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Warning && i.RuleId == UiRuleIds.StyleSelectable),
                "ColorTint 缺组件应 Warning：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 未知令牌_Error()
        {
            string themePath = TempDir + "/PartialTheme.asset";
            var asset = ScriptableObject.CreateInstance<UiThemeAsset>();
            asset.Colors = new[] { new UiThemeColorEntry { Id = "color.bg.screen", Value = Color.black } };
            AssetDatabase.CreateAsset(asset, themePath);
            AssetDatabase.SaveAssets();

            string prefab = SavePrefab("sel_unknown", root =>
            {
                AddButton(root);
                root.AddComponent<UiSelectableTheme>();
            });

            List<UiIssue> issues = Check(prefab, themePath);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Error), "缺 5 枚 selectable 令牌应 Error：\n" + string.Join("\n", issues));
        }

        [Test]
        public void ColorBlock漂移_Warning()
        {
            string asset = SavePrefab("sel_drift", root =>
            {
                var button = AddButton(root);
                var block = button.colors;
                block.normalColor = Color.magenta;
                button.colors = block;
                root.AddComponent<UiSelectableTheme>();
            });

            List<UiIssue> issues = Check(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Warning), "漂移应 Warning");
        }

        [Test]
        public void 嵌套实例_由源模板检查_容器不重复报()
        {
            string sourcePath = SavePrefab("sel_nested_source", root => AddButton(root));
            var root = new GameObject("sel_nested_container", typeof(RectTransform));
            string pagePath = TempDir + "/sel_nested_container.prefab";
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), root.transform);
                instance.name = "Nested_Button";
                PrefabUtility.SaveAsPrefabAsset(root, pagePath);
            }
            finally { Object.DestroyImmediate(root); }

            Assert.IsEmpty(Check(pagePath), "嵌套实例由源模板负责，容器不应重复报缺组件/漂移");
        }

        [Test]
        public void 非ColorTint_跳过()
        {
            string asset = SavePrefab("sel_none", root =>
            {
                var button = AddButton(root);
                button.transition = Selectable.Transition.None;
            });

            Assert.IsEmpty(Check(asset), "非 ColorTint 不适用");
        }
    }
}