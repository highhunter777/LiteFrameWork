using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 复合态的历史模式：复合态**退出时**记录其子路径，重进时按此恢复。
    /// `None` = 每次从 `InitialChild` 进；`Shallow` = 只记住直接子态；`Deep` = 记住整条子链。
    /// </summary>
    public enum HistoryMode
    {
        None,
        Shallow,
        Deep,
    }

    /// <summary>
    /// 复合态声明（层级状态机）：**纯描述树形状**——只写"谁是它的子态、默认从哪个子态进、要不要历史"，
    /// 不含行为。行为由该复合态自己的 <see cref="IStage{TId,TReq}"/>（在 `stages` 里注册）承担。
    /// 职责分离的好处：树的形状可以独立于钩子实现被审阅/测试，且不会出现"两处都声明子集"的漂移。
    /// </summary>
    public sealed class CompositeSpec<TId> where TId : struct
    {
        private readonly TId[] _children;

        /// <summary>复合态自身 id（它必须在 `stages` 里注册了阶段实例）。</summary>
        public TId Id { get; }

        /// <summary>无历史可恢复时的入口子态。</summary>
        public TId InitialChild { get; }

        /// <summary>历史模式。</summary>
        public HistoryMode History { get; }

        public IReadOnlyList<TId> Children => _children;

        public CompositeSpec(TId id, TId initialChild, HistoryMode history, params TId[] children)
        {
            if (children == null || children.Length == 0)
                throw new ArgumentException($"复合态 {id} 必须至少声明一个子态", nameof(children));

            bool hasInitial = false;
            var cmp = EqualityComparer<TId>.Default;
            foreach (var c in children)
                if (cmp.Equals(c, initialChild)) { hasInitial = true; break; }
            if (!hasInitial)
                throw new ArgumentException($"复合态 {id} 的 InitialChild={initialChild} 不在 children 里", nameof(initialChild));

            Id = id;
            InitialChild = initialChild;
            History = history;
            _children = (TId[])children.Clone();
        }
    }

    /// <summary>
    /// 事件接收位（层级状态机的冒泡）：**可选实现**——不实现就是"不处理事件"。
    /// 一个阶段可实现多个 `IEventSink&lt;T&gt;` 以处理多种事件类型。
    /// </summary>
    public interface IEventSink<TEvt>
    {
        /// <summary>处理事件：返回 true = 已消费（冒泡停止）。</summary>
        bool TryHandle(in TEvt e);
    }
}
