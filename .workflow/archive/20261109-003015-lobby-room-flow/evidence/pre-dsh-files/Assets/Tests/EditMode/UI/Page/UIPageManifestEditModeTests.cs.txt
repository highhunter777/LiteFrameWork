using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UITools.Page;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-3 页面清单：`tbuiform` 行 + 已有清单 + 契约存在性对账；条目按页面（prefab）唯一、
    /// `forms[]` 承载多行登记；维护字段保留、身份字段刷新；草稿与状态流转受迁移表约束。
    /// </summary>
    public sealed class UIPageManifestEditModeTests
    {
        private const string TempDir = "Assets/LiteGame/Modules/UI/Content/Screens/TempPageManifestTests";

        [OneTimeSetUp]
        public void SetUp()
        {
            EnsureFolder(TempDir);
            CreatePrefab("Page_Alpha");
            CreatePrefab("Page_Beta");
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string full = Absolute(path);
            if (Directory.Exists(full))
            {
                // 上次运行被中断留下的裸目录（无 meta）会让 CreateFolder 在写 meta 时打日志——先清掉再建
                Directory.Delete(full, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void CreatePrefab(string name)
        {
            string path = TempDir + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var root = new GameObject(name, typeof(RectTransform));
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static UIPageTableRow Row(string name, int id, int layer = 1, bool fullScreen = true)
            => new UIPageTableRow
            {
                id = id,
                prefab = "LiteGame/Modules/UI/Content/Screens/TempPageManifestTests/" + name + ".prefab",
                layer = layer,
                fullScreen = fullScreen,
            };

        private static string PrefabPath(string name) => TempDir + "/" + name + ".prefab";

        [Test]
        public void 首次生成_按表行建立条目()
        {
            var rows = new[] { Row("Page_Alpha", 901), Row("Page_Beta", 902, layer: 2, fullScreen: false) };

            var result = UIPageManifestSync.Sync(rows, null);

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.AreEqual(2, result.Manifest.Pages.Count);
            Assert.AreEqual("Page_Alpha", result.Manifest.Pages[0].Name, "按页面名稳定排序");
            UIPageManifestEntry alpha = result.Manifest.Pages.Single(x => x.Name == "Page_Alpha");
            Assert.AreEqual("Assets/LiteGame/Modules/UI/Content/Screens/TempPageManifestTests/Page_Alpha.prefab", alpha.PrefabPath);
            Assert.AreEqual(1, alpha.Forms.Count);
            Assert.AreEqual(901, alpha.Forms[0].FormId);
            Assert.AreEqual(1, alpha.Forms[0].Layer);
            Assert.IsTrue(alpha.Forms[0].FullScreen);
            Assert.AreEqual("Lru", alpha.CacheStrategy, "tbuiform 无缓存列 → 默认 Lru");
            Assert.AreEqual("art.ui", alpha.Owner);
            Assert.AreEqual("Registered", alpha.Status, "表行存在＝已登记");
            Assert.AreEqual("Assets/LiteGame/Editor/UITools/Contracts/Page_Alpha.contract.json", alpha.ContractPath);
            Assert.IsFalse(alpha.ContractPresent);
            StringAssert.StartsWith("sha256:", result.Manifest.SourceHash);
            Assert.AreEqual(0, result.Manifest.Warnings.Count);
        }

        [Test]
        public void 同页多行_合并为多表单条目()
        {
            var rows = new[]
            {
                Row("Page_Alpha", 906, layer: 1, fullScreen: false),
                Row("Page_Alpha", 901, layer: 3, fullScreen: false),
            };

            var result = UIPageManifestSync.Sync(rows, null);

            Assert.IsTrue(result.Ok, result.ErrorText);
            UIPageManifestEntry entry = result.Manifest.Pages.Single();
            Assert.AreEqual(2, entry.Forms.Count, "同一 prefab 的多行登记不得变成两条页面条目");
            Assert.AreEqual(901, entry.Forms[0].FormId, "forms 按 formId 升序");
            Assert.AreEqual(3, entry.Forms[0].Layer);
            Assert.AreEqual(906, entry.Forms[1].FormId);
            Assert.AreEqual(0, result.Manifest.Warnings.Count);
        }
        [Test]
        public void 对账保留维护字段_刷新身份字段()
        {
            var existing = new UIPageManifest
            {
                Pages =
                {
                    new UIPageManifestEntry
                    {
                        Name = "Page_Alpha",
                        Forms = { new UIPageFormEntry { FormId = 901, Layer = 1, FullScreen = true } },
                        Owner = "art.ta",
                        Status = "Wired",
                        DesignRef = "figma://alpha",
                        DeviceProfiles = new List<string> { "1080p" },
                        Notes = "已接线",
                    },
                },
            };

            var result = UIPageManifestSync.Sync(new[] { Row("Page_Alpha", 901, layer: 3) }, existing);

            Assert.IsTrue(result.Ok, result.ErrorText);
            UIPageManifestEntry entry = result.Manifest.Pages.Single();
            Assert.AreEqual("art.ta", entry.Owner, "维护字段必须保留");
            Assert.AreEqual("Wired", entry.Status);
            Assert.AreEqual("figma://alpha", entry.DesignRef);
            CollectionAssert.AreEqual(new[] { "1080p" }, entry.DeviceProfiles);
            Assert.AreEqual("已接线", entry.Notes);
            Assert.AreEqual(3, entry.Forms[0].Layer, "身份字段以表为准刷新");
        }

        [Test]
        public void 新增表行_新增条目()
        {
            var existing = new UIPageManifest
            {
                Pages =
                {
                    new UIPageManifestEntry
                    {
                        Name = "Page_Alpha",
                        Forms = { new UIPageFormEntry { FormId = 901 } },
                        Status = "Wired",
                        Owner = "art.ui",
                    },
                },
            };

            var result = UIPageManifestSync.Sync(new[] { Row("Page_Alpha", 901), Row("Page_Beta", 902) }, existing);

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.AreEqual(2, result.Manifest.Pages.Count);
            UIPageManifestEntry beta = result.Manifest.Pages.Single(x => x.Name == "Page_Beta");
            Assert.AreEqual(902, beta.Forms.Single().FormId);
            Assert.AreEqual("Registered", beta.Status);
            Assert.AreEqual("art.ui", beta.Owner);
        }

        [Test]
        public void 孤儿条目_Draft保留_非Draft告警()
        {
            var existing = new UIPageManifest
            {
                Pages =
                {
                    new UIPageManifestEntry { Name = "Page_Ghost", Status = "Draft", Owner = "art.ui" },
                    new UIPageManifestEntry
                    {
                        Name = "Page_Old",
                        Forms = { new UIPageFormEntry { FormId = 900 } },
                        Status = "Wired",
                        Owner = "art.ui",
                    },
                },
            };

            var result = UIPageManifestSync.Sync(new[] { Row("Page_Alpha", 901) }, existing);

            Assert.IsTrue(result.Ok, result.ErrorText);
            CollectionAssert.AreEquivalent(new[] { "Page_Alpha", "Page_Ghost", "Page_Old" },
                result.Manifest.Pages.Select(x => x.Name).ToArray());
            Assert.IsTrue(result.Manifest.Warnings.Any(x => x.Contains("Page_Old")), "表行缺失的非 Draft 条目要告警");
            Assert.IsFalse(result.Manifest.Warnings.Any(x => x.Contains("Page_Ghost")), "草稿页未登记是正常状态");
        }

        [Test]
        public void 重复formId_报错()
        {
            var result = UIPageManifestSync.Sync(new[] { Row("Page_Alpha", 901), Row("Page_Beta", 901) }, null);

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("重复", result.ErrorText);
        }

        [Test]
        public void 非法状态_报错且不写盘()
        {
            var existing = new UIPageManifest
            {
                Pages = { new UIPageManifestEntry { Name = "Page_Alpha", Status = "Bogus" } },
            };
            var rows = new[] { Row("Page_Alpha", 901) };

            var result = UIPageManifestSync.Sync(rows, existing);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("状态", result.ErrorText);

            string output = TempDir + "/invalid-manifest.json";
            var disk = UIPageManifestSync.SyncToDisk(rows, existing, output);
            Assert.IsFalse(disk.Ok);
            Assert.IsFalse(File.Exists(Absolute(output)), "负例不得写盘");
        }

        [Test]
        public void 确定性_重复生成零diff()
        {
            var rows = new[] { Row("Page_Alpha", 901), Row("Page_Beta", 902) };

            var a = UIPageManifestSync.Sync(rows, null);
            var b = UIPageManifestSync.Sync(rows, null);
            Assert.IsTrue(a.Ok, a.ErrorText);
            Assert.IsTrue(b.Ok, b.ErrorText);

            string json = UIPageManifestJson.Serialize(a.Manifest);
            Assert.AreEqual(json, UIPageManifestJson.Serialize(b.Manifest), "同输入重复对账必须逐字节一致");
            StringAssert.DoesNotContain("generatedAt", json);
        }

        [Test]
        public void sourceHash_对维护字段敏感()
        {
            var rows = new[] { Row("Page_Alpha", 901) };
            var first = UIPageManifestSync.Sync(rows, new UIPageManifest
            {
                Pages = { new UIPageManifestEntry { Name = "Page_Alpha", Owner = "art.ui" } },
            });
            var second = UIPageManifestSync.Sync(rows, new UIPageManifest
            {
                Pages = { new UIPageManifestEntry { Name = "Page_Alpha", Owner = "art.ta" } },
            });

            Assert.IsTrue(first.Ok, first.ErrorText);
            Assert.IsTrue(second.Ok, second.ErrorText);
            Assert.AreNotEqual(first.Manifest.SourceHash, second.Manifest.SourceHash);
        }

        [Test]
        public void 写盘读回一致_输出路径限定Assets()
        {
            var rows = new[] { Row("Page_Alpha", 901) };
            string output = TempDir + "/manifest.json";

            var result = UIPageManifestSync.SyncToDisk(rows, null, output);
            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.IsTrue(File.Exists(Absolute(output)), "成功对账必须写盘");

            UIPageManifest loaded = UIPageManifestStore.Load(output);
            Assert.AreEqual(result.Manifest.SourceHash, loaded.SourceHash);
            Assert.AreEqual(result.Manifest.Pages.Count, loaded.Pages.Count);
            Assert.AreEqual(result.Manifest.Pages[0].Forms[0].FormId, loaded.Pages[0].Forms[0].FormId);

            var outside = UIPageManifestSync.SyncToDisk(rows, null, "Temp/UIPageManifest.json");
            Assert.IsFalse(outside.Ok);
            StringAssert.Contains("Assets/", outside.ErrorText);
        }

        [Test]
        public void 草稿创建_幂等()
        {
            var manifest = new UIPageManifest();

            UIPageManifestEntry draft = UIPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out string error);
            Assert.IsNull(error);
            Assert.IsNotNull(draft);
            Assert.AreEqual("Page_Alpha", draft.Name);
            Assert.AreEqual(0, draft.Forms.Count, "未登记草稿没有表行");
            Assert.AreEqual("Draft", draft.Status);
            Assert.AreEqual("art.ui", draft.Owner);
            Assert.AreEqual(1, manifest.Pages.Count);
            StringAssert.StartsWith("sha256:", manifest.SourceHash);

            UIPageManifestEntry again = UIPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out error);
            Assert.IsNull(error);
            Assert.AreSame(draft, again, "重复起页必须幂等");
            Assert.AreEqual(1, manifest.Pages.Count);
        }

        [Test]
        public void 状态流转_合法与非法()
        {
            var manifest = new UIPageManifest();
            UIPageManifestEntry draft = UIPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out _);

            Assert.IsTrue(UIPageManifestEditor.SetStatus(manifest, "Page_Alpha", UIPageStatus.Registered, out string error), error);
            Assert.AreEqual("Registered", draft.Status);
            Assert.IsTrue(UIPageManifestEditor.SetStatus(manifest, "Page_Alpha", UIPageStatus.Wired, out error), error);
            Assert.AreEqual("Wired", draft.Status);

            Assert.IsFalse(UIPageManifestEditor.SetStatus(manifest, "Page_Alpha", UIPageStatus.Draft, out error),
                "Wired→Draft 不在迁移表内");
            StringAssert.Contains("迁移", error);

            Assert.IsTrue(UIPageManifestEditor.SetStatus(manifest, "Page_Alpha", UIPageStatus.Wired, out error),
                "同值＝无操作");
            Assert.IsFalse(UIPageManifestEditor.SetStatus(manifest, "Page_Nope", UIPageStatus.Wired, out error));
            StringAssert.Contains("不存在", error);
        }

        [Test]
        public void 编辑维护字段_更新指纹()
        {
            var manifest = new UIPageManifest();
            UIPageManifestEntry draft = UIPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out _);
            string before = manifest.SourceHash;

            Assert.IsTrue(UIPageManifestEditor.SetEditorial(manifest, "Page_Alpha", "art.ta", "figma://alpha",
                new[] { "1080p", "mobile-landscape" }, "备注", out string error), error);

            Assert.AreEqual("art.ta", draft.Owner);
            Assert.AreEqual("figma://alpha", draft.DesignRef);
            CollectionAssert.AreEqual(new[] { "1080p", "mobile-landscape" }, draft.DeviceProfiles);
            Assert.AreEqual("备注", draft.Notes);
            Assert.AreNotEqual(before, manifest.SourceHash, "维护字段变化必须改变内容指纹");
        }

        [Test]
        public void 显式移除_退役条目删除并重算指纹()
        {
            var manifest = new UIPageManifest
            {
                Pages = { new UIPageManifestEntry { Name = "Page_Old", Status = "Registered" } },
            };
            string before = manifest.SourceHash;

            Assert.IsTrue(UIPageManifestEditor.Remove(manifest, "Page_Old", out string error), error);
            Assert.AreEqual(0, manifest.Pages.Count, "退役条目必须从清单移除");
            Assert.AreNotEqual(before, manifest.SourceHash, "移除后重算指纹");
            Assert.IsFalse(UIPageManifestEditor.Remove(manifest, "Page_Old", out error), "重复移除应失败");
            StringAssert.Contains("不存在", error);
        }

        [Test]
        public void 真实页面_基线齐全且零错误()
        {
            var result = UIPageManifestSync.Sync();

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.AreEqual(12, result.Manifest.Pages.Count, "当前表源应为 12 个页面（含 Tmp_* 系统面）");
            Assert.AreEqual(13, result.Manifest.Pages.Sum(page => page.Forms.Count), "登记行应为 13 行（Tmp_Dialog 双行）");
            Assert.IsFalse(result.Manifest.Pages.Any(page => page.Name == "UIMain"), "页面清单不得再含样例页");
            Assert.IsTrue(result.Manifest.Pages.All(page => page.Forms.Count > 0), "真实页面都应有表行");
            Assert.IsTrue(result.Manifest.Pages.All(page => !string.IsNullOrEmpty(page.Owner)), "Owner 不得为空");
            Assert.IsTrue(result.Manifest.Pages.All(page => UIPageStatusRules.TryParse(page.Status, out _)),
                "状态必须在五态词表内");
            UIPageManifestEntry dialog = result.Manifest.Pages.Single(page => page.Name == "Tmp_Dialog");
            Assert.AreEqual(2, dialog.Forms.Count, "Tmp_Dialog 在表中登记两次（201 系统错误面 / 206 确认面）");
            Assert.AreEqual(0, result.Manifest.Warnings.Count, string.Join("\n", result.Manifest.Warnings));
            StringAssert.StartsWith("sha256:", result.Manifest.SourceHash);
        }

        private static string Absolute(string assetPath)
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
