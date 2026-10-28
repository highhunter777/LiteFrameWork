using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Compose;
using LiteGame.Editor.UiTools.Validation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UI 编排面板（《UI编辑器工具专项设计》§5）的**逻辑面**覆盖：目标解析单源
    /// （<see cref="UiComposeTarget"/>：Stage 活体 / 资产只读两态）、窄档校验路径（资产面上下文 →
    /// 报告可定位）、报告的行反查定位、"未接入族显式列出"契约。
    /// 窗口与分区绘制面属手测（与既有工具窗口同口径——纯 IMGUI/Odin 绘制不做自动化断言）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiComposePanelEditModeTests
    {
        private const string TempDir = "Assets/TempUiComposePanelTests";

        [SetUp]
        public void EnsureTempDir()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "TempUiComposePanelTests");
        }

        [TearDown]
        public void CleanupTempDir()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        /// <summary>合成源根（表目录缺席 → 表路径规则跳过，测试不与真实表面对账耦合）。</summary>
        private static string SyntheticRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "ui-compose-root-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string MakePrefab(string name, params string[] children)
        {
            var root = new GameObject(name, typeof(RectTransform));
            foreach (string child in children)
            {
                var go = new GameObject(child, typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
            }
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        // ---- 目标解析（§5：Stage 活体 / 资产只读） ----

        [Test]
        public void 目标解析_非prefab资产返回空()
        {
            var loose = new GameObject("LooseObject");
            try
            {
                Assert.IsNull(UiComposeTarget.Resolve(loose), "非 prefab 资产不构成编排目标");
            }
            finally
            {
                Object.DestroyImmediate(loose);
            }
            Assert.IsNull(UiComposeTarget.Resolve(null));
        }

        [Test]
        public void 目标解析_未开Stage时展示根为资产本身()
        {
            string path = MakePrefab("Page_ComposeTarget", "Txt_Title");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var target = UiComposeTarget.Resolve(asset);

            Assert.IsNotNull(target);
            Assert.AreEqual(path, target.PrefabPath);
            Assert.IsFalse(target.ViaStage, "未开 Prefab Stage → 资产只读态");
            Assert.AreSame(asset, target.DisplayRoot);
        }

        // ---- 窄档校验：资产面上下文，报告可定位 ----

        [Test]
        public void 窄档_本prefab违规并可按报告节点反查定位()
        {
            string path = MakePrefab("Page_ComposeNarrow", "BtnClose");   // 无下划线 → §9.2 文法违规
            var context = new UiValidationContext(SyntheticRoot(), new string[0], new[] { path });

            var report = UiValidator.Run(context);

            var hit = report.Issues.FirstOrDefault(i => i.RuleId == "ui.binding-naming"
                                                        && i.Node != null && i.Node.Contains("BtnClose"));
            Assert.IsNotNull(hit, "窄档须报出本 prefab 的命名违规");
            Assert.AreEqual(UiSeverity.Error, hit.Severity);
            Assert.IsNotEmpty(hit.FixHint, "fixHint 必给（§4）");

            var target = UiComposeTarget.Resolve(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            Assert.IsNull(UiBindingRename.FindByPath(target.DisplayRoot, hit.Node),
                "报告 node 口径含根名——树口径不认（反查须经 FindNodeForReport）");
            Assert.IsNotNull(UiComposeTarget.FindNodeForReport(target.DisplayRoot, hit.Node),
                "报告节点须可反查定位（面板「定位节点」契约）");
        }

        [Test]
        public void 反查_缺失路径返回空不抛()
        {
            string path = MakePrefab("Page_ComposeLookup", "Txt_Title");
            var target = UiComposeTarget.Resolve(AssetDatabase.LoadAssetAtPath<GameObject>(path));

            Assert.IsNull(UiBindingRename.FindByPath(target.DisplayRoot, "Txt_NotThere"), "缺失路径 → null（不抛）");
        }

        [Test]
        public void 报告_未接入族显式列出不冒充通过()
        {
            string path = MakePrefab("Page_ComposePending", "Txt_Title");
            var report = UiValidator.Run(new UiValidationContext(SyntheticRoot(), new string[0], new[] { path }));

            // §4：报告必须带**族清单**（含 Implemented 标志）——未接入的族才可能被显式列出。
            // 全部族都已接入时"未接入集合为空"是合法结论，不能反过来要求恒非空。
            Assert.IsNotEmpty(report.Families, "族清单必须进报告（§4：未接入族靠它显式列出）");
            foreach (var family in report.Families)
                Assert.IsFalse(string.IsNullOrEmpty(family.Family), "族名不可为空");
        }
    }
}
