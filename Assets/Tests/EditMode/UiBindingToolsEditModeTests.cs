using System.IO;
using System.Linq;
using System.Reflection;
using LiteCodeGen;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 绑定标记工具（UI 工具链步 3，《UI编辑器工具专项设计》§2.1）的引擎面覆盖：
    /// 前缀组合（§9.5）、改名计划的同文联动（§9.1）、批量换前缀、重名挡在应用前、
    /// 应用落位（深度序——父子同批不互踩）、引用点扫描（§9.2 改名联动清单）、
    /// 生成物 hash（§2.1.5：稳定/敏感/嵌套根独立/记录-校验闭环 + 规则接入校验核心）。
    /// 窗口是引擎之上的 IMGUI 窗口壳——此处只做绑定/建树的冒烟，交互面属手测。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiBindingToolsEditModeTests
    {
        private const string TempDir = "Assets/TempUiBindingToolsTests";

        [SetUp]
        public void EnsureTempDir()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "TempUiBindingToolsTests");
        }

        [TearDown]
        public void CleanupTempDir()
        {
            if (AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.DeleteAsset(TempDir);
        }

        // ---- 前缀组合（§9.5"选前缀、只填语义名"） ----

        [Test]
        public void 组合_词表前缀加语义_过文法()
        {
            Assert.IsTrue(UiBindingRename.TryComposeName("Button_", "Close", out string name, out _), "词表前缀 + PascalCase 语义应组合成功");
            Assert.AreEqual("Button_Close", name);
            Assert.IsTrue(UiBindingRename.MatchesGrammar(name), "组合结果须过 §9.2 文法（构造性合规）");

            Assert.IsFalse(UiBindingRename.TryComposeName("Lst_", "Foo", out _, out string reason1), "自造前缀不得组合");
            Assert.IsNotEmpty(reason1);
            Assert.IsFalse(UiBindingRename.TryComposeName("Txt_", "", out _, out _), "空语义不得组合");
            Assert.IsFalse(UiBindingRename.TryComposeName("Txt_", "9x", out _, out _), "语义不得以数字起头");
            Assert.IsFalse(UiBindingRename.TryComposeName("Txt_", "A_b", out _, out _), "语义不得含下划线（全名恰一个下划线）");
            Assert.IsFalse(UiBindingRename.TryComposeName("Txt_", "中", out _, out _), "语义须 ASCII");
        }

        // ---- 改名计划：同文联动三形态（§9.1） ----

        [Test]
        public void 改名计划_同文联动三形态()
        {
            var root = new GameObject("Page_Plan", typeof(RectTransform));
            try
            {
                var synced = NewChild(root, "Button_Ok");
                synced.AddComponent<BindNode>().BindName = "Button_Ok";
                var planSynced = UiBindingRename.PlanRename(synced.transform, "Button_Close");
                Assert.IsTrue(planSynced.BindNameChanges, "同文名节点：BindName 须随节点名同批改");
                Assert.AreEqual("Button_Close", planSynced.NewBindName);

                var empty = NewChild(root, "Cot_Panel");
                empty.AddComponent<BindNode>().BindName = "";               // 模板标记：不预设索引名（§9.4）
                var planEmpty = UiBindingRename.PlanRename(empty.transform, "Cot_Board");
                Assert.IsFalse(planEmpty.BindNameChanges, "空 BindName 不得被改名发明索引名");

                var drift = NewChild(root, "Button_X");
                drift.AddComponent<BindNode>().BindName = "LegacyKey";      // 既有不同文漂移
                var planDrift = UiBindingRename.PlanRename(drift.transform, "Button_Fix");
                Assert.AreEqual("Button_Fix", planDrift.NewBindName, "漂移面按构造性同文收口（§9.1：与节点名同文必须）");

                var plain = NewChild(root, "Bg_Board");
                var planPlain = UiBindingRename.PlanRename(plain.transform, "Bg_Base");
                Assert.IsFalse(planPlain.HasBindNode, "非绑定节点：仅改名，无索引联动");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---- 批量换前缀 ----

        [Test]
        public void 批量换前缀_保留语义_文法外跳过()
        {
            var root = new GameObject("Page_Batch", typeof(RectTransform));
            try
            {
                var ok = NewChild(root, "Button_Ok");
                var noop = NewChild(root, "Txt_Title");
                NewChild(root, "A_B_C");      // 语义段含下划线——跳过
                NewChild(root, "Lst_Foo");    // 自造前缀——跳过

                var plans = UiBindingRename.PlanRetargetPrefix(new[]
                {
                    ok.transform, noop.transform,
                    root.transform.Find("A_B_C"), root.transform.Find("Lst_Foo"),
                }, "Txt_");

                Assert.AreEqual(3, plans.Count, "可解析语义且目标前缀合法者皆可换前缀（含自造段治愈为词表段）；语义段非法者跳过");
                Assert.AreEqual("Txt_Ok", plans.First(p => p.OldName == "Button_Ok").NewName);
                Assert.AreEqual("Txt_Title", plans.First(p => p.OldName == "Txt_Title").NewName, "同前缀名换前缀＝无变化计划（幂等面）");
                Assert.AreEqual("Txt_Foo", plans.First(p => p.OldName == "Lst_Foo").NewName, "自造前缀的合法语义段可治愈到词表段");
                Assert.IsFalse(plans.Any(p => p.OldName == "A_B_C"), "语义段含下划线（B_C）者不得参与");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---- 计划校验：重名挡在应用前 ----

        [Test]
        public void 计划校验_重名冲突挡在应用前()
        {
            var root = new GameObject("Page_Dup", typeof(RectTransform));
            try
            {
                var a = NewChild(root, "Button_One");
                var b = NewChild(root, "Button_Two");
                var occupied = NewChild(root, "Txt_Title");

                var plans = new[]
                {
                    UiBindingRename.PlanRename(a.transform, "Txt_Same"),
                    UiBindingRename.PlanRename(b.transform, "Txt_Same"),
                    UiBindingRename.PlanRename(occupied.transform, "Txt_New"),
                };
                var errors = UiBindingRename.ValidatePlans(root, plans);
                Assert.AreEqual(1, errors.Count, "同批重名必须报错：\n" + string.Join("\n", errors));
                StringAssert.Contains("重名冲突", errors[0]);

                var collide = UiBindingRename.ValidatePlans(root, new[]
                {
                    UiBindingRename.PlanRename(a.transform, "Txt_New2"),   // 兄弟已名 Txt_New?——构造独名再验占用面
                });
                Assert.AreEqual(0, collide.Count);

                var taken = UiBindingRename.ValidatePlans(root, new[]
                {
                    UiBindingRename.PlanRename(b.transform, "Txt_Title"),  // 现存兄弟同名——不得压上
                });
                Assert.AreEqual(1, taken.Count, "新名被现存兄弟占用必须报错");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---- 应用落位：深度序（父改名不踩子路径） + BindName 同步 ----

        [Test]
        public void 应用计划_父子同批不互踩_落位且同文()
        {
            var prefab = MakeTempPrefab("Page_Apply", root =>
            {
                var panel = NewChild(root, "Cot_Panel");
                panel.AddComponent<BindNode>().BindName = "Cot_Panel";
                var button = NewChild(panel, "Button_Ok");
                button.AddComponent<BindNode>().BindName = "Button_Ok";
            });
            string path = AssetDatabase.GetAssetPath(prefab);

            // 计划在改前树上生成（父子同批——深层先应用）
            var plans = new[]
            {
                UiBindingRename.PlanRename(prefab.transform.Find("Cot_Panel"), "Cot_Board"),
                UiBindingRename.PlanRename(prefab.transform.Find("Cot_Panel/Button_Ok"), "Button_Close"),
            };
            Assert.AreEqual(0, UiBindingRename.ValidatePlans(prefab, plans).Count, "合法计划不应有校验错");

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                UiBindingRename.ApplyPlans(contents, plans);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var renamedPanel = reloaded.transform.Find("Cot_Board");
            Assert.IsNotNull(renamedPanel, "父改名落位");
            var renamedButton = renamedPanel.Find("Button_Close");
            Assert.IsNotNull(renamedButton, "子在其父改名后仍被应用（深度序）");
            Assert.AreEqual("Cot_Board", renamedPanel.GetComponent<BindNode>().BindName, "BindName 同文同步");
            Assert.AreEqual("Button_Close", renamedButton.GetComponent<BindNode>().BindName, "BindName 同文同步");
        }

        // ---- 引用点扫描（§9.2 改名联动清单） ----

        [Test]
        public void 引用扫描_命中cs与lua字面量_带行号()
        {
            string dir = FullTempDir();
            File.WriteAllText(Path.Combine(dir, "Sample.cs"),
                "var a = nav.OnButton(\"ScanMe\", fn);\n");
            File.WriteAllText(Path.Combine(dir, "Page.lua"),
                "self.ui:Pulse(\"ScanMe\")\n");

            var hits = UiBindingReferenceScan.Scan("ScanMe", new[] { TempDir });
            Assert.AreEqual(2, hits.Count, "cs + lua 各一处");
            Assert.IsTrue(hits.All(h => h.Line == 1), "行号 1 起");
            StringAssert.DoesNotContain("ScanMe2", string.Join("|", hits.Select(h => h.Text)));

            var none = UiBindingReferenceScan.Scan("NoSuchName_Xyz", new[] { TempDir });
            Assert.AreEqual(0, none.Count, "无字面量引用＝空清单");
        }

        [Test]
        public void 引用扫描_真实Lua引用面命中()
        {
            var hits = UiBindingReferenceScan.Scan("Button_Close");   // 默认扫描根（Lua/UI/App/Tests）
            Assert.IsNotEmpty(hits, "UiTestPage.lua 的 OnButton/Flash(\"Button_Close\") 必须被清单列出");
            Assert.IsTrue(hits.Any(h => h.File.EndsWith("Lua/Tests/UiTestPage.lua")), "命中面包含 Lua 测试载体");
        }

        // ---- 生成物 hash（§2.1.5） ----

        [Test]
        public void hash_同内容稳定_绑定面敏感_嵌套根独立()
        {
            var prefab = MakeTempPrefab("Page_Hash", root =>
            {
                var bind = NewChild(root, "Button_Ok");
                bind.AddComponent<BindNode>().BindName = "Button_Ok";
                var nestedRootGo = NewChild(root, "Cot_Nested");
                nestedRootGo.AddComponent<BindRoot>().ScriptName = "NestedUnit";
                var nestedBind = NewChild(nestedRootGo, "Txt_Inner");
                nestedBind.AddComponent<BindNode>().BindName = "Txt_Inner";
            });
            string path = AssetDatabase.GetAssetPath(prefab);

            string h1 = UiBindGenerationHash.ComputeHash(prefab);
            string h2 = UiBindGenerationHash.ComputeHash(
                AssetDatabase.LoadAssetAtPath<GameObject>(path));
            Assert.AreEqual(h1, h2, "同内容必同 hash（重新装载亦然）");

            string pathAgain = path;
            var contents = PrefabUtility.LoadPrefabContents(pathAgain);
            try
            {
                // 嵌套生成根子树改动：不属本根的绑定面——hash 不变（BindSearch 同一作用域语义）
                contents.transform.Find("Cot_Nested/Txt_Inner").GetComponent<BindNode>().BindName = "Txt_Changed";
                string unchanged = UiBindGenerationHash.ComputeHash(contents);
                Assert.AreEqual(h1, unchanged, "嵌套 BindRoot 子树不入本根指纹");

                contents.transform.Find("Button_Ok").GetComponent<BindNode>().BindName = "Button_Moved";
                string changed = UiBindGenerationHash.ComputeHash(contents);
                Assert.AreNotEqual(h1, changed, "本根绑定面任何变更必须改 hash");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        [Test]
        public void hash_记录校验闭环_规则接入核心()
        {
            string manifest = TempDir + "/manifest.json";
            string path = null;
            var prefab = MakeTempPrefab("Page_HashRule", root =>
            {
                var bindRoot = root.AddComponent<BindRoot>();
                bindRoot.ScriptName = "HashRuleGen";
                bindRoot.ScriptsFolder = TempDir + "/Gen";
                var bind = NewChild(root, "Button_Ok");
                bind.AddComponent<BindNode>().BindName = "Button_Ok";
            });
            path = AssetDatabase.GetAssetPath(prefab);
            Directory.CreateDirectory(Path.Combine(FullTempDir(), "Gen"));
            File.WriteAllText(Path.Combine(FullTempDir(), "Gen", "HashRuleGen.cs"), "// gen");
            File.WriteAllText(Path.Combine(FullTempDir(), "Gen", "HashRuleGen.Designer.cs"), "// gen");

            var context = new UiValidationContext(UiValidator.ProjectRoot,
                assetPaths: new[] { path }, bindManifestPath: manifest);
            Assert.IsTrue(UiBindGenerationHash.Check(new[] { path }, manifest).Any(i => i.Kind == UiBindHashIssueKind.NotRecorded),
                "未记录＝显红，不冒充通过");

            UiBindGenerationHash.Record(path, manifest);
            Assert.AreEqual(0, HashIssuesIn(UiValidator.Run(context)).Count, "记录后对账干净");

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.transform.Find("Button_Ok").GetComponent<BindNode>().BindName = "Button_Drift";
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            var stale = HashIssuesIn(UiValidator.Run(context));
            Assert.AreEqual(1, stale.Count, "源改了没重记录＝Stale Error");
            StringAssert.Contains("Stale", stale[0].Message);

            UiBindGenerationHash.Record(path, manifest);
            Assert.AreEqual(0, HashIssuesIn(UiValidator.Run(context)).Count, "重记录后恢复干净");

            File.Delete(Path.Combine(FullTempDir(), "Gen", "HashRuleGen.cs"));
            var missing = HashIssuesIn(UiValidator.Run(context));
            Assert.AreEqual(1, missing.Count, "生成物文件缺失＝Error");
            StringAssert.Contains("GeneratedMissing", missing[0].Message);
        }

        [Test]
        public void hash_孤儿条目_按Warning报()
        {
            string manifest = TempDir + "/manifest.json";
            var prefab = MakeTempPrefab("Page_Orphan", root =>
            {
                var bind = NewChild(root, "Button_Ok");
                bind.AddComponent<BindNode>().BindName = "Button_Ok";
            });
            string path = AssetDatabase.GetAssetPath(prefab);
            UiBindGenerationHash.Record(path, manifest);

            AssetDatabase.DeleteAsset(path);
            var issues = UiBindGenerationHash.Check(new string[0], manifest);
            Assert.AreEqual(1, issues.Count, "prefab 已删而清单残留＝OrphanEntry");
            Assert.AreEqual(UiBindHashIssueKind.OrphanEntry, issues[0].Kind);
        }

        // ---- 规则端口 ----

        [Test]
        public void hash规则_端口标识与族()
        {
            var rule = new UiBindingHashRule();
            Assert.AreEqual("ui.binding-hash", rule.RuleId);
            Assert.AreEqual("命名/绑定", rule.Family);      // P1-4 族名对齐九族表后的口径
        }

        // ---- 窗口壳冒烟（引擎建树经公开绑定口） ----

        [Test]
        public void 窗口_绑定prefab构建树_不抛()
        {
            var prefab = MakeTempPrefab("Page_Window", root =>
            {
                var panel = NewChild(root, "Cot_Panel");
                NewChild(panel, "Button_Ok");
            });

            var editor = new UiBindingEditor();
            editor.BindPrefab(prefab);
            var rows = editor.Rows;
            Assert.AreEqual(3, rows.Count, "根 + Cot + Button 三行");
            Assert.AreEqual("Button_Ok", rows[2].Name);
            Assert.AreEqual("Cot_Panel/Button_Ok", rows[2].Path, "行路径与生成/报告定位口径一致");
        }

        // ---- 辅助 ----

        private static string FullTempDir()
            => Path.Combine(UiValidator.ProjectRoot, TempDir.Replace('/', Path.DirectorySeparatorChar));

        private static GameObject MakeTempPrefab(string name, System.Action<GameObject> build)
        {
            string path = $"{TempDir}/{name}.prefab";
            var go = new GameObject(name, typeof(RectTransform));
            build(go);
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static System.Collections.Generic.List<UiIssue> HashIssuesIn(UiReport report)
            => report.Issues.Where(i => i.RuleId == "ui.binding-hash").ToList();
    }
}
