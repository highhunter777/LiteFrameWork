using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Toolchain;
using NUnit.Framework;
using UnityEditor;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-5/P1-6 收口：契约与清单能力必须有工具入口（Hub/CLI），生成物必须有可对账基线，
    /// 试点页的静态文案必须由 <see cref="LiteClient.UI.LTextKeyRef"/> 声明而不是 Lua 动态写。
    /// 只断言可观察面（目录/清单/契约产物），不依赖工具内部实现。
    /// </summary>
    public sealed class UiToolchainWiringEditModeTests
    {
        private static readonly string[] ContractToolIds =
        {
            "ui.contract",
            "ui.contract-diff",
            "ui.widget-manifest",
            "ui.page-manifest",
        };

        private static readonly string PilotPagePath = "Assets/ShareResource/UI/Screens/Page_Login.prefab";

        private static readonly string ProjectRoot = ToolchainManifestLoader.ProjectRoot;

        private static bool IsContractEligible(string pageName)
        {
            return pageName.StartsWith("Page_", StringComparison.Ordinal)
                || pageName.StartsWith("Hud_", StringComparison.Ordinal)
                || pageName.StartsWith("Tmp_", StringComparison.Ordinal);
        }

        private static List<UiPageManifestEntry> LoadEligiblePages()
        {
            UiPageManifest manifest = UiPageManifestStore.Load();
            return manifest.Pages
                .Where(entry => entry != null && IsContractEligible(entry.Name))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();
        }

        [Test]
        public void 工具面_四个契约与清单动作已登记且可执行()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            Assert.That(catalog.Errors, Is.Empty, string.Join("\n", catalog.Errors));

            foreach (string id in ContractToolIds)
            {
                EditorToolDescriptor descriptor = catalog.Find(id);
                Assert.That(descriptor, Is.Not.Null, id + " 未在 scripts/gate/toolchain.json 登记");
                Assert.That(descriptor.Kind, Is.EqualTo(ToolchainKinds.Action), id + " 必须是 action");
                Assert.That(descriptor.Plane, Is.EqualTo(ToolchainPlanes.Editor), id + " 必须是 plane=editor");
                Assert.That(descriptor.Execute, Is.Not.Null, id + " 缺少 handler");

                bool writes = id != "ui.contract-diff";
                if (writes)
                {
                    Assert.That(descriptor.Tool.Writes.Any(scope => scope != "none"), Is.True,
                        id + " 应声明写入面");
                    Assert.That(descriptor.OutputPaths.Length, Is.GreaterThan(0),
                        id + " 是写入者，必须登记产物形态（outputs）");
                }
            }
        }

        [Test]
        public void 试点页_Page_Login_静态文案三条进入契约()
        {
            UiContractGenerationResult result = UiContractGenerator.GenerateRegistered(PilotPagePath);
            Assert.That(result.Ok, Is.True, result.ErrorText);

            string[] keys = result.Contract.Text
                .Select(item => item.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                new[] { "Common.Settings", "UI.Login.GuestEnter", "UI.Login.Title" },
                keys,
                "试点页静态文案应恰为三条（标题/游客进入/设置）");
        }

        [Test]
        public void 试点页_Lua_不再动态写静态文案()
        {
            string path = Path.Combine(ProjectRoot, "Assets/LiteGame/Lua/UI/Page_Login.lua");
            Assert.That(File.Exists(path), Is.True, "Lua 页面不存在：" + path);
            string lua = File.ReadAllText(path);

            Assert.That(lua.Contains("SetTextKey(\"Txt_Title\""), Is.False,
                "Txt_Title 已挂 LTextKeyRef，Lua 不得再写（双写分叉）");
            Assert.That(lua.Contains("SetTextKey(\"Txt_GuestLogin\""), Is.False,
                "Txt_GuestLogin 已挂 LTextKeyRef，Lua 不得再写");
            Assert.That(lua.Contains("SetTextKey(\"Txt_Settings\""), Is.False,
                "Txt_Settings 已挂 LTextKeyRef，Lua 不得再写");
            Assert.That(lua.Contains("SetTextKeyArgs(\"Txt_Version\""), Is.True,
                "Txt_Version 带参数，仍由 Lua 写");
            Assert.That(lua.Contains("SetTextKey(\"Txt_Status\""), Is.True,
                "Txt_Status 随状态变化，仍由 Lua 写");
        }

        [Test]
        public void 契约基线_每个可生成页已入库()
        {
            List<UiPageManifestEntry> pages = LoadEligiblePages();
            Assert.That(pages.Count, Is.EqualTo(11), "UIMain 退役后应恰有 11 个可生成页");

            foreach (UiPageManifestEntry entry in pages)
            {
                string contractPath = UiContractPaths.ForPage(entry.Name);
                Assert.That(File.Exists(UiContractPaths.FullPath(contractPath)), Is.True,
                    entry.Name + " 缺少契约基线：" + contractPath);
            }
        }

        [Test]
        public void 页面清单_契约入库后contractPresent为真()
        {
            List<UiPageManifestEntry> pages = LoadEligiblePages();
            Assert.That(pages.Count, Is.EqualTo(11), "UIMain 退役后应恰有 11 个可生成页");

            foreach (UiPageManifestEntry entry in pages)
            {
                Assert.That(entry.ContractPresent, Is.True, entry.Name + " 的 contractPresent 仍为假");
                Assert.That(File.Exists(UiContractPaths.FullPath(entry.ContractPath)), Is.True,
                    entry.Name + " 的 contractPath 不指向真实文件");
            }
        }

        [Test]
        public void 批量生成_常驻壳跳过并告警_其余页面写盘_重复生成零diff()
        {
            const string tempDir = "Assets/LiteGame/Editor/UiTools/ContractsTestTemp";
            EnsureFolder(tempDir);
            try
            {
                UiContractBatchResult first = UiContractBatch.Generate(tempDir);
                Assert.That(first.Ok, Is.True, string.Join("\n", first.Errors));
                Assert.That(first.Written.Count, Is.EqualTo(11), "应写入 11 份页面契约");
                Assert.That(first.Skipped, Is.Empty, "UIMain 退役后不应再有非常驻壳登记行");
                Assert.That(first.Warnings.Any(item => item.Contains("多行登记")), Is.True,
                    "多行登记应告警（Tmp_Dialog 201/206）");

                var bytes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (UiContractBatchEntry entry in first.Written)
                    bytes[entry.ContractPath] = File.ReadAllText(UiContractPaths.FullPath(entry.ContractPath));

                UiContractBatchResult second = UiContractBatch.Generate(tempDir);
                Assert.That(second.Ok, Is.True, string.Join("\n", second.Errors));
                foreach (UiContractBatchEntry entry in second.Written)
                {
                    Assert.That(File.ReadAllText(UiContractPaths.FullPath(entry.ContractPath)),
                        Is.EqualTo(bytes[entry.ContractPath]), entry.PageName + " 重复生成出现 diff");
                }
            }
            finally
            {
                DeleteFolder(tempDir);
            }
        }

        [Test]
        public void 批量对账_无基线只告警_破坏性变更判失败()
        {
            const string tempDir = "Assets/LiteGame/Editor/UiTools/ContractsDiffTestTemp";
            EnsureFolder(tempDir);
            try
            {
                UiContractBatchDiffResult noBaseline = UiContractBatch.Diff(tempDir);
                Assert.That(noBaseline.Errors, Is.Empty, string.Join("\n", noBaseline.Errors));
                Assert.That(noBaseline.MissingBaseline.Count, Is.EqualTo(11), "空基线目录应逐个报告无基线");
                Assert.That(noBaseline.BreakingCount, Is.Zero);

                UiContractBatchResult generated = UiContractBatch.Generate(tempDir);
                Assert.That(generated.Ok, Is.True, string.Join("\n", generated.Errors));

                UiPageContract login = UiContractStore.Load(UiContractPaths.ForPage("Page_Login"));
                string tempLoginPath = tempDir + "/Page_Login.contract.json";
                login.Controls.Add(new UiContractControl
                {
                    BindName = "Button_Ghost",
                    NodePath = "Cot_SafeArea/Button_Ghost",
                    Type = "UnityEngine.UI.Button",
                    Kind = "Command",
                    Required = true,
                });
                UiContractStore.Save(tempLoginPath, login);

                UiContractBatchDiffResult broken = UiContractBatch.Diff(tempDir);
                Assert.That(broken.BreakingCount, Is.GreaterThan(0), "基线多出的控件（现页面已不存在）应判破坏性变更");
                Assert.That(broken.Ok, Is.False);
            }
            finally
            {
                DeleteFolder(tempDir);
            }
        }

        [Test]
        public void 起页对账_注入清单路径可落盘()
        {
            const string tempDir = "Assets/Tests/TempUiWiring";
            const string tempManifest = tempDir + "/UiPageManifestWiringTest.json";
            EnsureFolder(tempDir);
            try
            {
                string message = UiPageScaffold.ReconcilePageManifest(tempManifest);
                Assert.That(message, Does.Contain("已对账"), "起页对账应返回可读结论：" + message);
                Assert.That(File.Exists(UiPageManifestPaths.FullPath(tempManifest)), Is.True,
                    "对账结果应写到注入路径");

                UiPageManifest manifest = UiPageManifestStore.Load(tempManifest);
                Assert.That(manifest.Pages.Count, Is.GreaterThan(0), "对账清单不得为空");
            }
            finally
            {
                if (File.Exists(UiPageManifestPaths.FullPath(tempManifest)))
                    AssetDatabase.DeleteAsset(tempManifest);
                DeleteFolder(tempDir);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void DeleteFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
        }
    }
}
