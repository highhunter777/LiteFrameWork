using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UITools.Contracts;
using LiteGame.Editor.UITools.Page;
using LiteGame.Toolchain;
using NUnit.Framework;
using UnityEditor;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-5/P1-6 收口：契约与清单能力必须有工具入口（Hub/CLI），生成物必须有可对账基线。
    /// </summary>
    public sealed class UIToolchainWiringEditModeTests
    {
        private static readonly string[] ContractToolIds =
        {
            "ui.contract",
            "ui.contract-diff",
            "ui.widget-manifest",
            "ui.page-manifest",
        };

        private static readonly string PilotPagePath = "Assets/LiteGame/Modules/UI/Content/Screens/Page_Login.prefab";

        private static readonly string ProjectRoot = ToolchainManifestLoader.ProjectRoot;

        private static bool IsContractEligible(string pageName)
        {
            return pageName.StartsWith("Page_", StringComparison.Ordinal)
                || pageName.StartsWith("Hud_", StringComparison.Ordinal)
                || pageName.StartsWith("Tmp_", StringComparison.Ordinal);
        }

        private static List<UIPageManifestEntry> LoadEligiblePages()
        {
            UIPageManifest manifest = UIPageManifestStore.Load();
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
        public void 试点页_Page_Login_事件接线进入契约()
        {
            UIContractGenerationResult result = UIContractGenerator.GenerateRegistered(PilotPagePath);
            Assert.That(result.Ok, Is.True, result.ErrorText);

            // P1-2 起静态文案由 C# 页逻辑按 key 写入，prefab 不挂 LTextKeyRef——text 面为空是设计。
            Assert.That(result.Contract.Text, Is.Empty, "prefab 不承载静态文案键（页面逻辑统一写 key）");

            // events[] 由批量链（清单投影 + 页源接线）写入契约基线；单页生成不含接线。
            UIPageContract contract = UIContractStore.Load(UIContractPaths.ForPage("Page_Login"));
            UIContractEventBinding guest = contract.Events
                .SingleOrDefault(item => item.BindName == "Btn_GuestLogin");
            Assert.That(guest, Is.Not.Null, "游客登录按钮必须接线进契约 events[]");
            Assert.That(guest.Event, Is.EqualTo("onClick"));
            Assert.That(guest.Handler, Is.EqualTo("Login"), "handler 取 C# 页方法名（非脚本下划线前缀形）");
        }

        [Test]
        public void 契约基线_每个可生成页已入库()
        {
            List<UIPageManifestEntry> pages = LoadEligiblePages();
            Assert.That(pages.Count, Is.EqualTo(12), "P4 后清单基线为 12 页（13 行）");

            foreach (UIPageManifestEntry entry in pages)
            {
                string contractPath = UIContractPaths.ForPage(entry.Name);
                Assert.That(File.Exists(UIContractPaths.FullPath(contractPath)), Is.True,
                    entry.Name + " 缺少契约基线：" + contractPath);
            }
        }

        [Test]
        public void 页面清单_契约入库后contractPresent为真()
        {
            List<UIPageManifestEntry> pages = LoadEligiblePages();
            Assert.That(pages.Count, Is.EqualTo(12), "P4 后清单基线为 12 页（13 行）");

            foreach (UIPageManifestEntry entry in pages)
            {
                Assert.That(entry.ContractPresent, Is.True, entry.Name + " 的 contractPresent 仍为假");
                Assert.That(File.Exists(UIContractPaths.FullPath(entry.ContractPath)), Is.True,
                    entry.Name + " 的 contractPath 不指向真实文件");
            }
        }

        [Test]
        public void 批量生成_常驻壳跳过并告警_其余页面写盘_重复生成零diff()
        {
            const string tempDir = "Assets/LiteGame/Editor/UITools/ContractsTestTemp";
            EnsureFolder(tempDir);
            try
            {
                UIContractBatchResult first = UIContractBatch.Generate(tempDir);
                Assert.That(first.Ok, Is.True, string.Join("\n", first.Errors));
                Assert.That(first.Written.Count, Is.EqualTo(12), "应写入 12 份页面契约");
                Assert.That(first.Skipped, Is.Empty, "UIMain 退役后不应再有非常驻壳登记行");
                Assert.That(first.Warnings.Any(item => item.Contains("多行登记")), Is.True,
                    "多行登记应告警（Tmp_Dialog 201/206）");

                var bytes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (UIContractBatchEntry entry in first.Written)
                    bytes[entry.ContractPath] = File.ReadAllText(UIContractPaths.FullPath(entry.ContractPath));

                UIContractBatchResult second = UIContractBatch.Generate(tempDir);
                Assert.That(second.Ok, Is.True, string.Join("\n", second.Errors));
                foreach (UIContractBatchEntry entry in second.Written)
                {
                    Assert.That(File.ReadAllText(UIContractPaths.FullPath(entry.ContractPath)),
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
            const string tempDir = "Assets/LiteGame/Editor/UITools/ContractsDiffTestTemp";
            EnsureFolder(tempDir);
            try
            {
                UIContractBatchDiffResult noBaseline = UIContractBatch.Diff(tempDir);
                Assert.That(noBaseline.Errors, Is.Empty, string.Join("\n", noBaseline.Errors));
                Assert.That(noBaseline.MissingBaseline.Count, Is.EqualTo(12), "空基线目录应逐个报告无基线");
                Assert.That(noBaseline.BreakingCount, Is.Zero);

                UIContractBatchResult generated = UIContractBatch.Generate(tempDir);
                Assert.That(generated.Ok, Is.True, string.Join("\n", generated.Errors));

                UIPageContract login = UIContractStore.Load(UIContractPaths.ForPage("Page_Login"));
                string tempLoginPath = tempDir + "/Page_Login.contract.json";
                login.Controls.Add(new UIContractControl
                {
                    BindName = "Button_Ghost",
                    NodePath = "Cot_SafeArea/Button_Ghost",
                    Type = "UnityEngine.UI.Button",
                    Kind = "Command",
                    Required = true,
                });
                UIContractStore.Save(tempLoginPath, login);

                UIContractBatchDiffResult broken = UIContractBatch.Diff(tempDir);
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
            const string tempManifest = tempDir + "/UIPageManifestWiringTest.json";
            EnsureFolder(tempDir);
            try
            {
                string message = UIPageManifestSync.ReconcileToDisk(tempManifest);
                Assert.That(message, Does.Contain("已对账"), "起页对账应返回可读结论：" + message);
                Assert.That(File.Exists(UIPageManifestPaths.FullPath(tempManifest)), Is.True,
                    "对账结果应写到注入路径");

                UIPageManifest manifest = UIPageManifestStore.Load(tempManifest);
                Assert.That(manifest.Pages.Count, Is.GreaterThan(0), "对账清单不得为空");
            }
            finally
            {
                if (File.Exists(UIPageManifestPaths.FullPath(tempManifest)))
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
