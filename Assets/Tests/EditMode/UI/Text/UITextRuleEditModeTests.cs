using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteFramework.UI;
using LiteGame.Editor.UITools.Text;
using LiteGame.Editor.UITools.Validation;
using LiteGame.Editor.UITools.Validation.Rules;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 文本族四规则（工具链设计 §6.2「文本」）：key 存在性（LTextKeyRef 与页面逻辑 SetTextKey* 两路）、 占位符使用、语言列完整、富文本白名单。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UITextRuleEditModeTests
    {
        private const string TempDir = "Assets/TempUiTextRuleTests";
        private const string LogicTempDir = "Assets/TempUiTextRuleLogic~";   // ~ 结尾：编辑器不编译（夹具仅文本）

        private static readonly UILTextEntry[] Seed =
        {
            new UILTextEntry("UI.Test.Hello", "你好", "Hello"),
            new UILTextEntry("UI.Test.Args", "共 {0} 人", "Total {0}"),
            new UILTextEntry("UI.Test.Rich", "<b>粗体</b>与<color=red>红</color>", "plain"),
        };

        [SetUp]
        public void SetUp() => EnsureFolder(TempDir);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
            string logicFull = Path.Combine(UIValidator.ProjectRoot, LogicTempDir).Replace('/', Path.DirectorySeparatorChar);
            if (Directory.Exists(logicFull)) Directory.Delete(logicFull, true);
        }

        // ---- ui.text.static-key ----

        [Test]
        public void 静态key_LTextKeyRef在表通过_缺key报Error()
        {
            string good = SavePrefabWithKey("Page_Good", "UI.Test.Hello");
            string bad = SavePrefabWithKey("Page_Bad", "UI.Test.Missing");
            var rule = new TextStaticKeyRule(LogicTempDir);        // 注入空扫描根——只跑资产面（真实逻辑根归全量校准）
            var context = ContextFor(new[] { good, bad });

            var issues = rule.Check(context).ToList();

            var errors = issues.Where(i => i.Severity == UISeverity.Error).ToList();
            Assert.AreEqual(1, errors.Count, "缺 key 须报一条 Error：\n" + string.Join("\n", issues));
            Assert.AreEqual(UIRuleIds.TextStaticKey, errors[0].RuleId);
            StringAssert.Contains("UI.Test.Missing", errors[0].Message, "报文点名缺失 key");
            Assert.AreEqual(bad, errors[0].Asset, "问题归属缺 key 的 prefab");
            Assert.AreEqual(0, issues.Count(i => i.Asset == good), "在表 key 零误报");
        }

        [Test]
        public void 静态key_CSharp侧SetTextKey用缺key报Error()
        {
            WriteCSharp("Page_DemoLogic.cs",
                "class Page_DemoLogic { void OnShow() { SetTextKey(\"Txt_A\", \"UI.Test.Missing\"); } }");
            var rule = new TextStaticKeyRule(LogicTempDir);

            var issues = rule.Check(ContextFor(new string[0])).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UISeverity.Error), "C# 缺 key 须报一条 Error：\n"
                + string.Join("\n", issues));
            StringAssert.Contains("UI.Test.Missing", issues[0].Message);
            StringAssert.Contains("Page_DemoLogic.cs", issues[0].Asset, "问题归属页逻辑件");
        }

        [Test]
        public void 静态key_CSharp侧合法key零误报()
        {
            WriteCSharp("Page_DemoLogic.cs",
                "class Page_DemoLogic { void OnShow() { SetTextKey(\"Txt_A\", \"UI.Test.Hello\"); } }");
            var rule = new TextStaticKeyRule(LogicTempDir);

            var issues = rule.Check(ContextFor(new string[0])).ToList();

            CollectionAssert.IsEmpty(issues, "在表 key 不应报：\n" + string.Join("\n", issues));
        }

        // ---- ui.text.placeholder ----

        [Test]
        public void 占位符_静态键挂带参条目报Error()
        {
            string prefab = SavePrefabWithKey("Page_Args", "UI.Test.Args");
            var rule = new TextPlaceholderRule(LogicTempDir);      // 注入空扫描根——只跑资产面

            var issues = rule.Check(ContextFor(new[] { prefab })).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UISeverity.Error),
                "LTextKeyRef 无参挂带参条目须报 Error：\n" + string.Join("\n", issues));
            Assert.AreEqual(UIRuleIds.TextPlaceholder, issues[0].RuleId);
        }

        [Test]
        public void 占位符_CSharpSetTextKey参数数不符报Warning()
        {
            WriteCSharp("Page_DemoLogic.cs", string.Join("\n",
                "class Page_DemoLogic",
                "{",
                "    void OnShow()",
                "    {",
                "        SetTextKey(\"Txt_A\", \"UI.Test.Args\");",
                "        SetTextKey(\"Txt_B\", \"UI.Test.Args\", 5);",
                "    }",
                "}"));
            var rule = new TextPlaceholderRule(LogicTempDir);

            var issues = rule.Check(ContextFor(new string[0])).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UISeverity.Warning),
                "SetTextKey 参数数（0）与条目占位符数（1）不符须报 Warning：\n" + string.Join("\n", issues));
            Assert.AreEqual(0, issues.Count(i => i.Message != null && i.Message.Contains("Txt_B")),
                "参数数相符的调用零误报");
            Assert.AreEqual(0, issues.Count(i => i.Severity == UISeverity.Error), "无 Error");
        }

        // ---- ui.text.locale-column ----

        [Test]
        public void 语言列_源语言空报Error_英文空报Warning()
        {
            var seed = new[]
            {
                new UILTextEntry("K.NoZh", "", "En"),
                new UILTextEntry("K.NoEn", "中文", ""),
                new UILTextEntry("K.Full", "中文", "En"),
            };
            var rule = new TextLocaleColumnRule();

            var issues = rule.Check(ContextFor(new string[0], seed)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UISeverity.Error), "源语言空须报 Error：\n"
                + string.Join("\n", issues));
            Assert.AreEqual(1, issues.Count(i => i.Severity == UISeverity.Warning), "英文空须报 Warning：\n"
                + string.Join("\n", issues));
            Assert.AreEqual(0, issues.Count(i => i.Message.Contains("K.Full")), "全列齐零误报");
        }

        // ---- ui.text.rich-text ----

        [Test]
        public void 富文本_白名单标签通过_越界标签报Warning()
        {
            var seed = new[]
            {
                new UILTextEntry("K.Ok", "<b>粗</b><i>斜</i><color=red>红</color><size=20>号</size>", "plain"),
                new UILTextEntry("K.Bad", "<sprite=1>图标</sprite>", "x"),
                new UILTextEntry("K.Plain", "共 {0} 人", "plain"),
            };
            var rule = new TextRichTextRule();

            var issues = rule.Check(ContextFor(new string[0], seed)).ToList();

            var warnings = issues.Where(i => i.Severity == UISeverity.Warning).ToList();
            Assert.AreEqual(1, warnings.Count, "越白名单标签须报一条 Warning：\n" + string.Join("\n", issues));
            Assert.AreEqual(UIRuleIds.TextRichText, warnings[0].RuleId);
            StringAssert.Contains("sprite", warnings[0].Message, "报文点名越界标签");
        }

        // ---- 辅助 ----

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static UIValidationContext ContextFor(IEnumerable<string> assets,
            IReadOnlyList<UILTextEntry> seed = null) =>
            new UIValidationContext(UIValidator.ProjectRoot, null, assets.ToList(),
                lTextSeed: seed ?? Seed);

        /// <summary>造带 LTextKeyRef 的合成页面 prefab（静态文案节点）。</summary>
        private static string SavePrefabWithKey(string name, string key)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var node = new GameObject("Txt_Sample", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            node.transform.SetParent(root.transform, false);
            var textRef = node.AddComponent<LTextKeyRef>();
            textRef.Key = key;
            string path = TempDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        private static void WriteCSharp(string fileName, string content)
        {
            string dir = Path.Combine(UIValidator.ProjectRoot, LogicTempDir).Replace('/', Path.DirectorySeparatorChar);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), content);
        }
    }
}
