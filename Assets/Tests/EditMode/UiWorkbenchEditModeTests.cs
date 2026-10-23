using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Compose;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Workbench;
using LiteGame.Toolchain;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UI 工作台守卫（《UI 编辑器工具链专项设计》§2 工作台）：角色动作矩阵、透视清单、共享上下文、
    /// 页面清单投影与确定性保存、契约报告投影、单一制作入口（ui.workbench 在册 / ui.compose 退役）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UiWorkbenchEditModeTests
    {
        private const string TempDir = "Assets/LiteGame/Editor/UiTools/TempWorkbenchTests";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.CreateFolder("Assets/LiteGame/Editor/UiTools", "TempWorkbenchTests");
            AssetDatabase.Refresh();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }


        private static void AssertForbidden(UiWorkbenchLane lane, UiWorkbenchAction action)
        {
            Assert.IsFalse(UiLanePolicy.IsAllowed(lane, action), lane + " / " + action + " 应禁止");
            string reason = UiLanePolicy.DisabledReason(lane, action);
            Assert.IsNotNull(reason, lane + " / " + action + " 禁用必须给原因");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void 角色矩阵_美术可起页_不可登记与绑定生成()
        {
            Assert.IsTrue(UiLanePolicy.IsAllowed(UiWorkbenchLane.Art, UiWorkbenchAction.Scaffold), "美术可起页");
            AssertForbidden(UiWorkbenchLane.Art, UiWorkbenchAction.Register);
            AssertForbidden(UiWorkbenchLane.Art, UiWorkbenchAction.GenerateBinding);
            Assert.IsNull(UiLanePolicy.DisabledReason(UiWorkbenchLane.Art, UiWorkbenchAction.Scaffold), "允许时无原因");
        }

        [Test]
        public void 角色矩阵_工程可登记与绑定生成_不可起页()
        {
            Assert.IsTrue(UiLanePolicy.IsAllowed(UiWorkbenchLane.Engineering, UiWorkbenchAction.Register), "工程可登记");
            Assert.IsTrue(UiLanePolicy.IsAllowed(UiWorkbenchLane.Engineering, UiWorkbenchAction.GenerateBinding), "工程可绑定生成");
            AssertForbidden(UiWorkbenchLane.Engineering, UiWorkbenchAction.Scaffold);
        }

        [Test]
        public void 角色矩阵_每个动作至少一个角色允许且禁止必有原因()
        {
            foreach (UiWorkbenchAction action in Enum.GetValues(typeof(UiWorkbenchAction)))
            {
                bool anyAllowed = false;
                foreach (UiWorkbenchLane lane in Enum.GetValues(typeof(UiWorkbenchLane)))
                {
                    if (UiLanePolicy.IsAllowed(lane, action))
                    {
                        anyAllowed = true;
                        continue;
                    }

                    AssertForbidden(lane, action);
                }

                Assert.IsTrue(anyAllowed, action + " 至少一个角色允许，否则动作是死码");
            }
        }

        [Test]
        public void 透视清单_五透视按序且启用()
        {
            IReadOnlyList<UiWorkbenchPerspective> all = UiWorkbenchPerspectives.All;
            Assert.AreEqual(5, all.Count, "P2-1 五透视：页面/控件库/绑定/契约/校验");
            CollectionAssert.AreEqual(
                new[]
                {
                    UiWorkbenchPerspectiveId.Page,
                    UiWorkbenchPerspectiveId.Widgets,
                    UiWorkbenchPerspectiveId.Binding,
                    UiWorkbenchPerspectiveId.Contract,
                    UiWorkbenchPerspectiveId.Validation,
                },
                all.Select(p => p.Id).ToArray(), "透视顺序与设计 §2.2 一致");

            Assert.AreEqual(all.Count, all.Select(p => p.Title).Distinct().Count(), "显示名唯一");
            for (int i = 1; i < all.Count; i++)
                Assert.Less(all[i - 1].Order, all[i].Order, "Order 严格递增");
            Assert.IsTrue(all.All(p => p.Enabled), "P2-1 五透视全部可用");
            Assert.IsNotNull(UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Contract));
            Assert.IsNull(UiWorkbenchPerspectives.Find((UiWorkbenchPerspectiveId)999), "未知 id 返回 null");
        }

        [Test]
        public void 状态_切透视与角色不丢上下文()
        {
            var state = new UiWorkbenchState();
            Assert.AreEqual(UiWorkbenchLane.Engineering, state.Lane, "默认工程 Lane");
            Assert.AreEqual(UiWorkbenchPerspectiveId.Page, state.Perspective, "默认页面透视");

            const string prefab = "Assets/ShareResource/UI/Screens/Page_Login.prefab";
            state.SelectPage("Page_Login");
            state.SetTarget(prefab);
            state.MarkDirty();

            state.SetPerspective(UiWorkbenchPerspectiveId.Contract);
            state.SetLane(UiWorkbenchLane.Art);

            Assert.AreEqual("Page_Login", state.SelectedPage, "切透视不丢选中页");
            Assert.AreEqual(prefab, state.TargetPath, "切角色不丢目标 prefab");
            Assert.AreEqual(UiWorkbenchPerspectiveId.Contract, state.Perspective);
            Assert.IsTrue(state.HasUnsavedEdits, "未保存标记保留");

            state.ClearDirty();
            Assert.IsFalse(state.HasUnsavedEdits);
        }

        [Test]
        public void 页面清单投影_真实基线十一页十二行()
        {
            IReadOnlyList<UiWorkbenchPageRow> rows = UiWorkbenchPageList.LoadDefault(out string error);
            Assert.IsNull(error, error ?? string.Empty);
            Assert.AreEqual(11, rows.Count, "页面清单 11 页");
            Assert.AreEqual(12, rows.Sum(row => row.FormIds.Count), "12 行登记（Tmp_Dialog 两行）");

            UiWorkbenchPageRow login = rows.Single(row => row.Name == "Page_Login");
            Assert.AreEqual("Page_Login", login.Name);
            Assert.IsTrue(login.ContractPresent, "Page_Login 契约已生成");
            Assert.IsNotEmpty(login.Owner);
            Assert.IsNotEmpty(login.PrefabPath);
        }

        [Test]
        public void 页面清单投影_缺失时显式报错不伪造空表()
        {
            IReadOnlyList<UiWorkbenchPageRow> rows =
                UiWorkbenchPageList.Load(TempDir + "/missing-page-manifest.json", out string error);
            Assert.IsNotNull(error, "缺清单必须显式错误");
            Assert.IsNotEmpty(error);
            Assert.IsEmpty(rows, "不伪造条目");
        }

        [Test]
        public void 页面编辑_维护字段落盘确定性()
        {
            UiPageManifest manifest = UiPageManifestStore.Load();
            string before = manifest.SourceHash;

            Assert.IsTrue(
                UiWorkbenchPageEditor.ApplyEditorial(manifest, "Page_Login", "art.ui.workbench",
                    "Docs/设计稿/login.png", "工作台夹具临时编辑", out string error),
                error ?? string.Empty);
            Assert.AreNotEqual(before, manifest.SourceHash, "维护字段变化必须重算 sourceHash");

            string firstPath = TempDir + "/manifest-a.json";
            string secondPath = TempDir + "/manifest-b.json";
            UiPageManifestStore.Save(manifest, firstPath);
            UiPageManifestStore.Save(manifest, secondPath);

            CollectionAssert.AreEqual(
                File.ReadAllBytes(UiPageManifestPaths.FullPath(firstPath)),
                File.ReadAllBytes(UiPageManifestPaths.FullPath(secondPath)),
                "重复保存必须逐字节一致（无时间戳/顺序稳定）");
        }

        [Test]
        public void 契约报告_生成与对账投影()
        {
            var generated = new UiContractBatchResult();
            generated.Written.Add(new UiContractBatchEntry
            {
                PageName = "Page_Login",
                PrefabPath = "Assets/ShareResource/UI/Screens/Page_Login.prefab",
                ContractPath = "Assets/LiteGame/Editor/UiTools/Contracts/Page_Login.contract.json",
            });
            generated.Skipped.Add("Tmp_Skip");
            UiContractReportView generatedView =
                UiContractReportView.FromGeneration(generated, "Assets/LiteGame/Editor/UiTools/Contracts");
            Assert.AreEqual(1, generatedView.Written);
            Assert.AreEqual(1, generatedView.Skipped);
            Assert.IsTrue(generatedView.Ok);
            Assert.AreEqual("Assets/LiteGame/Editor/UiTools/Contracts", generatedView.OutputDirectory);
            StringAssert.Contains("Page_Login", string.Join("\n", generatedView.Lines));

            var diffResult = new UiContractDiffResult();
            diffResult.Changes.Add(new UiContractChange
            {
                Kind = UiContractChangeKind.Removed,
                Section = "controls",
                Key = "Button_Removed",
                Detail = "控件被删除",
                Breaking = true,
            });
            var batchDiff = new UiContractBatchDiffResult();
            batchDiff.Compared.Add(new UiContractDiffEntry
            {
                PageName = "Page_Login",
                ContractPath = "Assets/LiteGame/Editor/UiTools/Contracts/Page_Login.contract.json",
                Diff = diffResult,
            });

            UiContractReportView diffView = UiContractReportView.FromDiff(batchDiff);
            Assert.AreEqual(1, diffView.Breaking, "破坏性计数");
            Assert.IsFalse(diffView.Ok, "破坏性 = 不通过");
            StringAssert.Contains("破坏性", diffView.Summary);
        }

        [Test]
        public void 阶段可用性_起页登记已实现()
        {
            Assert.IsTrue(UiWorkbenchAvailability.IsImplemented(UiWorkbenchAction.Scaffold), "起页 P2-2 已建");
            Assert.IsTrue(UiWorkbenchAvailability.IsImplemented(UiWorkbenchAction.Register), "登记 P2-2 已建");
            Assert.IsNull(UiWorkbenchAvailability.DisabledReason(UiWorkbenchAction.Scaffold));
            Assert.IsNull(UiWorkbenchAvailability.DisabledReason(UiWorkbenchAction.Register));
            Assert.IsTrue(UiLanePolicy.IsAllowed(UiWorkbenchLane.Art, UiWorkbenchAction.Scaffold),
                "Lane 仍按角色门控");
            Assert.IsFalse(UiLanePolicy.IsAllowed(UiWorkbenchLane.Art, UiWorkbenchAction.Register));
        }

        [Test]
        public void 透视能力标签_迁移未丢动作()
        {
            IReadOnlyList<string> capabilities = UiWorkbenchPerspectives.AllCapabilities();
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "page.list", "page.metadata",
                    "widgets.insert", "widgets.properties",
                    "binding.edit", "binding.generate", "binding.hash",
                    "contract.generate", "contract.diff",
                    "validate.narrow", "validate.full",
                }, capabilities, "五个分区的能力标签必须全部映射进透视");

            UiWorkbenchPerspective binding = UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Binding);
            CollectionAssert.Contains(binding.Capabilities, "binding.generate", "绑定生成不得在迁移中丢失");
            CollectionAssert.Contains(
                UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Widgets).Capabilities,
                "widgets.properties", "受控属性不得在迁移中丢失");
        }

        [Test]
        public void 契约生成_临时目录十一份且重复逐字节一致()
        {
            string directory = TempDir + "/Contracts";
            UiContractBatchResult first = UiContractBatch.Generate(directory);
            Assert.IsTrue(first.Ok, first.ToReport());
            Assert.AreEqual(11, first.Written.Count, first.ToReport());

            var firstBytes = new Dictionary<string, byte[]>();
            foreach (UiContractBatchEntry entry in first.Written)
            {
                string full = UiPageManifestPaths.FullPath(entry.ContractPath);
                Assert.IsTrue(File.Exists(full), "契约文件已写出：" + entry.ContractPath);
                firstBytes[entry.ContractPath] = File.ReadAllBytes(full);
            }

            UiContractBatchResult second = UiContractBatch.Generate(directory);
            Assert.IsTrue(second.Ok, second.ToReport());
            Assert.AreEqual(11, second.Written.Count, second.ToReport());
            foreach (UiContractBatchEntry entry in second.Written)
            {
                CollectionAssert.AreEqual(
                    firstBytes[entry.ContractPath],
                    File.ReadAllBytes(UiPageManifestPaths.FullPath(entry.ContractPath)),
                    entry.PageName + " 重复生成必须逐字节一致");
            }
        }

        [Test]
        public void 角色矩阵_允许集与设计逐项一致()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    UiWorkbenchAction.OpenPage, UiWorkbenchAction.EditMetadata, UiWorkbenchAction.Scaffold,
                    UiWorkbenchAction.InsertWidget, UiWorkbenchAction.EditBinding,
                    UiWorkbenchAction.GenerateContract, UiWorkbenchAction.Validate, UiWorkbenchAction.Preview,
                },
                UiLanePolicy.Allowed(UiWorkbenchLane.Art).ToArray(), "美术允许集");

            CollectionAssert.AreEqual(
                new[]
                {
                    UiWorkbenchAction.OpenPage, UiWorkbenchAction.InsertWidget, UiWorkbenchAction.EditBinding,
                    UiWorkbenchAction.GenerateContract, UiWorkbenchAction.DiffContract,
                    UiWorkbenchAction.Validate, UiWorkbenchAction.Preview,
                },
                UiLanePolicy.Allowed(UiWorkbenchLane.Technical).ToArray(), "TA 允许集");

            CollectionAssert.AreEqual(
                new[]
                {
                    UiWorkbenchAction.OpenPage, UiWorkbenchAction.EditMetadata, UiWorkbenchAction.Register,
                    UiWorkbenchAction.EditBinding, UiWorkbenchAction.GenerateBinding,
                    UiWorkbenchAction.GenerateContract, UiWorkbenchAction.DiffContract,
                    UiWorkbenchAction.Validate, UiWorkbenchAction.Preview,
                },
                UiLanePolicy.Allowed(UiWorkbenchLane.Engineering).ToArray(), "工程允许集");
        }

        [Test]
        public void 透视能力标签_逐透视精确匹配()
        {
            CollectionAssert.AreEqual(new[] { "page.list", "page.metadata", "page.scaffold", "page.register" },
                UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Page).Capabilities, "页面透视");
            CollectionAssert.AreEqual(new[] { "widgets.insert", "widgets.properties" },
                UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Widgets).Capabilities, "控件库透视");
            CollectionAssert.AreEqual(new[] { "binding.edit", "binding.generate", "binding.hash" },
                UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Binding).Capabilities, "绑定透视");
            CollectionAssert.AreEqual(new[] { "contract.generate", "contract.diff" },
                UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Contract).Capabilities, "契约透视");
            CollectionAssert.AreEqual(new[] { "validate.narrow", "validate.full" },
                UiWorkbenchPerspectives.Find(UiWorkbenchPerspectiveId.Validation).Capabilities, "校验透视");
        }

        [Test]
        public void 页面透视_选中回调与目标绑定()
        {
            var state = new UiWorkbenchState();
            var section = new UiWorkbenchPageSection();
            section.Reload(state);
            Assert.IsNull(section.Error, section.Error ?? string.Empty);
            Assert.AreEqual(11, section.Rows.Count);

            UiWorkbenchPageRow selected = null;
            section.PageSelected += row => selected = row;
            section.Select(state, 0);

            Assert.IsNotNull(selected, "选中必须通知宿主（目标绑定单源）");
            Assert.AreEqual(selected.Name, state.SelectedPage);
            Assert.AreEqual(selected.PrefabPath, state.TargetPath, "共享目标随选中更新");
        }

        [Test]
        public void 页面透视_缺清单显式错误不伪造()
        {
            var section = new UiWorkbenchPageSection();
            section.Reload(new UiWorkbenchState(), TempDir + "/missing-page-manifest.json");
            Assert.IsNotNull(section.Error);
            Assert.IsEmpty(section.Rows);
        }

        [Test]
        public void 页面编辑_非法状态迁移原子失败()
        {
            var state = new UiWorkbenchState();
            var section = new UiWorkbenchPageSection();
            section.Reload(state);
            UiWorkbenchPageRow row = section.Rows.Single(item => item.Name == "Page_Login");
            section.Select(state, section.Rows.ToList().IndexOf(row));
            string before = section.SourceHash;

            bool applied = section.TryApplyEdits(state, row, "art.ui.changed", "Docs/x.png", "意图改坏",
                UiPageStatus.Draft, _ => { });

            Assert.IsFalse(applied, "Registered → Draft 非法，必须整体失败");
            Assert.AreEqual(before, section.SourceHash, "失败不得改动内存清单（原子）");
            Assert.IsFalse(state.HasUnsavedEdits, "失败不得标脏");
        }

        [Test]
        public void 契约对账_真实基线无破坏性()
        {
            UiContractBatchDiffResult diff = UiContractBatch.Diff();
            Assert.IsTrue(diff.Ok, diff.ToReport());
            Assert.AreEqual(11, diff.Compared.Count, diff.ToReport());
            Assert.AreEqual(0, diff.BreakingCount, diff.ToReport());
        }

        [Test]
        public void 目标解析_Stage打开时指向Stage根()
        {
            const string path = "Assets/ShareResource/UI/Screens/Page_Login.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);

            PrefabStage stage = PrefabStageUtility.OpenPrefab(path);
            Assert.IsNotNull(stage, "Prefab Stage 打开");
            try
            {
                UiComposeTarget target = UiComposeTarget.Resolve(prefab);
                Assert.IsTrue(target.ViaStage, "Stage 打开时 ViaStage 必须为真（否则控件属性/生成写资产根）");
                Assert.AreSame(stage.prefabContentsRoot, target.DisplayRoot, "DisplayRoot 指向 Stage 副本");
            }
            finally
            {
                StageUtility.GoToMainStage();
            }
        }

        [Test]
        public void 偏好_内存实现往返()
        {
            var settings = new UiWorkbenchSettingsMemory();
            settings.Lane = UiWorkbenchLane.Technical;
            settings.Perspective = UiWorkbenchPerspectiveId.Contract;
            Assert.AreEqual(UiWorkbenchLane.Technical, settings.Lane);
            Assert.AreEqual(UiWorkbenchPerspectiveId.Contract, settings.Perspective);
        }

        [Test]
        public void 工具清单_工作台在册_旧制作窗口退役()
        {
            EditorToolCatalog catalog = EditorToolCatalog.Load();
            EditorToolDescriptor workbench = catalog.Find("ui.workbench");
            Assert.IsNotNull(workbench, "ui.workbench 必须在册");
            Assert.AreEqual(ToolchainKinds.Window, workbench.Kind);
            Assert.AreEqual("UI 工作台", workbench.Title);
            Assert.IsNull(workbench.Error, "handler 必须已注册：" + workbench.Error);
            Assert.IsNull(catalog.Find("ui.compose"), "旧制作窗口 ui.compose 已退役");
            Assert.IsNull(catalog.Find("ui.page-new"), "起页向导 ui.page-new 已退役（P2-2 由 ui.page-scaffold 承接）");
        }
    }
}
