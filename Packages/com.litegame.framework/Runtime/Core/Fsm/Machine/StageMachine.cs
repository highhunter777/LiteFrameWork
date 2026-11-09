using System;
using System.Collections.Generic;

namespace LiteFramework
{

    /// <summary>
    /// 阶段状态机（**基础机**）：状态表 + payload + 两段式迁移 + 帧末应用。
    /// </summary>
    public class StageMachine<TId, TReq> : StageMachineCore<TId, TReq>, ITickable, IModuleStats, IStageHost<TId, TReq>
        where TId : struct
    {
        private IStage<TId, TReq> _current;
        private TId _currentId;

        public StageMachine(string name, params (TId id, IStage<TId, TReq> stage)[] stages)
            : base(name, stages) { InitializeCore(); }

        /// <summary>当前阶段 id（未 Start 时无意义，配 <see cref="Started"/> 判读）。</summary>
        public override TId Current => _currentId;

        /// <summary>是否已 Start。</summary>
        public override bool Started => _current != null;

        /// <summary>启动（一次）；全部阶段已 OnInit。</summary>
        public void Start(TId initialId)
        {
            EnsureInitialized();
            if (_current != null) throw new InvalidOperationException($"{_name}:Start 只能调用一次");
            var stage = GetStage(initialId);
            _current = stage;
            _currentId = initialId;
            _stageTime = 0f;
            _stageFrames = 0;
            stage.OnEnter(this, default);                       // 初始进入无 leave，立即执行（payload 用 default）
        }

        /// <summary>应用挂起请求：`OnLeave`(旧) → `OnEnter`(新, in payload)。</summary>
        internal override void Advance()
        {
            if (!_hasPending) return;

            var next = GetStage(_pendingId);
            var nextId = _pendingId;
            var req = _pendingReq;
            _hasPending = false;
            _pendingReq = default;                              // 交接后清引用（防 payload 里的对象被长期持有）

            _inLeave = true;
            try { _current.OnLeave(this); }
            finally { _inLeave = false; }

            _transitionCount++;
            _current = next;
            _currentId = nextId;
            _stageTime = 0f;
            _stageFrames = 0;
            next.OnEnter(this, in req);                         // 此处再 Request → 写 pending，下次 Advance 生效（不递归）
        }

        /// <summary>
        /// 停止并回到**未启动态**（可再次 `Start`）：对当前阶段调 `OnLeave`（与 `Start` 的 `OnEnter` 对称， 让阶段收尾），然后清挂起 / 计时 / 计数（回到"刚构造"状态）。
        /// </summary>
        public virtual void Reset()
        {
            EnsureInitialized();
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
            ClearPendingAndCounters();
        }

        /// <summary>每帧驱动：`OnUpdate` → 应用挂起（一帧最多一次迁移）。</summary>
        public void Tick(float realDelta)
        {
            if (_current == null) return;                       // 未启动：静默跳过（创建与 Start 应同帧）
            _stageTime += realDelta;
            _stageFrames++;                                     // 帧窗口计数（每次 Tick 一帧）
            _current.OnUpdate(this, realDelta);
            if (_hasPending) Advance();
        }

        // ---- IModuleStats（HUD：注册即发现；禁每帧分配）----。

        /// <summary>统计快照（子类可 override 追加自己的项——先调 base 再 Add）。</summary>
        public virtual void Snapshot(Dictionary<string, string> into)
        {
            EnsureInitialized();
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
