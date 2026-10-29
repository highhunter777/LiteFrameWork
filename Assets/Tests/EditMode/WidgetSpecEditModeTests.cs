using System.Collections.Generic;
using LiteFramework.UI;
using LiteCodeGen;
using LiteGame.Editor.UiTools.Widgets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-2 控件规格作者面：WidgetSpec 默认声明、插槽发现（只收 `Cot_*`/`Slot_*`，排除 `Clone_`/
    /// 模板内部件/嵌套 `BindRoot` 子树）、发现结果幂等并入（保留作者填写）。
    /// </summary>
    public sealed class WidgetSpecEditModeTests
    {
        private const string TempDir = "Assets/TempWidgetSpecTests";
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object created in _created)
                if (created != null) Object.DestroyImmediate(created);
            _created.Clear();
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        private GameObject NewRoot()
        {
            var root = new GameObject("Tmp_Probe", typeof(RectTransform));
            _created.Add(root);
            return root;
        }

        private GameObject NewChild(Transform parent, string name, params System.Type[] extra)
        {
            var child = new GameObject(name, extra.Length == 0 ? new[] { typeof(RectTransform) } : extra);
            child.transform.SetParent(parent, false);
            _created.Add(child);
            return child;
        }

        [Test]
        public void 默认声明_空插槽空状态_固定尺寸()
        {
            WidgetSpec spec = NewRoot().AddComponent<WidgetSpec>();

            Assert.AreEqual("art.ui", spec.Owner, "默认归属＝模板视觉 owner");
            Assert.AreEqual(string.Empty, spec.Summary);
            Assert.AreEqual(0, spec.Slots.Length);
            Assert.AreEqual(0, spec.States.Length);
            Assert.AreEqual(0, spec.Events.Length);
            Assert.AreEqual(UiWidgetSizeBehavior.Fixed, spec.SizeBehavior);
            Assert.AreEqual(0, spec.Constraints.Length);
        }

        [Test]
        public void 发现插槽_只收Cot与Slot_排除克隆与嵌套根与模板内部()
        {
            GameObject root = NewRoot();
            NewChild(root.transform, "Cot_Header");
            NewChild(root.transform, "Slot_Content");
            NewChild(root.transform, "Txt_Title");
            GameObject clone = NewChild(root.transform, "Clone_Item");
            NewChild(clone.transform, "Cot_Clone");
            GameObject nestedRoot = NewChild(root.transform, "GenRoot");
            nestedRoot.AddComponent<BindRoot>().ScriptName = "GenRoot";
            NewChild(nestedRoot.transform, "Cot_Gen");

            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "TempWidgetSpecTests");
            GameObject inner = new GameObject("Tmp_Inner", typeof(RectTransform));
            var innerSlot = new GameObject("Cot_Inner", typeof(RectTransform));
            innerSlot.transform.SetParent(inner.transform, false);
            PrefabUtility.SaveAsPrefabAsset(inner, TempDir + "/Tmp_Inner.prefab");
            Object.DestroyImmediate(inner);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TempDir + "/Tmp_Inner.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(template);
            instance.transform.SetParent(root.transform, false);
            _created.Add(instance);

            List<WidgetSlotSpec> slots = WidgetSpecAuthoring.DiscoverSlots(root);

            var names = new List<string>();
            foreach (WidgetSlotSpec slot in slots) names.Add(slot.Name);
            CollectionAssert.AreEquivalent(new[] { "Cot_Header", "Slot_Content" }, names,
                "只收 Cot_*/Slot_*；Clone_ 子树、嵌套 BindRoot 子树、模板内部件都不是本模板插槽");
        }

        [Test]
        public void 发现结果并入_幂等_保留作者填写()
        {
            GameObject root = NewRoot();
            NewChild(root.transform, "Cot_Header");
            NewChild(root.transform, "Slot_Content");
            WidgetSpec spec = root.AddComponent<WidgetSpec>();
            spec.Slots = new[]
            {
                new WidgetSlotSpec { Name = "Cot_Header", Required = true, Description = "标题区" },
            };

            int added = WidgetSpecAuthoring.ApplyDiscoveredSlots(spec, root);
            Assert.AreEqual(1, added, "只补缺失的 Slot_Content");
            Assert.AreEqual(2, spec.Slots.Length);
            Assert.AreEqual("Cot_Header", spec.Slots[0].Name);
            Assert.AreEqual("标题区", spec.Slots[0].Description, "已有条目保留作者填写");

            int again = WidgetSpecAuthoring.ApplyDiscoveredSlots(spec, root);
            Assert.AreEqual(0, again, "重复并入必须幂等");
            Assert.AreEqual(2, spec.Slots.Length);
        }
    }
}
