using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Reference;
using LiteGame.Editor.UiTools.Validation;
using LiteGame.Editor.UiTools.Validation.Rules;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 契约族补全面（工具链设计 §6.2「契约」+ §5.2）：漂移对账（重生成逐字节比较，含手改检出）、
    /// 引用对账（未消费/越界）、文本双写，以及生成器的清单维护字段投影与 events[] 应用。
    /// 漂移用例走真实 Page_Login（生成器路径守卫辖内）；引用/双写用合成清单+合成契约+合成 Lua 注入。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiContractReconciliationEditModeTests
    {
        private const string TempDir = "Assets/TempUiContractTests";
        private const string RealLoginPage = "Assets/ShareResource/UI/Screens/Page_Login.prefab";
        private const string RealLoginContract = "Assets/LiteGame/Editor/UiTools/Contracts/Page_Login.contract.json";

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

        private static string FullPath(string assetPath)
            => Path.Combine(UiValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

        // ---- ui.contract.drift ----

        [Test]
        public void 漂移_手改契约与prefab重生成不一致报Error()
        {
            UiPageContract tampered = UiContractStore.Load(RealLoginContract);
            if (tampered.Controls.Count > 0) tampered.Controls[0].Required = !tampered.Controls[0].Required;
            string tamperedPath = TempDir + "/Page_Login.tampered.contract.json";
            File.WriteAllText(FullPath(tamperedPath), UiContractJson.Serialize(tampered));

            var manifest = ManifestFor(page =>
            {
                page.Name = "Page_Login";
                page.PrefabPath = RealLoginPage;
                page.Forms.Add(new UiPageFormEntry { FormId = 11, Layer = 0, FullScreen = true });
                page.LuaPath = "UI.Page_Login";
                page.ContractPath = tamperedPath;
                page.ContractPresent = true;
            });

            var issues = new ContractDriftRule().Check(ContextFor(manifest)).ToList();

            var errors = issues.Where(i => i.Severity == UiSeverity.Error).ToList();
            Assert.AreEqual(1, errors.Count, "盘上契约与重生成不一致（含手改）须报 Error：\n" + string.Join("\n", issues));
            Assert.AreEqual(UiRuleIds.ContractDrift, errors[0].RuleId);
            StringAssert.Contains("Page_Login", errors[0].Asset ?? errors[0].Message, "问题归属页面");
        }

        [Test]
        public void 漂移_契约与prefab一致零误报()
        {
            var manifest = ManifestFor(page =>
            {
                page.Name = "Page_Login";
                page.PrefabPath = RealLoginPage;
                page.Forms.Add(new UiPageFormEntry { FormId = 11, Layer = 0, FullScreen = true });
                page.LuaPath = "UI.Page_Login";
                page.ContractPath = RealLoginContract;
                page.ContractPresent = true;
            });

            var issues = new ContractDriftRule().Check(ContextFor(manifest)).ToList();

            CollectionAssert.IsEmpty(issues.Where(i => i.Severity == UiSeverity.Error),
                "重生成与盘上一致须零 Error：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 漂移_无契约页不重复报_由页面清单规则承担()
        {
            var manifest = ManifestFor(page =>
            {
                page.Name = "Page_Login";
                page.PrefabPath = RealLoginPage;
                page.Forms.Add(new UiPageFormEntry { FormId = 11, Layer = 0, FullScreen = true });
                page.ContractPath = RealLoginContract;
                page.ContractPresent = false;               // 缺契约归 ui.page-manifest
            });

            var issues = new ContractDriftRule().Check(ContextFor(manifest)).ToList();

            CollectionAssert.IsEmpty(issues, "contractPresent=false 的页面本规则不辖：\n" + string.Join("\n", issues));
        }

        // ---- ui.contract.reference ----

        [Test]
        public void 引用对账_未消费报Warning_越界报Error_面内引用零误报()
        {
            string luaPath = WritePageLua("Page_Demo.lua", string.Join("\n",
                "function M:OnShow()",
                "    self.ui:OnButton(\"Button_Go\", function() self:_Go() end)",
                "    self.ui:SetVisible(\"Txt_Title\", false)",
                "    self.ui:OnButton(\"Button_Ghost\", function() end)",
                "end"));
            string manifestPath = WriteDemoManifest();

            var rule = new ContractReferenceRule(_ => luaPath);
            var issues = rule.Check(ContextFor(manifestPath)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Warning),
                "required 控件未被 Lua 字面量消费须报 Warning（Button_Idle）：\n" + string.Join("\n", issues));
            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error),
                "调用点名不在契约面须报 Error（Button_Ghost）：\n" + string.Join("\n", issues));
            Assert.IsTrue(issues.All(i => i.RuleId == UiRuleIds.ContractReference));
            Assert.AreEqual(0, issues.Count(i => (i.Message ?? string.Empty).Contains("Button_Go")),
                "面内消费（Button_Go）零误报");
        }

        // ---- ui.contract.text-double-write ----

        [Test]
        public void 文本双写_静态文案节点被Lua再写报Error()
        {
            string luaPath = WritePageLua("Page_Demo.lua",
                "self.ui:SetTextKey(\"Txt_Title\", \"UI.Test.Hello\")");
            var rule = new ContractTextDoubleWriteRule(_ => luaPath);

            var issues = rule.Check(ContextFor(WriteDemoManifest())).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Severity == UiSeverity.Error),
                "LTextKeyRef 节点被 Lua SetTextKey 再写须报 Error：\n" + string.Join("\n", issues));
            Assert.AreEqual(UiRuleIds.ContractTextDoubleWrite, issues[0].RuleId);
            StringAssert.Contains("Txt_Title", issues[0].Message, "报文点名双写节点");
        }

        [Test]
        public void 文本双写_无再写零误报()
        {
            string luaPath = WritePageLua("Page_Demo.lua",
                "self.ui:OnButton(\"Button_Go\", function() end)");
            var rule = new ContractTextDoubleWriteRule(_ => luaPath);

            var issues = rule.Check(ContextFor(WriteDemoManifest())).ToList();

            CollectionAssert.IsEmpty(issues, "无文本写入零误报：\n" + string.Join("\n", issues));
        }

        // ---- 生成器：清单维护字段投影 + events[] 应用 ----

        [Test]
        public void 生成器_清单维护字段投影进契约()
        {
            var contract = NewDemoContract();
            var entry = new UiPageManifestEntry { Owner = "art.ui", DesignRef = "Figma:demo" };

            UiContractGenerator.ApplyManifestEditorial(contract, entry);

            Assert.AreEqual("art.ui", contract.Page.Owner, "Owner 从页面清单投影");
            Assert.AreEqual("Figma:demo", contract.Page.DesignRef, "DesignRef 从页面清单投影");

            UiContractGenerator.ApplyManifestEditorial(contract, null);
            Assert.IsNull(contract.Page.Owner, "清单缺条目＝不臆造（null 透传，调用方告警）");
        }

        [Test]
        public void 生成器_events应用_接线进契约并稳定排序()
        {
            var contract = NewDemoContract();
            string hashBefore = contract.SourceHash;

            UiContractGenerator.ApplyEvents(contract, new List<UiLuaEventWiring>
            {
                new UiLuaEventWiring { Api = "OnButton", Event = "onClick", Name = "Button_Go", Handler = "_Go" },
                new UiLuaEventWiring { Api = "OnButton", Event = "onClick", Name = "Button_Aa", Handler = "anonymous" },
            });

            Assert.AreEqual(2, contract.Events.Count, "接线须进 events[]");
            Assert.AreEqual("Button_Aa", contract.Events[0].BindName, "按 bindName|event 稳定排序");
            Assert.AreEqual("onClick", contract.Events[0].Event);
            Assert.AreEqual("anonymous", contract.Events[0].Handler);
            Assert.AreNotEqual(hashBefore, contract.SourceHash, "event 行入 sourceHash（§5.2.1 口径）——重算");

            // 幂等：同一接线集合重复应用结果一致
            string hashOnce = contract.SourceHash;
            UiContractGenerator.ApplyEvents(contract, new List<UiLuaEventWiring>
            {
                new UiLuaEventWiring { Api = "OnButton", Event = "onClick", Name = "Button_Aa", Handler = "anonymous" },
                new UiLuaEventWiring { Api = "OnButton", Event = "onClick", Name = "Button_Go", Handler = "_Go" },
            });
            Assert.AreEqual(hashOnce, contract.SourceHash, "接线集合同内容不同顺序——结果须一致");
        }

        [Test]
        public void 生成器_真实Page_Login经清单投影与接线_契约含owner与events()
        {
            // 全链真实面：表行 + 真实清单 → 生成（投影 + Lua 接线）——契约补全的端到端钉
            var rows = UiPageScaffold.ListPages();
            var manifest = UiPageManifestStore.Load();
            var entry = manifest.Pages.FirstOrDefault(p => p.Name == "Page_Login");
            Assert.IsNotNull(entry, "真实清单须有 Page_Login");

            UiContractGenerationResult result = UiContractBatch.GenerateRegistered(RealLoginPage, rows, manifest);

            Assert.IsTrue(result.Ok, "生成须成功：" + result.ErrorText);
            Assert.AreEqual("art.ui", result.Contract.Page.Owner, "owner 从清单投影");
            Assert.IsNotNull(result.Contract.Events.FirstOrDefault(e =>
                e.BindName == "Button_GuestLogin" && e.Event == "onClick"), "登录钮接线须进 events[]");
        }

        // ---- 辅助 ----

        private static UiPageContract NewDemoContract() => new UiPageContract
        {
            Page = new UiContractPage
            {
                FormId = 900,
                Name = "Page_Demo",
                PrefabPath = RealLoginPage,
                LuaPath = "UI.Page_Demo",
                Layer = 0,
                FullScreen = true,
            },
            Controls =
            {
                new UiContractControl { BindName = "Button_Go", NodePath = "Button_Go", Type = "UnityEngine.UI.Button", Kind = "Command", Required = true },
                new UiContractControl { BindName = "Button_Idle", NodePath = "Button_Idle", Type = "UnityEngine.UI.Button", Kind = "Command", Required = true },
            },
            Text =
            {
                new UiContractTextBinding { NodePath = "Cot_Header/Txt_Title", Key = "UI.Test.Hello", Args = 0 },
            },
        };

        private static UiValidationContext ContextFor(string manifestPath)
            => new UiValidationContext(UiValidator.ProjectRoot, null, new string[0], pageManifestPath: manifestPath);

        private static string ManifestFor(System.Action<UiPageManifestEntry> build)
        {
            var manifest = new UiPageManifest();
            var page = new UiPageManifestEntry();
            build(page);
            manifest.Pages.Add(page);
            return WriteManifest(manifest);
        }

        /// <summary>合成 Demo 页（契约面＝Button_Go/Button_Idle + 静态 Txt_Title），供引用/双写用例注入。</summary>
        private static string WriteDemoManifest()
        {
            var manifest = new UiPageManifest();
            var page = new UiPageManifestEntry
            {
                Name = "Page_Demo",
                PrefabPath = RealLoginPage,
                LuaPath = "UI.Page_Demo",
                ContractPresent = true,
            };
            page.Forms.Add(new UiPageFormEntry { FormId = 900, Layer = 0, FullScreen = true });
            page.ContractPath = TempDir + "/Page_Demo.contract.json";
            manifest.Pages.Add(page);

            File.WriteAllText(FullPath(page.ContractPath), UiContractJson.Serialize(NewDemoContract()));
            return WriteManifest(manifest);
        }

        private static string WriteManifest(UiPageManifest manifest)
        {
            string path = TempDir + "/manifest-" + System.Guid.NewGuid().ToString("N").Substring(0, 8) + ".json";
            File.WriteAllText(FullPath(path), LiteGame.Editor.UiTools.Page.UiPageManifestJson.Serialize(manifest));
            return path;
        }

        private static string WritePageLua(string fileName, string content)
        {
            string path = TempDir + "/" + fileName;
            File.WriteAllText(FullPath(path), content);
            return path;
        }
    }
}
