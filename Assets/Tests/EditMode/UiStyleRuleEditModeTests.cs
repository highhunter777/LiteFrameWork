using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using LiteGame.EditorTools.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 样式族两规则（工具链设计 §6.2「样式」/§8.1 漂移报告）：颜色 token 对账与文字档对账——
    /// 规则复用 <c>UiStyleTool</c>/<c>UiTextStyleTool</c> 的对账单源（本夹具同时钉「规则只投影、不复制第二份阈值/遍历」）。
    /// 合成 prefab 落临时目录注入；真实面基线（颜色漂移/未标记计数）由全量校准门禁承担。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiStyleRuleEditModeTests
    {
        private const string TempDir = "Assets/TempUiStyleRuleTests";

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
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static TMP_FontAsset Font()
            => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ShareResource/Fonts/NotoSansSC SDF.asset");

        private static string SavePrefab(string name, System.Action<GameObject> build)
        {
            var root = new GameObject(name, typeof(RectTransform));
            build(root);
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        private static GameObject Node(GameObject parent, string nodeName, params System.Type[] components)
        {
            var all = new List<System.Type> { typeof(RectTransform) };
            all.AddRange(components);
            var go = new GameObject(nodeName, all.ToArray());
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static UiValidationContext ContextFor(params string[] assets)
            => new UiValidationContext(UiValidator.ProjectRoot, null, assets);

        // ---- ui.style.color-drift ----

        [Test]
        public void 颜色对账_非token色报Warning_token色零误报()
        {
            string path = SavePrefab("Page_Color", root =>
            {
                var bare = Node(root, "Img_Bare", typeof(Image));
                bare.GetComponent<Image>().color = new Color(0.33f, 0.22f, 0.11f, 1f);
                var token = Node(root, "Img_Token", typeof(Image));
                token.GetComponent<Image>().color = UiStyle.Primary;
            });

            var issues = new StyleColorDriftRule().Check(ContextFor(path)).ToList();

            var warnings = issues.Where(i => i.Severity == UiSeverity.Warning).ToList();
            Assert.AreEqual(1, warnings.Count, "手调色须报一条 Warning（可见债，不阻断）：\n" + string.Join("\n", issues));
            Assert.AreEqual(UiRuleIds.StyleColorDrift, warnings[0].RuleId);
            Assert.AreEqual(path, warnings[0].Asset);
            StringAssert.Contains("Img_Bare", warnings[0].Message ?? warnings[0].Node, "报文可定位节点");
        }

        // ---- ui.style.text-drift ----

        [Test]
        public void 文字档_未标记文本报Warning()
        {
            string path = SavePrefab("Page_Unmarked", root =>
            {
                var text = Node(root, "Txt_Bare", typeof(TMPro.TextMeshProUGUI));
                text.GetComponent<TMPro.TextMeshProUGUI>().font = Font();
            });

            var issues = new StyleTextDriftRule().Check(ContextFor(path)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Warning),
                "无 UiTextStyleRef 的文本须报未标记 Warning：\n" + string.Join("\n", issues));
            Assert.AreEqual(UiRuleIds.StyleTextDrift, issues[0].RuleId);
        }

        [Test]
        public void 文字档_标记属性漂移报Warning_标记失效报Error()
        {
            string path = SavePrefab("Page_Drift", root =>
            {
                var drifted = Node(root, "Txt_Drift", typeof(TMPro.TextMeshProUGUI));
                var tmp = drifted.GetComponent<TMPro.TextMeshProUGUI>();
                tmp.font = Font();
                tmp.fontSize = 14f;                        // Title 档基线是 24——漂移
                drifted.AddComponent<UiTextStyleRef>().Role = UiTextRole.Title;

                var broken = Node(root, "Txt_Broken");     // 无文本组件——标记失效
                broken.AddComponent<UiTextStyleRef>().Role = UiTextRole.Body;
            });

            var issues = new StyleTextDriftRule().Check(ContextFor(path)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Warning),
                "字号漂移须报 Warning：\n" + string.Join("\n", issues));
            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error),
                "标记失效（无文本组件）须报 Error：\n" + string.Join("\n", issues));
            Assert.IsTrue(issues.All(i => i.RuleId == UiRuleIds.StyleTextDrift));
        }

        [Test]
        public void 文字档_基线相符零误报()
        {
            string path = SavePrefab("Page_Marked", root =>
            {
                var text = Node(root, "Txt_Body", typeof(TMPro.TextMeshProUGUI));
                text.GetComponent<TMPro.TextMeshProUGUI>().font = Font();
                text.AddComponent<UiTextStyleRef>().Role = UiTextRole.Body;
            });
            // 用工具单源把档位基线写进组件——规则与工具对账同一份真相
            UiTextStyleTool.ApplyTextStyles(new[] { TempDir });

            var issues = new StyleTextDriftRule().Check(ContextFor(path)).ToList();

            CollectionAssert.IsEmpty(issues.Where(i => i.Message != null && i.Message.Contains("Txt_Body")),
                "工具应用后的基线节点零误报：\n" + string.Join("\n", issues));
        }
    }
}
