using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Widgets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-2 控件清单：从 `Tmp_*` 模板 + WidgetSpec + 受控属性 + 绑定 hash 汇总确定性清单；
    /// 缺 spec 只告警，声明不一致（插槽/事件/约束）为错误且不写盘。
    /// </summary>
    public sealed class UiWidgetManifestEditModeTests
    {
        private const string WidgetsDir = "Assets/ShareResource/UI/Widgets";
        private const string DirA = WidgetsDir + "/TempUiWidgetManifestA";
        private const string DirB = WidgetsDir + "/TempUiWidgetManifestB";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(DirA);
            EnsureFolder(DirB);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(DirA)) AssetDatabase.DeleteAsset(DirA);
            if (AssetDatabase.IsValidFolder(DirB)) AssetDatabase.DeleteAsset(DirB);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static GameObject NewTemplate(string dir, string summary = "步进器", string[] events = null,
            bool withSpec = true, string[] slots = null, UiWidgetState[] states = null,
            UiWidgetSizeBehavior size = UiWidgetSizeBehavior.Fixed, string[] constraints = null)
        {
            var root = new GameObject("Tmp_Stepper", typeof(RectTransform));
            root.AddComponent<Stepper>();
            var slot = new GameObject("Cot_Content", typeof(RectTransform));
            slot.transform.SetParent(root.transform, false);

            if (withSpec)
            {
                var spec = root.AddComponent<WidgetSpec>();
                spec.Owner = "art.ui";
                spec.Summary = summary;
                spec.Slots = (slots ?? new[] { "Cot_Content" })
                    .Select(x => new WidgetSlotSpec { Name = x, Required = true, Description = "内容区" })
                    .ToArray();
                spec.States = states ?? new[] { UiWidgetState.Normal, UiWidgetState.Disabled };
                spec.Events = events ?? new[] { "Stepper.OnChanged" };
                spec.SizeBehavior = size;
                spec.Constraints = constraints ?? new[] { "范围由字段上下限决定" };
            }

            string path = dir + "/Tmp_Stepper.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static string PathOf(GameObject prefab) => AssetDatabase.GetAssetPath(prefab);

        [Test]
        public void 清单_汇总声明与属性与hash()
        {
            GameObject prefab = NewTemplate(DirA);

            var result = UiWidgetManifestGenerator.Generate(new[] { PathOf(prefab) });

            Assert.IsTrue(result.Ok, result.ErrorText);
            UiWidgetManifestEntry entry = result.Manifest.Widgets.Single();
            Assert.AreEqual("Tmp_Stepper", entry.Template);
            Assert.AreEqual("Stepper_", entry.UsageSegment, "使用段来自词表模板段");
            Assert.IsTrue(entry.SpecPresent);
            Assert.AreEqual("art.ui", entry.Owner);
            Assert.AreEqual("步进器", entry.Summary);
            Assert.AreEqual(1, entry.Slots.Count);
            Assert.AreEqual("Cot_Content", entry.Slots[0].Name);
            Assert.IsTrue(entry.Slots[0].Required);
            CollectionAssert.AreEqual(new[] { "Normal", "Disabled" }, entry.States.ToArray(), "状态按枚举序稳定排序");
            CollectionAssert.AreEqual(new[] { "Stepper.OnChanged" }, entry.Events.ToArray());
            Assert.AreEqual("Fixed", entry.SizeBehavior);
            CollectionAssert.AreEqual(new[] { "范围由字段上下限决定" }, entry.Constraints.ToArray());

            Assert.AreEqual(3, entry.Properties.Count, "受控属性来自 [UiWidgetProperty] 白名单");
            Assert.IsTrue(entry.Properties.Any(x => x.Member == "Min" && x.Kind == "Int"));
            Assert.AreEqual(64, entry.BindingHash.Length, "模板 bindingHash 为 64 位十六进制");
            StringAssert.StartsWith("sha256:", result.Manifest.SourceHash);
            Assert.AreEqual(0, result.Manifest.Warnings.Count);
        }

        [Test]
        public void 清单_缺spec_告警不阻断()
        {
            GameObject prefab = NewTemplate(DirA, withSpec: false);

            var result = UiWidgetManifestGenerator.Generate(new[] { PathOf(prefab) });

            Assert.IsTrue(result.Ok, "缺 spec 只告警，不阻断清单生成");
            UiWidgetManifestEntry entry = result.Manifest.Widgets.Single();
            Assert.IsFalse(entry.SpecPresent);
            Assert.IsNull(entry.Owner);
            Assert.IsNull(entry.Summary);
            Assert.AreEqual(0, entry.Slots.Count);
            Assert.AreEqual(3, entry.Properties.Count, "缺 spec 仍能投影受控属性");
            Assert.IsTrue(result.Manifest.Warnings.Any(x => x.Contains("WidgetSpec")), "告警需可定位到模板");
        }

        [Test]
        public void 清单_确定性_重复生成零diff()
        {
            GameObject prefab = NewTemplate(DirA);
            string path = PathOf(prefab);

            var a = UiWidgetManifestGenerator.Generate(new[] { path });
            var b = UiWidgetManifestGenerator.Generate(new[] { path });
            Assert.IsTrue(a.Ok, a.ErrorText);
            Assert.IsTrue(b.Ok, b.ErrorText);

            string jsonA = UiWidgetManifestJson.Serialize(a.Manifest);
            Assert.AreEqual(jsonA, UiWidgetManifestJson.Serialize(b.Manifest), "同输入重复生成必须逐字节一致");
            StringAssert.DoesNotContain("generatedAt", jsonA);
        }

        [Test]
        public void sourceHash_对声明敏感_对路径不敏感()
        {
            GameObject a = NewTemplate(DirA);
            GameObject b = NewTemplate(DirB);
            var first = UiWidgetManifestGenerator.Generate(new[] { PathOf(a) });
            var second = UiWidgetManifestGenerator.Generate(new[] { PathOf(b) });
            Assert.IsTrue(first.Ok, first.ErrorText);
            Assert.IsTrue(second.Ok, second.ErrorText);
            Assert.AreEqual(first.Manifest.SourceHash, second.Manifest.SourceHash,
                "同模板名同声明在不同目录 → 指纹相同（路径/GUID 不进指纹）");

            GameObject changed = NewTemplate(DirB, summary: "步进器（改）");
            var third = UiWidgetManifestGenerator.Generate(new[] { PathOf(changed) });
            Assert.IsTrue(third.Ok, third.ErrorText);
            Assert.AreNotEqual(first.Manifest.SourceHash, third.Manifest.SourceHash, "声明变化必须改变指纹");

            GameObject changedStates = NewTemplate(DirB, states: new[] { UiWidgetState.Normal });
            var fourth = UiWidgetManifestGenerator.Generate(new[] { PathOf(changedStates) });
            Assert.IsTrue(fourth.Ok, fourth.ErrorText);
            Assert.AreNotEqual(first.Manifest.SourceHash, fourth.Manifest.SourceHash, "状态声明变化必须改变指纹");
        }

        [Test]
        public void 清单_插槽节点缺失_报错()
        {
            GameObject prefab = NewTemplate(DirA, slots: new[] { "Cot_Missing" });

            var result = UiWidgetManifestGenerator.Generate(new[] { PathOf(prefab) });

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("插槽", result.ErrorText);
        }

        [Test]
        public void 清单_重复插槽_报错()
        {
            GameObject prefab = NewTemplate(DirA, slots: new[] { "Cot_Content", "Cot_Content" });

            var result = UiWidgetManifestGenerator.Generate(new[] { PathOf(prefab) });

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("重复", result.ErrorText);
        }

        [Test]
        public void 清单_事件成员解析不到_报错()
        {
            GameObject prefab = NewTemplate(DirA, events: new[] { "Stepper.OnNothing" });

            var result = UiWidgetManifestGenerator.Generate(new[] { PathOf(prefab) });

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("事件", result.ErrorText);
        }

        [Test]
        public void 清单_空事件名_报错()
        {
            GameObject prefab = NewTemplate(DirA, events: new[] { string.Empty });

            var result = UiWidgetManifestGenerator.Generate(new[] { PathOf(prefab) });

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("事件", result.ErrorText);
        }

        [Test]
        public void 清单_空约束_报错()
        {
            GameObject prefab = NewTemplate(DirA, constraints: new[] { "  " });

            var result = UiWidgetManifestGenerator.Generate(new[] { PathOf(prefab) });

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("约束", result.ErrorText);
        }

        [Test]
        public void 清单_写盘读回一致_输出路径限定Assets()
        {
            GameObject prefab = NewTemplate(DirA);
            string output = DirA + "/manifest.json";

            var result = UiWidgetManifestGenerator.GenerateToDisk(output);
            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.IsTrue(File.Exists(Absolute(output)), "成功生成必须写盘");

            UiWidgetManifest loaded = UiWidgetManifestStore.Load(output);
            Assert.AreEqual(result.Manifest.SourceHash, loaded.SourceHash);
            Assert.AreEqual(result.Manifest.Widgets.Count, loaded.Widgets.Count);

            var outside = UiWidgetManifestGenerator.GenerateToDisk("Temp/UiWidgetManifest.json");
            Assert.IsFalse(outside.Ok);
            StringAssert.Contains("Assets/", outside.ErrorText);
        }

        [Test]
        public void 真实模板目录_全部声明且零告警()
        {
            var result = UiWidgetManifestGenerator.Generate();

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.GreaterOrEqual(result.Manifest.Widgets.Count, 30, "控件模板不少于词表登记的 30 件");
            Assert.IsTrue(result.Manifest.Widgets.All(x => x.SpecPresent),
                "所有 Tmp_* 模板都应声明 WidgetSpec：" + string.Join(", ",
                    result.Manifest.Widgets.Where(x => !x.SpecPresent).Select(x => x.Template)));
            Assert.AreEqual(0, result.Manifest.Warnings.Count, string.Join("\n", result.Manifest.Warnings));
            StringAssert.StartsWith("sha256:", result.Manifest.SourceHash);
        }

        private static string Absolute(string assetPath)
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
