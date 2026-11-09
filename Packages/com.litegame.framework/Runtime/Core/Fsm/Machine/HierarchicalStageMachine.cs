using System;
using System.Collections.Generic;
using System.Text;

namespace LiteFramework
{
    /// <summary>
    /// 层级状态机（HSM）：`StageMachine` 的超集——状态空间是一棵**树**（复合态可含子层）。
    /// </summary>
    public class HierarchicalStageMachine<TId, TReq> : StageMachineCore<TId, TReq>, ITickable, IModuleStats, IStageHost<TId, TReq>
        where TId : struct
    {
        private readonly List<TId> _active = new List<TId>(4);      // 根 → 最深活动态

        // ---- 层级树表示法（本机独有；共享内核不持有树语义）----。
        private readonly Dictionary<TId, CompositeSpec<TId>> _composite = new Dictionary<TId, CompositeSpec<TId>>();
        private readonly Dictionary<TId, TId> _parent = new Dictionary<TId, TId>();   // 非根 → 父
        private readonly Dictionary<TId, List<TId>> _history = new Dictionary<TId, List<TId>>();   // 复合态 → 上次退出时的子路径
        private readonly List<TId> _roots = new List<TId>(2);       // 根集合（无父者；HSM 允许多根）

        public HierarchicalStageMachine(string name,
            (TId id, IStage<TId, TReq> stage)[] stages,
            CompositeSpec<TId>[] composites,
            TransitionTable<TId, TReq> transitions = null)
            : base(name, stages)
        {
            BuildTree(composites);                                  // 原序：注册 → 建树 → 迁移表校验 → 审计 → OnInit
            InitializeCore(transitions);
        }

        /// <summary>根集合（无父的阶段；HSM 允许多根，跨根迁移即全退全进）。</summary>
        public IReadOnlyList<TId> Roots => _roots;

        public override bool Started => _active.Count > 0;

        /// <summary>最深活动态。</summary>
        public override TId Current => _active[_active.Count - 1];

        /// <summary>活动路径（根 → 最深活动态）；迁移事务进行中会看到中间态。</summary>
        public IReadOnlyList<TId> ActivePath => _active;

        /// <summary>上一次事务是否中途抛异常而中断（诊断；成功事务会清零）。</summary>
        public bool Interrupted { get; private set; }

        /// <summary>中断时的目标 id（诊断）。</summary>
        public TId InterruptedTarget { get; private set; }

        /// <summary>最深活动态实例（看门狗读取超时声明）。</summary>
        protected override IStage<TId, TReq> ActiveStage => _active.Count > 0 ? _stages[_active[_active.Count - 1]] : null;

        /// <summary>层级机的重入指引：回不去的活动态按"先退到父态"重启。</summary>
        protected override InvalidOperationException ReentryError(TId id)
            => new InvalidOperationException($"{_name}:重入禁止({id})——重启语义请拆阶段或先退到父态");

        /// <summary>启动：从给定根一路展开到叶（无历史 → 全用 `InitialChild`）。只能一次。</summary>
        public void Start(TId root)
        {
            EnsureInitialized();
            if (_active.Count > 0) throw new InvalidOperationException($"{_name}:Start 只能调用一次");
            if (_parent.ContainsKey(root))
                throw new InvalidOperationException($"{_name}:{root} 不是根（它有父态）");
            _transitions?.Lock();                               // 封板——两机同口径
            StageGraphDiagnostics<TId, TReq>.Audit(_name, _stages, _composite, _parent, _transitions, root);   // §3.5 深校验（一次性、只告警）

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

        // ---- 快照与恢复（§3.4：层级机含活动路径与复合历史）----。

        /// <summary>结构快照：活动路径、复合历史、驻留与计数；未启动抛。</summary>
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
            return capture;
        }

        /// <summary>
        /// 从快照恢复（《状态机专项设计》§3.4）。
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
        }

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
        public override void Advance()
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

        /// <summary>每帧驱动：OnUpdate 根→叶 → 自动边/看门狗/应用挂起（公共尾段见内核）。</summary>
        public void Tick(float realDelta)
        {
            if (_active.Count == 0) return;
            _stageTime += realDelta;
            _stageFrames++;
            _inStageCallback = true;                            // OnUpdate 期间禁止快照恢复重入。
            try
            {
                for (int i = 0; i < _active.Count; i++)
                    _stages[_active[i]].OnUpdate(this, realDelta);
            }
            finally { _inStageCallback = false; }
            TickAutoEdgesAndApply();
        }

        /// <summary>
        /// 停止并回到未启动态（可再次 `Start`）：活动路径**深→浅**逐层 `OnLeave`（与 enter 的浅→深对称）， 然后清路径/历史/挂起/计数/中断标记。
        /// </summary>
        public virtual void Reset()
        {
            EnsureInitialized();
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
            _inStageCallback = false;
            Interrupted = false;
            InterruptedTarget = default;
            ClearPendingAndCounters();
        }

        /// <summary>事件冒泡：**先评估迁移边**（公共入口见内核——§3.1 集成序两机一致），
        /// 未命中则从最深活动态向根问 <see cref="IEventSink{TEvt}"/>，首个实现的返回 true 即停。</summary>
        public bool Raise<TEvt>(in TEvt e)
        {
            if (TryConsumeByTransitionEdge(in e)) return true;
            for (int i = _active.Count - 1; i >= 0; i--)
                if (_stages[_active[i]] is IEventSink<TEvt> sink && sink.TryHandle(in e)) return true;
            return false;
        }

        // ---- 迁移事务 ----。

        private void ApplyTransition(TId target, in TReq req)
        {
            var targetPath = BuildPath(target);
            int lca = CommonPrefix(_active, targetPath);
            int oldLen = _active.Count;

            // ① 先记录历史（读完整旧路径，此时不能弹出）。
            for (int i = oldLen - 1; i >= lca; i--)
                if (_composite.TryGetValue(_active[i], out var spec) && spec.History != HistoryMode.None)
                    RecordHistory(spec, i);

            // ② 退出：深 → 浅（不含 LCA 层）。
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

            // ③ 进入：浅 → 深（LCA 层已在路径上，不重跑它的 OnEnter）——进入段属自身推进窗口。
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

        // ---- IModuleStats ----。

        /// <summary>统计快照（子类可 override 追加自己的项——先调 base 再 Add）。
        /// 诊断计数段单源在内核（§4；既有键不动，HUD 兼容）。</summary>
        public virtual void Snapshot(Dictionary<string, string> into)
        {
            EnsureInitialized();
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
            AppendSharedCounters(into);
        }

        /// <summary>DOT 边段覆写：默认段（迁移边 + 时长链）之后补复合父子（虚线）。</summary>
        protected override void ExportGraphEdges(System.Text.StringBuilder sb)
        {
            base.ExportGraphEdges(sb);
            foreach (var spec in _composite.Values)
                for (int i = 0; i < spec.Children.Count; i++)
                    sb.AppendLine($"  \"{spec.Id}\" -> \"{spec.Children[i]}\" [style=dotted, label=\"child\"];");
        }

        // ---- 层级树构建（本机表示法；共享内核不持有）----。

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
