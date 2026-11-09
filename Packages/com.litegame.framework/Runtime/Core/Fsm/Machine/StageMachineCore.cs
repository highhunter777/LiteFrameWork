using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>状态机共享内核：阶段注册、迁移请求与公共读数。</summary>
    public abstract class StageMachineCore<TId, TReq> : IStageHost<TId, TReq>
        where TId : struct
    {
        protected static readonly IEqualityComparer<TId> Cmp = EqualityComparer<TId>.Default;

        protected readonly string _name;
        protected readonly Dictionary<TId, IStage<TId, TReq>> _stages;
        private bool _initialized;

        protected bool _hasPending;
        protected TId _pendingId;
        protected TReq _pendingReq;
        protected bool _inLeave;
        protected float _stageTime;
        protected int _stageFrames;
        protected long _transitionCount;

        /// <summary>构造共享件：只做阶段注册（重复 id/空实例 fail-fast）。
        /// 子类构造尾段必须调用 <see cref="InitializeCore"/>；层级机须在调用前完成建树。</summary>
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
                _stages.Add(id, stage);                          // 同一实例可挂多个 id
            }
        }

        /// <summary>初始化共享内核（子类构造尾段调用一次）：全部阶段 `OnInit`。</summary>
        protected void InitializeCore()
        {
            if (_initialized) throw new InvalidOperationException($"{_name}:InitializeCore 只能调用一次");
            _initialized = true;
            foreach (var s in _stages.Values) s.OnInit(this);
        }

        /// <summary>初始化守卫：子类构造漏调 <see cref="InitializeCore"/> 时显性失败。</summary>
        protected void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException($"{_name}:机器未初始化：子类构造未调用 InitializeCore");
        }

        // ---- 机器表示法缝（子类必答：当前活动态是什么）----。

        /// <summary>是否已 Start（平面机：有当前态；层级机：活动路径非空）。</summary>
        public abstract bool Started { get; }

        /// <summary>当前活动态 id（平面机：当前态；层级机：最深活动态）。</summary>
        public abstract TId Current { get; }

        /// <summary>应用挂起请求（帧末）：平面机 = 单级进出；层级机 = LCA 事务。</summary>
        internal abstract void Advance();

        // ---- 共享读数（两机同口径）----。

        public bool HasPending => _hasPending;

        public TId PendingId => _pendingId;

        public float StageTime => _stageTime;

        public int StageFrames => _stageFrames;

        /// <summary>累计迁移/事务次数（诊断；层级机一次跨层事务计一次）。</summary>
        public long TransitionCount => _transitionCount;

        // ---- 请求管线（守卫 → 入队；两机同语义）----。

        /// <summary>发起迁移请求（只入队，last-wins）；通过守卫后返回 true。</summary>
        public bool Request(TId nextId, in TReq req) => RequestCore(nextId, in req);

        /// <summary>无 payload 的迁移请求。</summary>
        public bool Request(TId nextId) => RequestCore(nextId, default);

        private bool RequestCore(TId nextId, in TReq req)
        {
            ValidateRequest(nextId);
            EnqueuePending(nextId, in req);
            return true;
        }

        /// <summary>守卫：未 Start / `OnLeave` 窗口 / 未注册 / 重入（= 当前活动态）→ 抛。</summary>
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

        /// <summary>挂起入队（last-wins：目标 + payload 一起覆盖）。</summary>
        private void EnqueuePending(TId nextId, in TReq req)
        {
            _pendingId = nextId;
            _pendingReq = req;
            _hasPending = true;
        }

        /// <summary>查阶段实例（子类/阶段代码用；未注册抛）。</summary>
        protected IStage<TId, TReq> GetStage(TId id)
            => _stages.TryGetValue(id, out var s)
                ? s
                : throw new InvalidOperationException($"{_name}:阶段未注册 {id}");

        /// <summary>清挂起与运行计数（两机 `Reset` 共用；机器表示法字段由各机自清）。</summary>
        protected void ClearPendingAndCounters()
        {
            _hasPending = false;
            _pendingReq = default;
            _stageTime = 0f;
            _stageFrames = 0;
            _transitionCount = 0;
        }

        public string StatsName => _name;
    }
}
