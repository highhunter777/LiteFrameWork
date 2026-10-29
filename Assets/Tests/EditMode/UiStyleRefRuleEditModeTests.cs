using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteFramework.UI;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// ui.style.ref 规则族（P2-3 B2 / AC5）：五判据——UiStyleRef 未知令牌 Error / 值漂移 Warning / 无 Graphic Error；
    /// widget 状态 token 三态（未知 Error / 漂移 Warning / 空合法）；同节点 UiStyleRef＋IThemeBound 双写者 Error。
    /// 阈值单源（UiStyleResolver.HitEpsilon）；规则注册进 UiValidator 默认规则集。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiStyleRefRuleEditModeTests
    {
        private const string TempDir = "Assets/TempUiStyleRefRuleTests";

        [SetUp]
        public void SetUp() => EnsureFolder(TempDir);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string SavePrefab(string name, System.Action<GameObject> build)
        {
            string path = TempDir + "/" + name + ".prefab";
            var root = new GameObject(name, typeof(RectTransform));
            build(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        private static UiValidationContext ContextFor(params string[] assets)
            => new UiValidationContext(UiValidator.ProjectRoot, null, assets);

        private static List<UiIssue> CheckRule(string asset)
            => new StyleRefRule().Check(ContextFor(asset)).ToList();

        private static Color TokenColor(string id)
        {
            UiTokenCatalog catalog = UiTokenCatalog.Load();
            Assert.IsTrue(catalog.TryColor(id, out Color value), id + " 应在令牌集");
            return value;
        }

        [Test]
        public void 未知令牌_Error()
        {
            string asset = SavePrefab("ref_unknown", root =>
            {
                var node = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.AddComponent<UiStyleRef>().ColorToken = "color.nope";
            });

            List<UiIssue> issues = CheckRule(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Error && i.Node.Contains("Icon")),
                "未知令牌应报 Error：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 值与令牌一致_该规则零问题()
        {
            Color primary = TokenColor("color.brand.primary");
            string asset = SavePrefab("ref_clean", root =>
            {
                var node = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.GetComponent<Image>().color = primary;
                node.AddComponent<UiStyleRef>().ColorToken = "color.brand.primary";
            });

            Assert.IsEmpty(CheckRule(asset), "声明与值一致——零问题");
        }

        [Test]
        public void 值漂移_Warning()
        {
            string asset = SavePrefab("ref_drift", root =>
            {
                var node = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.GetComponent<Image>().color = Color.magenta;      // 远离令牌现值
                node.AddComponent<UiStyleRef>().ColorToken = "color.brand.primary";
            });

            List<UiIssue> issues = CheckRule(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Warning && i.Node.Contains("Icon")),
                "声明值漂移应报 Warning：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 无Graphic_Error()
        {
            string asset = SavePrefab("ref_notarget", root =>
            {
                var node = new GameObject("Holder", typeof(RectTransform));
                node.transform.SetParent(root.transform, false);
                node.AddComponent<UiStyleRef>().ColorToken = "color.bg.screen";
            });

            List<UiIssue> issues = CheckRule(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Error),
                "挂 ref 无 Graphic 应报 Error：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 双写者_同节点UiStyleRef与IThemeBound_Error()
        {
            string asset = SavePrefab("ref_dual", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.AddComponent<StateButton>();
                node.AddComponent<UiStyleRef>().ColorToken = "color.bg.screen";
            });

            List<UiIssue> issues = CheckRule(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Error && i.Node.Contains("Btn")),
                "UiStyleRef＋IThemeBound 同节点（双写者）应报 Error：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 双写者_writer认领的tab目标_Error()
        {
            Color primary = TokenColor("color.brand.primary");
            string asset = SavePrefab("ref_writer_ancestor", root =>
            {
                var tabs = root.AddComponent<TabGroup>();
                var tab = new GameObject("Tab0", typeof(RectTransform), typeof(Image), typeof(Button));
                tab.transform.SetParent(root.transform, false);
                tabs.Tabs.Add(tab.GetComponent<Button>());
                tab.GetComponent<Image>().color = primary;
                tab.AddComponent<UiStyleRef>().ColorToken = "color.brand.primary";
            });

            List<UiIssue> issues = CheckRule(asset);
            Assert.IsTrue(issues.Any(i => i.Severity == UiSeverity.Error && i.Node.Contains("Tab0")),
                "writer 子树内的 UiStyleRef 应报 Error（不只同节点）：\n" + string.Join("\n", issues));
        }

        [Test]
        public void SelectableTheme同节点ref_不误报()
        {
            Color primary = TokenColor("color.brand.primary");
            string asset = SavePrefab("ref_selectable_theme", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
                node.transform.SetParent(root.transform, false);
                node.GetComponent<Image>().color = primary;
                node.AddComponent<UiSelectableTheme>();
                node.AddComponent<UiStyleRef>().ColorToken = "color.brand.primary";
            });

            List<UiIssue> issues = CheckRule(asset);
            Assert.IsFalse(issues.Any(i => i.Severity == UiSeverity.Error),
                "UiSelectableTheme 只写 tint，不是 Graphic writer——基色 ref 不应报双写者：\n" + string.Join("\n", issues));
        }

        [Test]
        public void widget状态token_未知Error_漂移Warning_空合法()
        {
            Color normal = TokenColor("color.state.interact.normal");
            string unknown = SavePrefab("w_unknown", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.AddComponent<StateButton>().NormalColorToken = "color.nope";
            });
            Assert.IsTrue(CheckRule(unknown).Any(i => i.Severity == UiSeverity.Error),
                "widget 未知 token 应报 Error");

            string drift = SavePrefab("w_drift", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                var button = node.AddComponent<StateButton>();
                button.NormalColorToken = "color.state.interact.normal";
                button.NormalColor = Color.magenta;                    // 序列化值漂移
            });
            Assert.IsTrue(CheckRule(drift).Any(i => i.Severity == UiSeverity.Warning),
                "widget 已填 token 但序列化值漂移应报 Warning");

            string empty = SavePrefab("w_empty", root =>
            {
                var node = new GameObject("Btn", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.AddComponent<StateButton>();                      // 全部 token 为空
            });
            Assert.IsEmpty(CheckRule(empty), "空 token＝未令牌化，合法不报（B4 存量口径）");
        }

        [Test]
        public void 规则注册进默认规则集()
        {
            string asset = SavePrefab("ref_registered", root =>
            {
                var node = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                node.transform.SetParent(root.transform, false);
                node.AddComponent<UiStyleRef>().ColorToken = "color.nope";
            });

            UiReport report = UiValidator.Run(ContextFor(asset));
            Assert.IsTrue(report.Issues.Any(i => i.RuleId == UiRuleIds.StyleRef),
                "ui.style.ref 应进 UiValidator 默认规则集：\n" + string.Join("\n", report.Issues.Select(i => i.RuleId)));
        }
    }
}
