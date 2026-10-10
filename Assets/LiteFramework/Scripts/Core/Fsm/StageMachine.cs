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
    /// **迁移语义（守卫/准入/挂起/自动边/看门狗）单源在 <see cref="StageMachineCore{TId,TReq}"/>**
    /// ——与层级机共享同一内核，改语义只动内核一处；本类只持有平面表示法：单活动态 + 单级进出 + 单 sink。
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
    /// 给子类的扩展点：<see cref="StageMachineCore{TId,TReq}.CanAccept"/>（准入判定）、
    /// <see cref="StageMachineCore{TId,TReq}.OnStagePreempted"/>（离场前通知）、
    /// <see cref="StageMachineCore{TId,TReq}.RequestCore"/>（带优先级的请求内核）、
    /// <see cref="Snapshot"/>（统计）、<see cref="StageMachineCore{TId,TReq}.GetStage"/>、
    /// <see cref="StageMachineCore{TId,TReq}.InStageCallback"/>。
    /// </summary>
    public class StageMachine<TId, TReq> : StageMachineCore<TId, TReq>, ITickable, IModuleStats, IStageHost<TId, TReq>
        where TId : struct
    {
        private IStage<TId, TReq> _current;
        private TId _currentId;

        public StageMachine(string name, params (TId id, IStage<TId, TReq> stage)[] stages)
            : base(name, null, stages, null, buildTree: false) { }

        /// <summary>带迁移表的构造（配置期 fail-fast：边 From/To 未注册 / To==From 当场抛）。</summary>
        public StageMachine(string name, TransitionTable<TId, TReq> transitions, params (TId id, IStage<TId, TReq> stage)[] stages)
            : base(name, transitions, stages, null, buildTree: false) { }

        /// <summary>当前阶段 id（未 Start 时无意义，配 <see cref="Started"/> 判读）。</summary>
        public override TId Current => _currentId;

        /// <summary>是否已 Start。</summary>
        public override bool Started => _current != null;

        /// <summary>当前活动态实例（看门狗读超时声明；与 <see cref="CurrentStage"/> 同物——前者是内核缝、后者是子类查询面）。</summary>
        protected override IStage<TId, TReq> ActiveStage => _current;

        /// <summary>当前阶段实例（子类查询中断规则/恢复意愿用）。</summary>
        protected IStage<TId, TReq> CurrentStage => _current;

        /// <summary>启动（一次）；全部阶段已 OnInit。</summary>
        public void Start(TId initialId)
        {
            if (_current != null) throw new InvalidOperationException($"{_name}:Start 只能调用一次");
            _transitions?.Lock();                                // 封板（Start 后加边抛——§3.6）
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            StageGraphDiagnostics<TId, TReq>.Audit(_name, _stages, null, null, _transitions, initialId);   // §3.5 深校验
#endif
            var stage = GetStage(initialId);
            _current = stage;
            _currentId = initialId;
            _stageTime = 0f;
            _stageFrames = 0;
            EnterStage(stage, default);                          // 初始进入无 leave，立即执行（payload 用 default）
            _audit.RecordEnter(initialId);
        }

        /// <summary>应用挂起请求：`OnLeave`(旧) → `OnEnter`(新, in payload)。</summary>
        public override void Advance()
        {
            if (!_hasPending) return;

            var next = GetStage(_pendingId);
            var nextId = _pendingId;
            var req = _pendingReq;
            var kind = _pendingKind;
            _hasPending = false;
            _pendingReq = default;                              // 交接后清引用（防 payload 里的对象被长期持有）

            OnStagePreempted(_currentId);                       // 扩展点：子类可记录"被抢占前的状态"

            _inLeave = true;
            try { _current.OnLeave(this); }
            finally { _inLeave = false; }

            _audit.RecordLeave(_currentId, _stageTime);         // 本段驻留计入旧态（§4）
            _audit.RecordTransition(_currentId, nextId, kind);  // 审计 From→To（此刻 _currentId 仍为旧态）
            _transitionCount++;
            _current = next;
            _currentId = nextId;
            _stageTime = 0f;
            _stageFrames = 0;
            EnterStage(next, in req);                           // 此处再 Request → 写 pending，下次 Advance 生效（不递归）
            _audit.RecordEnter(nextId);
        }

        // ---- 快照与恢复（§3.4）----

        /// <summary>结构快照：当前态 + 恢复栈（子类经 <see cref="CaptureCore"/> 补充）+ 驻留/计数；未启动抛。捕获时一次性分配。</summary>
        public FsmCapture<TId> Capture()
        {
            if (_current == null) throw new InvalidOperationException($"{_name}:Start 之前禁止 Capture");
            var capture = new FsmCapture<TId>
            {
                ActivePath = new[] { _currentId },
                StageTime = _stageTime,
                StageFrames = _stageFrames,
                TransitionCount = _transitionCount,
                TimeoutCount = TimeoutCount,
            };
            CaptureCore(capture);
            return capture;
        }

        /// <summary>子类补充扩展态（恢复栈等；抢占型机体转调共用件）。</summary>
        protected virtual void CaptureCore(FsmCapture<TId> into) { }

        /// <summary>
        /// 从快照恢复（《状态机专项设计》§3.4）。<see cref="FsmRestoreMode.Hooks"/>（默认）复用迁移事务——
        /// 目标与当前不同则旧 `OnLeave` → 新 `OnEnter`(default)；<see cref="FsmRestoreMode.Silent"/> 纯结构置换零回调。
        /// 两模式都**清挂起请求**（结构性裁决，挂起让位）；浮点按位置回。
        /// 约束：Start 前 / 阶段回调内 / `OnLeave` 窗口内 → 抛；capture 内 id 未注册（结构已变）→ 抛。
        /// </summary>
        public void Restore(in FsmCapture<TId> capture, FsmRestoreMode mode = FsmRestoreMode.Hooks)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (_current == null) throw new InvalidOperationException($"{_name}:Start 之前禁止 Restore");
            if (_inStageCallback) throw new InvalidOperationException($"{_name}:阶段回调内禁止 Restore");
            if (_inLeave) throw new InvalidOperationException($"{_name}:OnLeave 期间禁止 Restore");
            if (capture.ActivePath == null || capture.ActivePath.Length == 0)
                throw new ArgumentException("快照无活动路径", nameof(capture));

            var target = capture.ActivePath[0];
            var next = GetStage(target);                        // 未注册抛（结构已变）

            _hasPending = false;                                // 结构性裁决：挂起让位
            _pendingReq = default;

            if (mode == FsmRestoreMode.Hooks && !Cmp.Equals(target, _currentId))
            {
                _inLeave = true;                                // 复用迁移事务：旧 Leave → 新 Enter(default)
                try { _current.OnLeave(this); }
                finally { _inLeave = false; }
                _audit.RecordLeave(_currentId, _stageTime);
                _audit.RecordTransition(_currentId, target, TransitionKind.Restore);
                _current = next;
                _currentId = target;
                EnterStage(next, default);
                _audit.RecordEnter(target);
            }
            else
            {
                if (!Cmp.Equals(target, _currentId))
                    _audit.RecordTransition(_currentId, target, TransitionKind.Restore);   // Silent 状态变化也留审计
                _current = next;                                // Silent（或 Hooks 同态零分歧）：纯置换
                _currentId = target;
            }

            _stageTime = capture.StageTime;
            _stageFrames = capture.StageFrames;
            _transitionCount = capture.TransitionCount;
            TimeoutCount = capture.TimeoutCount;
            RestoreCore(capture);
        }

        /// <summary>子类置回扩展态（恢复栈等）。</summary>
        protected virtual void RestoreCore(FsmCapture<TId> from) { }

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
            _inLeave = false;
            _inStageCallback = false;
            ClearPendingAndCounters();
        }

        /// <summary>每帧驱动：`OnUpdate` → 自动边/看门狗/应用挂起（公共尾段见内核 <see cref="StageMachineCore{TId,TReq}.TickAutoEdgesAndApply"/>）。</summary>
        public void Tick(float realDelta)
        {
            if (_current == null) return;                       // 未启动：静默跳过（创建与 Start 应同帧）
            _stageTime += realDelta;
            _stageFrames++;                                     // 帧窗口计数（每次 Tick 一帧）
            UpdateStage(realDelta);
            TickAutoEdgesAndApply();
        }

        /// <summary>
        /// 事件处理：《状态机专项设计》§3.1 集成序——**先评估迁移边、后问阶段 sink**（公共入口见内核）；
        /// 未命中则问当前阶段 sink——平面机只有一个活动阶段，故等价于"问它一次"；
        /// 层级机的冒泡在 <see cref="HierarchicalStageMachine{TId,TReq}.Raise{TEvt}"/>。
        /// 未注册事件无边零短路。
        /// </summary>
        public bool Raise<TEvt>(in TEvt e)
        {
            if (TryConsumeByTransitionEdge(in e)) return true;
            return _current is IEventSink<TEvt> sink && sink.TryHandle(in e);
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

        /// <summary>统计快照（子类可 override 追加自己的项——先调 base 再 Add）。
        /// 诊断计数段单源在内核（§4；既有键不动，HUD 兼容）。</summary>
        public virtual void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["当前阶段"] = _current == null ? "(未启动)" : _currentId.ToString();
            into["阶段时长"] = _stageTime.ToString("0.0");
            into["阶段帧数"] = _stageFrames.ToString();
            into["阶段数"] = _stages.Count.ToString();
            into["累计切换"] = _transitionCount.ToString();
            into["待应用"] = _hasPending.ToString();
            AppendSharedCounters(into);
        }
    }
}
