using System;
using System.Collections.Generic;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 激活事务存储用例（C1-⑩：《热更与内容发布专项设计》§8——Confirmed/Candidate/PendingActivation
    /// 三态记录与中断恢复决策）：启动回退已确认版本（持久化、计数有上限）、确认单调推进、
    /// 损坏/缺失记录回内置、ActiveGeneration 反映 Confirmed。内存假 IO——不落盘。
    /// </summary>
    public sealed class ActivationTransactionStoreTests
    {
        /// <summary>内存 IO 假件：可预置记录、捕获保存次数；损坏注入 = 抛（真实件返回 null 的等价路径单独测）。</summary>
        private sealed class MemoryIO : IActivationRecordIO
        {
            public ActivationRecord Preloaded;
            public List<ActivationRecord> Saved = new List<ActivationRecord>();

            public ActivationRecord TryLoad() => Preloaded;

            public void Save(ActivationRecord record)
            {
                Saved.Add(record);
                Preloaded = record;                            // 保存后可再读（往返）
            }
        }

        private static ActivationTransactionStore Store(MemoryIO io) => new ActivationTransactionStore(io);

        [Fact]
        public void 无记录_全新安装_builtin起点_恢复无动作()
        {
            var io = new MemoryIO();                          // 无预置记录
            var store = Store(io);

            var record = store.RecoverOnStartup();

            Assert.Equal("builtin", record.ConfirmedReleaseId);
            Assert.Equal(0UL, record.ConfirmedVersion);
            Assert.Null(record.PendingReleaseId);             // 无在途
            Assert.Empty(io.Saved);                           // 干净态不写盘
            Assert.Equal(new ContentGeneration("builtin", 0UL), store.ActiveGeneration);
        }

        [Fact]
        public void 在途候选_启动恢复_回退Confirmed_持久化()
        {
            var io = new MemoryIO
            {
                Preloaded = new ActivationRecord
                {
                    ConfirmedReleaseId = "builtin",
                    PendingReleaseId = "release-2",
                    PendingState = ActivationState.Candidate,
                }
            };
            var store = Store(io);

            var record = store.RecoverOnStartup();

            Assert.Null(record.PendingReleaseId);             // 在途清空（回退已确认版本）
            Assert.Equal(1, record.RecoveryAttempts);         // 尝试计数
            Assert.Equal("builtin", record.ConfirmedReleaseId);
            Assert.Single(io.Saved);                          // 回退决策已持久化
            Assert.Equal("builtin", io.Preloaded.ConfirmedReleaseId);
        }

        [Fact]
        public void 在途待激活_启动恢复_同样回退_不使用半成品()
        {
            var io = new MemoryIO
            {
                Preloaded = new ActivationRecord
                {
                    ConfirmedReleaseId = "release-1",
                    ConfirmedVersion = 3UL,
                    PendingReleaseId = "release-2",
                    PendingState = ActivationState.PendingActivation,
                }
            };
            var store = Store(io);

            var record = store.RecoverOnStartup();

            Assert.Null(record.PendingReleaseId);
            Assert.Equal("release-1", record.ConfirmedReleaseId);   // 回退到上一可用版本（不是半成品）
            Assert.Equal(new ContentGeneration("release-1", 3UL), store.ActiveGeneration);
        }

        [Fact]
        public void 恢复超限_仍以Confirmed继续_失败留档_不无限重启()
        {
            var io = new MemoryIO
            {
                Preloaded = new ActivationRecord
                {
                    ConfirmedReleaseId = "builtin",
                    PendingReleaseId = "release-9",
                    PendingState = ActivationState.PendingActivation,
                    RecoveryAttempts = ActivationTransactionStore.MaxRecoveryAttempts,   // 已达上限
                }
            };
            var store = Store(io);

            var record = store.RecoverOnStartup();

            Assert.Null(record.PendingReleaseId);             // 放弃在途
            Assert.Equal("builtin", record.ConfirmedReleaseId);
            Assert.NotNull(record.LastFailure);               // 失败原因留档（诊断）
            Assert.Contains("release-9", record.LastFailure);
            Assert.Contains("超上限", record.LastFailure);
        }

        [Fact]
        public void 确认提交_单调推进_清在途_失败留档清空()
        {
            var io = new MemoryIO();
            var store = Store(io);

            store.BeginCandidate("release-2");
            store.MarkPendingActivation();
            store.Confirm("release-2", 5UL);

            var record = store.Current;
            Assert.Equal("release-2", record.ConfirmedReleaseId);
            Assert.Equal(5UL, record.ConfirmedVersion);
            Assert.Null(record.PendingReleaseId);
            Assert.Null(record.LastFailure);
            Assert.Equal(new ContentGeneration("release-2", 5UL), store.ActiveGeneration);
            Assert.Equal(3, io.Saved.Count);                  // Begin/MarkPending/Confirm 三次持久化
        }

        [Fact]
        public void 待激活标记_无在途候选时拒绝()
        {
            var store = Store(new MemoryIO());
            Assert.Throws<InvalidOperationException>(() => store.MarkPendingActivation());
        }

        [Fact]
        public void 候选开始_空releaseId拒绝()
        {
            var store = Store(new MemoryIO());
            Assert.Throws<ArgumentNullException>(() => store.BeginCandidate(null));
            Assert.Throws<ArgumentNullException>(() => store.Confirm("", 1UL));
        }

        [Fact]
        public void 失败记录_留档且不改Confirmed()
        {
            var io = new MemoryIO
            {
                Preloaded = new ActivationRecord { ConfirmedReleaseId = "release-1", ConfirmedVersion = 2UL },
            };
            var store = Store(io);

            store.RecordFailure("候选校验失败：tbcombatnum 损坏");

            Assert.Equal("release-1", store.Current.ConfirmedReleaseId);   // Confirmed 不动
            Assert.Equal("候选校验失败：tbcombatnum 损坏", store.Current.LastFailure);
        }
    }
}
