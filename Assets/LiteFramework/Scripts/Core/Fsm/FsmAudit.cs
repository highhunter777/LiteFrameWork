using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>迁移来源（《状态机专项设计》§4 审计 Kind）：手动请求 / 自动边（事件边∪条件边）/ 恢复 / 强制（ForceState∪看门狗）/ 快照恢复。</summary>
    public enum TransitionKind : byte
    {
        Manual = 0,
        Auto = 1,
        Resume = 2,
        Forced = 3,
        Restore = 4,
    }

    /// <summary>审计环条目（§4）：序号单调递增（区分"未记录"与"第 0 条"）；From == To 不存在（重入禁令）。</summary>
    public struct TransitionAuditEntry<TId> where TId : struct
    {
        public long Seq;
        public TId From;
        public TId To;
        public TransitionKind Kind;
    }

    /// <summary>per-stage 驻留统计（§4，读取面形态镜像 <c>EventChannelInfo</c>）：进入次数与累计驻留秒（含被快照回滚的真实发生时长——诊断面不参与结构态）。</summary>
    public struct StageInfo<TId> where TId : struct
    {
        public TId Id;
        public long Enters;
        public double TotalSeconds;
    }

    /// <summary>
    /// 状态机诊断共用件（《状态机专项设计》§4）：审计环（最近 32 条迁移，预分配零分配）+
    /// per-stage 进入/驻留累计 + 自动边命中/拒绝/恢复成功计数。平面机与层级机各持一份实例；
    /// 记录点全部在热路径上的低频事件（迁移/进入/拒绝——一帧至多一次），读取面（GetAudit/GetStageInfos）
    /// 分配只发生在调用时。诊断统计**不进快照**（§3.4 结构态不含；Restore 不动诊断历史）。
    /// </summary>
    internal sealed class FsmAudit<TId> where TId : struct
    {
        /// <summary>审计环容量（对齐命令中心审计环）。</summary>
        public const int Capacity = 32;

        private struct StageBox
        {
            public long Enters;
            public double TotalSeconds;
        }

        private readonly TransitionAuditEntry<TId>[] _ring = new TransitionAuditEntry<TId>[Capacity];
        private int _start;
        private int _count;
        private long _seq;
        private readonly Dictionary<TId, StageBox> _stages = new Dictionary<TId, StageBox>();

        /// <summary>自动边（事件边∪条件边）命中累计。</summary>
        public long AutoEdgeHits { get; private set; }

        /// <summary>per-<see cref="RejectReason"/> 累计（索引 = 枚举值；None 桶恒 0——被接受不计）。</summary>
        public long[] Rejects { get; } = new long[5];

        /// <summary><c>TryResume</c> 成功恢复累计。</summary>
        public long ResumeSuccess { get; private set; }

        public FsmAudit(IEnumerable<TId> stageIds)
        {
            foreach (var id in stageIds) _stages[id] = default;
        }

        public void RecordEnter(TId id)
        {
            var box = _stages[id];
            box.Enters++;
            _stages[id] = box;
        }

        /// <summary>迁移离开时累计驻留（把当前段 <paramref name="seconds"/> 计入旧态）。</summary>
        public void RecordLeave(TId id, double seconds)
        {
            var box = _stages[id];
            box.TotalSeconds += seconds;
            _stages[id] = box;
        }

        public void RecordTransition(TId from, TId to, TransitionKind kind)
        {
            if (_count == Capacity)
            {
                _ring[_start] = default;                       // 放开滚出条目引用（TId 值类型，防御性清理）
                _start = (_start + 1) % Capacity;
                _count--;
            }
            int tail = (_start + _count) % Capacity;
            _ring[tail] = new TransitionAuditEntry<TId> { Seq = ++_seq, From = from, To = to, Kind = kind };
            _count++;
        }

        public void RecordAutoEdgeHit() => AutoEdgeHits++;

        public void RecordReject(RejectReason reason)
        {
            if (reason != RejectReason.None) Rejects[(int)reason]++;
        }

        public void RecordResumeSuccess() => ResumeSuccess++;

        /// <summary>最近审计（旧 → 新；读取面分配）。</summary>
        public List<TransitionAuditEntry<TId>> GetAudit()
        {
            var list = new List<TransitionAuditEntry<TId>>(_count);
            for (int i = 0; i < _count; i++) list.Add(_ring[(_start + i) % Capacity]);
            return list;
        }

        /// <summary>per-stage 统计（读取面分配；字符串化由调用方做——不进热路径）。</summary>
        public List<StageInfo<TId>> GetStageInfos()
        {
            var list = new List<StageInfo<TId>>(_stages.Count);
            foreach (var kv in _stages)
                list.Add(new StageInfo<TId> { Id = kv.Key, Enters = kv.Value.Enters, TotalSeconds = kv.Value.TotalSeconds });
            return list;
        }

        /// <summary>清全部诊断态（机器 Reset 时转调；Restore 不调——诊断历史独立于结构态）。</summary>
        public void Reset()
        {
            _start = 0;
            _count = 0;
            _seq = 0;
            AutoEdgeHits = 0;
            ResumeSuccess = 0;
            for (int i = 0; i < Rejects.Length; i++) Rejects[i] = 0;
            foreach (var id in new List<TId>(_stages.Keys)) _stages[id] = default;   // 读取面频度，分配可接受
        }
    }
}
