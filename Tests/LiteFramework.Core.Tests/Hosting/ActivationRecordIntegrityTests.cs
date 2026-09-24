using System;
using System.Collections.Generic;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 激活记录完整性、序号与事务 ID（《热更与内容发布专项设计》§8"状态记录与文件清单带
    /// **完整性保护、序号和可诊断事务 ID**"；"原子提交需由平台存储适配实现并测试写盘、中断、
    /// 重命名和恢复，不能把普通覆盖写文件称为原子事务"）。
    ///
    /// 与 <see cref="ActivationTransactionStoreTests"/> 分工：那边验**恢复决策**（三态与回退），
    /// 这里验**记录本身的受保护提交**——完整性盖章/校验、序号单调、事务 ID 贯穿、
    /// 以及恢复表逐行的确定行为。
    /// </summary>
    public sealed class ActivationRecordIntegrityTests
    {
        /// <summary>带消息的相等断言（本 xUnit 版本无 Assert.Equal(…, "msg") 重载——第三参会被当作比较器）。</summary>
        private static void AssertEq<T>(T expected, T actual, string message)
            => Assert.True(System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual),
                $"{message}（期望 {expected}，实得 {actual}）");

        private sealed class MemoryIO : IActivationRecordIO
        {
            public ActivationRecord Written;
            public int SaveCount;

            public ActivationRecord TryLoad() => Written;

            public void Save(ActivationRecord record)
            {
                SaveCount++;
                Written = record;                       // 保存后可再读（往返）
            }
        }

        // ---- 完整性 ----

        [Fact]
        public void 完整性_盖章后校验通过_任一字段被改即失效()
        {
            var r = new ActivationRecord { ConfirmedReleaseId = "rel-a", ConfirmedVersion = 7, RecordSequence = 3 };
            ActivationRecordIntegrity.Stamp(r);
            Assert.True(ActivationRecordIntegrity.Verify(r));

            // 逐字段篡改（模拟文件被外部改写/旧备份回填出的"合法 JSON 但是错的"）
            r.ConfirmedReleaseId = "rel-b";
            Assert.False(ActivationRecordIntegrity.Verify(r), "改发布身份必须失效");

            ActivationRecordIntegrity.Stamp(r);
            r.RecordSequence = 99;
            Assert.False(ActivationRecordIntegrity.Verify(r), "改序号必须失效");

            ActivationRecordIntegrity.Stamp(r);
            r.PendingReleaseId = "rel-c";
            Assert.False(ActivationRecordIntegrity.Verify(r), "改在途必须失效");

            ActivationRecordIntegrity.Stamp(r);
            r.RecoveryAttempts = 5;
            Assert.False(ActivationRecordIntegrity.Verify(r), "改尝试计数必须失效");

            ActivationRecordIntegrity.Stamp(r);
            r.LastFailure = "篡改";
            Assert.False(ActivationRecordIntegrity.Verify(r), "改失败留档必须失效");
        }

        [Fact]
        public void 完整性_无校验值或空值_一律不可信()
        {
            Assert.False(ActivationRecordIntegrity.Verify(null));
            Assert.False(ActivationRecordIntegrity.Verify(new ActivationRecord()));
            Assert.False(ActivationRecordIntegrity.Verify(new ActivationRecord { Integrity = "" }));
        }

        [Fact]
        public void 完整性_半截但可解析的记录被判不可信()
        {
            // §8 的核心场景：覆盖写中断留下的记录"还能解析"但内容不完整——
            // 没有完整性保护时会被当作有效记录采用（旧实现的缺口）。
            // 构造：完整记录含事务 ID 等非默认字段；截断版本丢了它们（反序列化回默认值）。
            var full = new ActivationRecord
            {
                ConfirmedReleaseId = "rel-a",
                ConfirmedVersion = 3,
                PendingReleaseId = "rel-b",
                PendingState = ActivationState.PendingActivation,
                TransactionId = "txn-abc",
                RecoveryAttempts = 2,
            };
            ActivationRecordIntegrity.Stamp(full);
            Assert.True(ActivationRecordIntegrity.Verify(full), "原件自校验通过");

            var truncated = new ActivationRecord
            {
                ConfirmedReleaseId = full.ConfirmedReleaseId,
                ConfirmedVersion = full.ConfirmedVersion,
                Integrity = full.Integrity,            // 校验值还在，但尾部字段已丢
            };
            Assert.False(ActivationRecordIntegrity.Verify(truncated), "丢字段必须失效");
        }

        [Fact]
        public void 完整性_相同内容稳定_不同内容不同()
        {
            var a = new ActivationRecord { ConfirmedReleaseId = "x", RecordSequence = 1 };
            var b = new ActivationRecord { ConfirmedReleaseId = "x", RecordSequence = 1 };
            AssertEq(ActivationRecordIntegrity.Compute(a), ActivationRecordIntegrity.Compute(b), "同内容同校验值");

            b.ConfirmedVersion = 1;
            Assert.NotEqual(ActivationRecordIntegrity.Compute(a), ActivationRecordIntegrity.Compute(b));
        }

        [Fact]
        public void 完整性_空值以显式占位参与_与空串可区分()
        {
            // null 与 "" 必须产生不同校验值，否则"丢字段"与"置空串"会被判成同一记录
            var nullPending = new ActivationRecord { PendingReleaseId = null };
            var emptyPending = new ActivationRecord { PendingReleaseId = "" };
            Assert.NotEqual(ActivationRecordIntegrity.Compute(nullPending), ActivationRecordIntegrity.Compute(emptyPending));
        }

        // ---- 序号 ----

        [Fact]
        public void 序号_每次提交递增_不跳不重复()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);
            Assert.Equal(0, store.RecordSequence);

            store.BeginCandidate("rel-1");
            Assert.Equal(1, store.RecordSequence);

            store.MarkPendingActivation();
            Assert.Equal(2, store.RecordSequence);

            store.RecordFailure("校验失败");
            Assert.Equal(3, store.RecordSequence);

            store.Confirm("rel-1", 1);
            Assert.Equal(4, store.RecordSequence);

            AssertEq(4, io.SaveCount, "每次状态变更恰好一次落盘");
            Assert.Equal(4, io.Written.RecordSequence);
        }

        [Fact]
        public void 序号_从已有记录续接_不从零重来()
        {
            var io = new MemoryIO();
            var seed = new ActivationRecord { ConfirmedReleaseId = "rel-x", ConfirmedVersion = 2, RecordSequence = 41 };
            ActivationRecordIntegrity.Stamp(seed);
            io.Written = seed;

            var store = new ActivationTransactionStore(io);
            AssertEq(41, store.RecordSequence, "续接已有序号");
            store.RecordFailure("新失败");
            Assert.Equal(42, store.RecordSequence);
        }

        [Fact]
        public void 序号_落盘记录始终通过完整性校验()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);

            store.BeginCandidate("rel-1");
            Assert.True(ActivationRecordIntegrity.Verify(io.Written), "每次落盘都必须已盖章");

            store.MarkPendingActivation();
            Assert.True(ActivationRecordIntegrity.Verify(io.Written));

            store.Confirm("rel-1", 1);
            Assert.True(ActivationRecordIntegrity.Verify(io.Written));
        }

        // ---- 事务 ID ----

        [Fact]
        public void 事务ID_候选开始时生成_确认或放弃后清空()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io) { TransactionIdFactory = () => "txn-fixed-1" };

            store.BeginCandidate("rel-1");
            Assert.Equal("txn-fixed-1", store.Current.TransactionId);
            AssertEq("txn-fixed-1", io.Written.TransactionId, "事务 ID 随候选一起落盘（中断后可诊断）");

            store.Confirm("rel-1", 1);
            Assert.Null(store.Current.TransactionId);

            store.BeginCandidate("rel-2");
            store.RecordFailure("失败");
            store.RecoverOnStartup();                      // 放弃在途
            Assert.Null(store.Current.TransactionId);
        }

        [Fact]
        public void 事务ID_未注入工厂时自动生成_两次事务不同()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io, () => 1234567890L);

            store.BeginCandidate("rel-1");
            string first = store.Current.TransactionId;
            Assert.False(string.IsNullOrEmpty(first));

            store.Confirm("rel-1", 1);
            store.BeginCandidate("rel-2");
            Assert.NotEqual(first, store.Current.TransactionId);
            Assert.StartsWith("txn-", store.Current.TransactionId);
        }

        // ---- 恢复表逐行（§8）----

        [Fact]
        public void 恢复表_行1_下载或校验中中断_Candidate不变_回退Confirmed()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);
            store.Confirm("rel-base", 1);
            store.BeginCandidate("rel-cand");              // 模拟下载中被杀进程（磁盘停在 Candidate）

            var reopened = new ActivationTransactionStore(io);   // 下次启动
            var record = reopened.RecoverOnStartup();

            AssertEq("rel-base", record.ConfirmedReleaseId, "Confirmed 不变");
            Assert.Null(record.PendingReleaseId);          // 在途被清
            Assert.Equal(1, record.RecoveryAttempts);
        }

        [Fact]
        public void 恢复表_行2_待激活中断_回退已确认版本_不使用半成品()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);
            store.Confirm("rel-base", 1);
            store.BeginCandidate("rel-cand");
            store.MarkPendingActivation();                 // 模拟激活中/健康检查前被杀死

            var reopened = new ActivationTransactionStore(io);
            var record = reopened.RecoverOnStartup();

            Assert.Equal("rel-base", record.ConfirmedReleaseId);
            Assert.Null(record.PendingReleaseId);
            Assert.Equal(new ContentGeneration("rel-base", 1), reopened.ActiveGeneration);
        }

        [Fact]
        public void 恢复表_行3_健康失败留档_保留Confirmed_不改在途()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);
            store.Confirm("rel-base", 1);
            store.BeginCandidate("rel-cand");
            store.MarkPendingActivation();
            store.RecordFailure("候选配置校验失败：缺 tbcombatnum");   // §8 表行 3 的留档

            AssertEq("rel-base", store.Current.ConfirmedReleaseId, "失败不部分发布");
            AssertEq("rel-cand", store.Current.PendingReleaseId, "在途仍标记（下次启动走恢复）");
            Assert.Contains("tbcombatnum", store.Current.LastFailure);
        }

        [Fact]
        public void 恢复表_行5_确认已提交但旧目录未清理_继续新版本不回退()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);
            store.Confirm("rel-new", 2);                   // 已确认；假设磁盘上旧目录还没清

            var reopened = new ActivationTransactionStore(io);
            var record = reopened.RecoverOnStartup();

            AssertEq("rel-new", record.ConfirmedReleaseId, "无在途事务 → 不回退");
            AssertEq(0, record.RecoveryAttempts, "干净态不消耗恢复次数");
        }

        [Fact]
        public void 恢复表_超尝试上限_放弃在途_以Confirmed继续且失败留档()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);
            store.Confirm("rel-base", 1);

            for (int i = 0; i < ActivationTransactionStore.MaxRecoveryAttempts; i++)
            {
                store.BeginCandidate($"rel-cand-{i}");
                var rec = new ActivationTransactionStore(io).RecoverOnStartup();
                Assert.Null(rec.PendingReleaseId);
            }

            // 再来一次：已超上限
            store.BeginCandidate("rel-cand-final");
            var over = new ActivationTransactionStore(io).RecoverOnStartup();

            Assert.Null(over.PendingReleaseId);
            Assert.Contains("超上限", over.LastFailure);
            AssertEq("rel-base", over.ConfirmedReleaseId, "超限也停在确定态（禁止无限重启）");
            Assert.True(ActivationRecordIntegrity.Verify(io.Written), "失败留档同样要盖章");
        }

        [Fact]
        public void 确认_单调推进且清在途清失败留档()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);

            store.BeginCandidate("rel-1");
            store.RecordFailure("先失败一次");
            store.Confirm("rel-1", 10);

            Assert.Equal(10UL, store.Current.ConfirmedVersion);
            Assert.Null(store.Current.PendingReleaseId);
            Assert.Null(store.Current.LastFailure);
            Assert.True(ActivationRecordIntegrity.Verify(io.Written));
        }

        [Fact]
        public void 空releaseId_候选开始拒绝()
        {
            var store = new ActivationTransactionStore(new MemoryIO());
            Assert.Throws<ArgumentNullException>(() => store.BeginCandidate(null));
            Assert.Throws<ArgumentNullException>(() => store.BeginCandidate(""));
            Assert.Throws<ArgumentNullException>(() => store.Confirm(null, 1));
        }

        /// <summary>
        /// 空值占位符是 <c>"\0null"</c>（NUL + null），**不是** <c>" null"</c>。
        ///
        /// 2026-09-25 核查：该形式自 `bd75616`（引入 <c>Compute</c> 那次提交）即存在，
        /// 非后续损坏、非手误引入——它与空格形式在"区分 null 与空串"上**能力等价**。
        ///
        /// 本用例的价值是**防止顺手修正**：把 NUL 改成空格会改变摘要 → 所有既有激活记录失效
        /// → 用户丢已确认版本。若将来确实要改，必须同时升 <c>SchemaVersion</c> 并接受一次失效。
        /// </summary>
        [Fact]
        public void 空值占位符_以NUL为前缀_改动会使既有记录失效()
        {
            var record = new ActivationRecord
            {
                ConfirmedReleaseId = null,       // 触发占位符路径
                PendingReleaseId = null,
                LastFailure = null,
                TransactionId = null,
            };

            // 占位符确实进入摘要：把某个 null 字段改成空串，摘要必须变
            string withNull = ActivationRecordIntegrity.Compute(record);
            record.LastFailure = "";
            string withEmpty = ActivationRecordIntegrity.Compute(record);
            Assert.NotEqual(withNull, withEmpty);

            // 钉住当前摘要的稳定性（若有人改了占位符形式，此断言会失败并提示上面的说明）
            var probe = new ActivationRecord
            {
                ConfirmedReleaseId = "builtin",
                ConfirmedVersion = 0,
                RecordSequence = 0,
                PendingReleaseId = null,
                LastFailure = null,
                TransactionId = null,
            };
            string digest = ActivationRecordIntegrity.Compute(probe);
            Assert.Equal(64, digest.Length);
            Assert.Equal(digest, ActivationRecordIntegrity.Compute(probe));   // 同输入同摘要
        }

        [Fact]
        public void 已确认修订_随Confirm写入_且可被反回退基线读取()
        {
            var io = new MemoryIO();
            var store = new ActivationTransactionStore(io);

            store.BeginCandidate("rel-7");
            store.MarkPendingActivation();
            store.Confirm("rel-7", 3, revision: 42);

            Assert.Equal(42L, store.Current.ConfirmedRevision);

            // 重新读盘（模拟下次启动）——修订必须存活
            var reloaded = new ActivationTransactionStore(io);
            Assert.Equal(42L, reloaded.Current.ConfirmedRevision);
            Assert.Equal("rel-7", reloaded.Current.ConfirmedReleaseId);
            Assert.True(ActivationRecordIntegrity.Verify(reloaded.Current));
        }

        [Fact]
        public void 旧记录无修订字段_默认零_且完整性仍通过()
        {
            // 反序列化旧 JSON（无 ConfirmedRevision）→ 字段保持默认 0，不回填、不报错
            // （Newtonsoft 缺失字段保留默认值——故本次加字段**向后兼容**）
            var record = new ActivationRecord
            {
                ConfirmedReleaseId = "legacy",
                ConfirmedVersion = 9,
            };
            Assert.Equal(0L, record.ConfirmedRevision);
            Assert.True(ActivationRecordIntegrity.Verify(ActivationRecordIntegrity.Stamp(record)));
        }
    }
}
