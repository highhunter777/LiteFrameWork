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
        public void 门禁_规则族全部接入登记_待接入子项描述含原因()
        {
            var report = UiValidator.Run(ContextWithSource("var x = 1;\n"));
            var pending = report.Families.Where(f => !f.Implemented).Select(f => f.Family).ToList();
            CollectionAssert.IsEmpty(pending, "十族须全部接入（本批收口后无整族待接入；"
                + "族内未落地子项在描述里列明原因，不再以整族挂待接入）：\n" + string.Join(",", pending));

            // 已接入族描述不得退化为空话——未落地子项必须写明原因（结构/资源/命名族等部分接入的先例口径）
            foreach (var family in report.Families.Where(f => f.Implemented))
            {
                Assert.IsNotEmpty(family.Note, "已接入族的描述不得为空");
            }
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
            Assert.AreEqual("命名/绑定", rule.Family);
        }

        [Test]
        public void 绑定命名_合法名通过_违规名报错()
        {
            var ok = RunOnTempPrefab("Tmp_Fixture", root =>
            {
                AddChild(root, "Cot_Panel", typeof(RectTransform));
                AddChild(root, "Txt_Title", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            });
            Assert.AreEqual(0, ok.Issues.Count(i => i.RuleId == "ui.binding-naming" && i.Severity == UiSeverity.Error), "合法名不应报错：\n" + ok.Render());

            var bad = RunOnTempPrefab("Tmp_Fixture", root =>
            {
                AddChild(root, "A_B_C", typeof(RectTransform));      // 多下划线
                AddChild(root, "Lst_Foo", typeof(RectTransform));    // 自造段（非词表）
            });
            Assert.AreEqual(1, bad.Issues.Count(i => i.RuleId == "ui.binding-naming" && i.Severity == UiSeverity.Error), "多下划线应报 Error：\n" + bad.Render());
            Assert.AreEqual(1, bad.Issues.Count(i => i.RuleId == "ui.binding-naming" && i.Severity == UiSeverity.Warning), "自造段应报 Warning：\n" + bad.Render());
        }

        [Test]
        public void 绑定命名_页面节点禁下划线前缀_模板内允许()
        {
            var page = RunOnTempPrefab("Page_Test", root =>
                AddChild(root, "_Bg", typeof(RectTransform)));
            Assert.AreEqual(1, page.Issues.Count(i => i.RuleId == "ui.binding-naming" && i.Severity == UiSeverity.Error), "页面节点 `_` 前缀应报 Error：\n" + page.Render());

            var tpl = RunOnTempPrefab("Tmp_Test", root =>
                AddChild(root, "_Bg", typeof(RectTransform)));
            Assert.AreEqual(0, tpl.Issues.Count(i => i.RuleId == "ui.binding-naming"), "模板内 `_` 私有件合法：\n" + tpl.Render());
        }

        // ---- 表内资源路径规则（步 5：资源族首条） ----

        [Test]
        public void 表路径_端口标识与族()
        {
            var rule = new AssetTablePathRule();
            Assert.AreEqual("ui.asset-table-paths", rule.RuleId);
            Assert.AreEqual("资源", rule.Family);
        }

        [Test]
        public void 表路径_判据_路径形态识别不误伤()
        {
            Assert.IsTrue(AssetTablePathRule.IsPathLike("ShareResource/UI/Screens/Tmp_Dialog.prefab"));
            Assert.IsTrue(AssetTablePathRule.IsPathLike("Assets/Scenes/Boot.unity"));
            Assert.IsFalse(AssetTablePathRule.IsPathLike("UI.UIMain"), "LuaPath 点分模块名不是资产路径");
            Assert.IsFalse(AssetTablePathRule.IsPathLike(""));
        }

        [Test]
        public void 表路径_核对面_悬空报Error_在盘通过()
        {
            string root = UiValidator.ProjectRoot;
            var entries = new List<AssetTablePathRule.PathEntry>
            {
                new AssetTablePathRule.PathEntry("UI/tbuiform.bytes", "Prefab", "Id=999",
                    "ShareResource/UI/Screens/Tmp_Dialog.prefab"),        // 在盘（真实资产）——不应报
                new AssetTablePathRule.PathEntry("UI/tbuiform.bytes", "Prefab", "Id=998",
                    "ShareResource/UI/Screens/NoSuch.prefab"),           // 悬空（uiform 病类合成形态）
                new AssetTablePathRule.PathEntry("X/dummy.bytes", "Scene", "Id=997",
                    "Assets/Scenes/NoSuch.unity"),                        // 带 Assets/ 前缀形态的悬空
            };

            var issues = AssetTablePathRule.CheckPaths(root, entries, new (string, System.Type)[0]);
            var errors = issues.Where(i => i.Severity == UiSeverity.Error).ToList();
            Assert.AreEqual(2, errors.Count, "两条悬空路径各报一条 Error");
            Assert.IsTrue(errors.All(i => i.RuleId == "ui.asset-table-paths"), "报告项带稳定规则标识");
            StringAssert.Contains("NoSuch.prefab", errors[0].Message, "报文点名悬空值");
            Assert.AreEqual(0, issues.Count(i => i.Node == "Id=999"), "在盘路径零误报");
        }

        [Test]
        public void 表路径_真实表装载提取_零悬空()
        {
            // uiform 悬空两次实锤后的回归钉：现役全部表装载提取，路径形态值全部在盘
            var entries = AssetTablePathRule.ExtractPathEntries(UiValidator.ProjectRoot);
            Assert.IsTrue(entries.Count >= 11, "现役 tbuiform 至少 11 行带 prefab 路径（装载面真实提取），实得 " + entries.Count);
            var issues = AssetTablePathRule.CollectMissingPaths(UiValidator.ProjectRoot);
            Assert.AreEqual(0, issues.Count(i => i.Severity == UiSeverity.Error),
                "真实表零悬空：\n" + string.Join("\n", issues.Select(i => i.ToString())));
        }

        // ---- 门禁接线：真实资产面全量校验（步 5——视觉面由 VisualSingleSourceEditModeTests 既有用例单守，此处不重复扫源码） ----

        [Test]
        public void 门禁_真实资产面_命名hash表路径零Error()
        {
            // 全量规则跑真实数据：Error＝0；Warning＝校准基线（样式对账债——手调色/未标记文本，SHOULD 级可见不阻断；
            // 整改或例外后基线随之更新——P1 边界收口批实测：颜色 161 + 文字 141 + UIMain 根名债 1）
            var assetsOnly = new UiValidationContext(UiValidator.ProjectRoot,
                sourceDirs: null,
                assetPaths: UiValidator.BuildDefaultContext().AssetPaths);
            var report = UiValidator.Run(assetsOnly);

            Assert.AreEqual(0, report.ErrorCount, "真实资产面不得有 Error：\n" + report.Render());
            Assert.AreEqual(0, report.Issues.Count(i => i.RuleId == "ui.binding-naming" && i.Severity == UiSeverity.Error), "命名规则零 Error");
            Assert.AreEqual(0, report.Issues.Count(i => i.RuleId == "ui.binding-hash"), "hash 对账零问题");
            Assert.AreEqual(0, report.Issues.Count(i => i.RuleId == "ui.asset-table-paths" && i.Severity == UiSeverity.Error), "表路径零悬空");

            // 校准钉：文本/引用对账/双写/布局/性能在真实面零问题；Warning 基线＝样式对账债 + UIMain 根名债
            Assert.AreEqual(0, report.Issues.Count(i => i.RuleId == UiRuleIds.TextStaticKey
                || i.RuleId == UiRuleIds.TextPlaceholder || i.RuleId == UiRuleIds.TextLocaleColumn
                || i.RuleId == UiRuleIds.TextRichText || i.RuleId == UiRuleIds.LayoutScaler
                || i.RuleId == UiRuleIds.PerformanceRaycastTarget || i.RuleId == UiRuleIds.PerformanceLayoutDepth
                || i.RuleId == UiRuleIds.PerformanceAnimator || i.RuleId == UiRuleIds.ContractDrift
                || i.RuleId == UiRuleIds.ContractReference || i.RuleId == UiRuleIds.ContractTextDoubleWrite),
                "文本/契约对账/布局/性能族真实面零问题：\n" + report.Render());
            Assert.AreEqual(1, report.Issues.Count(i => i.RuleId == "ui.binding-naming" && i.Severity == UiSeverity.Warning),
                "唯一既有 Warning＝UIMain 根名债（§9.6 登记随页面批次）");
            int colorDrift = report.Issues.Count(i => i.RuleId == UiRuleIds.StyleColorDrift);
            int textDrift = report.Issues.Count(i => i.RuleId == UiRuleIds.StyleTextDrift);
            Assert.AreEqual(161, colorDrift, "颜色对账基线（Screens+Widgets+Hud 手调色——债可见，整改后更新此钉）");
            Assert.AreEqual(141, textDrift, "文字档对账基线（未标记文本——债可见，整改后更新此钉）");
            Assert.AreEqual(303, report.WarningCount, "Warning 总量＝161+141+1（样式债＋UIMain 根名债）");
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
