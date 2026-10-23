using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Text;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 文本族四规则（工具链设计 §6.2「文本」）：key 存在性（LTextKeyRef 与 Lua SetTextKey* 两路）、
    /// 占位符使用、语言列完整、富文本白名单。表数据面走 <c>LTextSeed</c> 注入（合成条目），
    /// Lua 面经规则构造器注入扫描根；真实表正例由全量校准门禁承担。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiTextRuleEditModeTests
    {
        private const string TempDir = "Assets/TempUiTextRuleTests";
        private const string LuaTempDir = "Assets/TempUiTextRuleLua";

        private static readonly UiLTextEntry[] Seed =
        {
            new UiLTextEntry("UI.Test.Hello", "你好", "Hello"),
            new UiLTextEntry("UI.Test.Args", "共 {0} 人", "Total {0}"),
            new UiLTextEntry("UI.Test.Rich", "<b>粗体</b>与<color=red>红</color>", "plain"),
        };

        [SetUp]
        public void SetUp() => EnsureFolder(TempDir);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
            if (AssetDatabase.IsValidFolder(LuaTempDir)) AssetDatabase.DeleteAsset(LuaTempDir);
        }

        // ---- ui.text.static-key ----

        [Test]
        public void 静态key_LTextKeyRef在表通过_缺key报Error()
        {
            string good = SavePrefabWithKey("Page_Good", "UI.Test.Hello");
            string bad = SavePrefabWithKey("Page_Bad", "UI.Test.Missing");
            var rule = new TextStaticKeyRule(LuaTempDir);        // 注入空扫描根——只跑资产面（真实 Lua 目录归全量校准）
            var context = ContextFor(new[] { good, bad });

            var issues = rule.Check(context).ToList();

            var errors = issues.Where(i => i.Severity == UiSeverity.Error).ToList();
            Assert.AreEqual(1, errors.Count, "缺 key 须报一条 Error：\n" + string.Join("\n", issues));
            Assert.AreEqual(UiRuleIds.TextStaticKey, errors[0].RuleId);
            StringAssert.Contains("UI.Test.Missing", errors[0].Message, "报文点名缺失 key");
            Assert.AreEqual(bad, errors[0].Asset, "问题归属缺 key 的 prefab");
            Assert.AreEqual(0, issues.Count(i => i.Asset == good), "在表 key 零误报");
        }

        [Test]
        public void 静态key_Lua侧SetTextKey用缺key报Error()
        {
            WriteLua("Page_Demo.lua",
                "function M:OnShow()\n    self.ui:SetTextKey(\"Txt_A\", \"UI.Test.Missing\")\nend\n");
            var rule = new TextStaticKeyRule(LuaTempDir);

            var issues = rule.Check(ContextFor(new string[0])).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error), "Lua 缺 key 须报一条 Error：\n"
                + string.Join("\n", issues));
            StringAssert.Contains("UI.Test.Missing", issues[0].Message);
            StringAssert.Contains("Page_Demo.lua", issues[0].Asset, "问题归属 Lua 文件");
        }

        [Test]
        public void 静态key_Lua侧合法key零误报()
        {
            WriteLua("Page_Demo.lua",
                "function M:OnShow()\n    self.ui:SetTextKey(\"Txt_A\", \"UI.Test.Hello\")\nend\n");
            var rule = new TextStaticKeyRule(LuaTempDir);

            var issues = rule.Check(ContextFor(new string[0])).ToList();

            CollectionAssert.IsEmpty(issues, "在表 key 不应报：\n" + string.Join("\n", issues));
        }

        // ---- ui.text.placeholder ----

        [Test]
        public void 占位符_静态键挂带参条目报Error()
        {
            string prefab = SavePrefabWithKey("Page_Args", "UI.Test.Args");
            var rule = new TextPlaceholderRule(LuaTempDir);      // 注入空扫描根——只跑资产面

            var issues = rule.Check(ContextFor(new[] { prefab })).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error),
                "LTextKeyRef 无参挂带参条目须报 Error：\n" + string.Join("\n", issues));
            Assert.AreEqual(UiRuleIds.TextPlaceholder, issues[0].RuleId);
        }

        [Test]
        public void 占位符_LuaSetTextKey用带参条目报Error_SetTextKeyArgs参数不符报Warning()
        {
            WriteLua("Page_Demo.lua", string.Join("\n",
                "function M:OnShow()",
                "    self.ui:SetTextKey(\"Txt_A\", \"UI.Test.Args\")",
                "    self.ui:SetTextKeyArgs(\"Txt_B\", \"UI.Test.Args\", 1, 2)",
                "    self.ui:SetTextKeyArgs(\"Txt_C\", \"UI.Test.Args\", 5)",
                "end"));
            var rule = new TextPlaceholderRule(LuaTempDir);

            var issues = rule.Check(ContextFor(new string[0])).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error),
                "SetTextKey 用带参条目须报 Error：\n" + string.Join("\n", issues));
            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Warning),
                "SetTextKeyArgs 参数数（2）与条目占位符数（1）不符须报 Warning：\n" + string.Join("\n", issues));
            Assert.AreEqual(0, issues.Count(i => i.Message != null && i.Message.Contains("Txt_C")),
                "参数数相符的调用零误报");
        }

        // ---- ui.text.locale-column ----

        [Test]
        public void 语言列_源语言空报Error_英文空报Warning()
        {
            var seed = new[]
            {
                new UiLTextEntry("K.NoZh", "", "En"),
                new UiLTextEntry("K.NoEn", "中文", ""),
                new UiLTextEntry("K.Full", "中文", "En"),
            };
            var rule = new TextLocaleColumnRule();

            var issues = rule.Check(ContextFor(new string[0], seed)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error), "源语言空须报 Error：\n"
                + string.Join("\n", issues));
            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Warning), "英文空须报 Warning：\n"
                + string.Join("\n", issues));
            Assert.AreEqual(0, issues.Count(i => i.Message.Contains("K.Full")), "全列齐零误报");
        }

        // ---- ui.text.rich-text ----

        [Test]
        public void 富文本_白名单标签通过_越界标签报Warning()
        {
            var seed = new[]
            {
                new UiLTextEntry("K.Ok", "<b>粗</b><i>斜</i><color=red>红</color><size=20>号</size>", "plain"),
                new UiLTextEntry("K.Bad", "<sprite=1>图标</sprite>", "x"),
                new UiLTextEntry("K.Plain", "共 {0} 人", "plain"),
            };
            var rule = new TextRichTextRule();

            var issues = rule.Check(ContextFor(new string[0], seed)).ToList();

            var warnings = issues.Where(i => i.Severity == UiSeverity.Warning).ToList();
            Assert.AreEqual(1, warnings.Count, "越白名单标签须报一条 Warning：\n" + string.Join("\n", issues));
            Assert.AreEqual(UiRuleIds.TextRichText, warnings[0].RuleId);
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

        private static UiValidationContext ContextFor(IEnumerable<string> assets,
            IReadOnlyList<UiLTextEntry> seed = null) =>
            new UiValidationContext(UiValidator.ProjectRoot, null, assets.ToList(),
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

        private static void WriteLua(string fileName, string content)
        {
            EnsureFolder(LuaTempDir);
            File.WriteAllText(Path.Combine(UiValidator.ProjectRoot, LuaTempDir, fileName)
                .Replace('/', Path.DirectorySeparatorChar), content);
        }
    }
}
