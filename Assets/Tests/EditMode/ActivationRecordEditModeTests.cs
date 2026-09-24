using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// FileActivationRecordIO 用例：文件往返、损坏/缺失/未知结构版本 → null（不可信即弃——
    /// 回 builtin 起点，fail-safe）；**完整性保护**（半截/被改写的记录一律不可信，
    /// 《热更与内容发布专项设计》§8）。FileSys 在 EditMode 直读 persistentDataPath（无 YooAsset 依赖）。
    /// 用独立测试路径，不触碰真实 activation.json。
    ///
    /// 注：**生产路径由 ActivationTransactionStore 提交（自动盖章）**，业务不应直接 Save 未盖章记录；
    /// 本文件的"未盖章 → 不可信"负例正是这一纪律的回归卡。
    /// </summary>
    public sealed class ActivationRecordEditModeTests : UnityTestBase
    {
        private const string TestPath = "test_activation_record.json";

        [SetUp]
        protected void InitFileSys()
        {
            FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
            if (FileSys.Exists(TestPath)) FileSys.Delete(TestPath);   // 残留清理（上次运行中断）
        }

        [TearDown]
        protected void CleanUp()
        {
            if (FileSys.Exists(TestPath)) FileSys.Delete(TestPath);
        }

        [Test]
        public void 文件往返_保存后可读回_字段一致()
        {
            var io = new FileActivationRecordIO(TestPath);
            io.Save(ActivationRecordIntegrity.Stamp(new ActivationRecord   // 提交前盖章（生产路径由 Store 完成）
            {
                ConfirmedReleaseId = "release-2",
                ConfirmedVersion = 7UL,
                PendingReleaseId = "release-3",
                PendingState = ActivationState.PendingActivation,
                RecoveryAttempts = 2,
                LastFailure = "unit-test",
            }));

            var loaded = io.TryLoad();

            Assert.NotNull(loaded);
            Assert.AreEqual("release-2", loaded.ConfirmedReleaseId);
            Assert.AreEqual(7UL, loaded.ConfirmedVersion);
            Assert.AreEqual("release-3", loaded.PendingReleaseId);
            Assert.AreEqual(ActivationState.PendingActivation, loaded.PendingState);
            Assert.AreEqual(2, loaded.RecoveryAttempts);
            Assert.AreEqual("unit-test", loaded.LastFailure);
        }

        [Test]
        public void 缺文件_返回null_全新安装路径()
        {
            var io = new FileActivationRecordIO(TestPath);
            Assert.Null(io.TryLoad());
        }

        [Test]
        public void 损坏文件_返回null_回内置起点()
        {
            FileSys.WriteAllText(TestPath, "{ not-valid-json !!!");
            var io = new FileActivationRecordIO(TestPath);

            Assert.Null(io.TryLoad(), "损坏记录必须按不可信处理——fail-safe 回 builtin，不阻止启动");
        }

        [Test]
        public void 未知结构版本_返回null_不猜语义()
        {
            // 未来的记录结构演进：本版本不认识 = 不可信（热更 §8 完整性保护的最小形态）
            FileSys.WriteAllText(TestPath, "{\"SchemaVersion\":99,\"ConfirmedReleaseId\":\"future\"}");
            var io = new FileActivationRecordIO(TestPath);

            Assert.Null(io.TryLoad());
        }

        // ---- 完整性保护（§8）----

        [Test]
        public void 完整性_未盖章记录_视为不可信()
        {
            // 回归卡：无完整性校验值的记录一律不可信（否则任何"合法 JSON 但内容错"的文件都会被采用）。
            var io = new FileActivationRecordIO(TestPath);
            io.Save(new ActivationRecord { ConfirmedReleaseId = "release-x", ConfirmedVersion = 1 });

            Assert.IsNull(io.TryLoad(), "未盖章 = 不可信 = 回 builtin 起点");
        }

        [Test]
        public void 完整性_记录被改写_视为不可信()
        {
            var io = new FileActivationRecordIO(TestPath);
            var record = ActivationRecordIntegrity.Stamp(new ActivationRecord
            {
                ConfirmedReleaseId = "release-y",
                ConfirmedVersion = 5,
            });
            io.Save(record);

            Assert.IsNotNull(io.TryLoad(), "盖章记录可读回");

            // 模拟外部改写（改内容但校验值不变）
            record.ConfirmedReleaseId = "release-evil";
            io.Save(record);                                     // 注意：Save 不重新盖章（IO 不做隐式修复）

            Assert.IsNull(io.TryLoad(), "内容与校验值不符 = 不可信");
        }

        [Test]
        public void 完整性_丢字段的半截记录_视为不可信()
        {
            // §8 的写入中断场景：记录"还能解析"但丢了尾部字段——完整性必须拦住。
            var io = new FileActivationRecordIO(TestPath);
            var full = ActivationRecordIntegrity.Stamp(new ActivationRecord
            {
                ConfirmedReleaseId = "release-z",
                ConfirmedVersion = 9,
                PendingReleaseId = "release-cand",
                TransactionId = "txn-1",
            });
            io.Save(full);

            var truncated = new ActivationRecord
            {
                ConfirmedReleaseId = "release-z",
                ConfirmedVersion = 9,
                Integrity = full.Integrity,
            };
            io.Save(truncated);

            Assert.IsNull(io.TryLoad(), "丢字段的半截记录不可信");
        }

        [Test]
        public void 完整性_Store提交的往返_读回后校验通过()
        {
            // 生产路径：Store 提交 → 读回 → 再建 Store 续接（端到端，不经手工盖章）。
            var io = new FileActivationRecordIO(TestPath);
            var store = new ActivationTransactionStore(io) { TransactionIdFactory = () => "txn-e2e" };
            store.BeginCandidate("release-e2e");

            var reopened = new ActivationTransactionStore(new FileActivationRecordIO(TestPath));
            Assert.IsNotNull(reopened.Current, "读回成功");
            Assert.AreEqual("release-e2e", reopened.Current.PendingReleaseId);
            Assert.AreEqual("txn-e2e", reopened.Current.TransactionId);
            Assert.AreEqual(1, reopened.Current.RecordSequence);
        }
    }
}