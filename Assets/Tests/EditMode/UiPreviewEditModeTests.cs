using System;
using System.Linq;
using System.Reflection;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Preview;
using LiteGame.Toolchain;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UI 制作主线守卫（《UI编辑器工具专项设计》§8）：
    /// 预览场景存在且被收集规则排除、脚手架模板根契约齐备、页面名校验、Hub 入口收口、
    /// 工作台入口已退役（防回引）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UiPreviewEditModeTests
    {
        [Test]
        public void 预览场景_存在且在Editor目录被收集规则忽略()
        {
            StringAssert.StartsWith("Assets/LiteGame/Editor/", UiPagePreview.ScenePath, "预览场景必须住 Editor 目录");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(UiPagePreview.ScenePath), "预览场景资产存在");

            var infoType = Type.GetType("YooAsset.Editor.EditorAssetInfo, YooAsset.Editor");
            Assert.IsNotNull(infoType, "YooAsset.Editor 程序集在场（收集管线依赖）");
            var ruleType = typeof(LiteGame.Editor.LiteGameIgnoreRule);
            var rule = Activator.CreateInstance(ruleType);
            var method = ruleType.GetMethod("IsIgnoreAsset");
            Assert.IsNotNull(method, "忽略规则入口在册");

            object preview = Activator.CreateInstance(infoType, new object[] { UiPagePreview.ScenePath });
            Assert.IsTrue((bool)method.Invoke(rule, new[] { preview }), "预览场景不得进生产包");
            object product = Activator.CreateInstance(infoType, new object[] { "Assets/Scenes/Boot.unity" });
            Assert.IsFalse((bool)method.Invoke(rule, new[] { product }), "产品场景不得被误排除");
        }

        [Test]
        public void 脚手架模板_根契约齐备()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiPageScaffold.TemplatePath);
            Assert.IsNotNull(prefab, "脚手架模板存在：" + UiPageScaffold.TemplatePath);
            Assert.IsNotNull(prefab.GetComponent<Canvas>(), "根 Canvas");
            Assert.IsNotNull(prefab.GetComponent<CanvasGroup>(), "根 CanvasGroup");
            Assert.IsNotNull(prefab.GetComponent<GraphicRaycaster>(), "根 GraphicRaycaster");
            var scaler = prefab.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "根 CanvasScaler");
            Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution, "参考分辨率 1920×1080");
            Assert.AreEqual(0.5f, scaler.matchWidthOrHeight, "Match 0.5");
            Assert.IsNotNull(prefab.GetComponentInChildren<SafeAreaReceiver>(true), "Cot_SafeArea + SafeAreaReceiver");
        }

        [Test]
        public void 页面名校验_正负例()
        {
            Assert.IsTrue(UiPageScaffold.IsValidPageName("Page_Login"));
            Assert.IsTrue(UiPageScaffold.IsValidPageName("Page_Test2"));
            Assert.IsFalse(UiPageScaffold.IsValidPageName("Page_"));
            Assert.IsFalse(UiPageScaffold.IsValidPageName("Page_bad"));
            Assert.IsFalse(UiPageScaffold.IsValidPageName("Tmp_Page"));
            Assert.IsFalse(UiPageScaffold.IsValidPageName(null));
        }

        [Test]
        public void 工具清单_制作主线入口在册且旧窗口退役()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            Assert.IsNotNull(catalog.Find("ui.page-new"), "新建页向导在册");
            Assert.AreEqual(ToolchainKinds.Window, catalog.Find("ui.page-new").Kind);
            Assert.IsNotNull(catalog.Find("ui.compose"), "UI 制作窗口在册");
            Assert.AreEqual("UI 制作", catalog.Find("ui.compose").Title);
            Assert.IsNull(catalog.Find("ui.binding"), "独立绑定窗口已退役");
            Assert.IsNull(catalog.Find("ui.writeback"), "独立回写窗口已退役");
        }

        [Test]
        public void 工作台入口_已退役()
        {
            Assert.IsNull(typeof(DevStartupConfig).GetField("WorkbenchMode",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), "启动开关已移除");
            Assert.IsNull(typeof(DevStartupConfig).GetField("WorkbenchModeEnabled",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), "静态镜像已移除");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/UiWorkbench.unity"), "工作台场景已删除");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LiteGame/DevTool/WorkbenchUI.prefab"), "工作台面板已删除");
        }

        [Test]
        public void 开发页_已退役()
        {
            Assert.IsNull(typeof(DevStartupConfig).GetField("DevMenu",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), "开发页启动开关已移除");
            Assert.IsNull(typeof(DevStartupConfig).GetField("DevMenuEnabled",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), "开发页静态镜像已移除");
            Assert.IsNull(typeof(DevStartupConfig).Assembly.GetType("LiteGame.DevMenuLauncher"), "开发页引导已移除");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/LiteGame/DevTool/DevMenuPanel.cs"), "开发页面板脚本已删除");
            Assert.IsNull(Type.GetType("LiteGame.DevMenuPanel, LiteGame.DevTool"), "开发页面板类型已移除");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LiteGame/DevTool/DevMenuUI.prefab"), "开发页预制体已删除");
        }
    }
}
