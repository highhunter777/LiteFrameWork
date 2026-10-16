using System;
using System.Reflection;
using LiteGame.Editor.UiTools.WriteBack;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// prefab 回写工具（《UI工作台设计》§2「回写」，W2 交付）的逻辑面覆盖：
    /// 差异枚举（显式记录 / 数值差异分列——前者默认勾选、后者默认不勾）、逐属性写入资产、
    /// 不可回写面显式列出、非实例选择显式失败。
    /// 另含两条**单源守卫**：驱动件菜单路径 == 窗口常量（两程序集无引用关系——只能守卫）；
    /// dev 资产排除名单（每条路径存在 + 规则真的忽略 + 产品场景不误伤）。
    ///
    /// 来源判据经**实测校准**（探针证据见施工记录）：Image 等组件的记录式改动产生非默认记录项（可认）；
    /// RectTransform 的位置/尺寸/锚点路径在实例创建时即登记为默认项、此后恒标默认 ⇒ 只能归"数值差异"
    /// 由人工核对——脚本/驱动写面同样落这一侧（设计"驱动写面天然屏蔽"由此保持）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class PrefabWriteBackEditModeTests
    {
        private const string TempDir = "Assets/TempPrefabWriteBackTests";
        private const string WorkbenchScenePath = "Assets/Scenes/UiWorkbench.unity";

        [SetUp]
        public void EnsureTempDir()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "TempPrefabWriteBackTests");
        }

        [TearDown]
        public void CleanupTempDir()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        private static string MakePrefab(string name, params Type[] components)
        {
            var types = new Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            Array.Copy(components, 0, types, 1, components.Length);
            var root = new GameObject(name, types);
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return path;
        }

        /// <summary>夹具：实例上做两处改动——①Image 颜色走"记录改动"（Unity 记成非默认项）；
        /// ②RectTransform 尺寸只写值（实测 Unity 对该路径恒标默认记录 ⇒ 归"数值差异"侧）。</summary>
        private static GameObject MakeModifiedInstance(string name, out string path)
        {
            path = MakePrefab(name, typeof(Image));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var rectSo = new SerializedObject(instance.GetComponent<RectTransform>());
            rectSo.FindProperty("m_SizeDelta").vector2Value = new Vector2(123f, 45f);
            rectSo.ApplyModifiedProperties();
            var image = instance.GetComponent<Image>();
            var imageSo = new SerializedObject(image);
            imageSo.FindProperty("m_Color").colorValue = Color.red;
            imageSo.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);      // Inspector 口径的记录
            return instance;
        }

        private static PrefabWriteBackItem Find(PrefabWriteBackScanResult scan, string propertyPath)
            => scan.Items.Find(i => i.PropertyPath == propertyPath);

        [Test]
        public void 回写_显式记录默认勾选_数值差异默认不勾()
        {
            var instance = MakeModifiedInstance("Tmp_WriteBackOrigin", out _);
            try
            {
                var scan = PrefabWriteBackScan.Scan(instance);
                Assert.IsNull(scan.Error, scan.Error);
                var colorItem = Find(scan, "m_Color");
                var sizeItem = Find(scan, "m_SizeDelta");
                Assert.IsNotNull(colorItem, "颜色差异在列（组件属性）");
                Assert.IsNotNull(sizeItem, "尺寸差异在列（结构体内属性）");
                Assert.AreEqual(PrefabWriteBackOrigin.RecordedOverride, colorItem.Origin,
                    "Unity 记录在案的改动 = 显式记录（默认勾选）");
                Assert.IsTrue(colorItem.Selected, "显式记录默认勾选");
                Assert.AreEqual(PrefabWriteBackOrigin.RuntimeDiff, sizeItem.Origin,
                    "RectTransform 路径恒标默认记录 ⇒ 归数值差异（实测校准：Inspector 改动认不出）");
                Assert.IsFalse(sizeItem.Selected, "数值差异默认不勾——须核对「实例值 → 资产值」");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void 回写_未勾选不落盘_勾选后固化并复扫归零()
        {
            var instance = MakeModifiedInstance("Tmp_WriteBackApply", out string path);
            try
            {
                var scan = PrefabWriteBackScan.Scan(instance);
                var sizeItem = Find(scan, "m_SizeDelta");
                var colorItem = Find(scan, "m_Color");
                Assert.IsNotNull(sizeItem, "夹具差异在列");
                Assert.IsNotNull(colorItem, "夹具差异在列");
                Vector2 assetSize = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                    .GetComponent<RectTransform>().sizeDelta;                        // 资产原尺寸（未改动前）

                // ①未勾选项不落盘：数值差异那条取消勾选，只写显式记录项
                sizeItem.Selected = false;
                var first = PrefabWriteBackApply.Apply(scan.Items);
                Assert.IsNull(first.Error, first.Error);
                Assert.AreEqual(1, first.Applied, string.Join(" | ", first.Failures));
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.AreEqual(Color.red, saved.GetComponent<Image>().color, "显式记录项已固化");
                Assert.AreEqual(assetSize, saved.GetComponent<RectTransform>().sizeDelta,
                    "未勾选的数值差异没有落盘（资产保持原尺寸）");

                // ②复扫：已固化项不再列为差异；未固化项仍在列——勾选后可固化（数值差异只是默认不勾，不是不可写）
                var rescanAfterFirst = PrefabWriteBackScan.Scan(instance);
                Assert.IsNull(Find(rescanAfterFirst, "m_Color"), "显式记录项固化后即归零");
                var sizeAgain = Find(rescanAfterFirst, "m_SizeDelta");
                Assert.IsNotNull(sizeAgain, "未固化的数值差异仍在列");
                sizeAgain.Selected = true;
                var second = PrefabWriteBackApply.Apply(rescanAfterFirst.Items);
                Assert.AreEqual(1, second.Applied, string.Join(" | ", second.Failures));
                saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.AreEqual(new Vector2(123f, 45f), saved.GetComponent<RectTransform>().sizeDelta,
                    "勾选后数值差异固化生效");

                // ③复扫：等值即无差异
                var rescan = PrefabWriteBackScan.Scan(instance);
                Assert.IsNull(Find(rescan, "m_SizeDelta"), "固化后等值——不再列为差异");
                Assert.IsNull(Find(rescan, "m_Color"), "固化后等值——不再列为差异");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void 回写_实例新增组件显式不可回写()
        {
            string path = MakePrefab("Tmp_WriteBackAdded", typeof(Image));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                var added = instance.AddComponent(typeof(Slider));            // 源 prefab 无此组件——结构面
                Assert.IsNotNull(added, "夹具：实例上新增组件成功");
                Assert.IsTrue(PrefabUtility.IsAddedComponentOverride(added));

                var scan = PrefabWriteBackScan.Scan(instance);
                Assert.IsNull(scan.Error, scan.Error);
                var item = scan.Items.Find(i => i.Block == PrefabWriteBackBlock.AddedComponent
                    && i.OwnerLabel.Contains(nameof(Slider)));
                Assert.IsNotNull(item, "实例新增组件显式列出（不静默跳过）");
                Assert.IsFalse(item.CanApply, "新增组件不可勾选落地");

                PrefabWriteBackApply.Apply(scan.Items);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Slider>(),
                    "新增组件没有落盘（结构面归 Prefab Stage / Unity Overrides）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void 回写_场景引用显式不可回写()
        {
            string path = MakePrefab("Tmp_WriteBackSceneRef", typeof(Image), typeof(Button));
            string otherPath = MakePrefab("Tmp_WriteBackSceneRefOther", typeof(Image));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var other = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(otherPath));
            try
            {
                var originalGraphic = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Button>().targetGraphic;
                var buttonSo = new SerializedObject(instance.GetComponent<Button>());
                buttonSo.FindProperty("m_TargetGraphic").objectReferenceValue = other.GetComponent<Image>();
                buttonSo.ApplyModifiedProperties();

                var scan = PrefabWriteBackScan.Scan(instance);
                Assert.IsNull(scan.Error, scan.Error);
                var item = Find(scan, "m_TargetGraphic");
                Assert.IsNotNull(item, "场景引用差异在列");
                Assert.AreEqual(PrefabWriteBackBlock.SceneReference, item.Block, "指向场景对象的引用判为不可回写");
                Assert.IsFalse(item.CanApply, "不可回写项不可落地");
                Assert.IsFalse(item.Selected, "不可回写项不默认勾选");

                PrefabWriteBackApply.Apply(scan.Items);
                Assert.AreSame(originalGraphic,
                    AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Button>().targetGraphic,
                    "场景引用没有落盘（资产保持原值）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void 回写_非实例选择显式失败()
        {
            var plain = new GameObject("Tmp_WriteBackPlain", typeof(RectTransform));
            try
            {
                var scan = PrefabWriteBackScan.Scan(plain);
                Assert.IsNotNull(scan.Error, "非 prefab 实例选择必须显式失败（运行时副本无 prefab 关联）");
                Assert.AreEqual(0, scan.Items.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void 单源守卫_回写菜单路径与驱动件同值且菜单项在册()
        {
            var driverType = Type.GetType("LiteGame.UIWorkbenchDriver, LiteGame.DevHUD");
            Assert.IsNotNull(driverType, "驱动件类型必须可解析（改名/移件则同步更新本守卫）");
            var field = driverType.GetField("WriteBackMenuPath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "驱动件回写菜单路径常量必须在册");
            Assert.AreEqual(PrefabWriteBackWindow.MenuPath, (string)field.GetValue(null),
                "两处菜单路径必须同值——单源由本守卫钉住（红线 5：不得只靠注释保持一致）");

            var attribute = typeof(PrefabWriteBackWindow).GetMethod("Open", BindingFlags.Public | BindingFlags.Static)
                ?.GetCustomAttribute<MenuItem>();
            Assert.IsNotNull(attribute, "窗口菜单项在册");
            Assert.AreEqual(PrefabWriteBackWindow.MenuPath, attribute.menuItem, "菜单项路径 == 常量单源");
        }

        [Test]
        public void 单源守卫_dev资产排除名单存在且规则真的忽略()
        {
            var ruleType = typeof(LiteGame.Editor.LiteGameIgnoreRule);
            var field = ruleType.GetField("DevAssetExclusions", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "dev 资产排除名单必须在册");
            var exclusions = (string[])field.GetValue(null);
            Assert.IsNotNull(exclusions);
            Assert.Greater(exclusions.Length, 0, "名单非空");
            foreach (string excluded in exclusions)
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(excluded),
                    $"排除名单路径必须真实存在（改名/移动即红）:{excluded}");
            CollectionAssert.Contains(exclusions, WorkbenchScenePath, "工作台场景在排除名单");

            // 规则真的忽略它——测试程序集不引 YooAsset.Editor（既有先例：反射构造 EditorAssetInfo）
            var infoType = Type.GetType("YooAsset.Editor.EditorAssetInfo, YooAsset.Editor");
            Assert.IsNotNull(infoType, "YooAsset.Editor 程序集在场（收集管线依赖）");
            var rule = Activator.CreateInstance(ruleType);
            var method = ruleType.GetMethod("IsIgnoreAsset");
            Assert.IsNotNull(method, "忽略规则入口在册");
            object workbenchInfo = Activator.CreateInstance(infoType, new object[] { WorkbenchScenePath });
            Assert.IsTrue((bool)method.Invoke(rule, new[] { workbenchInfo }),
                "工作台场景被收集规则忽略（不入生产包）——发布剥离口径 W2");
            object productInfo = Activator.CreateInstance(infoType, new object[] { "Assets/Scenes/Boot.unity" });
            Assert.IsFalse((bool)method.Invoke(rule, new[] { productInfo }), "产品场景不得被误排除");
        }
    }
}
