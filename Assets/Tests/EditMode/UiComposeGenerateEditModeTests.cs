using System.IO;
using System.Linq;
using LiteCodeGen;
using LiteCodeGen.EditorTools;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Validation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 生成分区（§5"校验/生成"）的逻辑面覆盖：生成根查找三态（根上 / 子物体 / 无 —— 无＝路径 B 未启用，
    /// 面板显式说明而不报错）、生成物 hash 的**记录-校验闭环**（面板"生成并记录"与
    /// <c>ui.binding-hash</c> 规则的收口契约）。
    /// **不调 <see cref="BindGenerator"/>**：生成会写码文件并触发域重载，属窗口手测面；此处钉被复用的查询与对账面。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiComposeGenerateEditModeTests
    {
        private const string TempDir = "Assets/TempUiComposeGenerateTests";

        [SetUp]
        public void EnsureTempDir()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "TempUiComposeGenerateTests");
        }

        [TearDown]
        public void CleanupTempDir()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        /// <summary>合成源根（表目录缺席 → 表路径规则跳过）。</summary>
        private static string SyntheticRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "ui-compose-gen-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string MakePrefab(string name, bool rootOnSelf, bool rootInChild)
        {
            var root = new GameObject(name, typeof(RectTransform));
            if (rootOnSelf) root.AddComponent<BindRoot>();
            if (rootInChild)
            {
                var child = new GameObject("Cot_Generated", typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
                child.AddComponent<BindRoot>();
            }
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        [Test]
        public void 生成根查找_三态()
        {
            string selfPath = MakePrefab("Page_GenRootOnSelf", rootOnSelf: true, rootInChild: false);
            string childPath = MakePrefab("Page_GenRootInChild", rootOnSelf: false, rootInChild: true);
            string nonePath = MakePrefab("Page_GenNoRoot", rootOnSelf: false, rootInChild: false);

            var selfAsset = AssetDatabase.LoadAssetAtPath<GameObject>(selfPath);
            var selfRoot = BindMenus.FindRootInPrefab(selfAsset);
            Assert.IsNotNull(selfRoot);
            Assert.AreSame(selfAsset, selfRoot.gameObject, "根上有生成根 → 取它");

            var childAsset = AssetDatabase.LoadAssetAtPath<GameObject>(childPath);
            var childRoot = BindMenus.FindRootInPrefab(childAsset);
            Assert.IsNotNull(childRoot, "根上没有时取子物体首个生成根");
            Assert.AreEqual("Cot_Generated", childRoot.gameObject.name);

            var noneAsset = AssetDatabase.LoadAssetAtPath<GameObject>(nonePath);
            Assert.IsNull(BindMenus.FindRootInPrefab(noneAsset), "无生成根 → null（面板显式说明路径 B 未启用）");
        }

        [Test]
        public void 记录与校验闭环_同一作者动作后未记录与漂移清零()
        {
            string path = MakePrefab("Page_GenClosure", rootOnSelf: true, rootInChild: false);
            string manifest = TempDir + "/bind-manifest.json";
            var context = new UiValidationContext(SyntheticRoot(), new string[0], new[] { path }, manifest);

            var before = UiValidator.Run(context);
            var issuesBefore = before.Issues.Where(i => i.RuleId == "ui.binding-hash").ToList();
            Assert.IsNotEmpty(issuesBefore, "带 BindRoot 但未记录 → hash 规则须显红");
            Assert.IsTrue(issuesBefore.Any(i => i.Message.Contains("NotRecorded")), "未记录＝Error（不冒充通过）");
            Assert.AreEqual(UiSeverity.Error, issuesBefore[0].Severity);

            UiBindGenerationHash.Record(path, manifest);          // 面板"生成并记录"的记录步

            var after = UiValidator.Run(context);
            var issuesAfter = after.Issues.Where(i => i.RuleId == "ui.binding-hash").ToList();
            Assert.IsFalse(issuesAfter.Any(i => i.Message.Contains("NotRecorded")), "记录后「未记录」须清零");
            Assert.IsFalse(issuesAfter.Any(i => i.Message.Contains("Stale")), "记录后「漂移」须清零");

            // 面板状态行口径：记录值与当前绑定面指纹一致（配方例不生成代码，故不涉"生成物在盘"项）
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsTrue(UiBindGenerationHash.TryGetRecordedHash(path, out string recorded, manifest));
            Assert.AreEqual(UiBindGenerationHash.ComputeHash(prefab), recorded, "记录值＝当前绑定面指纹");
        }
    }
}
