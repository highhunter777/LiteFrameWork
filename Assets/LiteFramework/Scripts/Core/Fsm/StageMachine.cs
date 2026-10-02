using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 阶段契约：状态逻辑写成类，**状态标识是任意结构体** <typeparamref name="TId"/>（推荐 `enum`/`int` 等实现
    /// `IEquatable` 的类型以获免装箱比较）。状态对象是**无状态单例**——禁实例字段（同一实例会被多个机器共用），
    /// 可变数据走 payload 或宿主（驻留帧数读 <see cref="IStageHost{TId,TReq}.StageFrames"/>）。
    /// </summary>
    public interface IStage<TId, TReq> where TId : struct
    {
        void OnInit(IStageHost<TId, TReq> m);
        void OnEnter(IStageHost<TId, TReq> m, in TReq req);
        void OnUpdate(IStageHost<TId, TReq> m, float elapseSeconds);
        void OnLeave(IStageHost<TId, TReq> m);
    }

    /// <summary>
    /// 阶段状态机（**基础机**）：状态表 + payload + 两段式迁移 + 帧末应用。
    /// 只负责"状态怎么切"——**不含抢占/恢复/优先级**；需要那些能力用 <see cref="PreemptiveStageMachine{TId,TReq}"/>（子类，纯加法）；
    /// 层级 / 历史 / 冒泡用独立的 <see cref="HierarchicalStageMachine{TId,TReq}"/>。
    ///
    /// 迁移语义（7 条）：
    /// ① `Start` 前 `Tick` 静默；`Start` 前 `Request` 抛；
    /// ② `Request` 只入队（两段式）；`Tick` = `OnUpdate` → 有挂起则 `Advance`（帧末应用、一帧最多一变）；
    /// ③ 同帧多次 `Request` = last-wins（payload 同步覆盖）；
    /// ④ `OnEnter` 内 `Request` → 写 pending，下次 `Advance` 生效（不递归）；
    /// ⑤ 重入（请求 = 当前）→ 抛；⑥ `OnLeave` 期间 `Request` → 抛；⑦ 未注册 id → 抛。
    /// 另：`StageFrames` 每次 `Tick` +1（不是 dt 累加）、迁移后归零（帧窗口判据）。
    ///
    /// **异常策略不在内核**：阶段回调不捕获异常，由驱动层兜。
    /// 给子类的扩展点：<see cref="CanAccept"/>（准入判定）、<see cref="OnStagePreempted"/>（离场前通知）、
    /// <see cref="RequestCore"/>（带优先级的请求内核）、<see cref="Snapshot"/>（统计）、<see cref="GetStage"/>、<see cref="InStageCallback"/>。
    /// </summary>
    public class StageMachine<TId, TReq> : ITickable, IModuleStats, IStageHost<TId, TReq>
        where TId : struct
    {
        private static readonly IEqualityComparer<TId> Cmp = EqualityComparer<TId>.Default;

        private readonly string _name;
        private readonly Dictionary<TId, IStage<TId, TReq>> _stages;

        private IStage<TId, TReq> _current;
        private TId _currentId;
        private bool _hasPending;
        private TId _pendingId;
        private TReq _pendingReq;             // payload：与 pending 一起交接（last-wins 时同步覆盖）
        private bool _inLeave;                // OnLeave 执行中（改道禁令窗口）
        private bool _inStageCallback;        // 阶段钩子执行中（此时发起的迁移 = "自身推进"）
        private float _stageTime;             // 秒（单机吃 deltaTime）
        private int _stageFrames;             // 整数帧（帧窗口用；每次 Tick +1）
        private long _transitionCount;

        public StageMachine(string name, params (TId id, IStage<TId, TReq> stage)[] stages)
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
            foreach (var s in _stages.Values) s.OnInit(this);     // 全部阶段先于 Start 初始化一遍
        }

        /// <summary>当前阶段 id（未 Start 时无意义，配 <see cref="Started"/> 判读）。</summary>
        public TId Current => _currentId;

        /// <summary>是否已 Start。</summary>
        public bool Started => _current != null;

        /// <summary>是否有挂起请求。</summary>
        public bool HasPending => _hasPending;

        /// <summary>挂起目标 id（无挂起时无意义）。</summary>
        public TId PendingId => _pendingId;

        /// <summary>当前阶段已持续时长（秒；`Advance` 后归零）。</summary>
        public float StageTime => _stageTime;

        /// <summary>当前阶段驻留的**整数帧数**（每次 `Tick` +1，迁移后归零）——帧窗口判据。</summary>
        public int StageFrames => _stageFrames;

        /// <summary>累计迁移次数（诊断）。</summary>
        public long TransitionCount => _transitionCount;

        /// <summary>
        /// 最近一次请求被拒的原因（每次 `Request`/`TryResume` 刷新；被接受则回 <see cref="RejectReason.None"/>）。
        /// 基础机恒接受，故恒为 None；抢占型子类会写 <see cref="RejectReason.Priority"/> / <see cref="RejectReason.InterruptDisallowed"/> 等。
        /// </summary>
        public RejectReason LastReject { get; private set; }

        /// <summary>阶段钩子是否正在执行（子类判定"自身推进"用）。</summary>
        protected bool InStageCallback => _inStageCallback;

        /// <summary>当前阶段实例（子类查询中断规则/恢复意愿用）。</summary>
        protected IStage<TId, TReq> CurrentStage => _current;

        /// <summary>启动（一次）；全部阶段已 OnInit。</summary>
        public void Start(TId initialId)
        {
            if (_current != null) throw new InvalidOperationException($"{_name}:Start 只能调用一次");
            var stage = GetStage(initialId);
            _current = stage;
            _currentId = initialId;
            _stageTime = 0f;
            _stageFrames = 0;
            EnterStage(stage, default);                          // 初始进入无 leave，立即执行（payload 用 default）
        }

        /// <summary>发起迁移请求（只入队）。基础机**恒接受**（返回 true）；抢占型子类可能返回 false。</summary>
        public bool Request(TId nextId, in TReq req) => RequestCore(nextId, in req, int.MinValue);

        /// <summary>无 payload 的迁移请求。</summary>
        public bool Request(TId nextId) => RequestCore(nextId, default, int.MinValue);

        /// <summary>
        /// 请求内核（守卫 → 准入判定 → 入队）。子类可暴露"带优先级"的公开重载转调它；
        /// <paramref name="priority"/> 对基础机无意义（<see cref="CanAccept"/> 默认忽略它）。
        /// </summary>
        protected bool RequestCore(TId nextId, in TReq req, int priority)
        {
            ValidateRequest(nextId);
            if (!CanAccept(nextId, priority)) return false;       // 被拒 = 正常路径（子类在 CanAccept 里 MarkRejected）
            LastReject = RejectReason.None;                       // 被接受 → 清上次原因（LastReject 恒反映"最近一次"）
            EnqueuePending(nextId, in req);
            return true;
        }

        /// <summary>子类在被拒时上报原因（供 <see cref="LastReject"/> 读数）。</summary>
        protected void MarkRejected(RejectReason reason) => LastReject = reason;

        /// <summary>
        /// 绕过准入判定的入队口（**恢复类迁移**用：`TryResume` 是"回到更早的状态"，不该被抢占规则再挡一次）。
        /// 守卫（未 Start / OnLeave / 未注册 / 重入）照常生效。
        /// </summary>
        protected bool EnqueueRequest(TId nextId, in TReq req)
        {
            ValidateRequest(nextId);
            EnqueuePending(nextId, in req);
            return true;
        }

        private void ValidateRequest(TId nextId)
        {
            if (_current == null) throw new InvalidOperationException($"{_name}:Start 之前禁止 Request");
            if (_inLeave) throw new InvalidOperationException($"{_name}:OnLeave 期间禁止 Request（离场中改道自相矛盾）");
            GetStage(nextId);                                   // 未注册 → 当场抛（不等帧末）
            if (Cmp.Equals(nextId, _currentId))
                throw new InvalidOperationException($"{_name}:重入禁止({nextId})——重启语义请拆阶段或先退出");
        }

        private void EnqueuePending(TId nextId, in TReq req)
        {
            _pendingId = nextId;                                // last-wins
            _pendingReq = req;                                  // payload 同步 last-wins
            _hasPending = true;
        }

        /// <summary>准入判定（扩展点）：基础机恒接受；抢占型子类在此实现"优先级 + 中断规则"。</summary>
        protected virtual bool CanAccept(TId incomingId, int priority) => true;

        /// <summary>旧阶段被替换前的通知（扩展点）：抢占型子类在此做"入恢复栈"。</summary>
        protected virtual void OnStagePreempted(TId outgoingId) { }

        /// <summary>应用挂起请求：`OnLeave`(旧) → `OnEnter`(新, in payload)。</summary>
        public void Advance()
        {
            if (!_hasPending) return;

            var next = GetStage(_pendingId);
            var nextId = _pendingId;
            var req = _pendingReq;
            _hasPending = false;
            _pendingReq = default;                              // 交接后清引用（防 payload 里的对象被长期持有）

            OnStagePreempted(_currentId);                       // 扩展点：子类可记录"被抢占前的状态"

            _inLeave = true;
            try { _current.OnLeave(this); }
            finally { _inLeave = false; }

            _transitionCount++;
            _current = next;
            _currentId = nextId;
            _stageTime = 0f;
            _stageFrames = 0;
            EnterStage(next, in req);                           // 此处再 Request → 写 pending，下次 Advance 生效（不递归）
        }

        /// <summary>
        /// 停止并回到**未启动态**（可再次 `Start`）：对当前阶段调 `OnLeave`（与 `Start` 的 `OnEnter` 对称，
        /// 让阶段收尾），然后清挂起 / 计时 / 计数（回到"刚构造"状态）。
        /// `OnLeave` 抛异常时**机器仍保证已复位**（异常继续向外传播，内核不捕获）。
        /// </summary>
        public virtual void Reset()
        {
            var leaving = _current;
            if (leaving != null)
            {
                try
                {
                    _inLeave = true;                             // 与 Advance 的离场窗口同语义：OnLeave 内再 Request 当场抛
                    leaving.OnLeave(this);
                }
                finally { ClearRuntimeState(); }
                return;
            }
            ClearRuntimeState();
        }

        private void ClearRuntimeState()
        {
            _current = null;
            _currentId = default;
            _hasPending = false;
            _pendingReq = default;
            _inLeave = false;
            _inStageCallback = false;
            _stageTime = 0f;
            _stageFrames = 0;
            _transitionCount = 0;
            LastReject = RejectReason.None;
        }

        /// <summary>每帧驱动：`OnUpdate` → 有挂起则 `Advance`（一帧最多一变）。</summary>
        public void Tick(float realDelta)
        {
            if (_current == null) return;                       // 未启动：静默跳过（创建与 Start 应同帧）
            _stageTime += realDelta;
            _stageFrames++;                                     // 帧窗口计数（每次 Tick 一帧）
            UpdateStage(realDelta);
            if (_hasPending) Advance();
        }

        /// <summary>
        /// 事件处理（可选接口 <see cref="IEventSink{TEvt}"/>）：平面机只有一个活动阶段，故等价于"问它一次"。
        /// 层级机的冒泡在 <see cref="HierarchicalStageMachine{TId,TReq}.Raise{TEvt}"/>。
        /// </summary>
        public bool Raise<TEvt>(in TEvt e)
            => _current is IEventSink<TEvt> sink && sink.TryHandle(in e);

        /// <summary>查阶段（子类/表驱动件用；未注册抛）。</summary>
        protected IStage<TId, TReq> GetStage(TId id)
        {
            if (!_stages.TryGetValue(id, out var s))
                throw new InvalidOperationException($"{_name}:阶段未注册 {id}");
            return s;
        }

        // ---- 钩子包装：执行期间置"_inStageCallback"（= 阶段自身推进；子类抢占规则应放行）----

        private void EnterStage(IStage<TId, TReq> stage, in TReq req)
        {
            _inStageCallback = true;
            try { stage.OnEnter(this, in req); }
            finally { _inStageCallback = false; }
        }

        private void UpdateStage(float delta)
        {
            _inStageCallback = true;
            try { _current.OnUpdate(this, delta); }
            finally { _inStageCallback = false; }
        }

        // ---- IModuleStats（HUD：注册即发现；禁每帧分配）----

        public string StatsName => _name;

        /// <summary>统计快照（子类可 override 追加自己的项——先调 base 再 Add）。</summary>
        public virtual void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["当前阶段"] = _current == null ? "(未启动)" : _currentId.ToString();
            into["阶段时长"] = _stageTime.ToString("0.0");
            into["阶段帧数"] = _stageFrames.ToString();
            into["阶段数"] = _stages.Count.ToString();
            into["累计切换"] = _transitionCount.ToString();
            into["待应用"] = _hasPending.ToString();
        }
    }
}
