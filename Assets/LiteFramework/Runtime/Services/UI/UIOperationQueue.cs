using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{

    /// <summary>
    /// 统一队列原语实现（唯一允许 <c>Queue&lt;T&gt;</c> 语义作为 UI 调度器的地方）。
    /// 内部用 List 承载优先级插入：导航/转场容量小、全默认优先级，行为与 FIFO 完全一致；
    /// 弹窗按优先级出队（高先出、同级 FIFO）。
    /// </summary>
    public sealed class UIOperationQueue<T> : IUIOperationQueue<T>
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

        public UIOperationQueue(int capacity)
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
