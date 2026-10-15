using System.Linq;
using LiteGame.Editor.UiTools.Compose;
using LiteGame.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 受控属性白名单（《UI编辑器工具专项设计》§5"受控属性"）覆盖：标注成员才进行集、
    /// 接线引用字段不进面、非自有程序集（UGUI 契约件）组件不产行、值型归类（最小集 + 未支持显式列）、
    /// 真实模板资产面回归。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiShellPropertyEditModeTests
    {
        private GameObject _probe;

        [TearDown]
        public void Cleanup()
        {
            if (_probe != null) Object.DestroyImmediate(_probe);
            _probe = null;
        }

        [Test]
        public void 白名单_只出标注成员_接线字段不进面()
        {
            _probe = new GameObject("Txt_Probe", typeof(RectTransform));
            _probe.AddComponent<HpBar>();

            var rows = UiShellPropertyCatalog.Collect(_probe);
            var members = rows.Select(r => r.MemberName).ToList();

            CollectionAssert.Contains(members, "ValueFormat", "标注字段须进受控面");
            CollectionAssert.Contains(members, "BackLerpSpeed", "标注字段须进受控面");
            CollectionAssert.DoesNotContain(members, "Slider", "接线引用字段＝内部实现，不进受控面");
            CollectionAssert.DoesNotContain(members, "ValueText", "接线引用字段＝内部实现，不进受控面");
            CollectionAssert.DoesNotContain(members, "BackFill", "接线引用字段＝内部实现，不进受控面");
        }

        [Test]
        public void 白名单_非自有程序集组件不产行()
        {
            _probe = new GameObject("Cot_Probe", typeof(RectTransform));
            _probe.AddComponent<UnityEngine.UI.Slider>();                 // UGUI 契约件
            _probe.AddComponent<TMPro.TextMeshProUGUI>();

            CollectionAssert.IsEmpty(UiShellPropertyCatalog.Collect(_probe), "非 LiteGame.UI 组件不产生受控行");
        }

        [Test]
        public void 白名单_显示名与值型承自声明()
        {
            _probe = new GameObject("Txt_ProbeNames", typeof(RectTransform));
            _probe.AddComponent<HpBar>();

            var row = UiShellPropertyCatalog.Collect(_probe).First(r => r.MemberName == "ValueFormat");

            Assert.AreEqual("数值文本格式", row.DisplayName, "显示名取自 attribute");
            Assert.AreEqual(UiShellPropKind.String, row.Kind);
            Assert.IsNotEmpty(row.Tooltip, "字段 Tooltip 带入行（面板提示用）");
            Assert.AreEqual("HpBar", row.ComponentTypeName);
            Assert.IsNotNull(row.Key);
        }

        [Test]
        public void 值型归类_最小集与未支持显式区分()
        {
            Assert.AreEqual(UiShellPropKind.Bool, UiShellPropertyCatalog.Classify(typeof(bool)));
            Assert.AreEqual(UiShellPropKind.Int, UiShellPropertyCatalog.Classify(typeof(int)));
            Assert.AreEqual(UiShellPropKind.Float, UiShellPropertyCatalog.Classify(typeof(float)));
            Assert.AreEqual(UiShellPropKind.String, UiShellPropertyCatalog.Classify(typeof(string)));
            Assert.AreEqual(UiShellPropKind.Enum, UiShellPropertyCatalog.Classify(typeof(UiTextRole)));
            Assert.AreEqual(UiShellPropKind.Unsupported, UiShellPropertyCatalog.Classify(typeof(Color)),
                "未支持值型须显式归类（面板列「未支持值型」，不静默跳过）");
            Assert.AreEqual(UiShellPropKind.Unsupported, UiShellPropertyCatalog.Classify(typeof(GameObject)));
        }

        [Test]
        public void 真实资产_模板受控行存在()
        {
            const string path = "Assets/ShareResource/UI/Widgets/Tmp_HpBar.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(asset, "模板资产缺失：" + path);

            var rows = UiShellPropertyCatalog.Collect(asset);

            Assert.IsNotEmpty(rows, "真实模板应产生受控行");
            var hp = rows.Where(r => r.ComponentTypeName == "HpBar").Select(r => r.MemberName).ToList();
            CollectionAssert.Contains(hp, "ValueFormat");
            CollectionAssert.Contains(hp, "BackLerpSpeed");
            CollectionAssert.DoesNotContain(hp, "Slider");
        }
    }
}
