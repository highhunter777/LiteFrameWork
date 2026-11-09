using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 状态机共享内核：阶段注册、迁移请求、自动边、看门狗与诊断。
    /// </summary>
    public abstract class StageMachineCore<TId, TReq> : IStageHost<TId, TReq>
        where TId : struct
    {
        protected static readonly IEqualityComparer<TId> Cmp = EqualityComparer<TId>.Default;

        protected readonly string _name;
        protected readonly Dictionary<TId, IStage<TId, TReq>> _stages;
        protected TransitionTable<TId, TReq> _transitions;   // 自动迁移配置（null = 纯手动；InitializeCore 赋值）
        protected FsmAudit<TId> _audit;                      // 诊断共用件（§4：审计环/驻留/计数）
        private bool _initialized;

        protected bool _hasPending;
        protected TId _pendingId;
        protected TReq _pendingReq;             // payload：与 pending 一起交接（last-wins 时同步覆盖）
        protected TransitionKind _pendingKind;  // 挂起来源（Manual/Auto/Forced——审计用，§4）
        protected bool _inLeave;                // OnLeave 执行中（改道禁令窗口）
        protected bool _inStageCallback;        // 阶段钩子执行中（快照恢复重入守卫）
        protected float _stageTime;             // 秒（单机吃 deltaTime）
        protected int _stageFrames;             // 整数帧（帧窗口用；每次 Tick +1）
        protected long _transitionCount;

        /// <summary>构造共享件：只做阶段注册（重复 id/空实例 fail-fast）。
        /// 子类构造尾段必须调用 <see cref="InitializeCore"/>；层级机须在调用前完成建树（原序保持：
        /// 注册 → 建树 → 迁移表校验 → 审计 → <c>OnInit</c>）。</summary>
        protected StageMachineCore(string name, (TId id, IStage<TId, TReq> stage)[] stages)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (stages == null || stages.Length == 0) throw new ArgumentException("状态机至少需要一个阶段", nameof(stages));

            _name = name;
            _stages = new Dictionary<TId, IStage<TId, TReq>>(stages.Length);
            foreach (var (id, stage) in stages)
            {
                if (stage == null) throw new ArgumentException($"阶段 {id} 的实例为 null", nameof(stages));
                if (_stages.ContainsKey(id)) throw new ArgumentException($"重复阶段 id:{id}", nameof(stages));
                _stages.Add(id, stage);                          // 同一实例可挂多个 id（表驱动复用）
            }
        }

        /// <summary>初始化共享内核（子类构造尾段调用一次）：迁移表校验 → 审计 → 全部阶段 `OnInit`。</summary>
        protected void InitializeCore(TransitionTable<TId, TReq> transitions)
        {
            if (_initialized) throw new InvalidOperationException($"{_name}:InitializeCore 只能调用一次");
            _initialized = true;
            if (transitions != null)
                transitions.Validate(id => _stages.ContainsKey(id));
            _transitions = transitions;
            _audit = new FsmAudit<TId>(_stages.Keys);
            foreach (var s in _stages.Values) s.OnInit(this);     // 全部阶段先于 Start 初始化一遍
        }

        /// <summary>初始化守卫：子类构造漏调 <see cref="InitializeCore"/> 时显性失败（不静默退化为无迁移表）。</summary>
        protected void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException($"{_name}:机器未初始化：子类构造未调用 InitializeCore");
        }

        // ---- 机器表示法缝（子类必答：当前活动态是什么）----。

        /// <summary>是否已 Start（平面机：有当前态；层级机：活动路径非空）。</summary>
        public abstract bool Started { get; }

        /// <summary>当前活动态 id（平面机：当前态；层级机：**最深活动态**）——自动边/看门狗/重入判据都问它。</summary>
        public abstract TId Current { get; }

        /// <summary>当前活动态实例（看门狗读它的 <see cref="IStageTimeout{TId}"/> 可选接口）。</summary>
        protected abstract IStage<TId, TReq> ActiveStage { get; }

        /// <summary>应用挂起请求（帧末）：平面机 = 单级进出；层级机 = LCA 事务。</summary>
        public abstract void Advance();

        // ---- 共享读数（两机同口径）----。

        /// <summary>是否有挂起请求。</summary>
        public bool HasPending => _hasPending;

        /// <summary>挂起目标 id（无挂起时无意义）。</summary>
        public TId PendingId => _pendingId;

        /// <summary>当前阶段已持续时长（秒；`Advance` 后归零）。</summary>
        public float StageTime => _stageTime;

        /// <summary>当前阶段驻留的**整数帧数**（每次 `Tick` +1，迁移后归零）——帧窗口判据。</summary>
        public int StageFrames => _stageFrames;

        /// <summary>累计迁移/事务次数（诊断；层级机一次跨层事务计一次）。</summary>
        public long TransitionCount => _transitionCount;

        /// <summary>驻留看门狗触发次数（§3.2；持续增长说明存在常态性卡态，值得配置/流程复核）。</summary>
        public int TimeoutCount { get; protected set; }

        // ---- 请求管线（守卫 → 入队；两机同语义）----。

        /// <summary>发起迁移请求（只入队，last-wins）；通过守卫后返回 true。</summary>
        public bool Request(TId nextId, in TReq req) => RequestCore(nextId, in req);

        /// <summary>无 payload 的迁移请求。</summary>
        public bool Request(TId nextId) => RequestCore(nextId, default);

        /// <summary>强制迁移入队；应用时以 Forced 来源记录审计。</summary>
        public void ForceState(TId nextId, in TReq req)
        {
            ValidateRequest(nextId);
            EnqueuePending(nextId, in req, TransitionKind.Forced);
        }

        /// <summary>无 payload 的强制迁移。</summary>
        public void ForceState(TId nextId) => ForceState(nextId, default);

        private bool RequestCore(TId nextId, in TReq req, TransitionKind kind = TransitionKind.Manual)
        {
            ValidateRequest(nextId);
            EnqueuePending(nextId, in req, kind);
            return true;
        }

        /// <summary>
        /// 守卫（两机同语义）：未 Start / `OnLeave` 窗口 / 未注册 / 重入（= 当前活动态）→ 抛。
        /// 重入的提示语按机器形态（<see cref="ReentryError"/>——平面/层级对"怎么重启"的指引不同）。
        /// </summary>
        protected virtual void ValidateRequest(TId nextId)
        {
            if (!Started) throw new InvalidOperationException($"{_name}:Start 之前禁止 Request");
            if (_inLeave) throw new InvalidOperationException($"{_name}:OnLeave 期间禁止 Request（离场中改道自相矛盾）");
            GetStage(nextId);                                   // 未注册 → 当场抛（不等帧末）
            if (Cmp.Equals(nextId, Current))
                throw ReentryError(nextId);
        }

        /// <summary>重入提示语（扩展点）：平面机建议"先退出"，层级机建议"先退到父态"。</summary>
        protected virtual InvalidOperationException ReentryError(TId id)
            => new InvalidOperationException($"{_name}:重入禁止({id})——重启语义请拆阶段或先退出");

        /// <summary>挂起入队（last-wins：目标 + payload + 审计来源一起覆盖）。</summary>
        private void EnqueuePending(TId nextId, in TReq req, TransitionKind kind)
        {
            _pendingId = nextId;                                // last-wins
            _pendingReq = req;                                  // payload 同步 last-wins
            _pendingKind = kind;
            _hasPending = true;
        }

        /// <summary>查阶段实例（子类/表驱动件用；未注册抛）。</summary>
        protected IStage<TId, TReq> GetStage(TId id)
            => _stages.TryGetValue(id, out var s)
                ? s
                : throw new InvalidOperationException($"{_name}:阶段未注册 {id}");

        /// <summary>注册查询（Restore 校验快照 id 用，不抛）。</summary>
        protected bool IsRegistered(TId id) => _stages.ContainsKey(id);

        // ---- 驱动尾段与事件入口的公共部分 ----。

        /// <summary>无挂起时评估自动条件边（显式请求永不被覆盖）→ 驻留看门狗（同帧边命中则让位）→ 应用挂起。
        /// 两机 `Tick` 在各自的 OnUpdate 传播后调用（一帧最多一次迁移/事务）。</summary>
        protected void TickAutoEdgesAndApply()
        {
            if (!_hasPending)
            {
                if (_transitions != null && _transitions.TryTickEdge(Current, out var to, out var req))
                {
                    _audit.RecordAutoEdgeHit();
                    RequestCore(to, in req, TransitionKind.Auto);
                }
                else if (ActiveStage is IStageTimeout<TId> timeout
                    && timeout.TimeoutFrames > 0 && _stageFrames >= timeout.TimeoutFrames)
                {
                    ForceState(timeout.TimeoutTarget);          // 驻留异常升级，仍执行迁移守卫。
                    TimeoutCount++;
                }
            }
            if (_hasPending) Advance();
        }

        /// <summary>事件先过迁移边（命中即入队并视为已消费——《状态机专项设计》§3.1 集成序，两机一致）。
        /// 返回 false = 未命中，调用方继续问阶段 sink（平面机问当前态一次；层级机自最深活动态向根冒泡）。</summary>
        protected bool TryConsumeByTransitionEdge<TEvt>(in TEvt e)
        {
            if (_transitions == null || !_transitions.TryRaiseEdge(Current, e, out var to, out var req)) return false;
            _audit.RecordAutoEdgeHit();
            RequestCore(to, in req, TransitionKind.Auto);   // 与手动请求共用守卫和入队。
            return true;                                          // 消费：不冒泡、不问 sink
        }

        /// <summary>清挂起与运行计数（两机 `Reset` 共用；机器表示法字段——当前态/活动路径/历史——由各机自清）。</summary>
        protected void ClearPendingAndCounters()
        {
            _hasPending = false;
            _pendingReq = default;
            _stageTime = 0f;
            _stageFrames = 0;
            _transitionCount = 0;
            TimeoutCount = 0;
            _audit.Reset();
        }

        // ---- IModuleStats 公共段 ----。

        public string StatsName => _name;

        /// <summary>诊断计数段（两机 Snapshot 尾部共用）。</summary>
        protected void AppendSharedCounters(Dictionary<string, string> into)
        {
            into["自动边命中"] = _audit.AutoEdgeHits.ToString();
            into["看门狗触发"] = TimeoutCount.ToString();
        }

        /// <summary>审计读取面（§4）：最近 32 条迁移，旧 → 新序；低频轮询分配可接受（HUD/GM 面板消费）。</summary>
        public IReadOnlyList<TransitionAuditEntry<TId>> GetTransitionAudit()
        {
            EnsureInitialized();
            return _audit.GetAudit();
        }

        /// <summary>per-stage 统计读取面（§4）：id/进入次数/累计驻留秒。</summary>
        public IReadOnlyList<StageInfo<TId>> GetStageInfos()
        {
            EnsureInitialized();
            return _audit.GetStageInfos();
        }

        /// <summary>图导出（§4）：DOT 文本——节点 = 阶段、边段见 <see cref="ExportGraphEdges"/>
        /// （默认 = 迁移边〔事件边/条件边/Any〕∪ 时长→NextId 链；层级机补复合父子虚线）。</summary>
        public string ExportGraph()
        {
            EnsureInitialized();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"digraph \"{_name}\" {{");
            sb.AppendLine("  rankdir=LR;");
            foreach (var id in _stages.Keys)
                sb.AppendLine($"  \"{id}\";");
            ExportGraphEdges(sb);
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>DOT 边段（扩展点）：默认输出迁移边与时长链；层级机覆写补复合父子边（追加在默认段之后）。</summary>
        protected virtual void ExportGraphEdges(System.Text.StringBuilder sb)
        {
            if (_transitions != null)
                foreach (var e in _transitions.Edges)
                {
                    var label = e.EventType != null ? $"evt:{e.EventType.Name}" : "cond";
                    var from = e.IsAny ? "*" : e.From.ToString();
                    sb.AppendLine($"  \"{from}\" -> \"{e.To}\" [label=\"{label}\"];");
                }
            foreach (var kv in _stages)
                if (kv.Value is TableStage<TId, TReq> ts && ts.Spec.HasNext)
                    sb.AppendLine($"  \"{kv.Key}\" -> \"{ts.Spec.NextId}\" [label=\"dur{ts.Spec.DurationFrames}f\", style=dashed];");
        }
    }
}
