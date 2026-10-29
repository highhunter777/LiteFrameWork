using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// UI 操作队列端口（路线图 M0 接缝 3 / M2 全量迁移）：导航、转场、弹窗共用同一套
    /// "容量 / 优先级 / 就绪筛选 / 拒绝 / 取队"原语，不再各自持有私有队列与容量计数。
    ///
    /// **为什么仍是原语而不是统一执行器**：转场是在导航操作**内部**被 UIService 调用的（打开/关闭页面时
    /// 播放出入场），若共用同一个执行器会形成"导航槽内等转场槽"的嵌套等待；弹窗的互斥组/合并语义
    /// 也需要各自的出队判据。故保留各自执行时序，只把"排队"收成一份实现——M2 的收敛点是
    /// **一个队列实现**，M3 再把超时/取消/合并策略抽成统一操作对象。
    /// </summary>
    public interface IUiOperationQueue<T>
    {
        /// <summary>当前排队数（不含正在执行的操作）。</summary>
        int Count { get; }

        /// <summary>排队上限（≤0 视为 1）。</summary>
        int Capacity { get; set; }

        /// <summary>因队列满被拒绝的累计次数（可观测拒绝）。</summary>
        int RejectedCount { get; }

        /// <summary>入队（默认优先级 = 0）；队列满 → false 并累计拒绝（调用方决定抛出/丢弃并记日志）。</summary>
        bool TryEnqueue(T item, int priority = 0);

        /// <summary>取队首（按优先级降序、同级 FIFO）；空 → false。</summary>
        bool TryDequeue(out T item);

        /// <summary>取"就绪项"中优先级最高者（同级 FIFO）；<paramref name="ready"/> = null 表示全部就绪。
        /// 弹窗用它按互斥组筛选（组忙的条目留在队里，不阻塞其他组）。</summary>
        bool TryDequeueWhere(Func<T, bool> ready, out T item);

        /// <summary>清空排队（不改变拒绝计数）。</summary>
        void Clear();
    }

    /// <summary>
    /// 统一队列原语实现（唯一允许 <c>Queue&lt;T&gt;</c> 语义作为 UI 调度器的地方）。
    /// 内部用 List 承载优先级插入：导航/转场容量小、全默认优先级，行为与 FIFO 完全一致；
    /// 弹窗按优先级出队（高先出、同级 FIFO）。
    /// </summary>
    public sealed class UiOperationQueue<T> : IUiOperationQueue<T>
    {
        private struct Slot
        {
            public T Item;
            public int Priority;
            public long Seq;
        }

        private readonly List<Slot> _items;
        private int _capacity;
        private long _seq;

        public UiOperationQueue(int capacity)
        {
            _capacity = capacity > 0 ? capacity : 1;
            _items = new List<Slot>(Math.Min(_capacity, 8));
        }

        public int Count => _items.Count;

        public int Capacity
        {
            get => _capacity;
            set => _capacity = value > 0 ? value : 1;
        }

        public int RejectedCount { get; private set; }

        public bool TryEnqueue(T item, int priority = 0)
        {
            if (_items.Count >= _capacity)
            {
                RejectedCount++;
                return false;
            }
            _items.Add(new Slot { Item = item, Priority = priority, Seq = _seq++ });
            return true;
        }

        public bool TryDequeue(out T item) => TryDequeueWhere(null, out item);

        public bool TryDequeueWhere(Func<T, bool> ready, out T item)
        {
            int best = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                Slot candidate = _items[i];
                if (ready != null && !ready(candidate.Item)) continue;
                if (best < 0) { best = i; continue; }
                Slot current = _items[best];
                if (candidate.Priority > current.Priority
                    || (candidate.Priority == current.Priority && candidate.Seq < current.Seq))
                    best = i;
            }

            if (best < 0)
            {
                item = default;
                return false;
            }

            item = _items[best].Item;
            _items.RemoveAt(best);
            return true;
        }

        public void Clear() => _items.Clear();
    }
}
