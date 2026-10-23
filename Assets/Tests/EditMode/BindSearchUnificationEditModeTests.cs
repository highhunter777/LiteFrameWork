using System.Collections.Generic;
using System.Linq;
using LiteCodeGen;
using LiteCodeGen.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 绑定遍历统一（审计 L247 收口）：生成器域标记收集与 hash/契约/校验域共用同一「哪些节点属于本页/本模板」
    /// 语义——<c>Clone_</c> 子树、模板（prefab 实例）内部件不入本页 marks；嵌套 <c>BindRoot</c> 不深入；
    /// 根节点自身不入。既有语义（标识符校验/重名收集/层级遍历序）保留在生成器域适配内。
    /// 真实资产面零 BindRoot（探针实证），统一后生成物零变化由 hash 回归套件（UiBindingToolsEditModeTests）另守。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class BindSearchUnificationEditModeTests
    {
        private const string TempDir = "Assets/TempBindSearchTests";

        [SetUp]
        public void SetUp() => EnsureFolder(TempDir);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static GameObject Node(GameObject parent, string name, bool bindNode)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (bindNode) go.AddComponent<BindNode>();
            if (parent != null) go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static List<BindMark> Search(GameObject root, List<string> errors)
            => BindSearch.Search(root.transform, errors);

        [Test]
        public void 新语义_Clone_子树BindNode不入marks()
        {
            var root = Node(null, "Page_Test", false);
            root.AddComponent<BindRoot>();
            Node(root, "Button_A", true);
            var clone = Node(root, "Clone_List", true);
            Node(clone, "Clone_Inner", true);
            var errors = new List<string>();

            var marks = Search(root, errors);

            Assert.AreEqual(1, marks.Count, "Clone_ 子树（含其根与内部件）不属于本页接口面：\n  "
                + string.Join("\n  ", marks.Select(m => m.MemberName)));
            Assert.AreEqual("Button_A", marks[0].MemberName);
        }

        [Test]
        public void 新语义_模板内部件不入marks()
        {
            // 模板（prefab 实例）是独立生成单位：实例根与其内部件都不入本页 marks
            var template = Node(null, "Tmp_Widget", true);
            Node(template, "W_Inner", true);
            string templatePath = TempDir + "/Tmp_Widget.prefab";
            PrefabUtility.SaveAsPrefabAsset(template, templatePath);
            Object.DestroyImmediate(template);

            var root = Node(null, "Page_Tpl", false);
            root.AddComponent<BindRoot>();
            Node(root, "Button_A", true);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(templatePath));
            instance.transform.SetParent(root.transform, false);
            var errors = new List<string>();

            var marks = Search(root, errors);

            Assert.AreEqual(1, marks.Count, "模板实例根（Tmp_Widget）与内部件（W_Inner）不入本页 marks：\n  "
                + string.Join("\n  ", marks.Select(m => m.MemberName)));
            Assert.AreEqual("Button_A", marks[0].MemberName);
        }

        [Test]
        public void 语义_嵌套BindRoot不深入且自身不入父列表()
        {
            var root = Node(null, "Page_Nest", false);
            root.AddComponent<BindRoot>();
            Node(root, "Button_A", true);
            var nested = Node(root, "Sub", false);
            nested.AddComponent<BindRoot>();
            Node(nested, "Sub_Button", true);
            var errors = new List<string>();

            var marks = Search(root, errors);

            Assert.AreEqual(1, marks.Count, "嵌套 BindRoot 子树独立生成，不入父列表：\n  "
                + string.Join("\n  ", marks.Select(m => m.MemberName)));
            Assert.AreEqual("Button_A", marks[0].MemberName);
        }

        [Test]
        public void 语义_根节点自身不入marks()
        {
            var root = Node(null, "Page_Self", true);      // 根上直接挂 BindNode
            root.AddComponent<BindRoot>();
            Node(root, "Button_A", true);
            var errors = new List<string>();

            var marks = Search(root, errors);

            Assert.AreEqual(1, marks.Count, "根节点自身不是成员：\n  " + string.Join("\n  ", marks.Select(m => m.MemberName)));
            Assert.AreEqual("Button_A", marks[0].MemberName);
        }

        [Test]
        public void 标识符_partial关键字拒识()
        {
            var root = Node(null, "Page_Keyword", false);
            root.AddComponent<BindRoot>();
            Node(root, "partial", true);
            var errors = new List<string>();

            var marks = Search(root, errors);

            Assert.AreEqual(0, marks.Count, "partial 作为成员名生成会产非法代码——须拒识不入 marks");
            Assert.IsTrue(errors.Any(e => e.Contains("partial")), "错误收集须点名节点：\n" + string.Join("\n", errors));
        }

        [Test]
        public void 标识符_重名收集错误且marks稳定()
        {
            var root = Node(null, "Page_Dup", false);
            root.AddComponent<BindRoot>();
            Node(root, "Button_A", true);
            Node(root, "Button_B", true);
            Node(root, "Button_A", true);                   // 重名
            var errors = new List<string>();

            var marks = Search(root, errors);

            Assert.AreEqual(2, marks.Count, "重名只入一次，合法两名各一：\n  "
                + string.Join("\n  ", marks.Select(m => m.MemberName)));
            Assert.AreEqual(1, errors.Count(e => e.Contains("重名")), "重名错误收集一条");
            CollectionAssert.AreEqual(new[] { 0, 1 }, marks.Select(m => m.OrderIndex),
                "层级遍历序稳定（OrderIndex 连续 0 基）");
        }

        [Test]
        public void 生成输入等价_常规层级marks与既有语义一致()
        {
            // 无 Clone_/无嵌套 prefab/无嵌套 BindRoot 的常规层级：统一后 marks 必须与既有语义逐项一致
            //（生成文本零变化的判据——marks 即生成输入）
            var root = Node(null, "Page_Normal", false);
            root.AddComponent<BindRoot>();
            Node(root, "Button_A", true);
            Node(root, "Cot_Panel", false);
            Node(root, "Button_B", true);
            var errors = new List<string>();

            var marks = Search(root, errors);

            CollectionAssert.IsEmpty(errors);
            CollectionAssert.AreEqual(new[] { "Button_A", "Button_B" },
                marks.Select(m => m.MemberName).ToList(), "深度优先序");
            CollectionAssert.AreEqual(new[] { 0, 1 }, marks.Select(m => m.OrderIndex).ToList());
            Assert.IsTrue(marks.All(m => m.Bind != null), "标记携带 BindNode 引用");
        }
    }
}
