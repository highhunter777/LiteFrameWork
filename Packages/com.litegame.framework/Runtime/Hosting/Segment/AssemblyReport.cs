using System;
using System.Collections.Generic;
using System.Text;

namespace LiteFramework
{
    /// <summary>
    /// 装配报告：实际初始化序 + 产物归属（提供段/消费段）+ 未解析读取；装配成功后只读。
    /// </summary>
    public sealed class AssemblyReport
    {
        private readonly string[] _segmentOrder;
        private readonly AssemblyProduct[] _products;
        private readonly string[] _unresolved;

        internal AssemblyReport(string[] segmentOrder, AssemblyProduct[] products, string[] unresolved)
        {
            _segmentOrder = segmentOrder;
            _products = products;
            _unresolved = unresolved;
        }

        /// <summary>实际初始化顺序（拓扑序）。</summary>
        public IReadOnlyList<string> SegmentOrder => _segmentOrder;

        /// <summary>产物归属表（按提供顺序）。</summary>
        public IReadOnlyList<AssemblyProduct> Products => _products;

        /// <summary>无人提供却被消费的读取面（`消费段 → 类型`）。</summary>
        public IReadOnlyList<string> Unresolved => _unresolved;

        /// <summary>多行文本形式（启动日志/调试控制台/测试断言用）。</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("order=").Append(_segmentOrder.Length).Append(':').Append(string.Join(",", _segmentOrder));
            text.Append(" products=").Append(_products.Length);
            text.Append(" unresolved=").Append(_unresolved.Length);
            for (int i = 0; i < _products.Length; i++)
            {
                AssemblyProduct product = _products[i];
                text.Append('\n').Append(product.Product.Name).Append(" <- ").Append(product.Provider);
                if (product.Consumers.Count > 0)
                    text.Append(" -> ").Append(string.Join(",", product.Consumers));
            }
            for (int i = 0; i < _unresolved.Length; i++) text.Append("\nunresolved: ").Append(_unresolved[i]);
            return text.ToString();
        }
    }

    /// <summary>单个产物的归属：类型、提供段、消费段（按首次消费顺序）。</summary>
    public sealed class AssemblyProduct
    {
        internal AssemblyProduct(Type product, string provider, string[] consumers)
        {
            Product = product;
            Provider = provider;
            Consumers = consumers;
        }

        public Type Product { get; }
        public string Provider { get; }
        public IReadOnlyList<string> Consumers { get; }
    }
}
