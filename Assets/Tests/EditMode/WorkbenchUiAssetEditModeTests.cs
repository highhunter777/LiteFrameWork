using System;
using System.Reflection;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 工作台 UI 资产守卫（W2 追加：面板抽 dev 预制体）：
    /// ①面板预制体存在且**驱动 20 字段引用齐备**（W0 曾因 `RectTransform` 赋 `GameObject` 字段被 Unity
    /// 静默置空而断线——本守卫把这类缺陷机检化）；②路径单源（`DebugTuner.WorkbenchPrefabPath` ↔ 真实资产，
    /// 构建器同引用）；③工作台场景**不含面板**（面板资产单源在预制体，场景/ Boot 两入口同走自建）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class WorkbenchUiAssetEditModeTests
    {
        private static Type DriverType => Type.GetType("LiteGame.UIWorkbenchDriver, LiteGame.DevTool");
        private static Type TunerType => Type.GetType("LiteGame.DebugTuner, LiteGame.App");

        [Test]
        public void 面板预制体_存在且驱动引用齐备()
        {
            var tunerType = TunerType;
            Assert.IsNotNull(tunerType, "DebugTuner 类型可解析（改名/移件则同步更新本守卫）");
            string path = (string)tunerType.GetField("WorkbenchPrefabPath", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            Assert.IsFalse(string.IsNullOrEmpty(path), "预制体路径常量在册");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"面板预制体必须存在:{path}");

            var driverType = DriverType;
            Assert.IsNotNull(driverType, "驱动类型可解析");
            var driver = prefab.GetComponent(driverType);
            Assert.IsNotNull(driver, "预制体根必须挂 UIWorkbenchDriver（自建入口靠它）");

            var so = new SerializedObject(driver);
            var iterator = so.GetIterator();
            int refs = 0;
            var missing = new System.Collections.Generic.List<string>();
            for (bool enter = true; iterator.NextVisible(enter); enter = false)
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (iterator.propertyPath == "m_Script") continue;
                refs++;
                if (iterator.objectReferenceValue == null) missing.Add(iterator.propertyPath);
            }
            Assert.Greater(refs, 15, "驱动接线字段数在册（面板 9 + 工具条 11）");
            CollectionAssert.IsEmpty(missing,
                "面板预制体的驱动引用不得为空（W0 断线根因：类型不符赋值被静默置空——重跑 AgentScripts/BuildWorkbenchPanel.cs）");
        }

        [Test]
        public void 单源守卫_工作台场景不含面板件()
        {
            var driverType = DriverType;
            Assert.IsNotNull(driverType, "驱动类型可解析");
            string driverGuid = AssetDatabase.AssetPathToGUID("Assets/LiteGame/DevTool/UIWorkbenchDriver.cs");
            Assert.IsFalse(string.IsNullOrEmpty(driverGuid), "驱动脚本 GUID 可解析");

            string scenePath = "Assets/Scenes/UiWorkbench.unity";
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath), "工作台场景存在");
            string sceneText = System.IO.File.ReadAllText(scenePath);
            Assert.IsFalse(sceneText.Contains(driverGuid),
                "工作台场景不得内嵌面板（面板资产单源在 dev 预制体——场景/Boot 两入口同走 DebugTuner 自建）");
        }
    }
}
