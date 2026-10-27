using System;
using System.Collections.Generic;

namespace LiteClient.UI
{
    /// <summary>
    /// UI 操作队列端口（路线图 M0 接缝 3 / I4）：导航与转场共用同一套"容量 / 拒绝 / 取队"原语，
    /// 不再各自持有 <c>Queue&lt;T&gt;</c> 与容量计数。
    ///
    /// **为什么是原语而不是统一执行器**：转场是在导航操作**内部**被 UIService 调用的（打开/关闭页面时
    /// 播放出入场），若共用同一个执行器会形成"导航槽内等转场槽"的嵌套等待。故两边保留各自的执行时序，
    /// 只把排队语义（FIFO、容量上限、拒绝计数）收成一份实现；M2 的 UI 队列全量迁移再统一执行面。
    /// </summary>
    public interface IUiOperationQueue<T>
    {
        /// <summary>当前排队数（不含正在执行的操作）。</summary>
        int Count { get; }

        /// <summary>排队上限（≤0 视为 1）。</summary>
        int Capacity { get; set; }

        /// <summary>因队列满被拒绝的累计次数（可观测拒绝）。</summary>
        int RejectedCount { get; }

        /// <summary>入队；队列满 → false 并累计拒绝（调用方决定抛出/丢弃并记日志）。</summary>
        bool TryEnqueue(T item);

        /// <summary>取队首；空 → false。</summary>
        bool TryDequeue(out T item);

        /// <summary>清空排队（不改变拒绝计数）。</summary>
        void Clear();
    }

    /// <summary>统一队列原语实现（唯一允许 `Queue&lt;T&gt;` 作为 UI 调度器的地方）。</summary>
    public sealed class UiOperationQueue<T> : IUiOperationQueue<T>
    {
        private readonly Queue<T> _items;
        private int _capacity;

        public UiOperationQueue(int capacity)
        {
            _capacity = capacity > 0 ? capacity : 1;
            _items = new Queue<T>(Math.Min(_capacity, 8));
        }

        public int Count => _items.Count;

        public int Capacity
        {
            get => _capacity;
            set => _capacity = value > 0 ? value : 1;
        }

        public int RejectedCount { get; private set; }

        public bool TryEnqueue(T item)
        {
            if (_items.Count >= _capacity)
            {
                RejectedCount++;
                return false;
            }
            _items.Enqueue(item);
            return true;
        }

        public bool TryDequeue(out T item)
        {
            if (_items.Count == 0)
            {
                item = default;
                return false;
            }
            item = _items.Dequeue();
            return true;
        }

        public void Clear() => _items.Clear();
    }
}
