using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using NUnit.Framework;
using UnityEngine;

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
            Assert.Contains("文本", pending, "文本族未接入——须显式列出");
            Assert.Contains("资源", pending);
            Assert.IsFalse(pending.Contains("绑定"), "绑定族本步已接入——不应再列待接入");
            Assert.IsFalse(pending.Contains("渲染/适配"), "渲染/适配族已接入——不应再列待接入");
        }

        // ---- 规则端口 ----

        [Test]
        public void 视觉规则_端口标识与族()
        {
            var rule = new RuntimeVisualRule();
            Assert.AreEqual("ui.runtime-visual", rule.RuleId);
            Assert.AreEqual("渲染/适配", rule.Family);
        }

        // ---- 绑定命名规则（资产面；造临时 prefab 喂规则） ----

        [Test]
        public void 绑定命名_端口标识与族()
        {
            var rule = new BindingNamingRule();
            Assert.AreEqual("ui.binding-naming", rule.RuleId);
            Assert.AreEqual("绑定", rule.Family);
        }

        [Test]
        public void 绑定命名_合法名通过_违规名报错()
        {
            var ok = RunOnTempPrefab("Tmp_Fixture", root =>
            {
                AddChild(root, "Cot_Panel", typeof(RectTransform));
                AddChild(root, "Txt_Title", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            });
            Assert.AreEqual(0, ok.ErrorCount, "合法名不应报错：\n" + ok.Render());

            var bad = RunOnTempPrefab("Tmp_Fixture", root =>
            {
                AddChild(root, "A_B_C", typeof(RectTransform));      // 多下划线
                AddChild(root, "Lst_Foo", typeof(RectTransform));    // 自造段（非词表）
            });
            Assert.AreEqual(1, bad.ErrorCount, "多下划线应报 Error：\n" + bad.Render());
            Assert.AreEqual(1, bad.WarningCount, "自造段应报 Warning：\n" + bad.Render());
        }

        [Test]
        public void 绑定命名_页面节点禁下划线前缀_模板内允许()
        {
            var page = RunOnTempPrefab("Page_Test", root =>
                AddChild(root, "_Bg", typeof(RectTransform)));
            Assert.AreEqual(1, page.ErrorCount, "页面节点 `_` 前缀应报 Error：\n" + page.Render());

            var tpl = RunOnTempPrefab("Tmp_Test", root =>
                AddChild(root, "_Bg", typeof(RectTransform)));
            Assert.AreEqual(0, tpl.ErrorCount, "模板内 `_` 私有件合法：\n" + tpl.Render());
        }

        /// <summary>造临时 prefab（Assets 下——规则经 AssetDatabase 装载），跑规则后删除。</summary>
        private static UiReport RunOnTempPrefab(string rootName, System.Action<GameObject> build)
        {
            string dir = "Assets/TempUiValidationTests";
            if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "TempUiValidationTests");
            string path = $"{dir}/{rootName}.prefab";
            var go = new GameObject(rootName, typeof(RectTransform));
            build(go);
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            try
            {
                var ctx = new UiValidationContext(UiValidator.ProjectRoot, null, new[] { path });
                return UiValidator.Run(ctx);
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(path);
                // 夹目录空了则删（避免测试留痕）
                if (UnityEditor.AssetDatabase.IsValidFolder(dir)
                    && UnityEditor.AssetDatabase.FindAssets("", new[] { dir }).Length == 0)
                    UnityEditor.AssetDatabase.DeleteAsset(dir);
            }
        }

        private static void AddChild(GameObject parent, string name, params System.Type[] comps)
        {
            var all = new List<System.Type> { typeof(RectTransform) };
            foreach (var c in comps) if (c != typeof(RectTransform)) all.Add(c);
            var go = new GameObject(name, all.ToArray());
            go.transform.SetParent(parent.transform, false);
        }
    }
}
