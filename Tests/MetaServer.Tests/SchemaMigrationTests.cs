using System;
using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using MetaServer.Contracts.Persistence;
using MetaServer.Infrastructure.Persistence;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// Schema 迁移契约（Meta 专项 §9.1"索引与迁移显式版本化；迁移失败有明确结果与回滚方式，
    /// 不静默半迁移"、§14 必备故障矩阵"迁移失败"）。
    /// </summary>
    public sealed class SchemaMigrationTests
    {
        private static SchemaMigrationRunner Runner(
            FakeMigrationStep[] steps, FakeSchemaVersionState state)
        {
            return new SchemaMigrationRunner(steps, new FakeSchemaVersionStore(state));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 步骤表有缺口_显式拒绝()
        {
            var state = new FakeSchemaVersionState();
            var runner = Runner(
                new[] { new FakeMigrationStep(1), new FakeMigrationStep(3) }, state);

            var outcome = await runner.MigrateAsync(2, CancellationToken.None);

            var rejected = Assert.IsType<MigrationOutcome.Rejected>(outcome);
            Assert.Contains("不连续", rejected.Reason);
            Assert.Equal(0, state.Current);   // 什么都没动
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 步骤表有重复_显式拒绝()
        {
            var state = new FakeSchemaVersionState();
            var runner = Runner(
                new[] { new FakeMigrationStep(1), new FakeMigrationStep(1) }, state);

            var outcome = await runner.MigrateAsync(2, CancellationToken.None);

            Assert.IsType<MigrationOutcome.Rejected>(outcome);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 目标低于当前版本_显式拒绝不降级()
        {
            var state = new FakeSchemaVersionState { Current = 3 };
            var runner = Runner(new[] { new FakeMigrationStep(1), new FakeMigrationStep(2) }, state);

            var outcome = await runner.MigrateAsync(2, CancellationToken.None);

            var rejected = Assert.IsType<MigrationOutcome.Rejected>(outcome);
            Assert.Contains("低于当前版本", rejected.Reason);
            Assert.Equal(3, state.Current);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 目标超出步骤表覆盖_显式拒绝()
        {
            var state = new FakeSchemaVersionState();
            var runner = Runner(new[] { new FakeMigrationStep(1) }, state);

            var outcome = await runner.MigrateAsync(2, CancellationToken.None);

            Assert.IsType<MigrationOutcome.Rejected>(outcome);
            Assert.Equal(0, state.Current);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 空步骤表_目标零_UpToDate()
        {
            var state = new FakeSchemaVersionState();
            var runner = Runner(Array.Empty<FakeMigrationStep>(), state);

            var outcome = await runner.MigrateAsync(0, CancellationToken.None);

            var upToDate = Assert.IsType<MigrationOutcome.UpToDate>(outcome);
            Assert.Equal(0, upToDate.CurrentVersion);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 已在目标版本_UpToDate_不重复应用()
        {
            var state = new FakeSchemaVersionState { Current = 2 };
            FakeMigrationStep[] steps =
            {
                new FakeMigrationStep(1), new FakeMigrationStep(2), new FakeMigrationStep(3),
            };
            var runner = Runner(steps, state);

            var outcome = await runner.MigrateAsync(2, CancellationToken.None);

            Assert.IsType<MigrationOutcome.UpToDate>(outcome);
            Assert.All(steps, step => Assert.Equal(0, step.MigrateCalls));   // 未触碰任何步骤
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 从零迁移_按序应用_逐版本写回()
        {
            var state = new FakeSchemaVersionState();
            FakeMigrationStep[] steps =
            {
                new FakeMigrationStep(1), new FakeMigrationStep(2), new FakeMigrationStep(3),
            };
            var runner = Runner(steps, state);

            var outcome = await runner.MigrateAsync(3, CancellationToken.None);

            var completed = Assert.IsType<MigrationOutcome.Completed>(outcome);
            Assert.Equal(new long[] { 1, 2, 3 }, completed.AppliedVersions);
            Assert.Equal(3, state.Current);   // 每步成功即写回（确认点）
            Assert.All(steps, step => Assert.Equal(1, step.MigrateCalls));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 部分已应用_只执行缺失段()
        {
            var state = new FakeSchemaVersionState { Current = 1 };
            FakeMigrationStep[] steps =
            {
                new FakeMigrationStep(1), new FakeMigrationStep(2), new FakeMigrationStep(3),
            };
            var runner = Runner(steps, state);

            var outcome = await runner.MigrateAsync(3, CancellationToken.None);

            var completed = Assert.IsType<MigrationOutcome.Completed>(outcome);
            Assert.Equal(new long[] { 2, 3 }, completed.AppliedVersions);
            Assert.Equal(0, steps[0].MigrateCalls);   // 已确认步骤不重复应用
            Assert.Equal(1, steps[1].MigrateCalls);
            Assert.Equal(1, steps[2].MigrateCalls);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 中途失败_逆序回滚_报告恢复版本()
        {
            var state = new FakeSchemaVersionState();
            FakeMigrationStep[] steps =
            {
                new FakeMigrationStep(1),
                new FakeMigrationStep(2),
                new FakeMigrationStep(3, migrateFailure: new InvalidOperationException("注入：v3 失败")),
            };
            var runner = Runner(steps, state);

            var outcome = await runner.MigrateAsync(3, CancellationToken.None);

            var failed = Assert.IsType<MigrationOutcome.Failed>(outcome);
            Assert.Equal(3, failed.FailedAtVersion);
            Assert.Contains("注入", failed.Reason);
            Assert.False(failed.RollbackFailed);
            Assert.Equal(0, failed.RecoveredVersion);   // 已应用步骤全部回退，版本归零
            Assert.Equal(0, state.Current);
            Assert.Equal(1, steps[2].RollbackCalls);    // 失败步本身也被回滚（可能部分应用）
            Assert.Equal(1, steps[1].RollbackCalls);    // 已确认步骤逆序回滚
            Assert.Equal(1, steps[0].RollbackCalls);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 失败步回滚也失败_显式报告()
        {
            var state = new FakeSchemaVersionState();
            FakeMigrationStep[] steps =
            {
                new FakeMigrationStep(1),
                new FakeMigrationStep(2,
                    migrateFailure: new InvalidOperationException("注入：v2 失败"),
                    rollbackFailure: new InvalidOperationException("注入：v2 回滚失败")),
            };
            var runner = Runner(steps, state);

            var outcome = await runner.MigrateAsync(2, CancellationToken.None);

            var failed = Assert.IsType<MigrationOutcome.Failed>(outcome);
            Assert.Equal(2, failed.FailedAtVersion);
            Assert.True(failed.RollbackFailed);          // 回滚失败必须显式，不得静默
            Assert.Equal(0, failed.RecoveredVersion);    // 步骤 1 仍被尽力回退
            Assert.Equal(0, state.Current);
        }
    }
}
