using System.Linq;
using LiteGame.Editor.UiTools.Compose;
using LiteGame.Editor.UiTools.Validation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 控件库分区（§5"控件库"）的逻辑面覆盖：**目录即清单**（扫 Widgets 的 `Tmp_` 模板，不另存清单文件）、
    /// Screens 目录件不误收、模板名判据与使用段映射、插入前置守卫（只在 Prefab Stage 内可插）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiWidgetLibraryEditModeTests
    {
        private const string TempDir = "Assets/TempUiWidgetLibraryTests";

        [SetUp]
        public void EnsureTempDir()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "TempUiWidgetLibraryTests");
        }

        [TearDown]
        public void CleanupTempDir()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        // ---- 目录即清单（真实资产面） ----

        [Test]
        public void 真实资产_枚举模板且全为Tmp前缀()
        {
            var entries = UiWidgetLibrary.Load();
            var names = entries.Select(e => e.TemplateName).ToList();

            Assert.GreaterOrEqual(names.Count, 27, "控件模板不少于《命名词表与控件清单》§1.3 登记的 27 件");
            foreach (string name in names)
            {
                Assert.IsTrue(name.StartsWith("Tmp_"), "模板本体名须 Tmp_ 起头：" + name);
                Assert.IsTrue(UiWidgetLibrary.IsTemplateName(name));
            }
            CollectionAssert.Contains(names, "Tmp_HpBar");
            CollectionAssert.Contains(names, "Tmp_VirtualList");
        }

        [Test]
        public void 目录隔离_只收Widgets不收Screens()
        {
            var paths = UiWidgetLibrary.Load().Select(e => e.Path).ToList();

            foreach (string path in paths)
                Assert.IsFalse(path.StartsWith(UiValidator.ScreensDir), "控件库只收 Widgets 目录：" + path);
            CollectionAssert.DoesNotContain(paths, "Assets/ShareResource/UI/Screens/Tmp_Loading.prefab",
                "Screens 下的 Tmp_ 框架夹具不是控件模板");
        }

        // ---- 判据（纯函数） ----

        [Test]
        public void 模板名判据_前缀与文法()
        {
            Assert.IsTrue(UiWidgetLibrary.IsTemplateName("Tmp_Dialog"));
            Assert.IsFalse(UiWidgetLibrary.IsTemplateName("Page_Login"), "页面根不是控件模板");
            Assert.IsFalse(UiWidgetLibrary.IsTemplateName("Dialog_Confirm"), "业务使用段不是模板本体");
            Assert.IsFalse(UiWidgetLibrary.IsTemplateName("TmpDialog"), "缺下划线不过 §9.2 文法");
            Assert.IsFalse(UiWidgetLibrary.IsTemplateName(null));
            Assert.IsFalse(UiWidgetLibrary.IsTemplateName(string.Empty));
        }

        [Test]
        public void 使用段映射_类型段与结构工具件()
        {
            Assert.AreEqual("HpBar_", UiWidgetLibrary.UsageSegmentOf("Tmp_HpBar"));
            Assert.AreEqual("VirtualList_", UiWidgetLibrary.UsageSegmentOf("Tmp_VirtualList"));
            Assert.IsNull(UiWidgetLibrary.UsageSegmentOf("Tmp_SafeArea"), "结构工具件不占类型段");
            Assert.IsNull(UiWidgetLibrary.UsageSegmentOf("Tmp_Baseline"), "结构工具件不占类型段");
        }

        // ---- 插入守卫 ----

        [Test]
        public void 插入守卫_未绑定与未开Stage均不可插且给原因()
        {
            Assert.IsFalse(UiWidgetInstaller.CanInstall(string.Empty, out string unboundReason));
            Assert.IsNotEmpty(unboundReason, "未绑定目标须给原因");

            string path = TempDir + "/Page_WidgetInstall.prefab";
            var root = new GameObject("Page_WidgetInstall", typeof(RectTransform));
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Assert.IsFalse(UiWidgetInstaller.CanInstall(path, out string stageReason));
            Assert.IsNotEmpty(stageReason, "未开 Prefab Stage 须给原因（插入只在原生编辑态进行）");

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(UiWidgetLibrary.Load().First().Path);
            Assert.IsNull(UiWidgetInstaller.Install(template, null, path, out string installReason),
                "不可插入时返回 null（不静默半插）");
            Assert.IsNotEmpty(installReason);
        }

        [Test]
        public void 插入守卫_模板缺失返回空并给原因()
        {
            Assert.IsNull(UiWidgetInstaller.Install(null, null, "Assets/LiteFramework/Runtime/Modules/UI/Nope.prefab", out string reason));
            Assert.AreEqual("模板资产缺失", reason);
        }
    }
}
