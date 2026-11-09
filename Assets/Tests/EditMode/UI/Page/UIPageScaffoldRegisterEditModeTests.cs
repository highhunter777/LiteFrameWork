using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LiteFramework.UI;
using LiteCodeGen;
using LiteGame.Editor.UITools.Binding;
using LiteGame.Editor.UITools.Contracts;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Validation;
using LiteGame.Editor.UITools.Workbench;
using LiteGame.Toolchain;
using LiteTesting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// UI 系统 P2-2 守卫（《UI 编辑器工具链专项设计》§3.1/§3.5/§3.6）： 起页（基线复制/根契约/安全区归属/原子清理）、草稿契约（formId=0/批量/漂移/diff 口径）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UIPageScaffoldRegisterEditModeTests
    {
        private const string TempRoot = "Assets/Tests/TempUiPageSplit";
        private const string TempContracts = TempRoot + "/Contracts";
        private const string TempManifest = TempRoot + "/PageManifest.json";
        private const string TempBindManifest = TempRoot + "/UIBindManifest.json";
        private const string TableCopyRelative = "Temp/ui-page-split/uiform.xlsx";
        private const string BaselineA = "Assets/LiteGame/Modules/UI/Content/Screens/Tmp_BaselineA.prefab";
        private const string BaselineList = "Assets/LiteGame/Modules/UI/Content/Screens/Tmp_ListScreen.prefab";
        private const int ValidId = 21;   // 17 已被产品页 Page_ButtonDemo 占用；21 为空闲区

        private static string RealTableFullPath
            => Path.Combine(UIValidator.ProjectRoot, UIPageTableHelper.TableRelativePath);

        private static string TableCopyFullPath
            => Path.Combine(UIValidator.ProjectRoot, TableCopyRelative);

        private static string FullPath(string assetPath)
            => Path.Combine(UIValidator.ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

        private static string PrefabPath(string pageName) => UIValidator.ScreensDir + "/" + pageName + ".prefab";


        private static string DraftContractPath(string pageName) => TempContracts + "/" + pageName + ".contract.json";

        private byte[] _realTableBytes;
        private byte[] _realManifestBytes;
        private byte[] _realBindManifestBytes;
        private string _contractsBaselineHash;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realTableBytes = File.ReadAllBytes(RealTableFullPath);
            _realManifestBytes = ReadIfExists(FullPath(UIPageManifestPaths.DefaultPath));
            _realBindManifestBytes = ReadIfExists(FullPath(UIBindGenerationHash.DefaultManifestPath));
            _contractsBaselineHash = HashDirectory(UIContractPaths.ContractDirectory);
            EnsureAssetsFolder("Assets/Tests");
            DeleteAssetsFolder(TempRoot);
            EnsureAssetsFolder("Assets/Tests");
            AssetDatabase.CreateFolder("Assets/Tests", "TempUiPageSplit");
            AssetDatabase.CreateFolder(TempRoot, "Contracts");
            Directory.CreateDirectory(Path.GetDirectoryName(TableCopyFullPath));
            Directory.CreateDirectory(Path.Combine(UIValidator.ProjectRoot, "Temp/ui-page-split/backups"));
            AssetDatabase.Refresh();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            CleanupTestPages();
            DeleteAssetsFolder(TempRoot);
            if (Directory.Exists(Path.GetDirectoryName(TableCopyFullPath)))
                Directory.Delete(Path.GetDirectoryName(TableCopyFullPath), true);
            AssetDatabase.Refresh();

            CollectionAssert.AreEqual(_realTableBytes, File.ReadAllBytes(RealTableFullPath),
                "真实 #uiform.xlsx 不得被夹具改动");
            Assert.AreEqual(_contractsBaselineHash, HashDirectory(UIContractPaths.ContractDirectory),
                "真实契约基线（11 份）不得被夹具改动");
            CollectionAssert.AreEqual(_realManifestBytes,
                ReadIfExists(FullPath(UIPageManifestPaths.DefaultPath)), "真实 UIPageManifest.json 不得被夹具改动");
            CollectionAssert.AreEqual(_realBindManifestBytes,
                ReadIfExists(FullPath(UIBindGenerationHash.DefaultManifestPath)), "真实 UIBindManifest.json 不得被夹具改动");
        }

        private static byte[] ReadIfExists(string fullPath) => File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;

        [SetUp]
        public void SetUp()
        {
            CleanupTestPages();
            DeleteAssetIfExists(TempManifest);
            DeleteAssetIfExists(TempBindManifest);
            DeleteAssetsFolder(TempContracts);
            EnsureAssetsFolder(TempRoot);
            AssetDatabase.CreateFolder(TempRoot, "Contracts");
            File.Copy(RealTableFullPath, TableCopyFullPath, true);
            AssetDatabase.Refresh();
        }

        // ---------------- 起页 ----------------

        [Test]
        public void 起页_基线复制_根契约齐备_安全区挂内容容器_草稿落盘()
        {
            UIPageScaffold.ScaffoldResult result = Scaffold("Page_TestScaffoldA", BaselineA);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(UIPageScaffold.ScaffoldFailure.None, result.FailedAt);
            string prefabPath = PrefabPath("Page_TestScaffoldA");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "prefab 已产出：" + prefabPath);
            Assert.AreEqual("Page_TestScaffoldA", prefab.name, "根名与文件名一致");
            Assert.IsNotNull(prefab.GetComponent<Canvas>(), "根 Canvas");
            Assert.IsNotNull(prefab.GetComponent<CanvasGroup>(), "根 CanvasGroup");
            Assert.IsNotNull(prefab.GetComponent<GraphicRaycaster>(), "根 GraphicRaycaster");
            var scaler = prefab.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "根 CanvasScaler");
            Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution);
            Assert.AreEqual(0.5f, scaler.matchWidthOrHeight);

            Transform content = prefab.transform.Find("Cot_Baseline");
            Assert.IsNotNull(content, "Tmp_BaselineA 的内容容器存在");
            Assert.IsNotNull(content.GetComponent<SafeAreaReceiver>(),
                "安全区必须挂内容容器（不是另建空壳）");

            UIPageManifest manifest = UIPageManifestStore.Load(TempManifest);
            Assert.AreEqual(1, manifest.Pages.Count, "草稿条目 1 条");
            UIPageManifestEntry entry = manifest.Pages[0];
            Assert.AreEqual("Page_TestScaffoldA", entry.Name);
            Assert.AreEqual("Draft", entry.Status);
            Assert.IsEmpty(entry.Forms, "草稿无登记行");
            Assert.AreEqual("art.ui", entry.Owner);
            CollectionAssert.AreEqual(_realTableBytes, File.ReadAllBytes(RealTableFullPath), "起页不得写表");
        }

        [Test]
        public void 起页_列表基线_保留既有安全区不重复()
        {
            UIPageScaffold.ScaffoldResult result = Scaffold("Page_TestScaffoldList", BaselineList);
            Assert.IsTrue(result.Ok, result.Error);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("Page_TestScaffoldList"));
            Assert.IsNotNull(prefab.GetComponent<CanvasScaler>(), "列表基线也要补根缩放");
            Assert.IsNotNull(prefab.GetComponent<GraphicRaycaster>());
            var receivers = prefab.GetComponentsInChildren<SafeAreaReceiver>(true);
            Assert.AreEqual(1, receivers.Length, "已有安全区不得重复添加（Tmp_ListScreen 内容节点已挂）");
            Assert.IsNull(prefab.transform.Find("Cot_SafeArea"), "已有安全区时不另建空壳");
        }

        [Test]
        public void 起页_目标已存在_拒绝且不写清单()
        {
            AssetDatabase.CopyAsset(BaselineA, PrefabPath("Page_TestScaffoldB"));
            AssetDatabase.Refresh();

            UIPageScaffold.ScaffoldResult result = Scaffold("Page_TestScaffoldB", BaselineA);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(UIPageScaffold.ScaffoldFailure.Validate, result.FailedAt, result.Error);
            Assert.IsFalse(File.Exists(FullPath(TempManifest)), "预检失败不得写清单");
        }

        [Test]
        public void 起页_非法页名与缺失基线_拒绝()
        {
            UIPageScaffold.ScaffoldResult badName = Scaffold("Page_bad", BaselineA);
            Assert.AreEqual(UIPageScaffold.ScaffoldFailure.Validate, badName.FailedAt);

            UIPageScaffold.ScaffoldResult missingBaseline =
                Scaffold("Page_TestScaffoldC", "Assets/LiteGame/Modules/UI/Content/Screens/Tmp_Missing.prefab");
            Assert.AreEqual(UIPageScaffold.ScaffoldFailure.Validate, missingBaseline.FailedAt);
            Assert.IsFalse(File.Exists(FullPath(PrefabPath("Page_TestScaffoldC"))));
        }

        [Test]
        public void 起页_清单写盘失败_删除prefab且清单不变()
        {
            string blocked = TempRoot + "/blocked";
            File.WriteAllText(FullPath(blocked), "block");
            AssetDatabase.Refresh();
            string manifestPath = blocked + "/manifest.json";

            UIPageScaffold.ScaffoldResult result =
                UIPageScaffold.Scaffold("Page_TestScaffoldD", BaselineA, manifestPath, false);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(UIPageScaffold.ScaffoldFailure.Manifest, result.FailedAt, result.Error);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("Page_TestScaffoldD")),
                "清单失败必须删除新建 prefab（原子失败）");
            Assert.AreEqual("block", File.ReadAllText(FullPath(blocked)), "阻断文件不被破坏");
        }

        // ---------------- 草稿契约 ----------------

        [Test]
        public void 草稿契约_批量生成formId零_确定性()
        {
            Assert.IsTrue(Scaffold("Page_TestScaffoldE", BaselineA).Ok);

            UIContractBatchResult first = UIContractBatch.Generate(TempContracts, LoadTempManifest());
            Assert.IsTrue(first.Ok, first.ToReport());
            Assert.IsTrue(first.Written.Any(entry => entry.Draft && entry.PageName == "Page_TestScaffoldE"),
                "批量必须产出草稿契约：" + first.ToReport());

            string path = DraftContractPath("Page_TestScaffoldE");
            Assert.IsTrue(File.Exists(FullPath(path)), path);
            UIPageContract contract = UIContractStore.Load(path);
            Assert.AreEqual(0, contract.Page.FormId, "草稿 formId=0");
            Assert.AreEqual(UIContractGenerator.DraftLayer, contract.Page.Layer);
            Assert.AreEqual(UIContractGenerator.DraftFullScreen, contract.Page.FullScreen);
            byte[] before = File.ReadAllBytes(FullPath(path));

            UIContractBatchResult second = UIContractBatch.Generate(TempContracts, LoadTempManifest());
            Assert.IsTrue(second.Ok, second.ToReport());
            CollectionAssert.AreEqual(before, File.ReadAllBytes(FullPath(path)), "草稿契约重复生成逐字节一致");
        }

        [Test]
        public void 草稿契约_空清单不产草稿()
        {
            UIContractBatchResult result = UIContractBatch.Generate(TempContracts, new UIPageManifest());
            Assert.IsTrue(result.Ok, result.ToReport());
            Assert.IsFalse(result.Written.Any(entry => entry.Draft), "空清单不得伪造草稿");
        }

        [Test]
        public void 草稿契约_重生成与盘上一致()
        {
            Assert.IsTrue(Scaffold("Page_TestScaffoldF", BaselineA).Ok);
            UIContractBatch.Generate(TempContracts, LoadTempManifest());
            UIPageManifest manifest = LoadTempManifest();
            UIPageManifestEntry entry = manifest.Pages.Single(page => page.Name == "Page_TestScaffoldF");

            UIContractGenerationResult regenerated = UIContractBatch.Regenerate(entry, allowDraft: true);
            Assert.IsTrue(regenerated.Ok, regenerated.ErrorText);
            Assert.AreEqual(0, regenerated.Contract.Page.FormId);
            string onDisk = File.ReadAllText(FullPath(DraftContractPath("Page_TestScaffoldF")));
            Assert.AreEqual(UIContractJson.Serialize(regenerated.Contract), onDisk, "重生成逐字节一致");
        }

        [Test]
        public void 契约diff_草稿晋升与维护字段变化_非破坏()
        {
            UIPageContract draft = DraftContract();
            UIPageContract promoted = Clone(draft);
            promoted.Page.FormId = 17;
            promoted.Page.CacheStrategy = "DestroyOnClose";
            promoted.Page.Layer = 2;
            promoted.Page.FullScreen = false;
            UIContractDiffResult diff = UIContractDiff.Compare(draft, promoted);
            Assert.IsFalse(diff.HasBreaking, "草稿晋升/维护字段变化不得判破坏：" + string.Join(";", diff.Changes.Select(c => c.Key)));

            UIPageContract renamed = Clone(promoted);
            renamed.Page.FormId = 18;
            Assert.IsTrue(UIContractDiff.Compare(promoted, renamed).HasBreaking, "真实 formId 变更仍是破坏性");

            UIPageContract demoted = Clone(promoted);
            demoted.Page.FormId = 0;
            Assert.IsTrue(UIContractDiff.Compare(promoted, demoted).HasBreaking,
                "真实→0（表行删除/回退草稿）必须判破坏，不能与草稿晋升同豁免");
        }

        // ---------------- 清单与缓存策略 ----------------

        [Test]
        public void 清单保存_原子替换且无临时残留()
        {
            var manifest = new UIPageManifest();
            UIPageManifestEntry draft = UIPageManifestEditor.EnsureDraft(manifest, PrefabPath("Page_Login"), "art.ui",
                out string error);
            Assert.IsNotNull(draft, error);
            UIPageManifestStore.Save(manifest, TempManifest);

            Assert.IsTrue(File.Exists(FullPath(TempManifest)));
            Assert.IsFalse(File.Exists(FullPath(TempManifest) + ".tmp"), "原子写不得残留 .tmp");
            UIPageManifest loaded = UIPageManifestStore.Load(TempManifest);
            Assert.AreEqual(1, loaded.Pages.Count);
            StringAssert.StartsWith("sha256:", loaded.SourceHash);
        }

        [Test]
        public void 缓存策略_设置与对账保留_非法拒绝()
        {
            PrepareTempManifestFromRealTable();
            UIPageManifest manifest = UIPageManifestStore.Load(TempManifest);
            string before = manifest.SourceHash;
            Assert.IsTrue(UIPageManifestEditor.SetCacheStrategy(manifest, "Page_Login", "DestroyOnClose",
                out string error), error);
            Assert.AreNotEqual(before, manifest.SourceHash, "维护字段变化需重算指纹");
            Assert.IsFalse(UIPageManifestEditor.SetCacheStrategy(manifest, "Page_Login", "Bogus",
                out string invalid), "非法策略必须拒绝");
            StringAssert.Contains("非法", invalid);
            UIPageManifestStore.Save(manifest, TempManifest);

            UIPageManifestSyncResult sync = UIPageManifestSync.Sync(UIPageTableHelper.ListPages(), manifest);
            Assert.IsTrue(sync.Ok, sync.ErrorText);
            Assert.AreEqual("DestroyOnClose", sync.Manifest.Pages.Single(p => p.Name == "Page_Login").CacheStrategy,
                "对账必须保留维护字段");
        }

        [Test]
        public void 契约投影_缓存策略与清单一致()
        {
            PrepareTempManifestFromRealTable();
            UIPageManifest manifest = UIPageManifestStore.Load(TempManifest);
            Assert.IsTrue(UIPageManifestEditor.SetCacheStrategy(manifest, "Page_Login", "Resident",
                out string error), error);
            UIPageManifestEntry entry = manifest.Pages.Single(p => p.Name == "Page_Login");

            UIContractGenerationResult result = UIContractBatch.Regenerate(entry);
            Assert.IsTrue(result.Ok, result.ErrorText);
            Assert.AreEqual("Resident", result.Contract.Page.CacheStrategy, "契约投影同一缓存策略");
        }

        // ---------------- 登记 ----------------

        [Test]
        public void 登记_正常路径_表清单缓存契约_hash规则()
        {
            const string page = "Page_TestRegisterA";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));

            var codegen = new FakeCodegen();
            UIPageRegisterResult result = UIPageRegistration.Register(Request(page, ValidId, codegen: codegen));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(ValidId, result.FormId);
            CollectionAssert.AreEqual(new[] { "luban", "hash" }, codegen.Order, "代码生成链顺序");

            Assert.IsTrue(UIPageTableHelper.TryListPages(out List<UIPageTableRow> rows, out string tableError,
                TableCopyRelative), tableError);
            Assert.IsTrue(rows.Any(row => row.id == ValidId && row.PrefabPath == PrefabPath(page)), "表行已写入");
            Assert.IsTrue(File.Exists(result.BackupPath), "表备份存在：" + result.BackupPath);

            UIPageManifest manifest = LoadTempManifest();
            UIPageManifestEntry entry = manifest.Pages.Single(item => item.Name == page);
            Assert.AreEqual("Registered", entry.Status);
            Assert.AreEqual(1, entry.Forms.Count);
            Assert.AreEqual(ValidId, entry.Forms[0].FormId);
            Assert.AreEqual("DestroyOnClose", entry.CacheStrategy, "缓存策略保留");
            Assert.IsTrue(entry.ContractPresent);

            UIPageContract contract = UIContractStore.Load(entry.ContractPath);
            Assert.AreEqual(ValidId, contract.Page.FormId, "登记后契约刷新真实 formId");
            Assert.AreEqual("DestroyOnClose", contract.Page.CacheStrategy, "契约投影同一缓存策略");

            Assert.IsFalse(File.Exists(FullPath(TempBindManifest)), "无 BindRoot 不得写绑定 hash（不伪造）");
        }

        [Test]
        public void 登记_缺草稿契约_拒绝且零产物()
        {
            const string page = "Page_TestRegisterB";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            byte[] tableBefore = File.ReadAllBytes(TableCopyFullPath);

            UIPageRegisterResult result = UIPageRegistration.Register(Request(page, ValidId));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(UIPageRegisterFailure.Contract, result.FailedAt, result.Error);
            CollectionAssert.AreEqual(tableBefore, File.ReadAllBytes(TableCopyFullPath), "预检失败不得写表");
            Assert.IsTrue(UIPageTableHelper.TryListPages(out List<UIPageTableRow> rows, out _, TableCopyRelative));
            Assert.IsFalse(rows.Any(row => row.id == ValidId), "预检失败不得写表");
        }

        [Test]
        public void 登记_契约formId非零或guid不符_拒绝()
        {
            const string page = "Page_TestRegisterC";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            UIPageContract contract = DraftContract();
            contract.Page.Name = page;
            contract.Page.PrefabPath = PrefabPath(page);
            contract.Page.PrefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath(page));
            contract.Page.FormId = 5;
            UIContractStore.Save(DraftContractPath(page), contract);

            UIPageRegisterResult result = UIPageRegistration.Register(Request(page, ValidId));
            Assert.AreEqual(UIPageRegisterFailure.Contract, result.FailedAt, result.Error);

            contract.Page.FormId = 0;
            contract.Page.PrefabGuid = new string('0', 32);
            UIContractStore.Save(DraftContractPath(page), contract);
            UIPageRegisterResult stale = UIPageRegistration.Register(Request(page, ValidId));
            Assert.AreEqual(UIPageRegisterFailure.Contract, stale.FailedAt, "同名旧契约（guid 不符）必须拒绝");
        }

        [Test]
        public void 登记_无草稿条目_拒绝()
        {
            const string page = "Page_TestRegisterD";
            AssetDatabase.CopyAsset(BaselineA, PrefabPath(page));
            AssetDatabase.Refresh();
            WriteDraftContract(page, 0);

            UIPageRegisterResult result = UIPageRegistration.Register(Request(page, ValidId));
            Assert.AreEqual(UIPageRegisterFailure.Validate, result.FailedAt, result.Error);
        }

        [Test]
        public void 登记_id冲突与layer越界_拒绝且零产物()
        {
            const string page = "Page_TestRegisterE";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));

            UIPageRegisterResult conflict = UIPageRegistration.Register(Request(page, 11));
            Assert.AreEqual(UIPageRegisterFailure.Validate, conflict.FailedAt, conflict.Error);

            UIPageRegisterRequest badLayer = Request(page, ValidId);
            badLayer.Layer = 4;
            Assert.AreEqual(UIPageRegisterFailure.Validate,
                UIPageRegistration.Register(badLayer).FailedAt, "layer 0–3");

            UIPageRegisterRequest badCache = Request(page, ValidId);
            badCache.CacheStrategy = "Bogus";
            Assert.AreEqual(UIPageRegisterFailure.Validate,
                UIPageRegistration.Register(badCache).FailedAt, "缓存策略词表");
        }

        [Test]
        public void 登记_表写已落行但报失败_核验回滚并复核()
        {
            const string page = "Page_TestRegisterF";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            var table = new FailAfterWriteTablePort(new UIPageTableHelperPort(TableCopyRelative));
            byte[] before = File.ReadAllBytes(TableCopyFullPath);

            UIPageRegisterResult result =
                UIPageRegistration.Register(Request(page, ValidId, table: table));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(UIPageRegisterFailure.WriteTable, result.FailedAt, result.Error);
            Assert.IsTrue(table.RollbackCalls > 0, "必须按备份回滚");
            Assert.IsTrue(UIPageTableHelper.TryListPages(out List<UIPageTableRow> rows, out _, TableCopyRelative));
            Assert.IsFalse(rows.Any(row => row.id == ValidId), "回滚后复核行不存在");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(TableCopyFullPath), "表恢复写前字节");
        }

        [Test]
        public void 登记_表写失败且核验读表失败_不得声称已复核()
        {
            const string page = "Page_TestRegisterF2";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            var table = new FailAfterWriteTablePort(new UIPageTableHelperPort(TableCopyRelative))
            {
                LookupFails = true,
            };

            UIPageRegisterResult result =
                UIPageRegistration.Register(Request(page, ValidId, table: table));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(UIPageRegisterFailure.WriteTable, result.FailedAt, result.Error);
            StringAssert.Contains("核验失败", result.Error, "读表失败必须显式报 Unknown，不得当作无行");
            StringAssert.DoesNotContain("已回滚并复核", result.Error);
        }

        [Test]
        public void 登记_回滚后核验读表失败_报无法复核()
        {
            const string page = "Page_TestRegisterF3";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            var table = new UnknownAfterRollbackTablePort(new UIPageTableHelperPort(TableCopyRelative));

            UIPageRegisterResult result =
                UIPageRegistration.Register(Request(page, ValidId, table: table));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(UIPageRegisterFailure.WriteTable, result.FailedAt, result.Error);
            StringAssert.Contains("回滚后无法复核", result.Error);
            StringAssert.DoesNotContain("已回滚并复核", result.Error);
        }

        [Test]
        public void 登记_Luban失败_回滚表_清单仍Draft_重跑Luban()
        {
            const string page = "Page_TestRegisterG";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            var codegen = new FakeCodegen { LubanOk = false };

            UIPageRegisterResult result = UIPageRegistration.Register(Request(page, ValidId, codegen: codegen));

            Assert.AreEqual(UIPageRegisterFailure.Codegen, result.FailedAt, result.Error);
            Assert.AreEqual(2, codegen.LubanCalls, "失败回滚后要重跑 Luban 复原生成物");
            Assert.IsTrue(UIPageTableHelper.TryListPages(out List<UIPageTableRow> rows, out _, TableCopyRelative));
            Assert.IsFalse(rows.Any(row => row.id == ValidId));
            Assert.AreEqual("Draft", LoadTempManifest().Pages.Single(p => p.Name == page).Status);
        }

        [Test]
        public void 登记_BuildHash失败_回滚并复核()
        {
            const string page = "Page_TestRegisterH";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            var codegen = new FakeCodegen { HashOk = false };

            UIPageRegisterResult result = UIPageRegistration.Register(Request(page, ValidId, codegen: codegen));

            Assert.AreEqual(UIPageRegisterFailure.Codegen, result.FailedAt, result.Error);
            CollectionAssert.AreEqual(new[] { "luban", "hash", "luban" }, codegen.Order);
            Assert.IsTrue(UIPageTableHelper.TryListPages(out List<UIPageTableRow> rows, out _, TableCopyRelative));
            Assert.IsFalse(rows.Any(row => row.id == ValidId));
        }

        [Test]
        public void 登记_契约刷新失败_显式报红()
        {
            const string page = "Page_TestRegisterI";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            var refresher = new FakeRefresher { Ok = false, Error = "模拟刷新失败" };

            UIPageRegisterResult result =
                UIPageRegistration.Register(Request(page, ValidId, refresher: refresher));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(UIPageRegisterFailure.ContractRefresh, result.FailedAt, result.Error);
            StringAssert.Contains("契约未刷新", result.Error);
            Assert.AreEqual("Registered", LoadTempManifest().Pages.Single(p => p.Name == page).Status,
                "契约刷新失败不回滚已完成的登记");
        }

        [Test]
        public void 登记_注入表路径必须同时注入生成链()
        {
            const string page = "Page_TestRegisterJ";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            UIPageRegisterRequest request = Request(page, ValidId);
            request.Codegen = null;

            UIPageRegisterResult result = UIPageRegistration.Register(request);
            Assert.AreEqual(UIPageRegisterFailure.Validate, result.FailedAt, result.Error);
        }

        [Test]
        public void 登记_挂BindRoot_记录绑定hash()
        {
            const string page = "Page_TestRegisterK";
            Assert.IsTrue(Scaffold(page, BaselineA).Ok);
            Assert.IsTrue(TryGenerateDraftContract(page));
            string prefabPath = PrefabPath(page);
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var root = contents.AddComponent<BindRoot>();
                root.ScriptName = "PageTestRegisterKView";
                root.ScriptsFolder = TempRoot + "/Gen";
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.ImportAsset(prefabPath);

            UIPageRegisterResult result = UIPageRegistration.Register(Request(page, ValidId));

            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(File.Exists(FullPath(TempBindManifest)), "路径 B 记录 hash 清单");
            StringAssert.Contains(prefabPath, File.ReadAllText(FullPath(TempBindManifest)));
        }

        // ---------------- 工作台 ----------------

        [Test]
        public void 工作台_起页登记已实现_能力标签_工具在册()
        {
            Assert.IsTrue(UIWorkbenchAvailability.IsImplemented(UIWorkbenchAction.Scaffold));
            Assert.IsTrue(UIWorkbenchAvailability.IsImplemented(UIWorkbenchAction.Register));
            Assert.IsNull(UIWorkbenchAvailability.DisabledReason(UIWorkbenchAction.Scaffold));
            Assert.IsNull(UIWorkbenchAvailability.DisabledReason(UIWorkbenchAction.Register));

            IReadOnlyList<string> capabilities = UIWorkbenchPerspectives.Find(UIWorkbenchPerspectiveId.Page).Capabilities;
            CollectionAssert.IsSubsetOf(new[] { "page.scaffold", "page.register" }, capabilities);

            EditorToolCatalog catalog = EditorToolCatalog.Load();
            foreach (string id in new[] { "ui.page-scaffold", "ui.page-register" })
            {
                EditorToolDescriptor tool = catalog.Find(id);
                Assert.IsNotNull(tool, id + " 未登记");
                Assert.AreEqual(ToolchainKinds.Window, tool.Kind);
                Assert.IsNull(tool.Error, id + " handler 错误：" + tool.Error);
            }
        }

        [Test]
        public void 工作台_页面透视请求事件()
        {
            var state = new UIWorkbenchState();
            var section = new UIWorkbenchPageSection();
            section.Reload(state, TempManifest);
            int scaffold = 0;
            int register = 0;
            section.ScaffoldRequested += () => scaffold++;
            section.RegisterRequested += () => register++;

            section.RequestScaffold();
            section.RequestRegister();

            Assert.AreEqual(1, scaffold);
            Assert.AreEqual(1, register);
        }

        [Test]
        public void 工作台_外部清单变更_焦点重载保留选中()
        {
            PrepareTempManifestFromRealTable();
            var state = new UIWorkbenchState();
            var section = new UIWorkbenchPageSection();
            section.Reload(state, TempManifest);
            Assert.IsNull(section.Error, section.Error ?? string.Empty);
            section.Select(state, 0);
            string selected = state.SelectedPage;
            string target = state.TargetPath;

            TouchManifestExternally("art.ui.external");

            Assert.IsTrue(section.ReloadIfChanged(state), "外部变更应触发重载");
            Assert.AreEqual(selected, state.SelectedPage, "重载保留选中页");
            Assert.AreEqual(target, state.TargetPath, "重载保留目标 prefab");
        }

        [Test]
        public void 工作台_未保存编辑时不自动重载()
        {
            PrepareTempManifestFromRealTable();
            var state = new UIWorkbenchState();
            var section = new UIWorkbenchPageSection();
            section.Reload(state, TempManifest);
            section.Select(state, 0);
            state.MarkDirty();

            TouchManifestExternally("art.ui.external");

            Assert.IsFalse(section.ReloadIfChanged(state));
            Assert.IsTrue(section.ExternalChanged, "外部变更需显式提示");
        }

        [Test]
        public void 工作台_磁盘外改后保存_拒绝覆盖()
        {
            PrepareTempManifestFromRealTable();
            var state = new UIWorkbenchState();
            var section = new UIWorkbenchPageSection();
            section.Reload(state, TempManifest);
            section.Select(state, 0);
            TouchManifestExternally("art.ui.external");
            string diskHash = UIPageManifestStore.Load(TempManifest).SourceHash;

            Assert.IsFalse(section.TrySave(state, _ => { }), "磁盘指纹变化必须拒绝保存");
            Assert.AreEqual(diskHash, UIPageManifestStore.Load(TempManifest).SourceHash, "不覆盖外部写入");
        }

        [Test]
        public void 工作台_应用到内存后保存_写入磁盘()
        {
            PrepareTempManifestFromRealTable();
            var state = new UIWorkbenchState();
            var section = new UIWorkbenchPageSection();
            section.Reload(state, TempManifest);
            section.Select(state, 0);
            UIWorkbenchPageRow row = section.Rows[0];
            Assert.IsTrue(UIPageStatusRules.TryParse(row.Status, out UIPageStatus status));

            Assert.IsTrue(section.TryApplyEdits(state, row, "art.ui.new", "设计稿v2", "备注v2", status, _ => { }),
                "应用应成功");
            var logs = new List<string>();
            Assert.IsTrue(section.TrySave(state, logs.Add), "应用后保存必须成功（不得再判未应用）：" + string.Join(" | ", logs));

            UIPageManifestEntry saved = LoadTempManifest().Pages.Find(item => item.Name == row.Name);
            Assert.IsNotNull(saved);
            Assert.AreEqual("art.ui.new", saved.Owner);
            Assert.AreEqual("设计稿v2", saved.DesignRef);
            Assert.AreEqual("备注v2", saved.Notes);
        }

        // ---------------- 夹具辅助 ----------------

        private static UIPageScaffold.ScaffoldResult Scaffold(string pageName, string baselinePath)
            => UIPageScaffold.Scaffold(pageName, baselinePath, TempManifest, openStage: false);

        private static UIPageManifest LoadTempManifest() => UIPageManifestStore.Load(TempManifest);

        private static bool TryGenerateDraftContract(string pageName)
        {
            UIContractBatchResult generated = UIContractBatch.Generate(TempContracts, LoadTempManifest());
            if (!generated.Ok) return false;
            return File.Exists(FullPath(DraftContractPath(pageName)));
        }

        private static void WriteDraftContract(string pageName, int formId)
        {
            UIPageContract contract = DraftContract();
            contract.Page.Name = pageName;
            contract.Page.PrefabPath = PrefabPath(pageName);
            contract.Page.PrefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath(pageName));
            contract.Page.FormId = formId;
            UIContractStore.Save(DraftContractPath(pageName), contract);
        }

        private static UIPageRegisterRequest Request(string pageName, int id,
            IUIPageTablePort table = null, IUIPageCodegenPipeline codegen = null,
            IUIPageContractRefresher refresher = null)
            => new UIPageRegisterRequest
            {
                PageName = pageName,
                Id = id,
                Layer = 1,
                FullScreen = true,
                CacheStrategy = "DestroyOnClose",
                ManifestPath = TempManifest,
                ContractPath = DraftContractPath(pageName),
                TablePath = TableCopyRelative,
                BindManifestPath = TempBindManifest,
                Table = table ?? new UIPageTableHelperPort(TableCopyRelative),
                Codegen = codegen ?? new FakeCodegen(),
                ContractRefresher = refresher ?? new DefaultRefresher(),
            };

        private static UIPageContract DraftContract()
            => new UIPageContract
            {
                Page = new UIContractPage
                {
                    FormId = 0,
                    Name = "Page_Draft",
                    PrefabPath = "Assets/LiteGame/Modules/UI/Content/Screens/Page_Draft.prefab",
                    PrefabGuid = new string('a', 32),
                    
                    Layer = UIContractGenerator.DraftLayer,
                    FullScreen = UIContractGenerator.DraftFullScreen,
                    CacheStrategy = UIPageContract.DefaultCacheStrategy,
                },
            };

        private static UIPageContract Clone(UIPageContract contract)
            => UIContractJson.Deserialize(UIContractJson.Serialize(contract));

        private void PrepareTempManifestFromRealTable()
        {
            var sync = UIPageManifestSync.Sync(UIPageTableHelper.ListPages(), null);
            Assert.IsTrue(sync.Ok, sync.ErrorText);
            UIPageManifestStore.Save(sync.Manifest, TempManifest);
        }

        private static void TouchManifestExternally(string owner)
        {
            UIPageManifest manifest = UIPageManifestStore.Load(TempManifest);
            Assert.IsTrue(UIPageManifestEditor.SetEditorial(manifest, manifest.Pages[0].Name, owner, null, null, null,
                out string error), error);
            UIPageManifestStore.Save(manifest, TempManifest);
        }

        private static void CleanupTestPages()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { UIValidator.ScreensDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("Page_TestScaffold", StringComparison.Ordinal)
                    || name.StartsWith("Page_TestRegister", StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.Refresh();
        }

        private static void EnsureAssetsFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureAssetsFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void DeleteAssetsFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
        }

        private static void DeleteAssetIfExists(string path)
        {
            if (File.Exists(FullPath(path))) AssetDatabase.DeleteAsset(path);
        }

        private static string HashDirectory(string assetDirectory)
        {
            string full = UIContractPaths.FullPath(assetDirectory);
            if (!Directory.Exists(full)) return string.Empty;
            var entries = Directory.GetFiles(full, "*.contract.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => Path.GetFileName(path) + ":" + Sha256(File.ReadAllBytes(path)));
            return Sha256(System.Text.Encoding.UTF8.GetBytes(string.Join("|", entries)));
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        // ---------------- 假端口 ----------------

        private sealed class FakeCodegen : IUIPageCodegenPipeline
        {
            public bool LubanOk = true;
            public bool HashOk = true;
            public int LubanCalls;
            public int HashCalls;
            public readonly List<string> Order = new List<string>();

            public UIPageCodegenRun RunLuban()
            {
                LubanCalls++;
                Order.Add("luban");
                return LubanOk ? UIPageCodegenRun.Success("fake luban") : UIPageCodegenRun.Failure("假 Luban 失败");
            }

            public UIPageCodegenRun RefreshBuildHash()
            {
                HashCalls++;
                Order.Add("hash");
                return HashOk ? UIPageCodegenRun.Success("fake hash") : UIPageCodegenRun.Failure("假 BuildHash 失败");
            }
        }

        private sealed class FakeRefresher : IUIPageContractRefresher
        {
            public bool Ok = true;
            public string Error;
            public int Calls;

            public bool TryRefresh(UIPageManifestEntry entry, out string error)
            {
                Calls++;
                error = Ok ? null : Error;
                return Ok;
            }
        }

        private sealed class DefaultRefresher : IUIPageContractRefresher
        {
            public bool TryRefresh(UIPageManifestEntry entry, out string error)
            {
                error = null;
                try
                {
                    UIContractGenerationResult result = UIContractBatch.Regenerate(entry);
                    if (!result.Ok)
                    {
                        error = result.ErrorText;
                        return false;
                    }

                    UIContractStore.Save(entry.ContractPath, result.Contract);
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }
        }

        private sealed class FailAfterWriteTablePort : IUIPageTablePort
        {
            private readonly IUIPageTablePort _inner;

            public FailAfterWriteTablePort(IUIPageTablePort inner)
            {
                _inner = inner;
            }

            public int RollbackCalls { get; private set; }

            /// <summary>模拟核验读表失败（三态 Unknown），验证不得声称“已复核”。</summary>
            public bool LookupFails { get; set; }

            public bool TryListPages(out List<UIPageTableRow> rows, out string error)
                => _inner.TryListPages(out rows, out error);

            public int SuggestId() => _inner.SuggestId();

            public bool Check(string pageName, int id, int layer, bool fullScreen, out string error)
                => _inner.Check(pageName, id, layer, fullScreen, out error);

            public UIPageTableWriteResult Write(string pageName, int id, int layer, bool fullScreen)
            {
                UIPageTableWriteResult written = _inner.Write(pageName, id, layer, fullScreen);
                return written.Ok
                    ? UIPageTableWriteResult.Failure("模拟：进程在保存后被中断", written.BackupPath)
                    : written;
            }

            public bool Rollback(string backup, out string error)
            {
                RollbackCalls++;
                return _inner.Rollback(backup, out error);
            }

            public UIPageRowLookup LookupRow(string pageName, int id, out UIPageTableRow row, out string error)
            {
                if (LookupFails)
                {
                    row = null;
                    error = "模拟：读表失败";
                    return UIPageRowLookup.Unknown;
                }
                return _inner.LookupRow(pageName, id, out row, out error);
            }
        }

        /// <summary>写成功但报失败；首次核验可见行，回滚后核验读表失败（覆盖 RollbackAndVerify 的 Unknown 分支）。</summary>
        private sealed class UnknownAfterRollbackTablePort : IUIPageTablePort
        {
            private readonly IUIPageTablePort _inner;

            public UnknownAfterRollbackTablePort(IUIPageTablePort inner) => _inner = inner;

            public bool TryListPages(out List<UIPageTableRow> rows, out string error)
                => _inner.TryListPages(out rows, out error);

            public int SuggestId() => _inner.SuggestId();

            public bool Check(string pageName, int id, int layer, bool fullScreen, out string error)
                => _inner.Check(pageName, id, layer, fullScreen, out error);

            public UIPageTableWriteResult Write(string pageName, int id, int layer, bool fullScreen)
            {
                UIPageTableWriteResult written = _inner.Write(pageName, id, layer, fullScreen);
                return written.Ok
                    ? UIPageTableWriteResult.Failure("模拟：保存后中断", written.BackupPath)
                    : written;
            }

            public bool Rollback(string backup, out string error)
            {
                _inner.Rollback(backup, out error);
                RolledBack = true;
                return true;
            }

            public bool RolledBack { get; private set; }

            public UIPageRowLookup LookupRow(string pageName, int id, out UIPageTableRow row, out string error)
            {
                if (RolledBack)
                {
                    row = null;
                    error = "模拟：回滚后读表失败";
                    return UIPageRowLookup.Unknown;
                }
                return _inner.LookupRow(pageName, id, out row, out error);
            }
        }
    }
}
