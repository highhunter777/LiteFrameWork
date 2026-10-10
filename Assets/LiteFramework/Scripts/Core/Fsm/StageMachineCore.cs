using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 状态机家族**共享内核**（《状态机专项设计》§3 单源纪律）：平面机（<see cref="StageMachine{TId,TReq}"/>）
    /// 与层级机（<see cref="HierarchicalStageMachine{TId,TReq}"/>）的**迁移语义**收口在此——
    /// 请求管线（守卫 → 准入 → 入队）、挂起语义（last-wins + 审计来源）、自动边/看门狗评估序、
    /// 恢复通道、拒绝上报与诊断读取面（审计环 / per-stage 统计 / DOT 图导出公共段 / Snapshot 计数段）。
    /// 两机只保留**表示法**差异：平面机 = 单活动态 + 单级进出 + 单 sink；层级机 = 活动路径树 +
    /// LCA 事务 + 事件冒泡。迁移语义改动只动本文件——此前两内核逐行互抄，是全项目语义漂移风险最高的复制面。
    ///
    /// **机器表示法缝**（子类必须实现）：<see cref="Started"/> / <see cref="Current"/> /
    /// <see cref="ActiveStage"/>（准入、自动边、看门狗、重入判据都问当前活动态）与 <see cref="Advance"/>
    /// （帧末应用：平面 = 单级进出，层级 = LCA 事务）。
    /// **扩展点缝**（子类可覆写）：<see cref="CanAccept"/>（准入判定）、<see cref="OnStagePreempted"/>
    /// （离场前通知）、<see cref="ReentryError"/>（重入提示语）、<see cref="ExportGraphEdges"/>
    /// （DOT 边段——层级机补复合父子）。
    ///
    /// **异常策略不在内核**：阶段回调不捕获异常，由驱动层兜。
    /// 内核实现 <see cref="IStageHost{TId,TReq}"/> 的共享半边（请求/驻留读数）；表示法半边
    /// （<see cref="Started"/>/<see cref="Current"/>）由机器实现——契约对阶段恒完整。
    /// </summary>
    public abstract class StageMachineCore<TId, TReq> : IStageHost<TId, TReq>
        where TId : struct
    {
        protected static readonly IEqualityComparer<TId> Cmp = EqualityComparer<TId>.Default;

        protected readonly string _name;
        protected readonly Dictionary<TId, IStage<TId, TReq>> _stages;
        protected readonly TransitionTable<TId, TReq> _transitions;   // 自动迁移配置（null = 纯手动）
        protected readonly FsmAudit<TId> _audit;                      // 诊断共用件（§4：审计环/驻留/计数）

        // ---- 层级树（树语义状态；平面机恒空集合——构造不置 buildTree）----
        protected readonly Dictionary<TId, CompositeSpec<TId>> _composite;
        protected readonly Dictionary<TId, TId> _parent;              // 非根 → 父
        protected readonly Dictionary<TId, List<TId>> _history;       // 复合态 → 上次退出时的子路径
        protected readonly List<TId> _roots = new List<TId>(2);      // 根集合（无父者；层级机允许多根）

        protected bool _hasPending;
        protected TId _pendingId;
        protected TReq _pendingReq;             // payload：与 pending 一起交接（last-wins 时同步覆盖）
        protected TransitionKind _pendingKind;  // 挂起来源（Manual/Auto/Resume/Forced——审计用，§4）
        protected bool _inLeave;                // OnLeave 执行中（改道禁令窗口）
        protected bool _inStageCallback;        // 阶段钩子执行中（此时发起的迁移 = "自身推进"）
        protected float _stageTime;             // 秒（单机吃 deltaTime）
        protected int _stageFrames;             // 整数帧（帧窗口用；每次 Tick +1）
        protected long _transitionCount;

        /// <summary>构造共享件：注册 → 树构建（仅层级机）→ 迁移表校验 → 审计 → OnInit（两机同序）。
        /// 平面机不置 <paramref name="buildTree"/>（树集合恒空）；层级机必置并传 <paramref name="composites"/>
        /// （null = 全根形态——多根流程线，根推导与环检测仍执行）。</summary>
        protected StageMachineCore(string name, TransitionTable<TId, TReq> transitions,
            (TId id, IStage<TId, TReq> stage)[] stages, CompositeSpec<TId>[] composites, bool buildTree)
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

            _composite = new Dictionary<TId, CompositeSpec<TId>>();
            _parent = new Dictionary<TId, TId>();
            _history = new Dictionary<TId, List<TId>>();
            if (buildTree) BuildTree(composites);

            if (transitions != null)
                transitions.Validate(id => _stages.ContainsKey(id));
            _transitions = transitions;
            _audit = new FsmAudit<TId>(_stages.Keys);
            foreach (var s in _stages.Values) s.OnInit(this);     // 全部阶段先于 Start 初始化一遍
        }

        // ---- 机器表示法缝（子类必答：当前活动态是什么）----

        /// <summary>是否已 Start（平面机：有当前态；层级机：活动路径非空）。</summary>
        public abstract bool Started { get; }

        /// <summary>当前活动态 id（平面机：当前态；层级机：**最深活动态**）——准入/自动边/看门狗/重入判据都问它。</summary>
        public abstract TId Current { get; }

        /// <summary>当前活动态实例（看门狗读它的 <see cref="IStageTimeout{TId}"/> 可选接口）。</summary>
        protected abstract IStage<TId, TReq> ActiveStage { get; }

        /// <summary>应用挂起请求（帧末）：平面机 = 单级进出；层级机 = LCA 事务。</summary>
        public abstract void Advance();

        // ---- 共享读数（两机同口径）----

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

        /// <summary>
        /// 最近一次请求被拒的原因（每次 `Request`/`TryResume` 刷新；被接受则回 <see cref="RejectReason.None"/>）。
        /// 基础机无准入恒接受，故恒为 None；抢占型子类会写 <see cref="RejectReason.Priority"/> /
        /// <see cref="RejectReason.InterruptDisallowed"/> 等。
        /// </summary>
        public RejectReason LastReject { get; private set; }

        /// <summary>阶段钩子是否正在执行（子类判定"自身推进"用）。</summary>
        protected bool InStageCallback => _inStageCallback;

        // ---- 请求管线（守卫 → 准入 → 入队；两机同语义）----

        /// <summary>发起迁移请求（只入队，last-wins）。基础机**恒接受**（返回 true）；抢占型子类可能返回 false。</summary>
        public bool Request(TId nextId, in TReq req) => RequestCore(nextId, in req, int.MinValue);

        /// <summary>无 payload 的迁移请求。</summary>
        public bool Request(TId nextId) => RequestCore(nextId, default, int.MinValue);

        /// <summary>
        /// 强制迁移（《状态机专项设计》§3.2）：**绕过准入判定**（优先级/中断规则）入队。
        /// 编程错误检查照常（强制不豁免错误）：重入/未注册/Start 前/OnLeave 窗口仍抛。
        /// 基础机上与 <see cref="Request(TId)"/> 等价（恒接受）；差异在抢占型子类（绕准入）。
        /// 消费端：GM 跳态、驻留看门狗升级。
        /// </summary>
        public void ForceState(TId nextId, in TReq req)
        {
            ValidateRequest(nextId);
            LastReject = RejectReason.None;
            EnqueuePending(nextId, in req, TransitionKind.Forced);
        }

        /// <summary>无 payload 的强制迁移。</summary>
        public void ForceState(TId nextId) => ForceState(nextId, default);

        /// <summary>
        /// 请求内核（守卫 → 准入判定 → 入队）。子类可暴露"带优先级"的公开重载转调它；
        /// <paramref name="priority"/> 对基础机无意义（<see cref="CanAccept"/> 默认忽略它）；
        /// <paramref name="kind"/> 为审计来源（§4），公开 `Request` = Manual、自动边转调 = Auto。
        /// </summary>
        protected bool RequestCore(TId nextId, in TReq req, int priority, TransitionKind kind = TransitionKind.Manual)
        {
            ValidateRequest(nextId);
            if (!CanAccept(nextId, priority)) return false;       // 被拒 = 正常路径（子类在 CanAccept 里 MarkRejected）
            LastReject = RejectReason.None;                       // 被接受 → 清上次原因（LastReject 恒反映"最近一次"）
            EnqueuePending(nextId, in req, kind);
            return true;
        }

        /// <summary>准入判定（扩展点）：基础机恒接受；抢占型子类在此实现"优先级 + 中断规则"
        /// （裁决读当前活动态实例——经 <see cref="ActiveStage"/> 取）。</summary>
        protected virtual bool CanAccept(TId incomingId, int priority) => true;

        /// <summary>旧阶段被替换前的通知（扩展点）：传出被迁走的活动态 id（平面机 = 当前态，
        /// 层级机 = 最深活动态）；抢占型子类在此做"入恢复栈"。</summary>
        protected virtual void OnStagePreempted(TId outgoingId) { }

        /// <summary>子类在被拒时上报原因（供 <see cref="LastReject"/> 读数与 per-Reason 计数）。</summary>
        protected void MarkRejected(RejectReason reason)
        {
            LastReject = reason;
            _audit.RecordReject(reason);
        }

        /// <summary>子类在 <c>TryResume</c> 成功时转调（§4 Resume 成功计数）。</summary>
        protected void RecordResumeSuccess() => _audit.RecordResumeSuccess();

        /// <summary>
        /// 绕过准入判定的入队口（**恢复类迁移**用：`TryResume` 是"回到更早的状态"，不该被抢占规则再挡一次）。
        /// 守卫（未 Start / OnLeave / 未注册 / 重入）照常生效。审计 Kind = Resume。
        /// </summary>
        protected bool EnqueueRequest(TId nextId, in TReq req)
        {
            ValidateRequest(nextId);
            EnqueuePending(nextId, in req, TransitionKind.Resume);
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

        // ---- 驱动尾段与事件入口的公共部分 ----

        /// <summary>无挂起时评估自动条件边（显式请求永不被覆盖）→ 驻留看门狗（同帧边命中则让位）→ 应用挂起。
        /// 两机 `Tick` 在各自的 OnUpdate 传播后调用（一帧最多一次迁移/事务）。</summary>
        protected void TickAutoEdgesAndApply()
        {
            if (!_hasPending)
            {
                if (_transitions != null && _transitions.TryTickEdge(Current, out var to, out var req))
                {
                    _audit.RecordAutoEdgeHit();
                    RequestCore(to, in req, int.MinValue, TransitionKind.Auto);
                }
                else if (ActiveStage is IStageTimeout<TId> timeout
                    && timeout.TimeoutFrames > 0 && _stageFrames >= timeout.TimeoutFrames)
                {
                    ForceState(timeout.TimeoutTarget);          // §3.2：驻留异常升级（绕准入不绕编程错误检查）
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
            RequestCore(to, in req, int.MinValue, TransitionKind.Auto);   // 手动请求同通道；被准入拒绝也是"机器做的裁决"
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
            LastReject = RejectReason.None;
            _audit.Reset();
        }

        // ---- IModuleStats 公共段 ----

        public string StatsName => _name;

        /// <summary>诊断计数段（两机 `Snapshot` 尾部共用——键序即输出序，HUD 依赖保持不变）。</summary>
        protected void AppendSharedCounters(Dictionary<string, string> into)
        {
            into["自动边命中"] = _audit.AutoEdgeHits.ToString();
            into["看门狗触发"] = TimeoutCount.ToString();
            into["拒绝-优先级"] = _audit.Rejects[(int)RejectReason.Priority].ToString();
            into["拒绝-中断规则"] = _audit.Rejects[(int)RejectReason.InterruptDisallowed].ToString();
            into["恢复成功"] = _audit.ResumeSuccess.ToString();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>审计读取面（§4）：最近 32 条迁移，旧 → 新序；低频轮询分配可接受。</summary>
        public IReadOnlyList<TransitionAuditEntry<TId>> GetTransitionAudit() => _audit.GetAudit();

        /// <summary>per-stage 统计读取面（§4）：id/进入次数/累计驻留秒。</summary>
        public IReadOnlyList<StageInfo<TId>> GetStageInfos() => _audit.GetStageInfos();

        /// <summary>图导出（§4）：DOT 文本——节点 = 阶段、边段见 <see cref="ExportGraphEdges"/>
        /// （默认 = 迁移边〔事件边/条件边/Any〕∪ 时长→NextId 链；层级机补复合父子虚线）。</summary>
        public string ExportGraph()
        {
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
#endif

        // ---- 层级树构建（仅层级机；平面机树集合恒空）----

        /// <summary>复合树装配与校验（fail-fast）：重复复合态/子态未注册/多父/无根/成环当场抛。
        /// composites = null 时为**全根形态**（多根流程线）——根推导与环检测仍执行。</summary>
        private void BuildTree(CompositeSpec<TId>[] composites)
        {
            if (composites != null)
            {
                foreach (var spec in composites)
                {
                    if (spec == null) throw new ArgumentException("composites 含 null 元素", nameof(composites));
                    if (!_stages.ContainsKey(spec.Id))
                        throw new ArgumentException($"复合态 {spec.Id} 未在 stages 里注册阶段实例", nameof(composites));
                    if (_composite.ContainsKey(spec.Id))
                        throw new ArgumentException($"重复复合态声明:{spec.Id}", nameof(composites));
                    _composite.Add(spec.Id, spec);

                    foreach (var child in spec.Children)
                    {
                        if (!_stages.ContainsKey(child))
                            throw new ArgumentException($"复合态 {spec.Id} 的子态 {child} 未在 stages 里注册", nameof(composites));
                        if (_parent.TryGetValue(child, out var exist))
                            throw new ArgumentException(
                                $"阶段 {child} 有多个父（{exist} / {spec.Id}）——HSM 要求是一棵树", nameof(composites));
                        _parent.Add(child, spec.Id);
                    }
                }
            }

            // 允许多根（如流程线的 Launch/Preload/Main/Error 是平级根）：跨根迁移 = 全退全进（LCA=0）。
            // 只要求：每个非根至多一个父、且父子关系无环（下面逐点上行探测）。
            var roots = new List<TId>();
            foreach (var id in _stages.Keys)
                if (!_parent.ContainsKey(id)) roots.Add(id);
            if (roots.Count == 0)
                throw new ArgumentException("HSM 无根（父子关系成环）", nameof(composites));
            _roots.AddRange(roots);

            foreach (var id in _stages.Keys)
            {
                var cur = id;
                int hops = 0;
                while (_parent.TryGetValue(cur, out var p))
                {
                    cur = p;
                    if (++hops > _stages.Count) throw new ArgumentException($"父子关系存在环（经过 {id}）", nameof(composites));
                }
            }
        }
    }
}
