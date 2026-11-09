using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// UI 操作队列端口（路线图 M0 接缝 3 / M2 全量迁移）：导航、转场、弹窗共用同一套 "容量 / 优先级 / 就绪筛选 / 拒绝 / 取队"原语，不再各自持有私有队列与容量计数。
    /// </summary>
    public interface IUIOperationQueue<T>
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
}
