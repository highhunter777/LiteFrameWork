using System;
using System.Collections.Generic;
using System.Text;

namespace LiteFramework
{
    /// <summary>
    /// 层级状态机（HSM）：`StageMachine` 的超集——状态空间是一棵**树**（复合态可含子层），
    /// 共享同一份 <see cref="IStage{TId,TReq}"/> 契约与"帧末应用 / last-wins / 离场禁改道"等语义。
    /// flat 机 = 无复合态时的退化形态。需要优先级抢占/恢复时用子类
    /// <see cref="PreemptiveHierarchicalStageMachine{TId,TReq}"/>（《状态机专项设计》§3.3，纯加法）。
    ///
    /// 与平面机的差异：
    /// ① **一次迁移 = 一次事务**：`Request` 只入队，`Advance` 应用——算出目标路径与当前活动路径的**共同祖先（LCA）**，
    ///    LCA 之下先按 **深→浅** 退出、再按 **浅→深** 进入，同帧原子完成（平面机的"一帧最多一变"在此改写为"一帧最多一次事务"）；
    /// ② **OnUpdate 传播：根→叶**；
    /// ③ **降层不重跑祖先**：目标已是活动路径上的祖先时只退出（不重跑它的 `OnEnter`）；
    /// ④ **显式目标优先、未指定层级按历史/初始展开**（`CompositeSpec.History`）——"回到父态"即"回到它上次的子页"；
    /// ⑤ **事件冒泡**：`Raise&lt;TEvt&gt;` 从最深活动态向根问 <see cref="IEventSink{TEvt}"/>，首个消费即停；
    /// ⑥ **异常不捕获**：事务中途抛 → 立即中止，活动路径保留**已完成部分**（不回滚），置 `Interrupted` 供诊断。
    ///
    /// 宿主与快照：`ITickable` + `IModuleStats`。
    /// 给子类的扩展点：<see cref="CanAcceptRequest"/>（准入判定）、<see cref="OnDeepestPreempted"/>（被抢占通知）、
    /// <see cref="EnqueueTransition"/>（绕准入入队口）、<see cref="MarkRejected"/>、<see cref="DeepestStage"/>、<see cref="InStageCallback"/>。
    /// </summary>
    public class HierarchicalStageMachine<TId, TReq> : ITickable, IModuleStats, IStageHost<TId, TReq>
        where TId : struct
    {
        private static readonly IEqualityComparer<TId> Cmp = EqualityComparer<TId>.Default;

        private readonly string _name;
        private readonly Dictionary<TId, IStage<TId, TReq>> _stages;
        private readonly Dictionary<TId, CompositeSpec<TId>> _composite;
        private readonly Dictionary<TId, TId> _parent;              // 非根 → 父
        private readonly Dictionary<TId, List<TId>> _history;       // 复合态 → 上次退出时的子路径
        private readonly List<TId> _active = new List<TId>(4);      // 根 → 最深活动态
        private readonly List<TId> _roots = new List<TId>(2);       // 根集合（无父者；允许多根）
        private readonly TransitionTable<TId, TReq> _transitions;   // 自动迁移配置（两机同语义）
        private readonly FsmAudit<TId> _audit;                      // 诊断共用件（§4：审计环/驻留/计数）

        private bool _hasPending;
        private TId _pendingId;
        private TReq _pendingReq;
        private TransitionKind _pendingKind;  // 挂起来源（审计用，§4）
        private bool _inLeave;
        private bool _inStageCallback;      // 阶段钩子执行中（此时发起的迁移 = "自身推进"，抢占子类应放行）
        private float _stageTime;
        private int _stageFrames;
        private long _transitionCount;

        public HierarchicalStageMachine(string name,
            (TId id, IStage<TId, TReq> stage)[] stages,
            CompositeSpec<TId>[] composites,
            TransitionTable<TId, TReq> transitions = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (stages == null || stages.Length == 0) throw new ArgumentException("状态机至少需要一个阶段", nameof(stages));

            _name = name;
            _stages = new Dictionary<TId, IStage<TId, TReq>>(stages.Length);
            foreach (var (id, stage) in stages)
            {
                if (stage == null) throw new ArgumentException($"阶段 {id} 的实例为 null", nameof(stages));
                if (_stages.ContainsKey(id)) throw new ArgumentException($"重复阶段 id:{id}", nameof(stages));
                _stages.Add(id, stage);
            }

            _composite = new Dictionary<TId, CompositeSpec<TId>>();
            _parent = new Dictionary<TId, TId>();
            _history = new Dictionary<TId, List<TId>>();

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

            if (transitions != null)
                transitions.Validate(id => _stages.ContainsKey(id));
            _transitions = transitions;
            _audit = new FsmAudit<TId>(_stages.Keys);
            foreach (var s in _stages.Values) s.OnInit(this);       // 全部阶段先于 Start 初始化一遍
        }

        /// <summary>根集合（无父的阶段；HSM 允许多根，跨根迁移即全退全进）。</summary>
        public IReadOnlyList<TId> Roots => _roots;

        public bool Started => _active.Count > 0;

        /// <summary>最深活动态。</summary>
        public TId Current => _active[_active.Count - 1];

        /// <summary>活动路径（根 → 最深活动态）；迁移事务进行中会看到中间态。</summary>
        public IReadOnlyList<TId> ActivePath => _active;

        public bool HasPending => _hasPending;
        public TId PendingId => _pendingId;
        public float StageTime => _stageTime;

        /// <summary>最深活动态驻留的整数帧数（每次 `Tick` +1，事务后归零）。</summary>
        public int StageFrames => _stageFrames;

        /// <summary>迁移**事务**计数（一次跨层请求算一次，不是层数）。</summary>
        public long TransitionCount => _transitionCount;

        /// <summary>驻留看门狗触发次数（§3.2；层级机按最深活动态的声明裁决）。</summary>
        public int TimeoutCount { get; private set; }

        /// <summary>上一次事务是否中途抛异常而中断（诊断；成功事务会清零）。</summary>
        public bool Interrupted { get; private set; }

        /// <summary>中断时的目标 id（诊断）。</summary>
        public TId InterruptedTarget { get; private set; }

        /// <summary>
        /// 最近一次请求被拒的原因（每次 `Request`/`TryResume` 刷新；被接受则回 <see cref="RejectReason.None"/>）。
        /// 基类无准入恒接受，故恒为 None；抢占型子类会写 <see cref="RejectReason.Priority"/> /
        /// <see cref="RejectReason.InterruptDisallowed"/> 等。
        /// </summary>
        public RejectReason LastReject { get; private set; }

        /// <summary>阶段钩子是否正在执行（子类判定"自身推进"用）。</summary>
        protected bool InStageCallback => _inStageCallback;

        /// <summary>最深活动态的阶段实例（子类查询中断规则/恢复意愿/优先级用）。</summary>
        protected IStage<TId, TReq> DeepestStage => _active.Count > 0 ? _stages[_active[_active.Count - 1]] : null;

        /// <summary>启动：从给定根一路展开到叶（无历史 → 全用 `InitialChild`）。只能一次。</summary>
        public void Start(TId root)
        {
            if (_active.Count > 0) throw new InvalidOperationException($"{_name}:Start 只能调用一次");
            if (_parent.ContainsKey(root))
                throw new InvalidOperationException($"{_name}:{root} 不是根（它有父态）");
            _transitions?.Lock();                               // 封板——两机同口径
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            StageGraphDiagnostics<TId, TReq>.Audit(_name, _stages, _composite, _parent, _transitions, root);   // §3.5 深校验
#endif

            _active.Add(root);
            ExpandDown(_active);
            _inStageCallback = true;                            // 初始进入也属阶段钩子（自身推进窗口）
            try
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    _stages[_active[i]].OnEnter(this, default);
                    _audit.RecordEnter(_active[i]);
                }
            }
            finally { _inStageCallback = false; }
            _stageTime = 0f;
            _stageFrames = 0;
        }

        /// <summary>
        /// 发起迁移请求（只入队，last-wins）。目标可以是任意层级的阶段 id——**路径由树反查**，调用方不给路径。
        /// 未 Start / 未注册 / 重入（= 当前最深）/ `OnLeave` 窗口内 → 当场抛。
        /// 基类不做准入（恒接受）；抢占型子类覆写 <see cref="CanAcceptRequest"/> 后可能返回 false。
        /// </summary>
        public bool Request(TId target, in TReq req) => RequestCore(target, in req, int.MinValue);

        public bool Request(TId target) => RequestCore(target, default, int.MinValue);

        /// <summary>请求内核（校验 → 准入 → 入队）；<paramref name="priority"/> 供抢占子类的带优先级覆盖重载使用；
        /// <paramref name="kind"/> 为审计来源（§4），公开 `Request` = Manual、自动边转调 = Auto。</summary>
        protected bool RequestCore(TId target, in TReq req, int priority, TransitionKind kind = TransitionKind.Manual)
        {
            ValidateRequest(target);
            if (!CanAcceptRequest(target, priority)) return false;    // 被拒 = 正常路径（子类在 CanAcceptRequest 里 MarkRejected）
            LastReject = RejectReason.None;
            EnqueueTransition(target, in req, kind);
            return true;
        }

        /// <summary>
        /// 强制迁移（《状态机专项设计》§3.2）：**绕过准入判定**入队；编程错误检查照常
        /// （重入/未注册/Start 前/OnLeave 窗口仍抛）。消费端：GM 跳态、驻留看门狗升级。
        /// </summary>
        public void ForceState(TId target, in TReq req)
        {
            ValidateRequest(target);
            LastReject = RejectReason.None;
            EnqueueTransition(target, in req, TransitionKind.Forced);
        }

        /// <summary>无 payload 的强制迁移。</summary>
        public void ForceState(TId target) => ForceState(target, default);

        /// <summary>准入判定（扩展点）：基类恒接受；抢占型子类在此实现"最深活动态 vs 来者"的优先级/中断规则裁决。</summary>
        protected virtual bool CanAcceptRequest(TId target, int priority) => true;

        /// <summary>子类在被拒时上报原因（供 <see cref="LastReject"/> 读数与 per-Reason 计数）。</summary>
        protected void MarkRejected(RejectReason reason)
        {
            LastReject = reason;
            _audit.RecordReject(reason);
        }

        /// <summary>子类在 <c>TryResume</c> 成功时转调（§4 Resume 成功计数）。</summary>
        protected void RecordResumeSuccess() => _audit.RecordResumeSuccess();

        /// <summary>事务退出段开始前的通知（扩展点）：传出被迁走的最深活动态 id——抢占型子类在此做"入恢复栈"。</summary>
        protected virtual void OnDeepestPreempted(TId outgoingDeepest) { }

        private void ValidateRequest(TId target)
        {
            if (_active.Count == 0) throw new InvalidOperationException($"{_name}:Start 之前禁止 Request");
            if (_inLeave) throw new InvalidOperationException($"{_name}:OnLeave 期间禁止 Request（离场中改道自相矛盾）");
            if (!_stages.ContainsKey(target)) throw new InvalidOperationException($"{_name}:阶段未注册 {target}");
            if (Cmp.Equals(target, Current))
                throw new InvalidOperationException($"{_name}:重入禁止({target})——重启语义请拆阶段或先退到父态");
        }

        /// <summary>绕过准入的入队口（恢复类迁移用；校验照常）。</summary>
        protected void EnqueueTransition(TId target, in TReq req, TransitionKind kind)
        {
            _pendingId = target;
            _pendingReq = req;
            _pendingKind = kind;
            _hasPending = true;
        }

        /// <summary>校验 + 绕准入入队（<c>TryResume</c> 的恢复通道：回到更早的状态，不该被抢占规则再挡一次）。审计 Kind = Resume。</summary>
        protected bool EnqueueRequest(TId target, in TReq req)
        {
            ValidateRequest(target);
            EnqueueTransition(target, in req, TransitionKind.Resume);
            return true;
        }

        /// <summary>查阶段实例（子类做准入判定读来者优先级/中断规则用；未注册抛）。</summary>
        protected IStage<TId, TReq> GetStage(TId id)
            => _stages.TryGetValue(id, out var s)
                ? s
                : throw new InvalidOperationException($"{_name}:阶段未注册 {id}");

        /// <summary>注册查询（Restore 校验快照 id 用，不抛）。</summary>
        protected bool IsRegistered(TId id) => _stages.ContainsKey(id);

        // ---- 快照与恢复（§3.4：层级机含活动路径与复合历史）----

        /// <summary>结构快照：活动路径 + 复合历史 + 恢复栈（抢占子类经 <see cref="CaptureCore"/> 补充）+ 驻留/计数；未启动抛。</summary>
        public FsmCapture<TId> Capture()
        {
            if (_active.Count == 0) throw new InvalidOperationException($"{_name}:Start 之前禁止 Capture");
            var capture = new FsmCapture<TId>
            {
                ActivePath = _active.ToArray(),
                History = new Dictionary<TId, TId[]>(_history.Count),
                StageTime = _stageTime,
                StageFrames = _stageFrames,
                TransitionCount = _transitionCount,
                TimeoutCount = TimeoutCount,
            };
            foreach (var kv in _history)
                capture.History[kv.Key] = kv.Value.ToArray();
            CaptureCore(capture);
            return capture;
        }

        /// <summary>子类补充扩展态（恢复栈等；抢占型机体转调共用件）。</summary>
        protected virtual void CaptureCore(FsmCapture<TId> into) { }

        /// <summary>
        /// 从快照恢复（《状态机专项设计》§3.4）。<see cref="FsmRestoreMode.Hooks"/>（默认）：按快照**精确路径**
        /// （不重展开）走事务回调序——分歧尾深→浅 `OnLeave`、目标尾浅→深 `OnEnter`(default)；
        /// <see cref="FsmRestoreMode.Silent"/> 纯结构置换零回调。两模式都清挂起请求（结构性裁决）；
        /// 历史/恢复栈/驻留/计数按快照置回，浮点按位。
        /// 约束：Start 前 / 阶段回调内 / `OnLeave` 窗口内 → 抛；快照内 id 未注册（结构已变）→ 抛。
        /// </summary>
        public void Restore(in FsmCapture<TId> capture, FsmRestoreMode mode = FsmRestoreMode.Hooks)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (_active.Count == 0) throw new InvalidOperationException($"{_name}:Start 之前禁止 Restore");
            if (_inStageCallback) throw new InvalidOperationException($"{_name}:阶段回调内禁止 Restore");
            if (_inLeave) throw new InvalidOperationException($"{_name}:OnLeave 期间禁止 Restore");
            var path = ValidateCapture(capture);                // 路径/历史 id 未注册 → 抛

            _hasPending = false;                                // 结构性裁决：挂起让位
            _pendingReq = default;

            var fromDeepest = Current;
            if (mode == FsmRestoreMode.Hooks)
                ApplyRestore(path);                             // LCA 分歧回调序（精确路径，不重展开）
            else
            {
                _active.Clear();
                for (int i = 0; i < path.Length; i++) _active.Add(path[i]);
            }
            if (!Cmp.Equals(fromDeepest, Current))
                _audit.RecordTransition(fromDeepest, Current, TransitionKind.Restore);   // 路径变化留审计（全同零分歧不记）

            _history.Clear();
            if (capture.History != null)
                foreach (var kv in capture.History)
                    _history[kv.Key] = new List<TId>(kv.Value);

            _stageTime = capture.StageTime;
            _stageFrames = capture.StageFrames;
            _transitionCount = capture.TransitionCount;
            TimeoutCount = capture.TimeoutCount;
            RestoreCore(capture);
        }

        /// <summary>子类置回扩展态（恢复栈等）。</summary>
        protected virtual void RestoreCore(FsmCapture<TId> from) { }

        private TId[] ValidateCapture(FsmCapture<TId> capture)
        {
            var path = capture.ActivePath;
            if (path == null || path.Length == 0)
                throw new ArgumentException("快照无活动路径", nameof(capture));
            for (int i = 0; i < path.Length; i++)
                if (!_stages.ContainsKey(path[i]))
                    throw new InvalidOperationException($"{_name}:快照路径含未注册 id:{path[i]}（机器结构已变，快照失效）");
            if (capture.History != null)
                foreach (var kv in capture.History)
                {
                    if (!_stages.ContainsKey(kv.Key))
                        throw new InvalidOperationException($"{_name}:快照历史键未注册:{kv.Key}（机器结构已变，快照失效）");
                    for (int i = 0; i < kv.Value.Length; i++)
                        if (!_stages.ContainsKey(kv.Value[i]))
                            throw new InvalidOperationException($"{_name}:快照历史值未注册:{kv.Value[i]}（机器结构已变，快照失效）");
                }
            return path;
        }

        /// <summary>Hooks 恢复事务：LCA 之下深→浅退、浅→深进——路径取快照原值（与 ApplyTransition 的差异：不重展开、不动历史）。</summary>
        private void ApplyRestore(TId[] path)
        {
            int lca = CommonPrefix(_active, path);
            int oldLen = _active.Count;
            if (lca == oldLen && lca == path.Length) return;    // 路径全同：零分歧零回调

            _inLeave = true;
            try
            {
                for (int i = oldLen - 1; i >= lca; i--)
                {
                    var id = _active[i];
                    _stages[id].OnLeave(this);
                    _active.RemoveAt(i);
                }
            }
            finally { _inLeave = false; }

            _inStageCallback = true;
            try
            {
                for (int i = lca; i < path.Length; i++)
                {
                    var id = path[i];
                    _active.Add(id);
                    _stages[id].OnEnter(this, default);
                }
            }
            finally { _inStageCallback = false; }
        }

        /// <summary>应用挂起请求：LCA 之下先退（深→浅）后进（浅→深）。成功事务记审计（中断事务不记——由 <see cref="Interrupted"/> 诊断）。</summary>
        public void Advance()
        {
            if (!_hasPending) return;

            var target = _pendingId;
            var req = _pendingReq;
            var kind = _pendingKind;
            _hasPending = false;
            _pendingReq = default;

            try
            {
                var fromDeepest = Current;
                var residenceSeconds = _stageTime;               // ApplyTransition 会归零，先取本段驻留
                ApplyTransition(target, in req);
                Interrupted = false;
                _audit.RecordLeave(fromDeepest, residenceSeconds);   // 本段驻留计入旧最深态（§4）
                _audit.RecordTransition(fromDeepest, Current, kind);
            }
            catch
            {
                Interrupted = true;                              // 不回滚：保留已完成部分，供诊断
                InterruptedTarget = target;
                throw;
            }
        }

        /// <summary>每帧驱动：OnUpdate 根→叶 → 自动条件边（仅本帧无挂起）→
        /// 驻留看门狗（条件边之后评估，同帧边命中则让位）→ 应用挂起（一帧最多一次事务）。</summary>
        public void Tick(float realDelta)
        {
            if (_active.Count == 0) return;
            _stageTime += realDelta;
            _stageFrames++;
            _inStageCallback = true;                            // OnUpdate 期间发起的迁移 = 自身推进（抢占子类放行）
            try
            {
                for (int i = 0; i < _active.Count; i++)
                    _stages[_active[i]].OnUpdate(this, realDelta);
            }
            finally { _inStageCallback = false; }
            if (!_hasPending)
            {
                if (_transitions != null && _transitions.TryTickEdge(Current, out var to, out var req))
                {
                    _audit.RecordAutoEdgeHit();
                    RequestCore(to, in req, int.MinValue, TransitionKind.Auto);
                }
                else if (_stages[Current] is IStageTimeout<TId> timeout
                    && timeout.TimeoutFrames > 0 && _stageFrames >= timeout.TimeoutFrames)
                {
                    ForceState(timeout.TimeoutTarget);          // §3.2 强制迁移（绕准入；重入/未注册照抛）
                    TimeoutCount++;
                }
            }
            if (_hasPending) Advance();
        }

        /// <summary>
        /// 停止并回到未启动态（可再次 `Start`）：活动路径**深→浅**逐层 `OnLeave`（与 enter 的浅→深对称），
        /// 然后清路径/历史/挂起/计数/中断标记。钩子抛异常时机器仍保证已复位（异常继续传播）。
        /// 子类覆写时先清自己的再调 base。
        /// </summary>
        public virtual void Reset()
        {
            _hasPending = false;
            _pendingReq = default;

            if (_active.Count > 0)
            {
                var leaving = new List<TId>(_active);            // 先拷贝：OnLeave 期间 ActivePath 视为已清空
                _active.Clear();
                _inLeave = true;
                try
                {
                    for (int i = leaving.Count - 1; i >= 0; i--)  // 深 → 浅
                        _stages[leaving[i]].OnLeave(this);
                }
                finally { _inLeave = false; }
            }

            _history.Clear();
            _stageTime = 0f;
            _stageFrames = 0;
            _transitionCount = 0;
            TimeoutCount = 0;
            LastReject = RejectReason.None;
            _inStageCallback = false;
            Interrupted = false;
            InterruptedTarget = default;
            _audit.Reset();
        }

        /// <summary>事件冒泡：**先评估迁移边**（命中即经 Request 入队并消费——§3.1 集成序两机一致），
        /// 未命中则从最深活动态向根问 <see cref="IEventSink{TEvt}"/>，首个实现的返回 true 即停。</summary>
        public bool Raise<TEvt>(in TEvt e)
        {
            if (_transitions != null && _transitions.TryRaiseEdge(Current, e, out var to, out var req))
            {
                _audit.RecordAutoEdgeHit();
                RequestCore(to, in req, int.MinValue, TransitionKind.Auto);   // 与手动请求同管线（抢占子类经准入）
                return true;                                    // 消费：不冒泡
            }
            for (int i = _active.Count - 1; i >= 0; i--)
                if (_stages[_active[i]] is IEventSink<TEvt> sink && sink.TryHandle(in e)) return true;
            return false;
        }

        // ---- 迁移事务 ----

        private void ApplyTransition(TId target, in TReq req)
        {
            var targetPath = BuildPath(target);
            int lca = CommonPrefix(_active, targetPath);
            int oldLen = _active.Count;

            // ① 先记录历史（读完整旧路径，此时不能弹出）
            for (int i = oldLen - 1; i >= lca; i--)
                if (_composite.TryGetValue(_active[i], out var spec) && spec.History != HistoryMode.None)
                    RecordHistory(spec, i);

            // ①' 被抢占通知（扩展点）：与平面机 Advance 的 OnStagePreempted 对齐——退出段开始前，
            //    传出被迁走的最深活动态（抢占子类据此入恢复栈；祖先路径由历史机制兜回，无需快照）
            if (oldLen > 0)
                OnDeepestPreempted(_active[oldLen - 1]);

            // ② 退出：深 → 浅（不含 LCA 层）。
            // 注意 `_inLeave` 只包住退出阶段——OnEnter 内 Request 必须合法（与平面机语义一致，
            // 否则"进入即想再迁"这种最自然的写法会被自己拦掉）。
            _inLeave = true;
            try
            {
                for (int i = oldLen - 1; i >= lca; i--)
                {
                    var id = _active[i];
                    _stages[id].OnLeave(this);
                    _active.RemoveAt(i);
                }
            }
            finally { _inLeave = false; }

            // ③ 进入：浅 → 深（LCA 层已在路径上，不重跑它的 OnEnter）——进入段属自身推进窗口
            _inStageCallback = true;
            try
            {
                for (int i = lca; i < targetPath.Count; i++)
                {
                    var id = targetPath[i];
                    _active.Add(id);
                    _stages[id].OnEnter(this, in req);
                    _audit.RecordEnter(id);
                }
            }
            finally { _inStageCallback = false; }

            // ④ 刷新活动复合态的历史：**子态在同一父态内切换**也要更新父的历史，
            // 否则"降层到父态"会回到更早的旧子页（历史只在父态整体退出时记录是不够的）。
            for (int i = 0; i < _active.Count; i++)
                if (_composite.TryGetValue(_active[i], out var spec) && spec.History != HistoryMode.None)
                    RecordHistory(spec, i);

            _transitionCount++;
            _stageTime = 0f;
            _stageFrames = 0;
        }

        /// <summary>目标路径 = 根→目标的显式链 + 目标之下的展开（历史/初始）。</summary>
        private List<TId> BuildPath(TId target)
        {
            var chain = new List<TId>(4);                        // target → 根
            var cur = target;
            while (true)
            {
                chain.Add(cur);
                if (!_parent.TryGetValue(cur, out var p)) break;
                cur = p;
            }

            var path = new List<TId>(chain.Count + 2);
            for (int i = chain.Count - 1; i >= 0; i--) path.Add(chain[i]);
            ExpandDown(path);
            return path;
        }

        /// <summary>
        /// 向下展开到叶：优先本次展开已取的 **deep 历史链**，其次该层自己的历史（浅历史只取首项），
        /// 最后退回 `InitialChild`。这样"回到父态"= 回到它上次的子页（UI 直觉），而 deep 能恢复整条链。
        /// </summary>
        private void ExpandDown(List<TId> path)
        {
            List<TId> deepSeed = null;
            int seedIdx = 0;

            while (_composite.TryGetValue(path[path.Count - 1], out var spec))
            {
                TId child;
                if (deepSeed != null && seedIdx < deepSeed.Count)
                {
                    child = deepSeed[seedIdx++];                 // deep 链回放
                }
                else if (spec.History != HistoryMode.None &&
                         _history.TryGetValue(spec.Id, out var h) && h.Count > 0)
                {
                    child = h[0];
                    if (spec.History == HistoryMode.Deep && h.Count > 1)
                    {
                        deepSeed = h;                            // 后续层级继续按同一条链回放
                        seedIdx = 1;
                    }
                }
                else
                {
                    child = spec.InitialChild;
                }
                path.Add(child);
            }
        }

        private void RecordHistory(CompositeSpec<TId> spec, int index)
        {
            int subCount = _active.Count - index - 1;            // 该复合态之下的子路径长度
            if (subCount <= 0) return;

            int take = spec.History == HistoryMode.Deep ? subCount : 1;
            var list = new List<TId>(take);
            for (int k = 0; k < take; k++) list.Add(_active[index + 1 + k]);
            _history[spec.Id] = list;
        }

        private static int CommonPrefix(List<TId> a, List<TId> b)
        {
            int n = Math.Min(a.Count, b.Count);
            int i = 0;
            while (i < n && Cmp.Equals(a[i], b[i])) i++;
            return i;
        }

        private static int CommonPrefix(List<TId> a, TId[] b)
        {
            int n = Math.Min(a.Count, b.Length);
            int i = 0;
            while (i < n && Cmp.Equals(a[i], b[i])) i++;
            return i;
        }

        // ---- IModuleStats ----

        public string StatsName => _name;

        /// <summary>统计快照（子类可 override 追加自己的项——先调 base 再 Add）。
        /// §4 诊断增量键：自动边命中/超时/拒绝计数/恢复成功（既有键不动，HUD 兼容）。</summary>
        public virtual void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            if (_active.Count == 0)
            {
                into["当前路径"] = "(未启动)";
            }
            else
            {
                var sb = new StringBuilder(_active.Count * 8);
                for (int i = 0; i < _active.Count; i++)
                {
                    if (i > 0) sb.Append('/');
                    sb.Append(_active[i]);
                }
                into["当前路径"] = sb.ToString();
            }
            into["深度"] = _active.Count.ToString();
            into["阶段数"] = _stages.Count.ToString();
            into["复合态数"] = _composite.Count.ToString();
            into["历史项"] = _history.Count.ToString();
            into["阶段时长"] = _stageTime.ToString("0.0");
            into["阶段帧数"] = _stageFrames.ToString();
            into["累计事务"] = _transitionCount.ToString();
            into["中断中"] = Interrupted.ToString();
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

        /// <summary>图导出（§4）：DOT 文本——节点 = 阶段、边 = 迁移边 ∪ 时长→NextId 链 ∪ 复合父子（虚线）。</summary>
        public string ExportGraph()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"digraph \"{_name}\" {{");
            sb.AppendLine("  rankdir=LR;");
            foreach (var id in _stages.Keys)
                sb.AppendLine($"  \"{id}\";");
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
            foreach (var spec in _composite.Values)
                for (int i = 0; i < spec.Children.Count; i++)
                    sb.AppendLine($"  \"{spec.Id}\" -> \"{spec.Children[i]}\" [style=dotted, label=\"child\"];");
            sb.AppendLine("}");
            return sb.ToString();
        }
#endif
    }
}
