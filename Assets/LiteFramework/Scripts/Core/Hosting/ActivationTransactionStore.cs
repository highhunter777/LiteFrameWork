using System;

namespace LiteFramework
{
    /// <summary>
    /// 激活事务状态（《热更与内容发布专项设计》§8——磁盘记录至少区分的三态）：
    /// 文件提交、运行时激活与健康确认分开记录；进程中断后从事务记录恢复。
    /// </summary>
    public enum ActivationState
    {
        /// <summary>候选事务进行中（下载/校验中——§8 表行 1 的中断区间：该阶段被中断时，下次启动按记录清理该候选的临时文件后以 Confirmed 继续）。</summary>
        Candidate = 0,

        /// <summary>已持久化"待激活"标记、运行时尚未通过健康检查——下一次启动的恢复决策点。</summary>
        PendingActivation = 1,

        /// <summary>健康检查通过、确认记录已写盘的当前正式版本。</summary>
        Confirmed = 2,
    }

    /// <summary>激活事务记录（持久化 DTO——序列化由 IO 适配承担，Core 不依赖具体格式）。</summary>
    public sealed class ActivationRecord
    {
        /// <summary>记录结构版本（不识别的版本 = 记录不可信，按无记录处理——fail-safe 回内置）。</summary>
        public int SchemaVersion = 1;

        /// <summary>当前已确认（健康检查通过）的发布身份；builtin = 内置兜底，永远合法可用。</summary>
        public string ConfirmedReleaseId = ContentGeneration.BuiltinReleaseId;

        /// <summary>已确认发布的代次值（单调）。</summary>
        public ulong ConfirmedVersion;

        /// <summary>
        /// 已确认发布的**发布修订**（§6 反回退"候选修订低于已确认修订即拒绝"）。
        ///
        /// **与 <see cref="ConfirmedVersion"/> 语义不同**：后者是同 Release 内的代次计数（每次确认 +1），
        /// 前者是发布描述里的单调修订（<see cref="ReleaseManifest.Revision"/>）。
        /// 防重放旧清单要用后者——拿代次当修订会误判。
        ///
        /// **未并入完整性摘要**：并进去会改变既有记录的摘要 → 全部失效（用户丢已确认版本）。
        /// 代价是**它本身不受完整性保护**（篡改该字段不会被检出）——故它只用于"提高拒绝门槛"，
        /// 不是安全边界；真正的重放防护由签名与发布修订单调性共同承担（§6）。
        /// 待下一次因其它原因升 <see cref="SchemaVersion"/> 时并入。
        /// </summary>
        public long ConfirmedRevision;

        /// <summary>在途发布身份（null = 无在途事务）。</summary>
        public string PendingReleaseId;

        /// <summary>在途状态（PendingReleaseId 非 null 时有效）。</summary>
        public ActivationState PendingState;

        /// <summary>累计恢复尝试次数（有上限——禁止无限重启，热更 §8）。</summary>
        public int RecoveryAttempts;

        /// <summary>最近一次失败原因（诊断；不上报敏感数据）。</summary>
        public string LastFailure;

        // ---- §8"状态记录与文件清单带完整性保护、序号和可诊断事务 ID" ----

        /// <summary>记录序号（每次成功提交 +1）。用于识别**回退到旧记录**（重放/文件系统还原）。
        /// 读到的序号低于内存已知序号即视为过期记录，不采用。</summary>
        public long RecordSequence;

        /// <summary>可诊断事务 ID（本次在途事务的身份；日志/工单据此定位）。</summary>
        public string TransactionId;

        /// <summary>
        /// 完整性校验值（对**除本字段外**的记录内容的稳定摘要；由 <see cref="ActivationRecordIntegrity"/> 计算）。
        /// 覆盖写被中断留下的半截 JSON 若仍可解析，会因校验值不符而被判不可信——
        /// 这是"普通覆盖写"与"受保护提交"的分界（§8"不能把普通覆盖写文件称为原子事务"）。
        /// </summary>
        public string Integrity;
    }

    /// <summary>
    /// 激活记录的完整性计算（§8"状态记录…带完整性保护"）。
    ///
    /// **为什么需要**：即使 <c>File.Replace</c> 把"写一半"挡在目标文件之外，也无法排除
    /// 文件系统还原、外部工具改写、旧备份回填等场景；且校验值是**序号+内容**的一致性凭据——
    /// 缺少它时，一份被截断但仍合法的 JSON（例如恰好丢了尾部字段）会被当作有效记录采用。
    ///
    /// 规范化规则（与内容摘要同一纪律）：字段顺序固定、序数比较、空值用显式标记占位——
    /// **不得**依赖反射遍历顺序或平台换行。
    ///
    /// **空值占位符是字面量 `"null"`**：`??` 只对 null 生效，空串 `""` 原样参与——
    /// 两者因此可区分（见 <c>null≠空串</c> 用例）。**规范化规则是既有磁盘记录的稳定性契约**：
    /// 改动占位符/字段顺序/序数口径会改变全部既有记录的摘要 → 校验失效 → 按无记录处理、
    /// 回内置重确认（用户丢已确认版本），故不得随手调整。
    /// </summary>
    public static class ActivationRecordIntegrity
    {
        /// <summary>计算完整性值（小写 hex）。调用方先算、再赋给 <see cref="ActivationRecord.Integrity"/> 后写盘。</summary>
        public static string Compute(ActivationRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            var sb = new System.Text.StringBuilder(256);
            sb.Append("v=").Append(r.SchemaVersion).Append('\n');
            sb.Append("seq=").Append(r.RecordSequence).Append('\n');
            sb.Append("confirmed=").Append(r.ConfirmedReleaseId ?? "null").Append('\n');
            sb.Append("cv=").Append(r.ConfirmedVersion).Append('\n');
            sb.Append("pending=").Append(r.PendingReleaseId ?? "null").Append('\n');
            sb.Append("pstate=").Append((int)r.PendingState).Append('\n');
            sb.Append("attempts=").Append(r.RecoveryAttempts).Append('\n');
            sb.Append("failure=").Append(r.LastFailure ?? "null").Append('\n');
            sb.Append("txn=").Append(r.TransactionId ?? "null");

            return ContentHash.Sha256Hex(sb.ToString());
        }

        /// <summary>校验记录完整性（false = 不可信，按无记录处理）。</summary>
        public static bool Verify(ActivationRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.Integrity)) return false;
            return ContentHash.HexEquals(r.Integrity, Compute(r));
        }

        /// <summary>计算并写入 <see cref="ActivationRecord.Integrity"/>（提交前调用）。</summary>
        public static ActivationRecord Stamp(ActivationRecord r)
        {
            r.Integrity = Compute(r);
            return r;
        }
    }

    /// <summary>激活记录的持久化端口（Unity 侧以 FileSys/JSON 实现；L1 用内存假件）。</summary>
    public interface IActivationRecordIO
    {
        /// <summary>读记录；无记录或记录不可解析 = 返回 null（调用方按"全新安装/损坏回内置"处理）。</summary>
        ActivationRecord TryLoad();

        /// <summary>原子写记录（实现方负责先验证后提交与临时文件切换——热更 §8"原子提交由平台存储适配实现"）。</summary>
        void Save(ActivationRecord record);
    }

    /// <summary>
    /// 激活事务存储（《热更与内容发布专项设计》§8 激活、健康确认与中断恢复）：
    ///
    /// - **启动恢复决策**：<see cref="RecoverOnStartup"/>——读记录；有在途（Candidate/PendingActivation）
    ///   即按恢复表回退，尝试计数 +1 并持久化；超上限仍以 Confirmed 继续（确定态，记录失败原因，
    ///   不无限重启）。
    /// - **确认提交**：<see cref="Confirm"/>——健康检查通过后写盘（Confirmed 单调推进，Pending 清空）。
    /// - **候选生命周期**：<see cref="BeginCandidate"/> → <see cref="MarkPendingActivation"/> → Confirm/RecordFailure。
    ///   磁盘上"确认写盘失败"（§8 表：不声称已确认）由 IO 实现方抛出——本类不吞。
    /// - **完整性**：每次写盘前盖章（<see cref="ActivationRecordIntegrity.Stamp"/>）；读入时校验，
    ///   不符即按无记录处理（fail-safe 回 builtin）——见 <see cref="IActivationRecordIO.TryLoad"/> 实现方。
    /// - **序号**：每次成功提交递增，用于识别过期记录回填。
    /// - **事务 ID**：每次 <see cref="BeginCandidate"/> 生成，贯穿该次事务的日志与恢复。
    /// </summary>
    public sealed class ActivationTransactionStore
    {
        /// <summary>恢复尝试上限（超过后不再尝试恢复，直接以 Confirmed 继续——热更 §8 次数有上限）。</summary>
        public const int MaxRecoveryAttempts = 3;

        private readonly IActivationRecordIO _io;
        private readonly Func<long> _nowMs;

        /// <summary>当前记录（构造时读入；启动恢复后为确定态）。</summary>
        public ActivationRecord Current { get; private set; }

        /// <summary>事务 ID 序号（诊断用；与时间戳共同构成可读 ID）。</summary>
        private static int s_txnSeq;

        /// <param name="nowMs">单调毫秒源（生成事务 ID 用；null = 环境计时器——测试可注入固定值）。</param>
        public ActivationTransactionStore(IActivationRecordIO io, Func<long> nowMs = null)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
            _nowMs = nowMs ?? DefaultNowMs;
            Current = _io.TryLoad() ?? new ActivationRecord();   // 无记录/不可信 = 全新安装（builtin 起点）
        }

        private static long DefaultNowMs()
            => System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>上次提交的记录序号（诊断/断言用）。</summary>
        public long RecordSequence => Current.RecordSequence;

        /// <summary>调用方给出的显式事务 ID（null = 自动生成）。测试可固定以便断言。</summary>
        public Func<string> TransactionIdFactory { get; set; }

        /// <summary>
        /// 诊断端口（可选；**组合根接线**——《框架先行建设与业务接入专项设计》§8「可诊断」）：
        /// Core 不认识具体诊断面（见 <see cref="IDiagRecordSink"/>），只在本类**掌握稳定分类码的落点**
        /// （失败留档/启动恢复）调用它。一次内容失败即可与版本/会话/房间面的记录按事务 ID 对齐。
        /// null = 未接线（事实仍在 <see cref="ActivationRecord.LastFailure"/> /
        /// <see cref="ActivationRecord.TransactionId"/> 的磁盘留档里）。
        /// </summary>
        public IDiagRecordSink DiagSink { get; set; }

        /// <summary>关联键构造（与诊断面 <c>JoinKey</c> 同纪律：字段序固定、空值写 "-"）——
        /// 内容/事务面的键形：`release=…;txn=…`。</summary>
        private static string ContentKey(string releaseId, string txnId)
            => "release=" + (string.IsNullOrEmpty(releaseId) ? "-" : releaseId)
             + ";txn=" + (string.IsNullOrEmpty(txnId) ? "-" : txnId);

        /// <summary>
        /// 启动恢复决策（§8 中断/失败阶段表逐行）：
        /// <list type="bullet">
        /// <item>无在途 → 干净态，直接返回 Confirmed。</item>
        /// <item>Candidate（下载/校验中）→ 清理该候选的临时归属，以 Confirmed 继续（§8 表行 1）。</item>
        /// <item>PendingActivation（已持久化待激活、运行时未健康）→ 尝试一次受控恢复；
        ///   无远程重取通道时 = 回退 Confirmed（§8 表行 2）。</item>
        /// <item>超尝试上限 → 放弃在途、以 Confirmed 继续（确定态，禁止无限重启）。</item>
        /// <item>正式内容健康检查失败 → 由调用方 <see cref="RecordFailure"/> 留档后再走本方法（§8 表行 3）。</item>
        /// </list>
        /// </summary>
        public ActivationRecord RecoverOnStartup()
        {
            if (Current.PendingReleaseId == null)
                return Current;                                  // 干净态：无在途事务

            string pendingRelease = Current.PendingReleaseId;     // 诊断用：清空前留副本（下两处清空在途字段）
            string pendingTxn = Current.TransactionId;
            Current.RecoveryAttempts++;
            if (Current.RecoveryAttempts > MaxRecoveryAttempts)
            {
                // 超限：放弃恢复尝试，以 Confirmed 继续（确定态）；失败原因留档供诊断
                Current.LastFailure = $"恢复尝试超上限（{Current.RecoveryAttempts - 1}），放弃在途 {Current.PendingReleaseId}——以 Confirmed {Current.ConfirmedReleaseId} 继续";
                Current.PendingReleaseId = null;
                Current.PendingState = default;
                Current.TransactionId = null;                    // 事务已了结（与正常恢复路径一致）
                Save();
                DiagSink?.Record("Txn", "ActivationRecovered",
                    ContentKey(pendingRelease, pendingTxn), Current.LastFailure);
                return Current;
            }

            // 受控恢复（首版形态 = 回退已确认版本；远程重取随下载接线批）
            Current.PendingReleaseId = null;
            Current.PendingState = default;
            Current.TransactionId = null;                        // 在途已放弃，事务 ID 不再有意义（§8 可诊断性：避免残留误导）
            Save();
            DiagSink?.Record("Txn", "ActivationRecovered", ContentKey(pendingRelease, pendingTxn),
                $"启动恢复：放弃在途 {pendingRelease ?? "-"}，以 Confirmed {Current.ConfirmedReleaseId} 继续（尝试 {Current.RecoveryAttempts}）");
            return Current;
        }

        /// <summary>开始一次候选事务（磁盘记录 Candidate——下载开始前进入，使下载/校验中中断有临时归属可清理（§8 表行 1）；校验通过、激活窗口前推进为 PendingActivation）。
        /// 同时生成可诊断事务 ID（§8）。</summary>
        public void BeginCandidate(string releaseId)
        {
            if (string.IsNullOrEmpty(releaseId)) throw new ArgumentNullException(nameof(releaseId));
            Current.PendingReleaseId = releaseId;
            Current.PendingState = ActivationState.Candidate;
            Current.TransactionId = TransactionIdFactory != null
                ? TransactionIdFactory()
                : $"txn-{_nowMs():x}-{System.Threading.Interlocked.Increment(ref s_txnSeq):x}";
            Save();
        }

        /// <summary>进入激活窗口前持久化 PendingActivation（进程中断后可恢复，§8 表行 2）。</summary>
        public void MarkPendingActivation()
        {
            if (Current.PendingReleaseId == null)
                throw new InvalidOperationException("无在途候选——MarkPendingActivation 必须在 BeginCandidate 之后");
            Current.PendingState = ActivationState.PendingActivation;
            Save();
        }

        /// <summary>健康检查通过，确认提交（Confirmed 单调推进；Pending 清空）。</summary>
        /// <param name="releaseId">已确认的发布身份。</param>
        /// <param name="version">同 Release 内的代次值。</param>
        /// <param name="revision">发布描述里的单调修订（§6 反回退基线）。</param>
        public void Confirm(string releaseId, ulong version, long revision = 0)
        {
            if (string.IsNullOrEmpty(releaseId)) throw new ArgumentNullException(nameof(releaseId));
            Current.ConfirmedReleaseId = releaseId;
            Current.ConfirmedVersion = version;
            Current.ConfirmedRevision = revision;
            Current.PendingReleaseId = null;
            Current.PendingState = default;
            Current.LastFailure = null;
            Current.TransactionId = null;                        // 事务已了结
            Save();
        }

        /// <summary>记录失败（候选隔离/保留 Confirmed——不部分发布）。</summary>
        public void RecordFailure(string reason)
        {
            Current.LastFailure = reason ?? "";
            Save();
            // 诊断（§8「可诊断」）：失败事实 + 事务 ID 进诊断面（stage/code 词表单源在诊断面：Txn/ActivationFailed）
            DiagSink?.Record("Txn", "ActivationFailed",
                ContentKey(Current.PendingReleaseId, Current.TransactionId), Current.LastFailure);
        }

        /// <summary>本次启动应使用的内容代次（ConfirmedReleaseId + 单调值——builtin 起点恒可用）。</summary>
        public ContentGeneration ActiveGeneration => new ContentGeneration(Current.ConfirmedReleaseId, Current.ConfirmedVersion);

        /// <summary>提交：递增序号 + 盖章完整性 + 写盘（§8 序号/完整性保护的单点落点）。</summary>
        private void Save()
        {
            Current.RecordSequence++;
            ActivationRecordIntegrity.Stamp(Current);
            _io.Save(Current);
        }
    }
}
