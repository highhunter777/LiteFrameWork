using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public sealed class SubscriptionBag : IDisposable
    {
        private List<Action> _items;   // 惰性分配：零订阅的袋子零 List 开销
        private bool _disposed;

        /// <summary>当前未释放的订阅数（0 = 袋子干净）。供归还点断言与 DevHUD 观测（订阅数单调增长 = 泄漏信号）。</summary>
        public int Count => _items?.Count ?? 0;

        /// <summary>是否已释放（释放后 Add 抛 ObjectDisposedException）。</summary>
        public bool IsDisposed => _disposed;

        public void Add(Action unsubscribe)
        {
            if (unsubscribe == null) throw new ArgumentNullException(nameof(unsubscribe));
            if (_disposed) throw new ObjectDisposedException(nameof(SubscriptionBag));
            (_items ??= new List<Action>(4)).Add(unsubscribe);
        }

        public void Dispose()
        {
            if (_disposed) return;                 // 幂等：靠标志位，不靠列表空判
            _disposed = true;                      // 先置位：委托执行期间的重入 Add 当场被拦
            if (_items == null) return;

            var items = _items;
            _items = null;                         // 先摘走再遍历：遍历期间无人能改这份列表
            for (int i = items.Count - 1; i >= 0; i--)   // 逆序 = 注册的对称栈式清理
            {
                try { items[i](); }
                    catch (Exception e)
                    {
                        Log.Error(e, "Dispose 单个 Subscribe 失败", "SubscriptionBag");   // 单个失败不中断其余
                    }
            }
        }
    }
}
