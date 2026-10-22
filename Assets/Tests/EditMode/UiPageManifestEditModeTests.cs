using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor.UiTools.Page;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-3 页面清单：`tbuiform` 行 + 已有清单 + 契约存在性对账；条目按页面（prefab）唯一、
    /// `forms[]` 承载多行登记；维护字段保留、身份字段刷新；草稿与状态流转受迁移表约束。
    /// </summary>
    public sealed class UiPageManifestEditModeTests
    {
        private const string TempDir = "Assets/ShareResource/UI/Screens/TempPageManifestTests";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempDir);
            CreatePrefab("Page_Alpha");
            CreatePrefab("Page_Beta");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
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

        private static UiPageScaffold.PageRow Row(string name, int id, int layer = 1, bool fullScreen = true,
            string lua = null)
            => new UiPageScaffold.PageRow
            {
                id = id,
                prefab = "ShareResource/UI/Screens/TempPageManifestTests/" + name + ".prefab",
                lua = lua ?? "UI." + name,
                layer = layer,
                fullScreen = fullScreen,
            };

        private static string PrefabPath(string name) => TempDir + "/" + name + ".prefab";

        [Test]
        public void 首次生成_按表行建立条目()
        {
            var rows = new[] { Row("Page_Alpha", 901), Row("Page_Beta", 902, layer: 2, fullScreen: false) };

            var result = UiPageManifestSync.Sync(rows, null);

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.AreEqual(2, result.Manifest.Pages.Count);
            Assert.AreEqual("Page_Alpha", result.Manifest.Pages[0].Name, "按页面名稳定排序");
            UiPageManifestEntry alpha = result.Manifest.Pages.Single(x => x.Name == "Page_Alpha");
            Assert.AreEqual("Assets/ShareResource/UI/Screens/TempPageManifestTests/Page_Alpha.prefab", alpha.PrefabPath);
            Assert.AreEqual(1, alpha.Forms.Count);
            Assert.AreEqual(901, alpha.Forms[0].FormId);
            Assert.AreEqual(1, alpha.Forms[0].Layer);
            Assert.IsTrue(alpha.Forms[0].FullScreen);
            Assert.AreEqual("UI.Page_Alpha", alpha.LuaPath);
            Assert.AreEqual("Lru", alpha.CacheStrategy, "tbuiform 无缓存列 → 默认 Lru");
            Assert.AreEqual("art.ui", alpha.Owner);
            Assert.AreEqual("Registered", alpha.Status, "表行存在＝已登记");
            Assert.AreEqual("Assets/LiteGame/Editor/UiTools/Contracts/Page_Alpha.contract.json", alpha.ContractPath);
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

            var result = UiPageManifestSync.Sync(rows, null);

            Assert.IsTrue(result.Ok, result.ErrorText);
            UiPageManifestEntry entry = result.Manifest.Pages.Single();
            Assert.AreEqual(2, entry.Forms.Count, "同一 prefab 的多行登记不得变成两条页面条目");
            Assert.AreEqual(901, entry.Forms[0].FormId, "forms 按 formId 升序");
            Assert.AreEqual(3, entry.Forms[0].Layer);
            Assert.AreEqual(906, entry.Forms[1].FormId);
            Assert.AreEqual(0, result.Manifest.Warnings.Count);
        }

        [Test]
        public void 多行lua不一致_置空并告警()
        {
            var rows = new[] { Row("Page_Alpha", 901, lua: "UI.A"), Row("Page_Alpha", 902, lua: "UI.B") };

            var result = UiPageManifestSync.Sync(rows, null);

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.IsNull(result.Manifest.Pages.Single().LuaPath);
            Assert.IsTrue(result.Manifest.Warnings.Any(x => x.Contains("luaPath")));
        }

        [Test]
        public void 对账保留维护字段_刷新身份字段()
        {
            var existing = new UiPageManifest
            {
                Pages =
                {
                    new UiPageManifestEntry
                    {
                        Name = "Page_Alpha",
                        Forms = { new UiPageFormEntry { FormId = 901, Layer = 1, FullScreen = true } },
                        Owner = "art.ta",
                        Status = "Wired",
                        DesignRef = "figma://alpha",
                        DeviceProfiles = new List<string> { "1080p" },
                        Notes = "已接线",
                    },
                },
            };

            var result = UiPageManifestSync.Sync(new[] { Row("Page_Alpha", 901, layer: 3, lua: "UI.Page_New") }, existing);

            Assert.IsTrue(result.Ok, result.ErrorText);
            UiPageManifestEntry entry = result.Manifest.Pages.Single();
            Assert.AreEqual("art.ta", entry.Owner, "维护字段必须保留");
            Assert.AreEqual("Wired", entry.Status);
            Assert.AreEqual("figma://alpha", entry.DesignRef);
            CollectionAssert.AreEqual(new[] { "1080p" }, entry.DeviceProfiles);
            Assert.AreEqual("已接线", entry.Notes);
            Assert.AreEqual(3, entry.Forms[0].Layer, "身份字段以表为准刷新");
            Assert.AreEqual("UI.Page_New", entry.LuaPath);
        }

        [Test]
        public void 新增表行_新增条目()
        {
            var existing = new UiPageManifest
            {
                Pages =
                {
                    new UiPageManifestEntry
                    {
                        Name = "Page_Alpha",
                        Forms = { new UiPageFormEntry { FormId = 901 } },
                        Status = "Wired",
                        Owner = "art.ui",
                    },
                },
            };

            var result = UiPageManifestSync.Sync(new[] { Row("Page_Alpha", 901), Row("Page_Beta", 902) }, existing);

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.AreEqual(2, result.Manifest.Pages.Count);
            UiPageManifestEntry beta = result.Manifest.Pages.Single(x => x.Name == "Page_Beta");
            Assert.AreEqual(902, beta.Forms.Single().FormId);
            Assert.AreEqual("Registered", beta.Status);
            Assert.AreEqual("art.ui", beta.Owner);
        }

        [Test]
        public void 孤儿条目_Draft保留_非Draft告警()
        {
            var existing = new UiPageManifest
            {
                Pages =
                {
                    new UiPageManifestEntry { Name = "Page_Ghost", Status = "Draft", Owner = "art.ui" },
                    new UiPageManifestEntry
                    {
                        Name = "Page_Old",
                        Forms = { new UiPageFormEntry { FormId = 900 } },
                        Status = "Wired",
                        Owner = "art.ui",
                    },
                },
            };

            var result = UiPageManifestSync.Sync(new[] { Row("Page_Alpha", 901) }, existing);

            Assert.IsTrue(result.Ok, result.ErrorText);
            CollectionAssert.AreEquivalent(new[] { "Page_Alpha", "Page_Ghost", "Page_Old" },
                result.Manifest.Pages.Select(x => x.Name).ToArray());
            Assert.IsTrue(result.Manifest.Warnings.Any(x => x.Contains("Page_Old")), "表行缺失的非 Draft 条目要告警");
            Assert.IsFalse(result.Manifest.Warnings.Any(x => x.Contains("Page_Ghost")), "草稿页未登记是正常状态");
        }

        [Test]
        public void 重复formId_报错()
        {
            var result = UiPageManifestSync.Sync(new[] { Row("Page_Alpha", 901), Row("Page_Beta", 901) }, null);

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("重复", result.ErrorText);
        }

        [Test]
        public void 非法状态_报错且不写盘()
        {
            var existing = new UiPageManifest
            {
                Pages = { new UiPageManifestEntry { Name = "Page_Alpha", Status = "Bogus" } },
            };
            var rows = new[] { Row("Page_Alpha", 901) };

            var result = UiPageManifestSync.Sync(rows, existing);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("状态", result.ErrorText);

            string output = TempDir + "/manifest.json";
            var disk = UiPageManifestSync.SyncToDisk(rows, existing, output);
            Assert.IsFalse(disk.Ok);
            Assert.IsFalse(File.Exists(Absolute(output)), "负例不得写盘");
        }

        [Test]
        public void 确定性_重复生成零diff()
        {
            var rows = new[] { Row("Page_Alpha", 901), Row("Page_Beta", 902) };

            var a = UiPageManifestSync.Sync(rows, null);
            var b = UiPageManifestSync.Sync(rows, null);
            Assert.IsTrue(a.Ok, a.ErrorText);
            Assert.IsTrue(b.Ok, b.ErrorText);

            string json = UiPageManifestJson.Serialize(a.Manifest);
            Assert.AreEqual(json, UiPageManifestJson.Serialize(b.Manifest), "同输入重复对账必须逐字节一致");
            StringAssert.DoesNotContain("generatedAt", json);
        }

        [Test]
        public void sourceHash_对维护字段敏感()
        {
            var rows = new[] { Row("Page_Alpha", 901) };
            var first = UiPageManifestSync.Sync(rows, new UiPageManifest
            {
                Pages = { new UiPageManifestEntry { Name = "Page_Alpha", Owner = "art.ui" } },
            });
            var second = UiPageManifestSync.Sync(rows, new UiPageManifest
            {
                Pages = { new UiPageManifestEntry { Name = "Page_Alpha", Owner = "art.ta" } },
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

            var result = UiPageManifestSync.SyncToDisk(rows, null, output);
            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.IsTrue(File.Exists(Absolute(output)), "成功对账必须写盘");

            UiPageManifest loaded = UiPageManifestStore.Load(output);
            Assert.AreEqual(result.Manifest.SourceHash, loaded.SourceHash);
            Assert.AreEqual(result.Manifest.Pages.Count, loaded.Pages.Count);
            Assert.AreEqual(result.Manifest.Pages[0].Forms[0].FormId, loaded.Pages[0].Forms[0].FormId);

            var outside = UiPageManifestSync.SyncToDisk(rows, null, "Temp/UiPageManifest.json");
            Assert.IsFalse(outside.Ok);
            StringAssert.Contains("Assets/", outside.ErrorText);
        }

        [Test]
        public void 草稿创建_幂等()
        {
            var manifest = new UiPageManifest();

            UiPageManifestEntry draft = UiPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out string error);
            Assert.IsNull(error);
            Assert.IsNotNull(draft);
            Assert.AreEqual("Page_Alpha", draft.Name);
            Assert.AreEqual(0, draft.Forms.Count, "未登记草稿没有表行");
            Assert.AreEqual("Draft", draft.Status);
            Assert.AreEqual("art.ui", draft.Owner);
            Assert.AreEqual(1, manifest.Pages.Count);
            StringAssert.StartsWith("sha256:", manifest.SourceHash);

            UiPageManifestEntry again = UiPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out error);
            Assert.IsNull(error);
            Assert.AreSame(draft, again, "重复起页必须幂等");
            Assert.AreEqual(1, manifest.Pages.Count);
        }

        [Test]
        public void 状态流转_合法与非法()
        {
            var manifest = new UiPageManifest();
            UiPageManifestEntry draft = UiPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out _);

            Assert.IsTrue(UiPageManifestEditor.SetStatus(manifest, "Page_Alpha", UiPageStatus.Registered, out string error), error);
            Assert.AreEqual("Registered", draft.Status);
            Assert.IsTrue(UiPageManifestEditor.SetStatus(manifest, "Page_Alpha", UiPageStatus.Wired, out error), error);
            Assert.AreEqual("Wired", draft.Status);

            Assert.IsFalse(UiPageManifestEditor.SetStatus(manifest, "Page_Alpha", UiPageStatus.Draft, out error),
                "Wired→Draft 不在迁移表内");
            StringAssert.Contains("迁移", error);

            Assert.IsTrue(UiPageManifestEditor.SetStatus(manifest, "Page_Alpha", UiPageStatus.Wired, out error),
                "同值＝无操作");
            Assert.IsFalse(UiPageManifestEditor.SetStatus(manifest, "Page_Nope", UiPageStatus.Wired, out error));
            StringAssert.Contains("不存在", error);
        }

        [Test]
        public void 编辑维护字段_更新指纹()
        {
            var manifest = new UiPageManifest();
            UiPageManifestEntry draft = UiPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Alpha"), "art.ui",
                out _);
            string before = manifest.SourceHash;

            Assert.IsTrue(UiPageManifestEditor.SetEditorial(manifest, "Page_Alpha", "art.ta", "figma://alpha",
                new[] { "1080p", "mobile-landscape" }, "备注", out string error), error);

            Assert.AreEqual("art.ta", draft.Owner);
            Assert.AreEqual("figma://alpha", draft.DesignRef);
            CollectionAssert.AreEqual(new[] { "1080p", "mobile-landscape" }, draft.DeviceProfiles);
            Assert.AreEqual("备注", draft.Notes);
            Assert.AreNotEqual(before, manifest.SourceHash, "维护字段变化必须改变内容指纹");
        }

        [Test]
        public void 真实页面_基线齐全且零错误()
        {
            var result = UiPageManifestSync.Sync();

            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.GreaterOrEqual(result.Manifest.Pages.Count, 12, "真实 tbuiform 有 13 行、12 个唯一页面");
            Assert.GreaterOrEqual(result.Manifest.Pages.Sum(page => page.Forms.Count), 13, "登记行总数不得少于 13");
            Assert.IsTrue(result.Manifest.Pages.All(page => page.Forms.Count > 0), "真实页面都应有表行");
            Assert.IsTrue(result.Manifest.Pages.All(page => !string.IsNullOrEmpty(page.Owner)), "Owner 不得为空");
            Assert.IsTrue(result.Manifest.Pages.All(page => UiPageStatusRules.TryParse(page.Status, out _)),
                "状态必须在五态词表内");
            UiPageManifestEntry dialog = result.Manifest.Pages.Single(page => page.Name == "Tmp_Dialog");
            Assert.AreEqual(2, dialog.Forms.Count, "Tmp_Dialog 在表中登记两次（201 系统错误面 / 206 确认面）");
            Assert.AreEqual(0, result.Manifest.Warnings.Count, string.Join("\n", result.Manifest.Warnings));
            StringAssert.StartsWith("sha256:", result.Manifest.SourceHash);
        }

        private static string Absolute(string assetPath)
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
