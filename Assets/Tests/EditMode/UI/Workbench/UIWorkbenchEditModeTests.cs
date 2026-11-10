using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UITools.Compose;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Workbench;
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
    public sealed class UIWorkbenchEditModeTests
    {
        private const string TempDir = "Assets/LiteGame/Editor/UITools/TempWorkbenchTests";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.CreateFolder("Assets/LiteGame/Editor/UITools", "TempWorkbenchTests");
            AssetDatabase.Refresh();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }


        private static void AssertForbidden(UIWorkbenchLane lane, UIWorkbenchAction action)
        {
            Assert.IsFalse(UILanePolicy.IsAllowed(lane, action), lane + " / " + action + " 应禁止");
            string reason = UILanePolicy.DisabledReason(lane, action);
            Assert.IsNotNull(reason, lane + " / " + action + " 禁用必须给原因");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void 角色矩阵_美术可起页_不可登记与绑定生成()
        {
            Assert.IsTrue(UILanePolicy.IsAllowed(UIWorkbenchLane.Art, UIWorkbenchAction.Scaffold), "美术可起页");
            AssertForbidden(UIWorkbenchLane.Art, UIWorkbenchAction.Register);
            AssertForbidden(UIWorkbenchLane.Art, UIWorkbenchAction.GenerateBinding);
            Assert.IsNull(UILanePolicy.DisabledReason(UIWorkbenchLane.Art, UIWorkbenchAction.Scaffold), "允许时无原因");
        }

        [Test]
        public void 角色矩阵_工程可登记与绑定生成_不可起页()
        {
            Assert.IsTrue(UILanePolicy.IsAllowed(UIWorkbenchLane.Engineering, UIWorkbenchAction.Register), "工程可登记");
            Assert.IsTrue(UILanePolicy.IsAllowed(UIWorkbenchLane.Engineering, UIWorkbenchAction.GenerateBinding), "工程可绑定生成");
            AssertForbidden(UIWorkbenchLane.Engineering, UIWorkbenchAction.Scaffold);
        }

        [Test]
        public void 角色矩阵_每个动作至少一个角色允许且禁止必有原因()
        {
            foreach (UIWorkbenchAction action in Enum.GetValues(typeof(UIWorkbenchAction)))
            {
                bool anyAllowed = false;
                foreach (UIWorkbenchLane lane in Enum.GetValues(typeof(UIWorkbenchLane)))
                {
                    if (UILanePolicy.IsAllowed(lane, action))
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
        public void 透视清单_四透视按序且启用()
        {
            IReadOnlyList<UIWorkbenchPerspective> all = UIWorkbenchPerspectives.All;
            Assert.AreEqual(4, all.Count, "P2-1 四透视：页面/控件库/绑定/校验");
            CollectionAssert.AreEqual(
                new[]
                {
                    UIWorkbenchPerspectiveId.Page,
                    UIWorkbenchPerspectiveId.Widgets,
                    UIWorkbenchPerspectiveId.Binding,
                    UIWorkbenchPerspectiveId.Validation,
                },
                all.Select(p => p.Id).ToArray(), "透视顺序与设计 §2.2 一致");

            Assert.AreEqual(all.Count, all.Select(p => p.Title).Distinct().Count(), "显示名唯一");
            for (int i = 1; i < all.Count; i++)
                Assert.Less(all[i - 1].Order, all[i].Order, "Order 严格递增");
            Assert.IsTrue(all.All(p => p.Enabled), "四透视全部可用");
            Assert.IsNull(UIWorkbenchPerspectives.Find((UIWorkbenchPerspectiveId)999), "未知 id 返回 null");
        }

        [Test]
        public void 状态_切透视与角色不丢上下文()
        {
            var state = new UIWorkbenchState();
            Assert.AreEqual(UIWorkbenchLane.Engineering, state.Lane, "默认工程 Lane");
            Assert.AreEqual(UIWorkbenchPerspectiveId.Page, state.Perspective, "默认页面透视");

            const string prefab = "Assets/LiteGame/Modules/UI/Content/Screens/Page_Login.prefab";
            state.SelectPage("Page_Login");
            state.SetTarget(prefab);
            state.MarkDirty();

            state.SetPerspective(UIWorkbenchPerspectiveId.Validation);
            state.SetLane(UIWorkbenchLane.Art);

            Assert.AreEqual("Page_Login", state.SelectedPage, "切透视不丢选中页");
            Assert.AreEqual(prefab, state.TargetPath, "切角色不丢目标 prefab");
            Assert.AreEqual(UIWorkbenchPerspectiveId.Validation, state.Perspective);
            Assert.IsTrue(state.HasUnsavedEdits, "未保存标记保留");

            state.ClearDirty();
            Assert.IsFalse(state.HasUnsavedEdits);
        }

        [Test]
        public void 页面清单投影_真实基线十二页十三行()
        {
            IReadOnlyList<UIWorkbenchPageRow> rows = UIWorkbenchPageList.LoadDefault(out string error);
            Assert.IsNull(error, error ?? string.Empty);
            Assert.AreEqual(12, rows.Count, "页面清单 12 页");
            Assert.AreEqual(13, rows.Sum(row => row.FormIds.Count), "13 行登记（Tmp_Dialog 两行）");

            UIWorkbenchPageRow login = rows.Single(row => row.Name == "Page_Login");
            Assert.AreEqual("Page_Login", login.Name);
            Assert.IsNotEmpty(login.Owner);
            Assert.IsNotEmpty(login.PrefabPath);
        }

        [Test]
        public void 页面清单投影_缺失时显式报错不伪造空表()
        {
            IReadOnlyList<UIWorkbenchPageRow> rows =
                UIWorkbenchPageList.Load(TempDir + "/missing-page-manifest.json", out string error);
            Assert.IsNotNull(error, "缺清单必须显式错误");
            Assert.IsNotEmpty(error);
            Assert.IsEmpty(rows, "不伪造条目");
        }

        [Test]
        public void 页面编辑_维护字段落盘确定性()
        {
            UIPageManifest manifest = UIPageManifestStore.Load();
            string before = manifest.SourceHash;

            Assert.IsTrue(
                UIWorkbenchPageEditor.ApplyEditorial(manifest, "Page_Login", "art.ui.workbench",
                    "Docs/设计稿/login.png", "工作台夹具临时编辑", out string error),
                error ?? string.Empty);
            Assert.AreNotEqual(before, manifest.SourceHash, "维护字段变化必须重算 sourceHash");

            string firstPath = TempDir + "/manifest-a.json";
            string secondPath = TempDir + "/manifest-b.json";
            UIPageManifestStore.Save(manifest, firstPath);
            UIPageManifestStore.Save(manifest, secondPath);

            CollectionAssert.AreEqual(
                File.ReadAllBytes(UIPageManifestPaths.FullPath(firstPath)),
                File.ReadAllBytes(UIPageManifestPaths.FullPath(secondPath)),
                "重复保存必须逐字节一致（无时间戳/顺序稳定）");
        }

        [Test]
        public void 阶段可用性_起页登记已实现()
        {
            Assert.IsTrue(UIWorkbenchAvailability.IsImplemented(UIWorkbenchAction.Scaffold), "起页 P2-2 已建");
            Assert.IsTrue(UIWorkbenchAvailability.IsImplemented(UIWorkbenchAction.Register), "登记 P2-2 已建");
            Assert.IsNull(UIWorkbenchAvailability.DisabledReason(UIWorkbenchAction.Scaffold));
            Assert.IsNull(UIWorkbenchAvailability.DisabledReason(UIWorkbenchAction.Register));
            Assert.IsTrue(UILanePolicy.IsAllowed(UIWorkbenchLane.Art, UIWorkbenchAction.Scaffold),
                "Lane 仍按角色门控");
            Assert.IsFalse(UILanePolicy.IsAllowed(UIWorkbenchLane.Art, UIWorkbenchAction.Register));
        }

        [Test]
        public void 透视能力标签_迁移未丢动作()
        {
            IReadOnlyList<string> capabilities = UIWorkbenchPerspectives.AllCapabilities();
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "page.list", "page.metadata",
                    "widgets.insert", "widgets.properties",
                    "binding.edit", "binding.generate", "binding.hash",
                    "validate.narrow", "validate.full",
                }, capabilities, "四个分区的能力标签必须全部映射进透视");

            UIWorkbenchPerspective binding = UIWorkbenchPerspectives.Find(UIWorkbenchPerspectiveId.Binding);
            CollectionAssert.Contains(binding.Capabilities, "binding.generate", "绑定生成不得在迁移中丢失");
            CollectionAssert.Contains(
                UIWorkbenchPerspectives.Find(UIWorkbenchPerspectiveId.Widgets).Capabilities,
                "widgets.properties", "受控属性不得在迁移中丢失");
        }

        [Test]
        public void 角色矩阵_允许集与设计逐项一致()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    UIWorkbenchAction.OpenPage, UIWorkbenchAction.EditMetadata, UIWorkbenchAction.Scaffold,
                    UIWorkbenchAction.InsertWidget, UIWorkbenchAction.EditBinding,
                    UIWorkbenchAction.Validate, UIWorkbenchAction.Preview,
                },
                UILanePolicy.Allowed(UIWorkbenchLane.Art).ToArray(), "美术允许集");

            CollectionAssert.AreEqual(
                new[]
                {
                    UIWorkbenchAction.OpenPage, UIWorkbenchAction.InsertWidget, UIWorkbenchAction.EditBinding,
                    UIWorkbenchAction.Validate, UIWorkbenchAction.Preview,
                },
                UILanePolicy.Allowed(UIWorkbenchLane.Technical).ToArray(), "TA 允许集");

            CollectionAssert.AreEqual(
                new[]
                {
                    UIWorkbenchAction.OpenPage, UIWorkbenchAction.EditMetadata, UIWorkbenchAction.Register,
                    UIWorkbenchAction.EditBinding, UIWorkbenchAction.GenerateBinding,
                    UIWorkbenchAction.Validate, UIWorkbenchAction.Preview,
                },
                UILanePolicy.Allowed(UIWorkbenchLane.Engineering).ToArray(), "工程允许集");
        }

        [Test]
        public void 透视能力标签_逐透视精确匹配()
        {
            CollectionAssert.AreEqual(new[] { "page.list", "page.metadata", "page.scaffold", "page.register" },
                UIWorkbenchPerspectives.Find(UIWorkbenchPerspectiveId.Page).Capabilities, "页面透视");
            CollectionAssert.AreEqual(new[] { "widgets.insert", "widgets.properties" },
                UIWorkbenchPerspectives.Find(UIWorkbenchPerspectiveId.Widgets).Capabilities, "控件库透视");
            CollectionAssert.AreEqual(new[] { "binding.edit", "binding.generate", "binding.hash" },
                UIWorkbenchPerspectives.Find(UIWorkbenchPerspectiveId.Binding).Capabilities, "绑定透视");
            CollectionAssert.AreEqual(new[] { "validate.narrow", "validate.full" },
                UIWorkbenchPerspectives.Find(UIWorkbenchPerspectiveId.Validation).Capabilities, "校验透视");
        }

        [Test]
        public void 页面透视_选中回调与目标绑定()
        {
            var state = new UIWorkbenchState();
            var section = new UIWorkbenchPageSection();
            section.Reload(state);
            Assert.IsNull(section.Error, section.Error ?? string.Empty);
            Assert.AreEqual(12, section.Rows.Count);

            UIWorkbenchPageRow selected = null;
            section.PageSelected += row => selected = row;
            section.Select(state, 0);

            Assert.IsNotNull(selected, "选中必须通知宿主（目标绑定单源）");
            Assert.AreEqual(selected.Name, state.SelectedPage);
            Assert.AreEqual(selected.PrefabPath, state.TargetPath, "共享目标随选中更新");
        }

        [Test]
        public void 页面透视_缺清单显式错误不伪造()
        {
            var section = new UIWorkbenchPageSection();
            section.Reload(new UIWorkbenchState(), TempDir + "/missing-page-manifest.json");
            Assert.IsNotNull(section.Error);
            Assert.IsEmpty(section.Rows);
        }

        [Test]
        public void 页面编辑_非法状态迁移原子失败()
        {
            var state = new UIWorkbenchState();
            var section = new UIWorkbenchPageSection();
            section.Reload(state);
            UIWorkbenchPageRow row = section.Rows.Single(item => item.Name == "Page_Login");
            section.Select(state, section.Rows.ToList().IndexOf(row));
            string before = section.SourceHash;

            bool applied = section.TryApplyEdits(state, row, "art.ui.changed", "Docs/x.png", "意图改坏",
                UIPageStatus.Draft, _ => { });

            Assert.IsFalse(applied, "Registered → Draft 非法，必须整体失败");
            Assert.AreEqual(before, section.SourceHash, "失败不得改动内存清单（原子）");
            Assert.IsFalse(state.HasUnsavedEdits, "失败不得标脏");
        }

        [Test]
        public void 目标解析_Stage打开时指向Stage根()
        {
            const string path = "Assets/LiteGame/Modules/UI/Content/Screens/Page_Login.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);

            PrefabStage stage = PrefabStageUtility.OpenPrefab(path);
            Assert.IsNotNull(stage, "Prefab Stage 打开");
            try
            {
                UIComposeTarget target = UIComposeTarget.Resolve(prefab);
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
            var settings = new UIWorkbenchSettingsMemory();
            settings.Lane = UIWorkbenchLane.Technical;
            settings.Perspective = UIWorkbenchPerspectiveId.Validation;
            Assert.AreEqual(UIWorkbenchLane.Technical, settings.Lane);
            Assert.AreEqual(UIWorkbenchPerspectiveId.Validation, settings.Perspective);
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
