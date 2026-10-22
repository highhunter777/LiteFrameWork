using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using LiteGame.Editor.UiTools.Widgets;
using LiteClient.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-4 四条规则（结构/缺 WidgetSpec/控件清单漂移/页面清单漂移）与报告确定性：
    /// 正例与违规负例、真实资产面零 Error；清单规则只在显式资产面执行（合成上下文不触发）。
    /// </summary>
    public sealed class UiValidationRulesEditModeTests
    {
        private const string TempDir = "Assets/TempUiValidationTests";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempDir);
        }

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

        private static string PrefabPath(string name) => TempDir + "/" + name + ".prefab";

        private static GameObject SavePrefab(string name, System.Action<GameObject> build)
        {
            var root = new GameObject(name, typeof(RectTransform));
            build(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name));
        }

        private static void AddChild(GameObject parent, string name, params System.Type[] components)
        {
            var all = new List<System.Type> { typeof(RectTransform) };
            all.AddRange(components);
            var child = new GameObject(name, all.ToArray());
            child.transform.SetParent(parent.transform, false);
        }

        private static UiValidationContext ContextFor(params string[] assets) =>
            new UiValidationContext(UiValidator.ProjectRoot, null, assets);

        private static List<UiIssue> Check(IUiRule rule, UiValidationContext context) =>
            rule.Check(context).ToList();

        [Test]
        public void 页面结构_完整根契约通过()
        {
            SavePrefab("Page_Good", root =>
            {
                root.AddComponent<Canvas>();
                root.AddComponent<CanvasScaler>();
                root.AddComponent<GraphicRaycaster>();
                root.AddComponent<CanvasGroup>();
                AddChild(root, "SafeArea", typeof(SafeAreaReceiver));
            });

            var issues = Check(new PageStructureRule(), ContextFor(PrefabPath("Page_Good")));

            CollectionAssert.IsEmpty(issues, "完整根契约不应报问题：" +
                string.Join("\n", issues.Select(i => i.ToString())));
        }

        [Test]
        public void 页面结构_缺组件与安全区报Error()
        {
            SavePrefab("Page_Bad", root => { });

            var issues = Check(new PageStructureRule(), ContextFor(PrefabPath("Page_Bad")));

            var errors = issues.Where(i => i.Severity == UiSeverity.Error).ToList();
            Assert.AreEqual(2, errors.Count, "一为根组件缺失、一为 SafeAreaReceiver 缺失：" +
                string.Join("\n", issues.Select(i => i.ToString())));
            StringAssert.Contains("Canvas", errors[0].Message);
            StringAssert.Contains("SafeAreaReceiver", errors[1].Message);
            Assert.IsTrue(errors.All(i => i.RuleId == UiRuleIds.PageStructure));
            Assert.IsTrue(errors.All(i => !string.IsNullOrEmpty(i.FixHint)));
        }

        [Test]
        public void 控件规格_缺spec报Error_有spec通过()
        {
            SavePrefab("Tmp_NoSpec", root => { });
            var missing = Check(new WidgetSpecRule(), ContextFor(PrefabPath("Tmp_NoSpec")));
            Assert.AreEqual(1, missing.Count(i => i.Severity == UiSeverity.Error));
            Assert.AreEqual(UiRuleIds.WidgetSpec, missing[0].RuleId);

            SavePrefab("Tmp_WithSpec", root =>
            {
                var spec = root.AddComponent<WidgetSpec>();
                spec.Summary = "测试控件";
            });
            var ok = Check(new WidgetSpecRule(), ContextFor(PrefabPath("Tmp_WithSpec")));
            CollectionAssert.IsEmpty(ok, "有 WidgetSpec 的模板不应报错");
        }

        [Test]
        public void 控件清单_未生成报Info()
        {
            SavePrefab("Tmp_NoManifest", root =>
            {
                var spec = root.AddComponent<WidgetSpec>();
                spec.Summary = "测试控件";
            });
            string manifest = TempDir + "/no-manifest.json";

            var issues = Check(new WidgetManifestRule(), new UiValidationContext(UiValidator.ProjectRoot, null,
                new[] { PrefabPath("Tmp_NoManifest") }, widgetManifestPath: manifest));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(UiSeverity.Info, issues[0].Severity, "缺失清单是提示不是阻断");
            StringAssert.Contains("未生成", issues[0].Message);
        }

        [Test]
        public void 控件清单_漂移报Error()
        {
            SavePrefab("Tmp_Drift", root =>
            {
                var spec = root.AddComponent<WidgetSpec>();
                spec.Summary = "旧摘要";
            });
            string manifestPath = TempDir + "/widget-manifest.json";
            var generated = UiWidgetManifestGenerator.Generate(new[] { PrefabPath("Tmp_Drift") });
            Assert.IsTrue(generated.Ok, generated.ErrorText);
            UiWidgetManifestStore.Save(generated.Manifest, manifestPath);

            var same = Check(new WidgetManifestRule(), new UiValidationContext(UiValidator.ProjectRoot, null,
                new[] { PrefabPath("Tmp_Drift") }, widgetManifestPath: manifestPath));
            CollectionAssert.IsEmpty(same, "清单与模板一致时不应报问题：" +
                string.Join("\n", same.Select(i => i.ToString())));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("Tmp_Drift"));
            prefab.GetComponent<WidgetSpec>().Summary = "新摘要";
            PrefabUtility.SavePrefabAsset(prefab);

            var drift = Check(new WidgetManifestRule(), new UiValidationContext(UiValidator.ProjectRoot, null,
                new[] { PrefabPath("Tmp_Drift") }, widgetManifestPath: manifestPath));
            Assert.AreEqual(1, drift.Count(i => i.Severity == UiSeverity.Error), "模板改了清单没重跑＝漂移");
            StringAssert.Contains("漂移", drift[0].Message);
        }

        [Test]
        public void 页面清单_缺失报Warning_漂移报Error()
        {
            const string pageAsset = "Assets/ShareResource/UI/Screens/Page_Login.prefab";
            string missingPath = TempDir + "/missing-page-manifest.json";
            var missing = Check(new PageManifestRule(), new UiValidationContext(UiValidator.ProjectRoot, null,
                new[] { pageAsset }, pageManifestPath: missingPath));
            Assert.AreEqual(1, missing.Count(i => i.Severity == UiSeverity.Warning));
            StringAssert.Contains("缺失", missing[0].Message);

            string driftPath = TempDir + "/stale-page-manifest.json";
            File.WriteAllText(Path.Combine(UiValidator.ProjectRoot, driftPath),
                "{\n  \"schemaVersion\": \"1.0\",\n  \"generator\": { \"name\": \"ui-page-manifest\", \"version\": \"1.0\" },\n" +
                "  \"pages\": [],\n  \"warnings\": [],\n  \"sourceHash\": \"sha256:" + new string('0', 64) + "\"\n}\n");
            AssetDatabase.ImportAsset(driftPath);

            var drift = Check(new PageManifestRule(), new UiValidationContext(UiValidator.ProjectRoot, null,
                new[] { pageAsset }, pageManifestPath: driftPath));
            Assert.AreEqual(1, drift.Count(i => i.Severity == UiSeverity.Error), "清单与表不同步＝漂移：" +
                string.Join("\n", drift.Select(i => i.ToString())));
            StringAssert.Contains("漂移", drift[0].Message);
        }

        [Test]
        public void 真实资产面_新规则零Error()
        {
            var context = new UiValidationContext(UiValidator.ProjectRoot, null,
                UiValidator.BuildDefaultContext().AssetPaths);
            var report = UiValidator.Run(context);

            var newRules = new[]
            {
                UiRuleIds.PageStructure, UiRuleIds.WidgetSpec, UiRuleIds.WidgetManifest, UiRuleIds.PageManifest,
            };
            var errors = report.Issues.Where(i => i.Severity == UiSeverity.Error && newRules.Contains(i.RuleId)).ToList();
            CollectionAssert.IsEmpty(errors, "真实资产面不得有 Error：\n" + string.Join("\n", errors.Select(i => i.ToString())));
            Assert.AreEqual(0, report.Issues.Count(i => i.RuleId == UiRuleIds.PageManifest && i.Severity != UiSeverity.Info),
                "页面清单基线应已同步：" + report.Render());
        }

        [Test]
        public void 报告_稳定排序()
        {
            var report = new UiReport();
            report.Add(new UiIssue { RuleId = "ui.b", Asset = "Assets/B.prefab", Severity = UiSeverity.Warning });
            report.Add(new UiIssue { RuleId = "ui.a", Asset = "Assets/Z.prefab", Severity = UiSeverity.Warning });
            report.Add(new UiIssue { RuleId = "ui.a", Asset = "Assets/A.prefab", Severity = UiSeverity.Warning });

            report.SortIssues();

            Assert.AreEqual("ui.a", report.Issues[0].RuleId);
            Assert.AreEqual("Assets/A.prefab", report.Issues[0].Asset);
            Assert.AreEqual("Assets/Z.prefab", report.Issues[1].Asset);
            Assert.AreEqual("ui.b", report.Issues[2].RuleId);
        }
    }
}
