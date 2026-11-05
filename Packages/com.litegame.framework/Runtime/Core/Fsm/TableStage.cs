using System;

namespace LiteFramework
{
    /// <summary>
    /// 表驱动阶段：**所有同构状态共享这一个实现**，行为全部读
    /// <see cref="StageSpec{TId,TReq}"/>；与平面机（<see cref="StageMachine{TId,TReq}"/>）配套使用。
    ///
    /// **零自身状态**（关键）：驻留帧数从 `host.StageFrames` 读，不自己存——因此同一个 `TableStage` 实例
    /// 可以被**多个角色/多个机器**同时使用（若自存计数器，两个角色会互相污染）。
    /// 这与框架一贯的"阶段对象 = 无状态单例"一致。
    ///
    /// 时长：`DurationFrames` 到点后走 `NextId`（M3-7 删抢占机后**无恢复栈**——`AutoResumeOnEnd` 已无读取方）。
    /// </summary>
    public sealed class TableStage<TId, TReq> : IStage<TId, TReq>, IStageTimeout<TId>
        where TId : struct
    {
        private readonly StageSpec<TId, TReq> _spec;

        public TableStage(StageSpec<TId, TReq> spec)
            => _spec = spec ?? throw new ArgumentNullException(nameof(spec));

        /// <summary>本阶段对应的表行（只读用途；改表请改数据源后重建——表是配置，不是运行期可变状态）。</summary>
        public StageSpec<TId, TReq> Spec => _spec;

        public int TimeoutFrames => _spec.TimeoutFrames;

        public TId TimeoutTarget => _spec.TimeoutTarget;

        public void OnInit(IStageHost<TId, TReq> m) { }

        public void OnEnter(IStageHost<TId, TReq> m, in TReq req)
            => _spec.OnEnterAction?.Invoke(_spec);

        public void OnUpdate(IStageHost<TId, TReq> m, float elapseSeconds)
        {
            _spec.OnUpdateAction?.Invoke(_spec);

            if (_spec.DurationFrames <= 0 || m.StageFrames < _spec.DurationFrames) return;

            // 到点：走表里配的下一步（M3-7 删抢占机后无"恢复栈顶"路径——AutoResumeOnEnd 不再生效）。
            if (_spec.HasNext) m.Request(_spec.NextId);
        }

        public void OnLeave(IStageHost<TId, TReq> m)
            => _spec.OnLeaveAction?.Invoke(_spec);
    }
}
