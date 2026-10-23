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

        // ---- 结构族扩展：HUD 根与 Tmp_ 系统面（制作规范 §3.1/§3.3 对齐实况口径） ----

        [Test]
        public void HUD结构_缺根组件报Error_无射线合法()
        {
            SavePrefab("Hud_Good", root =>
            {
                root.AddComponent<Canvas>();
                root.AddComponent<CanvasScaler>();
                root.AddComponent<CanvasGroup>();
                // 无 GraphicRaycaster＝合法（按需；子 Canvas 模式与根无射线都是已裁决形态）
            });
            var good = Check(new PageStructureRule(), ContextFor(PrefabPath("Hud_Good")));
            CollectionAssert.IsEmpty(good, "HUD 根无射线合法（射线按需，不要求 SafeArea）：\n" +
                string.Join("\n", good.Select(i => i.ToString())));

            SavePrefab("Hud_Bad", root =>
            {
                root.AddComponent<Canvas>();
                root.AddComponent<CanvasGroup>();
                // 缺 CanvasScaler
            });
            var bad = Check(new PageStructureRule(), ContextFor(PrefabPath("Hud_Bad")));
            Assert.AreEqual(1, bad.Count(i => i.Severity == UiSeverity.Error), "HUD 根缺缩放器须报 Error：\n" +
                string.Join("\n", bad.Select(i => i.ToString())));
            Assert.AreEqual(UiRuleIds.PageStructure, bad[0].RuleId);
        }

        [Test]
        public void Tmp系统面结构_CanvasGroup必备_缺则Error_带缩放器报Error_无射线合法()
        {
            // 系统面按 Screens 路径段判定——合成目录带同名子目录（真实面＝Screens/Tmp_* 系统页）
            string screensDir = TempDir + "/Screens";
            EnsureFolder(screensDir);
            string SaveInScreens(string name, System.Action<GameObject> build)
            {
                var root = new GameObject(name, typeof(RectTransform));
                build(root);
                string path = screensDir + "/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                return path;
            }

            string good = SaveInScreens("Tmp_SysGood", root =>
            {
                root.AddComponent<Canvas>();
                root.AddComponent<CanvasGroup>();
                // 无 CanvasScaler＝合法（继承缩放）；无射线＝合法（按需）
            });
            var goodIssues = Check(new PageStructureRule(), ContextFor(good));
            CollectionAssert.IsEmpty(goodIssues, "系统面基线形态（Canvas+CanvasGroup+无缩放器）零误报：\n" +
                string.Join("\n", goodIssues.Select(i => i.ToString())));

            string noGroup = SaveInScreens("Tmp_SysNoGroup", root => root.AddComponent<Canvas>());
            var noGroupIssues = Check(new PageStructureRule(), ContextFor(noGroup));
            Assert.AreEqual(1, noGroupIssues.Count(i => i.Severity == UiSeverity.Error), "缺 CanvasGroup 须报 Error：\n" +
                string.Join("\n", noGroupIssues.Select(i => i.ToString())));

            string scaler = SaveInScreens("Tmp_SysScaler", root =>
            {
                root.AddComponent<Canvas>();
                root.AddComponent<CanvasGroup>();
                root.AddComponent<CanvasScaler>();       // 系统面禁自带缩放器（UI-LAYOUT-01）
            });
            var scalerIssues = Check(new PageStructureRule(), ContextFor(scaler));
            Assert.AreEqual(1, scalerIssues.Count(i => i.Severity == UiSeverity.Error), "系统面自带缩放器须报 Error：\n" +
                string.Join("\n", scalerIssues.Select(i => i.ToString())));
            StringAssert.Contains("缩放", scalerIssues[0].Message, "报文说明继承缩放口径");

            // Widgets 形态的 Tmp_（无 Screens 路径段）不辖——控件模板归 WidgetSpecRule/词表域
            SavePrefab("Tmp_Widget", root => { });
            var widgetIssues = Check(new PageStructureRule(), ContextFor(PrefabPath("Tmp_Widget")));
            CollectionAssert.IsEmpty(widgetIssues, "控件模板（Widgets 形态）本规则不辖：\n" +
                string.Join("\n", widgetIssues.Select(i => i.ToString())));
        }
    }
}
