using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UI 校验核心（《UI编辑器工具专项设计》§4）的 L2 EditMode 覆盖：规则端口形状、报告契约
    /// （<c>{ruleId,severity,asset,node,message,fixHint}</c>）、"未接入族显式列出"、正负例。
    ///
    /// §4 硬要求："每条硬规则提供正例与违规负例"——本用例用合成上下文喂正/负例，
    /// 不断言真实代码面（真实面由 <see cref="VisualSingleSourceEditModeTests"/> 守）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiValidationEditModeTests
    {
        private static UiValidationContext ContextWithSource(string sourceText)
        {
            // 合成源码落临时文件，喂给规则（源码面规则按目录遍历，故造临时目录）
            string root = Path.Combine(Path.GetTempPath(), "ui-validate-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Sample.cs"), sourceText);
            return new UiValidationContext(root, new[] { "." }, null);
        }

        // ---- 正例：干净源码 → 0 问题 ----

        [Test]
        public void 正例_干净源码_无违规()
        {
            var report = UiValidator.Run(ContextWithSource("var x = 1;\nvoid F() { }\n"));
            Assert.AreEqual(0, report.ErrorCount, "干净源码不应报错");
            Assert.IsTrue(report.Passed);
        }

        [Test]
        public void 正例_层容器骨架_不判违规()
        {
            // §7：层容器节点是骨架不是视觉（不带视觉组件参数）
            var report = UiValidator.Run(ContextWithSource("var root = new GameObject(\"[UIRoot]\");\n"));
            Assert.AreEqual(0, report.ErrorCount);
        }

        // ---- 负例：构建视觉 → 1 条 Error（规则必须真的会红） ----

        [Test]
        public void 负例_代码建Canvas_报一条Error()
        {
            var report = UiValidator.Run(ContextWithSource("var go = new GameObject(\"Root\", typeof(Canvas), typeof(CanvasScaler));"));
            Assert.AreEqual(1, report.ErrorCount, "构造 Canvas 必须判违规");
            Assert.IsFalse(report.Passed, "有 Error 时报告不得为通过");
        }

        // ---- 报告契约 ----

        [Test]
        public void 报告项_五字段齐全()
        {
            var report = UiValidator.Run(ContextWithSource("panel.AddComponent<Image>();"));
            Assert.AreEqual(1, report.Issues.Count);
            var i = report.Issues[0];
            Assert.IsNotEmpty(i.RuleId, "ruleId 必填");
            Assert.IsNotEmpty(i.Asset, "asset 必填");
            Assert.IsNotEmpty(i.Message, "message 必填");
            Assert.IsNotEmpty(i.FixHint, "fixHint 必填（§4：出错要给怎么改）");
            Assert.AreEqual(UiSeverity.Error, i.Severity);
        }

        [Test]
        public void 报告_未接入族显式列出不冒充通过()
        {
            var report = UiValidator.Run(ContextWithSource("var x = 1;\n"));
            var pending = report.Families.Where(f => !f.Implemented).Select(f => f.Family).ToList();
            Assert.IsNotEmpty(pending, "未接入的族必须在报告里列出");
            Assert.Contains("绑定", pending, "绑定族本步未接入——须显式列出");
            Assert.Contains("文本", pending);
        }

        // ---- 规则端口 ----

        [Test]
        public void 视觉规则_端口标识与族()
        {
            var rule = new RuntimeVisualRule();
            Assert.AreEqual("ui.runtime-visual", rule.RuleId);
            Assert.AreEqual("渲染/适配", rule.Family);
        }
    }
}
