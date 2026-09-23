using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// FileActivationRecordIO 用例（C1-⑩）：文件往返、损坏/缺失/未知结构版本 → null（不可信即弃——
    /// 回 builtin 起点，fail-safe）。FileSys 在 EditMode 直读 persistentDataPath（无 YooAsset 依赖）。
    /// 用独立测试路径，不触碰真实 activation.json。
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
            io.Save(new ActivationRecord
            {
                ConfirmedReleaseId = "release-2",
                ConfirmedVersion = 7UL,
                PendingReleaseId = "release-3",
                PendingState = ActivationState.PendingActivation,
                RecoveryAttempts = 2,
                LastFailure = "unit-test",
            });

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
    }
}
