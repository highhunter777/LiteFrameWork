using System;
using System.Collections.Generic;
using System.Text;

namespace LiteFramework
{
    /// <summary>
    /// 层级状态机（HSM）：`StageMachine` 的超集——状态空间是一棵**树**（复合态可含子层），
    /// 共享同一份 <see cref="IStage{TId,TReq}"/> 契约与"帧末应用 / last-wins / 离场禁改道"等语义。
    /// flat 机 = 无复合态时的退化形态。
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
    /// </summary>
    public sealed class HierarchicalStageMachine<TId, TReq> : ITickable, IModuleStats, IStageHost<TId, TReq>
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

        private bool _hasPending;
        private TId _pendingId;
        private TReq _pendingReq;
        private bool _inLeave;
        private float _stageTime;
        private int _stageFrames;
        private long _transitionCount;

        public HierarchicalStageMachine(string name,
            (TId id, IStage<TId, TReq> stage)[] stages,
            CompositeSpec<TId>[] composites)
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

        /// <summary>上一次事务是否中途抛异常而中断（诊断；成功事务会清零）。</summary>
        public bool Interrupted { get; private set; }

        /// <summary>中断时的目标 id（诊断）。</summary>
        public TId InterruptedTarget { get; private set; }

        /// <summary>启动：从给定根一路展开到叶（无历史 → 全用 `InitialChild`）。只能一次。</summary>
        public void Start(TId root)
        {
            if (_active.Count > 0) throw new InvalidOperationException($"{_name}:Start 只能调用一次");
            if (_parent.ContainsKey(root))
                throw new InvalidOperationException($"{_name}:{root} 不是根（它有父态）");

            _active.Add(root);
            ExpandDown(_active);
            for (int i = 0; i < _active.Count; i++)
                _stages[_active[i]].OnEnter(this, default);
            _stageTime = 0f;
            _stageFrames = 0;
        }

        /// <summary>
        /// 发起迁移请求（只入队，last-wins）。目标可以是任意层级的阶段 id——**路径由树反查**，调用方不给路径。
        /// 未 Start / 未注册 / 重入（= 当前最深）/ `OnLeave` 窗口内 → 当场抛。
        /// </summary>
        public bool Request(TId target, in TReq req)
        {
            if (_active.Count == 0) throw new InvalidOperationException($"{_name}:Start 之前禁止 Request");
            if (_inLeave) throw new InvalidOperationException($"{_name}:OnLeave 期间禁止 Request（离场中改道自相矛盾）");
            if (!_stages.ContainsKey(target)) throw new InvalidOperationException($"{_name}:阶段未注册 {target}");
            if (Cmp.Equals(target, Current))
                throw new InvalidOperationException($"{_name}:重入禁止({target})——重启语义请拆阶段或先退到父态");

            _pendingId = target;
            _pendingReq = req;
            _hasPending = true;
            return true;                       // 层级机不做优先级抢占
        }

        public bool Request(TId target) => Request(target, default);

        /// <summary>应用挂起请求：LCA 之下先退（深→浅）后进（浅→深）。</summary>
        public void Advance()
        {
            if (!_hasPending) return;

            var target = _pendingId;
            var req = _pendingReq;
            _hasPending = false;
            _pendingReq = default;

            try
            {
                ApplyTransition(target, in req);
                Interrupted = false;
            }
            catch
            {
                Interrupted = true;                              // 不回滚：保留已完成部分，供诊断
                InterruptedTarget = target;
                throw;
            }
        }

        /// <summary>每帧驱动：OnUpdate 根→叶，然后应用挂起（一帧最多一次事务）。</summary>
        public void Tick(float realDelta)
        {
            if (_active.Count == 0) return;
            _stageTime += realDelta;
            _stageFrames++;
            for (int i = 0; i < _active.Count; i++)
                _stages[_active[i]].OnUpdate(this, realDelta);
            if (_hasPending) Advance();
        }

        /// <summary>
        /// 停止并回到未启动态（可再次 `Start`）：活动路径**深→浅**逐层 `OnLeave`（与 enter 的浅→深对称），
        /// 然后清路径/历史/挂起/计数/中断标记。钩子抛异常时机器仍保证已复位（异常继续传播）。
        /// </summary>
        public void Reset()
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
            Interrupted = false;
            InterruptedTarget = default;
        }

        /// <summary>事件冒泡：从最深活动态向根，首个实现的 <see cref="IEventSink{TEvt}"/> 返回 true 即停。</summary>
        public bool Raise<TEvt>(in TEvt e)
        {
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

            // ③ 进入：浅 → 深（LCA 层已在路径上，不重跑它的 OnEnter）
            for (int i = lca; i < targetPath.Count; i++)
            {
                var id = targetPath[i];
                _active.Add(id);
                _stages[id].OnEnter(this, in req);
            }

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

        // ---- IModuleStats ----

        public string StatsName => _name;

        public void Snapshot(Dictionary<string, string> into)
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
        }
    }
}
