using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 布局/适配族与性能静态族规则（工具链设计 §6.2「布局/适配」「性能」；制作规范 UI-LAYOUT-01/UI-PREFAB-03）：
    /// CanvasScaler 基线与第二缩放器禁令、raycastTarget 预算、布局深度、UI 禁 Animator。
    /// 阈值单源＝<see cref="UiRuleThresholds"/>；真实面（Page_* 全合规）由全量校准门禁承担。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiLayoutPerformanceRuleEditModeTests
    {
        private const string TempDir = "Assets/TempUiLayoutRuleTests";

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
            var root = new GameObject(name, typeof(RectTransform));
            build(root);
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        private static GameObject Node(GameObject parent, string nodeName, params System.Type[] components)
        {
            var all = new List<System.Type> { typeof(RectTransform) };
            all.AddRange(components);
            var go = new GameObject(nodeName, all.ToArray());
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static CanvasScaler StandardScaler(GameObject root)
        {
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return scaler;
        }

        private static UiValidationContext ContextFor(params string[] assets)
            => new UiValidationContext(UiValidator.ProjectRoot, null, assets);

        // ---- ui.layout.scaler ----

        [Test]
        public void 缩放器_基线相符通过_参数偏离报Error()
        {
            string good = SavePrefab("Page_GoodScaler", root => StandardScaler(root));

            string modeBad = SavePrefab("Page_BadMode", root =>
            {
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            });
            string resBad = SavePrefab("Page_BadResolution", root =>
            {
                var scaler = StandardScaler(root);
                scaler.referenceResolution = new Vector2(1280f, 720f);
            });
            string matchBad = SavePrefab("Page_BadMatch", root =>
            {
                var scaler = StandardScaler(root);
                scaler.matchWidthOrHeight = 0f;
            });

            var rule = new LayoutScalerRule();
            CollectionAssert.IsEmpty(rule.Check(ContextFor(good)).ToList(), "合规缩放器零误报");

            foreach (string bad in new[] { modeBad, resBad, matchBad })
            {
                var issues = rule.Check(ContextFor(bad)).ToList();
                Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error),
                    "缩放参数偏离基线须报 Error：" + bad + "\n" + string.Join("\n", issues));
                Assert.AreEqual(UiRuleIds.LayoutScaler, issues[0].RuleId);
            }
        }

        [Test]
        public void 缩放器_页面子树第二套缩放报Error()
        {
            string path = SavePrefab("Page_SecondScaler", root =>
            {
                StandardScaler(root);
                Node(root, "Cot_Panel", typeof(CanvasScaler));
            });

            var issues = new LayoutScalerRule().Check(ContextFor(path)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error),
                "子树第二套 CanvasScaler 违反 UI-LAYOUT-01：\n" + string.Join("\n", issues));
            StringAssert.Contains("第二套", issues[0].Message, "报文说明禁止口径");
        }

        [Test]
        public void 缩放器_非Page根不检查()
        {
            string tmp = SavePrefab("Tmp_NoScaler", root => { });

            var issues = new LayoutScalerRule().Check(ContextFor(tmp)).ToList();

            CollectionAssert.IsEmpty(issues, "Tmp_ 系统面继承缩放（无 CanvasScaler 合法），规则不辖：\n"
                + string.Join("\n", issues));
        }

        // ---- ui.performance.raycast-target ----

        [Test]
        public void 射线目标_超预算报Warning_预算内零误报()
        {
            int budget = UiRuleThresholds.RaycastTargetBudget;
            string over = SavePrefab("Tmp_RaycastOver", root =>
            {
                for (int i = 0; i <= budget; i++) Node(root, "Img_" + i, typeof(Image));
            });
            string within = SavePrefab("Tmp_RaycastWithin", root =>
            {
                for (int i = 0; i < budget; i++) Node(root, "Img_" + i, typeof(Image));
            });

            var rule = new PerformanceRaycastTargetRule();
            var overIssues = rule.Check(ContextFor(over)).ToList();
            var withinIssues = rule.Check(ContextFor(within)).ToList();

            Assert.AreEqual(1, overIssues.Count(i => i.Severity == UiSeverity.Warning),
                "超预算须报一条 Warning：\n" + string.Join("\n", overIssues));
            Assert.AreEqual(UiRuleIds.PerformanceRaycastTarget, overIssues[0].RuleId);
            CollectionAssert.IsEmpty(withinIssues, "预算内零误报：\n" + string.Join("\n", withinIssues));
        }

        // ---- ui.performance.layout-depth ----

        [Test]
        public void 布局深度_嵌套链超限报Warning_限内零误报()
        {
            int limit = UiRuleThresholds.LayoutDepthLimit;

            string over = SavePrefab("Tmp_DepthOver", root =>
            {
                Transform current = root.transform;
                for (int i = 0; i <= limit; i++)
                    current = Node(current.gameObject, "Cot_" + i, typeof(VerticalLayoutGroup)).transform;
            });
            string within = SavePrefab("Tmp_DepthWithin", root =>
            {
                Transform current = root.transform;
                for (int i = 0; i < limit; i++)
                    current = Node(current.gameObject, "Cot_" + i, typeof(VerticalLayoutGroup)).transform;
            });

            var rule = new PerformanceLayoutDepthRule();
            Assert.AreEqual(1, rule.Check(ContextFor(over)).Count(i => i.Severity == UiSeverity.Warning),
                "嵌套链超限须报 Warning：\n" + string.Join("\n", rule.Check(ContextFor(over))));
            CollectionAssert.IsEmpty(rule.Check(ContextFor(within)).ToList(), "限内零误报");
        }

        // ---- ui.performance.animator ----

        [Test]
        public void 动画器_页面根挂Animator报Error_子树报Warning()
        {
            string rootBad = SavePrefab("Page_RootAnimator", root => root.AddComponent<Animator>());
            string subBad = SavePrefab("Page_SubAnimator", root =>
            {
                StandardScaler(root);
                Node(root, "Cot_Decor", typeof(Animator));
            });
            string clean = SavePrefab("Page_Clean", root => StandardScaler(root));

            var rule = new PerformanceAnimatorRule();
            Assert.AreEqual(1, rule.Check(ContextFor(rootBad)).Count(i => i.Severity == UiSeverity.Error),
                "页面根 Animator 违反 UI-PREFAB-03 须报 Error：\n" + string.Join("\n", rule.Check(ContextFor(rootBad))));
            Assert.AreEqual(1, rule.Check(ContextFor(subBad)).Count(i => i.Severity == UiSeverity.Warning),
                "子树 Animator 须报 Warning：\n" + string.Join("\n", rule.Check(ContextFor(subBad))));
            CollectionAssert.IsEmpty(rule.Check(ContextFor(clean)).ToList(), "零 Animator 页面零误报");
        }
    }
}
