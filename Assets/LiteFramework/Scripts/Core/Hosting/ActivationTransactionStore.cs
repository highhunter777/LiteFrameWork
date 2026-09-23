using System;

namespace LiteFramework
{
    /// <summary>
    /// 激活事务状态（《热更与内容发布专项设计》§8——磁盘记录至少区分的三态）：
    /// 文件提交、运行时激活与健康确认分开记录；进程中断后从事务记录恢复。
    /// </summary>
    public enum ActivationState
    {
        /// <summary>候选内容已提交到磁盘（下载/校验完成），尚未进入激活窗口。</summary>
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
        public string ConfirmedReleaseId = "builtin";

        /// <summary>已确认发布的代次值（单调）。</summary>
        public ulong ConfirmedVersion;

        /// <summary>在途发布身份（null = 无在途事务）。</summary>
        public string PendingReleaseId;

        /// <summary>在途状态（PendingReleaseId 非 null 时有效）。</summary>
        public ActivationState PendingState;

        /// <summary>累计恢复尝试次数（有上限——禁止无限重启，热更 §8）。</summary>
        public int RecoveryAttempts;

        /// <summary>最近一次失败原因（诊断；不上报敏感数据）。</summary>
        public string LastFailure;
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
    /// 激活事务存储（《热更与内容发布专项设计》§8 激活、健康确认与中断恢复的 C1-⑩ 最小落点）：
    ///
    /// - **启动恢复决策**：<see cref="RecoverOnStartup"/>——读记录；有在途（Candidate/PendingActivation）
    ///   即回退 Confirmed（C1 无远程重取通道——受控恢复 = 回退到永远合法的已确认版本；重取候选随热更批），
    ///   尝试计数 +1 并持久化；超上限仍以 Confirmed 继续（确定态，记录失败原因，不无限重启）。
    /// - **确认提交**：<see cref="Confirm"/>——健康检查通过后写盘（Confirmed 单调推进，Pending 清空）。
    /// - **候选生命周期**：<see cref="BeginCandidate"/> → <see cref="MarkPendingActivation"/> → Confirm/RecordFailure。
    ///   磁盘上"确认写盘失败"（§8 表：不声称已确认）由 IO 实现方抛出——本类不吞。
    /// - Host 下载/验签/真实远程重取归热更批；本类只冻结事务状态机与恢复决策。
    /// </summary>
    public sealed class ActivationTransactionStore
    {
        /// <summary>恢复尝试上限（超过后不再尝试恢复，直接以 Confirmed 继续——热更 §8 次数有上限）。</summary>
        public const int MaxRecoveryAttempts = 3;

        private readonly IActivationRecordIO _io;

        /// <summary>当前记录（构造时读入；启动恢复后为确定态）。</summary>
        public ActivationRecord Current { get; private set; }

        public ActivationTransactionStore(IActivationRecordIO io)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
            Current = _io.TryLoad() ?? new ActivationRecord();   // 无记录/不可信 = 全新安装（builtin 起点）
        }

        /// <summary>
        /// 启动恢复决策：有在途事务 → 回退 Confirmed（尝试计数 +1 持久化；超限仍以 Confirmed 继续）。
        /// 返回恢复后的记录——调用方（Patch 流程）据此确定本次启动使用的内容身份
        /// （ConfirmedReleaseId → ContentGeneration），不使用半成品。
        /// </summary>
        public ActivationRecord RecoverOnStartup()
        {
            if (Current.PendingReleaseId == null)
                return Current;                                  // 干净态：无在途事务

            Current.RecoveryAttempts++;
            if (Current.RecoveryAttempts > MaxRecoveryAttempts)
            {
                // 超限：放弃恢复尝试，以 Confirmed 继续（确定态）；失败原因留档供诊断
                Current.LastFailure = $"恢复尝试超上限（{Current.RecoveryAttempts - 1}），放弃在途 {Current.PendingReleaseId}——以 Confirmed {Current.ConfirmedReleaseId} 继续";
                Current.PendingReleaseId = null;
                Current.PendingState = default;
                _io.Save(Current);
                return Current;
            }

            // 受控恢复（C1 形态 = 回退已确认版本；远程重取随热更批）
            Current.PendingReleaseId = null;
            Current.PendingState = default;
            _io.Save(Current);
            return Current;
        }

        /// <summary>开始一次候选事务（磁盘记录 Candidate——下载/校验完成后进入，激活窗口前）。</summary>
        public void BeginCandidate(string releaseId)
        {
            if (string.IsNullOrEmpty(releaseId)) throw new ArgumentNullException(nameof(releaseId));
            Current.PendingReleaseId = releaseId;
            Current.PendingState = ActivationState.Candidate;
            _io.Save(Current);
        }

        /// <summary>进入激活窗口前持久化 PendingActivation（进程中断后可恢复，§8 表行 2）。</summary>
        public void MarkPendingActivation()
        {
            if (Current.PendingReleaseId == null)
                throw new InvalidOperationException("无在途候选——MarkPendingActivation 必须在 BeginCandidate 之后");
            Current.PendingState = ActivationState.PendingActivation;
            _io.Save(Current);
        }

        /// <summary>健康检查通过，确认提交（Confirmed 单调推进；Pending 清空）。</summary>
        public void Confirm(string releaseId, ulong version)
        {
            if (string.IsNullOrEmpty(releaseId)) throw new ArgumentNullException(nameof(releaseId));
            Current.ConfirmedReleaseId = releaseId;
            Current.ConfirmedVersion = version;
            Current.PendingReleaseId = null;
            Current.PendingState = default;
            Current.LastFailure = null;
            _io.Save(Current);
        }

        /// <summary>记录失败（候选隔离/保留 Confirmed——不部分发布）。</summary>
        public void RecordFailure(string reason)
        {
            Current.LastFailure = reason ?? "";
            _io.Save(Current);
        }

        /// <summary>本次启动应使用的内容代次（ConfirmedReleaseId + 单调值——builtin 起点恒可用）。</summary>
        public ContentGeneration ActiveGeneration => new ContentGeneration(Current.ConfirmedReleaseId, Current.ConfirmedVersion);
    }
}
